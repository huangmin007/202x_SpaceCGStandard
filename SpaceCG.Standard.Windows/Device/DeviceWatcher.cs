// ============================================================================
// DeviceWatcher.cs
// 设备热插拔监听器（仅 Windows，纯 C#，无 UI 框架依赖）
// 基于 CfgMgr32 的 CM_Register_Notification（Windows 8+），P/Invoke 声明见 SpaceCG.WindowsAPI.CfgMgr32
//
// 设计要点：
//   1. 回调尽快返回、不做阻塞操作：原生回调只做内存解析与派发，友好名称补全
//      （需 I/O 枚举）交给线程池延时执行。
//   2. pContext 传入 GCHandle 还原实例，支持多实例；delegate 存实例字段防 GC。
//   3. 回调可能早于 CM_Register_Notification 返回 → 上下文与委托须提前就绪。
//   4. HCMNOTIFICATION 用 SafeHandle 管理；CM_Unregister_Notification 会等待未决
//      回调，禁止在回调线程内 Dispose（死锁）。
//   5. 注册不会为已存在接口发通知 → 由 GetPresentInterfaces 在注册后枚举。
//
// 用法（WPF）：
//   在 UI 线程创建并 Start，事件自动 marshal 回创建线程：
//
//   _monitor = new DeviceWatcher();
//   _monitor.DeviceChanged += (sender, e) =>
//   {
//       LogText.AppendText($"[{e.Category}] {(e.IsArrival ? "接入" : "移除")} {e.DeviceName}\r\n");
//   };
//   _monitor.Start();
//   // 窗口关闭时：_monitor.Dispose();
//
// 控制台 / 无同步上下文环境：事件在工作线程直呼，跨线程 UI 请自行 Dispatch。
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using SpaceCG.WindowsAPI.CfgMgr32;

namespace SpaceCG.Device
{
    /// <summary>设备变化类型（设备接口到达 / 移除）</summary>
    public enum DeviceChangeType
    {
        /// <summary>设备接入</summary>
        Arrival,
        /// <summary>设备移除</summary>
        Removal,
    }

    /// <summary>
    /// 设备类别（按设备接口类 GUID 归类）。
    /// 未识别的接口类归入 <see cref="Other"/>，可通过
    /// <see cref="DeviceChangedEventArgs.InterfaceClassGuid"/> 进一步区分。
    /// </summary>
    public enum DeviceCategory
    {
        /// <summary>串口设备（GUID_DEVINTERFACE_COMPORT，COM3 等）</summary>
        Serial,
        /// <summary>磁盘 / 卷设备（GUID_DEVINTERFACE_VOLUME，盘符）</summary>
        Disk,
        /// <summary>USB 设备（GUID_DEVINTERFACE_USB_DEVICE）</summary>
        UsbDevice,
        /// <summary>HID 设备（GUID_DEVINTERFACE_HID）</summary>
        Hid,
        /// <summary>其他 / 未识别的设备接口类</summary>
        Other,
    }

    /// <summary>设备热插拔事件参数（不可变）</summary>
    public sealed class DeviceChangedEventArgs : EventArgs
    {        
        /// <summary>变化类型（接入 / 移除）</summary>
        public DeviceChangeType ChangeType { get; }

        /// <summary>是否为设备接入</summary>
        public bool IsArrival { get { return ChangeType == DeviceChangeType.Arrival; } }

        /// <summary>设备类别</summary>
        public DeviceCategory Category { get; }

        /// <summary>设备接口类 GUID（即时层事件携带原生值；识别层补全事件为对应已知 GUID）</summary>
        public Guid InterfaceClassGuid { get; }

        /// <summary>显示名称：COM3 / E:\ / 设备接口符号链接路径</summary>
        public string DeviceName { get; }

        /// <summary>设备接口符号链接（即时层原始信息）</summary>
        public string DevicePath { get; }

        /// <summary>事件时间</summary>
        public DateTime Time { get; }

