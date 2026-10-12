using System;
using System.Collections.Concurrent;
using System.Threading;
using Trace = SpaceCG.Diagnostics.Trace;

namespace SpaceCG.Generic
{
    /// <summary>
    /// 泛型对象池，用于复用频繁创建/销毁的短生命周期对象，减少 GC 压力。
    /// <para>线程安全：所有公开方法均为线程安全，适用于多线程生产者-消费者场景。</para>
    /// <para>容量控制：池中缓存对象数不超过 <see cref="MaxCount"/>（近似值，高并发下可能略微超出）。</para>
    /// <para>典型用法：<c>var item = pool.Rent(); ...; pool.Return(item);</c></para>
    /// </summary>
    /// <typeparam name="T">池化对象类型，必须具有无参构造函数。</typeparam>
    public class ObjectPool<T> where T : new()
    {
        /// <summary>默认最大容量。</summary>
        private const int DefaultMaxCount = 64;

        /// <summary>
        /// 对象池的最大容量（防止内存无限增长）。
        /// <para>高并发场景下实际缓存数可能略微超出此值，属于近似控制。</para>
        /// </summary>
        public int MaxCount { get; }
        /// <summary>
        /// 当前池中缓存的可用对象数（近似值，用于监控）。
        /// </summary>
        public int Count => _count;

        /// <summary>  当前池中对象数（近似值，用于容量控制）。  </summary>
        private int _count = 0;

        /// <summary>内部对象队列。</summary>
        protected readonly ConcurrentQueue<T> _pool = new ConcurrentQueue<T>();

        /// <summary>
        /// 创建指定初始容量的对象池，最大容量使用默认值（<see cref="DefaultMaxCount"/>）。
        /// </summary>
        /// <param name="initialCount">预创建的对象数量，必须 &gt;= 0。</param>
        /// <exception cref="ArgumentOutOfRangeException">initialCount &lt; 0 或 initialCount &gt; MaxCount。</exception>
        public ObjectPool(int initialCount) : this (initialCount, DefaultMaxCount) 
        { 
        }

        /// <summary>
        /// 创建对象池。
        /// </summary>
        /// <param name="initialCount">预创建的对象数量，必须 &gt;= 0 且 &lt;= <paramref name="maxCount"/>。</param>
        /// <param name="maxCount">池中缓存对象的最大数量。</param>
        /// <exception cref="ArgumentOutOfRangeException">
        /// initialCount &lt; 0、maxCount &lt;= 0、或 initialCount &gt; maxCount。
        /// </exception>
        public ObjectPool(int initialCount, int maxCount)
        {
            if (initialCount < 0 || initialCount > maxCount)
                throw new ArgumentOutOfRangeException(nameof(initialCount), $"initialCount({initialCount}) 必须在 [0, maxCount({maxCount})] 范围内");
            if (maxCount <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxCount), $"maxCount({maxCount}) 必须大于 0");

            MaxCount = maxCount;
            for (int i = 0; i < initialCount; i++)
            {
                _pool.Enqueue(new T());
                _count++;
            }
        }

        /// <summary>
        /// 从池中租用一个 <typeparamref name="T"/> 实例。
        /// <para>池为空时创建新实例（不占用池容量）。</para>
        /// </summary>
        /// <returns>可用的对象实例。</returns>
        public T Rent()
        {
            if (_pool.TryDequeue(out var item))
            {
                Interlocked.Decrement(ref _count);
                return item;
            }

            return new T();
        }

        /// <summary>
        /// 将 <typeparamref name="T"/> 实例归还到池中。
        /// <para>池容量达到 <see cref="MaxCount"/> 上限时，实例将被丢弃（由 GC 回收）。</para>
        /// <para>注意：归还前建议重置对象状态（如清空集合、重置标志位），避免脏数据被下次租用方读取。</para>
        /// </summary>
        /// <param name="item">要归还的对象实例，null 将被静默忽略。</param>
        public void Return(T item)
        {
            if (item == null) return;

            // 容量近似控制：高并发下可能略微超出 MaxCount，但对内存影响可控
            if (Interlocked.Increment(ref _count) > MaxCount)
            {
                Interlocked.Decrement(ref _count);
                // 若对象实现了 IDisposable，归还失败时主动释放资源
                if (item is IDisposable disposable)
                {
                    try { disposable.Dispose(); }
                    catch (Exception) { }
                }
                return;
            }

            _pool.Enqueue(item);
        }

        /// <summary>
        /// 清空对象池，释放所有缓存的实例。
        /// <para>若 <typeparamref name="T"/> 实现了 <see cref="IDisposable"/>，会逐一调用 <see cref="IDisposable.Dispose"/>。</para>
        /// <para>通常在服务停止或需要回收内存时调用。清空期间可能有其他线程继续归还对象，Count 可能不为 0。</para>
        /// </summary>
        public void Clear()
        {
            while (_pool.TryDequeue(out var item))
            {
                Interlocked.Decrement(ref _count);

                // 若对象实现了 IDisposable，归还失败时主动释放资源
                if (item is IDisposable disposable)
                {
                    try { disposable.Dispose(); }
                    catch (Exception) { }
                }
            }
        }

    }
}
