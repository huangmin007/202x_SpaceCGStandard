using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Trace = SpaceCG.Diagnostics.Trace;
using SpaceCG.WindowsAPI.SetupAPI;
using SpaceCG.WindowsAPI.CfgMgr32;

namespace SpaceCG.Extensions
{
    #region 设备数据模型
    /// <summary>
    /// Windows 设备基础信息。
    /// <para>包含 SetupAPI/CfgMgr32 可轻量获取的核心属性，不依赖 CreateFile/DeviceIoControl。</para>
    /// <para>线程安全：数据载体类，读操作线程安全。</para>
    /// </summary>
    public class DeviceInfo
    {
        /// <summary>设备实例句柄（SP_DEVINFO_DATA.DevInst）</summary>
        public uint DevInst { get; internal set; }

        /// <summary>设备实例 ID（CfgMgr32.CM_Get_Device_ID）</summary>
        public string InstanceId { get; internal set; }

        /// <summary>设备友好名称（SPDRP_FRIENDLYNAME）</summary>
        public string FriendlyName { get; internal set; }

        /// <summary>设备路径（SetupAPI.SetupDiGetDeviceInterfaceDetail.DevicePath）</summary>
        public string DevicePath { get; internal set; }

        /// <summary>设备安装类 GUID（SP_DEVINFO_DATA.ClassGuid）</summary>
        public Guid ClassGuid { get; internal set; }

        /// <summary>设备类名（SPDRP_CLASS，如 "Ports"、"USB"、"DiskDrive"）</summary>
        public string ClassName { get; internal set; }

        /// <summary>设备硬件 ID 列表（SPDRP_HARDWAREID，REG_MULTI_SZ）</summary>
        public string[] HardwareIds { get; internal set; }

        /// <summary>首选硬件 ID（HardwareIds 的第一项）</summary>
        public string HardwareId => HardwareIds?.FirstOrDefault();

        /// <summary>显示名称（优先 FriendlyName，其次 DevicePath，最后 InstanceId）</summary>
        public virtual string DisplayName => FriendlyName ?? DevicePath ?? InstanceId;

        /// <inheritdoc />
        public override string ToString() =>
            $"FriendlyName:{FriendlyName}  HardwareId:{HardwareId}  InstanceId:{InstanceId}  DevicePath:{DevicePath}  ({ClassGuid})";
    }

    /// <summary>
    /// USB 设备信息。
    /// <para>所有属性均可在枚举阶段通过字符串解析轻量填充，不依赖设备 I/O。</para>
    /// </summary>
    public sealed class UsbDeviceInfo : DeviceInfo
    {
        /// <summary>USB Vendor ID（从 HardwareId 解析 "VID_XXXX"）</summary>
        public string Vid { get; internal set; }

        /// <summary>USB Product ID（从 HardwareId 解析 "PID_XXXX"）</summary>
        public string Pid { get; internal set; }

        /// <summary>USB 设备序列号（从 InstanceId 尾部提取，含 '&amp;' 则为 Windows 伪值 → null）</summary>
        public string SerialNumber { get; internal set; }

        /// <inheritdoc />
        public override string ToString() => $"{base.ToString()} Vid:{Vid} Pid:{Pid} SerialNumber:{SerialNumber}";
    }

    /// <summary>
    /// 串口设备信息。
    /// </summary>
    public sealed class SerialDeviceInfo : DeviceInfo
    {
        /// <summary>串口名称（如 "COM3"、"COM10"，从 FriendlyName 解析）</summary>
        public string PortName { get; internal set; }

        /// <summary>USB Vendor ID（从 HardwareId 解析 "VID_XXXX"）</summary>
        public string Vid { get; internal set; }

        /// <summary>USB Product ID（从 HardwareId 解析 "PID_XXXX"）</summary>
        public string Pid { get; internal set; }

        /// <summary>USB 设备序列号（从 InstanceId 尾部提取）</summary>
        public string SerialNumber { get; internal set; }

        /// <inheritdoc />
        public override string ToString() => $"{base.ToString()} PortName:{PortName} Vid:{Vid} Pid:{Pid} SerialNumber:{SerialNumber}";
    }

