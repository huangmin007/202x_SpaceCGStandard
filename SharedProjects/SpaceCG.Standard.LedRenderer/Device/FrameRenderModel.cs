using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using SpaceCG.Extensions;
using Trace = SpaceCG.Diagnostics.Trace;

namespace SpaceCG.Device
{
    /// <summary>
    /// 数据帧渲染模型抽象基类，管理数据帧的渲染队列及重复帧去重。
    /// </summary>
    /// <remarks>
    /// <para><b>架构说明：</b></para>
    /// <para>  一路渲染总线(<see cref="LedRenderBus"/>)可接多个控制器，每个控制器可接多路灯带(<see cref="LedStripObject"/>)，每路灯带独立渲染。</para>
    /// <para>  控制器和灯带均继承自本类，共享相同的帧队列管理和去重逻辑。</para>
    /// <para><b>数据帧协议结构：</b></para>
    /// <para>  [0-2] 帧头 (0xDD 0x55 0xEE)</para>
    /// <para>  [3-4] 组地址 (ushort, Big-Endian)，值范围：0~1024； </para>
    /// <para>  [5-6] 设备地址 (ushort, Big-Endian)，值范围：0~4096；当值为 0 时，表示总线上的所有设备 </para>
    /// <para>  [7]   端口号，值范围：0~30；当值为 0 时，表示当前设备上的所有端口号 </para>
    /// <para>  [8]   功能码 (0x98/0x99=颜色帧, 0x9B=上电显示, 0x9C=关闭上电显示...)</para>
    /// <para>  [9]   灯带类型</para>
    /// <para>  [10-11] 保留字节 (ushort, Big-Endian)；IC 起始位置(1~768/1024)，1 表示从第一颗灯珠开始点亮。</para>
    /// <para>  [12-13] 颜色数据长度 (ushort, Big-Endian)，值范围：3~3072，须与帧总长度一致 (dataLength + 18 == frame.Length) </para>
    /// <para>  [14-15] 扩展/重复次数 (ushort, Big-Endian)，值范围：0~1024 </para>
    /// <para>  [16..^2] 颜色数据</para>
    /// <para>  [^2-^1] 帧尾 (0xAA 0xBB)</para>
    /// <para><b>数据帧长度约束：</b></para>
    /// <para>  RGB 最小帧长: 16(帧头) + 2(帧尾) + 3(RGB最小数据) = 21 字节；最大长度：16(帧头) + 2(帧尾) + 3072(1024*3 RGB最大数据) = 3099 字节 </para>
    /// <para>  WRGB 最小帧长: 16(帧头) + 2(帧尾) + 4(WRGB最小数据) = 22 字节；最大长度：16(帧头) + 2(帧尾) + 3072(768*4 WRGB最大数据) = 3099 字节 </para>
    /// <para><b>线程安全：</b></para>
    /// <para>  渲染状态（<c>_renderingCount</c>、<c>_lastRenderingFrame</c>、<c>_renderingRepeatCount</c>）通过 <c>_renderingLock</c> 同步。</para>
    /// <para>  渲染队列使用 <see cref="ConcurrentQueue{T}"/> 保证线程安全。</para>
    /// <para>  灯带属性（Group、Address、Port 等）在初始化后不变，无额外同步。</para>
    /// </remarks>
    public abstract class FrameRenderModel
    {
        #region const 常量定义
        /// <summary>  RGB 灯珠支持的最大 Led 灯珠数量  </summary>
        protected internal const ushort MaxRGBLedCount = 1024;
        /// <summary>  RGBW 灯珠支持的最大 Led 灯珠数量  </summary>
        protected internal const ushort MaxWRGBLedCount = 768;

