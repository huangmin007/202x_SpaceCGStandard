using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO.Ports;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Xml.Linq;
using Microsoft.Win32;
using SpaceCG.Extensions;
using SpaceCG.Generic;
using SpaceCG.Net;

namespace Z_TestWpfApp
{
    /// <summary>
    /// MainWindow.xaml 的交互逻辑
    /// </summary>
    public partial class MainWindow : Window
    {
        RpcServerBase rpcServer;

        RpcClientBase rpcClient;

        DeviceWatcher deviceWatcher;

        // 准备两个数组，填充4MB大小的数据
        private static readonly byte[] XBytes = Enumerable.Range(0, 1024 * 4).Select(c => (byte)c).ToArray();
        private static readonly byte[] YBytes = Enumerable.Range(0, 1024 * 4).Select(c => (byte)c).ToArray();

        LedRenderControl ledRenderControl;

        public MainWindow()
        {
            InitializeComponent();
            
            Trace.Listeners.Add(new LoggerTraceListener(true));

            Trace.TraceInformation("Hello...");

            rpcServer = new RpcServer4X(2000, RpcServer4X.XmlTerminate);
            rpcServer.RegisterObject("Demo", this);
            rpcServer.Start();            
        }
        protected override void OnClosing(CancelEventArgs e)
        {
            base.OnClosing(e);

            rpcServer?.Dispose();
            //messageWindow?.Dispose();
        }

        protected override async void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            Trace.TraceInformation($"Key: {e.Key}");
            
            stopwatch.Restart();
            long ms = 0;
            switch (e.Key)
            {
                case Key.D1:
                    ledRenderControl.RenderSceneId(1);
                    await rpcClient.InvokeActionAsync("Demo", "test", new object[] {1,2 });
                    break;

                case Key.D2:
                    ledRenderControl.RenderSceneId(2);
                    var result = await rpcClient.InvokeFuncAsync("Demo", nameof(Test), new object[] { "Hello,world" });
                    Trace.TraceInformation($"Response::{result}");
                    Trace.TraceInformation($"ReturnType::{result.ReturnType}");
                    Trace.TraceInformation($"ReturnValue::{result.ReturnValue}");

                    if (result.ReturnValue is IEnumerable<IEnumerable<int>> resultEnumerable)
                    {
                        foreach (var item in resultEnumerable)
                        {
                            Trace.WriteLine($">>{string.Join(",", item)}");
                        }
                    }
                    
                    Trace.WriteLine($"ReturnValue::{result.ReturnValue}");
                    break;

                case Key.D9:
                    rpcClient.Connect();
                    break;

                case Key.D0:
                    rpcClient.Close();
                    break;

                case Key.D:
                    test(0);
                    break;

                case Key.A:
                    var result0 = InstanceExtensions.TryInvokeMethod(this, "Test", new object[] { "Hello,world" }, out var returnResult);
                    ms = stopwatch.ElapsedTicks;
                    Trace.WriteLine($"Result:{result0},ReturnValue:{returnResult}  use:{ms},,,,{returnResult.GetType() == typeof(Task)}");

                    var value = await InstanceExtensions.GetReturnValue(returnResult);
                    if (returnResult is Task task)
                    {
                        var returnType = returnResult.GetType();
                        var isReturnTaskOfT = returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(Task<>);

                        await task.ConfigureAwait(false);
                        if (isReturnTaskOfT)
                        {
                            var resultProperty = returnType.GetProperty("Result");
                            var rv = resultProperty?.GetValue(returnResult);
                            Trace.WriteLine($">>>>{rv}");
                        }
                    }

                    break;
                case Key.Z:
                    var result1 = InstanceExtensions.TryInvokeMethod(this, "SetWindowState", "0", out var returnValue1);
                    ms = stopwatch.ElapsedTicks;
                    Trace.WriteLine($"Result:{result1},ReturnValue:{returnValue1}  use:{ms}");
                    break;
                case Key.X:
                    var result2 = InstanceExtensions.TryInvokeMethod(this, "SetWindowState", "0,1", out var returnValue2);
                    ms = stopwatch.ElapsedTicks;

                    Trace.WriteLine($"Result:{result2},ReturnValue:{returnValue2}  use:{ms}");
                    break;
            }
        }

