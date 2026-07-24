using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Xml.Linq;
using SpaceCG.Drawing;
using SpaceCG.Extensions;

namespace SpaceCG.Device
{

    /// <summary>
    /// Led 灯带对象
    /// </summary>
    public class LedStripObject
    {
        #region const 常量定义
        /// <summary>  RGB 灯珠支持的最大 Led 灯珠数量  </summary>
        internal const ushort MaxRGBLedCount = 1024;
        /// <summary>  RGBW 灯珠支持的最大 Led 灯珠数量  </summary>
        internal const ushort MaxWRGBLedCount = 768;
        /// <summary>  帧尾字节数  </summary>
        internal const int FrameFooterLength = 2;
        /// <summary>  帧头字节数  </summary>
        internal const int FrameHeaderLength = 16;
        /// <summary>  默认的帧队列最大长度 </summary>
        internal const int DefaultQueueMaxCount = 3;
        internal const int FramePoolMaxCount = 16;
        #endregion

        /// <summary>
        /// 备注信息，用于标识其它信息
        /// </summary>
        public string Comment { get; set; } = string.Empty;

        /// <summary> 获取或设置一个用于存储有关此元素的自定义信息的任意对象值。 </summary>
        public object Tag { get; set; }


        #region 这些都是 Led 灯带的属性量
        /// <summary>
        /// Led 灯带支持的最大 Led 灯珠数量
        /// </summary>
        internal ushort MaxLedCount => ColorFormat.GetChannelCount() == 3 ? MaxRGBLedCount : MaxWRGBLedCount;
        /// <summary>
        /// Led 灯带的组地址
        /// </summary>
        public ushort Group { get; private set; } = 0;
        /// <summary>
        /// Led 灯带的设备地址
        /// </summary>
        public ushort Address { get; private set; } = 0x0001;
        /// <summary>
        /// Led 灯带的设备端口号
        /// </summary>
        public byte Port { get; private set; } = 0x00;
        /// <summary>
        /// Led 灯带的保留数据
        /// </summary>
        public ushort Reserved { get; set; } = 0x0000;
        /// <summary>
        /// Led 灯带的类型
        /// </summary>
        public LedType LedType { get; private set; } = LedType.WS2812B;
        /// <summary>
        /// Led 灯带的颜色格式
        /// </summary>
        public ColorFormat ColorFormat { get; private set; } = ColorFormat.RGB;
        /// <summary>
        /// Led 灯带的灯珠数量
        /// </summary>
        public int LedCount => _ledPoints.Count;
        /// <summary>
        /// Led 灯带的唯一标识，用于标识 Led 灯带
        /// <para>由端口号和设备地址组成，UID = (Port &lt;&lt; 16) | Address</para>
        /// </summary>
        public uint UID => (uint)(Port << 16 | Address);
        #endregion

        #region 这些是渲染优化相关的属性 
        /// <summary>
        /// 【渲染优化参数】Led 灯带的渲染 (数据写入后) 等待超时时间，单位：毫秒，默认为 0 毫秒
        /// <para>有些总线类型在数据写入时阻塞或写入速度较快，导致 Led 灯珠花屏，可以通过数据写入后等待几毫秒，来避免花屏的问题</para>
        /// </summary>
        public int Timeout
        {
            get { return _timeout; }
            set
            {
                if (value < 0 || value > 1000)
                    throw new ArgumentOutOfRangeException($"Timeout 必须在 0-1000 之间.");
                _timeout = value;
            }
        }
        private int _timeout = 0;

        /// <summary>
        /// 【渲染优化参数】 Led 灯带数据的填充数量，默认为 0, 表示数据填充所有灯珠 <see cref="LedCount"/>
        /// <para>该参数如果大于 0，则应该与 <see cref="RepeatCount"/> 参数配合使用，以达渲染数据优化的目</para>
        /// <para>例如：整体呼吸效果、对称流水效果等等</para>
        /// </summary>
        public int FillCount
        {
            get
            {
                if (__fillCount <= 0) return LedCount;
                if (__fillCount > LedCount) return LedCount;
                return __fillCount;
            }
            set
            {
                __fillCount = Math.Max(0, Math.Min(value, LedCount));
            }
        }
        private int __fillCount = 0;

        /// <summary>
        /// 【渲染优化参数】 Led 灯带数据复制次数，默认为 1 次
        /// <para>该参数一般与 <see cref="FillCount"/> 参数配合使用，以达渲染数据优化的目的</para>
        /// </summary>
        public int RepeatCount
        {
            get
            {
                if (__repeatCount < 1) return 1;
                if (__repeatCount > LedCount) return LedCount;
                return __repeatCount;
            }
            set
            {
                __repeatCount = Math.Max(1, Math.Min(value, LedCount));
            }
        }
        private volatile int __repeatCount = 1;

