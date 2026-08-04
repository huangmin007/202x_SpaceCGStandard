using System;
using System.Collections.Generic;
using SpaceCG.Extensions;
using Trace = SpaceCG.Diagnostics.Trace;

namespace SpaceCG.Generic
{
    /// <summary>
    /// 数据包事件参数。Packet 直接引用内部缓冲区内存，为零拷贝视图。
    /// 事件回调返回后缓冲区可能被覆盖，如需长期持有请在回调内自行拷贝
    /// </summary>
    public class PacketEventArgs : EventArgs
    {
        /// <summary>
        /// 完整数据包在内部缓冲区中的零拷贝视图。
        /// <para>此视图仅在事件回调期间有效，回调返回后可能被覆盖。</para>
        /// </summary>
        public ArraySegment<byte> Packet { get; }

        /// <summary>
        /// 使用内部缓冲区的零拷贝视图初始化事件参数。
        /// </summary>
        /// <param name="packet">数据包在内部缓冲区中的零拷贝视图。</param>
        public PacketEventArgs(ArraySegment<byte> packet)
        {
            Packet = packet;
        }
    }

    /// <summary>
    /// 数据协议解析器抽象基类。采用生产者-消费者模式：
    /// 外部写入原始字节数据，内部匹配完整数据包并通过 PacketReceived 事件抛出。
    /// <para>内部使用线性缓冲区 + 读写双指针，当尾部空间不足时自动紧凑。
    /// 缓冲区满载且无完整包时清空全部数据以防内存无限增长。</para>
    /// <para>子类需实现 Parse 返回匹配到的数据包视图，返回 default 表示未找到完整包。</para>
    /// </summary>
    public abstract class ProtocolParser
    {
        private readonly byte[] _buffer;
        private readonly int _bufferSize;
        private readonly int _compactThreshold;
        private readonly object _syncLock = new object();

        /// <summary>读指针：指向缓冲区中待解析数据的起始位置。</summary>
        private int _readPosition;
        /// <summary>写指针：指向缓冲区中下一个可写入数据的位置。</summary>
        private int _writePosition;

        /// <summary> 缓冲区实际容量。</summary>
        public int Capacity => _bufferSize;
        /// <summary>获取内部缓冲区数组引用。</summary>
        protected internal byte[] Buffer => _buffer;

        /// <summary>获取当前读指针位置。</summary>
        public int ReadPosition
        {
            get => _readPosition;
            private set { lock (_syncLock) { _readPosition = value; } }
        }
        /// <summary>获取当前写指针位置。</summary>
        public int WritePosition
        {
            get => _writePosition;
            internal set
            {
                if (value < 0) throw new ArgumentOutOfRangeException(nameof(value), value, "写指针位置不能小于 0。");
                if (value > Capacity) throw new ArgumentOutOfRangeException(nameof(value), value, "写指针位置不能大于缓冲区容量。");

                lock (_syncLock) { _writePosition = value; }
            }
        }
        /// <summary> 获取缓冲区中当前待解析的字节数。 </summary>
        public int Available
        {
            get
            {
                lock (_syncLock) { return _writePosition - _readPosition; }
            }
        }

        /// <summary>
        /// 完整数据包时触发事件。
        /// </summary>
        public event EventHandler<PacketEventArgs> PacketReceived;

        /// <summary> 构造函数  </summary>
        public ProtocolParser() : this(1024)
        {
        }

        /// <summary>
        /// 使用指定缓冲区大小初始化解析器。
        /// </summary>
        /// <param name="bufferSize"> 缓冲区最大字节数,必须大于 0。紧凑阈值自动设为 <c>bufferSize / 4</c>。 </param>
        /// <exception cref="ArgumentException"><paramref name="bufferSize"/> 小于或等于 0 时抛出。</exception>
        public ProtocolParser(int bufferSize)
        {
            if (bufferSize <= 0)
                throw new ArgumentException("缓冲区大小必须大于 0。", nameof(bufferSize));

            _bufferSize = bufferSize;
            _compactThreshold = bufferSize / 4;

            _buffer = new byte[bufferSize];
        }

