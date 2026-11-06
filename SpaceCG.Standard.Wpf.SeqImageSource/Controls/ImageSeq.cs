using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SharpDX.Direct3D9;
using SpaceCG.Extensions;

namespace SpaceCG.Controls
{
    /// <summary>
    /// Image Sequence 
    /// </summary>
    public partial class ImageSeq : Image
    {
        /// <summary>
        /// FrameChanged Event
        /// </summary>
        public event EventHandler<EventArgs> FrameChanged;

        #region Dependency Properties
        /// <summary> Folder Property  </summary>
        public static readonly DependencyProperty FolderProperty = 
            DependencyProperty.Register(nameof(Folder), typeof(string), typeof(ImageSeq), new PropertyMetadata(null, OnFolderChanged));
        /// <summary>
        /// 序列帧所在文件夹
        /// <para>序列帧图片支持 *.png|*.jpg|*.bmp|*.jpeg 格式；图片文件名中需要有数字序号，用于计算排序。</para>
        /// </summary>
        public string Folder
        {
            get => (string)GetValue(FolderProperty);
            set => SetValue(FolderProperty, value);
        }
        private static void OnFolderChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var ctl = d as ImageSeq;
            if (ctl != null) ctl.DesignerPreview();
        }

        /// <summary>  AutoOpen Property </summary>
        public static readonly DependencyProperty AutoOpenProperty =
            DependencyProperty.Register(nameof(AutoOpen), typeof(bool), typeof(ImageSeq), new PropertyMetadata(true));
        /// <summary> 自动打开序列帧目录  </summary>
        public bool AutoOpen
        {
            get => (bool)GetValue(AutoOpenProperty);
            set => SetValue(AutoOpenProperty, value);
        }

        /// <summary> AutoPlay Property </summary>
        public static readonly DependencyProperty AutoPlayProperty =
            DependencyProperty.Register(nameof(AutoPlay), typeof(bool), typeof(ImageSeq), new PropertyMetadata(true));
        /// <summary>  打开序列帧目录后是否自动播放  </summary>
        public bool AutoPlay
        {
            get => (bool)GetValue(AutoPlayProperty);
            set => SetValue(AutoPlayProperty, value);
        }

#if false
        /// <summary>  Loop Property  </summary>
        public static readonly DependencyProperty LoopProperty =
            DependencyProperty.Register(nameof(Loop), typeof(bool), typeof(ImageSeq), new PropertyMetadata(true));
        /// <summary> 是否循环播放  </summary>
        public bool Loop
        {
            get => (bool)GetValue(LoopProperty);
            set => SetValue(LoopProperty, value);
        }
#endif
        /// <summary> ABLoopStart Property  </summary>
        public static readonly DependencyProperty ABLoopStartProperty =
            DependencyProperty.Register(nameof(ABLoopStart), typeof(int), typeof(ImageSeq), new PropertyMetadata(int.MinValue, (d, e)=>
            {
                var ctl = d as ImageSeq;
                if (ctl != null) ctl.abStartIndex = (int)e.NewValue;
            }));
        /// <summary> AB循环起始帧索引  </summary>
        public int ABLoopStart
        {
            get => (int)GetValue(ABLoopStartProperty);
            set => SetValue(ABLoopStartProperty, value);
        }
        /// <summary> ABLoopEnd Property  </summary>
        public static readonly DependencyProperty ABLoopEndProperty =
            DependencyProperty.Register(nameof(ABLoopEnd), typeof(int), typeof(ImageSeq), new PropertyMetadata(int.MaxValue, (d, e) =>
            {
                var ctl = d as ImageSeq;
                if (ctl != null) ctl.abEndIndex = (int)e.NewValue;
            }));
        /// <summary> AB循环结束帧索引，-1表示最后一帧  </summary>
        public int ABLoopEnd
        {
            get => (int)GetValue(ABLoopEndProperty);
            set => SetValue(ABLoopEndProperty, value);
        }

