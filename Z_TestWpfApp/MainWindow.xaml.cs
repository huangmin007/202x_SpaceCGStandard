using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Net.Sockets;
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
            rpcServer.RegisterObject("Window", this);
            rpcServer.Start();
        }
        protected override void OnClosing(CancelEventArgs e)
        {
            base.OnClosing(e);

            rpcServer?.Dispose();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            var str = "0x01";
            var res = int.TryParse(str, out var value);
            float.TryParse(str, out var fvalue);
            //Trace.WriteLine(str.ConvertTo(typeof(WindowState)));
            
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

        public void SetColors(IEnumerable<IEnumerable<Color>> colors, IEnumerable<int> widths)
        {

        }
        public void SetColors(IEnumerable<IEnumerable<Color>> colors, IReadOnlyList<int> widths)
        {

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