        /// <summary>创建设备热插拔事件参数</summary>
        /// <param name="changeType">变化类型（接入 / 移除）</param>
        /// <param name="category">设备类别</param>
        /// <param name="interfaceClassGuid">设备接口类 GUID（未识别时为 Guid.Empty）</param>
        /// <param name="deviceName">显示名称：COM3 / E:\ / 设备接口符号链接</param>
        /// <param name="devicePath">设备接口符号链接（识别层补全事件中与 deviceName 相同）</param>
        /// <param name="time">事件时间</param>
        public DeviceChangedEventArgs(DeviceChangeType changeType, DeviceCategory category, Guid interfaceClassGuid, string deviceName, string devicePath, DateTime time)
        {
            ChangeType = changeType;
            Category = category;
            InterfaceClassGuid = interfaceClassGuid;
            DeviceName = deviceName;
            DevicePath = devicePath;
            Time = time;
        }
    }

    /// <summary>
    /// 设备热插拔监听器（纯 C#，仅 Windows，需 Windows 8 及以上）。
    /// <para>
    /// 线程安全级别：实例方法线程安全（内部加锁）；事件回调在 Start() 调用线程
    /// 触发（存在 SynchronizationContext 时，如 WPF/WinForms UI 线程），否则在
    /// CfgMgr32 原生回调线程触发。
    /// </para>
    /// <para>
    /// 同一进程支持多个实例。不可在事件回调内调用 <see cref="Dispose"/>。
    /// </para>
    /// </summary>
    public sealed class DeviceWatcher : IDisposable
    {
        /// <summary>
        /// 设备变化事件（在 Start() 调用线程回调，无同步上下文时为原生回调线程）。
        /// <para>同一物理设备可能先后触发两次事件：先即时层（符号链接路径），后识别层（COM3 / 盘符等友好名称）。</para>
        /// </summary>
        public event EventHandler<DeviceChangedEventArgs> DeviceChanged;

        // ---------------- 常用设备接口类 GUID（公开，供按类订阅与结果区分） ----------------

        /// <summary>GUID_DEVINTERFACE_VOLUME（磁盘/卷）</summary>
        public static readonly Guid VolumeInterfaceClass = new Guid("53F5630D-B6BF-11D0-94F2-00A0C91EFB8B");
        /// <summary>GUID_DEVINTERFACE_COMPORT（串口）</summary>
        public static readonly Guid ComPortInterfaceClass = new Guid("86E0D1E0-8089-11D0-9CE4-08003E301F73");
        /// <summary>GUID_DEVINTERFACE_USB_DEVICE（USB 设备）</summary>
        public static readonly Guid UsbDeviceInterfaceClass = new Guid("A5DCBF10-6530-11D2-901F-00C04FB951ED");
        /// <summary>GUID_DEVINTERFACE_HID（HID 设备）</summary>
        public static readonly Guid HidInterfaceClass = new Guid("4D1E55B2-F16F-11CF-88CB-001111000030");

        // ---------------- 常量 ----------------
        private const uint ERROR_SUCCESS = 0;
        private const uint CM_NOTIFY_FILTER_FLAG_ALL_INTERFACE_CLASSES = 0x00000001;

        /// <summary>接入事件的延时快照延迟（毫秒），等设备列表就绪</summary>
        private const int ArrivalSnapshotDelayMs = 300;
        /// <summary>移除事件的延时快照延迟（毫秒），等系统清干净设备列表</summary>
        private const int RemovalSnapshotDelayMs = 1000;

        /// <summary>
        /// HCMNOTIFICATION 原生句柄（SafeHandle 管理，释放时调用 CM_Unregister_Notification）。
        /// </summary>
        private sealed class NativeNotificationHandle : SafeHandle
        {
            /// <summary>创建无效句柄（等待填充）</summary>
            public NativeNotificationHandle() : base(IntPtr.Zero, true) { }

            /// <summary>包装 CM_Register_Notification 返回的原生句柄</summary>
            internal NativeNotificationHandle(IntPtr existingHandle) : base(IntPtr.Zero, true)
            {
                SetHandle(existingHandle);
            }

