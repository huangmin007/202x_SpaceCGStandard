using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SpaceCG;

namespace SpaceCG.Net
{
    /// <summary>接收到数据时的事件参数。</summary>
    public class DataReceivedEventArgs : EventArgs
    {
        /// <summary>远程终结点。</summary>
        public IPEndPoint RemoteEndPoint { get; internal set; }

        /// <summary>接收到的数据副本。调用方可以安全持有，无需担心缓冲区复用。</summary>
        public byte[] Data { get; internal set; }

        /// <inheritdoc /> 
        public override string ToString()
        {
            return $"{RemoteEndPoint} DataLength:{Data?.Length}";
        }
    }

    /// <summary>连接状态枚举。</summary>
    public enum ReconnectState
    {
        /// <summary>空闲，尚未发起连接。</summary>
        Idle,

        /// <summary>正在连接中。</summary>
        Connecting,

        /// <summary>已成功连接，正在接收数据。</summary>
        Connected,

        /// <summary>连接中断，正在等待重连。</summary>
        Reconnecting,

        /// <summary>已释放，不再可用。</summary>
        Disposed
    }

    /// <summary>
    /// 带自动重连功能的 TCP 客户端封装。
    /// <para>当连接意外中断或首次连接失败时，会在 3~5 秒随机延迟后自动重连；
    /// 调用 <see cref="Close"/> 或 <see cref="Dispose()"/> 则停止重连并释放资源。</para>
    /// <para>线程安全级别：线程安全（内部使用锁保护状态切换，事件回调通过捕获的 <see cref="SynchronizationContext"/> 调度）。</para>
    /// </summary>
    /// <remarks>
    /// 使用方式：
    /// <code>
    /// var client = new AutoReconnectTcpClient("192.168.1.100", 8080);
    /// client.DataReceived += (s, e) => Console.WriteLine($"收到 {e.Data.Length} 字节");
    /// client.Connected += (s, e) => Console.WriteLine("已连接");
    /// client.ConnectAsync(); // 启动连接及自动重连
    /// // 发送数据
    /// await client.WriteAsync(data, 0, data.Length);
    /// // 主动断开（不再重连）
    /// client.Close();
    /// // 或直接释放
    /// // client.Dispose();
    /// </code>
    /// </remarks>
    public sealed class TcpClientEx : IDisposable
    {
        #region 字段
        /// <summary>读取缓冲区大小（默认 8192 字节）。</summary>
        private const int ReadBufferSize = 8192;
        /// <summary>数据接收缓冲区（预分配，避免每次重连分配）。</summary>
        private readonly byte[] StreamBuffer = new byte[ReadBufferSize];

        private readonly int _port;
        private readonly string _host;

        /// <summary>保护 _tcpClient、_state、_manualClose 的锁。</summary>
        private readonly object _lock = new object();
        /// <summary>重连延迟随机数生成器。</summary>
        private readonly Random _random = new Random();
        /// <summary>构造时捕获的同步上下文，用于将事件回调调度回原始线程（如 UI 线程）。</summary>
        private readonly SynchronizationContext _syncContext;
        
        /// <summary>TCP 客户端实例。访问受 <see cref="_lock"/> 保护。</summary>
        private TcpClient _tcpClient;
        /// <summary>当前连接状态。</summary>
        private ReconnectState _state = ReconnectState.Idle;
        /// <summary>取消令牌源，用于停止异步循环。</summary>
        private CancellationTokenSource _cancellationTokenSource;

        /// <summary>缓存的数据发送超时值，用于重连时恢复。</summary>
        private int _sendTimeout;
        /// <summary>缓存的数据接收超时值，用于重连时恢复。</summary>
        private int _receiveTimeout;

        /// <summary>true 表示手动关闭，不再自动重连。</summary>
        private bool _manualClose;
        /// <summary>是否已释放。</summary>
        private bool _disposed;
        #endregion

