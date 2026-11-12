using System;
using System.Collections.Generic;
using System.Xml.Linq;
using Trace = SpaceCG.Diagnostics.Trace;

namespace SpaceCG.NModbus.Data
{
    /// <summary>Modbus 寄存器类型，对应四个数据区。</summary>
    public enum RegisterType : byte
    {
        /// <summary>离散输入（只读，功能码 02）。</summary>
        CoilInputs,

        /// <summary>线圈（可读写，功能码 01/05/15）。</summary>
        CoilDiscretes,

        /// <summary>输入寄存器（只读，功能码 04）。</summary>
        InputRegisters,

        /// <summary>保持寄存器（可读写，功能码 03/06/16）。</summary>
        HoldingRegisters,
    }

    /// <summary>
    /// 寄存器对象的描述。描述"从哪个从站的哪个区、哪段起始地址读多少点，同步到本地数据存储的哪个地址"。
    /// </summary>
    public class RegisterPoint : IEquatable<RegisterPoint>
    {
        /// <summary>寄存器块名称，仅用于日志与配置可读性，不参与相等性比较。</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary> 寄存器键值 </summary>
        public uint Key => (uint)(((byte)PointType << 24) | (SlaveAddress << 16) | StartAddress);

        /// <summary>目标从站地址（UnitId）。</summary>
        public byte SlaveAddress { get; private set; }

        /// <summary>要读取的寄存器区类型。</summary>
        public RegisterType PointType { get; private set; }

        /// <summary>远端寄存器起始地址，按配置约定需 &gt; 0。</summary>
        public ushort StartAddress { get; private set; }

        /// <summary>读取点数量，需 &gt; 0。</summary>
        public ushort NumberOfPoints { get; private set; }

        /// <summary>写入本地数据存储的目标起始地址。</summary>
        public ushort LocalStartAddress { get; private set; }

        /// <summary> 最后一次读取异常的时间，用于控制重试间隔。</summary>
        internal long LastExceptionTime { get; set; }

        internal int Sleep { get; set; } = 0;

        /// <summary>创建空实例，供解析成功后逐属性赋值。</summary>
        internal RegisterPoint()
        {
        }

        /// <summary>创建实例。</summary>
        /// <param name="slaveAddress">从站地址（UnitId）。</param>
        /// <param name="pointType">寄存器区类型。</param>
        /// <param name="startAddress">远端起始地址。</param>
        /// <param name="numberOfPoints">读取点数，需大于 0。</param>
        /// <param name="localAddress">写入本地存储的起始地址。</param>
        public RegisterPoint(byte slaveAddress, RegisterType pointType, ushort startAddress, ushort numberOfPoints, ushort localAddress)
        {
            if (!Enum.IsDefined(typeof(RegisterType), pointType))
                throw new ArgumentOutOfRangeException(nameof(pointType), pointType, "非法的寄存器区类型。");

            if (numberOfPoints == 0)
                throw new ArgumentException("numberOfPoints must be greater than 0.", nameof(numberOfPoints));

            PointType = pointType;
            SlaveAddress = slaveAddress;
            LocalStartAddress = localAddress;
            StartAddress = startAddress;
            NumberOfPoints = numberOfPoints;
        }

        /// <summary>判断两个对象是否来自同一段远端寄存器（从站地址、区类型、远端起始地址相同），不比较点数与本地地址。</summary>
        /// <param name="other">另一块配置；为 null 时返回 false。</param>
        /// <returns>同源返回 true。</returns>
        public bool IsSameSource(RegisterPoint other)
        {
            if (ReferenceEquals(other, null)) return false;

            return Key == other.Key;
        }

        /// <summary>判断两块映射到的本地存储区间是否有重叠。</summary>
        /// <remarks>
        /// 区间按半开区间处理：[LocalAddress, LocalAddress + NumberOfPoints)。复杂度 O(1)。
        /// </remarks>
        /// <param name="other">另一块配置；为 null 时返回 false。</param>
        /// <returns>有重叠返回 true。</returns>
        public bool IsOverlap(RegisterPoint other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (PointType != other.PointType) return false;

            // 用 int 计算，避免 LocalAddress + NumberOfPoints 超过 65535 时溢出
            var end = LocalStartAddress + NumberOfPoints;
            var otherEnd = other.LocalStartAddress + other.NumberOfPoints;

            return LocalStartAddress < otherEnd && other.LocalStartAddress < end;
        }

        #region Equals & GetHashCode & operator ==
        /// <summary>判断两块配置是否完全一样（从站地址、区类型、远端起始地址、点数、本地地址都相同）。</summary>
        /// <remarks>不比较负载数据（Values）；比较的是"地址描述"。</remarks>
        /// <param name="other">另一块配置；为 null 时返回 false。</param>
        /// <returns>相同返回 true。</returns>
        public bool Equals(RegisterPoint other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(this, other)) return true;

