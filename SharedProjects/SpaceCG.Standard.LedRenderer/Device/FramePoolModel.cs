using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using SpaceCG.Extensions;

namespace SpaceCG.Device
{
    /// <summary>
    /// LED 灯带数据帧池抽象基类，管理数据帧的租借、渲染队列及重复帧去重。
    /// </summary>
    /// <remarks>
    /// <para><b>数据帧协议结构：</b></para>
    /// <para>  [0-2] 帧头 (0xDD 0x55 0xEE)</para>
    /// <para>  [3-4] 组地址 (ushort, Big-Endian)，值范围：0~1024 </para>
    /// <para>  [5-6] 设备地址 (ushort, Big-Endian)，值范围：0~4096 </para>
    /// <para>  [7]   端口号，值范围：0~30 </para>
    /// <para>  [8]   功能码 (0x98/0x99=颜色帧, 0x9B=上电显示, 0x9C=关闭上电显示)</para>
    /// <para>  [9]   灯带类型</para>
    /// <para>  [10-11] 保留字节 (ushort, Big-Endian)</para>
    /// <para>  [12-13] 颜色数据长度 (ushort, Big-Endian)，值范围：3~3072，须与帧总长度一致 (dataLength + 18 == frame.Length) </para>
    /// <para>  [14-15] 扩展/重复次数 (ushort, Big-Endian)，值范围：0~1024 </para>
    /// <para>  [16..^2] 颜色数据</para>
    /// <para>  [^2-^1] 帧尾 (0xAA 0xBB)</para>
    /// <para><b>帧长度约束：</b></para>
    /// <para>  RGB 最小帧长: 16(帧头) + 2(帧尾) + 3(RGB最小数据) = 21 字节</para>
    /// <para>  WRGB 最小帧长: 16(帧头) + 2(帧尾) + 4(WRGB最小数据) = 22 字节</para>
    /// <para><b>池容量：</b></para>
    /// <para>  渲染队列最大: <see cref="MaxRenderingFrameCount"/> = 3</para>
    /// <para><b>线程安全：</b></para>
    /// <para>  渲染状态（<c>_renderingCount</c>、<c>_lastRenderingFrame</c>、<c>_renderingRepeatCount</c>）通过 <c>_renderingLock</c> 同步。</para>
    /// <para>  渲染队列使用 <see cref="ConcurrentQueue{T}"/> 保证线程安全。</para>
    /// <para>  灯带属性（Group、Address、Port 等）在初始化后不变，无额外同步。</para>
    /// </remarks>
    public abstract class FramePoolModel
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
        /// <summary> 数据帧的最小字节长度(RGB)  </summary>
        internal const int RGBFrameBaseLength = FrameHeaderLength + FrameFooterLength + 3;
        /// <summary> 数据帧的最小字节长度(WRGB)  </summary>
        internal const int WRGBFrameBaseLength = FrameHeaderLength + FrameFooterLength + 4;

        /// <summary> 渲染队列最大容量 </summary>
        internal const int MaxRenderingFrameCount = 3;

#if false
        internal const int OffsetGroup = 3;
        internal const int OffsetAddress = 5;
        internal const int OffsetPort = 7;
        internal const int OffsetFunction = 8;
        internal const int OffsetLedType = 9;
        internal const int OffsetLength = 12;
        internal const int OffsetRepeat = 14;
        internal const int OffsetColor = 16;
#endif
        #endregion

        #region 灯带相关属性        
        /// <summary>
        /// Led 灯带的组地址
        /// </summary>
        public ushort Group { get; set; } = 0;
        /// <summary>
        /// Led 灯带的设备地址
        /// </summary>
        public ushort Address { get; protected set; } = 0x0001;
        /// <summary>
        /// Led 灯带的设备端口号
        /// </summary>
        public byte Port { get; protected set; } = 0x00;
        /// <summary>
        /// Led 灯带的保留数据
        /// </summary>
        public ushort Reserved { get; set; } = 0x0000;
        /// <summary>
        /// Led 灯带的类型
        /// </summary>
        public LedType LedType { get; protected set; } = LedType.WS2812B;
        /// <summary>
        /// Led 灯带的颜色格式
        /// </summary>
        public ColorFormat ColorFormat { get; protected set; } = ColorFormat.RGB;
        /// <summary>
        /// Led 灯带的灯珠数量，或是总线上最长的灯带灯珠数量
        /// </summary>
        public int LedCount { get; protected set; } = 0;
        #endregion

