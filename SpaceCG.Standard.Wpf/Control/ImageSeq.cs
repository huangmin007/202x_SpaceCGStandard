using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
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
    public sealed class ImageSeq : Image
    {
        /// <summary> 待渲染队列默认长度</summary>
        private const int DefaultRenderFrameCount = 8;
        private static readonly string[] SupportImageExtensions = new string[] { ".png", ".jpg", ".jpeg", ".bmp" };

        /// <summary> Frame Render Changed Event  </summary>
        public event EventHandler<EventArgs> RenderChanged;

        #region Dependency Properties
        /// <summary> 标识 <see cref="FilePath"/> 依赖属性。  </summary>
        public static readonly DependencyProperty FilePathProperty = DependencyProperty.Register(nameof(FilePath), typeof(string), typeof(ImageSeq), new PropertyMetadata(null, (d, e)=>
        {
            var ctl = d as ImageSeq;
            if (ctl != null) ctl.OnFilePathChanged();
        }));
        /// <summary>
        /// 序列帧所在文件夹，或是 zip 压缩包文件。
        /// <para>序列帧图片支持 *.png|*.jpg|*.bmp|*.jpeg 格式；图片文件名中需要有数字序号，用于计算排序。</para>
        /// </summary>
        public string FilePath
        {
            get => (string)GetValue(FilePathProperty);
            set => SetValue(FilePathProperty, value);
        }

        /// <summary> 标识 <see cref="ABLoopStart"/> 依赖属性。  </summary>
        public static readonly DependencyProperty ABLoopStartProperty =
            DependencyProperty.Register(nameof(ABLoopStart), typeof(int), typeof(ImageSeq), new PropertyMetadata(0, (d, e) =>
            {
                var ctl = d as ImageSeq;
                if (ctl != null) ctl.abStartIndex = (int)e.NewValue;
            }));
        /// <summary> AB循环起始帧索引。默认值为 0。 </summary>
        public int ABLoopStart
        {
            get => (int)GetValue(ABLoopStartProperty);
            set => SetValue(ABLoopStartProperty, value);
        }
        /// <summary> 标识 <see cref="ABLoopEnd"/> 依赖属性。  </summary>
        public static readonly DependencyProperty ABLoopEndProperty =
            DependencyProperty.Register(nameof(ABLoopEnd), typeof(int), typeof(ImageSeq), new PropertyMetadata(-1, (d, e) =>
            {
                var ctl = d as ImageSeq;
                if (ctl != null) ctl.abEndIndex = (int)e.NewValue;
            }));
        /// <summary> AB循环结束帧索引。默认值为 -1 表示 <see cref="FrameCount"/> - 1 位置的帧。</summary>
        public int ABLoopEnd
        {
            get => (int)GetValue(ABLoopEndProperty);
            set => SetValue(ABLoopEndProperty, value);
        }

        /// <summary> 标识 <see cref="AutoPlay"/> 依赖属性。  </summary>
        public static readonly DependencyProperty AutoPlayProperty =
            DependencyProperty.Register(nameof(AutoPlay), typeof(bool), typeof(ImageSeq), new PropertyMetadata(false));
        /// <summary> 打开序列帧目录后是否自动播放。默认值为 false，不自动播放，暂停在第一帧位置。 </summary>
        public bool AutoPlay
        {
            get => (bool)GetValue(AutoPlayProperty);
            set => SetValue(AutoPlayProperty, value);
        }

        /// <summary> 标识 <see cref="Fps"/> 依赖属性。 </summary>
        public static readonly DependencyProperty FpsProperty =
            DependencyProperty.Register(nameof(Fps), typeof(int), typeof(ImageSeq), new PropertyMetadata(25,
            (d, e) =>
            {
                var ctl = d as ImageSeq;
                if (ctl != null)
                {
                    var fps = (int)e.NewValue;
                    ctl.fps = fps;
                    ctl.renderInterval = (int)Math.Round(1000.0 / fps, MidpointRounding.AwayFromZero);
                }
            },
            (d, baseValue) =>
            {
                var fps = (int)baseValue;
                return fps < 1 ? 1 : fps > 60 ? 60 : fps;
            }));
        /// <summary> 设置序列播放帧率。默认为 25 fps </summary>
        public int Fps
        {
            get => (int)GetValue(FpsProperty);
            set => SetValue(FpsProperty, value);
        }

        /// <summary> 标识 <see cref="CacheStep"/> 依赖属性。 </summary>
        public static readonly DependencyProperty CacheStepProperty =
            DependencyProperty.Register(nameof(CacheStep), typeof(int), typeof(ImageSeq), new PropertyMetadata(0, 
            (d, e) =>
            {
                var ctl = (ImageSeq)d;
                var step = (int)e.NewValue;
                ctl.cacheCondition = CacheEvery(step);
            },
            (d, baseValue) =>
            {
                var step = (int)baseValue;
                return step < 0 ? 0 : step;
            }));
        /// <summary> 帧的缓存步长。默认为 0，0=不缓存，1=全缓存，N=缓存 frameIndex % N == 0 的帧 </summary>
        public int CacheStep
        {
            get => (int)GetValue(CacheStepProperty);
            set => SetValue(CacheStepProperty, value);
        }
        #endregion

        #region 只读依赖属性
        private static readonly DependencyPropertyKey IsPlayingPropertyKey = DependencyProperty.RegisterReadOnly(nameof(IsPlaying), typeof(bool), typeof(ImageSeq), new PropertyMetadata(false));
        /// <summary> <see cref="IsPlaying"/> 的只读依赖属性标识 </summary>
        public static readonly DependencyProperty IsPlayingProperty = IsPlayingPropertyKey.DependencyProperty;
        /// <summary> 序列是否正在播放  </summary>
        public bool IsPlaying
        {
            get => (bool)GetValue(IsPlayingProperty);
            private set => SetValue(IsPlayingPropertyKey, value);
        }

        public static readonly DependencyPropertyKey FrameIndexPropertyKey = DependencyProperty.RegisterReadOnly(nameof(FrameIndex), typeof(int), typeof(ImageSeq), new PropertyMetadata(-1));
        /// <summary> <see cref="FrameIndex"/> 的只读依赖属性标识 </summary>
        public static readonly DependencyProperty FrameIndexProperty = FrameIndexPropertyKey.DependencyProperty;
        /// <summary> 序列当前播放的帧索引 </summary>
        public int FrameIndex
        {
            get => (int)GetValue(FrameIndexProperty);
            private set => SetValue(FrameIndexPropertyKey, value);
        }

        private static readonly DependencyPropertyKey FrameCountPropertyKey = DependencyProperty.RegisterReadOnly(nameof(FrameCount), typeof(int), typeof(ImageSeq), new PropertyMetadata(0));
        /// <summary> <see cref="FrameCount"/> 的只读依赖属性标识 </summary>
        public static readonly DependencyProperty FrameCountProperty = FrameCountPropertyKey.DependencyProperty;
        /// <summary> 序列总帧数  </summary>
        public int FrameCount
        {
            get => (int)GetValue(FrameCountProperty);
            private set => SetValue(FrameCountPropertyKey, value);
        }

        private static readonly DependencyPropertyKey AverageDecodeTimePropertyKey = DependencyProperty.RegisterReadOnly(nameof(AverageDecodeTime), typeof(TimeSpan),typeof(ImageSeq),new PropertyMetadata(TimeSpan.Zero));        
        /// <summary> <see cref="AverageDecodeTime"/> 的只读依赖属性标识 </summary>
        public static readonly DependencyProperty AverageDecodeTimeProperty = AverageDecodeTimePropertyKey.DependencyProperty;        
        /// <summary>
        /// 最近 <see cref="Fps"/> 帧的平均解码耗时（只读，诊断用）。
        /// <para>不参与缓存决策；受 <see cref="CacheCondition"/> 影响——缓存命中帧不计入统计。</para>
        /// </summary>
        public TimeSpan AverageDecodeTime
        {
            get => (TimeSpan)GetValue(AverageDecodeTimeProperty);
            private set => SetValue(AverageDecodeTimePropertyKey, value);
        }
        #endregion

        private volatile int frameCount = 0;
        private volatile int abEndIndex = -1;
        private volatile int abStartIndex = 0;

        private volatile int decoderIndex = -1;
        private volatile int seekFrameIndex = -1;

        private volatile int fps = 25;
        private volatile int renderInterval = 40;
        private volatile int avgDecodeTimeMs = 0;
        private double nextRenderMilliseconds = 0.0;

        private Int32Rect dirtyRect;
        private WriteableBitmap writeableBitmap;

        private Task decoderTask;
        private ZipArchive zipArchive;
        private CancellationTokenSource cancellationTokenSource;
        private readonly Stopwatch renderStopwatch = new Stopwatch();

        /// <summary> 文件路径字典 </summary>
        private readonly ConcurrentDictionary<int, string> BitmapFileNames = new ConcurrentDictionary<int, string>();
        /// <summary> 缓存的帧字典 </summary>
        private readonly ConcurrentDictionary<int, BitmapFrame> CacheFrames = new ConcurrentDictionary<int, BitmapFrame>();
        /// <summary> 待渲染的帧队列 </summary>
        private readonly ConcurrentQueue<(int Index, BitmapFrame Frame)> RenderFrames = new ConcurrentQueue<(int, BitmapFrame)>();

        /// <summary> 序列图像文件名称 </summary>
        public IEnumerable<string> FileNames => BitmapFileNames.Values;

        /// <summary>
        /// 缓存帧条件代理：输入帧索引（0-based），返回 true 表示缓存该帧的解码结果。
        /// <para>代理在后台解码线程被调用（每帧一次，O(1) 期望），实现必须线程安全，
        /// 禁止直接访问 UI 元素或依赖属性（需要 UI 数据时应在赋值前捕获快照）。</para>
        /// </summary>
        public delegate bool CacheFramePredicate(int frameIndex);
        /// <summary>
        /// 缓存条件代理，控制哪些帧的解码结果进入内存缓存，降低长序列的内存占用。默认 null = 不缓存。
        /// <para>变更只影响后续解码的帧，不清空既有缓存；需要清空缓存时调用 <see cref="ClearCache"/>。</para>
        /// <para>内存量级：每缓存一帧 ≈ 宽 × 高 × 4 字节（Bgra32）。</para>
        /// </summary>
        public CacheFramePredicate CacheCondition
        {
            get => cacheCondition;
            set => cacheCondition = value;
        }
        private volatile CacheFramePredicate cacheCondition;

        /// <summary> 全缓存条件。长序列慎用，否则内存占用过大。 </summary>
        public static readonly CacheFramePredicate CacheAll = _ => true;
        /// <summary>
        /// 生成 "每 step 帧缓存一帧" 的条件代理
        /// </summary>
        /// <param name="step">缓存步长</param>
        /// <param name="offset">起始偏移。CacheEvery(2, 1) → 缓存 1, 3, 5…；CacheEvery(10) → 缓存 0, 10, 20…</param>
        public static CacheFramePredicate CacheEvery(int step, int offset = 0)
        {
            if (step <= 0) return null;
            if (step == 1) return CacheAll;

            // 双重取模防负数（index < offset 时 C# 的 % 返回负值）
            return index => ((index - offset) % step + step) % step == 0;
        }

        /// <summary>
        /// 格式转换字典
        /// </summary>
        private static readonly ConcurrentDictionary<PixelFormat, (PixelFormat Format, BitmapPalette Palette)> FormatConvertedDict = new ConcurrentDictionary<PixelFormat, (PixelFormat, BitmapPalette)>()
        {
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
            decoderTask = Task.Run(() => LoopDecodeFrameAsync(cancellationTokenSource.Token), cancellationTokenSource.Token);

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

            if (FrameIndex % 2 == 0)
            {
                AverageDecodeTime = TimeSpan.FromMilliseconds(avgDecodeTimeMs);
            }
        }
        /// <summary> 从帧队列中渲染下一帧  </summary>
        private void RenderNextFrame()
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
        /// <summary> 等待下一帧渲染 </summary>
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
        private void RenderBitmapFrame(BitmapFrame bitmapFrame)
        {
            if (bitmapFrame == null)
                throw new ArgumentNullException();

            if (bitmapFrame.Format != writeableBitmap.Format)
            {
                Trace.TraceWarning($"图像帧格式不匹配: {bitmapFrame.Format} vs {writeableBitmap.Format}, frame skipped");
                return;
            }

            //var w = bitmapFrame.PixelWidth;
            //var h = bitmapFrame.PixelHeight;

            // 帧与位图取交集：CopyPixels 不做缩放/ 转换，越界即异常或写穿缓冲
            var w = Math.Min(bitmapFrame.PixelWidth, dirtyRect.Width);
            var h = Math.Min(bitmapFrame.PixelHeight, dirtyRect.Height);

            var stride = writeableBitmap.BackBufferStride;
            //var bufferSize = stride * h;
            var bufferSize = stride * dirtyRect.Height;

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
        private async Task LoopDecodeFrameAsync(CancellationToken cancellationToken)
        {
            var stopwatch = Stopwatch.StartNew();
            var renderFrameCount = DefaultRenderFrameCount;

            var averageElapsed = 0.0d;
            var elapsedQueue = new Queue<long>();

            while (!cancellationToken.IsCancellationRequested)
            {
                if (BitmapFileNames.IsEmpty)
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
                if (!BitmapFileNames.ContainsKey(position))
                {
                    try { await Task.Delay(2, cancellationToken); }
                    catch { }
                    continue;
                }

                try
                {                    
                    BitmapFrame bitmapFrame;
                    if (CacheFrames.TryGetValue(position, out bitmapFrame))
                    {
                        RenderFrames.Enqueue((position, bitmapFrame));
                    }
                    else
                    {
                        stopwatch.Restart();
                        var filePath = BitmapFileNames[position];
                        bitmapFrame = DecodeBitmapFrame(filePath);
                        if (bitmapFrame == null) continue;

                        var elapsed = stopwatch.ElapsedMilliseconds;
                        RenderFrames.Enqueue((position, bitmapFrame));

                        // 缓存条件
                        var condition = cacheCondition;
                        if (condition != null && condition.Invoke(position))
                        {
                            CacheFrames.AddOrUpdate(position, bitmapFrame, (k, v) => bitmapFrame);
                        }

                        // 计算平均解码时间
                        var queueMaxCount = fps;
                        elapsedQueue.Enqueue(elapsed);
                        while (elapsedQueue.Count > queueMaxCount)
                        {
                            _ = elapsedQueue.Dequeue();
                        }
                        averageElapsed = elapsedQueue.Average();
                        avgDecodeTimeMs = (int)Math.Round(averageElapsed);

                        //Trace.WriteLine($"DecodeBitmap: {position}  AverageDecodeTime:{averageElapsed}ms  QueueCount:{RenderFrames.Count}");
                    }
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
        private void OnFilePathChanged()
        {
            //Trace.WriteLine($"FilePath Changed ... {FilePath}");
            if (string.IsNullOrWhiteSpace(FilePath)) return;

            var fileNames = Array.Empty<string>();
            var fileFullPath = Path.IsPathRooted(FilePath) ? FilePath : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, FilePath);

            if (File.Exists(fileFullPath))
            {
                var ext = Path.GetExtension(fileFullPath).ToLower();
                if (ext == ".zip")
                {
                    zipArchive?.Dispose();
                    zipArchive = null;

                    zipArchive = ZipFile.OpenRead(fileFullPath);
                    //Trace.TraceInformation($"ZipFile.OpenRead Mode:{zipArchive.Mode} Count:{zipArchive.Entries.Count}");
                    fileNames = GetImageFileNames(zipArchive, SupportImageExtensions);
                }
            }
            else if (Directory.Exists(fileFullPath))
            {
                zipArchive?.Dispose();
                zipArchive = null;

                fileNames = GetImageFileNames(fileFullPath, SupportImageExtensions);
            }
            else
            {
                throw new FileNotFoundException($"File Not Found: {fileFullPath}");
            }

            if (fileNames.Length == 0) return;
            var firstFrame = DecodeBitmapFrame(fileNames.FirstOrDefault());
#if true
            if (DesignerProperties.GetIsInDesignMode(this))
            {
                base.Source = firstFrame;
                return;
            }
#endif

            CacheFrames.Clear();
            RenderFrames.Clear();
            BitmapFileNames.Clear();

            FrameIndex = -1;
            IsPlaying = AutoPlay;
            FrameCount = fileNames.Length;
            AverageDecodeTime = TimeSpan.Zero;

            frameCount = fileNames.Length;
            decoderIndex = 0;
            seekFrameIndex = -1;
            avgDecodeTimeMs = 0;
            writeableBitmap = null;

            var w = firstFrame.PixelWidth;
            var h = firstFrame.PixelHeight;
            dirtyRect = new Int32Rect(0, 0, w, h);

            var pixelFormat = firstFrame.Format;
            var bitmapPalette = firstFrame.Palette;
            Trace.TraceInformation($"{FilePath} FrameCount:{frameCount} PixelFormat: {pixelFormat} Size:{w}x{h}");

            writeableBitmap = new WriteableBitmap(w, h, firstFrame.DpiX, firstFrame.DpiY, pixelFormat, bitmapPalette);
            this.Source = writeableBitmap;
            
            for (var i = 0; i < fileNames.Length; i++)
            {
                BitmapFileNames.TryAdd(i, fileNames[i]);
            }
            if (!IsPlaying) AwaitRenderNextFrame();
        }

        /// <summary>
        /// 解码一个 BitmapFrame 对象
        /// </summary>
        /// <param name="fileName"></param>
        /// <returns></returns>
        private BitmapFrame DecodeBitmapFrame(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName)) return null;

            Stream bitmapStream = null;
            BitmapFrame bitmapFrame = null;

            if (zipArchive == null)
            {
                bitmapStream = File.OpenRead(fileName);
            }
            else
            {
                var zipArchiveEntry = zipArchive.GetEntry(fileName);
                if (zipArchiveEntry == null) return null;
                //Trace.WriteLine($"ZipFileEntry: {fileName}  {zipArchiveEntry.CompressedLength}/{zipArchiveEntry.Length}");
                using (var zipStream = zipArchiveEntry.Open())
                {
                    bitmapStream = new MemoryStream();
                    zipStream.CopyTo(bitmapStream);
                    bitmapStream.Position = 0;
                }
            }

            using (bitmapStream)
            {
                var decoder = BitmapDecoder.Create(bitmapStream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                bitmapFrame = decoder.Frames[0];

                if (FormatConvertedDict.TryGetValue(bitmapFrame.Format, out var target))
                {
                    BitmapSource bitmapSource = new FormatConvertedBitmap(bitmapFrame, target.Format, target.Palette, 0);
                    bitmapSource = new CachedBitmap(bitmapSource, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                    bitmapFrame = BitmapFrame.Create(bitmapSource);
                }

                bitmapFrame.Freeze();
            }

            return bitmapFrame;
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

            BitmapFileNames.Clear();
            CacheFrames.Clear();
            RenderFrames.Clear();
            renderStopwatch.Stop();

            zipArchive?.Dispose();
            zipArchive = null;

            FrameIndex = -1;
            FrameCount = 0;
            IsPlaying = false;
            AverageDecodeTime = TimeSpan.Zero;

            frameCount = 0;
            decoderIndex = 0;
            seekFrameIndex = -1;
            avgDecodeTimeMs = 0;
            writeableBitmap = null;

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

        /// <summary> 清空已缓存的解码帧 </summary>
        public void ClearCache() => CacheFrames.Clear();
        /// <summary> 播放序列  </summary>
        public void Play() { IsPlaying = true; }
        /// <summary> 暂停序列  </summary>
        public void Pause() { IsPlaying = false; }
        /// <summary> Seek 到指定的帧索引  </summary>
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
        /// 获取文件夹内图像文件列表，按文件名中的第一组数字排序，返回 (顺序帧下标, 文件路径) 序列
        /// </summary>
        /// <param name="folder">图像文件夹路径</param>
        /// <param name="extensions">扩展名过滤列表，如 { ".png", ".jpg", ".jpeg", ".bmp" }，不区分大小写</param>
        /// <returns>Key 为从 0 开始的连续帧序号，Value 为完整文件路径；参数无效时抛出异常</returns>
        /// <exception cref="ArgumentException">folder 为空或目录不存在</exception>
        /// <exception cref="ArgumentNullException">extensions 为 null</exception>
        public static string[] GetImageFileNames(string folder, IEnumerable<string> extensions)
        {
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder) || extensions == null) 
                return Array.Empty<string>();

            var regex = new Regex(@"\d+");
            var files = from fpath in Directory.GetFiles(folder, "*.*", SearchOption.TopDirectoryOnly)
                        let fextn = Path.GetExtension(fpath)?.ToLower()
                        where extensions.Contains(fextn, StringComparer.OrdinalIgnoreCase)
                        let match = regex.Match(Path.GetFileNameWithoutExtension(fpath))
                        where match.Success && int.TryParse(match.Value, out int number)
                        orderby int.Parse(match.Value)
                        select fpath;

            return files.ToArray();
        }
        /// <summary>
        /// 获取压缩包内图像文件列表，按文件名中的第一组数字排序，返回 (顺序帧下标, 条目全名) 序列
        /// </summary>
        /// <param name="zipArchive">已打开的 ZipArchive，生命周期由调用方管理</param>
        /// <param name="extensions">扩展名过滤列表，如 { ".png", ".jpg", ".jpeg", ".bmp" }，不区分大小写</param>
        /// <returns>Key 为从 0 开始的连续帧序号，Value 为 ZipArchiveEntry.FullName；参数无效时抛出异常</returns>
        /// <exception cref="ArgumentNullException">zipArchive 或 extensions 为 null</exception>
        public static string[] GetImageFileNames(ZipArchive zipArchive, IEnumerable<string> extensions)
        {
            if (zipArchive == null || extensions == null) return Array.Empty<string>();

            var regex = new Regex(@"\d+");
            var files = from entry in zipArchive.Entries
                        let fextn = Path.GetExtension(entry.FullName)?.ToLower()
                        where extensions.Contains(fextn, StringComparer.OrdinalIgnoreCase)
                        let match = regex.Match(Path.GetFileNameWithoutExtension(entry.FullName))
                        where match.Success && int.TryParse(match.Value, out int number)
                        orderby int.Parse(match.Value)
                        select entry.FullName;

            return files.ToArray();
        }

    }
}
