using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using SpaceCG.Device;

namespace SpaceCG.Drawing
{
    /// <summary>
    /// 实时绘图/捕获桌面对象
    /// </summary>
    public class DrawingDesktop : IDrawingDisplay
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
        /// <exception cref="NotImplementedException">桌面绘图对象不支持此属性</exception>
        public object DrawingElement => throw new NotImplementedException("桌面绘图对象不支持此属性");

        /// <inheritdoc/>
        public double Fps { get; private set; } = 0.0;

        /// <inheritdoc/>
        public bool IsDrawing { get; private set; } = false;

        /// <inheritdoc/>
        public event EventHandler<DrawingEventArgs> NewDrawingFrame;

        private Task _draingTask;
        private volatile bool _isCapturing = false;

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
        public DrawingDesktop(System.Drawing.Rectangle rectangle, int interval): this()
        {
            this.Interval = interval;
            this.Rectangle = rectangle;
        }
        
        /// <inheritdoc/>
        public void StartDrawing()
        {
            if (_isCapturing) return;

            _isCapturing = true;
            _draingTask = Task.Factory.StartNew(DrawingThread, this, TaskCreationOptions.LongRunning);
        }

        /// <inheritdoc/>
        public void StartDrawing(System.Drawing.Rectangle rectangle, int interval)
        {
            if (_isCapturing) return;

            this.Interval = interval;
            this.Rectangle = rectangle;

            StartDrawing();
        }

        /// <inheritdoc/>
        public void StopDrawing()
        {
            if (!_isCapturing) return;

            this.Fps = 0;
            _isCapturing = false;

            _draingTask.Wait(100);
            _draingTask.Dispose();
            _draingTask = null;
        }

        /// <summary>
        /// 实时绘制处理线程
        /// </summary>
        /// <param name="state"></param>
        private static void DrawingThread(object state)
        {
            DrawingDesktop drawing = state as DrawingDesktop;
            if (drawing == null) return;

            //基于固定时间窗口的帧率计数法
            TimeSpan WindowTimes = TimeSpan.FromSeconds(1);
            Queue<DateTime> FrameTimes = new Queue<DateTime>(60);

            var rectangle = drawing.Rectangle;
            var interval = drawing.Interval > 0 ? drawing.Interval : 40;

            drawing.IsDrawing = true;
            Stopwatch stopwatch = new Stopwatch();

            var eventArgs = new DrawingEventArgs();
            var bitmap = new System.Drawing.Bitmap(rectangle.Width, rectangle.Height, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
            var graphics = System.Drawing.Graphics.FromImage(bitmap);
            graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighSpeed;
            graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighSpeed;
            graphics.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighSpeed;
            graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBilinear;

            while (drawing._isCapturing)
            {
                stopwatch.Restart();
                FrameTimes.Enqueue(DateTime.Now);
#if false
                using (System.Drawing.Bitmap bitmap = new System.Drawing.Bitmap(rectangle.Width, rectangle.Height, System.Drawing.Imaging.PixelFormat.Format24bppRgb))
                {
                    using (System.Drawing.Graphics graphics = System.Drawing.Graphics.FromImage(bitmap))
                    {
                        graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBilinear;
                        graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighSpeed;
                        graphics.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighSpeed;
                        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighSpeed;

                        graphics.CopyFromScreen(rectangle.X, rectangle.Y, 0, 0, rectangle.Size);
                    }

                    if (drawing.NewDrawingFrame != null)
                    {
                        var bmpd = bitmap.LockBits(rectangle, System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
                        try
                        {
                            var eventArgs = new DrawingEventArgs(bmpd.Scan0, bmpd.Stride, bmpd.Width, bmpd.Height, ColorFormat.BGR);
                            eventArgs.Source = bitmap;
                            eventArgs.ElapsedMilliseconds = stopwatch.ElapsedMilliseconds;

                            drawing.NewDrawingFrame.Invoke(drawing, eventArgs);
                        }
                        catch(Exception ex)
                        {
                        }
                        finally
                        {
                            bitmap.UnlockBits(bmpd);
                        }
                    }
                }
#else
                graphics.CopyFromScreen(rectangle.X, rectangle.Y, 0, 0, rectangle.Size);
                if (drawing.NewDrawingFrame != null)
                {
                    var bmpd = bitmap.LockBits(rectangle, System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
                    try
                    {
                        //var eventArgs = new DrawingEventArgs(bmpd.Scan0, bmpd.Stride, bmpd.Width, bmpd.Height, ColorFormat.BGR);
                        eventArgs.Source = bitmap;
                        eventArgs.Pixels = bmpd.Scan0;
                        eventArgs.Stride = bmpd.Stride;
                        eventArgs.Width = bmpd.Width;
                        eventArgs.Height = bmpd.Height;
                        eventArgs.PixelFormat = ColorFormat.BGR;
                        eventArgs.ElapsedMilliseconds = stopwatch.ElapsedMilliseconds;

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
#endif
                var now = DateTime.Now;
                while (FrameTimes.Count > 0 && now - FrameTimes.Peek() > WindowTimes)
                {
                    FrameTimes.Dequeue();
                }
                drawing.Fps = FrameTimes.Count;

                var elapsed = stopwatch.ElapsedMilliseconds;
                var timeout = (int)(interval - elapsed);
                if (timeout > 5) Thread.Sleep(timeout - 3);
            }

            bitmap.Dispose();
            graphics.Dispose();

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