        /// <summary> IsOpened Property  </summary>
        public static readonly DependencyProperty IsOpenedProperty =
            DependencyProperty.Register(nameof(IsOpened), typeof(bool), typeof(ImageSeq), new PropertyMetadata(false));
        public bool IsOpened
        {
            get => (bool)GetValue(IsOpenedProperty);
            private set => SetValue(IsOpenedProperty, value);
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
        /// <summary> CurrentFrameIndex Property </summary>
        public static readonly DependencyProperty CurrentFrameIndexProperty = 
            DependencyProperty.Register(nameof(CurrentFrameIndex), typeof(int), typeof(ImageSeq), new PropertyMetadata(-1));
        /// <summary> 序列当前播放的帧索引 </summary>
        public int CurrentFrameIndex
        {
            get => (int)GetValue(CurrentFrameIndexProperty);
            private set => SetValue(CurrentFrameIndexProperty, value);
        }
        /// <summary> TotalFrameCount Property  </summary>
        public static readonly DependencyProperty TotalFrameCountProperty =
            DependencyProperty.Register(nameof(TotalFrameCount), typeof(int), typeof(ImageSeq), new PropertyMetadata(0));
        /// <summary> 序列总帧数  </summary>
        public int TotalFrameCount
        {
            get => (int)GetValue(TotalFrameCountProperty);
            private set => SetValue(TotalFrameCountProperty, value);
        }

        /// <summary>  CacheFrameCount Property  </summary>
        public static readonly DependencyProperty CacheFrameCountProperty =
            DependencyProperty.Register(nameof(CacheFrameCount), typeof(int), typeof(ImageSeq), new PropertyMetadata(8, (d, e) =>
            {
                var ctl = d as ImageSeq;
                if (ctl != null) ctl.cacheFrameCount = (int)e.NewValue;
            }));
        /// <summary> 序列缓存帧的数量，默认为 8 帧  </summary>
        public int CacheFrameCount
        {
            get => (int)GetValue(CacheFrameCountProperty);
            set => SetValue(CacheFrameCountProperty, value);
        }
#if true
        /// <inheritdoc cref="Image.Source"/> 
        public new ImageSource Source
        {
            get => base.Source;
            set => base.Source = value;
        }
#endif
        #endregion

        private int abEndIndex = int.MaxValue;
        private int abStartIndex = int.MinValue;
        private int cacheFrameCount = 8;
        private volatile int seekFlags = -1;

        private Int32Rect dirtyRect;
        private readonly D3DImage d3dImage;
        private CancellationTokenSource cancellationTokenSource;
        private readonly ConcurrentQueue<System.Drawing.Bitmap> CacheFrames = new ConcurrentQueue<System.Drawing.Bitmap>();

        private Texture dynamicTexture;
        private Surface dynamicSurface;
        private Texture renderTargetTexture;
        private Surface renderTargetSurface;

        /// <summary>
        /// 构造函数
        /// </summary>
        public ImageSeq()
        {            
            d3dImage = new D3DImage();
            d3dImage.IsFrontBufferAvailableChanged += D3DImage_IsFrontBufferAvailableChanged;

            base.Source = d3dImage;
            dirtyRect = new Int32Rect(0, 0, 0, 0);

#if false
            Loaded += (s, e) =>
            {
                var window = Window.GetWindow(this);
                var handle = new WindowInteropHelper(window).Handle;
                Trace.WriteLine($"window:{window},,{handle}");

                D3D9Shared.Initialize(handle);
            };
#endif
            Unloaded += (s, e) =>
            {
                //Close();
                //d3dImage = null;
            };
        }
        private void D3DImage_IsFrontBufferAvailableChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            Trace.TraceInformation($"D3DImage IsFrontBufferAvailable: {d3dImage.IsFrontBufferAvailable}");

            if (renderTargetSurface == null || renderTargetSurface.NativePointer == IntPtr.Zero) return;
            if (!d3dImage.IsFrontBufferAvailable) return;

            try
            {
                d3dImage.Lock();
                d3dImage.SetBackBuffer(D3DResourceType.IDirect3DSurface9, renderTargetSurface.NativePointer);
                d3dImage.Unlock();
            }
            catch (Exception ex)
            {
                Trace.TraceError($"D3DImage_IsFrontBufferAvailableChanged Exception: {ex.Message}");
            }
        }

