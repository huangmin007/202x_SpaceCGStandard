#define UseQueue    // 启用 Queue<T> + lock 模式

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using SpaceCG.Extensions;
using Trace = SpaceCG.Diagnostics.Trace;

namespace SpaceCG.Device
{
    /// <summary>
    /// 数据帧渲染模型抽象基类，提供帧队列管理、重复帧去重以及空闲帧池复用能力。
    /// </summary>
    /// <remarks>
    /// <para><b>架构说明：</b></para>
    /// <para>  一路渲染总线(<see cref="LedRenderBus"/>)可挂载多个控制器，每个控制器可接多路灯带(<see cref="LedStripObject"/>)，每路灯带独立渲染。</para>
    /// <para>  控制器和灯带均继承自本类，共享相同的帧队列管理和去重逻辑。</para>
    /// <para><b>数据帧协议结构：</b></para>
    /// <para>  [0-2] 帧头 (0xDD 0x55 0xEE)</para>
    /// <para>  [3-4] 组地址 (ushort, Big-Endian)，值范围：0~1024； </para>
    /// <para>  [5-6] 设备地址 (ushort, Big-Endian)，值范围：0~4096；当值为 0 时，表示总线上的所有设备 </para>
    /// <para>  [7]   端口号，值范围：0~6；当值为 0 时，表示当前设备上的所有端口号 </para>
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
    /// <para>  渲染状态（<c>_renderingCount</c>、<c>_lastRenderingFrame</c>、<c>_renderingRepeatCount</c>）通过 <c>_renderingStateLock</c> 同步。</para>
    /// <para>  渲染队列使用 <see cref="Queue{T}"/> + <c>lock</c> 或 <see cref="ConcurrentQueue{T}"/> 保证线程安全。</para>
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
        /// <summary> 数据帧的最大字节长度(字节) </summary>
        protected internal const int FrameMaxLength = FrameHeaderLength + FrameFooterLength + 3 * 1024;

        /// <summary> 渲染队列最大容量 </summary>
        protected internal const int MaxRenderingFrameCount = 3;
        /// <summary> 空闲帧池最大容量 </summary>
        protected internal const int MaxAvailableFrameCount = 8;

        /// <summary> 无响应帧发送后的默认等待时间，单位：毫秒  </summary>
        protected internal const int DefaultTimeout = 10;
        /// <summary> 默认设备响应超时时间，单位：毫秒 </summary>
        protected internal const int DefaultResponseTimeout = 300;

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
        /// <summary> 组地址，范围 [0, 1024]。 </summary>
        public ushort Group { get; set; } = 0;
        /// <summary> 设备地址，范围 [0, 4096]；0 表示广播。 </summary>
        public ushort Address { get; private set; } = 0x0001;
        /// <summary> 设备端口号，范围 [0, 6]；0 表示所有端口。 </summary>
        public byte Port { get; private set; } = 0x00;
        /// <summary> 保留数据字段，写入帧 [10-11] 字节。</summary>
        public ushort Reserved { get; set; } = 0x0000;
        /// <summary> 灯带芯片类型（如 WS2812B、SK6812 等）。 </summary>
        public LedType LedType { get;  private set; } = LedType.WS2812B;
        /// <summary> 灯带颜色格式（如 RGB、GRB、WRGB 等），决定每像素通道排列。 </summary>
        public ColorFormat ColorFormat { get; private set; } = ColorFormat.RGB;
        /// <summary>
        /// 灯珠数量。对于 <see cref="LedRenderBus"/>，表示总线上最长的灯带灯珠数量；
        /// 对于 <see cref="LedStripObject"/>，表示实际物理灯珠数量。
        /// </summary>
        public int LedCount { get; protected set; } = 0;
        /// <summary>
        /// 当前设备或灯带单路支持的最大灯珠数量。
        /// </summary>
        /// <remarks>
        /// 根据 <see cref="ColorFormat"/> 在构造时自动确定：
        /// 三通道（RGB）→ <see cref="MaxRGBLedCount"/> = 1024，
        /// 四通道（WRGB）→ <see cref="MaxWRGBLedCount"/> = 768。
        /// </remarks>
        public ushort CurrentMaxLedCount { get; private set; } = MaxRGBLedCount;
        #endregion

