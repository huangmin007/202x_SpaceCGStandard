using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using SpaceCG.Extensions;
using Point = System.Drawing.Point;
using Trace = SpaceCG.Diagnostics.Trace;

namespace SpaceCG.Device
{
    /// <summary>
    /// Led 灯带对象，管理灯珠物理坐标映射、渲染优化参数，并继承 <see cref="FrameRenderModel"/> 的帧队列与去重能力。
    /// </summary>
    /// <remarks>
    /// <para><b>灯珠坐标：</b><see cref="LedPoints"/> 中的顺序即为物理信号顺序，添加时必须与实际布线一致。</para>
    /// <para><b>渲染优化：</b><see cref="FillCount"/> 与 <see cref="RepeatCount"/> 配合可减少数据帧大小，
    /// 适用于呼吸灯、流水灯等对称效果。</para>
    /// <para><b>线程安全：</b>灯珠坐标的增删操作非线程安全，应在初始化阶段完成。渲染队列操作继承自基类的线程安全保证。</para>
    /// </remarks>
    public class LedStripObject : FrameRenderModel
    {
        #region Public Properties
        /// <summary>
        /// 灯带唯一标识，由端口号和设备地址组合计算。
        /// </summary>
        /// <remarks>计算公式：<c>UID = (Port &lt;&lt; 16) | Address</c>。</remarks>
        public uint UID { get; private set; }

        /// <summary>
        /// 【渲染优化参数】填充数量。≤0 时等同于 <see cref="FrameRenderModel.LedCount"/>（全部灯珠）。
        /// </summary>
        /// <remarks>
        /// <para>配合 <see cref="RepeatCount"/> 使用可实现数据压缩渲染。</para>
        /// <para>例如：填充 10 颗灯珠颜色，重复 10 次即可覆盖 100 颗灯珠的对称效果。</para>
        /// </remarks>
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
        /// 【渲染优化参数】数据重复/扩展次数，最小为 1。
        /// </summary>
        /// <remarks>
        /// 配合 <see cref="FillCount"/> 使用。例如：fillCount=10, repeatCount=5 可覆盖 50 颗灯珠。
        /// </remarks>
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
        /// 灯珠在位图上的坐标集合，列表顺序即物理信号顺序。
        /// </summary>
        /// <remarks>
        /// <para><b>重要：</b>坐标顺序必须与灯带实际布线方向一致，否则渲染画面会出现错位。</para>
        /// <para>返回的是 <see cref="_ledPoints"/> 的实时只读视图，灯珠变更时无需重新获取。</para>
        /// </remarks>
        public IReadOnlyList<Point> LedPoints
        {
            get
            {
                if (_ledPointsReadOnly == null)
                    _ledPointsReadOnly = _ledPoints.AsReadOnly();
                return _ledPointsReadOnly;
            }
        }
        /// <summary>
        /// <see cref="LedPoints"/> 的缓存只读包装器。
        /// <see cref="List{T}.AsReadOnly"/> 返回对 <see cref="_ledPoints"/> 的实时视图，灯珠变更时无需重建此缓存。
        /// </summary>
        private IReadOnlyList<Point> _ledPointsReadOnly;

        /// <summary>
        /// 是否允许渲染当前灯带的颜色数据帧。默认 <c>true</c>。
        /// </summary>
        /// <remarks>
        /// <para>设为 <c>false</c> 时，渲染管线跳过此灯带的颜色帧，但不影响指令帧。</para>
        /// <para>可用于暂停/恢复特定灯带的效果渲染。</para>
        /// </remarks>
        public bool IsRenderEnabled { get; set; } = true;
        #endregion

        /// <summary>
        /// 灯珠坐标集合变化时触发（增删改操作后）。
        /// </summary>
        /// <remarks>在 <see cref="AddPoint(Point)"/>、<see cref="RemovePoint(int)"/> 等方法中触发。</remarks>
        public event EventHandler<EventArgs> LedPointsChanged;

        /// <inheritdoc cref="LedPoints"/> 
        private readonly List<Point> _ledPoints = new List<Point>(512);