            if (PointType != other.PointType) return false;
            if (SlaveAddress != other.SlaveAddress) return false;

            if (LocalStartAddress != other.LocalStartAddress) return false;
            if (StartAddress != other.StartAddress) return false;
            if (NumberOfPoints != other.NumberOfPoints) return false;

            return true;
        }

        /// <summary>判断两块配置是否完全一样。</summary>
        /// <param name="obj">待比较对象。</param>
        /// <returns>相同返回 true。</returns>
        public override bool Equals(object obj)
        {
            return Equals(obj as RegisterPoint);
        }

        /// <summary>按五个地址字段计算哈希，与 Equals 保持一致。</summary>
        /// <returns>哈希值。</returns>
        public override int GetHashCode()
        {
            unchecked
            {
                var hash = 17;
                hash = hash * 31 + SlaveAddress;
                hash = hash * 31 + (int)PointType;
                hash = hash * 31 + StartAddress;
                hash = hash * 31 + NumberOfPoints;
                hash = hash * 31 + LocalStartAddress;
                return hash;
            }
        }

        /// <summary>判断两块配置是否完全相同。</summary>
        /// <param name="left">左值，可为 null。</param>
        /// <param name="right">右值，可为 null。</param>
        /// <returns>相同返回 true。</returns>
        public static bool operator ==(RegisterPoint left, RegisterPoint right)
        {
            return ReferenceEquals(left, null) ? ReferenceEquals(right, null) : left.Equals(right);
        }
        /// <summary>判断两块配置是否不同。</summary>
        /// <param name="left">左值，可为 null。</param>
        /// <param name="right">右值，可为 null。</param>
        /// <returns>不同返回 true。</returns>
        public static bool operator !=(RegisterPoint left, RegisterPoint right)
        {
            return !(left == right);
        }
        #endregion

        /// <inheritdoc /> 
        public override string ToString()
        {
            return $"RegistersPoint: {Name} {PointType}, SlaveAddress:{SlaveAddress}, StartAddress:{StartAddress}, NumberOfPoints:{NumberOfPoints}, LocalAddress:{LocalStartAddress}";
        }

        /// <summary>在一组配置中找出所有本地区间重叠的配对，供配置校验使用。</summary>
        /// <remarks>复杂度 O(n²)：配置块数通常只有几十个，够用且绝对不会漏检；块数上千时再改扫描线算法。</remarks>
        /// <param name="points">配置集合。</param>
        /// <returns>重叠配对列表，无重叠时为空列表。</returns>
        /// <exception cref="ArgumentNullException">points 为 null。</exception>
        public static List<(RegisterPoint First, RegisterPoint Second)> FindOverlaps(IEnumerable<RegisterPoint> points)
        {
            if (points == null) throw new ArgumentNullException(nameof(points));

            var list = new List<RegisterPoint>();
            foreach (var point in points)
            {
                if (point != null) list.Add(point);
            }

            var result = new List<(RegisterPoint, RegisterPoint)>();
            for (var i = 0; i < list.Count; i++)
            {
                for (var j = i + 1; j < list.Count; j++)
                {
                    if (list[i].IsOverlap(list[j])) result.Add((list[i], list[j]));
                }
            }

            return result;
        }