        /// <summary>
        /// 【渲染优化参数】待渲染的帧队列最大长度，默认为 3；如果队列满了，则丢弃最旧的帧。
        /// <para>该参数可以控制多个总线、多个灯带同时渲染时之间的时间差，以达到最佳的视觉同步效果</para>
        /// </summary>
        public int QueueMaxCount
        {
            get => _queueMaxCount;
            set
            {
                if (value < 1 || value > 60)
                    throw new ArgumentOutOfRangeException($"QueueMaxCount 必须在 1-60 之间.");
                _queueMaxCount = value;
            }
        }
        private volatile int _queueMaxCount = DefaultQueueMaxCount;

        /// <summary>
        /// 允许使用 位图像素 数据，默认为 true。主要用于控制二维渲染数据，或外部数据插入渲染。
        /// <para>如果启用，则使用 位图像素 数据</para>
        /// <para>此属性可用于外部数据的插入渲染，或控制当前灯带的 暂停/恢复 等操作</para>
        /// </summary>
        public bool UseBitmapPixels { get; set; } = true;

        /// <summary>
        /// 当前 Led 灯带的渲染帧率(数据写入帧率)
        /// </summary>
        public int Fps { get; internal set; } = 0;
        /// <summary>
        /// 当前待渲染的帧队列当前长度
        /// </summary>
        public int FrameCount => _frameQueue.Count;

        /// <summary>
        /// Led 灯带的灯珠位置，或者说在位图上的坐标位置，该集合也描述了实际的 Led 灯珠的物理顺序位置
        /// <para>注意：灯珠位置在集合列表中是有先后顺序的，一定要与物理位置或顺序保持一致</para>
        /// </summary>
        public IReadOnlyList<System.Drawing.Point> LedPoints
        {
            get
            {
                if (_ledPointsReadOnly == null)
                    _ledPointsReadOnly = _ledPoints.AsReadOnly();
                return _ledPointsReadOnly;
            }
        }
        private IReadOnlyList<System.Drawing.Point> _ledPointsReadOnly;
        #endregion

        /// <summary>  Led 灯带待渲染的帧队列  </summary>
        private readonly ConcurrentQueue<byte[]> _frameQueue = new ConcurrentQueue<byte[]>();
        /// <summary>  数据帧缓冲池，避免频繁创建和销毁数据帧  </summary>
        private readonly ConcurrentQueue<byte[]> _framePool = new ConcurrentQueue<byte[]>();
        /// <inheritdoc cref="LedPoints"/> 
        private readonly List<System.Drawing.Point> _ledPoints = new List<System.Drawing.Point>(512);

        private volatile int _renderFps = 0;
        private volatile int _renderCount = 0;
        private volatile byte[] _lastFrame = Array.Empty<byte>();

        private LedStripObject()
        {
            // 淡蓝色：0x008EFF
        }

        /// <summary>
        /// Led 灯带的构造函数
        /// </summary>
        /// <param name="address"></param>
        /// <param name="port"></param>
        /// <param name="ledType"></param>
        /// <param name="ledColorFormat"></param>
        public LedStripObject(ushort address, byte port, LedType ledType = LedType.WS2812B, ColorFormat ledColorFormat = ColorFormat.RGB)
        {
            this.Port = port;
            this.Address = address;

            this.LedType = ledType;
            this.ColorFormat = ledColorFormat;
        }


