using System;
using System.Diagnostics;
using System.IO.Ports;
using System.Threading;
using System.Threading.Tasks;
using NModbus.IO;
using SpaceCG.Extensions;

namespace SpaceCG.NModbus.IO
{
    /// <summary>
    ///     Concrete Implementor - http://en.wikipedia.org/wiki/Bridge_Pattern
    /// </summary>
    public class SerialPortAdapter : IStreamResource, IDisposable
    {
        private const string NewLine = "\r\n";

        private Stopwatch _stopwatch;
        private SerialPort _serialPort;
        public int InfiniteTimeout => Timeout.Infinite;

        public int ReadTimeout
        {
            get
            {
                return _serialPort.ReadTimeout;
            }
            set
            {
                _serialPort.ReadTimeout = value;
            }
        }
        public int WriteTimeout
        {
            get
            {
                return _serialPort.WriteTimeout;
            }
            set
            {
                _serialPort.WriteTimeout = value;
            }
        }

        private Task _connectedTask;
        private CancellationTokenSource _cts;

        private readonly string _portName;

        public SerialPortAdapter(SerialPort serialPort)
        {
            _serialPort = serialPort;
            _serialPort.NewLine = "\r\n";

            _portName = serialPort.PortName;
            _stopwatch = Stopwatch.StartNew();

            serialPort.PortName = SerialPortExtensions.GetPortName(_portName);

            try
            {
                if (!serialPort.IsOpen) serialPort.Open();
            }
            catch { }

            _cts = new CancellationTokenSource();
            _connectedTask = Task.Factory.StartNew(EnsureConnected, this, _cts.Token);
        }
        private async Task EnsureConnected(object state)
        {
            var instance = (state as SerialPortAdapter);
            if (instance == null) return;

            var portName = instance._portName;
            var serialPort = instance._serialPort;
            var cancellationToken = instance._cts.Token;

            while (!cancellationToken.IsCancellationRequested)
            {
                if (serialPort == null) break;

                try
                {
                    if (serialPort.IsOpen)
                    {
                        await Task.Delay(100, cancellationToken);
                        continue;
                    }
                }
                catch (Exception ex)
                {
                    Trace.TraceWarning($"EnsureConnected: {ex.Message}");
                    break;
                }
                
                try
                {
                    // 等待 1 秒后，重新尝试打开串口
                    for (int i = 0; i < 10; i++)
                    {
                        if (serialPort == null || cancellationToken.IsCancellationRequested) break;
                        await Task.Delay(100, cancellationToken);
                    }

                    if (serialPort.IsOpen) continue;
                    if (cancellationToken.IsCancellationRequested) break;

                    serialPort.PortName = SerialPortExtensions.GetPortName(portName);
                    serialPort.Open();
                }
                catch (Exception ex)
                {
                    Trace.TraceWarning($"EnsureConnected Open Exception: {ex.Message}");
                }
            }
        }

        public void DiscardInBuffer()
        {
            _serialPort.DiscardInBuffer();
        }

        public int Read(byte[] buffer, int offset, int count)
        {
            //Trace.WriteLine($"Read Count:{count}");

            var sleepCount = 0;
            var beginMilliseconds = _stopwatch.ElapsedMilliseconds;
            var readTimeout = _serialPort.ReadTimeout <= 0 ? 3000 : _serialPort.ReadTimeout;

            while (true)
            {
                if (_stopwatch.ElapsedMilliseconds - beginMilliseconds > readTimeout)
                    throw new TimeoutException("Read Timeout");

                if (_serialPort.BytesToRead < count)
                {
                    sleepCount++;
                    Thread.Sleep(1);
                    continue;
                }

                //Trace.WriteLine($"SleepCount::{sleepCount}  {count}");
                return _serialPort.Read(buffer, offset, count);
            }
        }

        public void Write(byte[] buffer, int offset, int count)
        {
            _serialPort.Write(buffer, offset, count);
        }

        public void Dispose()
        {
            Dispose(true);
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
                    _connectedTask?.Wait(300);
                    _connectedTask?.Dispose();
                }
                finally { _connectedTask = null; }

                try { _cts?.Dispose(); }
                finally { _cts = null; }

                SerialPort serialPort = _serialPort;
                if (serialPort != null)
                {
                    serialPort.Dispose();
                }
                _serialPort = null;

                _stopwatch.Stop();
                _stopwatch = null;
            }
        }

    }
}
