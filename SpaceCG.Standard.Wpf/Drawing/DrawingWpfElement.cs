using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SpaceCG.Device;
using Rectangle = System.Drawing.Rectangle;

namespace SpaceCG.Drawing
{
    /// <summary>
    /// 实时绘制 WPF 显示元素，通过 <see cref="DispatcherTimer"/> 在 UI 线程定时截取
    /// <see cref="FrameworkElement"/> 的渲染内容，并通过 <see cref="NewDrawingFrame"/> 事件向外推送像素数据。
    /// </summary>
    /// <remarks>
    /// <para><b>线程模型：</b>必须在 WPF UI 线程（拥有 <see cref="Dispatcher"/> 的线程）上构造和使用。
    /// <see cref="DispatcherTimer"/> 的 Tick 在 UI 线程触发，<see cref="NewDrawingFrame"/> 事件同步在该线程执行。
    /// 订阅者应尽快消费，避免阻塞 UI 线程导致画面卡顿。</para>
    /// <para><b>渲染管线：</b>每 Tick 执行 <c>RenderTargetBitmap.Render(visualElement)</c> →
    /// <c>CopyPixels</c> 到托管缓冲区 → 通过 <see cref="DrawingEventArgs"/> 推送给订阅者。
    /// <c>CopyPixels</c> 会产生一次完整像素拷贝（Pbgra32 格式）。</para>
    /// <para><b>性能特征：</b><see cref="RenderTargetBitmap"/>、像素缓冲区和 <see cref="DrawingEventArgs"/> 实例复用，
    /// 仅在 <see cref="Rectangle"/> 尺寸变化时重建。FPS 基于 1 秒滑动时间窗口统计。</para>
    /// <para><b>生命周期：</b>通过 <see cref="StartDrawing()"/> 启动、<see cref="StopDrawing"/> 停止。
    /// <see cref="IDisposable.Dispose"/> 自动调用 <see cref="StopDrawing"/> 并释放 WPF 资源。</para>
    /// <para><b>像素格式：</b>输出为 <c>Pbgra32</c>（预乘 Alpha 的 BGRA 8:8:8:8），对应 <see cref="ColorFormat.BGRA"/>。</para>
    /// </remarks>
    public class DrawingWpfElement : IDrawingDisplay<FrameworkElement>
    {
        #region Public Properties
        /// <inheritdoc/>
        /// <remarks>默认值 40ms（约 25 FPS），有效范围 [16, 1000]ms。</remarks>
        public int Interval
        {
            get => _interval;
            set
            {
                if (value < 16 || value > 1000)
                    throw new ArgumentOutOfRangeException(nameof(value), "绘图处理时间必须在 16ms~1000ms 之间");
                _interval = value;
            }
        }
        private int _interval = 40;

        /// <inheritdoc/>
        /// <remarks>默认截取区域 (0, 0, 600, 32)，尺寸上限 1024×1024。
        /// 修改此值后需重新调用 <see cref="StartDrawing()"/> 以重建渲染目标。</remarks>
        public Rectangle Rectangle
        {
            get => _rectangle;
            set
            {
                if (value.X < 0 || value.Y < 0 || value.Width < 0 || value.Height < 0 || value.Width > 1024 || value.Height > 1024)
                    throw new ArgumentOutOfRangeException(nameof(value), "绘图尺寸必须在 (0,0,1024,1024) 范围内");
                _rectangle = value;
            }
        }
        private Rectangle _rectangle = new Rectangle(0, 0, 600, 32);

        /// <inheritdoc/>
        public double Fps { get; private set; } = 0.0;
        /// <inheritdoc/>
        public bool IsDrawing => _dispatcherTimer?.IsEnabled ?? false;
        /// <inheritdoc/>
        public event EventHandler<DrawingEventArgs> NewDrawingFrame;

        /// <summary>
        /// 要绘制的 WPF 显示元素。
        /// </summary>
        /// <remarks>
        /// <para>支持任何 <see cref="FrameworkElement"/> 派生类型（如 <see cref="System.Windows.Controls.Control"/>、
        /// <see cref="System.Windows.Controls.Panel"/> 等），渲染其完整的可视化树。</para>
        /// <para>构造函数中设置后不可变更，如需切换绘制目标请创建新实例。</para>
        /// </remarks>
        public FrameworkElement DrawingElement { get; private set; }
        #endregion

