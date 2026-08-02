using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Linq;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Threading;
using System.Text;
using System.Xml.Linq;
using SpaceCG.Extensions;
using SpaceCG.IO;
using Trace = SpaceCG.Diagnostics.Trace;
using Bitmap = System.Drawing.Bitmap;
using Imaging = System.Drawing.Imaging;
using Rectangle = System.Drawing.Rectangle;

namespace SpaceCG.Device
{
    /// <summary>
    /// Led 渲染总线对象，管理一条物理传输通道 + 灯带集合 + 渲染线程 + 帧调度
    /// </summary>
    /// <remarks>
    /// <para><b>渲染线程模型：</b>单线程串行消费总线队列和灯带队列，按端口排序后依次写入传输通道。
    /// 通过 <see cref="StartRender"/> / <see cref="StopRender"/> 控制启停。</para>
    /// <para><b>传输通道：</b>支持 SERIAL / TCP / UDP 三种类型，通过 <see cref="ITransportChannel"/> 抽象。</para>
    /// <para><b>线程安全：</b>灯带集合使用 <see cref="ConcurrentDictionary{TKey, TValue}"/>；
    /// 静态集合 <see cref="BusCollections"/> 非线程安全，应在主线程初始化阶段操作。</para>
    /// </remarks>
    public sealed partial class LedRenderBus : FrameRenderModel, IDisposable
    {
        #region Public Property
        /// <summary>
        /// 设备响应超时时间（ms），范围 [10, 1000]。默认 300 ms。
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">值不在合法范围内。</exception>
        public int ResponseTimeout
        {
            get => _responseTimeout;
            set
            {
                if (value < 10 || value > 1000)
                    throw new ArgumentOutOfRangeException($"ResposeTimeout 必须在 10-1000 毫秒之间.");
                _responseTimeout = value;
            }
        }
        private int _responseTimeout = DefaultResponseTimeout;

        /// <summary> 渲染线程是否正在运行。 </summary>
        public bool IsRendering { get; private set; }

        /// <summary>
        /// 渲染线程循环频率（循环次数/秒），区别于 <see cref="Fps"/>（实际渲染帧率）。
        /// </summary>
        /// <remarks>队列为空或灯带暂停时循环仍在运行但 FPS=0，此值反映线程活跃度。 </remarks>
        public int LoopFps { get; private set; } = 0;

        /// <summary> 总线实例 ID，按创建顺序自动分配。 </summary>
        public int BusId { get; private set; } = 0;

        /// <summary> 总线上所有灯带的灯珠总数。 </summary>
        public int TotalLedCount { get; private set; } = 0;

        /// <summary> 总线上所有设备地址的去重集合。 </summary>
        public IEnumerable<ushort> LedDevices { get; private set; }

        /// <summary> 渲染总线关联的 <see cref="LedStripObject"/> 对象的集合，以 UID 为键。 </summary>
        public IReadOnlyDictionary<uint, LedStripObject> LedStrips => _ledStrips;
        private readonly ConcurrentDictionary<uint, LedStripObject> _ledStrips = new ConcurrentDictionary<uint, LedStripObject>(2, 7);
        #endregion


        #region TransportChannel
        /// <summary> 传输通道类型（SERIAL / TCP / UDP）。</summary>
        public ChannelType Type => Channel.Type;
        /// <summary> 底层传输通道对象 </summary>
        private ITransportChannel Channel { get; set; }
        /// <summary> 传输通道名称 </summary>
        public string Name => Channel != null ? Channel.Name : string.Empty;
        /// <summary> 传输通道是否处于连接状态 </summary>
        public bool IsConnected => Channel != null && Channel.IsConnected;
        #endregion


        private Task _renderingTask;
        private CancellationTokenSource _cts;
        /// <summary>响应数据读取缓冲区（1KB）。</summary>
        private readonly byte[] _responseBuffer = new byte[1024];
        /// <summary>响应超时计时器。</summary>
        private readonly Stopwatch _responseStopwatch = new Stopwatch();


        #region Static & Collections 静态集合
        /// <summary>
        /// 所有渲染总线实例的全局集合。
        /// </summary>
        /// <remarks>非线程安全，应在主线程初始化阶段操作。</remarks>
        public static IReadOnlyList<LedRenderBus> Collections
        {
            get
            {
                if (BusCollectionsReadOnly == null)
                    BusCollectionsReadOnly = BusCollections.AsReadOnly();
                return BusCollectionsReadOnly;
            }
        }
        private static IReadOnlyList<LedRenderBus> BusCollectionsReadOnly;
        private static readonly List<LedRenderBus> BusCollections = new List<LedRenderBus>(32);

        /// <summary>
        ///设备异常响应消息码 → 中文说明映射表。
        /// </summary>
        public static readonly IReadOnlyDictionary<string, string> FrameExceptionMessages = new Dictionary<string, string>()
        {
            { "HERR", "指令头错误" },
            { "GERR", "组地址错误，超出最大范围值 1024" },
            { "AERR", "设备地址错误，超出最大范围值 4096" },
            { "PERR", "端口地址错误，超出最大范围值 6" },
            { "CERR", "功能码错误" },
            { "IERR", "LED 灯带类型错误" },
            { "LERR", "数据长度错误" },
            { "RERR", "扩展次数错误，取范围值在 1~1024" },
            { "TERR", "指令尾部错误" },
            { "DERR", "数据长度与颜色数据字节数不符" },
            { "Timeout", "数据帧接收不完整或接收超时" },
            { "SaveInsErr", "设置上电 显示(0x9B)/关闭(0x9C) 颜色保存失败" },
            { nameof(ResponseTimeout), "自定义，设备响应超时" },
        };
        #endregion


