using System;
using System.Threading;
using System.Threading.Tasks;

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
        /// 发送完整请求数据，并阻塞等待由 <paramref name="framePredicate"/> 判定边界的响应。
        /// </summary>
        /// <param name="data">请求数据。</param>
        /// <param name="framePredicate">响应完整性判定器。</param>
        /// <returns>接收到的完整响应数据。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="data"/> 或 <paramref name="framePredicate"/> 为 <c>null</c>。</exception>
        /// <exception cref="TimeoutException">在 <see cref="ResponseTimeout"/> 内未收到完整响应。</exception>
        byte[] Transceive(byte[] data, ResponseFramePredicate framePredicate);

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
        byte[] Transceive(byte[] data, int offset, int length, ResponseFramePredicate framePredicate);

        /// <summary>
        /// 发送完整请求数据，并阻塞等待固定长度的响应。
        /// </summary>
        /// <param name="data">请求数据。</param>
        /// <param name="responseFixedLength">期望的响应固定字节数。</param>
        /// <returns>接收到的完整响应数据，长度等于 <paramref name="responseFixedLength"/>。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="data"/> 为 <c>null</c>。</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="responseFixedLength"/> 小于 0。</exception>
        /// <exception cref="TimeoutException">在 <see cref="ResponseTimeout"/> 内未收到完整响应。</exception>
        byte[] Transceive(byte[] data, int responseFixedLength);

        /// <summary>
        /// 发送请求数据的指定区段，并阻塞等待固定长度的响应。
        /// </summary>
        /// <param name="data">请求数据缓冲区。</param>
        /// <param name="offset"><paramref name="data"/> 中开始发送的字节偏移量。</param>
        /// <param name="length">从 <paramref name="data"/> 中发送的字节数。</param>
        /// <param name="responseFixedLength">期望的响应固定字节数。</param>
        /// <returns>接收到的完整响应数据，长度等于 <paramref name="responseFixedLength"/>。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="data"/> 为 <c>null</c>。</exception>
        /// <exception cref="ArgumentException"><paramref name="offset"/> 或 <paramref name="length"/> 越界。</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="responseFixedLength"/> 小于 0。</exception>
        /// <exception cref="TimeoutException">在 <see cref="ResponseTimeout"/> 内未收到完整响应。</exception>
        byte[] Transceive(byte[] data, int offset, int length, int responseFixedLength);

        /// <summary>
        /// 异步发送完整请求数据，并等待由 <paramref name="framePredicate"/> 判定边界的响应。
        /// </summary>
        /// <param name="data">请求数据。</param>
        /// <param name="framePredicate">响应完整性判定器。</param>
        /// <param name="cancellationToken">用于取消等待的取消令牌。</param>
        /// <returns>表示异步操作的响应数据。</returns>
        /// <exception cref="OperationCanceledException">操作被取消。</exception>
        Task<byte[]> TransceiveAsync(byte[] data, ResponseFramePredicate framePredicate, CancellationToken cancellationToken);

        /// <summary>
        /// 异步发送完整请求数据，并等待固定长度的响应。
        /// </summary>
        /// <param name="data">请求数据。</param>
        /// <param name="responseFixedLength">期望的响应固定字节数。</param>
        /// <param name="cancellationToken">用于取消等待的取消令牌。</param>
        /// <returns>表示异步操作的响应数据。</returns>
        /// <exception cref="OperationCanceledException">操作被取消。</exception>
        Task<byte[]> TransceiveAsync(byte[] data, int responseFixedLength, CancellationToken cancellationToken);
    }
}
