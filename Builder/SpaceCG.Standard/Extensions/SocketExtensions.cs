using System;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using SpaceCG.Generic;
using Trace = SpaceCG.Diagnostics.Trace;

namespace SpaceCG.Extensions
{
    /// <summary>
    /// <see cref="TcpClient"/> 和 <see cref="UdpClient"/> 扩展方法。
    /// <para>提供 TCP 连接状态检查与自动重连、UDP 绑定与持续接收、以及协议解析整合等能力。</para>
    /// </summary>
    public static partial class SocketExtensions
    {
        /// <summary>
        /// 检查 <see cref="Socket"/> 是否处于已连接状态。
        /// </summary>
        /// <param name="socket"></param>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsConnected(this Socket socket)
        {
            if (socket == null) return false;

            try
            {
                if (!socket.Connected) return false;
                return !(socket.Poll(0, SelectMode.SelectRead) && socket.Available == 0);
            }
            catch (Exception) { return false; }
        }

        /// <summary>
        /// 检查 <see cref="TcpClient"/> 是否处于已连接状态。
        /// </summary>
        /// <param name="tcpClient">要检查的 TCP 客户端。</param>
        /// <returns>如果连接正常返回 <c>true</c>；如果已断开或客户端为 <c>null</c> 则返回 <c>false</c>。</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsConnected(this TcpClient tcpClient)
        {
            if (tcpClient == null || tcpClient.Client == null) return false;
            return tcpClient.Client.IsConnected();
        }

        /// <summary>
        /// TCP 自动重连方法。循环检查连接状态，断开时自动清理旧连接并重连。
        /// <para>连接正常时每 200 毫秒检查一次，连接断开后等待 3 秒后重试。注意：<paramref name="cancellationToken"/> 不能能空，或是默认值，否则无法取消自动重连。</para>
        /// </summary>
        /// <param name="tcpClient">初始 TCP 客户端实例。方法内部会替换此引用，调用方需在 onConnected 回调中更新外部引用。</param>
        /// <param name="address">远程 IP 地址或主机名。</param>
        /// <param name="port">远程端口号，1-65535。</param>
        /// <param name="onConnected">连接成功回调，参数为新创建的 TcpClient 实例。</param>
        /// <param name="cancellationToken">取消令牌，取消时退出循环。</param>
        /// <returns>循环终止时完成的任务。</returns>
        /// <exception cref="ArgumentNullException">tcpClient 为 null，或 address 为 null/空白时抛出。</exception>
        /// <exception cref="ArgumentOutOfRangeException">port 不在 1-65535 范围内时抛出。</exception>
        /// <example>
        /// <code>
        /// // 1. 启动自动重连（仅管理连接，不读取数据）
        /// var client = new TcpClient();
        /// var cts = new CancellationTokenSource();
        /// var task = client.ConnectAsync("127.0.0.1", 2001, newClient => client = newClient, cts.Token);
        ///
        /// // 2. 通过 client 发送数据（需先自行判断 IsConnected）
        /// if (client.IsConnected())
        /// {
        ///     var stream = client.GetStream();
        ///     // ... 发送数据 ...
        /// }
        ///
        /// // 3. 退出时取消并等待
        /// cts.Cancel();
        /// await task;
        /// client?.Dispose();
        /// </code>
        /// </example>
        public static async Task ConnectAsync(this TcpClient tcpClient, string address, int port, Action<TcpClient> onConnected, CancellationToken cancellationToken)
        {
            if (tcpClient == null) throw new ArgumentNullException(nameof(tcpClient));
            if (port < 1 || port > 65535) throw new ArgumentOutOfRangeException(nameof(port));
            if (string.IsNullOrWhiteSpace(address)) throw new ArgumentNullException(nameof(address));

            if (onConnected == null) throw new ArgumentNullException(nameof(onConnected), "不能为空，否则自动重连后会丢失新的连接对象。");
            if (!cancellationToken.CanBeCanceled) throw new ArgumentNullException(nameof(cancellationToken), "不能为空，否则无法取消自动重连。");

            var delay = TimeSpan.FromSeconds(3.0);
            var sendBufferSize = tcpClient.SendBufferSize;
            var receiveBufferSize = tcpClient.ReceiveBufferSize;

            while (!cancellationToken.IsCancellationRequested)
            {
                // 连接状态检查
                if (tcpClient.IsConnected())
                {
                    try { await Task.Delay(200, cancellationToken).ConfigureAwait(false); }
                    catch (Exception ex) when (ex is OperationCanceledException || ex is ObjectDisposedException) { break; }
                    catch (Exception) { if (cancellationToken.IsCancellationRequested) break; }
                    continue;
                }

                // 清理连接对象
                try { tcpClient?.Dispose(); }
                finally { tcpClient = null; }

                // 创建新的连接对象
                try
                {
                    var newClient = new TcpClient();
                    await newClient.ConnectAsync(address, port).ConfigureAwait(false);

                    tcpClient = newClient;
                    newClient.SendBufferSize = sendBufferSize;
                    newClient.ReceiveBufferSize = receiveBufferSize;
                    Trace.TraceInformation($"客户端连接成功 {newClient.Client.LocalEndPoint} -> {newClient.Client.RemoteEndPoint}");

                    try { onConnected.Invoke(newClient); }
                    catch (Exception ex) { Trace.TraceWarning($"onConnected 回调异常: {ex.Message}"); }
                }
                catch (Exception ex) when (ex is OperationCanceledException || ex is ObjectDisposedException) { break; }
                catch (Exception ex)
                {
                    if (cancellationToken.IsCancellationRequested) break;
                    Trace.TraceWarning($"客户端连接失败: {ex.Message}，重试中 .....");

                    try
                    {
                        await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                        continue;
                    }
                    catch (Exception ex0) when (ex0 is OperationCanceledException || ex0 is ObjectDisposedException) { break; }
                }
            }
        }

