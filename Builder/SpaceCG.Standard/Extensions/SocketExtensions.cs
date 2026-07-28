using System;
using System.IO;
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
                return !(socket.Poll(0, SelectMode.SelectRead) && socket.Available == 0) && socket.Connected;
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
        /// 带自动重连的异步连接与数据读取辅助方法。将连接管理、协议解析、断线重连整合为一个异步循环。
        /// <para>工作流程：</para>
        /// <list type="number">
        ///     <item><b>连接检查</b>：检测到连接断开（或从未连接）时，清理旧的 <see cref="TcpClient"/> 实例并等待 1 秒。</item>
        ///     <item><b>重新连接</b>：创建新的 <see cref="TcpClient"/>，尝试异步连接目标地址。成功后继承原客户端的缓冲区大小配置，并通过 <paramref name="onConnected"/> 回调通知调用方。</item>
        ///     <item><b>数据读取</b>：连接成功后进入 <see cref="ProtocolParser.ReadFromAsync"/> 循环，持续从网络流中读取数据并通过 <paramref name="protocolParser"/> 解析数据包。</item>
        ///     <item><b>循环重试</b>：当数据读取因连接断开或其他异常退出时，回到步骤 1 检查连接状态并重连。</item>
        /// </list>
        /// <para>连接失败时的重试间隔固定为 3 秒。</para>
        /// <para>
        /// <b>取消机制</b>：通过 <see cref="CancellationToken.Register(System.Action)"/> 注册回调，
        /// 当 <paramref name="cancellationToken"/> 被取消时自动关闭当前连接的 <see cref="TcpClient"/>，
        /// 从而中断阻塞的 <see cref="Stream.ReadAsync(byte[], int, int, CancellationToken)"/> 操作。
        /// 遇到 <see cref="OperationCanceledException"/> 或 <see cref="ObjectDisposedException"/> 时终止整个循环。
        /// </para>
        /// <para>
        /// <b>注意</b>：由于 <see cref="TcpClient"/> 是引用类型，<paramref name="client"/> 参数本身不会被修改，
        /// 调用方必须在 <paramref name="onConnected"/> 回调中更新外部持有的引用。
        /// 此外，建议始终传入由 <see cref="CancellationTokenSource"/> 创建的可取消令牌；
        /// <see cref="CancellationToken.None"/> 无法被取消，只能通过 <see cref="ProtocolParser.Dispose(bool)"/> 退出循环。
        /// </para>
        /// </summary>
        /// <param name="client">
        /// 初始的 TCP 客户端实例，用于获取 <see cref="TcpClient.SendBufferSize"/> 和
        /// <see cref="TcpClient.ReceiveBufferSize"/> 配置。不能为 <c>null</c>。
        /// </param>
        /// <param name="address">远程服务端 IP 地址或主机名。</param>
        /// <param name="port">远程服务端端口号，范围 1-65535。</param>
        /// <param name="protocolParser">
        /// 协议解析器实例，用于解析从网络流中读取的字节数据。
        /// 内部调用 <see cref="ProtocolParser.ReadFromAsync"/> 进入持续读取循环，
        /// 解析出的数据包通过 <see cref="ProtocolParser.PacketReceived"/> 事件抛出。
        /// 当 <paramref name="protocolParser"/> 被 Dispose 时（<see cref="ProtocolParser.IsDisposed"/> 为 <c>true</c>），循环自动退出。
        /// </param>
        /// <param name="onConnected">
        /// 每次连接成功时的回调，参数为新创建的 <see cref="TcpClient"/> 实例。
        /// 调用方应在此回调中更新对客户端实例的引用，以便在外部进行发送操作。
        /// </param>
        /// <param name="cancellationToken">
        /// 用于取消整个重连循环的令牌。取消时内部会关闭当前连接的 <see cref="TcpClient"/>
        /// 以中断阻塞的 <see cref="Stream.ReadAsync(byte[], int, int, CancellationToken)"/>，随后循环退出。
        /// <para>传入 <see cref="CancellationToken.None"/> 将无法被取消。</para>
        /// </param>
        /// <returns>一个表示异步操作的任务。任务完成意味着循环已终止（取消、Dispose 或不可恢复的错误）。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="client"/> 为 <c>null</c>，或 <paramref name="address"/> 为 <c>null</c> 或空白字符串时抛出。 </exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="port"/> 不在 1-65535 范围内时抛出。</exception>
        /// <example>
        /// <code>
        /// using SpaceCG.Extensions;
        /// using SpaceCG.Generic;
        ///
        /// // 1. 创建协议解析器（以 CRLF 结尾的文本协议为例）
        /// var parser = new FooterProtocolParser(new byte[] { 0x0D, 0x0A });
        ///
        /// // 2. 订阅数据包事件，在回调中处理解析出的完整数据包
        /// parser.PacketReceived += (sender, e) =>
        /// {
        ///     var message = Encoding.UTF8.GetString(e.Packet.Array, e.Packet.Offset, e.Packet.Count);
        ///     Console.WriteLine($"收到消息: {message}");
        /// };
        ///
        /// // 3. 调用方持有客户端引用，通过回调更新
        /// TcpClient client = new TcpClient();
        /// var cts = new CancellationTokenSource();
        ///
        /// // 4. 在后台任务中启动自动重连循环
        /// var connectTask = Task.Run(async () =>
        /// {
        ///     await client.ConnectWithRetryAsync(
        ///         "127.0.0.1",
        ///         2001,
        ///         parser,
        ///         newClient =>
        ///         {
        ///             client = newClient;  // 更新外部引用
        ///             Console.WriteLine($"已连接到 {newClient.Client.RemoteEndPoint}");
        ///         },
        ///         cts.Token
        ///     );
        /// });
        ///
        /// // 5. 在需要退出时取消令牌
        /// cts.Cancel();
        /// await connectTask;
        ///
        /// // 6. 清理资源
        /// client.Dispose();
        /// parser.Dispose();
        /// </code>
        /// </example>
        public static async Task ConnectWithRetryAsync(this TcpClient client, string address, int port, ProtocolParser protocolParser, Action<TcpClient> onConnected, CancellationToken cancellationToken)
        {
            if (client == null) throw new ArgumentNullException(nameof(client));
            if (port < 1 || port > 65535) throw new ArgumentOutOfRangeException(nameof(port));
            if (string.IsNullOrWhiteSpace(address)) throw new ArgumentNullException(nameof(address));

            var tcpClient = client;
            var delay = TimeSpan.FromSeconds(3.0);

            var sendBufferSize = tcpClient.SendBufferSize;
            var receiveBufferSize = tcpClient.ReceiveBufferSize;

            // 注册取消时关闭连接
            var cancelReg = cancellationToken.Register(() =>
            {
                try
                {
                    tcpClient?.Close();
                    tcpClient?.Dispose();
                }
                catch (Exception) { }
            });

            while (!cancellationToken.IsCancellationRequested && !protocolParser.IsDisposed)
            {
                // 连接断开，或没有连接
                if (!tcpClient.IsConnected())
                {
                    try
                    {
                        tcpClient?.Close();
                        tcpClient?.Dispose();
                    }
                    finally { tcpClient = null; }

                    // 等待 1 秒
                    try { await Task.Delay(1000, cancellationToken).ConfigureAwait(false); }
                    catch (Exception ex) when (ex is OperationCanceledException || ex is ObjectDisposedException) { break; }

                    if (cancellationToken.IsCancellationRequested || protocolParser.IsDisposed) break;

                    try
                    {
                        // 重新连接处理
                        var newClient = new TcpClient();
                        await newClient.ConnectAsync(address, port).ConfigureAwait(false);

                        newClient.SendBufferSize = sendBufferSize;
                        newClient.ReceiveBufferSize = receiveBufferSize;

                        tcpClient = newClient;
                        Trace.TraceInformation($"客户端连接成功 {tcpClient.Client.LocalEndPoint} -> {tcpClient.Client.RemoteEndPoint}");

                        try { onConnected?.Invoke(newClient); }
                        catch (Exception ex) { Trace.TraceWarning($"onConnected 回调异常: {ex.Message}"); }
                    }
                    catch (Exception ex) when (ex is OperationCanceledException || ex is ObjectDisposedException) { break; }
                    catch (Exception ex)
                    {
                        if (cancellationToken.IsCancellationRequested || protocolParser.IsDisposed) break;

                        // 等待下次重连处理
                        Trace.TraceWarning($"客户端连接失败: {ex.Message}，重试中 .....");
                        try
                        {
                            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                            continue;
                        }
                        catch (Exception ex0) when (ex0 is OperationCanceledException || ex0 is ObjectDisposedException) { break; }
                    }
                }

                if (cancellationToken.IsCancellationRequested || protocolParser.IsDisposed) break;

                // 数据接收处理
                try { await protocolParser.ReadFromAsync(tcpClient.GetStream(), cancellationToken).ConfigureAwait(false); }
                catch (Exception ex) when (ex is OperationCanceledException || ex is ObjectDisposedException) { break; }
                catch (Exception) { if (cancellationToken.IsCancellationRequested || protocolParser.IsDisposed) break; }
            }

            cancelReg.Dispose();
            // 确保退出时清理最后的连接
            try
            {
                tcpClient?.Close();
                tcpClient?.Dispose();
            }
            finally { tcpClient = null; }
        }

        /// <summary>
        /// UDP 异步数据接收辅助方法。将本地绑定、协议解析、异常重建整合为一个异步循环。
        /// <para>工作流程：</para>
        /// <list type="number">
        ///     <item><b>绑定检查</b>：检测到 <see cref="UdpClient"/> 未绑定（<see cref="UdpClient.Client"/> 为 <c>null</c>）时，
        ///         清理旧实例，等待 1 秒后尝试重新绑定。</item>
        ///     <item><b>重新绑定</b>：创建新的 <see cref="UdpClient"/>，绑定到指定本地地址和端口。
        ///         成功后通过 <paramref name="onConnected"/> 回调通知调用方。</item>
        ///     <item><b>数据接收</b>：绑定成功后进入 <see cref="ProtocolParser.ReadFromAsync"/> 循环，
        ///         持续从网络流中读取数据并通过 <paramref name="protocolParser"/> 解析数据包。</item>
        ///     <item><b>循环重试</b>：当数据接收异常退出时，强制关闭当前实例，回到步骤 1 触发重绑。</item>
        /// </list>
        /// <para>
        /// <b>注意</b>：UDP 是无连接协议，不存在 TCP 意义上的"连接/断连"。
        /// 本方法中的"重试"仅指本机 Socket 异常时重建 <see cref="UdpClient"/> 并重新绑定。
        /// </para>
        /// <para>绑定失败时的重试间隔固定为 3 秒。</para>
        /// <para>
        /// <b>取消机制</b>：通过 <see cref="CancellationToken.Register(System.Action)"/> 注册回调，
        /// 当 <paramref name="cancellationToken"/> 被取消时自动关闭当前 <see cref="UdpClient"/>，
        /// 从而中断阻塞的 <see cref="Stream.ReadAsync(byte[],int,int,CancellationToken)"/> 操作。
        /// 遇到 <see cref="OperationCanceledException"/> 或 <see cref="ObjectDisposedException"/> 时终止整个循环。
        /// </para>
        /// <para>
        /// <b>退出机制</b>：除了取消令牌外，调用 <see cref="ProtocolParser.Dispose()"/> 也会触发循环退出。
        /// </para>
        /// <para>
        /// <b>注意</b>：由于 <see cref="UdpClient"/> 是引用类型，<paramref name="client"/> 参数本身不会被修改，
        /// 调用方必须在 <paramref name="onConnected"/> 回调中更新外部持有的引用。
        /// 此外，建议始终传入由 <see cref="CancellationTokenSource"/> 创建的可取消令牌；
        /// <see cref="CancellationToken.None"/> 无法被取消，只能通过 <see cref="ProtocolParser.Dispose(bool)"/> 退出循环。
        /// </para>
        /// </summary>
        /// <param name="client">
        /// 初始的 UDP 客户端实例。如果已绑定则直接使用，否则内部会重新创建并绑定。不能为 <c>null</c>。
        /// </param>
        /// <param name="localAddress">要绑定的本地 IP 地址。传入 <c>"0.0.0.0"</c> 监听所有网络接口。</param>
        /// <param name="localPort">要绑定的本地端口号，范围 1-65535。</param>
        /// <param name="protocolParser">
        /// 协议解析器实例，用于解析从网络流中读取的字节数据。
        /// 内部调用 <see cref="ProtocolParser.ReadFromAsync"/> 进入持续读取循环，
        /// 解析出的数据包通过 <see cref="ProtocolParser.PacketReceived"/> 事件抛出。
        /// 当 <paramref name="protocolParser"/> 被 Dispose 时（<see cref="ProtocolParser.IsDisposed"/> 为 <c>true</c>），循环自动退出。
        /// </param>
        /// <param name="onConnected">
        /// 每次绑定成功时的回调，参数为新创建的 <see cref="UdpClient"/> 实例。
        /// 调用方应在此回调中更新对客户端实例的引用。
        /// </param>
        /// <param name="cancellationToken">
        /// 用于取消整个接收循环的令牌。取消时内部会关闭当前 <see cref="UdpClient"/>
        /// 以中断阻塞的读取操作，随后循环退出。
        /// <para>传入 <see cref="CancellationToken.None"/> 将无法被取消。</para>
        /// </param>
        /// <returns>一个表示异步操作的任务。任务完成意味着循环已终止。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="client"/> 为 <c>null</c>，或 <paramref name="localAddress"/> 为 <c>null</c> 或空白字符串时抛出。</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="localPort"/> 不在 1-65535 范围内时抛出。</exception>
        /// <example>
        /// <code>
        /// using SpaceCG.Extensions;
        /// using SpaceCG.Generic;
        ///
        /// // 1. 创建协议解析器（以 CRLF 结尾的文本协议为例）
        /// var parser = new FooterProtocolParser(new byte[] { 0x0D, 0x0A });
        ///
        /// // 2. 订阅数据包事件
        /// parser.PacketReceived += (sender, e) =>
        /// {
        ///     var message = Encoding.UTF8.GetString(e.Packet.Array, e.Packet.Offset, e.Packet.Count);
        ///     Console.WriteLine($"收到消息: {message}");
        /// };
        ///
        /// // 3. 调用方持有客户端引用，通过回调更新
        /// UdpClient client = new UdpClient();
        /// var cts = new CancellationTokenSource();
        ///
        /// // 4. 在后台任务中启动 UDP 接收循环
        /// var receiveTask = Task.Run(async () =>
        /// {
        ///     await client.ReceiveAsync(
        ///         "0.0.0.0",
        ///         2001,
        ///         parser,
        ///         newClient =>
        ///         {
        ///             client = newClient;  // 更新外部引用
        ///             Console.WriteLine($"UDP 已绑定到 {newClient.Client.LocalEndPoint}");
        ///         },
        ///         cts.Token
        ///     );
        /// });
        ///
        /// // 5. 在需要退出时取消令牌
        /// cts.Cancel();
        /// await receiveTask;
        ///
        /// // 6. 清理资源
        /// client.Dispose();
        /// parser.Dispose();
        /// </code>
        /// </example>
        public static async Task ReceiveAsync(this UdpClient client, string localAddress, int localPort, ProtocolParser protocolParser, Action<UdpClient> onConnected, CancellationToken cancellationToken)
        {
            if (client == null) throw new ArgumentNullException(nameof(client));
            if (localPort < 1 || localPort > 65535) throw new ArgumentOutOfRangeException(nameof(localPort));
            if (string.IsNullOrWhiteSpace(localAddress)) throw new ArgumentNullException(nameof(localAddress));

            var udpClient = client;
            var delay = TimeSpan.FromSeconds(3.0);

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

            while (!cancellationToken.IsCancellationRequested && !protocolParser.IsDisposed)
            {
                // 检查是否需要重新绑定
                if (udpClient == null || udpClient.Client == null)
                {
                    try
                    {
                        udpClient?.Close();
                        udpClient?.Dispose();
                    }
                    finally { udpClient = null; }

                    try { await Task.Delay(1000, cancellationToken).ConfigureAwait(false); }
                    catch (Exception ex) when (ex is OperationCanceledException || ex is ObjectDisposedException) { break; }

                    if (cancellationToken.IsCancellationRequested || protocolParser.IsDisposed) break;

                    try
                    {
                        var newClient = new UdpClient();
                        newClient.Client.Bind(new IPEndPoint(IPAddress.Parse(localAddress), localPort));

                        udpClient = newClient;
                        Trace.TraceInformation($"UDP 客户端绑定成功 {udpClient.Client.LocalEndPoint}");

                        try { onConnected?.Invoke(newClient); }
                        catch (Exception ex) { Trace.TraceWarning($"onConnected 回调异常: {ex.Message}"); }
                    }
                    catch (Exception ex) when (ex is OperationCanceledException || ex is ObjectDisposedException) { break; }
                    catch (Exception ex)
                    {
                        if (cancellationToken.IsCancellationRequested || protocolParser.IsDisposed) break;

                        Trace.TraceWarning($"UDP 客户端绑定失败: {ex.Message}，重试中 .....");
                        try
                        {
                            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                            continue;
                        }
                        catch (Exception ex0) when (ex0 is OperationCanceledException || ex0 is ObjectDisposedException) { break; }
                    }
                }

                if (cancellationToken.IsCancellationRequested || protocolParser.IsDisposed) break;

                // 数据接收处理
                try
                {
                    using (var stream = new NetworkStream(udpClient.Client, false))
                    {
                        await protocolParser.ReadFromAsync(stream, cancellationToken).ConfigureAwait(false);
                    }
                }
                catch (Exception ex) when (ex is OperationCanceledException || ex is ObjectDisposedException) { break; }
                catch (Exception) { if (cancellationToken.IsCancellationRequested || protocolParser.IsDisposed) break; }
            }

            cancelReg.Dispose();
            try
            {
                udpClient?.Close();
                udpClient?.Dispose();
            }
            finally { udpClient = null; }
        }

    }
}
