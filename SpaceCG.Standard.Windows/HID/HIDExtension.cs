namespace SpaceCG.WindowsAPI.HID
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Runtime.InteropServices;
    using Microsoft.Win32.SafeHandles;
    using SpaceCG.WindowsAPI.Kernel32;
    using SpaceCG.WindowsAPI.SetupAPI;

    /// <summary>
    /// HID Extension
    /// </summary>
    public static partial class HIDExtensions
    {
        /// <summary>
        /// 获取 HID 设备路径列表
        /// </summary>
        /// <returns></returns>
        public static string[] GetHIDDevicesPath()
        {
            Guid HidGuid = Guid.Empty;
            HID.HidD_GetHidGuid(ref HidGuid);
            List<string> deviceList = new List<string>(64);

            IntPtr HidInfoSet = SetupAPI.SetupDiGetClassDevs(ref HidGuid, IntPtr.Zero, IntPtr.Zero, DIGCF.PRESENT | DIGCF.DEVICEINTERFACE);
            if (HidInfoSet == IntPtr.Zero) return deviceList.ToArray();

            bool hasNext;
            uint index = 0;
            SP_DEVICE_INTERFACE_DATA interfaceInfo = SP_DEVICE_INTERFACE_DATA.Create();

            do
            {
                hasNext = SetupAPI.SetupDiEnumDeviceInterfaces(HidInfoSet, IntPtr.Zero, ref HidGuid, index, ref interfaceInfo);

                if (hasNext)
                {
                    uint bufferSize = 0;
                    SetupAPI.SetupDiGetDeviceInterfaceDetail(HidInfoSet, ref interfaceInfo, IntPtr.Zero, bufferSize, out bufferSize, IntPtr.Zero);

                    IntPtr pDetail = Marshal.AllocHGlobal((int)bufferSize);
                    Marshal.WriteInt32(pDetail, (IntPtr.Size == 4) ? (Marshal.SystemDefaultCharSize + 4) : 8);
                    var result = SetupAPI.SetupDiGetDeviceInterfaceDetail(HidInfoSet, ref interfaceInfo, pDetail, bufferSize, out bufferSize, IntPtr.Zero);

                    if (result)
                    {
                        IntPtr ptr = new IntPtr(pDetail.ToInt32() + 4);
                        deviceList.Add(Marshal.PtrToStringAuto(ptr));
                        //Console.WriteLine(Marshal.PtrToStringAuto(ptr));
                        Marshal.FreeHGlobal(pDetail);
                    }

                    index++;
                }
            }
            while (hasNext);

            SetupAPI.SetupDiDestroyDeviceInfoList(HidInfoSet);

            return deviceList.ToArray();
        }

        public static string GetHidUsage(HIDP_CAPS capabilities)
        {
            int num = capabilities.UsagePage * 256 + capabilities.Usage;

            if (num == 0x0102) return "mouse";
            if (num == 0x0106) return "keyboard";

            return null;
        }


        #region Get Attributes
        public static bool GetHIDDeviceAttributes(SafeFileHandle hidHandle, ref HIDD_ATTRIBUTES attribute)
        {
            bool result = HID.HidD_GetAttributes(hidHandle, ref attribute);
            return result;
        }

        public static bool GetHIDDeviceAttributes(string devicePath, ref HIDD_ATTRIBUTES attribute)
        {
            if (string.IsNullOrWhiteSpace(devicePath)) throw new ArgumentNullException(nameof(devicePath), "设备路径不能为空");

            IntPtr handle = Kernel32.CreateFile(devicePath,
                AccessRights.DEFAULT,
                ShareMode.FILE_SHARE_READ | ShareMode.FILE_SHARE_WRITE,
                IntPtr.Zero,
                CreationDisposition.OPEN_EXISTING,
                0,
                IntPtr.Zero);

            bool result = HID.HidD_GetAttributes(new SafeFileHandle(handle, true), ref attribute);

            return result;
        }

        public static HIDD_ATTRIBUTES GetHIDDeviceAttributes(string devicePath)
        {
            HIDD_ATTRIBUTES attribute = HIDD_ATTRIBUTES.Create();
            GetHIDDeviceAttributes(devicePath, ref attribute);

            return attribute;
        }
        #endregion

        #region Get Capabilities        
        public static bool GetHIDDeviceCapabilities(SafeFileHandle hidHandle, ref HIDP_CAPS caps)
        {
            int length = -1;
            IntPtr preparsedData = IntPtr.Zero;
            bool result = HID.HidD_GetPreparsedData(hidHandle, ref preparsedData);

            if (result)
            {
                length = HID.HidP_GetCaps(preparsedData, ref caps);
                HID.HidD_FreePreparsedData(preparsedData);
            }
            return result && length > 0;
        }

        public static bool GetHIDDeviceCapabilities(string devicePath, ref HIDP_CAPS caps)
        {
            if (string.IsNullOrWhiteSpace(devicePath)) throw new ArgumentNullException(nameof(devicePath), "设备路径不能为空");

            IntPtr hidHandle = Kernel32.CreateFile(devicePath,
                AccessRights.DEFAULT,
                ShareMode.FILE_SHARE_READ | ShareMode.FILE_SHARE_WRITE,
                IntPtr.Zero,
                CreationDisposition.OPEN_EXISTING,
                0,
                IntPtr.Zero);

            bool result = GetHIDDeviceCapabilities(new SafeFileHandle(hidHandle, true), ref caps);

            return result;
        }

        public static HIDP_CAPS GetHIDDeviceCapabilities(string devicePath)
        {
            if (string.IsNullOrWhiteSpace(devicePath)) throw new ArgumentNullException(nameof(devicePath), "设备路径不能为空");

            IntPtr hidHandle = Kernel32.CreateFile(devicePath,
                AccessRights.DEFAULT,
                ShareMode.FILE_SHARE_READ | ShareMode.FILE_SHARE_WRITE,
                IntPtr.Zero,
                CreationDisposition.OPEN_EXISTING,
                0,
                IntPtr.Zero);

            HIDP_CAPS caps = new HIDP_CAPS();
            bool result = GetHIDDeviceCapabilities(new SafeFileHandle(hidHandle, true), ref caps);

            return caps;
        }
        #endregion


        public static HidDeviceInfo GetDevice(int vid, int pid)
        {
            string[] paths = HIDExtensions.GetHIDDevicesPath();

            for (int i = 0; i < paths.Length; i++)
            {
                Console.WriteLine(paths[i]);
                IntPtr handle = Kernel32.CreateFile(paths[i], 3221225472u, 3, IntPtr.Zero, 3, 0x40000000, IntPtr.Zero);
                if(handle == IntPtr.Zero)
                {
                    Console.WriteLine("SafeFileHandle IsInvalid");
                    Console.WriteLine(Kernel32Extensions.GetSystemErrorMessage("CreateFile"));
                    continue;
                }

                HIDD_ATTRIBUTES attributes = new HIDD_ATTRIBUTES();
                bool result1 = HIDExtensions.GetHIDDeviceAttributes(new SafeFileHandle(handle, true), ref attributes);
                if(!result1)
                {
                    Console.WriteLine("GetHIDDeviceAttributes Error");
                    continue;
                }
                Console.WriteLine(attributes);

                HIDP_CAPS caps = new HIDP_CAPS();
                bool result2 = HIDExtensions.GetHIDDeviceCapabilities(new SafeFileHandle(handle, true), ref caps);
                if (!result2)
                {
                    Console.WriteLine("GetHIDDeviceCapabilities Error.");
                    continue;
                }

                if (attributes.VendorID == vid && attributes.ProductID == pid && caps.InputReportByteLength > 0)
                {
                    Console.WriteLine("{0}  {1}", HIDExtensions.GetHidUsage(caps), caps);
                    if(caps.InputReportByteLength > 0)
                    {
                        Console.WriteLine("Index::{0}", i);
                    }

                    int numberBuffer = 0;
                    bool result3 = HID.HidD_GetNumInputBuffers(new SafeFileHandle(handle, true), ref numberBuffer);
                    Console.WriteLine("result3:{0} {1}", result3, numberBuffer);

                    byte[] inputReportBuffer = new byte[caps.InputReportByteLength];
                    bool result4 = HID.HidD_GetInputReport(new SafeFileHandle(handle, true), inputReportBuffer, inputReportBuffer.Length + 1);
                    Console.WriteLine("result4:{0}", result4);

                    HidDeviceInfo device = new HidDeviceInfo();
                    device.Path = paths[i];
                    device.Capabilities = caps;
                    device.Attributes = attributes;
                    device.HidUsage = GetHidUsage(caps);
                    /* 
                     device.HidHandle = Kernel32.Kernel32.CreateFile(paths[i],
                         0u,//AccessRights.GENERIC_READ | AccessRights.GENERIC_WRITE,
                         ShareMode.FILE_SHARE_READ | ShareMode.FILE_SHARE_WRITE,
                         IntPtr.Zero,
                         CreationDisposition.OPEN_EXISTING,
                         0,
                         IntPtr.Zero);
                         */
                    device.HidHandle = new SafeFileHandle(handle, true);
                    device.InputReportLength = caps.InputReportByteLength;
                    //bool gib = HID.HidD_GetNumInputBuffers(handle, ref device.InputReportLength);
                    //Console.WriteLine("GNIB:{0} {1}", gib, device.InputReportLength);
                    
                    device.FileStream = new FileStream(device.HidHandle, FileAccess.ReadWrite, caps.InputReportByteLength, true);
                    HID.HidD_FlushQueue(device.HidHandle);
                
                    return device;
                }
            }

            return null;// deviceList.ToArray();
        }

    }

    public class HidDeviceInfo
    {
        public string Path;

        public HIDD_ATTRIBUTES Attributes;

        public HIDP_CAPS Capabilities;

        public int InputReportLength;

        public FileStream FileStream;

        public SafeFileHandle HidHandle;

        public string HidUsage;

        public override string ToString()
        {
            return $"Path:{Path}, vid:{Attributes.VendorID}, pid:{Attributes.ProductID}, Usage:{HidUsage}";
        }
    }
}