        /// <summary>  帧尾字节数  </summary>
        protected internal const int FrameFooterLength = 2;
        /// <summary>  帧头字节数  </summary>
        protected internal const int FrameHeaderLength = 16;
        /// <summary> 数据帧的最小字节长度(RGB)  </summary>
        protected internal const int RGBFrameBaseLength = FrameHeaderLength + FrameFooterLength + 3;
        /// <summary> 数据帧的最小字节长度(WRGB)  </summary>
        protected internal const int WRGBFrameBaseLength = FrameHeaderLength + FrameFooterLength + 4;

        /// <summary> 渲染队列最大容量 </summary>
        protected internal const int MaxRenderingFrameCount = 3;

#if false
        protected internal const int OffsetGroup = 3;
        protected internal const int OffsetAddress = 5;
        protected internal const int OffsetPort = 7;
        protected internal const int OffsetFunction = 8;
        protected internal const int OffsetLedType = 9;
        protected internal const int OffsetLength = 12;
        protected internal const int OffsetRepeat = 14;
        protected internal const int OffsetColor = 16;
#endif
        #endregion

        #region 设备单路灯带的相关属性        
        /// <summary>
        /// 组地址
        /// </summary>
        public ushort Group { get; set; } = 0;
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
        public LedType LedType { get;  private set; } = LedType.WS2812B;
        /// <summary>
        /// Led 灯带的颜色格式
        /// </summary>
        public ColorFormat ColorFormat { get; private set; } = ColorFormat.RGB;
        /// <summary>
        /// Led 灯带的灯珠数量，或是总线上最长的灯带灯珠数量
        /// </summary>
        public int LedCount { get; protected set; } = 0;
        /// <summary>
        /// 当前设备或灯带单路支持的最大 Led 灯珠数量。
        /// </summary>
        /// <remarks>
        /// 根据 <see cref="ColorFormat"/> 在构造函数中自动确定：三通道(RGB) 为 <see cref="MaxRGBLedCount"/> = 1024，四通道(WRGB) 为 <see cref="MaxWRGBLedCount"/> = 768。
        /// </remarks>
        protected ushort CurrentMaxLedCount { get; private set; } = MaxRGBLedCount;
        #endregion

        #region 渲染相关属性
        /// <summary>
        /// 渲染状态同步锁，保护 <see cref="_lastRenderingFrame"/>、<see cref="_renderingRepeatCount"/>、<see cref="_renderingCount"/> 的并发访问。
        /// </summary>
        private readonly object _renderingLock = new object();
        /// <summary>
        /// 渲染帧队列，存放待渲染管线消费的数据帧。
        /// </summary>
        /// <remarks>使用 <see cref="ConcurrentQueue{T}"/> 保证线程安全的入队/出队操作。</remarks>
        //private readonly Queue<byte[]> _renderingFrames = new Queue<byte[]>(16);
        private readonly ConcurrentQueue<byte[]> _renderingFrames = new ConcurrentQueue<byte[]>();
        
