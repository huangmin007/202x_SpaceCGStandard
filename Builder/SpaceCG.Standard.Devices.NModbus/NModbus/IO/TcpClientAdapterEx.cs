using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using NModbus.IO;

namespace SpaceCG.NModbus.IO
{
    // 未完成
    public class TcpClientAdapterEx : IStreamResource, IDisposable
    {
        private TcpClient _tcpClient;

        private Task _reconnectTask;
        private CancellationTokenSource _cts;

        private readonly int _port;
        private readonly string _host;

        private volatile int _readTimeout = Timeout.Infinite;
        private volatile int _writeTimeout = Timeout.Infinite;

        public int InfiniteTimeout => Timeout.Infinite;

        public int ReadTimeout
        {
            get => _readTimeout;
            set
            {
                _readTimeout = value;
                var stream = _tcpClient?.GetStream();
                if (stream != null) stream.ReadTimeout = value;
            }
        }

        public int WriteTimeout
        {
            get => _writeTimeout;
            set
            {
                _writeTimeout = value;
                var stream = _tcpClient?.GetStream();
                if (stream != null) stream.WriteTimeout = value;
            }
        }

        public TcpClientAdapterEx(string host, int port)
        {
            if (string.IsNullOrWhiteSpace(host)) throw new ArgumentException("主机地址不能为空", nameof(host));
            if (port <= 0 || port > 65535) throw new ArgumentException("端口号不正确", nameof(port));

            _host = host;
            _port = port;

            //this._tcpClient = tcpClient;
            this._cts = new CancellationTokenSource();

            _reconnectTask = EnsureConnected(_cts.Token);
        }

        private async Task EnsureConnected(CancellationToken cancellationToken)
        {
            var delay = TimeSpan.FromSeconds(3.0);
            var sendBufferSize = _tcpClient.SendBufferSize;
            var receiveBufferSize = _tcpClient.ReceiveBufferSize;

            var readTimeout = _tcpClient.GetStream()?.ReadTimeout ?? -1;
            var writeTimeout = _tcpClient.GetStream()?.WriteTimeout ?? -1;
            var remoteEndPoint = _tcpClient.Client.RemoteEndPoint as IPEndPoint;

            while (!cancellationToken.IsCancellationRequested)
            {
                // 连接状态检查
                if (_tcpClient.Connected)
                {
                    try { await Task.Delay(500, cancellationToken).ConfigureAwait(false); }
                    catch (Exception ex) when (ex is OperationCanceledException || ex is ObjectDisposedException) { break; }
                    catch (Exception) { if (cancellationToken.IsCancellationRequested) break; }
                    continue;
                }

                // 清理连接对象
                try { _tcpClient?.Dispose(); }
                finally { _tcpClient = null; }

                // 创建新的连接对象
                try
                {
                    var newClient = new TcpClient();
                    await newClient.ConnectAsync(remoteEndPoint.Address, remoteEndPoint.Port).ConfigureAwait(false);

                    _tcpClient = newClient;

                    if (readTimeout > 0)
                        newClient.GetStream().ReadTimeout = readTimeout;
                    if (writeTimeout > 0)
                        newClient.GetStream().WriteTimeout = writeTimeout;

                    newClient.SendBufferSize = sendBufferSize;
                    newClient.ReceiveBufferSize = receiveBufferSize;
                    Trace.TraceInformation($"客户端连接成功 {newClient.Client.LocalEndPoint} -> {newClient.Client.RemoteEndPoint}");
                }
                catch (Exception ex) when (ex is OperationCanceledException || ex is ObjectDisposedException) { break; }
                catch (Exception ex)
                {
                    if (cancellationToken.IsCancellationRequested) break;
                    Trace.TraceWarning($"客户端连接失败: {ex.Message}，重试中 .....");

                    try
                    {
                        await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                        continue;
                    }
                    catch (Exception ex0) when (ex0 is OperationCanceledException || ex0 is ObjectDisposedException) { break; }
                }
            }
        }

        public void Write(byte[] buffer, int offset, int size)
        {
            this._tcpClient.GetStream().Write(buffer, offset, size);
        }

        public int Read(byte[] buffer, int offset, int size)
        {
            return this._tcpClient.GetStream().Read(buffer, offset, size);
        }

        public void DiscardInBuffer()
        {
            this._tcpClient.GetStream().Flush();
        }

        public void Dispose()
        {
            this.Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                try { _cts?.Cancel(); }
                catch { }

                try 
                {
                    _reconnectTask?.Wait(500);
                    _reconnectTask?.Dispose(); 
                }
                finally
                {
                    _reconnectTask = null;
                }

                try { _cts?.Dispose(); }
                finally { _cts = null; }

                try { _tcpClient?.Dispose(); }
                finally { _tcpClient = null; }
            }
        }


    }
}
