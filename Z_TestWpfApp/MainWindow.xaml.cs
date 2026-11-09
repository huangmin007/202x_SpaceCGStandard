using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.IO.Ports;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using SpaceCG;
using SpaceCG.Device;
using SpaceCG.Diagnostics;
using SpaceCG.Extensions;
using SpaceCG.Generic;
using SpaceCG.IO;
using SpaceCG.Net;
using SpaceCG.NModbus;
using SpaceCG.NModbus.Data;
using Trace = System.Diagnostics.Trace;

namespace Z_TestWpfApp
{
    /// <summary>
    /// MainWindow.xaml 的交互逻辑
    /// </summary>
    public partial class MainWindow : Window
    {
        RpcServerBase rpcServer;

        ZipArchive zipArchive;
        private readonly ConcurrentDictionary<int, string> BitmapFiles = new ConcurrentDictionary<int, string>();



        public MainWindow()
        {
            InitializeComponent();            
            Trace.Listeners.Add(new LoggerTraceListener(true));         
        }

        protected override async void OnClosing(CancelEventArgs e)
        {
            base.OnClosing(e);

            zipArchive?.Dispose();
        }

        protected override async void OnKeyDown(KeyEventArgs e)
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
            }
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            zipArchive = ZipFile.OpenRead("D:\\SpaceCG\\0A_Projects\\2024\\2024_WPF_Animation\\WPF_Animation\\WPF_Animations\\bin\\Debug\\net6.0-windows\\Resources\\KeyFrames\\leopard_red.zip");

            var files = GetImageFileNames(zipArchive, new string[] { ".png", ".jpg", ".jpeg", ".bmp" });

            BitmapFiles.Clear();
            foreach (var file in files)
                BitmapFiles.AddOrUpdate(file.Key, file.Value, (k, v) => v);

            Trace.WriteLine($"BitmapFiles: {BitmapFiles.Count}");
            foreach (var file in BitmapFiles)
                Trace.WriteLine($"file:{file.Value}  {Path.GetFileNameWithoutExtension(file.Value)}");
            //zipArchive.GetEntry("");
        }

        /// <summary>
        /// 获取图像文件列表，并按文件名序号(文件名中的第一组数字)排列
        /// </summary>
        /// <param name="folder"></param>
        /// <returns>Key 为从 0 开始的连续帧序号，Value 为完整文件路径</returns>
        public static IEnumerable<KeyValuePair<int, string>> GetImageFileNames(string folder, IEnumerable<string> extensions)
        {
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
                yield break;

            var regex = new Regex(@"\d+");
            //var extensions = new string[] { ".png", ".jpg", ".jpeg", ".bmp" };

            var files = from fpath in Directory.GetFiles(folder, "*.*", SearchOption.TopDirectoryOnly)
                        let fextn = Path.GetExtension(fpath)?.ToLower()
                        where extensions.Contains(fextn, StringComparer.OrdinalIgnoreCase)
                        let match = regex.Match(Path.GetFileNameWithoutExtension(fpath))
                        where match.Success && int.TryParse(match.Value, out int number)
                        orderby int.Parse(match.Value)
                        select fpath;

            int index = 0;
            foreach (var file in files)
                yield return new KeyValuePair<int, string>(index++, file);
        }

        public static IEnumerable<KeyValuePair<int, string>> GetImageFileNames(ZipArchive zipArchive, IEnumerable<string> extensions)
        {
            if (zipArchive == null || zipArchive.Entries.Count == 0) yield break;

            var regex = new Regex(@"\d+");
            //var extensions = new string[] { ".png", ".jpg", ".jpeg", ".bmp" };

            var files = from entry in zipArchive.Entries
                        let fextn = Path.GetExtension(entry.FullName)?.ToLower()
                        where extensions.Contains(fextn, StringComparer.OrdinalIgnoreCase)
                        let match = regex.Match(Path.GetFileNameWithoutExtension(entry.FullName))
                        where match.Success && int.TryParse(match.Value, out int number)
                        orderby int.Parse(match.Value)
                        select entry.FullName;

            int index = 0;
            foreach (var file in files)
                yield return new KeyValuePair<int, string>(index++, file);
        }
    }
}
