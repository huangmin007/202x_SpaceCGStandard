using System;
using System.Net.Sockets;
using SpaceCG.Extensions;
using Trace = SpaceCG.Diagnostics.Trace;

namespace SpaceCG.IO
{
    /// <summary>
    /// TCP 客户同步传输连接对象
    /// </summary>
    public sealed class TcpClientTransport : ITransportChannel
    {
        private readonly int _port;
        private readonly string _hostname;

        /// <summary>
        /// <see cref="TcpClient"/> 对象
        /// </summary>
        private TcpClient _tcpClient;

        /// <inheritdoc/>
        public ChannelType Type => ChannelType.TCP;

        /// <inheritdoc/>
        public string Name => $"{Type}_{_hostname}_{_port}";

        /// <inheritdoc/>
        public bool IsConnected => SocketExtensions.IsConnected(_tcpClient);

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
            if (string.IsNullOrWhiteSpace(hostname))
                throw new ArgumentException("参数不能为空", nameof(hostname));

            if (port <= 0 || port > 65535)
                throw new ArgumentException("端口号不正确", nameof(port));

            this._port = port;
            this._hostname = hostname;
        }
        /// <summary>
        /// TCP 客户传输连接对象
        /// </summary>
        /// <param name="arguments">TCP 客户端连接参数，格式：hostname port </param>
        /// <exception cref="ArgumentException"></exception>
        public TcpClientTransport(params string[] arguments)
        {
            if (arguments == null || arguments.Length == 0)
                throw new ArgumentException("参数不能为空", nameof(arguments));
            if (arguments.Length < 2)
                throw new ArgumentException("参数个数不能小于2，应包括 hostname 和 port", nameof(arguments));

            if (!int.TryParse(arguments[1], out var port) || port < 1 || port > 65535)
                throw new ArgumentException("端口号不正确", nameof(arguments));

            this._port = port;
            this._hostname = arguments[0];
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

            _tcpClient = new TcpClient();
            _tcpClient.SendBufferSize = 8192 * 64;
            _tcpClient.ReceiveBufferSize = 8192 * 8;
            _tcpClient.Connect(_hostname, _port);
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

    }
}
