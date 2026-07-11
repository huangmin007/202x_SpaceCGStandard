using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace SpaceCG.Net
{
    /// <summary>
    /// <see cref="TcpClient"/> 扩展方法。
    /// <para>提供非阻塞的连接状态检查，用于读写循环中的快速健康探测。</para>
    /// </summary>
    public static class TcpClientExtensions
    {
        /// <summary>
        /// 检查 <see cref="TcpClient"/> 是否处于已连接状态。
        /// </summary>
        /// <param name="tcpClient">要检查的 TCP 客户端。</param>
        /// <returns>如果连接正常返回 <c>true</c>；如果已断开或客户端为 <c>null</c> 则返回 <c>false</c>。</returns>
        public static bool IsConnected(this TcpClient tcpClient)
        {
            if (tcpClient == null || tcpClient.Client == null) return false;
            //return !((tcpClient.Client.Poll(1000, SelectMode.SelectRead) && (tcpClient.Client.Available == 0)) || !tcpClient.Client.Connected);
            try
            {
                // 0 微秒表示立即返回，不阻塞
                return !(tcpClient.Client.Poll(0, SelectMode.SelectRead) && tcpClient.Client.Available == 0) && tcpClient.Client.Connected;
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>
    /// RPC 客户端，用于向服务端发送远程调用请求并接收响应。
    /// <para>（当前版本为占位类，具体功能待后续实现。可参考 <see cref="RPCServer4X"/> 的 XML 消息格式自行构建客户端。）</para>
    /// </summary>
    public class RPCClient
    {
    }
}