        /// <summary>
        /// 写入数据到缓冲区并立即尝试解析完整数据包。
        /// 按缓冲区尾部剩余空间写入，能写多少就写多少，调用者根据返回值判断是否需重试剩余数据。
        /// </summary>
        /// <param name="data">源字节数组。</param>
        /// <param name="offset">源数组起始索引。</param>
        /// <param name="count">要写入的字节数。</param>
        /// <returns>实际写入的字节数。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="data"/> 为 null 时抛出。</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="offset"/> 或 <paramref name="count"/> 超出数组范围时抛出。</exception>
        public int Write(byte[] data, int offset, int count)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));
            if (data.Length == 0 || count <= 0) return 0;
            if (offset < 0 || offset + count > data.Length)
                throw new ArgumentOutOfRangeException();

            CompactBuffer();

            try
            {
                lock (_syncLock)
                {
                    var writeCount = Math.Min(count, _bufferSize - _writePosition);
                    System.Buffer.BlockCopy(data, offset, _buffer, _writePosition, writeCount);
                    _writePosition += writeCount;
                    
                    return writeCount;
                }
            }
            finally
            {
                ParseBuffer();
            }
        }
        /// <inheritdoc /> 
        public int Write(ArraySegment<byte> data) => Write(data.Array, data.Offset, data.Count);

        /// <summary>
        /// 整理缓冲区：归零空缓冲区、紧凑碎片数据、清空满载缓冲区。
        /// </summary>
        internal void CompactBuffer()
        {
            lock (_syncLock)
            {
                // 0. 数据正好分析完 → 所有指针归零
                if (_readPosition > _compactThreshold && _readPosition == _writePosition)
                {
                    _readPosition = 0;
                    _writePosition = 0;
                }
                // 1. 尾部剩余空间不足且前方有已消费空间 → 将未处理数据移到缓冲区头部
                else if (_readPosition > 0 && _bufferSize - _writePosition <= _compactThreshold)
                {
                    var pendingLength = _writePosition - _readPosition;
                    System.Buffer.BlockCopy(_buffer, _readPosition, _buffer, 0, pendingLength);
                    Trace.TraceInformation($"整理缓冲区，移动 {pendingLength} bytes");

                    _readPosition = 0;
                    _writePosition = pendingLength;
                }

                // 2. 防御性处理：如果缓冲区依然满了（说明单条消息超大或恶意攻击），清空以防死锁
                if (_writePosition == _bufferSize)
                {
                    var pendingLength = _writePosition - _readPosition;
                    Trace.TraceWarning($"缓冲区已满且无完整消息，丢弃 {pendingLength} bytes。请检查协议或数据源行为。");

                    _readPosition = 0;
                    _writePosition = 0;
                }
            }
        }

        /// <summary>
        /// 解析缓冲区：匹配所有完整数据包，触发 PacketReceived 事件。
        /// </summary>
        internal void ParseBuffer()
        {
            //var matchedPackets = new List<ArraySegment<byte>>(8);
            lock (_syncLock)
            {
                while (_readPosition < _writePosition)
                {
                    var pendingView = new ArraySegment<byte>(_buffer, _readPosition, _writePosition - _readPosition);

                    var packet = Parse(pendingView);
                    if (packet.Count == 0) break;

                    //matchedPackets.Add(packet);
                     _readPosition = packet.Offset + packet.Count;

#if true
                    try
                    {
                        PacketReceived?.Invoke(this, new PacketEventArgs(packet));
                    }
                    catch (Exception ex)
                    {
                        Trace.TraceError($"PacketReceived 事件回调抛出异常: {ex.Message}");
                    }
#endif
                }
            }

#if false
            for (var i = 0; i < matchedPackets.Count; i++)
            {
                try
                {
                    PacketReceived?.Invoke(this, new PacketEventArgs(matchedPackets[i]));
                }
                catch (Exception ex)
                {
                    Trace.TraceError($"PacketReceived 事件回调抛出异常: {ex.Message}");
                }
            }
#endif
        }

        /// <summary>
        /// 尝试从待解析数据中匹配一条完整数据包，由子类实现。
        /// 返回的 ArraySegment 直接引用内部缓冲区（零拷贝）。
        /// 返回 default 表示未找到完整包，需等待更多数据。
        /// </summary>
        /// <param name="pendingView">待解析数据的零拷贝视图。</param>
        protected abstract ArraySegment<byte> Parse(ArraySegment<byte> pendingView);

        /// <summary>
        /// 清空缓冲区中的所有数据，读写指针归零，恢复初始状态。
        /// <para>注意：清空后缓冲区中未解析的数据将丢失。</para>
        /// </summary>
        public void Clear()
        {
            lock (_syncLock)
            {
                _readPosition = 0;
                _writePosition = 0;
            }
        }        
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
        public FixedLengthProtocolParser(int fixedLength) : this(fixedLength, 1024)
        {
        }

        /// <summary>
        /// 使用指定固定包长度和缓冲区大小初始化解析器。
        /// </summary>
        /// <param name="fixedLength">每个数据包的固定字节长度，必须大于 0。</param>
        /// <param name="bufferSize">缓冲区最大字节数，超出后旧数据将被丢弃。</param>
        /// <exception cref="ArgumentException"><paramref name="fixedLength"/> 或 <paramref name="bufferSize"/> 无效时抛出。</exception>
        public FixedLengthProtocolParser(int fixedLength, int bufferSize) : base(bufferSize)
        {
            if (fixedLength <= 0)
                throw new ArgumentException("数据包固定长度必须大于 0。", nameof(fixedLength));

            _fixedLength = fixedLength;
        }

        /// <inheritdoc />
        /// <remarks>当缓冲区中累积数据量达到 <see cref="_fixedLength"/> 时，
        /// 返回从视图头部开始、长度为 <see cref="_fixedLength"/> 的数据包视图。</remarks>
        protected override ArraySegment<byte> Parse(ArraySegment<byte> pendingView)
        {
            if (pendingView.Count < _fixedLength) return default;

            return new ArraySegment<byte>(pendingView.Array, pendingView.Offset, _fixedLength);
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
        public FooterProtocolParser(byte[] footer) : this(footer, 1024)
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
        public FooterProtocolParser(byte[] footer, int bufferSize) : base(bufferSize)
        {
            if (footer == null || footer.Length == 0)
                throw new ArgumentException("尾部标记不能为 null 或空数组。", nameof(footer));
            if (footer.Length >= bufferSize)
                throw new ArgumentException("尾部标记长度必须小于缓冲区大小。", nameof(footer));

            _footer = new byte[footer.Length];
            footer.CopyTo(_footer, 0);
        }

        /// <inheritdoc />
        /// <remarks>在缓冲区中搜索尾部标记，找到后返回从视图头部到标记末尾（含尾部标记）的数据包视图。</remarks>
        protected override ArraySegment<byte> Parse(ArraySegment<byte> pendingView)
        {
            if (pendingView.Count < _footer.Length) return default;

            int footerIndex = pendingView.IndexOf(_footer);
            if (footerIndex < 0) return default;

            return new ArraySegment<byte>(pendingView.Array, pendingView.Offset, footerIndex + _footer.Length);
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
        public HeaderFooterProtocolParser(byte[] header, byte[] footer) : this(header, footer, 1024)
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
            header.CopyTo(_header, 0);

            _footer = new byte[footer.Length];
            footer.CopyTo(_footer, 0);
        }

        /// <inheritdoc />
        /// <remarks>
        /// 先搜索头部标记，再从头部标记之后搜索尾部标记。
        /// 返回仅包含 [头部标记 .. 尾部标记末尾] 的数据包视图，头部标记之前的垃圾数据不包含在视图中，
        /// 但会被基类在读指针推进时自动消费跳过。
        /// </remarks>
        protected override ArraySegment<byte> Parse(ArraySegment<byte> pendingView)
        {
            if (pendingView.Count < _header.Length + _footer.Length)
                return default;

            // 搜索头部标记（返回相对于 pendingView 的偏移）
            int headerIndex = pendingView.IndexOf(_header);
            if (headerIndex < 0) return default;

            // 从头部标记之后搜索尾部标记
            var offset = headerIndex + _header.Length;
            var length = pendingView.Count - offset;

            // 构造子视图，复用 ArraySegment 的 IndexOf 自动处理索引转换
            var subView = new ArraySegment<byte>(pendingView.Array, pendingView.Offset + offset, length);
            int footerIndex = subView.IndexOf(_footer);
            if (footerIndex < 0) return default;

            //只返回 [headerIndex, footerIndex+footerLen] 的有效数据，不含头部垃圾
            var packetOffset = pendingView.Offset + headerIndex;
            var packetLength = offset + footerIndex + _footer.Length - headerIndex;
            return new ArraySegment<byte>(pendingView.Array, packetOffset, packetLength);
        }
    }

}