        /// <summary>
        /// 设计器中预览
        /// </summary>
        protected void DesignerPreview()
        {
            if (!DesignerProperties.GetIsInDesignMode(this)) return;

            try
            {
                var folder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, Folder);
                var first = GetImageFiles(folder).FirstOrDefault();
                base.Source = string.IsNullOrWhiteSpace(first) ? null : new BitmapImage(new Uri(first));
            }
            catch { }
        }
        /// <inheritdoc />
        protected override void OnInitialized(EventArgs e)
        {
            base.OnInitialized(e);
            if (AutoOpen) Open();
        }
        /// <summary> 关闭并停止序列  </summary>
        public void Close()
        {
            seekFlags = -1;
            TotalFrameCount = 0;
            CurrentFrameIndex = 0;

            IsOpened = false;
            IsPlaying = false;

            try
            {
                cancellationTokenSource?.Cancel();
            }
            catch { }

            while (CacheFrames.Count > 0)
            {
                if (CacheFrames.TryDequeue(out var bitmap))
                    bitmap.Dispose();
            }

            d3dImage.Lock();
            d3dImage.SetBackBuffer(D3DResourceType.IDirect3DSurface9, IntPtr.Zero);
            d3dImage.Unlock();

            dynamicSurface?.Dispose();
            dynamicTexture?.Dispose();
            dynamicSurface = null;
            dynamicTexture = null;

            renderTargetSurface?.Dispose();
            renderTargetTexture?.Dispose();
            renderTargetSurface = null;
            renderTargetTexture = null;

            cancellationTokenSource?.Dispose();
            cancellationTokenSource = null;
        }
        /// <summary> 打开序列帧目录  </summary>
        public void Open()
        {
            if (DesignerProperties.GetIsInDesignMode(this)) return;
            if (string.IsNullOrWhiteSpace(Folder) || !Directory.Exists(Folder)) return;

            IsOpened = false;
            IsPlaying = false;

            seekFlags = -1;
            TotalFrameCount = 0;
            CurrentFrameIndex = -1;

            if (cancellationTokenSource != null)
            {
                try
                {
                    cancellationTokenSource?.Cancel();
                    cancellationTokenSource?.Dispose();
                }
                finally
                {
                    cancellationTokenSource = null;
                }
            }
            while (CacheFrames.Count > 0)
            {
                if (CacheFrames.TryDequeue(out var bitmap))
                    bitmap.Dispose();
            }

            var files = GetImageFiles(Folder);
            if (files.Length <= 0) return;
            TotalFrameCount = files.Length;

            // 取第一帧，获取图像尺寸信息
            var firstFrame = new System.Drawing.Bitmap(files[0]);
            int width = firstFrame.Width;
            int height = firstFrame.Height;

            // 需要重新创建纹理对象
            if (width != dirtyRect.Width || height != dirtyRect.Height)
            {
                try
                {
                    d3dImage.Lock();
                    d3dImage.SetBackBuffer(D3DResourceType.IDirect3DSurface9, IntPtr.Zero);
                    d3dImage.Unlock();

                    dynamicSurface?.Dispose();
                    dynamicTexture?.Dispose();

                    renderTargetSurface?.Dispose();
                    renderTargetTexture?.Dispose();
                }
                finally
                {
                    dynamicSurface = null;
                    dynamicTexture = null;

                    renderTargetSurface = null;
                    renderTargetTexture = null;
                }

                dirtyRect.Width = width;
                dirtyRect.Height = height;
                
                dynamicTexture = new Texture(D3D9Shared.Device, width, height, 1, Usage.Dynamic, Format.A8R8G8B8, Pool.Default);
                renderTargetTexture = new Texture(D3D9Shared.Device, width, height, 1, Usage.RenderTarget, Format.A8R8G8B8, Pool.Default);

                /*
                 * D3DImage 绑定的 Surface 只能在 UI 线程安全操作
                 */
                dynamicSurface = dynamicTexture.GetSurfaceLevel(0);
                //dynamicSurface = Surface.CreateOffscreenPlainEx(D3D9Shared.Device, width, height, Format.A8R8G8B8, Pool.Default, Usage.None);
                renderTargetSurface = renderTargetTexture.GetSurfaceLevel(0);
            }

            d3dImage.Lock();
            d3dImage.SetBackBuffer(D3DResourceType.IDirect3DSurface9, renderTargetSurface.NativePointer);
            d3dImage.Unlock();

            // 渲染并显示第一帧图像
            using (firstFrame)
            {
                firstFrame.Tag = 0;
                Render(firstFrame);
                UpdateDisplay();
            }

            cancellationTokenSource = new CancellationTokenSource();
            Task.Run(() => LoopReadTask(files, cancellationTokenSource.Token), cancellationTokenSource.Token);

            IsOpened = true;
            if (AutoPlay) Play();
            else Pause();
        }
        /// <summary>
        /// 打开序列帧目录
        /// </summary>
        /// <param name="folder"></param>
        /// <param name="autoPlay"></param>
        /// <param name="abLoopStart"></param>
        /// <param name="abLoopEnd"></param>
        public void Open(string folder, bool autoPlay, int abLoopStart = 0, int abLoopEnd = -1)
        {
            Folder = folder;
            AutoPlay = autoPlay;
            ABLoopEnd = abLoopEnd;
            ABLoopStart = abLoopStart;

            Open();
        }
        private async void LoopReadTask(string[] files, CancellationToken cancellationToken)
        {
            if (files == null || !files.Any()) return;

            var readIndex = 0;
            var count = files.Length;
            var endIndex = count - 1;
            //var cacheFrameCount = Math.Min(16, count);

            while (true)
            {
                if (cancellationToken == null || cancellationToken.IsCancellationRequested) break;

                try
                {
                    #region Seek Handler
                    if (seekFlags >= 0 && seekFlags <= endIndex)
                    {
                        // 清除缓存帧
                        while (CacheFrames.Count > 0)
                        {
                            if (CacheFrames.TryDequeue(out var bitmap0))
                                bitmap0.Dispose();
                        }

                        readIndex = seekFlags;
                        var renderIndex = seekFlags;
                        
                        // seek 后渲染显示一帧图像
                        d3dImage.Dispatcher.Invoke(() =>
                        {
                            using (var seekBitmap = new System.Drawing.Bitmap(files[renderIndex]))
                            {
                                seekBitmap.Tag = renderIndex;
                                Render(seekBitmap);
                                UpdateDisplay();
                            }
                        });

                        readIndex ++;    // 上面已经渲染了一帧
                        seekFlags = -1;
                        continue;
                    }
                    #endregion

                    if (cacheFrameCount <= 0) break;
                    if (cacheFrameCount > count) cacheFrameCount = count;

                    if (CacheFrames.Count >= cacheFrameCount)
                    {
                        await Task.Delay(10, cancellationToken);
                        continue;
                    }

                    var bitmap = new System.Drawing.Bitmap(files[readIndex]);
                    bitmap.Tag = readIndex;
                    CacheFrames.Enqueue(bitmap);

                    // 如果当前读取位置超出结束位置或 AB 循环的结束位置，则从头开始读取
                    if (readIndex >= endIndex || readIndex >= abEndIndex)
                    {
                        readIndex = abStartIndex >= 0 && abStartIndex <= endIndex ? abStartIndex : 0;
                    }
                    else
                    {
                        readIndex++;
                    }

                    await Task.Delay(2, cancellationToken);
                }
                catch (TaskCanceledException tex)
                {
                    Trace.WriteLine($"LoopReadTask TaskCanceledException ... {tex.Message}");
                    break;
                }
                catch (ObjectDisposedException oex)
                {
                    Trace.WriteLine($"LoopReadTask ObjectDisposedException ... {oex.Message}");
                    break;
                }
                catch (Exception ex)
                {
                    Trace.TraceError($"LoopReadTask Exception: {ex.Message}");
                    break;
                }
            }

            while (CacheFrames.Count > 0)
            {
                if (CacheFrames.TryDequeue(out var bitmap)) bitmap.Dispose();
            }
        }

        /// <summary> 播放序列  </summary>
        public void Play() { IsPlaying = true; }
        /// <summary> 暂停序列  </summary>
        public void Pause() { IsPlaying = false; }
        /// <summary>
        /// Seek 到指定帧
        /// </summary>
        /// <param name="frameIndex"></param>
        public void Seek(int frameIndex)
        {
            seekFlags = frameIndex >= 0 && frameIndex <= TotalFrameCount - 1 ? frameIndex : -1;
        }

        /// <summary>
        /// 渲染指定颜色
        /// </summary>
        /// <param name="color"></param>
        public void Render(Color color)
        {
            if (renderTargetSurface == null || renderTargetSurface.NativePointer == IntPtr.Zero) return;

            //var device = D3D9Shared.Device;
            //device.SetRenderTarget(0, renderTargetSurface);
            D3D9Shared.Device.ColorFill(renderTargetSurface, new SharpDX.Mathematics.Interop.RawColorBGRA(color.B, color.G, color.R, color.A));
        }