        /// <summary>
        /// 初始化 Led 灯带实例。
        /// </summary>
        /// <param name="address">设备地址，值范围：0 ~ 4096。</param>
        /// <param name="port">设备端口号，值范围：0 ~ 6。</param>
        /// <param name="ledType">灯带芯片类型，默认 <see cref="LedType.WS2812B"/>。</param>
        /// <param name="ledColorFormat">颜色格式，默认 <see cref="ColorFormat.GRB"/>。</param>
        public LedStripObject(ushort address, byte port, LedType ledType = LedType.WS2812B, ColorFormat ledColorFormat = ColorFormat.GRB)
            : base(address, port, ledType, ledColorFormat)
        {
            RenderingRepeatInterval = 12;
            UID = (uint)(Port << 16 | Address);
        }

        #region Add/Remove-Point/Points
        /// <summary>
        /// 在灯带末尾追加一颗灯珠。
        /// </summary>
        /// <param name="point">灯珠在位图上的坐标。</param>
        /// <exception cref="ArgumentOutOfRangeException">灯珠总数超过 <see cref="FrameRenderModel.CurrentMaxLedCount"/>。</exception>
        public void AddPoint(Point point)
        {
            if (_ledPoints.Count >= CurrentMaxLedCount)
                throw new ArgumentOutOfRangeException($"Led 灯带({LedType}/{ColorFormat})的灯珠总数量不能超过 {CurrentMaxLedCount}.");

            _ledPoints.Add(point);
            LedCount = _ledPoints.Count;
            LedPointsChanged?.Invoke(this, EventArgs.Empty);
        }
        /// <summary>
        /// 在指定索引处插入一颗灯珠。
        /// </summary>
        /// <param name="index">插入位置，范围 [0, 当前灯珠数]。</param>
        /// <param name="point">灯珠在位图上的坐标。</param>
        /// <exception cref="ArgumentOutOfRangeException">索引越界或总数超限。</exception>
        public void AddPoint(int index, Point point)
        {
            if (index < 0 || index > _ledPoints.Count)
                throw new ArgumentOutOfRangeException($"索引超出范围.");

            if (_ledPoints.Count >= CurrentMaxLedCount)
                throw new ArgumentOutOfRangeException($"Led 灯带的灯珠总数量不能超过 {CurrentMaxLedCount}.");

            _ledPoints.Insert(index, point);
            LedCount = _ledPoints.Count;
            LedPointsChanged?.Invoke(this, EventArgs.Empty);
        }
        /// <summary>
        /// 在灯带末尾追加一组灯珠。
        /// </summary>
        /// <param name="points">坐标点集合。null 或空集合时静默返回。</param>
        /// <exception cref="ArgumentOutOfRangeException">追加后总数超限。</exception>
        public void AddPoints(IEnumerable<Point> points)
        {
            if (points == null) return;

            var count = points.Count();
            if (count <= 0) return;

            if (_ledPoints.Count + count > CurrentMaxLedCount)
                throw new ArgumentOutOfRangeException($"添加的点数超过了 LED 灯带({LedType}/{ColorFormat})的限制长度 {CurrentMaxLedCount} 珠。");

            _ledPoints.AddRange(points);
            LedCount = _ledPoints.Count;
            LedPointsChanged?.Invoke(this, EventArgs.Empty);
        }
        /// <summary>
        /// 在指定索引处插入一组灯珠。
        /// </summary>
        /// <param name="index">插入起始位置，范围 [0, 当前灯珠数]。</param>
        /// <param name="points">坐标点集合。null 或空集合时静默返回。</param>
        /// <exception cref="ArgumentOutOfRangeException">索引越界或追加后总数超限。</exception>
        public void AddPoints(int index, IEnumerable<Point> points)
        {
            if (points == null) return;

            var count = points.Count();
            if (count <= 0) return;

            var ledCount = _ledPoints.Count;
            if (index < 0 || index > ledCount)
                throw new ArgumentOutOfRangeException($"索引超出范围.");

            if (ledCount + count > CurrentMaxLedCount)
                throw new ArgumentOutOfRangeException($"添加的点数超过了 LED 灯带({LedType}/{ColorFormat})的限制长度 {CurrentMaxLedCount} 珠。");

            _ledPoints.InsertRange(index, points);
            LedCount = _ledPoints.Count;
            LedPointsChanged?.Invoke(this, EventArgs.Empty);
        }
        /// <summary>
        /// 在灯带末尾添加从 <paramref name="start"/> 到 <paramref name="end"/> 的直线点集。
        /// </summary>
        /// <param name="start">线段起点。</param>
        /// <param name="end">线段终点。</param>
        public void AddPoints(Point start, Point end) => AddPoints(DrawingExtensions.GetPoints(start, end));

