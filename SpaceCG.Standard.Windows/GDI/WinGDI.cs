/***
 * 
 * 
 * 
 * 
 * 
 **/

namespace SpaceCG.WindowsAPI.Gdi
{
    using System;
    using System.Collections.Generic;
    using System.Runtime.InteropServices;

    #region Enumerations
    /// <summary>
    /// Display device state
    /// </summary>
    [Flags]
    public enum DisplayStateFlags : uint
    {
        /// <summary>
        /// DISPLAY_DEVICE_ACTIVE 指定是否通过相应的 GDI 视图将监视器显示为“打开”
        /// </summary>
        DISPLAY_DEVICE_ATTACHED_TO_DESKTOP = 0x00000001,
        DISPLAY_DEVICE_MULTI_DRIVER = 0x00000002,
        /// <summary>
        /// 主桌面位于设备上。对于具有单个显示卡的系统，始终设置为该设置。对于具有多个显示卡的系统，只有一个设备可以具有此设置。
        /// </summary>
        DISPLAY_DEVICE_PRIMARY_DEVICE = 0x00000004,
        /// <summary>
        /// 表示用于镜像应用程序图纸以进行远程处理或其他目的的伪设备。一个不可见的伪监视器与此设备关联。例如，NetMeeting使用它。
        /// </summary>
        DISPLAY_DEVICE_MIRRORING_DRIVER = 0x00000008,
        /// <summary>
        /// 该设备与VGA兼容
        /// </summary>
        DISPLAY_DEVICE_VGA_COMPATIBLE = 0x00000010,
        /// <summary>
        /// 该设备是可移动的；它不能是主要显示。
        /// </summary>
        DISPLAY_DEVICE_REMOVABLE = 0x00000020,
        DISPLAY_DEVICE_ACC_DRIVER = 0x00000040,
        /// <summary>
        /// 该设备具有比其输出设备支持的更多显示模式。
        /// </summary>
        DISPLAY_DEVICE_MODESPRUNED = 0x08000000,

        DISPLAY_DEVICE_RDPUDD = 0x01000000,
        DISPLAY_DEVICE_REMOTE = 0x04000000,
        DISPLAY_DEVICE_DISCONNECT = 0x02000000,
        DISPLAY_DEVICE_TS_COMPATIBLE = 0x00200000,
        DISPLAY_DEVICE_UNSAFE_MODES_ON = 0x00080000,
    }
    #endregion


    #region Structures
    /// <summary>
    /// DISPLAY_DEVICEA, * PDISPLAY_DEVICEA, * LPDISPLAY_DEVICEA;
    /// </summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    public struct DISPLAY_DEVICE
    {
        /// <summary>
        /// <see cref="DISPLAY_DEVICE"/> Size
        /// </summary>
        public uint cb;
        
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceString;

        /// <summary>
        /// Display State Flags, see <see cref="DisplayStateFlags"/>
        /// </summary>
        public DisplayStateFlags StateFlags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceID;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceKey;

        /// <summary>
        /// Create <see cref="DISPLAY_DEVICE"/> Object.
        /// </summary>
        /// <returns></returns>
        public static DISPLAY_DEVICE Create()
        {
            DISPLAY_DEVICE lpDisplay = new DISPLAY_DEVICE();
            lpDisplay.cb = DISPLAY_DEVICE.Size;
            lpDisplay.StateFlags = DisplayStateFlags.DISPLAY_DEVICE_ATTACHED_TO_DESKTOP;

            return lpDisplay;
        }

        /// <summary>
        /// <see cref="DISPLAY_DEVICE"/> Size
        /// </summary>
        public static readonly uint Size = (uint)Marshal.SizeOf(typeof(DISPLAY_DEVICE));

        /// <summary>
        /// <inheritdoc/>
        /// </summary>
        /// <returns></returns>
        public override string ToString()
        {
            return $"DISPLAY_DEVICE:[DeviceName:{DeviceName}, DeviceString:{DeviceString}], DeviceID:{DeviceID}, DeviceKey:{DeviceKey}";
        }
    }
    #endregion


