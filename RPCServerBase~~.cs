using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Authentication.ExtendedProtection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using SpaceCG.Extensions;

namespace SpaceCG.Net
{
    /// <summary>
    /// 远程过程调用(Remote Procedure Call) 或 反射程序控制(Reflection Program Control) 服务端抽象基类(协议数据抽象基类)。
    /// <para>提供 TCP 客户端连接管理、数据接收解析(数据行)、方法反射调用、结果响应等基础能力。</para>
    /// <para>
    /// <list type="bullet">
    /// <item>基类是以数据行为单位进行分割，换行回车 CRLF, 0D0A, \r\n或是空行为</item>
    /// <item>子类继承实现 <see cref="ParseInvokeMessage"/> 和 <see cref="ConvertResponseMessage"/> 以支持不同的消息协议</item>
    /// <item>通过 <see cref="ClientMessageInvoking"/> 事件可拦截、取消、修改客户端调用消息的执行</item>
    /// <item>基类通过数据行进行分组，子类可以将一个调用消息放在一行，或将多个调用消息放在一行，具体由子类决定</item>
    /// </list>
    /// </para>
    /// </summary>
    public abstract class RPCServerBase : IDisposable
    {
        /// <summary> 对象名称或方法名称的命名规则正则表达式，允许字母开头后跟字母、数字、下划线。 </summary>
        public static readonly Regex NamedRegex = new Regex(@"^[a-zA-Z_][a-zA-Z0-9_]*$", RegexOptions.Compiled);

        private bool _isDisposed;
        private TcpListener _tcpListener;
        private CancellationTokenSource _cts;
        /// <summary> 当前线程的同步上下文，用于将异步消息分派到指定上下文中执行。 </summary>
        private SynchronizationContext _syncContext;

        /// <summary> 获取服务是否正在运行。 </summary>
        public bool IsRunning => _tcpListener != null && _cts != null;
        /// <summary> 监听的本地 IP 地址和端口号。 </summary>
        public IPEndPoint LocalEndPoint { get; private set; }

        /// <summary> 客户端接入连接事件。 </summary>
        public event EventHandler<IPEndPoint> ClientConnected;
        /// <summary> 客户端断开连接事件。 </summary>
        public event EventHandler<IPEndPoint> ClientDisconnected;
        /// <summary>
        /// 客户端执行调用消息时事件。
        /// <para>通过设置 <see cref="CancelEventArgs.Cancel"/> 为 <c>true</c> 可拦截、取消、或是修改客户端调用消息的执行。</para>
        /// </summary>
        public event EventHandler<InvokeMessageEventArgs> ClientMessageInvoking;

        /// <summary> 当前已连接的 <see cref="TcpClient"/> 只读集合。 </summary>
        public IReadOnlyList<TcpClient> Clients => _clients;
        /// <summary> 内部已连接的客户端列表。 </summary>
        private readonly List<TcpClient> _clients = new List<TcpClient>();

        /// <summary> 获取或设置发送缓冲区大小，单位字节，默认 32KB。 </summary>
        public int SendBufferSize { get; set; } = 1024 * 32;
        /// <summary> 获取或设置接收缓冲区大小，单位字节，默认 64KB。 </summary>
        public int ReceiveBufferSize { get; set; } = 1024 * 64;
        /// <summary> 数据行分隔符字节数组，使用 CRLF（0x0D, 0x0A）作为新行标识符。 </summary>
        public static readonly byte[] NewLine = new byte[] { 0x0D, 0x0A };
        
        /// <summary>
        /// 方法过滤器列表，匹配的方法将不允许被远程调用。
        /// <para>格式为：{ObjectName}.{MethodName}，其中 ObjectName 支持通配符 '*'。</para>
        /// <para>例如："*.Dispose" 禁止反射访问所有对象的 Dispose 方法；默认已添加 "*.Dispose" 和 "*.Close"。</para>
        /// </summary>
        public readonly List<string> MethodFilters = new List<string>(16) { "*.Dispose", "*.Close" };