        /// <summary>
        /// 初始化渲染总线实例。总线级别禁用重复帧去重（<see cref="FrameRenderModel.RenderingRepeatInterval"/> = 0）。
        /// </summary>
        /// <param name="channelType">传输通道类型。</param>
        /// <param name="channelParams">通道参数，多个参数以逗号、冒号或分号分隔。</param>
        /// <param name="defaultLedType">默认灯带类型。</param>
        /// <param name="defaultColorFormat">默认颜色格式。</param>
        /// <exception cref="ArgumentNullException">channelParams 为空。</exception>
        /// <exception cref="ArgumentException">channelParams 格式不正确。</exception>
        public LedRenderBus(ChannelType channelType, string channelParams, LedType defaultLedType = LedType.WS2812B, ColorFormat defaultColorFormat = ColorFormat.GRB)
            : base(0, 0, defaultLedType, defaultColorFormat)
        {
            if (string.IsNullOrWhiteSpace(channelParams))
                throw new ArgumentNullException(nameof(channelParams), "参数不能为空");

            string[] arguments = null;
            if (channelParams.IndexOf(',') != -1) arguments = channelParams.Split(',');
            else if (channelParams.IndexOf(':') != -1) arguments = channelParams.Split(':');
            else if (channelParams.IndexOf(';') != -1) arguments = channelParams.Split(';');
            else throw new ArgumentException("参数格式不正确，多个参数以逗号分隔", nameof(channelParams));

            if (channelType == ChannelType.SERIAL)
                Channel = new SerialPortTransport(arguments);
            else if (channelType == ChannelType.TCP)
                Channel = new TcpClientTransport(arguments);
            else if (channelType == ChannelType.UDP)
                Channel = new UdpClientTransport(arguments);

            Channel.ReadTimeout = 300;
            Channel.WriteTimeout = 300;
            BusCollections.Add(this);

            BusId = BusCollections.Count;
            RenderingRepeatInterval = 0;
        }


        #region 辅助方法
        /// <summary> 根据地址和端口查找渲染模型。address=0 或 port=0 时返回总线自身。 </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private FrameRenderModel GetRenderModel(ushort address, byte port)
        {
            if (address == 0 || port == 0) return this;
            return LedStrips.Values.FirstOrDefault(x => x.Address == address && x.Port == port);
        }
        #endregion


        #region 添加/移除灯带
        /// <summary> 灯珠变更时更新总线统计信息（最长灯带长度、总数、设备地址集合）。</summary>
        private void OnPointsChanged(object sender, EventArgs args)
        {
            LedCount = _ledStrips.Values.Max(x => x.LedCount);
            TotalLedCount = _ledStrips.Values.Sum(x => x.LedCount);
            LedDevices = LedStrips.Values.Select(x => x.Address).Distinct().ToArray();
        }
        /// <summary>
        /// 将灯带添加到总线
        /// </summary>
        /// <param name="ledStrip"></param>
        /// <exception cref="ArgumentNullException">ledStrip 为 null。</exception>
        /// <exception cref="ArgumentException">UID 重复。</exception>
        public void AddLedStrip(LedStripObject ledStrip)
        {
            if (ledStrip == null) 
                throw new ArgumentNullException(nameof(ledStrip), "参数不能为空");
            if (_ledStrips.ContainsKey(ledStrip.UID)) 
                throw new ArgumentException($"LedStrip Address:{ledStrip.Address} Port:{ledStrip.Port} UID:{ledStrip.UID:X8} 已经存在于总线中", nameof(LedStripObject));

            if (!_ledStrips.TryAdd(ledStrip.UID, ledStrip))
                throw new ArgumentException($"添加 LedStrip 到总线失败");

            OnPointsChanged(this, EventArgs.Empty);
            ledStrip.LedPointsChanged += OnPointsChanged;
        }
        /// <summary>
        /// 从总线中移除指定的灯带
        /// </summary>
        /// <param name="uid"></param>
        public void RemoveLedStrip(uint uid)
        {
            if (_ledStrips.TryRemove(uid, out var ledStrip))
            {
                ledStrip.LedPointsChanged -= OnPointsChanged;
                OnPointsChanged(this, EventArgs.Empty);
            }
        }
        /// <summary>
        /// 从总线中移除指定的灯带
        /// </summary>
        /// <param name="ledStrip"></param>
        /// <returns></returns>
        public void RemoveLedStrip(LedStripObject ledStrip) => RemoveLedStrip(ledStrip.UID);
        /// <summary>
        /// 清空总线上所有灯带。
        /// </summary>
        public void ClearLedStrips()
        {
            foreach (var ledStrip in _ledStrips.Values)
            {
                ledStrip.LedPointsChanged -= OnPointsChanged;
            }
            _ledStrips.Clear();

            LedCount = 0;
            TotalLedCount = 0;
            LedDevices = Array.Empty<ushort>();
        }
        /// <summary>
        /// 跟据 UID 获取指定的灯带对象，不存在返回 null。
        /// </summary>
        /// <param name="uid"></param>
        /// <returns></returns>
        public LedStripObject GetLedStrip(uint uid) => _ledStrips.TryGetValue(uid, out var ledStrip) ? ledStrip : null;
        /// <summary>
        /// 跟据地址和端口获取指定的灯带对象，不存在返回 null。
        /// </summary>
        /// <param name="address"></param>
        /// <param name="port"></param>
        /// <returns></returns>
        public LedStripObject GetLedStrip(ushort address, byte port) => _ledStrips.Values.FirstOrDefault(x => x.Address == address && x.Port == port);
        #endregion


