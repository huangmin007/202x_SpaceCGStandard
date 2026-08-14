using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using SpaceCG.Device;
using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Drawing2D;
using Trace = SpaceCG.Diagnostics.Trace;

namespace SpaceCG.Drawing
{
    /// <summary>
    /// 实时截取桌面指定区域的像素数据，并通过 <see cref="NewDrawingFrame"/> 事件向外推送。
    /// </summary>
    /// <remarks>
    /// <para><b>线程模型：</b>截屏循环运行在独立的 <see cref="TaskCreationOptions.LongRunning"/> 后台线程中，
    /// <see cref="NewDrawingFrame"/> 事件在该线程同步触发。订阅者应尽快消费，避免阻塞截屏循环。</para>
    /// <para><b>性能特征：</b>使用单一 <see cref="Bitmap"/> + <see cref="Graphics"/> 实例复用策略，
    /// 避免每帧分配。FPS 基于 1 秒滑动时间窗口统计。</para>
    /// <para><b>像素格式：</b>截屏输出为 24bpp RGB，像素排列为 BGR（与 GDI <c>Format24bppRgb</c> 一致）。</para>
    /// <para><b>生命周期：</b>通过 <see cref="StartDrawing()"/> 启动、<see cref="StopDrawing"/> 停止。
    /// <see cref="IDisposable.Dispose"/> 自动调用 <see cref="StopDrawing"/>。</para>
    /// </remarks>
    public class DrawingDesktop : IDrawingDisplay
    {
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
        /// <remarks>默认截取区域 (0, 0, 600, 32)，尺寸上限 1024×1024。</remarks>
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
        public bool IsDrawing { get; private set; } = false;

        /// <inheritdoc/>
        public event EventHandler<DrawingEventArgs> NewDrawingFrame;

        private Task _drawingTask;
        private CancellationTokenSource _cts;

        /// <summary>
        /// 构造函数
        /// </summary>
        public DrawingDesktop()
        {
        }

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="rectangle"></param>
        /// <param name="interval"></param>
        public DrawingDesktop(Rectangle rectangle, int interval): this()
        {
            this.Interval = interval;
            this.Rectangle = rectangle;
        }
        
