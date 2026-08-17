using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using SpaceCG.Extensions;
using Trace = SpaceCG.Diagnostics.Trace;

namespace SpaceCG.IO
{
    /// <summary>
    /// 响应帧完整性判定器委托。
    /// 用于在接收缓冲中判断一条响应是否已完整接收，并返回完整响应的字节长度。
    /// </summary>
    /// <param name="buffer">当前已接收的数据缓冲区（仅包含有效数据）。</param>
    /// <returns>
    /// 完整响应的字节长度（&gt; 0）；若数据尚未构成完整响应，返回 -1。
    /// </returns>
    /// <remarks>
    /// 该方法会在每次收到新数据后被重复调用，应保持轻量、无副作用，
    /// 避免在此委托中分配对象或执行耗时操作。
    /// </remarks>
    public delegate int ResponseFramePredicate(ArraySegment<byte> buffer);

    /// <summary>
    /// 表示支持 "请求-响应" 通信模式的客户端会话对象。
    /// 一次完整通信 = 发送一条请求数据 + 等待一条对应响应，严格按照串行 FIFO 顺序处理。
    /// 响应边界由固定长度或 <see cref="ResponseFramePredicate"/> 判定器确定。
    /// </summary>
    /// <remarks>
    /// <para><b>并发模型</b>：本类采用"单后台线程 + 严格串行 FIFO 队列"模型。
    /// 每次仅处理一条请求，前一条完成（收到响应、超时或取消）后才处理下一条，
    /// 因此多个调用方并发提交的 <see cref="TransceiveAsync(byte[], int, int, ResponseFramePredicate, CancellationToken)"/> 会被顺序串行执行。</para>
    /// <para><b>两种交互模式</b>：
    /// <see cref="WriteAsync"/>（只写，发射后即忘，可配置帧间延迟 <see cref="WriteFrameDelay"/>）；
    /// <see cref="TransceiveAsync(byte[], int, int, ResponseFramePredicate, CancellationToken)"/>（写读，请求后等待 ACK 响应，可配置超时 <see cref="ResponseTimeout"/>）。</para>
    /// <para><b>线程安全级别</b>：线程安全（入队操作由 ConcurrentQueue 保证；实际发送与读取在单一后台线程上执行）。</para>
    /// </remarks>
    public class RequestResponseSession : IDisposable
    {
        private enum InteractionMode
        {
            /// <summary> 只写：发送完成即回报，不等响应。 </summary>
            WriteOnly,

            /// <summary> 写读：发送后等待 ACK 响应字节并回报。 </summary>
            RequestResponse,
        }

        /// <summary>
        /// 待处理的请求条目，封装一次"只写"或"写读"交互所需的全部数据与完成信号。
        /// </summary>
        /// <remarks>
        /// <see cref="CompletionSource"/> 为 null 表示"只写"模式（只发不等响应）；
        /// 非 null 表示"写读"模式，通过该 TaskCompletionSource 向等待方回报结果或异常。
        /// </remarks>
        private class Pending
        {
            /// <summary> 全局自增的请求 Id 分配器，使用 Interlocked 保证跨线程递增唯一。 </summary>
            private static int _id;
            /// <summary> 请求 Id，用于日志与异常定位。 </summary>
            public int Id { get; }
            /// <summary> 待发送的请求数据缓冲区。 </summary>
            public byte[] Data { get; set; }
            /// <summary> <see cref="Data"/> 中开始发送的字节偏移量。 </summary>
            public int Offset { get; set; }
            /// <summary> 从 <see cref="Data"/> 中发送的字节数。 </summary>
            public int Length { get; set; }

            public InteractionMode Mode { get; }

            /// <summary> 只写模式下，发送完成后的帧间延迟（毫秒）。 </summary>
            public int WriteFrameDelay { get; set; } = 20;
            /// <summary> 写读模式下，等待完整响应的时间上限（毫秒）。 </summary>
            public int ResponseTimeout { get; set; } = 300;
            /// <summary> 写读模式等待期间可取消的令牌；只写模式不使用。 </summary>
            public CancellationToken CancellationToken { get; set; }
            /// <summary> 写读模式的响应完整性判定器；只写模式为 null。 </summary>
            public ResponseFramePredicate FramePredicate { get; set; }
            /// <summary> 完成信号源；null 表示只写模式。 </summary>
            public TaskCompletionSource<byte[]> CompletionSource { get; set; }

