using System;
using System.Collections.Concurrent;

namespace SpaceCG.Extensions
{
    /// <summary>
    /// 集合与可枚举类型的扩展方法。
    /// </summary>
    public static partial class EnumerableExtensions
    {
        /// <summary>
        /// 清空 <see cref="ConcurrentQueue{T}"/> 中的所有元素，并释放其中实现
        /// <see cref="IDisposable"/> 接口的元素所持有的非托管资源。
        /// </summary>
        /// <typeparam name="T">队列元素类型。</typeparam>
        /// <param name="queue">要清空的队列。若为 null 将抛出 <see cref="ArgumentNullException"/>。</param>
        /// <exception cref="ArgumentNullException"><paramref name="queue"/> 为 null 时抛出。</exception>
        /// <remarks>
        /// <para>采用逐一出队（TryDequeue）的方式清空，而非直接替换内部存储，
        /// 以便在释放前访问每个元素。仅当元素运行时类型实现了 <see cref="IDisposable"/> 时才调用其
        /// <see cref="IDisposable.Dispose"/>；未实现 IDisposable 的元素仅被移除，不参与释放。</para>
        /// <para>并发说明：本方法并非原子操作。清空过程中若有其他线程并发入队，
        /// 新元素可能被一并出队释放，也可能残留；请勿在清空期间继续向队列写入。</para>
        /// <para>异常说明：元素 <see cref="IDisposable.Dispose"/> 抛出的异常会被忽略，
        /// 以保证清空操作不因单个元素释放失败而中断。若需感知释放失败，请改用逐元素显式出队释放。</para>
        /// </remarks>
        public static void Clear<T>(this ConcurrentQueue<T> queue)
        {
            if (queue == null)
                throw new ArgumentNullException(nameof(queue));

            while (!queue.IsEmpty)
            {
                if (!queue.TryDequeue(out var item)) continue;

                // item 运行时类型实现 IDisposable 时，释放其持有的资源
                if (item is IDisposable disposable)
                {
                    try { disposable.Dispose(); }
                    catch { }
                }
            }
        }
    }
}
