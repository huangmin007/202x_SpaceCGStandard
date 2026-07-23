using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Trace = SpaceCG.Diagnostics.Trace;

namespace SpaceCG.Generic
{
    /// <summary>
    /// Device Broadcast Type
    /// <para><see cref="MessageType.WM_DEVICECHANGE"/> wParam Data, device-change event</para>
    /// <para>参考：C:\Program Files (x86)\Windows Kits\10\Include\10.0.18362.0\um  Dbt.h </para>
    /// </summary>
    public enum DeviceBroadcastType
    {
        /// <summary>
        /// appy begin. lParam  = (not used)
        /// </summary>
        DBT_APPYBEGIN = 0x0000,
        /// <summary>
        /// appy end. lParam  = (not used)
        /// </summary>
        DBT_APPYEND = 0x0001,
        /// <summary>
        /// 当 configmg 完成进程树批处理时发送. lParam  = 0
        /// </summary>
        DBT_DEVNODES_CHANGED = 0x0007,
        /// <summary>
        /// sent to ask if a config change is allowed. lParam  = 0
        /// </summary>
        DBT_QUERYCHANGECONFIG = 0x0017,
        /// <summary>
        /// sent when a config has changed, lParam  = 0
        /// </summary>
        DBT_CONFIGCHANGED = 0x0018,
        /// <summary>
        /// someone cancelled the config change, lParam  = 0
        /// </summary>
        DBT_CONFIGCHANGECANCELED = 0x0019,
        /// <summary>
        /// this message is sent when the display monitor has changed and the system should change the display mode to match it.
        /// <para>lParam  = new resolution to use (LOWORD=x, HIWORD=y) if 0, use the default res for current config</para>
        /// </summary>
        DBT_MONITORCHANGE = 0x001B,
        /// <summary>
        /// The shell has finished login on: VxD can now do Shell_EXEC.  lParam  = 0
        /// </summary>
        DBT_SHELLLOGGEDON = 0x0020,
        /// <summary>
        /// lParam  = CONFIGMG API Packet, CONFIGMG ring 3 call.
        /// </summary>
        DBT_CONFIGMGAPI32 = 0x0022,
        /// <summary>
        /// CONFIGMG ring 3 call. lParam  = 0
        /// </summary>
        DBT_VXDINITCOMPLETE = 0x0023,
        /// <summary>
        /// Message = WM_DEVICECHANGE, wParam  = DBT_VOLLOCK*, lParam  = pointer to VolLockBroadcast structure described below.
        /// <para>Messages issued by IFSMGR for volume locking purposes on WM_DEVICECHANGE. All these messages pass a pointer to a struct which has no pointers.</para>
        /// </summary>
        DBT_VOLLOCKQUERYLOCK = 0x8041,
        /// <summary>
        /// Message = WM_DEVICECHANGE, wParam  = DBT_VOLLOCK*, lParam  = pointer to VolLockBroadcast structure described below.
        /// <para>Messages issued by IFSMGR for volume locking purposes on WM_DEVICECHANGE. All these messages pass a pointer to a struct which has no pointers.</para>
        /// </summary>
        DBT_VOLLOCKLOCKTAKEN = 0x8042,
        /// <summary>
        /// Message = WM_DEVICECHANGE, wParam  = DBT_VOLLOCK*, lParam  = pointer to VolLockBroadcast structure described below.
        /// <para>Messages issued by IFSMGR for volume locking purposes on WM_DEVICECHANGE. All these messages pass a pointer to a struct which has no pointers.</para>
        /// </summary>
        DBT_VOLLOCKLOCKFAILED = 0x8043,
        /// <summary>
        /// Message = WM_DEVICECHANGE, wParam  = DBT_VOLLOCK*, lParam  = pointer to VolLockBroadcast structure described below.
        /// <para>Messages issued by IFSMGR for volume locking purposes on WM_DEVICECHANGE. All these messages pass a pointer to a struct which has no pointers.</para>
        /// </summary>
        DBT_VOLLOCKQUERYUNLOCK = 0x8044,
        /// <summary>
        /// Message = WM_DEVICECHANGE, wParam  = DBT_VOLLOCK*, lParam  = pointer to VolLockBroadcast structure described below.
        /// <para>Messages issued by IFSMGR for volume locking purposes on WM_DEVICECHANGE. All these messages pass a pointer to a struct which has no pointers.</para>
        /// </summary>
        DBT_VOLLOCKLOCKRELEASED = 0x8045,
        /// <summary>
        /// Message = WM_DEVICECHANGE, wParam  = DBT_VOLLOCK*, lParam  = pointer to VolLockBroadcast structure described below.
        /// <para>Messages issued by IFSMGR for volume locking purposes on WM_DEVICECHANGE. All these messages pass a pointer to a struct which has no pointers.</para>
        /// </summary>
        DBT_VOLLOCKUNLOCKFAILED = 0x8046,
        /// <summary>
        /// Message issued by IFS manager when it detects that a drive is run out of free space. lParam = drive number of drive that is out of disk space (1-based)
        /// </summary>
        DBT_NO_DISK_SPACE = 0x0047,
        /// <summary>
        /// lParam  = drive number of drive that is low on disk space (1-based)
        /// </summary>
        DBT_LOW_DISK_SPACE = 0x0048,
        /// <summary>
        /// configmg private
        /// </summary>
        DBT_CONFIGMGPRIVATE = 0x7FFF,
        /// <summary>
        /// system detected a new device
        /// </summary>
        DBT_DEVICEARRIVAL = 0x8000,
        /// <summary>
        /// wants to remove, may fail
        /// </summary>
        DBT_DEVICEQUERYREMOVE = 0x8001,
        /// <summary>
        /// removal aborted
        /// </summary>
        DBT_DEVICEQUERYREMOVEFAILED = 0x8002,
        /// <summary>
        /// Device Move Complete
        /// </summary>
        DBT_DEVICEREMOVECOMPLETE = 0x8004,
        /// <summary>
        /// type specific event
        /// </summary>
        DBT_DEVICETYPESPECIFIC = 0x8005,
        /// <summary>
        /// user-defined event
        /// </summary>
        DBT_CUSTOMEVENT = 0x8006,
        /// <summary>
        /// (WIN7) system detected a new device
        /// </summary>
        DBT_DEVINSTENUMERATED = 0x8007,
        /// <summary>
        /// (WIN7) device installed and started
        /// </summary>
        DBT_DEVINSTSTARTED = 0x8008,
        /// <summary>
        /// (WIN7) device removed from system
        /// </summary>
        DBT_DEVINSTREMOVED = 0x8009,
        /// <summary>
        /// (WIN7) a property on the device changed
        /// </summary>
        DBT_DEVINSTPROPERTYCHANGED = 0x800A,
        /// <summary>
        /// User defined
        /// </summary>
        DBT_USERDEFINED = 0xFFFF,
    }

