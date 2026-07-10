using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Net.Sockets;
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
using SpaceCG.Extensions;
using SpaceCG.Net;

namespace Z_TestWpfApp
{
    /// <summary>
    /// MainWindow.xaml 的交互逻辑
    /// </summary>
    public partial class MainWindow : Window
    {
        RPCServerBase rpcServer;
        public MainWindow()
        {
            InitializeComponent();

            rpcServer = new RPCServer4X(2000);
            rpcServer.RegisterObject("Demo", this);
            //rpcServer.RegisterObject("Window", this);
            rpcServer.Start();
        }
        protected override void OnClosing(CancelEventArgs e)
        {
            base.OnClosing(e);

            rpcServer?.Dispose();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            var s0 = "0x01,True,32,False";
            var s1 = "0x01,3,[True,True,False]";
            var s2 = "0x01,[0,3,4,7],[True,True,False,True]";
            var s3 = "[[#FFDDDDDD,#00DDDDDD]],[15],1.5,-1";
            var s4 = "[[#FFFFFF00,#FF00FF00],[#FFFFFF00]]";
            var s5 = "[#FFFFFF00,#FF00FF00]";

            var a0 = s0.ToObjectArray();
            var a1 = s1.ToObjectArray();
            var a2 = s2.ToObjectArray();
            var a3 = s3.ToObjectArray();
            var a4 = s4.ToObjectArray();
            var a5 = s5.ToObjectArray();

            Trace.WriteLine($"test");


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

        public void Echo(string msg)
        {
            Trace.WriteLine($"ECHO::{msg}");
        }

        public bool Echo2(string msg)
        {
            Trace.WriteLine($"ECHO2::{msg}");
            return true;
        }

        public bool SetColor(Color color)
        {
            Rectangle_0.Fill = new SolidColorBrush(color);
            return true;
        }
        public void SetColors(IEnumerable<Color> colors)
        {
            Trace.WriteLine($"IEnumerable<Color> colors");
        }

        public void SetColors(IEnumerable<IEnumerable<Color>> colors)
        {
            Trace.WriteLine($"IEnumerable<IEnumerable<Color>> colors");
        }

        public int SetColors(IEnumerable<IEnumerable<Color>> colors, IEnumerable<int> widths)
        {
            Trace.WriteLine($"IEnumerable<IEnumerable<Color>> colors, IEnumerable<int> widths");
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
