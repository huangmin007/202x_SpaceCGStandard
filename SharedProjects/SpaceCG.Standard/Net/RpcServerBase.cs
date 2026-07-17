using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using SpaceCG;
using SpaceCG.Extensions;

namespace SpaceCG.Net
{
    /// <summary>
    /// 远程过程调用(Remote Procedure Call) 或 反射程序控制(Reflection Program Control) 服务端抽象基类。
    /// <para>提供 TCP 客户端连接管理、数据分隔解析（默认以 CRLF 分隔数据）、方法反射调用、结果响应等基础能力。</para>
    /// <para>
    /// <list type="bullet">
    /// <item>基类默认使用 CRLF (0x0D 0x0A, \r\n) 分隔符数据消息。</item>
    /// <item>子类继承实现 <see cref="DeserializeInvokeMessage"/> 和 <see cref="SerializeResponseMessage"/> 以支持不同的消息协议。</item>
    /// <item>通过 <see cref="ClientInvokeRequest"/> 事件可拦截或取消客户端调用请求的执行。</item>
    /// <item>方法调用通过 <see cref="SynchronizationContext.Send(SendOrPostCallback, object)"/> 封送到构造线程（通常为 UI 线程）执行。</item>
    /// </list>
    /// </para>
    /// <para>线程安全级别：线程安全（写入使用信号量序列化，客户端字典使用 ConcurrentDictionary）。</para>
    /// </summary>
    public abstract class RpcServerBase : IDisposable
    {
        /// <summary> 使用 CRLF（0x0D, 0x0A）的消息分隔符。 </summary>
        public static readonly byte[] NewLine = new byte[] { 0x0D, 0x0A };
        /// <summary> 对象名称或方法名称的命名规则正则表达式，允许字母开头后跟字母、数字、下划线。 </summary>
        public static readonly Regex IdentifierPattern = new Regex(@"^[a-zA-Z_][a-zA-Z0-9_]*$", RegexOptions.Compiled);

        private bool _isDisposed;
        private Task _acceptConnectTask;
        private TcpListener _tcpListener;
        private CancellationTokenSource _cts;
        private SynchronizationContext _syncContext;
        
        #region Public Properties
        /// <summary> 获取服务是否正在运行（监听器已启动且未取消）。 </summary>
        public bool IsRunning => _tcpListener != null && _cts != null && !_cts.IsCancellationRequested;
        /// <summary> 监听的本地 IP 地址和端口号。 </summary>
        public IPEndPoint LocalEndPoint { get; private set; }
        /// <summary> 当前已连接的 <see cref="TcpClient"/> 只读集合。 </summary>
        public IEnumerable<TcpClient> Clients => ClientWriteSemaphores.Keys;
        /// <summary> 获取或设置发送缓冲区大小，单位字节，默认 32KB。 </summary>
        public int SendBufferSize { get; set; } = 1024 * 32;
        /// <summary> 获取或设置接收缓冲区大小，单位字节，默认 64KB。 </summary>
        public int ReceiveBufferSize { get; set; } = 1024 * 64;
        /// <summary> 已注册的可调用方法标识符集合。格式：<c>"Object.Method(SVT,...)"</c>。 </summary>
        public IEnumerable<string> AvailableMethods => RegisteredMethods.Keys;
        /// <summary>
        /// 获取或设置字节数据的分隔符，用于在 TCP 流中标识一条完整消息的边界。如果为空则默认使用 <see cref="NewLine"/>。
        /// <para>默认值为 CRLF（<c>0x0D, 0x0A</c>），即 <see cref="NewLine"/>。</para>
        /// <para>可设置为其他自定义分隔符，如：LFLF (<c>0x0A0A</c>)、多个 NULL 字符 (<c>0x0000</c>)、或自定义多字节序列。</para>
        /// <para>注意：分隔符必须能唯一标识消息边界，避免与消息体内容冲突。修改此值会影响所有新连接的会话，
        /// 已建立的连接不受影响（每个会话在连接建立时快照当前值）。</para>
        /// <para>线程安全：读取线程安全，写入操作应在服务启动前完成。</para>
        /// </summary>
        public byte[] Delimiters { get; protected set; } = NewLine;
        #endregion

        #region events
        /// <summary> 客户端接入连接事件。 </summary>
        public event EventHandler<IPEndPoint> ClientConnected;
        /// <summary> 客户端断开连接事件。 </summary>
        public event EventHandler<IPEndPoint> ClientDisconnected;
        /// <summary>
        /// 客户端执行调用请求事件。
        /// <para>通过设置 <see cref="InvokeMessageEventArgs.Cancel"/> 为 <c>true</c> 可拦截、取消客户端调用消息的执行。</para>
        /// </summary>
        public event EventHandler<InvokeMessageEventArgs> ClientInvokeRequest;
        #endregion

