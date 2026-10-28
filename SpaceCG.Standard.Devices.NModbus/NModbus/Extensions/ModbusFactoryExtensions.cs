using System;
using System.IO.Ports;
using NModbus;
using SpaceCG.NModbus.IO;

namespace SpaceCG.NModbus
{
    public static class ModbusFactoryExtensions
    {
        public static IModbusSerialMaster CreateRtuMaster(this IModbusFactory factory, SerialPort serialPort)
        {
            SerialPortAdapter serialPortAdapter = new SerialPortAdapter(serialPort);
            return factory.CreateRtuMaster(serialPortAdapter);
        }

        public static IModbusRtuTransport CreateRtuTransport(this IModbusFactory factory, SerialPort serialPort)
        {
            SerialPortAdapter serialPortAdapter = new SerialPortAdapter(serialPort);
            return factory.CreateRtuTransport(serialPortAdapter);
        }

        public static IModbusSlaveNetwork CreateRtuSlaveNetwork(this IModbusFactory factory, SerialPort serialPort)
        {
            SerialPortAdapter serialPortAdapter = new SerialPortAdapter(serialPort);
            return factory.CreateRtuSlaveNetwork(serialPortAdapter);
        }

        public static IModbusSerialMaster CreateAsciiMaster(this IModbusFactory factory, SerialPort serialPort)
        {
            SerialPortAdapter serialPortAdapter = new SerialPortAdapter(serialPort);
            return factory.CreateAsciiMaster(serialPortAdapter);
        }

        public static IModbusAsciiTransport CreateAsciiTransport(this IModbusFactory factory, SerialPort serialPort)
        {
            SerialPortAdapter serialPortAdapter = new SerialPortAdapter(serialPort);
            return factory.CreateAsciiTransport(serialPortAdapter);
        }

        public static IModbusSlaveNetwork CreateAsciiSlaveNetwork(this IModbusFactory factory, SerialPort serialPort)
        {
            SerialPortAdapter serialPortAdapter = new SerialPortAdapter(serialPort);
            return factory.CreateAsciiSlaveNetwork(serialPortAdapter);
        }

        /// <summary>
        /// Create Serial Port Rtu Master
        /// </summary>
        /// <param name="factory"></param>
        /// <param name="portName"></param>
        /// <param name="baudRate"></param>
        /// <param name="parity"></param>
        /// <param name="dataBits"></param>
        /// <param name="stopBits"></param>
        /// <returns></returns>
        public static IModbusSerialMaster CreateRtuMaster(this IModbusFactory factory, string portName, int baudRate, Parity parity = Parity.None, int dataBits = 8, StopBits stopBits = StopBits.One)
        {
            var serialPort = new SerialPort(portName, baudRate, parity, dataBits, stopBits);
            var serialPortAdapter = new SerialPortAdapter(serialPort);
            return factory.CreateRtuMaster(serialPortAdapter);
        }

        /// <summary>
        /// CreateIpMasterEx->TcpClientAdapterEx
        /// </summary>
        /// <param name="factory"></param>
        /// <param name="host"></param>
        /// <param name="port"></param>
        /// <returns></returns>
        public static IModbusMaster CreateIpMaster(this IModbusFactory factory, string host, int port)
        {
            TcpClientAdapterEx tcpClientAdapter = new TcpClientAdapterEx(host, port);
            return factory.CreateIpMaster(tcpClientAdapter);
        }
    }
}
