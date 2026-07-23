using System;
using Trace = SpaceCG.Diagnostics.Trace;

namespace SpaceCG.IO
{
    /// <summary>
    /// 传输通道对象
    /// </summary>
    public class TransportChannel:ITransportChannel
    {
        /// <inheritdoc/>
        public object Tag { get; set; }

        /// <summary>
        /// 传输通道对象
        /// </summary>
        private ITransportChannel _transportChannel;// { get; private set; }

        /// <summary>
        /// 传输出通道对象
        /// </summary>
        /// <param name="type"></param>
        /// <param name="transportParams"></param>
        public TransportChannel(TransportType type, params string[] transportParams)
        {
            if (transportParams.Length < 2)
                throw new ArgumentException(nameof(transportParams), $"参数 {transportParams} 格式错误，长度不得小于 2");

            if (type == TransportType.SERIAL)
            {
                if (transportParams.Length == 2)
                    _transportChannel = new SerialPortTransport(transportParams[0], int.Parse(transportParams[1]));
                else if (transportParams.Length == 3)
                    _transportChannel = new SerialPortTransport(transportParams[0], int.Parse(transportParams[1]), int.Parse(transportParams[2]));
                else if (transportParams.Length == 4)
                    _transportChannel = new SerialPortTransport(transportParams[0], int.Parse(transportParams[1]), int.Parse(transportParams[2]), int.Parse(transportParams[3]));
                else if (transportParams.Length == 5)
                    _transportChannel = new SerialPortTransport(transportParams[0], int.Parse(transportParams[1]), int.Parse(transportParams[2]), int.Parse(transportParams[3]), int.Parse(transportParams[4]));
            }
            else if (type == TransportType.TCP)
            {
                _transportChannel = new TcpClientTransport(transportParams[0], int.Parse(transportParams[1]));
            }
            else if (type == TransportType.UDP)
            {
                _transportChannel = new UdpClientTransport(transportParams[0], int.Parse(transportParams[1]));
            }
        }

        /// <inheritdoc/>
        public TransportType Type => _transportChannel.Type;
        /// <inheritdoc/>
        public string Name => _transportChannel?.Name;
        /// <inheritdoc/>
        public bool IsConnected => _transportChannel?.IsConnected ?? false;
        /// <inheritdoc/>
        public int Available => _transportChannel?.Available ?? 0;
        /// <inheritdoc/>
        public int ReadTimeout { get => _transportChannel.ReadTimeout; set => _transportChannel.ReadTimeout = value; }
        /// <inheritdoc/>
        public int WriteTimeout { get => _transportChannel.WriteTimeout; set => _transportChannel.WriteTimeout = value; }


        /// <inheritdoc/>
        public void Open()
        {
            _transportChannel?.Open();
        }
        /// <inheritdoc/>
        public void Close()
        {
            _transportChannel?.Close();
        }

        /// <inheritdoc/>
        public int Read(byte[] buffer, int offset, int count)
        {
            return _transportChannel?.Read(buffer, offset, count) ?? 0;
        }
        /// <inheritdoc/>
        public void Write(byte[] buffer, int offset, int count)
        {
            _transportChannel?.Write(buffer, offset, count);
        }

        /// <inheritdoc/>
        public void ClearReadBuffer()
        {
            _transportChannel?.ClearReadBuffer();
        }
        /// <inheritdoc/>
        public void ClearWriteBuffer()
        {
            _transportChannel?.ClearWriteBuffer();
        }
        
        /// <inheritdoc/>
        public void Dispose()
        {
            _transportChannel?.Dispose();
            _transportChannel = null;
        }

    }
}
