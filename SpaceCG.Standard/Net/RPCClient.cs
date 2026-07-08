using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace SpaceCG.Net
{
    public static class TcpClientExtensions
    {
        /// <summary>
        /// <see cref="TcpClient"/> 连接状态
        /// </summary>
        /// <param name="tcpClient"></param>
        /// <returns></returns>
        public static bool IsConnected(this TcpClient tcpClient)
        {
            if (tcpClient == null || tcpClient.Client == null) return false;
            return !((tcpClient.Client.Poll(1000, SelectMode.SelectRead) && (tcpClient.Client.Available == 0)) || !tcpClient.Client.Connected);
        }
    }

    public class RPCClient
    {
    }
}
