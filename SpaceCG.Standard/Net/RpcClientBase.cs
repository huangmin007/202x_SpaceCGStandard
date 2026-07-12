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
    /// <item><see cref="InvokeAsync"/> 发送请求并等待响应（ResponseMode=1，强制响应）。</item>
    /// <item><see cref="NotifyAsync"/> 发送单向通知不等待响应（ResponseMode=-1，服务端不响应）。</item>
    /// <item>意外断开时自动重连，手动调用 <see cref="Close"/> 不重连。</item>
    /// </list>
    /// </para>
    /// <para>线程安全级别：线程安全（发送使用信号量序列化，响应匹配使用 ConcurrentDictionary）。</para>
    /// </summary>
    public abstract class RpcClientBase : IDisposable
    {
        /// <summary> 数据行分隔符字节数组，使用 CRLF（0x0D, 0x0A），与 <see cref="RpcServerBase.NewLine"/> 一致。 </summary>
        public static readonly byte[] NewLine = RpcServerBase.NewLine;

        private bool _isDisposed;
        private bool _userDisconnected;
        private TcpClient _tcpClient;
        private NetworkStream _networkStream;
        private CancellationTokenSource _readCts;

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
        public bool IsConnected
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
        public IPEndPoint LocalEndPoint => _tcpClient?.Client?.LocalEndPoint as IPEndPoint;

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

        #region Events
        /// <summary>
        /// 连接成功事件。
        /// </summary>
        public event EventHandler<EventArgs> Connected;
        /// <summary>
        /// 连接断开事件。
        /// </summary>
        public event EventHandler<EventArgs> Disconnected;
        #endregion

        #region Constructors
        /// <summary>
        /// 使用指定的 IP 地址和端口号初始化 <see cref="RpcClientBase"/> 类的新实例。
        /// </summary>
        /// <param name="ipAddress">服务端的 IP 地址。</param>
        /// <param name="port">服务端的端口号，范围 1-65535。</param>
        /// <exception cref="ArgumentNullException"><paramref name="ipAddress"/> 为 null 时抛出。</exception>
        /// <exception cref="ArgumentException">端口号不在 1-65535 范围内时抛出。</exception>
        public RpcClientBase(IPAddress ipAddress, int port)
        {
            if (ipAddress == null)
                throw new ArgumentNullException(nameof(ipAddress));
            if (port < 1 || port > 65535)
                throw new ArgumentException("端口号必须在 1-65535 之间", nameof(port));

            RemoteEndPoint = new IPEndPoint(ipAddress, port);
        }
        /// <inheritdoc cref="RpcClientBase"/>
        public RpcClientBase(string hostname, int port) : this(IPAddress.Parse(hostname), port) { }        
        #endregion

        #region Public API: Connect / Close
        /// <summary>
        /// 异步连接到服务端，并启动后台接收循环。
        /// <para>如果已在连接状态，则忽略本次调用。</para>
        /// </summary>
        /// <returns>一个表示异步连接操作的任务。</returns>
        /// <exception cref="ObjectDisposedException">实例已释放时抛出。</exception>
        public async Task ConnectAsync()
        {
            if (_isDisposed)
                throw new ObjectDisposedException(nameof(RpcClientBase));

            if (IsConnected) return;

            _userDisconnected = false;
            await ConnectInternalAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// 断开与服务端的连接，取消所有待响应的调用。
        /// <para>手动断开后不会自动重连，需再次调用 <see cref="ConnectAsync"/> 恢复。</para>
        /// </summary>
        public void Close()
        {
            _userDisconnected = true;
            CloseTcpClientInternal(notifiDisconnected: true);
        }
        #endregion

        #region Public API:  Notify / Invoke
        /// <summary>
        /// 单向通知远程方法，不等待响应（ResponseMode = -1）。
        /// <para>适用于无返回参数、状态同步、日志上报等 fire-and-forget 场景。</para>
        /// </summary>
        /// <returns>发送是否成功（仅表示写入 TCP 流成功，不表示服务端处理成功）。</returns>
        public async Task<bool> NotifyAsync(string objectName, string methodName, object[] parameters = null)
        {
            if (_isDisposed)
                throw new ObjectDisposedException(nameof(RpcClientBase));
            if (!IsConnected) return false;

            var invokeMessage = InvokeMessage.Create(objectName, methodName, parameters);
            invokeMessage.Id = Interlocked.Increment(ref _messageId);
            invokeMessage.ResponseMode = -1;

            try
            {
                var bytes = SerializeInvokeMessage(invokeMessage);
                if (bytes == null || bytes.Length == 0) return false;

                await WriteAsync(bytes).ConfigureAwait(false);
                return true;
            }
            catch (Exception ex)
            {
                Trace.TraceWarning($"RPC NotifyAsync 发送失败：({ex.GetType().Name}) {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 调用远程方法并等待返回结果（ResponseMode = 1，强制响应）。
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
        public async Task<ResponseMessage> InvokeAsync(string objectName, string methodName, object[] parameters = null, TimeSpan? timeout = null)
        {
            if (_isDisposed)
                throw new ObjectDisposedException(nameof(RpcClientBase));
            if (!IsConnected)
                return ResponseMessage.Create(-100, "Client not connected");

            var invokeMessage = InvokeMessage.Create(objectName, methodName, parameters);
            invokeMessage.Id = Interlocked.Increment(ref _messageId);
            invokeMessage.ResponseMode = 1; // 强制响应

            var tcs = new TaskCompletionSource<ResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
            var pending = new PendingCall { Tcs = tcs };

            if (!_pendingCalls.TryAdd(invokeMessage.Id, pending))
            {
                return ResponseMessage.Create(-96, $"Message Id {invokeMessage.Id} already exists in pending calls");
            }

            try
            {
                var bytes = SerializeInvokeMessage(invokeMessage);

                if (bytes == null || bytes.Length == 0)
                {
                    if (_pendingCalls.TryRemove(invokeMessage.Id, out var removed))
                    {
                        removed.SetError(-98, "Serialize result is empty");
                    }
                    return await tcs.Task.ConfigureAwait(false);
                }

                // 序列化后再检查连接状态
                if (!IsConnected)
                {
                    if (_pendingCalls.TryRemove(invokeMessage.Id, out var removed))
                    {
                        removed.SetError(-99, "Client connection closed");
                    }
                    return await tcs.Task.ConfigureAwait(false);
                }

                await WriteAsync(bytes).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                if (_pendingCalls.TryRemove(invokeMessage.Id, out var removed))
                {
                    removed.SetError(-97, $"Client write failed: {ex.Message}");
                }
                return await tcs.Task.ConfigureAwait(false);
            }

            // 等待响应（带超时）
            return await WaitForResponseAsync(tcs, invokeMessage.Id, timeout ?? DefaultTimeout).ConfigureAwait(false);
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

        #region Private: Connect / Disconnect

        /// <summary>
        /// 内部连接实现。
        /// </summary>
        private async Task ConnectInternalAsync()
        {
            var tcpClient = new TcpClient();
            try
            {
                await tcpClient.ConnectAsync(RemoteEndPoint.Address, RemoteEndPoint.Port).ConfigureAwait(false);

                tcpClient.SendBufferSize = SendBufferSize;
                tcpClient.ReceiveBufferSize = ReceiveBufferSize;

                _tcpClient = tcpClient;
                _networkStream = tcpClient.GetStream();

                _readCts = new CancellationTokenSource();
                var readToken = _readCts.Token;

                // 启动后台接收循环
                _ = Task.Run(() => ReadLoopAsync(readToken), readToken);

                Connected?.Invoke(this, EventArgs.Empty);
                Trace.TraceInformation($"RPC 客户端已连接到 {RemoteEndPoint}");
            }
            catch (Exception)
            {
                tcpClient.Dispose();
                throw;
            }
        }

        /// <summary>
        /// 内部关闭 TCP 客户端连接，取消所有待响应调用。
        /// </summary>
        /// <param name="notifiDisconnected">是否触发 <see cref="Disconnected"/> 事件。</param>
        private void CloseTcpClientInternal(bool notifiDisconnected)
        {
            // 取消接收循环
            try
            {
                _readCts?.Cancel();
            }
            catch (Exception) { }

            var tcpClient = Interlocked.Exchange(ref _tcpClient, null);
            if (tcpClient != null)
            {
                try
                {
                    _networkStream?.Dispose();
                }
                catch (Exception) { }

                try
                {
                    tcpClient.Dispose();
                }
                catch (Exception) { }
            }

            _networkStream = null;

            // 将所有待响应的 PendingCall 以连接断开错误完成
            CancelAllPendingCalls(-99, "Connection closed");

            if (notifiDisconnected)
            {
                Disconnected?.Invoke(this, EventArgs.Empty);
            }
            Trace.TraceInformation($"RPC 客户端已断开 {RemoteEndPoint}");
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

        #endregion

        #region Private: Read Loop (Ring Buffer + CRLF Split)

        /// <summary>
        /// 后台接收循环，使用环形缓冲（Ring Buffer）模式以 CRLF 拆分数据行。
        /// <para>与服务端 <see cref="RpcServerBase"/> 的 ReadTcpClientAsync 使用相同的环形缓冲策略。</para>
        /// </summary>
        /// <param name="cancelToken">用于取消读取操作的令牌。</param>
        private async Task ReadLoopAsync(CancellationToken cancelToken)
        {
            var tcpClient = _tcpClient;
            var networkStream = _networkStream;

            if (tcpClient == null || networkStream == null) return;

            var localEndPoint = tcpClient.Client.LocalEndPoint as IPEndPoint;

            var bufferSize = tcpClient.ReceiveBufferSize / 2;
            var buffer = new byte[bufferSize];

            var writePosition = 0;      // 缓冲区写入位置（写指针）
            var pendingLength = 0;      // 缓冲区中未消费数据的长度
            var readPosition = 0;       // 数据消费的起始位置（读指针）

            try
            {
                while (!cancelToken.IsCancellationRequested && tcpClient.IsConnected())
                {
                    var count = await networkStream.ReadAsync(buffer, writePosition, bufferSize - writePosition, cancelToken).ConfigureAwait(false);
                    if (count == 0) break;

                    Debug.WriteLine($"RPC 客户端收到来自 {RemoteEndPoint} 的数据 {count} bytes");

                    writePosition += count;
                    pendingLength += count;

                    #region 扫描缓冲区中所有完整的数据行
                    while (readPosition < pendingLength)
                    {
                        var endIndex = buffer.IndexOf(NewLine, readPosition, pendingLength - readPosition);
                        if (endIndex < 0) break;

                        // 提取完整的数据行（不含 NewLine）
                        var dataLine = new ArraySegment<byte>(buffer, readPosition, endIndex - readPosition);
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
                        if (response != null)
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
                        var b = buffer[readPosition];
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
                        Buffer.BlockCopy(buffer, readPosition, buffer, 0, remaining);

                        writePosition = remaining;
                        pendingLength = remaining;
                        readPosition = 0;
                    }

                    if (writePosition == bufferSize)
                    {
                        Trace.TraceWarning($"RPC 客户端接收缓冲区已满且无完整消息，清空 {pendingLength} bytes");
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
                Trace.TraceError($"RPC 客户端接收循环异常：({ex.GetType().Name}) {ex.Message}");
            }
            finally
            {
                // 断开后的处理
                var wasUnexpected = tcpClient.IsConnected() && !_isDisposed && !_userDisconnected;
                CloseTcpClientInternal(notifiDisconnected: wasUnexpected);

                // 意外断开则自动重连
                if (wasUnexpected)
                {
                    _ = ReconnectLoopAsync();
                }
            }
        }

        #endregion

        #region Private: Reconnect

        /// <summary>
        /// 自动重连循环，固定间隔 3 秒尝试重连，直到成功或用户断开。
        /// </summary>
        private async Task ReconnectLoopAsync()
        {
            Trace.TraceInformation($"RPC 客户端开始重连 {RemoteEndPoint}...");

            while (!_isDisposed && !_userDisconnected)
            {
                try
                {
                    await Task.Delay(3000).ConfigureAwait(false);

                    if (_isDisposed || _userDisconnected || IsConnected) break;

                    await ConnectInternalAsync().ConfigureAwait(false);
                    Trace.TraceInformation($"RPC 客户端重连 {RemoteEndPoint} 成功");
                    return; // 重连成功，退出循环
                }
                catch (Exception ex) when (ex is OperationCanceledException || ex is ObjectDisposedException)
                {
                    return; // 正常退出
                }
                catch (Exception ex)
                {
                    Trace.TraceWarning($"RPC 客户端重连 {RemoteEndPoint} 失败：({ex.GetType().Name}) {ex.Message}");
                }
            }
        }

        #endregion

        #region Private: Invoke / Send / Dispatch
        
        /// <summary>
        /// 等待响应，支持超时。
        /// </summary>
        private async Task<ResponseMessage> WaitForResponseAsync(TaskCompletionSource<ResponseMessage> tcs, int messageId, TimeSpan timeout)
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
                    if (_pendingCalls.TryRemove(messageId, out _))
                    {
                        tcs.TrySetResult(ResponseMessage.Create(-90, "Response timeout"));
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
        private async Task WriteAsync(byte[] data)
        {
            if (data == null || data.Length == 0) return;

            await _sendSemaphore.WaitAsync().ConfigureAwait(false);
            try
            {
                if (!IsConnected)
                    throw new InvalidOperationException("Client not connected");

                await _networkStream.WriteAsync(data, 0, data.Length).ConfigureAwait(false);
                await _networkStream.FlushAsync().ConfigureAwait(false);
            }
            finally
            {
                _sendSemaphore.Release();
            }
        }

        /// <summary>
        /// 将接收到的响应消息分派到对应的 <see cref="PendingCall"/>。
        /// </summary>
        /// <param name="response">解析出的响应消息。</param>
        private void DispatchResponse(ResponseMessage response)
        {
            if (response == null) return;
            if (_pendingCalls.TryRemove(response.Id, out var pending))
            {
                pending.SetResult(response);
            }
            else
            {
                Debug.WriteLine($"RPC 客户端收到未匹配的响应消息 Id:{response.Id}");
            }
        }
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
            _userDisconnected = true;

            CloseTcpClientInternal(notifiDisconnected: true);

            try
            {
                _readCts?.Dispose();
            }
            catch (Exception) { }

            try
            {
                _sendSemaphore?.Dispose();
            }
            catch (Exception) { }

            _readCts = null;
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