        #region 渲染相关属性
        /// <summary>
        /// 渲染状态同步锁，保护 <see cref="_lastRenderingFrame"/>、<see cref="_renderingRepeatCount"/>、<see cref="_renderingCount"/> 的并发访问。
        /// </summary>
        private readonly object _renderingLock = new object();
        /// <summary>
        /// 正在渲染的数据帧池 — 当前被渲染管线占用的帧
        /// </summary>
        private readonly ConcurrentQueue<byte[]> _renderingFrames = new ConcurrentQueue<byte[]>();
        
        /// <summary>
        /// 自上次 <see cref="ResetRenderingCounter"/> 调用以来已渲染的帧累计数量，用于计算 FPS。
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
        public int RenderingRepeatInterval { get; protected set; } = 10;
        /// <summary>
        /// 获取当前渲染队列中待处理的帧数量。
        /// </summary>
        /// <remarks>
        /// <para>该值为 <see cref="_renderingFrames"/> 队列的实时快照，包含所有待渲染帧（含颜色帧和非颜色帧）。</para>
        /// <para>返回值范围：0 ~ 无上限（受 <see cref="EnqueueFrame"/> 的溢出策略控制，颜色帧通常不超过 <see cref="MaxRenderingFrameCount"/> = 3）。</para>
        /// <para><b>线程安全：</b><see cref="ConcurrentQueue{T}.Count"/> 为快照值，在并发入队/出队时可能已过期，仅作近似参考。</para>
        /// </remarks>
        public int RenderingFrameCount => _renderingFrames.Count;
        /// <summary> 
        /// 当前渲染帧率（帧/秒）。 
        /// </summary>
        public int Fps { get; private set; } = 0;
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
            var ledCount = LedCount;
            if (ledCount <= 0) throw new InvalidOperationException("LedCount must be greater than 0");

            byte[] frame = CreateColorFrame(1, ledCount);

            // 设置上电显示的颜色（9B）
            // 关闭上电显示功能（9C）
            frame[8] = (byte)(isShow ? 0x9B : 0x9C);    // 功能码

            if (isShow)
            {
                // 通道索引表
                var inputIndices = colorFormat.GetChannelIndices();
                var outputIndices = ColorFormat.GetChannelIndices();

                // 颜色的通道数量
                int inputChannelCount = inputIndices.Count;
                int outputChannelCount = outputIndices.Count;

                if (inputChannelCount != 4)
                    throw new ArgumentException("输入颜色值 (uint类型) 的通道数量必须为 4 ", nameof(colorFormat));

                int index = -1, outputOffset = 0;
                for (var j = 0; j < outputChannelCount; j++)
                {
                    outputOffset = j + FrameHeaderLength;
                    index = inputIndices.IndexOf(outputIndices[j]);
                    frame[outputOffset] = (byte)((color >> (24 - index * 8)) & 0xFF);
                }
            }

