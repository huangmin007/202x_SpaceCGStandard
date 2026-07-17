using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Text;

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
            catch
            {
                return false;
            }
        }
    }
}