        /// <summary> 客户端调用消息队列，服务端可从该队列中获取客户端的调用消息，并执行调用。 </summary>
        private readonly ConcurrentQueue<InvokeMessage> InvokeMessages = new ConcurrentQueue<InvokeMessage>();
        /// <summary> 注册的对象实例集合 </summary>
        private readonly ConcurrentDictionary<string, object> RegisterObjects = new ConcurrentDictionary<string, object>();
        /// <summary> 
        /// 实例的缓存方法信息；在 <see cref="RegisterObject"/> 时将实例的所有公共方法和扩展方法预缓存在字典中。
        /// </summary>
        private readonly ConcurrentDictionary<string, MethodInfo> InstanceCacheMethodInfos = new ConcurrentDictionary<string, MethodInfo>();

        /// <summary>
        /// 使用指定的 IP 地址和端口号初始化 <see cref="RPCServerBase"/> 类的新实例。
        /// </summary>
        /// <param name="ipAddress">服务器绑定的本地 IP 地址，如 <see cref="IPAddress.Any"/> 表示监听所有网卡。</param>
        /// <param name="localPort">服务器监听的本地端口号，范围 1-65535。</param>
        /// <exception cref="ArgumentException">端口号不在 1-65535 范围内时抛出。</exception>
        public RPCServerBase(IPAddress ipAddress, int localPort)
        {
            if (localPort < 1 || localPort > 65535)
                throw new ArgumentException("端口号必须在 1-65535 之间");

            LocalEndPoint = new IPEndPoint(ipAddress, localPort);
            _syncContext = SynchronizationContext.Current ?? new SynchronizationContext();
        }
        /// <summary>
        /// 使用指定端口号初始化 <see cref="RPCServerBase"/> 类的新实例，监听所有网卡。
        /// </summary>
        /// <param name="localPort">服务器监听的本地端口号，范围 1-65535。</param>
        public RPCServerBase(int localPort) : this(IPAddress.Any, localPort) { }

        /// <summary>
        /// 注册可被远程调用/访问的对象实例。
        /// </summary>
        /// <param name="objectName">对象的注册名称，用于客户端调用时定位目标对象，需符合 <see cref="NamedRegex"/> 命名规则。</param>
        /// <param name="objectInstance">对象实例，不能为 null 或自身实例。</param>
        /// <exception cref="ObjectDisposedException">实例已释放时抛出。</exception>
        /// <exception cref="ArgumentNullException">对象名称为空、格式不正确或实例为 null 时抛出。</exception>
        /// <exception cref="ArgumentException">对象类型不合法或名称已存在时抛出。</exception>
        public void RegisterObject(string objectName, object objectInstance)
        {
            if (_isDisposed) throw new ObjectDisposedException(nameof(RPCServerBase));

            if (string.IsNullOrWhiteSpace(objectName) || !NamedRegex.IsMatch(objectName))
                throw new ArgumentNullException(nameof(objectName), "对象名称不能为空或命名格式不正确");

            if (objectInstance == null || objectInstance == this)
                throw new ArgumentNullException(nameof(objectInstance), "对象实例不能为空，也不能注册自身实例");

            var objectType = objectInstance.GetType();
            if (objectType.IsValueType || objectType == typeof(RPCServerBase) /*|| objectType == typeof(RPCClient)*/)
                throw new ArgumentException($"不能注册的对象实例类型 {objectType}");

            if (RegisterObjects.ContainsKey(objectName))
                throw new ArgumentException($"已存在的对象名称 {objectName}");

            if (!RegisterObjects.TryAdd(objectName, objectInstance))
                throw new ArgumentException($"注册对象 {objectName} 实例  {objectInstance} 失败");

            CacheInstanceMethods(objectName, objectInstance);
        }
        /// <summary>
        /// 缓存实例的公共方法和公共扩展方法
        /// </summary>
        /// <param name="objectName"></param>
        /// <param name="objectInstance"></param>
        private void CacheInstanceMethods(string objectName, object objectInstance)
        {
            if (InstanceCacheMethodInfos.ContainsKey(objectName)) return;

            var instanceType = objectInstance.GetType();
            // 实例的公共方法
            foreach (var method in instanceType.GetMethods())
            {
                if (!method.IsPublic) continue;
                if (method.IsVirtual || method.IsSpecialName) continue;

                var parameters = method.GetParameters();
                var paramsSign = parameters.Select(p => p.ParameterType).GetTypesSignature();

                if (paramsSign.Contains("REF")) continue;
                var objectMethodKey = $"{objectName}.{method.Name}({paramsSign})";

                var count = 0;
                var objectMethodKeyClone = objectMethodKey;
                while (InstanceCacheMethodInfos.ContainsKey(objectMethodKeyClone))
                {
                    objectMethodKeyClone = $"{objectMethodKey}_{count++}";
                }

                Debug.WriteLine($"{objectMethodKeyClone}");
                InstanceCacheMethodInfos.TryAdd(objectMethodKeyClone, method);
            }
            Debug.WriteLine($"-----------------------------------");
            // 实例的公共扩展方法
            var extensionType = typeof(ExtensionAttribute);
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly.GlobalAssemblyCache) continue;
                foreach (var type in assembly.GetExportedTypes())
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
                            var paramsSign = parameters.Skip(1).Select(p => p.ParameterType).GetTypesSignature();
                            var objectMethodKey = $"{objectName}.{method.Name}({paramsSign})_(Ext)";

