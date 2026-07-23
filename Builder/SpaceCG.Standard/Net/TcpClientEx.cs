using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using SpaceCG.Extensions;
using Trace = SpaceCG.Diagnostics.Trace;

namespace SpaceCG.Net
{
    /// <summary>接收到数据时的事件参数。</summary>
    public class DataReceivedEventArgs : EventArgs
    {
        /// <summary> 接收到的数据。</summary>
        public byte[] Data { get; internal set; }

        /// <summary> 本地终结点。</summary>
        public IPEndPoint LocalEndPoint { get; internal set; }

        /// <summary> 远程终结点。</summary>
        public IPEndPoint RemoteEndPoint { get; internal set; }

        /// <summary>
        /// 构造函数。
        /// </summary>
        /// <param name="data"></param>
        /// <param name="localEndPoint"></param>
        /// <param name="remoteEndPoint"></param>
        public DataReceivedEventArgs(byte[] data, IPEndPoint localEndPoint, IPEndPoint remoteEndPoint)
        {
            Data = data;
            LocalEndPoint = localEndPoint;
            RemoteEndPoint = remoteEndPoint;
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return $"{RemoteEndPoint} DataLength:{Data?.Length ?? 0}";
        }
    }

    /// <summary>
    /// 增强型 TCP 客户端，提供自动重连、异步发送/接收、事件通知等基础能力。
    /// <para>核心特性：</para>
    /// <list type="bullet">
    ///     <item><b>自动重连</b>：意外断开后按 <see cref="ReconnectDelay"/> 间隔自动重连，手动 <see cref="Close"/> 不会重连。</item>
    ///     <item><b>异步收发</b>：发送使用信号量序列化防止字节交错；接收使用独立消费循环并通过事件通知调用方。</item>
    ///     <item><b>事件驱动</b>：通过 <see cref="Connected"/>、<see cref="Disconnected"/>、<see cref="DataReceived"/> 事件获知状态变化。</item>
    ///     <item><b>环形缓冲</b>：在消费循环中使用高效环形缓冲区处理接收数据，减少内存拷贝。</item>
    /// </list>
    /// <para>线程安全级别：线程安全。</para>
    /// </summary>
    /// <example>
    /// <code>
    /// var client = new TcpClientEx("127.0.0.1", 8888);
    /// client.DataReceived += (sender, e) =>
    /// {
    ///     Console.WriteLine($"收到 {e.Data.Length} 字节: {Encoding.UTF8.GetString(e.Data)}");
    /// };
    /// client.Connected += (sender, e) => Console.WriteLine("已连接");
    /// client.Disconnected += (sender, e) => Console.WriteLine("已断开");
    /// client.Connect();
    /// await client.WriteAsync(Encoding.UTF8.GetBytes("Hello"));
    /// client.Close();
    /// </code>
    /// </example>
    public sealed class TcpClientEx : IDisposable
    {
        private readonly object _lock = new object();

        private bool _isDisposed;
        private bool _isManualClosed;

        private Task _connectTask;
        private TcpClient _tcpClient;
        private CancellationTokenSource _cts;

        /// <summary>
        /// 发送信号量，序列化并发写入，避免多个 WriteAsync 调用在底层 Socket 上交错字节。
        /// </summary>
        private readonly SemaphoreSlim _sendSemaphore = new SemaphoreSlim(1, 1);

        #region Public Properties
        /// <summary>
        /// 获取客户端是否已连接到服务端。
        /// </summary>
        public bool IsConnected
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
        /// 获取或设置默认操作超时时间，默认 3 秒。
        /// </summary>
        public TimeSpan DefaultTimeout { get; set; } = TimeSpan.FromSeconds(3.0);
        /// <summary>
        /// 获取或设置自动重连的延迟时间，默认 3 秒。
        /// <para>连接断开后等待此间隔再尝试重新连接。</para>
        /// <para>设置 <see cref="TimeSpan.MaxValue"/> 时禁用自动重连。</para>
        /// <para>设置 &lt;= 0 时立即重连（不等待），非常不建议。</para>
        /// </summary>
        public TimeSpan ReconnectDelay { get; set; } = TimeSpan.FromSeconds(3.0);
        #endregion

