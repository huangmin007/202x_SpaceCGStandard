using System;
using NModbus.Device;
using NModbus;
using Trace = SpaceCG.Diagnostics.Trace;

namespace SpaceCG.NModbus.Data
{
    /// <summary>
    /// Modbus 从站一个数据区（线圈 / 离散量 / 输入寄存器 / 保持寄存器）的存储，底层是一块连续数组，数组下标就是点地址。
    /// <para>与 <see cref="PointSource{T}"/> 相比：读写越界不抛异常（读补 0、写截断）；容量可配避免占用过多内存；
    /// 并提供单个点的读写。</para>
    /// </summary>
    /// <remarks>
    /// 写入路径（关键约定）：
    /// </remarks>
    /// <typeparam name="T">点值类型：bool 用于线圈 / 离散量，ushort 用于寄存器。</typeparam>
    public sealed class RegistersPointSource<T> : IPointSource<T> where T : struct
    {
        /// <summary> 拷贝方式阈值：不超过 16 个元素时逐元素赋值，超过时用 Array.Copy。 </summary>
        private const int CopyThreshold = 16;
        /// <summary>默认容量 65535 个点，可用地址 0~65534。</summary>
        private const ushort MaxCapacity = 0xFFFF;

        /// <summary>点值数组，延迟创建；索引即 Modbus 点地址。</summary>
        private readonly Lazy<T[]> _points;
        /// <summary>容量（最大地址 + 1）。单独存一份，避免为了取长度而提前触发数组分配。</summary>
        private readonly int _capacity;
        /// <summary>读写 _points 的同步锁，保证单次访问的线程安全。</summary>
        private readonly object _syncRoot = new object();

        /// <summary> 获取点区容量大小。 </summary>
        public ushort Capacity => (ushort)_capacity;

        /// <summary>读取前触发，可用于在返回前刷新/同步外部状态到点区。</summary>
        public event EventHandler<PointEventArgs> BeforeRead;
        /// <summary>写入前触发。</summary>
        public event EventHandler<PointEventArgs<T>> BeforeWrite;
        /// <summary>写入完成后触发，可用于通知业务层；回调中只应入队，不应直接改状态。</summary>
        public event EventHandler<PointEventArgs<T>> AfterWrite;

        /// <summary>用默认容量构造。</summary>
        public RegistersPointSource() : this(MaxCapacity)
        {
        }

        /// <summary>用指定容量构造；数组不会立即分配，首次读写时才创建。</summary>
        /// <param name="capacity">分配存储容量大小。</param>
        /// <exception cref="ArgumentOutOfRangeException">capacity 为 0。</exception>
        public RegistersPointSource(ushort capacity)
        {
            if (capacity == 0)
                throw new ArgumentOutOfRangeException(nameof(capacity), "容量必须大于 0。");

            _capacity = capacity;
            _points = new Lazy<T[]>(() => new T[capacity]); // 数组延迟到首次访问时才分配
        }

        /// <summary>读取指定区间的点值副本（改副本不影响点区）。</summary>
        /// <remarks>
        /// 返回数组长度恒等于 numberOfPoints：越界的部分补 0（bool 补 false），不会变短、也不抛异常。复杂度 O(numberOfPoints)。
        /// </remarks>
        /// <param name="startAddress">起始地址。</param>
        /// <param name="numberOfPoints">读取点数量，越界部分按 remarks 补 0。</param>
        /// <returns>长度等于 numberOfPoints 的点值副本。</returns>
        public T[] ReadPoints(ushort startAddress, ushort numberOfPoints)
        {
            if (numberOfPoints == 0) return Array.Empty<T>();

            var result = new T[numberOfPoints];
            var available = _capacity - startAddress;
            var readLength = Math.Min(numberOfPoints, available);

            if (readLength > 0)
            {
                lock (_syncRoot)
                {
                    var source = _points.Value;
                    if (readLength <= CopyThreshold)
                    {
                        for (int i = 0; i < readLength; i++)
                        {
                            result[i] = source[startAddress + i];
                        }
                    }
                    else
                    {
                        Array.Copy(source, startAddress, result, 0, readLength);
                    }
                }
            }

            if (readLength != numberOfPoints)
            {
                Trace.TraceWarning($"读取点区越界，已补 0：StartAddress={startAddress}, 请求={numberOfPoints}, 有效={readLength}, 容量={_capacity}");
            }

            return result;
        }