        #region Private Fields
        /// <summary>像素数据托管缓冲区，在 <c>CopyPixels</c> 后暂存像素数据。
        /// 使用 <c>fixed</c> 语句钉住后传给 <see cref="DrawingEventArgs.Pixels"/>。
        /// </summary>
        private byte[] _pixelsBuffer;
        /// <summary>帧渲染耗时计时器。</summary>
        private Stopwatch _stopwatch;
        /// <summary>WPF UI 线程定时器，Tick 事件在 UI 线程触发。</summary>
        private DispatcherTimer _dispatcherTimer;

        /// <summary>WPF 渲染目标位图，复用避免每帧分配。</summary>
        private DrawingVisual _background;
        /// <summary>事件参数复用实例，减少 GC 分配。</summary>
        private DrawingEventArgs _drawingEventArgs;
        private RenderTargetBitmap _renderTargetBitmap;

        /// <summary>
        /// 1 秒滑动窗口帧率统计：记录每帧时间戳（毫秒），淘汰超过 1 秒的旧记录，剩余数即为瞬时 FPS。
        /// </summary>
        private const int WindowTimes = 1000;
        private readonly Queue<long> _frameTimes = new Queue<long>(60);
        #endregion

        /// <summary>
        /// 使用指定的 WPF 元素构造实例。
        /// </summary>
        /// <param name="visualElement">要绘制的 WPF 元素。</param>
        /// <exception cref="ArgumentNullException"><paramref name="visualElement"/> 为 null。</exception>
        /// <remarks>
        /// <para><b>重要：</b>必须在 WPF UI 线程上调用此构造函数，因为 <see cref="DispatcherTimer"/> 需要关联当前线程的 <see cref="Dispatcher"/>。</para>
        /// </remarks>
        public DrawingWpfElement(FrameworkElement visualElement)
        {
            if (visualElement == null)
                throw new ArgumentNullException(nameof(visualElement));

            this.DrawingElement = visualElement;

            _stopwatch = new Stopwatch();
            _dispatcherTimer = new DispatcherTimer(DispatcherPriority.Render, Dispatcher.CurrentDispatcher);
            _dispatcherTimer.Tick += OnDispatcherTimerTick;
        }

        /// <summary>
        /// 使用指定的 WPF 元素、区域和间隔构造实例。
        /// </summary>
        /// <param name="visualElement">要绘制的 WPF 元素。</param>
        /// <param name="rectangle">截取区域，须在 (0, 0, 1024, 1024) 范围内。</param>
        /// <param name="interval">每帧间隔，单位毫秒，范围 [16, 1000]。</param>
        /// <remarks>必须在 WPF UI 线程上调用。</remarks>
        public DrawingWpfElement(FrameworkElement visualElement, Rectangle rectangle, int interval) : this(visualElement)
        {
            this.Interval = interval;
            this.Rectangle = rectangle;
        }

        /// <inheritdoc/>
        /// <remarks>
        /// <para>首次调用时创建 <see cref="RenderTargetBitmap"/> 和像素缓冲区。
        /// 重复调用时若已处于绘制状态则静默返回。</para>
        /// <para>若 <see cref="Rectangle"/> 在 Stop/Start 之间被修改，会按新尺寸重建渲染目标。</para>
        /// </remarks>
        public void StartDrawing()
        {
            if (_dispatcherTimer.IsEnabled) return;

            Fps = 0;
            _frameTimes.Clear();

            // 绘制的元素是否是默认背景
            var isBlackBackground = (DrawingElement is Panel panel && panel.Background is SolidColorBrush colorBrush && colorBrush.Color == Colors.Black);
            if (!isBlackBackground)
            {
                _background = new DrawingVisual();
                using (DrawingContext dc = _background.RenderOpen())
                {
                    dc.DrawRectangle(Brushes.Black, null, new Rect(0, 0, Rectangle.Width, Rectangle.Height));
                }
            }
            
            _renderTargetBitmap = new RenderTargetBitmap(Rectangle.Width, Rectangle.Height, 96, 96, PixelFormats.Pbgra32);
            var stride = (_renderTargetBitmap.PixelWidth * _renderTargetBitmap.Format.BitsPerPixel + 7) / 8;

            _drawingEventArgs = new DrawingEventArgs(IntPtr.Zero, Rectangle.Width, Rectangle.Height, stride, ColorFormat.BGRA);

            _stopwatch.Restart();
            _dispatcherTimer.Interval = TimeSpan.FromMilliseconds(Interval);
            _dispatcherTimer.Start();
        }