        /// <summary>
        /// 自上次 <see cref="ResetRenderingState"/> 调用以来已渲染的帧累计数量，用于计算 FPS。
        /// </summary>
        /// <remarks>受 <c>_renderingLock</c> 保护。</remarks>
        private int _renderingCount = 0;
        /// <summary>
        /// 连续相同数据帧的累计计数。当帧内容与 <see cref="_lastRenderingFrame"/> 相同时递增，
        /// 达到 <see cref="RenderingRepeatInterval"/> 的整数倍时才实际渲染一次，其余跳过以降低无效渲染开销。
        /// </summary>
        /// <remarks>受 <c>_renderingLock</c> 保护。</remarks>
        private int _renderingRepeatCount = 0;
        /// <summary>
        /// 上一次实际渲染的数据帧引用，用于与当前帧做内容比较以检测连续相同帧。
        /// </summary>
        /// <remarks>受 <c>_renderingLock</c> 保护。初始值为 <see cref="Array.Empty{T}"/>，确保首帧必然渲染。</remarks>
        private byte[] _lastRenderingFrame = Array.Empty<byte>();
        /// <summary>
        /// 连续相同帧的渲染间隔。当连续相同帧累计次数为该值的整数倍时，才实际渲染一次。
        /// </summary>
        /// <remarks>
        /// <para>设置为 0 或负数时禁用去重优化，每帧都渲染。</para>
        /// <para>子类可在初始化时按需调整此值。</para>
        /// </remarks>
        public int RenderingRepeatInterval { get; protected set; } = 8;
        /// <summary>
        /// 获取当前渲染队列中待处理渲染的帧数量。
        /// </summary>
        /// <remarks>
        /// <para>该值为 <see cref="_renderingFrames"/> 队列的实时快照，包含所有待渲染帧（含颜色帧和非颜色帧）。</para>
        /// <para>返回值范围：0 ~ 无上限（受 <see cref="EnqueueFrame"/> 的溢出策略控制，颜色帧通常不超过 <see cref="MaxRenderingFrameCount"/> = 3）。</para>
        /// <para><b>线程安全：</b><see cref="ConcurrentQueue{T}.Count"/> 为快照值，在并发入队/出队时可能已过期，仅作近似参考。</para>
        /// </remarks>
        public int PendingFrameCount => _renderingFrames.Count;
        /// <summary> 
        /// 当前渲染帧率（帧/秒）。 
        /// </summary>
        public int Fps { get; internal set; } = 0;

        /// <summary>
        /// 渲染一帧数据后，需要等待的时间，单位：毫秒，默认为 0 毫秒
        /// <para>有些帧数据写入后需要等待设备处理，可以此参数让其线程等待几毫秒，来避免设备处理帧异常的问题</para>
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
        /// 获取或设置是否允许渲染当前设备或灯带的数据帧。
        /// </summary>
        /// <remarks>
        /// <para>设置为 <c>false</c> 时，渲染管线将跳过此设备或灯带，不发送任何数据帧。</para>
        /// <para>可用于实现渲染的暂停/恢复控制，或按条件禁用特定灯带。</para>
        /// <para>默认值：<c>true</c>。</para>
        /// </remarks>
        public bool IsRenderEnabled { get; set; } = true;
        #endregion

        #region 其它属性
        /// <summary> 获取或设置一个用于存储有关此元素的自定义信息的任意对象值。 </summary>
        public object Tag { get; set; }
        /// <summary> 备注信息，用于标识总线的用途或其他信息 </summary>
        public string Comment { get; set; } = string.Empty;
        #endregion

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="address"></param>
        /// <param name="port"></param>
        /// <param name="ledType"></param>
        /// <param name="ledColorFormat"></param>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        public FrameRenderModel(ushort address, byte port, LedType ledType, ColorFormat ledColorFormat)
        {
            if (address > 4096) throw new ArgumentOutOfRangeException(nameof(address), address, "地址值必须在 0 ~ 4096 范围内");
            if (port > 6) throw new ArgumentOutOfRangeException(nameof(port), port, "端口号必须在 0 ~ 6 范围内");

            this.Port = port;
            this.Address = address;

            this.LedType = ledType;
            this.ColorFormat = ledColorFormat;

            this.CurrentMaxLedCount = ColorFormat.GetChannelCount() == 3 ? MaxRGBLedCount : MaxWRGBLedCount;
        }

