using System;
using System.Diagnostics;
using System.Linq;
using System.Net.Sockets;

namespace SpaceCG.IO
{
    /// <summary>
    /// UDP 客户传输连接对象
    /// </summary>
    internal class UdpClientTransport : ITransportChannel
    {
        private UdpClient _udpClient;

        /// <inheritdoc/>
        public object Tag { get; set; }

        private int _port;
        private string _hostname;
        private bool _isConnect = false;

        /// <inheritdoc/>
        public TransportType Type => TransportType.UDP;

        /// <inheritdoc/>
        public string Name => $"{Type}_{_hostname}_{_port}";

        /// <inheritdoc/>
        public bool IsConnected => _udpClient == null ? false : _isConnect;
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

        /// <inheritdoc/>
        public void Open()
        {
            if (_udpClient == null) return;
            try
            {
                _isConnect = true;
                _udpClient.Connect(_hostname, _port);
            }
            catch (Exception ex)
            {
                _isConnect = false;
                Trace.TraceWarning($"UDP({Name}) 建立连接失败：{ex.Message}");
            }
        }
        /// <inheritdoc/>
        public void Close()
        {
            if (_udpClient == null) return;

            _isConnect = false;
            _udpClient.Close();
        }

        /// <inheritdoc/>
        public int Read(byte[] buffer, int offset, int count)
        {
            if (_udpClient == null) return 0;

            if (buffer == null)
            {
                throw new ArgumentNullException("buffer");
            }
            if (offset < 0)
            {
                throw new ArgumentOutOfRangeException("offset", "Argument offset must be greater than or equal to 0.");
            }
            if (offset > buffer.Length)
            {
                throw new ArgumentOutOfRangeException("offset", "Argument offset cannot be greater than the length of buffer.");
            }
            if (count < 0)
            {
                throw new ArgumentOutOfRangeException("count", "Argument count must be greater than or equal to 0.");
            }
            if (count > buffer.Length - offset)
            {
                throw new ArgumentOutOfRangeException("count", "Argument count cannot be greater than the length of buffer minus offset.");
            }

            //EndPoint ipEndPoint = new IPEndPoint(IPAddress.Any, 0);
            //return this._udpClient.Client.ReceiveFrom(buffer, offset, count, SocketFlags.None, ref ipEndPoint);

            return this._udpClient.Client.Receive(buffer, offset, count, SocketFlags.None);
        }
        /// <inheritdoc/>
        public void Write(byte[] buffer, int offset, int count)
        {
            if (_udpClient == null) return;

            if (buffer == null)
            {
                throw new ArgumentNullException("buffer");
            }
            if (offset < 0)
            {
                throw new ArgumentOutOfRangeException("offset", "Argument offset must be greater than or equal to 0.");
            }
            if (offset > buffer.Length)
            {
                throw new ArgumentOutOfRangeException("offset", "Argument offset cannot be greater than the length of buffer.");
            }
            if (count < 0)
            {
                throw new ArgumentOutOfRangeException("count", "Argument count must be greater than or equal to 0.");
            }
            if (count > buffer.Length - offset)
            {
                throw new ArgumentOutOfRangeException("count", "Argument count cannot be greater than the length of buffer minus offset.");
            }

            if (offset <= 0)
                this._udpClient.Send(buffer, count);
            else
                this._udpClient.Send(buffer.Skip(offset).ToArray<byte>(), count);
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