        #region readonly fields
        /// <summary>
        /// 方法调用并发控制信号量。
        /// <para>初始许可数 8，最大许可数 64，用于限制同时执行反射调用的并发数量，防止 ThreadPool 线程因 <see cref="SynchronizationContext.Send(SendOrPostCallback, object)"/> 阻塞而过度膨胀。</para>
        /// </summary>
        private readonly SemaphoreSlim ProcessInvokeSemaphore = new SemaphoreSlim(8, 64);
        /// <summary> 客户端写入消息并发控制信号量。 </summary>
        private readonly ConcurrentDictionary<TcpClient, SemaphoreSlim> ClientWriteSemaphores = new ConcurrentDictionary<TcpClient, SemaphoreSlim>();
        
        /// <summary>
        /// 方法过滤器列表，匹配的方法将不允许被远程调用。
        /// <para>格式为：{ObjectName}.{MethodName}，其中 ObjectName 支持通配符 '*'。</para>
        /// <para>例如："*.Dispose" 禁止反射访问所有对象的 Dispose 方法；默认已添加 "*.Dispose" 和 "*.Close"。</para>
        /// </summary>
        public readonly List<string> MethodFilters = new List<string>(16) { "*.Dispose", "*.Close" }; 
        /// <summary> 注册对象的实例集合 </summary>
        protected readonly ConcurrentDictionary<string, object> RegisterObjects = new ConcurrentDictionary<string, object>();
        /// <summary> 注册对象的实例缓存的方法信息；在 <see cref="RegisterObject"/> 时将实例的所有公共方法和扩展方法预缓存在字典中。 </summary>
        protected readonly ConcurrentDictionary<string, MethodInfo> RegisteredMethods = new ConcurrentDictionary<string, MethodInfo>();

        //private readonly RpcMessagePool<InvokeMessage> InvokeMessagePool = new RpcMessagePool<InvokeMessage>(25, 128);
        //private readonly RpcMessagePool<ResponseMessage> ResponseMessagePool = new RpcMessagePool<ResponseMessage>(25, 128);
        #endregion

        #region Constructors
        /// <summary>
        /// 使用指定的 IP 地址和端口号初始化 <see cref="RpcServerBase"/> 类的新实例。
        /// </summary>
        /// <param name="ipAddress">服务器绑定的本地 IP 地址，如 <see cref="IPAddress.Any"/> 表示监听所有网卡。</param>
        /// <param name="localPort">服务器监听的本地端口号，范围 1-65535。</param>
        /// <exception cref="ArgumentException">端口号不在 1-65535 范围内时抛出。</exception>
        public RpcServerBase(IPAddress ipAddress, int localPort)
        {
            if (localPort < 1 || localPort > 65535)
                throw new ArgumentException("端口号必须在 1-65535 之间");

            LocalEndPoint = new IPEndPoint(ipAddress, localPort);
            _syncContext = SynchronizationContext.Current ?? new SynchronizationContext();
        }
        /// <summary>
        /// 使用指定端口号初始化 <see cref="RpcServerBase"/> 类的新实例，监听所有网卡。
        /// </summary>
        /// <param name="localPort">服务器监听的本地端口号，范围 1-65535。</param>
        public RpcServerBase(int localPort) : this(IPAddress.Any, localPort) { }
        /// <inheritdoc cref="RpcServerBase(IPAddress, int)"/>
        public RpcServerBase(string ipAddress, int localPort) : this(IPAddress.Parse(ipAddress), localPort) { }

        #endregion