        /// <summary>接口实现：先触发 BeforeRead 事件以刷新状态，再执行实际读取。</summary>
        /// <param name="startAddress">起始地址。</param>
        /// <param name="numberOfPoints">读取点数量。</param>
        /// <returns>点值数组副本。</returns>
        T[] IPointSource<T>.ReadPoints(ushort startAddress, ushort numberOfPoints)
        {
            var beforeRead = this.BeforeRead;
            if (beforeRead != null)
            {
                beforeRead(this, new PointEventArgs(startAddress, numberOfPoints));
            }

            return ReadPoints(startAddress, numberOfPoints);
        }

        /// <summary>将点值写入指定起始地址开始的连续地址。</summary>
        /// <remarks>
        /// 越界处理：起始地址越界则整帧丢弃；部分越界则只写有效部分，越界部分丢弃，不抛异常。复杂度 O(points.Length)。
        /// </remarks>
        /// <param name="startAddress">起始地址。</param>
        /// <param name="points">待写入的点值数组。</param>
        public void WritePoints(ushort startAddress, T[] points)
        {
            if (points == null || points.Length == 0) return;

            var available = _capacity - startAddress;
            if (available <= 0) return;
            var writeLength = Math.Min(points.Length, available);

            lock (_syncRoot)
            {
                var target = _points.Value;
                if (writeLength <= CopyThreshold)
                {
                    for (var i = 0; i < writeLength; i++)
                    {
                        target[startAddress + i] = points[i];
                    }
                }
                else
                {
                    Array.Copy(points, 0, target, startAddress, writeLength);
                }
            }

            if (writeLength != points.Length)
            {
                Trace.TraceWarning($"写入点区越界，已截断：StartAddress={startAddress}, 请求={points.Length}, 实际写入={writeLength}, 容量={_capacity}");
            }
        }

        /// <summary>
        /// 接口实现：写入前触发 BeforeWrite，写入后触发 AfterWrite（两个事件共用同一事件参数实例）。
        /// </summary>
        /// <param name="startAddress">起始地址。</param>
        /// <param name="points">待写入的点值数组。</param>
        void IPointSource<T>.WritePoints(ushort startAddress, T[] points)
        {
            PointEventArgs<T> eventArgs = null;
            var beforeWrite = this.BeforeWrite;
            if (beforeWrite != null)
            {
                eventArgs = new PointEventArgs<T>(startAddress, points);
                beforeWrite(this, eventArgs);
            }

            WritePoints(startAddress, points);

            var afterWrite = this.AfterWrite;
            if (afterWrite != null)
            {
                if (eventArgs == null)
                    eventArgs = new PointEventArgs<T>(startAddress, points);
                afterWrite(this, eventArgs);
            }
        }

        /// <summary>读单个点值，省去一次数组分配，适合高频单点访问。</summary>
        /// <remarks>复杂度 O(1)。NModbus 不会走这里，它只调数组版接口。</remarks>
        /// <param name="address">点地址。</param>
        /// <returns>点值；地址越界返回 0（bool 为 false）。</returns>
        public T ReadPoint(ushort address)
        {
            if (address >= _capacity) return default(T);

            lock (_syncRoot)
            {
                return _points.Value[address];
            }
        }

        /// <summary>写单个点值，省去一次数组分配，适合高频单点访问。</summary>
        /// <remarks>复杂度 O(1)。NModbus 不会走这里，它只调数组版接口。</remarks>
        /// <param name="address">点地址。</param>
        /// <param name="value">点值。</param>
        public void WritePoint(ushort address, T value)
        {
            if (address >= _capacity) return;

            lock (_syncRoot)
            {
                _points.Value[address] = value;
            }
        }

    }


}
