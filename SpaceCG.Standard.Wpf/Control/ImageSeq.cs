using System;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SpaceCG.Extensions;

namespace SpaceCG.Control
{
    public class ImageSeq : Image
    {
        /// <summary>
        /// Frame Render Changed Event
        /// </summary>
        public event EventHandler<EventArgs> RenderChanged;

        #region Dependency Properties
        /// <summary> FilePath Property  </summary>
        public static readonly DependencyProperty FilePathProperty = DependencyProperty.Register(nameof(FilePath), typeof(string), typeof(ImageSeq), new PropertyMetadata(null, (d, e)=>
        {
            var ctl = d as ImageSeq;
            if (ctl != null) ctl.OnFilePathChanged();
        }));
        /// <summary>
        /// 序列帧所在文件夹
        /// <para>序列帧图片支持 *.png|*.jpg|*.bmp|*.jpeg 格式；图片文件名中需要有数字序号，用于计算排序。</para>
        /// </summary>
        public string FilePath
        {
            get => (string)GetValue(FilePathProperty);
            set => SetValue(FilePathProperty, value);
        }

        /// <summary> ABLoopStart Property  </summary>
        public static readonly DependencyProperty ABLoopStartProperty =
            DependencyProperty.Register(nameof(ABLoopStart), typeof(int), typeof(ImageSeq), new PropertyMetadata(0, (d, e) =>
            {
                var ctl = d as ImageSeq;
                if (ctl != null) ctl.abStartIndex = (int)e.NewValue;
            }));
        /// <summary> AB循环起始帧索引。
        /// <para>默认值为 0。</para>  
        /// </summary>
        public int ABLoopStart
        {
            get => (int)GetValue(ABLoopStartProperty);
            set => SetValue(ABLoopStartProperty, value);
        }
        /// <summary> ABLoopEnd Property  </summary>
        public static readonly DependencyProperty ABLoopEndProperty =
            DependencyProperty.Register(nameof(ABLoopEnd), typeof(int), typeof(ImageSeq), new PropertyMetadata(-1, (d, e) =>
            {
                var ctl = d as ImageSeq;
                if (ctl != null) ctl.abEndIndex = (int)e.NewValue;
            }));
        /// <summary> AB循环结束帧索引。
        /// <para>默认值为 -1 表示最后一帧。</para> 
        /// </summary>
        public int ABLoopEnd
        {
            get => (int)GetValue(ABLoopEndProperty);
            set => SetValue(ABLoopEndProperty, value);
        }

        /// <summary> IsPlaying Property  </summary>
        public static readonly DependencyProperty IsPlayingProperty =
            DependencyProperty.Register(nameof(IsPlaying), typeof(bool), typeof(ImageSeq), new PropertyMetadata(false));
        /// <summary> 序列是否正在播放  </summary>
        public bool IsPlaying
        {
            get => (bool)GetValue(IsPlayingProperty);
            private set => SetValue(IsPlayingProperty, value);
        }

        /// <summary> FrameIndex Property </summary>
        public static readonly DependencyProperty FrameIndexProperty =
            DependencyProperty.Register(nameof(FrameIndex), typeof(int), typeof(ImageSeq), new PropertyMetadata(-1));
        /// <summary> 序列当前播放的帧索引 </summary>
        public int FrameIndex
        {
            get => (int)GetValue(FrameIndexProperty);
            private set => SetValue(FrameIndexProperty, value);
        }

        /// <summary> FrameCount Property  </summary>
        public static readonly DependencyProperty FrameCountProperty =
            DependencyProperty.Register(nameof(FrameCount), typeof(int), typeof(ImageSeq), new PropertyMetadata(0, (d, e)=>
            {
                var ctl = d as ImageSeq;
                if (ctl != null) ctl.frameCount = (int)e.NewValue;
            }));
        /// <summary> 序列总帧数  </summary>
        public int FrameCount
        {
            get => (int)GetValue(FrameCountProperty);
            private set => SetValue(FrameCountProperty, value);
        }

        /// <summary>  UseCache Property  </summary>
        public static readonly DependencyProperty UseCacheProperty =
            DependencyProperty.Register(nameof(UseCache), typeof(bool), typeof(ImageSeq), new PropertyMetadata(false, (d, e) =>
            {
                var ctl = d as ImageSeq;
                if (ctl != null)
                {
                    ctl.useCacheFrames = (bool)e.NewValue;
                    if (!ctl.useCacheFrames) ctl.CacheFrames.Clear();
                }
            }));
        /// <summary> 使用序列缓存  </summary>
        public bool UseCache
        {
            get => (bool)GetValue(UseCacheProperty);
            set => SetValue(UseCacheProperty, value);
        }