    /// <summary>
    /// <see cref="DEV_BROADCAST_HDR"/> 结构体字段 <see cref="DEV_BROADCAST_HDR.dbch_devicetype"/> 的值之一
    /// <para>参考：https://docs.microsoft.com/en-us/windows/win32/api/dbt/ns-dbt-dev_broadcast_hdr </para>
    /// </summary>
    public enum DeviceType : uint
    {
        /// <summary>
        /// oem-defined device type, OEM 或 IHV 定义的设备类型。此结构是 <see cref="DEV_BROADCAST_OEM"/> 结构。
        /// </summary>
        DBT_DEVTYP_OEM = 0x00000000,
        /// <summary>
        /// devnode number
        /// </summary>
        DBT_DEVTYP_DEVNODE = 0x00000001,
        /// <summary>
        /// logical volume, 逻辑卷。此结构是 <see cref="DEV_BROADCAST_VOLUME"/> 结构。
        /// </summary>
        DBT_DEVTYP_VOLUME = 0x00000002,
        /// <summary>
        /// serial/parallel, 端口设备（串行或并行）。此结构是 <see cref="DEV_BROADCAST_PORT"/> 结构。
        /// </summary>
        DBT_DEVTYP_PORT = 0x00000003,
        /// <summary>
        /// network resource
        /// </summary>
        DBT_DEVTYP_NET = 0x00000004,
        /// <summary>
        /// device interface class, 设备类别。此结构是 <see cref="DEV_BROADCAST_DEVICEINTERFACE"/> 结构。
        /// </summary>
        DBT_DEVTYP_DEVICEINTERFACE = 0x00000005,
        /// <summary>
        /// file system handle, 文件系统句柄。此结构是 <see cref="DEV_BROADCAST_HANDLE"/> 结构。
        /// </summary>
        DBT_DEVTYP_HANDLE = 0x00000006,
        /// <summary>
        /// device instance
        /// </summary>
        DBT_DEVTYP_DEVINST = 0x00000007,
    }

    #region WinAPI 声明 (Private Nested Class)
    /// <summary>
    /// Windows API 声明（P/Invoke、结构体、常量、委托），全部内聚以消除外部依赖。
    /// <para>参考：https://docs.microsoft.com/en-us/windows/win32/api/winuser/ </para>
    /// </summary>
    internal static partial class NativeMethods
    {
        // ==================== 常量 ====================
        /// <summary>关闭窗口消息</summary>
        public const uint WM_CLOSE = 0x0010;

        /// <summary>窗口销毁消息</summary>
        public const uint WM_DESTROY = 0x0002;

        /// <summary>用户自定义消息起始值</summary>
        public const uint WM_USER = 0x0400;

        public const uint WM_DEVICECHANGE = 0x0219;
        public const uint WM_POWERBROADCAST = 0x0218;

        /// <summary>
        /// 消息窗口父句柄常量（值为 -3），使窗口成为仅消息窗口（Message-only Window），
        /// 不可见且不接收广播消息。
        /// </summary>
        public static readonly IntPtr HWND_MESSAGE = new IntPtr(-3);

        /// <summary>
        /// 磁盘设备接口 GUID（GUID_DEVINTERFACE_DISK）。
        /// </summary>
        public static readonly Guid GUID_DEVINTERFACE_DISK = new Guid("53F56307-B6BF-11D0-94F2-00A0C91EFB8B");

        /// <summary>
        /// 串口设备接口 GUID（GUID_DEVINTERFACE_COMPORT）。
        /// </summary>
        public static readonly Guid GUID_DEVINTERFACE_COMPORT = new Guid("86E0D1E0-8089-11D0-9CE4-08003E301F73");

        // ==================== 委托 ====================
        /// <summary>
        /// 窗口过程委托，标准调用约定（StdCall）。
        /// </summary>
        /// <param name="hWnd">窗口句柄</param>
        /// <param name="msg">消息标识</param>
        /// <param name="wParam">消息附加参数</param>
        /// <param name="lParam">消息附加参数</param>
        /// <returns>消息处理结果</returns>
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        public delegate IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        /// <summary>
        /// <see cref="User32.RegisterDeviceNotification"/> 函数参考 Flags 的参数之一
        /// <para>参考： https://docs.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-registerdevicenotificationa </para>
        /// </summary>
        public enum DeviceNotifyFlag : uint
        {
            /// <summary>
            /// 该 hRecipient 参数是一个窗口句柄。
            /// </summary>
            DEVICE_NOTIFY_WINDOW_HANDLE = 0x00000000,
            /// <summary>
            /// 该 hRecipient 参数是服务状态句柄。
            /// </summary>
            DEVICE_NOTIFY_SERVICE_HANDLE = 0x00000001,
            /// <summary>
            /// 通知接收者所有设备接口类的设备接口事件。(dbcc_classguid成员将被忽略。) 仅当 dbch_devicetype 成员是 <see cref="DeviceType.DBT_DEVTYP_DEVICEINTERFACE"/> 时，才可以使用此值。
            /// </summary>
            DEVICE_NOTIFY_ALL_INTERFACE_CLASSES = 0x00000004,
        }

        #region Structures
        /// <summary>
        /// 用作与通过 <see cref="MessageType.WM_DEVICECHANGE"/> 消息报告的设备事件相关的信息的标准标头 。
        /// <para>WM_DEVICECHANGE lParam Data, event-specific data</para>
        /// <para>由于此结构包含可变长度字段，因此可以将其用作创建指向用户定义结构的指针的模板。请注意，该结构不得包含指针。示：<see cref="DEV_BROADCAST_USERDEFINED"/>, <see cref="DEV_BROADCAST_PORT"/>, <see cref="DEV_BROADCAST_VOLUME"/>, <see cref="DEV_BROADCAST_OEM"/> 等</para>
        /// <para> <see cref="DEV_BROADCAST_HDR"/> 结构的成员 包含在每个设备管理结构中。要确定您通过 <see cref="MessageType.WM_DEVICECHANGE"/> 接收到的结构，请将其视为 <see cref="DEV_BROADCAST_HDR"/> 结构并检查其 dbch_devicetype 成员。</para>
        /// <para>参考：https://docs.microsoft.com/en-us/windows/win32/api/dbt/ns-dbt-dev_broadcast_hdr </para>
        /// </summary>
        public struct DEV_BROADCAST_HDR
        {
            /// <summary>
            /// 此结构的大小，以字节为单位。
            /// <para>如果这是用户定义的事件，则此成员必须是此标头的大小，加上 <see cref="DEV_BROADCAST_USERDEFINED"/> 结构中的可变长度数据的 大小。</para>
            /// </summary>
            public uint dbch_size;
            /// <summary>
            /// 设备类型，确定跟随前三个成员的事件特定信息。
            /// </summary>
            public DeviceType dbch_devicetype;
            /// <summary>
            /// 保留，不使用。
            /// </summary>
            public uint dbch_reserved;