            public Pending(InteractionMode mode)
            {
                Mode = mode;
                Id = GetRequestId();
            }

            public void SetResult(byte[] result)
            {
                CompletionSource?.SetResult(result);
            }

            public void SetException(Exception exception)
            {
                CompletionSource?.SetException(exception);
            }

            private int GetRequestId()
            {
                var id = Interlocked.Increment(ref _id);
                if (id > 0) return id;

                // 溢出到负数或零时，通过 CAS 重置为 1 并重新获取
                if (id <= 0)
                {
                    Interlocked.CompareExchange(ref _id, 0, id);
                    id = Interlocked.Increment(ref _id);
                }
                return id;
            }
        }

        /// <summary>
        /// 只写模式下的帧间延迟（毫秒），即在发送无响应帧（如广播帧、无回包指令）后，
        /// 处理线程等待的时间。用于避免设备因连续写入而来不及处理帧数据。
        /// </summary>
        /// <value>取值范围 [1, 1000]，默认 16 毫秒。</value>
        /// <exception cref="ArgumentOutOfRangeException">值不在 [1, 1000] 范围内时抛出。</exception>
        public int WriteFrameDelay
        {
            get { return _writeFrameDelay; }
            set
            {
                if (value < 1 || value > 1000)
                    throw new ArgumentOutOfRangeException($"{nameof(WriteFrameDelay)} 必须在 1-1000 之间.");
                _writeFrameDelay = value;
            }
        }
        private int _writeFrameDelay = 16;
        /// <summary>
        /// 写读模式下的响应超时时间（毫秒），超过该时间未收到完整响应将抛出 <see cref="TimeoutException"/>。
        /// </summary>
        /// <value>取值范围 [1, 1000]，默认 200 毫秒。</value>
        /// <exception cref="ArgumentOutOfRangeException">值不在 [1, 1000] 范围内时抛出。</exception>
        public int ResponseTimeout
        {
            get { return _responseTimeout; }
            set
            {
                if (value < 1 || value > 1000)
                    throw new ArgumentOutOfRangeException($"{nameof(ResponseTimeout)} 必须在 1-1000 之间.");
                _responseTimeout = value;
            }
        }
        private int _responseTimeout = 200;

        private Task _processTask;
        private CancellationTokenSource _cts;
        private ITransportChannel _transportChannel;
        private readonly ConcurrentQueue<Pending> _pendingQueue = new ConcurrentQueue<Pending>();