        /// <inheritdoc/>
        public void StartDrawing()
        {
            if (IsDrawing) return;
            if (_cts != null || _drawingTask != null) return;

            _cts = new CancellationTokenSource();
            _drawingTask = Task.Factory.StartNew(DrawingThread, this, _cts.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }

        /// <inheritdoc/>
        public void StartDrawing(Rectangle rectangle, int interval)
        {
            if (IsDrawing) return;

            this.Interval = interval;
            this.Rectangle = rectangle;

            StartDrawing();
        }

        /// <inheritdoc/>
        public void StopDrawing()
        {
            try { _cts?.Cancel(); }
            catch { }

            try
            {
                _drawingTask?.Wait(500);
                _drawingTask?.Dispose();
            }
            finally
            {
                _drawingTask = null;
            }

            try { _cts?.Dispose(); }
            finally { _cts = null; }

            Fps = 0;
            IsDrawing = false;
        }

        /// <summary>
        /// 桌面截屏主循环线程（LongRunning Task）。
        /// </summary>
        /// <param name="state"><see cref="DrawingDesktop"/> 实例。</param>
        /// <remarks>
        /// <para><b>帧率统计：</b>采用 1 秒滑动时间窗口法——记录每帧时间戳，淘汰超过 1 秒的旧记录，
        /// 剩余记录数即为瞬时 FPS。相比简单帧计数法，对间隔不均匀的场景更准确。</para>
        /// <para><b>资源复用：</b><see cref="Bitmap"/> 和 <see cref="Graphics"/> 在线程生命周期内复用，
        /// <see cref="DrawingEventArgs"/> 实例复用以避免每帧分配。</para>
        /// <para><b>事件参数复用注意：</b><see cref="DrawingEventArgs"/> 在多帧间复用，
        /// 订阅者不应缓存该实例或其 <see cref="DrawingEventArgs.Pixels"/> 指针到事件处理外部。</para>
        /// <para><b>间隔控制：</b>截屏+事件触发后计算实际耗时，若小于目标间隔则 Sleep 补齐差值。
        /// 预留 2ms 余量以补偿 Sleep 精度误差。</para>
        /// </remarks>
        private static void DrawingThread(object state)
        {
            var drawing = state as DrawingDesktop;
            if (drawing == null) return;

            // 1 秒滑动窗口帧率统计
            const int WindowTimes = 1000;   // ms
            Queue<long> frameTimes = new Queue<long>(60);

            var rectangle = drawing.Rectangle;
            var interval = drawing.Interval > 0 ? drawing.Interval : 40;
            // LockBits 锁定整个 bitmap
            var lockRectangle = new Rectangle(0, 0, rectangle.Width, rectangle.Height);

            // 事件参数复用，减少每帧 GC 分配
            var eventArgs = new DrawingEventArgs();
            var bitmap = new Bitmap(rectangle.Width, rectangle.Height, PixelFormat.Format24bppRgb);
            var graphics = Graphics.FromImage(bitmap);
            // 配置 Graphics 为速度优先
            graphics.SmoothingMode = SmoothingMode.HighSpeed;
            graphics.PixelOffsetMode = PixelOffsetMode.HighSpeed;
            graphics.CompositingQuality = CompositingQuality.HighSpeed;
            graphics.InterpolationMode = InterpolationMode.HighQualityBilinear;

            drawing.IsDrawing = true;
            var stopwatch = Stopwatch.StartNew();
            var cancellationToken = drawing._cts.Token;
            var beginTime = stopwatch.ElapsedMilliseconds;

            while (!cancellationToken.IsCancellationRequested)
            {
                // 记录本帧开始时间戳，用于帧率统计和耗时计算
                beginTime = stopwatch.ElapsedMilliseconds;
                frameTimes.Enqueue(beginTime);

                // 从屏幕指定坐标截取像素到 bitmap
                graphics.CopyFromScreen(rectangle.X, rectangle.Y, 0, 0, rectangle.Size);
                if (drawing.NewDrawingFrame != null)
                {
                    var bmpd = bitmap.LockBits(lockRectangle, ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
                    try
                    {
                        //var eventArgs = new DrawingEventArgs(bmpd.Scan0, bmpd.Stride, bmpd.Width, bmpd.Height, ColorFormat.BGR);
                        eventArgs.Source = bitmap;
                        eventArgs.Pixels = bmpd.Scan0;
                        eventArgs.Width = bmpd.Width;
                        eventArgs.Height = bmpd.Height;
                        eventArgs.Stride = bmpd.Stride;
                        eventArgs.PixelFormat = ColorFormat.BGR;
                        eventArgs.ElapsedMilliseconds = stopwatch.ElapsedMilliseconds - beginTime;

                        drawing.NewDrawingFrame.Invoke(drawing, eventArgs);
                    }
                    catch (Exception ex)
                    {
                        Trace.TraceError($"绘制桌面图像异常：{ex.ToString()}");
                    }
                    finally
                    {
                        bitmap.UnlockBits(bmpd);
                    }
                }

                // 淘汰超过 1 秒的旧时间戳，剩余数即为当前 FPS
                var currentTime = stopwatch.ElapsedMilliseconds;
                while (frameTimes.Count > 0 && currentTime - frameTimes.Peek() >= WindowTimes)
                {
                    frameTimes.Dequeue();
                }
                drawing.Fps = frameTimes.Count;

                // 补齐间隔：目标间隔 - 实际耗时，预留 2ms 避免过度 Sleep
                var useTime = stopwatch.ElapsedMilliseconds - beginTime;
                var diffTime = (int)(interval - useTime);
                if (diffTime > 3) Thread.Sleep(diffTime - 2);
            }

            // 释放线程级 GDI 资源
            bitmap.Dispose();
            graphics.Dispose();
            stopwatch.Stop();

            drawing.Fps = 0;
            drawing.IsDrawing = false;
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            StopDrawing();
        }
    }
}
