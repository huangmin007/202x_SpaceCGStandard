using System;
using System.IO.Ports;
using System.Threading;
using System.Threading.Tasks;
using SpaceCG.Generic;

namespace SpaceCG.Extensions
{
    /// <summary>
    /// 串口扩展方法
    /// </summary>
    public static partial class SocketExtensions
    {
        /// <summary>
        /// 串口异步接收解析方法。循环从串口读取数据写入 ProtocolParser 并触发帧解析。
        /// <para>串口未打开时自动尝试打开，无数据时等待 10ms 后重试，未打开时等待 3s 后重试。</para>
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
                    #region 等待串口就绪
                    try
                    {
                        if (!serialPort.IsOpen || serialPort.BytesToRead <= 0)
                        {
                            var delay = !serialPort.IsOpen ? 3000 : 10;
                            await Task.Delay(delay).ConfigureAwait(false);

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
                        var bytesRead = serialPort.Read(protocolParser.Buffer, protocolParser.WritePosition, protocolParser.WritableBytes);
                        
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
    }
}