        /// <summary>尝试从 XML 节点解析寄存器块配置。</summary>
        /// <remarks>
        /// 全部属性均为必填，且 StartAddress、NumberOfPoints 必须大于 0（配置按 1 基地址约定，0 视为非法）。
        /// XML 属性名由 nameof(属性名) 决定：Type/SlaveAddress/StartAddress/NumberOfPoints/LocalAddress，
        /// 因此重命名属性会改变配置契约，需同步修改配置文件。
        /// </remarks>
        /// <param name="element">包含 Type/SlaveAddress/StartAddress/NumberOfPoints/LocalAddress 属性的 XML 节点。</param>
        /// <param name="registers">解析成功时返回配置对象，失败时为 null。</param>
        /// <returns>解析成功返回 true；必填属性缺失、格式非法或数值不合法返回 false。</returns>
        public static bool TryParse(XElement element, out RegisterPoint registers)
        {
            registers = null;
            if (element == null) throw new ArgumentNullException(nameof(element));

            // 任一属性缺失（Attribute 为 null）时，?.Value 传入 null 会导致 TryParse 失败，等同于必填校验            
            if (!Enum.TryParse(element.Attribute(nameof(PointType))?.Value, true, out RegisterType pointType))
            {
                Trace.TraceWarning($"寄存器块属性 {nameof(PointType)} 缺失或格式非法。");
                return false;
            }
            if (!Enum.IsDefined(typeof(RegisterType), pointType))
            {
                Trace.TraceWarning($"无法识别的的寄存器类型：{element.Attribute(nameof(PointType))?.Value} {pointType}.");
                return false;
            }
            if (!ushort.TryParse(element.Attribute(nameof(NumberOfPoints))?.Value, out var numberOfPoints) || numberOfPoints <= 0)
            {
                Trace.TraceWarning($"寄存器块属性 {nameof(NumberOfPoints)} 缺失、格式非法或数值不合法。");
                return false;
            }

            byte slaveAddress = 0x01;
            if (byte.TryParse(element.Attribute(nameof(SlaveAddress))?.Value, out byte _slaveAddress))
            {
                slaveAddress = _slaveAddress;
            }
            ushort localStartAddress = 0;
            if (ushort.TryParse(element.Attribute(nameof(LocalStartAddress))?.Value, out var _localStartAddress))
            {
                localStartAddress = _localStartAddress;
            }
            ushort startAddress = 0;
            if (ushort.TryParse(element.Attribute(nameof(StartAddress))?.Value, out var _startAddress))
            {
                startAddress = _startAddress;
            }

            registers = new RegisterPoint();

            registers.PointType = pointType;
            registers.SlaveAddress = slaveAddress;
            registers.StartAddress = startAddress;

            registers.NumberOfPoints = numberOfPoints;
            registers.LocalStartAddress = localStartAddress;

            registers.Name = element.Attribute(nameof(Name))?.Value;

            if (int.TryParse(element.Attribute(nameof(Sleep))?.Value, out var _sleep) && _sleep > 0)
                registers.Sleep = _sleep;

            return true;
        }

    }

    /// <summary>
    /// 带负载数据的寄存器块：在 <see cref="RegisterPoint"/> 的地址描述之上，附带一组待写入 / 已读出的值。
    /// 典型用法：RegistersPoint&lt;ushort&gt; 承载保持寄存器的写入值（配合 <see cref="ModbusBusSynchronizer"/> 的写入队列）。
    /// </summary>
    /// <remarks>
    /// 相等性沿用基类：只比较地址描述，不比较 Values。
    /// </remarks>
    /// <typeparam name="T">值类型：ushort 对应寄存器，bool 对应线圈 / 离散量。</typeparam>
    public class RegisterPoint<T> : RegisterPoint where T : struct
    {
        /// <summary>寄存器值数组；顺序与 <see cref="StartAddress"/> 起始的连续地址一一对应。</summary>
        public T[] Values { get; private set; }

        /// <summary> 构造函数。  </summary>
        public RegisterPoint() { }

        /// <summary>创建带负载数据的寄存器块，点数由 values 长度决定。</summary>
        /// <param name="slaveAddress">从站地址（UnitId）。</param>
        /// <param name="pointType">寄存器区类型。</param>
        /// <param name="startAddress">远端起始地址。</param>
        /// <param name="values">寄存器值，顺序与连续地址一一对应。</param>
        /// <param name="localAddress">写入本地存储的起始地址。</param>
        public RegisterPoint(byte slaveAddress, RegisterType pointType, ushort startAddress, T[] values, ushort localAddress = 0)
            : base(slaveAddress, pointType, startAddress, GetNumberOfPoints(values), localAddress)
        {
            Values = values;
        }

        /// <summary>由值数组长度得到点数，并做合法性校验。</summary>
        /// <param name="values">寄存器值数组。</param>
        /// <returns>点数。</returns>
        /// <exception cref="ArgumentNullException">values 为 null。</exception>
        /// <exception cref="ArgumentException">values 为空数组或长度超过 65535。</exception>
        private static ushort GetNumberOfPoints(T[] values)
        {
            if (values == null) throw new ArgumentNullException(nameof(values), "寄存器值数组不能为空。");
            if (values.Length == 0) throw new ArgumentException("寄存器值数组不能为空数组。", nameof(values));
            if (values.Length > ushort.MaxValue) throw new ArgumentException($"寄存器值数量不能超过 {ushort.MaxValue}。", nameof(values));

            return (ushort)values.Length;
        }

        /// <summary>按地址描述五元组与 <typeparamref name="T"/> 计算哈希，与类型敏感的相等性保持一致。</summary>
        /// <returns>哈希值。</returns>
        public override int GetHashCode()
        {
            unchecked
            {
                return base.GetHashCode() * 31 + typeof(T).GetHashCode();
            }
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return $"{base.ToString()}, Values:{string.Join(",", Values)}";
        }
    }
}