        /// <inheritdoc/>
        public void StartDrawing(Rectangle rectangle, int interval)
        {
            if (_dispatcherTimer.IsEnabled) return;

            this.Interval = interval;
            this.Rectangle = rectangle;

            StartDrawing();
        }

        /// <inheritdoc/>
        public void StopDrawing()
        {
            _stopwatch.Stop();
            _dispatcherTimer.Stop();

            _background = null;
            _drawingEventArgs = null;
            _renderTargetBitmap = null;

            Fps = 0;
            _frameTimes.Clear();
        }

        /// <summary>
        /// <see cref="DispatcherTimer"/> 的 Tick 事件处理，在 WPF UI 线程上执行。
        /// </summary>
        /// <remarks>
        /// <para><b>执行流程：</b>Render 元素到 <see cref="_renderTargetBitmap"/> →
        /// <c>CopyPixels</c> 到托管缓冲区 → 通过 <see cref="DrawingEventArgs"/> 推送 → 更新 FPS。</para>
        /// <para><b>指针生命周期警告：</b><see cref="DrawingEventArgs.Pixels"/> 指向 <see cref="_pixelsBuffer"/>，
        /// 该指针在 <c>fixed</c> 块内有效。虽然事件同步触发时指针有效，但订阅者不应缓存
        /// <see cref="DrawingEventArgs"/> 实例或 <see cref="DrawingEventArgs.Pixels"/> 指针到事件处理外部。</para>
        /// <para><b>性能注意：</b><c>CopyPixels</c> 产生一次完整像素拷贝（<c>width × height × 4</c> 字节），对于 1024×1024 区域约为 4MB/帧，是主要性能开销。</para>
        /// </remarks>
        private unsafe void OnDispatcherTimerTick(object sender, EventArgs e)
        {
            var visualElement = DrawingElement;
            if (visualElement == null) return;

            var beginTime = _stopwatch.ElapsedMilliseconds;
            _frameTimes.Enqueue(beginTime);

            // 确保元素已测量和排列
            //visualElement.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            //visualElement.Arrange(new Rect(visualElement.DesiredSize));

            if (_background != null)
                _renderTargetBitmap.Render(_background);
            _renderTargetBitmap.Render(visualElement);

            if (NewDrawingFrame != null)
            {
                // 计算 Pbgra32 格式的步长和缓冲区大小
                var stride = _drawingEventArgs.Stride;
                var bufferSize = stride * _renderTargetBitmap.PixelHeight;

                // 仅在尺寸变化时重新分配缓冲区
                if (_pixelsBuffer == null || _pixelsBuffer.Length != bufferSize)
                    _pixelsBuffer = new byte[bufferSize];

                // 将像素数据拷贝到托管缓冲区，fixed 钉住后传给事件参数
                fixed (byte* buffer = _pixelsBuffer)
                {
                    IntPtr pixels = (IntPtr)buffer;
                    _renderTargetBitmap.CopyPixels(Int32Rect.Empty, pixels, bufferSize, stride);

                    //var drawingEventArgs = new DrawingEventArgs(pixels, stride, Rectangle.Width, Rectangle.Height, ColorFormat.BGRA);
                    _drawingEventArgs.UpdateSource(null, pixels, _stopwatch.ElapsedMilliseconds - beginTime);
                    NewDrawingFrame.Invoke(this, _drawingEventArgs);
                }
            }

            // 淘汰超过 1 秒的旧时间戳，剩余数即为当前 FPS
            var currentTime = _stopwatch.ElapsedMilliseconds;
            while (_frameTimes.Count > 0 && currentTime - _frameTimes.Peek() >= WindowTimes)
            {
                _frameTimes.Dequeue();
            }
            Fps = _frameTimes.Count;
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            StopDrawing();
        }
    }
}
