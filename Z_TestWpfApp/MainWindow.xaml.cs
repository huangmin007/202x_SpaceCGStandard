using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO.Ports;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using SpaceCG.Device;
using SpaceCG.Extensions;
using SpaceCG.Generic;
using SpaceCG.Net;
using static System.Net.Mime.MediaTypeNames;
using Point = System.Drawing.Point;

namespace Z_TestWpfApp
{
    /// <summary>
    /// MainWindow.xaml 的交互逻辑
    /// </summary>
    public partial class MainWindow : Window
    {
        RpcServerBase rpcServer;
        RpcClientBase rpcClient;
        LedRenderControl ledRenderControl;

        private LedRenderBus _ledRenderBus;

        public MainWindow()
        {
            InitializeComponent();
            
            Trace.Listeners.Add(new LoggerTraceListener(true));
         
        }
        protected override void OnClosing(CancelEventArgs e)
        {
            base.OnClosing(e);

            rpcServer?.Dispose();
            _ledRenderBus?.Dispose();
            _cts?.Cancel();
        }

        protected override async void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            Trace.TraceInformation($"Key: {e.Key}");
            
            stopwatch.Restart();
            long ms = 0;
            switch (e.Key)
            {
                case Key.D0:
                    //ledRenderControl.RenderSceneId(0);
                    break;
                case Key.D1:
                    //ledRenderControl.RenderSceneId(1);
                    await rpcClient.InvokeActionAsync("Demo", "Test", new object[] {1,2 });
                    break;

                case Key.D2:
                    ledRenderControl.RenderSceneId(2);
#if true
                    if (rpcClient == null) break;
                    var result = await rpcClient.InvokeFuncAsync("Demo", nameof(Test), new object[] { "Hello,world" });
                    //var result = await rpcClient.InvokeFuncAsync("Demo", nameof(SetColor), new object[] { Colors.Red }); 
                    Trace.TraceInformation($"Response::{result}");
                    Trace.TraceInformation($"ReturnType::{result.ReturnType}");
                    Trace.TraceInformation($"ReturnValue::{result.GetReturnValue<string>()}");
                    //Trace.TraceInformation($"ReturnValue::{result.GetReturnValue<IEnumerable<IEnumerable<int>>>()}");  //IEnumerable<IEnumerable<int>>

                    //var value22 = result.GetReturnValue<IEnumerable<IEnumerable<int>>>();
                    //Trace.WriteLine($"{string.Join(",", value22)}");
#endif
                    break;

                case Key.D9:
                    break;

                case Key.D:
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

                case Key.C:
                    _ledRenderBus.OpenChannel();
                    _ledRenderBus.StartRender();
                    break;

                case Key.V:
                    _ledRenderBus.AddColorFrame(0x0001, 0x01, 0xFF003300, 0, 1, 10);
                    break;

                case Key.B:
                    LedRenderBus.Collections[0].PauseRender(0x0001);
                    LedRenderBus.Collections[0].ClearRender(0x0001, true);

                    break;
                case Key.N:
                    LedRenderBus.Collections.ResumeRender();
                    break;
            }
        }

        Stopwatch stopwatch = new Stopwatch();
        Task connectTask;
        CancellationTokenSource _cts;

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            rpcServer = new RpcServer4X(2000);
            rpcServer.RegisterObject("Demo", this);
            rpcServer.Start();

            rpcClient = new RpcClient4X("127.0.0.1", 2000);
            rpcClient.Connect();

            var parser = new FooterProtocolParser(new byte[] { 0x0D, 0x0A });
            parser.FrameReceived += (s0, e0) =>
            {
                var message = Encoding.UTF8.GetString(e0.FrameView.Array, e0.FrameView.Offset, e0.FrameView.Count);
                Trace.WriteLine($"{DateTime.Now:hh:mm:ss.fff}收到消息: {message}");
            };
            _cts = new CancellationTokenSource();

            //TcpClient client = new TcpClient();
            //_ = client.ConnectAsync("127.0.0.1", 2001);
            //connectTask = client.ReceiveParseAsync(parser, newClient => client = newClient, _cts.Token);
            //connectTask = client.ReceiveParseAsync("127.0.0.1", 2001, parser, newClient => client = newClient, _cts.Token);

            //UdpClient client = new UdpClient(2002);
            //connectTask = client.ReceiveParseAsync(parser, _cts.Token);

            //var serial = new SerialPort("COM3", 115200);
            //connectTask = serial.ReceiveParseAsync(parser, _cts.Token);

#if false
            _ledRenderBus = new LedRenderBus(SpaceCG.IO.ChannelType.SERIAL, "CH343,921600");
            var ledStrip = new LedStripObject(0x0001, 0x01);
            ledStrip.AddPoints(new Point(0, 0), new Point(99, 0));
            _ledRenderBus.AddLedStrip(ledStrip);
#else
            var config = XElementExtensions.LoadConfig($"Resources/Config.xml");
            ledRenderControl = new LedRenderControl(Canvas_Leds);
            ledRenderControl.InitializeComponent(config.Element("DrawingDisplay"), config.Element("LedDevices"), config.Element("Scenes"));
            //ledRenderControl.StartRender();
            //ledRenderControl.RenderSceneId(1);

            //LedRenderBus.Collections[0].SetDeviceBaudRate(0x0000, 0x0001, 921600);
            //LedRenderBus.Collections[0].SetPowerOnColor(0x0001, 0x01, 0x0000FF00, false, ColorFormat.ARGB);
#endif

            Trace.WriteLine("Ready ... ");
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
            await Task.Delay(2000).ConfigureAwait(false);

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