        #region 属性
        /// <summary>
        /// 获取当前是否与服务端保持连接（通过 Poll 探活检测）。
        /// <para>如果 Socket 可读但 Available 为 0，说明对端已发送 FIN 包，判定为断开。</para>
        /// </summary>
        public bool IsConnected => IsSocketConnected(_tcpClient);

        /// <summary>
        /// 通过 Poll 探活检测 Socket 是否真正连接。
        /// <para>Poll(SelectRead) 为 true 且 Available == 0 表示对端已断开（收到 FIN）。</para>
        /// </summary>
        /// <param name="tcpClient">待检测的 TcpClient。</param>
        /// <returns>连接正常返回 true，否则返回 false。</returns>
        private static bool IsSocketConnected(TcpClient tcpClient)
        {
            if (tcpClient == null || tcpClient.Client == null) return false;

            Socket socket = tcpClient.Client;
            return !((socket.Poll(1000, SelectMode.SelectRead) && (socket.Available == 0)) || !socket.Connected);
        }

        /// <summary>获取当前连接状态（枚举为值类型，读写原子，不加锁）。</summary>
        public ReconnectState State => _state;

        /// <summary>获取本地终结点（引用读写原子 + ?. 链安全，不加锁）。</summary>
        public IPEndPoint LocalEndPoint => _tcpClient?.Client?.LocalEndPoint as IPEndPoint;

        /// <summary>获取远程终结点（引用读写原子 + ?. 链安全，不加锁）。</summary>
        public IPEndPoint RemoteEndPoint => _tcpClient?.Client?.RemoteEndPoint as IPEndPoint;

        /// <summary>获取或设置重连延迟的最小值（默认 3 秒）。</summary>
        public TimeSpan MinReconnectDelay { get; set; } = TimeSpan.FromSeconds(3);

        /// <summary>获取或设置重连延迟的最大值（默认 5 秒）。</summary>
        public TimeSpan MaxReconnectDelay { get; set; } = TimeSpan.FromSeconds(5);

        /// <summary>获取或设置数据接收超时（毫秒）。0 表示无限等待。</summary>
        public int ReceiveTimeout
        {
            get => _tcpClient?.ReceiveTimeout ?? _receiveTimeout;
            set
            {
                _receiveTimeout = value;
                lock (_lock) { if (_tcpClient != null) _tcpClient.ReceiveTimeout = value; }
            }
        }
        /// <summary>获取或设置数据发送超时（毫秒）。0 表示无限等待。</summary>
        public int SendTimeout
        {
            get => _tcpClient?.SendTimeout ?? _sendTimeout;
            set
            {
                _sendTimeout = value;
                lock (_lock) { if (_tcpClient != null) _tcpClient.SendTimeout = value; }
            }
        }
        #endregion

        #region 事件
        /// <summary>正在尝试重连时触发。</summary>
        public event EventHandler<EventArgs> Reconnecting;
        /// <summary>连接成功时触发。</summary>
        public event EventHandler<EventArgs> Connected;
        /// <summary>连接断开时触发。</summary>
        public event EventHandler<EventArgs> Disconnected;

        /// <summary>发生异常时触发（不中断重连流程）。</summary>
        public event EventHandler<Exception> ExceptionEvent;
        /// <summary> 接收到数据时触发。</summary>
        public event EventHandler<DataReceivedEventArgs> DataReceived;
        #endregion

        #region 构造函数
        /// <summary>
        /// 初始化 <see cref="TcpClientEx"/> 的新实例。
        /// <para>注意：此构造函数不设置连接目标，需要后续调用 <see cref="ConnectAsync(string, int)"/>。</para>
        /// </summary>
        public TcpClientEx()
        {
            _host = null;
            _port = 0;
            _syncContext = SynchronizationContext.Current;
        }

