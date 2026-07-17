using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace SpaceCG.Generic
{
#if false
    /// <summary>
    /// 数据包事件参数，在 <see cref="ProtocolParser"/> 解析到完整数据包时通过 <see cref="ProtocolParser.PacketReceived"/> 事件传递。
    /// <para>提供两种数据访问方式：
    /// <list type="number">
    /// <item><see cref="RawView"/>：内部缓冲区的零拷贝 <see cref="ArraySegment{T}"/> 视图，仅在事件回调期间有效，回调返回后可能被覆盖。</item>
    /// <item><see cref="Packet"/>：独立于内部缓冲区的字节数组副本，调用方可安全长期持有。首次访问时触发惰性拷贝。</item>
    /// </list>
    /// </para>
    /// <para>高性能场景：若仅在事件回调内消费数据（如转发、写入网络流），直接使用 <see cref="RawView"/> 即可避免拷贝。</para>
    /// <para>长期持有场景：若需要在事件回调之外持有数据，访问 <see cref="Packet"/> 属性即可获得独立副本。</para>
    /// </summary>
    public class PacketEventArgs : EventArgs
    {
        private byte[] _packet;
        private ArraySegment<byte> _rawView;
        private bool _hasPacket;

        /// <summary>
        /// 完整数据包在内部缓冲区中的零拷贝视图。
        /// <para>此视图仅在事件回调期间有效，回调返回后内部缓冲区可能被覆盖。</para>
        /// <para>如果需要在事件回调之外使用数据，请访问 <see cref="Packet"/> 属性获取独立副本。</para>
        /// </summary>
        public ArraySegment<byte> RawView
        {
            get => _rawView;
            internal set
            {
                _rawView = value;
                _hasPacket = false;
                _packet = null;
            }
        }

        /// <summary>
        /// 从内部缓冲区拷贝的完整数据包副本（字节数组）。
        /// <para>惰性拷贝：首次访问时从 <see cref="RawView"/> 拷贝，后续访问直接返回缓存副本。</para>
        /// <para>此副本独立于内部缓冲区，调用方可长期持有。</para>
        /// </summary>
        public byte[] Packet
        {
            get
            {
                if (!_hasPacket)
                {
                    if (_rawView.Array == null || _rawView.Count == 0)
                        return null;

                    _packet = new byte[_rawView.Count];
                    Buffer.BlockCopy(_rawView.Array, _rawView.Offset, _packet, 0, _rawView.Count);
                    _hasPacket = true;
                }
                return _packet;
            }
        }

        /// <summary>
        /// 初始化 <see cref="PacketEventArgs"/> 类的新实例。
        /// 通过 <see cref="RawView"/> 设置内部零拷贝视图，<see cref="Packet"/> 首次访问时惰性拷贝。
        /// </summary>
        /// <param name="buffer">内部缓冲区数组引用。</param>
        /// <param name="offset">数据包在缓冲区中的起始偏移。</param>
        /// <param name="count">数据包字节数。</param>
        public PacketEventArgs(byte[] buffer, int offset, int count)
        {
            _rawView = new ArraySegment<byte>(buffer, offset, count);
            _hasPacket = false;
            _packet = null;
        }

        /// <summary>
        /// 初始化 <see cref="PacketEventArgs"/> 类的新实例（兼容旧版，直接传入已拷贝副本）。
        /// </summary>
        /// <param name="packet">完整数据包的字节数组副本。</param>
        public PacketEventArgs(byte[] packet)
        {
            _packet = packet;
            _hasPacket = true;
            _rawView = default;
        }
    }

    /// <summary>
    /// 数据协议解析器抽象基类。采用生产者-消费者模式：
    /// <para>外部通过 <see cref="Write(byte)"/> / <see cref="Write(byte[], int, int)"/> 写入原始字节数据（生产者），
    /// 内部通过 <see cref="TryParse"/> 匹配完整数据包并通过 <see cref="PacketReceived"/> 事件抛出（消费者）。</para>
    /// <para>内部使用环形缓冲区（Ring Buffer）模式管理字节数据：以 <c>byte[]</c> + 三指针
    /// （<c>_readPosition</c>、<c>_writePosition</c>、<c>_pendingLength</c>）实现高效的零拷贝数据视图。</para>
    /// <para>当缓冲区累积超过容量限制时，将从头部丢弃最旧的数据以防止内存无限增长。</para>
    /// <para>线程安全：此类不保证线程安全，多线程并发访问需要外部同步。</para>
    /// </summary>
    public abstract class ProtocolParser
    {
        /// <summary>环形缓冲区默认容量（1024 字节）。</summary>
        public const int DefaultBufferSize = 1024;

        private readonly byte[] _buffer;
        private readonly int _bufferSize;

        /// <summary>
        /// 读指针：指向环形缓冲区中待解析数据的起始位置。
        /// </summary>
        private int _readPosition;
        /// <summary>
        /// 写指针：指向环形缓冲区中下一个可写入数据的位置。
        /// </summary>
        private int _writePosition;
        /// <summary>
        /// 待解析字节数：从 <see cref="_readPosition"/> 开始尚未被解析的有效数据长度。
        /// </summary>
        //private int _pendingLength;
        /// <summary>
        /// 获取缓冲区中当前待解析的字节数。
        /// </summary>
        //public int Available => _pendingLength;
        public int Available => _writePosition - _readPosition;

        /// <summary>
        /// 每当成功解析出一条完整数据包时触发。数据包以 <see cref="PacketEventArgs"/> 形式传递。
        /// <para>事件参数中包含完整数据包的字节数组副本。</para>
        /// </summary>
        public event EventHandler<PacketEventArgs> PacketReceived;

        /// <summary>
        /// 使用默认缓冲区大小（<see cref="DefaultBufferSize"/> 字节）初始化解析器。
        /// </summary>
        public ProtocolParser() : this(DefaultBufferSize)
        {
        }

        /// <summary>
        /// 使用指定缓冲区大小初始化解析器。
        /// </summary>
        /// <param name="bufferSize">缓冲区最大字节数，超出后旧数据将被丢弃。必须大于 0。</param>
        /// <exception cref="ArgumentException"><paramref name="bufferSize"/> 小于或等于 0 时抛出。</exception>
        public ProtocolParser(int bufferSize)
        {
            if (bufferSize <= 0)
                throw new ArgumentException("缓冲区大小必须大于 0。", nameof(bufferSize));

            _bufferSize = bufferSize;
            _buffer = new byte[bufferSize];
        }

        #region 写入数据
        /// <summary>
        /// 将单个字节写入环形缓冲区，并立即尝试解析完整数据包。
        /// </summary>
        /// <param name="data">要写入的字节。</param>
        public void Write(byte data)
        {
            EnsureCapacity(1);
            _buffer[_writePosition] = data;
            AdvanceWrite(1);
            ParseBufferInternal();
        }

        /// <summary>
        /// 将字节数组的指定范围写入环形缓冲区，并立即尝试解析完整数据包。
        /// <para>支持跨边界写入：当写入数据超出缓冲区末尾时自动环绕到开头继续写入。</para>
        /// </summary>
        /// <param name="data">包含源数据的字节数组。</param>
        /// <param name="offset"><paramref name="data"/> 中开始复制的起始索引（从 0 开始）。</param>
        /// <param name="count">要写入的字节数。</param>
        /// <exception cref="ArgumentNullException"><paramref name="data"/> 为 null 时抛出。</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="offset"/> 或 <paramref name="count"/> 超出数组范围时抛出。</exception>
        public int Write(byte[] data, int offset, int count)
        {
            if (data == null || data.Length == 0 || count == 0) return 0;
            if (offset < 0 || offset > data.Length)
                throw new ArgumentOutOfRangeException(nameof(offset));
            if (count < 0 || offset + count > data.Length)
                throw new ArgumentOutOfRangeException(nameof(count));

            // 1.先写入缓冲区，按缓冲区剩余空间写入，能写多少就写多少
            var writeCount = Math.Min(count, _bufferSize - _writePosition);

            Buffer.BlockCopy(data, offset, _buffer, _writePosition, writeCount);
            _writePosition += writeCount;

            // 先尝试解析已有数据，释放空间
            ParseBufferInternal();

            // 数据正好分析完 → 所有指针归零
            if (_readPosition == _writePosition)
            {
                _readPosition = 0;
                _writePosition = 0;
            }
            // 缓冲区剩余有效空间不足 1/8，紧凑：将未处理数据移到开头
            else if (_bufferSize - _writePosition < _bufferSize / 8)
            {
                var remaining = _writePosition - _readPosition;
                Buffer.BlockCopy(_buffer, _readPosition, _buffer, 0, remaining);
                Trace.TraceInformation($"移动缓冲区数据 {remaining} bytes");

                _readPosition = 0;
                _writePosition = remaining;
            }
            // 缓冲区已经满了，清空防止死锁
            else if (_writePosition == _bufferSize)
            {
                Trace.TraceWarning($"缓冲区已满，且没有完整的数据消息，清空缓冲区 {_writePosition - _readPosition} bytes");
                _writePosition = 0;
                _readPosition = 0;
            }

            return writeCount;

        }

        #endregion

        #region 环形缓冲区管理

        /// <summary>
        /// 确保环形缓冲区有足够的空闲空间容纳 <paramref name="neededBytes"/> 字节。
        /// <para>空间不足时从头部丢弃最旧的未消费数据；当需求超过缓冲区总容量时直接清空。</para>
        /// </summary>
        /// <param name="neededBytes">需要写入的字节数。</param>
        private void EnsureCapacity(int neededBytes)
        {
            // 单次写入超过缓冲区总容量，直接清空（放弃本次写入以外的所有旧数据）
            if (neededBytes > _bufferSize)
            {
                Clear();
                return;
            }

            // 循环丢弃最旧字节，直到有足够空闲空间
            while (_pendingLength + neededBytes > _bufferSize && _pendingLength > 0)
            {
                _readPosition = (_readPosition + 1) % _bufferSize;
                _pendingLength--;
            }

            // 全部丢弃后指针归零，为后续写入提供最大连续可用空间
            if (_pendingLength == 0)
            {
                _readPosition = 0;
                _writePosition = 0;
            }
        }

        /// <summary>
        /// 推进写指针并累加待解析长度。
        /// <para>写指针按环形取模方式前进，<see cref="_pendingLength"/> 增加 <paramref name="count"/>。</para>
        /// </summary>
        /// <param name="count">写入的字节数。</param>
        private void AdvanceWrite(int count)
        {
            _writePosition = (_writePosition + count) % _bufferSize;
            _pendingLength += count;
        }

        /// <summary>
        /// 紧凑环形缓冲区：将未消费数据平移到缓冲区开头，消除读指针之前的碎片空间。
        /// <para>紧凑后：<see cref="_readPosition"/> = 0，<see cref="_writePosition"/> = <see cref="_pendingLength"/>，
        /// 未消费数据在 [0..<see cref="_pendingLength"/>) 区间连续存放。</para>
        /// <para>注意：紧凑策略保证了调用时数据不跨越物理边界（读指针过半或空闲不足 1/8 时触发），
        /// 因此只需处理情形 A（数据连续但未在开头）的平移。</para>
        /// </summary>
        private void CompactBuffer()
        {
            // 已在开头，无需紧凑
            if (_readPosition == 0)
                return;

            // 无数据，直接归零
            if (_pendingLength <= 0)
            {
                _readPosition = 0;
                _writePosition = 0;
                return;
            }

            // 数据连续但未在开头 → 整体平移到 [0.._pendingLength)
            Buffer.BlockCopy(_buffer, _readPosition, _buffer, 0, _pendingLength);

            _readPosition = 0;
            _writePosition = _pendingLength;
        }

        /// <summary>
        /// 获取待解析数据的连续 <see cref="ArraySegment{T}"/> 视图（零拷贝）。
        /// <para>正常情况数据始终连续（紧凑策略保证），跨物理边界仅在紧凑触发前极小时间窗口内可能出现。
        /// 若出现则通过 <see cref="Debug.Fail"/> 标记逻辑漏洞，并兜底调用 <see cref="CompactBuffer"/>。</para>
        /// <para>注意：此视图直接引用内部缓冲区内存，调用方（子类 <see cref="TryParse"/>）不应长期持有。</para>
        /// </summary>
        /// <returns>待解析数据的连续字节视图；无数据时返回空段（Count = 0）。</returns>
        private ArraySegment<byte> GetPendingView()
        {
            if (_pendingLength == 0)
                return new ArraySegment<byte>(_buffer, 0, 0);

            // 防御性检查：紧凑策略已保证数据不跨边界，若出现则说明存在逻辑漏洞
            if (_readPosition + _pendingLength > _bufferSize)
            {
                Debug.Fail("数据跨越物理边界，紧凑策略可能存在漏洞。");
                CompactBuffer();
            }

            return new ArraySegment<byte>(_buffer, _readPosition, _pendingLength);
        }

        #endregion

        #region 解析循环

        /// <summary>
        /// 内部解析循环：反复调用子类的 <see cref="TryParse"/> 查找完整数据包，
        /// 找到后通过 <see cref="PacketReceived"/> 事件抛出零拷贝视图（消费者自行决定是否拷贝）。
        /// <para>解析循环退出后执行环形缓冲收尾：归零空缓冲区、紧凑碎片数据、清空满载缓冲区防死锁。</para>
        /// </summary>
        private void ParseBufferInternal()
        {
            // 热路径：缓存成员引用到局部变量，减少间接寻址
            byte[] buffer = _buffer;
            int bufferSize = _bufferSize;

            while (_pendingLength > 0)
            {
                var pendingView = GetPendingView();
                if (pendingView.Count == 0)
                    break;

                if (!TryParse(pendingView, out int consumeCount))
                    break;

                if (consumeCount <= 0 || consumeCount > _pendingLength)
                    break;

                // 零拷贝：将内部缓冲区视图传递给事件参数，不立即拷贝
                var args = new PacketEventArgs(buffer, _readPosition, consumeCount);

                // 推进读指针（环形取模，必须在事件触发之前完成，防止重入读到旧数据）
                _readPosition = (_readPosition + consumeCount) % bufferSize;
                _pendingLength -= consumeCount;

                // 事件回调中消费者可直接使用 RawView（零拷贝），也可访问 Packet（惰性拷贝）
                PacketReceived?.Invoke(this, args);
            }

            #region 环形缓冲收尾：根据消费情况移动指针

            // 数据正好分析完 → 所有指针归零
            if (_pendingLength == 0)
            {
                _readPosition = 0;
                _writePosition = 0;
            }
            // 读指针过半 或 剩余空间不足 1/8 → 紧凑：将未处理数据移到缓冲区开头
            else if (_readPosition > 0 && (_readPosition > bufferSize / 2 || bufferSize - _pendingLength < bufferSize / 8))
            {
                var remaining = _pendingLength;
                CompactBuffer();
                Trace.TraceInformation($"协议解析器紧凑缓冲区数据 {remaining} bytes");
            }

            // 缓冲区已经满了，清空防止死锁
            if (_pendingLength == bufferSize)
            {
                Trace.TraceWarning($"协议解析器缓冲区已满，且没有完整的数据消息，清空缓冲区 {_pendingLength} bytes");
                Clear();
            }

            #endregion
        }

        #endregion

        #region 抽象方法（子类实现协议逻辑）

        /// <summary>
        /// 尝试从待解析数据视图中匹配一条完整数据包。由子类实现具体协议逻辑。
        /// <para><paramref name="consumeCount"/> 表示从 <paramref name="pendingView"/> 头部开始消费的字节数，
        /// 基类将提取这部分字节作为完整数据包并推进读指针。</para>
        /// <para><paramref name="pendingView"/> 是内部缓冲区的连续零拷贝视图，仅在本方法调用期间有效，不应长期持有。</para>
        /// </summary>
        /// <param name="pendingView">待解析数据的连续只读视图。</param>
        /// <param name="consumeCount">输出参数：从视图头部开始消费的字节数。返回 0 表示未找到完整包，需要等待更多数据。</param>
        /// <returns>找到一条完整数据包返回 true；否则返回 false。</returns>
        protected abstract bool TryParse(ArraySegment<byte> pendingView, out int consumeCount);

        #endregion

        #region 公开方法

        /// <summary>
        /// 清空环形缓冲区中的所有数据，所有指针归零，恢复初始状态。
        /// </summary>
        public void Clear()
        {
            _readPosition = 0;
            _writePosition = 0;
            _pendingLength = 0;
        }

        #endregion

        #region IndexOf 辅助方法

        /// <summary>
        /// 在 <see cref="ArraySegment{T}"/> 视图的字节序列中查找模式字节序列的首次出现位置。
        /// <para>供子类在 <see cref="TryParse"/> 中搜索分隔符或标记时使用。</para>
        /// <para>算法：采用头尾双过滤策略，先同时比对首尾字节，两者都命中才进入内循环逐字节比对。
        /// 时间复杂度：平均 O(n)；空间复杂度 O(1)。</para>
        /// </summary>
        /// <param name="source">要搜索的源字节序列视图。</param>
        /// <param name="pattern">要查找的模式字节序列。</param>
        /// <param name="index">搜索起始位置（相对于视图起始的偏移），默认为 0。</param>
        /// <returns>模式序列首次出现的位置偏移（相对于视图起始）；如果未找到或参数无效则返回 -1。</returns>
        public static int IndexOf(ArraySegment<byte> source, IReadOnlyList<byte> pattern, int index = 0)
        {
            if (source.Array == null || pattern == null)
                return -1;

            int sCount = source.Count;
            int pCount = pattern.Count;

            // 边界检查：空模式、模式过长、起始索引越界
            if (pCount == 0 || pCount > sCount || index < 0 || index >= sCount)
                return -1;

            int limit = sCount - pCount;
            if (index > limit)
                return -1;

            byte[] array = source.Array;
            int offset = source.Offset;
            byte head = pattern[0];

            // 单字节模式：使用 Array.IndexOf 获得 JIT 内在优化
            if (pCount == 1)
            {
                return Array.IndexOf(array, head, offset + index, sCount - index) - offset;
            }

            int pLast = pCount - 1;
            byte tail = pattern[pLast];

            // 双字节模式：直接比对两个字节
            if (pCount == 2)
            {
                for (int i = index; i <= limit; i++)
                {
                    if (array[offset + i] == head && array[offset + i + 1] == tail)
                        return i;
                }
                return -1;
            }

            // 头尾双过滤 + 中间逐字节比对
            // 随机数据下首尾同时命中的概率 ≈ 1/65536，第一步几乎全部排除
            for (int i = index; i <= limit; i++)
            {
                if (array[offset + i] != head || array[offset + i + pLast] != tail)
                    continue;

                // 逐字节比对中间部分（首尾已验过，跳过）
                int j = 1;
                for (j = 1; j < pLast; j++)
                {
                    if (array[offset + i + j] != pattern[j])
                        break;
                }

                if (j == pLast)
                    return i;
            }

            return -1;
        }

        /// <summary>
        /// 在只读字节列表中查找模式字节序列的首次出现位置。（兼容旧版，保留供外部使用）
        /// <para>算法复杂度：O(n*m)，其中 n 为源长度，m 为模式长度。适用于短模式和中小规模数据。</para>
        /// </summary>
        /// <param name="source">要搜索的源字节序列。</param>
        /// <param name="pattern">要查找的模式字节序列。</param>
        /// <param name="index">搜索起始位置（从 0 开始计数），默认为 0。</param>
        /// <returns>模式序列首次出现的索引位置；如果未找到或参数无效则返回 -1。</returns>
        public static int IndexOf(IReadOnlyList<byte> source, IReadOnlyList<byte> pattern, int index = 0)
        {
            if (source == null || pattern == null)
                return -1;

            var sCount = source.Count;
            var pCount = pattern.Count;

            if (pCount == 0 || pCount > sCount || index < 0 || index >= sCount)
                return -1;

            var limit = sCount - pCount;
            if (index > limit)
                return -1;

            var first = pattern[0];

            for (int i = index; i <= limit; i++)
            {
                if (source[i] != first)
                    continue;

                bool match = true;
                for (int j = 1; j < pCount; j++)
                {
                    if (source[i + j] != pattern[j])
                    {
                        match = false;
                        break;
                    }
                }

                if (match)
                    return i;
            }

            return -1;
        }

        #endregion
    }

    /// <summary>
    /// 固定长度协议解析器。当缓冲区中累积的数据量达到指定固定长度时，
    /// 将该长度的连续数据视为一个完整数据包提取。
    /// <para>使用场景：协议中每个数据包长度固定且一致，如某些传感器数据帧、固定长度指令等。</para>
    /// </summary>
    public sealed class FixedLengthProtocolParser : ProtocolParser
    {
        /// <summary>每个完整数据包的固定字节长度。</summary>
        private readonly int _fixedLength;

        /// <summary>
        /// 使用指定固定包长度和默认缓冲区大小初始化解析器。
        /// </summary>
        /// <param name="fixedLength">每个数据包的固定字节长度，必须大于 0。</param>
        /// <exception cref="ArgumentException"><paramref name="fixedLength"/> 小于或等于 0 时抛出。</exception>
        public FixedLengthProtocolParser(int fixedLength)
            : this(fixedLength, DefaultBufferSize)
        {
        }

        /// <summary>
        /// 使用指定固定包长度和缓冲区大小初始化解析器。
        /// </summary>
        /// <param name="fixedLength">每个数据包的固定字节长度，必须大于 0。</param>
        /// <param name="bufferSize">缓冲区最大字节数，超出后旧数据将被丢弃。</param>
        /// <exception cref="ArgumentException"><paramref name="fixedLength"/> 或 <paramref name="bufferSize"/> 无效时抛出。</exception>
        public FixedLengthProtocolParser(int fixedLength, int bufferSize)
            : base(bufferSize)
        {
            if (fixedLength <= 0)
                throw new ArgumentException("数据包固定长度必须大于 0。", nameof(fixedLength));

            _fixedLength = fixedLength;
        }

        /// <inheritdoc />
        protected override bool TryParse(ArraySegment<byte> pendingView, out int consumeCount)
        {
            consumeCount = 0;

            if (pendingView.Count < _fixedLength)
                return false;

            consumeCount = _fixedLength;
            return true;
        }
    }

    /// <summary>
    /// 尾部标记协议解析器。在缓冲区中搜索指定的尾部标记字节序列，
    /// 找到后将从缓冲区当前读位置到标记末尾的所有数据作为一个完整数据包提取。
    /// <para>使用场景：以固定后缀（如 0x0D 0x0A 换行符）结尾的文本协议。</para>
    /// </summary>
    public sealed class FooterProtocolParser : ProtocolParser
    {
        /// <summary>尾部标记字节序列（防御性拷贝）。</summary>
        private readonly byte[] _footer;

        /// <summary>
        /// 使用指定尾部标记和默认缓冲区大小初始化解析器。
        /// </summary>
        /// <param name="footer">标记数据包结束的尾部字节序列，不能为 null 或空数组。</param>
        /// <exception cref="ArgumentException"><paramref name="footer"/> 为 null 或空数组时抛出。</exception>
        public FooterProtocolParser(byte[] footer)
            : this(footer, DefaultBufferSize)
        {
        }

        /// <summary>
        /// 使用指定尾部标记和缓冲区大小初始化解析器。
        /// </summary>
        /// <param name="footer">标记数据包结束的尾部字节序列。</param>
        /// <param name="bufferSize">缓冲区最大字节数，超出后旧数据将被丢弃。</param>
        /// <exception cref="ArgumentException">
        /// <paramref name="footer"/> 为 null、空、或长度超过 <paramref name="bufferSize"/> 时抛出。
        /// </exception>
        public FooterProtocolParser(byte[] footer, int bufferSize)
            : base(bufferSize)
        {
            if (footer == null || footer.Length == 0)
                throw new ArgumentException("尾部标记不能为 null 或空数组。", nameof(footer));
            if (footer.Length >= bufferSize)
                throw new ArgumentException("尾部标记长度必须小于缓冲区大小。", nameof(footer));

            _footer = new byte[footer.Length];
            Array.Copy(footer, _footer, footer.Length);
        }

        /// <inheritdoc />
        protected override bool TryParse(ArraySegment<byte> pendingView, out int consumeCount)
        {
            consumeCount = 0;

            if (pendingView.Count < _footer.Length)
                return false;

            int footerIndex = IndexOf(pendingView, _footer);
            if (footerIndex < 0)
                return false;

            consumeCount = footerIndex + _footer.Length;
            return true;
        }
    }

    /// <summary>
    /// 头尾标记协议解析器。在缓冲区中先搜索头部标记，再从头部标记之后搜索尾部标记，
    /// 找到后将以头部标记为起始、尾部标记为结束的完整数据包提取。
    /// <para>注意：头部标记之前的垃圾数据将被丢弃（从读位置推进到头部标记处），
    /// 仅将头部标记到尾部标记末尾之间的数据作为完整包。</para>
    /// <para>使用场景：以特定起始/结束标记界定包边界的二进制协议，如某些串口通信帧格式。</para>
    /// </summary>
    public sealed class HeaderFooterProtocolParser : ProtocolParser
    {
        /// <summary>头部标记字节序列（防御性拷贝）。</summary>
        private readonly byte[] _header;

        /// <summary>尾部标记字节序列（防御性拷贝）。</summary>
        private readonly byte[] _footer;

        /// <summary>
        /// 使用指定头尾标记和默认缓冲区大小初始化解析器。
        /// </summary>
        /// <param name="header">标记数据包开始的头部字节序列。</param>
        /// <param name="footer">标记数据包结束的尾部字节序列。</param>
        /// <exception cref="ArgumentException"><paramref name="header"/> 或 <paramref name="footer"/> 无效时抛出。</exception>
        public HeaderFooterProtocolParser(byte[] header, byte[] footer)
            : this(header, footer, DefaultBufferSize)
        {
        }

        /// <summary>
        /// 使用指定头尾标记和缓冲区大小初始化解析器。
        /// </summary>
        /// <param name="header">标记数据包开始的头部字节序列。</param>
        /// <param name="footer">标记数据包结束的尾部字节序列。</param>
        /// <param name="bufferSize">缓冲区最大字节数，超出后旧数据将被丢弃。</param>
        /// <exception cref="ArgumentException">
        /// <paramref name="header"/> 或 <paramref name="footer"/> 为 null/空数组，
        /// 或任一标记长度超过 <paramref name="bufferSize"/> 时抛出。
        /// </exception>
        public HeaderFooterProtocolParser(byte[] header, byte[] footer, int bufferSize)
            : base(bufferSize)
        {
            if (header == null || header.Length == 0)
                throw new ArgumentException("头部标记不能为 null 或空数组。", nameof(header));
            if (footer == null || footer.Length == 0)
                throw new ArgumentException("尾部标记不能为 null 或空数组。", nameof(footer));
            if (header.Length >= bufferSize)
                throw new ArgumentException("头部标记长度必须小于缓冲区大小。", nameof(header));
            if (footer.Length >= bufferSize)
                throw new ArgumentException("尾部标记长度必须小于缓冲区大小。", nameof(footer));

            _header = new byte[header.Length];
            Array.Copy(header, _header, header.Length);

            _footer = new byte[footer.Length];
            Array.Copy(footer, _footer, footer.Length);
        }

        /// <inheritdoc />
        protected override bool TryParse(ArraySegment<byte> pendingView, out int consumeCount)
        {
            consumeCount = 0;

            if (pendingView.Count < _header.Length + _footer.Length)
                return false;

            // 搜索头部标记
            int headerIndex = IndexOf(pendingView, _header);
            if (headerIndex < 0)
                return false;

            // 从头部标记之后搜索尾部标记
            int footerIndex = IndexOf(pendingView, _footer, headerIndex + _header.Length);
            if (footerIndex < 0)
                return false;

            // consumeCount 从 pendingView 头部开始：头部标记位置 + 到尾部标记末尾
            consumeCount = footerIndex + _footer.Length;
            return true;
        }
    }

#endif
}