            /// <summary>
            /// <see cref="DEV_BROADCAST_HDR"/> 结构数据大小，以字节为单位。
            /// </summary>
            public static readonly uint Size = (uint)Marshal.SizeOf(typeof(DEV_BROADCAST_HDR));

            /// <summary>
            /// @ToString()
            /// </summary>
            /// <returns></returns>
            public override string ToString()
            {
                return $"[DEV_BROADCAST_HDR] size:{dbch_size}, devicetype:{dbch_devicetype}";
            }
        }

        /// <summary>
        /// 包含有关调制解调器，串行或并行端口的信息。(DEV_BROADCAST_PORT_A, *PDEV_BROADCAST_PORT_A)
        /// <para>参考：https://docs.microsoft.com/zh-cn/windows/win32/api/dbt/ns-dbt-dev_broadcast_port_a </para>
        /// </summary>
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        public struct DEV_BROADCAST_PORT
        {
            /// <summary>
            /// 有关受 <see cref="DEV_BROADCAST_HDR"/> 结构指定的 <see cref="MessageType.WM_DEVICECHANGE"/> 消息影响的设备的信息。
            /// </summary>
            public DEV_BROADCAST_HDR dbcp_head;
            /// <summary>
            /// 以空值结尾的字符串，用于指定端口或连接到该端口的设备的友好名称。友好名称旨在帮助用户快速准确地识别设备-例如，"COM1" 和 "Standard 28800 bps Modem" 被视为友好名称。
            /// </summary>
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
            public string dbcp_name;
            /// <summary>
            /// @ToString()
            /// </summary>
            /// <returns></returns>
            public override string ToString()
            {
                return $"[DEV_BROADCAST_PORT] head:[{dbcp_head}], name:{dbcp_name}";
            }
        }

        /// <summary>
        /// 包含有关逻辑卷的信息。
        /// <para>尽管 dbcv_unitmask 成员可以在任何消息中指定多个卷，但这不能保证为指定事件仅生成一个消息。多个系统功能部件可以同时独立地为逻辑卷生成消息。</para>
        /// <para>仅在支持软弹出机制的设备中为媒体发送用于媒体到达和删除的消息。例如，应用程序将看不到软盘的与介质相关的卷消息。每当发出网络命令时，就不会发送网络驱动器到达和卸下的消息，而是当网络连接由于硬件事件而消失时发送。</para>
        /// <para>参考：https://docs.microsoft.com/zh-cn/windows/win32/api/dbt/ns-dbt-dev_broadcast_volume </para>
        /// </summary>
        public struct DEV_BROADCAST_VOLUME
        {
            /// <summary>
            /// 有关受 <see cref="DEV_BROADCAST_HDR"/> 结构指定的 <see cref="MessageType.WM_DEVICECHANGE"/> 消息影响的设备的信息。
            /// </summary>
            public DEV_BROADCAST_HDR dbcv_head;
            /// <summary>
            /// 逻辑单元掩码标识一个或多个逻辑单元。掩码中的每一位对应一个逻辑驱动器。位0代表驱动器A，位1代表驱动器B，依此类推。
            /// </summary>
            public uint dbcv_unitmask;
            /// <summary>
            /// 此参数可以是下列值之一。
            /// <para>DBTF_MEDIA  0x0001 更改会影响驱动器中的介质。如果未设置，则更改会影响物理设备或驱动器。</para>
            /// <para>DBTF_NET    0x0002  指示的逻辑卷是网络卷。</para>
            /// </summary>
            public ushort dbcv_flags;
            /// <summary>
            /// @ToString()
            /// </summary>
            /// <returns></returns>
            public override string ToString()
            {
                return $"[DEV_BROADCAST_VOLUME] head:[{dbcv_head}], mask:{dbcv_unitmask}, flags:{dbcv_flags}";
            }
        }