        #region Add/Remove-Point/AddPoints
        /// <summary>
        /// 在当前灯带的结尾处添加一颗灯珠，并映射到位图上的坐标位置
        /// <para>注意：灯珠位置在集合列表中是有先后顺序的，一定要与物理位置或顺序保持一致</para>
        /// </summary>
        /// <param name="point"></param>
        public void AddPoint(System.Drawing.Point point)
        {
            if (_ledPoints.Count >= MaxLedCount)
                throw new ArgumentOutOfRangeException($"Led 灯带({LedType}/{ColorFormat})的灯珠总数量不能超过 {MaxLedCount}.");

            _ledPoints.Add(point);
        }
        /// <summary>
        /// 在当前灯带的指定索引处添加一颗灯珠，并映射到位图上的坐标位置
        /// <para>注意：灯珠位置在集合列表中是有先后顺序的，一定要与物理位置或顺序保持一致</para>
        /// </summary>
        /// <param name="index"></param>
        /// <param name="point"></param>
        /// <exception cref="ArgumentException"></exception>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        public void AddPoint(int index, System.Drawing.Point point)
        {
            if (index < 0 || index > _ledPoints.Count)
                throw new ArgumentOutOfRangeException($"索引超出范围.");

            if (_ledPoints.Count >= MaxLedCount)
                throw new ArgumentOutOfRangeException($"Led 灯带的灯珠总数量不能超过 {MaxLedCount}.");

            _ledPoints.Insert(index, point);
        }
        /// <summary>
        /// 在当前灯带的结尾处添加一组灯珠，并映射到位图上的坐标位置。
        /// <para>注意：灯珠位置在集合列表中是有先后顺序的，一定要与物理位置或顺序保持一致</para>
        /// </summary>
        /// <param name="points"></param>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        /// <exception cref="ArgumentNullException"></exception>
        public void AddPoints(IEnumerable<System.Drawing.Point> points)
        {
            if (points == null || !points.Any()) return;

            if (_ledPoints.Count + points.Count() > MaxLedCount)
                throw new ArgumentOutOfRangeException($"添加的点数超过了 LED 灯带({LedType}/{ColorFormat})的限制长度 {MaxLedCount} 珠。");

            _ledPoints.AddRange(points);
        }
        /// <summary>
        /// 在当前灯带的指定索引处添加一组灯珠，并映射到位图上的坐标位置。
        /// <para>注意：灯珠位置在集合列表中是有先后顺序的，一定要与物理位置或顺序保持一致</para>
        /// </summary>
        /// <param name="index"></param>
        /// <param name="points"></param>
        public void AddPoints(int index, IEnumerable<System.Drawing.Point> points)
        {
            if (points == null || !points.Any()) return;

            var ledCount = _ledPoints.Count;
            if (index < 0 || index > ledCount)
                throw new ArgumentOutOfRangeException($"索引超出范围.");

            if (ledCount + points.Count() > MaxLedCount)
                throw new ArgumentOutOfRangeException($"添加的点数超过了 LED 灯带({LedType}/{ColorFormat})的限制长度 {MaxLedCount} 珠。");

            _ledPoints.InsertRange(index, points);
        }
        /// <summary>
        /// 在当前灯带的结尾处添加一组灯珠，从 <paramref name="start"/> 到 <paramref name="end"/> 的直线点集，并映射到位图上的坐标位置。
        /// <para>注意：灯珠位置在集合列表中是有先后顺序的，一定要与物理位置或顺序保持一致</para>
        /// </summary>
        /// <param name="start"></param>
        /// <param name="end"></param>
        public void AddPoints(System.Drawing.Point start, System.Drawing.Point end) => AddPoints(DrawingExtensions.GetPoints(start, end));

        /// <summary>
        /// 移除所有灯珠
        /// </summary>
        public void ClearPoints() => _ledPoints.Clear();
        /// <summary>
        /// 确定当前灯带是否包含指定坐标处的灯珠
        /// </summary>
        /// <param name="point"></param>
        /// <returns></returns>
        public bool ContainsPoint(System.Drawing.Point point) => _ledPoints.Contains(point);

        /// <summary>
        /// 移除指定索引处的灯珠
        /// </summary>
        /// <param name="index"></param>
        public void RemovePoint(int index) => _ledPoints.RemoveAt(index);
        /// <summary>
        /// 移除指定坐标处的灯珠
        /// </summary>
        /// <param name="point"></param>
        public void RemovePoint(System.Drawing.Point point) => _ledPoints.Remove(point);
        /// <summary>
        /// 移除指定范围的灯珠
        /// </summary>
        /// <param name="index"></param>
        /// <param name="count"></param>
        public void RemovePoints(int index, int count) => _ledPoints.RemoveRange(index, count);
        /// <summary>
        /// 移除指定坐标集合的灯珠
        /// </summary>
        /// <param name="points"></param>
        public void RemovePoints(IEnumerable<System.Drawing.Point> points) => _ledPoints.RemoveAll(points.Contains);
        #endregion


