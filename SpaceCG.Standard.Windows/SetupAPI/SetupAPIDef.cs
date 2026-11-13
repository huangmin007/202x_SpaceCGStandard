namespace SpaceCG.WindowsAPI.SetupAPI
{
    using System;
    using System.Runtime.InteropServices;


	#region Enumerations	
	/// <summary>
	/// <see cref="SetupAPI.SetupDiGetClassDevs"/> 函数参考的 零个或多个标志
	/// <para>参考：https://docs.microsoft.com/en-us/windows/win32/api/setupapi/nf-setupapi-setupdigetclassdevsw </para>
	/// </summary>
	[Flags]
	public enum DIGCF:uint
	{
		/// <summary>
		/// 对于指定的设备接口类，仅返回与系统默认设备接口关联的设备（如果已设置）。
		/// </summary>
		DEFAULT = 0x00000001,
		/// <summary>
		/// 仅返回系统中当前存在的设备。
		/// </summary>
		PRESENT = 0x00000002,
		/// <summary>
		/// 返回所有设备设置类或所有设备接口类的已安装设备的列表。
		/// </summary>
		ALLCLASSES = 0x00000004,
		/// <summary>
		/// 仅返回属于当前硬件配置文件的设备。
		/// </summary>
		PROFILE = 0x00000008,
		/// <summary>
		/// 返回支持指定设备接口类的设备接口的设备。如果 Enumerator 参数指定设备实例 ID，则必须在 Flags 参数中设置此标志。
		/// </summary>
		DEVICEINTERFACE = 0x00000010,
	}

	/// <summary>
	/// <see cref="SetupAPI.SetupDiGetDeviceRegistryProperty"/> 函数参考的 设备属性（SPDRP_*，Regstr.h / SetupAPI.h）
	/// <para>参考：https://learn.microsoft.com/zh-cn/windows-hardware/drivers/install/spdrp-xxx </para>
	/// </summary>
	public enum SPDRP : uint
	{
		DEVICEDESC = 0x00000000,
		HARDWAREID = 0x00000001,
		COMPATIBLEIDS = 0x00000002,
		SERVICE = 0x00000004,
		CLASS = 0x00000007,
		CLASSGUID = 0x00000008,
		DRIVER = 0x00000009,
		MFG = 0x0000000B,
		FRIENDLYNAME = 0x0000000C,
		LOCATION_INFORMATION = 0x0000000D,
		PHYSICAL_DEVICE_OBJECT_NAME = 0x0000000E,
		CAPABILITIES = 0x0000000F,
		UI_NUMBER = 0x00000010,
		UPPERFILTERS = 0x00000011,
		LOWERFILTERS = 0x00000012,
		BUSTYPEGUID = 0x00000013,
		LEGACYBUSTYPE = 0x00000014,
		BUSNUMBER = 0x00000015,
		ENUMERATOR_NAME = 0x00000016,
		SECURITY = 0x00000017,
		SECURITY_SDS = 0x00000018,
		DEVTYPE = 0x00000019,
		EXCLUSIVE = 0x0000001A,
		CHARACTERISTICS = 0x0000001B,
		ADDRESS = 0x0000001C,
		UI_NUMBER_DESC_FORMAT = 0x0000001D,
		DEVICE_POWER_DATA = 0x0000001E,
		REMOVAL_POLICY = 0x0000001F,
		REMOVAL_POLICY_HW_DEFAULT = 0x00000020,
		REMOVAL_POLICY_OVERRIDE = 0x00000021,
		INSTALL_STATE = 0x00000022,
		LOCATION_PATHS = 0x00000023,
		BASE_CONTAINERID = 0x00000024,
		PORTNAME = 0x00000037,
	}
	#endregion


    #region Structures
    /// <summary>
    /// 定义设备信息集中的设备接口。
    /// <para>参考：https://docs.microsoft.com/en-us/windows/win32/api/setupapi/ns-setupapi-sp_device_interface_data </para>
    /// </summary>
    public struct SP_DEVICE_INTERFACE_DATA
	{
		public uint cbSize;

		public Guid InterfaceClassGuid;

		public uint Flags;

		public IntPtr Reserved;

		/// <summary>
		/// <see cref="SP_DEVICE_INTERFACE_DATA"/> 结构大小
		/// </summary>
		public readonly static uint Size = (uint)Marshal.SizeOf(typeof(SP_DEVICE_INTERFACE_DATA));

		public static SP_DEVICE_INTERFACE_DATA Create()
		{
			return new SP_DEVICE_INTERFACE_DATA()
			{
				cbSize = (uint)Marshal.SizeOf(typeof(SP_DEVICE_INTERFACE_DATA)),
			};
		}
	}

	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
	public struct SP_DEVICE_INTERFACE_DETAIL_DATA
	{
		public uint cbSize;

		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
		public string DevicePath;

		/// <summary>
		/// <see cref="SP_DEVICE_INTERFACE_DETAIL_DATA"/> 结构大小
		/// </summary>
		public readonly static uint Size = (uint)Marshal.SizeOf(typeof(SP_DEVICE_INTERFACE_DETAIL_DATA));


		public static SP_DEVICE_INTERFACE_DETAIL_DATA Create()
		{
			return new SP_DEVICE_INTERFACE_DETAIL_DATA()
			{
				cbSize = (uint)Marshal.SizeOf(typeof(SP_DEVICE_INTERFACE_DETAIL_DATA)),
			};
		}

		/// <summary>
		/// @ToString()
		/// </summary>
		/// <returns></returns>
		public override string ToString()
		{
			return $"cbSize:{cbSize}, DevicePath:{DevicePath}";
		}

	}

	public struct SP_DEVINFO_DATA
	{
		public uint cbSize;

		public Guid ClassGuid;

		public uint DevInst;

		public IntPtr Reserved;

		/// <summary>
		/// <see cref="SP_DEVINFO_DATA"/> 结构大小
		/// </summary>
		public readonly static uint Size = (uint)Marshal.SizeOf(typeof(SP_DEVINFO_DATA));

	}

	
	#endregion


	#region Delegates
	#endregion


	#region Notifications
	#endregion


	public static partial class SetupAPI
    {
		#region 设备类 / 设备接口 GUID（Regstr.h / Usbiodef.h 等）
		/// <summary>端口类设备安装类 GUID（GUID_DEVCLASS_PORTS）</summary>
		public static readonly Guid GUID_DEVCLASS_PORTS = new Guid("4D36E978-E325-11CE-BFC1-08002BE10318");

		/// <summary>串口设备接口 GUID（GUID_DEVINTERFACE_COMPORT）
		/// <para>参考：https://learn.microsoft.com/zh-cn/windows-hardware/drivers/install/guid-devinterface-comport </para></summary>
		public static readonly Guid GUID_DEVINTERFACE_COMPORT = new Guid("86E0D1E0-8089-11D0-9CE4-08003E301F73");

		/// <summary>USB 设备接口 GUID（GUID_DEVINTERFACE_USB_DEVICE）</summary>
		public static readonly Guid GUID_DEVINTERFACE_USB_DEVICE = new Guid("A5DCBF10-6530-11D2-901F-00C04FB951ED");

		/// <summary>磁盘设备接口 GUID（GUID_DEVINTERFACE_DISK）</summary>
		public static readonly Guid GUID_DEVINTERFACE_DISK = new Guid("53F56307-B6BF-11D0-94F2-00A0C91EFB8B");

		/// <summary>卷设备接口 GUID（GUID_DEVINTERFACE_VOLUME）</summary>
		public static readonly Guid GUID_DEVINTERFACE_VOLUME = new Guid("53F5630D-B6BF-11D0-94F2-00A0C91EFB8B");
		#endregion

		#region 注册表值类型（Winnt.h REG_*，SetupDiGetDeviceRegistryProperty 的 propertyRegDataType 输出值）
		/// <summary>REG_SZ，以 '\0' 结尾的 Unicode 字符串</summary>
		public const uint REG_SZ = 1;
		/// <summary>REG_EXPAND_SZ，含环境变量引用的字符串</summary>
		public const uint REG_EXPAND_SZ = 2;
		/// <summary>REG_BINARY，二进制数据</summary>
		public const uint REG_BINARY = 3;
		/// <summary>REG_DWORD，32 位整数</summary>
		public const uint REG_DWORD = 4;
		/// <summary>REG_MULTI_SZ，多重 '\0' 结尾的字符串数组</summary>
		public const uint REG_MULTI_SZ = 7;
		#endregion
	}
}
