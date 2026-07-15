using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
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
using SpaceCG.Extensions;
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

        public MainWindow()
        {
            InitializeComponent();

            rpcServer = new RpcServer4X(2000);
            rpcServer.RegisterObject("Demo", this);
            rpcServer.Start();
            
        }
        protected override void OnClosing(CancelEventArgs e)
        {
            base.OnClosing(e);

            rpcServer?.Dispose();
        }

        protected override async void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            Trace.WriteLine($"Key: {e.Key}");

            switch(e.Key)
            {
                case Key.D1:
                    await rpcClient.InvokeActionAsync("Demo", "test", new object[] {1,2 });
                    break;

                case Key.D2:
                    var result = await rpcClient.InvokeFuncAsync("Demo", "SetColor", new object[] { "#FF00FF00" });
                    Trace.WriteLine($"Response::{result}");
                    Trace.WriteLine($"ReturnType::{result.ReturnType}");
                    Trace.WriteLine($"ReturnValue::{result.ReturnValue}");

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
                    test();
                    break;
            }
        }

        Stopwatch stopwatch = new Stopwatch();

        private void test()
        {

            var s0 = "0x01,True,32,False";
            var s1 = "0x01,3,[True,True,False]";
            var s2 = "0x01,[0,3,4,7],[True,True,False,True]";
            var s3 = "[[#FFDDDDDD,#00DDDDDD]],[15],1.5,-1";
            var s4 = "[[#FFFFFF00,#FF00FF00],[#FFFFFF00,]]";
            var s5 = "[#FFFFFF00,#FF00FF00]";


            stopwatch.Restart();

            var a0 = StringExtensions.ParseParameters(s0);
            var a1 = StringExtensions.ParseParameters(s1);
            var a2 = StringExtensions.ParseParameters(s2);
            var a3 = StringExtensions.ParseParameters(s3);
            var a4 = StringExtensions.ParseParameters(s4);
            var a5 = StringExtensions.ParseParameters(s5);

            var a44 = a4.GetParameterSignature();

            var t0 = a0.Select(x => x.GetType()).ToList();
            var t1 = a1.Select(x => x.GetType()).ToList();
            var t2 = a2.Select(x => x.GetType()).ToList();
            var t3 = a3.Select(x => x.GetType()).ToList();
            var t4 = a4.Select(x => x.GetType()).ToList();
            var t5 = a5.Select(x => x.GetType()).ToList();

            var si0 = t0.GetParameterSignature();
            var si1 = t1.GetParameterSignature();
            var si2 = t2.GetParameterSignature();
            var si3 = t3.GetParameterSignature();
            var si4 = t4.GetParameterSignature();
            var si5 = t5.GetParameterSignature();

            var ticks = stopwatch.ElapsedTicks;

            Trace.WriteLine($"ticks:{ticks}");

            Trace.WriteLine($"test");
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            //var str = "System.Collections.Generic.IEnumerable`1[System.Collections.Generic.IEnumerable`1[System.Int32]]";
            //var returnType = Type.GetType(str, true);

            rpcClient = new RpcClient4X("127.0.0.1", 2001);
            rpcClient.Connect();

            const string Dictionary = nameof(Dictionary);

            IList<List<int>> a6 = new List<List<int>>()
            {
                new List<int>(){ 1,2,3, },
                new List<int>(){ 4,5,6, },
            };
            Trace.WriteLine($">>{StringExtensions.ConvertToString(a6)}<<");

            var a7 = "'hello world, \"test\" hell.'";
            Trace.WriteLine($">>{StringExtensions.ConvertToString(a7)}<<");


            var bytes = new byte[] {0x01,0x02 };
            var type = bytes.GetType();
            Trace.WriteLine(bytes.GetType());

            F2(bytes);
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
            Trace.WriteLine($"ECHO2::{msg}");
            return true;
        }

        public IEnumerable<IEnumerable<int>> SetColor(Color color)
        {
            Rectangle_0.Fill = new SolidColorBrush(color);

            var a0 = new List<int>() { 1, 2, 3, 4, 5 };
            var a1 = new List<int>() { 6, 7, 8, 9, 10 };

            return new List<List<int>>() { a0, a1 };
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
    }

    public static partial class WindowExtensions
    {
        public static void SetWindowState(this Window window, WindowState state)
        {

        }

        public static void SetWindowState(this MainWindow window, string state)
        {

        }
    }
}