        #region Events
        /// <summary>连接成功时触发。事件在消费线程上引发，调用方需自行封送到 UI 线程。</summary>
        public event EventHandler<EventArgs> Connected;
        /// <summary>连接断开时触发。事件在消费线程上引发，调用方需自行封送到 UI 线程。</summary>
        public event EventHandler<EventArgs> Disconnected;
        /// <summary>接收到数据时触发。事件在消费线程上引发，调用方需自行封送到 UI 线程。</summary>
        public event EventHandler<DataReceivedEventArgs> DataReceived;
        #endregion

        #region Constructors
        /// <summary>
        /// 使用指定的 IP 地址和端口号初始化 <see cref="TcpClientEx"/> 类的新实例。
        /// </summary>
        /// <param name="address">远程服务端 IP 地址。</param>
        /// <param name="port">远程服务端端口号。</param>
        /// <exception cref="ArgumentNullException">address 为 null。</exception>
        /// <exception cref="ArgumentException">端口号不在 1-65535 范围内。</exception>
        public TcpClientEx(IPAddress address, int port)
        {
            if (address == null)
                throw new ArgumentNullException(nameof(address));
            if (port < 1 || port > 65535)
                throw new ArgumentException("端口号必须在 1-65535 之间", nameof(port));

            RemoteEndPoint = new IPEndPoint(address, port);
        }
        /// <inheritdoc cref="TcpClientEx(IPAddress, int)"/>
        public TcpClientEx(string address, int port) : this(IPAddress.Parse(address), port) { }
        #endregion

        #region Connect / Close
        /// <summary>
        /// 连接到服务端。如果已在连接状态，则忽略本次调用。
        /// <para>内部启动异步循环连接任务，连接成功后自动开始接收数据。</para>
        /// </summary>
        /// <param name="address">远程服务端 IP 地址。</param>
        /// <param name="port">远程服务端端口号。</param>
        /// <exception cref="ObjectDisposedException">实例已释放时抛出。</exception>
        /// <exception cref="ArgumentNullException">address 为 null。</exception>
        /// <exception cref="ArgumentException">端口号不在 1-65535 范围内。</exception>
        public void Connect(IPAddress address, int port)
        {
            if (_isDisposed)
                throw new ObjectDisposedException(nameof(TcpClientEx));
            if (address == null)
                throw new ArgumentNullException(nameof(address));
            if (port < 1 || port > 65535)
                throw new ArgumentException("端口号必须在 1-65535 之间", nameof(port));

            if (RemoteEndPoint.Port != port) RemoteEndPoint.Port = port;
            if (RemoteEndPoint.Address != address) RemoteEndPoint.Address = address;

            _isManualClosed = false;
            _cts = new CancellationTokenSource();
            _connectTask = ConnectWithRetryAsync(RemoteEndPoint.Address, RemoteEndPoint.Port, _cts.Token);
        }
        /// <inheritdoc cref="Connect(IPAddress, int)"/>
        public void Connect() => Connect(RemoteEndPoint.Address, RemoteEndPoint.Port);
        /// <inheritdoc cref="Connect(IPAddress, int)"/>
        public void Connect(string address, int port) => Connect(IPAddress.Parse(address), port);

        /// <summary>
        /// 断开与服务端的连接。
        /// <para>手动断开后不会自动重连，需再次调用 <see cref="Connect()"/> 恢复。</para>
        /// </summary>
        public void Close()
        {
            _isManualClosed = true;

            try { _cts?.Cancel(); }
            catch (Exception) { }

            lock (_lock)
            {
                CloseTcpClient();
            }

            try
            {
                _connectTask?.Wait(100);
                _connectTask?.Dispose();
            }
            catch (Exception) { }
            finally
            {
                _connectTask = null;
            }

            try { _cts?.Dispose(); }
            finally
            {
                _cts = null;
            }
        }
        /// <summary>
        /// 关闭并释放内部 TcpClient（不加锁，由调用方保证线程安全）。
        /// </summary>
        private void CloseTcpClient()
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