        Stopwatch stopwatch = new Stopwatch();

        public void test(int a)
        {
            stopwatch.Restart();
            var result = XBytes.SequenceEqual(0, XBytes.Length, YBytes, 0, YBytes.Length);
            var ticks0 = stopwatch.ElapsedTicks;
            Trace.WriteLine($"ticks:{ticks0}");

            stopwatch.Restart();
            var result1 = XBytes.SequenceEqual(0, XBytes.Length, YBytes, 0, YBytes.Length);
            var ticks1 = stopwatch.ElapsedTicks;
            Trace.WriteLine($"ticks:{ticks1}");


            Trace.WriteLine($">>>>>>teset....{a}");
            var s0 = @"1,2,3,'hello world, hi say:""hello I\'m world""'";
            //var s0 = ",,,";
            var s1 = "0x01,3,[True,True,False],'hello world, hi say:\\\"hello I\\'m world\\\"'";
            var s2 = "0x01,[0,3,4,7],[True,True,False,True]";
            var s3 = "[[#FFDDDDDD,#00DDDDDD]],[15],1.5,-1";
            var s4 = "[[[#FFFFFF00,#FF00FF00],[#FFFFFF00,]]]";
            var s5 = "[#FFFFFF00,#FF00FF00]";

            var f0 = new byte[] {0x01 };
            
            stopwatch.Restart();

            StringExtensions.TryParseParameters(s0, out var a0);
            StringExtensions.TryParseParameters(s1, out var a1);
            StringExtensions.TryParseParameters(s2, out var a2);
            StringExtensions.TryParseParameters(s3, out var a3);
            StringExtensions.TryParseParameters(s4, out var a4);
            StringExtensions.TryParseParameters(s5, out var a5);

            var a44 = a4.Select(x => x.GetType()).GetSignature();

            var t0 = a0.Select(x => x.GetType()).GetSignature();
            var t1 = a1.Select(x => x.GetType()).GetSignature();
            var t2 = a2.Select(x => x.GetType()).GetSignature();
            var t3 = a3.Select(x => x.GetType()).GetSignature();
            var t4 = a4.Select(x => x.GetType()).GetSignature();
            var t5 = a5.Select(x => x.GetType()).GetSignature();

            var ticks = stopwatch.ElapsedTicks;

            Trace.TraceInformation($"ticks:{ticks}");

            Trace.TraceInformation($"test");
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
#if true
            Trace.WriteLine($"Serial Devices ------------------------------------");
            var serialDevices = SystemExtensions.GetSerialDevices();
            foreach(var device in serialDevices)
            {
                Trace.WriteLine($"{device}");
            }

            Trace.WriteLine($"USB Devices ------------------------------------");
            var usbDevices = SystemExtensions.GetUsbDevices();
            foreach (var device in usbDevices)
            {
                Trace.WriteLine($"{device}");
            }

            Trace.WriteLine($"Disk Devices ------------------------------------");
            var diskDevices = SystemExtensions.GetDiskDevices();
            foreach (var device in diskDevices)
            {
                Trace.WriteLine($"{device}");
            }

            Trace.WriteLine($"Volume Devices ------------------------------------");
            var volumeDevices = SystemExtensions.GetVolumeDevices();
            foreach (var device in volumeDevices)
            {
                Trace.WriteLine($"{device}");
            }
#endif
            deviceWatcher = new DeviceWatcher();
            deviceWatcher.DeviceArrived += DeviceWatcher_DeviceArrived;
            deviceWatcher.DeviceRemoved += DeviceWatcher_DeviceRemoved;
            deviceWatcher.Start();

            var config = XElementExtensions.LoadConfig($"Resources/Config.xml");
            ledRenderControl = new LedRenderControl(Canvas_Leds);
            ledRenderControl.InitializeComponent(config.Element("DrawingDisplay"), config.Element("LedDevices"), config.Element("Scenes"));
            ledRenderControl.StartRender();
            ledRenderControl.RenderSceneId(1);

            test(0);
            var ips = GetLocalIPAddresses().ToArray();

            var ips2 = NetworkExtensions.GetLocalIPv4Addresses().ToArray();

            var config2 = XElementExtensions.LoadConfig($"Resources/Config.xml");
            Trace.WriteLine($"config...{config2}");

            //var str = "System.Collections.Generic.IEnumerable`1[System.Collections.Generic.IEnumerable`1[System.Int32]]";
            //var returnType = Type.GetType(str, true);
            SecurityElement.Escape("");

            rpcClient = new RpcClient4X("127.0.0.1", 2000);
            rpcClient.ResponseTimeout = TimeSpan.FromSeconds(2000);
            rpcClient.Connect();

            const string Dictionary = nameof(Dictionary);

            IList<List<int>> a6 = new List<List<int>>()
            {
                new List<int>(){ 1,2,3, },
                new List<int>(){ 4,5,6, },
            };
            Trace.WriteLine($">>{StringExtensions.SerializeValue(a6)}<<");

            var a7 = "'hello world, \"test\" hell.'";
            Trace.WriteLine($">>{StringExtensions.SerializeValue(a7)}<<");

            await Test("aaa");
#if false
            var type = this.GetType();
            foreach(var method in type.GetMethods())
            {
                if (!method.IsPublic) continue;
                if (method.IsVirtual || method.IsSpecialName) continue;

                var parameters = method.GetParameters();
                if (parameters == null || parameters.Length == 0) continue;

                var paramsSings = parameters.Select(p => p.ParameterType).GetTypesSignature();
                Trace.WriteLine($"This::{method.Name}::{paramsSings}");
            }
#endif

#if false
            //var types = string.Join(",", a4.Select(x => TypeExtensions.GetTypeSignature(x.GetType(), x)));
            //Trace.WriteLine(types);

            var paramsSings_1 = a4.GetParamsSignature();
            var paramsSings_2 = a5.GetParamsSignature();
            Trace.WriteLine($"Test11::{paramsSings_1}");
            Trace.WriteLine($"Test22::{paramsSings_2}");
            Trace.WriteLine($"Test::{paramsSings_2}");
#endif
        }

