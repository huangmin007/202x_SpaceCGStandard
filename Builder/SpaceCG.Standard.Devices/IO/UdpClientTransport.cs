using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using Trace = SpaceCG.Diagnostics.Trace;

namespace SpaceCG.IO
{
    /// <summary>
    /// UDP 客户同步传输连接对象
    /// </summary>
    public sealed class UdpClientTransport : ITransportChannel
    {
        private UdpClient _udpClient;

        private readonly int _port;
        private readonly string _hostname;
        private bool _isConnected = false;

        /// <inheritdoc/>
        public ChannelType Type => ChannelType.UDP;

        /// <inheritdoc/>
        public string Name => $"{Type}_{_hostname}_{_port}";

        /// <inheritdoc/>
        public bool IsConnected => _udpClient == null ? false : _isConnected;
        /// <inheritdoc/>
        public int Available => _udpClient == null ? 0 : _udpClient.Available;

        /// <inheritdoc/>
        public int ReadTimeout
        {
            get { return _udpClient.Client.ReceiveTimeout; }
            set { _udpClient.Client.ReceiveTimeout = value; }
        }
        /// <inheritdoc/>
        public int WriteTimeout
        {
            get { return _udpClient.Client.SendTimeout; }
            set { _udpClient.Client.SendTimeout = value; }
        }

        /// <summary>
        /// UDP 客户传输连接对象
        /// </summary>
        /// <param name="hostname"></param>
        /// <param name="port"></param>
        public UdpClientTransport(string hostname, int port)
        {
            if (string.IsNullOrEmpty(hostname))
                throw new ArgumentException("参数不能为空", nameof(hostname));
            if (port <= 0 || port > 65535)
                throw new ArgumentException("端口号不正确", nameof(port));

            _port = port;
            _hostname = hostname;

            _udpClient = new UdpClient(hostname, port);
            _udpClient.Client.SendBufferSize = 8192 * 128;
            _udpClient.Client.ReceiveBufferSize = 8192 * 64;
        }
        /// <summary>
        /// UDP 客户传输连接对象
        /// </summary>
        /// <param name="arguments"></param>
        /// <exception cref="ArgumentException"></exception>
        public UdpClientTransport(params string[] arguments)
        {
            if (arguments == null || arguments.Length == 0)
                throw new ArgumentException("参数不能为空", nameof(arguments));
            if (arguments.Length < 2)
                throw new ArgumentException("参数个数不能小于2，应包括 hostname 和 port", nameof(arguments));

            if (!int.TryParse(arguments[1], out var port) || port < 1 || port > 65535)
                throw new ArgumentException("端口号不正确", nameof(arguments));

            _port = port;
            _hostname = arguments[0];

            _udpClient = new UdpClient(_hostname, _port);
            _udpClient.Client.SendBufferSize = 8192 * 128;
            _udpClient.Client.ReceiveBufferSize = 8192 * 64;
        }

        /// <inheritdoc/>
        public void Open()
        {
            if (_udpClient == null) return;
            if (_isConnected) return;

            if (_udpClient != null)
            {
                _udpClient.Close();
            }

            _udpClient.Connect(_hostname, _port);
            _isConnected = true;
        }

        /// <inheritdoc/>
        public void Close()
        {
            if (_udpClient == null) return;

            _udpClient.Close();
            _isConnected = false;
        }

        /// <inheritdoc/>
        public int Read(byte[] buffer, int offset, int count)
        {
            if (_udpClient == null || !_isConnected) return 0;

            //EndPoint ipEndPoint = new IPEndPoint(IPAddress.Any, 0);
            //return this._udpClient.Client.ReceiveFrom(buffer, offset, count, SocketFlags.None, ref ipEndPoint);
            return this._udpClient.Client.Receive(buffer, offset, count, SocketFlags.None);
        }
        /// <inheritdoc/>
        public void Write(byte[] buffer, int offset, int count)
        {
            if (_udpClient == null || !_isConnected) return;

            if (offset <= 0)
                _udpClient.Send(buffer, count);
            else
                _udpClient.Send(buffer.Skip(offset).ToArray<byte>(), count);
        }

        /// <inheritdoc/>
        public void ClearReadBuffer() { }
        /// <inheritdoc/>
        public void ClearWriteBuffer() { }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (_udpClient == null) return;

            Close();
            _udpClient.Dispose();
            _udpClient = null;
        }

    }
}