#if false
        /// <summary>
        /// 渲染 Texture 对象
        /// </summary>
        /// <param name="texture"></param>
        public void Render(Texture texture) => Render(texture?.GetSurfaceLevel(0));
#endif
        /// <summary>
        /// 渲染 Surface 对象
        /// </summary>
        /// <param name="surface"></param>
        /// <param name="pixelShared"></param>
        public void Render(Surface surface, SharpDX.Direct3D9.PixelShader pixelShared = null)
        {
            if (surface == null || surface.NativePointer == IntPtr.Zero) return;
            if (renderTargetSurface == null || renderTargetSurface.NativePointer == IntPtr.Zero) return;
            
            try
            {
                //D3D9Shared.Device.StretchRectangle(surface, renderTargetSurface, TextureFilter.Linear);
                D3D9Shared.SpriteDrawTexture(dynamicTexture, renderTargetSurface, pixelShared);
            }
            catch (Exception ex)
            {
                Trace.TraceWarning($"Render Surface Exception: {ex.Message}");
                return;
            }

            if (surface.Tag is int tag)
            {
                if (CurrentFrameIndex != tag)
                {
                    CurrentFrameIndex = tag;
                    FrameChanged?.Invoke(this, EventArgs.Empty);
                }
            }
            else
            {
                if (int.TryParse(surface.Tag?.ToString(), out var frameIndex) && CurrentFrameIndex != frameIndex)
                {
                    CurrentFrameIndex = frameIndex;
                    FrameChanged?.Invoke(this, EventArgs.Empty);
                }
            }
        }
        /// <summary>
        /// 渲染 Bitmap 对象
        /// </summary>
        /// <param name="bitmap"></param>
        /// <param name="pixelShared"></param>
        public void Render(System.Drawing.Bitmap bitmap, SharpDX.Direct3D9.PixelShader pixelShared = null)
        {
            if (bitmap == null) return;
            if (dynamicSurface == null || dynamicSurface.NativePointer == IntPtr.Zero) return;

            if (bitmap.CopyTo(dynamicSurface))
            {
                dynamicSurface.Tag = bitmap.Tag;
                Render(dynamicSurface, pixelShared);
            }
        }
        /// <summary>
        /// 渲染下一帧序列，并更新显示
        /// </summary>
        /// <param name="pixelShared"></param>
        public void RenderNext(SharpDX.Direct3D9.PixelShader pixelShared = null)
        {
            if (!IsPlaying) return;
            if (cancellationTokenSource == null || cancellationTokenSource.IsCancellationRequested) return;
            
            if (CacheFrames.TryDequeue(out var bitmap))
            {
                using (bitmap)
                {
                    Render(bitmap, pixelShared);
                }
                UpdateDisplay();
            }
        }
        /// <summary>
        /// 刷新显示 D3DImage 
        /// </summary>
        public void UpdateDisplay()
        {
            if (d3dImage == null) return;
            if (!d3dImage.IsFrontBufferAvailable) return;

            d3dImage.Lock();
            try
            {
                d3dImage.AddDirtyRect(dirtyRect);
            }
            finally
            {
                d3dImage.Unlock();
            }

            base.Source = d3dImage;
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
