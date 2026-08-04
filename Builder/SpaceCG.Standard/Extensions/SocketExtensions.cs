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

#if false
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
#endif
    }
}
