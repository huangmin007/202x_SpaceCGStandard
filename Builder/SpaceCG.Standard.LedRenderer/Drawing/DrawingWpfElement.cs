using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SpaceCG.Device;

namespace SpaceCG.Drawing
{
    /// <summary>
    /// 实时绘制 WPF 显示元素
    /// </summary>
    public class DrawingWpfElement : IDrawingDisplay
    {
        /// <inheritdoc/>
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
        public System.Drawing.Rectangle Rectangle
        {
            get => _rectangle;
            set
            {
                if (value.X < 0 || value.Y < 0 || value.Width < 0 || value.Height < 0 || value.Width > 1024 || value.Height > 1024)
                    throw new ArgumentOutOfRangeException(nameof(value), "绘图尺寸必须在 (0,0,1024,1024) 范围内");
                _rectangle = value;
            }
        }
        private System.Drawing.Rectangle _rectangle = new System.Drawing.Rectangle(0, 0, 600, 32);

        /// <inheritdoc/>
        public double Fps { get; private set; } = 0.0;
        /// <inheritdoc/>
        public bool IsDrawing => _dispatcherTimer?.IsEnabled ?? false;
        /// <inheritdoc/>
        public event EventHandler<DrawingEventArgs> NewDrawingFrame;

        /// <summary>
        /// 要绘制的 WPF 显示元素，该对象是一个 <see cref="FrameworkElement"/> 类型。
        /// </summary>
        public object DrawingElement { get; private set; }

        private byte[] _pixelsBuffer;        
        private Stopwatch _stopwatch;
        private DispatcherTimer _dispatcherTimer;

        private DrawingVisual drawingVisual;
        private DrawingEventArgs drawingEventArgs;
        private RenderTargetBitmap renderTargetBitmap;

        //基于固定时间窗口的帧率计数法
        private readonly TimeSpan WindowTimes = TimeSpan.FromSeconds(1);
        private readonly Queue<DateTime> FrameTimes = new Queue<DateTime>(60);

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="visualElement"></param>
        public DrawingWpfElement(FrameworkElement visualElement)
        {
            this.DrawingElement = visualElement;

            _stopwatch = new Stopwatch();
            _dispatcherTimer = new DispatcherTimer();
            _dispatcherTimer.Tick += OnDispatcherTimerTick;
        }

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="visualElement"></param>
        /// <param name="rectangle"></param>
        /// <param name="interval"></param>
        public DrawingWpfElement(FrameworkElement visualElement, System.Drawing.Rectangle rectangle, int interval) : this(visualElement)
        {
            this.Interval = interval;
            this.Rectangle = rectangle;
        }

        private unsafe void OnDispatcherTimerTick(object sender, EventArgs e)
        {
            var visualElement = DrawingElement as FrameworkElement;
            if (visualElement == null) return;

            _stopwatch.Restart();
            FrameTimes.Enqueue(DateTime.Now);

            //var renderTargetBitmap = new RenderTargetBitmap(Rectangle.Width, Rectangle.Height, 96, 96, PixelFormats.Pbgra32);
            // 确保元素已测量和排列
            //visualElement.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            //visualElement.Arrange(new Rect(visualElement.DesiredSize));

            renderTargetBitmap.Render(drawingVisual);
            renderTargetBitmap.Render(visualElement);

            if (NewDrawingFrame != null)
            {
                // 计算所需缓冲区大小
                int stride = (renderTargetBitmap.PixelWidth * renderTargetBitmap.Format.BitsPerPixel + 7) / 8;
                int bufferSize = stride * renderTargetBitmap.PixelHeight;

                if(_pixelsBuffer == null || _pixelsBuffer.Length != bufferSize)
                    _pixelsBuffer = new byte[bufferSize];

                fixed (byte* buffer = _pixelsBuffer)
                {
                    IntPtr pixels = (IntPtr)buffer;
                    renderTargetBitmap.CopyPixels(Int32Rect.Empty, pixels, bufferSize, stride);

                    //var drawingEventArgs = new DrawingEventArgs(pixels, stride, Rectangle.Width, Rectangle.Height, ColorFormat.BGRA);
                    drawingEventArgs.Pixels = pixels;
                    drawingEventArgs.Stride = stride;
                    drawingEventArgs.Width = Rectangle.Width;
                    drawingEventArgs.Height = Rectangle.Height;
                    drawingEventArgs.Source = renderTargetBitmap;
                    drawingEventArgs.PixelFormat = ColorFormat.BGRA;
                    drawingEventArgs.ElapsedMilliseconds = _stopwatch.ElapsedMilliseconds;

                    NewDrawingFrame.Invoke(this, drawingEventArgs);
                }
            }
            
            var now = DateTime.Now;
            while (FrameTimes.Count > 0 && now - FrameTimes.Peek() > WindowTimes)
            {
                FrameTimes.Dequeue();
            }
            Fps = FrameTimes.Count;            
        }


        /// <inheritdoc/>
        public void StartDrawing()
        {
            if (_dispatcherTimer.IsEnabled) return;

            Fps = 0;
            FrameTimes.Clear();

            drawingVisual = new DrawingVisual();
            using (DrawingContext dc = drawingVisual.RenderOpen())
            {
                dc.DrawRectangle(Brushes.Black, null, new Rect(0, 0, Rectangle.Width, Rectangle.Height));
            }
            drawingEventArgs = new DrawingEventArgs();
            renderTargetBitmap = new RenderTargetBitmap(Rectangle.Width, Rectangle.Height, 96, 96, PixelFormats.Pbgra32);

            _dispatcherTimer.Interval = new TimeSpan(0, 0, 0, 0, Interval);
            _dispatcherTimer.Start();
        }

        /// <inheritdoc/>
        public void StartDrawing(System.Drawing.Rectangle rectangle, int interval)
        {
            if (_dispatcherTimer.IsEnabled) return;

            this.Interval = interval;
            this.Rectangle = rectangle;

            StartDrawing();
        }

        /// <inheritdoc/>
        public void StopDrawing()
        {
            _dispatcherTimer.Stop();

            FrameTimes.Clear();
            Fps = 0;
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            StopDrawing();
        }
    }
}
