using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SpaceCG.Extensions;
using Trace = SpaceCG.Diagnostics.Trace;

namespace SpaceCG.Generic
{
    /// <summary>
    /// 数据包事件参数，在 <see cref="ProtocolParser"/> 解析到完整数据包时
    /// 通过 <see cref="ProtocolParser.PacketReceived"/> 事件传递。
    /// <para>注意：<see cref="Packet"/> 直接引用内部缓冲区内存，为零拷贝视图。
    /// 事件回调返回后内部缓冲区可能被覆盖，若需长期持有数据请在回调内自行拷贝。</para>
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
    /// <para>外部通过 <see cref="Write(byte[], int, int)"/> 或 <see cref="ReadFromAsync"/> 
    /// 写入原始字节数据（生产者），内部通过 <see cref="Parse"/> 匹配完整数据包并通过 
    /// <see cref="PacketReceived"/> 事件抛出（消费者）。</para>
    /// <para>内部使用线性缓冲区（Linear Buffer）模式管理字节数据：
    /// 以 <c>byte[]</c> + 读写双指针（<c>_readPosition</c>、<c>_writePosition</c>）实现零拷贝数据视图，
    /// 当尾部可用空间不足时触发紧凑（Compact）将未消费数据移到缓冲区头部。</para>
    /// <para>当缓冲区累积超过容量限制且无完整数据包时，将清空全部数据以防止内存无限增长。</para>
    /// <para>子类需实现 <see cref="Parse"/> 返回匹配到的数据包视图（<see cref="ArraySegment{T}"/>），
    /// 返回 <c>default</c> 表示未找到完整包。</para>
    /// <para>线程安全：线程安全。所有写入和解析操作通过内部信号量（<see cref="SemaphoreSlim"/>）序列化。
    /// ⚠ <b>注意</b>：<see cref="PacketReceived"/> 事件在锁内触发，事件回调中<b>绝对不可</b>再次调用本类的 
    /// <see cref="Write"/>、<see cref="ReadFromAsync"/>、<see cref="Clear"/> 或 <see cref="ClearAsync"/> 方法，
    /// 否则会因 <see cref="SemaphoreSlim"/> 不支持重入而导致死锁。</para>
    /// </summary>
    public abstract class ProtocolParser : IDisposable
    {
        private readonly byte[] _buffer;
        private readonly int _bufferSize;
        private readonly int _compactThreshold;

        /// <summary>读写信号量，序列化所有对缓冲区的访问操作。</summary>
        private readonly SemaphoreSlim _syncSemaphore = new SemaphoreSlim(1, 1);

        /// <summary>标记对象是否已被释放。</summary>
        private bool _disposed;
        /// <summary>读指针：指向缓冲区中待解析数据的起始位置。</summary>
        private int _readPosition;
        /// <summary>写指针：指向缓冲区中下一个可写入数据的位置。</summary>
        private int _writePosition;
        /// <summary>
        /// 获取缓冲区中当前待解析的字节数。当锁被占用时返回近似值，仅用于监控/日志，不可用于精确控制逻辑。
        /// <para>尝试非阻塞获取锁以读取精确值；若锁被占用则返回近似值（非线程安全读取），避免阻塞调用线程。</para>
        /// </summary>
        public int Available
        {
            get
            {
                if (_disposed) throw new ObjectDisposedException(nameof(ProtocolParser));
                // 非阻塞尝试获取锁，避免线程池饥饿
                if (_syncSemaphore.Wait(0))
                {
                    try { return _writePosition - _readPosition; }
                    finally { _syncSemaphore.Release(); }
                }
                return Math.Max(0, _writePosition - _readPosition);
            }
        }        
        /// <summary>获取当前读指针位置。</summary>
        public int ReadPosition => _readPosition;
        /// <summary>获取当前写指针位置。</summary>
        public int WritePosition => _writePosition;
        /// <summary>获取内部缓冲区数组引用（供子类在事件回调中访问）。</summary>
        internal byte[] InternalBuffer => _buffer;

        /// <summary>
        /// 每当成功解析出一条完整数据包时触发。
        /// <para>事件参数中的 <see cref="PacketEventArgs.Packet"/> 是子类 <see cref="Parse"/> 返回的零拷贝视图，
        /// 直接引用内部缓冲区内存。事件回调返回后内部缓冲区可能被覆盖，若需长期持有请在回调内自行拷贝。</para>
        /// </summary>
        public event EventHandler<PacketEventArgs> PacketReceived;

        /// <summary> 构造函数  </summary>
        public ProtocolParser() : this(1024)
        {
        }

