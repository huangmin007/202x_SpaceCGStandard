using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO.Ports;
using System.Linq;
using System.Net;
using System.Runtime.CompilerServices;
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
    /// 表示支持"请求-响应"通信模式的客户端对象。
    /// 一次完整通信 = 发送一条请求数据 + 等待一条对应响应。
    /// 响应边界由固定长度或 <see cref="ResponseFramePredicate"/> 判定器确定。
    /// </summary>
    /// <remarks>
    /// 线程安全级别：由实现类自行声明；本接口不保证线程安全。
    /// </remarks>
    public interface IRequestResponseClient
    {
        /// <summary>
        /// 获取单次请求-响应通信的响应等待超时时间。
        /// </summary>
        /// <value>响应超时时间，超过该时间未收到完整响应将抛出 <see cref="TimeoutException"/>。</value>
        int ResponseTimeout { get; }

        /// <summary>
        /// 发送请求数据的指定区段，并阻塞等待由 <paramref name="framePredicate"/> 判定边界的响应。
        /// </summary>
        /// <param name="data">请求数据缓冲区。</param>
        /// <param name="offset"><paramref name="data"/> 中开始发送的字节偏移量。</param>
        /// <param name="length">从 <paramref name="data"/> 中发送的字节数。</param>
        /// <param name="framePredicate">响应完整性判定器。</param>
        /// <returns>接收到的完整响应数据。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="data"/> 或 <paramref name="framePredicate"/> 为 <c>null</c>。</exception>
        /// <exception cref="ArgumentException"><paramref name="offset"/> 或 <paramref name="length"/> 越界。</exception>
        /// <exception cref="TimeoutException">在 <see cref="ResponseTimeout"/> 内未收到完整响应。</exception>
        IReadOnlyList<byte> Transceive(byte[] data, int offset, int length, ResponseFramePredicate framePredicate);

        /// <summary>
        /// 异步发送完整请求数据，并等待由 <paramref name="framePredicate"/> 判定边界的响应。
        /// </summary>
        /// <param name="data">请求数据。</param>
        /// <param name="offset"><paramref name="data"/> 中开始发送的字节偏移量。</param>
        /// <param name="length">从 <paramref name="data"/> 中发送的字节数。</param>
        /// <param name="framePredicate">响应完整性判定器。</param>
        /// <param name="cancellationToken">用于取消等待的取消令牌。</param>
        /// <returns>表示异步操作的响应数据。</returns>
        /// <exception cref="OperationCanceledException">操作被取消。</exception>
        Task<IReadOnlyList<byte>> TransceiveAsync(byte[] data, int offset, int length, ResponseFramePredicate framePredicate, CancellationToken cancellationToken);
    }

    public class RequestResponseBase : IDisposable
    {
        private class Request
        {
            private static int _requestId;

            public int Id { get; }

            public byte[] Data { get; set; }

            public int Offset { get; set; }

            public int Length { get; set; }

            public int ResponseLength { get; set; }

            public CancellationToken CancellationSource { get; set; }

            public ResponseFramePredicate FramePredicate { get; set; }

            public TaskCompletionSource<byte[]> CompletionSource { get; set; }

            public Request()
            {
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
                var id = Interlocked.Increment(ref _requestId);
                if (id > 0) return id;

                // 溢出到负数或零时，通过 CAS 重置为 1 并重新获取
                if (id <= 0)
                {
                    Interlocked.CompareExchange(ref _requestId, 0, id);
                    id = Interlocked.Increment(ref _requestId);
                }
                return id;
            }
        }

        private Task _transportTask;
        private CancellationTokenSource _cts;
        private ITransportChannel _transportChannel;
        private readonly ConcurrentQueue<Request> _requestQueue = new ConcurrentQueue<Request>();

        public RequestResponseBase(ChannelType type, string arguments)
        {
            _transportChannel = TransportChannel.Create(type, arguments);
            _transportChannel.ReadTimeout = 300;
            _transportChannel.WriteTimeout = 300;
            try { _transportChannel.Open(); }
            catch { }

            _cts = new CancellationTokenSource();
            _transportTask = Task.Factory.StartNew(TransportLoop, this, _cts.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }

        private static void TransportLoop(object state)
        {
            var requestResponse = (RequestResponseBase)state;

            var channel = requestResponse._transportChannel;

            var channelName = channel.Name;
            var readTimeout = channel.ReadTimeout;
            var responseTimeout = readTimeout > 0 ? readTimeout : 100;

            var spinWait = new SpinWait();
            var stopwatch = new Stopwatch();
            var requestQueue = requestResponse._requestQueue;
            var cancellationToken = requestResponse._cts.Token;

            while (!cancellationToken.IsCancellationRequested)
            {                
                #region 连接状态检查
                while (!channel.IsConnected && !cancellationToken.IsCancellationRequested)
                {
                    // 等待 3 秒后尝试重连，分段 Sleep 避免 Stop/Close/Dispose 等待超时
                    for (int i = 0; i < 30; i++)
                    {
                        Thread.Sleep(100);
                        if (cancellationToken.IsCancellationRequested) break;
                    }

                    try
                    {
                        channel.Close();

                        Thread.Sleep(100);
                        if (cancellationToken.IsCancellationRequested) break;

                        channel.Open();
                    }
                    catch (Exception ex)
                    {
                        Trace.TraceWarning($"通道 ({channelName}) 连接异常：{ex.Message}");
                    }

                    Thread.Sleep(100);
                }
                #endregion

                if (!requestQueue.IsEmpty && requestQueue.TryDequeue(out var request))
                {
                    Trace.WriteLine($"dequeue....{request.Id}");
                    channel.Write(request.Data, 0, request.Data.Length);

                    var received = 0;
                    var responseLength = request.ResponseLength;
                    var responseBuffer = new byte[request.ResponseLength];

                    spinWait.Reset();
                    stopwatch.Restart();

                    while (!cancellationToken.IsCancellationRequested)
                    {
                        if (stopwatch.ElapsedMilliseconds >= responseTimeout)
                        {
                            request.SetException(new TimeoutException($"请求 ID:{request.Id} 响应超时({channelName})"));
                            break;
                        }

                        int available = channel.Available;
                        if (available <= 0)
                        {
                            spinWait.SpinOnce();
                            continue;
                        }

                        spinWait.Reset();

                        var readCount = Math.Min(available, responseLength - received);
                        var bytesToRead = channel.Read(responseBuffer, received, readCount);

                        received += bytesToRead;
                        if (received == responseLength)
                        {
                            request.SetResult(responseBuffer);
                            break;
                        }
                    }
                }
            }

            stopwatch.Stop();
        }

        public async Task<byte[]> TransceiveAsync(byte[] data, int offset, int length, int responseLength, CancellationToken cancellationToken)
        {
            var request = new Request();
            request.Data = data;
            request.Offset = offset;
            request.Length = length;
            request.FramePredicate = null;
            request.ResponseLength = responseLength;
            request.CancellationSource = cancellationToken;

            var completionSource = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
            request.CompletionSource = completionSource;
            Trace.WriteLine($"add....{request.Id}");
            _requestQueue.Enqueue(request);

            return await completionSource.Task.ConfigureAwait(false);
        }

        public async Task<byte[]> TransceiveAsync(byte[] data, int offset, int length, ResponseFramePredicate framePredicate, CancellationToken cancellationToken)
        {
            var request = new Request();
            request.Data = data;
            request.Offset = offset;
            request.Length = length;
            request.ResponseLength = -1;
            request.FramePredicate = framePredicate;
            request.CancellationSource = cancellationToken;

            var completionSource = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
            request.CompletionSource = completionSource;

            _requestQueue.Enqueue(request);

            return await completionSource.Task.ConfigureAwait(false);
        }

        public void Dispose()
        {
            _cts?.Cancel();
            _requestQueue.Clear();

            try
            {
                _transportTask?.Wait(300);
                _transportTask?.Dispose();
            }
            finally
            {
                _transportTask = null;
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