            EnqueueFrame(frame);
        }
        #endregion

        #region CreateColorFrame/AddColorFrame 系列方法
        /// <summary>
        /// 创建一个颜色数据帧，并填充帧头、帧尾及协议字段（除颜色数据外）。
        /// </summary>
        /// <param name="fillCount">填充的灯珠数量（颜色数据中的像素数），会被钳制到 [1, <see cref="LedCount"/>] 范围。</param>
        /// <param name="repeatCount">颜色数据的扩展/重复次数，会被钳制到 [1, <see cref="LedCount"/>] 范围。</param>
        /// <returns>已填充协议头尾字段的帧缓冲区，颜色数据区域为全零。</returns>
        /// <exception cref="InvalidOperationException"><see cref="LedCount"/> 未初始化（≤0）。</exception>
        internal byte[] CreateColorFrame(int fillCount, int repeatCount)
        {
            var ledCount = LedCount;
            if (ledCount <= 0 || ledCount > 1024)
                throw new InvalidOperationException("LedCount must be greater than 0 and less than 1024");

            if (fillCount <= 0) fillCount = 1;
            if (fillCount > ledCount) fillCount = ledCount;

            if (repeatCount <= 0) repeatCount = 1;
            if (repeatCount > ledCount) repeatCount = ledCount;

            // 优化计算，如果参数设置的灯珠总数量大于实际的数量，则优化重复次数
            int totalCount = fillCount * repeatCount;
            if (totalCount > ledCount) repeatCount = (int)Math.Ceiling(ledCount / (double)fillCount);

            // 颜色数据占用的字节长度
            var colorSize = fillCount * ColorFormat.GetChannelCount();
            // 当前帧的字节总长度
            var frameSize = colorSize + FrameHeaderLength + FrameFooterLength;
            byte[] frame = new byte[frameSize];

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
        /// <summary>
        /// 添加待渲染的颜色数据帧，会检查该数据帧是否符合当前 Led 灯带的参数（例如：检查帧头/帧尾格式、地址/端口号/渲染数量是否匹配等等）
        /// <para>写入的是完整数据帧；会自动计算更新 Group/Reserved/Repeat 数据</para>
        /// </summary>
        /// <param name="frame"></param>
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
            var frame = CreateColorFrame(1, repeat);

            for (var j = 0; j < outputChannelCount; j++)
            {
                outputOffset = j + FrameHeaderLength;
                index = inputIndices.IndexOf(outputIndices[j]);
                frame[outputOffset] = (byte)((color >> (24 - index * 8)) & 0xFF);
            }

            EnqueueFrame(frame);
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
            renderCount = Math.Min(renderCount, ledCount);
            byte[] frame = CreateColorFrame(renderCount, repeat);

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
                    channelMap[i] = inputIndices.IndexOf(outputIndices[i]);
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

            EnqueueFrame(frame);
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
            int renderCount = Math.Min(colors.Count, ledCount);
            byte[] frame = CreateColorFrame(renderCount, repeat);

            // 预计算通道索引映射
            int[] channelMap = new int[outputChannelCount];
            for (i = 0; i < outputChannelCount; i++)
            {
                channelMap[i] = inputIndices.IndexOf(outputIndices[i]);
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

            EnqueueFrame(frame);
        }
        #endregion

        #region 待渲染的帧池 系列方法
        /// <summary>
        /// 将待渲染的数据帧加入渲染队列。
        /// </summary>
        /// <remarks>
        /// <para><b>溢出策略：</b>当队列中颜色帧数量超过 <see cref="MaxRenderingFrameCount"/> 时，
        /// 丢弃队首最旧的颜色帧（直接丢弃，由 GC 回收），确保新帧能及时渲染。</para>
        /// <para>非颜色帧（功能码 ≠ 0x98 || 0x99，如上电显示指令）不参与溢出清理，始终保留。</para>
        /// </remarks>
        /// <param name="frame">待入队的数据帧。<c>null</c> 或长度不足的帧会被忽略。</param>
        private void EnqueueFrame(byte[] frame)
        {
            if (frame == null || frame.Length < RGBFrameBaseLength) return;

            _renderingFrames.Enqueue(frame);
            var isColorFrame = (frame[8] == 0x98 || frame[8] == 0x99);
            if (!isColorFrame) return;   // 如果当前帧不是颜色数据帧，则不需要检查溢出问题

            while (_renderingFrames.Count > MaxRenderingFrameCount && _renderingFrames.TryPeek(out var _frame))
            {
                isColorFrame = (_frame[8] == 0x98 || _frame[8] == 0x99);
                if (!isColorFrame) break;  // 非颜色数据帧，下次在检查

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
                    return true;
                }

                frame = _frame;
                _renderingCount++;
                _renderingRepeatCount = 0;
                _lastRenderingFrame = _frame;
                return true;
            }
        }
        /// <summary>
        /// 清空渲染队列，丢弃所有待渲染的数据帧
        /// </summary>
        public void ClearRenderingFrames()
        {
            lock (_renderingLock)
            {
                _renderingCount = 0;
                _renderingRepeatCount = 0;
                _lastRenderingFrame = Array.Empty<byte>();
            }
            while (!_renderingFrames.IsEmpty)
            {
                _renderingFrames.TryDequeue(out _);
            }
        }
        /// <summary>
        /// 重置渲染统计计数器，将 <see cref="Fps"/> 更新为本周期渲染帧数，并清零 <see cref="_renderingCount"/> 和 <see cref="_renderingRepeatCount"/>。
        /// </summary>
        /// <remarks>
        /// 通常由外部定时器（如每秒一次）调用，用于计算实时帧率。受 <c>_renderingLock</c> 保护。
        /// </remarks>
        internal void ResetRenderingCounter()
        {
            lock (_renderingLock)
            {
                Fps = _renderingCount;

                _renderingCount = 0;
                _renderingRepeatCount = 0;
            }
        }
        #endregion

    }
}