        #region Channel / Render Contrl 渲染控制
        /// <summary> 打开总线传输通道  </summary>
        public void OpenChannel() => Channel.Open();
        /// <summary> 关闭总线传输通道  </summary>
        public void CloseChannel() => Channel.Close();

        /// <summary> 启动渲染线程（LongRunning Task）  </summary>
        public void StartRender()
        {
            if (IsRendering) return;
            if (_cts != null || _renderingTask != null) return;

            _cts = new CancellationTokenSource();
            _renderingTask = Task.Factory.StartNew(RenderingBusThread, this, _cts.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }
        /// <summary> 停止渲染线程，等待最多 1 秒后强制释放资源。 </summary>
        public void StopRender()
        {
            try { _cts?.Cancel(); }
            catch { }

            try
            {
                _renderingTask?.Wait(1000);
                _renderingTask?.Dispose();
            }
            finally { _renderingTask = null; }

            try { _cts?.Dispose(); }
            finally { _cts = null; }

            IsRendering = false;
            _responseStopwatch.Stop();
        }

        /// <summary>
        /// 暂停指定地址灯带的颜色帧渲染。
        /// </summary>
        /// <param name="address">设备地址(所有端口号)，0 表示所有设备</param>
        public void PauseRender(ushort address)
        {
            if (address == 0)
            {
                foreach (var ledStrip in LedStrips.Values)
                    ledStrip.IsRenderEnabled = false;
            }
            else
            {
                foreach (var ledStrip in LedStrips.Values)
                {
                    if (ledStrip.Address == address)
                    {
                        ledStrip.IsRenderEnabled = false;
                        break;
                    }
                }
            }
        }
        /// <summary>
        /// 恢复指定地址灯带的颜色帧渲染。
        /// </summary>
        /// <param name="address">设备地址(所有端口号)，0 表示所有设备</param>
        public void ResumeRender(ushort address)
        {
            if (address == 0)
            {
                foreach (var ledStrip in LedStrips.Values)
                    ledStrip.IsRenderEnabled = true;
            }
            else
            {
                foreach (var ledStrip in LedStrips.Values)
                {
                    if (ledStrip.Address == address)
                    {
                        ledStrip.IsRenderEnabled = true;
                        break;
                    }
                }
            }
        }
        /// <summary>
        /// 清空指定地址灯带的渲染队列。
        /// </summary>
        /// <param name="address">设备地址，0 表示所有设备。</param>
        /// <param name="clear">清空后是否发送全黑帧关闭灯带。</param>
        public void ClearRender(ushort address, bool clear)
        {
            if (address == 0)
            {
                foreach (var ledStrip in LedStrips.Values)
                {
                    ledStrip.ClearRenderingFrames();
                }
            }
            else
            {
                foreach (var ledStrip in LedStrips.Values)
                {
                    if (ledStrip.Address == address)
                    {
                        ledStrip.ClearRenderingFrames();
                        break;
                    }
                }
            }

            if (clear)
            {
                this.AddColorFrame(address, 0x00, 0x00000000, 0, 1, this.LedCount, ColorFormat.ARGB);
            }
        }
        #endregion


        #region 设置上电显示颜色/设备波特率/设备数据处理超时时间
        /// <summary>
        /// 创建一个固定长度为 21 字节的数据帧。
        /// </summary>
        /// <param name="group">组地址，范围 [0, 1024]。</param>
        /// <param name="address">设备地址，范围 [0, 4096]。</param>
        /// <param name="port">端口号，范围 [0, 6]。</param>
        /// <param name="funCode">功能码。</param>
        /// <param name="value">3 字节数据值（如波特率、超时时间）。</param>
        /// <exception cref="ArgumentOutOfRangeException">参数超出合法范围。</exception>
        private static byte[] CreateEmptyFrame(ushort group, ushort address, byte port, byte funCode, int value)
        {
            if (port < 0 || port > 6) throw new ArgumentOutOfRangeException(nameof(port), "端口号不能大于 6");
            if (group < 0 || group > 1024) throw new ArgumentOutOfRangeException(nameof(group), "组地址不能大于 1024");
            if (address < 0 || address > 4096) throw new ArgumentOutOfRangeException(nameof(address), "地址不能大于 4096");

            byte[] frame = new byte[21];

            frame[0] = 0xDD;
            frame[1] = 0x55;
            frame[2] = 0xEE;

            frame[3] = (byte)(group >> 8);          // 组地址
            frame[4] = (byte)(group & 0xFF);

            frame[5] = (byte)(address >> 8);        // 设备地址
            frame[6] = (byte)(address & 0xFF);

            frame[7] = port;                        // 端口号

            frame[8] = funCode;                     // 功能码
            frame[9] = (byte)LedType.WS2812B;       // 灯带类型

            frame[10] = 0x00;                       // 保留字节
            frame[11] = 0x00;

            frame[12] = 0x00;     // 数据长度
            frame[13] = 0x03;

            frame[14] = 0x00;    // 扩展次数
            frame[15] = 0x01;

            frame[16] = (byte)(value >> 16);
            frame[17] = (byte)(value >> 8);
            frame[18] = (byte)(value & 0xFF);

            frame[frame.Length - 2] = 0xAA;
            frame[frame.Length - 1] = 0xBB;

            return frame;
        }

        /// <summary>
        /// 设置设备波特率（功能码 0x95）。<b>注意：请谨慎操作，修改设备波特率后，设备需要重新上电后生效</b>。
        /// <para>设备波特率只支持：9600、115200、230400、460800、921600，其它波特率设备不支持</para>
        /// </summary>
        /// <param name="group">组地址。</param>
        /// <param name="address">设备地址。</param>
        /// <param name="baudRate">波特率，仅支持 9600/115200/230400/460800/921600。</param>
        /// <exception cref="ArgumentException">波特率不在支持列表中。</exception>
        public void SetDeviceBaudRate(ushort group, ushort address, int baudRate = 921600)
        {
            if (baudRate != 9600 && baudRate != 115200 && baudRate != 230400 && baudRate != 460800 && baudRate != 921600)
                throw new ArgumentException("设备波特率只支持：9600、115200、230400、460800、921600，其它波特率设备不支持");

            EnqueueFrame(CreateEmptyFrame(group, address, 0x00, 0x95, baudRate));
        }
        /// <summary>
        /// 设置总线上设备处理数据的超时时间（功能码 0x8E）。控制器串口通信超时时间默认值是 5ms，可修改的范围是 5ms ~ 1000ms。
        /// <para>大部分情况下，主机向控制器发送的每条指令都是一次性发送的，中间不会断开，所以不需要修改控制器的通信超时时间。 
        /// 只有在受主机硬件限制，主机做不到把一条显示指令一次性发送完时，也就是一条指令被分成多段发送时。这时如果收到的控制器反馈是 "timeout",
        /// 那么就需要修改控制器的通信超时时间。</para>
        /// <para>举例：使用网口转串口设备给控制发送命令时，单条 IP 包最长 1492 个字节，如果显示指令长度超过了 1492 字节，就会被网络分成多次发送，此时可能收到控制器回复 timeout，这时可以尝试修改控制器通信超时时间。</para>
        /// </summary>
        /// <param name="group">组地址。</param>
        /// <param name="address">设备地址。</param>
        /// <param name="timeout">超时时间（ms），范围 [5, 1000]。</param>
        /// <exception cref="ArgumentException">超时时间不在合法范围内。</exception>
        public void SetDeviceTimeout(ushort group, ushort address, ushort timeout = 5)
        {
            if (timeout < 5 || timeout > 1000) throw new ArgumentException("超时时间范围必须在 5-1000 之间");

            EnqueueFrame(CreateEmptyFrame(group, address, 0x00, 0x8E, timeout));
        }
        /// <summary>
        /// 设置设备上电显示颜色（功能码 0x9B）或关闭上电显示（功能码 0x9C）。
        /// </summary>
        /// <param name="address">设备地址。</param>
        /// <param name="port">端口号。</param>
        /// <param name="color">上电颜色值（4 通道格式）。</param>
        /// <param name="isShow">true=设置上电颜色，false=关闭上电显示。</param>
        /// <param name="colorFormat">输入颜色格式，必须是 4 通道。</param>
        /// <exception cref="ArgumentException">设备不存在或输入格式错误。</exception>
        public void SetPowerOnColor(ushort address, byte port, uint color, bool isShow = true, ColorFormat colorFormat = ColorFormat.ARGB)
        {
            FrameRenderModel renderModel = GetRenderModel(address, port);
            if (renderModel == null)
                throw new ArgumentException($"未找到地址为 {address} 端口为 {port} 的设备", nameof(address)); ;

            byte[] frame = renderModel.CreateEmptyColorFrame(1, renderModel.LedCount);

            // 设置上电显示的颜色（9B）
            // 关闭上电显示功能（9C）
            frame[8] = (byte)(isShow ? 0x9B : 0x9C);    // 功能码

            if (isShow)
            {
                // 通道索引表
                var inputIndices = colorFormat.GetChannelIndices();
                var outputIndices = ColorFormat.GetChannelIndices();

                // 颜色的通道数量
                int inputChannelCount = inputIndices.Count;
                int outputChannelCount = outputIndices.Count;

                if (inputChannelCount != 4)
                    throw new ArgumentException("输入颜色值 (uint类型) 的通道数量必须为 4 ", nameof(colorFormat));

                int index = -1, outputOffset = 0;
                for (var j = 0; j < outputChannelCount; j++)
                {
                    outputOffset = j + FrameHeaderLength;
                    index = inputIndices.IndexOf(outputIndices[j]);
                    frame[outputOffset] = (byte)((color >> (24 - index * 8)) & 0xFF);
                }
            }

            EnqueueFrame(frame);
        }
        #endregion


        #region RenderBitmap/Pixels
        /// <summary>
        /// 将 <see cref="Bitmap"/> 渲染到总线的所有灯带。二维渲染，参考 <see cref="LedStripObject.LedPoints"/> 集合的顺序及坐标数据。
        /// <para><see cref="LedStripObject"/> 对象会跟据灯珠的坐标位置在 <paramref name="bitmap"/> 上取数据进行渲染</para>
        /// </summary>
        /// <param name="bitmap">待渲染的位图，格式锁定为 24bpp RGB。</param>
        /// <exception cref="ArgumentException">bitmap 为 null 或尺寸为 0。</exception>
        public unsafe void RenderBitmap(Bitmap bitmap)
        {
            if (bitmap == null || bitmap.Width <= 0 || bitmap.Height <= 0)
                throw new ArgumentException("参数不能为空或图像尺寸不得为 0");

            var bmpd = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height),
                Imaging.ImageLockMode.ReadOnly,
                Imaging.PixelFormat.Format24bppRgb);

