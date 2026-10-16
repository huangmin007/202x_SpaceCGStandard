using System;
using System.Diagnostics;
using System.IO.Ports;
using System.Threading;
using NModbus.IO;

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

        public int InfiniteTimeout => -1;

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

        public SerialPortAdapter(SerialPort serialPort)
        {
            _serialPort = serialPort;
            _serialPort.NewLine = "\r\n";

            _stopwatch = Stopwatch.StartNew();
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