        /// <summary>
        /// 包含有关一类设备的信息。(DEV_BROADCAST_DEVICEINTERFACE_A, *PDEV_BROADCAST_DEVICEINTERFACE_A)
        /// <para>参考：https://docs.microsoft.com/zh-cn/windows/win32/api/dbt/ns-dbt-dev_broadcast_deviceinterface_a </para>
        /// </summary>
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        public struct DEV_BROADCAST_DEVICEINTERFACE
        {
            /// <summary>
            /// 有关受 <see cref="DEV_BROADCAST_HDR"/> 结构指定的 <see cref="MessageType.WM_DEVICECHANGE"/> 消息影响的设备的信息。
            /// </summary>
            public DEV_BROADCAST_HDR dbcc_head;
            /// <summary>
            /// 接口设备类的 GUID。
            /// </summary>
            public Guid dbcc_classguid;
            /// <summary>
            /// 以空值结尾的字符串，用于指定设备的名称。
            /// <para>通过 <see cref="MessageType.WM_DEVICECHANGE"/> 消息将此结构返回到窗口时，dbcc_name 字符串将适当地转换为 ANSI。服务始终会收到 Unicode 字符串，无论它们调用 <see cref="RegisterDeviceNotificationW"/> 还是 <see cref="RegisterDeviceNotificationA"/>。</para>
            /// </summary>
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 255)]
            public string dbcc_name;
            /// <summary>
            /// @ToString()
            /// </summary>
            /// <returns></returns>
            public override string ToString()
            {
                return $"[DEV_BROADCAST_DEVICEINTERFACE] head:[{dbcc_head}], guid:{dbcc_classguid}, name:{dbcc_name}";
            }
        }

        // ==================== 结构体 ====================
        /// <summary>
        /// 扩展窗口类结构体，用于 <see cref="RegisterClassEx"/>。
        /// <para>参考：https://docs.microsoft.com/en-us/windows/win32/api/winuser/ns-winuser-wndclassexw </para>
        /// </summary>
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        public struct WNDCLASSEX
        {
            /// <summary>结构体大小</summary>
            public uint cbSize;

            /// <summary>窗口类样式</summary>
            public uint style;

            /// <summary>窗口过程函数指针</summary>
            public WndProc lpfnWndProc;

            /// <summary>额外类内存字节数</summary>
            public int cbClsExtra;

            /// <summary>额外窗口内存字节数</summary>
            public int cbWndExtra;

            /// <summary>实例句柄</summary>
            public IntPtr hInstance;

            /// <summary>图标句柄</summary>
            public IntPtr hIcon;

            /// <summary>光标句柄</summary>
            public IntPtr hCursor;

            /// <summary>背景画刷句柄</summary>
            public IntPtr hbrBackground;

            /// <summary>菜单名称</summary>
            public string lpszMenuName;

            /// <summary>窗口类名称</summary>
            public string lpszClassName;

            /// <summary>小图标句柄</summary>
            public IntPtr hIconSm;
        }

        /// <summary>
        /// Windows 消息结构体。
        /// <para>参考：https://docs.microsoft.com/en-us/windows/win32/api/winuser/ns-winuser-msg </para>
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct MSG
        {
            /// <summary>目标窗口句柄</summary>
            public IntPtr hwnd;

            /// <summary>消息标识</summary>
            public uint message;

            /// <summary>消息附加参数</summary>
            public IntPtr wParam;

            /// <summary>消息附加参数</summary>
            public IntPtr lParam;

            /// <summary>消息投递时间</summary>
            public uint time;

            /// <summary>消息投递时的光标 X 坐标</summary>
            public int pt_x;

            /// <summary>消息投递时的光标 Y 坐标</summary>
            public int pt_y;
        }
        #endregion

        #region P/Invoke 函数
        // ==================== P/Invoke 函数 ====================
        /// <summary>
        /// 注册窗口类。
        /// <para>参考：https://docs.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-registerclassexw </para>
        /// </summary>
        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        public static extern ushort RegisterClassEx([In] ref WNDCLASSEX lpWndClass);

        /// <summary>
        /// 创建窗口。
        /// <para>参考：https://docs.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-createwindowexw </para>
        /// </summary>
        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        public static extern IntPtr CreateWindowEx(
            uint dwExStyle, string lpClassName, string lpWindowName, uint dwStyle,
            int x, int y, int nWidth, int nHeight,
            IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

        /// <summary>
        /// 销毁窗口。
        /// <para>参考：https://docs.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-destroywindow </para>
        /// </summary>
        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool DestroyWindow(IntPtr hWnd);

        /// <summary>
        /// 从消息队列获取消息（阻塞）。
        /// <para>参考：https://docs.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getmessage </para>
        /// </summary>
        [DllImport("user32.dll")]
        public static extern bool GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

        /// <summary>
        /// 翻译虚拟键消息为字符消息。
        /// <para>参考：https://docs.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-translatemessage </para>
        /// </summary>
        [DllImport("user32.dll")]
        public static extern bool TranslateMessage([In] ref MSG lpMsg);

        /// <summary>
        /// 将消息分派给窗口过程。
        /// <para>参考：https://docs.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-dispatchmessage </para>
        /// </summary>
        [DllImport("user32.dll")]
        public static extern IntPtr DispatchMessage([In] ref MSG lpMsg);

        /// <summary>
        /// 默认窗口过程。
        /// <para>参考：https://docs.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-defwindowprocw </para>
        /// </summary>
        [DllImport("user32.dll")]
        public static extern IntPtr DefWindowProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);

        /// <summary>
        /// 向窗口消息队列投递消息（非阻塞）。
        /// <para>参考：https://docs.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-postmessagew </para>
        /// </summary>
        [DllImport("user32.dll")]
        public static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        /// <summary>
        /// 向消息循环发送退出消息。
        /// <para>参考：https://docs.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-postquitmessage </para>
        /// </summary>
        [DllImport("user32.dll")]
        public static extern void PostQuitMessage(int nExitCode);

        /// <summary>
        /// 获取模块句柄。
        /// <para>参考：https://docs.microsoft.com/en-us/windows/win32/api/libloaderapi/nf-libloaderapi-getmodulehandlew </para>
        /// </summary>
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr GetModuleHandle(string lpModuleName);

        /// <summary>
        /// 注册窗口将接收其通知的设备或设备类型。
        /// <para>应用程序使用 <see cref="BroadcastSystemMessage"/> 函数发送事件通知 。具有顶层窗口的任何应用程序都可以通过处理 <see cref="MessageType.WM_DEVICECHANGE"/> 消息来接收基本通知 。应用程序可以使用 <see cref="RegisterDeviceNotification"/> 函数进行注册以接收设备通知。</para>
        /// <para>服务可以使用 <see cref="RegisterDeviceNotification"/> 函数进行注册以接收设备通知。如果服务在 hRecipient 参数中指定了窗口句柄 ，则将通知发送到窗口过程。如果 hRecipient 是服务状态句柄，则 SERVICE_CONTROL_DEVICEEVENT 通知将发送到服务控制处理程序。有关服务控制处理程序的更多信息，请参见 <see cref="HandlerEx"/>。</para>
        /// <para>确保尽快处理即插即用设备事件。否则，系统可能无法响应。如果事件处理程序要执行可能阻止执行的操作（例如I / O），则最好启动另一个线程以异步方式执行该操作。</para>
        /// <para>当不再需要 <see cref="RegisterDeviceNotification"/> 返回的设备通知句柄时， 必须通过调用 <see cref="UnregisterDeviceNotification"/> 函数来关闭 它们。</para>
        /// <para>参考：https://docs.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-registerdevicenotificationa </para>
        /// </summary>
        /// <param name="hRecipient">窗口或服务的句柄，它将接收 NotificationFilter 参数中指定的设备的设备事件 。可以在多次调用 <see cref="RegisterDeviceNotification"/> 的过程中使用同一窗口句柄 。
        ///     <para>服务可以指定窗口句柄或服务状态句柄。</para></param>
        /// <param name="NotificationFilter">指向数据块的指针，该数据块指定应为其发送通知的设备的类型。该块始终以 <see cref="DEV_BROADCAST_HDR"/> 结构开始。该头之后的数据取决于 dbch_devicetype 成员的值，该值 可以是 <see cref="DeviceType.DBT_DEVTYP_DEVICEINTERFACE"/>  或 <see cref="DeviceType.DBT_DEVTYP_HANDLE"/>。</param>
        /// <param name="Flags"><see cref="DeviceNotifyFlag"/> 值之一 </param>
        /// <returns>如果函数成功，则返回值是设备通知句柄。如果函数失败，则返回值为NULL。要获取扩展的错误信息，请调用 <see cref="Marshal.GetLastWin32Error"/> 或 <see cref="Marshal.GetHRForLastWin32Error"/>。</returns>
        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern IntPtr RegisterDeviceNotification(IntPtr hRecipient, IntPtr NotificationFilter, DeviceNotifyFlag Flags);

        /// <summary>
        /// 关闭指定的设备通知句柄。
        /// <para>参考：https://docs.microsoft.com/zh-cn/windows/win32/api/winuser/nf-winuser-unregisterdevicenotification </para>
        /// </summary>
        /// <param name="Handle"><see cref="RegisterDeviceNotification"/> 函数返回的设备通知句柄 。</param>
        /// <returns>如果函数成功，则返回值为非零。如果函数失败，则返回值为零。要获取扩展的错误信息，请调用 <see cref="Marshal.GetLastWin32Error"/> 或 <see cref="Marshal.GetHRForLastWin32Error"/>。</returns>
        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool UnregisterDeviceNotification(IntPtr Handle);
        #endregion
    }
    #endregion

    /// <summary>
    /// 独立的后台消息窗口封装，用于接收 Windows 系统消息。
    /// <para>线程安全：<see cref="Start"/> 和 <see cref="Dispose"/> 非线程安全，调用者负责串行化。</para>
    /// <para>线程模型：内部在独立 STA 后台线程中创建隐藏窗口并运行消息循环，不阻塞调用线程。</para>
    /// <para>生命周期：调用 <see cref="Start"/> 创建窗口并启动消息循环；调用 <see cref="Dispose"/> 优雅关闭。</para>
    /// <para>典型用法：创建实例 → <see cref="Start"/> → 订阅 <see cref="MessageReceived"/> 处理消息 → <see cref="Dispose"/> 清理。</para>
    /// <para>所有 WinAPI 声明内聚于私有嵌套类 <c>NativeMethods</c>，无外部依赖。</para>
    /// </summary>
    public sealed class DeviceWatcher : IDisposable
    {
        #region 字段与属性
        /// <summary>窗口创建完成的同步信号</summary>
        private readonly ManualResetEventSlim _readyEvent = new ManualResetEventSlim(false);

        /// <summary>窗口过程委托字段，必须保持引用以防止被 GC 回收</summary>
        private readonly NativeMethods.WndProc _wndProcDelegate;

        /// <summary>唯一窗口类名，基于 GUID 生成以防止多实例冲突</summary>
        private readonly string _className;

        private readonly List<IntPtr> _notifyHandles = new List<IntPtr>(); // 存储注册的通知句柄

        private Thread _thread;
        private IntPtr _hwnd;
        private bool _disposed;

        /// <summary>
        /// 获取底层消息窗口的句柄。
        /// <para>仅在 <see cref="Start"/> 成功调用后有效，否则为 <see cref="IntPtr.Zero"/>。</para>
        /// </summary>
        public IntPtr Handle => _hwnd;

        /// <summary>
        /// 当消息窗口接收到消息时触发。
        /// <para>回调在后台 STA 线程中执行，订阅者需自行处理线程同步和异常。</para>
        /// <para>参数顺序：(uint msg, IntPtr wParam, IntPtr lParam)</para>
        /// </summary>
        //public event Action<uint, IntPtr, IntPtr> MessageReceived;
        #endregion

        /// <summary>
        /// 设备插入时触发。
        /// <para>回调在后台 STA 线程中执行，订阅者需自行处理线程同步和异常。</para>
        /// <para>通过 <see cref="DeviceChangedEventArgs.DeviceType"/> 区分具体子类类型，安全转换为对应 EventArgs：</para>
        /// <para><see cref="DeviceType.DBT_DEVTYP_PORT"/> → <see cref="PortDeviceChangedEventArgs"/></para>
        /// <para><see cref="DeviceType.DBT_DEVTYP_VOLUME"/> → <see cref="VolumeDeviceChangedEventArgs"/></para>
        /// <para><see cref="DeviceType.DBT_DEVTYP_DEVICEINTERFACE"/> → <see cref="InterfaceDeviceChangedEventArgs"/></para>
        /// </summary>
        public event EventHandler<DeviceChangedEventArgs> DeviceArrived;

        /// <summary>
        /// 设备移除时触发。
        /// <para>回调在后台 STA 线程中执行，订阅者需自行处理线程同步和异常。</para>
        /// <para>通过 <see cref="DeviceChangedEventArgs.DeviceType"/> 区分具体子类类型，安全转换为对应 EventArgs。</para>
        /// </summary>
        public event EventHandler<DeviceChangedEventArgs> DeviceRemoved;

        #region 构造函数与启动
        /// <summary>
        /// 创建 <see cref="DeviceWatcher"/> 的新实例。
        /// <para>构造完成后需调用 <see cref="Start"/> 启动消息循环。</para>
        /// </summary>
        public DeviceWatcher()
        {
            _className = "SpaceCG_MsgWnd_" + Guid.NewGuid().ToString("N");
            _wndProcDelegate = WindowProc;
        }

        /// <summary>
        /// 在后台 STA 线程中创建隐藏消息窗口并启动消息循环。
        /// </summary>
        /// <exception cref="TimeoutException">窗口创建在 5 秒内未完成时抛出。</exception>
        /// <exception cref="InvalidOperationException">窗口句柄为空（创建失败）时抛出。</exception>
        /// <remarks>
        /// 重复调用此方法无副作用（幂等）。
        /// </remarks>
        public void Start()
        {
            if (_thread != null) return;

            _thread = new Thread(ThreadMain)
            {
                IsBackground = true,
                Name = "MessageWindowThread",
            };
            // WinAPI 消息循环必须在单线程单元 (STA) 中运行
            _thread.SetApartmentState(ApartmentState.STA);
            _thread.Start();

            // 阻塞等待，直到窗口在后台线程创建完毕
            if (!_readyEvent.Wait(TimeSpan.FromSeconds(5)))
            {
                throw new TimeoutException("MessageWindow 创建超时。");
            }

            if (_hwnd == IntPtr.Zero)
            {
                throw new InvalidOperationException("MessageWindow 创建失败，句柄为空。");
            }

            // 窗口创建成功后发送自定义消息，验证消息循环正常工作
            NativeMethods.PostMessage(_hwnd, NativeMethods.WM_USER + 1, (IntPtr)123, (IntPtr)456);
        }

        /// <summary>
        /// 为指定设备接口 GUID 注册设备通知。
        /// <para>RegisterDeviceNotification 仅支持 DBT_DEVTYP_DEVICEINTERFACE 和 DBT_DEVTYP_HANDLE 两种类型。</para>
        /// <para>若 flags 包含 DEVICE_NOTIFY_ALL_INTERFACE_CLASSES，则 interfaceClassGuid 被忽略（传 Guid.Empty）。</para>
        /// </summary>
        private void RegisterDeviceInterfaceNotification(Guid interfaceClassGuid, NativeMethods.DeviceNotifyFlag flags = NativeMethods.DeviceNotifyFlag.DEVICE_NOTIFY_WINDOW_HANDLE)
        {
            var iface = new NativeMethods.DEV_BROADCAST_DEVICEINTERFACE
            {
                dbcc_head = new NativeMethods.DEV_BROADCAST_HDR
                {
                    dbch_size = (uint)Marshal.SizeOf<NativeMethods.DEV_BROADCAST_DEVICEINTERFACE>(),
                    dbch_devicetype = DeviceType.DBT_DEVTYP_DEVICEINTERFACE,
                    dbch_reserved = 0
                },
                dbcc_name = string.Empty,
                dbcc_classguid = interfaceClassGuid,
            };

            int structSize = (int)iface.dbcc_head.dbch_size;
            IntPtr buffer = Marshal.AllocHGlobal(structSize);
            try
            {
                Marshal.StructureToPtr(iface, buffer, false);
                IntPtr hNotify = NativeMethods.RegisterDeviceNotification(_hwnd, buffer, flags);
                if (hNotify != IntPtr.Zero)
                {
                    _notifyHandles.Add(hNotify);
                }
                else
                {
                    Trace.TraceWarning($"[DeviceWatcher] 注册设备通知失败 (Guid: {interfaceClassGuid}, Flags: {flags}), Error: {Marshal.GetLastWin32Error()}");
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        private void UnregisterAllDeviceNotifications()
        {
            foreach (var handle in _notifyHandles)
            {
                if (handle != IntPtr.Zero)
                {
                    NativeMethods.UnregisterDeviceNotification(handle);
                }
            }
            _notifyHandles.Clear();
        }

        /// <summary>
        /// 后台线程入口：注册窗口类 → 创建隐藏窗口 → 运行消息循环 → 清理。
        /// </summary>
        private void ThreadMain()
        {
            try
            {
                // 步骤 1：获取当前进程模块句柄
                IntPtr hInstance = NativeMethods.GetModuleHandle(null);
                if (hInstance == IntPtr.Zero)
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "GetModuleHandle 失败");
                }

                // 步骤 2：注册窗口类
                NativeMethods.WNDCLASSEX wndClass = new NativeMethods.WNDCLASSEX
                {
                    cbSize = (uint)Marshal.SizeOf(typeof(NativeMethods.WNDCLASSEX)),
                    lpfnWndProc = _wndProcDelegate,
                    hInstance = hInstance,
                    lpszClassName = _className
                };

                ushort result = NativeMethods.RegisterClassEx(ref wndClass);
                if (result == 0)
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "RegisterClassEx 失败");
                }

                // 步骤 3：创建隐藏消息窗口
                // 系统广播消息（如 WM_DEVICECHANGE 0x0219, WM_POWERBROADCAST 0x0218）,系统会强制发给所有顶层窗口
                _hwnd = NativeMethods.CreateWindowEx(0, _className, "HiddenMsgWindow", 0, 0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, hInstance, IntPtr.Zero);

                if (_hwnd == IntPtr.Zero)
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateWindowEx 失败");
                }

                // 1. 注册监听所有逻辑卷 (U盘/移动硬盘) — GUID_DEVINTERFACE_VOLUME
                RegisterDeviceInterfaceNotification(NativeMethods.GUID_DEVINTERFACE_DISK);
                //RegisterDeviceInterfaceNotification(new Guid("53F56307-B6BF-11D0-94F2-00A0C91EFB8B"));
                // 2. 注册监听所有串口 (COM) — GUID_DEVINTERFACE_COMPORT
                RegisterDeviceInterfaceNotification(NativeMethods.GUID_DEVINTERFACE_COMPORT);
                //RegisterDeviceInterfaceNotification(new Guid("86E0D1E0-8089-11D0-9CE4-08003E301F73"));
                // 3. 注册监听所有设备接口 (USB/HID 等) — DEVICE_NOTIFY_ALL_INTERFACE_CLASSES
                RegisterDeviceInterfaceNotification(Guid.Empty, NativeMethods.DeviceNotifyFlag.DEVICE_NOTIFY_ALL_INTERFACE_CLASSES);

                // 通知调用线程窗口已就绪
                _readyEvent.Set();

                // 步骤 4：消息循环（GetMessage 返回 false 时退出，即收到 WM_QUIT）
                NativeMethods.MSG msg;
                while (NativeMethods.GetMessage(out msg, IntPtr.Zero, 0, 0))
                {
                    NativeMethods.TranslateMessage(ref msg);
                    NativeMethods.DispatchMessage(ref msg);
                }
            }
            finally
            {
                // 步骤 5：兜底清理
                // 5a. 取消所有设备通知注册
                UnregisterAllDeviceNotifications();

                // 5b. 确保窗口被销毁
                if (_hwnd != IntPtr.Zero)
                {
                    NativeMethods.DestroyWindow(_hwnd);
                    _hwnd = IntPtr.Zero;
                }

                // 5c. 防止 Dispose 中因 Wait 未收到信号而永久阻塞
                _readyEvent.Set();
            }
        }
        #endregion

        #region 消息处理
        /// <summary>
        /// 窗口过程回调，处理系统消息并分发给外部订阅者。
        /// </summary>
        /// <param name="hWnd">窗口句柄</param>
        /// <param name="msg">消息标识</param>
        /// <param name="wParam">消息附加参数</param>
        /// <param name="lParam">消息附加参数</param>
        /// <returns>消息处理结果。对于已处理的 <c>WM_CLOSE</c> 和 <c>WM_DESTROY</c> 返回 <see cref="IntPtr.Zero"/>，其余交由默认窗口过程处理。</returns>
        private IntPtr WindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            switch (msg)
            {
                case NativeMethods.WM_CLOSE:
                    // 收到关闭消息，在创建窗口的线程中销毁窗口
                    NativeMethods.DestroyWindow(hWnd);
                    return IntPtr.Zero;

                case NativeMethods.WM_DESTROY:
                    // 窗口销毁后发送 WM_QUIT，使 GetMessage 返回 false 结束消息循环
                    NativeMethods.PostQuitMessage(0);
                    return IntPtr.Zero;
            }

            try
            {
                //Trace.WriteLine($"[MessageWindow] 收到消息: msg=0x{msg:X4}, wParam={wParam}, lParam={lParam}");

                // 将消息分发给外部订阅者
                //MessageReceived?.Invoke(msg, wParam, lParam);
                if (msg == NativeMethods.WM_DEVICECHANGE && lParam != IntPtr.Zero)
                {
                    OnMessageReceived(msg, wParam, lParam);
                }
            }
            catch (Exception ex)
            {
                // 订阅者异常不应中断消息循环，仅记录以辅助诊断
                Trace.TraceWarning($"[MessageWindow] 消息处理异常: msg=0x{msg:X4}, {ex}");
            }

            // 未处理的消息交由默认窗口过程
            return NativeMethods.DefWindowProc(hWnd, msg, wParam, lParam);
        }
        #endregion

        private void OnMessageReceived(uint msg, IntPtr wParam, IntPtr lParam)
        {
            DeviceBroadcastType eventType = (DeviceBroadcastType)wParam.ToInt32();

            if (eventType != DeviceBroadcastType.DBT_DEVICEARRIVAL && eventType != DeviceBroadcastType.DBT_DEVICEREMOVECOMPLETE)
                return;

            // 1. 读取通用头部
            var header = Marshal.PtrToStructure<NativeMethods.DEV_BROADCAST_HDR>(lParam);

            // 2. 根据设备类型分发构建事件参数并触发事件
            if (header.dbch_devicetype == DeviceType.DBT_DEVTYP_VOLUME)
            {
                var volume = Marshal.PtrToStructure<NativeMethods.DEV_BROADCAST_VOLUME>(lParam);
                BuildAndRaiseVolumeEvent(eventType, volume);
            }
            else if (header.dbch_devicetype == DeviceType.DBT_DEVTYP_PORT)
            {
                var port = Marshal.PtrToStructure<NativeMethods.DEV_BROADCAST_PORT>(lParam);
                BuildAndRaisePortEvent(eventType, port);
            }
            else if (header.dbch_devicetype == DeviceType.DBT_DEVTYP_DEVICEINTERFACE)
            {
                var iface = Marshal.PtrToStructure<NativeMethods.DEV_BROADCAST_DEVICEINTERFACE>(lParam);
                BuildAndRaiseInterfaceEvent(eventType, iface);
            }
        }

        /// <summary>
        /// 从 <see cref="DEV_BROADCAST_VOLUME"/> 构建事件参数并触发事件。
        /// </summary>
        private void BuildAndRaiseVolumeEvent(DeviceBroadcastType eventType, NativeMethods.DEV_BROADCAST_VOLUME volume)
        {
            for (int i = 0; i < 26; i++)
            {
                if ((volume.dbcv_unitmask & (1u << i)) != 0)
                {
                    var driveLetter = $"{(char)('A' + i)}:";
                    var isNetworkDrive = (volume.dbcv_flags & 0x0002) != 0;

                    // 尝试获取 DriveInfo（拔出时可能失败）
                    DriveInfo drive = null;
                    try
                    {
                        drive = new DriveInfo(driveLetter);
                    }
                    catch (Exception)
                    {
                        // 设备拔出时 DriveInfo 构造函数可能抛异常
                    }

                    var args = new VolumeDeviceChangedEventArgs(eventType, driveLetter, isNetworkDrive, drive);
                    RaiseDeviceEvent(args);
                }
            }
        }

        /// <summary>
        /// 从 <see cref="DEV_BROADCAST_PORT"/> 构建事件参数并触发事件。
        /// </summary>
        private void BuildAndRaisePortEvent(DeviceBroadcastType eventType, NativeMethods.DEV_BROADCAST_PORT port)
        {
            string portName = port.dbcp_name;
            string friendlyName = port.dbcp_name;

            var args = new PortDeviceChangedEventArgs(eventType, portName, friendlyName);
            RaiseDeviceEvent(args);
        }

        /// <summary>
        /// 从 <see cref="DEV_BROADCAST_DEVICEINTERFACE"/> 构建事件参数并触发事件。
        /// </summary>
        private void BuildAndRaiseInterfaceEvent(DeviceBroadcastType eventType, NativeMethods.DEV_BROADCAST_DEVICEINTERFACE iface)
        {
            var args = new InterfaceDeviceChangedEventArgs(eventType, iface.dbcc_name, iface.dbcc_classguid);
            RaiseDeviceEvent(args);
        }

        /// <summary>
        /// 根据事件类型分发到 <see cref="DeviceArrived"/> 或 <see cref="DeviceRemoved"/> 事件。
        /// </summary>
        /// <param name="args">设备变更事件参数。</param>
        private void RaiseDeviceEvent(DeviceChangedEventArgs args)
        {
            if (args.EventType == DeviceBroadcastType.DBT_DEVICEARRIVAL)
            {
                DeviceArrived?.Invoke(this, args);
            }
            else if (args.EventType == DeviceBroadcastType.DBT_DEVICEREMOVECOMPLETE)
            {
                DeviceRemoved?.Invoke(this, args);
            }
        }

        #region IDisposable 实现
        /// <inheritdoc />
        /// <remarks>
        /// <para>通过 <c>PostMessage(WM_CLOSE)</c> 通知消息循环线程自行销毁窗口，避免跨线程调用 <c>DestroyWindow</c>。</para>
        /// <para>最多等待 3 秒让后台线程优雅退出。</para>
        /// <para>重复调用无副作用（幂等）。</para>
        /// </remarks>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            if (_hwnd != IntPtr.Zero && _thread != null && _thread.IsAlive)
            {
                // 通过 PostMessage 发送 WM_CLOSE，让消息循环线程自行销毁窗口
                // 不能跨线程直接调用 DestroyWindow
                NativeMethods.PostMessage(_hwnd, NativeMethods.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);

                // 等待后台线程优雅退出（最多 3 秒）
                _thread.Join(3000);
            }

            _readyEvent.Dispose();
        }
        #endregion
    }

    #region 事件参数类

    /// <summary>
    /// 设备变更事件参数（抽象基类）。
    /// <para>根据 <see cref="DeviceType"/> 可安全转换为对应子类：</para>
    /// <para><see cref="DeviceType.DBT_DEVTYP_VOLUME"/> → <see cref="VolumeDeviceChangedEventArgs"/></para>
    /// <para><see cref="DeviceType.DBT_DEVTYP_PORT"/> → <see cref="PortDeviceChangedEventArgs"/></para>
    /// <para><see cref="DeviceType.DBT_DEVTYP_DEVICEINTERFACE"/> → <see cref="InterfaceDeviceChangedEventArgs"/></para>
    /// </summary>
    public abstract class DeviceChangedEventArgs : EventArgs
    {
        /// <summary>
        /// 设备广播事件类型（插入/移除）。
        /// </summary>
        public DeviceBroadcastType EventType { get; }

        /// <summary>
        /// 设备广播结构类型，对应 <see cref="DEV_BROADCAST_HDR.dbch_devicetype"/>。
        /// <para>用于判断具体子类类型。</para>
        /// </summary>
        public DeviceType DeviceType { get; }

        /// <summary>
        /// 构造 <see cref="DeviceChangedEventArgs"/> 实例。
        /// </summary>
        /// <param name="eventType">设备广播事件类型。</param>
        /// <param name="deviceType">设备广播结构类型。</param>
        protected DeviceChangedEventArgs(DeviceBroadcastType eventType, DeviceType deviceType)
        {
            EventType = eventType;
            DeviceType = deviceType;
        }
    }

    /// <summary>
    /// 逻辑卷设备变更事件参数。
    /// <para>对应 <see cref="DEV_BROADCAST_VOLUME"/> 结构。</para>
    /// </summary>
    public sealed class VolumeDeviceChangedEventArgs : DeviceChangedEventArgs
    {
        /// <summary>
        /// 驱动器号（如 "D:"）。
        /// </summary>
        public string DriveLetter { get; }

        /// <summary>
        /// 是否为网络映射驱动器。
        /// </summary>
        public bool IsNetworkDrive { get; }

        /// <summary>
        /// .NET 原生驱动器信息对象。
        /// <para>设备拔出时可能为 null（驱动器已不可用）。</para>
        /// <para>可通过 <see cref="DriveInfo.DriveType"/> 获取驱动器类型（Fixed/Removable/Network 等）。</para>
        /// <para>可通过 <see cref="DriveInfo.VolumeLabel"/> 获取卷标。</para>
        /// <para>可通过 <see cref="DriveInfo.DriveFormat"/> 获取文件系统类型。</para>
        /// </summary>
        public DriveInfo Drive { get; }

        /// <summary>
        /// 构造 <see cref="VolumeDeviceChangedEventArgs"/> 实例。
        /// </summary>
        /// <param name="eventType">设备广播事件类型。</param>
        /// <param name="driveLetter">驱动器号。</param>
        /// <param name="isNetworkDrive">是否为网络驱动器。</param>
        /// <param name="drive">驱动器信息（可为 null）。</param>
        internal VolumeDeviceChangedEventArgs(DeviceBroadcastType eventType, string driveLetter, bool isNetworkDrive, DriveInfo drive)
            : base(eventType, DeviceType.DBT_DEVTYP_VOLUME)
        {
            DriveLetter = driveLetter;
            IsNetworkDrive = isNetworkDrive;
            Drive = drive;
        }
    }

    /// <summary>
    /// 端口设备变更事件参数（串口/并口）。
    /// <para>对应 <see cref="DEV_BROADCAST_PORT"/> 结构。</para>
    /// </summary>
    public sealed class PortDeviceChangedEventArgs : DeviceChangedEventArgs
    {
        /// <summary>
        /// 端口名称（如 "COM3"、"LPT1"）。
        /// <para>从 <see cref="DEV_BROADCAST_PORT.dbcp_name"/> 解析提取。</para>
        /// </summary>
        public string PortName { get; }

        /// <summary>
        /// 端口友好名称（如 "USB Serial Port (COM3)"）。
        /// <para>原始 <see cref="DEV_BROADCAST_PORT.dbcp_name"/> 的值。</para>
        /// </summary>
        public string PortFriendlyName { get; }

        /// <summary>
        /// 构造 <see cref="PortDeviceChangedEventArgs"/> 实例。
        /// </summary>
        /// <param name="eventType">设备广播事件类型。</param>
        /// <param name="portName">端口名称。</param>
        /// <param name="portFriendlyName">端口友好名称。</param>
        internal PortDeviceChangedEventArgs(DeviceBroadcastType eventType, string portName, string portFriendlyName)
            : base(eventType, DeviceType.DBT_DEVTYP_PORT)
        {
            PortName = portName;
            PortFriendlyName = portFriendlyName;
        }
    }

    /// <summary>
    /// 设备接口变更事件参数（USB/HID 等通用设备接口类）。
    /// <para>对应 <see cref="DEV_BROADCAST_DEVICEINTERFACE"/> 结构。</para>
    /// </summary>
    public sealed class InterfaceDeviceChangedEventArgs : DeviceChangedEventArgs
    {
        /// <summary>
        /// 设备路径（dbcc_name），格式如 "\\?\USB#VID_1A86&PID_7523#..."。
        /// <para>可用于 <c>CreateFile</c> 打开设备句柄。</para>
        /// </summary>
        public string DevicePath { get; }

        /// <summary>
        /// 设备接口类 GUID。
        /// <para>常见值：GUID_DEVINTERFACE_USB_DEVICE、GUID_DEVINTERFACE_DISK、GUID_DEVINTERFACE_HID 等。</para>
        /// </summary>
        public Guid ClassGuid { get; }

        /// <summary>
        /// 构造 <see cref="InterfaceDeviceChangedEventArgs"/> 实例。
        /// </summary>
        /// <param name="eventType">设备广播事件类型。</param>
        /// <param name="devicePath">设备路径。</param>
        /// <param name="classGuid">设备接口类 GUID。</param>
        internal InterfaceDeviceChangedEventArgs(DeviceBroadcastType eventType, string devicePath, Guid classGuid)
            : base(eventType, DeviceType.DBT_DEVTYP_DEVICEINTERFACE)
        {
            DevicePath = devicePath;
            ClassGuid = classGuid;
        }
    }

    #endregion
}