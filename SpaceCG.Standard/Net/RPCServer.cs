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
    /// 远程过程调用(Remote Procedure Call) 或 反射程序控制(Reflection Program Control) 服务端。
    /// <para>
    /// <list type="bullet">
    /// <item>默认实现 XML 消息协议</item>
    /// <item>可继承，实现不同的协议内容</item>
    /// <item>可拦截、取消调用执行</item>
    /// </list>
    /// </para>
    /// </summary>
    public class RPCServer : IDisposable
    {
        /// <summary> 对象名称或方法名称的命名规则正则表达式  </summary>
        public static readonly Regex NamedRegex = new Regex(@"^[a-zA-Z_][a-zA-Z0-9_]*$", RegexOptions.Compiled);

        private bool _isDisposed;
        private TcpListener _tcpLstener;
        private CancellationTokenSource _cts;
        private SynchronizationContext _syncContext;

        /// <summary> 获取服务是否正在运行 </summary>
        public bool IsRunning => _tcpLstener != null && _cts != null;
        /// <summary>  监听的本地 IP 地址和端口号  </summary>
        public IPEndPoint LocalEndPoint { get; private set; }

        /// <summary>  客户端连接事件  </summary>
        public event EventHandler<IPEndPoint> ClientConnected;
        /// <summary>  客户端断开连接事件  </summary>
        public event EventHandler<IPEndPoint> ClientDisconnected;
        /// <summary>
        /// 客户端调用消息接收事件
        /// <para>可拦截、取消客户端调用消息执行，取消后不会执行客户端的调用消息</para>
        /// </summary>
        public event EventHandler<InvokeMessageEventArgs> ClientMessageInvoking;

        /// <summary>  已连接客户端集合  </summary>
        public IReadOnlyList<TcpClient> Clients => _clients;
        private readonly List<TcpClient> _clients = new List<TcpClient>();

        /// <summary> 获取或设置发送缓冲区大小，单位字节，默认 32KB。  </summary>
        public int SendBufferSize { get; set; } = 1024 * 32;
        /// <summary> 获取或设置接收缓冲区大小，单位字节，默认 64KB。  </summary>
        public int ReceiveBufferSize { get; set; } = 1024 * 64;

        /// <summary> 新行字节数组，默认使用 CRLF 作为新行标识符。 </summary>
        public static readonly byte[] NewLine = new byte[] { 0x0D, 0x0A };
        /// <summary> XML 结束标识字节数组，默认使用 "/>" 作为 XML 结束标识符。 </summary>
        public static readonly byte[] XMLEndFlags = new byte[] { 0x2F, 0x3E };

        /// <summary>
        /// 可控制、访问的对象的方法过滤集合，指定对象的方法不在访问范围内；
        /// <para>字符格式为：{ObjectName}.{MethodName}, ObjectName 支持通配符 '*'</para>
        /// <para>例如："*.Dispose" 禁止反射访问所有对象的 Dispose 方法，默认已添加 *.Dispose, *.Close</para>
        /// </summary>
        public readonly List<string> MethodFilters = new List<string>(16) { "*.Dispose", "*.Close" };

        /// <summary> 客户端调用消息队列，服务端可从该队列中获取客户端的调用消息，并执行调用。 </summary>
        private readonly ConcurrentQueue<InvokeMessage> InvokeMessages = new ConcurrentQueue<InvokeMessage>();
        /// <summary> 注册的对象实例集合 </summary>
        private readonly ConcurrentDictionary<string, object> RegisterObjects = new ConcurrentDictionary<string, object>();
        /// <summary> 历史调用过的唯一方法，无歧义的方法  </summary>
        private readonly ConcurrentDictionary<string, MethodInfo> CacheMethodInfos = new ConcurrentDictionary<string, MethodInfo>();
        
        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="ipAddress"></param>
        /// <param name="localPort"></param>
        /// <exception cref="ArgumentException"></exception>
        public RPCServer(IPAddress ipAddress, int localPort)
        {
            if (localPort < 1 || localPort > 65535)
                throw new ArgumentException("端口号必须在 1-65535 之间");

            LocalEndPoint = new IPEndPoint(ipAddress, localPort);
            _syncContext = SynchronizationContext.Current ?? new SynchronizationContext();
        }
        /// <summary>
        /// 构造函数，监听所有网卡的指定端口
        /// </summary>
        /// <param name="localPort"></param>
        public RPCServer(int localPort) : this(IPAddress.Any, localPort) { }

        /// <summary>
        /// 注册可远程访问/操作的对象
        /// </summary>
        /// <param name="objectName">对象名称</param>
        /// <param name="objectInstance">对象实例</param>
        public void RegisterObject(string objectName, object objectInstance)
        {
            if (_isDisposed) throw new ObjectDisposedException(nameof(RPCServer));

            if (string.IsNullOrWhiteSpace(objectName) || !NamedRegex.IsMatch(objectName))
                throw new ArgumentNullException(nameof(objectName), "对象名称不能为空或命名格式不正确");

            if (objectInstance == null || objectInstance == this)
                throw new ArgumentNullException(nameof(objectInstance), "对象实例不能为空，也不能注册自身实例");

            var objectType = objectInstance.GetType();
            if (objectType.IsValueType || objectType == typeof(RPCServer) /*|| objectType == typeof(RPCClient)*/)
                throw new ArgumentException($"不能注册的对象实例类型 {objectType}");

            if (RegisterObjects.ContainsKey(objectName))
                throw new ArgumentException($"已存在的对象名称 {objectName}");

            if (!RegisterObjects.TryAdd(objectName, objectInstance))
                throw new ArgumentException($"注册对象 {objectName} 实例  {objectInstance} 失败");
        }

        /// <summary>
        /// 启动服务端
        /// </summary>
        public void Start()
        {
            if (_isDisposed)
                throw new ObjectDisposedException(nameof(RPCServer));

            if (IsRunning || _cts != null) return;

            _cts = new CancellationTokenSource();
            _tcpLstener = new TcpListener(LocalEndPoint);

            try
            {
                _tcpLstener.Start();
                Trace.WriteLine($"RPC 服务已启动，监听地址：{_tcpLstener.LocalEndpoint}");
            }
            catch (Exception ex)
            {
                Trace.TraceError($"启动 RPC 服务失败：({ex.GetType().Name}) {ex.Message}");
                Stop();
                return;
            }

            var acceptToken = _cts.Token;
            _ = Task.Run(() => TcpClientAcceptAsync(acceptToken), acceptToken);

            var invokeToken = _cts.Token;
            _ = Task.Run(() => InvokeMessageHandler(invokeToken), invokeToken);
        }

        /// <summary>
        /// 停止服务端
        /// </summary>
        /// <exception cref="ObjectDisposedException"></exception>
        public void Stop()
        {
            if (!IsRunning || _cts == null) return;

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
                _tcpLstener?.Stop();
                _tcpLstener?.Server.Dispose();
            }
            catch
            {
                // 忽略
            }

            _cts.Dispose();

            _cts = null;
            _tcpLstener = null;
            Trace.TraceInformation($"RPC 服务已停止");
        }
        /// <summary>
        /// 接受 TCP 客户端连接
        /// </summary>
        /// <param name="cancelToken"></param>
        /// <returns></returns>
        private async Task TcpClientAcceptAsync(CancellationToken cancelToken)
        {
            try
            {
                while (!cancelToken.IsCancellationRequested)
                {
                    var client = await _tcpLstener.AcceptTcpClientAsync().ConfigureAwait(false);
                    _ = TcpClientReadAsync(client, cancelToken);
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
        /// 处理 Tcp 客户端连接
        /// </summary>
        private async Task TcpClientReadAsync(TcpClient client, CancellationToken cancelToken)
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
            // 如果读取的数据全部分析完或刚好分析完一个完整的数据帧后，可以将 offset 设置 0
            var bufferSize = client.ReceiveBufferSize / 2;
            var clientBuffer = new byte[bufferSize];    

            var offset = 0;     // buffer 中已写入数据的末尾位置（下一次 ReadAsync 的写入起点，写指针）
            var length = 0;     // buffer 中有效数据的长度 (未分析读取的数据量)
            var position = 0;   // 数据分析的起始位置 (读指针)

            try
            {
                var stream = client.GetStream();
                while (!cancelToken.IsCancellationRequested && client.IsConnected())
                {
                    var count = await stream.ReadAsync(clientBuffer, offset, bufferSize - offset, cancelToken);
                    if (count == 0) break;  // 客户端已断开了连接
                    Debug.WriteLine($"收到来自 {remoteEndPoint} 的数据 {count} bytes ");

                    offset += count;
                    length += count;

                    #region 扫描缓冲区中所有完整的消息帧
                    while (position < length)
                    {
                        var index = clientBuffer.IndexOf(NewLine, position, length - position);
                        if (index < 0) break;

                        // 提取完整的消息帧（不含 NewLine 本身）
                        var messageFrame = new ArraySegment<byte>(clientBuffer, position, index - position);

                        // 解析客户端的消息
                        if (TryParseMessageFrame(remoteEndPoint, messageFrame, out var invokeMessage) && invokeMessage != null)
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
                            InvokeMessages.Enqueue(invokeMessage);
                        }
                        else
                        {

                        }

                        // 移动读指针，跳过已消费的消息和换行符
                        position = index + NewLine.Length;
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
        /// 客户端消息调用处理
        /// </summary>
        /// <param name="cancelToken"></param>
        /// <returns></returns>
        private async Task InvokeMessageHandler(CancellationToken cancelToken)
        {
            while (!cancelToken.IsCancellationRequested)
            {
                if (InvokeMessages.IsEmpty)
                {
                    await Task.Delay(1, cancelToken).ConfigureAwait(false);
                    continue;
                }

                if (!InvokeMessages.TryDequeue(out var invokeMessage))
                {
                    await Task.Delay(1, cancelToken).ConfigureAwait(false);
                    continue;
                }

                TryCallMethod(invokeMessage, out var invokeResult);

                // 执行响应客户端调用结果
                if (invokeResult == null) continue;
                if (invokeMessage.TcpClient == null) continue;
                if (!invokeMessage.TcpClient.IsConnected()) continue;

                try
                {
                    var responseBytes = ResponseMessageFrame(invokeResult);
                    if (responseBytes?.Length > 0 && invokeMessage.TcpClient.IsConnected())
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
                    invokeMessage = null;
                }
            }
        }


        /// <summary>
        /// 解析客户端发送的完整消息帧，以换行回车（'\r\n' 或十六进制 0D0A）作为结束符的消息帧。
        /// <para>子类可继承重写该函数，从而实现不同的消息协议</para>
        /// </summary>
        /// <param name="remoteEndPoint">客户端远程端点地址</param>
        /// <param name="messageFrame">客户端的消息帧（包含一个完整行的消息字节数据）</param>
        /// <param name="invokeMessage">解析后的完整调用消息对象</param>
        /// <returns>解析成功返回 true；否则返回 false</returns>
        protected virtual bool TryParseMessageFrame(IPEndPoint remoteEndPoint, ArraySegment<byte> messageFrame, out InvokeMessage invokeMessage)
        {
            invokeMessage = null;
            var message = string.Empty;

            try
            {
                message = Encoding.UTF8.GetString(messageFrame.Array, messageFrame.Offset, messageFrame.Count);
                Debug.WriteLine($">>>{message}");
            }
            catch (Exception ex)
            {
                Trace.TraceWarning($"客户端 {remoteEndPoint} 数据帧异常：{ex.Message}");
                return false;
            }

            try
            {
                XElement element = XElement.Parse(message);
                invokeMessage = element.ToInvokeMessage();
                return true;
            }
            catch (Exception ex)
            {
                Trace.TraceWarning($"解析客户端 {remoteEndPoint} 数据帧 \"{message}\" 异常：{ex.Message}");
            }

            return false;
        }

        /// <summary>
        /// 响应客户端的调用消息格式。
        /// <para>子类可继承重写该函数，从而实现不同的消息协议</para>
        /// </summary>
        /// <param name="invokeResult"></param>
        /// <returns></returns>
        protected virtual byte[] ResponseMessageFrame(InvokeResult invokeResult)
        {
            if (invokeResult == null) return Array.Empty<byte>();
            return invokeResult.ToXElementBytes();
        }

        /// <summary>
        /// 尝试调用注册对象的公共方法。
        /// </summary>
        /// <param name="invokeMessage"></param>
        /// <param name="invokeResult"></param>
        /// <returns></returns>
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
                //if (!SpaceCG.Extensions.TypeExtensions.ConvertFrom(invokeMessage.Parameters[i], destinationType, out object convertValue))
                {
                    //return InvokeResult.Create(invokeMessage, -4, $"Object method '{invokeMessage.ObjectName}.{invokeMessage.MethodName}' parameter {i} convert from {invokeMessage.Parameters[i]?.GetType()} to {destinationType} failed");
                }
                //convertParameters[i + offset] = convertValue;
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
        /// 获取注册对象的可调用方法的集合。
        /// </summary>
        /// <param name="instanceType"></param>
        /// <param name="invokeMessage"></param>
        /// <returns></returns>
        private IEnumerable<MethodInfo> GetInvokeMethods(Type instanceType, InvokeMessage invokeMessage)
        {
            if (instanceType == null || invokeMessage == null) return Array.Empty<MethodInfo>();

            var paramsLength = invokeMessage.Parameters?.Length ?? 0;
            var methodCacheKey = $"{invokeMessage.ObjectName}.{invokeMessage.MethodName}.{paramsLength}";
            if (CacheMethodInfos.TryGetValue(methodCacheKey, out var methodInfo)) return new[] { methodInfo };

            // Get Instance Methods
            var methodInfos = from method in instanceType.GetMethods(BindingFlags.Public)   // 这里需要考虑继承的公共方法
                              where method.Name == invokeMessage.MethodName && method.GetParameters().Length == paramsLength
                              select method;

            var extensionType = typeof(ExtensionAttribute);
            if (!methodInfos.Any())
            {
                //Get Extension Methods                
                methodInfos = from assembly in AppDomain.CurrentDomain.GetAssemblies()
                              where !assembly.GlobalAssemblyCache
                              from type in assembly.GetExportedTypes()
                              where type.IsSealed && !type.IsGenericType && !type.IsNested && type.IsAbstract
                              from method in type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.DeclaredOnly)
                              where method.Name == invokeMessage.MethodName && method.IsDefined(extensionType, false)
                              let methodParams = method.GetParameters()
                              where methodParams?.Length == (paramsLength + 1) && methodParams[0].ParameterType == instanceType
                              select method;
                Debug.WriteLine($"Get Extension Methods Count: {methodInfos?.Count()}");
            }

            var methodCount = methodInfos.Count();
            if (methodCount == 0) return Array.Empty<MethodInfo>();
            if (methodCount == 1)   // 只有一个方法，不存在歧义，记录，下次不用重复查询
            {
                CacheMethodInfos.TryAdd(methodCacheKey, methodInfos.First());
                return methodInfos;
            }
            if (paramsLength == 0) return null;

            // 有多个方法(参数数量一致)，存在歧义，对比参数类型
            var stringType = typeof(string);
            var methodList = methodInfos.ToList();
            var inputParamTypes = invokeMessage.Parameters.Select(p => p?.GetType() ?? stringType).ToArray(); // ["0","1","12"],"1.5","1","-1",[["0","2"],["1","3"]]
            for (int i = 0; i < methodList.Count; i++)
            {
                var offset = methodList[i].IsDefined(extensionType, false) ? 1 : 0;
                var methodParamTypes = methodList[i].GetParameters().Select(p => p.ParameterType).ToArray();

                for (int k = 0; i < paramsLength; k++)
                {
                    var inputParamType = inputParamTypes[i];
                    var methodParamType = methodParamTypes[k + offset];

                    if ((inputParamType == methodParamType) || (inputParamType == stringType && methodParamType.IsValueType) ||
                        (inputParamType.IsArray && methodParamType.IsArray) || (inputParamType.IsArray && methodParamType.IsGenericType))
                    {
                        continue;
                    }

                    methodList.RemoveAt(i--);
                    break;
                }
            }

            return methodList;
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
