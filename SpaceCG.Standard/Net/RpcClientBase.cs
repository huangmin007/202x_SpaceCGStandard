using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
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
    /// <para>提供 TCP 连接管理、数据行收发、请求/响应匹配、超时管理等基础能力。</para>
    /// <para>
    /// <list type="bullet">
    /// <item>基类以 CRLF (0x0D 0x0A) 为数据行分割标识，每行一条消息。</item>
    /// <item>子类继承实现 <see cref="SerializeInvokeMessage"/> 和 <see cref="DeserializeResponseMessage"/> 以支持不同的消息协议。</item>
    /// <item><see cref="InvokeFuncAsync"/> 发送请求并等待响应（ResponseMode=1，必须响应）。</item>
    /// <item><see cref="InvokeActionAsync"/> 发送单向通知不等待响应（ResponseMode=-1，服务端不响应）。</item>
    /// <item>意外断开时自动重连，手动调用 <see cref="Close"/> 不重连。</item>
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
        private int _messageId;

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
                return client != null && client.IsConnected();
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
        /// 获取或设置默认的请求超时时间，默认 3 秒。
        /// </summary>
        public TimeSpan DefaultTimeout { get; set; } = TimeSpan.FromSeconds(3);
        #endregion

        #region Constructors
        /// <summary>
        /// 初始化 <see cref="RpcClientBase"/> 类的新实例。
        /// </summary>
        public RpcClientBase() 
        { 
            RemoteEndPoint = new IPEndPoint(IPAddress.Loopback, 2000); 
        }
        /// <summary>
        /// 使用指定的 IP 地址和端口号初始化 <see cref="RpcClientBase"/> 类的新实例。
        /// </summary>
        /// <param name="address">服务端的 IP 地址。</param>
        /// <param name="port">服务端的端口号，范围 1-65535。</param>
        /// <exception cref="ArgumentNullException"><paramref name="address"/> 为 null 时抛出。</exception>
        /// <exception cref="ArgumentException">端口号不在 1-65535 范围内时抛出。</exception>
        public RpcClientBase(string address, int port)
        {
            if (string.IsNullOrWhiteSpace(address))
                throw new ArgumentNullException(nameof(address));
            if (port < 1 || port > 65535)
                throw new ArgumentException("端口号必须在 1-65535 之间", nameof(port));

            RemoteEndPoint = new IPEndPoint(IPAddress.Parse(address), port);
        }
        #endregion

        #region Public API: Connect / Close
        /// <summary>
        /// 异步连接到服务端，并启动后台接收循环。
        /// </summary>
        public void Connect() => Connect(RemoteEndPoint.Address.ToString(), RemoteEndPoint.Port);

        /// <summary>
        /// 异步连接到服务端，并启动后台接收循环。
        /// <para>如果已在连接状态，则忽略本次调用。</para>
        /// </summary>
        /// <exception cref="ObjectDisposedException">实例已释放时抛出。</exception>
        public void Connect(string address, int port)
        {
            if (_isDisposed)
                throw new ObjectDisposedException(nameof(RpcClientBase));

            if (string.IsNullOrWhiteSpace(address))
                throw new ArgumentNullException(nameof(address));
            if (port < 1 || port > 65535)
                throw new ArgumentException("端口号必须在 1-65535 之间", nameof(port));

            if (Connected || _cts != null) return;

            RemoteEndPoint.Port = port;
            RemoteEndPoint.Address = IPAddress.Parse(address);

            _manualClosed = false;
            _cts = new CancellationTokenSource();

            _ = ConnectLoopAsync(address, port, _cts.Token);
        }

        /// <summary>
        /// 断开与服务端的连接，取消所有待响应的调用。
        /// <para>手动断开后不会自动重连，需再次调用 <see cref="Connect()"/> 恢复。</para>
        /// </summary>
        public void Close()
        {
            _manualClosed = true;

            if (_cts != null)
            {
                try
                {
                    _cts?.Cancel();
                }
                catch { }

                try
                {
                    _cts?.Dispose();
                }
                finally
                {
                    _cts = null;
                }
            }

            lock (_lock)
            {
                CloseTcpClientInternal();
            }
        }
        #endregion

        #region Connect & Receive
        /// <summary>
        /// 内部连接实现。
        /// </summary>
        private async Task ConnectLoopAsync(string address, int port, CancellationToken cancellationToken)
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
                catch (Exception ex)
                {
                    Debug.WriteLine($"RPC 客户端连接失败: {ex.Message}，重试中 .....");
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                await ReceiveLoopAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        /// <summary>
        /// 后台接收循环，使用环形缓冲（Ring Buffer）模式以 CRLF 拆分数据行。
        /// <para>与服务端 <see cref="RpcServerBase"/> 的 ReadTcpClientAsync 使用相同的环形缓冲策略。</para>
        /// </summary>
        /// <param name="cancelToken">用于取消读取操作的令牌。</param>
        private async Task ReceiveLoopAsync(CancellationToken cancelToken)
        {
            var tcpClient = _tcpClient;
            var clientStream = _tcpClient?.GetStream();

            if (tcpClient == null || clientStream == null) return;
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
                        var dataLine = new ArraySegment<byte>(clientBuffer, readPosition, endIndex - readPosition);
                        readPosition = endIndex + NewLine.Length;

                        // 解析响应消息并分派（一行一条响应）
                        ResponseMessage response = null;
                        try
                        {
                            response = DeserializeResponseMessage(dataLine);
                        }
                        catch (Exception ex)
                        {
                            Trace.TraceWarning($"RPC 客户端 {localEndPoint} 解析响应消息异常：({ex.GetType().Name}) {ex.Message}");
                            continue;
                        }

                        // 将接收到的响应消息分派到对应的 PendingCall
                        if (response != null && response.Id >= 0)
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
                CancelAllPendingCalls(-99, "Connection closed");
                // 断开后的处理
                Trace.TraceInformation($"RPC 客户端 {localEndPoint} 已断开连接");
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
        /// <summary>
        /// 取消所有待响应的调用。
        /// </summary>
        /// <param name="code">错误状态码。</param>
        /// <param name="description">错误描述信息。</param>
        private void CancelAllPendingCalls(int code, string description)
        {
            // 收集所有待取消的 key，避免在枚举时修改字典
            var keys = new List<int>();
            foreach (var kvp in _pendingCalls)
            {
                keys.Add(kvp.Key);
            }

            foreach (var key in keys)
            {
                if (_pendingCalls.TryRemove(key, out var pending))
                {
                    pending.SetError(code, description);
                }
            }
        }

        /// <summary>
        /// 等待响应，支持超时。
        /// </summary>
        private async Task<ResponseMessage> WaitForResponseAsync(TaskCompletionSource<ResponseMessage> tcs, InvokeMessage invokeMessage, TimeSpan timeout)
        {
            using (var timeoutCts = new CancellationTokenSource())
            {
                var completedTask = await Task.WhenAny(tcs.Task, Task.Delay(timeout, timeoutCts.Token)).ConfigureAwait(false);

                if (completedTask == tcs.Task)
                {
                    // 正常完成，取消多余的 Delay
                    timeoutCts.Cancel();
                    return await tcs.Task.ConfigureAwait(false);
                }
                else
                {
                    // 超时：从字典移除并返回超时错误
                    if (_pendingCalls.TryRemove(invokeMessage.Id, out _))
                    {                        
                        var responseMessage = ResponseMessage.Create(invokeMessage, -97, "Response timeout");
                        tcs.TrySetResult(responseMessage);
                    }
                    return await tcs.Task.ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// 线程安全地写入字节数据到网络流。
        /// <para>使用 <see cref="_sendSemaphore"/> 序列化并发写入，防止底层 Socket 字节交错。</para>
        /// </summary>
        /// <param name="data">待写入的字节数据。</param>
        protected async Task WriteAsync(byte[] data)
        {
            if (data == null || data.Length == 0) return;

            await _sendSemaphore.WaitAsync().ConfigureAwait(false);

            try
            {
                if (!Connected)
                    throw new InvalidOperationException("Client not connected");

                var clientStream = _tcpClient.GetStream();
                await clientStream.WriteAsync(data, 0, data.Length).ConfigureAwait(false);
                await clientStream.FlushAsync().ConfigureAwait(false);
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
        #endregion


        #region Public API: InvokeAction / InvokeFunc
        /// <summary>
        /// 发起单向远程调用，不等待服务端响应（ResponseMode = -1）。
        /// <para>对应 C# 的 <c>Action</c> 委托语义——无返回值，发射后即忘（fire-and-forget）。</para>
        /// <para>适用于状态同步、日志上报、事件通知等无需返回结果的场景。</para>
        /// </summary>
        /// <param name="objectName">目标对象名称，需符合 <see cref="RpcServerBase.IdentifierPattern"/> 命名规则。</param>
        /// <param name="methodName">目标方法名称，需符合 <see cref="RpcServerBase.IdentifierPattern"/> 命名规则。</param>
        /// <param name="parameters">方法参数对象数组，可为 null 表示无参调用。</param>
        /// <returns>发送是否成功（仅表示写入 TCP 流成功，不表示服务端处理成功）。</returns>
        public async Task InvokeActionAsync(string objectName, string methodName, object[] parameters = null)
        {
            if (_isDisposed)
                throw new ObjectDisposedException(nameof(RpcClientBase));

            if (!Connected)
            {
                Trace.TraceWarning($"没有连接到服务端 {RemoteEndPoint}，无法发送消息");
                return;
            }

            var invokeMessage = InvokeMessage.Create(objectName, methodName, parameters);
            invokeMessage.Id = Interlocked.Increment(ref _messageId);
            invokeMessage.ResponseMode = -1;

            byte[] messageBytes = null;
            try
            {
                messageBytes = SerializeInvokeMessage(invokeMessage);
            }
            catch (Exception ex)
            {
                Trace.TraceWarning($"RPC 客户端 {LocalEndPoint} 序列化消息失败：({ex.GetType().Name}) {ex.Message}");
                return;
            }

            if (messageBytes == null || messageBytes.Length == 0) return;

            try
            {
                await WriteAsync(messageBytes).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Trace.TraceWarning($"RPC 客户端 {LocalEndPoint} InvokeActionAsync 发送消息失败：({ex.GetType().Name}) {ex.Message}");
            }
        }

        /// <summary>
        /// 发起远程请求调用并等待服务端返回结果（ResponseMode = 1，必须响应）。
        /// <para>对应 C# 的 <c>Func&lt;T&gt;</c> 委托语义——有返回值，必须等待结果。</para>
        /// <para>通过 <see cref="ResponseMessage.Code"/> 判断调用是否成功（负数=失败）。</para>
        /// <para>通过 <see cref="ResponseMessage.Description"/> 获取错误描述信息。</para>
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

            var invokeMessage = InvokeMessage.Create(objectName, methodName, parameters);
            invokeMessage.Id = Interlocked.Increment(ref _messageId);
            invokeMessage.ResponseMode = 1;

            if (!Connected)
                return ResponseMessage.Create(invokeMessage, -100, "Client not connected");

            byte[] messageBytes = null;
            try
            {
                messageBytes = SerializeInvokeMessage(invokeMessage);
            }
            catch (Exception ex)
            {
                Trace.TraceWarning($"RPC 客户端 {LocalEndPoint} 序列化消息失败：({ex.GetType().Name}) {ex.Message}");
                return ResponseMessage.Create(invokeMessage, -89, "Message serialization failed");
            }

            var tcs = new TaskCompletionSource<ResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
            var pending = new PendingCall { Tcs = tcs };
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
                        removed.SetError(-88, "Message serialize result is empty");
                    }
                    return await tcs.Task.ConfigureAwait(false);
                }

                // 序列化后再检查连接状态
                if (!Connected)
                {
                    if (_pendingCalls.TryRemove(invokeMessage.Id, out var removed))
                    {
                        removed.SetError(-99, "Client connection closed");
                    }
                    return await tcs.Task.ConfigureAwait(false);
                }

                await WriteAsync(messageBytes).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                if (_pendingCalls.TryRemove(invokeMessage.Id, out var removed))
                {
                    removed.SetError(-98, $"Client write failed: {ex.Message}");
                }
                return await tcs.Task.ConfigureAwait(false);
            }

            // 等待响应（带超时）
            return await WaitForResponseAsync(tcs, invokeMessage, timeout ?? DefaultTimeout).ConfigureAwait(false);
        }
        #endregion


        #region Abstract Methods (Subclass Protocol Layer)
        /// <summary>
        /// 将调用消息序列化为待发送的字节数组。
        /// <para>子类实现不同协议格式（XML / JSON 等），与 <see cref="RpcServerBase.SerializeResponseMessage"/> 镜像对称。</para>
        /// </summary>
        /// <param name="invokeMessage">待序列化的调用消息。</param>
        /// <returns>可直接写入 NetworkStream 的字节数组（应以 CRLF 结尾以标识行结束）。</returns>
        protected abstract byte[] SerializeInvokeMessage(InvokeMessage invokeMessage);

        /// <summary>
        /// 将接收到的数据行解析为一条响应消息。
        /// <para>一条数据行对应一条响应，子类实现不同协议的响应解析。</para>
        /// </summary>
        /// <param name="dataLine">一个完整的数据行字节数据（不含尾部 CRLF）。</param>
        /// <returns>解析出的响应消息；无法解析则返回 <c>null</c>。</returns>
        protected abstract ResponseMessage DeserializeResponseMessage(ArraySegment<byte> dataLine);
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

        #region Inner Types
        /// <summary>
        /// 待响应的调用信息，封装 <see cref="TaskCompletionSource{TResult}"/> 用于异步等待。
        /// </summary>
        private sealed class PendingCall
        {
            /// <summary>
            /// 任务完成源，用于异步等待和通知调用方。
            /// </summary>
            public TaskCompletionSource<ResponseMessage> Tcs;

            /// <summary>
            /// 根据响应消息设置成功结果。
            /// </summary>
            /// <param name="response">服务端返回的响应消息。</param>
            public void SetResult(ResponseMessage response)
            {
                Tcs?.TrySetResult(response);
            }

            /// <summary>
            /// 设置错误结果（客户端侧错误，非服务端返回）。
            /// </summary>
            /// <param name="code">错误状态码（负数）。</param>
            /// <param name="description">错误描述信息。</param>
            public void SetError(int code, string description)
            {
                if (Tcs == null) return;

                var response = new ResponseMessage
                {
                    Code = code,
                    Description = description,
                    Timestamp = DateTimeOffset.UtcNow,
                };
                Tcs.TrySetResult(response);
            }
        }
        #endregion
    }
}