        #region 渲染相关属性
        /// <summary>
        /// 空闲数据帧池 — 用于复用 byte[] 缓冲区以减少 GC 分配。
        /// </summary>
#if UseQueue
        private readonly Queue<byte[]> _availableFrames = new Queue<byte[]>(16);
        private readonly object _availableFramesLock = new object();
#else
        private readonly ConcurrentQueue<byte[]> _availableFrames = new ConcurrentQueue<byte[]>();
#endif
        /// <summary>
        /// 待渲染帧队列，存放待渲染管线消费的数据帧。
        /// </summary>
#if UseQueue
        private readonly Queue<byte[]> _renderingFrames = new Queue<byte[]>(8);
        private readonly object _renderingFramesLock = new object();
#else
        private readonly ConcurrentQueue<byte[]> _renderingFrames = new ConcurrentQueue<byte[]>();
#endif
        /// <summary>
        /// 获取当前渲染队列中待处理的帧数量。
        /// </summary>
        /// <remarks>
        /// <para>受 <see cref="EnqueueFrame"/> 溢出策略控制，颜色帧通常不超过 <see cref="MaxRenderingFrameCount"/> = 3。</para>
        /// <para>Queue 模式下为精确值（O(1)）；ConcurrentQueue 模式下为快照值（O(n)），仅作近似参考。</para>
        /// </remarks>
#if UseQueue
        public int PendingFrameCount { get { lock (_renderingFramesLock) { return _renderingFrames.Count; } } }
#else
        public int PendingFrameCount => _renderingFrames.Count;
#endif

        /// <summary>
        /// 渲染状态同步锁，保护 <see cref="_lastRenderingFrame"/>、<see cref="_renderingRepeatCount"/>、<see cref="_renderingCount"/> 的并发读写。
        /// </summary>
        private readonly object _renderingStateLock = new object();
        /// <summary>
        /// 自上次 <see cref="ResetRenderingState"/> 调用以来已渲染的帧累计数量，可用于计算 FPS。
        /// </summary>
        /// <remarks>受 <c>_renderingStateLock</c> 保护。</remarks>
        private int _renderingCount = 0;
        /// <summary>
        /// 连续相同数据帧的累计计数。当帧内容与 <see cref="_lastRenderingFrame"/> 相同时递增，
        /// 达到 <see cref="RenderingRepeatInterval"/> 的整数倍时才实际渲染一次，其余跳过以降低无效渲染开销。
        /// </summary>
        /// <remarks>受 <c>_renderingStateLock</c> 保护。</remarks>
        private int _renderingRepeatCount = 0;
        /// <summary>
        /// 上一次实际渲染的数据帧引用，用于与当前帧做内容比较以检测连续相同帧。
        /// </summary>
        /// <remarks>受 <c>_renderingStateLock</c> 保护。初始值为 <see cref="Array.Empty{T}"/>，确保首帧必然渲染。</remarks>
        private byte[] _lastRenderingFrame = Array.Empty<byte>();
        /// <summary>
        /// 连续相同帧的渲染间隔。当连续相同帧累计次数为该值的整数倍时，才实际渲染一次。
        /// </summary>
        /// <remarks>
        /// <para>设置为小于 1 时禁用去重优化，每帧都渲染。</para>
        /// <para>子类可在初始化时按需调整此值。默认值：8（灯带）/ 0（总线）。</para>
        /// </remarks>
        public int RenderingRepeatInterval { get; protected set; } = 8;
        
        /// <summary> 
        /// 当前渲染帧率（帧/秒）。 
        /// </summary>
        public int Fps { get; internal set; } = 0;
        /// <summary>
        /// 无响应帧（例如：广播帧）发送后线程等待时间（ms），范围 [0, 1000]。
        /// </summary>
        /// <remarks>用于避免设备因连续写入而来不及处理帧数据。</remarks>
        /// <exception cref="ArgumentOutOfRangeException">值不在 [0, 1000] 范围内。</exception>
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
        private int _timeout = DefaultTimeout;
        #endregion

        #region 其它属性
        /// <summary> 自定义附加对象，可用于存储任意关联数据。 </summary>
        public object Tag { get; set; }
        /// <summary> 备注信息，用于标识总线用途或其他描述。 </summary>
        public string Comment { get; set; } = string.Empty;
        #endregion

