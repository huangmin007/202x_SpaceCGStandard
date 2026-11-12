using System;
using SpaceCG.NModbus.Data;

namespace SpaceCG.NModbus
{
    /// <summary>
    /// 寄存器块值变更事件参数：描述「哪一块寄存器」在一次同步轮询中由什么值变成了什么值。
    /// </summary>
    /// <remarks>事件触发时本地存储已写入 <see cref="NewValue"/>，按 LocalAddress 回读得到的是新值而非 <see cref="OldValue"/>。</remarks>
    public class RegisterPointChangedEventArgs<T> : EventArgs where T : struct
    {
        /// <summary>发生变更的寄存器块配置。</summary>
        public RegisterPoint RegisterPoint { get; }

        /// <summary>本次从远端读回的新值，长度等于点数，顺序与 <see cref="RegisterPoint.StartAddress"/> 起的连续地址对应。</summary>
        public T[] NewValue { get; }

        /// <summary>写入本地存储前的旧值，长度与 <see cref="NewValue"/> 相同，越界部分补 0 / false。</summary>
        public T[] OldValue { get; }

        public RegisterPointChangedEventArgs(RegisterPoint registersPoint, T[] newValue, T[] oldValue)
        {
            RegisterPoint = registersPoint;
            NewValue = newValue;
            OldValue = oldValue;
        }

        public override string ToString()
        {
#if !DEBUG
            return $"RegisterPointChangedEventArgs: {RegisterPoint}";
#else
            return $"RegisterPointChangedEventArgs:{RegisterPoint} OldValue:{string.Join(",", OldValue)}, NewValue:{string.Join(",", NewValue)}";
#endif
        }
    }

}