        #region CreateEmptyFrame/AddFrame 系列方法
        /// <summary>
        /// 添加一个通用数据帧到渲染队列（不限定帧类型，如上电显示指令等非颜色帧）。仅校验帧头/帧尾格式、端口号和设备地址。
        /// </summary>
        /// <param name="frame">待入队的完整数据帧。</param>
        /// <exception cref="ArgumentException">帧格式错误或地址/端口不匹配。</exception>
        /// <remarks>与 <see cref="AddColorFrame(byte[])"/> 不同，本方法不校验功能码和灯带类型，也不修正 Group/Reserved/Repeat 字段。</remarks>
        public void AddFrame(byte[] frame)
        {
            if (frame == null || frame.Length < RGBFrameBaseLength ||
                frame[0] != 0xDD || frame[1] != 0x55 || frame[2] != 0xEE ||
                frame[frame.Length - 2] != 0xAA || frame[frame.Length - 1] != 0xBB)
                throw new ArgumentException("数据帧格式错误", nameof(frame));

            if (Port != frame[7]) throw new ArgumentException("数据帧的端口号不匹配");
            if (Address != ((frame[5] << 8) | frame[6])) throw new ArgumentException("数据帧的设备地址不匹配");

            EnqueueFrame(frame);
        }
        #endregion

        #region CreateColorFrame/AddColorFrame 系列方法        
        /// <summary>
        /// 创建一个颜色数据帧，并填充帧头、帧尾及协议字段（除颜色数据外）。
        /// </summary>
        /// <param name="fromPosition">点亮灯珠 IC 的起始位置，可取值范围：[0, <see cref="LedCount"/>]。 小于 1 时功能码为 0x99，大于 1 时功能码为 0x98 且写入 [10-11] 字节。</param>
        /// <param name="fillCount">填充的灯珠数量（颜色数据中的像素数），会被钳制到 [1, <see cref="LedCount"/>] 范围。</param>
        /// <param name="repeatCount">颜色数据的扩展/重复次数，会被钳制到 [1, <see cref="LedCount"/>] 范围。</param>
        /// <returns>已填充协议头尾字段的帧缓冲区，颜色数据区域为全零。</returns>
        /// <exception cref="InvalidOperationException"><see cref="LedCount"/> 未初始化（≤0）。</exception>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        protected internal byte[] CreateEmptyColorFrame(int fromPosition, int fillCount, int repeatCount)
        {
            var ledCount = LedCount;
            if (ledCount <= 0 || ledCount > CurrentMaxLedCount)
                throw new InvalidOperationException($"灯珠数量必须在 0 ~ {CurrentMaxLedCount} 之间");
            if (fromPosition > ledCount)
                throw new ArgumentOutOfRangeException(nameof(fromPosition), fromPosition, $"IC 显示索引偏移位置必须在 0 ~ {ledCount} 之间");

            if (fromPosition < 0) fromPosition = 0;

            if (fillCount <= 0) fillCount = 1;
            if (fillCount > ledCount) fillCount = ledCount;

            if (repeatCount <= 0) repeatCount = 1;
            if (repeatCount > ledCount) repeatCount = ledCount;

            // 实际需要点亮的灯珠数量
            int targetFillCount = ledCount - fromPosition;
            // 优化计算，如果 填充*重复次数 的总灯珠数量 大于实际需要点亮的数量，则优化重复次数和填充数量
            int repeatFillCount = fillCount * repeatCount;
            if (repeatFillCount > targetFillCount && repeatCount > 1)
            {
                repeatCount = (int)Math.Ceiling(targetFillCount / (double)fillCount);
            }

            // 颜色数据占用的字节长度
            var colorSize = fillCount * ColorFormat.GetChannelCount();
            // 当前帧的字节总长度
            var frameSize = colorSize + FrameHeaderLength + FrameFooterLength;

            byte[] frame = new byte[frameSize];

            frame[0] = 0xDD;
            frame[1] = 0x55;
            frame[2] = 0xEE;

            frame[3] = (byte)(Group >> 8);                  // 组地址
            frame[4] = (byte)(Group & 0xFF);

            frame[5] = (byte)(Address >> 8);                // 设备地址
            frame[6] = (byte)(Address & 0xFF);

            frame[7] = Port;                                // 端口号

            frame[8] = (byte)(fromPosition > 1 ? 0x98 : 0x99);    // 功能码
            frame[9] = (byte)LedType;                       // 灯带类型

            if (fromPosition > 1)
            {
                frame[10] = (byte)(fromPosition >> 8);      // IC 起始索引偏移
                frame[11] = (byte)(fromPosition & 0xFF);
            }
            else
            {
                frame[10] = (byte)(Reserved >> 8);          // 保留字段
                frame[11] = (byte)(Reserved & 0xFF);
            }

            frame[12] = (byte)(colorSize >> 8);             // 数据长度
            frame[13] = (byte)(colorSize & 0xFF);

            frame[14] = (byte)(repeatCount >> 8);           // 扩展次数
            frame[15] = (byte)(repeatCount & 0xFF);

            frame[frame.Length - 2] = 0xAA;
            frame[frame.Length - 1] = 0xBB;

            return frame;
        }
        /// <inheritdoc cref="CreateEmptyColorFrame(int, int, int)"/>
        protected internal byte[] CreateEmptyColorFrame(int fillCount, int repeatCount) => CreateEmptyColorFrame(Reserved, fillCount, repeatCount);