        private void DeviceWatcher_DeviceArrived(object sender, DeviceChangedEventArgs e)
        {
            //if (e.DeviceType != DeviceType.DBT_DEVTYP_DEVICEINTERFACE) return;
            Trace.WriteLine($"DeviceArrived::EventType:{e.EventType} DeviceType:{e.DeviceType} Type:{e.GetType().Name}");
            switch (e)
            {
                case VolumeDeviceChangedEventArgs vol:
                    Trace.WriteLine($"  DriveLetter:{vol.DriveLetter} IsNetwork:{vol.IsNetworkDrive} Drive:{vol.Drive} {vol.Drive.VolumeLabel}");
                    break;
                case PortDeviceChangedEventArgs port:
                    Trace.WriteLine($"  PortName:{port.PortName} FriendlyName:{port.PortFriendlyName}");
                    break;
                case InterfaceDeviceChangedEventArgs iface:
                    Trace.WriteLine($"  DevicePath:{iface.DevicePath} ClassGuid:{iface.ClassGuid}");
                    break;
            }
        }

        private void DeviceWatcher_DeviceRemoved(object sender, DeviceChangedEventArgs e)
        {
            //if (e.DeviceType != DeviceType.DBT_DEVTYP_DEVICEINTERFACE) return;
            Trace.WriteLine($"DeviceRemoved::EventType:{e.EventType} DeviceType:{e.DeviceType} Type:{e.GetType().Name}");
            switch (e)
            {
                case VolumeDeviceChangedEventArgs vol:
                    Trace.WriteLine($"  DriveLetter:{vol.DriveLetter} IsNetwork:{vol.IsNetworkDrive}");
                    break;
                case PortDeviceChangedEventArgs port:
                    Trace.WriteLine($"  PortName:{port.PortName} FriendlyName:{port.PortFriendlyName}");
                    break;
                case InterfaceDeviceChangedEventArgs iface:
                    Trace.WriteLine($"  DevicePath:{iface.DevicePath} ClassGuid:{iface.ClassGuid}");
                    break;
            }
        }



