using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;
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
        #region 设备接口 GUID（readonly，按值传递给 P/Invoke
        /// <summary>
        /// 串口设备接口 GUID（GUID_DEVINTERFACE_COMPORT）。
        /// </summary>
        public static readonly Guid GUID_DEVINTERFACE_COMPORT = new Guid("86E0D1E0-8089-11D0-9CE4-08003E301F73");

        /// <summary>
        /// USB 设备接口 GUID（GUID_DEVINTERFACE_USB_DEVICE）。
        /// </summary>
        public static readonly Guid GUID_DEVINTERFACE_USB_DEVICE = new Guid("A5DCBF10-6530-11D2-901F-00C04FB951ED");

        /// <summary>
        /// 磁盘设备接口 GUID（GUID_DEVINTERFACE_DISK）。
        /// </summary>
        public static readonly Guid GUID_DEVINTERFACE_DISK = new Guid("53F56307-B6BF-11D0-94F2-00A0C91EFB8B");

        /// <summary>
        /// 卷设备接口 GUID（GUID_DEVINTERFACE_VOLUME）。
        /// </summary>
        public static readonly Guid GUID_DEVINTERFACE_VOLUME = new Guid("53F5630D-B6BF-11D0-94F2-00A0C91EFB8B");

        /// <summary>
        /// 人机接口设备 (HID - 鼠标/键盘/游戏手柄等)
        /// </summary>
        public static readonly Guid GUID_DEVINTERFACE_HID = new Guid("4D1E55B2-F16F-11CF-88CB-001111000030");
        #endregion


        #region SetupAPI 标志位

        /// <summary>仅获取当前存在的设备。</summary>
        internal const int DIGCF_PRESENT = 0x00000002;

        /// <summary>返回接口类设备。</summary>
        internal const int DIGCF_DEVICEINTERFACE = 0x00000010;

        // ===== 设备注册表属性 ID =====

        /// <summary>包含设备的友好名称的 REG_SZ 字符串。</summary>
        internal const uint SPDRP_FRIENDLYNAME = 0x0000000C;

        /// <summary>端口名称（COMx）。</summary>
        internal const uint SPDRP_PORTNAME = 0x00000037;

        /// <summary>包含设备硬件 ID 列表的 REG_MULTI_SZ 字符串。</summary>
        internal const uint SPDRP_HARDWAREID = 0x00000001;

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
        /// 枚举包含在设备信息集中的设备接口。
        /// </summary>
        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        internal static extern bool SetupDiEnumDeviceInterfaces(IntPtr hDevInfoSet, IntPtr devInfoData, [In] ref Guid interfaceClassGuid, uint memberIndex, ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData);

        /// <summary>
        /// 返回有关设备接口的详细信息（不返回 SP_DEVINFO_DATA）。
        /// </summary>
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr hDevInfoSet, ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData, IntPtr deviceInterfaceDetailData, uint deviceInterfaceDetailDataSize, out uint requiredSize, IntPtr deviceInfoData);

        /// <summary>
        /// 返回有关设备接口的详细信息（同时返回 SP_DEVINFO_DATA）。
        /// </summary>
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr hDevInfoSet, ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData, IntPtr deviceInterfaceDetailData, uint deviceInterfaceDetailDataSize, out uint requiredSize, ref SP_DEVINFO_DATA deviceInfoData);

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
    public abstract class DeviceInfo
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

        public virtual string DisplayName => FriendlyName ?? DevicePath ?? InstanceId;

        /// <inheritdoc /> 
        public override string ToString() =>
            $"FriendlyName:{FriendlyName}  HardwareId:{HardwareId}  InstanceId:{InstanceId}  DevicePath:{DevicePath}  ({ClassGuid})";
    }

    /// <summary>
    /// USB 设备信息。
    /// <para>继承自 <see cref="DeviceInfo"/>，增加 USB 特有的 VID/PID/SerialNumber 属性。</para>
    /// <para>所有属性均可在枚举阶段通过字符串解析轻量填充，不依赖设备 I/O。</para>
    /// </summary>
    public class UsbDeviceInfo : DeviceInfo
    {
        /// <summary>
        /// USB Vendor ID（供应商 ID）。
        /// <para>从 <see cref="DeviceInfo.HardwareId"/> 中解析 "VID_XXXX" 模式，提取 4 位十六进制值。</para>
        /// <para>非 USB 设备或解析失败时为 null。</para>
        /// </summary>
        public string Vid { get; internal set; }

        /// <summary>
        /// USB Product ID（产品 ID）。
        /// <para>从 <see cref="DeviceInfo.HardwareId"/> 中解析 "PID_XXXX" 模式，提取 4 位十六进制值。</para>
        /// <para>非 USB 设备或解析失败时为 null。</para>
        /// </summary>
        public string Pid { get; internal set; }

        /// <summary>
        /// USB 设备硬件序列号。
        /// <para>从 <see cref="DeviceInfo.InstanceId"/> 最后一个反斜杠后提取。</para>
        /// <para>若提取结果包含 '&amp;' 字符，判定为 Windows 自动生成的伪序列号，返回 null。</para>
        /// </summary>
        public string SerialNumber { get; internal set; }

        /// <inheritdoc/>
        public override string ToString() => $"{base.ToString()} Vid:{Vid} Pid:{Pid} SerialNumber:{SerialNumber}";
    }

    /// <summary>
    /// 串口设备信息。
    /// <para>继承自 <see cref="UsbDeviceInfo"/>（因为现代串口设备多为 USB 转串口芯片）。</para>
    /// <para>对于主板原生串口（非 USB），Vid/Pid/SerialNumber 将为 null。</para>
    /// <para>所有属性均可在枚举阶段轻量填充，不依赖设备 I/O。</para>
    /// </summary>
    public sealed class SerialDeviceInfo : UsbDeviceInfo
    {
        /// <summary>
        /// 串口名称（例如 "COM3"、"COM10"）。
        /// <para>来源：优先从 SPDRP_PORTNAME 获取，若为空则从 FriendlyName 解析。</para>
        /// </summary>
        public string PortName { get; internal set; }

        /// <inheritdoc/>
        public override string ToString() => $"{base.ToString()} PortName:{PortName}";
    }

    /// <summary>
    /// 物理磁盘设备信息（GUID_DEVINTERFACE_DISK）。物理层：Disk（磁盘设备）。
    /// <para>表示物理层磁盘，对应 "\\.\PhysicalDrive0" 等设备路径。</para>
    /// <para>轻量属性（枚举阶段填充）：FriendlyName, DevicePath, HardwareId, InstanceId。</para>
    /// <para>重量属性（需额外 API 调用）：SerialNumber, IsMediaChange, SizeBytes, Volumes。</para>
    /// <para>线程安全：数据载体类，读操作线程安全。</para>
    /// </summary>
    public sealed class DiskDeviceInfo : DeviceInfo
    {
        /// <summary>
        /// 磁盘编号（从 DevicePath 解析，例如 "\\.\PhysicalDrive0" → "0"）。
        /// <para>暂未实现自动解析，需调用方自行从 DevicePath 提取。</para>
        /// </summary>
        public int DiskNumber { get; internal set; }

        /// <summary>
        /// 是否为可移除/可热插拔媒体（如 U 盘、移动硬盘）。
        /// <para>需通过 CreateFile + IOCTL_STORAGE_GET_HOTPLUG_INFO 获取。</para>
        /// <para>未填充时为 false（默认值），无法与"非可移除"区分，需结合 IsDetailPopulated 判断。</para>
        /// </summary>
        public bool IsMediaChange { get; internal set; }

        /// <summary>
        /// 磁盘硬件序列号。
        /// <para>需通过 CreateFile + IOCTL_STORAGE_QUERY_PROPERTY 获取 STORAGE_DEVICE_DESCRIPTOR.SerialNumber。</para>
        /// <para>注意：不能通过 InstanceId 尾部提取（那是设备实例片段，非真实硬件序列号）。</para>
        /// </summary>
        public string SerialNumber { get; internal set; }

        /// <summary>
        /// 磁盘总大小（字节）。
        /// <para>需通过 CreateFile + IOCTL_DISK_GET_DRIVE_GEOMETRY_EX 获取 DISK_GEOMETRY_EX.DiskSize。</para>
        /// <para>未填充时为 0。</para>
        /// </summary>
        public long SizeBytes { get; internal set; }

        /// <summary>
        /// 此物理磁盘上包含的所有逻辑卷（一对多关联）。
        /// <para>需通过 IOCTL_VOLUME_GET_VOLUME_DISK_EXTENTS 或 WMI 反向关联查询。</para>
        /// <para>未填充时为 null。</para>
        /// </summary>
        public IReadOnlyList<VolumeDeviceInfo> Volumes { get; internal set; } = Array.Empty<VolumeDeviceInfo>();

        /// <inheritdoc/>
        public override string ToString() => $"{base.ToString()}  DiskNumber:{DiskNumber}  IsMediaChange:{IsMediaChange}  SerialNumber:{SerialNumber}  SizeBytes:{SizeBytes}";
    }

    /// <summary>
    /// 逻辑卷设备信息（GUID_DEVINTERFACE_VOLUME）。
    /// <para>表示逻辑层卷（分区），对应 "\\.\Volume{GUID}" 设备路径。</para>
    /// <para>轻量属性（枚举阶段填充）：FriendlyName, DevicePath, HardwareId, InstanceId, VolumeName。</para>
    /// <para>重量属性（需额外 API 调用）：DriveLetter, LabelName, IsNetworkDrive, FileSystem。</para>
    /// </summary>
    public sealed class VolumeDeviceInfo : DeviceInfo
    {
        /// <summary>
        /// 卷 GUID 路径（从 DevicePath 直接取值，如 "\\?\Volume{xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx}"）。
        /// <para>也可通过 GetVolumeNameForVolumeMountPoint 获取。</para>
        /// </summary>
        public string VolumeName { get; internal set; }
        /// <summary>
        /// 挂载的驱动器号（如 "C:"、"D:"）。
        /// <para>需通过 GetVolumePathNamesForVolumeName 获取，未挂载的卷为 null。</para>
        /// </summary>
        public string DriveLetter { get; internal set; }
        /// <summary>
        /// 卷标名称（如 "系统盘"、"Data"）。
        /// <para>需通过 GetVolumeInformation 或 DriveInfo.VolumeLabel 获取。</para>
        /// </summary>
        public string LabelName { get; internal set; }

        /// <summary>
        /// 是否为网络映射驱动器。
        /// <para>需通过 GetDriveType 判断 DRIVE_REMOTE，未填充时为 false。</para>
        /// </summary>
        public bool IsNetworkDrive { get; internal set; }

        /// <summary>
        /// 文件系统类型（如 "NTFS"、"FAT32"、"exFAT"、"ReFS"）。
        /// <para>需通过 GetVolumeInformation 或 DriveInfo.DriveFormat 获取。</para>
        /// </summary>
        public string FileSystem { get; internal set; }

        /// <inheritdoc/>
        public override string ToString() => $"{base.ToString()} VolumeName:{VolumeName} DriveLetter:{DriveLetter} LabelName:{LabelName} " +
            $"FileSystem:{FileSystem}  IsNetworkDrive:{(IsNetworkDrive ? "Network" : "Local")}";
    }
    #endregion


    /// <summary>
    /// Windows 设备信息查询辅助类。
    /// <para>使用 SetupAPI 枚举系统中的串口、USB、磁盘、卷等即插即用设备。</para>
    /// <para>线程安全：此类为静态工具类，不维护可变状态，可在多线程环境中并发调用。</para>
    /// </summary>
    public static class SystemExtensions
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

        #region 核心枚举引擎 (复用逻辑)
        /// <summary>
        /// 通用的设备枚举引擎，封装了 SetupAPI 的标准调用流程。
        /// </summary>
        /// <typeparam name="T">返回的设备信息类型。</typeparam>
        /// <param name="classGuid">设备接口 GUID。</param>
        /// <param name="selector">用于从设备数据构建目标对象的工厂函数。</param>
        /// <returns>设备信息列表。</returns>
        internal static IReadOnlyList<T> EnumerateDevices<T>(Guid classGuid, Func<IntPtr, NativeMethods.SP_DEVINFO_DATA, string, T> selector)
        {
            IntPtr hDevInfoSet = NativeMethods.SetupDiGetClassDevs(ref classGuid, null, IntPtr.Zero, (uint)(NativeMethods.DIGCF_PRESENT | NativeMethods.DIGCF_DEVICEINTERFACE));
            if (hDevInfoSet == IntPtr.Zero || hDevInfoSet == InvalidHandle) return Array.Empty<T>();

            var results = new List<T>(16);
            try
            {
                uint index = 0;
                var interfaceData = new NativeMethods.SP_DEVICE_INTERFACE_DATA();
                interfaceData.cbSize = (uint)Marshal.SizeOf(interfaceData);

                while (NativeMethods.SetupDiEnumDeviceInterfaces(hDevInfoSet, IntPtr.Zero, ref classGuid, index, ref interfaceData))
                {
                    index++;
                    if (TryGetDeviceInterfaceDetail(hDevInfoSet, ref interfaceData, out string devicePath, out var devInfoData))
                    {
                        T device = selector(hDevInfoSet, devInfoData, devicePath);
                        if (device != null)
                        {
                            results.Add(device);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Trace.TraceWarning($"枚举设备信息失败 (GUID: {classGuid})：{ex.Message}");
                return Array.Empty<T>();
            }
            finally
            {
                NativeMethods.SetupDiDestroyDeviceInfoList(hDevInfoSet);
            }

            return results;
        }
        #endregion

        #region 公共查询方法
        /// <summary>
        /// 枚举系统中所有可用的 USB 设备信息。
        /// </summary>
        /// <returns>USB 设备信息列表，若无设备返回空列表。</returns>
        public static IReadOnlyList<UsbDeviceInfo> GetUsbDevices() => EnumerateDevices(NativeMethods.GUID_DEVINTERFACE_USB_DEVICE, (hDevInfoSet, devInfo, devPath) =>
        {
            string instanceId = GetDeviceInstanceId(devInfo.DevInst);
            string hardwareId = GetDevicePropertyString(hDevInfoSet, ref devInfo, NativeMethods.SPDRP_HARDWAREID);
            string friendlyName = GetDevicePropertyString(hDevInfoSet, ref devInfo, NativeMethods.SPDRP_FRIENDLYNAME);

            ExtractVidPidFromHardwareId(hardwareId, out string vid, out string pid);

            return new UsbDeviceInfo
            {
                Vid = vid,
                Pid = pid,
                DevicePath = devPath,
                HardwareId = hardwareId,
                InstanceId = instanceId,
                FriendlyName = friendlyName,
                ClassGuid = devInfo.ClassGuid,
                SerialNumber = ExtractSerialNumberFromInstanceId(instanceId)
            };
        });

        /// <summary>
        /// 枚举系统中所有可用的串口设备信息。
        /// </summary>
        /// <returns>串口设备信息列表，若无设备返回空列表。</returns>
        public static IReadOnlyList<SerialDeviceInfo> GetSerialDevices() => EnumerateDevices(NativeMethods.GUID_DEVINTERFACE_COMPORT, (hDevInfoSet, devInfo, devPath) =>
        {
            string instanceId = GetDeviceInstanceId(devInfo.DevInst);
            string portName = GetDevicePropertyString(hDevInfoSet, ref devInfo, NativeMethods.SPDRP_PORTNAME);
            string hardwareId = GetDevicePropertyString(hDevInfoSet, ref devInfo, NativeMethods.SPDRP_HARDWAREID);
            string friendlyName = GetDevicePropertyString(hDevInfoSet, ref devInfo, NativeMethods.SPDRP_FRIENDLYNAME);

            if (string.IsNullOrEmpty(portName) && !string.IsNullOrEmpty(friendlyName))
            {
                string parsed = ExtractPortNameFromFriendlyName(friendlyName);
                if (!string.IsNullOrEmpty(parsed)) portName = parsed;
            }

            ExtractVidPidFromHardwareId(hardwareId, out string vid, out string pid);

            return new SerialDeviceInfo
            {
                Vid = vid,
                Pid = pid,
                PortName = portName,
                DevicePath = devPath,
                HardwareId = hardwareId,
                InstanceId = instanceId,
                FriendlyName = friendlyName,
                ClassGuid = devInfo.ClassGuid,
                SerialNumber = ExtractSerialNumberFromInstanceId(instanceId)
            };
        });

        /// <summary>
        /// 枚举系统中所有可用的磁盘设备信息。
        /// </summary>
        /// <returns>磁盘设备信息列表，若无设备返回空列表。</returns>
        public static IReadOnlyList<DiskDeviceInfo> GetDiskDevices() => EnumerateDevices(NativeMethods.GUID_DEVINTERFACE_DISK, (hDevInfoSet, devInfo, devPath) =>
        {
            string instanceId = GetDeviceInstanceId(devInfo.DevInst);
            string hardwareId = GetDevicePropertyString(hDevInfoSet, ref devInfo, NativeMethods.SPDRP_HARDWAREID);
            string friendlyName = GetDevicePropertyString(hDevInfoSet, ref devInfo, NativeMethods.SPDRP_FRIENDLYNAME);

            return new DiskDeviceInfo
            {
                DevicePath = devPath,
                HardwareId = hardwareId,
                InstanceId = instanceId,
                FriendlyName = friendlyName,
                ClassGuid = devInfo.ClassGuid,
                SerialNumber = ExtractSerialNumberFromInstanceId(instanceId)
            };
        });

        /// <summary>
        /// 枚举系统中所有可用的卷设备信息。
        /// </summary>
        /// <returns>卷设备信息列表，若无设备返回空列表。</returns>
        public static IReadOnlyList<VolumeDeviceInfo> GetVolumeDevices() => EnumerateDevices(NativeMethods.GUID_DEVINTERFACE_VOLUME, (hDevInfoSet, devInfo, devPath) =>
        {
            string instanceId = GetDeviceInstanceId(devInfo.DevInst);
            string hardwareId = GetDevicePropertyString(hDevInfoSet, ref devInfo, NativeMethods.SPDRP_HARDWAREID);
            string friendlyName = GetDevicePropertyString(hDevInfoSet, ref devInfo, NativeMethods.SPDRP_FRIENDLYNAME);

            return new VolumeDeviceInfo
            {
                DevicePath = devPath,
                HardwareId = hardwareId,
                InstanceId = instanceId,
                FriendlyName = friendlyName,
                ClassGuid = devInfo.ClassGuid,
            };
        });
        #endregion

        #region 辅助解析方法
        /// <summary>
        /// 调用 SetupDiGetDeviceInterfaceDetail 获取设备接口详细信息和设备信息数据。
        /// <para>内部封装两次调用模式：第一次获取缓冲区大小，第二次获取实际数据。</para>
        /// </summary>
        /// <param name="hDevInfoSet">设备信息集句柄。</param>
        /// <param name="interfaceData">设备接口数据。</param>
        /// <param name="devicePath">输出：设备路径字符串；失败时为 null。</param>
        /// <param name="devInfoData">输出：设备信息数据；失败时为默认值。</param>
        /// <returns>成功返回 true，失败返回 false。</returns>
        internal static bool TryGetDeviceInterfaceDetail(IntPtr hDevInfoSet, ref NativeMethods.SP_DEVICE_INTERFACE_DATA interfaceData, out string devicePath, out NativeMethods.SP_DEVINFO_DATA devInfoData)
        {
            devicePath = null;
            devInfoData = default;

            // 第一次调用：获取所需缓冲区大小
            NativeMethods.SetupDiGetDeviceInterfaceDetail(hDevInfoSet, ref interfaceData, IntPtr.Zero, 0, out uint requiredSize, IntPtr.Zero);

            if (requiredSize == 0) return false;

            IntPtr detailBuffer = Marshal.AllocHGlobal((int)requiredSize);
            try
            {
                // 写入 SP_DEVICE_INTERFACE_DETAIL_DATA 的 cbSize（x86: 6, x64: 8）
                Marshal.WriteInt32(detailBuffer, DetailDataCbSize);

                devInfoData = new NativeMethods.SP_DEVINFO_DATA();
                devInfoData.cbSize = (uint)Marshal.SizeOf(devInfoData);

                bool success = NativeMethods.SetupDiGetDeviceInterfaceDetail(hDevInfoSet, ref interfaceData, detailBuffer, requiredSize, out requiredSize, ref devInfoData);

                if (!success) return false;

                IntPtr pDevicePath = IntPtr.Add(detailBuffer, DetailDataCbSize);
                devicePath = Marshal.PtrToStringUni(pDevicePath);

                return true;
            }
            finally
            {
                Marshal.FreeHGlobal(detailBuffer);
            }
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

        /// <summary>
        /// 从设备实例 ID 中提取硬件序列号。
        /// <para>实例 ID 格式通常为 "...\XXXX"，提取最后一个反斜杠后的部分作为序列号。</para>
        /// <para>若提取结果包含 '&amp;' 字符，则判定为 Windows 自动生成的伪序列号，返回 null。</para>
        /// </summary>
        /// <param name="instanceId">设备实例 ID 字符串。</param>
        /// <returns>硬件序列号；若实例 ID 无效或为伪序列号则返回 null。</returns>
        internal static string ExtractSerialNumberFromInstanceId(string instanceId)
        {
            if (string.IsNullOrEmpty(instanceId)) return null;

            int pos = instanceId.LastIndexOf('\\');
            if (pos < 0) return null;

            string value = instanceId.Substring(pos + 1);
            // 如果包含 '&'，说明是 Windows 生成的伪序列号，而非真实硬件序列号
            if (value.Contains("&")) return null;

            return value;
        }

        /// <summary>
        /// 从硬件 ID 字符串中解析 USB 设备的 VID（Vendor ID）和 PID（Product ID）。
        /// <para>识别 "VID_XXXX" 和 "PID_XXXX" 模式，提取其后 4 位十六进制标识符。</para>
        /// </summary>
        /// <param name="hardwareId">硬件 ID 字符串（如 "USB\VID_1A86&PID_7523"）。</param>
        /// <param name="vid">输出：Vendor ID（不含 "VID_" 前缀）；解析失败时为 null。</param>
        /// <param name="pid">输出：Product ID（不含 "PID_" 前缀）；解析失败时为 null。</param>
        internal static void ExtractVidPidFromHardwareId(string hardwareId, out string vid, out string pid)
        {
            vid = null;
            pid = null;
            if (string.IsNullOrWhiteSpace(hardwareId)) return;

            int vidIndex = hardwareId.IndexOf("VID_", StringComparison.OrdinalIgnoreCase);
            if (vidIndex >= 0 && vidIndex + 8 <= hardwareId.Length)
            {
                vid = hardwareId.Substring(vidIndex + 4, 4);
            }

            int pidIndex = hardwareId.IndexOf("PID_", StringComparison.OrdinalIgnoreCase);
            if (pidIndex >= 0 && pidIndex + 8 <= hardwareId.Length)
            {
                pid = hardwareId.Substring(pidIndex + 4, 4);
            }
        }

        #endregion

    }

}
