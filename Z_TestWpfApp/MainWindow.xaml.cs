using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
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
        RPCServer rpcServer;
        public MainWindow()
        {
            InitializeComponent();

            rpcServer = new RPCServer(2000);
            rpcServer.Start();
        }
        protected override void OnClosing(CancelEventArgs e)
        {
            base.OnClosing(e);

            rpcServer?.Dispose();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            var str = "normAl";
            Trace.WriteLine(str.ConvertTo(typeof(WindowState)));

            var s0 = "0x01,True,32,False";
            var s1 = "0x01,3,[True,True,False]";
            var s2 = "0x01,[0,3,4,7],[True,True,False,True]";
            var s3 = "[[#FFDDDDDD,#00DDDDDD]],[15],1.5,-1";
            var s4 = "'hello,world',0x01,3,'ni?,hao,[aa,bb]', [True,True,False],['aaa,bb,c','ni,hao'],15,\"aa,aaa\",15";
            var s5 = "1,,3";

            var a0 = s0.ToObjectArray();
            var a1 = s1.ToObjectArray();
            var a2 = s2.ToObjectArray();
            var a3 = s3.ToObjectArray();
            var a4 = s4.ToObjectArray();
            var a5 = s5.ToObjectArray();

            Trace.WriteLine($"test");

        }
    }
}
