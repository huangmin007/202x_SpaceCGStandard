using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Xml.Linq;
using SpaceCG.Drawing;
using SpaceCG.Extensions;

namespace SpaceCG.Device
{
    /// <summary>
    /// Led 灯带对象
    /// </summary>
    public class LedStripObject : FramePoolModel
    {
        #region Public Properties
        /// <summary> 获取或设置一个用于存储有关此元素的自定义信息的任意对象值。 </summary>
        public object Tag { get; set; }
        /// <summary>  备注信息，用于标识其它信息  </summary>
        public string Comment { get; set; } = string.Empty;

        /// <summary>
        /// Led 灯带的唯一标识，用于标识 Led 灯带
        /// <para>由端口号和设备地址组成，UID = (Port &lt;&lt; 16) | Address</para>
        /// </summary>
        public uint UID => (uint)(Port << 16 | Address);

        /// <summary>
        /// 获取或设置是否允许渲染当前灯带的数据帧。
        /// </summary>
        /// <remarks>
        /// <para>设置为 <c>false</c> 时，渲染管线将跳过此灯带，不发送任何数据。</para>
        /// <para>可用于实现灯带的暂停/恢复控制，或按条件禁用特定灯带。</para>
        /// <para>默认值：<c>true</c>。</para>
        /// </remarks>
        public bool IsRenderEnabled { get; set; } = true;

        /// <summary>
        /// 【渲染优化参数】Led 灯带的渲染 (数据写入后) 等待超时时间，单位：毫秒，默认为 0 毫秒
        /// <para>有些总线类型在数据写入时阻塞或写入速度较快，导致 Led 灯珠花屏，可以通过数据写入后等待几毫秒，来避免花屏的问题</para>
        /// </summary>
        public int Timeout
        {
            get { return __timeout; }
            set
            {
                if (value < 0 || value > 1000)
                    throw new ArgumentOutOfRangeException($"Timeout 必须在 0-1000 之间.");
                __timeout = value;
            }
        }
        private int __timeout = 0;

        /// <summary>
        /// 【渲染优化参数】 Led 灯带数据的填充数量，默认为 0, 表示数据填充所有灯珠 <see cref="LedCount"/>
        /// <para>该参数如果大于 0，则应该与 <see cref="RepeatCount"/> 参数配合使用，以达渲染数据优化的目</para>
        /// <para>例如：整体呼吸效果、对称流水效果等等</para>
        /// </summary>
        public int FillCount
        {
            get
            {
                if (__fillCount <= 0) return LedCount;
                if (__fillCount > LedCount) return LedCount;
                return __fillCount;
            }
            set
            {
                __fillCount = Math.Max(0, Math.Min(value, LedCount));
            }
        }
        private int __fillCount = 0;

        /// <summary>
        /// 【渲染优化参数】 Led 灯带数据复制次数，默认为 1 次
        /// <para>该参数一般与 <see cref="FillCount"/> 参数配合使用，以达渲染数据优化的目的</para>
        /// </summary>
        public int RepeatCount
        {
            get
            {
                if (__repeatCount < 1) return 1;
                if (__repeatCount > LedCount) return LedCount;
                return __repeatCount;
            }
            set
            {
                __repeatCount = Math.Max(1, Math.Min(value, LedCount));
            }
        }
        private int __repeatCount = 1;

        /// <summary>
        /// Led 灯带的灯珠位置，或者说在位图上的坐标位置，该集合也描述了实际的 Led 灯珠的物理顺序位置
        /// <para>注意：灯珠位置在集合列表中是有先后顺序的，一定要与物理位置或顺序保持一致</para>
        /// </summary>
        public IReadOnlyList<System.Drawing.Point> LedPoints
        {
            get
            {
                if (_ledPointsReadOnly == null)
                    _ledPointsReadOnly = _ledPoints.AsReadOnly();
                return _ledPointsReadOnly;
            }
        }
        private IReadOnlyList<System.Drawing.Point> _ledPointsReadOnly;
        #endregion

        /// <summary> 当前 Led 灯带支持的最大 Led 灯珠数量  </summary>
        private readonly ushort _currentMaxLedCount = MaxRGBLedCount;
        /// <inheritdoc cref="LedPoints"/> 
        private readonly List<System.Drawing.Point> _ledPoints = new List<System.Drawing.Point>(512);

        private LedStripObject()
        {
            // 淡蓝色：0x008EFF
        }

        /// <summary>
        /// Led 灯带的构造函数
        /// </summary>
        /// <param name="address"></param>
        /// <param name="port"></param>
        /// <param name="ledType"></param>
        /// <param name="ledColorFormat"></param>
        public LedStripObject(ushort address, byte port, LedType ledType = LedType.WS2812B, ColorFormat ledColorFormat = ColorFormat.RGB)
        {
            this.Port = port;
            this.Address = address;

            this.LedType = ledType;
            this.ColorFormat = ledColorFormat;

            _currentMaxLedCount = ColorFormat.GetChannelCount() == 3 ? MaxRGBLedCount : MaxWRGBLedCount;
        }