        /// <summary>
        /// 带自动重连的 TCP 异步接收方法。将连接管理、数据读取、协议解析整合为一个循环。
        /// <para><paramref name="cancellationToken"/> 不能能空，或是默认值，否则无法取消自动重连。</para>
        /// <para>如果不关心数据发送，则 <paramref name="onConnected"/> 可为空，为空时 <paramref name="cancellationToken"/> 会注册取消时关闭连接。</para>
        /// <para>工作流程：</para>
        /// <list type="number">
        /// <item>连接断开时清理旧 TcpClient，等待 3 秒后重连。</item>
        /// <item>创建新 TcpClient 连接目标地址，通过 onConnected 回调通知调用方。</item>
        /// <item>连接成功后循环读取网络流数据，写入 ProtocolParser 并解析。</item>
        /// <item>读取异常时回到步骤 1。</item>
        /// </list>
        /// <para>连接失败重试间隔 3 秒。通过 CancellationToken 取消时自动关闭当前连接并退出循环。</para>
        /// <para>注意：TcpClient 是引用类型，如果关注数据发送，则调用方需在 onConnected 回调中更新外部引用。</para>
        /// </summary>
        /// <param name="tcpClient">初始 TCP 客户端实例，用于获取缓冲区配置。不能为 null。</param>
        /// <param name="address">远程 IP 地址或主机名。</param>
        /// <param name="port">远程端口号，1-65535。</param>
        /// <param name="protocolParser">数据协议解析器。</param>
        /// <param name="onConnected">连接成功回调，参数为新 TcpClient。调用方应在此更新外部引用。</param>
        /// <param name="cancellationToken">取消令牌。取消时关闭当前连接并退出循环。</param>
        /// <returns>循环终止时完成的任务。</returns>
        /// <example>
        /// <code>
        /// // 1. 创建协议解析器（以 CRLF 结尾的文本协议为例）
        /// var parser = new FooterProtocolParser(new byte[] { 0x0D, 0x0A });
        ///
        /// // 2. 订阅数据帧事件
        /// parser.FrameReceived += (sender, e) =>
        /// {
        ///     var message = Encoding.UTF8.GetString(e.FramwView.Array, e.FramwView.Offset, e.FramwView.Count);
        ///     Console.WriteLine($"收到: {message}");
        /// };
        ///
        /// // 3. 启动自动重连接收循环
        /// TcpClient client = new TcpClient();
        /// _ = client.ConnectAsync("127.0.0.1", 2001);
        /// var cts = new CancellationTokenSource();
        ///
        /// var task = client.ReceiveParseAsync("127.0.0.1", 2001, parser, newClient => client = newClient, cts.Token);
        ///
        /// // 4. 退出时取消并等待
        /// cts.Cancel();
        /// await task;
        /// client?.Dispose();
        /// </code>
        /// </example>
        public static async Task ReceiveParseAsync(this TcpClient tcpClient, string address, int port, ProtocolParser protocolParser, Action<TcpClient> onConnected, CancellationToken cancellationToken)
        {
            if (tcpClient == null) throw new ArgumentNullException(nameof(tcpClient));
            if (protocolParser == null) throw new ArgumentNullException(nameof(protocolParser));

            if (port < 1 || port > 65535) throw new ArgumentOutOfRangeException(nameof(port));
            if (string.IsNullOrWhiteSpace(address)) throw new ArgumentNullException(nameof(address));
            if (!cancellationToken.CanBeCanceled) throw new ArgumentNullException(nameof(cancellationToken), "不能为空，否则无法取消重连并退出任务。");

            var delay = TimeSpan.FromSeconds(3.0);
            var sendBufferSize = tcpClient.SendBufferSize;
            var receiveBufferSize = tcpClient.ReceiveBufferSize;

            // 注册取消时关闭连接
            CancellationTokenRegistration cancelReg = default;
            if (onConnected == null)
            {
                cancelReg = cancellationToken.Register(() =>
                {
                    try
                    {
                        tcpClient?.Close();
                        tcpClient?.Dispose();
                    }
                    catch (Exception) { }
                });
            }

            while (!cancellationToken.IsCancellationRequested)
            {
                #region 连接断开，或没有连接，重新连接处理
                if (!tcpClient.IsConnected())
                {
                    try { tcpClient?.Dispose(); }
                    finally { tcpClient = null; }

                    try
                    {
                        var newClient = new TcpClient();
                        await newClient.ConnectAsync(address, port).ConfigureAwait(false);

                        tcpClient = newClient;
                        newClient.SendBufferSize = sendBufferSize;
                        newClient.ReceiveBufferSize = receiveBufferSize;
                        Trace.TraceInformation($"客户端连接成功 {newClient.Client.LocalEndPoint} -> {newClient.Client.RemoteEndPoint}");

                        try { onConnected?.Invoke(newClient); }
                        catch (Exception ex) { Trace.TraceWarning($"onConnected 回调异常: {ex.Message}"); }
                    }
                    catch (Exception ex) when (ex is OperationCanceledException || ex is ObjectDisposedException) { break; }
                    catch (Exception ex)
                    {
                        if (cancellationToken.IsCancellationRequested) break;
                        Trace.TraceWarning($"客户端连接失败: {ex.Message}，重试中 .....");
                        try
                        {
                            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                            continue;
                        }
                        catch (Exception ex0) when (ex0 is OperationCanceledException || ex0 is ObjectDisposedException) { break; }
                    }
                }
                #endregion

                if (cancellationToken.IsCancellationRequested) break;

                #region 数据接收处理                                    
                var clientStream = tcpClient.GetStream();
                while (!cancellationToken.IsCancellationRequested && tcpClient.IsConnected())
                {
                    try
                    {
                        var bytesRead = await clientStream.ReadAsync(protocolParser.Buffer, protocolParser.WritePosition, protocolParser.WritableBytes, cancellationToken).ConfigureAwait(false);
                        if (bytesRead == 0) break;

                        protocolParser.WritePosition += bytesRead;
                        protocolParser.ProcessBuffer();
                    }
                    catch (Exception ex) when (ex is OperationCanceledException || ex is ObjectDisposedException) { break; }
                    catch (Exception) { if (cancellationToken.IsCancellationRequested) break; }
                }                
                #endregion
            }

            if (onConnected == null)
            {
                cancelReg.Dispose();
            }
        }