        /// <summary>
        /// 构造函数。
        /// </summary>
        /// <param name="type"></param>
        /// <param name="arguments"></param>
        public RequestResponseSession(ChannelType type, string arguments)
        {
            _transportChannel = TransportChannel.Create(type, arguments);
            _transportChannel.ReadTimeout = 300;
            _transportChannel.WriteTimeout = 300;

            try
            {
                _transportChannel.Open();
            }
            catch (Exception ex)
            {
                Trace.TraceWarning($"通道 ({_transportChannel.Name}) 连接异常：{ex.Message}");
            }

            _cts = new CancellationTokenSource();
            _processTask = Task.Factory.StartNew(ProcessLoopThread, this, _cts.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }

        /// <summary>
        /// 后台处理线程主体：负责连接保持、按 FIFO 顺序从队列取出请求、发送数据，
        /// 并在写读模式下读取并解析完整响应帧后回报结果。
        /// </summary>
        /// <param name="state">宿主 <see cref="RequestResponseSession"/> 实例。</param>
        /// <remarks>
        /// 该线程是"严格串行 FIFO"的核心：同一时刻仅处理一条请求。
        /// 对每个请求重置读/写指针，因此不保留跨请求的残留响应数据（适用于"一问一答"协议）。
        /// </remarks>
        private static void ProcessLoopThread(object state)
        {
            var requestResponse = (RequestResponseSession)state;

            var channel = requestResponse._transportChannel;
            var channelName = channel.Name;
            var readTimeout = channel.ReadTimeout;

            var spinWait = new SpinWait();
            var stopwatch = new Stopwatch();
            var requestQueue = requestResponse._pendingQueue;
            var cancellationToken = requestResponse._cts.Token;

            var readPosition = 0;
            var writePosition = 0;
            const int BufferSize = 1024;
            var frameBuffer = new byte[BufferSize];

            while (!cancellationToken.IsCancellationRequested)
            {
                #region 连接状态检查
                while (!channel.IsConnected && !cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        channel.Close();

                        Thread.Sleep(10);
                        if (cancellationToken.IsCancellationRequested) break;

                        channel.Open();
                    }
                    catch (Exception ex)
                    {
                        Trace.TraceWarning($"通道 ({channelName}) 连接异常：{ex.Message}");
                    }

                    if (!channel.IsConnected)
                    {
                        // 等待 3 秒后尝试重连，分段 Sleep 避免 Stop/Close/Dispose 等待超时
                        for (int i = 0; i < 100; i++)
                        {
                            Thread.Sleep(30);
                            if (cancellationToken.IsCancellationRequested) break;
                        }
                    }
                }
                #endregion

                if (requestQueue.IsEmpty || !requestQueue.TryDequeue(out var request))
                {
                    Thread.Sleep(1);
                    continue;
                }

                readPosition = 0;
                writePosition = 0;
                if (request.CancellationToken.IsCancellationRequested) continue;

                #region Write：先发送请求数据。
                channel.Write(request.Data, request.Offset, request.Length);
                if (request.Mode == InteractionMode.WriteOnly)
                {
                    if (request.WriteFrameDelay > 0)
                        Thread.Sleep(request.WriteFrameDelay);
                    request.CompletionSource.TrySetResult(Array.Empty<byte>());
                    continue;
                }
                #endregion

                #region Read：再读取响应数据。
                spinWait.Reset();
                stopwatch.Restart();
                var responseTimeout = Math.Max(0, request.ResponseTimeout);
                if (responseTimeout <= 0) responseTimeout = readTimeout > 0 ? readTimeout : 200;

                while (true)
                {                
                    if (stopwatch.ElapsedMilliseconds >= responseTimeout)
                    {
                        request.SetException(new TimeoutException($"请求 ID:{request.Id} 响应超时({channelName})"));
                        break;
                    }
                    if (cancellationToken.IsCancellationRequested || request.CancellationToken.IsCancellationRequested)
                    {
                        request.SetException(new OperationCanceledException($"请求 ID:{request.Id} 被取消({channelName})"));
                        break;
                    }

                    int available = channel.Available;
                    if (available <= 0)
                    {
                        spinWait.SpinOnce();
                        continue;
                    }

                    spinWait.Reset();

                    var readCount = Math.Min(available, frameBuffer.Length - writePosition);
                    var bytesToRead = channel.Read(frameBuffer, writePosition, readCount);

                    if (bytesToRead <= 0) continue;
                    writePosition += bytesToRead;

                    var pendingView = new ArraySegment<byte>(frameBuffer, readPosition, writePosition - readPosition);
                    var frameLength = request.FramePredicate(pendingView);
                    if (frameLength > 0)
                    {
                        var responseBuffer = new byte[frameLength];
                        Buffer.BlockCopy(frameBuffer, readPosition, responseBuffer, 0, frameLength);

                        readPosition += frameLength;
                        request.SetResult(responseBuffer);
                        break;
                    }
                }
                #endregion
            }

            stopwatch.Stop();
        }

        /// <summary>
        /// 只写模式：将数据入队并异步发送，发送后即返回，不等待设备响应（发射后即忘）。
        /// </summary>
        /// <param name="data">待发送的数据缓冲区。</param>
        /// <param name="offset"><paramref name="data"/> 中开始发送的字节偏移量。</param>
        /// <param name="length">从 <paramref name="data"/> 中发送的字节数。</param>
        /// <param name="cancellationToken">取消操作的通知。</param>
        /// <returns>表示异步入队操作的任务；注意该方法仅将数据写入队列，
        /// 实际发送由后台处理线程执行，返回的 Task 不代表底层通道已完成写入。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="data"/> 为 null 或长度为 0。</exception>
        public async Task WriteAsync(byte[] data, int offset, int length, CancellationToken cancellationToken)
        {
            if (_transportChannel == null || !_transportChannel.IsConnected)
                throw new InvalidOperationException($"传输通道 {_transportChannel?.Name} 未连接。");

            var pending = new Pending(InteractionMode.WriteOnly);
            pending.Data = data;
            pending.Offset = offset;
            pending.Length = length;
            pending.WriteFrameDelay = WriteFrameDelay;
            pending.CancellationToken = cancellationToken;

            var completionSource = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
            pending.CompletionSource = completionSource;

            _pendingQueue.Enqueue(pending);

            await completionSource.Task.ConfigureAwait(false);
        }