        #region Add/Remove-Point/Points
        /// <summary>
        /// 在当前灯带的结尾处添加一颗灯珠，并映射到位图上的坐标位置
        /// <para>注意：灯珠位置在集合列表中是有先后顺序的，一定要与物理位置或顺序保持一致</para>
        /// </summary>
        /// <param name="point"></param>
        public void AddPoint(System.Drawing.Point point)
        {
            if (_ledPoints.Count >= _currentMaxLedCount)
                throw new ArgumentOutOfRangeException($"Led 灯带({LedType}/{ColorFormat})的灯珠总数量不能超过 {_currentMaxLedCount}.");

            _ledPoints.Add(point);
            LedCount = _ledPoints.Count;
        }
        /// <summary>
        /// 在当前灯带的指定索引处添加一颗灯珠，并映射到位图上的坐标位置
        /// <para>注意：灯珠位置在集合列表中是有先后顺序的，一定要与物理位置或顺序保持一致</para>
        /// </summary>
        /// <param name="index"></param>
        /// <param name="point"></param>
        /// <exception cref="ArgumentException"></exception>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        public void AddPoint(int index, System.Drawing.Point point)
        {
            if (index < 0 || index > _ledPoints.Count)
                throw new ArgumentOutOfRangeException($"索引超出范围.");

            if (_ledPoints.Count >= _currentMaxLedCount)
                throw new ArgumentOutOfRangeException($"Led 灯带的灯珠总数量不能超过 {_currentMaxLedCount}.");

            _ledPoints.Insert(index, point);
            LedCount = _ledPoints.Count;
        }
        /// <summary>
        /// 在当前灯带的结尾处添加一组灯珠，并映射到位图上的坐标位置。
        /// <para>注意：灯珠位置在集合列表中是有先后顺序的，一定要与物理位置或顺序保持一致</para>
        /// </summary>
        /// <param name="points"></param>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        /// <exception cref="ArgumentNullException"></exception>
        public void AddPoints(IEnumerable<System.Drawing.Point> points)
        {
            if (points == null) return;
            var count = points.Count();
            if (count <= 0) return;

            if (_ledPoints.Count + count > _currentMaxLedCount)
                throw new ArgumentOutOfRangeException($"添加的点数超过了 LED 灯带({LedType}/{ColorFormat})的限制长度 {_currentMaxLedCount} 珠。");

            _ledPoints.AddRange(points);
            LedCount = _ledPoints.Count;
        }
        /// <summary>
        /// 在当前灯带的指定索引处添加一组灯珠，并映射到位图上的坐标位置。
        /// <para>注意：灯珠位置在集合列表中是有先后顺序的，一定要与物理位置或顺序保持一致</para>
        /// </summary>
        /// <param name="index"></param>
        /// <param name="points"></param>
        public void AddPoints(int index, IEnumerable<System.Drawing.Point> points)
        {
            if (points == null || !points.Any()) return;

            var ledCount = _ledPoints.Count;
            if (index < 0 || index > ledCount)
                throw new ArgumentOutOfRangeException($"索引超出范围.");

            if (ledCount + points.Count() > _currentMaxLedCount)
                throw new ArgumentOutOfRangeException($"添加的点数超过了 LED 灯带({LedType}/{ColorFormat})的限制长度 {_currentMaxLedCount} 珠。");

            _ledPoints.InsertRange(index, points);
            LedCount = _ledPoints.Count;
        }
        /// <summary>
        /// 在当前灯带的结尾处添加一组灯珠，从 <paramref name="start"/> 到 <paramref name="end"/> 的直线点集，并映射到位图上的坐标位置。
        /// <para>注意：灯珠位置在集合列表中是有先后顺序的，一定要与物理位置或顺序保持一致</para>
        /// </summary>
        /// <param name="start"></param>
        /// <param name="end"></param>
        public void AddPoints(System.Drawing.Point start, System.Drawing.Point end) => AddPoints(DrawingExtensions.GetPoints(start, end));

