using System;
using NModbus;
using NModbus.Data;

namespace SpaceCG.NModbus.Data
{    
    /// <summary>
    /// Modbus 本地数据存储区：聚合四个点源（离散输入、线圈、输入寄存器、保持寄存器）。
    /// <para>与<see cref="SlaveDataStore"/>的区别是在构造函数中增加了初使的容量大小，避免占用过大的内存。</para>
    /// </summary>
    /// <remarks>
    /// 写入路径（关键约定）：
    /// ① 主站写入走 <see cref="ISlaveDataStore"/> 接口 → <see cref="IPointSource{T}"/> 方法，会触发点源的 BeforeWrite / AfterWrite；
    /// ② 设备侧回写走点源的公有 ReadPoints / WritePoints 重载，不触发任何事件，避免自激回环与事件风暴。
    /// <para>四个区共用构造时指定的容量，地址范围 [0, capacity)；越界读补 0、写截断，详见 <see cref="RegistersPointSource{T}"/>。</para>
    /// <para>线程安全：各点源自身读写加锁，跨区一致性由调用方保证。</para>
    /// </remarks>
    public sealed class LocalDataStore : ISlaveDataStore
    {
        private readonly RegistersPointSource<bool> _coilInputs;
        private readonly RegistersPointSource<bool> _coilDiscretes;
        private readonly RegistersPointSource<ushort> _inputRegisters;
        private readonly RegistersPointSource<ushort> _holdingRegisters;

        /// <summary>离散输入（只读，功能码 02）。</summary>
        public RegistersPointSource<bool> CoilInputs => _coilInputs;
        /// <summary>线圈（可读写，功能码 01/05/15）。</summary>
        public RegistersPointSource<bool> CoilDiscretes => _coilDiscretes;
        /// <summary>输入寄存器（只读，功能码 04）。</summary>
        public RegistersPointSource<ushort> InputRegisters => _inputRegisters;
        /// <summary>保持寄存器（可读写，功能码 03/06/16）。</summary>
        public RegistersPointSource<ushort> HoldingRegisters => _holdingRegisters;

        // 显式实现：NModbus 只通过 IPointSource<T> 接口访问点源
        IPointSource<bool> ISlaveDataStore.CoilInputs => _coilInputs;
        IPointSource<bool> ISlaveDataStore.CoilDiscretes => _coilDiscretes;
        IPointSource<ushort> ISlaveDataStore.InputRegisters => _inputRegisters;
        IPointSource<ushort> ISlaveDataStore.HoldingRegisters => _holdingRegisters;

        /// <summary>以默认容量（65535 点）构造四个点源。</summary>
        public LocalDataStore() : this(0xFFFF)
        {
        }

        /// <summary>以指定容量构造四个点源。</summary>
        public LocalDataStore(ushort capacity)
        {
            _coilInputs = new RegistersPointSource<bool>(capacity);
            _coilDiscretes = new RegistersPointSource<bool>(capacity);
            _inputRegisters = new RegistersPointSource<ushort>(capacity);
            _holdingRegisters = new RegistersPointSource<ushort>(capacity);
        }

    }

}
