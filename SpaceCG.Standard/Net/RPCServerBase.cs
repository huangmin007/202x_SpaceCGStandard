using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.CompilerServices;
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
    /// <para>提供 TCP 客户端连接管理、消息接收解析、方法反射调用、结果响应等基础能力。</para>
    /// <para>
    /// <list type="bullet">
    /// <item>子类继承实现 <see cref="ParseInvokeMessage"/> 和 <see cref="ResponseInvokeMessage"/> 以支持不同的消息协议</item>
    /// <item>通过 <see cref="ClientMessageInvoking"/> 事件可拦截、取消客户端调用消息的执行</item>
    /// <item>基类通数据行进行分组，子类可以将一个消息放在一行，或将多个消息放在一行</item>
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

        /// <summary> 客户端连接事件。 </summary>
        public event EventHandler<IPEndPoint> ClientConnected;
        /// <summary> 客户端断开连接事件。 </summary>
        public event EventHandler<IPEndPoint> ClientDisconnected;
        /// <summary>
        /// 客户端调用消息接收事件。
        /// <para>通过设置 <see cref="InvokeMessageEventArgs.Cancel"/> 为 <c>true</c> 可拦截取消客户端调用消息的执行。</para>
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
        /// <summary> 消息行分隔符字节数组，使用 CRLF（0x0D, 0x0A）作为新行标识符。 </summary>
        public static readonly byte[] NewLine = new byte[] { 0x0D, 0x0A };
        
        /// <summary>
        /// 方法过滤器列表，匹配的方法将不允许被远程调用。
        /// <para>格式为：{ObjectName}.{MethodName}，其中 ObjectName 支持通配符 '*'。</para>
        /// <para>例如："*.Dispose" 禁止反射访问所有对象的 Dispose 方法；默认已添加 "*.Dispose" 和 "*.Close"。</para>
        /// </summary>
        public readonly List<string> MethodFilters = new List<string>(16) { "*.Dispose", "*.Close" };

        /// <summary> 方法过滤器的 <see cref="HashSet{T}"/> 版本，用于 O(1) 快速查找。 </summary>
        private readonly HashSet<string> _methodFilterSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "*.Dispose", "*.Close"
        };

        /// <summary> 客户端调用消息队列，服务端可从该队列中获取客户端的调用消息，并执行调用。 </summary>
        private readonly ConcurrentQueue<InvokeMessage> InvokeMessages = new ConcurrentQueue<InvokeMessage>();
        /// <summary> 注册的对象实例集合 </summary>
        private readonly ConcurrentDictionary<string, object> RegisterObjects = new ConcurrentDictionary<string, object>();
        /// <summary> 历史调用过的唯一方法，无歧义的方法  </summary>
        //private readonly ConcurrentDictionary<string, MethodInfo> CacheMethodInfos = new ConcurrentDictionary<string, MethodInfo>();
        /// <summary> 
        /// 实例的缓存方法信息；在 <see cref="RegisterObject"/> 时将实例的所有公共方法和扩展方法预注册在此。
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
                var paramsKey = string.Join(",", parameters.Select(p => TypeExtensions.GetParameterKey(p.ParameterType)));

                if (paramsKey.Contains("REF")) continue;
                var objectMethodKey = $"{objectName}.{method.Name}({paramsKey})";

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
                            var paramsKey = string.Join(",", parameters.Select(p => TypeExtensions.GetParameterKey(p.ParameterType)));
                            var objectMethodKey = $"{objectName}.{method.Name}({paramsKey})(Ext)";

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
        /// 停止 RPC 服务端，取消所有待处理消息，断开所有客户端连接并释放监听器资源。
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
        /// <para>使用环形缓冲（Ring Buffer）模式，以 CRLF 分隔行消息，通过 <see cref="ParseInvokeMessage"/> 解析消息。</para>
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

            var remoteEndPoint = client.Client.RemoteEndPoint as IPEndPoint;
            ClientConnected?.Invoke(this, remoteEndPoint);
            Trace.TraceInformation($"客户端 {remoteEndPoint} 已连接");

            // 环形缓冲（Ring Buffer）设计
            // 如果读取的数据全部分析完或刚好分析完一个完整的数据行后，可以将 offset 设置 0
            var bufferSize = client.ReceiveBufferSize / 2;
            var clientBuffer = new byte[bufferSize];    

            var offset = 0;     // buffer 中已写入数据的末尾位置（下一次 ReadAsync 的写入起点，写指针）
            var length = 0;     // buffer 中有效数据的长度 (未分析读取的数据量)
            var position = 0;   // 数据分析的起始位置 (读指针)

            try
            {
                var stream = client.GetStream();
                while (!cancelToken.IsCancellationRequested && client.Connected)
                {
                    var count = await stream.ReadAsync(clientBuffer, offset, bufferSize - offset, cancelToken);
                    if (count == 0) break;  // 客户端已断开了连接
                    Debug.WriteLine($"收到来自 {remoteEndPoint} 的数据 {count} bytes ");

                    offset += count;
                    length += count;

                    #region 扫描缓冲区中所有完整的行消息
                    while (position < length)
                    {
                        var index = clientBuffer.IndexOf(NewLine, position, length - position);
                        if (index < 0) break;

                        // 提取完整的行消息（不含 NewLine 本身）
                        var messageLine = new ArraySegment<byte>(clientBuffer, position, index - position);

                        // 移动读指针，跳过已消费的消息和换行符
                        position = index + NewLine.Length;

                        // 解析客户端的行消息
                        var invokeMessages = ParseInvokeMessage(messageLine, remoteEndPoint);
                        if (invokeMessages != null && invokeMessages.Any())
                        {
                            foreach (var invokeMessage in invokeMessages)
                            {
                                // 客户端调用消息进入队列，等待处理
                                if (ClientMessageInvoking != null)
                                {
                                    var eventArgs = new InvokeMessageEventArgs(invokeMessage, remoteEndPoint);
                                    ClientMessageInvoking.Invoke(this, eventArgs);
                                    if (eventArgs.Cancel)
                                    {
                                        Trace.TraceWarning($"客户端 {remoteEndPoint} 的调用消息被拦截取消: {invokeMessage}");
                                        continue;
                                    }
                                }

                                invokeMessage.TcpClient = client;
                                invokeMessage.ClientEndPoint = remoteEndPoint;
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
                        Trace.TraceInformation($"移动客户端 {remoteEndPoint} 缓冲区数据 {remaining} bytes");

                        offset = remaining;
                        length = remaining;
                        position = 0;
                    }

                    // 缓冲区已经满了，清空防止死锁
                    if (offset == bufferSize)
                    {
                        Trace.TraceWarning($"客户端 {remoteEndPoint} 缓冲区已满且无完整消息，清空缓冲区 {length} bytes");
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
                Trace.TraceError($"处理客户端 {remoteEndPoint} 数据时异常: ({ex.GetType().Name}) {ex.Message}");
            }
            finally
            {
                lock (_clients) { _clients.Remove(client); }

                client.Dispose();
                ClientDisconnected?.Invoke(this, remoteEndPoint);
                Trace.TraceInformation($"客户端 {remoteEndPoint} 已断开");
            }
        }

        /// <summary>
        /// 解析客户端发送的完整消息行（以 CRLF 作为结束符的一行消息数据）。
        /// <para>子类继承重写该方法，实现不同协议的消息解析逻辑。</para>
        /// </summary>
        /// <param name="messageLine">客户端的行消息字节数据（不含尾部的 CRLF）。</param>
        /// <param name="remoteEndPoint">发送消息的客户端远程端点地址。</param>
        /// <returns>解析成功返回一条或多条 <see cref="InvokeMessage"/>；过滤、失败则返回空集合。</returns>
        protected abstract IEnumerable<InvokeMessage> ParseInvokeMessage(ArraySegment<byte> messageLine, IPEndPoint remoteEndPoint);

        /// <summary>
        /// 将调用结果序列化为响应数据，用于发送回客户端。
        /// <para>子类继承重写该方法，实现不同协议的响应格式。</para>
        /// </summary>
        /// <param name="invokeResult">调用结果对象。</param>
        /// <param name="remoteEndPoint">目标客户端的远程端点地址。</param>
        /// <returns>序列化后的响应 UTF-8 字节数组；如果不响应则时返回空数组。 </returns>
        protected abstract byte[] ResponseInvokeMessage(InvokeResult invokeResult, IPEndPoint remoteEndPoint);

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
                    //Thread.Sleep(1);
                    await Task.Delay(1, cancelToken).ConfigureAwait(false);
                    continue;
                }

                if (!InvokeMessages.TryDequeue(out var invokeMessage))
                {
                    //Thread.Sleep(1);
                    await Task.Delay(1, cancelToken).ConfigureAwait(false);
                    continue;
                }

                TryCallMethod(invokeMessage, out var invokeResult);

                // 执行响应客户端调用结果
                if (invokeResult == null) continue;
                if (invokeMessage.TcpClient == null) continue;
                if (!invokeMessage.TcpClient.Connected) continue;

                try
                {
                    var responseBytes = ResponseInvokeMessage(invokeResult, invokeMessage.ClientEndPoint);
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

        /// <summary>
        /// 尝试通过反射调用注册对象的公共实例方法或公共扩展方法。
        /// <para>支持方法过滤器检查、参数类型解析、扩展方法匹配。</para>
        /// </summary>
        /// <param name="invokeMessage">客户端调用消息。</param>
        /// <param name="invokeResult">输出调用结果，包含状态码、描述信息和返回值。调用失败时返回错误码。</param>
        /// <returns>调用成功返回 <c>true</c>；否则返回 <c>false</c>。</returns>
        public bool TryCallMethod(InvokeMessage invokeMessage, out InvokeResult invokeResult)
        {
            invokeResult = null;
            if (invokeMessage == null) return false;
            
            if (!RegisterObjects.TryGetValue(invokeMessage.ObjectName, out var objectInstance))
            {
                invokeResult = InvokeResult.Create(invokeMessage, -1, $"Object '{invokeMessage.ObjectName}' not register");
                return false;
            }
            if (MethodFilters.IndexOf($"*.{invokeMessage.MethodName}") != -1 || MethodFilters.IndexOf($"{invokeMessage.ObjectName}.{invokeMessage.MethodName}") != -1)
            {
                invokeResult = InvokeResult.Create(invokeMessage, -2, $"Object method '{invokeMessage.ObjectName}.{invokeMessage.MethodName}' is not allowed to be invoked");
                return false;
            }

            var instanceType = objectInstance.GetType();
            var methodInfos = GetInvokeMethods(instanceType, invokeMessage);
            if (methodInfos.Count() != 1)
            {
                invokeResult = InvokeResult.Create(invokeMessage, -3, $"Object method '{invokeMessage.ObjectName}.{invokeMessage.MethodName}' has {methodInfos.Count()} same methods named '{invokeMessage.MethodName}'");
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
                    invokeResult = InvokeResult.Create(invokeMessage, -4, $"Object method '{invokeMessage.ObjectName}.{invokeMessage.MethodName}' parameter {i} convert from {invokeMessage.Parameters[i]?.GetType()} to {destinationType} failed");
                    return false;
                }

                convertParameters[i + offset] = convertValue;
            }

            // 消息分派到指定的上下文
            Action<SendOrPostCallback, object> dispatcher;
            if (invokeMessage.IsAsync)
                dispatcher = _syncContext.Post;
            else dispatcher = _syncContext.Send;

            invokeResult = InvokeResult.Create(invokeMessage);
            invokeResult.ReturnType = methodInfo.ReturnType;

            try
            {                
                dispatcher.Invoke(state =>
                {
                    var result = methodInfo.Invoke(objectInstance, convertParameters);

                    if (state is InvokeResult iResult)
                    {
                        iResult.Code = methodInfo.ReturnType == typeof(void) ? 0 : 1;
                        iResult.Description = "OK";
                        iResult.ReturnValue = result;
                    }
                }, invokeResult);
                return true;
            }
            catch (Exception ex)
            {
                invokeResult.Code = -5;
                invokeResult.Description = $"Object method '{invokeMessage.ObjectName}.{invokeMessage.MethodName}' invoke exception: ({ex.GetType().Name}) {ex.Message}";

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

            var paramsKey = "";
            var paramsLength = invokeMessage.Parameters?.Length ?? 0;
            if (paramsLength > 0)
            {                
                paramsKey = string.Join(",", invokeMessage.Parameters.Select(p => TypeExtensions.GetParameterKey(p.GetType(), "SVT")));
            }

            var methodCacheKey = $"{invokeMessage.ObjectName}.{invokeMessage.MethodName}({paramsKey})";
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

    internal static partial class TypeExtensions
    {
        internal static readonly ConcurrentDictionary<Type, Type[]> CacheTypeInterfaces = new ConcurrentDictionary<Type, Type[]>();

        /// <summary>
        /// 获取参数的标志信息
        /// </summary>
        /// <param name="paramType"></param>
        /// <returns></returns>
        internal static string GetParameterKey(Type paramType)
        {
            Trace.WriteLine($"paramType:{paramType}");
            if (paramType == null) return "";
            if (paramType.IsEnum || paramType.IsValueType || paramType == typeof(string)) return "SVT";

            //Trace.WriteLine($"IsArray:{paramType.IsArray} IsGenericType:{paramType.IsGenericType}");
            if (paramType.IsArray) return $"[{GetParameterKey(paramType.GetElementType())}]";
            if (paramType.IsGenericType && paramType.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            {
                var genericArgs = paramType.GetGenericArguments();
                if (genericArgs.Length == 1) return $"[{GetParameterKey(genericArgs[0])}]";
                else return $"[({genericArgs.Length},REF)]";
            }

            return "REF";
        }

        /// <summary>
        /// 获取参数的标志信息
        /// </summary>
        /// <param name="paramType"></param>
        /// <param name="returnFlag"></param>
        /// <returns></returns>
        internal static string GetParameterKey(Type paramType, string returnFlag)
        {
            Trace.WriteLine($"paramType:{paramType}");
            if (paramType == null) return "";
            if (paramType.IsEnum || paramType.IsValueType || paramType == typeof(string)) return "SVT";

            //Trace.WriteLine($"IsArray:{paramType.IsArray} IsGenericType:{paramType.IsGenericType}");
            if (paramType.IsArray) return $"[{GetParameterKey(paramType.GetElementType(), returnFlag)}]";
            if (paramType.IsGenericType && paramType.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            {
                var genericArgs = paramType.GetGenericArguments();
                if (genericArgs.Length == 1) return $"[{GetParameterKey(genericArgs[0], returnFlag)}]";
                else return $"[({genericArgs.Length},REF)]";
            }

            return returnFlag;
        }

        /// <summary>
        /// 判断类型是否实现 IEnumerable&lt;T&gt;
        /// </summary>
        /// <param name="type"></param>
        internal static bool ImplementsIEnumerable(Type type)
        {
            if (type == null) return false;

            Type iEnumerableType = typeof(IEnumerable<>);
            var typeInterfaces = CacheTypeInterfaces.GetOrAdd(type, t => t.GetInterfaces());

            // 检查类型是否直接实现 IEnumerable<T>
            foreach (var interfaceType in typeInterfaces)
            {
                if (interfaceType.IsGenericType && interfaceType.GetGenericTypeDefinition() == iEnumerableType)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 尝试将调用参数值转换为目标方法参数类型。
        /// <para>支持标量值类型、字符串、数组和 IEnumerable&lt;T&gt; 多层集合类型的递归转换。</para>
        /// </summary>
        /// <param name="value">原始参数值（来自 <see cref="StringExtensions.ToObjectArray"/> 输出，叶子节点均为 <see cref="string"/>）。</param>
        /// <param name="targetType">目标参数类型。</param>
        /// <param name="conversionValue">输出的转换后值。</param>
        /// <returns>转换成功返回 <c>true</c>；否则返回 <c>false</c>。</returns>
        internal static bool TryConvertParameter(object value, Type targetType, out object conversionValue)
        {
            conversionValue = null;
            if (targetType == null) return false;
            if (value == null || targetType == typeof(void)) return true;

            Type valueType = value.GetType();
            // 类型兼容检查（含接口实现关系）
            if (!targetType.IsArray && (valueType == targetType || targetType.IsAssignableFrom(valueType)))
            {
                conversionValue = value;
                return true;
            }
            // 标量：值类型 & 字符串 转换
            if ((targetType.IsValueType || targetType == typeof(string)) && (valueType.IsValueType || valueType == typeof(string)))
            {
                if (value is string stringValue && stringValue.TryConvertTo(targetType, out var targetValue)) conversionValue = targetValue;
                else conversionValue = value;
                return true;
            }

            // 数组：元素类型相同 → 直接赋值
            if (targetType.IsArray && valueType.IsArray && targetType.GetElementType() == valueType.GetElementType())
            {
                conversionValue = value;
                return true;
            }
            // 数组：元素类型不同 → 递归转换每个元素
            if (targetType.IsArray && valueType.IsArray && targetType.GetElementType() != valueType.GetElementType())
            {
                return TryConvertToArray((Array)value, targetType.GetElementType(), out conversionValue);
            }

            // 目标类型是：System.Collections.IEnumerable<T> 或实现、继承 IEnumerable<T> 接口的子类
            if (targetType.IsGenericType && valueType.IsArray && targetType.GetGenericArguments()?.Length == 1)
            {
                var genericTypeDefinition = targetType.GetGenericTypeDefinition();
                if (genericTypeDefinition == typeof(IEnumerable<>) || ImplementsIEnumerable(genericTypeDefinition))
                {
                    return TryConvertToArray((Array)value, targetType.GetGenericArguments()[0], out conversionValue);
                }
            }

            return false;
        }

        /// <summary>
        /// 将 object[] 转换为强类型数组，递归转换每个元素。
        /// <para>单个元素转换失败时，使用元素类型的默认值填充，不终止整个数组的转换。</para>
        /// </summary>
        /// <param name="valueArray">源 object[] 数组。</param>
        /// <param name="elementType">目标元素类型。</param>
        /// <param name="conversionValue">输出的强类型数组。</param>
        /// <returns>始终返回 <c>true</c>（元素转换失败时填充默认值）。</returns>
        internal static bool TryConvertToArray(Array valueArray, Type elementType, out object conversionValue)
        {
            conversionValue = null;
            if (valueArray == null || elementType == null) return false;

            Array instanceValue = Array.CreateInstance(elementType, valueArray.Length);

            for (int i = 0; i < valueArray.Length; i++)
            {
                if (!TryConvertParameter(valueArray.GetValue(i), elementType, out object cValue))
                {
                    // 转换失败时填充元素类型的默认值
                    cValue = elementType.IsValueType ? Activator.CreateInstance(elementType) : null;
                }
                instanceValue.SetValue(cValue, i);
            }

            conversionValue = instanceValue;
            return true;
        }

    }
}
