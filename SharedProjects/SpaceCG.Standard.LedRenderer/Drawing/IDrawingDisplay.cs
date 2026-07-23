using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SpaceCG.Device;

namespace SpaceCG.Drawing
{
    
    /// <summary>
    /// 实时绘制显示对象接口
    /// </summary>
    public interface IDrawingDisplay : IDisposable
    {
        /// <summary>
        /// 每帧绘制处理的间隔时间，单位：毫秒
        /// <para>每帧绘制处理的时间应控制在 16~1000 毫秒之间</para>
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException" />
        int Interval { get; }

        /// <summary>
        /// 实时绘制的区域
        /// <para>绘制区域应控制在大小在 (0,0,1024,1024) 之内</para>
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException" />
        System.Drawing.Rectangle Rectangle { get; }

        /// <summary>
        /// 要实时绘制的元素或显示对象
        /// </summary>
        object DrawingElement { get; }

        /// <summary>
        /// 实时绘制的帧率
        /// </summary>
        double Fps { get; }

        /// <summary>
        /// 是否正在实时绘制显示对象
        /// </summary>
        bool IsDrawing { get; }

        /// <summary>
        /// 开始启动实时绘制
        /// </summary>
        void StartDrawing();

        /// <summary>
        /// 开始启动实时绘制
        /// </summary>
        /// <param name="rectangle"></param>
        /// <param name="interval"></param>
        void StartDrawing(System.Drawing.Rectangle rectangle, int interval);

        /// <summary>
        /// 停止实时绘制
        /// </summary>
        void StopDrawing();

        /// <summary>
        /// 绘制产生新的显示帧事件
        /// </summary>
        event EventHandler<DrawingEventArgs> NewDrawingFrame;
    }

    /// <summary>
    /// 绘制事件参数
    /// </summary>
    public class DrawingEventArgs : EventArgs
    {
        /// <summary>
        /// 为绘制而创建的位图或源对象
        /// </summary>
        public object Source { get; internal set; }

        /// <summary>
        /// 绘制的像素数据
        /// </summary>
        public IntPtr Pixels { get; internal set; }

        /// <summary>
        /// 绘制的像素数据步长
        /// </summary>
        public int Stride { get; internal set; }

        /// <summary>
        /// 绘制的宽度
        /// </summary>
        public int Width { get; internal set; }

        /// <summary>
        /// 绘制的高度
        /// </summary>
        public int Height { get; internal set; }

        /// <summary>
        /// 绘制的像素颜色格式
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
        /// <param name="stride"></param>
        /// <param name="width"></param>
        /// <param name="height"></param>
        /// <param name="pixelFormat"></param>
        public DrawingEventArgs(IntPtr pixels, int stride, int width, int height, ColorFormat pixelFormat)
        {
            Pixels = pixels;
            Stride = stride;

            Width = width;
            Height = height;

            PixelFormat = pixelFormat;
        }
    }

}