            try
            {
                RenderPixels(bmpd.Scan0, bitmap.Width, bitmap.Height, bmpd.Stride, ColorFormat.BGR);
            }
            catch (Exception ex)
            {
                Trace.TraceError($"[{Name}] RenderBitmap Error: {ex}");
            }
            finally
            {
                bitmap.UnlockBits(bmpd);
            }
        }

        /// <inheritdoc cref="RenderPixels(byte*, int, int, int, ColorFormat)"/>
        public unsafe void RenderPixels(IntPtr pixels, int width, int height, int stride, ColorFormat pixelFormat)
            => RenderPixels((byte*)pixels, width, height, stride, pixelFormat);

        /// <summary>
        /// 将像素数据渲染到总线的所有灯带，逐灯珠根据其坐标从像素缓冲区采样。。二维渲染，参考 <see cref="LedStripObject.LedPoints"/> 集合的顺序及坐标数据。
        /// <para><see cref="LedStripObject"/> 对象会跟据自身参数取 <paramref name="pixels"/> 的部份数据进行渲染</para>
        /// </summary>
        /// <param name="pixels">图像的像素数据的指针</param>
        /// <param name="width">图像的宽度（像素）</param>
        /// <param name="height">图像的高度（像素）</param>
        /// <param name="stride">图像的扫描宽度，必须 ≥ width × 通道数。</param>
        /// <param name="pixelFormat">图像的颜色格式</param>
        /// <exception cref="ArgumentException">参数无效。</exception>
        /// <remarks>
        /// <para>超出图像范围的灯珠坐标填充黑色（0x00）。</para>
        /// <para>输出通道在输入中不存在时（如 WRGB 输出但 BGR 输入缺少 W），填充 0xFF（最大亮度）。</para>
        /// </remarks>
        public unsafe void RenderPixels(byte* pixels, int width, int height, int stride, ColorFormat pixelFormat)
        {
            if (pixels == null || width <= 0 || height <= 0 || stride < width * 3)
                throw new ArgumentException("参数不能为空或图像尺寸不得为 0");

            var inputIndices = pixelFormat.GetChannelIndices();    // 输入像素排列的通道索引表
            var inputChannelCount = inputIndices.Count;            // 颜值的通道数量

            if (stride < width * inputChannelCount)
                throw new ArgumentException($"stride 必须大于等于 width * {inputChannelCount}");

            var pixelRect = new Rectangle(0, 0, width, height);

            try
            {
                foreach (var ledStrip in LedStrips.Values)
                {
                    var ledCount = ledStrip.LedCount;
                    if (ledCount <= 0) continue;

                    // 如果指定了灯珠的填充数量，则使用填充数量，不按其实际的灯珠数量
                    if (ledStrip.FillCount > 0)
                        ledCount = ledStrip.FillCount;

                    // 输出像素排列的通道索引表
                    var outputIndices = ledStrip.ColorFormat.GetChannelIndices();
                    var outputChannelCount = outputIndices.Count;

                    // 预计算索引映射，如果存在 -1, 则表示需要补充 Alpha 通道
                    int[] channelMap = new int[outputChannelCount];
                    for (var i = 0; i < outputChannelCount; i++)
                    {
                        channelMap[i] = inputIndices.IndexOf(outputIndices[i]);
                    }

                    var ledPoints = ledStrip.LedPoints;
                    var frameOffset = FrameHeaderLength;
                    var frame = ledStrip.CreateEmptyColorFrame(ledCount, ledStrip.RepeatCount);

                    for (int i = 0; i < ledCount; i++)
                    {
                        var point = ledPoints[i];

                        // 超出图像范围的坐标点填充渲染黑色
                        if (!pixelRect.Contains(point))
                        {
                            for (int j = 0; j < outputChannelCount; j++)
                            {
                                frame[frameOffset++] = 0x00;
                            }
                            continue;
                        }
                        
                        byte* pixelOffset = pixels + point.Y * stride + point.X * inputChannelCount;

                        for (int j = 0; j < outputChannelCount; j++)
                        {
                            var index = channelMap[j];
                            byte* pixel = pixelOffset + index;

                            //Trace.Write($"index:{index} pixel:{*pixel} ,,, ");
                            frame[frameOffset++] = (index >= 0) ? *pixel : (byte)0xFF;
                        }
                    }
                                        
                    ledStrip.AddColorFrame(frame);
                    //Trace.WriteLine($"length::{frameOffset}//{frame.Length}");
                }
            }
            catch (Exception ex)
            {
                Trace.TraceError($"[{Name}] RenderPixels Error: {ex}");
            }
        }
        #endregion