        /// <summary>
        /// 初始化 <see cref="TcpClientEx"/> 的新实例，指定目标主机和端口。
        /// </summary>
        /// <param name="host">目标主机名或 IP 地址。</param>
        /// <param name="port">目标端口号。</param>
        /// <exception cref="ArgumentNullException"><paramref name="host"/> 为 null 或空字符串。</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="port"/> 不在 1~65535 范围内。</exception>
        public TcpClientEx(string host, int port)
        {
            if (string.IsNullOrWhiteSpace(host)) throw new ArgumentNullException(nameof(host));
            if (port <= 0 || port > 65535) throw new ArgumentOutOfRangeException(nameof(port), "端口号必须在 1~65535 之间。");

            _host = host;
            _port = port;
            _syncContext = SynchronizationContext.Current;
        }

        #endregion

        #region 公共方法

        /// <summary>
        /// 使用构造函数中指定的目标地址启动异步连接，并在连接中断后自动重连。
        /// <para>如果已连接或正在连接/重连中，则忽略本次调用。</para>
        /// </summary>
        public void ConnectAsync() => ConnectAsync(_host, _port);
        
        /// <summary>
        /// 启动到指定目标的异步连接，并在连接中断后自动重连。
        /// <para>如果已连接或正在连接/重连中，则忽略本次调用。</para>
        /// </summary>
        /// <param name="host">目标主机名或 IP 地址。</param>
        /// <param name="port">目标端口号。</param>
        /// <exception cref="ArgumentNullException"><paramref name="host"/> 为 null 或空字符串。</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="port"/> 不在 1~65535 范围内。</exception>
        public void ConnectAsync(string host, int port)
        {
            if (string.IsNullOrWhiteSpace(host)) throw new ArgumentNullException(nameof(host));
            if (port <= 0 || port > 65535) throw new ArgumentOutOfRangeException(nameof(port), "端口号必须在 1~65535 之间。");

            CancellationToken token;
            lock (_lock)
            {
                if (_disposed) throw new ObjectDisposedException(nameof(TcpClientEx));
                if (_state == ReconnectState.Connecting || _state == ReconnectState.Connected) return;

                _manualClose = false;
                _state = ReconnectState.Connecting;

                _cancellationTokenSource?.Cancel();
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = new CancellationTokenSource();

                token = _cancellationTokenSource.Token;
            }

            // 启动连接循环（不阻塞调用方）
            _ = Task.Run(() => ConnectLoopAsync(host, port, token));
        }

        /// <summary>异步发送数据到远程服务端。</summary>
        /// <param name="buffer">要发送的数据缓冲区。</param>
        /// <param name="offset">缓冲区中的起始偏移量。</param>
        /// <param name="count">要发送的字节数。</param>
        /// <returns>发送成功返回 true，否则返回 false。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="buffer"/> 为 null。</exception>
        public async Task<bool> WriteAsync(byte[] buffer, int offset, int count)
        {
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));
            if (offset < 0 || count < 0 || buffer.Length == 0 || offset + count > buffer.Length) return false;

            TcpClient client;
            lock (_lock)
            {
                if (_disposed || _state != ReconnectState.Connected || _tcpClient == null) return false;
                client = _tcpClient;
            }