            /// <summary>句柄无效（未注册或已释放）</summary>
            public override bool IsInvalid { get { return handle == IntPtr.Zero; } }

            /// <summary>由 GC 临界终结调用，不可在回调线程内触发（会等待未决回调导致死锁）</summary>
            protected override bool ReleaseHandle()
            {
                return CfgMgr32.CM_Unregister_Notification(handle) == CfgMgr32.CR_SUCCESS;
            }
        }

        // ---------------- 内部状态 ----------------

        private readonly List<Guid> _interfaceClassGuids;       // 要订阅的设备接口类；为空 = 全部接口类
        private readonly List<NativeNotificationHandle> _notificationHandles = new List<NativeNotificationHandle>();
        private readonly object _deviceListLock = new object();

        private CM_NOTIFY_CALLBACK _nativeCallback;             // 实例字段持有 delegate，防 GC
        private GCHandle _contextHandle;                        // 注册前分配，回调经它还原实例
        private SynchronizationContext _synchronizationContext; // Start() 时捕获，事件 marshal 回调用线程
        private volatile bool _started;
        private bool _disposed;

        // 识别层（串口/盘符快照 diff）状态
        private HashSet<string> _lastSerialPorts = new HashSet<string>();
        private HashSet<string> _lastLogicalDrives = new HashSet<string>();
        private bool _baselineInitialized;
        private bool _snapshotQueued;                           // 防止并发重复排队
        private int _snapshotDelayMs;                           // 已排队快照的最大延迟

        /// <summary>
        /// 创建设备热插拔监听器实例。
        /// </summary>
        /// <param name="interfaceClassGuids">
        /// 要订阅的设备接口类 GUID 集合（如 <see cref="ComPortInterfaceClass"/>）；
        /// 为 null 或空时订阅全部设备接口类（ALL_INTERFACE_CLASSES）。
        /// </param>
        public DeviceWatcher(IEnumerable<Guid> interfaceClassGuids = null)
        {
            _interfaceClassGuids = interfaceClassGuids == null
                ? new List<Guid>()
                : new List<Guid>(interfaceClassGuids.Where(g => g != Guid.Empty).Distinct());
        }

        /// <summary>
        /// 启动监听。请在需要接收事件的线程调用（WPF 在 UI 线程调用，
        /// 事件自动 marshal 回该线程）。
        /// </summary>
        /// <exception cref="InvalidOperationException">重复 Start、已 Dispose，或注册失败（Windows 版本过低/参数非法）</exception>
        public void Start()
        {
            if (_started) throw new InvalidOperationException("DeviceWatcher 已启动。");
            if (_disposed) throw new ObjectDisposedException(nameof(DeviceWatcher));

            _started = true;
            _synchronizationContext = SynchronizationContext.Current;

            // 基线快照：记录当前串口/盘符，避免把已存在设备当"接入"派发
            _lastSerialPorts = GetSerialPortNames();
            _lastLogicalDrives = GetLogicalDriveNames();
            _baselineInitialized = true;

            // 按文档顺序：先注册，后枚举现有接口。
            // 注意：回调可能在 CM_Register_Notification 返回之前就被调用，
            // 因此 GCHandle 与 delegate 必须在注册前就绪。
            _contextHandle = GCHandle.Alloc(this, GCHandleType.Normal);
            _nativeCallback = OnNativeCallback;

            try
            {
                RegisterNotifications();
            }
            catch
            {
                ReleaseNativeResources();
                _started = false;
                throw;
            }
        }

        /// <summary>
        /// 停止监听并释放原生通知句柄。
        /// <para>禁止在事件回调内调用（CM_Unregister_Notification 会等待未决回调完成，导致死锁）。</para>
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            ReleaseNativeResources();
        }

        private void ReleaseNativeResources()
        {
            lock (_deviceListLock)
            {
                foreach (var notificationHandle in _notificationHandles)
                    notificationHandle.Dispose();
                _notificationHandles.Clear();
            }

            if (_contextHandle.IsAllocated)
            {
                _contextHandle.Free();
                _nativeCallback = null;
            }
        }