        /// <summary>
        /// 初始化帧渲染模型实例。
        /// </summary>
        /// <param name="address">设备地址，范围 [0, 4096]。</param>
        /// <param name="port">设备端口号，范围 [0, 6]。</param>
        /// <param name="ledType">灯带芯片类型。</param>
        /// <param name="ledColorFormat">灯带颜色格式，决定通道数和最大灯珠数。</param>
        /// <exception cref="ArgumentOutOfRangeException">address > 4096 或 port > 6。</exception>
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

        #region CreateEmptyColorFrame
        /// <summary>
        /// 从空闲帧池租借（或新建）一个数据帧，并跟据 <b>当前对象的参数</b> 填充帧头、帧尾及部份协议字段。
        /// </summary>
        /// <param name="fromPosition">点亮灯珠 IC 的起始位置，有效值范围：[1, <see cref="LedCount"/>]。 小于 1 时功能码为 0x99，大于 1 时功能码为 0x98 且写入 [10-11] 字节。</param>
        /// <param name="fillCount">填充的灯珠数量（颜色数据中的像素数），会被钳制到 [1, <see cref="LedCount"/>] 范围。</param>
        /// <param name="repeatCount">颜色数据的扩展/重复次数，会被钳制到 [1, <see cref="LedCount"/>] 范围。当 fillCount × repeatCount > 实际需点亮数量时，自动缩减 repeatCount。</param>
        /// <returns>已填充协议头尾字段的数据帧。</returns>
        /// <exception cref="InvalidOperationException"><see cref="LedCount"/> 未初始化（≤0）。</exception>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        protected internal byte[] CreateEmptyColorFrame(int fromPosition, int fillCount, int repeatCount)
        {
            var ledCount = LedCount;
            if (ledCount <= 0 || ledCount > CurrentMaxLedCount)
                throw new InvalidOperationException($"灯珠数量必须在 1 ~ {CurrentMaxLedCount} 之间");
            if (fromPosition > ledCount)
                throw new ArgumentOutOfRangeException(nameof(fromPosition), fromPosition, $"IC 显示偏移位置必须在 1 ~ {ledCount} 之间");

            if (fromPosition < 0) fromPosition = 0;
            if (fromPosition > ledCount) fromPosition = ledCount;

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
            var frame = RentFrame(frameSize);

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
                frame[10] = (byte)(fromPosition >> 8);      // IC 起始位置
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
        #endregion

        #region CreateColorFrame
        /// <summary>
        /// 创建单色颜色帧（<paramref name="color"/> 填充所有像素），并返回完整帧缓冲区。
        /// </summary>
        /// <param name="color">32 位颜色值（如 ARGB 格式）。</param>
        /// <param name="fromPosition">点亮灯珠 IC 的起始位置，有效值范围：[1, <see cref="LedCount"/>]。</param>
        /// <param name="fillCount">填充的灯珠数量，会被钳制到 [1, <see cref="LedCount"/>] 范围。</param>
        /// <param name="repeatCount">颜色数据重复/扩展次数，会被钳制到 [1, <see cref="LedCount"/>] 范围。</param>
        /// <param name="colorFormat"><paramref name="color"/> 数据的颜色值格式，(<c>uint</c> 类型) 必须是 4 通道（如 ARGB、RGBA）。</param>
        /// <returns>已填充完整颜色数据的帧。</returns>
        /// <exception cref="ArgumentException">输入颜色格式通道数不为 4。</exception>
        protected internal byte[] CreateColorFrame(uint color, int fromPosition, int fillCount, int repeatCount, ColorFormat colorFormat = ColorFormat.ARGB)
        {
            // 通道索引表
            var inputIndices = colorFormat.GetChannelIndices();
            var outputIndices = ColorFormat.GetChannelIndices();

            // 颜色的通道数量
            int inputChannelCount = inputIndices.Count;
            int outputChannelCount = outputIndices.Count;

            if (inputChannelCount != 4)
                throw new ArgumentException("输入颜色值 (uint类型) 的通道数量必须为 4 ", nameof(colorFormat));

            int index = 0, outputOffset = 0;
            var frame = CreateEmptyColorFrame(fromPosition, fillCount, repeatCount);

            // 4 通道输入 → 3/4 通道输出，逐通道映射
            for (var j = 0; j < outputChannelCount; j++)
            {
                outputOffset = j + FrameHeaderLength;
                index = inputIndices.IndexOf(outputIndices[j]);
                frame[outputOffset] = (byte)((color >> (24 - index * 8)) & 0xFF);
            }

            return frame;
        }
        /// <summary>
        /// 创建多色颜色帧（<paramref name="colors"/> 逐像素填充），并返回完整帧缓冲区。
        /// </summary>
        /// <param name="colors">颜色数据集合，需要指定颜色通道格式 <paramref name="colorFormat"/></param>
        /// <param name="fromPosition">点亮灯珠 IC 的起始位置，有效值范围：[1, <see cref="LedCount"/>]。</param>
        /// <param name="repeatCount">颜色数据重复/扩展次数，会被钳制到 [1, <see cref="LedCount"/>] 范围。</param>
        /// <param name="colorFormat"><paramref name="colors"/> 数据的颜色值格式，(<c>uint</c> 类型) 必须是 4 通道（如 ARGB、RGBA），(<c>byte</c> 类型) 可以是 3 通道或 4 通道。</param>
        /// <returns>已填充完整颜色数据的帧。</returns>
        /// <exception cref="ArgumentException">colors 为空或输入通道数不匹配。</exception>
        protected internal byte[] CreateColorFrame(IReadOnlyList<byte> colors, int fromPosition, int repeatCount, ColorFormat colorFormat = ColorFormat.RGB)
        {
            if (colors == null || colors.Count == 0)
                throw new ArgumentException("颜色值数组不能为空，或长度不正确");

            // 通道索引表
            var inputIndices = colorFormat.GetChannelIndices();
            var outputIndices = ColorFormat.GetChannelIndices();

            // 颜值的通道数量
            int inputChannelCount = inputIndices.Count;
            int outputChannelCount = outputIndices.Count;

            if (colors.Count < inputChannelCount || colors.Count % inputChannelCount != 0)
                throw new ArgumentException($"颜色数据长度 {colors.Count} 与通道数 {inputChannelCount} 不匹配", nameof(colors));

            var renderCount = colors.Count / inputChannelCount;
            var frame = CreateEmptyColorFrame(fromPosition, Math.Min(renderCount, LedCount), repeatCount);
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
                        // index < 0 表示输出通道在输入中不存在，填充 0xFF（如 Alpha 通道补白）
                        frame[outputOffset + j] = (index >= 0) ? colors[inputOffset + index] : (byte)0xFF;
                    }
                }
            }

            return frame;
        }
        /// <inheritdoc cref="CreateColorFrame(IReadOnlyList{byte}, int, int, ColorFormat)"/> 
        protected internal byte[] CreateColorFrame(IReadOnlyList<uint> colors, int fromPosition, int repeatCount, ColorFormat colorFormat = ColorFormat.ARGB)
        {
            if (colors == null || colors.Count == 0)
                throw new ArgumentException("颜色值数组不能为空");

            // 通道索引表
            var inputIndices = colorFormat.GetChannelIndices();
            var outputIndices = ColorFormat.GetChannelIndices();

            // 颜色的通道数量
            int inputChannelCount = inputIndices.Count;
            int outputChannelCount = outputIndices.Count;

            if (inputChannelCount != 4)
                throw new ArgumentException("输入颜色值 (uint类型) 的通道数量必须为 4 ", nameof(colorFormat));

            int i = 0, j = 0, index = -1, outputOffset = 0;
            var frame = CreateEmptyColorFrame(fromPosition, Math.Min(colors.Count, LedCount), repeatCount);
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

            return frame;
        }
        #endregion

        #region AddColorFrame 
        /// <summary>
        /// 添加待渲染的颜色数据帧到 <b>当前对象的渲染队列</b>。额外校验功能码(0x98/0x99)、灯带类型、颜色数据长度，并自动修正部份字段。
        /// </summary>
        /// <param name="frame">待入队的完整颜色帧，调用方拥有所有权，入队后不应再修改。</param>
        /// <exception cref="ArgumentException">帧格式错误、功能码非颜色帧、灯带类型不匹配、端口号不匹配（非 0）、设备地址不匹配（非 0）、颜色数据长度与格式不匹配。</exception>
        public void AddColorFrame(byte[] frame)
        {
            if (frame == null || frame.Length < RGBFrameBaseLength || frame.Length > FrameMaxLength ||
                frame[0] != 0xDD || frame[1] != 0x55 || frame[2] != 0xEE ||
                frame[frame.Length - 2] != 0xAA || frame[frame.Length - 1] != 0xBB)
                throw new ArgumentException("数据帧格式错误", nameof(frame));

            var isColorFrame = frame.IsColorFrame();
            if (!isColorFrame) throw new ArgumentException("数据帧的功能码不正确");

            // 总线渲染模型，则不用检查
            if (Address == 0x0000 && Port == 0x00)
            {
                EnqueueFrame(frame);
                return;
            }

            // 灯带渲染模型
            var port = frame.GetPort();
            var group = frame.GetGroup();
            var address = frame.GetAddress();
            var ledType = frame.GetLedType();
            if (port != Port) throw new ArgumentException("数据帧的端口号不匹配");
            if (group != Group) throw new ArgumentException("数据帧的组地址不匹配");
            if (address != Address) throw new ArgumentException("数据帧的设备地址不匹配");
            if (ledType != LedType) throw new ArgumentException("数据帧的灯带类型不匹配");

            var ledCount = LedCount;
            var repeat = frame.GetRepeat();

            if (repeat < 1) repeat = 1;
            if (repeat > ledCount) repeat = ledCount;
            int colorChannelCount = ColorFormat.GetChannelCount();

            var colorSize = frame.Length - 18;              //颜色字节的数据长度
            if (colorSize % colorChannelCount != 0)
                throw new ArgumentException($"数据帧的颜色字节长度 {colorSize} 不正确，与灯带的颜色格式 {ColorFormat} 不匹配");

            // 重新计算扩展次数，并修正
            var fillCount = colorSize / colorChannelCount;      //填充的数据量
            if (fillCount * repeat > ledCount) repeat = (int)Math.Ceiling(ledCount / (float)fillCount);

            frame[14] = (byte)(repeat >> 8);    // 扩展次数
            frame[15] = (byte)(repeat & 0xFF);

            EnqueueFrame(frame);
        }
        #endregion

        #region 空闲帧池 系列方法
        /// <summary>
        /// 从空闲帧池中租借一个指定大小的帧缓冲区。池中无匹配大小时新建。
        /// </summary>
        /// <param name="frameSize">所需帧字节长度，范围 [<see cref="RGBFrameBaseLength"/>, <see cref="FrameMaxLength"/>]。</param>
        /// <returns>大小为 <paramref name="frameSize"/> 的字节数组，内容可能包含旧数据（调用方需覆写）。</returns>
        /// <exception cref="ArgumentOutOfRangeException">frameSize 超出合法范围。</exception>
        /// <remarks>
        /// <para>稳定渲染场景下帧大小不变，池命中率极高，可显著减少 byte[] 分配。</para>
        /// <para>不匹配的帧直接从池中丢弃，由 GC 回收。</para>
        /// </remarks>
        protected byte[] RentFrame(int frameSize)
        {
            if (frameSize < RGBFrameBaseLength || frameSize > FrameMaxLength)
                throw new ArgumentOutOfRangeException(nameof(frameSize), "租用数据帧大小超出范围");

            // 一般情况下，10~30 帧/秒，场景或效果不切换的情况下，帧长度会一直是一样长的；
            // 这种情况，从空闲池中取比 new 的收益大，命中概率极高，不用重新分配内存 byte[]
            // 即使长度不匹配，清空空闲池，在 new 重新入池，也能减少绝大部份一直 new byte[] 的情况
            // 还一种更优的情况(.NET std2.0环境)，使用 ArraySegment<byte>，总长度满足的情况下就可以租借出去，但代码改动有点多，留未来考虑、升级(整体收益也差不多)
            byte[] frame = Array.Empty<byte>();
#if UseQueue
            lock (_availableFramesLock)
            {
                while (_availableFrames.Count > 0)
                {
                    frame = _availableFrames.Dequeue();

                    if (frame == null) continue;
                    if (frame.Length == frameSize) break;

                    frame = Array.Empty<byte>();
                }
            }
#else
            while (_availableFrames.TryDequeue(out frame))
            {
                if (frame == null) continue;
                if (frame.Length == frameSize) break;
                frame = Array.Empty<byte>();
            }
#endif
            if (frame == null || frame.Length != frameSize)
            {
                frame = new byte[frameSize];
            }
            return frame;
        }
        /// <summary>
        /// 将使用完毕的数据帧归还到空闲帧池，供后续 <see cref="RentFrame"/> 复用。
        /// 当池中帧数已达 <see cref="MaxAvailableFrameCount"/> 上限时丢弃该帧，由 GC 回收。
        /// </summary>
        /// <param name="frame">待归还的数据帧。若为 <c>null</c> 或长度不足则忽略。</param>
        private void ReturnFrame(byte[] frame)
        {
            if (frame == null || frame.Length < RGBFrameBaseLength) return;
#if UseQueue
            lock (_availableFramesLock)
            {
                if (_availableFrames.Count >= MaxAvailableFrameCount) return;
                _availableFrames.Enqueue(frame);
            }
#else
            if (_availableFrames.Count >= MaxAvailableFrameCount) return;
            _availableFrames.Enqueue(frame);
#endif
        }
        /// <summary>
        /// 清空空闲帧池，丢弃所有缓存帧由 GC 回收。
        /// 通常在灯带参数变更（如 <see cref="LedCount"/> 变化）后调用。
        /// </summary>
        protected internal void ClearAvailableFrames()
        {
#if UseQueue
            lock (_availableFramesLock)
            {
                if (_availableFrames.Count > 0)
                {
                    _availableFrames.Clear();
                }
            }
#else
            while (!_availableFrames.IsEmpty)
            {
                _availableFrames.TryDequeue(out _); // 帧引用丢弃，GC 会回收
            }
#endif
        }
        #endregion

        #region 待渲染的帧池 系列方法
        /// <summary>
        /// 将待渲染的数据帧加入渲染队列。非颜色帧（例如：指令帧）不参与溢出清理，始终保留。
        /// </summary>
        /// <param name="frame">待入队的数据帧。<c>null</c> 或长度不足的帧会被忽略。</param>
        /// <remarks>
        /// <para>溢出帧通过 <see cref="ReturnFrame"/> 归还到空闲池，而非直接丢弃。</para>
        /// </remarks>
        protected internal void EnqueueFrame(byte[] frame)
        {
            if (frame == null || frame.Length < RGBFrameBaseLength) return;

#if UseQueue
            lock (_renderingFramesLock)
            {
                _renderingFrames.Enqueue(frame);
                // 保留非颜色帧（如指令帧），如果当前帧不是颜色数据帧，则不需要检查溢出问题
                var isColorFrame = (frame[8] == 0x98 || frame[8] == 0x99);
                if (!isColorFrame) return;

                while (_renderingFrames.Count > MaxRenderingFrameCount)
                {
                    // 保留非颜色帧（如指令帧）
                    var _frame = _renderingFrames.Peek();
                    isColorFrame = (_frame[8] == 0x98 || _frame[8] == 0x99);
                    if (!isColorFrame) break;  // 非颜色数据帧，下次在检查

                    ReturnFrame(_renderingFrames.Dequeue());
                }
            }
#else
            _renderingFrames.Enqueue(frame);

            // 保留非颜色帧（如指令帧）
            var isColorFrame = (frame[8] == 0x98 || frame[8] == 0x99);
            if (!isColorFrame) return;   // 如果当前帧不是颜色数据帧，则不需要检查溢出问题

            while (_renderingFrames.Count > MaxRenderingFrameCount)
            {
                // 保留非颜色帧（如指令帧）
                if (_renderingFrames.TryPeek(out var _frame))
                {
                    isColorFrame = (_frame[8] == 0x98 || _frame[8] == 0x99);
                    if (!isColorFrame) break;  // 非颜色数据帧，下次在检查
                }

                if (_renderingFrames.TryDequeue(out var __frame))
                {
                    ReturnFrame(__frame);
                }
            }
#endif
        }
        /// <summary>
        /// 从渲染队列中取出下一帧，并结合重复帧去重逻辑决定是否实际渲染。
        /// </summary>
        /// <param name="frame">输出参数：待渲染帧；跳过时为 null。</param>
        /// <returns>
        /// <c>true</c>：成功获取到待渲染帧（队列非空且满足渲染条件）；
        /// <c>false</c>：队列为空、帧无效、或帧与上一帧相同且未达到 <see cref="RenderingRepeatInterval"/> 的整数倍。
        /// </returns>
        /// <remarks>
        /// <para><b>性能设计：</b>颜色帧内容比较（P/Invoke memcmp）在 <c>_renderingStateLock</c> 锁外执行，
        /// 避免阻塞生产者线程的 <see cref="EnqueueFrame"/> 操作。</para>
        /// <para><b>内存管理：</b>被跳过的帧保留为新的 <c>_lastRenderingFrame</c> 供下次比较；
        /// 被替换的旧帧通过 <c>finally</c> 块归还到空闲池，确保不泄漏。</para>
        /// </remarks>
        protected internal bool TryDequeueFrame(out byte[] frame)
        {
            frame = null;
            byte[] currentFrame = null;
#if UseQueue
            lock (_renderingFramesLock)
            {
                if (_renderingFrames.Count <= 0) return false;
                currentFrame = _renderingFrames.Dequeue();
            }
#else
            if (_renderingFrames.IsEmpty) return false;
            if (!_renderingFrames.TryDequeue(out currentFrame)) return false;
#endif
            // 防御性，如果存在帧无效，则返回
            if (currentFrame == null || currentFrame.Length < RGBFrameBaseLength) return false;

            // 记录 准备渲染的当前帧，是否与已经渲染的上一帧相同
            byte[] lastFrame;
            int repeatInterval;
            lock (_renderingStateLock)
            {
                lastFrame = _lastRenderingFrame;
                repeatInterval = RenderingRepeatInterval;
                _lastRenderingFrame = Array.Empty<byte>();
            }
            // 在锁外执行帧内容比较（FastSequenceEqual 内部调用 memcmp）
            var isColorFrame = (currentFrame[8] == 0x98 || currentFrame[8] == 0x99);
            var isSameFrame = repeatInterval > 1 && isColorFrame && lastFrame.FastSequenceEqual(currentFrame);

            try
            {
                lock (_renderingStateLock)
                {
                    if (isSameFrame)
                    {
                        _renderingRepeatCount++;
                        if (_renderingRepeatCount % repeatInterval != 0)
                        {
                            // 帧相同，且未达到重复渲染间隔，跳过渲染当前帧
                            _lastRenderingFrame = currentFrame;
                            return false;
                        }
                    }
                    else
                    {
                        _renderingRepeatCount = 0;
                    }

                    // 正常渲染
                    frame = currentFrame;
                    _renderingCount++;
                    _lastRenderingFrame = currentFrame;
                    return true;
                }
            }
            finally
            {                
                ReturnFrame(lastFrame); // 归还上一帧到空闲池
            }
        }
        /// <summary>
        /// 清空渲染队列并重置渲染状态。
        /// </summary>
        /// <remarks>清空渲染队列，队列中的帧直接丢弃不归还；内部再调用 <see cref="ResetRenderingState"/>。</remarks>
        public void ClearRenderingFrames()
        {
#if UseQueue
            lock (_renderingFramesLock)
            {
                if (_renderingFrames.Count > 0)
                {
                    _renderingFrames.Clear();
                }
            }
#else
            while (!_renderingFrames.IsEmpty)
            {
                _renderingFrames.TryDequeue(out _);
            }
#endif
            ResetRenderingState();
        }
        /// <summary>
        /// 重置渲染状态：结算 FPS，清零计数器，归还旧帧引用。
        /// </summary>
        /// <remarks>
        /// <para>通常在每秒结算或灯带配置变更时调用。</para>
        /// <para><see cref="ReturnFrame"/> 在锁外执行，避免持锁期间阻塞其他操作。</para>
        /// </remarks>
        protected internal void ResetRenderingState()
        {
            byte[] lastFrame;
            lock (_renderingStateLock)
            {
                Fps = _renderingCount;
                lastFrame = _lastRenderingFrame;

                _renderingCount = 0;
                _renderingRepeatCount = 0;
                _lastRenderingFrame = Array.Empty<byte>();
            }
            ReturnFrame(lastFrame);
        }
        #endregion

    }

}
