/***
 *
 * Configuration Manager (CfgMgr32.dll)
 * 设备配置管理函数，即插即用（PnP）设备通知与设备 ID 查询
 * 参考：https://learn.microsoft.com/zh-cn/windows/win32/api/cfgmgr32/
 *
 **/

namespace SpaceCG.WindowsAPI.CfgMgr32
{
    using System;
    using System.Runtime.InteropServices;
    using System.Text;


    #region Enumerations
    /// <summary>
    /// <see cref="CM_NOTIFY_FILTER.FilterType"/> 的值之一，指定通知过滤器类型。
    /// <para>参考：https://learn.microsoft.com/zh-cn/windows/win32/api/cfgmgr32/ne-cfgmgr32-cm_notify_filter_type </para>
    /// </summary>
    public enum CM_NOTIFY_FILTER_TYPE : uint
    {
        /// <summary>按设备接口类过滤（<see cref="CM_NOTIFY_FILTER.ClassGuid"/> 有效）。此结构是设备接口通知过滤器。</summary>
        DeviceInterface = 0,
        /// <summary>按文件句柄过滤。此结构是设备句柄通知过滤器。</summary>
        DeviceHandle = 1,
        /// <summary>按设备实例 ID 过滤。此结构是设备实例通知过滤器。</summary>
        DeviceInstance = 2,
    }

    /// <summary>
    /// <see cref="CM_NOTIFY_CALLBACK"/> 回调的设备事件类型。
    /// <para>参考：https://learn.microsoft.com/zh-cn/windows/win32/api/cfgmgr32/ne-cfgmgr32-cm_notify_action </para>
    /// </summary>
    public enum CM_NOTIFY_ACTION : uint
    {
        /// <summary>设备接口到达（GUID_DEVINTERFACE_*）</summary>
        DeviceInterfaceArrival = 0,
        /// <summary>设备接口移除</summary>
        DeviceInterfaceRemoval = 1,
        /// <summary>查询是否允许移除设备（可否决）</summary>
        DeviceQueryRemove = 2,
        /// <summary>移除设备的请求被取消</summary>
        DeviceQueryRemoveFailed = 3,
        /// <summary>设备即将移除</summary>
        DeviceRemovePending = 4,
        /// <summary>设备已移除</summary>
        DeviceRemoved = 5,
        /// <summary>设备自定义事件</summary>
        DeviceCustomEvent = 6,
        /// <summary>（Windows 8+）设备实例已枚举</summary>
        DeviceInstanceEnumerated = 7,
        /// <summary>（Windows 8+）设备实例已启动</summary>
        DeviceInstanceStarted = 8,
        /// <summary>（Windows 8+）设备实例已移除</summary>
        DeviceInstanceRemoved = 9,
    }
    #endregion