        // ---------------- 即时层：注册与原生回调 ----------------

        private void RegisterNotifications()
        {
            if (_interfaceClassGuids.Count > 0)
            {
                // 按接口类逐个注册（过滤更精确，回调更少）
                foreach (var interfaceClassGuid in _interfaceClassGuids)
                {
                    RegisterOne(interfaceClassGuid, 0);
                }
            }
            else
            {
                // ALL_INTERFACE_CLASSES：ClassGuid 必须为全零
                RegisterOne(Guid.Empty, CM_NOTIFY_FILTER_FLAG_ALL_INTERFACE_CLASSES);
            }
        }

        private void RegisterOne(Guid interfaceClassGuid, uint flags)
        {
            var filter = CM_NOTIFY_FILTER.CreateDeviceInterface(interfaceClassGuid, flags);

            int configRet = CfgMgr32.CM_Register_Notification(ref filter, GCHandle.ToIntPtr(_contextHandle), _nativeCallback, out IntPtr notifyHandle);
            var notificationHandle = new NativeNotificationHandle(notifyHandle);

            // CR_SUCCESS=0；CR_INVALID_DATA=0x1F 过滤器非法；CR_INVALID_FLAG=0x04 标志非法；
            // CR_INVALID_POINTER=0x03 空指针；CR_OUT_OF_MEMORY=0x02 内存不足
            if (configRet != 0)
                throw new InvalidOperationException($"CM_Register_Notification 失败，CR=0x{configRet:X8}。需 Windows 8+；请检查过滤器定义。");

            _notificationHandles.Add(notificationHandle);
        }

        /// <summary>
        /// CfgMgr32 原生回调（CfgMgr32 工作线程，可能早于 Start() 返回被调用）。
        /// 复杂度 O(1)，只做纯内存解析与派发，禁止阻塞操作。
        /// </summary>
        private uint OnNativeCallback(IntPtr notificationHandle, IntPtr context, CM_NOTIFY_ACTION action, IntPtr eventData, uint eventDataSize)
        {
            // 经 pContext 还原实例（多实例安全）
            var watcher = GCHandle.FromIntPtr(context).Target as DeviceWatcher;
            if (watcher == null || watcher._disposed) return ERROR_SUCCESS;

            var eventArgs = ParseNativeEventData(action, eventData, eventDataSize);
            if (eventArgs == null) return ERROR_SUCCESS;

            watcher.RaiseDeviceChanged(eventArgs);

            // 友好名称补全需要 I/O 枚举（文档要求回调内不做 I/O），丢给线程池延时执行
            watcher.QueueSnapshot(eventArgs.IsArrival ? ArrivalSnapshotDelayMs : RemovalSnapshotDelayMs);

            return ERROR_SUCCESS;
        }

        /// <summary>解析 CM_NOTIFY_EVENT_DATA（联合体，按 FilterType 分支）。复杂度 O(1)</summary>
        private static DeviceChangedEventArgs ParseNativeEventData(CM_NOTIFY_ACTION action, IntPtr eventData, uint eventDataSize)
        {
            if (eventData == IntPtr.Zero || eventDataSize < 8) return null;

            // 仅关注接口到达/移除；其余动作（QueryRemove/CustomEvent 等）不产生事件
            bool arrival = action == CM_NOTIFY_ACTION.DeviceInterfaceArrival;
            if (!arrival && action != CM_NOTIFY_ACTION.DeviceInterfaceRemoval) return null;

            // CM_NOTIFY_EVENT_DATA 头部：FilterType(4B) + Reserved(4B)，联合体起始偏移 8
            var filterType = (CM_NOTIFY_FILTER_TYPE)Marshal.ReadInt32(eventData, 0);

            string devicePath = null;
            Guid interfaceClassGuid = Guid.Empty;

            switch (filterType)
            {
                case CM_NOTIFY_FILTER_TYPE.DeviceInterface:
                    // u.DeviceInterface：GUID ClassGuid（偏移 8）+ WCHAR SymbolicLink[]（偏移 24）
                    if (eventDataSize < 26) return null;
                    var guidBytes = new byte[16];
                    Marshal.Copy(new IntPtr(eventData.ToInt64() + 8), guidBytes, 0, 16);
                    interfaceClassGuid = new Guid(guidBytes);
                    devicePath = Marshal.PtrToStringUni(new IntPtr(eventData.ToInt64() + 24));
                    break;

                case CM_NOTIFY_FILTER_TYPE.DeviceInstance:
                    // u.DeviceInstance：WCHAR InstanceId[]（偏移 8）
                    devicePath = Marshal.PtrToStringUni(new IntPtr(eventData.ToInt64() + 8));
                    break;

                case CM_NOTIFY_FILTER_TYPE.DeviceHandle:
                    // u.DeviceHandle：HANDLE hTarget（偏移 8）
                    devicePath = "0x" + Marshal.ReadIntPtr(eventData, 8).ToInt64().ToString("X");
                    break;
            }

            return new DeviceChangedEventArgs(arrival ? DeviceChangeType.Arrival : DeviceChangeType.Removal,
                Categorize(interfaceClassGuid), interfaceClassGuid, devicePath, devicePath, DateTime.UtcNow);
        }

