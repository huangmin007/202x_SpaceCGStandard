namespace SpaceCG.WindowsAPI.HID
{
    using System;
    using System.Runtime.InteropServices;
    using Microsoft.Win32.SafeHandles;

    #region Enumerations
    public enum HIDP_REPORT_TYPE : ushort
    {
        HidP_Input = 0,
        HidP_Output = 1,
        HidP_Feature = 2
    }
    #endregion


    #region Structures
    public struct HIDD_ATTRIBUTES
	{
		public int Size;

		public ushort VendorID;

		public ushort ProductID;

		public ushort VersionNumber;

		public static HIDD_ATTRIBUTES Create()
		{
			return new HIDD_ATTRIBUTES()
			{
				Size = Marshal.SizeOf(typeof(HIDD_ATTRIBUTES)),
			};
		}

		public override string ToString()
		{
			return $"[HIDD_ATTRIBUTES]Size:{Size}, VendorID:{VendorID}, ProductID:{ProductID}, VersionNumber:{VersionNumber}";
		}
	}

	public struct HIDP_CAPS
	{
		public short Usage;

		public short UsagePage;

		public short InputReportByteLength;

		public short OutputReportByteLength;

		public short FeatureReportByteLength;

		[MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)]
		public short[] Reserved;

		public short NumberLinkCollectionNodes;

		public short NumberInputButtonCaps;

		public ushort NumberInputValueCaps;

		public short NumberInputDataIndices;

		public short NumberOutputButtonCaps;

		public short NumberOutputValueCaps;

		public short NumberOutputDataIndices;

		public short NumberFeatureButtonCaps;

		public short NumberFeatureValueCaps;

		public short NumberFeatureDataIndices;

		public override string ToString()
		{
			return $"[HIDP_CAPS]OutputReportByteLength:{OutputReportByteLength}, InputReportByteLength:{InputReportByteLength}, NumberInputValueCaps:{NumberInputValueCaps}";
		}
	}

    [StructLayout(LayoutKind.Sequential)]
    public struct HIDP_VALUE_CAPS
    {
        public ushort UsagePage;
        public byte ReportID;

        [MarshalAs(UnmanagedType.U1)]
        public bool IsAlias;

        public ushort BitField;
        public ushort LinkCollection;
        public ushort LinkUsage;
        public ushort LinkUsagePage;

        [MarshalAs(UnmanagedType.U1)]
        public bool IsRange;
        [MarshalAs(UnmanagedType.U1)]
        public bool IsStringRange;
        [MarshalAs(UnmanagedType.U1)]
        public bool IsDesignatorRange;
        [MarshalAs(UnmanagedType.U1)]
        public bool IsAbsolute;
        [MarshalAs(UnmanagedType.U1)]
        public bool HasNull;

        public byte Reserved;
        public ushort BitSize;
        public ushort ReportCount;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 5)]
        public ushort[] Reserved2;

        public uint UnitsExp;
        public uint Units;
        public int LogicalMin;
        public int LogicalMax;
        public int PhysicalMin;
        public int PhysicalMax;

        // Range
        public ushort UsageMin;
        public ushort UsageMax;
        public ushort StringMin;
        public ushort StringMax;
        public ushort DesignatorMin;
        public ushort DesignatorMax;
        public ushort DataIndexMin;
        public ushort DataIndexMax;

        // NotRange
        public ushort Usage => UsageMin;
        // ushort Reserved1;
        public ushort NotRange_Reserved1 => UsageMax;
        public ushort StringIndex => StringMin;
        // ushort Reserved2;
        public ushort NotRange_Reserved2 => StringMax;
        public ushort DesignatorIndex => DesignatorMin;
        // ushort Reserved3;
        public ushort NotRange_Reserved3 => DesignatorMax;
        public ushort DataIndex => DataIndexMin;
        // ushort Reserved4;
        public ushort NotRange_Reserved4 => DataIndexMax;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct HIDP_VALUE_CAPS_RANGE
    {
        public ushort UsageMin;
        public ushort UsageMax;
        public ushort StringMin;
        public ushort StringMax;
        public ushort DesignatorMin;
        public ushort DesignatorMax;
        public ushort DataIndexMin;
        public ushort DataIndexMax;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct HIDP_VALUE_CAPS_NOT_RANGE
    {
        public ushort Usage;
        public ushort Reserved1;
        public ushort StringIndex;
        public ushort Reserved2;
        public ushort DesignatorIndex;
        public ushort Reserved3;
        public ushort DataIndex;
        public ushort Reserved4;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct HIDP_BUTTON_CAPS
    {
        public ushort UsagePage;
        public byte ReportID;

        [MarshalAs(UnmanagedType.U1)]
        public bool IsAlias;

        public ushort BitField;
        public ushort LinkCollection;
        public ushort LinkUsage;
        public ushort LinkUsagePage;

        [MarshalAs(UnmanagedType.U1)]
        public bool IsRange;
        [MarshalAs(UnmanagedType.U1)]
        public bool IsStringRange;
        [MarshalAs(UnmanagedType.U1)]
        public bool IsDesignatorRange;
        [MarshalAs(UnmanagedType.U1)]
        public bool IsAbsolute;

        public ushort ReportCount;
        public ushort Reserved2;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 9)]
        public uint[] Reserved;

        // Range
        public ushort UsageMin;
        public ushort UsageMax;
        public ushort StringMin;
        public ushort StringMax;
        public ushort DesignatorMin;
        public ushort DesignatorMax;
        public ushort DataIndexMin;
        public ushort DataIndexMax;

        // NotRange
        public ushort Usage => UsageMin;
        // ushort Reserved1;
        public ushort NotRange_Reserved1 => UsageMax;
        public ushort StringIndex => StringMin;
        // ushort Reserved2;
        public ushort NotRange_Reserved2 => StringMax;
        public ushort DesignatorIndex => DesignatorMin;
        // ushort Reserved3;
        public ushort NotRange_Reserved3 => DesignatorMax;
        public ushort DataIndex => DataIndexMin;
        // ushort Reserved4;
        public ushort NotRange_Reserved4 => DataIndexMax;
    }

    #endregion


    #region Delegates
    #endregion


    #region Notifications
    #endregion


    /// <summary>
    /// hdi.dll Human Interface Devices (HID)
    /// <para>参考：https://docs.microsoft.com/zh-cn/windows-hardware/drivers/hid/ </para>
    /// <para>参考：https://docs.microsoft.com/en-us/windows-hardware/drivers/ddi/_hid/ </para>
    /// </summary>
    public static partial class HID
    {
		public const int MAX_USB_DEVICES = 64;


        #region Functions

        #region 设备发现和设置
        #endregion

        #region 数据移动
        #endregion

        #region 报表创建和解释
        #endregion

        [DllImport("hid.dll", SetLastError = true)]
		public static extern bool HidD_FlushQueue(SafeFileHandle HidDeviceObject);

		[DllImport("hid.dll", SetLastError = true)]
		public static extern bool HidD_FreePreparsedData(IntPtr PreparsedData);

		[DllImport("hid.dll", SetLastError = true)]
		public static extern void HidD_GetHidGuid(ref Guid HidGuid);

		[DllImport("hid.dll", SetLastError = true)]
		[return:MarshalAs(UnmanagedType.Bool)]
		public static extern bool HidD_GetAttributes(SafeFileHandle HidDeviceObject, ref HIDD_ATTRIBUTES Attributes);

		[DllImport("hid.dll", SetLastError = true)]
		public static extern int HidP_GetCaps(IntPtr PreparsedData, ref HIDP_CAPS Capabilities);
        //[DllImport("hid.dll", SetLastError = true)]
        //public static extern int HidP_GetCaps(IntPtr PreparsedData, out HIDP_CAPS Capabilities);

        [DllImport("hid.dll", SetLastError = true)]
		public static extern bool HidD_SetFeature(SafeFileHandle HidDeviceObject, byte[] lpReportBuffer, int ReportBufferLength);

		[DllImport("hid.dll", SetLastError = true)]
		public static extern bool HidD_GetFeature(SafeFileHandle HidDeviceObject, byte[] lpReportBuffer, int ReportBufferLength);

		[DllImport("hid.dll", SetLastError = true)]
		public static extern bool HidD_SetOutputReport(SafeFileHandle HidDeviceObject, byte[] lpReportBuffer, int ReportBufferLength);

		[DllImport("hid.dll", SetLastError = true)]
		public static extern bool HidD_GetInputReport(SafeFileHandle HidDeviceObject, byte[] lpReportBuffer, int ReportBufferLength);

		[DllImport("hid.dll", SetLastError = true)]
		public static extern bool HidD_SetNumInputBuffers(SafeFileHandle HidDeviceObject, int NumberBuffers);

		[DllImport("hid.dll", SetLastError = true)]
		public static extern bool HidD_GetNumInputBuffers(SafeFileHandle HidDeviceObject, ref int NumberBuffers);

		[DllImport("hid.dll", SetLastError = true)]
		public static extern bool HidD_GetPreparsedData(SafeFileHandle HidDeviceObject, ref IntPtr PreparsedData);

        [DllImport("hid.dll", SetLastError = true)]
        public static extern bool HidD_GetPreparsedData(IntPtr HidDeviceObject, out IntPtr PreparsedData);

        [DllImport("hid.dll", SetLastError = true)]
		public static extern int HidP_GetValueCaps(int ReportType, byte[] ValueCaps, ref ushort ValueCapsLength, IntPtr PreparsedData);

        [DllImport("hid.dll", SetLastError = true)]
        public static extern int HidP_GetValueCaps(int ReportType, [Out] HIDP_VALUE_CAPS[] ValueCaps, ref ushort ValueCapsLength, IntPtr PreparsedData);

        /// <summary>
        /// 
        /// </summary>
        /// <param name="ReportType"> 输入报告类型 (一般为 Input)</param>
        /// <param name="UsagePage"> Usage Page (如 0x01=Generic Desktop)</param>
        /// <param name="LinkCollection">通常为 0</param>
        /// <param name="Usage">Usage ID (如 0x30=X, 0x31=Y)</param>
        /// <param name="UsageValue">输出值 (逻辑坐标)</param>
        /// <param name="PreparsedData">预解析数据 (来自 GetRawInputDeviceInfo)</param>
        /// <param name="Report">报告数据</param>
        /// <param name="ReportLength">报告长度</param>
        /// <returns></returns>
        [DllImport("hid.dll", SetLastError = true)]
        public static extern int HidP_GetUsageValue( ushort ReportType,ushort UsagePage, ushort LinkCollection, ushort Usage, out uint UsageValue, IntPtr PreparsedData, byte[] Report, uint ReportLength);

        [DllImport("hid.dll", SetLastError = true)]
        public static extern int HidP_GetUsageValue(ushort ReportType, ushort UsagePage, ushort LinkCollection, ushort Usage, out uint UsageValue, IntPtr PreparsedData, IntPtr Report, uint ReportLength);

        [DllImport("hid.dll", SetLastError = true)]
        public static extern int HidP_GetButtonCaps(int ReportType, [Out] HIDP_BUTTON_CAPS[] ButtonCaps, ref ushort ButtonCapsLength, IntPtr PreparsedData);

        [DllImport("hid.dll", SetLastError = true)]
        public static extern int HidP_GetUsages(short ReportType, ushort UsagePage, ushort LinkCollection, out ushort[] Usages, ref uint UsageLength, IntPtr PreparsedData, byte[] Report, uint ReportLength);
        #endregion
    }

	/// <summary>
	/// WindowsAPI HID 库，扩展常用/通用，功能/函数，扩展示例，以及使用方式
	/// </summary>
	public static partial class HIDExtensions
	{
	}
}