        #region ConnectWithRetryAsync & HandleServerSessionAsync
        /// <summary>
        /// 连接循环任务：持续尝试连接直到成功或取消。
        /// <para>连接成功时触发 <see cref="Connected"/> 事件并进入会话处理循环。</para>
        /// </summary>
        private async Task ConnectWithRetryAsync(IPAddress address, int port, CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                lock (_lock)
                {
                    LocalEndPoint = null;
                    CloseTcpClient();
                    if (_isDisposed || _isManualClosed) return;
                }

                var newClient = new TcpClient();
                lock (_lock)
                {
                    if (_isDisposed || _isManualClosed)
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
                        if (_isManualClosed || _isDisposed || cancellationToken.IsCancellationRequested)
                        {
                            CloseTcpClient();
                            return;
                        }
                    }

                    _tcpClient.SendBufferSize = SendBufferSize;
                    _tcpClient.ReceiveBufferSize = ReceiveBufferSize;
                    LocalEndPoint = _tcpClient.Client.LocalEndPoint as IPEndPoint;

                    Trace.TraceInformation($"TCP 客户端 {LocalEndPoint} 已连接到 {RemoteEndPoint}");
                    Connected?.Invoke(this, EventArgs.Empty);
                }
                catch (Exception ex) when (ex is OperationCanceledException || ex is ObjectDisposedException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    var delay = ReconnectDelay;
                    if (delay == TimeSpan.MaxValue) break;
                    Trace.TraceWarning($"TCP 客户端连接失败: {ex.Message}，重试中 .....");

                    if (delay > TimeSpan.Zero)
                    {
                        await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                    }
                    continue;
                }

                await HandleServerSessionAsync(_tcpClient, cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// 处理与服务端的会话：持续接收数据并通过 <see cref="DataReceived"/> 事件通知调用方。
        /// </summary>
        private async Task HandleServerSessionAsync(TcpClient tcpClient, CancellationToken cancelToken)
        {
            if (tcpClient == null) return;

            var clientStream = tcpClient.GetStream();
            var clientEndPoint = tcpClient.Client.LocalEndPoint as IPEndPoint;

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

                    writePosition += count;
                    pendingLength += count;

                    // 将缓冲区中所有待消费数据拷贝并通过事件抛出
                    var availableLength = pendingLength - readPosition;
                    if (availableLength > 0 && DataReceived != null)
                    {
                        var data = new byte[availableLength];
                        Buffer.BlockCopy(clientBuffer, readPosition, data, 0, availableLength);
                        readPosition = pendingLength;

                        DataReceived.Invoke(this, new DataReceivedEventArgs(data, clientEndPoint, RemoteEndPoint));
                    }

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
                        Trace.TraceInformation($"TCP 客户端 {clientEndPoint} 移动缓冲区数据 {remaining} bytes");

                        writePosition = remaining;
                        pendingLength = remaining;
                        readPosition = 0;
                    }

                    if (writePosition == bufferSize)
                    {
                        Trace.TraceWarning($"TCP 客户端 {clientEndPoint} 接收缓冲区已满且无完整消息，清空 {pendingLength} bytes");
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
                Trace.TraceError($"TCP 客户端 {clientEndPoint} 接收数据异常：({ex.GetType().Name}) {ex.Message}");
            }
            finally
            {
                Trace.TraceInformation($"TCP 客户端 {clientEndPoint} 已断开连接");
                Disconnected?.Invoke(this, EventArgs.Empty);
            }
        }
        #endregion

        #region WriteAsync
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
                throw new ObjectDisposedException(nameof(TcpClientEx));
            if (!IsConnected)
                throw new InvalidOperationException("客户端未连接");
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
                Trace.TraceWarning($"TCP 客户端 {LocalEndPoint} 发送数据异常：({ex.GetType().Name}) {ex.Message}");
            }
            finally
            {
                _sendSemaphore.Release();
            }
        }
        /// <inheritdoc cref="WriteAsync(byte[], int, int, CancellationToken)"/>
        public async Task WriteAsync(byte[] data) => await WriteAsync(data, 0, data.Length, _cts.Token);
        #endregion

        #region IDisposable
        /// <summary>
        /// 释放占用的所有资源。包括断开连接、释放信号量和取消令牌。
        /// </summary>
        public void Dispose()
        {
            if (_isDisposed) return;

            _isDisposed = true;
            _isManualClosed = true;

            Close();

            try { _sendSemaphore?.Dispose(); }
            catch (Exception) { }
        }
        #endregion
    }
}
