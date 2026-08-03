using System;
using SpaceCG.Device;
using Rectangle = System.Drawing.Rectangle;

namespace SpaceCG.Drawing
{
    /// <summary>
    /// 实时绘制显示对象的基础接口，定义通用的绘制控制、帧率统计和帧就绪事件。
    /// </summary>
    /// <remarks>
    /// <para><b>线程模型：</b>绘制在独立的后台线程（LongRunning Task）或 UI 定时器中执行，
    /// <see cref="NewDrawingFrame"/> 事件在绘制线程上同步触发，订阅者应在事件处理中尽快完成消费。</para>
    /// <para><b>生命周期：</b>调用 <see cref="StartDrawing()"/> 启动，<see cref="StopDrawing"/> 停止，
    /// <see cref="IDisposable.Dispose"/> 等价于 <see cref="StopDrawing"/>。</para>
    /// <para><b>典型实现：</b><see cref="IDrawingDisplay{TUIElement}"/>（含绘制目标元素）、
    /// <see cref="DrawingDesktop"/>（桌面截屏，无元素）、<see cref="DrawingWpfElement"/>（WPF 控件）。</para>
    /// </remarks>
    public interface IDrawingDisplay : IDisposable
    {
        /// <summary>
        /// 每帧绘制处理的间隔时间，单位：毫秒。
        /// </summary>
        /// <value>有效范围 [16, 1000]，默认值由实现类决定。</value>
        /// <exception cref="ArgumentOutOfRangeException">值不在 [16, 1000] 范围内。</exception>
        int Interval { get; set; }

        /// <summary>
        /// 实时绘制的显示区域。
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">坐标或尺寸超出限制。</exception>
        Rectangle Rectangle { get; set; }

        /// <summary>
        /// 实时绘制的帧率（帧/秒）。
        /// </summary>
        double Fps { get; }

        /// <summary>
        /// 是否正在实时绘制显示对象
        /// </summary>
        bool IsDrawing { get; }

        /// <summary>
        /// 开始启动实时绘制。
        /// </summary>
        void StartDrawing();

        /// <summary>
        /// 使用指定的区域和间隔启动实时绘制。
        /// </summary>
        /// <param name="rectangle">截屏/绘制区域。</param>
        /// <param name="interval">每帧间隔，单位毫秒，范围 [16, 1000]。</param>
        void StartDrawing(Rectangle rectangle, int interval);

        /// <summary>
        /// 停止实时绘制
        /// </summary>
        void StopDrawing();

        /// <summary>
        /// 当新一帧像素数据就绪时触发。
        /// </summary>
        event EventHandler<DrawingEventArgs> NewDrawingFrame;
    }
    /// <summary>
    /// 带绘制目标元素的实时绘制接口，继承 <see cref="IDrawingDisplay"/> 的所有绘制控制能力。
    /// </summary>
    /// <typeparam name="TUIElement">待绘制的 UI 元素类型，必须是引用类型。例如 <see cref="DrawingDesktop"/> 使用 <c>object</c>（无具体元素），
    /// <see cref="DrawingWpfElement"/> 使用 <see cref="System.Windows.FrameworkElement"/>。</typeparam>
    /// <remarks>
    /// <para>不需要绘制目标元素的实现（如桌面截屏）可直接实现 <see cref="IDrawingDisplay"/> 基接口。</para>
    /// </remarks>
    public interface IDrawingDisplay<TUIElement> : IDrawingDisplay where TUIElement : class
    {
        /// <summary>
        /// 要实时绘制的 UI 元素或显示对象。
        /// </summary>
        TUIElement DrawingElement { get; }
    }

    /// <summary>
    /// 绘制帧就绪事件参数，携带当前帧的像素数据指针及元信息。
    /// </summary>
    public class DrawingEventArgs : EventArgs
    {
        /// <summary>
        /// 为绘制而创建的位图或源对象
        /// </summary>
        public object Source { get; internal set; }

        /// <summary>
        /// 绘制的像素数据起始地址。
        /// </summary>
        public IntPtr Pixels { get; internal set; }

        /// <summary>
        /// 像素数据宽度（像素）。
        /// </summary>
        public int Width { get; internal set; }

        /// <summary>
        /// 像素数据高度（像素）。
        /// </summary>
        public int Height { get; internal set; }

        /// <summary>
        /// 像素数据扫描步长（字节）。
        /// </summary>
        public int Stride { get; internal set; }

        /// <summary>
        /// 像素颜色通道排列格式，桌面截屏通常为 <see cref="ColorFormat.BGR"/>。
        /// </summary>
        public ColorFormat PixelFormat { get; internal set; }

        /// <summary>
        /// 绘制当前帧的耗时，单位：毫秒
        /// </summary>
        public long ElapsedMilliseconds { get; internal set; }

        /// <summary>
        /// 构造函数
        /// </summary>
        public DrawingEventArgs()
        {

        }

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="pixels"></param>
        /// <param name="width"></param>
        /// <param name="height"></param>
        /// <param name="stride"></param>
        /// <param name="pixelFormat"></param>
        public DrawingEventArgs(IntPtr pixels, int width, int height, int stride, ColorFormat pixelFormat)
        {
            Pixels = pixels;

            Width = width;
            Height = height;
            Stride = stride;
            PixelFormat = pixelFormat;
        }
    }

}
