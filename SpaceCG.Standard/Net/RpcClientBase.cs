using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using SpaceCG.Extensions;

namespace SpaceCG.Net
{
    /// <summary>
    /// <see cref="TcpClient"/> 扩展方法。
    /// <para>提供非阻塞的连接状态检查，用于读写循环中的快速健康探测。</para>
    /// </summary>
    public static class TcpClientExtensions
    {
        /// <summary>
        /// 检查 <see cref="TcpClient"/> 是否处于已连接状态。
        /// </summary>
        /// <param name="tcpClient">要检查的 TCP 客户端。</param>
        /// <returns>如果连接正常返回 <c>true</c>；如果已断开或客户端为 <c>null</c> 则返回 <c>false</c>。</returns>
        public static bool IsConnected(this TcpClient tcpClient)
        {
            if (tcpClient == null || tcpClient.Client == null) return false;

            try
            {
                return !(tcpClient.Client.Poll(0, SelectMode.SelectRead) && tcpClient.Client.Available == 0) && tcpClient.Client.Connected;
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>
    /// RPC 客户端抽象基类，与服务端 <see cref="RpcServerBase"/> 镜像对称设计。
    /// <para>提供 TCP 连接管理、环形缓冲 CRLF 数据行拆分、请求/响应 Id 匹配、超时管理、自动重连等基础能力。</para>
    /// <para>
    /// <list type="bullet">
    ///     <item>基类以 CRLF (0x0D 0x0A) 为数据行分割标识，每行一条消息。</item>
    ///     <item>子类继承实现 <see cref="SerializeInvokeMessage"/> 和 <see cref="DeserializeResponseMessage"/> 以支持不同的消息协议。</item>
    ///     <item><see cref="InvokeActionAsync"/> 发送单向通知不等待响应（ResponseMode=-1，fire-and-forget）。</item>
    ///     <item><see cref="InvokeFuncAsync"/> 发送请求并等待响应（ResponseMode=1，请求-响应模式）。</item>
    ///     <item>意外断开时自动重连，手动调用 <see cref="Close"/> 不会重连。</item>
    /// </list>
    /// </para>
    /// <para>线程安全级别：线程安全（发送使用信号量序列化，响应匹配使用 ConcurrentDictionary）。</para>
    /// </summary>
    public abstract class RpcClientBase : IDisposable
    {
        /// <summary> 数据行分隔符字节数组，使用 CRLF（0x0D, 0x0A），与 <see cref="RpcServerBase.NewLine"/> 一致。 </summary>
        public static readonly byte[] NewLine = RpcServerBase.NewLine;

        private readonly object _lock = new object();

        private bool _isDisposed;
        private bool _manualClosed;

        private TcpClient _tcpClient;
        private CancellationTokenSource _cts;

        /// <summary> 消息 Id 生成器，<see cref="Interlocked.Increment(ref int)"/> 保证线程安全。 </summary>
        private int _messageId = 0;

        /// <summary>
        /// 发送信号量，序列化并发写入，避免多个 WriteAsync 调用在底层 Socket 上交错字节。
        /// </summary>
        private readonly SemaphoreSlim _sendSemaphore = new SemaphoreSlim(1, 1);
        /// <summary>
        /// 待响应的调用字典。
        /// <para>key: 消息 Id, value: 待处理调用信息（TaskCompletionSource）。</para>
        /// </summary>
        private readonly ConcurrentDictionary<int, PendingCall> _pendingCalls = new ConcurrentDictionary<int, PendingCall>();

        #region Public Properties
        /// <summary>
        /// 获取客户端是否已连接到服务端。
        /// </summary>
        public bool Connected
        {
            get
            {
                var client = _tcpClient;
                return _cts != null && client != null && client.IsConnected();
            }
        }

        /// <summary>
        /// 获取服务端的远程 IP 端点地址。
        /// </summary>
        public IPEndPoint RemoteEndPoint { get; private set; }
        /// <summary>
        /// 获取客户端的本地 IP 端点地址。
        /// </summary>
        public IPEndPoint LocalEndPoint { get; private set; }
        
        /// <summary>
        /// 获取或设置发送缓冲区大小，单位字节，默认 32KB。
        /// </summary>
        public int SendBufferSize { get; set; } = 1024 * 32;
        /// <summary>
        /// 获取或设置接收缓冲区大小，单位字节，默认 64KB。
        /// </summary>
        public int ReceiveBufferSize { get; set; } = 1024 * 64;
        /// <summary>
        /// 获取或设置默认的远程方法调用请求超时时间，默认 3 秒。
        /// </summary>
        public TimeSpan DefaultTimeout { get; set; } = TimeSpan.FromSeconds(3);
        #endregion

        #region Constructors
        /// <summary>
        /// 使用指定的 IP 地址和端口号初始化 <see cref="RpcClientBase"/> 类的新实例。
        /// </summary>
        /// <param name="address"></param>
        /// <param name="port"></param>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="ArgumentException"></exception>
        public RpcClientBase(IPAddress address, int port)
        {
            if (address == null)
                throw new ArgumentNullException(nameof(address));
            if (port < 1 || port > 65535)
                throw new ArgumentException("端口号必须在 1-65535 之间", nameof(port));

            RemoteEndPoint = new IPEndPoint(address, port);
        }
        /// <inheritdoc cref="RpcClientBase(IPAddress, int)"/>
        public RpcClientBase() : this(IPAddress.Any, 2000) { }
        /// <inheritdoc cref="RpcClientBase(IPAddress, int)"/>
        public RpcClientBase(string address, int port) : this(IPAddress.Parse(address), port) { }
        #endregion

        #region Connect / Close
        /// <summary>
        /// 连接到服务端。如果已在连接状态，则忽略本次调用。
        /// <para>开始连接时，内部会启动异步循环连接任务，连接成功后自动开始接收数据任务。</para>
        /// </summary>
        /// <param name="address"></param>
        /// <param name="port"></param>
        public void Connect(IPAddress address, int port)
        {
            if (_isDisposed)
                throw new ObjectDisposedException(nameof(RpcClientBase));
            if (address == null)
                throw new ArgumentNullException(nameof(address));
            if (port < 1 || port > 65535)
                throw new ArgumentException("端口号必须在 1-65535 之间", nameof(port));

            if (RemoteEndPoint.Port != port) RemoteEndPoint.Port = port;
            if (RemoteEndPoint.Address != address) RemoteEndPoint.Address = address;

            _manualClosed = false;
            _cts = new CancellationTokenSource();
            _ = ConnectLoopAsync(RemoteEndPoint.Address, RemoteEndPoint.Port, _cts.Token);
        }
        /// <inheritdoc cref="Connect(IPAddress, int)"/>
        public void Connect() => Connect(RemoteEndPoint.Address, RemoteEndPoint.Port);
        /// <inheritdoc cref="Connect(IPAddress, int)"/>
        public void Connect(string address, int port) => Connect(IPAddress.Parse(address), port);

        /// <summary>
        /// 断开与服务端的连接，取消所有待响应的调用。
        /// <para>手动断开后不会自动重连，需再次调用 <see cref="Connect()"/> 恢复。</para>
        /// </summary>
        public void Close()
        {
            _manualClosed = true;

            try { _cts?.Cancel(); }
            catch (Exception) { }

            lock (_lock)
            {
                CloseTcpClientInternal();
            }

            try { _cts?.Dispose(); }
            finally
            {
                _cts = null;
            }
        }
        /// <summary>
        /// 关闭并释放内部 TcpClient (未加锁)。
        /// </summary>
        private void CloseTcpClientInternal()
        {
            if (_tcpClient == null) return;

            try
            {
                _tcpClient?.Close();
            }
            catch { /* 忽略关闭时的异常 */ }

            try
            {
                _tcpClient?.Dispose();
            }
            catch { /* 忽略释放时的异常 */ }
            finally
            {
                _tcpClient = null;
            }
        }
        #endregion

        #region ConnectLoop & HandleSessionAsync
        /// <summary>
        /// 连接循环任务，内部会启动异步循环连接任务，连接成功后自动开始接收数据任务。
        /// </summary>
        /// <param name="address"></param>
        /// <param name="port"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        private async Task ConnectLoopAsync(IPAddress address, int port, CancellationToken cancellationToken)
        {
            TimeSpan delay = TimeSpan.FromSeconds(3.0);
            while (!cancellationToken.IsCancellationRequested)
            {
                lock (_lock)
                {
                    LocalEndPoint = null;
                    CloseTcpClientInternal();
                    if (_isDisposed || _manualClosed) return;
                }

                var newClient = new TcpClient();
                lock (_lock)
                {
                    if (_isDisposed || _manualClosed)
                    {
                        newClient?.Dispose();
                        return;
                    }
                    _tcpClient = newClient;
                }

                try
                {
                    await _tcpClient.ConnectAsync(address, port).ConfigureAwait(false);

                    lock (_lock)
                    {
                        if (_manualClosed || _isDisposed || cancellationToken.IsCancellationRequested)
                        {
                            CloseTcpClientInternal();
                            return;
                        }
                    }

                    _tcpClient.SendBufferSize = SendBufferSize;
                    _tcpClient.ReceiveBufferSize = ReceiveBufferSize;
                    LocalEndPoint = _tcpClient.Client.LocalEndPoint as IPEndPoint;
                    Trace.TraceInformation($"RPC 客户端 {LocalEndPoint} 已连接到 {RemoteEndPoint}");
                }
                catch (Exception ex) when (ex is OperationCanceledException || ex is ObjectDisposedException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"RPC 客户端连接失败: {ex.Message}，重试中 .....");
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                await HandleSessionAsync(_tcpClient, cancellationToken).ConfigureAwait(false);
            }
        }
        /// <summary>
        /// 处理与服务端的会话：使用环形缓冲（Ring Buffer）模式以 CRLF 拆分数据行，每行一条响应消息。
        /// <para>解析出的响应通过 Id 匹配分派到对应的 <see cref="_pendingCalls"/>，断开时取消所有待响应调用。</para>
        /// </summary>
        /// <param name="tcpClient">已连接的 TCP 客户端。</param>
        /// <param name="cancelToken">用于取消读取操作的令牌。</param>
        private async Task HandleSessionAsync(TcpClient tcpClient, CancellationToken cancelToken)
        {
            if (tcpClient == null) return;

            var clientStream = tcpClient.GetStream();
            var localEndPoint = tcpClient.Client.LocalEndPoint as IPEndPoint;

            var bufferSize = tcpClient.ReceiveBufferSize / 2;
            var clientBuffer = new byte[bufferSize];

            var writePosition = 0;      // 缓冲区写入位置（写指针）
            var pendingLength = 0;      // 缓冲区中未消费数据的长度
            var readPosition = 0;       // 数据消费的起始位置（读指针）

            try
            {
                while (!cancelToken.IsCancellationRequested && tcpClient.IsConnected())
                {
                    var count = await clientStream.ReadAsync(clientBuffer, writePosition, bufferSize - writePosition, cancelToken).ConfigureAwait(false);
                    if (count == 0) break;

                    Debug.WriteLine($"RPC 客户端收到来自 {RemoteEndPoint} 的数据 {count} bytes");

                    writePosition += count;
                    pendingLength += count;

                    #region 扫描缓冲区中所有完整的数据行
                    while (readPosition < pendingLength)
                    {
                        var endIndex = clientBuffer.IndexOf(NewLine, readPosition, pendingLength - readPosition);
                        if (endIndex < 0) break;

                        // 提取完整的数据行（不含 NewLine）
                        var messageBytes = new ArraySegment<byte>(clientBuffer, readPosition, endIndex - readPosition);
                        readPosition = endIndex + NewLine.Length;

                        // 解析响应消息并分派（一行一条响应）
                        ResponseMessage response = null;
                        try
                        {
                            response = DeserializeResponseMessage(messageBytes);
                        }
                        catch (Exception ex)
                        {
                            Trace.TraceWarning($"RPC 客户端 {localEndPoint} 解析响应消息反序列化异常：({ex.GetType().Name}) {ex.Message}");
                            continue;
                        }

                        // 将接收到的响应消息分派到对应的 PendingCall
                        if (response != null && response.Id > 0)
                        {
                            if (_pendingCalls.TryRemove(response.Id, out var pending))
                            {
                                pending.SetResult(response);
                            }
                            else
                            {
                                Trace.TraceWarning($"RPC 客户端 {localEndPoint} 收到未匹配的响应消息 Id:{response.Id}");
                            }
                        }
                    }
                    #endregion

                    #region 跳过尾随的空白/零值数据
                    while (readPosition < pendingLength)
                    {
                        var b = clientBuffer[readPosition];
                        if (b == 0x00 || b == 0x20 || b == 0x09 || b == 0x0A || b == 0x0D)
                        {
                            readPosition++;
                        }
                        else
                        {
                            break;
                        }
                    }
                    #endregion

                    #region 环形缓冲收尾：根据消费情况移动指针
                    if (readPosition == pendingLength)
                    {
                        writePosition = 0;
                        pendingLength = 0;
                        readPosition = 0;
                    }
                    else if (readPosition > 0 && bufferSize - pendingLength < bufferSize / 8)
                    {
                        var remaining = pendingLength - readPosition;
                        Buffer.BlockCopy(clientBuffer, readPosition, clientBuffer, 0, remaining);
                        Trace.TraceInformation($"RPC 客户端 {localEndPoint} 移动缓冲区数据 {remaining} bytes");

                        writePosition = remaining;
                        pendingLength = remaining;
                        readPosition = 0;
                    }

                    if (writePosition == bufferSize)
                    {
                        Trace.TraceWarning($"RPC 客户端 {localEndPoint} 接收缓冲区已满且无完整消息，清空 {pendingLength} bytes");
                        writePosition = 0;
                        pendingLength = 0;
                        readPosition = 0;
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
                Trace.TraceError($"RPC 客户端 {localEndPoint} 接收数据异常：({ex.GetType().Name}) {ex.Message}");
            }
            finally
            {
                // 将所有待响应的 PendingCall 以连接断开错误完成
                CancelAllPendingCalls(-102, "Connection closed");
                // 断开后的处理
                Trace.TraceInformation($"RPC 客户端 {localEndPoint} 已断开连接");
            }
        }

        /// <summary>
        /// 取消所有待响应的调用。
        /// </summary>
        /// <param name="code">错误状态码。</param>
        /// <param name="description">错误描述信息。</param>
        private void CancelAllPendingCalls(int code, string description)
        {
            var keys = _pendingCalls.Keys.ToList();            
            foreach (var key in keys)
            {
                if (_pendingCalls.TryRemove(key, out var pending))
                {
                    pending.SetError(code, description);
                }
            }
        }

        /// <summary>
        /// 等待响应消息，支持超时处理。
        /// <para>正常完成时返回服务端响应；超时时从待响应字典移除并从 <see cref="TaskCompletionSource{TResult}"/> 返回超时错误。</para>
        /// </summary>
        /// <param name="taskSource">用于等待响应的任务完成源。</param>
        /// <param name="invokeMessage">原始调用消息，用于超时时构造错误响应。</param>
        /// <param name="timeout">超时时间。</param>
        /// <returns>服务端返回的响应消息，或超时/错误响应。</returns>
        private async Task<ResponseMessage> WaitResponseMessageAsync(TaskCompletionSource<ResponseMessage> taskSource, InvokeMessage invokeMessage, TimeSpan timeout)
        {
            using (var timeoutCts = new CancellationTokenSource())
            {
                var completedTask = await Task.WhenAny(taskSource.Task, Task.Delay(timeout, timeoutCts.Token)).ConfigureAwait(false);

                if (completedTask == taskSource.Task)
                {
                    // 正常完成，取消多余的 Delay
                    timeoutCts.Cancel();
                    return await taskSource.Task.ConfigureAwait(false);
                }
                else
                {
                    // 超时：从字典移除并返回超时错误
                    if (_pendingCalls.TryRemove(invokeMessage.Id, out _))
                    {                        
                        var responseMessage = ResponseMessage.Create(invokeMessage, -97, "Response timeout");
                        taskSource.TrySetResult(responseMessage);
                    }
                    return await taskSource.Task.ConfigureAwait(false);
                }
            }
        }
        #endregion


        #region WriteAsync / InvokeActionAsync / InvokeFuncAsync
        /// <summary>
        /// 通过信号量序列化写入字节数据到网络流，防止并发写入导致字节交错。
        /// </summary>
        /// <param name="data">待写入的字节数组。</param>
        /// <param name="offset">写入起始偏移量。</param>
        /// <param name="length">写入字节长度。</param>
        /// <param name="cancellationToken">用于取消写入操作的令牌。</param>
        /// <returns>一个表示异步写入操作的任务。</returns>
        /// <exception cref="ObjectDisposedException">实例已释放时抛出。</exception>
        /// <exception cref="InvalidOperationException">客户端未连接时抛出。</exception>
        /// <exception cref="ArgumentNullException">data 为 null 时抛出。</exception>
        /// <exception cref="ArgumentOutOfRangeException">offset/length 越界时抛出。</exception>
        public async Task WriteAsync(byte[] data, int offset, int length, CancellationToken cancellationToken)
        {
            if (_isDisposed)
                throw new ObjectDisposedException(nameof(RpcClientBase));
            if (!Connected)
                throw new InvalidOperationException("Client not connected");
            if (data == null || data.Length == 0)
                throw new ArgumentNullException(nameof(data));
            if (offset < 0 || offset >= data.Length || length <= 0 || offset + length > data.Length)
                throw new ArgumentOutOfRangeException(nameof(length));

            try
            {
                await _sendSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

                var clientStream = _tcpClient.GetStream();
                await clientStream.WriteAsync(data, offset, length, cancellationToken).ConfigureAwait(false);
                await clientStream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is OperationCanceledException || ex is ObjectDisposedException)
            {
            }
            catch (Exception ex)
            {
                Trace.TraceWarning($"RPC 客户端 {LocalEndPoint} 发送数据异常：({ex.GetType().Name}) {ex.Message}");
            }
            finally
            {
                _sendSemaphore.Release();
            }
        }
        /// <inheritdoc cref="WriteAsync(byte[], int, int, CancellationToken)"/>
        public async Task WriteAsync(byte[] data) => await WriteAsync(data, 0, data.Length, _cts.Token);

        /// <summary>
        /// 发起单向远程调用，不等待服务端响应（ResponseMode = -1）。
        /// <para>对应 C# 的 <c>Action</c> 委托语义——无返回值，发射后即忘（fire-and-forget）。</para>
        /// <para>适用于单向调用、无返回值调用、状态同步、日志上报、事件通知等无需返回结果的场景。</para>
        /// </summary>
        /// <param name="objectName">目标对象名称，需符合 <see cref="RpcServerBase.IdentifierPattern"/> 命名规则。</param>
        /// <param name="methodName">目标方法名称，需符合 <see cref="RpcServerBase.IdentifierPattern"/> 命名规则。</param>
        /// <param name="parameters">方法参数对象数组，可为 null 表示无参调用。</param>
        /// <returns>发送是否成功（仅表示写入 TCP 流成功，不表示服务端处理成功）。</returns>
        public async Task<bool> InvokeActionAsync(string objectName, string methodName, object[] parameters = null)
        {
            if (_isDisposed)
                throw new ObjectDisposedException(nameof(RpcClientBase));

            if (!Connected)
            {
                Trace.TraceWarning($"没有连接到服务端 {RemoteEndPoint}，无法发送消息");
                return false;
            }

            byte[] messageBytes = null;
            var invokeMessage = InvokeMessage.Create(objectName, methodName, parameters, Interlocked.Increment(ref _messageId), -1);

            try
            {
                messageBytes = SerializeInvokeMessage(invokeMessage);
            }
            catch (Exception ex)
            {
                Trace.TraceWarning($"RPC 客户端 {LocalEndPoint} 序列化消息异常：({ex.GetType().Name}) {ex.Message}");
                return false;
            }

            if (messageBytes == null || messageBytes.Length == 0) return false;

            try
            {
                await WriteAsync(messageBytes).ConfigureAwait(false);
                return true;
            }
            catch (Exception ex)
            {
                Trace.TraceWarning($"RPC 客户端 {LocalEndPoint} InvokeActionAsync 发送消息异常：({ex.GetType().Name}) {ex.Message}");
            }

            return false;
        }

        /// <summary>
        /// 发起远程请求调用并等待服务端返回结果（ResponseMode = 1，必须响应）。
        /// <para>对应 C# 的 <c>Func&lt;T&gt;</c> 委托语义——有返回值，必须等待结果。</para>
        /// <para>通过 <see cref="ResponseMessage.Code"/> 判断调用是否成功（小于 0 表示失败，大于等于 0 表示成功，等于 1 表示成功且有 <see cref="ResponseMessage.ReturnValue"/> 值）。</para>
        /// <para>通过 <see cref="ResponseMessage.Description"/> 获取状态描述信息。</para>
        /// <para>通过 <see cref="ResponseMessage.ReturnValue"/> 获取原始返回值，可自行转换为强类型。</para>
        /// </summary>
        /// <param name="objectName">目标对象名称，需符合 <see cref="RpcServerBase.IdentifierPattern"/> 命名规则。</param>
        /// <param name="methodName">目标方法名称，需符合 <see cref="RpcServerBase.IdentifierPattern"/> 命名规则。</param>
        /// <param name="parameters">方法参数对象数组，可为 null 表示无参调用。</param>
        /// <param name="timeout">超时时间，null 表示使用 <see cref="DefaultTimeout"/>。</param>
        /// <returns>包含状态码、描述信息和原始返回值的响应消息。</returns>
        /// <exception cref="ObjectDisposedException">实例已释放时抛出。</exception>
        public async Task<ResponseMessage> InvokeFuncAsync(string objectName, string methodName, object[] parameters = null, TimeSpan? timeout = null)
        {
            if (_isDisposed)
                throw new ObjectDisposedException(nameof(RpcClientBase));

            var invokeMessage = InvokeMessage.Create(objectName, methodName, parameters, Interlocked.Increment(ref _messageId), 1);

            if (!Connected)
                return ResponseMessage.Create(invokeMessage, -100, "Client not connected");

            byte[] messageBytes = null;
            try
            {
                messageBytes = SerializeInvokeMessage(invokeMessage);
            }
            catch (Exception ex)
            {
                Trace.TraceWarning($"RPC 客户端 {LocalEndPoint} 消息序列化失败：({ex.GetType().Name}) {ex.Message}");
                return ResponseMessage.Create(invokeMessage, -105, "Message serialization failed");
            }

            var tcs = new TaskCompletionSource<ResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
            var pending = new PendingCall 
            { 
                TaskSource = tcs,
                InvokeMessage = invokeMessage,
            };
            if (!_pendingCalls.TryAdd(invokeMessage.Id, pending))
            {
                return ResponseMessage.Create(invokeMessage, -96, $"Message Id {invokeMessage.Id} already exists in pending calls");
            }

            try
            {
                if (messageBytes == null || messageBytes.Length == 0)
                {
                    if (_pendingCalls.TryRemove(invokeMessage.Id, out var removed))
                    {
                        removed.SetError(-106, "Message serialize result is empty");
                    }
                    return await tcs.Task.ConfigureAwait(false);
                }

                // 序列化后再检查连接状态
                if (!Connected)
                {
                    if (_pendingCalls.TryRemove(invokeMessage.Id, out var removed))
                    {
                        removed.SetError(-101, "Client connection closed");
                    }
                    return await tcs.Task.ConfigureAwait(false);
                }

                await WriteAsync(messageBytes).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                if (_pendingCalls.TryRemove(invokeMessage.Id, out var removed))
                {
                    removed.SetError(-107, $"Client write failed: {ex.Message}");
                }
                return await tcs.Task.ConfigureAwait(false);
            }

            // 等待响应（带超时）
            return await WaitResponseMessageAsync(tcs, invokeMessage, timeout ?? DefaultTimeout).ConfigureAwait(false);
        }
        #endregion


        #region Abstract Methods (Subclass Protocol Layer)
        /// <summary>
        /// 将调用消息序列化为待发送的字节数组。
        /// <para>子类实现不同协议格式（XML / JSON 等），与 <see cref="RpcServerBase.SerializeResponseMessage"/> 镜像对称。</para>
        /// </summary>
        /// <param name="requestMessage">待序列化的调用消息。</param>
        /// <returns>可直接写入 NetworkStream 的字节数组（应以 CRLF 结尾以标识行结束）。</returns>
        protected abstract byte[] SerializeInvokeMessage(InvokeMessage requestMessage);

        /// <summary>
        /// 将接收到的数据行解析为一条响应消息。
        /// <para>一条数据行对应一条响应，子类实现不同协议的响应解析。</para>
        /// </summary>
        /// <param name="responseMessage">一个完整的数据行字节数据（不含尾部 CRLF）。</param>
        /// <returns>解析出的响应消息；无法解析则返回 <c>null</c>。</returns>
        protected abstract ResponseMessage DeserializeResponseMessage(ArraySegment<byte> responseMessage);
        #endregion


        #region IDisposable
        /// <summary>
        /// 释放 RPC 客户端占用的所有资源。
        /// <para>包括断开连接、取消所有待响应调用、释放信号量和取消令牌。</para>
        /// </summary>
        public void Dispose()
        {
            if (_isDisposed) return;

            _isDisposed = true;
            _manualClosed = true;

            Close();

            try
            {
                _cts?.Dispose();
            }
            catch (Exception) { }

            try
            {
                _sendSemaphore?.Dispose();
            }
            catch (Exception) { }

            _cts = null;
        }

        #endregion


        #region Internal Types
        /// <summary>
        /// 待响应的调用信息，封装 <see cref="TaskCompletionSource{TResult}"/> 用于异步等待。
        /// </summary>
        private sealed class PendingCall
        {
            /// <summary>
            /// 任务的调用消息。
            /// </summary>
            public InvokeMessage InvokeMessage { get; set; }

            /// <summary>
            /// 任务完成源，用于异步等待和通知调用方。
            /// </summary>
            public TaskCompletionSource<ResponseMessage> TaskSource { get; set; }

            /// <summary>
            /// 根据响应消息设置成功结果。
            /// </summary>
            /// <param name="response">服务端返回的响应消息。</param>
            public void SetResult(ResponseMessage response)
            {
                TaskSource?.TrySetResult(response);
            }

            /// <summary>
            /// 设置错误结果。
            /// </summary>
            /// <param name="code">错误状态码（负数）。</param>
            /// <param name="description">错误描述信息。</param>
            public void SetError(int code, string description)
            {
                if (TaskSource == null) return;
                if (InvokeMessage == null) return;

                var responseMessage = ResponseMessage.Create(InvokeMessage, code, description);
                TaskSource.TrySetResult(responseMessage);
            }
        }
        #endregion
    }
}