        /// <inheritdoc cref="ReceiveParseAsync(TcpClient, string, int, ProtocolParser, Action{TcpClient}, CancellationToken)"/> 
        public static async Task ReceiveParseAsync(this TcpClient tcpClient, ProtocolParser protocolParser, Action<TcpClient> onConnected, CancellationToken cancellationToken)
        {
            var remoteEP = tcpClient.Client.RemoteEndPoint as IPEndPoint;
            await tcpClient.ReceiveParseAsync(remoteEP.Address.ToString(), remoteEP.Port, protocolParser, onConnected, cancellationToken).ConfigureAwait(false);
        }
        

        /// <summary>
        /// UDP 异步接收解析方法。在已绑定的 UdpClient 上循环接收数据报，写入 ProtocolParser 并解析。
        /// <para>调用前需自行绑定 UdpClient 到目标端口，UdpClient 生命周期由调用方管理。</para>
        /// </summary>
        /// <param name="udpClient">已绑定的 UDP 客户端实例。不能为 null 且必须已绑定。</param>
        /// <param name="protocolParser">数据协议解析器。</param>
        /// <param name="cancellationToken">取消令牌。取消时关闭 UdpClient 并退出循环。</param>
        /// <returns>循环终止时完成的任务。</returns>
        /// <exception cref="InvalidOperationException">UdpClient 未绑定时抛出。</exception>
        /// <example>
        /// <code>
        /// // 1. 创建并绑定 UdpClient
        /// var client = new UdpClient();
        /// client.Client.Bind(new IPEndPoint(IPAddress.Any, 2001));
        ///
        /// // 2. 创建协议解析器（固定长度 128 字节数据报）
        /// var parser = new FixedLengthProtocolParser(128);
        ///
        /// // 3. 订阅数据帧事件
        /// parser.FrameReceived += (sender, e) =>
        /// {
        ///     Console.WriteLine($"收到 {e.FramwView.Count} 字节");
        /// };
        ///
        /// // 4. 启动接收解析循环
        /// var cts = new CancellationTokenSource();
        /// var task = client.ReceiveParseAsync(parser, cts.Token);
        ///
        /// // 5. 退出时取消并等待
        /// cts.Cancel();
        /// await task;
        /// client?.Dispose();
        /// </code>
        /// </example>
        public static async Task ReceiveParseAsync(this UdpClient udpClient, ProtocolParser protocolParser, CancellationToken cancellationToken)
        {
            if (udpClient == null) throw new ArgumentNullException(nameof(udpClient));
            if (protocolParser == null) throw new ArgumentNullException(nameof(protocolParser));
            if (!udpClient.Client.IsBound) throw new InvalidOperationException("UdpClient 未绑定到本地端口");

            // 注册取消时关闭 UDP 客户端
            var cancelReg = cancellationToken.Register(() =>
            {
                try
                {
                    udpClient?.Close();
                    udpClient?.Dispose();
                }
                catch (Exception) { }
            });

            #region 数据接收处理            
            await Task.Run(() =>
            {
                EndPoint remoteEndPoint = new IPEndPoint(IPAddress.Any, 0);
                while (!cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        if (udpClient.Client == null || !udpClient.Client.IsBound) break;
                        var bytesRead = udpClient.Client.ReceiveFrom(protocolParser.Buffer, protocolParser.WritePosition, protocolParser.WritableBytes, SocketFlags.None, ref remoteEndPoint);
                        
                        protocolParser.WritePosition += bytesRead;
                        protocolParser.ProcessBuffer();
                    }
                    catch (Exception ex) when (ex is OperationCanceledException || ex is ObjectDisposedException) { break; }
                    catch (Exception) { if (cancellationToken.IsCancellationRequested) break; }
                }
            });
            #endregion

            cancelReg.Dispose();
        }

    }
}