        #region 设置上电显示颜色
        /// <summary>
        /// 设置上电显示颜色
        /// </summary>
        /// <param name="color">填充的颜色数据</param>
        /// <param name="isShow">开启/关闭上电显示颜色</param>
        /// <param name="colorFormat"> <paramref name="color"/> 的颜色格式，默认为 ARGB</param>
        public void SetPowerOnColor(uint color, bool isShow = true, ColorFormat colorFormat = ColorFormat.ARGB)
        {
            byte[] frame = CreateEmptyFrame(1, this.LedCount);

            // 设置上电显示的颜色（9B）
            // 关闭上电显示功能（9C）
            frame[8] = (byte)(isShow ? 0x9B : 0x9C);    // 功能码

            if (isShow)
            {
                // 通道索引表
                var inputIndices = colorFormat.GetChannelIndices();
                var outputIndices = ColorFormat.GetChannelIndices();

                // 颜色的通道数量
                int inputChannelCount = inputIndices.Length;
                int outputChannelCount = outputIndices.Length;

                if (inputChannelCount != 4)
                    throw new ArgumentException("输入颜色值 (uint类型) 的通道数量必须为 4 ", nameof(colorFormat));

                int index = -1, outputOffset = 0;
                for (var j = 0; j < outputChannelCount; j++)
                {
                    outputOffset = j + FrameHeaderLength;

                    index = Array.IndexOf(inputIndices, outputIndices[j]);

                    frame[outputOffset] = (byte)((color >> (24 - index * 8)) & 0xFF);
                }
            }

            _frameQueue.Enqueue(frame);
            CheckFrameQueueCount();
        }
        #endregion


        #region AddFrame 系列方法
        /// <summary>
        /// 添加待渲染的颜色数据帧，会检查该数据帧是否符合当前 Led 灯带的参数（例如：检查帧头/帧尾格式、地址/端口号/渲染数量是否匹配等等）
        /// <para>写入的是完整数据帧；会自动计算更新 Group/Reserved/Repeat 数据</para>
        /// </summary>
        /// <param name="frame"></param>
        internal void AddColorFrame(byte[] frame)
        {
            if (frame == null || frame.Length < 21 ||
                frame[0] != 0xDD || frame[1] != 0x55 || frame[2] != 0xEE ||
                frame[frame.Length - 2] != 0xAA || frame[frame.Length - 1] != 0xBB)
                throw new ArgumentException("数据帧格式错误", nameof(frame));
            if (frame[8] != 0x99) throw new ArgumentException("数据帧的功能码不正确");

            if (this.Port != frame[7]) throw new ArgumentException("数据帧的端口号不匹配");
            if (this.LedType != (LedType)frame[9]) throw new ArgumentException("数据帧的灯带类型不匹配");
            if (this.Address != (ushort)((frame[5] << 8) | frame[6])) throw new ArgumentException("数据帧的设备地址不匹配");

            var ledCount = this.LedCount;
            var repeat = (ushort)((frame[14] << 8) | frame[15]);    // GetRepeatCount(frame);
            if (repeat < 1 || repeat > ledCount) throw new ArgumentException($"数据帧的扩展次数 {repeat} 不正确，或超出灯珠数量 {ledCount} 范围");

            int colorChannelCount = ColorFormat.GetChannelCount();

            var colorSize = frame.Length - 18;              //颜色字节的数据长度
            if (colorSize % colorChannelCount != 0)
                throw new ArgumentException($"数据帧的颜色字节长度 {colorSize} 不正确，与灯带的颜色格式 {ColorFormat} 不匹配");

            // 重新计算扩展次数，并修正
            var fillCount = colorSize / colorChannelCount;      //填充的数据量
            if (fillCount * repeat > ledCount) repeat = (ushort)Math.Ceiling(ledCount / (float)fillCount);

            //frame[0] = 0xDD;
            //frame[1] = 0x55;
            //frame[2] = 0xEE;

            frame[3] = (byte)(Group >> 8);    // 组地址
            frame[4] = (byte)(Group & 0xFF);

            //frame[5] = (byte)(Address >> 8);    // 设备地址
            //frame[6] = (byte)(Address & 0xFF);

            //frame[7] = Port;              // 端口号

            //frame[8] = 0x99;            // 功能码
            //frame[9] = (byte)Type;      // 灯带类型

            frame[10] = (byte)(Reserved >> 8);    // 保留字节
            frame[11] = (byte)(Reserved & 0xFF);

            //frame[12] = (byte)(colorSize >> 8);    // 数据长度
            //frame[13] = (byte)(colorSize & 0xFF);

            frame[14] = (byte)(repeat >> 8);    // 扩展次数
            frame[15] = (byte)(repeat & 0xFF);

            //frame[frame.Length - 2] = 0xAA;
            //frame[frame.Length - 1] = 0xBB;

            _frameQueue.Enqueue(frame);
            CheckFrameQueueCount();
        }
        /// <summary>
        /// 添加待渲染的颜色数据帧
        /// </summary>
        /// <param name="r"></param>
        /// <param name="g"></param>
        /// <param name="b"></param>
        /// <param name="repeat"></param>
        public void AddColorFrame(byte r, byte g, byte b, int repeat) => AddColorFrame((uint)(0xFF << 24 | r << 16 | g << 8 | b), repeat, ColorFormat.ARGB);
        /// <summary>
        /// 添加待渲染的颜色数据帧
        /// <para>输入颜色值 (<see cref="uint"/>类型) 数组 <paramref name="color"/> 颜色通道 <paramref name="colorFormat"/> 必须是 四通道 类型</para>
        /// </summary>
        /// <param name="color">颜色值数据，需要指定颜色通道格式 <paramref name="colorFormat"/></param>
        /// <param name="repeat">颜色数据重复次数</param>
        /// <param name="colorFormat"><paramref name="color"/> 数据的颜色值格式</param>
        /// <exception cref="ArgumentException"></exception>
        public void AddColorFrame(uint color, int repeat, ColorFormat colorFormat = ColorFormat.ARGB)
        {
            if (repeat == 0 || repeat > LedCount)
                throw new ArgumentException($"参数 repeat 不能为 0 或超过灯珠数量 {LedCount} 范围");

            // 通道索引表
            var inputIndices = colorFormat.GetChannelIndices();
            var outputIndices = ColorFormat.GetChannelIndices();

            // 颜色的通道数量
            int inputChannelCount = inputIndices.Length;
            int outputChannelCount = outputIndices.Length;

            if (inputChannelCount != 4)
                throw new ArgumentException("输入颜色值 (uint类型) 的通道数量必须为 4 ", nameof(colorFormat));

            int index = -1, outputOffset = 0;
            byte[] frame = CreateEmptyFrame(1, repeat);

            for (var j = 0; j < outputChannelCount; j++)
            {
                outputOffset = j + FrameHeaderLength;

                index = Array.IndexOf(inputIndices, outputIndices[j]);

                frame[outputOffset] = (byte)((color >> (24 - index * 8)) & 0xFF);
            }

            _frameQueue.Enqueue(frame);
            CheckFrameQueueCount();
        }

