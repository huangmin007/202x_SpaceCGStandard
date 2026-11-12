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
    internal sealed class SerialPortTransport : ITransportChannel
    {
        /// <summary>
        /// Windows 串口名称的正则表达式
        /// </summary>
        public static readonly Regex PortNameRegexForWindows = new Regex("^COM[0-9]{1,2}$", RegexOptions.IgnoreCase);

        /// <inheritdoc/>
        public object Tag { get; set; }
        
        /// <inheritdoc/>
        public ChannelType Type => ChannelType.Serial;
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

        /// <summary>
        /// <see cref="SerialPort"/> 对象
        /// </summary>
        private SerialPort _serialPort;
        private readonly string _portName = string.Empty;

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

            this._portName = portName;
            var actualPortName = SerialPortExtensions.GetPortName(_portName);

            _serialPort = new SerialPort();
            if (!string.IsNullOrWhiteSpace(actualPortName))
            {
                _serialPort.PortName = actualPortName;
            }
            _serialPort.BaudRate = baudRate;
            _serialPort.Parity = (Parity)Enum.Parse(typeof(Parity), parity.ToString());
            _serialPort.DataBits = dataBits;
            _serialPort.StopBits = (StopBits)Enum.Parse(typeof(StopBits), stopBits.ToString());

            _serialPort.NewLine = "\r\n";
            _serialPort.ReadBufferSize = 4096 * 8;
            _serialPort.WriteBufferSize = 2048 * 64;
        }

        /// <summary>
        /// 串口传输连接对象
        /// </summary>
        /// <param name="arguments">串口配置参数，依次为：串口名称、波特率、校验位、数据位、停止位</param>
        /// <exception cref="ArgumentException"></exception>
        public SerialPortTransport(params string[] arguments)
        {
            if (arguments == null || arguments.Length == 0) 
                throw new ArgumentException("参数不能为空", nameof(arguments));
            if (arguments.Length < 2)
                throw new ArgumentException("参数个数不能小于2，应包括串口名称和波特率", nameof(arguments));

            if (!int.TryParse(arguments[1], out var baudRate)) 
                throw new ArgumentException("波特率必须为整数", nameof(arguments));

            var parity = Parity.None;
            var dataBits = 8;
            var stopBits = StopBits.One;
            if (arguments.Length >= 3 && Enum.TryParse(arguments[2], out parity)) { }
            if (arguments.Length >= 4 && int.TryParse(arguments[3], out dataBits)) { }
            if (arguments.Length >= 5 && Enum.TryParse(arguments[4], out stopBits)) { }

            this._portName = arguments[0];
            var actualPortName = SerialPortExtensions.GetPortName(_portName);

            _serialPort = new SerialPort();
            if (!string.IsNullOrWhiteSpace(actualPortName))
            {
                _serialPort.PortName = actualPortName;
            }
            _serialPort.BaudRate = baudRate;
            _serialPort.Parity = parity;
            _serialPort.DataBits = dataBits;
            _serialPort.StopBits = stopBits;

            _serialPort.NewLine = "\r\n";
            _serialPort.ReadBufferSize = 4096 * 16;
            _serialPort.WriteBufferSize = 2048 * 64;
        }

        /// <inheritdoc/>
        public void Open()
        {
            if (_serialPort == null) return;
            if (_serialPort.IsOpen) return;

            if (SerialPort.GetPortNames().Length == 0)
                throw new Exception("没有找到可用的串口设备");

            var portName = SerialPortExtensions.GetPortName(_portName);
            if (string.IsNullOrWhiteSpace(portName))
                throw new Exception($"跟据指定名称 {_portName} 获取串口失败");

            _serialPort.PortName = portName;
            _serialPort.Open();
            _serialPort.DiscardInBuffer();
            _serialPort.DiscardOutBuffer();
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
            if (_serialPort == null || !_serialPort.IsOpen) return 0;

            return _serialPort.Read(buffer, offset, count);
        }

        /// <inheritdoc/>
        public void Write(byte[] buffer, int offset, int count)
        {
            if (_serialPort == null || !_serialPort.IsOpen) return;

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

    }
}