        private static DeviceCategory Categorize(Guid interfaceClassGuid)
        {
            if (interfaceClassGuid == VolumeInterfaceClass) return DeviceCategory.Disk;
            if (interfaceClassGuid == ComPortInterfaceClass) return DeviceCategory.Serial;
            if (interfaceClassGuid == UsbDeviceInterfaceClass) return DeviceCategory.UsbDevice;
            if (interfaceClassGuid == HidInterfaceClass) return DeviceCategory.Hid;
            return DeviceCategory.Other;
        }

        // ---------------- 枚举现有接口（文档：先注册，后枚举） ----------------

        /// <summary>
        /// 枚举当前已存在的指定设备接口类的全部接口符号链接。
        /// <para>
        /// 对应文档备注：CM_Register_Notification 不会为已存在的设备接口发出通知，
        /// 正确用法是先注册再调用本方法枚举现有接口（两步之间到达的接口既有通知
        /// 也会出现在返回列表中，不会遗漏）。
        /// </para>
        /// </summary>
        /// <param name="interfaceClassGuid">设备接口类 GUID</param>
        /// <returns>接口符号链接列表（失败或无设备时返回空列表）</returns>
        public static IReadOnlyList<string> GetPresentInterfaces(Guid interfaceClassGuid)
        {
            var empty = new string[0];
            if (interfaceClassGuid == Guid.Empty) return empty;

            uint bufferSize = 0;
            int configRet = CfgMgr32.CM_Get_Device_Interface_List_Size(ref bufferSize, ref interfaceClassGuid, IntPtr.Zero, CfgMgr32.CM_GET_DEVICE_INTERFACE_LIST_PRESENT);
            if (configRet != 0 || bufferSize == 0) return empty;

            IntPtr buffer = Marshal.AllocHGlobal((int)bufferSize * 2);   // 多重以 '\0' 结尾的 WCHAR 串
            try
            {
                configRet = CfgMgr32.CM_Get_Device_Interface_List(ref interfaceClassGuid, IntPtr.Zero, buffer, bufferSize, CfgMgr32.CM_GET_DEVICE_INTERFACE_LIST_PRESENT);
                if (configRet != 0) return empty;

                var interfaces = new List<string>();
                long position = buffer.ToInt64();
                long end = position + (long)bufferSize * 2;
                while (position < end)
                {
                    string item = Marshal.PtrToStringUni(new IntPtr(position));
                    if (string.IsNullOrEmpty(item)) break;
                    interfaces.Add(item);
                    position += (item.Length + 1) * 2;
                }
                return interfaces;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        // ---------------- 事件派发 ----------------

        private void RaiseDeviceChanged(DeviceChangedEventArgs eventArgs)
        {
            var handler = DeviceChanged;
            if (handler == null) return;

            var synchronizationContext = _synchronizationContext;
            if (synchronizationContext != null) synchronizationContext.Post(state => handler(this, (DeviceChangedEventArgs)state), eventArgs);   // 回到 Start() 调用线程
            else handler(this, eventArgs);                                       // 无同步上下文：回调线程直呼
        }

        // ---------------- 识别层：延时快照 diff（补全 COM3 / 盘符等友好名称） ----------------

        /// <summary>合并去重的延时快照调度（线程池执行，原生回调线程调用）</summary>
        private void QueueSnapshot(int delayMs)
        {
            lock (_deviceListLock)
            {
                if (_snapshotQueued)
                {
                    _snapshotDelayMs = Math.Max(_snapshotDelayMs, delayMs);
                    return;
                }
                _snapshotQueued = true;
                _snapshotDelayMs = delayMs;
            }

            Task.Run(async () =>
            {
                int delay;
                lock (_deviceListLock) delay = _snapshotDelayMs;
                await Task.Delay(delay);

                try { RefreshDeviceLists(); }
                finally
                {
                    lock (_deviceListLock)
                    {
                        _snapshotQueued = false;
                        _snapshotDelayMs = 0;
                    }
                }
            });
        }

        /// <summary>串口/盘符快照并与上次 diff，派发增减事件。复杂度 O(n)</summary>
        private void RefreshDeviceLists()
        {
            if (_disposed) return;

            HashSet<string> serialPorts;
            HashSet<string> logicalDrives;
            try
            {
                serialPorts = GetSerialPortNames();
                logicalDrives = GetLogicalDriveNames();
            }
            catch (Exception ex) when (ex is IOException || ex is SystemException)
            {
                LogWarning("设备快照失败：" + ex.Message);
                return;
            }

            lock (_deviceListLock)
            {
                if (_baselineInitialized)
                {
                    DiffAndDispatch(serialPorts, _lastSerialPorts, DeviceCategory.Serial, ComPortInterfaceClass);
                    DiffAndDispatch(logicalDrives, _lastLogicalDrives, DeviceCategory.Disk, VolumeInterfaceClass);
                }
                _lastSerialPorts = serialPorts;
                _lastLogicalDrives = logicalDrives;
            }
        }

        /// <summary>枚举当前可用串口名（COM3 等）。静态纯查询，无副作用</summary>
        private static HashSet<string> GetSerialPortNames()
        {
            var portNames = new HashSet<string>();
            try
            {
                foreach (var portName in SerialPort.GetPortNames().Distinct())
                    portNames.Add(portName);
            }
            catch (Exception ex) when (ex is IOException || ex is SystemException)
            {
                LogWarning("枚举串口失败：" + ex.Message);
            }
            return portNames;
        }

        /// <summary>枚举当前就绪的逻辑驱动器名（C:\ 等）。静态纯查询，无副作用</summary>
        private static HashSet<string> GetLogicalDriveNames()
        {
            var driveNames = new HashSet<string>();
            try
            {
                foreach (var drive in DriveInfo.GetDrives())
                {
                    bool isReady;
                    try { isReady = drive.IsReady; }
                    catch (IOException) { isReady = false; }
                    if (isReady) driveNames.Add(drive.Name);
                }
            }
            catch (Exception ex) when (ex is IOException || ex is SystemException)
            {
                LogWarning("枚举磁盘失败：" + ex.Message);
            }
            return driveNames;
        }

        private void DiffAndDispatch(HashSet<string> current, HashSet<string> previous, DeviceCategory category, Guid interfaceClassGuid)
        {
            var now = DateTime.Now;
            foreach (var name in current)
                if (!previous.Contains(name))
                    RaiseDeviceChanged(new DeviceChangedEventArgs(DeviceChangeType.Arrival, category, interfaceClassGuid, name, name, now));
            foreach (var name in previous)
                if (!current.Contains(name))
                    RaiseDeviceChanged(new DeviceChangedEventArgs(DeviceChangeType.Removal, category, interfaceClassGuid, name, name, now));
        }

        private static void LogWarning(string message)
        {
            System.Diagnostics.Debug.WriteLine("[DeviceWatcher] " + message);
        }
    }
}
