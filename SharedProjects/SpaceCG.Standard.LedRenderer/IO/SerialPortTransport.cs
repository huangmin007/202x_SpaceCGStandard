using System;
using System.IO.Ports;
using System.Text.RegularExpressions;
using SpaceCG.Extensions;
using Trace = SpaceCG.Diagnostics.Trace;

namespace SpaceCG.IO
{
    /// <summary>
    /// 串口传输连接对象
    /// </summary>
    internal class SerialPortTransport : ITransportChannel
    {
        /// <summary>
        /// Windows 串口名称的正则表达式
        /// </summary>
        public static readonly Regex PortNameRegexForWindows = new Regex("^COM[0-9]{1,2}$", RegexOptions.IgnoreCase);

        /// <inheritdoc/>
        public object Tag { get; set; }

        /// <summary>
        /// <see cref="SerialPort"/> 对象
        /// </summary>
        private SerialPort _serialPort;

        /// <inheritdoc/>
        public TransportType Type => TransportType.SERIAL;

        /// <inheritdoc/>
        public string Name => $"{Type}_{_serialPort.PortName}_{_serialPort.BaudRate}";

        /// <inheritdoc/>
        public bool IsConnected => _serialPort == null ? false : _serialPort.IsOpen;

        /// <inheritdoc/>
        public int Available => _serialPort == null ? 0 : _serialPort.BytesToRead;

        /// <inheritdoc/>
        public int ReadTimeout
        {
            get { return _serialPort.ReadTimeout; }
            set { _serialPort.ReadTimeout = value; }
        }

        /// <inheritdoc/>
        public int WriteTimeout
        {
            get { return _serialPort.WriteTimeout; }
            set { _serialPort.WriteTimeout = value; }
        }

        private readonly string PortName = string.Empty;

        /// <summary>
        /// 串口传输连接对象
        /// </summary>
        /// <param name="portName"></param>
        /// <param name="baudRate"></param>
        /// <param name="parity"></param>
        /// <param name="dataBits"></param>
        /// <param name="stopBits"></param>
        public SerialPortTransport(string portName, int baudRate, int parity = 0, int dataBits = 8, int stopBits = 1)
        {
            if (string.IsNullOrWhiteSpace(portName))
                throw new ArgumentException("参数不能为空", nameof(portName));
            if (baudRate <= 0) throw new ArgumentException("波特率必须大于0");

            this.PortName = portName;

            _serialPort = new SerialPort();
            _serialPort.PortName = GetPortName(PortName);
            _serialPort.BaudRate = baudRate;
            _serialPort.DataBits = dataBits;
            _serialPort.Parity = (Parity)Enum.Parse(typeof(Parity), parity.ToString());
            _serialPort.StopBits = (StopBits)Enum.Parse(typeof(StopBits), stopBits.ToString());

            _serialPort.NewLine = "\r\n";
            _serialPort.ReadBufferSize = 4096 * 8;
            _serialPort.WriteBufferSize = 2048 * 64;
        }

        /// <inheritdoc/>
        public void Open()
        {
            if (_serialPort == null) return;

            try
            {
                if (!_serialPort.IsOpen)
                {
                    _serialPort.PortName = GetPortName(PortName);
                    if (string.IsNullOrWhiteSpace(_serialPort.PortName))
                    {
                        Trace.TraceWarning($"跟据 {PortName} 获取串口失败");
                        return;
                    }

                    _serialPort.Open();
                    _serialPort.DiscardInBuffer();
                    _serialPort.DiscardOutBuffer();
                }
            }
            catch(Exception ex)
            {
                Trace.TraceWarning($"({Name}) 建立连接失败：{ex.Message}");
                throw ex;
            }
        }

        /// <inheritdoc/>
        public void Close()
        {
            if (_serialPort == null) return;

            if (_serialPort.IsOpen)
                _serialPort.Close();                
        }

        /// <inheritdoc/>
        public int Read(byte[] buffer, int offset, int count)
        {
            if (_serialPort == null) return 0;

            return _serialPort.Read(buffer, offset, count);
        }
        /// <inheritdoc/>
        public void Write(byte[] buffer, int offset, int count)
        {
            if (_serialPort == null) return;

            _serialPort.Write(buffer, offset, count);
            _serialPort.BaseStream.Flush();
        }

        /// <inheritdoc/>
        public void ClearReadBuffer()
        {
            if (_serialPort.IsOpen) _serialPort.DiscardInBuffer();
        }
        /// <inheritdoc/>
        public void ClearWriteBuffer()
        {
            if (_serialPort.IsOpen) _serialPort.DiscardOutBuffer();
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (_serialPort == null) return;

            if (_serialPort.IsOpen) _serialPort.Close();
            _serialPort.Dispose();
            _serialPort = null;
        }

        /// <summary>
        /// 获取当前计算机的唯一的串行端口号
        /// </summary>
        /// <param name="searchPattern">查找匹配字符，不区分大小写。</param>
        /// <returns>返回唯一串行端口号名称（例如："COM3"，"COM14"），如果没有找到则返回空字符串；如果找到多个，则返回其中的第一个。</returns>
        public static string GetPortName(string searchPattern)
        {
            if (PortNameRegexForWindows.IsMatch(searchPattern)) return searchPattern;

            var ports = SystemExtensions.GetSerialDevices();
            foreach (var port in ports)
            {
                if (port.FriendlyName.Contains(searchPattern)) return port.PortName;
            }

            return searchPattern;
        }

    }
}