        /// <summary>
        /// 移除指定索引处的灯珠
        /// </summary>
        /// <param name="index"></param>
        public void RemovePoint(int index)
        {
            _ledPoints.RemoveAt(index);
            LedCount = _ledPoints.Count;
        }
        /// <summary>
        /// 移除指定坐标处的灯珠
        /// </summary>
        /// <param name="point"></param>
        public void RemovePoint(System.Drawing.Point point)
        {
            _ledPoints.Remove(point);
            LedCount = _ledPoints.Count;
        }
        /// <summary>
        /// 移除指定范围的灯珠
        /// </summary>
        /// <param name="index"></param>
        /// <param name="count"></param>
        public void RemovePoints(int index, int count)
        {
            _ledPoints.RemoveRange(index, count);
            LedCount = _ledPoints.Count;
        }
        /// <summary>
        /// 移除指定坐标集合的灯珠
        /// </summary>
        /// <param name="points"></param>
        public void RemovePoints(IEnumerable<System.Drawing.Point> points)
        {
            _ledPoints.RemoveAll(points.Contains);
            LedCount = _ledPoints.Count;
        }

        /// <summary>
        /// 移除所有灯珠
        /// </summary>
        public void ClearPoints()
        {
            _ledPoints.Clear();
            LedCount = _ledPoints.Count;
        }
        /// <summary>
        /// 确定当前灯带是否包含指定坐标处的灯珠
        /// </summary>
        /// <param name="point"></param>
        /// <returns></returns>
        public bool ContainsPoint(System.Drawing.Point point) => _ledPoints.Contains(point);        
        #endregion

#if false
        /// <summary>
        /// 清空待渲染的帧队列
        /// </summary>
        /// <param name="off">是否关闭所有点亮的灯珠</param>
        public void ClearFrames(bool off = false)
        {
            ClearRenderingFrames();
#if true
            var ledCount = LedCount;
            if (off && ledCount > 0)
            {
                var frame = CreateColorFrame(1, ledCount);
                if (ColorFormat.GetChannelCount() == 4)
                {
                    var indices = ColorFormat.GetChannelIndices();
                    for (int i = 0; i < indices.Count; i++)
                    {
                        if (indices[i] == (byte)ColorChannel.A)
                            frame[FrameHeaderLength + i] = 0xFF;
                    }
                }
                AddColorFrame(frame);
            }
#endif
        }
#endif

        /// <inheritdoc/>
        public override string ToString()
        {
            return $"[{nameof(LedStripObject)}] Address:{Address} Port:{Port} Count:{LedCount}";
        }

        /// <summary>
        /// 试图创建 <see cref="LedStripObject"/> 对象的实例
        /// </summary>
        /// <param name="element"></param>
        /// <param name="ledStrip"></param>
        /// <returns></returns>
        public static bool TryCreateInstance(XElement element, out LedStripObject ledStrip)
        {
            ledStrip = null;
            if (element == null || element.Name != nameof(LedStripObject))
            {
                Trace.TraceWarning($"{nameof(LedStripObject)} 配置节点不存在或名称不正确");
                return false;
            }

            ledStrip = new LedStripObject();
            ledStrip.LedType = Enum.TryParse(element.Attribute(nameof(LedType))?.Value, true, out LedType ledType) ? ledType : LedType.WS2812B;
            ledStrip.ColorFormat = Enum.TryParse(element.Attribute(nameof(ColorFormat))?.Value, true, out ColorFormat colorFormat) ? colorFormat : ColorFormat.RGB;

            string pointsString = element.Attribute(nameof(LedPoints))?.Value;
            if (!string.IsNullOrWhiteSpace(pointsString))
            {
                if (DrawingExtensions.TryParsePoints(pointsString, out var _points))
                {
                    ledStrip.AddPoints(_points);
                }
            }
            foreach (var pointElement in element.Elements(nameof(LedPoints)))
            {
                if (DrawingExtensions.TryParsePoints(pointElement.Value, out var _points))
                {
                    ledStrip.AddPoints(_points);
                }
            }

            ledStrip.Comment = element.Attribute(nameof(Comment))?.Value;

            ledStrip.Port = byte.TryParse(element.Attribute(nameof(Port))?.Value, out byte port) ? port : (byte)0;
            ledStrip.Group = ushort.TryParse(element.Attribute(nameof(Group))?.Value, out ushort group) ? group : (ushort)0;
            ledStrip.Address = ushort.TryParse(element.Attribute(nameof(Address))?.Value, out ushort address) ? address : (ushort)0;
            ledStrip.Reserved = ushort.TryParse(element.Attribute(nameof(Reserved))?.Value, out ushort reserved) ? reserved : (ushort)0;

            ledStrip.Timeout = int.TryParse(element.Attribute(nameof(Timeout))?.Value, out int timeout) ? timeout : 0;

            ledStrip.FillCount = int.TryParse(element.Attribute(nameof(FillCount))?.Value, out int fill) ? fill : 0;
            ledStrip.RepeatCount = int.TryParse(element.Attribute(nameof(RepeatCount))?.Value, out int repeat) ? repeat : 1;

            ledStrip.IsRenderEnabled = bool.TryParse(element.Attribute(nameof(IsRenderEnabled))?.Value, out bool isRenderEnabled) ? isRenderEnabled : true;

            return true;
        }

    }
}