            try
            {
                NetworkStream stream = client.GetStream();
                await stream.WriteAsync(buffer, offset, count).ConfigureAwait(false);
                await stream.FlushAsync().ConfigureAwait(false);
                return true;
            }
            catch (Exception ex)
            {
                RaiseExceptionEvent(ex);
                return false;
            }
        }

        /// <summary>异步发送数据到远程服务端。</summary>
        /// <param name="buffer">要发送的数据缓冲区。</param>
        /// <returns>发送成功返回 true，否则返回 false。</returns>
        public Task<bool> WriteAsync(byte[] buffer)
        {
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));
            return WriteAsync(buffer, 0, buffer.Length);
        }

        /// <inheritdoc cref="WriteAsync(byte[])"/>
        public Task<bool> WriteAsync(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) throw new ArgumentNullException(nameof(message));            
            return WriteAsync(Encoding.UTF8.GetBytes(message));
        }

        /// <summary>手动关闭连接，停止自动重连。</summary>
        public void Close()
        {
            lock (_lock)
            {
                if (_disposed) return;
                
                _manualClose = true;
                _state = ReconnectState.Idle;
            }

            _cancellationTokenSource?.Cancel();
            CloseTcpClientInternal();
        }

        /// <summary>释放所有资源，停止自动重连。</summary>
        public void Dispose()
        {
            if (_disposed) return;

            lock (_lock) { _manualClose = true; _state = ReconnectState.Disposed; }

            _cancellationTokenSource?.Cancel();
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;

            CloseTcpClientInternal();

            _disposed = true;

            // 清空事件处理器，防止内存泄漏
            Connected = null;
            Disconnected = null;
            Reconnecting = null;
            DataReceived = null;
            ExceptionEvent = null;

            GC.SuppressFinalize(this);
        }
        #endregion

        #region 私有方法
        /// <summary>连接与重连主循环。O(n) 复杂度，n 为重连尝试次数（上限由取消令牌控制）。</summary>
        private async Task ConnectLoopAsync(string host, int port, CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                // 检查是否手动关闭
                lock (_lock)
                {
                    if (_manualClose || _disposed) return;
                    CloseTcpClientInternalNoLock();
                    _state = ReconnectState.Connecting;
                }

                var newClient = new TcpClient();
                if (_sendTimeout > 0) newClient.SendTimeout = _sendTimeout;
                if (_receiveTimeout > 0) newClient.ReceiveTimeout = _receiveTimeout;

                lock (_lock)
                {
                    if (_manualClose || _disposed) { newClient.Dispose(); return; }
                    _tcpClient = newClient;
                }

                try
                {
                    await _tcpClient.ConnectAsync(host, port).ConfigureAwait(false);

                    IPEndPoint remoteEP;
                    lock (_lock)
                    {
                        if (_manualClose || _disposed || cancellationToken.IsCancellationRequested)
                        {
                            CloseTcpClientInternalNoLock();
                            return;
                        }

                        _state = ReconnectState.Connected;
                        remoteEP = _tcpClient?.Client?.RemoteEndPoint as IPEndPoint;
                    }

                    RaiseConnectedEvent();

                    // 进入数据接收循环，连接中断或出错时退出
                    await ReceiveLoopAsync(_tcpClient, remoteEP, cancellationToken).ConfigureAwait(false);

                    // 接收循环退出（连接中断）
                    RaiseDisconnectedEvent();
                }
                catch (Exception ex) when (!(ex is OperationCanceledException))
                {
                    RaiseExceptionEvent(ex);
                    lock (_lock) { CloseTcpClientInternalNoLock(); }
                }

                // 检查是否需要重连
                lock (_lock)
                {
                    if (_manualClose || _disposed || cancellationToken.IsCancellationRequested)
                    {
                        CloseTcpClientInternalNoLock();
                        if (_state != ReconnectState.Disposed) _state = ReconnectState.Idle;
                        return;
                    }

                    _state = ReconnectState.Reconnecting;
                }

                RaiseReconnectingEvent();

                // 等待 3~5 秒随机延迟后重连
                TimeSpan delay = GetReconnectDelay();
                try { await Task.Delay(delay, cancellationToken).ConfigureAwait(false); }
                catch (OperationCanceledException) { return; }
            }
        }

        /// <summary>数据接收循环。O(n) 复杂度，n 为接收的数据包数量。</summary>
        /// <param name="client">已连接的 TcpClient（调用方负责传入有效引用）。</param>
        /// <param name="remoteEP">远程终结点（缓存，避免每次循环持锁读取）。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        private async Task ReceiveLoopAsync(TcpClient client, IPEndPoint remoteEP, CancellationToken cancellationToken)
        {
            NetworkStream stream;
            try
            {
                stream = client.GetStream();
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                RaiseExceptionEvent(ex);
                return;
            }

            while (!cancellationToken.IsCancellationRequested)
            {
                int bytesRead;
                try
                {
                    bytesRead = await stream.ReadAsync(StreamBuffer, 0, StreamBuffer.Length, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (!(ex is OperationCanceledException))
                {
                    RaiseExceptionEvent(ex);
                    return; // 读取异常，退出接收循环触发重连
                }

                if (bytesRead == 0) return; // 对端正常关闭连接

                // 复制数据并触发事件（通过同步上下文调度）
                byte[] dataCopy = new byte[bytesRead];
                Buffer.BlockCopy(StreamBuffer, 0, dataCopy, 0, bytesRead);
                RaiseDataReceivedEvent(remoteEP, dataCopy);
            }
        }

        /// <summary>获取重连延迟（MinReconnectDelay~MaxReconnectDelay 之间的随机值）。</summary>
        private TimeSpan GetReconnectDelay()
        {
            double minMs = MinReconnectDelay.TotalMilliseconds;
            double maxMs = MaxReconnectDelay.TotalMilliseconds;

            if (minMs < 0) minMs = 0;
            if (maxMs < minMs) maxMs = minMs;

            int delayMs = _random.Next((int)minMs, (int)maxMs + 1);
            return TimeSpan.FromMilliseconds(delayMs);
        }

        /// <summary>关闭并释放内部 TcpClient（不加锁，调用方需持有锁）。</summary>
        private void CloseTcpClientInternalNoLock()
        {
            if (_tcpClient != null)
            {
                try { _tcpClient.Close(); } catch { /* 忽略关闭时的异常 */ }
                try { _tcpClient.Dispose(); } catch { /* 忽略释放时的异常 */ }
                _tcpClient = null;
            }
        }

        /// <summary>关闭并释放内部 TcpClient（加锁版本）。</summary>
        private void CloseTcpClientInternal()
        {
            lock (_lock) { CloseTcpClientInternalNoLock(); }
        }
        #endregion

        #region 事件触发（通过同步上下文调度）
        /// <summary>触发 <see cref="Connected"/> 事件，通过同步上下文调度。</summary>
        private void RaiseConnectedEvent()
        {
            var handler = Connected;
            if (handler != null) PostToSyncContext(() => handler(this, EventArgs.Empty));
        }

        /// <summary>触发 <see cref="Disconnected"/> 事件，通过同步上下文调度。</summary>
        private void RaiseDisconnectedEvent()
        {
            var handler = Disconnected;
            if (handler != null) PostToSyncContext(() => handler(this, EventArgs.Empty));
        }

        /// <summary>触发 <see cref="Reconnecting"/> 事件，通过同步上下文调度。</summary>
        private void RaiseReconnectingEvent()
        {
            var handler = Reconnecting;
            if (handler != null) PostToSyncContext(() => handler(this, EventArgs.Empty));
        }

        /// <summary>触发 <see cref="DataReceived"/> 事件，通过同步上下文调度。</summary>
        /// <param name="remoteEP">远程终结点。</param>
        /// <param name="data">数据副本。</param>
        private void RaiseDataReceivedEvent(IPEndPoint remoteEP, byte[] data)
        {
            var handler = DataReceived;
            if (handler != null)
            {
                PostToSyncContext(() => handler(this, new DataReceivedEventArgs
                {
                    Data = data,
                    RemoteEndPoint = remoteEP,
                }));
            }
        }

        /// <summary>触发 <see cref="ExceptionEvent"/> 事件，通过同步上下文调度。</summary>
        private void RaiseExceptionEvent(Exception exception)
        {
            var handler = ExceptionEvent;
            if (handler != null) PostToSyncContext(() => handler(this, exception));
        }

        /// <summary>
        /// 将回调投递到捕获的同步上下文上执行。
        /// 如果构造时 <see cref="SynchronizationContext.Current"/> 为 null，则直接在当前线程执行。
        /// </summary>
        private void PostToSyncContext(Action action)
        {
            if (_syncContext != null) _syncContext.Post(_ => action(), null);
            else action();
        }

        #endregion
    }
}
