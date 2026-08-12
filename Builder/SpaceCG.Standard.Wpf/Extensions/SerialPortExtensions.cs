using System;
using System.IO;
using System.IO.Ports;
using System.Threading;
using System.Threading.Tasks;
using SpaceCG.Generic;

namespace SpaceCG.Extensions
{
    /// <summary>
    /// 串口扩展方法
    /// </summary>
    public static partial class SerialPortExtensions
    {
        /// <summary>
        /// 串口异步接收解析方法。循环从串口读取数据写入 ProtocolParser 并触发帧解析。
        /// <para>串口未打开时自动尝试打开，无数据时等待 1ms 后重试，未打开时等待 3000ms 后重试。</para>
        /// </summary>
        /// <param name="serialPort">串口实例。调用前需配置好波特率等参数，方法内仅负责 Open/Close。</param>
        /// <param name="protocolParser">协议解析器。数据写入 Buffer 后调用 ProcessBuffer 解析，匹配的帧通过 FrameReceived 事件抛出。</param>
        /// <param name="cancellationToken">取消令牌。取消时关闭串口并退出循环。</param>
        /// <returns>循环终止时完成的任务。</returns>
        /// <exception cref="ArgumentNullException">serialPort 或 protocolParser 为 null 时抛出。</exception>
        /// <example>
        /// <code>
        /// using System.IO.Ports;
        /// using System.Threading;
        /// using SpaceCG.Extensions;
        /// using SpaceCG.Generic;
        ///
        /// // 1. 配置串口
        /// var serialPort = new SerialPort("COM3", 115200);
        ///
        /// // 2. 创建协议解析器（以 CRLF 结尾的文本协议为例）
        /// var parser = new FooterProtocolParser(new byte[] { 0x0D, 0x0A });
        ///
        /// // 3. 订阅数据帧事件
        /// parser.FrameReceived += (sender, e) =>
        /// {
        ///     var text = Encoding.UTF8.GetString(e.FrameView.Array, e.FrameView.Offset, e.FrameView.Count);
        ///     Console.WriteLine($"收到: {text}");
        /// };
        ///
        /// // 4. 启动接收解析循环
        /// var cts = new CancellationTokenSource();
        /// var task = serialPort.ReceiveParseAsync(parser, cts.Token);
        ///
        /// // 5. 退出时取消并等待
        /// cts.Cancel();
        /// await task;
        /// serialPort.Dispose();
        /// </code>
        /// </example>
        public static async Task ReceiveParseAsync(this SerialPort serialPort, ProtocolParser protocolParser, CancellationToken cancellationToken)
        {
            if (serialPort == null) throw new ArgumentNullException(nameof(serialPort));
            if (protocolParser == null) throw new ArgumentNullException(nameof(protocolParser));

            // 注册取消时关闭连接
            var cancelReg = cancellationToken.Register(() =>
            {
                try
                {
                    serialPort?.Close();
                    serialPort?.Dispose();
                }
                catch (Exception) { }
            });

            try { if (!serialPort.IsOpen) serialPort.Open(); }
            catch (Exception) { }

            await Task.Run(async () =>
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    #region 等待串口 & 数据就绪
                    try
                    {
                        if (!serialPort.IsOpen || serialPort.BytesToRead <= 0)
                        {
                            var delay = !serialPort.IsOpen ? 3000 : 1;
                            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);

                            if (!serialPort.IsOpen) serialPort.Open();
                            continue;
                        }
                    }
                    catch (Exception ex) when (ex is OperationCanceledException || ex is ObjectDisposedException) { break; }
                    catch (Exception) { if (cancellationToken.IsCancellationRequested) break; }
                    #endregion

                    #region 读取数据并解析
                    try
                    {
                        var bytesRead = serialPort.BaseStream.Read(protocolParser.Buffer, protocolParser.WritePosition, protocolParser.WritableBytes);
                        
                        protocolParser.WritePosition += bytesRead;
                        protocolParser.ProcessBuffer();
                    }
                    catch (Exception ex) when (ex is OperationCanceledException || ex is ObjectDisposedException) { break; }
                    catch (Exception) { if (cancellationToken.IsCancellationRequested) break; }
                    #endregion
                }
            });

            cancelReg.Dispose();
            try
            {
                serialPort?.Close();
                serialPort?.Dispose();
            }
            catch (Exception) { }
        }

        #region Transceive
        /// <summary>
        /// 向串口发送数据，并阻塞等待接收指定长度的响应数据。此方法是同步（阻塞）调用，所有异常均向上层抛出。
        /// </summary>
        /// <param name="serialPort">已打开的串口实例。</param>
        /// <param name="data">待发送的数据缓冲区。</param>
        /// <param name="offset"><paramref name="data"/> 中开始发送的字节偏移量。</param>
        /// <param name="length">从 <paramref name="data"/> 中发送的字节数。</param>
        /// <param name="responseLength">期望接收的响应数据字节数。</param>
        /// <returns>接收到的完整响应数据，长度等于 <paramref name="responseLength"/>。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="serialPort"/> 为 <c>null</c>。</exception>
        /// <exception cref="InvalidOperationException">串口未打开。</exception>
        /// <exception cref="TimeoutException">在 <see cref="SerialPort.ReadTimeout"/> 时间内未收到完整响应。</exception>
        /// <exception cref="IOException">串口写入或读取时发生 I/O 错误。</exception>
        /// <remarks> 调用前会先清空接收缓冲区（<see cref="SerialPort.DiscardInBuffer"/>），以确保收到的响应数据对应当前发送的命令。超时时间由 <see cref="SerialPort.ReadTimeout"/> 决定，默认为 300ms。
        /// </remarks>
        public static byte[] Transceive(this SerialPort serialPort, byte[] data, int offset, int length, int responseLength)
        {
            if (serialPort == null) throw new ArgumentNullException(nameof(serialPort));
            if (!serialPort.IsOpen) throw new InvalidOperationException("串口未打开");
            if (data == null || data.Length == 0) throw new ArgumentNullException(nameof(data));

            serialPort.DiscardInBuffer();
            serialPort.BaseStream.Write(data, offset, length);
            serialPort.BaseStream.Flush();

            var received = 0;
            var buffer = new byte[responseLength];

            // 获取系统启动的 ms
            var beginTime = Environment.TickCount;
            var readTimeout = serialPort.ReadTimeout > 0 ? serialPort.ReadTimeout : 300;

            while (true)
            {
                if (Environment.TickCount - beginTime >= readTimeout)
                {
                    throw new TimeoutException($"串口通道 ({serialPort.PortName}) 读取超时");
                }

                if (serialPort.BytesToRead <= 0)
                {
                    Thread.Sleep(1);
                    continue;
                }

                var bytesToRead = serialPort.BaseStream.Read(buffer, received, responseLength - received);
                received += bytesToRead;
                if (received == responseLength) return buffer;
            }
        }
        /// <summary>
        /// 向串口发送 <paramref name="data"/> 的全部内容，并阻塞等待接收指定长度的响应数据。
        /// </summary>
        /// <param name="serialPort">已打开的串口实例。</param>
        /// <param name="data">待发送的数据缓冲区。</param>
        /// <param name="responseLength">期望接收的响应数据字节数。</param>
        /// <returns>接收到的完整响应数据，长度等于 <paramref name="responseLength"/>。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="data"/> 为 <c>null</c>。</exception>
        /// <inheritdoc cref="Transceive(SerialPort, byte[], int, int, int)"/>
        public static byte[] Transceive(this SerialPort serialPort, byte[] data, int responseLength) => serialPort.Transceive(data, 0, data.Length, responseLength);
        #endregion

        #region TransceiveAsync
        /// <summary>
        /// 异步向串口发送数据，并等待接收指定长度的响应数据。<br />
        /// 注意：在 .NET Framework 4.8 下，串口底层不支持真正的异步 I/O，此方法通过 <see cref="SerialPort.BytesToRead"/> 预检 + 异步轮询实现，避免 ReadAsync 在无数据时永久阻塞。
        /// </summary>
        /// <param name="serialPort">已打开的串口实例。</param>
        /// <param name="data">待发送的数据缓冲区。</param>
        /// <param name="offset"><paramref name="data"/> 中开始发送的字节偏移量。</param>
        /// <param name="sendLength">从 <paramref name="data"/> 中发送的字节数。</param>
        /// <param name="responseLength">期望接收的响应数据字节数。</param>
        /// <param name="cancellationToken">用于取消操作的取消令牌。</param>
        /// <returns>接收到的完整响应数据。</returns>
        /// <exception cref="OperationCanceledException">操作被取消。</exception>
        /// <inheritdoc cref="Transceive(SerialPort, byte[], int, int, int)"/>
        public static async Task<byte[]> TransceiveAsync(this SerialPort serialPort, byte[] data, int offset, int sendLength, int responseLength, CancellationToken cancellationToken)
        {
            if (serialPort == null) throw new ArgumentNullException(nameof(serialPort));
            if (!serialPort.IsOpen) throw new InvalidOperationException("串口未打开");
            if (data == null || data.Length == 0) throw new ArgumentNullException(nameof(data));

            // 清空接收缓冲区，发送命令
            serialPort.DiscardInBuffer();
            await serialPort.BaseStream.WriteAsync(data, offset, sendLength, cancellationToken).ConfigureAwait(false);
            await serialPort.BaseStream.FlushAsync(cancellationToken).ConfigureAwait(false);

            var received = 0;
            var buffer = new byte[responseLength];

            var beginTime = Environment.TickCount;
            var readTimeout = serialPort.ReadTimeout > 0 ? serialPort.ReadTimeout : 300;

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (Environment.TickCount - beginTime >= readTimeout)
                {
                    throw new TimeoutException($"串口通道 ({serialPort.PortName}) 读取超时：已接收 {received}/{responseLength} 字节");
                }

                if (serialPort.BytesToRead <= 0)
                {
                    // 无数据可读，异步等待 1ms 后重试（避免同步 Sleep 阻塞线程）
                    await Task.Delay(1, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                // 确认有数据后才调用 ReadAsync，避免永久阻塞
                var bytesToRead = await serialPort.BaseStream.ReadAsync(buffer, received, responseLength - received, cancellationToken).ConfigureAwait(false);

                if (bytesToRead == 0)
                    throw new IOException($"串口通道 ({serialPort.PortName}) 连接已关闭");

                received += bytesToRead;
                if (received == responseLength) return buffer;
            }
        }
        /// <summary>
        /// 异步向串口发送 <paramref name="data"/> 的全部内容，并等待接收指定长度的响应数据。
        /// </summary>
        /// <inheritdoc cref="TransceiveAsync(SerialPort, byte[], int, int, int, CancellationToken)"/>
        public static Task<byte[]> TransceiveAsync(this SerialPort serialPort, byte[] data, int responseLength, CancellationToken cancellationToken)
            => serialPort.TransceiveAsync(data, 0, data.Length, responseLength, cancellationToken);        
        #endregion
    }
}