        /// <summary>
        /// 添加待渲染的颜色数据帧，跟据 <paramref name="colors"/> 数据量 和 <paramref name="repeat"/> 填充灯珠
        /// <para>输入颜色值 (<see cref="byte"/>类型) 数组 <paramref name="colors"/> 颜色通道 <paramref name="colorFormat"/> 可以是 三通道 或 四通道 类型</para>
        /// </summary>
        /// <param name="colors">颜色值数组，需要指定颜色通道格式 <paramref name="colorFormat"/></param>
        /// <param name="repeat">颜色数据重复次数</param>
        /// <param name="colorFormat"><paramref name="colors"/> 数据的颜色值格式</param>
        /// <exception cref="ArgumentException"></exception>
        public void AddColorFrame(IReadOnlyList<byte> colors, int repeat, ColorFormat colorFormat = ColorFormat.RGB)
        {
            if (colors == null || colors.Count == 0)
                throw new ArgumentException("颜色值数组不能为空，或长度不正确");
            if (repeat == 0 || repeat > this.LedCount)
                throw new ArgumentException($"参数 repeat 不能为 0 或超过灯珠数量 {this.LedCount} 范围");

            // 通道索引表
            var inputIndices = colorFormat.GetChannelIndices();
            var outputIndices = ColorFormat.GetChannelIndices();

            // 颜值的通道数量
            int inputChannelCount = inputIndices.Length;
            int outputChannelCount = outputIndices.Length;

            if (colors.Count < inputChannelCount || colors.Count % inputChannelCount != 0)
                throw new ArgumentException($"颜色数据长度 {colors.Count} 与通道数 {inputChannelCount} 不匹配", nameof(colors));

            var renderCount = colors.Count / inputChannelCount;
            renderCount = Math.Min(renderCount, this.LedCount);
            byte[] frame = CreateEmptyFrame((ushort)renderCount, repeat);

            if (colorFormat == ColorFormat && colors is Array colorArray)
            {
                var renderSize = renderCount * outputChannelCount;
                Array.Copy(colorArray, 0, frame, FrameHeaderLength, renderSize);
            }
            else
            {
                int i = 0, j = 0, index = -1;
                int inputOffset = 0, outputOffset = 0;

                // 预计算通道索引映射
                int[] channelMap = new int[outputChannelCount];
                for (i = 0; i < outputChannelCount; i++)
                {
                    channelMap[i] = Array.IndexOf(inputIndices, outputIndices[i]);
                }

                for (i = 0; i < renderCount; i++)
                {
                    inputOffset = i * inputChannelCount;
                    outputOffset = i * outputChannelCount + FrameHeaderLength;

                    for (j = 0; j < outputChannelCount; j++)
                    {
                        index = channelMap[j];
                        frame[outputOffset + j] = (index >= 0) ? colors[inputOffset + index] : (byte)0xFF;
                    }
                }
            }

            _frameQueue.Enqueue(frame);
            CheckFrameQueueCount();
        }
        /// <summary>
        /// 添加待渲染的颜色数据帧，跟据 <paramref name="colors"/> 数据量 和 <paramref name="repeat"/> 填充灯珠
        /// <para>输入颜色值 (<see cref="uint"/>类型) 数组 <paramref name="colors"/> 颜色通道 <paramref name="colorFormat"/> 必须是 四通道 类型</para>
        /// </summary>
        /// <param name="colors">颜色值数组，需要指定颜色通道格式 <paramref name="colorFormat"/></param>
        /// <param name="repeat">颜色数据重复次数</param>
        /// <param name="colorFormat"><paramref name="colors"/> 数据的颜色值格式</param>
        /// <exception cref="ArgumentException"></exception>
        public void AddColorFrame(IReadOnlyList<uint> colors, int repeat, ColorFormat colorFormat = ColorFormat.ARGB)
        {
            if (colors == null || colors.Count == 0)
                throw new ArgumentException("颜色值数组不能为空");
            if (repeat == 0 || repeat > this.LedCount)
                throw new ArgumentException($"参数 repeat 不能为 0 或超过灯珠数量 {this.LedCount} 范围");

            // 通道索引表
            var inputIndices = colorFormat.GetChannelIndices();
            var outputIndices = ColorFormat.GetChannelIndices();

            // 颜色的通道数量
            int inputChannelCount = inputIndices.Length;
            int outputChannelCount = outputIndices.Length;

            if (inputChannelCount != 4)
                throw new ArgumentException("输入颜色值 (uint类型) 的通道数量必须为 4 ", nameof(colorFormat));

            int i = 0, j = 0, index = -1, outputOffset = 0;
            int renderCount = Math.Min(colors.Count, this.LedCount);
            byte[] frame = CreateEmptyFrame((ushort)renderCount, repeat);

            // 预计算通道索引映射
            int[] channelMap = new int[outputChannelCount];
            for (i = 0; i < outputChannelCount; i++)
            {
                channelMap[i] = Array.IndexOf(inputIndices, outputIndices[i]);
            }

            for (i = 0; i < renderCount; i++)
            {
                outputOffset = i * outputChannelCount + FrameHeaderLength;

                for (j = 0; j < outputChannelCount; j++)
                {
                    index = channelMap[j];
                    frame[outputOffset + j] = (byte)((colors[i] >> (24 - index * 8)) & 0xFF);
                }
            }

            _frameQueue.Enqueue(frame);
            CheckFrameQueueCount();
        }
        #endregion