    /// <summary>
    /// 物理磁盘设备信息（SetupAPI.GUID_DEVINTERFACE_DISK）。
    /// <para>仅包含枚举阶段可获取的轻量属性。重量属性（SerialNumber/SizeBytes 等）需额外 API 调用。</para>
    /// </summary>
    public sealed class DiskDeviceInfo : DeviceInfo
    {
        /// <summary>磁盘编号（从 DevicePath 解析，如 "\\.\PhysicalDrive0" → 0）</summary>
        public int DiskNumber { get; internal set; }

        /// <inheritdoc />
        public override string ToString() => $"{base.ToString()}  DiskNumber:{DiskNumber}";
    }

    /// <summary>
    /// 逻辑卷设备信息（SetupAPI.GUID_DEVINTERFACE_VOLUME）。
    /// <para>仅包含枚举阶段可获取的轻量属性。重量属性（DriveLetter/Label/FileSystem 等）需额外 API 调用。</para>
    /// </summary>
    public sealed class VolumeDeviceInfo : DeviceInfo
    {
        /// <summary>卷 GUID 路径（从 DevicePath 直接取值，如 "\\?\Volume{xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx}"）</summary>
        public string VolumeName { get; internal set; }

        /// <inheritdoc />
        public override string ToString() => $"{base.ToString()} VolumeName:{VolumeName}";
    }
    #endregion

    /// <summary>
    /// Windows 设备信息查询辅助类。
    /// <para>使用 SetupAPI 枚举系统中的串口、USB、磁盘、卷等即插即用设备。</para>
    /// <para>线程安全：静态工具类，不维护可变状态，可在多线程环境中并发调用。</para>
    /// </summary>
    public static class SystemExtensions
    {
        internal static readonly IntPtr InvalidHandle = new IntPtr(-1);

        internal static readonly int DetailDataCbSize = IntPtr.Size == 8 ? 8 : 6;