        /// <summary> AutoPlay Property </summary>
        public static readonly DependencyProperty AutoPlayProperty =
            DependencyProperty.Register(nameof(AutoPlay), typeof(bool), typeof(ImageSeq), new PropertyMetadata(false));
        /// <summary>  打开序列帧目录后是否自动播放  </summary>
        public bool AutoPlay
        {
            get => (bool)GetValue(AutoPlayProperty);
            set => SetValue(AutoPlayProperty, value);
        }

        public static readonly DependencyProperty FpsProperty =
            DependencyProperty.Register(nameof(Fps), typeof(int), typeof(ImageSeq), new PropertyMetadata(25,
            (d, e) =>
            {
                var ctl = d as ImageSeq;
                if (ctl != null)
                {
                    var fps = (int)e.NewValue;
                    ctl.renderInterval = (int)Math.Round(1000.0 / fps, MidpointRounding.AwayFromZero);
                }
            },
            (d, e) =>
            {
                var fps = (int)e;
                return fps < 1 ? 1 : fps > 60 ? 60 : fps;
            }));
        /// <summary> 序列播放帧率，默认为 25  </summary>
        public int Fps
        {
            get => (int)GetValue(FpsProperty);
            set => SetValue(FpsProperty, value);
        }

        #endregion

        private const int DefaultRenderFrameCount = 8;

        private volatile int abEndIndex = -1;
        private volatile int abStartIndex = 0;
        private volatile int frameCount = 0;
        private volatile int seekFrameIndex = -1;
        private volatile bool useCacheFrames = false;

        private volatile int decoderIndex = -1;
        private volatile int renderInterval = 40;
        private double nextRenderMilliseconds = 0.0;

        private Int32Rect dirtyRect;
        private WriteableBitmap writeableBitmap;

        private Task decoderTask;
        private CancellationTokenSource cancellationTokenSource;
        private readonly Stopwatch renderStopwatch = new Stopwatch();

        /// <summary> 文件路径字典 </summary>
        private readonly ConcurrentDictionary<int, string> BitmapFiles = new ConcurrentDictionary<int, string>();
        /// <summary> 缓存的帧字典 </summary>
        private readonly ConcurrentDictionary<int, BitmapFrame> CacheFrames = new ConcurrentDictionary<int, BitmapFrame>();
        /// <summary> 待渲染的帧队列 </summary>
        private readonly ConcurrentQueue<(int Index, BitmapFrame Frame)> RenderFrames = new ConcurrentQueue<(int, BitmapFrame)>();

        /// <summary>
        /// 格式转换字典
        /// </summary>
        private static readonly ConcurrentDictionary<PixelFormat, (PixelFormat Format, BitmapPalette Palette)> FormatConvertedDict = new ConcurrentDictionary<PixelFormat, (PixelFormat, BitmapPalette)>()
        {
            [PixelFormats.Default] = (PixelFormats.Bgra32, null),
            [PixelFormats.Indexed1] = (PixelFormats.Bgra32, null),
            [PixelFormats.Indexed2] = (PixelFormats.Bgra32, null),
            [PixelFormats.Indexed4] = (PixelFormats.Bgra32, null),
            [PixelFormats.Indexed8] = (PixelFormats.Bgra32, null),
        };

        public ImageSeq()
        {
            abEndIndex = ABLoopEnd;
            abStartIndex = ABLoopStart;
            
            Loaded += ImageSeq_Loaded;
            Unloaded += ImageSeq_Unloaded;
        }

