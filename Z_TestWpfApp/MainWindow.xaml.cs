using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using HidSharp;
using SpaceCG;
using SpaceCG.Controls;
using SpaceCG.Device;
using SpaceCG.Diagnostics;
using SpaceCG.Extensions;
using SpaceCG.Generic;
using SpaceCG.IO;
using Trace = System.Diagnostics.Trace;

namespace Z_TestWpfApp
{
    /// <summary>
    /// MainWindow.xaml 的交互逻辑
    /// </summary>
    public partial class MainWindow : Window
    {
        //RpcServerBase rpcServer;

        ZipArchive zipArchive;
        private readonly ConcurrentDictionary<int, string> BitmapFiles = new ConcurrentDictionary<int, string>();

        private DemoControlManager demoControlManager;

        public MainWindow()
        {
            InitializeComponent();            
            Trace.Listeners.Add(new LoggerTraceListener(true));

            demoControlManager = new DemoControlManager(this);
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            base.OnClosing(e);
            zipArchive?.Dispose();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            Trace.TraceInformation($"Key: {e.Key}");
            
            switch (e.Key)
            {
                case Key.D0:
                    imageSeq.Seek(99);
                    break;
                case Key.D1:
                    imageSeq.Seek(3);
                    break;

                case Key.D2:
                    imageSeq.AutoPlay = false;
                    //imageSeq.FilePath = "D:\\SpaceCG\\0A_Projects\\2024\\2024_WPF_Animation\\WPF_Animation\\WpfApp_KFs\\bin\\Debug\\net6.0-windows\\出场序列帧";
                    imageSeq.FilePath = "D:\\SpaceCG\\0A_Projects\\2024\\2024_WPF_Animation\\WPF_Animation\\WpfApp_KFs\\bin\\Debug\\net6.0-windows\\Car_KFs";
                    break;

                case Key.D3:
                    imageSeq.AutoPlay = true;
                    imageSeq.FilePath = "D:\\SpaceCG\\0A_Projects\\2024\\2024_WPF_Animation\\WPF_Animation\\WpfApp_KFs\\bin\\Debug\\net6.0-windows\\Car_KFs.zip";
                    break;

                case Key.Space:
                    if (imageSeq.IsPlaying)
                        imageSeq.Pause();
                    else
                        imageSeq.Play();
                    break;

                case Key.D5:
                    this.Language = System.Windows.Markup.XmlLanguage.GetLanguage("zh-CN");
                    break;
            }
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            DeviceList.Local.Changed += Local_Changed;
        }

        private void Local_Changed(object sender, DeviceListChangedEventArgs e)
        {
            Trace.WriteLine($"Device Changed: {e}");
            var devices = DeviceList.Local.GetSerialDevices();
            foreach(var device in devices)
            {
                Trace.WriteLine($"Device Changed: {device.DevicePath} {device.GetSerialNumber()}");
            }
        }

        private void ImageSeq_RenderChanged(object sender, EventArgs e)
        {
            var imageSeq = sender as ImageSequence;
            Trace.WriteLine($"RenderChanged: {imageSeq.FrameIndex}/{imageSeq.FrameCount}");

            //if (imageSeq.FrameIndex == 20) imageSeq.Pause();
        }
    }
}
