using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using SpaceCG.Extensions;

namespace SpaceCG
{
    public class RfidDevice : IDisposable
    {
        private SerialPort _serialPort;

        private Task _syncTask;
        private CancellationTokenSource _cts;

        public RfidDevice()
        {
            _serialPort = new SerialPort("COM3", 19200);
            _serialPort.ReadTimeout = 300;
            _serialPort.WriteTimeout = 300;
            //_serialPort.ReceivedBytesThreshold = 1;
            //_serialPort.DataReceived += SerialPort_DataReceived;
        }

        int count = 0;
        Stopwatch stopwatch = new Stopwatch();
        private void SerialPort_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            var buffer = new byte[9];
            var count = _serialPort.Read(buffer, 0, buffer.Length);

            count++;
            if (stopwatch.ElapsedMilliseconds >= 990)
            {
                Trace.WriteLine($"Count::{count},, {stopwatch.ElapsedMilliseconds},,,{string.Join(" ", buffer.Select(x => x.ToString("X2")))}");

                count = 0;
                stopwatch.Restart();
            }

            _serialPort.BaseStream.Write(ReadRFID, 0, ReadRFID.Length);
            _serialPort.BaseStream.Flush();
        }

        public void Open()
        {
            if (_serialPort.IsOpen) return;
            _serialPort.Open();
        }

        public void Close()
        {
            if (!_serialPort.IsOpen) return;
            _serialPort.Close();
        }

        public void StartSync()
        {
            //stopwatch.Restart();
            //_serialPort.BaseStream.Write(ReadRFID, 0, ReadRFID.Length);
            //_serialPort.BaseStream.Flush();

            if (_cts != null || _syncTask != null) return;
            _cts = new CancellationTokenSource();
            _syncTask = Task.Factory.StartNew(SyncThread, this, _cts.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }

        public void StopSync()
        {
            try { _cts?.Cancel(); }
            catch { }

            try
            {
                _syncTask?.Wait(300);
                _syncTask?.Dispose();
            }
            finally
            {
                _syncTask = null;
            }

            try { _cts?.Dispose(); }
            finally { _cts = null; }
        }
        private readonly byte[] ReadRFID = new byte[] { 0x02, 0x03, 0x00, 0x00, 0x00, 0x02, 0xC4, 0x38 };

        private void SyncThread(object state)
        {
            var device = state as RfidDevice;
            if (device == null) return;

            var serialPort = device._serialPort;
            var cancellationToken = device._cts.Token;

            var portName = serialPort.PortName;
            Trace.WriteLine($"PortName:{portName}");

            int count = 0;
            Stopwatch sw = new Stopwatch();
            sw.Restart();
            while (!cancellationToken.IsCancellationRequested)
            {
                while (!serialPort.IsOpen && !cancellationToken.IsCancellationRequested)
                {
                    // 等待 3 秒后尝试重连，分段 Sleep 避免 Stop/Close/Dispose 等待超时
                    for (int i = 0; i < 30; i++)
                    {
                        Thread.Sleep(100);
                        if (cancellationToken.IsCancellationRequested) break;
                    }

                    try
                    {
                        serialPort.Close();

                        Thread.Sleep(100);
                        if (cancellationToken.IsCancellationRequested) break;

                        serialPort.Open();
                    }
                    catch (Exception ex)
                    {
                        Trace.TraceWarning($"串口通道 ({portName}) 连接异常：{ex.Message}");
                    }

                    Thread.Sleep(100);
                }

                try
                {
                    var response = serialPort.Transceive(ReadRFID, 9);
                    count++;

                    if (sw.ElapsedMilliseconds >= 990)
                    {
                        Trace.WriteLine($"Count::{count},,{sw.ElapsedMilliseconds}");
                        count = 0;
                        sw.Restart();
                    }
                    //Trace.WriteLine($"Response [{count}] ({response.Length}) ... {string.Join(" ", response.Select(x => x.ToString("X2")))}");
                }
                catch (Exception ex)
                {
                    Trace.WriteLine($"Response Error: {ex.GetType().Name} {ex.Message}");
                }
            }
        }

        public void Dispose()
        {
            StopSync();
            Close();

            _serialPort?.Dispose();
            _serialPort = null;
        }
    }

}