        #region Register object & Cache methods
        /// <summary>
        /// 注册可被调用/访问的对象实例。
        /// </summary>
        /// <param name="objectName">对象的注册名称，用于客户端调用时定位目标对象，需符合 <see cref="IdentifierPattern"/> 命名规则。</param>
        /// <param name="objectInstance">对象实例，不能为 null 或自身实例。</param>
        /// <exception cref="ObjectDisposedException">实例已释放时抛出。</exception>
        /// <exception cref="ArgumentNullException">对象名称为空、格式不正确或实例为 null 时抛出。</exception>
        /// <exception cref="ArgumentException">对象类型不合法或名称已存在时抛出。</exception>
        public void RegisterObject(string objectName, object objectInstance)
        {
            if (_isDisposed) throw new ObjectDisposedException(nameof(RpcServerBase));

            if (string.IsNullOrWhiteSpace(objectName) || !IdentifierPattern.IsMatch(objectName))
                throw new ArgumentNullException(nameof(objectName), "对象名称不能为空或命名格式不正确");

            if (objectInstance == null || typeof(RpcServerBase).IsInstanceOfType(objectInstance))
                throw new ArgumentNullException(nameof(objectInstance), "对象实例不能为空，也不能注册 RpcServerBase 自身或其子类实例");

            var objectType = objectInstance.GetType();
            if (objectType.IsValueType /*|| objectType == typeof(RPCClient)*/)
                throw new ArgumentException($"不能注册的对象实例类型 {objectType}");

            if (RegisterObjects.ContainsKey(objectName))
                throw new ArgumentException($"已存在的对象名称 {objectName}");

            if (!RegisterObjects.TryAdd(objectName, objectInstance))
                throw new ArgumentException($"注册对象 {objectName} 实例  {objectInstance} 失败");

            CacheObjectMethods(objectName, objectInstance);
        }
        /// <summary>
        /// 缓存注册对象实例的公共方法和公共扩展方法。
        /// <para>扫描注册对象类型的所有公共实例方法（排除 virtual/special-name 方法、含 ref 参数的方法），
        /// 以及当前 AppDomain 中所有程序集的公共扩展方法，生成方法签名缓存键存入 <see cref="RegisteredMethods"/>。</para>
        /// <para>注意：扩展方法扫描涉及 AppDomain 全局反射，时间复杂度较高。</para>
        /// </summary>
        /// <param name="objectName">注册对象名称，用于生成方法缓存键前缀。</param>
        /// <param name="objectInstance">注册对象实例。</param>
        private void CacheObjectMethods(string objectName, object objectInstance)
        {
            var instanceType = objectInstance.GetType();
            var extensionType = typeof(ExtensionAttribute);

            // 缓存实例的公共方法
            foreach (var method in instanceType.GetMethods())
            {
                if (!method.IsPublic) continue;
                if (method.IsVirtual || method.IsSpecialName) continue;

                var parameters = method.GetParameters();
                var paramsSign = parameters.GetParameterSignature();

                if (paramsSign.Contains("REF")) continue;
                var objectMethodKey = $"{objectName}.{method.Name}({paramsSign})";

                var count = 0;
                var objectMethodKeyClone = objectMethodKey;
                while (RegisteredMethods.ContainsKey(objectMethodKeyClone))
                {
                    objectMethodKeyClone = $"{objectMethodKey}_{count++}";
                }

                //Debug.WriteLine($"{objectMethodKeyClone}");
                RegisteredMethods.TryAdd(objectMethodKeyClone, method);
            }
            //Debug.WriteLine("------------------------------ Extension ");
            // 缓存实例的公共扩展方法  
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly.GlobalAssemblyCache) continue;

                Type[] exportedTypes;
                try
                {
                    exportedTypes = assembly.GetExportedTypes();
                }
                catch (Exception ex)
                {
                    Trace.TraceWarning($"{assembly.FullName} GetExprotedTypes Exception: {ex.Message}");
                    continue;
                }

