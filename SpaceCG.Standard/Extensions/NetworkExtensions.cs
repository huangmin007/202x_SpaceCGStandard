using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Collections.Generic;
using System.Linq;
using Trace = SpaceCG.Diagnostics.Trace;

namespace SpaceCG.Extensions
{
    /// <summary>
    /// Network Extensions
    /// </summary>
    public static partial class NetworkExtensions
    {
        /// <summary>
        /// 获取本机所有处于活动状态的真实 IPv4 地址。
        /// </summary>
        /// <returns>本机活动的 IPv4 地址集合</returns>
        public static IEnumerable<IPAddress> GetLocalIPv4Addresses()
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(ni => ni.OperationalStatus == OperationalStatus.Up &&
                             ni.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .SelectMany(ni => ni.GetIPProperties().UnicastAddresses)
                .Where(ua => ua.Address.AddressFamily == AddressFamily.InterNetwork)
                .Select(ua => ua.Address)
                .Distinct(); // 去除可能的重复项
        }

        /// <summary>
        /// 获取本机可用的网络地址集合（MAC 地址到 IPv4 地址列表的映射）。
        /// </summary>
        /// <returns>只读字典，键为 MAC 地址，值为该接口下的 IPv4 地址集合</returns>
        public static IReadOnlyDictionary<PhysicalAddress, IEnumerable<IPAddress>> GetLocalInterfaceAddresses()
        {
            var addresses = new Dictionary<PhysicalAddress, List<IPAddress>>();

            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                // 过滤：仅保留状态为 Up、非回环、支持 IPv4 的接口
                if (ni.OperationalStatus != OperationalStatus.Up ||
                    ni.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
                    !ni.Supports(NetworkInterfaceComponent.IPv4))
                {
                    continue;
                }

                // 提取该接口下所有的 IPv4 单播地址
                var ipv4Addresses = ni.GetIPProperties().UnicastAddresses
                    .Where(ua => ua.Address.AddressFamily == AddressFamily.InterNetwork)
                    .Select(ua => ua.Address)
                    .ToList();

                if (ipv4Addresses.Count == 0) continue;

                var mac = ni.GetPhysicalAddress();

                // 优化：使用 TryAdd 或合并逻辑，防止因重复 MAC 地址导致 ArgumentException 崩溃
                if (addresses.TryGetValue(mac, out var existingIps))
                {
                    existingIps.AddRange(ipv4Addresses);
                }
                else
                {
                    addresses[mac] = ipv4Addresses;
                }
            }

            // 转换为 IReadOnlyDictionary 返回，保证外部不可修改
            return addresses.ToDictionary(
                kvp => kvp.Key,
                kvp => (IEnumerable<IPAddress>)kvp.Value.AsReadOnly()
            );
        }

        /// <summary>
        /// 获取本机所有活动网络接口的 IPv4 子网掩码。
        /// </summary>
        /// <returns>子网掩码数组</returns>
        public static IEnumerable<IPAddress> GetSubnetMasks()
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(ni => ni.OperationalStatus == OperationalStatus.Up &&
                             ni.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .SelectMany(ni => ni.GetIPProperties().UnicastAddresses)
                .Where(ua => ua.Address.AddressFamily == AddressFamily.InterNetwork && ua.IPv4Mask != null)
                .Select(ua => ua.IPv4Mask)
                .Distinct();
        }

        /// <summary>
        /// 获取本机所有活动网络接口对应的 IPv4 广播地址。
        /// </summary>
        /// <returns>广播地址集合</returns>
        public static IEnumerable<IPAddress> GetBroadcastAddresses()
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(ni => ni.OperationalStatus == OperationalStatus.Up &&
                             ni.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .SelectMany(ni => ni.GetIPProperties().UnicastAddresses)
                .Where(ua => ua.Address.AddressFamily == AddressFamily.InterNetwork && ua.IPv4Mask != null)
                .Select(ua => CalculateBroadcastAddress(ua.Address, ua.IPv4Mask))
                .Distinct();
        }
        /// <summary>
        /// 核心辅助方法：根据 IP 地址和子网掩码计算广播地址。
        /// 公式：广播地址 = IP地址 | (~子网掩码)
        /// </summary>
        private static IPAddress CalculateBroadcastAddress(IPAddress ipAddress, IPAddress subnetMask)
        {
            byte[] ipBytes = ipAddress.GetAddressBytes();
            byte[] maskBytes = subnetMask.GetAddressBytes();
            byte[] broadcastBytes = new byte[ipBytes.Length];

            for (int i = 0; i < ipBytes.Length; i++)
            {
                // 按位取反子网掩码，然后与 IP 地址进行按位或运算
                broadcastBytes[i] = (byte)(ipBytes[i] | ~maskBytes[i]);
            }

            return new IPAddress(broadcastBytes);
        }

        /// <summary>
        /// 获取指定 IPv4 地址所在网络接口的广播地址。
        /// </summary>
        /// <param name="ipAddress">目标 IPv4 地址</param>
        /// <returns>计算得出的广播地址；若未找到匹配接口或参数无效，则返回 null</returns>
        public static IPAddress GetBroadcastAddress(IPAddress ipAddress)
        {
            if (ipAddress == null || ipAddress.AddressFamily != AddressFamily.InterNetwork) return null;

            // 遍历所有活动接口，寻找包含该 IP 的单播地址信息
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                    continue;

                var ua = ni.GetIPProperties().UnicastAddresses
                    .FirstOrDefault(u => u.Address.Equals(ipAddress));

                // 找到匹配的 IP，且子网掩码有效
                if (ua != null && ua.IPv4Mask != null)
                {
                    return CalculateBroadcastAddress(ua.Address, ua.IPv4Mask);
                }
            }

            return null; // 未找到该 IP 所属的有效网络接口
        }

        /// <summary>
        /// 获取指定 IPv4 地址字符串所在网络接口的广播地址。
        /// </summary>
        /// <param name="ipAddress">目标 IPv4 地址字符串</param>
        /// <returns>计算得出的广播地址；若解析失败或未找到，则返回 null</returns>
        public static IPAddress GetBroadcastAddress(string ipAddress)
        {
            if (string.IsNullOrWhiteSpace(ipAddress)) return null;

            if (IPAddress.TryParse(ipAddress, out IPAddress address) && address.AddressFamily == AddressFamily.InterNetwork)
            {
                return GetBroadcastAddress(address);
            }

            return null;
        }
    }
}