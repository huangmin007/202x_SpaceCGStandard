using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;
using Trace = SpaceCG.Diagnostics.Trace;

namespace SpaceCG.Extensions
{
    #region NativeMethods
    /// <summary>
    /// Windows SetupAPI P/Invoke 声明。
    /// <para>集中管理所有原生函数导入、结构体和常量定义。</para>
    /// </summary>
    [SuppressUnmanagedCodeSecurity]
    internal static partial class NativeMethods
    {
        /// <summary>
        /// 端口类设备 GUID（GUID_DEVCLASS_PORTS）。
        /// <para>用于 SetupDiGetClassDevs 按设备类枚举，而非按接口枚举。</para>
        /// </summary>
        internal static readonly Guid GUID_DEVCLASS_PORTS = new Guid("4D36E978-E325-11CE-BFC1-08002BE10318");

        /// <summary>仅获取当前存在的设备。</summary>
        internal const int DIGCF_PRESENT = 0x00000002;
        /// <summary>设备注册表属性 ID，包含设备的友好名称的 REG_SZ 字符串。</summary>
        internal const uint SPDRP_FRIENDLYNAME = 0x0000000C;

        [StructLayout(LayoutKind.Sequential)]
        internal struct SP_DEVINFO_DATA
        {
            public uint cbSize;
            public Guid ClassGuid;
            public uint DevInst;
            public IntPtr Reserved;
        }

        #region P/Invoke 函数声明
        /// <summary>
        /// 返回设备信息集的句柄，其中包含本地计算机请求的设备信息元素。
        /// <para>参考：https://learn.microsoft.com/zh-cn/windows/win32/api/setupapi/nf-setupapi-setupdigetclassdevsw </para>
        /// </summary>
        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        internal static extern IntPtr SetupDiGetClassDevs([In] ref Guid classGuid, [MarshalAs(UnmanagedType.LPWStr)] string enumerator, IntPtr hwndParent, uint flags);

        /// <summary>
        /// 枚举设备信息集中的设备信息元素。
        /// <para>参考：https://learn.microsoft.com/zh-cn/windows/win32/api/setupapi/nf-setupapi-setupdienumdeviceinfo</para>
        /// </summary>
        /// <param name="DeviceInfoSet">SetupDiGetClassDevs 返回的设备信息集句柄。</param>
        /// <param name="MemberIndex">从 0 开始的索引，每次调用应递增。</param>
        /// <param name="DeviceInfoData">输出：填充的设备信息数据（调用前需设置 cbSize）。</param>
        /// <returns>成功返回 true，枚举完毕或失败返回 false。</returns>
        [DllImport("setupapi.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetupDiEnumDeviceInfo(IntPtr DeviceInfoSet, uint MemberIndex, ref SP_DEVINFO_DATA DeviceInfoData);

        /// <summary>
        /// 检索指定的即插即用设备属性。
        /// </summary>
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern bool SetupDiGetDeviceRegistryProperty(IntPtr deviceInfoSet, ref SP_DEVINFO_DATA deviceInfoData, uint property, out uint propertyRegDataType, byte[] propertyBuffer, uint propertyBufferSize, out uint requiredSize);

        /// <summary>
        /// 删除设备信息集并释放所有相关内存。
        /// </summary>
        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);
        #endregion
    }
    #endregion

    public static partial class SerialPortExtensions
    {        
        /// <summary>
        /// Windows 串口名称的正则表达式
        /// </summary>
        public static readonly Regex PortNameRegexForWindows = new Regex("^COM[0-9]{1,3}$", RegexOptions.IgnoreCase);

        #region 核心枚举引擎/辅助解析方法
        /// <summary>
        /// INVALID_HANDLE_VALUE 常量值（-1），用于校验 SetupAPI 返回的无效句柄。
        /// </summary>
        internal static readonly IntPtr InvalidHandle = new IntPtr(-1);

        /// <summary>
        /// 通用设备信息枚举引擎，封装 SetupAPI 的 SetupDiEnumDeviceInfo 标准调用流程。
        /// <para>适用于通过设备安装类 GUID 枚举的设备类型：Ports、Net、Bluetooth 等。</para>
        /// 因为 SetupDiEnumDeviceInfo 不遍历接口层，仅枚举设备节点。</para>
        /// <para>内部自动处理句柄生命周期（try-finally + SetupDiDestroyDeviceInfoList）。</para>
        /// </summary>
        /// <typeparam name="T">返回的设备信息类型，必须继承自 <see cref="DeviceInfo"/>。</typeparam>
        /// <param name="classGuid">设备安装类 GUID。</param>
        /// <param name="selector">
        /// 工厂函数，从设备信息集句柄和 SP_DEVINFO_DATA 构建目标对象。
        /// 返回 null 表示跳过当前设备。
        /// </param>
        /// <returns>设备信息列表，枚举失败或设备为空时返回空列表。</returns>
        internal static IReadOnlyList<T> EnumerateDeviceInfos<T>(Guid classGuid, Func<IntPtr, NativeMethods.SP_DEVINFO_DATA, T> selector)
        {
            IntPtr hDevInfoSet = NativeMethods.SetupDiGetClassDevs(ref classGuid, null, IntPtr.Zero, (uint)NativeMethods.DIGCF_PRESENT);

            if (hDevInfoSet == IntPtr.Zero || hDevInfoSet == InvalidHandle)
                return Array.Empty<T>();

            var devices = new List<T>(16);

            try
            {
                uint index = 0;
                var devInfo = new NativeMethods.SP_DEVINFO_DATA();
                devInfo.cbSize = (uint)Marshal.SizeOf(typeof(NativeMethods.SP_DEVINFO_DATA));

                while (NativeMethods.SetupDiEnumDeviceInfo(hDevInfoSet, index, ref devInfo))
                {
                    index++;
                    T device = selector(hDevInfoSet, devInfo);
                    if (device != null)
                    {
                        devices.Add(device);
                    }
                }
            }
            catch (Exception ex)
            {
                Trace.TraceWarning($"枚举设备信息数据失败 (GUID: {classGuid})：{ex.Message}");
                return Array.Empty<T>();
            }
            finally
            {
                NativeMethods.SetupDiDestroyDeviceInfoList(hDevInfoSet);
            }

            return devices;
        }

