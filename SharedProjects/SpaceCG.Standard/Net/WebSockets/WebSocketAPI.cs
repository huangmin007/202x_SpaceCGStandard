using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SpaceCG.Net.WebSockets
{
    /// <summary>
    /// 基于 <see cref="ClientWebSocket"/> 的 双向通信/双工通信，多轮会话、交互式通信 API 基类
    /// </summary>
    public abstract class WebSocketAPI : IDisposable
    {
        private volatile bool _isDisposed;
        private readonly object _socketLock = new object();

        /// <summary>
        /// 是否与服务端已连接
        /// </summary>
        public bool IsConnected
        {
            get
            {
                try
                {
                    var socket = _clientWebSocket;
                    return socket != null && socket.State == WebSocketState.Open;
                }
                catch { return false; }
            }
        }
        /// <summary>
        /// HTTP 请求头及其值
        /// </summary>
        public Dictionary<string, string> RequestHeader { get; private set; } = new Dictionary<string, string>();

        /// <summary>
        /// 文本数据请求队列
        /// </summary>
        protected readonly ConcurrentQueue<string> TextRequestQueue = new ConcurrentQueue<string>();
        /// <summary>
        /// 二进制数据请求队列
        /// </summary>
        protected readonly ConcurrentQueue<byte[]> BinaryRequestQueue = new ConcurrentQueue<byte[]>();

        private Task _sendTask;
        private Task _receiveTask;
        /// <summary>当前活动连接；所有访问必须经由 _socketLock。</summary>
        private ClientWebSocket _clientWebSocket = null;
        private CancellationTokenSource _cancellationTokenSource;

        /// <summary>
        /// 空闲超时时间
        /// </summary>
        public TimeSpan IdleTimeOut { get; set; } = TimeSpan.FromMinutes(3);

        private readonly Stopwatch _stopwatch = new Stopwatch();

        /// <summary>
        /// 构造函数
        /// </summary>
        public WebSocketAPI()
        {
            _cancellationTokenSource = new CancellationTokenSource();
            _sendTask = Task.Run(() => WebSocketSendAsync(_cancellationTokenSource.Token));
        }

        #region abstract 方法
        /// <summary>
        /// 获取 WebSocket URI 地址，或是认证后的 WebSocket URI 地址
        /// </summary>
        /// <returns></returns>
        protected abstract Uri GetAuthenticationUri();
        /// <summary>
        /// 分析处理服务端响应数据（文本数据）
        /// </summary>
        /// <param name="responseResult"></param>
        protected abstract void AnalyseResponseResult(string responseResult);
        /// <summary>
        /// 分析处理服务端响应数据（二进制数据）
        /// </summary>
        /// <param name="responseResult"></param>
        protected abstract void AnalyseResponseResult(byte[] responseResult);
        #endregion

        /// <summary>
        /// 清空所有请求数据队列
        /// </summary>
        public void ClearRequestQueue()
        {
            while (!TextRequestQueue.IsEmpty)
                TextRequestQueue.TryDequeue(out var _);

            while (!BinaryRequestQueue.IsEmpty)
                BinaryRequestQueue.TryDequeue(out var _);
        }

        /// <summary>
        /// 关闭 ClientWebSocket 连接。但等待发送线程并未退出，当队列中有数据时，就会重新建立连接。
        /// </summary>
        /// <param name="statusDescription"></param>
        /// <returns></returns>
        public async Task CloseAsync(string statusDescription)
        {
            if (_isDisposed) throw new ObjectDisposedException(nameof(WebSocketAPI));

            ClientWebSocket clientWebSocket = null;

            lock (_socketLock)
            {
                if (_clientWebSocket != null)
                {
                    clientWebSocket = _clientWebSocket;
                    _clientWebSocket = null;
                }
            }

            if (clientWebSocket == null) return;

            try
            {
                if (clientWebSocket.State == WebSocketState.Open)
                {
                    // 使用 CancellationToken.None 确保关闭操作不会被外部的 Cancel 意外中断
                    await clientWebSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, statusDescription, CancellationToken.None).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                Trace.TraceWarning($"ClientWebSocketAPI.CloseAsync 关闭连接异常：({ex.GetType().Name}){ex.Message}");
            }
            finally
            {
                _stopwatch.Stop();
                clientWebSocket.Dispose();
            }
        }

        /// <summary>
        /// ClientWebSocket 发送与重连控制线程
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        private async Task WebSocketSendAsync(CancellationToken cancellationToken)
        {
            Trace.TraceInformation($"WebSocketSendAsync 线程启动，ThreadId: {Environment.CurrentManagedThreadId}");

            while (!_isDisposed && !cancellationToken.IsCancellationRequested)
            {
                #region 0.检查连接状态
                lock(_socketLock)
                {
                    if (_clientWebSocket != null && _clientWebSocket.State >= WebSocketState.CloseSent)
                    {
                        _clientWebSocket.Dispose();
                        _clientWebSocket = null;
                    }
                }
                #endregion

                #region 0.检查连接空闲超时
                bool shouldClose = false;
                lock (_socketLock)
                {
                    if (_clientWebSocket != null && _clientWebSocket.State >= WebSocketState.Open && _stopwatch.Elapsed > IdleTimeOut)
                    {
                        shouldClose = true;
                        Trace.TraceInformation($"WebSocketSendAsync 空闲超时，主动关闭连接 ...");
                        // 注意：这里调用 CloseAsync 会释放锁，但由于我们在 lock 内判断，且 CloseAsync 内部会处理 _clientWebSocket = null，是安全的
                        // 为避免死锁，我们退出 lock 后再调用
                    }
                }
                if (shouldClose)
                {
                    Trace.TraceInformation("WebSocket 空闲超时，主动关闭连接...");
                    await CloseAsync("IdleTimeout").ConfigureAwait(false);
                    continue;
                }
                #endregion

                // 队列中无数据，等待
                if (TextRequestQueue.IsEmpty && BinaryRequestQueue.IsEmpty)
                {
                    await Task.Delay(50, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                #region 1.创建新的连接，如果需要
                if (_clientWebSocket == null)
                {
                    Trace.WriteLine($"WebSocketSendAsync 尝试创建新的连接 ...");
                    var authUri = GetAuthenticationUri();
                    if (authUri == null)
                    {
                        await Task.Delay(1000, cancellationToken).ConfigureAwait(false); // 等待 URI 准备就绪
                        continue;
                    }

                    var newSocket = new ClientWebSocket();
                    if (RequestHeader?.Count > 0)
                    {
                        foreach (var header in RequestHeader)
                        {
                            newSocket.Options.SetRequestHeader(header.Key, header.Value);
                        }
                    }

                    try
                    {
                        await newSocket.ConnectAsync(authUri, cancellationToken).ConfigureAwait(false);

                        lock (_socketLock)
                        {
                            _clientWebSocket = newSocket;
                        }
                        _stopwatch.Restart();
                        Trace.TraceInformation($"ClientWebSocket 连接成功：{authUri}");

                        // 启动独立的接收线程
                        if (newSocket.State == WebSocketState.Open)
                        {
                            if (_receiveTask != null && !_receiveTask.IsCompleted)
                            {
                                try { _receiveTask.Wait(TimeSpan.FromSeconds(1)); }
                                catch (Exception) { /* 忽略等待中的异常 */ }
                            }

                            _receiveTask = Task.Run(() => WebSocketReceiveAsync(_clientWebSocket, cancellationToken), cancellationToken);
                        }
                    }
                    catch (OperationCanceledException ex)
                    {
                        newSocket.Dispose();
                        Trace.TraceInformation("WebSocket 连接被取消。");
                        break; // 退出循环
                    }
                    catch (Exception ex)
                    {
                        newSocket.Dispose();
                        Trace.TraceError($"ClientWebSocket.ConnectAsync 连接异常: {ex.Message}, URI: {authUri}");
                        await Task.Delay(2000, cancellationToken).ConfigureAwait(false); // 连接失败后延迟重试，避免 CPU 空转
                        continue;
                    }
                }
                #endregion

                #region 2.发送数据
                // 发送文本数据
                if (TextRequestQueue.TryPeek(out string textRequest))
                {
                    ClientWebSocket clientWebSocket = null;
                    lock (_socketLock)
                    {
                        if (_clientWebSocket?.State == WebSocketState.Open)
                        {
                            _stopwatch.Restart();
                            clientWebSocket = _clientWebSocket;
                        }
                    }

                    if (clientWebSocket != null)
                    {
                        try
                        {
                            var requestBytes = new ArraySegment<byte>(Encoding.UTF8.GetBytes(textRequest));
                            await clientWebSocket.SendAsync(requestBytes, WebSocketMessageType.Text, true, cancellationToken).ConfigureAwait(false);

                            // 数据发送成功，从请求队列中移除
                            TextRequestQueue.TryDequeue(out var _);
                        }
                        catch (Exception ex)
                        {
                            Trace.TraceError($"ClientWebSocket.SendAsync MessageType.Text 数据发送异常：{ex.Message}");
                            lock (_socketLock)
                            {
                                if (_clientWebSocket == clientWebSocket)
                                {
                                    _clientWebSocket.Dispose();
                                    _clientWebSocket = null;
                                }
                            }
                        }
                    }
                }

                // 发送二进制数据
                if (BinaryRequestQueue.TryPeek(out byte[] binaryRequest))
                {
                    ClientWebSocket clientWebSocket = null;
                    lock(_socketLock)
                    {
                        if (_clientWebSocket?.State == WebSocketState.Open)
                        {
                            _stopwatch.Restart();
                            clientWebSocket = _clientWebSocket;
                        }
                    }

                    if (clientWebSocket != null)
                    {
                        try
                        {
                            var requestBytes = new ArraySegment<byte>(binaryRequest);
                            await clientWebSocket.SendAsync(requestBytes, WebSocketMessageType.Binary, true, cancellationToken).ConfigureAwait(false);

                            // 数据发送成功，从请求队列中移除
                            BinaryRequestQueue.TryDequeue(out var _);
                        }
                        catch (Exception ex)
                        {
                            Trace.TraceError($"ClientWebSocket.SendAsync MessageType.Binary 数据发送异常：{ex.Message}");
                            lock (_socketLock)
                            {
                                if (_clientWebSocket == clientWebSocket)
                                {
                                    _clientWebSocket.Dispose();
                                    _clientWebSocket = null;
                                }
                            }
                        }
                    }
                }
                #endregion
            }

            Trace.TraceInformation($"WebSocketSendAsync 线程退出, ThreadId: {Environment.CurrentManagedThreadId}");
        }

        /// <summary>
        /// ClientWebSocket 接收数据线程
        /// </summary>
        /// <param name="clientWebSocket"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        private async Task WebSocketReceiveAsync(ClientWebSocket clientWebSocket, CancellationToken cancellationToken)
        {
            Trace.TraceInformation($"WebSocketReceiveAsync 线程启动, ThreadId: {Environment.CurrentManagedThreadId}");

            using (var memoryStream = new MemoryStream())
            {
                var buffer = new byte[1024 * 64];

                while (!_isDisposed && !cancellationToken.IsCancellationRequested)
                {
                    // 检查传入的 socket 是否仍是当前有效的 socket
                    bool isValid = false;
                    lock (_socketLock)
                    {
                        if (_clientWebSocket == clientWebSocket && clientWebSocket.State < WebSocketState.CloseSent)
                        {
                            isValid = true;
                        }
                    }
                    // 连接已被外部替换或关闭，退出此接收线程
                    if (!isValid) break; 

                    try
                    {
                        _stopwatch.Restart();

                        // 接收数据并拼接到 MemoryStream 中
                        var receiveResult = await clientWebSocket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken).ConfigureAwait(false);
                        memoryStream.Write(buffer, 0, receiveResult.Count);
                        // 如果消息未结束，继续接收下一个分片
                        if (!receiveResult.EndOfMessage) continue;

                        // 消息接收完整，获取实际数据总长度
                        var messageType = receiveResult.MessageType;
                        var receiveLength = (int)memoryStream.Length;

                        #region 1. 关闭信息处理
                        if (messageType == WebSocketMessageType.Close)
                        {
                            Trace.TraceInformation("收到服务端 Close 消息，准备关闭连接。");

                            lock (_socketLock)
                            {
                                if (_clientWebSocket == clientWebSocket)
                                {
                                    _clientWebSocket = null;
                                }
                            }

                            try
                            {
                                await clientWebSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Received close message", CancellationToken.None).ConfigureAwait(false);
                            }
                            catch { }
                            finally
                            {
                                clientWebSocket.Dispose();
                            }
                            break;
                        }
                        #endregion

                        #region 2. Text/Binary 数据分析处理
                        try
                        {
                            if (messageType == WebSocketMessageType.Text)
                            {
                                AnalyseResponseResult(Encoding.UTF8.GetString(memoryStream.GetBuffer(), 0, receiveLength));
                            }
                            else if (messageType == WebSocketMessageType.Binary)
                            {
                                byte[] binaryData = new byte[receiveLength];
                                Buffer.BlockCopy(memoryStream.GetBuffer(), 0, binaryData, 0, receiveLength);
                                AnalyseResponseResult(binaryData);
                                //AnalyseResponseResult(memoryStream.GetBuffer());
                            }
                        }
                        catch (Exception ex)
                        {
                            Trace.TraceError($"WebSocket 消息分析处理异常: {ex.Message}");
                        }
                        #endregion

                        // 清空 MemoryStream
                        memoryStream.SetLength(0);
                    }
                    catch(Exception ex) when (ex is OperationCanceledException || ex is ObjectDisposedException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        Trace.TraceError($"WebSocket 接收异常 (连接可能已断开): {ex.Message}");
                        lock (_socketLock)
                        {
                            if (_clientWebSocket == clientWebSocket)
                            {
                                _clientWebSocket = null;
                            }
                        }
                        clientWebSocket.Dispose();
                        break;
                    }
                }
            }

            Trace.TraceInformation($"WebSocketReceiveAsync 线程退出, ThreadId: {Environment.CurrentManagedThreadId}");
        }
        
        /// <inheritdoc/>
        public virtual void Dispose()
        {
            if (_isDisposed) return;

            _stopwatch.Stop();
            ClearRequestQueue();

            try
            {
                // 尝试优雅关闭连接 (同步等待，最多 3 秒)
                CloseAsync("Disposing").GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                Trace.TraceWarning($"WebSocket 优雅关闭失败: {ex.Message}");
            }

            _isDisposed = true;
            _cancellationTokenSource?.Cancel();

            if (_sendTask != null && !_sendTask.IsCompleted)
            {
                bool completed = _sendTask.Wait(TimeSpan.FromSeconds(3));
                if (!completed)
                {
                    Trace.TraceWarning("WebSocket 发送线程未能在 3 秒内正常退出。");
                }
            }
            if(_receiveTask != null && !_receiveTask.IsCompleted)
            {
                bool completed = _receiveTask.Wait(TimeSpan.FromSeconds(3));
                if (!completed)
                {
                    Trace.TraceWarning("WebSocket 接收线程未能在 3 秒内正常退出。");
                }
            }

            lock (_socketLock)
            {
                _clientWebSocket?.Dispose();
                _clientWebSocket = null;
            }

            _cancellationTokenSource?.Dispose();

            _sendTask = null;
            _receiveTask = null;
            _cancellationTokenSource = null;

            GC.SuppressFinalize(this);
            Trace.TraceInformation("WebSocketAPI 资源释放完毕。");
        }
    }
}