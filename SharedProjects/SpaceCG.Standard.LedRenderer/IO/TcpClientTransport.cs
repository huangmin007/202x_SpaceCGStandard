using System;
using System.Net.Sockets;
using Trace = SpaceCG.Diagnostics.Trace;

namespace SpaceCG.IO
{
    /// <summary>
    /// TCP 客户传输连接对象
    /// </summary>
    internal class TcpClientTransport : ITransportChannel
    {
        private int _port;
        private string _hostname;

        /// <summary>
        /// <see cref="TcpClient"/> 对象
        /// </summary>
        private TcpClient _tcpClient;

        /// <inheritdoc/>
        public TransportType Type => TransportType.TCP;

        /// <inheritdoc/>
        public string Name => $"{Type}_{_hostname}_{_port}";

        /// <inheritdoc/>
        public object Tag { get; set; }

        /// <inheritdoc/>
        public bool IsConnected => IsOnline(_tcpClient);

        /// <inheritdoc/>
        public int Available => _tcpClient == null ? 0 : _tcpClient.Available;

        /// <inheritdoc/>
        public int ReadTimeout
        {
            get { return _tcpClient == null ? 0 : _tcpClient.GetStream().ReadTimeout; }
            set { if (_tcpClient != null) _tcpClient.GetStream().ReadTimeout = value; }
        }
        /// <inheritdoc/>
        public int WriteTimeout
        {
            get { return _tcpClient == null ? 0 : _tcpClient.GetStream().WriteTimeout; }
            set { if (_tcpClient != null) _tcpClient.GetStream().WriteTimeout = value; }
        }

        /// <summary>
        /// TCP 客户传输连接对象
        /// </summary>
        /// <param name="hostname"></param>
        /// <param name="port"></param>
        public TcpClientTransport(string hostname, int port)
        {
            if (string.IsNullOrEmpty(hostname))
                throw new ArgumentException("参数不能为空", nameof(hostname));
            if (port <= 0 || port > 65535)
                throw new ArgumentException("端口号不正确", nameof(port));

            _port = port;
            _hostname = hostname;
        }
        
        /// <inheritdoc/>
        public void Open()
        {
            if (IsConnected) return;

            if (_tcpClient != null)
            {
                _tcpClient.Close();
                _tcpClient.Dispose();
                _tcpClient = null;
            }

            try
            {
                _tcpClient = new TcpClient();
                _tcpClient.SendBufferSize = 8192 * 64;
                _tcpClient.ReceiveBufferSize = 8192 * 8;
                _tcpClient.Connect(_hostname, _port);
            }
            catch(Exception ex)
            {
                Trace.TraceWarning($"({Name}) 建立连接失败：{ex.Message}");
                Close();
            }
        }

        /// <inheritdoc/>
        public void Close()
        {
            if (_tcpClient == null) return;

            _tcpClient.Close();
            _tcpClient.Dispose();
            _tcpClient = null;
        }

        /// <inheritdoc/>
        public int Read(byte[] buffer, int offset, int count)
        {
            if (_tcpClient == null || !IsConnected) return 0;
            return _tcpClient.GetStream().Read(buffer, offset, count);
        }
        /// <inheritdoc/>
        public void Write(byte[] buffer, int offset, int count)
        {
            if (_tcpClient == null || !IsConnected) return;
            _tcpClient.GetStream().Write(buffer, offset, count);
        }

        /// <inheritdoc/>
        public void ClearReadBuffer()
        {
            if (_tcpClient == null || !IsConnected) return;
            _tcpClient.GetStream().Flush();
        }
        /// <inheritdoc/>
        public void ClearWriteBuffer()
        {
            if (_tcpClient == null || !IsConnected) return;
            _tcpClient.GetStream().Flush();
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            Close();
        }

        /// <summary>
        /// TcpClient 连接状态即时检查
        /// <para>参考：https://www.cnblogs.com/schyzhkj/p/13255291.html </para>
        /// </summary>
        /// <param name="tcpClient"></param>
        /// <returns></returns>
        private static bool IsOnline(TcpClient tcpClient)
        {
            if (tcpClient == null) return false;
            return !((tcpClient.Client.Poll(0, SelectMode.SelectRead) && (tcpClient.Client.Available == 0)) || !tcpClient.Client.Connected);
        }
    }
}