        /// <summary>
        /// 发送请求数据，并异步等待由 <paramref name="framePredicate"/> 判定边界的响应。
        /// </summary>
        /// <param name="data">请求数据缓冲区。</param>
        /// <param name="offset"><paramref name="data"/> 中开始发送的字节偏移量。</param>
        /// <param name="length">从 <paramref name="data"/> 中发送的字节数。</param>
        /// <param name="framePredicate">响应完整性判定器。</param>
        /// <param name="cancellationToken">取消操作的通知。</param>
        /// <returns>接收到的完整响应数据。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="data"/> 或 <paramref name="framePredicate"/> 为 <c>null</c>。</exception>
        /// <exception cref="ArgumentException"><paramref name="offset"/> 或 <paramref name="length"/> 越界。</exception>
        public async Task<byte[]> TransceiveAsync(byte[] data, int offset, int length, ResponseFramePredicate framePredicate, CancellationToken cancellationToken)
        {
            if (_transportChannel == null || !_transportChannel.IsConnected)
                throw new InvalidOperationException($"传输通道 {_transportChannel?.Name} 未连接。");

            if (data == null || data.Length == 0) throw new ArgumentNullException(nameof(data));
            if (offset < 0 || offset >= data.Length) throw new ArgumentOutOfRangeException(nameof(offset));
            if (length <= 0 || offset + length > data.Length) throw new ArgumentOutOfRangeException(nameof(length));
            if (framePredicate == null) throw new ArgumentNullException(nameof(framePredicate));
            
            var pending = new Pending(InteractionMode.RequestResponse);
            pending.Data = data;
            pending.Offset = offset;
            pending.Length = length;
            pending.FramePredicate = framePredicate;
            pending.CancellationToken = cancellationToken;

            var completionSource = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
            pending.CompletionSource = completionSource;

            _pendingQueue.Enqueue(pending);

            return await completionSource.Task.ConfigureAwait(false);
        }

        /// <inheritdoc cref="TransceiveAsync(byte[], int, int, ResponseFramePredicate, CancellationToken)"/>
        public async Task<byte[]> TransceiveAsync(byte[] data, ResponseFramePredicate framePredicate, CancellationToken cancellationToken)
            => await TransceiveAsync(data, 0, data.Length, framePredicate, cancellationToken);

        /// <summary>
        /// 发送请求数据，并异步等待固定长度 <paramref name="fixedLength"/> 字节的响应。
        /// 本重载等价于使用"缓冲区长度 &gt;= 响应长度即完整"的 <see cref="ResponseFramePredicate"/>。
        /// </summary>
        /// <inheritdoc cref="TransceiveAsync(byte[], int, int, ResponseFramePredicate, CancellationToken)"/>
        public async Task<byte[]> TransceiveAsync(byte[] data, int offset, int length, int fixedLength, CancellationToken cancellationToken)
        {
            if (fixedLength <= 0) 
                throw new ArgumentOutOfRangeException(nameof(fixedLength));
            return await TransceiveAsync(data, offset, length, (buffer) => ((buffer.Count >= fixedLength) ? fixedLength : -1), cancellationToken);
        }

        /// <inheritdoc cref="TransceiveAsync(byte[], int, int, ResponseFramePredicate, CancellationToken)"/>
        public async Task<byte[]> TransceiveAsync(byte[] data, int offset, int length, byte[] footer, CancellationToken cancellationToken)
        {
            if (footer == null || footer.Length == 0) 
                throw new ArgumentNullException(nameof(footer));

            return await TransceiveAsync(data, offset, length, (buffer) =>
            {
                if (buffer.Count < footer.Length) return -1;

                var index = buffer.LastIndexOf(footer);
                if (index < 0) return -1;

                return index + footer.Length;
            }, cancellationToken);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _cts?.Cancel();
            _pendingQueue.Clear((item) =>
            {
                item.CompletionSource?.TrySetCanceled();
            });

            try
            {
                _processTask?.Wait(300);
                _processTask?.Dispose();
            }
            finally
            {
                _processTask = null;
            }

            try
            {
                _transportChannel?.Dispose();
            }
            finally
            {
                _transportChannel = null;
            }

            try
            {
                _cts?.Dispose();
            }
            finally
            {
                _cts = null;
            }
        }

    }

}