    #region Delegates
    #endregion


    #region Notifications
    #endregion


    /// <summary>
    /// DLL 库功能/主题
    /// </summary>
    public static partial class Gdi
    {
        #region Constants
        #endregion

        #region Functions
        /// <summary>
        /// 使用 EnumDisplayDevices 函数可以获取有关当前会话中的显示设备的信息。
        /// <para>参考：https://docs.microsoft.com/zh-cn/windows/win32/api/wingdi/ns-wingdi-display_devicea </para>
        /// </summary>
        /// <param name="lpDevice">指向设备名称的指针。如果为 NULL，则函数根据 iDevNum 返回有关计算机上显示适配器的信息。</param>
        /// <param name="iDevNum">指定感兴趣的显示设备的索引值。操作系统用索引值标识当前会话中的每个显示设备。索引值是从0开始的连续整数。例如，如果当前会话具有三个显示设备，则它们由索引值0、1和2指定。</param>
        /// <param name="lpDisplayDevice">指向 DISPLAY_DEVICE 结构的指针，该结构接收有关 iDevNum 指定的显示设备的信息。调用之前EnumDisplayDevices，你必须在初始化CB成员DISPLAY_DEVICE的大小，以字节为单位的DISPLAY_DEVICE。</param>
        /// <param name="dwFlags">将此标志设置为EDD_GET_DEVICE_INTERFACE_NAME（0x00000001），以检索GUID_DEVINTERFACE_MONITOR的设备接口名称，该名称由操作系统在每个监视器的基础上注册。该值放置在lpDisplayDevice中返回的DISPLAY_DEVICE结构的DeviceID成员中。生成的设备接口名称可与SetupAPI函数一起使用，并用作GDI监视器设备和SetupAPI监视器设备之间的链接。</param>
        /// <returns></returns>
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        [return:MarshalAs(UnmanagedType.Bool)]
        public static extern bool EnumDisplayDevices(string lpDevice, uint iDevNum,  ref DISPLAY_DEVICE lpDisplayDevice, uint dwFlags);


        [DllImport("gdi32.dll")]
        private static extern int GetDeviceCaps(IntPtr hdc, int index);

        /// <summary>
        /// 检索用于指定窗口的客户区或整个屏幕的设备上下文（Device Context (DC)）。您可以在后续的 GDI 函数中使用返回的句柄来绘制 DC。设备上下文是一个不透明的数据结构，其值由GDI内部使用。
        /// </summary>
        /// <param name="hWnd">要获取其 DC 的窗口的句柄。如果此值为 NULL，则 GetDC 检索整个屏幕的 DC。</param>
        /// <returns>如果函数成功，则返回值是指定窗口的客户区DC的句柄。如果函数失败，则返回值为NULL。</returns>
        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDc);
        #endregion
    }


    /// <summary>
    /// xxx 库，扩展常用/通用，功能/函数，扩展示例，以及使用方式
    /// </summary>
    public static partial class GdiExtensions
    {
        /// <summary>
        /// 获取显示器设备
        /// </summary>
        /// <returns></returns>
        public static DISPLAY_DEVICE[] GetDisplayDevices()
        {
            var next = true;
            uint index = 0;
            List<DISPLAY_DEVICE> list = new List<DISPLAY_DEVICE>(32);

            do
            {
                DISPLAY_DEVICE lpDisplay = new DISPLAY_DEVICE();
                lpDisplay.cb = DISPLAY_DEVICE.Size;
                lpDisplay.StateFlags = DisplayStateFlags.DISPLAY_DEVICE_ATTACHED_TO_DESKTOP;

                var result = Gdi.EnumDisplayDevices(null, index, ref lpDisplay, 0x00000001);

                if (result)
                {
                    var boo = Gdi.EnumDisplayDevices(lpDisplay.DeviceName, 0, ref lpDisplay, 0x00000001);
                    if (boo) list.Add(lpDisplay);
                }

                if (result)
                {
                    index++;
                    next = true;
                }
                else
                {
                    next = false;
                }
            }
            while (next);

            return list.ToArray();
        }
    }

}