        /// <summary>
        /// 添加待渲染的颜色数据帧。额外校验功能码(0x98/0x99)、灯带类型、颜色数据长度，并自动修正 Group、Reserved、Repeat 字段。
        /// </summary>
        /// <param name="frame">待入队的完整颜色数据帧。</param>
        /// <exception cref="ArgumentException">帧格式、功能码、端口、类型、地址、颜色长度不匹配。</exception>
        public void AddColorFrame(byte[] frame)
        {
            if (frame == null || frame.Length < RGBFrameBaseLength ||
                frame[0] != 0xDD || frame[1] != 0x55 || frame[2] != 0xEE ||
                frame[frame.Length - 2] != 0xAA || frame[frame.Length - 1] != 0xBB)
                throw new ArgumentException("数据帧格式错误", nameof(frame));

            if (frame[8] != 0x98 && frame[8] != 0x99) throw new ArgumentException("数据帧的功能码不正确");

            if (Port != frame[7]) throw new ArgumentException("数据帧的端口号不匹配");
            if (LedType != (LedType)frame[9]) throw new ArgumentException("数据帧的灯带类型不匹配");
            if (Address != ((frame[5] << 8) | frame[6])) throw new ArgumentException("数据帧的设备地址不匹配");

            var ledCount = LedCount;
            var repeat = ((frame[14] << 8) | frame[15]);    // GetRepeatCount(frame);
            if (repeat < 1) repeat = 1;
            if (repeat > ledCount) repeat = ledCount;

            int colorChannelCount = ColorFormat.GetChannelCount();

            var colorSize = frame.Length - 18;              //颜色字节的数据长度
            if (colorSize % colorChannelCount != 0)
                throw new ArgumentException($"数据帧的颜色字节长度 {colorSize} 不正确，与灯带的颜色格式 {ColorFormat} 不匹配");

            // 重新计算扩展次数，并修正
            var fillCount = colorSize / colorChannelCount;      //填充的数据量
            if (fillCount * repeat > ledCount) repeat = (int)Math.Ceiling(ledCount / (float)fillCount);

            frame[3] = (byte)(Group >> 8);    // 组地址
            frame[4] = (byte)(Group & 0xFF);

            frame[10] = (byte)(Reserved >> 8);    // 保留字节
            frame[11] = (byte)(Reserved & 0xFF);

            frame[14] = (byte)(repeat >> 8);    // 扩展次数
            frame[15] = (byte)(repeat & 0xFF);

            EnqueueFrame(frame);
        }
        /// <summary>
        /// 添加待渲染的颜色数据帧。
        /// </summary>
        /// <param name="r"></param>
        /// <param name="g"></param>
        /// <param name="b"></param>
        /// <param name="start">点亮灯珠 IC 的起始位置。值范围：[0, <see cref="LedCount"/>]。</param>
        /// <param name="repeat">颜色数据重复次数。至少重复次数为 1 ，不能超过灯珠数量。</param>
        public void AddColorFrame(byte r, byte g, byte b, int start, int repeat) => AddColorFrame((uint)(0xFF << 24 | r << 16 | g << 8 | b), start, repeat, ColorFormat.ARGB);
        /// <summary>
        /// 添加待渲染的颜色数据帧。
        /// <para>输入颜色值 (<see cref="uint"/>类型) 数组 <paramref name="color"/> 颜色通道 <paramref name="colorFormat"/> 必须是 <b>四通道</b> 类型</para>
        /// </summary>
        /// <param name="color">颜色值数据，需要指定颜色通道格式 <paramref name="colorFormat"/></param>
        /// <param name="start">点亮灯珠 IC 的起始位置。值范围：[0, <see cref="LedCount"/>]。</param>
        /// <param name="repeat">颜色数据重复次数。至少重复次数为 1 ，不能超过灯珠数量。</param>
        /// <param name="colorFormat"><paramref name="color"/> 数据的颜色值格式</param>
        /// <exception cref="ArgumentException"></exception>
        public void AddColorFrame(uint color, int start, int repeat, ColorFormat colorFormat = ColorFormat.ARGB)
        {
            var ledCount = LedCount;
            if (repeat <= 0 || repeat > ledCount)
                throw new ArgumentException($"参数 repeat 不能为 0 或超过灯珠数量 {ledCount} 范围");

            // 通道索引表
            var inputIndices = colorFormat.GetChannelIndices();
            var outputIndices = ColorFormat.GetChannelIndices();

            // 颜色的通道数量
            int inputChannelCount = inputIndices.Count;
            int outputChannelCount = outputIndices.Count;

            if (inputChannelCount != 4)
                throw new ArgumentException("输入颜色值 (uint类型) 的通道数量必须为 4 ", nameof(colorFormat));

            int index = 0, outputOffset = 0;
            var frame = CreateEmptyColorFrame(start, 1, repeat);

            for (var j = 0; j < outputChannelCount; j++)
            {
                outputOffset = j + FrameHeaderLength;
                index = inputIndices.IndexOf(outputIndices[j]);
                frame[outputOffset] = (byte)((color >> (24 - index * 8)) & 0xFF);
            }

            EnqueueFrame(frame);
        }
        
