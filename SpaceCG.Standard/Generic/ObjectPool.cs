using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using System.Threading;

namespace SpaceCG.Generic
{
    /// <summary>
    /// 对象池
    /// </summary>
    /// <typeparam name="T">对象类型</typeparam>
    public class ObjectPool<T> where T : new()
    {
        /// <summary>
        /// 对象池的最大容量（防止内存无限增长）。
        /// </summary>
        public readonly int MaxCount = 64;

        /// <summary>
        /// 队列池
        /// </summary>
        protected readonly ConcurrentQueue<T> Pool = new ConcurrentQueue<T>();

        /// <summary>
        /// 当前池中对象数（近似值，用于容量控制）。
        /// </summary>
        private int _count;

        /// <summary>
        /// 
        /// </summary>
        public int Count => _count;

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="initialCount"></param>
        public ObjectPool(int initialCount) : this (initialCount, 64) 
        { 
        }

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="initialCount"></param>
        /// <param name="maxCount"></param>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        public ObjectPool(int initialCount, int maxCount) 
        {
            if (initialCount <= 0 || initialCount > maxCount)
                throw new ArgumentOutOfRangeException(nameof(initialCount));
            if (maxCount <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxCount));

            MaxCount = maxCount;
            for (int i = 0; i < initialCount; i++) 
            { 
                Pool.Enqueue(new T()); 
            }
        }

        /// <summary>
        /// 从池中租用一个 <see cref="T"/> 实例。
        /// <para>池为空时创建新实例</para>
        /// </summary>
        /// <returns></returns>
        public T Rent()
        {
            if (Pool.TryDequeue(out var message))
            {
                Interlocked.Decrement(ref _count);
                return message;
            }

            return new T();
        }

        /// <summary>
        /// 将 <see cref="T"/> 实例归还到池中。
        /// <para>池容量达到上限时，实例将被丢弃（由 GC 回收）。</para>
        /// </summary>
        /// <param name="message"></param>
        public void Return(T message)
        {
            if (message == null) return;

            // 容量控制：超过上限则丢弃
            if (Interlocked.Increment(ref _count) > MaxCount)
            {
                Interlocked.Decrement(ref _count);
                return; // 实例将由 GC 回收
            }

            Pool.Enqueue(message);
        }

        /// <summary>
        /// 清空对象池，释放所有缓存的实例。
        /// <para>通常在服务停止时调用。</para>
        /// </summary>
        public void Clear()
        {
            while (Pool.TryDequeue(out _))
            {
                Interlocked.Decrement(ref _count);
            }
        }
    }
}