        /// <summary>
        /// 移除指定索引处的灯珠。
        /// </summary>
        /// <param name="index"></param>
        public void RemovePoint(int index)
        {
            _ledPoints.RemoveAt(index);
            LedCount = _ledPoints.Count;
            LedPointsChanged?.Invoke(this, EventArgs.Empty);
        }
        /// <summary>
        /// 移除指定坐标处的灯珠（移除第一个匹配项）。
        /// </summary>
        /// <param name="point"></param>
        public void RemovePoint(Point point)
        {
            _ledPoints.Remove(point);
            LedCount = _ledPoints.Count;
            LedPointsChanged?.Invoke(this, EventArgs.Empty);
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
            LedPointsChanged?.Invoke(this, EventArgs.Empty);
        }
        /// <summary>
        /// 移除指定坐标集合的灯珠
        /// </summary>
        /// <param name="points"></param>
        public void RemovePoints(IEnumerable<Point> points)
        {
            _ledPoints.RemoveAll(points.Contains);
            LedCount = _ledPoints.Count;
            LedPointsChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// 移除所有灯珠
        /// </summary>
        public void ClearPoints()
        {
            _ledPoints.Clear();
            LedCount = _ledPoints.Count;
            LedPointsChanged?.Invoke(this, EventArgs.Empty);
        }
        /// <summary>
        /// 确定当前灯带是否包含指定坐标处的灯珠。
        /// </summary>
        /// <param name="point"></param>
        /// <returns></returns>
        public bool ContainsPoint(Point point) => _ledPoints.Contains(point);
        #endregion


        #region AddColorFrame 系列方法（数据帧都添加在 当前对象的渲染队列中）
        /// <summary>
        /// 添加待渲染的颜色数据帧到 <b>当前对象的渲染队列</b>。
        /// <para>输入颜色值 (<see cref="uint"/>类型) 数组 <paramref name="color"/> 颜色通道 <paramref name="colorFormat"/> 必须是 <b>四通道</b> 类型</para>
        /// </summary>
        /// <param name="color">颜色值数据，需要指定颜色通道格式 <paramref name="colorFormat"/></param>
        /// <param name="fromPosition">点亮灯珠 IC 的起始位置。值范围：[0, <see cref="LedCount"/>]。</param>
        /// <param name="repeat">颜色数据重复次数。至少重复次数为 1 ，不能超过灯珠数量。</param>
        /// <param name="colorFormat"><paramref name="color"/> 数据的颜色值格式</param>
        /// <exception cref="ArgumentException"></exception>
        public void AddColorFrame(uint color, int fromPosition, int fillCount, int repeatCount, ColorFormat colorFormat = ColorFormat.ARGB)
        {
            var frame = CreateColorFrame(color, fromPosition, fillCount, repeatCount, colorFormat);
            EnqueueFrame(frame);
        }
        /// <inheritdoc cref="AddColorFrame(IReadOnlyList{uint}, int, int, ColorFormat)"/>
        public void AddColorFrame(IReadOnlyList<byte> colors, int fromPosition, int repeatCount, ColorFormat colorFormat = ColorFormat.RGB)
        {
            var frame = CreateColorFrame(colors, fromPosition, repeatCount, colorFormat);
            EnqueueFrame(frame);
        }
        /// <summary>
        /// 添加待渲染的颜色数据帧到 <b>当前对象的渲染队列</b>。
        /// </summary>
        /// <param name="colors">颜色值数组，需要指定颜色通道格式 <paramref name="colorFormat"/></param>
        /// <param name="fromPosition">点亮灯珠 IC 的起始位置。值范围：[0, <see cref="LedCount"/>]。</param>
        /// <param name="repeatCount">颜色数据重复次数。至少重复次数为 1 ，不能超过灯珠数量。</param>
        /// <param name="colorFormat"><paramref name="colors"/> 数据的颜色值格式</param>
        /// <exception cref="ArgumentException"></exception>
        public void AddColorFrame(IReadOnlyList<uint> colors, int fromPosition, int repeatCount, ColorFormat colorFormat = ColorFormat.ARGB)
        {
            var frame = CreateColorFrame(colors, fromPosition, repeatCount, colorFormat);
            EnqueueFrame(frame);
        }
        #endregion


        /// <inheritdoc/>
        public override string ToString()
        {
            return $"[{nameof(LedStripObject)}] Address:{Address} Port:{Port} Count:{LedCount}";
        }

        /// <summary>
        /// 从 XML 配置节点创建 <see cref="LedStripObject"/> 实例。
        /// </summary>
        /// <param name="element">XML 配置节点，名称必须为 <c>"LedStripObject"</c>。</param>
        /// <param name="ledStrip">输出参数，创建成功时返回实例；失败时为 <c>null</c>。</param>
        /// <returns>创建成功返回 <c>true</c>，否则返回 <c>false</c>。</returns>
        /// <remarks>
        /// <para>支持从属性 <c>LedPoints</c> 或子元素 <c>&lt;LedPoints&gt;</c> 解析坐标数据。</para>
        /// <para>可选属性：Comment、Group、Reserved、Timeout、FillCount、RepeatCount、RenderingRepeatInterval。</para>
        /// </remarks>
        public static bool TryCreateInstance(XElement element, out LedStripObject ledStrip)
        {
            ledStrip = null;
            if (element == null || element.Name != nameof(LedStripObject))
            {
                Trace.TraceWarning($"{nameof(LedStripObject)} 配置节点不存在或名称不正确");
                return false;
            }

            if (!byte.TryParse(element.Attribute(nameof(Port))?.Value, out var port))
            {
                Trace.TraceWarning($"配置节点 {nameof(LedStripObject)} 的 {nameof(Port)} 属性值无效");
                return false;
            }
            if (!ushort.TryParse(element.Attribute(nameof(Address))?.Value, out var address))
            {
                Trace.TraceWarning($"配置节点 {nameof(LedStripObject)} 的 {nameof(Address)} 属性值无效");
                return false;
            }
            
            var ledType = Enum.TryParse(element.Attribute(nameof(LedType))?.Value, true, out LedType _ledType) ? _ledType : LedType.WS2812B;
            var colorFormat = Enum.TryParse(element.Attribute(nameof(ColorFormat))?.Value, true, out ColorFormat _colorFormat) ? _colorFormat : ColorFormat.GRB;

            ledStrip = new LedStripObject(address, port, ledType, colorFormat);

            // 解析灯珠坐标：优先从属性读取，再从子元素读取
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

            // 解析其他可选属性
            ledStrip.Comment = element.Attribute(nameof(Comment))?.Value;

            var groupAttr = element.Attribute(nameof(Group));
            if (groupAttr != null && ushort.TryParse(groupAttr.Value, out ushort group)) ledStrip.Group = group;

            var reservedAttr = element.Attribute(nameof(Reserved));
            if (reservedAttr != null && ushort.TryParse(reservedAttr.Value, out ushort reserved)) ledStrip.Reserved = reserved;
            
            var timeoutAttr = element.Attribute(nameof(Timeout));
            if (timeoutAttr != null && int.TryParse(timeoutAttr.Value, out int timeout)) ledStrip.Timeout = timeout;

            var fillCountAttr = element.Attribute(nameof(FillCount));
            if (fillCountAttr != null && int.TryParse(fillCountAttr.Value, out int fillCount)) ledStrip.FillCount = fillCount;

            var repeatCountAttr = element.Attribute(nameof(RepeatCount));
            if (repeatCountAttr != null && int.TryParse(repeatCountAttr.Value, out int repeatCount)) ledStrip.RepeatCount = repeatCount;

            var renderRepeatIntervalAttr = element.Attribute(nameof(RenderingRepeatInterval));
            if (renderRepeatIntervalAttr != null && int.TryParse(renderRepeatIntervalAttr.Value, out int renderingRepeatInterval))
                ledStrip.RenderingRepeatInterval = renderingRepeatInterval;

            return true;
        }

    }
}