                            var count = 0;
                            var objectMethodKeyClone = objectMethodKey;
                            while (InstanceCacheMethodInfos.ContainsKey(objectMethodKeyClone))
                            {
                                objectMethodKeyClone = $"{objectMethodKey}_{count++}";
                            }

                            Debug.WriteLine($"{objectMethodKeyClone}");
                            InstanceCacheMethodInfos.TryAdd(objectMethodKeyClone, method);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// 启动 RPC 服务端，开始监听客户端连接。
        /// <para>如果服务已在运行，则忽略本次调用。</para>
        /// </summary>
        /// <exception cref="ObjectDisposedException">实例已释放时抛出。</exception>
        public void Start()
        {
            if (_isDisposed)
                throw new ObjectDisposedException(nameof(RPCServerBase));

            if (IsRunning) return;

            _cts = new CancellationTokenSource();
            _tcpListener = new TcpListener(LocalEndPoint);

            try
            {
                _tcpListener.Start();
                Trace.WriteLine($"RPC 服务已启动，监听地址：{_tcpListener.LocalEndpoint}");
            }
            catch (Exception ex)
            {
                Trace.TraceError($"启动 RPC 服务失败：({ex.GetType().Name}) {ex.Message}");
                Stop();
                return;
            }

            var acceptToken = _cts.Token;
            _ = Task.Run(() => AcceptTcpClientAsync(acceptToken), acceptToken);

            var invokeToken = _cts.Token;
            _ = Task.Run(() => CallInvokeMessageAsync(invokeToken), invokeToken);
        }
        /// <summary>
        /// 停止 RPC 服务端，取消所有待处理消息数据，断开所有客户端连接并释放监听器资源。
        /// <para>如果服务未运行，则忽略本次调用。</para>
        /// </summary>
        public void Stop()
        {
            if (!IsRunning) return;

            _cts.Cancel();
            while (!InvokeMessages.IsEmpty)
            {
                InvokeMessages.TryDequeue(out _);
            }

            lock (_clients)
            {
                foreach (var client in _clients)
                {
                    try
                    {
                        client.Dispose();
                    }
                    catch
                    {
                        // 忽略
                    }
                }
                _clients.Clear();
            }

            try
            {
                _tcpListener?.Stop();
                _tcpListener?.Server.Dispose();
            }
            catch
            {
                // 忽略
            }

            _cts.Dispose();

            _cts = null;
            _tcpListener = null;
            Trace.TraceInformation($"RPC 服务已停止");
        }