        public static readonly Regex PortNameRegex = new Regex("^COM[0-9]{1,3}$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        #region SetupAPI 核心函数
        /// <summary>
        /// 通用设备接口枚举引擎，封装 SetupAPI.SetupDiEnumDeviceInterfaces 标准调用流程。
        /// <para>适用于通过设备接口 GUID 枚举的设备类型：USB、Disk、Volume 等。</para>
        /// <para>调用链：SetupAPI.SetupDiGetClassDevs → SetupAPI.SetupDiEnumDeviceInterfaces → SetupAPI.SetupDiGetDeviceInterfaceDetail → 逐个 selector 回调。</para>
        /// <para>内部自动处理句柄生命周期（try-finally + SetupAPI.SetupDiDestroyDeviceInfoList）。</para>
        /// </summary>
        /// <typeparam name="T">返回的设备信息类型</typeparam>
        /// <param name="classGuid">设备接口类 GUID</param>
        /// <param name="selector">工厂函数，接收 (hDevInfoSet, devInfoData, devicePath)，返回目标对象或 null（跳过）</param>
        /// <returns>设备信息列表；枚举异常或设备为空时返回空列表</returns>
        internal static IReadOnlyList<T> EnumerateDeviceInterfaces<T>(Guid classGuid, Func<IntPtr, SP_DEVINFO_DATA, string, T> selector)
        {
            IntPtr hDevInfoSet = SetupAPI.SetupDiGetClassDevs(ref classGuid, null, IntPtr.Zero, (uint)(DIGCF.PRESENT | DIGCF.DEVICEINTERFACE));
            if (hDevInfoSet == IntPtr.Zero || hDevInfoSet == InvalidHandle) return Array.Empty<T>();

            var devices = new List<T>(16);
            try
            {
                uint index = 0;
                var interfaceData = new SP_DEVICE_INTERFACE_DATA();
                interfaceData.cbSize = (uint)Marshal.SizeOf(interfaceData);

                while (SetupAPI.SetupDiEnumDeviceInterfaces(hDevInfoSet, IntPtr.Zero, ref classGuid, index, ref interfaceData))
                {
                    index++;
                    if (TryGetDeviceInterfaceDetail(hDevInfoSet, ref interfaceData, out string devicePath, out var devInfoData))
                    {
                        T device = selector(hDevInfoSet, devInfoData, devicePath);
                        if (device != null)
                        {
                            devices.Add(device);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Trace.TraceWarning($"枚举设备接口信息失败 (GUID: {classGuid})：{ex.Message}");
                return Array.Empty<T>();
            }
            finally
            {
                SetupAPI.SetupDiDestroyDeviceInfoList(hDevInfoSet);
            }

            return devices;
        }

        /// <summary>
        /// 通用设备信息枚举引擎，封装 SetupAPI.SetupDiEnumDeviceInfo 标准调用流程。
        /// <para>适用于通过设备安装类 GUID 枚举的设备类型：Ports 等。</para>
        /// <para>与 <see cref="EnumerateDeviceInterfaces{T}"/> 的区别：不获取 DevicePath，
        /// 因为 SetupAPI.SetupDiEnumDeviceInfo 不遍历接口层，仅枚举设备节点。</para>
        /// <para>调用链：SetupAPI.SetupDiGetClassDevs → SetupAPI.SetupDiEnumDeviceInfo → 逐个 selector 回调。</para>
        /// <para>内部自动处理句柄生命周期（try-finally + SetupAPI.SetupDiDestroyDeviceInfoList）。</para>
        /// </summary>
        /// <typeparam name="T">返回的设备信息类型</typeparam>
        /// <param name="classGuid">设备安装类 GUID</param>
        /// <param name="selector">工厂函数，接收 (hDevInfoSet, devInfoData)，返回目标对象或 null（跳过）</param>
        /// <returns>设备信息列表；枚举异常或设备为空时返回空列表</returns>
        internal static IReadOnlyList<T> EnumerateDeviceInfos<T>(Guid classGuid, Func<IntPtr, SP_DEVINFO_DATA, T> selector)
        {
            IntPtr hDevInfoSet = SetupAPI.SetupDiGetClassDevs(ref classGuid, null, IntPtr.Zero, (uint)DIGCF.PRESENT);
            if (hDevInfoSet == IntPtr.Zero || hDevInfoSet == InvalidHandle) return Array.Empty<T>();

            var devices = new List<T>(16);
            try
            {
                uint index = 0;
                var devInfo = new SP_DEVINFO_DATA();
                devInfo.cbSize = (uint)Marshal.SizeOf(typeof(SP_DEVINFO_DATA));

                while (SetupAPI.SetupDiEnumDeviceInfo(hDevInfoSet, index, ref devInfo))
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
                SetupAPI.SetupDiDestroyDeviceInfoList(hDevInfoSet);
            }

            return devices;
        }

        /// <summary>
        /// 获取设备接口详细信息和设备信息数据。
        /// <para>封装 SetupAPI.SetupDiGetDeviceInterfaceDetail 两次调用模式：
        /// 第一次传入空缓冲区获取 requiredSize，第二次分配缓冲区并获取实际数据。</para>
        /// <para>使用 Marshal.AllocHGlobal 分配非托管内存，通过 finally 确保释放。</para>
        /// </summary>
        /// <param name="hDevInfoSet">设备信息集句柄</param>
        /// <param name="interfaceData">设备接口数据（调用前需设置 cbSize）</param>
        /// <param name="devicePath">输出：设备路径字符串（如 "\\?\USB#VID_1A86&PID_7523#..."）</param>
        /// <param name="devInfoData">输出：设备信息数据（调用前 cbSize 已由本方法内部设置）</param>
        /// <returns>成功返回 true；失败返回 false，devicePath 为 null</returns>
        internal static bool TryGetDeviceInterfaceDetail(IntPtr hDevInfoSet, ref SP_DEVICE_INTERFACE_DATA interfaceData, out string devicePath, out SP_DEVINFO_DATA devInfoData)
        {
            devicePath = null;
            devInfoData = default;

            // 第一次调用：获取所需缓冲区大小
            SetupAPI.SetupDiGetDeviceInterfaceDetail(hDevInfoSet, ref interfaceData, IntPtr.Zero, 0, out uint requiredSize, IntPtr.Zero);
            if (requiredSize == 0) return false;

            IntPtr detailBuffer = Marshal.AllocHGlobal((int)requiredSize);
            try
            {
                // 写入 SP_DEVICE_INTERFACE_DETAIL_DATA 的 cbSize（x86: 6, x64: 8）
                Marshal.WriteInt32(detailBuffer, DetailDataCbSize);

                devInfoData = new SP_DEVINFO_DATA();
                devInfoData.cbSize = (uint)Marshal.SizeOf(devInfoData);

                bool success = SetupAPI.SetupDiGetDeviceInterfaceDetail(hDevInfoSet, ref interfaceData, detailBuffer, requiredSize, out requiredSize, ref devInfoData);
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
        /// 通过 CfgMgr32.CM_Get_Device_ID 获取指定设备实例的完整设备 ID 字符串。
        /// <para>返回格式如 "USB\VID_1A86&amp;PID_7523\5&amp;2C8A7B6F&amp;0&amp;3"。</para>
        /// </summary>
        /// <param name="devInst">设备实例句柄（SP_DEVINFO_DATA.DevInst）</param>
        /// <returns>设备 ID 字符串；调用失败返回 null</returns>
        internal static string GetDeviceInstanceId(uint devInst)
        {
            var buffer = new StringBuilder(256);
            int result = CfgMgr32.CM_Get_Device_ID(devInst, buffer, buffer.Capacity, 0);
            return result == 0 ? buffer.ToString() : null;
        }

        /// <summary>
        /// 获取设备注册表属性的原始字节数据，自动处理缓冲区不足的情况。
        /// <para>先用 256 字节缓冲区尝试，若 requiredSize 更大则重新分配并重试。</para>
        /// </summary>
        /// <param name="deviceInfoSet">设备信息集句柄</param>
        /// <param name="deviceInfoData">设备信息数据</param>
        /// <param name="property">属性 ID（SPDRP_*）</param>
        /// <param name="regType">输出：注册表值类型（REG_SZ / REG_MULTI_SZ / REG_DWORD 等）</param>
        /// <returns>属性数据字节数组；获取失败返回 null</returns>
        internal static byte[] GetPropertyBuffer(IntPtr deviceInfoSet, ref SP_DEVINFO_DATA deviceInfoData, uint property, out uint regType)
        {
            regType = 0;
            byte[] buffer = new byte[256];
            bool success = SetupAPI.SetupDiGetDeviceRegistryProperty(deviceInfoSet, ref deviceInfoData, property, out regType, buffer, (uint)buffer.Length, out uint requiredSize);

            if (success && requiredSize > 0)
            {
                Array.Resize(ref buffer, (int)requiredSize);
                return buffer;
            }

            if (requiredSize > 0)
            {
                buffer = new byte[requiredSize];
                success = SetupAPI.SetupDiGetDeviceRegistryProperty(deviceInfoSet, ref deviceInfoData, property, out regType, buffer, (uint)buffer.Length, out requiredSize);
                if (success && requiredSize > 0)
                {
                    Array.Resize(ref buffer, (int)requiredSize);
                    return buffer;
                }
            }
            return null;
        }

        /// <summary>
        /// 获取设备注册表属性字符串（REG_SZ / REG_EXPAND_SZ）。
        /// <para>自动去除末尾 '\0'，类型不匹配返回 null。</para>
        /// </summary>
        /// <param name="hDevInfoSet">设备信息集句柄</param>
        /// <param name="devInfoData">设备信息数据</param>
        /// <param name="property">属性 ID（SPDRP_*）</param>
        /// <returns>属性字符串；属性不存在或类型不匹配返回 null</returns>
        internal static string GetStringProperty(IntPtr hDevInfoSet, ref SP_DEVINFO_DATA devInfoData, uint property)
        {
            var buffer = GetPropertyBuffer(hDevInfoSet, ref devInfoData, property, out uint regType);
            if (buffer == null || buffer.Length == 0) return null;
            if (regType != SetupAPI.REG_SZ && regType != SetupAPI.REG_EXPAND_SZ) return null;

            var length = buffer.Length / 2;
            string result;
            try
            {
                result = Encoding.Unicode.GetString(buffer, 0, length * 2);
            }
            catch (Exception)
            {
                return null;
            }

            int index = result.IndexOf('\0');
            if (index >= 0) result = result.Substring(0, index);
            return result;
        }

        /// <summary>
        /// 获取设备注册表属性多字符串列表（REG_MULTI_SZ）。
        /// <para>按 '\0' 分割，去除空条目。类型不匹配返回空数组。</para>
        /// </summary>
        /// <param name="hDevInfoSet">设备信息集句柄</param>
        /// <param name="devInfoData">设备信息数据</param>
        /// <param name="property">属性 ID（SPDRP_*）</param>
        /// <returns>字符串数组；属性不存在或类型不匹配返回空数组</returns>
        internal static string[] GetMultiStringProperty(IntPtr hDevInfoSet, ref SP_DEVINFO_DATA devInfoData, uint property)
        {
            var buffer = GetPropertyBuffer(hDevInfoSet, ref devInfoData, property, out uint regType);

            if (buffer == null || buffer.Length == 0) return Array.Empty<string>();
            if (regType != SetupAPI.REG_MULTI_SZ) return Array.Empty<string>();

            var length = buffer.Length / 2;
            string result;
            try
            {
                result = Encoding.Unicode.GetString(buffer, 0, length * 2);
            }
            catch (Exception)
            {
                return Array.Empty<string>();
            }

            return result.Split(new char[] { '\0' }, StringSplitOptions.RemoveEmptyEntries);
        }

        /// <summary>
        /// 获取设备注册表属性 32 位无符号整数（REG_DWORD）。
        /// </summary>
        /// <param name="hDevInfoSet">设备信息集句柄</param>
        /// <param name="devInfoData">设备信息数据</param>
        /// <param name="property">属性 ID（SPDRP_*）</param>
        /// <returns>属性值；属性不存在、类型不匹配或长度不足返回 null</returns>
        internal static uint? GetUInt32Property(IntPtr hDevInfoSet, ref SP_DEVINFO_DATA devInfoData, uint property)
        {
            var buffer = GetPropertyBuffer(hDevInfoSet, ref devInfoData, property, out uint regType);

            if (buffer == null || buffer.Length == 0) return null;
            if (regType != SetupAPI.REG_DWORD) return null;

            if (buffer.Length < 4) return null;
            return BitConverter.ToUInt32(buffer, 0);
        }

        /// <summary>
        /// 获取设备注册表属性 GUID 值（存储为 REG_SZ 格式的 GUID 字符串，如 SPDRP_CLASSGUID）。
        /// </summary>
        /// <param name="hDevInfoSet">设备信息集句柄</param>
        /// <param name="devInfoData">设备信息数据</param>
        /// <param name="property">属性 ID（SPDRP_*）</param>
        /// <returns>解析后的 GUID；解析失败返回 Guid.Empty</returns>
        internal static Guid GetGuidProperty(IntPtr hDevInfoSet, ref SP_DEVINFO_DATA devInfoData, uint property)
        {
            var value = GetStringProperty(hDevInfoSet, ref devInfoData, property);
            if(string.IsNullOrWhiteSpace(value)) return Guid.Empty;

            if (Guid.TryParse(value, out Guid guid)) return guid;
            return Guid.Empty;
        }
        #endregion

        #region 填充核心属性
        /// <summary>
        /// 填充核心属性（每个设备枚举必然调用的最小集）。
        /// </summary>
        internal static void FillCoreInfo(IntPtr hDevInfoSet, ref SP_DEVINFO_DATA devInfo, DeviceInfo device)
        {
            device.DevInst = devInfo.DevInst;
            device.ClassGuid = devInfo.ClassGuid;
            device.InstanceId = GetDeviceInstanceId(devInfo.DevInst);
            device.FriendlyName = GetStringProperty(hDevInfoSet, ref devInfo, (uint)SPDRP.FRIENDLYNAME);
            device.ClassName = GetStringProperty(hDevInfoSet, ref devInfo, (uint)SPDRP.CLASS);
            device.HardwareIds = GetMultiStringProperty(hDevInfoSet, ref devInfo, (uint)SPDRP.HARDWAREID);
        }
        #endregion

        #region 公共查询方法
        /// <summary>
        /// 枚举系统中所有可用的串口设备信息。
        /// <para>基于 SetupAPI.GUID_DEVCLASS_PORTS 设备安装类枚举（SetupAPI.SetupDiEnumDeviceInfo，不遍历接口层，无法获取 DevicePath）。</para>
        /// </summary>
        public static IReadOnlyList<SerialDeviceInfo> GetSerialDevices() => EnumerateDeviceInfos(SetupAPI.GUID_DEVCLASS_PORTS, (hDevInfoSet, devInfo) =>
        {
            var deviceInfo = new SerialDeviceInfo();
            FillCoreInfo(hDevInfoSet, ref devInfo, deviceInfo);

            string portName = string.Empty;
            if (!string.IsNullOrEmpty(deviceInfo.FriendlyName))
            {
                string parsed = ExtractPortNameFromFriendlyName(deviceInfo.FriendlyName);
                if (!string.IsNullOrEmpty(parsed)) portName = parsed;
            }

            ExtractVidPidFromHardwareId(deviceInfo.HardwareId, out string vid, out string pid);
            deviceInfo.Vid = vid;
            deviceInfo.Pid = pid;
            deviceInfo.PortName = portName;
            deviceInfo.SerialNumber = ExtractSerialNumberFromInstanceId(deviceInfo.InstanceId);

            return deviceInfo;
        });

        /// <summary>
        /// 通过指定的设备接口 GUID 枚举设备，返回基础 <see cref="DeviceInfo"/> 列表。
        /// <para>适用于自定义 GUID 或未提供专用查询方法的设备接口类型。</para>
        /// <para>仅填充核心属性（DevInst、InstanceId、FriendlyName、DevicePath、ClassGuid、ClassName、HardwareIds），
        /// 不包含 VID/PID/PortName 等子类特有属性。</para>
        /// </summary>
        /// <param name="devInterfaceGuid">设备接口类 GUID</param>
        /// <returns>设备基础信息列表；枚举异常或设备为空时返回空列表</returns>
        public static IReadOnlyList<DeviceInfo> GetDeviceInterfaces(Guid devInterfaceGuid) => EnumerateDeviceInterfaces(devInterfaceGuid, (hDevInfoSet, devInfo, devPath) =>
        {
            var deviceInfo = new DeviceInfo();
            deviceInfo.DevicePath = devPath;
            FillCoreInfo(hDevInfoSet, ref devInfo, deviceInfo);

            return deviceInfo;
        });

        /// <summary>
        /// 枚举系统中所有可用的 USB 设备信息。
        /// <para>基于 SetupAPI.GUID_DEVINTERFACE_USB_DEVICE 枚举设备接口。</para>
        /// </summary>
        public static IReadOnlyList<UsbDeviceInfo> GetUsbDevices() => EnumerateDeviceInterfaces(SetupAPI.GUID_DEVINTERFACE_USB_DEVICE, (hDevInfoSet, devInfo, devPath) =>
        {
            var deviceInfo = new UsbDeviceInfo();
            deviceInfo.DevicePath = devPath;
            FillCoreInfo(hDevInfoSet, ref devInfo, deviceInfo);

            ExtractVidPidFromHardwareId(deviceInfo.HardwareId, out string vid, out string pid);
            deviceInfo.Vid = vid;
            deviceInfo.Pid = pid;
            deviceInfo.SerialNumber = ExtractSerialNumberFromInstanceId(deviceInfo.InstanceId);

            return deviceInfo;
        });

        /// <summary>
        /// 枚举系统中所有可用的物理磁盘设备信息（轻量属性）。
        /// <para>基于 SetupAPI.GUID_DEVINTERFACE_DISK 枚举设备接口。</para>
        /// <para>重量属性（SerialNumber/SizeBytes 等）需通过 CreateFile + DeviceIoControl 额外填充。</para>
        /// </summary>
        public static IReadOnlyList<DiskDeviceInfo> GetDiskDevices() => EnumerateDeviceInterfaces(SetupAPI.GUID_DEVINTERFACE_DISK, (hDevInfoSet, devInfo, devPath) =>
        {
            var deviceInfo = new DiskDeviceInfo();
            deviceInfo.DevicePath = devPath;
            FillCoreInfo(hDevInfoSet, ref devInfo, deviceInfo);

            if (!string.IsNullOrWhiteSpace(deviceInfo.InstanceId))
            {
                var match = Regex.Match(deviceInfo.InstanceId, @"(\d+)$");
                if (match.Success && int.TryParse(match.Groups[1].Value, out int index))
                {
                    deviceInfo.DiskNumber = index;
                }
            }

            return deviceInfo;
        });

        /// <summary>
        /// 枚举系统中所有可用的逻辑卷设备信息（轻量属性）。
        /// <para>基于 SetupAPI.GUID_DEVINTERFACE_VOLUME 枚举设备接口。</para>
        /// <para>重量属性（DriveLetter/Label/FileSystem 等）需通过 GetVolumePathNamesForVolumeName 等 API 额外填充。</para>
        /// </summary>
        public static IReadOnlyList<VolumeDeviceInfo> GetVolumeDevices() => EnumerateDeviceInterfaces(SetupAPI.GUID_DEVINTERFACE_VOLUME, (hDevInfoSet, devInfo, devPath) =>
        {
            var deviceInfo = new VolumeDeviceInfo();
            deviceInfo.DevicePath = devPath;
            FillCoreInfo(hDevInfoSet, ref devInfo, deviceInfo);

            if (!string.IsNullOrWhiteSpace(devPath))
                deviceInfo.VolumeName = devPath.TrimEnd('\\');

            return deviceInfo;
        });
        #endregion

        #region 辅助解析方法
        /// <summary>
        /// 从设备友好名称中提取串口号。
        /// <para>如 "USB-SERIAL CH340 (COM3)" → "COM3"、"Communications Port (COM1)" → "COM1"。</para>
        /// <para>匹配规则：查找最后一个 "COM"（不区分大小写），后跟 1~3 位数字。</para>
        /// </summary>
        /// <param name="friendlyName">设备友好名称</param>
        /// <returns>串口号（如 "COM3"）；无法提取返回 null</returns>
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
        /// <para>实例 ID 格式为 "...\XXXX"，提取最后一个 '\' 后的部分。</para>
        /// <para>若提取结果包含 '&amp;' 字符，判定为 Windows 自动生成的伪序列号，返回 null。</para>
        /// </summary>
        /// <param name="instanceId">设备实例 ID 字符串</param>
        /// <returns>硬件序列号；无效或为伪序列号返回 null</returns>
        internal static string ExtractSerialNumberFromInstanceId(string instanceId)
        {
            if (string.IsNullOrEmpty(instanceId)) return null;

            int pos = instanceId.LastIndexOf('\\');
            if (pos < 0) return null;

            string value = instanceId.Substring(pos + 1);
            if (value.Contains("&")) return null;

            return value;
        }

        /// <summary>
        /// 从硬件 ID 字符串中解析 USB 设备的 VID 和 PID。
        /// <para>识别 "VID_XXXX" 和 "PID_XXXX" 模式（不区分大小写），提取其后 4 位十六进制标识符。</para>
        /// </summary>
        /// <param name="hardwareId">硬件 ID 字符串（如 "USB\VID_1A86&amp;PID_7523"）</param>
        /// <param name="vid">输出：Vendor ID（不含 "VID_" 前缀）；解析失败为 null</param>
        /// <param name="pid">输出：Product ID（不含 "PID_" 前缀）；解析失败为 null</param>
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

        /// <summary>
        /// 获取当前计算机上与搜索模式匹配的串行端口号。
        /// <para>若 searchPattern 本身已是 "COM" + 数字的格式（如 "COM3"），直接返回。</para>
        /// <para>否则通过 <see cref="GetSerialDevices"/> 枚举所有串口设备进行匹配（不区分大小写）。</para>
        /// </summary>
        /// <param name="searchPattern">查找匹配模式，如 "CH340"、"USB Serial" 或直接 "COM3"。</param>
        /// <returns>匹配的串口号（如 "COM3"），若未匹配到则返回原始 searchPattern。</returns>
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