        private string F2(ICollection<byte> b)
        {
            Trace.WriteLine(b.Count);
            return b.Count.ToString();
        }

        public void Echo(string msg, string msg2)
        {
            Trace.WriteLine($"ECHO::{msg},,{msg2}");
        }

        public bool Echo2(string msg)
        {
            Trace.WriteLine($"ECHO2::{msg},,,{TextBox_0.Text}");
            return true;
        }

        public async Task<string> Test(string msg)
        {
            Trace.WriteLine($"MSG:::{msg}");
            await Task.Delay(1000).ConfigureAwait(false);

            Trace.WriteLine($"MSG:::{msg} ....");

            return $"OK~{msg} />t";
        }
        public async Task Test2(string msg)
        {
            Trace.WriteLine($"MSG:::{msg}");
            Trace.WriteLine($"MSG:::{msg} ....");
            await Task.Delay(10);
            //return $"OK~{msg}";
        }

        public IEnumerable<IEnumerable<int>> SetColor(Color color)
        {
            Rectangle_0.Fill = new SolidColorBrush(color);

            var a0 = new List<int>() { 1, 2, 3, 4, 5 };
            var a1 = new List<int>() { 6, 7, 8, 9, 10 };

            return new List<List<int>>() { a0, a1 };
        }
        public IEnumerable<int> SetColor1(Color color)
        {
            Rectangle_0.Fill = new SolidColorBrush(color);

            var a0 = new List<int>() { 1, 2, 3, 4, 5 };
            var a1 = new List<int>() { 6, 7, 8, 9, 10 };

            return a0;
        }
        public Color SetColor2(Color color)
        {
            Rectangle_0.Fill = new SolidColorBrush(color);

            var a0 = new List<int>() { 1, 2, 3, 4, 5 };
            var a1 = new List<int>() { 6, 7, 8, 9, 10 };

            return Colors.Red;
        }
        public IEnumerable<int> SetColors(IEnumerable<Color> colors)
        {
            Trace.WriteLine($"IEnumerable<Color> colors");
            return new int[] { 1, 2, 3 };
        }

        public string SetColors(IEnumerable<IEnumerable<Color>> colors)
        {
            Trace.WriteLine($"IEnumerable<IEnumerable<Color>> colors");
            return "hello world, \"test\" hell.";
        }

        public int SetColors(IEnumerable<IEnumerable<Color>> colors, IEnumerable<int> widths)
        {
            //Trace.WriteLine($"IEnumerable<IEnumerable<Color>> colors, IEnumerable<int> widths");
            return 12;
        }

        public int SetColors(IEnumerable<IEnumerable<IEnumerable<Color>>> colors, IEnumerable<int> widths)
        {
            Trace.WriteLine($"IEnumerable<IEnumerable<IEnumerable<Color>>> colors, IEnumerable<int> widths");
            return 16;
        }

        public int SetColors2(IReadOnlyList<IReadOnlyList<Color>> colors, IEnumerable<int> widths)
        {
            byte[] array = new byte[colors.Count];
            Trace.WriteLine($"IEnumerable<IEnumerable<IEnumerable<Color>>> colors, IEnumerable<int> widths");
            return 16;
        }

        /// <summary>
        /// 获取本机的 IPv4 地址
        /// </summary>
        /// <returns></returns>
        public static IEnumerable<IPAddress> GetLocalIPAddresses()
        {
            IPHostEntry host = Dns.GetHostEntry(Dns.GetHostName());
            IEnumerable<IPAddress> ips = from ipAddress in host.AddressList
                                         where ipAddress.AddressFamily == AddressFamily.InterNetwork
                                         select ipAddress;

            return ips;
        }
    }

    public static partial class WindowExtensions
    {
        public static void SetWindowState(this Window window, WindowState state)
        {
            Trace.WriteLine($"SetWindowState(this Window window, WindowState state)");
        }

        public static void SetWindowState(this MainWindow window, string state, int a)
        {
            Trace.WriteLine($"SetWindowState(this MainWindow window, WindowState state, int a)");
        }
    }
}