        #region 创建空白的颜色(黑色)的数据帧 CreateEmptyFrame
        /// <summary>
        /// 创建空白的颜色（黑色 0x000000）的数据帧（填充数据数量为 <see cref="LedCount"/>，复制数据次数为 1）
        /// <para>只是创建数据帧，并未进入待渲染的帧队列中</para>
        /// </summary>
        /// <returns></returns>
        internal byte[] CreateEmptyFrame() => CreateEmptyFrame(this.LedCount, 1);
        /// <summary>
        /// 创建空白的颜色（黑色 0x000000）的数据帧（跟据需要填充数据的灯珠数量 <paramref name="fillCount"/> 和 复制数据的次数 <paramref name="repeatCount"/> 的参数创建空白帧）
        /// <para>只是创建数据帧，并未进入待渲染的帧队列中</para>
        /// </summary>
        /// <param name="fillCount">需要填充数据的灯珠数量，不得小于 1 </param>
        /// <param name="repeatCount">需要将填充数据复制的次数，不得小于 1 </param>
        /// <returns></returns>
        /// <exception cref="ArgumentException"></exception>
        internal byte[] CreateEmptyFrame(int fillCount, int repeatCount)
        {
            var ledCount = this.LedCount;

            if (fillCount <= 0) fillCount = 1;
            if (fillCount > ledCount) fillCount = ledCount;

            //if (fillCount <= 0 || fillCount > ledCount)
            //    throw new ArgumentException($"数据填充的灯珠数量 {fillCount} 不能小于 1 或超过灯珠数量 {ledCount} 范围");

            if (repeatCount <= 0) repeatCount = 1;
            if (repeatCount > ledCount) repeatCount = ledCount;

            //if (repeatCount <= 0 || repeatCount > ledCount)
            //    throw new ArgumentException($"数据复制的次数 {repeatCount} 不能小于 1 或超过灯珠数量 {ledCount} 范围");

            int rrCount = fillCount * repeatCount;
            if (rrCount > ledCount) repeatCount = (int)Math.Ceiling(ledCount / (double)fillCount);

            var colorSize = fillCount * ColorFormat.GetChannelCount();
            var frameSize = colorSize + 18;

            byte[] frame = null;
            while (_framePool.TryDequeue(out frame))
            {
                if (frame.Length != frameSize)
                {
                    frame = null;
                    continue;
                }
                break;
            }
            if (frame == null)
            {
                frame = new byte[frameSize];
                //Debug.WriteLine($"new frame size:{frameSize} {Address}_{Port}");
            }

            //Trace.WriteLine($"Led {Address},{Port} LedCount:{ledCount}/{LedCount} FillCount:{fillCount}/{FillCount} RepeatCount:{repeatCount}/{RepeatCount} FrameSize:{frameSize}");

            frame[0] = 0xDD;
            frame[1] = 0x55;
            frame[2] = 0xEE;

            frame[3] = (byte)(Group >> 8);          // 组地址
            frame[4] = (byte)(Group & 0xFF);

            frame[5] = (byte)(Address >> 8);        // 设备地址
            frame[6] = (byte)(Address & 0xFF);

            frame[7] = Port;                        // 端口号

            frame[8] = 0x99;                        // 功能码
            frame[9] = (byte)LedType;               // 灯带类型

            frame[10] = (byte)(Reserved >> 8);      // 保留字节
            frame[11] = (byte)(Reserved & 0xFF);

            frame[12] = (byte)(colorSize >> 8);     // 数据长度
            frame[13] = (byte)(colorSize & 0xFF);

            frame[14] = (byte)(repeatCount >> 8);    // 扩展次数
            frame[15] = (byte)(repeatCount & 0xFF);

            frame[frame.Length - 2] = 0xAA;
            frame[frame.Length - 1] = 0xBB;

            return frame;
        }
        #endregion