                foreach (var type in exportedTypes)
                {
                    if (!type.IsSealed || type.IsGenericType || type.IsNested || !type.IsAbstract) continue;
                    foreach (var method in type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.DeclaredOnly))
                    {
                        if (!method.IsDefined(extensionType, false)) continue;

                        var parameters = method.GetParameters();
                        if (parameters == null || parameters.Length == 0) continue;

                        // 类型相同，或是父级类
                        if (parameters[0].ParameterType == instanceType || instanceType.IsSubclassOf(parameters[0].ParameterType))
                        {
                            var paramsSign = parameters.Skip(1).GetParameterSignature();
                            var objectMethodKey = $"{objectName}.{method.Name}({paramsSign})";

                            var count = 0;
                            var objectMethodKeyClone = objectMethodKey;
                            while (RegisteredMethods.ContainsKey(objectMethodKeyClone))
                            {
                                objectMethodKeyClone = $"{objectMethodKey}_{count++}";
                            }

                            //Debug.WriteLine($"{objectMethodKeyClone}");
                            RegisteredMethods.TryAdd(objectMethodKeyClone, method);
                        }
                    }
                }
            }
        }
        #endregion

        #region Start & Stop
        /// <summary>
        /// 启动 RPC 服务端，开始监听客户端连接。
        /// <para>如果服务已在运行，则忽略本次调用。</para>
        /// </summary>
        /// <exception cref="ObjectDisposedException">实例已释放时抛出。</exception>
        public void Start()
        {
            if (_isDisposed)
                throw new ObjectDisposedException(nameof(RpcServerBase));

            if (IsRunning) return;

            _cts = new CancellationTokenSource();
            _tcpListener = new TcpListener(LocalEndPoint);

            try
            {
                _tcpListener.Start();
                Trace.TraceInformation($"RPC 服务已启动，监听地址：{_tcpListener.LocalEndpoint}");
            }
            catch (Exception ex)
            {
                Trace.TraceError($"启动 RPC 服务失败：({ex.GetType().Name}) {ex.Message}");
                Stop();
                return;
            }

            _acceptConnectTask = AcceptClientConnectAsync(_cts.Token);
        }
        /// <summary>
        /// 停止 RPC 服务端，取消所有待处理消息数据，断开所有客户端连接并释放监听器资源。
        /// <para>如果服务未运行，则忽略本次调用。</para>
        /// </summary>
        public void Stop()
        {
            try { _cts?.Cancel(); }
            catch (Exception) { }

            try
            {
                _tcpListener?.Stop();
                _tcpListener?.Server.Dispose();
            }
            catch (Exception) { }
            finally
            {
                _tcpListener = null;
            }

            try
            {
                _acceptConnectTask?.Wait(100);
                _acceptConnectTask?.Dispose();
            }
            catch (Exception) { }
            finally
            {
                _acceptConnectTask = null;
            }

            var clients = ClientWriteSemaphores.ToList();
            foreach (var kv in clients)
            {
                try { kv.Key?.Dispose(); }
                catch (Exception) { }
                try { kv.Value?.Dispose(); }
                catch (Exception) { }
            }            
            ClientWriteSemaphores.Clear();

            try 
            { 
                _cts?.Dispose(); 
            }
            catch (Exception) { }
            finally
            {
                _cts = null;
            }

            Trace.TraceInformation($"RPC 服务已停止");
        }
        #endregion

        #region AcceptClient -> HandleClientSession(ReadAsync -> ProcessClientMessage -> ProcessInvokeMessage -> WriteResponseMessage)
        /// <summary>
        /// 异步接受 TCP 客户端连接的主循环。
        /// </summary>
        /// <param name="cancellationToken">用于取消接受操作的取消令牌。</param>
        /// <returns>一个表示异步操作的任务。</returns>
        private async Task AcceptClientConnectAsync(CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    var client = await _tcpListener.AcceptTcpClientAsync().ConfigureAwait(false);
                    _ = HandleClientSessionAsync(client, cancellationToken);
                }
            }
            catch (Exception ex) when (ex is OperationCanceledException || ex is ObjectDisposedException)
            {
                // 正常退出
            }
            catch (SocketException ex)
            {
                if (ex.SocketErrorCode == SocketError.OperationAborted) return; // 正常退出
                Trace.TraceError($"RPC 服务等待客户端连接时异常退出：({ex.GetType().Name} SocketErrorCode:{ex.SocketErrorCode}){ex.Message}");
            }
            catch (Exception ex)
            {
                Trace.TraceError($"RPC 服务等待客户端连接时发生异常：({ex.GetType().Name}) {ex.Message}");
            }
        }
        /// <summary>
        /// 处理 TCP 客户端连接会话(循环读取数据 -> 处理数据 -> 写入响应数据)。
        /// <para>使用环形缓冲（Ring Buffer）模式，以 <see cref="Delimiters" /> 分隔数据消息，通过 <see cref="DeserializeInvokeMessage"/> 反序列化数据消息。</para>
        /// </summary>
        /// <param name="client">已接受的 TCP 客户端连接。</param>
        /// <param name="cancellationToken">用于取消读取操作的取消令牌。</param>
        /// <returns>一个表示异步操作的任务。</returns>
        private async Task HandleClientSessionAsync(TcpClient client, CancellationToken cancellationToken)
        {
            var clientEndPoint = client.Client.RemoteEndPoint as IPEndPoint;            
            Trace.TraceInformation($"客户端 {clientEndPoint} 已连接");

            client.SendBufferSize = this.SendBufferSize;
            client.ReceiveBufferSize = this.ReceiveBufferSize;

            // 环形缓冲（Ring Buffer）设计
            // 如果读取的数据全部分析完或刚好分析完一个完整的数据消息后，缓冲区后面没有可分析数据时，可以将 offset 指针设置 0
            var bufferSize = this.ReceiveBufferSize / 2;
            var clientBuffer = new byte[bufferSize];

            byte[] delimiter = null;
            if (Delimiters?.Length > 0)
            {
                delimiter = new byte[Delimiters.Length];
                Delimiters.CopyTo(delimiter, 0);
            }
            else
            {
                delimiter = new byte[NewLine.Length];
                NewLine.CopyTo(delimiter, 0);
            }

            var readPosition = 0;       // 数据分析的起始位置 (读指针)
            var writePosition = 0;      // 写入数据的末尾位置（下一次 ReadAsync 的写入起点，写指针）
            var minBufferSize = bufferSize / 8;

            // 客户端写入消息同步锁，保证消息的完整性
            var clientSemaphore = new SemaphoreSlim(1, 1);
            if (!ClientWriteSemaphores.TryAdd(client, clientSemaphore))
            {
                // 理论上不应该发生，但做防御性处理
                clientSemaphore.Dispose();
                clientSemaphore = ClientWriteSemaphores[client]; // 使用已存在的
                Trace.TraceWarning($"客户端 {clientEndPoint} 的信号量已存在（异常情况）");
            }
            ClientConnected?.Invoke(this, clientEndPoint);

            try
            {
                var clientStream = client.GetStream();
                while (!cancellationToken.IsCancellationRequested && client.IsConnected())
                {
                    #region 整理缓冲区
                    // 0. 数据正好分析完 → 所有指针归零
                    if (readPosition == writePosition)
                    {
                        readPosition = 0;
                        writePosition = 0;
                    }
                    // 1. 整理缓冲区：如果尾部剩余空间不足，且前方有已消费的空间，则向前移动有效数据
                    else if (readPosition > 0 && writePosition > readPosition && (bufferSize - writePosition < minBufferSize))
                    {
                        var pendingLength = writePosition - readPosition;
                        Buffer.BlockCopy(clientBuffer, readPosition, clientBuffer, 0, pendingLength);
                        Trace.TraceInformation($"客户端 {clientEndPoint} 整理缓冲区，移动 {pendingLength} bytes");

                        readPosition = 0;
                        writePosition = pendingLength;
                    }
                    // 2. 防御性处理：如果缓冲区依然满了（说明单条消息超大或恶意攻击），清空以防死锁
                    if (writePosition == bufferSize)
                    {
                        var pendingLength = writePosition - readPosition;
                        Trace.TraceWarning($"客户端 {clientEndPoint} 缓冲区已满且无完整消息，丢弃 {pendingLength} bytes。请检查协议或客户端行为。");
                        // break;

                        readPosition = 0;
                        writePosition = 0;
                    }
                    #endregion

                    // 读取数据
                    var count = await clientStream.ReadAsync(clientBuffer, writePosition, bufferSize - writePosition, cancellationToken);
                    if (count == 0) break;  // 客户端已断开了连接
                    
                    writePosition += count;
                    //System.Diagnostics.Debug.WriteLine($"接收客户端 {clientEndPoint} 的数据 {count} bytes ");

                    #region 扫描缓冲区中所有完整的数据字节消息
                    while (readPosition < writePosition)
                    {
                        var delimiterPosition = clientBuffer.IndexOf(delimiter, readPosition, writePosition - readPosition);
                        if (delimiterPosition < 0) break;

                        // 提取完整的数据字节消息（含 delimiter 分割符本身）
                        var messageLength = delimiterPosition - readPosition + delimiter.Length;
                        var requestMessage = new ArraySegment<byte>(clientBuffer, readPosition, messageLength);

                        // 移动读指针，跳过已消费的数据和分割符
                        readPosition = delimiterPosition + delimiter.Length;

                        // 处理客户端请求的字节数据
                        _ = ProcessClientMessageAsync(client, requestMessage, cancellationToken);
                    }
                    #endregion
                }
            }
            catch (Exception ex) when (ex is OperationCanceledException || ex is ObjectDisposedException || ex is IOException)
            {
                // 正常退出
            }            
            catch (Exception ex)
            {
                Trace.TraceError($"接收处理客户端 {clientEndPoint} 数据时异常: ({ex.GetType().Name}) {ex.Message}");
            }
            finally
            {
                ClientWriteSemaphores.TryRemove(client, out var _);
                Trace.TraceInformation($"客户端 {clientEndPoint} 已断开连接");

                client?.Dispose();
                clientSemaphore?.Dispose();
                ClientDisconnected?.Invoke(this, clientEndPoint);
            }
        }

        /// <summary>
        /// 处理客户端的数据消息（反序列化 -> 事件拦截 -> 方法调用分发）。
        /// </summary>
        /// <param name="client">发送数据的 TCP 客户端连接。</param>
        /// <param name="requestMessage">一条完整的字节数据消息。</param>
        /// <param name="cancellationToken">用于取消操作的令牌。</param>
        /// <returns>一个表示异步操作的任务。</returns>
        protected async Task ProcessClientMessageAsync(TcpClient client, ArraySegment<byte> requestMessage, CancellationToken cancellationToken)
        {
            if (client == null || requestMessage == null || requestMessage.Count == 0) return;
            
            InvokeMessage invokeMessage = null;
            var clientEndPoint = client.Client.RemoteEndPoint as IPEndPoint;

            try
            {
                // 反序列化客户端的数据消息
                invokeMessage = DeserializeInvokeMessage(requestMessage, clientEndPoint);
            }
            catch (Exception ex)
            {
                // 这里不能响应客户端，因为不确定客户端的要求是否需要响应 ？？？？
                Trace.TraceWarning($"反序列化客户端 {clientEndPoint} 消息异常：({ex.GetType().Name}) {ex.Message}");
                return;
            }

            if (invokeMessage == null)
            {
                // 这里不能响应客户端，因为不确定客户端的要求是否需要响应 ？？？？
                Trace.TraceWarning($"反序列化客户端 {clientEndPoint} 消息为空");
                return;
            }

            invokeMessage.ClientEndPoint = clientEndPoint;
            if (invokeMessage.ResponseMode >= 0) invokeMessage.TcpClient = client;

            if (ClientInvokeRequest != null)
            {
                var eventArgs = new InvokeMessageEventArgs(invokeMessage, clientEndPoint);
                ClientInvokeRequest.Invoke(this, eventArgs);
                if (eventArgs.Cancel)
                {
                    Trace.TraceInformation($"客户端 {clientEndPoint} 的调用消息 (Id:{invokeMessage.Id}) 被拦截取消");
                    var responseMessage = ResponseMessage.Create(invokeMessage, -5, "Invoke request is intercepted and canceled");
                    await WriteResponseMessageAsync(invokeMessage, responseMessage, cancellationToken).ConfigureAwait(false);
                    return;
                }
            }

            await ProcessInvokeMessageAsync(invokeMessage, cancellationToken).ConfigureAwait(false);
        }
        /// <summary>
        /// 异步执行处理客户端调用消息的主流程。
        /// <para>通过信号量控制并发数，在 <see cref="_syncContext"/> 目标线程上反射调用注册对象的方法，并返回执行结果。</para>
        /// <para>处理流程：基本校验 -> 方法查找 -> 参数转换 -> 线程同步调用 -> 响应写入。</para>
        /// </summary>
        /// <param name="invokeMessage">客户端调用请求消息。</param>
        /// <param name="cancellationToken">用于取消操作的令牌。</param>
        /// <returns>一个表示异步操作的任务。</returns>
        protected async Task ProcessInvokeMessageAsync(InvokeMessage invokeMessage, CancellationToken cancellationToken)
        {
            if (invokeMessage == null) return;
            await ProcessInvokeSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

            var objectMethod = $"{invokeMessage.ObjectName}.{invokeMessage.MethodName}";
            try
            {
                #region 基本检查
                if (!RegisterObjects.TryGetValue(invokeMessage.ObjectName, out var objectInstance))
                {
                    var responseMessage = ResponseMessage.Create(invokeMessage, -10, $"Object ({invokeMessage.ObjectName}) not register");
                    await WriteResponseMessageAsync(invokeMessage, responseMessage, cancellationToken).ConfigureAwait(false);
                    return;
                }
                if (MethodFilters.IndexOf($"*.{invokeMessage.MethodName}") != -1 || MethodFilters.IndexOf(objectMethod) != -1)
                {
                    var responseMessage = ResponseMessage.Create(invokeMessage, -11, $"Object method ({objectMethod}) is not allowed to be invoked");
                    await WriteResponseMessageAsync(invokeMessage, responseMessage, cancellationToken).ConfigureAwait(false);
                    return;
                }
                #endregion

                #region 跟据输入的参数签名查找方法
                var paramsSign = "";
                var paramsLength = invokeMessage.Parameters?.Length ?? 0;
                if (paramsLength > 0) paramsSign = invokeMessage.Parameters.GetParameterSignature();
                var methodCacheKey = $"{invokeMessage.ObjectName}.{invokeMessage.MethodName}({paramsSign})";
                //Debug.WriteLine($"methodCacheKey:{methodCacheKey}");

                if (!RegisteredMethods.TryGetValue(methodCacheKey, out var methodInfo))
                {
                    var responseMessage = ResponseMessage.Create(invokeMessage, -12, $"Object method ({methodCacheKey}) is not available");
                    await WriteResponseMessageAsync(invokeMessage, responseMessage, cancellationToken).ConfigureAwait(false);
                    return;
                }
                #endregion

                #region 参数转换解析
                var methodParameters = methodInfo.GetParameters();
                var isExtensionMethod = methodInfo.IsDefined(typeof(ExtensionAttribute), false); // 是否是实例类型的扩展方法
                var offset = isExtensionMethod ? 1 : 0;

                object[] convertedParameters = new object[methodParameters.Length];
                if (isExtensionMethod) convertedParameters[0] = objectInstance;

                for (int i = 0; i < paramsLength; i++)
                {
                    var destinationType = methodParameters[i + offset].ParameterType;
                    if (!TypeExtensions.TryConvertParameter(invokeMessage.Parameters[i], destinationType, out object convertValue))
                    {
                        var responseMessage = ResponseMessage.Create(invokeMessage, -13, $"Object method ({objectMethod}) parameter ({i}) convert from ({invokeMessage.Parameters[i]?.GetType()}) to ({destinationType}) failed");
                        await WriteResponseMessageAsync(invokeMessage, responseMessage, cancellationToken).ConfigureAwait(false);
                        return;
                    }

                    convertedParameters[i + offset] = convertValue;
                }
                #endregion

                #region 执行方法调用（SyncContext.Send 模式）
                object invokeResult = null;
                Exception invokeException = null;

                _syncContext.Send(_ =>
                {
                    try
                    {
                        invokeResult = methodInfo.Invoke(objectInstance, convertedParameters);
                    }
                    catch (Exception ex)
                    {
                        invokeException = ex;
                    }
                }, null);

                // 处理结果如果发生异常，则响应异常信息
                if (invokeException != null)
                {
                    var description = $"Object method ({methodCacheKey}) invoke exception: ({invokeException.GetType().Name}) {invokeException.Message}";
                    Trace.TraceWarning($"客户端 {invokeMessage.ClientEndPoint} 执行调用消息异常： {description}");

                    var responseMessage = ResponseMessage.Create(invokeMessage, -14, description);
                    await WriteResponseMessageAsync(invokeMessage, responseMessage, cancellationToken).ConfigureAwait(false);
                    return;
                }
                // 成功响应
                if (invokeMessage.ResponseMode >= 0)
                {
                    var responseMessage = ResponseMessage.Create(invokeMessage, methodInfo.ReturnType == typeof(void) ? 0 : 1, "Success", methodInfo.ReturnType, invokeResult);
                    await WriteResponseMessageAsync(invokeMessage, responseMessage, cancellationToken).ConfigureAwait(false);
                }
                #endregion
            }
            catch (Exception ex)
            {
                var description = $"Process invoke method ({objectMethod}) exception: ({ex.GetType().Name}) {ex.Message}";
                Trace.TraceWarning($"客户端 {invokeMessage.ClientEndPoint} 处理执行调用消息异常： {description}");

                var responseMessage = ResponseMessage.Create(invokeMessage, -20, description);
                await WriteResponseMessageAsync(invokeMessage, responseMessage, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                ProcessInvokeSemaphore.Release();
            }
        }

        /// <summary>
        /// 将响应消息序列化并异步写入指定客户端网络流。
        /// </summary>
        /// <param name="client">目标 TCP 客户端连接。</param>
        /// <param name="responseMessage">待发送的响应消息对象。</param>
        /// <param name="cancellationToken">用于取消写入操作的令牌。</param>
        /// <returns>一个表示异步写入操作的任务。</returns>
        protected async Task WriteResponseMessageAsync(TcpClient client, ResponseMessage responseMessage, CancellationToken cancellationToken)
        {
            if (client == null || responseMessage == null) return;
            if (!client.Connected) return;

            byte[] responseBytes = null;
            var clientEndPoint = client.Client.RemoteEndPoint as IPEndPoint;

            try
            {
                responseBytes = SerializeResponseMessage(responseMessage, clientEndPoint);
            }
            catch (Exception ex)
            {
                Trace.TraceWarning($"序列化客户端 {clientEndPoint} 响应消息异常：({ex.GetType().Name}) {ex.Message}");
                return;
            }

            if (responseBytes == null || responseBytes.Length == 0) return;

            if (ClientWriteSemaphores.TryGetValue(client, out var semaphoreSlim))
            {
                try
                {
                    await semaphoreSlim.WaitAsync(cancellationToken).ConfigureAwait(false);

                    var clientStream = client.GetStream();
                    await clientStream.WriteAsync(responseBytes, 0, responseBytes.Length, cancellationToken).ConfigureAwait(false);
                    await clientStream.FlushAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is OperationCanceledException || ex is ObjectDisposedException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    Trace.TraceWarning($"客户端 {clientEndPoint} 写入响应消息异常：({ex.GetType().Name}) {ex.Message}");
                }
                finally
                {
                    try { semaphoreSlim.Release(); }
                    catch (ObjectDisposedException) { }
                }
            }
            else
            {
                Trace.TraceWarning($"客户端 {clientEndPoint} 未找到写入消息信号量。");
            }
        }
        /// <summary>
        /// 将响应消息序列化并异步写入客户端网络流。
        /// <para>会自动检查连接状态，当客户端已断开或无响应需求时跳过写入。</para>
        /// </summary>
        /// <param name="invokeMessage">原始调用请求消息，用于获取目标 <see cref="TcpClient"/> 和响应模式。</param>
        /// <param name="responseMessage">待发送的响应消息对象。</param>
        /// <param name="cancellationToken">用于取消写入操作的令牌。</param>
        /// <returns>一个表示异步写入操作的任务。</returns>
        protected async Task WriteResponseMessageAsync(InvokeMessage invokeMessage, ResponseMessage responseMessage, CancellationToken cancellationToken)
        {
            if (invokeMessage == null || responseMessage == null) return;

            if (invokeMessage.ResponseMode < 0) return;
            if (invokeMessage.ResponseMode == 0 && responseMessage.Code == 0) return;
            if (invokeMessage.TcpClient == null || !invokeMessage.TcpClient.Connected) return;

            byte[] responseBytes = null;
            try
            {
                responseBytes = SerializeResponseMessage(responseMessage, invokeMessage.ClientEndPoint);
            }
            catch (Exception ex)
            {
                Trace.TraceWarning($"序列化客户端 {invokeMessage.ClientEndPoint} 响应消息异常：({ex.GetType().Name}) {ex.Message}");
                return;
            }

            if (responseBytes == null || responseBytes.Length == 0) return;

            if (ClientWriteSemaphores.TryGetValue(invokeMessage.TcpClient, out var semaphoreSlim))
            {
                try
                {
                    await semaphoreSlim.WaitAsync(cancellationToken).ConfigureAwait(false);

                    var stream = invokeMessage.TcpClient.GetStream();
                    await stream.WriteAsync(responseBytes, 0, responseBytes.Length, cancellationToken).ConfigureAwait(false);
                    await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is OperationCanceledException || ex is ObjectDisposedException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    Trace.TraceWarning($"客户端 {invokeMessage.ClientEndPoint} 写入响应消息异常：({ex.GetType().Name}) {ex.Message}");
                }
                finally
                {
                    try { semaphoreSlim.Release(); }
                    catch (ObjectDisposedException) { }
                }
            }
            else
            {
                Trace.TraceWarning($"客户端 {invokeMessage.ClientEndPoint} 未找到写入消息信号量。");
            }
        }
        #endregion

        #region 子类重写抽象方法 DeserializeInvokeMessage & SerializeResponseMessage
        /// <summary>
        /// 解析客户端发送的一条完整字节数据消息（结尾以 <see cref="Delimiters"/> 标识符结束）。
        /// <para>子类继承重写该方法，实现不同协议的数据解析逻辑。</para>
        /// </summary>
        /// <param name="requestMessage">客户端的请求消息，字节数据消息（结尾以 <see cref="Delimiters"/> 标识符结束）。</param>
        /// <param name="clientEndPoint">发送数据的客户端远程端点地址。</param>
        /// <returns>解析成功返回一条 <see cref="InvokeMessage"/> 待服务端调用的消息；可过滤、修改、或解析失败则返回空。</returns>
        protected abstract InvokeMessage DeserializeInvokeMessage(ArraySegment<byte> requestMessage, IPEndPoint clientEndPoint);
        /// <summary>
        /// 将执行调用后的响应信息，序列化为响应字节数组（结尾应以 <see cref="Delimiters"/> 标识符结束），用于发送回客户端。
        /// <para>子类继承重写该方法，实现不同协议的响应格式。</para>
        /// </summary>
        /// <param name="responseMessage">执行调用后的响应对象。</param>
        /// <param name="clientEndPoint">目标客户端的远程端点地址。</param>
        /// <returns>序列化后的响应字节数组（结尾应以 <see cref="Delimiters"/> 标识符结束）；如果不响应则时返回空数组。 </returns>
        protected abstract byte[] SerializeResponseMessage(ResponseMessage responseMessage, IPEndPoint clientEndPoint);
        #endregion

        /// <summary>
        /// 释放 RPC 服务端占用的所有资源。
        /// <para>包括停止监听、断开所有客户端连接、清空注册对象。</para>
        /// </summary>
        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;

            Stop();

            RegisterObjects.Clear();
            RegisteredMethods.Clear();

            try
            {
                ProcessInvokeSemaphore?.Dispose();
            }
            catch (Exception) { }
        }

        
    }
}
