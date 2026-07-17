using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace SpaceCG.Generic
{
    /// <summary>
    /// 环形缓冲区（循环缓冲区），支持双端添加和移除元素。
    /// <para>当缓冲区已满时，从任意一端添加新元素会自动从另一端移除最旧的元素，确保缓冲区大小始终不超过容量。</para>
    /// <para>线程安全：此类不保证线程安全，多线程并发访问需要外部同步。支持通过索引器按逻辑索引随机访问。</para>
    /// </summary>
    /// <typeparam name="T">缓冲区中元素的类型。</typeparam>
    [DebuggerDisplay("Count = {Count}")]
    public class CircularBuffer<T> : IEnumerable<T>, IReadOnlyList<T>
    {
        private readonly T[] _buffer;

        /// <summary>
        /// 起始指针 _start。指向缓冲区中第一个有效元素的索引。
        /// </summary>
        private int _start;

        /// <summary>
        /// 结束指针 _end。指向缓冲区中最后一个有效元素之后的位置（即下一个可写入位置）。
        /// </summary>
        private int _end;

        /// <summary>
        /// 当前大小 _size。缓冲区中实际存储的元素数量。
        /// </summary>
        private int _size;

        /// <summary>
        /// 当前缓冲区大小（缓冲区中实际包含的元素数量）。
        /// </summary>
        public int Count => _size;

        /// <summary>
        /// 缓冲区的最大容量。
        /// <para>当缓冲区已满（IsFull = true）时，继续添加元素将会移除另一端的元素以腾出空间。</para>
        /// </summary>
        public int Capacity => _buffer.Length;

        /// <summary>
        /// 获取一个值，指示缓冲区是否为空（不包含任何元素）。
        /// </summary>
        public bool IsEmpty => Count == 0;

        /// <summary>
        /// 获取一个值，指示环形缓冲区是否已满。
        /// <para>当缓冲区已满时继续添加元素，将会导致另一端的元素被移除。</para>
        /// </summary>
        public bool IsFull => Count == Capacity;

        /// <summary>
        /// 初始化 <see cref="CircularBuffer{T}"/> 类的新实例。
        /// </summary>
        /// <param name="capacity">缓冲区容量，必须为正数。</param>
        public CircularBuffer(int capacity) : this(capacity, new T[] { })
        {
        }

        /// <summary>
        /// 初始化 <see cref="CircularBuffer{T}"/> 类的新实例。
        /// </summary>
        /// <param name="capacity">缓冲区容量，必须为正数。</param>
        /// <param name="items">用于填充缓冲区的初始元素。元素数量必须小于或等于容量。 建议：使用 Skip(x).Take(y).ToArray() 从任意可枚举集合构建此参数。 </param>
        public CircularBuffer(int capacity, T[] items)
        {
            if (capacity < 1)
            {
                throw new ArgumentException("环形缓冲区的容量不能为负数或零。", nameof(capacity));
            }
            if (items == null)
            {
                throw new ArgumentNullException(nameof(items), "初始元素数组不能为空。");
            }
            if (items.Length > capacity)
            {
                throw new ArgumentException("初始元素数量超过环形缓冲区容量。", nameof(items));
            }

            _buffer = new T[capacity];

            Array.Copy(items, _buffer, items.Length);
            _size = items.Length;

            _start = 0;
            _end = _size == capacity ? 0 : _size;
        }

        /// <summary>
        /// 通过索引访问缓冲区中的元素。索引不会像添加元素时那样循环环绕，有效区间为 [0, Size)（左闭右开）。
        /// </summary>
        /// <param name="index">要访问的元素索引。</param>
        /// <exception cref="IndexOutOfRangeException">当索引超出 [0, Size) 区间时抛出此异常。</exception>
        public T this[int index]
        {
            get
            {
                if (IsEmpty)
                {
                    throw new IndexOutOfRangeException(string.Format("无法访问索引 {0}，缓冲区为空。", index));
                }
                if (index >= _size)
                {
                    throw new IndexOutOfRangeException(string.Format("无法访问索引 {0}，缓冲区大小为 {1}。", index, _size));
                }
                int actualIndex = InternalIndex(index);
                return _buffer[actualIndex];
            }
            set
            {
                if (IsEmpty)
                {
                    throw new IndexOutOfRangeException(string.Format("无法访问索引 {0}，缓冲区为空。", index));
                }
                if (index >= _size)
                {
                    throw new IndexOutOfRangeException(string.Format("无法访问索引 {0}，缓冲区大小为 {1}。", index, _size));
                }
                int actualIndex = InternalIndex(index);
                _buffer[actualIndex] = value;
            }
        }

        /// <summary>
        /// 向缓冲区尾部添加一个元素。执行后，<see cref="PeekBack"/> / this[<see cref="Count"/> - 1] 将返回此元素。
        /// <para>当缓冲区已满时，头部元素（<see cref="PeekFront"/>）将被移除，以便为新元素腾出空间。</para>
        /// </summary>
        /// <param name="item">要添加到缓冲区尾部的元素。</param>
        public void AddBack(T item)
        {
            if (IsFull)
            {
                _buffer[_end] = item;
                Increment(ref _end);
                _start = _end;  // 头部指针同步移动，相当于弹出最旧元素
            }
            else
            {
                _buffer[_end] = item;
                Increment(ref _end);
                ++_size;
            }
        }
        /// <summary>
        /// 向缓冲区头部添加一个元素。执行后，<see cref="PeekFront"/> / this[0] 将返回此元素。
        /// <para>当缓冲区已满时，尾部元素（<see cref="PeekBack"/>）将被移除，以便为新元素腾出空间。</para>
        /// </summary>
        /// <param name="item">要添加到缓冲区头部的元素。</param>
        public void AddFront(T item)
        {
            if (IsFull)
            {
                Decrement(ref _start);
                _end = _start;          // 尾部指针同步移动，相当于弹出最旧元素
                _buffer[_start] = item;
            }
            else
            {
                Decrement(ref _start);
                _buffer[_start] = item;
                ++_size;
            }
        }

        /// <summary>
        /// 查看缓冲区尾部的元素，即 this[<see cref="Count"/> - 1]，不将其移除。
        /// </summary>
        /// <returns>缓冲区尾部类型为 <typeparamref name="T"/> 的元素值。</returns>
        public T PeekBack()
        {
            ThrowIfEmpty();
            return _buffer[(_end != 0 ? _end : Capacity) - 1];
        }
        /// <summary>
        /// 查看缓冲区头部的元素，即 this[0]，不将其移除。
        /// </summary>
        /// <returns>缓冲区头部类型为 <typeparamref name="T"/> 的元素值。</returns>
        public T PeekFront()
        {
            ThrowIfEmpty();
            return _buffer[_start];
        }

        /// <summary>
        /// 移除并返回缓冲区尾部的元素，<see cref="Count"/> 减 1。
        /// </summary>
        /// <returns>被移除的尾部元素。</returns>
        public T RemoveBack()
        {
            ThrowIfEmpty("无法从空缓冲区中移除元素。");
            Decrement(ref _end);

            var value = _buffer[_end];
            _buffer[_end] = default(T);     // 清理引用，便于垃圾回收
            --_size;

            return value;
        }
        /// <summary>
        /// 移除并返回缓冲区头部的元素，<see cref="Count"/> 减 1。
        /// </summary>
        /// <returns>被移除的头部元素。</returns>
        public T RemoveFront()
        {
            ThrowIfEmpty("无法从空缓冲区中移除元素。");

            var value = _buffer[_start];
            _buffer[_start] = default(T);   // 清理引用

            Increment(ref _start);
            --_size;

            return value;
        }

        /// <summary>
        /// 清空缓冲区内容。<see cref="Count"/> 重置为 0，<see cref="Capacity"/> 保持不变。
        /// </summary>
        public void Clear()
        {
            // 清空操作只需重置所有指针和大小
            _start = 0;
            _end = 0;
            _size = 0;
            Array.Clear(_buffer, 0, _buffer.Length);
        }

        /// <summary>
        /// 将缓冲区的逻辑内容按顺序复制到一个新数组中。
        /// </summary>
        /// <returns>包含缓冲区内容副本的新数组。</returns>
        public T[] ToArray()
        {
            T[] newArray = new T[Count];
            int newArrayOffset = 0;
            var segments = GetSegments();
            foreach (ArraySegment<T> segment in segments)
            {
                Array.Copy(segment.Array, segment.Offset, newArray, newArrayOffset, segment.Count);
                newArrayOffset += segment.Count;
            }
            return newArray;
        }

        /// <summary>
        /// 获取缓冲区内容的逻辑段列表，按插入顺序排列。
        /// <para>高效：不复制元素，仅返回 <see cref="ArraySegment{T}"/> 视图，适用于 <c>Send(IList&lt;ArraySegment&lt;Byte&gt;&gt;)</c> 等零拷贝场景。</para>
        /// <para>返回的段可能为空（当缓冲区为空时）。</para>
        /// </summary>
        /// <returns>包含最多 2 个段的列表，按逻辑顺序对应缓冲区内容。</returns>
        public IList<ArraySegment<T>> GetSegments()
        {
            return new[] { GetFirstSegment(), GetSecondSegment() };
        }

        #region IEnumerable<T> 实现
        /// <summary>
        /// 返回一个按逻辑顺序遍历缓冲区元素的枚举器。
        /// </summary>
        /// <returns>可用于迭代此集合的枚举器。</returns>
        public IEnumerator<T> GetEnumerator()
        {
            var segments = GetSegments();
            foreach (ArraySegment<T> segment in segments)
            {
                for (int i = 0; i < segment.Count; i++)
                {
                    yield return segment.Array[segment.Offset + i];
                }
            }
        }
        #endregion

        #region IEnumerable 实现
        IEnumerator IEnumerable.GetEnumerator()
        {
            return (IEnumerator)GetEnumerator();
        }
        #endregion

        /// <summary>
        /// 如果缓冲区为空则抛出异常。
        /// </summary>
        /// <param name="message">自定义异常消息，默认为 "无法访问空缓冲区。"</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ThrowIfEmpty(string message = "无法访问空缓冲区。")
        {
            if (IsEmpty)
            {
                throw new InvalidOperationException(message);
            }
        }

        /// <summary>
        /// 将提供的索引变量递增 1，如果达到容量则环绕回 0。
        /// </summary>
        /// <param name="index">要递增的索引引用</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void Increment(ref int index)
        {
            if (++index == Capacity)
            {
                index = 0;
            }
        }

        /// <summary>
        /// 将提供的索引变量递减 1，如果达到 0 则环绕回容量位置。
        /// </summary>
        /// <param name="index">要递减的索引引用</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void Decrement(ref int index)
        {
            if (index == 0)
            {
                index = Capacity;
            }
            index--;
        }

        /// <summary>
        /// 将外部逻辑索引转换为内部物理缓冲区数组的实际索引。
        /// </summary>
        /// <param name="index">外部逻辑索引，有效范围 [0, <see cref="Count"/>)。</param>
        /// <returns>转换后的内部物理索引。</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int InternalIndex(int index)
        {
            int actual = _start + index;
            if (actual >= Capacity) actual -= Capacity;
            return actual;
        }


        #region 内部段访问
        // 内部缓冲区数组最多由两个不连续的段组成。
        // 以下两个方法提供对这两个段的便捷访问，供 GetSegments() 使用。

        /// <summary>
        /// 获取缓冲区的第一个逻辑段（从 _start 到数组末尾或 _end）。
        /// </summary>
        private ArraySegment<T> GetFirstSegment()
        {
            if (IsEmpty)
            {
                return new ArraySegment<T>(new T[0]);
            }
            else if (_start < _end)
            {
                // 数据连续存储在 [_start, _end) 区间
                return new ArraySegment<T>(_buffer, _start, _end - _start);
            }
            else
            {
                // 数据跨越数组末尾：第一段从 _start 到数组末尾
                return new ArraySegment<T>(_buffer, _start, _buffer.Length - _start);
            }
        }

        /// <summary>
        /// 获取缓冲区的第二个逻辑段（仅在数据跨越数组末尾时非空）。
        /// </summary>
        private ArraySegment<T> GetSecondSegment()
        {
            if (IsEmpty)
            {
                return new ArraySegment<T>(new T[0]);
            }
            else if (_start < _end)
            {
                return new ArraySegment<T>(_buffer, _end, 0);
            }
            else
            {
                return new ArraySegment<T>(_buffer, 0, _end);
            }
        }
        #endregion
    }
}