        /// <summary>
        /// 使用指定缓冲区大小初始化解析器。
        /// </summary>
        /// <param name="bufferSize">
        /// 缓冲区最大字节数。紧凑阈值自动设为 <c>bufferSize / 8</c>，超出后旧数据将被丢弃。必须大于 0。
        /// </param>
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
        /// 将字节数组的指定范围写入缓冲区，并立即尝试解析完整数据包。
        /// <para>写入前自动整理缓冲区：归零空缓冲区、紧凑碎片数据、清空满载缓冲区。</para>
        /// <para>按缓冲区尾部剩余空间写入，能写多少就写多少。调用者应根据返回值判断是否需要重试剩余数据。</para>
        /// </summary>
        /// <param name="data">包含源数据的字节数组。</param>
        /// <param name="offset"><paramref name="data"/> 中开始复制的起始索引（从 0 开始）。</param>
        /// <param name="count">要写入的字节数。</param>
        /// <returns>实际写入缓冲区的字节数。可能小于 <paramref name="count"/>（当缓冲区尾部空间不足时）。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="data"/> 为 null 时抛出。</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="offset"/> 或 <paramref name="count"/> 超出数组范围时抛出。</exception>
        public int Write(byte[] data, int offset, int count)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(ProtocolParser));
            if (data == null || data.Length == 0 || count == 0) return 0;
            if (offset < 0 || offset > data.Length)
                throw new ArgumentOutOfRangeException(nameof(offset));
            if (count < 0 || offset + count > data.Length)
                throw new ArgumentOutOfRangeException(nameof(count));

            _syncSemaphore.Wait();

