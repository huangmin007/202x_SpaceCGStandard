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
        #region 设备接口 GUID（readonly，按 ref 传递给 P/Invoke）
        /// <summary>
        /// 端口类设备 GUID（GUID_DEVCLASS_PORTS）。
        /// <para>用于 SetupDiGetClassDevs 按设备类枚举，而非按接口枚举。</para>
        /// </summary>
        public static readonly Guid GUID_DEVCLASS_PORTS = new Guid("4D36E978-E325-11CE-BFC1-08002BE10318");
        #endregion

        #region SetupAPI 标志位
        /// <summary>仅获取当前存在的设备。</summary>
        internal const int DIGCF_PRESENT = 0x00000002;
        /// <summary>返回接口类设备。</summary>
        internal const int DIGCF_DEVICEINTERFACE = 0x00000010;

        // ===== 设备注册表属性 ID =====
        /// <summary>包含设备硬件 ID 列表的 REG_MULTI_SZ 字符串。</summary>
        internal const uint SPDRP_HARDWAREID = 0x00000001;
        /// <summary>包含设备的友好名称的 REG_SZ 字符串。</summary>
        internal const uint SPDRP_FRIENDLYNAME = 0x0000000C;
        /// <summary>包含设备位置信息的字符串。</summary>
        internal const uint SPDRP_LOCATION_INFORMATION = 0x0000000D;
        #endregion

        #region SetupAPI 结构体定义
        [StructLayout(LayoutKind.Sequential)]
        internal struct SP_DEVICE_INTERFACE_DATA
        {
            public uint cbSize;
            public Guid InterfaceClassGuid;
            public uint Flags;
            public IntPtr Reserved;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct SP_DEVINFO_DATA
        {
            public uint cbSize;
            public Guid ClassGuid;
            public uint DevInst;
            public IntPtr Reserved;
        }
        #endregion

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

        /// <summary>
        /// 获取指定设备实例的设备 ID 字符串。
        /// <para>参考：https://learn.microsoft.com/zh-cn/windows/win32/api/cfgmgr32/nf-cfgmgr32-cm_get_device_idw </para>
        /// </summary>
        /// <param name="dnDevInst">设备实例句柄（由 SetupAPI 提供的 DevInst 值）。</param>
        /// <param name="buffer">接收设备 ID 字符串的缓冲区。</param>
        /// <param name="bufferLen">缓冲区大小（以字符数为单位）。</param>
        /// <param name="ulFlags">标志位（通常为 0）。</param>
        /// <returns>成功返回 CR_SUCCESS(0)，失败返回 CR_* 错误码。</returns>
        [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
        internal static extern int CM_Get_Device_ID(uint dnDevInst, StringBuilder buffer, int bufferLen, int ulFlags);
        #endregion
    }
    #endregion


    #region 数据模型
    /// <summary>
    /// Windows 设备基础信息（抽象基类）。
    /// <para>包含 SetupAPI/CfgMgr32 可轻量获取的公共属性，不依赖 CreateFile/DeviceIoControl。</para>
    /// </summary>
    public class DeviceInfo
    {
        /// <summary>
        /// 设备友好名称（设备管理器显示的名称）。
        /// <para>来源：SetupDiGetDeviceRegistryProperty + SPDRP_FRIENDLYNAME。</para>
        /// </summary>
        public string FriendlyName { get; internal set; }

        /// <summary>
        /// 设备路径，可用于 CreateFile 打开设备。
        /// <para>来源：SetupDiGetDeviceInterfaceDetail 返回的 SP_DEVICE_INTERFACE_DETAIL_DATA.DevicePath。</para>
        /// </summary>
        public string DevicePath { get; internal set; }

        /// <summary>
        /// 硬件 ID（REG_MULTI_SZ 类型，取第一个字符串）。
        /// <para>来源：SetupDiGetDeviceRegistryProperty + SPDRP_HARDWAREID。</para>
        /// <para>USB 设备格式如 "USB\VID_1A86&amp;PID_7523"，磁盘格式如 "SCSI\DiskST1000DM010-2EP102"。</para>
        /// </summary>
        public string HardwareId { get; internal set; }

        /// <summary>
        /// Windows 设备实例 ID。
        /// <para>来源：CM_Get_Device_ID（cfgmgr32.dll）。</para>
        /// <para>格式如 "USB\VID_1A86&amp;PID_7523\5&amp;2C8A7B6F&amp;0&amp;3"。</para>
        /// </summary>
        public string InstanceId { get; internal set; }

        /// <summary>设备接口类的 GUID</summary>
        public Guid ClassGuid { get; internal set; }

        /// <summary> Display Name </summary> 
        public virtual string DisplayName => FriendlyName ?? DevicePath ?? InstanceId;

        /// <inheritdoc /> 
        public override string ToString() =>
            $"FriendlyName:{FriendlyName}  HardwareId:{HardwareId}  InstanceId:{InstanceId}  DevicePath:{DevicePath}  ({ClassGuid})";
    }    
    /// <summary>
    /// 串口设备信息。
    /// <para>继承自 <see cref="UsbDeviceInfo"/>（因为现代串口设备多为 USB 转串口芯片）。</para>
    /// <para>对于主板原生串口（非 USB），Vid/Pid/SerialNumber 将为 null。</para>
    /// <para>所有属性均可在枚举阶段轻量填充，不依赖设备 I/O。</para>
    /// </summary>
    public sealed class SerialDeviceInfo : DeviceInfo
    {
        /// <summary>
        /// 串口名称（例如 "COM3"、"COM10"）。
        /// <para>来源：优先从 SPDRP_PORTNAME 获取，若为空则从 FriendlyName 解析。</para>
        /// </summary>
        public string PortName { get; internal set; }

        /// <inheritdoc/>
        public override string ToString() => $"{base.ToString()} PortName:{PortName}";
    }
    #endregion


    /// <summary>
    /// Windows 设备信息查询辅助类。
    /// <para>使用 SetupAPI 枚举系统中的串口、USB、磁盘、卷等即插即用设备。</para>
    /// <para>线程安全：此类为静态工具类，不维护可变状态，可在多线程环境中并发调用。</para>
    /// </summary>
    internal static partial class SystemExtensions
    {
        /// <summary>
        /// INVALID_HANDLE_VALUE 常量值（-1），用于校验 SetupAPI 返回的无效句柄。
        /// </summary>
        internal static readonly IntPtr InvalidHandle = new IntPtr(-1);

        /// <summary>
        /// SP_DEVICE_INTERFACE_DETAIL_DATA 结构体的 cbSize 值。
        /// <para>x86: 6（4 字节 cbSize + 2 字节 DevicePath 首字符对齐）。</para>
        /// <para>x64: 8（4 字节 cbSize + 4 字节对齐填充）。</para>
        /// </summary>
        internal static readonly int DetailDataCbSize = IntPtr.Size == 8 ? 8 : 6;

        public static readonly Regex PortNameRegex = new Regex("^COM[0-9]{1,3}$", RegexOptions.IgnoreCase);


        #region 核心枚举引擎 (复用逻辑)
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
        #endregion

        /// <summary>
        /// 枚举系统中所有可用的串口设备信息。
        /// <para>基于 GUID_DEVCLASS_PORTS 设备安装类枚举，通过 SetupDiEnumDeviceInfo 而非 SetupDiEnumDeviceInterfaces。</para>
        /// <para>注意：此方案无法获取 DevicePath（SetupDiEnumDeviceInfo 不遍历接口层），DevicePath 始终为 null。</para>
        /// </summary>
        /// <returns>串口设备信息列表，若无设备返回空列表。</returns>
        public static IReadOnlyList<SerialDeviceInfo> GetSerialDevices() => EnumerateDeviceInfos(NativeMethods.GUID_DEVCLASS_PORTS, (hDevInfoSet, devInfo) =>
        {
            string instanceId = GetDeviceInstanceId(devInfo.DevInst);
            string hardwareId = GetDevicePropertyString(hDevInfoSet, ref devInfo, NativeMethods.SPDRP_HARDWAREID);
            string friendlyName = GetDevicePropertyString(hDevInfoSet, ref devInfo, NativeMethods.SPDRP_FRIENDLYNAME);

            string portName = string.Empty;
            if (!string.IsNullOrEmpty(friendlyName))
            {
                string parsed = ExtractPortNameFromFriendlyName(friendlyName);
                if (!string.IsNullOrEmpty(parsed)) portName = parsed;
            }

            return new SerialDeviceInfo
            {
                PortName = portName,
                HardwareId = hardwareId,
                InstanceId = instanceId,
                FriendlyName = friendlyName,
                ClassGuid = devInfo.ClassGuid,
            };
        });

        #region 辅助解析方法        
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
        /// 通过 CM_Get_Device_ID 获取指定设备实例的完整设备 ID 字符串。
        /// </summary>
        /// <param name="devInst">设备实例句柄（DevInst 值）。</param>
        /// <returns>设备 ID 字符串；若调用失败返回 null。</returns>
        internal static string GetDeviceInstanceId(uint devInst)
        {
            var sb = new StringBuilder(512);
            int ret = NativeMethods.CM_Get_Device_ID(devInst, sb, sb.Capacity, 0);

            return ret == 0 ? sb.ToString() : null;
        }

        /// <summary>
        /// 从友好名称中提取串口号（例如从 "USB-SERIAL CH340 (COM3)" 中提取 "COM3"）。
        /// </summary>
        /// <param name="friendlyName">设备友好名称。</param>
        /// <returns>串口号（如 "COM3"）；若无法提取返回 null。</returns>
        internal static string ExtractPortNameFromFriendlyName(string friendlyName)
        {
            if (string.IsNullOrEmpty(friendlyName))
                return null;

            int com = friendlyName.LastIndexOf("COM", StringComparison.OrdinalIgnoreCase);
            if (com < 0) return null;
            int end = com + 3;

            while (end < friendlyName.Length && char.IsDigit(friendlyName[end]))
            {
                end++;
            }

            return friendlyName.Substring(com, end - com);
        }
        #endregion

        /// <summary>
        /// 获取当前计算机上与搜索模式匹配的串行端口号。
        /// <para>若 searchPattern 本身已是 "COM" + 数字的格式（如 "COM3"、"COM14"），直接返回。</para>
        /// <para>否则通过 <see cref="GetSerialDevices"/> 枚举所有串口设备，匹配 FriendlyName 包含 searchPattern 的设备。</para>
        /// <para>匹配不区分大小写。若找到多个匹配设备，返回第一个。若未找到任何匹配，返回原始的 searchPattern。</para>
        /// </summary>
        /// <param name="searchPattern">
        /// 查找匹配模式，如 "CH340"、"USB Serial" 或直接 "COM3"。
        /// 若传入 "COM" + 数字格式，无需枚举直接返回。
        /// </param>
        /// <returns>匹配的串口号名称（例如 "COM3"、"COM14"），若未匹配到则返回原始 searchPattern。</returns>
        public static string GetPortName(string searchPattern)
        {
            if (PortNameRegex.IsMatch(searchPattern)) return searchPattern;

            var ports = GetSerialDevices();
            foreach (var port in ports)
            {
                if (string.IsNullOrWhiteSpace(port.FriendlyName)) continue;
                if (port.FriendlyName.IndexOf(searchPattern, StringComparison.OrdinalIgnoreCase) >= 0) return port.PortName;
            }

            return searchPattern;
        }

    }
}
