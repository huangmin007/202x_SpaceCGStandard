using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Trace = SpaceCG.Diagnostics.Trace;

namespace SpaceCG.Extensions
{
    /// <summary>
    /// <see cref="TcpClient"/> 扩展方法。
    /// <para>提供非阻塞的连接状态检查，用于读写循环中的快速健康探测。</para>
    /// </summary>
    public static partial class TcpClientExtensions
    {
        /// <summary>
        /// 检查 <see cref="TcpClient"/> 是否处于已连接状态。
        /// </summary>
        /// <param name="tcpClient">要检查的 TCP 客户端。</param>
        /// <returns>如果连接正常返回 <c>true</c>；如果已断开或客户端为 <c>null</c> 则返回 <c>false</c>。</returns>
        public static bool IsConnected(this TcpClient tcpClient)
        {
            if (tcpClient == null || tcpClient.Client == null) return false;

            try
            {
                return !(tcpClient.Client.Poll(0, SelectMode.SelectRead) && tcpClient.Client.Available == 0) && tcpClient.Client.Connected;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// 带自动重连的异步连接辅助方法。在连接断开后自动重建 TCP 连接，并通过回调将新实例通知调用方。
        /// <para>工作流程：</para>
        /// <list type="number">
        ///     <item>循环检查连接状态，已连接时等待 1 秒后再次检查。</item>
        ///     <item>检测到断开后，关闭并释放旧的 <see cref="TcpClient"/>，创建新实例尝试连接。</item>
        ///     <item>连接成功后通过 <paramref name="onConnected"/> 回调传递新实例，调用方应在回调中更新自身引用。</item>
        ///     <item>连接失败时等待 <paramref name="delay"/> 后重试，直到连接成功或取消。</item>
        /// </list>
        /// <para>新实例会继承原 <paramref name="tcpClient"/> 的 <see cref="TcpClient.SendBufferSize"/> 和 <see cref="TcpClient.ReceiveBufferSize"/>。</para>
        /// <para>注意：<paramref name="tcpClient"/> 参数本身不会被修改（引用类型限制），调用方必须在回调中更新外部持有的引用。</para>
        /// </summary>
        /// <param name="tcpClient">初始的 TCP 客户端实例，用于获取缓冲区大小配置。可以为 <c>null</c>。</param>
        /// <param name="address">远程服务端 IP 地址或主机名。</param>
        /// <param name="port">远程服务端端口号，范围 1-65535。</param>
        /// <param name="delay">连接失败后的重试等待间隔。设置为 <see cref="TimeSpan.Zero"/> 时立即重试。</param>
        /// <param name="onConnected">每次连接成功时的回调，参数为新创建的 <see cref="TcpClient"/> 实例。调用方应在此回调中更新对客户端实例的引用。</param>
        /// <param name="cancellationToken">用于取消重连循环的令牌。</param>
        /// <returns>一个表示异步操作的任务。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="tcpClient"/> 为 <c>null</c>，或 <paramref name="address"/> 为 <c>null</c> 或空白字符串时抛出。</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="port"/> 不在 1-65535 范围内时抛出。</exception>
        /// <example>
        /// <code>
        /// using SpaceCG.Extensions;
        ///
        /// // 调用方持有客户端引用，通过回调更新
        /// TcpClient client = new TcpClient();
        /// var cts = new CancellationTokenSource();
        ///
        /// await client.ConnectWithRetryAsync(
        ///     "127.0.0.1",
        ///     8888,
        ///     TimeSpan.FromSeconds(3),
        ///     newClient =>
        ///     {
        ///         client = newClient;                              // 更新外部引用
        ///         Console.WriteLine($"已连接到 {newClient.Client.RemoteEndPoint}");
        ///     },
        ///     cts.Token
        /// );
        ///
        /// // 使用 client 进行读写操作...
        /// // var stream = client.GetStream();
        /// // await stream.WriteAsync(data, 0, data.Length);
        ///
        /// // 取消重连循环
        /// cts.Cancel();
        /// </code>
        /// </example>
        public static async Task ConnectWithRetryAsync(this TcpClient tcpClient, string address, int port, TimeSpan delay, Action<TcpClient> onConnected, CancellationToken cancellationToken)
        {
            if (tcpClient == null) throw new ArgumentNullException(nameof(tcpClient));
            if (port < 1 || port > 65535) throw new ArgumentOutOfRangeException(nameof(port));
            if (string.IsNullOrWhiteSpace(address)) throw new ArgumentNullException(nameof(address));

            var _port = port;
            var _address = address;
            var _tcpClient = tcpClient;

            var _sendBufferSize = _tcpClient.SendBufferSize;
            var _receiveBufferSize = _tcpClient.ReceiveBufferSize;

            while (!cancellationToken.IsCancellationRequested)
            {
                var isConnected = _tcpClient.IsConnected();

                if (isConnected)
                {
                    await Task.Delay(1000, cancellationToken).ConfigureAwait(false);
                    continue;
                }
                else
                {
                    try
                    {
                        _tcpClient.Close();
                        _tcpClient.Dispose();
                    }
                    finally
                    {
                        _tcpClient = null;
                    }
                }

                if (cancellationToken.IsCancellationRequested) break;

                try
                {
                    var newClient = new TcpClient();
                    await newClient.ConnectAsync(_address, _port).ConfigureAwait(false);

                    newClient.SendBufferSize = _sendBufferSize;
                    newClient.ReceiveBufferSize = _receiveBufferSize;

                    _tcpClient = newClient;
                    onConnected?.Invoke(newClient);
                }
                catch (Exception ex) when (ex is OperationCanceledException || ex is ObjectDisposedException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Trace.TraceWarning($"客户端连接失败: {ex.Message}，重试中 .....");
                    if (delay > TimeSpan.Zero)
                    {
                        await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                    }
                    continue;
                }

                //await HandleServerSessionAsync(_tcpClient, cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
