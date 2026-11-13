namespace SpaceCG.WindowsAPI.SetupAPI
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Runtime.InteropServices;
    using System.Text;
    using System.Threading.Tasks;


	#region Enumerations
	#endregion


	#region Structures
	#endregion


	#region Delegates
	#endregion


	#region Notifications
	#endregion


	/// <summary>
	/// setupapi.dll
	/// <para>参考：https://docs.microsoft.com/zh-cn/windows/win32/api/setupapi/ </para>
	/// <para>参考：https://docs.microsoft.com/zh-cn/previous-versions/ff549791%28v%3dvs.85%29 </para>
	/// </summary>
	public static partial class SetupAPI
    {
        #region Functions 
        [DllImport("setupapi.dll", SetLastError = true)]
		public static extern int SetupDiCreateDeviceInfoList(ref Guid classGuid, int hwndParent);

        /// <summary>
        /// 删除设备信息集并释放所有相关内存
        /// </summary>
        /// <param name="deviceInfoSet"></param>
        /// <returns></returns>
		[DllImport("setupapi.dll", SetLastError = true)]
		public static extern int SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

        /// <summary>
        /// 枚举包含在设备信息集中的设备接口
        /// </summary>
        /// <param name="hDevInfoSet">指向包含要为其返回信息的设备接口的设备信息 集 的指针。 此句柄通常由 <see cref="SetupDiGetClassDevs"/> 返回。</param>
        /// <param name="devInfoData">指向 <see cref="SP_DEVINFO_DATA"/> 结构的指针，该结构指定 DeviceInfoSet 中的设备信息元素。 此参数是可选的，可以为 NULL。</param>
        /// <param name="interfaceClassGuid">指向 GUID 的指针，该 GUID 指定所请求接口的设备接口类。</param>
        /// <param name="memberIndex">设备信息集中接口列表中的从零开始的索引。</param>
        /// <param name="deviceInterfaceData"></param>
        /// <returns></returns>
        [DllImport("setupapi.dll", SetLastError = true)]
        public static extern bool SetupDiEnumDeviceInterfaces(IntPtr hDevInfoSet, IntPtr devInfoData, ref Guid interfaceClassGuid, uint memberIndex, ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData);
        /// <inheritdoc cref="SetupDiEnumDeviceInterfaces(IntPtr, IntPtr, ref Guid, uint, ref SP_DEVICE_INTERFACE_DATA)"/>
        [DllImport("setupapi.dll", SetLastError = true)]
        public static extern bool SetupDiEnumDeviceInterfaces(IntPtr hDevInfoSet, ref SP_DEVINFO_DATA devInfoData, ref Guid interfaceClassGuid, uint memberIndex, ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData);

        /// <summary>
        /// 枚举设备信息集中的设备信息元素（设备节点）。
        /// <para>与 <see cref="SetupDiEnumDeviceInterfaces"/> 的区别：仅枚举设备节点，不遍历接口层，无法获取 DevicePath。</para>
        /// <para>参考：https://learn.microsoft.com/zh-cn/windows/win32/api/setupapi/nf-setupapi-setupdienumdeviceinfo </para>
        /// </summary>
        /// <param name="DeviceInfoSet">设备信息集句柄，通常由 <see cref="SetupDiGetClassDevs"/> 返回</param>
        /// <param name="MemberIndex">从零开始的索引，每次调用递增；枚举完毕返回 false（ERROR_NO_MORE_ITEMS）</param>
        /// <param name="DeviceInfoData">输出：填充的设备信息数据（调用前须设置 cbSize）</param>
        /// <returns>成功返回 true；枚举完毕或失败返回 false，可调用 Marshal.GetLastWin32Error 获取错误码</returns>
        [DllImport("setupapi.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetupDiEnumDeviceInfo(IntPtr DeviceInfoSet, uint MemberIndex, ref SP_DEVINFO_DATA DeviceInfoData);


        /// <summary>
        /// 该函数返回的句柄，设备信息集，其中包含本地计算机请求的设备信息元素。
        /// <para>参考：https://docs.microsoft.com/en-us/windows/win32/api/setupapi/nf-setupapi-setupdigetclassdevsw </para>
        /// </summary>
        /// <param name="classGuid">指向设备设置类或设备接口类的 GUID 的指针。该指针是可选的，可以为 NULL。</param>
        /// <param name="enumerator">指向以 NULL 结尾的字符串的指针，该字符串指定以下选项。该指针是可选的，可以为NULL。如果未使用枚举值选择设备，则将 Enumerator 设置为 NULL
        ///		<para>1.即插即用（PnP）枚举器的标识符（ID）。该ID可以是值的全局唯一标识符（GUID）或符号名。例如，“ PCI”可用于指定PCI PnP值。PnP值的符号名称的其他示例包括“ USB”，“ PCMCIA”和“ SCSI”。</para>
        ///		<para>2.PnP 设备实例ID。指定PnP设备实例ID时，必须在Flags参数中设置DIGCF_DEVICEINTERFACE。</para>
        /// </param>
        /// <param name="hwndParent">顶层窗口的句柄，用于与在设备信息集中安装设备实例相关联的用户界面。该句柄是可选的，可以为 NULL。</param>
        /// <param name="flags"><see cref="DIGCF"/> 此参数可以是零个或多个以下标志的按位或。 </param>
        /// <returns></returns>
        [DllImport("setupapi.dll", SetLastError = true)]
		public static extern IntPtr SetupDiGetClassDevs(ref Guid classGuid, IntPtr enumerator, IntPtr hwndParent, DIGCF flags);
        /// <inheritdoc cref="SetupDiGetClassDevs(ref Guid, IntPtr, IntPtr, DIGCF)" />
        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Auto)]
        public static extern IntPtr SetupDiGetClassDevs(ref Guid classGuid, [MarshalAs(UnmanagedType.LPTStr)] string enumerator, IntPtr hwndParent, uint flags);

        /// <summary>
        /// 返回有关设备接口的详细信息
        /// </summary>
        /// <param name="hDevInfoSet"></param>
        /// <param name="deviceInterfaceData"></param>
        /// <param name="deviceInterfaceDetailData"></param>
        /// <param name="deviceInterfaceDetailDataSize"></param>
        /// <param name="requiredSize"></param>
        /// <param name="deviceInfoData"></param>
        /// <returns></returns>
        [DllImport("setupapi.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr hDevInfoSet, ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData, IntPtr deviceInterfaceDetailData, uint deviceInterfaceDetailDataSize, out uint requiredSize, IntPtr deviceInfoData);
        /// <inheritdoc cref="SetupDiGetDeviceInterfaceDetail(IntPtr, ref SP_DEVICE_INTERFACE_DATA, IntPtr, uint, out uint, IntPtr)"/>
        [DllImport("setupapi.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr hDevInfoSet, ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData, IntPtr deviceInterfaceDetailData, uint deviceInterfaceDetailDataSize, out uint requiredSize, ref SP_DEVINFO_DATA deviceInfoData);

        /// <summary>
        /// 检索指定的即插即用设备属性
        /// </summary>
        /// <param name="deviceInfoSet"></param>
        /// <param name="deviceInfoData"></param>
        /// <param name="property"></param>
        /// <param name="propertyRegDataType"></param>
        /// <param name="propertyBuffer"></param>
        /// <param name="propertyBufferSize"></param>
        /// <param name="requiredSize"></param>
        /// <returns></returns>
        [DllImport("setupapi.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern bool SetupDiGetDeviceRegistryProperty(IntPtr deviceInfoSet, ref SP_DEVINFO_DATA deviceInfoData, uint property, out uint propertyRegDataType, byte[] propertyBuffer, uint propertyBufferSize, out uint requiredSize);
        /// <inheritdoc cref="SetupDiGetDeviceRegistryProperty(IntPtr, ref SP_DEVINFO_DATA, uint, out uint, byte[], uint, out uint)"/>
        [DllImport("setupapi.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern bool SetupDiGetDeviceRegistryProperty(IntPtr deviceInfoSet, IntPtr deviceInfoData, uint property, out uint propertyRegDataType, byte[] propertyBuffer, uint propertyBufferSize, out uint requiredSize);

        #endregion
    }


    /// <summary>
    /// 表示一个串口设备的信息
    /// </summary>
    public class SerialDeviceInfo
    {
        /// <summary>设备友好名称（设备管理器显示的名称，例如 “USB-SERIAL CH340 (COM3)”）</summary>
        public string FriendlyName { get; set; }

        /// <summary>串口名称（例如 “COM3”）</summary>
        public string PortName { get; set; }

        /// <summary>硬件ID（包含 VID、PID 等信息）</summary>
        public string HardwareId { get; set; }

        /// <summary>设备路径（用于 CreateFile 打开设备）</summary>
        public string DevicePath { get; set; }

        /// <inheritdoc/>
        public override string ToString() => $"{FriendlyName} | {PortName} | {HardwareId} | {DevicePath}";
    }


    /// <summary>
    /// WindowsAPI SetupAPI 库，扩展常用/通用，功能/函数，扩展示例，以及使用方式
    /// </summary>
    public static partial class SetupAPIExtensions
	{
        /// <summary>
        /// 串口设备接口 GUID
        /// 定义在 WinDDK 的 GUID_DEVINTERFACE_COMPORT 中
        /// <para>参考：https://learn.microsoft.com/zh-cn/windows-hardware/drivers/install/guid-devinterface-comport </para>
        /// </summary>
        private static Guid GUID_DEVINTERFACE_COMPORT = new Guid("86E0D1E0-8089-11D0-9CE4-08003E301F73");


        /// <summary>
        /// 枚举系统中所有可用的串口设备信息
        /// </summary>
        public static IEnumerable<SerialDeviceInfo> EnumerateSerialDevices()
        {
            var results = new List<SerialDeviceInfo>();

            // 获取串口设备信息集合句柄
            IntPtr hDevInfoSet = SetupAPI.SetupDiGetClassDevs(ref GUID_DEVINTERFACE_COMPORT, IntPtr.Zero, IntPtr.Zero, DIGCF.PRESENT | DIGCF.DEVICEINTERFACE);
            if (hDevInfoSet == IntPtr.Zero || hDevInfoSet == new IntPtr(-1)) return results;

            try
            {
                uint index = 0;
                var interfaceData = new SP_DEVICE_INTERFACE_DATA();
                interfaceData.cbSize = (uint)Marshal.SizeOf(interfaceData);

                // 枚举每个设备接口
                while (SetupAPI.SetupDiEnumDeviceInterfaces(hDevInfoSet, IntPtr.Zero, ref GUID_DEVINTERFACE_COMPORT, index, ref interfaceData))
                {
                    index++;
                    uint requiredSize = 0;      // 第一次调用，获取需要的缓冲区大小
                    SetupAPI.SetupDiGetDeviceInterfaceDetail(hDevInfoSet, ref interfaceData, IntPtr.Zero, 0, out requiredSize, IntPtr.Zero);

                    // 分配缓冲区
                    IntPtr detailDataBuffer = Marshal.AllocHGlobal((int)requiredSize);
                    try
                    {
                        // 根据系统位数设置结构体 cbSize
                        if (IntPtr.Size == 8)
                            Marshal.WriteInt32(detailDataBuffer, 8); // 64位
                        else
                            Marshal.WriteInt32(detailDataBuffer, 6); // 32位

                        var devInfoData = new SP_DEVINFO_DATA();
                        devInfoData.cbSize = (uint)Marshal.SizeOf(devInfoData);

                        // 第二次调用，获取详细信息
                        var success = SetupAPI.SetupDiGetDeviceInterfaceDetail(hDevInfoSet, ref interfaceData, detailDataBuffer, requiredSize, out requiredSize, ref devInfoData);
                        if (!success) continue;

                        // 获取设备路径字符串
                        IntPtr pDevicePathName = IntPtr.Add(detailDataBuffer, 4 + Marshal.SystemDefaultCharSize);
                        string devicePath = Marshal.PtrToStringAuto(pDevicePathName);

                        // 获取设备属性
                        string friendly = GetPropertyString(hDevInfoSet, ref devInfoData, (uint)SPDRP.FRIENDLYNAME);
                        string portName = GetPropertyString(hDevInfoSet, ref devInfoData, (uint)SPDRP.PORTNAME);
                        string hardwareId = GetPropertyString(hDevInfoSet, ref devInfoData, (uint)SPDRP.HARDWAREID);

                        // 如果没有获取到端口名，从友好名中提取 (COMx)
                        if (string.IsNullOrEmpty(portName) && !string.IsNullOrEmpty(friendly))
                        {
                            var parsed = ParseComFromFriendlyName(friendly);
                            if (!string.IsNullOrEmpty(parsed)) portName = parsed;
                        }

                        results.Add(new SerialDeviceInfo
                        {
                            FriendlyName = friendly,
                            PortName = portName,
                            HardwareId = hardwareId,
                            DevicePath = devicePath
                        });
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(detailDataBuffer);
                    }
                }
            }
            finally
            {
                // 销毁设备信息集合
                SetupAPI.SetupDiDestroyDeviceInfoList(hDevInfoSet);
            }

            return results;
        }
#if false
        public static List<UsbDeviceInfo> EnumerateUsbDevices()
        {
            var results = new List<UsbDeviceInfo>();

            IntPtr hDevInfo = SetupDiGetClassDevs(ref GUID_DEVINTERFACE_DISK, null, IntPtr.Zero, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
            if (hDevInfo == IntPtr.Zero || hDevInfo == new IntPtr(-1))
                return results;

            try
            {
                uint index = 0;
                var interfaceData = new SP_DEVICE_INTERFACE_DATA();
                interfaceData.cbSize = (uint)Marshal.SizeOf(interfaceData);

                // 枚举每个设备接口
                while (SetupDiEnumDeviceInterfaces(hDevInfo, IntPtr.Zero, ref GUID_DEVINTERFACE_DISK, index, ref interfaceData))
                {
                    index++;

                    // 获取设备接口详细信息的缓冲区大小
                    uint requiredSize = 0;
                    SetupDiGetDeviceInterfaceDetail(hDevInfo, ref interfaceData, IntPtr.Zero, 0, out requiredSize, IntPtr.Zero);

                    // 分配缓冲区
                    IntPtr detailDataBuffer = Marshal.AllocHGlobal((int)requiredSize);
                    try
                    {
                        // 设置结构体的大小
                        if (IntPtr.Size == 8)
                            Marshal.WriteInt32(detailDataBuffer, 8); // 64位系统
                        else
                            Marshal.WriteInt32(detailDataBuffer, 6); // 32位系统

                        var devInfoData = new SP_DEVINFO_DATA();
                        devInfoData.cbSize = (uint)Marshal.SizeOf(devInfoData);

                        // 获取设备接口的详细信息
                        bool success = SetupDiGetDeviceInterfaceDetail(hDevInfo, ref interfaceData, detailDataBuffer, requiredSize, out requiredSize, ref devInfoData);
                        if (!success)
                            continue;

                        // 获取设备路径
                        IntPtr pDevicePathName = IntPtr.Add(detailDataBuffer, 4 + Marshal.SystemDefaultCharSize);
                        string devicePath = Marshal.PtrToStringAuto(pDevicePathName);

                        // 获取设备属性
                        string friendly = GetPropertyString(hDevInfo, ref devInfoData, SPDRP_FRIENDLYNAME);
                        string hardwareId = GetPropertyString(hDevInfo, ref devInfoData, SPDRP_HARDWAREID);

                        results.Add(new UsbDeviceInfo
                        {
                            FriendlyName = friendly,
                            DevicePath = devicePath,
                            HardwareId = hardwareId,
                            SerialNumber = ExtractSerialNumber(hardwareId) // 可根据硬件ID提取序列号（如果有）
                        });
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(detailDataBuffer);
                    }
                }
            }
            finally
            {
                SetupDiDestroyDeviceInfoList(hDevInfo);
            }

            return results;
        }
#endif
        /// <summary>
        /// 从 SetupAPI 获取指定设备属性字符串
        /// </summary>
        private static string GetPropertyString(IntPtr hDevInfoSet, ref SP_DEVINFO_DATA devInfoData, uint property)
        {
            uint regType;
            uint requiredSize = 0;
            byte[] buffer = new byte[512];

            bool success = SetupAPI.SetupDiGetDeviceRegistryProperty(hDevInfoSet, ref devInfoData, property, out regType, buffer, (uint)buffer.Length, out requiredSize);
            if (!success && requiredSize > 0)
            {
                // 如果缓冲区不够，则重新分配
                buffer = new byte[requiredSize];
                success = SetupAPI.SetupDiGetDeviceRegistryProperty(hDevInfoSet, ref devInfoData, property, out regType, buffer, (uint)buffer.Length, out requiredSize);
            }
            if (!success) return null;

            // 转换为字符串（优先使用 Unicode）
            try
            {
                string s = Encoding.Unicode.GetString(buffer, 0, (int)requiredSize);
                s = s.TrimEnd('\0');
                int idx = s.IndexOf('\0');
                if (idx >= 0) s = s.Substring(0, idx);

                return s;
            }
            catch
            {
                // 回退到 ANSI
                string s = Encoding.Default.GetString(buffer, 0, (int)requiredSize).TrimEnd('\0');
                int idx = s.IndexOf('\0');
                if (idx >= 0) s = s.Substring(0, idx);

                return s;
            }
        }

        /// <summary>
        /// 从友好名称中提取串口号（例如从 "USB-SERIAL CH340 (COM3)" 中提取 "COM3"）
        /// </summary>
        private static string ParseComFromFriendlyName(string friendly)
        {
            if (string.IsNullOrEmpty(friendly)) return null;

            int start = friendly.LastIndexOf("(COM", StringComparison.OrdinalIgnoreCase);
            if (start >= 0)
            {
                int end = friendly.IndexOf(')', start);
                if (end > start)
                    return friendly.Substring(start + 1, end - start - 1); // e.g. COM3
            }

            return null;
        }

        private static string ExtractSerialNumber(string hardwareId)
        {
            // 根据硬件ID提取序列号（可根据硬件ID的特定格式进行解析）
            // 示例：从 "USB\\VID_1A86&PID_7523\\6&1F59C3B0&0&2" 中提取序列号
            if (string.IsNullOrEmpty(hardwareId))
                return null;

            var parts = hardwareId.Split('\\');
            if (parts.Length > 1)
            {
                return parts[1]; // 返回序列号部分
            }
            return null;
        }
    }
}