        private void ImageSeq_Loaded(object sender, RoutedEventArgs e)
        {
            Loaded -= ImageSeq_Loaded;

            cancellationTokenSource = new CancellationTokenSource();
            var cancellationToken = cancellationTokenSource.Token;
            decoderTask = Task.Run(() => DecodeBitmapFrameAsync(cancellationToken), cancellationToken);

            renderStopwatch.Restart();
            CompositionTarget.Rendering += CompositionTarget_Rendering;
        }
        private void ImageSeq_Unloaded(object sender, RoutedEventArgs e) { Unload(); }
        private void CompositionTarget_Rendering(object sender, EventArgs e)
        {
            var elapsed = renderStopwatch.ElapsedMilliseconds;
            if (elapsed < nextRenderMilliseconds || !IsPlaying) return;

            var interval = renderInterval;
            nextRenderMilliseconds += interval;

            // UI 线程被长时间占用而落后太多时重新对齐，避免"追帧"连刷
            if (nextRenderMilliseconds <= elapsed) 
                nextRenderMilliseconds = elapsed + interval;

            //var ms0 = renderStopwatch.ElapsedMilliseconds;
            RenderNextFrame();
            //var ms1 = renderStopwatch.ElapsedMilliseconds;
            //Trace.WriteLine($"Render Use: {ms1 - ms0} ms");
        }
        /// <summary> 从帧队列中渲染下一帧  </summary>
        protected void RenderNextFrame()
        {
            if (!RenderFrames.TryDequeue(out var bitmap)) return;

            RenderBitmapFrame(bitmap.Frame);
            if (FrameIndex != bitmap.Index)
            {
                FrameIndex = bitmap.Index;
                this.RenderChanged?.Invoke(this, EventArgs.Empty);
                //Trace.WriteLine($"Render Frame Index: {bitmap.Index}");
            }            
        }
        private void AwaitRenderNextFrame()
        {
            Task.Run(async () =>
            {
                while (RenderFrames.IsEmpty)
                {
                    await Task.Delay(1);
                }
                await this.Dispatcher.InvokeAsync(() => RenderNextFrame());
            });
        }
        /// <summary> 渲染指定的 <see cref="BitmapFrame"/> 对象  </summary>
        /// <param name="bitmapFrame"></param>
        /// <exception cref="ArgumentNullException"></exception>
        protected void RenderBitmapFrame(BitmapFrame bitmapFrame)
        {
            if (bitmapFrame == null) 
                throw new ArgumentNullException();

            var w = bitmapFrame.PixelWidth;
            var h = bitmapFrame.PixelHeight;

            var stride = writeableBitmap.BackBufferStride;
            var bufferSize = stride * h;

            writeableBitmap.Lock();
            try
            {
                var sourceRect = new Int32Rect(0, 0, w, h);
                bitmapFrame.CopyPixels(sourceRect, writeableBitmap.BackBuffer, bufferSize, stride);
                writeableBitmap.AddDirtyRect(dirtyRect);
            }
            catch (Exception ex)
            {
                Trace.TraceWarning($"RenderBitmapFrame Exception: {ex.Message}");
            }
            finally
            {
                writeableBitmap.Unlock();
            }
        }
        /// <summary> 解码图像帧  </summary>
        /// <param name="cancellationToken"></param>
        private async void DecodeBitmapFrameAsync(CancellationToken cancellationToken)
        {
            var stopwatch = Stopwatch.StartNew();
            var renderFrameCount = DefaultRenderFrameCount;

            while (!cancellationToken.IsCancellationRequested)
            {
                if (BitmapFiles.IsEmpty)
                {
                    decoderIndex = 0;
                    try { await Task.Delay(10, cancellationToken); }
                    catch { }
                    continue;
                }

                if (RenderFrames.Count >= renderFrameCount)
                {
                    try { await Task.Delay(2, cancellationToken); }
                    catch { }
                    continue;
                }

                if (seekFrameIndex >= 0 && seekFrameIndex < frameCount)
                {
                    RenderFrames.Clear();
                    decoderIndex = seekFrameIndex;
                    seekFrameIndex = -1;
                }

                var position = decoderIndex;
                var useCache = useCacheFrames;
                if (!BitmapFiles.ContainsKey(position))
                {
                    try { await Task.Delay(2, cancellationToken); }
                    catch { }
                    continue;
                }

                try
                {
                    stopwatch.Restart();
                    BitmapFrame bitmapFrame;

                    if (useCache && CacheFrames.TryGetValue(position, out bitmapFrame))
                    {
                        RenderFrames.Enqueue((position, bitmapFrame));
                    }
                    else
                    {
                        var filePath = BitmapFiles[position];
                        using (var fs = File.OpenRead(filePath))
                        {
                            var decoder = BitmapDecoder.Create(fs, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                            bitmapFrame = decoder.Frames[0];

                            if (FormatConvertedDict.TryGetValue(bitmapFrame.Format, out var target))
                            {
                                BitmapSource bitmatSource = new FormatConvertedBitmap(bitmapFrame, target.Format, target.Palette, 0);
                                bitmatSource = new CachedBitmap(bitmatSource, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                                //bitmatSource.Freeze();
                                bitmapFrame = BitmapFrame.Create(bitmatSource);
                            }

                            bitmapFrame.Freeze();
                        }

                        RenderFrames.Enqueue((position, bitmapFrame));
                        if (useCache) CacheFrames.AddOrUpdate(position, bitmapFrame, (k, v) => bitmapFrame);
                    }
                    var elapsed = stopwatch.ElapsedMilliseconds;
                    //Trace.WriteLine($"DecodeBitmap: {position}  DecodeUseTime:{elapsed}ms  QueueCount:{RenderFrames.Count}");
                }
                catch (Exception ex)
                {
                    Trace.TraceWarning($"DecodeBitmap Exception: {ex.Message}");
                }

                // AB Loop AB值设置错误或超出则会出现停止不解码、不渲染
                var endIndex = abEndIndex;
                if (endIndex < 0) endIndex = frameCount + endIndex;
                decoderIndex = position == endIndex ? abStartIndex : position + 1;
            }
            stopwatch.Stop();
        }

        /// <summary> 文件路径发生变化时触发 </summary>
        protected void OnFilePathChanged()
        {
            //Trace.WriteLine($"FilePath Changed ... {FilePath}");
            if (string.IsNullOrWhiteSpace(FilePath)) return;

            var folder = Path.IsPathRooted(FilePath) ? FilePath : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, FilePath);
            if (!Directory.Exists(folder)) return;

            var files = GetImageFiles(folder);
            if (files.Length == 0) return;

            BitmapFrame bitmapFrame;
            var firstPath = files.FirstOrDefault();
            using (var fs = File.OpenRead(firstPath))
            {
                var decoder = BitmapDecoder.Create(new Uri(firstPath), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                bitmapFrame = decoder.Frames[0];

                if (DesignerProperties.GetIsInDesignMode(this))
                {
                    base.Source = bitmapFrame;
                    return;
                }
            }

            BitmapFiles.Clear();
            CacheFrames.Clear();
            RenderFrames.Clear();

            FrameIndex = -1;
            IsPlaying = AutoPlay;
            FrameCount = files.Length;

            decoderIndex = 0;
            seekFrameIndex = -1;
            writeableBitmap = null;

            var w = bitmapFrame.PixelWidth;
            var h = bitmapFrame.PixelHeight;
            dirtyRect = new Int32Rect(0, 0, w, h);

            var pixelFormat = bitmapFrame.Format;
            var bitmapPalette = bitmapFrame.Palette;
            Trace.TraceInformation($"{FilePath} FrameCount:{frameCount} PixelFormat: {pixelFormat} Size:{w}x{h}");

            if (FormatConvertedDict.TryGetValue(pixelFormat, out var target))
            {
                pixelFormat = target.Format;
                bitmapPalette = target.Palette;
            }
            writeableBitmap = new WriteableBitmap(w, h, bitmapFrame.DpiX, bitmapFrame.DpiY, pixelFormat, bitmapPalette);
            this.Source = writeableBitmap;
            
            for (var i = 0; i < files.Length; i++)
            {
                BitmapFiles.TryAdd(i, files[i]);
            }
            if (!IsPlaying) AwaitRenderNextFrame();
        }

        /// <inheritdoc />
        protected override void OnInitialized(EventArgs e)
        {
            base.OnInitialized(e);
            if (AutoPlay) Play();
        }

        /// <summary>  卸载资源  </summary>
        public void Unload()
        {
            Unloaded -= ImageSeq_Unloaded;
            CompositionTarget.Rendering -= CompositionTarget_Rendering;

            BitmapFiles.Clear();
            RenderFrames.Clear();
            renderStopwatch.Stop();

            try
            {
                cancellationTokenSource?.Cancel();
            }
            catch { }

            try
            {
                decoderTask?.Wait(100);
                decoderTask?.Dispose();
            }
            finally
            {
                decoderTask = null;
            }

            try
            {
                cancellationTokenSource?.Dispose();
            }
            finally
            {
                cancellationTokenSource = null;
            }
        }
        /// <summary> 播放序列  </summary>
        public void Play() { IsPlaying = true; }
        /// <summary> 暂停序列  </summary>
        public void Pause() { IsPlaying = false; }
        /// <summary>
        /// Seek 到指定的帧索引
        /// </summary>
        /// <param name="frameIndex"></param>
        public void Seek(int frameIndex)
        {
            if (frameIndex < 0 || frameIndex >= frameCount)
                throw new ArgumentOutOfRangeException(nameof(frameIndex));
            if (frameIndex == FrameIndex) return;

            RenderFrames.Clear();
            decoderIndex = -1;
            seekFrameIndex = frameIndex;

            if (!IsPlaying) AwaitRenderNextFrame();
        }

        /// <summary>
        /// 获取图像文件列表，并按文件名序号(文件名中的第一组数字)排列
        /// </summary>
        /// <param name="folder"></param>
        /// <returns></returns>
        public static string[] GetImageFiles(string folder)
        {
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
                return Array.Empty<string>();

            var regex = new Regex(@"\d+");
            var extensions = new string[] { ".png", ".jpg", ".jpeg", ".bmp" };

            var files = from fpath in Directory.GetFiles(folder, "*.*", SearchOption.TopDirectoryOnly)
                        let fextn = Path.GetExtension(fpath)?.ToLower()
                        where extensions.Contains(fextn, StringComparer.OrdinalIgnoreCase)
                        let match = regex.Match(Path.GetFileNameWithoutExtension(fpath))
                        where match.Success && int.TryParse(match.Value, out int number)
                        orderby int.Parse(match.Value)
                        select fpath;

            return files.ToArray();
        }

    }
}