    #region Structures
    /// <summary>
    /// 设备通知过滤器结构体，用于 <see cref="CM_Register_Notification"/>。
    /// <para>原生定义（cfgmgr32.h）为联合体 u：DeviceInterface(GUID, 16B) / DeviceHandle(HANDLE, 8B) / DeviceInstance(WCHAR[200], 400B)。</para>
    /// <para>托管侧联合体起始 16 字节用 <see cref="ClassGuid"/> 表示，剩余 384 字节由 <see cref="UnionPadding"/> 全零补位
    /// （仅注册 DeviceInterface 过滤器时使用；注册 DeviceInstance 过滤器需自行写入 UnionPadding）。</para>
    /// <para>参考：https://learn.microsoft.com/zh-cn/windows/win32/api/cfgmgr32/ns-cfgmgr32-cm_notify_filter </para>
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct CM_NOTIFY_FILTER
    {
        /// <summary>结构体大小，以字节为单位（原生 sizeof = 416）</summary>
        public uint cbSize;
        /// <summary>标志位：CM_NOTIFY_FILTER_FLAG_ALL_INTERFACE_CLASSES = 0x00000001（接收所有设备接口类事件，此时 ClassGuid 必须为全零）</summary>
        public uint Flags;
        /// <summary>过滤器类型</summary>
        public CM_NOTIFY_FILTER_TYPE FilterType;
        /// <summary>保留，必须为 0</summary>
        public uint Reserved;
        /// <summary>设备接口类 GUID（FilterType = DeviceInterface 时有效）</summary>
        public Guid ClassGuid;
        /// <summary>联合体剩余 384 字节补位（全零）</summary>
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 384)]
        public byte[] UnionPadding;

        /// <summary>
        /// 创建 DeviceInterface 类型过滤器。
        /// </summary>
        /// <param name="classGuid">设备接口类 GUID；传 <see cref="Guid.Empty"/> 时配合 ALL_INTERFACE_CLASSES 标志接收全部接口类事件</param>
        /// <param name="flags">标志位，0 或 CM_NOTIFY_FILTER_FLAG_ALL_INTERFACE_CLASSES</param>
        /// <returns>已填充 cbSize 的过滤器结构体</returns>
        public static CM_NOTIFY_FILTER CreateDeviceInterface(Guid classGuid, uint flags = 0)
        {
            return new CM_NOTIFY_FILTER
            {
                cbSize = (uint)Marshal.SizeOf(typeof(CM_NOTIFY_FILTER)),
                Flags = flags,
                FilterType = CM_NOTIFY_FILTER_TYPE.DeviceInterface,
                Reserved = 0,
                ClassGuid = classGuid,
                UnionPadding = new byte[384],
            };
        }
    }
    #endregion


    #region Delegates
    /// <summary>
    /// 设备通知回调函数原型（CM_NOTIFY_CALLBACK，CALLBACK = __stdcall）。
    /// <para>回调应尽快返回，禁止执行阻塞或耗时操作（I/O 等）；返回值仅 ERROR_SUCCESS(0) 合法。</para>
    /// <para>参考：https://learn.microsoft.com/zh-cn/windows/win32/api/cfgmgr32/nc-cfgmgr32-cm_notify_callback </para>
    /// </summary>
    /// <param name="Notify">通知句柄（HCMNOTIFICATION）</param>
    /// <param name="Context">注册时传入的调用方上下文指针</param>
    /// <param name="Action">设备事件类型</param>
    /// <param name="EventData">指向 <see cref="CM_NOTIFY_EVENT_DATA"/> 起始处的指针（联合体，按 FilterType 分支解析）</param>
    /// <param name="EventDataSize">EventData 数据大小，以字节为单位</param>
    /// <returns>ERROR_SUCCESS(0)</returns>
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate uint CM_NOTIFY_CALLBACK(IntPtr Notify, IntPtr Context, CM_NOTIFY_ACTION Action, IntPtr EventData, uint EventDataSize);
    #endregion


    /// <summary>
    /// cfgmgr32.dll，配置管理器函数。
    /// <para>参考：https://learn.microsoft.com/zh-cn/windows/win32/api/cfgmgr32/ </para>
    /// </summary>
    public static class CfgMgr32
    {
        /// <summary>CR_SUCCESS，函数执行成功</summary>
        public const int CR_SUCCESS = 0;

        /// <summary>CM_GET_DEVICE_INTERFACE_LIST_PRESENT，仅返回当前存在的设备接口</summary>
        public const uint CM_GET_DEVICE_INTERFACE_LIST_PRESENT = 0;

        /// <summary>
        /// 注册指定设备事件的 PnP 通知回调。
        /// <para>注意事项：回调可能在函数返回之前就被调用（上下文与委托须在调用前就绪）；
        /// 不会为已存在的设备接口发出通知（注册后应配合 <see cref="CM_Get_Device_Interface_List"/> 枚举现有接口）；
        /// 通知句柄必须调用 <see cref="CM_Unregister_Notification"/> 关闭，且不可在回调线程内调用（会死锁）。</para>
        /// <para>需 Windows 8 及以上。</para>
        /// <para>参考：https://learn.microsoft.com/zh-cn/windows/win32/api/cfgmgr32/nf-cfgmgr32-cm_register_notification </para>
        /// </summary>
        /// <param name="pFilter">指向 <see cref="CM_NOTIFY_FILTER"/> 结构的指针，指定要接收通知的设备事件</param>
        /// <param name="pContext">调用方定义的上下文指针，原样传递给回调</param>
        /// <param name="pCallback">设备通知回调函数</param>
        /// <param name="pNotifyContext">输出：通知句柄（HCMNOTIFICATION），须用 <see cref="CM_Unregister_Notification"/> 关闭</param>
        /// <returns>CR_SUCCESS(0) 表示成功；失败返回 CR_* 错误码（如 CR_INVALID_DATA = 0x1F、CR_INVALID_FLAG = 0x04、CR_INVALID_POINTER = 0x03、CR_OUT_OF_MEMORY = 0x02）</returns>
        [DllImport("CfgMgr32.dll", SetLastError = false)]
        public static extern int CM_Register_Notification(ref CM_NOTIFY_FILTER pFilter, IntPtr pContext, CM_NOTIFY_CALLBACK pCallback, out IntPtr pNotifyContext);

        /// <summary>
        /// 关闭指定的 PnP 通知句柄。会等待未决回调完成，禁止在回调线程内调用（会死锁）。
        /// <para>参考：https://learn.microsoft.com/zh-cn/windows/win32/api/cfgmgr32/nf-cfgmgr32-cm_unregister_notification </para>
        /// </summary>
        /// <param name="Notify"><see cref="CM_Register_Notification"/> 返回的通知句柄</param>
        /// <returns>CR_SUCCESS(0) 表示成功；失败返回 CR_* 错误码</returns>
        [DllImport("CfgMgr32.dll", SetLastError = false)]
        public static extern int CM_Unregister_Notification(IntPtr Notify);

        /// <summary>
        /// 获取指定设备接口类的设备接口列表大小（字符数）。
        /// <para>参考：https://learn.microsoft.com/zh-cn/windows/win32/api/cfgmgr32/nf-cfgmgr32-cm_get_device_interface_list_sizew </para>
        /// </summary>
        /// <param name="pulLen">输出：所需的缓冲区大小，以字符为单位（含多重 '\0' 结尾）</param>
        /// <param name="InterfaceClassGuid">设备接口类 GUID</param>
        /// <param name="DeviceId">设备实例 ID 过滤，通常为 IntPtr.Zero 表示不过滤</param>
        /// <param name="ulFlags">标志位：CM_GET_DEVICE_INTERFACE_LIST_PRESENT(0) 仅返回当前存在的接口</param>
        /// <returns>CR_SUCCESS(0) 表示成功；失败返回 CR_* 错误码</returns>
        [DllImport("CfgMgr32.dll", SetLastError = false)]
        public static extern int CM_Get_Device_Interface_List_Size(ref uint pulLen, ref Guid InterfaceClassGuid, IntPtr DeviceId, uint ulFlags);

        /// <summary>
        /// 获取指定设备接口类的设备接口符号链接列表（多重 '\0' 结尾的 Unicode 字符串）。
        /// <para>参考：https://learn.microsoft.com/zh-cn/windows/win32/api/cfgmgr32/nf-cfgmgr32-cm_get_device_interface_listw </para>
        /// </summary>
        /// <param name="InterfaceClassGuid">设备接口类 GUID</param>
        /// <param name="DeviceId">设备实例 ID 过滤，通常为 IntPtr.Zero 表示不过滤</param>
        /// <param name="Buffer">接收接口列表的缓冲区（WCHAR 数组）</param>
        /// <param name="BufferLen">缓冲区大小，以字符为单位（应使用 <see cref="CM_Get_Device_Interface_List_Size"/> 获取）</param>
        /// <param name="ulFlags">标志位：CM_GET_DEVICE_INTERFACE_LIST_PRESENT(0) 仅返回当前存在的接口</param>
        /// <returns>CR_SUCCESS(0) 表示成功；失败返回 CR_* 错误码</returns>
        [DllImport("CfgMgr32.dll", CharSet = CharSet.Unicode, SetLastError = false)]
        public static extern int CM_Get_Device_Interface_List(ref Guid InterfaceClassGuid, IntPtr DeviceId, IntPtr Buffer, uint BufferLen, uint ulFlags);

        /// <summary>
        /// 获取指定设备实例的设备 ID 字符串（如 "USB\VID_1A86&amp;PID_7523\5&amp;2C8A7B6F&amp;0&amp;3"）。
        /// <para>参考：https://learn.microsoft.com/zh-cn/windows/win32/api/cfgmgr32/nf-cfgmgr32-cm_get_device_idw </para>
        /// </summary>
        /// <param name="dnDevInst">设备实例句柄（SP_DEVINFO_DATA.DevInst）</param>
        /// <param name="Buffer">接收设备 ID 字符串的缓冲区</param>
        /// <param name="BufferLen">缓冲区大小，以字符为单位</param>
        /// <param name="ulFlags">标志位，通常为 0</param>
        /// <returns>CR_SUCCESS(0) 表示成功；失败返回 CR_* 错误码</returns>
        [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
        public static extern int CM_Get_Device_ID(uint dnDevInst, StringBuilder Buffer, int BufferLen, int ulFlags);
    }
}