        /// <inheritdoc cref="AddColorFrame(IReadOnlyList{uint}, int, int, ColorFormat)"/>
        public void AddColorFrame(IReadOnlyList<byte> colors, int startPos, int repeat, ColorFormat colorFormat = ColorFormat.RGB)
        {
            var ledCount = LedCount;
            if (colors == null || colors.Count == 0)
                throw new ArgumentException("颜色值数组不能为空，或长度不正确");
            if (repeat <= 0 || repeat > ledCount)
                throw new ArgumentException($"参数 repeat 不能为 0 或超过灯珠数量 {ledCount} 范围");

            // 通道索引表
            var inputIndices = colorFormat.GetChannelIndices();
            var outputIndices = ColorFormat.GetChannelIndices();

            // 颜值的通道数量
            int inputChannelCount = inputIndices.Count;
            int outputChannelCount = outputIndices.Count;

            if (colors.Count < inputChannelCount || colors.Count % inputChannelCount != 0)
                throw new ArgumentException($"颜色数据长度 {colors.Count} 与通道数 {inputChannelCount} 不匹配", nameof(colors));

            var renderCount = colors.Count / inputChannelCount;
            var frame = CreateEmptyColorFrame(startPos, Math.Min(renderCount, ledCount), repeat);
            var fillCount = (frame.Length - FrameHeaderLength - FrameFooterLength) / outputChannelCount;

            if (colorFormat == ColorFormat && colors is Array colorArray)
            {
                var renderSize = fillCount * outputChannelCount;
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
                    channelMap[i] = inputIndices.IndexOf(outputIndices[i]);
                }

                for (i = 0; i < fillCount; i++)
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

            EnqueueFrame(frame);
        }
        /// <summary>
        /// 添加待渲染的颜色数据帧。跟据 <paramref name="colors"/> 数据量 和 <paramref name="repeat"/> 填充灯珠。
        /// </summary>
        /// <param name="colors">颜色值数组，需要指定颜色通道格式 <paramref name="colorFormat"/></param>
        /// <param name="start">点亮灯珠 IC 的起始位置。值范围：[0, <see cref="LedCount"/>]。</param>
        /// <param name="repeat">颜色数据重复次数。至少重复次数为 1 ，不能超过灯珠数量。</param>
        /// <param name="colorFormat"><paramref name="colors"/> 数据的颜色值格式</param>
        /// <exception cref="ArgumentException"></exception>
        public void AddColorFrame(IReadOnlyList<uint> colors, int start, int repeat, ColorFormat colorFormat = ColorFormat.ARGB)
        {
            var ledCount = LedCount;
            if (colors == null || colors.Count == 0)
                throw new ArgumentException("颜色值数组不能为空");
            if (repeat <= 0 || repeat > ledCount)
                throw new ArgumentException($"参数 repeat 不能为 0 或超过灯珠数量 {ledCount} 范围");

            // 通道索引表
            var inputIndices = colorFormat.GetChannelIndices();
            var outputIndices = ColorFormat.GetChannelIndices();

            // 颜色的通道数量
            int inputChannelCount = inputIndices.Count;
            int outputChannelCount = outputIndices.Count;

            if (inputChannelCount != 4)
                throw new ArgumentException("输入颜色值 (uint类型) 的通道数量必须为 4 ", nameof(colorFormat));

            int i = 0, j = 0, index = -1, outputOffset = 0;
            var frame = CreateEmptyColorFrame(start, Math.Min(colors.Count, ledCount), repeat);
            var fillCount = (frame.Length - FrameHeaderLength - FrameFooterLength) / outputChannelCount;

            // 预计算通道索引映射
            int[] channelMap = new int[outputChannelCount];
            for (i = 0; i < outputChannelCount; i++)
            {
                channelMap[i] = inputIndices.IndexOf(outputIndices[i]);
            }

            for (i = 0; i < fillCount; i++)
            {
                outputOffset = i * outputChannelCount + FrameHeaderLength;

                for (j = 0; j < outputChannelCount; j++)
                {
                    index = channelMap[j];
                    frame[outputOffset + j] = (byte)((colors[i] >> (24 - index * 8)) & 0xFF);
                }
            }

            EnqueueFrame(frame);
        }
        #endregion