        #region AddColorFrame （数据帧都添加在 当前对象的渲染队列中）
        /// <summary>
        /// 添加待渲染的颜色数据帧到 <b>总线渲染队列</b>（委托给匹配的渲染模型创建帧，然后入队到总线）。
        /// </summary>
        /// <param name="address"></param>
        /// <param name="port"></param>
        /// <param name="color"></param>
        /// <param name="fromPosition"></param>
        /// <param name="fillCount"></param>
        /// <param name="repeatCount"></param>
        /// <param name="colorFormat"></param>
        /// <exception cref="ArgumentException"></exception>
        public void AddColorFrame(ushort address, byte port, uint color, int fromPosition, int fillCount, int repeatCount, ColorFormat colorFormat = ColorFormat.ARGB)
        {
            FrameRenderModel renderModel = GetRenderModel(address, port);
            if (renderModel == null)
                throw new ArgumentException($"未找到地址为 {address} 端口为 {port} 的设备", nameof(address));

            var frame = renderModel.CreateColorFrame(color, fromPosition, fillCount, repeatCount);
            EnqueueFrame(frame);
        }
        
        /// <inheritdoc cref="AddColorFrame(ushort, byte, IReadOnlyList{uint}, int, int, ColorFormat)"/> 
        public void AddColorFrame(ushort address, byte port, IReadOnlyList<byte> colors, int fromPosition, int repeatCount, ColorFormat colorFormat = ColorFormat.RGB)
        {
            FrameRenderModel renderModel = GetRenderModel(address, port);
            if (renderModel == null)
                throw new ArgumentException($"未找到地址为 {address} 端口为 {port} 的设备", nameof(address));

            var frame = renderModel.CreateColorFrame(colors, fromPosition, repeatCount);
            EnqueueFrame(frame);
        }
        /// <summary>
        /// 添加待渲染的颜色数据帧到 <b>总线渲染队列</b>（委托给匹配的渲染模型创建帧，然后入队到总线）。
        /// </summary>
        /// <param name="address"></param>
        /// <param name="port"></param>
        /// <param name="colors"></param>
        /// <param name="fromPosition">点亮灯珠 IC 的起始位置。值范围：[0, <see cref="LedCount"/>]。</param>
        /// <param name="repeatCount"></param>
        /// <param name="colorFormat"></param>
        /// <exception cref="ArgumentException"></exception>
        public void AddColorFrame(ushort address, byte port, IReadOnlyList<uint> colors, int fromPosition, int repeatCount, ColorFormat colorFormat = ColorFormat.ARGB)
        {
            FrameRenderModel renderModel = GetRenderModel(address, port);

            if (renderModel == null)
                throw new ArgumentException($"未找到地址为 {address} 端口为 {port} 的设备", nameof(address));

            var frame = renderModel.CreateColorFrame(colors, fromPosition, repeatCount);
            EnqueueFrame(frame);
        }
        #endregion