            try
            {
                CompactBufferBeforeWrite();

                // 按缓冲区尾部剩余空间写入，能写多少就写多少
                var bytesToWrite = Math.Min(count, _bufferSize - _writePosition);

                Buffer.BlockCopy(data, offset, _buffer, _writePosition, bytesToWrite);
                _writePosition += bytesToWrite;

                // 先尝试解析已有数据，释放空间
                ParseBufferInternal();

                return bytesToWrite;
            }
            finally
            {
                _syncSemaphore.Release();
            }
        }

        /// <summary>
        /// 从流中异步循环读取数据到缓冲区并解析，直到流读完或被取消。
        /// <para>每次读取后立即尝试解析，解析出的数据包通过 <see cref="PacketReceived"/> 事件抛出。</para>
        /// </summary>
        /// <param name="stream">要读取的输入流，必须支持读取（<see cref="Stream.CanRead"/> 为 <c>true</c>）。</param>
        /// <param name="cancellationToken">用于取消读取操作的令牌。</param>
        /// <exception cref="ArgumentNullException"><paramref name="stream"/> 为 null 时抛出。</exception>
        /// <exception cref="ArgumentException"><paramref name="stream"/> 不支持读取时抛出。</exception>
        /// <exception cref="OperationCanceledException">操作被取消或信号量等待被中断时抛出。</exception>
        public async Task ReadFromAsync(Stream stream, CancellationToken cancellationToken = default)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(ProtocolParser));
            if (stream == null)
                throw new ArgumentNullException(nameof(stream));
            if (!stream.CanRead)
                throw new ArgumentException("流不支持读取操作。", nameof(stream));

            while (!cancellationToken.IsCancellationRequested)
            {
                // 获取锁，在锁内整理缓冲区和读取数据
                await _syncSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

                try
                {
                    CompactBufferBeforeWrite();

                    int bytesRead = await stream.ReadAsync(_buffer, _writePosition, _bufferSize - _writePosition, cancellationToken).ConfigureAwait(false);
                    if (bytesRead == 0) break; // 流已读完

                    _writePosition += bytesRead;

                    // 尝试解析已有数据
                    ParseBufferInternal();
                }
                finally
                {
                    _syncSemaphore.Release();
                }
            }

            await _syncSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ParseBufferInternal();
            }
            finally
            {
                _syncSemaphore.Release();
            }
        }

        /// <summary>
        /// 写入前整理缓冲区：归零空缓冲区、紧凑碎片数据、清空满载缓冲区。
        /// <para>调用方必须已持有 <see cref="_syncSemaphore"/>。</para>
        /// </summary>
        private void CompactBufferBeforeWrite()
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
                Buffer.BlockCopy(_buffer, _readPosition, _buffer, 0, pendingLength);
                Trace.TraceInformation($"整理缓冲区，移动 {pendingLength} bytes");

                _readPosition = 0;
                _writePosition = pendingLength;
            }

            // 2. 防御性处理：如果缓冲区依然满了（说明单条消息超大或恶意攻击），清空以防死锁
            if (_writePosition == _bufferSize)
            {
                var pendingLength = _writePosition - _readPosition;
                Trace.TraceWarning($"客户端缓冲区已满且无完整消息，丢弃 {pendingLength} bytes。请检查协议或数据源行为。");

                _readPosition = 0;
                _writePosition = 0;
            }
        }

        /// <summary>
        /// 内部解析循环：反复调用子类的 <see cref="Parse"/> 查找完整数据包，
        /// 找到后通过 <see cref="PacketReceived"/> 事件抛出零拷贝视图，并推进读指针。
        /// <para>调用方必须已持有 <see cref="_syncSemaphore"/>。</para>
        /// <para>解析循环退出条件：无更多待解析数据、子类返回 <c>default</c>。</para>
        /// </summary>
        private void ParseBufferInternal()
        {
            while (_readPosition < _writePosition)
            {
                var pendingView = new ArraySegment<byte>(_buffer, _readPosition, _writePosition - _readPosition);

                var packet = Parse(pendingView);
                if (packet.Count == 0) break;

                // 推进读指针到数据包末尾（消费含包前垃圾在内的所有数据）
                _readPosition = packet.Offset + packet.Count;

                try
                {
                    // 触发事件，消费者可在回调中使用 Packet 零拷贝视图
                    PacketReceived?.Invoke(this, new PacketEventArgs(packet));
                }
                catch (Exception ex)
                {
                    // 记录异常但继续解析循环，防止单个错误回调阻塞后续数据包
                    Trace.TraceError($"PacketReceived 事件回调抛出异常: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// 尝试从待解析数据视图中匹配一条完整数据包。由子类实现具体协议逻辑。
        /// <para>返回的 <see cref="ArraySegment{T}"/> 直接引用内部缓冲区内存（零拷贝），
        /// 基类将其作为 <see cref="PacketEventArgs"/> 通过 <see cref="PacketReceived"/> 事件抛出。</para>
        /// <para>基类会根据返回视图的 <see cref="ArraySegment{T}.Offset"/> 和 <see cref="ArraySegment{T}.Count"/>
        /// 自动推进读指针，消费从 <paramref name="pendingView"/> 头部到数据包末尾的所有字节。
        /// 若子类返回的视图不包含头部垃圾数据，这些垃圾数据在读指针推进时被自动跳过。</para>
        /// <para>此方法在 <see cref="_syncSemaphore"/> 锁内调用，子类实现应保持轻量，
        /// 避免执行耗时操作阻塞其他写入请求。</para>
        /// </summary>
        /// <param name="pendingView">待解析数据的连续零拷贝视图，仅在本方法调用期间有效。</param>
        /// <returns>
        /// 匹配到的完整数据包视图（零拷贝，引用内部缓冲区）；
        /// 返回 <c>default</c>（<see cref="ArraySegment{T}.Count"/> 为 0）表示未找到完整数据包，需要等待更多数据。
        /// </returns>
        protected abstract ArraySegment<byte> Parse(ArraySegment<byte> pendingView);

        /// <summary>
        /// 清空缓冲区中的所有数据，读写指针归零，恢复初始状态。
        /// <para>注意：清空后缓冲区中未解析的数据将丢失。</para>
        /// <para>⚠️ <b>警告</b>：此方法为同步阻塞调用。<b>绝对不可</b>在 <see cref="PacketReceived"/> 事件回调中调用此方法，
        /// 否则会导致当前线程死锁（因当前线程已持有内部信号量且 SemaphoreSlim 不支持重入）。</para>
        /// </summary>
        public void Clear()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(ProtocolParser));

            _syncSemaphore.Wait();
            try
            {
                _readPosition = 0;
                _writePosition = 0;
            }
            finally
            {
                _syncSemaphore.Release();
            }
        }

        /// <summary>
        /// 异步清空缓冲区中的所有数据，读写指针归零，恢复初始状态。
        /// <para>注意：清空后缓冲区中未解析的数据将丢失。</para>
        /// <para>⚠ <b>警告</b>：<b>绝对不可</b>在 <see cref="PacketReceived"/> 事件回调中调用此方法，
        /// 否则会导致当前线程死锁（因当前线程已持有内部信号量且 SemaphoreSlim 不支持重入）。</para>
        /// </summary>
        /// <param name="cancellationToken">用于取消操作的令牌。</param>
        public async Task ClearAsync(CancellationToken cancellationToken = default)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(ProtocolParser));
            await _syncSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                _readPosition = 0;
                _writePosition = 0;
            }
            finally
            {
                _syncSemaphore.Release();
            }
        }

        /// <summary>
        /// 释放由 <see cref="ProtocolParser"/> 占用的非托管资源，并可选择释放托管资源。
        /// </summary>
        /// <param name="disposing">如果为 true，则释放托管资源和非托管资源；如果为 false，则仅释放非托管资源。</param>
        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    // 防御性编程：清空事件订阅，防止因外部未取消订阅导致的内存泄漏
                    PacketReceived = null;
                    // 释放托管资源
                    _syncSemaphore?.Dispose();                    
                }

                _disposed = true;
            }
        }

        /// <summary>
        /// 执行与释放或重置非托管资源关联的应用程序定义的任务。
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this); // 告诉 GC 不需要再调用终结器（如果有的话）
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