        /// <summary>
        /// 异步接受 TCP 客户端连接的主循环。
        /// </summary>
        /// <param name="cancelToken">用于取消接受操作的取消令牌。</param>
        /// <returns>一个表示异步操作的任务。</returns>
        private async Task AcceptTcpClientAsync(CancellationToken cancelToken)
        {
            try
            {
                while (!cancelToken.IsCancellationRequested)
                {
                    var client = await _tcpListener.AcceptTcpClientAsync().ConfigureAwait(false);
                    _ = ReadTcpClientAsync(client, cancelToken);
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
                Trace.TraceError($"RPC 服务等待客户端连接时生发异常：({ex.GetType().Name}) {ex.Message}");
            }
        }

        /// <summary>
        /// 处理单个 TCP 客户端连接的读取循环。
        /// <para>使用环形缓冲（Ring Buffer）模式，以 CRLF 分隔数据行，通过 <see cref="ParseInvokeMessage"/> 解析数据行。</para>
        /// </summary>
        /// <param name="client">已接受的 TCP 客户端连接。</param>
        /// <param name="cancelToken">用于取消读取操作的取消令牌。</param>
        /// <returns>一个表示异步操作的任务。</returns>
        private async Task ReadTcpClientAsync(TcpClient client, CancellationToken cancelToken)
        {
            lock (_clients) { _clients.Add(client); }

            // 配置已接受的 TcpClient 的 Socket 参数
            client.SendBufferSize = this.SendBufferSize;
            client.ReceiveBufferSize = this.ReceiveBufferSize;
            //client.NoDelay = true; // 禁用 Nagle 算法（低延迟场景）
            //client.LingerState = new LingerOption(true, 3);

            var clientEndPoint = client.Client.RemoteEndPoint as IPEndPoint;
            ClientConnected?.Invoke(this, clientEndPoint);
            Trace.TraceInformation($"客户端 {clientEndPoint} 已连接");

            // 环形缓冲（Ring Buffer）设计
            // 如果读取的数据全部分析完或刚好分析完一个完整的数据行后，缓冲区后面没有可分析数据时，可以将 offset 指针设置 0
            var bufferSize = client.ReceiveBufferSize / 2;
            var clientBuffer = new byte[bufferSize];    

            var offset = 0;     // buffer 中已写入数据的末尾位置（下一次 ReadAsync 的写入起点，写指针）
            var length = 0;     // buffer 中有效数据的长度 (未分析读取的数据量)
            var position = 0;   // 数据分析的起始位置 (读指针)

            try
            {
                var clientStream = client.GetStream();
                while (!cancelToken.IsCancellationRequested && client.Connected)
                {
                    var count = await clientStream.ReadAsync(clientBuffer, offset, bufferSize - offset, cancelToken);
                    if (count == 0) break;  // 客户端已断开了连接
                    Debug.WriteLine($"收到来自 {clientEndPoint} 的数据 {count} bytes ");

                    offset += count;
                    length += count;
                    
                    #region 扫描缓冲区中所有完整的数据行
                    while (position < length)
                    {
                        var endIndex = clientBuffer.IndexOf(NewLine, position, length - position);
                        if (endIndex < 0) break;

                        // 提取完整的数据行（不含 NewLine 本身）
                        var messageLine = new ArraySegment<byte>(clientBuffer, position, endIndex - position);

                        // 移动读指针，跳过已消费的数据和换行符
                        position = endIndex + NewLine.Length;

                        // 解析客户端的数据行
                        var invokeMessages = ParseInvokeMessage(messageLine, clientEndPoint);
                        if (invokeMessages != null && invokeMessages.Any())
                        {
                            foreach (var invokeMessage in invokeMessages)
                            {
                                // 客户端调用消息进入队列，等待处理
                                if (ClientMessageInvoking != null)
                                {
                                    var eventArgs = new InvokeMessageEventArgs(invokeMessage, clientEndPoint);
                                    ClientMessageInvoking.Invoke(this, eventArgs);
                                    if (eventArgs.Cancel)
                                    {
                                        Trace.TraceWarning($"客户端 {clientEndPoint} 的调用消息被拦截取消: {invokeMessage}");
                                        continue;
                                    }
                                }

                                invokeMessage.TcpClient = client;
                                invokeMessage.ClientEndPoint = clientEndPoint;
                                InvokeMessages.Enqueue(invokeMessage);
                            }
                        }
                        else
                        {

                        }
                    }
                    #endregion

                    #region 跳过尾随的空白/零值数据
                    // 00 空字符、20 空格、09 水平制表符、0A 换行符、0D 回车符
                    while (position < length)
                    {
                        var b = clientBuffer[position];                        
                        if (b == 0x00 || b == 0x20 || b == 0x09 || b == 0x0A || b == 0x0D)
                        {
                            position++;
                        }
                        else
                        {
                            break;
                        }
                    }
                    #endregion

                    #region 环形缓冲收尾：根据消费情况移动指针
                    // 数据正好分析完 → 所有指针归零
                    if (position == length)  
                    {                        
                        offset = 0;
                        length = 0;
                        position = 0;
                    }
                    // 缓冲区剩余空间不足 1/8，紧凑：将未处理数据移到开头
                    else if (position > 0 && bufferSize - length < bufferSize / 8)
                    {
                        var remaining = length - position;
                        Buffer.BlockCopy(clientBuffer, position, clientBuffer, 0, remaining);
                        Trace.TraceInformation($"移动客户端 {clientEndPoint} 缓冲区数据 {remaining} bytes");

                        offset = remaining;
                        length = remaining;
                        position = 0;
                    }

                    // 缓冲区已经满了，清空防止死锁
                    if (offset == bufferSize)
                    {
                        Trace.TraceWarning($"客户端 {clientEndPoint} 缓冲区已满且无完整消息，清空缓冲区 {length} bytes");
                        offset = 0;
                        length = 0;
                        position = 0;
                    }
                    #endregion
                }
            }
            catch (Exception ex) when (ex is OperationCanceledException || ex is ObjectDisposedException)
            {
                // 正常退出
            }            
            catch (Exception ex)
            {
                Trace.TraceError($"处理客户端 {clientEndPoint} 数据时异常: ({ex.GetType().Name}) {ex.Message}");
            }
            finally
            {
                lock (_clients) { _clients.Remove(client); }

                client.Dispose();
                ClientDisconnected?.Invoke(this, clientEndPoint);
                Trace.TraceInformation($"客户端 {clientEndPoint} 已断开");
            }
        }

        /// <summary>
        /// 解析客户端发送的完整数据行（以 CRLF 作为结束符的一数据行数据）。
        /// <para>子类继承重写该方法，实现不同协议的数据解析逻辑。</para>
        /// </summary>
        /// <param name="messageLine">客户端的数据行字节数据（不含尾部的 CRLF）。</param>
        /// <param name="remoteEndPoint">发送数据的客户端远程端点地址。</param>
        /// <returns>解析成功返回一条或多条 <see cref="InvokeMessage"/> 待调用的消息；可过滤、或失败则返回空集合。</returns>
        protected abstract IEnumerable<InvokeMessage> ParseInvokeMessage(ArraySegment<byte> messageLine, IPEndPoint remoteEndPoint);

        /// <summary>
        /// 将调用结果序列化为响应数据，用于发送回客户端。
        /// <para>子类继承重写该方法，实现不同协议的响应格式。</para>
        /// </summary>
        /// <param name="responseMessage">调用结果对象。</param>
        /// <param name="remoteEndPoint">目标客户端的远程端点地址。</param>
        /// <returns>序列化后的响应 UTF-8 字节数组；如果不响应则时返回空数组。 </returns>
        protected abstract byte[] ConvertResponseMessage(ResponseMessage responseMessage, IPEndPoint remoteEndPoint);

        /// <summary>
        /// 从消息队列中取出客户端调用消息并执行反射调用，随后将结果响应回客户端。
        /// </summary>
        /// <param name="cancelToken">用于取消处理循环的取消令牌。</param>
        /// <returns>一个表示异步操作的任务。</returns>
        private async Task CallInvokeMessageAsync(CancellationToken cancelToken)
        {
            while (!cancelToken.IsCancellationRequested)
            {
                if (InvokeMessages.IsEmpty)
                {
                    //await Task.Delay(1).ConfigureAwait(false);
                    //Trace.WriteLine("");
                    continue;
                }

                if (!InvokeMessages.TryDequeue(out var invokeMessage)) continue;

                TryCallMethod(invokeMessage, out var invokeResult);

                // 执行响应客户端调用结果
                if (invokeResult == null) continue;
                if (invokeMessage.TcpClient == null) continue;
                if (!invokeMessage.TcpClient.Connected) continue;

                try
                {
                    var responseBytes = ConvertResponseMessage(invokeResult, invokeMessage.ClientEndPoint);
                    if (responseBytes?.Length > 0)
                    {
                        var stream = invokeMessage.TcpClient.GetStream();
                        await stream.WriteAsync(responseBytes, 0, responseBytes.Length, cancelToken).ConfigureAwait(false);
                        await stream.FlushAsync(cancelToken).ConfigureAwait(false);
                    }
                }
                catch (Exception ex) when (ex is OperationCanceledException || ex is ObjectDisposedException)
                {
                    // 正常退出
                }
                catch (Exception ex)
                {
                    Trace.TraceError($"响应客户端 {invokeMessage.TcpClient.Client.RemoteEndPoint} 调用结果时异常: ({ex.GetType().Name}) {ex.Message}");
                }
                finally
                {
                    invokeMessage.TcpClient = null;
                    invokeMessage.ClientEndPoint = null;
                    invokeMessage = null;
                }
            }
        }

        private readonly SemaphoreSlim InvokeMessageSemaphoreSlim = new SemaphoreSlim(60);

        // CallMethodInvokeAsync();
        protected async Task CallInvokeMessageAsync(InvokeMessage invokeMessage, CancellationToken cancelToken)
        {
            if (invokeMessage == null) return;

            await InvokeMessageSemaphoreSlim.WaitAsync();

            try
            {
                #region 基本检查
                if (!RegisterObjects.TryGetValue(invokeMessage.ObjectName, out var objectInstance))
                {
                    var responseMessage = ResponseMessage.Create(invokeMessage, -1, $"Object '{invokeMessage.ObjectName}' not register");
                    await WriteResponseMessageAsync(invokeMessage, responseMessage, cancelToken).ConfigureAwait(false);
                    return;
                }
                var objectMetod = $"{invokeMessage.ObjectName}.{invokeMessage.MethodName}";
                if (MethodFilters.IndexOf($"*.{invokeMessage.MethodName}") != -1 || MethodFilters.IndexOf(objectMetod) != -1)
                {
                    var responseMessage = ResponseMessage.Create(invokeMessage, -2, $"Object method '{objectMetod}' is not allowed to be invoked");
                    await WriteResponseMessageAsync(invokeMessage, responseMessage, cancelToken).ConfigureAwait(false);
                    return;
                }
                #endregion

                #region 跟据输入的参数签名查找方法
                var paramsSign = "";
                var paramsLength = invokeMessage.Parameters?.Length ?? 0;
                if (paramsLength > 0) paramsSign = invokeMessage.Parameters.GetParamsSignature();
                var methodCacheKey = $"{invokeMessage.ObjectName}.{invokeMessage.MethodName}({paramsSign})";
                Trace.WriteLine($"methodCacheKey:{methodCacheKey}");
                if (!InstanceCacheMethodInfos.TryGetValue(methodCacheKey, out var methodInfo))
                {
                    var responseMessage = ResponseMessage.Create(invokeMessage, -3, $"Object method '{methodCacheKey}' is not available");
                    await WriteResponseMessageAsync(invokeMessage, responseMessage, cancelToken).ConfigureAwait(false);
                    return;
                }
                #endregion

                #region 解析转换参数
                var offset = 0;
                object[] convertParameters = null;
                var methodParameters = methodInfo.GetParameters();
                var isExtensionMethod = methodInfo.IsDefined(typeof(ExtensionAttribute), false); // 是否是实例类型的扩展方法
                if (isExtensionMethod)
                {
                    offset = 1;
                    convertParameters = new object[paramsLength + 1];
                    convertParameters[0] = objectInstance;
                }
                else
                {
                    offset = 0;
                    convertParameters = paramsLength == 0 ? null : new object[paramsLength];
                }
                for (int i = 0; i < paramsLength; i++)
                {
                    var destinationType = methodParameters[i + offset].ParameterType;
                    if (!TypeExtensions.TryConvertParameter(invokeMessage.Parameters[i], destinationType, out object convertValue))
                    {
                        var responseMessage = ResponseMessage.Create(invokeMessage, -4, $"Object method '{objectMetod}' parameter {i} convert from {invokeMessage.Parameters[i]?.GetType()} to {destinationType} failed");
                        await WriteResponseMessageAsync(invokeMessage, responseMessage, cancelToken).ConfigureAwait(false);
                        return;
                    }

                    convertParameters[i + offset] = convertValue;
                }
                #endregion

                #region 执行方法调用
                _syncContext.Post(async (state) =>
                {
                    var result = methodInfo.Invoke(objectInstance, convertParameters);
                    if (state is InvokeMessage iMessage && iMessage.ResponseMode > 0)
                    {
                        var responseMessage = ResponseMessage.Create(invokeMessage);
                        responseMessage.Description = "OK";
                        responseMessage.ReturnValue = result;
                        responseMessage.ReturnType = methodInfo.ReturnType;
                        responseMessage.Code = methodInfo.ReturnType == typeof(void) ? 0 : 1;

                        await WriteResponseMessageAsync(invokeMessage, responseMessage, cancelToken).ConfigureAwait(false);
                    }
                }, invokeMessage);
                #endregion
            }
            catch (Exception ex)
            {
                Trace.TraceWarning($"客户端 {invokeMessage.ClientEndPoint} 执行调用消息异常： {ex.GetType().Name} {ex.Message}");
                var responseMessage = ResponseMessage.Create(invokeMessage, -5, $"Object method '{invokeMessage.ObjectName}.{invokeMessage.MethodName}' invoke exception: ({ex.GetType().Name}) {ex.Message}");
                await WriteResponseMessageAsync(invokeMessage, responseMessage, cancelToken).ConfigureAwait(false);
            }
            finally
            {
                InvokeMessageSemaphoreSlim.Release();
            }
        }

        /// <summary>
        /// 写响应消息
        /// </summary>
        /// <param name="invokeMessage"></param>
        /// <param name="responseMessage"></param>
        /// <param name="cancelToken"></param>
        /// <returns></returns>
        protected async Task WriteResponseMessageAsync(InvokeMessage invokeMessage, ResponseMessage responseMessage, CancellationToken cancelToken = default)
        {
            if (invokeMessage == null || responseMessage == null) return;
            if (invokeMessage.ResponseMode == -1) return;
            if (invokeMessage.TcpClient != null || !invokeMessage.TcpClient.IsConnected()) return;

            var responseBytes = ConvertResponseMessage(responseMessage, invokeMessage.ClientEndPoint);
            if (responseBytes?.Length > 0)
            {
                var stream = invokeMessage.TcpClient.GetStream();
                await stream.WriteAsync(responseBytes, 0, responseBytes.Length, cancelToken).ConfigureAwait(false);
                await stream.FlushAsync(cancelToken).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// 尝试通过反射调用注册对象的公共实例方法或公共扩展方法。
        /// <para>支持方法过滤器检查、参数类型解析、扩展方法匹配。</para>
        /// </summary>
        /// <param name="invokeMessage">客户端调用消息。</param>
        /// <param name="invokeResult">输出调用结果，包含状态码、描述信息和返回值。调用失败时返回错误码。</param>
        /// <returns>调用成功返回 <c>true</c>；否则返回 <c>false</c>。</returns>
        public bool TryCallMethod(InvokeMessage invokeMessage, out ResponseMessage invokeResult)
        {
            invokeResult = null;
            if (invokeMessage == null) return false;
            
            if (!RegisterObjects.TryGetValue(invokeMessage.ObjectName, out var objectInstance))
            {
                invokeResult = ResponseMessage.Create(invokeMessage, -1, $"Object '{invokeMessage.ObjectName}' not register");
                return false;
            }

            var objectMetod = $"{invokeMessage.ObjectName}.{invokeMessage.MethodName}";
            if (MethodFilters.IndexOf($"*.{invokeMessage.MethodName}") != -1 || MethodFilters.IndexOf(objectMetod) != -1)
            {
                invokeResult = ResponseMessage.Create(invokeMessage, -2, $"Object method '{objectMetod}' is not allowed to be invoked");
                return false;
            }

            var instanceType = objectInstance.GetType();
            var methodInfos = GetInvokeMethods(instanceType, invokeMessage);
            if (methodInfos.Count() != 1)
            {
                invokeResult = ResponseMessage.Create(invokeMessage, -3, $"Object method '{objectMetod}' has {methodInfos.Count()} same methods named '{invokeMessage.MethodName}'");
                return false;
            }

            // 参数解析转换
            var offset = 0;
            object[] convertParameters = null;
            var paramsLength = invokeMessage.Parameters?.Length ?? 0;
            var methodInfo = methodInfos.First();
            var methodParameters = methodInfo.GetParameters();
            var isExtensionMethod = methodInfo.IsDefined(typeof(ExtensionAttribute), false); // 是否是实例类型的扩展方法
            if (isExtensionMethod)
            {
                offset = 1;
                convertParameters = new object[paramsLength + 1];
                convertParameters[0] = objectInstance;
            }
            else
            {
                offset = 0;
                convertParameters = paramsLength == 0 ? null : new object[paramsLength];
            }
            for (int i = 0; i < paramsLength; i++)
            {
                var destinationType = methodParameters[i + offset].ParameterType;
                if (!TypeExtensions.TryConvertParameter(invokeMessage.Parameters[i], destinationType, out object convertValue))
                {
                    invokeResult = ResponseMessage.Create(invokeMessage, -4, $"Object method '{objectMetod}' parameter {i} convert from {invokeMessage.Parameters[i]?.GetType()} to {destinationType} failed");
                    return false;
                }

                convertParameters[i + offset] = convertValue;
            }

            // 消息分派到指定的上下文
            Action<SendOrPostCallback, object> dispatcher = _syncContext.Post;

            invokeResult = ResponseMessage.Create(invokeMessage);
            invokeResult.ReturnType = methodInfo.ReturnType;

            try
            {
                dispatcher.Invoke(state =>
                {
                    var result = methodInfo.Invoke(objectInstance, convertParameters);

                    if (state is ResponseMessage iResult)
                    {
                        iResult.Code = methodInfo.ReturnType == typeof(void) ? 0 : 1;
                        iResult.Description = "OK";
                        iResult.ReturnValue = result;

                        //await Task.Delay(0);
                    }
                }, invokeResult);
                return true;
            }
            catch (Exception ex)
            {
                invokeResult.Code = -5;
                invokeResult.Description = $"Object method '{objectMetod}' invoke exception: ({ex.GetType().Name}) {ex.Message}";

                Trace.TraceWarning($"实例对象 {invokeMessage.ObjectName}({instanceType.FullName}) 的方法 {methodInfo.Name}({paramsLength}) 调用异常: {ex.Message}");
            }

            return false;
        }

        /// <summary>
        /// 获取注册对象中匹配指定方法的 <see cref="MethodInfo"/> 集合。
        /// <para>优先从缓存中查找已解析的方法；缓存未命中时搜索实例方法及扩展方法。</para>
        /// </summary>
        /// <param name="instanceType">注册对象的实际类型。</param>
        /// <param name="invokeMessage">客户端调用消息。</param>
        /// <returns>
        /// 匹配的方法集合：<br/>
        /// — 0 个：未找到匹配方法；<br/>
        /// — 1 个：唯一匹配（自动缓存）；<br/>
        /// — 多个：存在歧义，需通过参数类型进一步筛选。
        /// </returns>
        private IEnumerable<MethodInfo> GetInvokeMethods(Type instanceType, InvokeMessage invokeMessage)
        {
            if (instanceType == null || invokeMessage == null) return Array.Empty<MethodInfo>();

            var paramsSign = "";
            var paramsLength = invokeMessage.Parameters?.Length ?? 0;
            if (paramsLength > 0)
            {
                paramsSign = invokeMessage.Parameters.GetParamsSignature();
            }

            var methodCacheKey = $"{invokeMessage.ObjectName}.{invokeMessage.MethodName}({paramsSign})";
            Trace.WriteLine($"methodCacheKey:{methodCacheKey}");

            if (InstanceCacheMethodInfos.TryGetValue(methodCacheKey, out var methodInfo)) return new[] { methodInfo };

            return Array.Empty<MethodInfo>();
        }


        /// <inheritdoc/> 
        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;

            Stop();
            RegisterObjects.Clear();
            while (!InvokeMessages.IsEmpty)
            {
                InvokeMessages.TryDequeue(out _);
            }
        }
    }

}