        #region Channel Read/Write Thread
        /// <summary>
        /// 将数据帧立即写入连接通道，并根据功能码等待设备响应。
        /// <para>按 921600 波特率发送数据，一个字节(10bit)需要 10.85us，发送 1024 字节需要 1024 * 10.85us ≈ 11.6ms </para>
        /// <para>点亮一颗灯珠的时间为 30us，要点亮 1024 颗灯珠需要 1024 * 30us + 50us(复位信号) ≈ 30.77ms </para>
        /// </summary>
        /// <param name="frame">待发送的完整帧。</param>
        /// <returns>返回设备响应消息，无响应或组/广播地址返回空字符串 <see cref="string.Empty"/>; 超时时返回 <c>"ResponseTimeout"</c>。</returns>
        private string WriteFrame(byte[] frame)
        {
            if (Channel == null || !Channel.IsConnected) return string.Empty;

            Channel.Write(frame, 0, frame.Length);

            var funCode = frame[8];
            var port = frame.GetPort();
            var group = frame.GetGroup();
            var address = frame.GetAddress();

            // 记录非颜色数据帧
            if (funCode != 0x98 && funCode != 0x99)
                Trace.TraceInformation($"RenderBus {Name} Write Frame({frame.Length} bytes) to Device({group}/{address}/{port}): FunctionCode(0x{funCode:X2})");

            if (group != 0x0000) return string.Empty;
            if (address == 0x0000) return string.Empty;

            const string RecvEnd = nameof(RecvEnd);
            const string DisplayEnd = nameof(DisplayEnd);
            const string SaveInsEnd = nameof(SaveInsEnd);
            const string SaveInsErr = nameof(SaveInsErr);

            var message = string.Empty;

            // 0x99 显示颜色数据
            // 0x98 从指定的 IC 显示颜色数据
            // RecvEnd -> DisplayEnd
            if (funCode == 0x98 || funCode == 0x99)
            {
                message = ReadResponseUntil(msg => msg.Contains(DisplayEnd));
            }
            // 设置上电显示的颜色 (0x9B); 关闭上电显示功能 (9C) 
            // RecvEnd -> SaveInsEnd||SaveInsErr 
            else if (funCode == 0x9B || funCode == 0x9C)
            {
                message = ReadResponseUntil(msg => msg.Contains(SaveInsEnd) || msg.Contains(SaveInsErr));
            }
            // 修改控制器串口通信超时时间 (8E)；修改通信超时时间后立即生效，无需重启
            // RecvEnd
            else if (funCode == 0x8E)
            {
                message = ReadResponseUntil(m => m.Contains(RecvEnd));
            }

            Channel.ClearReadBuffer();
            //Debug.WriteLine($"RenderBus {Name} Write Frame {frame.Length} bytes, Respose Use Time:{_resposeStopwatch.ElapsedMilliseconds} ms");

            return message;
        }
        /// <summary>
        /// 循环读取响应直到满足条件或超时。
        /// </summary>
        /// <param name="condition">终止条件。</param>
        /// <returns>累积的响应消息；超时返回 <c>"ResponseTimeout"</c>。</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private string ReadResponseUntil(Func<string, bool> condition)
        {
            var count = 0;
            var message = string.Empty;
            var responseTimeout = ResponseTimeout;

            _responseStopwatch.Restart();

            while (!condition(message))
            {
                if (_responseStopwatch.ElapsedMilliseconds > responseTimeout)
                {
                    return nameof(ResponseTimeout);
                }

                if (Channel.Available <= 0)
                {
                    Thread.Sleep(1);
                    //Thread.Sleep(0);
                    //Thread.Yield();
                    continue;
                }

                var bytesRead = Channel.Read(_responseBuffer, count, Channel.Available);
                if (bytesRead > 0) count += bytesRead;
                message = Encoding.UTF8.GetString(_responseBuffer, 0, count);
            }

            return message;
        }
        /// <summary>
        /// 渲染总线主循环线程（LongRunning Task）。串行消费总线队列和灯带队列，按端口排序后依次写入通道。
        /// </summary>
        /// <param name="state"></param>
        /// <remarks>
        /// <para><b>调度顺序：</b>先消费总线队列（指令帧、颜色帧），再消费各灯带队列（颜色帧）。</para>
        /// <para><b>FPS 结算：</b>每约 985ms 结算一次，重置各灯带和总线自身的渲染计数器。</para>
        /// <para><b>异常处理：</b>连续 8 次异常帧后主动断开通道触发重连。</para>
        /// </remarks>
        private static void RenderingBusThread(object state)
        {
            LedRenderBus renderBus = state as LedRenderBus;
            if (renderBus == null) return;

            var busName = renderBus.Name;
            var cancellationToken = renderBus._cts.Token;
            var ledStrips = renderBus.LedStrips.Values.OrderBy(x => x.Port).ToList();

            Trace.TraceInformation($"[{busName}] 开始同步渲染，线程ID：{Thread.CurrentThread.ManagedThreadId}，灯带数量：{renderBus.LedStrips.Count} 条，最长灯带灯珠数量：{renderBus.LedCount} 颗，灯珠总数量：{renderBus.TotalLedCount} 颗");

            var loopCount = 0;              // 线程循环计数
            var writeFrameCount = 0;        // 总线渲染的总帧数
            var exceptionFrameCount = 0;    // 异常帧计数

            renderBus.IsRendering = true;
            Stopwatch stopwatch = Stopwatch.StartNew();

            while (!cancellationToken.IsCancellationRequested)
            {
                if (renderBus.Channel == null) break;

                #region 连接状态检查
                while (!renderBus.Channel.IsConnected && !cancellationToken.IsCancellationRequested)
                {
                    // 等待 3 秒后尝试重连，避免 Stop/Close/Dispose 等待超时
                    for (int i = 0; i < 30; i++)
                    {
                        Thread.Sleep(100);
                        if (cancellationToken.IsCancellationRequested) break;
                    }

                    try
                    {
                        renderBus.Channel.Close();
                        Thread.Sleep(200);
                        if (cancellationToken.IsCancellationRequested) break;

                        renderBus.Channel.Open();
                    }
                    catch (Exception ex)
                    {
                        Trace.TraceWarning($"渲染通道 {busName} 连接异常，{ex.Message}");
                    }

                    Thread.Sleep(200);
                }
                #endregion

                var ledStripsCount = renderBus.LedStrips.Count;
                if (ledStripsCount == 0)
                {
                    Thread.Sleep(16);
                    continue;
                }

                // 对端口进行排序，以便串行通道的效率提升
                if (ledStrips.Count != ledStripsCount)
                    ledStrips = renderBus.LedStrips.Values.OrderBy(x => x.Port).ToList();

                var hasWrittenFrame = false;

                #region 总线上的帧队列数据
                while (renderBus.TryDequeueFrame(out var frame))
                {
                    if (cancellationToken.IsCancellationRequested || renderBus.Channel == null) break;

                    try
                    {
                        writeFrameCount++;
                        hasWrittenFrame = true;
                        var message = renderBus.WriteFrame(frame);

                        if (!string.IsNullOrEmpty(message))
                        {
                            message = message.Trim().Replace("\r\n", " ");
                            if (FrameExceptionMessages.ContainsKey(message))
                            {
                                exceptionFrameCount++;
                                renderBus.Channel.ClearReadBuffer();
                                Trace.TraceError($"RenderBus {busName} Respose Error Message({message}): {FrameExceptionMessages[message]}");
                            }
                            else
                            {
                                exceptionFrameCount = 0;
                                //Trace.WriteLine($"RenderBus {busName} Respose Message: {message}");
                            }
                        }
                        else
                        {
                            if (renderBus.Timeout > 0) Thread.Sleep(renderBus.Timeout);
                        }
                    }
                    catch (Exception ex)
                    {
                        exceptionFrameCount++;
                        Trace.TraceWarning($"RenderBus {busName} Render Exception: {ex}");
                    }
                }
                #endregion

                #region 灯带上的帧队列数据
                foreach (var ledStrip in ledStrips)
                {
                    if (cancellationToken.IsCancellationRequested || renderBus.Channel == null) break;

                    if (!ledStrip.TryDequeueFrame(out var frame)) continue;
                    if (!ledStrip.IsRenderEnabled && (frame[8] == 0x98 || frame[8] == 0x99)) continue;

                    try
                    {
                        writeFrameCount++;
                        hasWrittenFrame = true;
                        var message = renderBus.WriteFrame(frame);

                        if (!string.IsNullOrEmpty(message))
                        {
                            message = message.Trim().Replace("\r\n", " ");
                            if (FrameExceptionMessages.ContainsKey(message))
                            {
                                exceptionFrameCount++;

                                ledStrip.ResetRenderingState();
                                renderBus.Channel.ClearReadBuffer();
                                Trace.TraceError($"RenderBus {busName}(Address:{ledStrip.Address} Port:{ledStrip.Port}) Device Respose Error Message({message}): {FrameExceptionMessages[message]} ");
                            }
                            else
                            {
                                exceptionFrameCount = 0;
                                //Trace.WriteLine($"RenderBus {busName}(Address:{ledStrip.Address} Port:{ledStrip.Port}) Device Respose Message: {message}");
                            }
                        }
                        else
                        {
                            if (ledStrip.Timeout > 0) Thread.Sleep(ledStrip.Timeout);
                        }
                    }
                    catch (Exception ex)
                    {
                        exceptionFrameCount++;
                        Trace.TraceWarning($"RenderBus [{busName}] {ledStrip} Render Exception: {ex}");
                    }
                }
                #endregion

                // 计算帧频
                var elapsed = stopwatch.ElapsedMilliseconds;
                if (elapsed > 985) // 1000 - 15.625
                {
                    foreach (var ledStrip in ledStrips)
                    {
                        ledStrip.ResetRenderingState();
                        if (!ledStrip.IsRenderEnabled) ledStrip.Fps = 0;
                    }

                    renderBus.ResetRenderingState();
                    renderBus.Fps = writeFrameCount;
                    renderBus.LoopFps = loopCount;

                    loopCount = 0;
                    writeFrameCount = 0;

                    stopwatch.Restart();
                }

                // 若连续异常帧超过指定数量则断开连接
                if (exceptionFrameCount > 8)
                {
                    Trace.TraceWarning($"RenderBus [{busName}] 超出指定数量的异常帧断开连接通道。");
                    exceptionFrameCount = 0;
                    renderBus.Channel.Close();
                }

                loopCount++;
                if (!hasWrittenFrame) Thread.Sleep(1);
            }

            foreach (var ledStrip in renderBus.LedStrips.Values)
            {
                ledStrip.ResetRenderingState();
            }

            renderBus.IsRendering = false;
            renderBus.ResetRenderingState();

            stopwatch.Stop();
            Trace.TraceInformation($"[{busName}] 已停止渲染，线程ID：{Thread.CurrentThread.ManagedThreadId}");
        }
        #endregion


