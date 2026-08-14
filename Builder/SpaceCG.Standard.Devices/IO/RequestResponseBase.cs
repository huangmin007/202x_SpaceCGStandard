using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using SpaceCG.Extensions;
using Trace = SpaceCG.Diagnostics.Trace;

namespace SpaceCG.IO
{
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

            public CancellationToken CancellationToken { get; set; }

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

                if (!requestQueue.IsEmpty && requestQueue.TryDequeue(out var request))
                {
                    channel.Write(request.Data, 0, request.Data.Length);

                    var received = 0;
                    var responseLength = request.ResponseLength;
                    var responseBuffer = new byte[request.ResponseLength];

                    spinWait.Reset();
                    stopwatch.Restart();

                    while (!request.CancellationToken.IsCancellationRequested)
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
            request.CancellationToken = cancellationToken;

            var completionSource = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
            request.CompletionSource = completionSource;

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
            request.CancellationToken = cancellationToken;

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