        /// <summary>
        /// 清空待渲染的帧队列
        /// </summary>
        /// <param name="off">是否关闭所有点亮的灯珠</param>
        public void ClearFrames(bool off = false)
        {
            while (!_frameQueue.IsEmpty && _frameQueue.TryDequeue(out var frame))
            {
                ;
            }

            _lastFrame = Array.Empty<byte>();

            if (off && this.LedCount > 0)
            {
                var frame = CreateEmptyFrame(1, this.LedCount);
                if (ColorFormat.GetChannelCount() == 4)
                {
                    var indices = ColorFormat.GetChannelIndices();
                    for (int i = 0; i < indices.Length; i++)
                    {
                        if (indices[i] == (byte)ColorChannel.A)
                            frame[FrameHeaderLength + i] = 0xFF;
                    }
                }
                _frameQueue.Enqueue(frame);
            }
        }

        /// <summary>
        /// 检查当前帧队列长度，如果超出设定的长度，则将开头的帧转移到帧资源池中
        /// </summary>
        private void CheckFrameQueueCount()
        {
            if (_frameQueue.Count <= QueueMaxCount) return;
            if (!_frameQueue.TryPeek(out var frame)) return;

            if (frame[8] != 0x99) return;
            if (_frameQueue.TryDequeue(out var _frame))
            {
                ReturnFramePool(_frame);
            }
        }       

        private void ReturnFramePool(byte[] frame)
        {
            if (frame == null || frame.Length == 0) return;
            _framePool.Enqueue(frame);

            while (_framePool.Count > FramePoolMaxCount)
                _framePool.TryDequeue(out _);
        }