        #region 待渲染的帧池 系列方法
        /// <summary>
        /// 将待渲染的数据帧加入渲染队列。当暂停渲染时，只保留最新的数据帧(不保护非颜色数据帧)。
        /// </summary>
        /// <remarks>
        /// <para>访问级别为 <c>protected internal</c>，允许同一程序集内的渲染总线或外部组件直接入队帧数据。</para>
        /// <para><b>溢出策略：</b>当队列中颜色帧数量超过 <see cref="MaxRenderingFrameCount"/> 时，
        /// 丢弃队首最旧的颜色帧（直接丢弃，由 GC 回收），确保新帧能及时渲染。</para>
        /// <para>非颜色帧（功能码 ≠ 0x98 且 ≠ 0x99，如上电显示指令）不参与溢出清理，始终保留。</para>
        /// </remarks>
        /// <param name="frame">待入队的数据帧。<c>null</c> 或长度不足的帧会被忽略。</param>
        protected internal void EnqueueFrame(byte[] frame)
        {
            if (frame == null || frame.Length < RGBFrameBaseLength) return;

            _renderingFrames.Enqueue(frame);

            // 正常渲染时，保留非颜色帧（如指令帧）
            if (IsRenderEnabled)
            {
                var isColorFrame = (frame[8] == 0x98 || frame[8] == 0x99);
                if (!isColorFrame) return;   // 如果当前帧不是颜色数据帧，则不需要检查溢出问题
            }

            while (_renderingFrames.Count > MaxRenderingFrameCount)
            {
                // 正常渲染时，保留非颜色帧（如指令帧）
                // 暂停渲染时，所有帧都可以丢弃
                if (IsRenderEnabled && _renderingFrames.TryPeek(out var _frame))
                {
                    var isColorFrame = (_frame[8] == 0x98 || _frame[8] == 0x99);
                    if (!isColorFrame) break;  // 非颜色数据帧，下次在检查
                }

                _renderingFrames.TryDequeue(out _); 
            }
        }
        /// <summary>
        /// 从渲染队列中取出下一个待渲染的数据帧。
        /// <para>如果有待渲染的帧，则返回 true, 并将帧数据写入 frame 参数，否则返回 false</para>
        /// <para>如果返回 false, 并不一定代表队列中是空的，也有可能是因为与上一帧数据相同，则返回 false，不重复渲染相同的数据帧，或者减少相同数据帧的渲染次数</para>
        /// </summary>
        /// <param name="frame">输出参数，当返回 <c>true</c> 时包含待渲染的帧数据；返回 <c>false</c> 时为 <c>null</c>。</param>
        /// <returns>
        /// <c>true</c>：成功获取到待渲染帧（队列非空且满足渲染条件）；
        /// <c>false</c>：队列为空、获取失败、或帧与上一帧相同且未达到重复渲染间隔。
        /// </returns>
        internal bool TryDequeueFrame(out byte[] frame)
        {
            frame = null;
            if (_renderingFrames.IsEmpty) return false;
            if (!_renderingFrames.TryDequeue(out var _frame)) return false;

            lock (_renderingLock)
            {
                var repeatInterval = RenderingRepeatInterval;

                // 渲染的数据帧相同，则减少渲染次数
                if (repeatInterval > 0 && _lastRenderingFrame.FastSequenceEqual(_frame))
                {
                    _renderingRepeatCount++;
                    if (_renderingRepeatCount % repeatInterval != 0)
                    {
                        _frame = null;
                        return false;
                    }

                    frame = _frame;
                    _renderingCount++;
                    _lastRenderingFrame = _frame;
                    //Trace.WriteLine($"{DateTime.Now:hh:mm:ss.fff} 渲染相同的帧 {Address}_{Port}  {_renderingRepeatCount}");
                    return true;
                }

                frame = _frame;
                _renderingCount++;
                _renderingRepeatCount = 0;
                _lastRenderingFrame = _frame;
                //Trace.WriteLine($"{DateTime.Now:hh:mm:ss.fff} 渲染相同的帧222 {Address}_{Port}  {_renderingRepeatCount}");
                return true;
            }
        }
        /// <summary>
        /// 清空渲染队列并重置渲染状态（含计数器、重复帧检测状态）。
        /// </summary>
        /// <remarks>内部调用 <see cref="ResetRenderingState"/> 重置渲染状态。</remarks>
        public void ClearRenderingFrames()
        {
            ResetRenderingState();

            while (!_renderingFrames.IsEmpty)
            {
                _renderingFrames.TryDequeue(out _);
            }
        }
        /// <summary>
        /// 重置渲染状态：清零渲染计数器、重复帧计数器和上一帧引用。
        /// </summary>
        /// <remarks>
        /// <para>通常在灯带配置变更或清空渲染队列时调用。受 <c>_renderingLock</c> 保护。</para>
        /// </remarks>
        protected internal void ResetRenderingState()
        {
            lock (_renderingLock)
            {
                Fps = _renderingCount;

                _renderingCount = 0;
                _renderingRepeatCount = 0;
                _lastRenderingFrame = Array.Empty<byte>();
            }
        }
        #endregion

    }

}