        /// <inheritdoc/>
        /// <remarks>
        /// <para>释放顺序：从全局集合移除 → 停止所有灯带渲染 → 清空所有渲染队列 → 总线广播 0x00000000 → 停止渲染线程 → 关闭通道。</para>
        /// </remarks>
        public void Dispose()
        {
            BusCollections.Remove(this);

            if (IsConnected && IsRendering)
            {
                foreach (var ledStrip in LedStrips.Values)
                {
                    ledStrip.IsRenderEnabled = false;
                }

                ClearRender(0, true);
                while (IsConnected && IsRendering && PendingFrameCount > 0)
                {
                    Thread.Sleep(1);
                }
            }

            StopRender();
            ClearLedStrips();

            Channel?.Close();
            Channel?.Dispose();
            Channel = null;
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            return $"[({nameof(LedRenderBus)}] Channel:{Channel.Name}  LedStripCount:{LedStrips.Count}";
        }

        /// <summary>
        /// 从 XML 配置节点创建 <see cref="LedRenderBus"/> 实例。
        /// </summary>
        /// <param name="element">XML 元素，名称必须为 <c>"LedRenderBus"</c>。</param>
        /// <param name="ledRenderBus">输出参数：成功时返回实例，失败时为 null。</param>
        /// <param name="createLedStrips">是否同时创建子节点中的灯带实例。</param>
        /// <returns>成功返回 <c>true</c>。</returns>
        public static bool TryCreateInstance(XElement element, out LedRenderBus ledRenderBus, bool createLedStrips = true)
        {
            ledRenderBus = null;
            if (element == null || element.Name != nameof(LedRenderBus))
            {
                Trace.TraceWarning($"{nameof(LedRenderBus)} 配置节点不存在或名称不正确");
                return false;
            }
            if (!Enum.TryParse<ChannelType>(element.Attribute(nameof(Type))?.Value, true, out var type))
            {
                Trace.TraceWarning($"{nameof(LedRenderBus)} 配置节点中 {nameof(Type)} 属性值不正确");
                return false;
            }
            if (string.IsNullOrWhiteSpace(element.Attribute("Params")?.Value))
            {
                Trace.TraceWarning($"{nameof(LedRenderBus)} 配置节点中 Params 属性值不能为空");
                return false;
            }

            // 必需要有子节点
            var ledStripElements = element.Elements(nameof(LedStripObject));
            if (ledStripElements.Count() <= 0)
            {
                Trace.TraceWarning($"{nameof(LedRenderBus)} 配置节点中 {nameof(LedStripObject)} 节点不存在");
                return false;
            }

            var ledType = Enum.TryParse(element.Attribute(nameof(LedType))?.Value, true, out LedType _ledType) ? _ledType : LedType.WS2812B;
            var colorFormat = Enum.TryParse(element.Attribute(nameof(ColorFormat))?.Value, true, out ColorFormat _colorFormat) ? _colorFormat : ColorFormat.GRB;

            ledRenderBus = new LedRenderBus(type, element.Attribute("Params").Value, ledType, colorFormat);
            ledRenderBus.Comment = element.Attribute(nameof(Comment))?.Value;

            var timeoutAttr = element.Attribute(nameof(Timeout));
            if (timeoutAttr != null && int.TryParse(timeoutAttr.Value, out var timeout)) ledRenderBus.Timeout = timeout;

            var responseTimeoutAttr = element.Attribute(nameof(ResponseTimeout));
            if (responseTimeoutAttr != null && int.TryParse(responseTimeoutAttr.Value, out var resposeTimeout)) ledRenderBus.ResponseTimeout = resposeTimeout;

            if (createLedStrips)
            {
                foreach (var ledStripElement in ledStripElements)
                {
                    if (LedStripObject.TryCreateInstance(ledStripElement, out var ledStrip))
                    {
                        ledRenderBus.AddLedStrip(ledStrip);
                    }
                }
            }

            return true;
        }

    }

}