        /// <summary>
        /// 从 SetupAPI 获取指定设备属性字符串。
        /// <para>内部自动处理缓冲区不足的情况，并兼容 REG_SZ 和 REG_MULTI_SZ 类型。</para>
        /// </summary>
        /// <param name="hDevInfoSet">设备信息集句柄。</param>
        /// <param name="devInfoData">设备信息数据。</param>
        /// <param name="property">属性 ID（SPDRP_*）。</param>
        /// <returns>属性字符串；若属性不存在或获取失败返回 null。</returns>
        internal static string GetDevicePropertyString(IntPtr hDevInfoSet, ref NativeMethods.SP_DEVINFO_DATA devInfoData, uint property)
        {
            byte[] buffer = new byte[512];
            bool success = NativeMethods.SetupDiGetDeviceRegistryProperty(hDevInfoSet, ref devInfoData, property, out uint regType, buffer, (uint)buffer.Length, out uint requiredSize);

            // 缓冲区不足时按 requiredSize 重新分配
            if (!success && requiredSize > 0)
            {
                buffer = new byte[requiredSize];
                success = NativeMethods.SetupDiGetDeviceRegistryProperty(hDevInfoSet, ref devInfoData, property, out regType, buffer, (uint)buffer.Length, out requiredSize);
            }

            if (!success || requiredSize == 0) return null;

            // 属性数据统一按 Unicode 解码（SetupAPI 在 Windows 上返回 UTF-16）
            string result;
            int length = (int)(requiredSize / 2);
            try
            {
                result = Encoding.Unicode.GetString(buffer, 0, length * 2);
            }
            catch (Exception)
            {
                return null;
            }

            // REG_MULTI_SZ 可能包含多个以 '\0' 分隔的字符串，取第一个；REG_SZ 末尾有一个 '\0'
            int nullIndex = result.IndexOf('\0');
            if (nullIndex >= 0)
                result = result.Substring(0, nullIndex);

            return result;
        }

        /// <summary>
        /// 从友好名称中提取串口号（例如从 "USB-SERIAL CH340 (COM3)" 中提取 "COM3"）。
        /// </summary>
        /// <param name="friendlyName">设备友好名称。</param>
        /// <returns>串口号（如 "COM3"）；若无法提取返回 null。</returns>
        internal static string ExtractPortNameFromFriendlyName(string friendlyName)
        {
            if (string.IsNullOrEmpty(friendlyName))
                return string.Empty;

            int com = friendlyName.LastIndexOf("COM", StringComparison.OrdinalIgnoreCase);
            if (com < 0) return string.Empty;
            int end = com + 3;

            while (end < friendlyName.Length && char.IsDigit(friendlyName[end]))
            {
                end++;
            }

            return friendlyName.Substring(com, end - com);
        }
        #endregion

        /// <summary>
        /// 获取当前计算机上所有串口设备的友好名称列表。
        /// </summary>
        /// <returns>
        /// 串口设备的友好名称集合（如 "USB-SERIAL CH340 (COM3)"、"COM4"）。
        /// 若枚举失败或无串口设备，返回空集合。
        /// </returns>
        public static IEnumerable<string> GetPortFriendlyNames() => EnumerateDeviceInfos(NativeMethods.GUID_DEVCLASS_PORTS, (hDevInfoSet, devInfo) =>
        {
            return GetDevicePropertyString(hDevInfoSet, ref devInfo, NativeMethods.SPDRP_FRIENDLYNAME);
        });

        /// <summary>
        /// 获取当前计算机上与搜索模式匹配的串行端口号。
        /// <para>若 searchPattern 本身已是 "COM" + 数字的格式（如 "COM3"、"COM14"），直接返回。</para>
        /// <para>否则通过 <see cref="GetPortFriendlyNames"/> 枚举所有串口设备的 FriendlyName 包含 searchPattern 的设备。</para>
        /// <para>匹配不区分大小写。若找到多个匹配设备，返回第一个。若未找到任何匹配，返回 <see cref="string.Empty"/>。</para>
        /// </summary>
        /// <param name="searchPattern"> 查找匹配模式，如 "CH340"、"USB Serial" 或直接 "COM3"。 若传入 "COM" + 数字格式，无需枚举直接返回。</param>
        /// <returns>匹配的串口号名称（例如 "COM3"、"COM14"），若未匹配到则返回 <see cref="string.Empty"/>。</returns>
        public static string GetPortName(string searchPattern)
        {
            if (string.IsNullOrWhiteSpace(searchPattern)) return string.Empty;
            if (PortNameRegexForWindows.IsMatch(searchPattern)) return searchPattern;

            var friendlyNames = GetPortFriendlyNames();
            foreach (var friendlyName in friendlyNames)
            {
                if (string.IsNullOrWhiteSpace(friendlyName)) continue;
                if (friendlyName.IndexOf(searchPattern, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return ExtractPortNameFromFriendlyName(friendlyName);
                }
            }

            return string.Empty;
        }

    }
}