        /// <summary>
        /// 尝试从帧队列中获取待渲染的帧
        /// <para>如果有待渲染的帧，则返回 true, 并将帧数据写入 frame 参数，否则返回 false</para>
        /// <para>如果返回 false, 并不一定代表队列中是空的，也有可能是因为与上一帧数据相同，则返回 false，不重复渲染相同的数据帧</para>
        /// </summary>
        /// <param name="frame"></param>
        /// <returns></returns>
        internal bool TryGetFrame(out byte[] frame)
        {
            frame = null;
            if (_frameQueue.IsEmpty) return false;

            CheckFrameQueueCount();

            if (_frameQueue.TryDequeue(out byte[] _frame))
            {
                if (ArrayExtensions.SequenceEqual(_lastFrame, _frame))
                {
                    _renderCount ++;
                    if (_renderCount % 10 == 0)
                    {
                        ReturnFramePool(_lastFrame);

                        frame = _frame;
                        _lastFrame = _frame;
                        Interlocked.Increment(ref _renderFps);
                        //Debug.WriteLine($"重复渲染相同的数据帧：{_renderCount}  Pool:{_framePool.Count}");
                        return true;
                    }

                    ReturnFramePool(_frame);

                    if (_renderCount >= int.MaxValue) 
                        _renderCount = 0;

                    //Debug.WriteLine($"不在重复渲染相同的数据帧：{_renderCount}  Pool:{_framePool.Count}");
                    return false;
                }

                _renderCount = 0;
                ReturnFramePool(_lastFrame);

                frame = _frame;
                _lastFrame = _frame;
                Interlocked.Increment(ref _renderFps);
                return true;
            }

            return false;
        }

        /// <summary>
        /// 重置渲染帧率计数器
        /// </summary>
        internal void ResetRenderFps()
        {
            Fps = Interlocked.Exchange(ref _renderFps, 0);
            //if (Fps <= 0) _lastFrame = Array.Empty<byte>();
        }
        /// <summary>
        /// 重置上一次渲染的帧数据
        /// </summary>
        internal void ResetLastFrame() => _lastFrame = Array.Empty<byte>();

        /// <inheritdoc/>
        public override string ToString()
        {
            return $"[{nameof(LedStripObject)}] Address:{Address} Port:{Port} Count:{LedCount}";
        }

        /// <summary>
        /// 试图创建 <see cref="LedStripObject"/> 对象的实例
        /// </summary>
        /// <param name="element"></param>
        /// <param name="ledStrip"></param>
        /// <returns></returns>
        public static bool TryCreateInstance(XElement element, out LedStripObject ledStrip)
        {
            ledStrip = null;
            if (element == null || element.Name != nameof(LedStripObject))
            {
                Trace.TraceWarning($"{nameof(LedStripObject)} 配置节点不存在或名称不正确");
                return false;
            }

            ledStrip = new LedStripObject();
            ledStrip.LedType = Enum.TryParse(element.Attribute(nameof(LedType))?.Value, true, out LedType ledType) ? ledType : LedType.WS2812B;
            ledStrip.ColorFormat = Enum.TryParse(element.Attribute(nameof(ColorFormat))?.Value, true, out ColorFormat colorFormat) ? colorFormat : ColorFormat.RGB;

            string pointsString = element.Attribute(nameof(LedPoints))?.Value;
            if (!string.IsNullOrWhiteSpace(pointsString))
            {
                if (DrawingExtensions.TryParsePoints(pointsString, out var _points))
                {
                    ledStrip.AddPoints(_points);
                }
            }
            foreach (var pointElement in element.Elements(nameof(LedPoints)))
            {
                if (DrawingExtensions.TryParsePoints(pointElement.Value, out var _points))
                {
                    ledStrip.AddPoints(_points);
                }
            }

            ledStrip.Comment = element.Attribute(nameof(Comment))?.Value;

            ledStrip.Port = byte.TryParse(element.Attribute(nameof(Port))?.Value, out byte port) ? port : (byte)0;
            ledStrip.Group = ushort.TryParse(element.Attribute(nameof(Group))?.Value, out ushort group) ? group : (ushort)0;
            ledStrip.Address = ushort.TryParse(element.Attribute(nameof(Address))?.Value, out ushort address) ? address : (ushort)0;
            ledStrip.Reserved = ushort.TryParse(element.Attribute(nameof(Reserved))?.Value, out ushort reserved) ? reserved : (ushort)0;

            ledStrip.Timeout = int.TryParse(element.Attribute(nameof(Timeout))?.Value, out int timeout) ? timeout : 0;

            ledStrip.FillCount = int.TryParse(element.Attribute(nameof(FillCount))?.Value, out int fill) ? fill : 0;
            ledStrip.RepeatCount = int.TryParse(element.Attribute(nameof(RepeatCount))?.Value, out int repeat) ? repeat : 1;

            ledStrip.QueueMaxCount = int.TryParse(element.Attribute(nameof(QueueMaxCount))?.Value, out int queueMaxCount) ? queueMaxCount : DefaultQueueMaxCount;
            ledStrip.UseBitmapPixels = bool.TryParse(element.Attribute(nameof(UseBitmapPixels))?.Value, out bool useBitmapPixels) ? useBitmapPixels : true;

            return true;
        }

    }
}
