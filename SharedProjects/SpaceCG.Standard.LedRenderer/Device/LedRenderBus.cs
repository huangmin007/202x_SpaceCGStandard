using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using SpaceCG.IO;

namespace SpaceCG.Device
{
    /// <summary>
    /// Led 渲染总线对象
    /// </summary>
    public sealed class LedRenderBus : IDisposable
    {
        /// <summary>
        /// 默认数据帧发送后等待的时间，单位：毫秒
        /// </summary>
        internal const int DefaultTimeout = 10;
        /// <summary>
        /// 默认设备响应超时时间，单位：毫秒
        /// </summary>
        internal const int DefaultResposeTimeout = 300;

        /// <summary>
        /// 总线上的公共帧写入后等待的时间，单位：ms；默认 10 ms
        /// <para>针对无响应信息的数据帧，例如：组地址不为 0，设备地址为 0 </para>
        /// </summary>
        public int Timeout
        {
            get => _timeout;
            set
            {
                if (value < 0 || value > 1000)
                    throw new ArgumentOutOfRangeException($"Timeout 必须在 0-1000 毫秒之间.");
                _timeout = value;
            }
        }
        private int _timeout = DefaultTimeout;

        /// <summary>
        /// 设备响应超时时间，单位：毫秒；默认 300 ms
        /// </summary>
        public int ResposeTimeout
        {
            get => _resposeTimeout;
            set
            {
                if (value < 10 || value > 1000)
                    throw new ArgumentOutOfRangeException($"ResposeTimeout 必须在 10-1000 毫秒之间.");
                _resposeTimeout = value;
            }        
        }
        private int _resposeTimeout = DefaultResposeTimeout;

        /// <summary>
        /// 备注信息，用于标识总线的用途或其他信息
        /// </summary>
        public string Comment { get; set; } = string.Empty;

        /// <summary>
        /// 传输通道类型
        /// </summary>
        public TransportType Type => Channel.Type;
        /// <summary>
        /// 传输通道对象
        /// </summary>
        private ITransportChannel Channel { get;  set; }        
        /// <summary>
        /// 传输通道名称
        /// </summary>
        public string Name => Channel != null ? Channel.Name : string.Empty;
        /// <summary>
        /// 传输通道是否处于连接状态
        /// </summary>
        public bool IsConnected => Channel != null && Channel.IsConnected;
        /// <summary>
        /// 总线是否处于渲染状态
        /// </summary>
        public bool IsRendering { get; private set; } = false;
        /// <summary>
        /// 当前总线的渲染帧率
        /// </summary>
        public int Fps { get; private set; } = 0;
        /// <summary>
        /// 每帧的渲染处理时间，单位：ms, 默认为 40ms
        /// </summary>
        public int Interval
        {
            get => _interval;
            set
            {
                if (value < 16 || value > 1000)
                    throw new ArgumentOutOfRangeException(nameof(value), "渲染间隔时间必须在16ms~1000ms之间");
                _interval = value;
            }
        }
        private int _interval = 40;

        /// <summary> 获取或设置一个用于存储有关此元素的自定义信息的任意对象值。 </summary>
        public object Tag { get; set; }

        /// <summary>  登记在总线上最长的灯带灯珠数量  </summary>
        public ushort MaxLedCount { get; private set; } = 0;
        /// <summary>  登记在总线上所有灯带的灯珠总数量  </summary>
        public uint TotalLedCount { get; private set; } = 0;
        /// <summary>  默认没有登记的灯珠类型  </summary>
        public LedType DefaultLedType { get; private set; } = LedType.WS2812B;
        /// <summary>  默认没有登记的灯珠颜色格式  </summary>
        public ColorFormat DefaultColorFormat { get; private set; } = ColorFormat.RGB;
        /// <summary>
        ///  总线上所有 Led 设备的地址集合(非重复的设备地址集合)
        /// </summary>
        public IEnumerable<ushort> LedDevices { get; private set; }
       
        private Task _renderTask;
        private volatile int _renderFps = 0;
        private volatile bool _isRendering = false;

        private byte[] _resposeBuffer = new byte[1024 * 32];
        private Stopwatch _resposeStopwatch = new Stopwatch();

        /// <summary>
        /// 渲染总线关联的 <see cref="LedStripObject"/> 对象的集合
        /// <para>Key = <see cref="LedStripObject.Port"/> &lt;&lt; 16 | <see cref="LedStripObject.Address"/></para>
        /// </summary>
        public IReadOnlyDictionary<uint, LedStripObject> LedStrips => _ledStrips;
        private readonly ConcurrentDictionary<uint, LedStripObject> _ledStrips = new ConcurrentDictionary<uint, LedStripObject>(2, 7);
        
        /// <summary>
        /// 总线上的帧队列
        /// </summary>
        private readonly ConcurrentQueue<byte[]> _frameQueue = new ConcurrentQueue<byte[]>();
        /// <summary>
        /// Led 数据帧渲染异常消息字典
        /// </summary>
        public static readonly IReadOnlyDictionary<string, string> FrameExceptionMessages = new Dictionary<string, string>()
        {
            {"HERR", "指令头错误" },
            {"GERR", "组地址错误，超出最大范围值 1024" },
            {"AERR", "设备地址错误，超出最大范围值 4096" },
            {"PERR", "端口地址错误，超出最大范围值 30" },
            {"CERR", "功能码错误" },
            {"IERR", "LED 灯带类型错误" },
            {"LERR", "数据长度错误" },
            {"RERR", "扩展次数错误，取范围值在 1~1024" },
            {"TERR", "指令尾部错误" },
            {"DERR", "数据长度与颜色数据字节数不符" },
            {"Timeout", "数据帧接收不完整或接收超时" },
            {"SaveInsErr", "设置上电 显示(0x9B)/关闭(0x9C) 颜色保存失败" },
            {"ResponseTimeout", "自定义，设备响应超时" },
        };

        #region Static Collections 静态集合
        /// <summary>
        /// 所有渲染总线的集合
        /// </summary>
        public static IReadOnlyList<LedRenderBus> Collections
        {
            get
            {
                if (BusCollectionsReadOnly == null)
                    BusCollectionsReadOnly = BusCollections.AsReadOnly();
                return BusCollectionsReadOnly;
            }
        }
        internal static Timer FpsTimer;
        private static int checkTick = 0;
        private static readonly List<LedRenderBus> BusCollections;
        private static IReadOnlyList<LedRenderBus> BusCollectionsReadOnly;
        static LedRenderBus()
        {
            BusCollections = new List<LedRenderBus>(32);
            FpsTimer = new Timer(OnTimerCallback, null, 300, 1000);
        }
        private static void OnTimerCallback(object state)
        {
            // 计时帧率计算法
            foreach (var bus in BusCollections)
            {
                foreach (var ledStrip in bus.LedStrips.Values)
                {
                    ledStrip.ResetRenderFps();
                }
                bus.Fps = Interlocked.Exchange(ref bus._renderFps, 0);
            }

            checkTick++;
            if (checkTick > 3)
            {
                checkTick = 0;
                BusCollections.CheckConnection();
            }
        }
        #endregion

        /// <summary>
        /// Led 渲染总线
        /// </summary>
        /// <param name="type">传输通道类型</param>
        /// <param name="transportParams">传输通道参数，多个参数以逗号分隔</param>
        /// <param name="defaultLedType">默认没有登记的/统一灯珠类型</param>
        /// <param name="defaultColorFormat">默认没有登记的/统一灯珠颜色</param>
        public LedRenderBus(TransportType type, string transportParams, LedType defaultLedType = LedType.WS2812B, ColorFormat defaultColorFormat = ColorFormat.RGB)
        {
            if (string.IsNullOrWhiteSpace(transportParams))
                throw new ArgumentNullException(nameof(transportParams), "参数不能为空");

            string[] transportParamsArray;
            if (transportParams.IndexOf(',') != -1)
                transportParamsArray = transportParams.Split(',');
            else if (transportParams.IndexOf(':') != -1)
                transportParamsArray = transportParams.Split(':');
            else if (transportParams.IndexOf(';') != -1)
                transportParamsArray = transportParams.Split(';');
            else
                throw new ArgumentException("参数格式错误，多个参数以逗号分隔");

            Channel = new TransportChannel(type, transportParamsArray);
            Channel.ReadTimeout = 300;
            Channel.WriteTimeout = 300;

            BusCollections.Add(this);
            DefaultLedType = defaultLedType;
            DefaultColorFormat = defaultColorFormat;        
        }


        #region 添加/移除灯带
        /// <summary>
        /// 添加灯带到总线
        /// </summary>
        /// <param name="ledStrip"></param>
        /// <returns></returns>
        public void AddLedStrip(LedStripObject ledStrip)
        {
            if (ledStrip == null) 
                throw new ArgumentNullException(nameof(ledStrip), "参数不能为空");
            if (_ledStrips.ContainsKey(ledStrip.UID)) 
                throw new ArgumentException($"LedStrip Address:{ledStrip.Address} Port:{ledStrip.Port} UID:{ledStrip.UID:X8} 已经存在于总线中", nameof(LedStripObject));

            if (!_ledStrips.TryAdd(ledStrip.UID, ledStrip))
                throw new ArgumentException($"添加 LedStrip 到总线失败");

            MaxLedCount = _ledStrips.Values.Max(x => x.LedCount);
            TotalLedCount = (uint)_ledStrips.Values.Sum(x => x.LedCount);
            LedDevices = LedStrips.Values.Select(x => x.Address).Distinct();
        }
        /// <summary>
        /// 从总线中移除指定的灯带
        /// </summary>
        /// <param name="uid"></param>
        public void RemoveLedStrip(uint uid)
        {
            _ledStrips.TryRemove(uid, out _);

            MaxLedCount = _ledStrips.Values.Max(x => x.LedCount);
            TotalLedCount = (uint)_ledStrips.Values.Sum(x => x.LedCount);
            LedDevices = LedStrips.Values.Select(x => x.Address).Distinct();
        }
        /// <summary>
        /// 从总线中移除指定的灯带
        /// </summary>
        /// <param name="ledStrip"></param>
        /// <returns></returns>
        public void RemoveLedStrip(LedStripObject ledStrip) => RemoveLedStrip(ledStrip.UID);
        /// <summary>
        /// 清空总线的所有灯带
        /// </summary>
        public void ClearLedStrips()
        {
            _ledStrips.Clear();

            MaxLedCount = 0;
            TotalLedCount = 0;
            LedDevices = Array.Empty<ushort>();
        }
        #endregion


        #region Render Contrl 渲染控制
        /// <summary>
        /// 启动总线渲染线程
        /// </summary>
        public void StartRender()
        {
            if (_isRendering || _renderTask != null) return;

            if (!Channel.IsConnected)  Channel.Open();

            _isRendering = true;
            _renderTask = Task.Factory.StartNew(RenderingBusThread, this, TaskCreationOptions.LongRunning);
        }
        /// <summary>
        /// 启动总线渲染线程，并指定渲染间隔时间
        /// </summary>
        /// <param name="interval"></param>
        public void StartRender(int interval)
        {
            this.Interval = interval;
            StartRender();        
        }
        /// <summary>
        /// 停止总线渲染线程
        /// </summary>
        public void StopRender()
        {
            if (!_isRendering || _renderTask == null) return;

            _isRendering = false;
            _renderTask?.Wait();
            _renderTask?.Dispose();
            _renderTask = null;
        }
        /// <summary>
        /// 暂停渲染指定的灯带（可用于使用其它方式插入渲染数据，例如：传感器数据、外部的交互数据等影响而临时插入的渲染数据）<br/>
        /// <para>暂停了使用 <see cref="RenderBitmap(System.Drawing.Bitmap)"/> 和 <see cref="RenderPixels(byte*,int,int,int,ColorFormat)"/> 函数渲染的灯带，其它函数仍然可以渲染数据</para>
        /// </summary>
        /// <param name="address">设备地址(所有端口号)，0 表示所有设备</param>
        public void PauseRender(ushort address)
        {
            if (address == 0)
            {
                foreach (var ledStrip in LedStrips.Values)
                {
                    ledStrip.UseBitmapPixels = false;
                    ledStrip.ClearFrames(false);
                }
                return;
            }

            foreach (var ledStrip in LedStrips.Values)
            {
                if (ledStrip.Address == address)
                {
                    ledStrip.UseBitmapPixels = false;
                    ledStrip.ClearFrames(false);
                    break;
                }
            }
        }
        /// <summary>
        /// 恢复渲染指定的灯带（可用于使用其它方式插入渲染数据，例如：传感器数据、外部的交互数据等影响而临时插入的渲染数据）<br/>
        /// <para>恢复了使用 <see cref="RenderBitmap(System.Drawing.Bitmap)"/> 和 <see cref="RenderPixels(byte*,int,int,int,ColorFormat)"/> 函数渲染的灯带</para>
        /// </summary>
        /// <param name="address">设备地址(所有端口号)，0 表示所有设备</param>
        public void ResumeRender(ushort address)
        {
            if (address == 0)
            {
                foreach (var ledStrip in LedStrips.Values)
                {
                    ledStrip.UseBitmapPixels = true;
                }
                return;
            }

            foreach (var ledStrip in LedStrips.Values)
            {
                if (ledStrip.Address == address)
                {
                    ledStrip.UseBitmapPixels = true;
                    break;
                }
            }
        }
        /// <summary>
        /// 清空总线中的指定灯带的待渲染数据
        /// </summary>
        /// <param name="address">设备地址(所有端口号)，0 表示所有设备</param>
        /// <param name="off">是否关闭灯带</param>
        public void ClearRender(ushort address, bool off)
        {
            if (address == 0)
            {
                foreach (var ledStrip in LedStrips.Values)
                {
                    ledStrip.ClearFrames(false);
                }

                if (off)
                {
                    var frame = CreateEmptyFrame(address, 1, MaxLedCount, DefaultLedType, DefaultColorFormat);
                    if (DefaultColorFormat.GetChannelCount() == 4)
                    {
                        var indices = DefaultColorFormat.GetChannelIndices();
                        for (int i = 0; i < indices.Length; i++)
                        {
                            if (indices[i] == (byte)ColorChannel.A)
                                frame[LedStripObject.FrameHeaderLength + i] = 0xFF;
                        }
                    }
                    _frameQueue.Enqueue(frame);
                }
                return;
            }

            foreach (var ledStrip in LedStrips.Values)
            {
                if (ledStrip.Address == address)
                {
                    ledStrip.ClearFrames(false);
                    break;
                }
            }

            if (off)
            {
                var ledCount = MaxLedCount;
                var ledType = DefaultLedType;
                var ledColorFormat = DefaultColorFormat;

                if (address != 0)
                {
                    var ledStrips = from ledStrip in LedStrips.Values
                                    where ledStrip.Address == address
                                    orderby ledStrip.LedCount descending
                                    select ledStrip;

                    if (ledStrips.Count() > 0)
                    {
                        var ledSprit = ledStrips.First();

                        ledType = ledSprit.LedType;
                        ledCount = ledSprit.LedCount;
                        ledColorFormat = ledSprit.ColorFormat;
                    }
                }

                var frame = CreateEmptyFrame(address, 1, ledCount, ledType, ledColorFormat);
                if (DefaultColorFormat.GetChannelCount() == 4)
                {
                    var indices = DefaultColorFormat.GetChannelIndices();
                    for (int i = 0; i < indices.Length; i++)
                    {
                        if (indices[i] == (byte)ColorChannel.A)
                            frame[LedStripObject.FrameHeaderLength + i] = 0xFF;
                    }
                }
                _frameQueue.Enqueue(frame);
            }
        }
        #endregion


        #region RenderBitmap/Pixels
        /// <summary>
        /// 渲染 <see cref="System.Drawing.Bitmap"/> 数据到总线的所有灯带中。二维渲染，参考 <see cref="LedStripObject.LedPoints"/> 集合的顺序及坐标数据。
        /// <para><see cref="LedStripObject"/> 对象会跟据灯珠的坐标位置在 <paramref name="bitmap"/> 上取数据进行渲染</para>
        /// </summary>
        /// <param name="bitmap"></param>
        public unsafe void RenderBitmap(System.Drawing.Bitmap bitmap)
        {
            if (LedStrips.Count == 0 || this.IsRendering == false) return;

            if (bitmap == null || bitmap.Width <= 0 || bitmap.Height <= 0)
                throw new ArgumentException("参数不能为空或图像尺寸不得为 0");

            var bmpd = bitmap.LockBits(new System.Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height),
                System.Drawing.Imaging.ImageLockMode.ReadOnly,
                System.Drawing.Imaging.PixelFormat.Format24bppRgb);

            try
            {
                RenderPixels(bmpd.Scan0, bmpd.Stride, bitmap.Width, bitmap.Height, ColorFormat.BGR);
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
        public unsafe void RenderPixels(IntPtr pixels, int stride, int width, int height, ColorFormat pixelFormat)
            => RenderPixels((byte*)pixels, stride, width, height, pixelFormat);

        /// <summary>
        /// 渲染像素数据到总线的所有灯带中。二维渲染，参考 <see cref="LedStripObject.LedPoints"/> 集合的顺序及坐标数据。
        /// <para><see cref="LedStripObject"/> 对象会跟据自身参数取 <paramref name="pixels"/> 的部份数据进行渲染</para>
        /// </summary>
        /// <param name="pixels">图像的像素数据的指针</param>
        /// <param name="stride">图像的扫描宽度</param>
        /// <param name="width">图像的宽度</param>
        /// <param name="height">图像的高度</param>
        /// <param name="pixelFormat">图像的颜色格式</param>
        /// <exception cref="ArgumentException"></exception>
        public unsafe void RenderPixels(byte* pixels, int stride, int width, int height, ColorFormat pixelFormat)
        {
            if (LedStrips.Count == 0 || this.IsRendering == false) return;

            if (pixels == null || width <= 0 || height <= 0 || stride < width * 3)
                throw new ArgumentException("参数不能为空或图像尺寸不得为 0");

            var inputIndices = pixelFormat.GetChannelIndices();     // 输入像素排列的通道索引表
            var inputChannelCount = inputIndices.Length;            // 颜值的通道数量

            if (stride < width * inputChannelCount)
                throw new ArgumentException($"stride 必须大于等于 width * {inputChannelCount}");

            var pixelRect = new System.Drawing.Rectangle(0, 0, width, height);

            try
            {
                foreach (var ledStrip in LedStrips.Values)
                {
                    if (!ledStrip.UseBitmapPixels) continue;

                    var ledCount = ledStrip.LedCount;
                    if (ledCount <= 0) continue;

                    // 如果指定了灯珠的填充数量，则使用填充数量，不按其实际的灯珠数量
                    if (ledStrip.FillCount > 0)
                        ledCount = ledStrip.FillCount;

                    // 输出像素排列的通道索引表
                    var outputIndices = ledStrip.ColorFormat.GetChannelIndices();
                    var outputChannelCount = outputIndices.Length;

                    // 预计算索引映射，如果存在 -1, 则表示需要补充 Alpha 通道
                    int[] channelMap = new int[outputChannelCount];
                    for (var i = 0; i < outputChannelCount; i++)
                    {
                        channelMap[i] = Array.IndexOf(inputIndices, outputIndices[i]);
                    }

                    var ledPoints = ledStrip.LedPoints;
                    var frameOffset = LedStripObject.FrameHeaderLength;
                    var frame = ledStrip.CreateEmptyFrame(ledCount, ledStrip.RepeatCount);

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


        #region 设置上电显示颜色/设备波特率/设备数据处理超时时间
        /// <summary>
        /// 设置总线上的设备上电显示的颜色
        /// </summary>
        /// <param name="address">设备地址(所有端口号)</param>
        /// <param name="color">设置的颜色值，颜色格式 <paramref name="colorFormat"/> </param>
        /// <param name="isShow">开启/关闭上电显示颜色</param>
        /// <param name="colorFormat"> <paramref name="color"/> 的颜色格式，默认为 ARGB</param>
        public void SetPowerOnColor(ushort address, uint color, bool isShow = true, ColorFormat colorFormat = ColorFormat.ARGB)
        {
            var ledCount = MaxLedCount;
            var ledType = DefaultLedType;
            var ledColorFormat = DefaultColorFormat;

            if (address != 0)
            {
                var ledStrips = from ledStrip in LedStrips.Values
                                where ledStrip.Address == address
                                orderby ledStrip.LedCount descending
                                select ledStrip;

                if (ledStrips.Count() > 0)
                {
                    var ledSprit = ledStrips.First();

                    ledType = ledSprit.LedType;
                    ledCount = ledSprit.LedCount;
                    ledColorFormat = ledSprit.ColorFormat;
                }
            }

            byte[] frame = CreateEmptyFrame(address, 1, ledCount, ledType, ledColorFormat);

            // 设置上电显示的颜色（9B）
            // 关闭上电显示功能（9C）
            frame[8] = (byte)(isShow ? 0x9B : 0x9C);    // 功能码

            if (isShow)
            {
                // 通道索引表
                var inputIndices = colorFormat.GetChannelIndices();
                var outputIndices = ledColorFormat.GetChannelIndices();

                // 颜色的通道数量
                int inputChannelCount = inputIndices.Length;
                int outputChannelCount = outputIndices.Length;

                if (inputChannelCount != 4)
                    throw new ArgumentException("输入颜色值 (uint类型) 的通道数量必须为 4 ", nameof(colorFormat));

                int index = -1, outputOffset = 0;
                for (var j = 0; j < outputChannelCount; j++)
                {
                    outputOffset = j + LedStripObject.FrameHeaderLength;

                    index = Array.IndexOf(inputIndices, outputIndices[j]);

                    frame[outputOffset] = (byte)((color >> (24 - index * 8)) & 0xFF);
                }
            }

            _frameQueue.Enqueue(frame);
        }

        /// <summary>
        /// 设置总线上设备的波特率；<b>注意：请谨慎操作，修改设备波特率后，设备需要重新上电后生效</b>。
        /// <para>设备波特率只支持：9600、115200、230400、460800、921600，其它波特率设备不支持</para>
        /// </summary>
        /// <param name="address"></param>
        /// <param name="baudRate"></param>
        public void SetDeviceBaudRate(ushort address, int baudRate = 921600)
        {
            if (baudRate != 9600 && baudRate != 115200 && baudRate != 230400 && baudRate != 460800 && baudRate != 921600)
                throw new ArgumentException("设备波特率只支持：9600、115200、230400、460800、921600，其它波特率设备不支持");

            byte[] frame = new byte[21];
            frame[0] = 0xDD;
            frame[1] = 0x55;
            frame[2] = 0xEE;

            frame[3] = 0x00;        // 组地址
            frame[4] = 0x00;
            //frame[3] = (byte)(group >> 8);        // 组地址
            //frame[4] = (byte)(group & 0xFF);

            frame[5] = (byte)(address >> 8);        // 设备地址
            frame[6] = (byte)(address & 0xFF);

            frame[7] = 0x00;                        // 端口号

            frame[8] = 0x95;                        // 功能码
            frame[9] = (byte)LedType.WS2812B;       // 灯带类型

            frame[10] = 0x00;                       // 保留字节
            frame[11] = 0x00;

            frame[12] = 0x00;     // 数据长度
            frame[13] = 0x03;

            frame[14] = 0x00;    // 扩展次数
            frame[15] = 0x01;

            frame[16] = (byte)(baudRate >> 16);
            frame[17] = (byte)(baudRate >> 8);
            frame[18] = (byte)(baudRate & 0xFF);

            frame[frame.Length - 2] = 0xAA;
            frame[frame.Length - 1] = 0xBB;

            _frameQueue.Enqueue(frame);
        }

        /// <summary>
        /// 设置总线上设备处理数据的超时时间；控制器串口通信超时时间默认值是 5ms，可修改的范围是 5ms ~ 1000ms。
        /// <para>大部分情况下，主机向控制器发送的每条指令都是一次性发送的，中间不会断开，所以不需要修改控制器的通信超时时间。 
        /// 只有在受主机硬件限制，主机做不到把一条显示指令一次性发送完时，也就是一条指令被分成多段发送时。这时如果收到的控制器反馈是 "timeout",
        /// 那么就需要修改控制器的通信超时时间。</para>
        /// <para>举例：使用网口转串口设备给控制发送命令时，单条 IP 包最长 1492 个字节，如果显示指令长度超过了 1492 字节，就会被网络分成多次发送，此时可能收到控制器回复 timeout，这时可以尝试修改控制器通信超时时间。</para>
        /// </summary>
        /// <param name="address"></param>
        /// <param name="timeout"></param>
        /// <exception cref="ArgumentException"></exception>
        public void SetDeviceTimeout(ushort address, ushort timeout = 5)
        {
            if (timeout < 5 || timeout > 1000) throw new ArgumentException("超时时间范围必须在 5-1000 之间");

            byte[] frame = new byte[21];
            frame[0] = 0xDD;
            frame[1] = 0x55;
            frame[2] = 0xEE;

            frame[3] = 0x00;        // 组地址
            frame[4] = 0x00;
            //frame[3] = (byte)(group >> 8);        // 组地址
            //frame[4] = (byte)(group & 0xFF);

            frame[5] = (byte)(address >> 8);        // 设备地址
            frame[6] = (byte)(address & 0xFF);

            frame[7] = 0x00;                        // 端口号

            frame[8] = 0x8E;                        // 功能码
            frame[9] = (byte)LedType.WS2812B;       // 灯带类型

            frame[10] = 0x00;                       // 保留字节
            frame[11] = 0x00;

            frame[12] = 0x00;     // 数据长度
            frame[13] = 0x03;

            frame[14] = 0x00;    // 扩展次数
            frame[15] = 0x01;

            frame[16] = (byte)(timeout >> 16);
            frame[17] = (byte)(timeout >> 8);
            frame[18] = (byte)(timeout & 0xFF);

            frame[frame.Length - 2] = 0xAA;
            frame[frame.Length - 1] = 0xBB;

            _frameQueue.Enqueue(frame);
        }
        #endregion


        #region AddColorFrame
        /// <summary>
        /// 添加待渲染的帧
        /// </summary>
        /// <param name="address">设备地址(所有端口号)，0 表示所有设备</param>
        /// <param name="r"></param>
        /// <param name="g"></param>
        /// <param name="b"></param>
        /// <param name="repeat">颜色数据重复次数</param>
        public void AddColorFrame(ushort address, byte r, byte g, byte b, ushort repeat) => AddColorFrame(address, (uint)(0xFF << 24 | r << 16 | g << 8 | b), repeat, ColorFormat.ARGB);
        /// <summary>
        /// 添加待渲染的帧
        /// <para>输入颜色值 (<see cref="uint"/>类型) 数组 <paramref name="color"/> 颜色通道 <paramref name="colorFormat"/> 必须是 四通道 类型</para>
        /// </summary>
        /// <param name="address">设备地址(所有端口号)，0 表示所有设备</param>
        /// <param name="color">颜色数据，需要指定颜色通道格式 <paramref name="colorFormat"/></param>
        /// <param name="repeat">颜色数据重复次数</param>
        /// <param name="colorFormat"><paramref name="color"/> 数据的颜色值格式</param>
        /// <exception cref="ArgumentException"></exception>
        public void AddColorFrame(ushort address, uint color, ushort repeat, ColorFormat colorFormat = ColorFormat.ARGB)
        {
            var ledCount = MaxLedCount;
            var ledType = DefaultLedType;
            var ledColorFormat = DefaultColorFormat;

            if (address != 0)
            {
                var ledStrips = from ledStrip in LedStrips.Values
                                where ledStrip.Address == address
                                orderby ledStrip.LedCount descending
                                select ledStrip;

                if (ledStrips.Count() > 0)
                {
                    var ledSprit = ledStrips.First();

                    ledType = ledSprit.LedType;
                    ledCount = ledSprit.LedCount;
                    ledColorFormat = ledSprit.ColorFormat;
                }
            }
            
            if (repeat == 0 || repeat > ledCount)
                throw new ArgumentException($"参数 repeat 不能为 0 或超过灯带(#{address})的灯珠总数量 {ledCount} 范围");

            // 通道索引表
            var inputIndices = colorFormat.GetChannelIndices();
            var outputIndices = ledColorFormat.GetChannelIndices();

            // 颜色的通道数量
            int inputChannelCount = inputIndices.Length;
            int outputChannelCount = outputIndices.Length;

            if (inputChannelCount != 4)
                throw new ArgumentException("输入颜色值 (uint类型) 的通道数量必须为 4 ", nameof(colorFormat));

            int index = -1, outputOffset = 0;
            byte[] frame = CreateEmptyFrame(address, 1, repeat, ledType, ledColorFormat);

            for (var j = 0; j < outputChannelCount; j++)
            {
                outputOffset = j + LedStripObject.FrameHeaderLength;

                index = Array.IndexOf(inputIndices, outputIndices[j]);

                frame[outputOffset] = (byte)((color >> (24 - index * 8)) & 0xFF);
            }

            _frameQueue.Enqueue(frame);
        }
        /// <summary>
        /// 添加待渲染的帧，跟据 <paramref name="colors"/> 数据量 和 <paramref name="repeat"/> 填充灯珠
        /// <para>输入颜色值 (<see cref="byte"/>类型) 数组 <paramref name="colors"/> 颜色通道 <paramref name="colorFormat"/> 可以是 三通道 或 四通道 类型</para>
        /// </summary>
        /// <param name="address">设备地址(所有端口号)，0 表示所有设备</param>
        /// <param name="colors">颜色值数组，需要指定颜色通道格式 <paramref name="colorFormat"/></param>
        /// <param name="repeat">颜色数据重复次数</param>
        /// <param name="colorFormat"><paramref name="colors"/> 数据的颜色值格式</param>
        /// <exception cref="ArgumentException"></exception>
        public void AddColorFrame(ushort address, IReadOnlyList<byte> colors, ushort repeat, ColorFormat colorFormat = ColorFormat.RGB)
        {
            var ledCount = MaxLedCount;
            var ledType = DefaultLedType;
            var ledColorFormat = DefaultColorFormat;

            if (address != 0)
            {
                var ledStrips = from ledStrip in LedStrips.Values
                                where ledStrip.Address == address
                                orderby ledStrip.LedCount descending
                                select ledStrip;

                if (ledStrips.Count() > 0)
                {
                    var ledSprit = ledStrips.First();

                    ledType = ledSprit.LedType;
                    ledCount = ledSprit.LedCount;
                    ledColorFormat = ledSprit.ColorFormat;
                }
            }

            if (colors == null || colors.Count == 0)
                throw new ArgumentException("颜色值数组不能为空，或长度不正确");
            if (repeat == 0 || repeat > ledCount)
                throw new ArgumentException($"参数 repeat 不能为 0 或超过灯带(#{address})的灯珠总数量 {ledCount} 范围");

            // 通道索引表
            var inputIndices = colorFormat.GetChannelIndices();
            var outputIndices = ledColorFormat.GetChannelIndices();

            // 颜值的通道数量
            int inputChannelCount = inputIndices.Length;
            int outputChannelCount = outputIndices.Length;

            if (colors.Count < inputChannelCount || colors.Count % inputChannelCount != 0)
                throw new ArgumentException($"颜色数据长度 {colors.Count} 与通道数 {inputChannelCount} 不匹配", nameof(colors));

            var renderCount = colors.Count / inputChannelCount;
            renderCount = Math.Min(renderCount, ledCount);
            byte[] frame = CreateEmptyFrame(address, (ushort)renderCount, repeat, ledType, ledColorFormat);

            if (colorFormat == ledColorFormat && colors is Array colorsArray)
            {
                var renderSize = renderCount * outputChannelCount;
                Array.Copy(colorsArray, 0, frame, LedStripObject.FrameHeaderLength, renderSize);
            }
            else
            {
                int i = 0, j = 0, index = -1;
                int inputOffset = 0, outputOffset = 0;

                // 预计算通道索引映射
                int[] channelMap = new int[outputChannelCount];
                for (i = 0; i < outputChannelCount; i++)
                {
                    channelMap[i] = Array.IndexOf(inputIndices, outputIndices[i]);
                }

                for (i = 0; i < renderCount; i++)
                {
                    inputOffset = i * inputChannelCount;
                    outputOffset = i * outputChannelCount + LedStripObject.FrameHeaderLength;

                    for (j = 0; j < outputChannelCount; j++)
                    {
                        index = channelMap[j];
                        frame[outputOffset + j] = (index >= 0) ? colors[inputOffset + index] : (byte)0xFF;
                    }
                }
            }

            _frameQueue.Enqueue(frame);
        }
        /// <summary>
        /// 添加待渲染的帧，跟据 <paramref name="colors"/> 数据量 和 <paramref name="repeat"/> 填充灯珠
        /// <para>输入颜色值 (<see cref="uint"/>类型) 数组 <paramref name="colors"/> 颜色通道 <paramref name="colorFormat"/> 必须是 四通道 类型</para>
        /// </summary>
        /// <param name="address">设备地址(所有端口号)，0 表示所有设备</param>
        /// <param name="colors">颜色值数组，需要指定颜色通道格式 <paramref name="colorFormat"/></param>
        /// <param name="repeat">颜色数据重复次数</param>
        /// <param name="colorFormat"><paramref name="colors"/> 数据的颜色值格式</param>
        /// <exception cref="ArgumentException"></exception>
        public void AddColorFrame(ushort address, IReadOnlyList<uint> colors, ushort repeat, ColorFormat colorFormat = ColorFormat.ARGB)
        {
            var ledCount = MaxLedCount;
            var ledType = DefaultLedType;
            var ledColorFormat = DefaultColorFormat;

            if (address != 0)
            {
                var ledStrips = from ledStrip in LedStrips.Values
                                where ledStrip.Address == address
                                orderby ledStrip.LedCount descending
                                select ledStrip;

                if (ledStrips.Count() > 0)
                {
                    var ledSprit = ledStrips.First();

                    ledType = ledSprit.LedType;
                    ledCount = ledSprit.LedCount;
                    ledColorFormat = ledSprit.ColorFormat;
                }
            }

            if (colors == null || colors.Count == 0)
                throw new ArgumentException("颜色值数组不能为空");
            if (repeat == 0 || repeat > ledCount)
                throw new ArgumentException($"参数 repeat 不能为 0 或超过灯带(#{address})的灯珠总数量 {ledCount} 范围");

            // 通道索引表
            var inputIndices = colorFormat.GetChannelIndices();
            var outputIndices = ledColorFormat.GetChannelIndices();

            // 颜色的通道数量
            int inputChannelCount = inputIndices.Length;
            int outputChannelCount = outputIndices.Length;

            if (inputChannelCount != 4)
                throw new ArgumentException("输入颜色值 (uint类型) 的通道数量必须为 4 ", nameof(colorFormat));

            int i = 0, j = 0, index = -1, outputOffset = 0;
            int renderCount = Math.Min(colors.Count, ledCount);
            byte[] frame = CreateEmptyFrame(address, (ushort)renderCount, repeat, ledType, ledColorFormat);

            // 预计算通道索引映射
            int[] channelMap = new int[outputChannelCount];
            for (i = 0; i < outputChannelCount; i++)
            {
                channelMap[i] = Array.IndexOf(inputIndices, outputIndices[i]);
            }

            for (i = 0; i < renderCount; i++)
            {
                outputOffset = i * outputChannelCount + LedStripObject.FrameHeaderLength;

                for (j = 0; j < outputChannelCount; j++)
                {
                    index = channelMap[j];
                    frame[outputOffset + j] = (byte)((colors[i] >> (24 - index * 8)) & 0xFF);
                }
            }

            _frameQueue.Enqueue(frame);
        }
        #endregion


        /// <summary>
        /// 创建空白的颜色（黑色 0x000000）的数据帧（跟据需要填充数据的灯珠数量 <paramref name="fillCount"/> 和 复制数据的次数 <paramref name="repeatCount"/> 的参数创建空白帧）
        /// <para>只是创建数据帧，并未进入待渲染的帧队列中</para>
        /// </summary>
        /// <param name="address">设备地址</param>
        /// <param name="fillCount">需要填充数据的灯珠数量，不得小于 1 </param>
        /// <param name="repeatCount">需要将填充数据复制的次数，不得小于 1 </param>
        /// <param name="ledType">灯带类型</param>
        /// <param name="ledColorFormat">要填充的数据颜色格式</param>
        /// <returns></returns>
        /// <exception cref="ArgumentException"></exception>
        internal byte[] CreateEmptyFrame(ushort address, ushort fillCount, ushort repeatCount, LedType ledType = LedType.WS2812B, ColorFormat ledColorFormat = ColorFormat.RGB)
        {
            var maxLedCount = ledColorFormat.GetMaxLedCount();
            if (fillCount <= 0 || fillCount > maxLedCount)
                throw new ArgumentException($"数据填充的灯珠数量 {fillCount} 不能小于 1 或超过灯珠数量 {maxLedCount} 范围");

            if (repeatCount <= 0 || repeatCount > maxLedCount)
                throw new ArgumentException($"数据复制的次数 {repeatCount} 不能小于 1 或超过灯珠数量 {maxLedCount} 范围");

            var colorSize = fillCount * ledColorFormat.GetChannelCount();
            var frameSize = colorSize + 18;

            byte[] frame = new byte[frameSize];

            frame[0] = 0xDD;
            frame[1] = 0x55;
            frame[2] = 0xEE;

            frame[3] = 0x00;        // 组地址
            frame[4] = 0x00;

            frame[5] = (byte)(address >> 8);        // 设备地址
            frame[6] = (byte)(address & 0xFF);

            frame[7] = 0x00;                        // 端口号

            frame[8] = 0x99;                        // 功能码
            frame[9] = (byte)ledType;               // 灯带类型

            frame[10] = 0x00;                       // 保留字节
            frame[11] = 0x00;

            frame[12] = (byte)(colorSize >> 8);     // 数据长度
            frame[13] = (byte)(colorSize & 0xFF);

            frame[14] = (byte)(repeatCount >> 8);    // 扩展次数
            frame[15] = (byte)(repeatCount & 0xFF);

            frame[frame.Length - 2] = 0xAA;
            frame[frame.Length - 1] = 0xBB;

            return frame;
        }

        /// <summary>
        /// 将数据帧立即写入连接通道
        /// <para>按 921600 波特率发送数据，一个字节(10bit)需要 10.85us，发送 1024 字节需要 1024 * 10.85us ≈ 11.6ms </para>
        /// <para>点亮一颗灯珠的时间为 30us，要点亮 1024 颗灯珠需要 1024 * 30us + 50us(复位信号) ≈ 30.77ms </para>
        /// </summary>
        /// <param name="frame"></param>
        /// <returns></returns>
        internal string WriteFrame(byte[] frame)
        {
            if (Channel == null || !Channel.IsConnected) return null;

            Channel.Write(frame, 0, frame.Length);

            var group = frame.GetGroup();
            var address = frame.GetAddress();

            if (frame[8] != 0x99)
                Trace.TraceInformation($"RenderBus {Name} Write Frame({frame.Length} bytes) to Device({group}/{address}/{frame.GetPort()}): FunctionCode(0x{frame[8]:X2})");

            if (group != 0x0000) return string.Empty;
            if (address == 0x0000) return string.Empty;

            _resposeStopwatch.Restart();
            string message = string.Empty;

            // 0x99 显示颜色数据
            // 0x98 从指定的 IC 显示颜色数据
            if (frame[8] == 0x99 || frame[8] == 0x98)
            {
                //RecvEnd DisplayEnd
                while (!message.Contains("DisplayEnd")) // && _resposeStopwatch.ElapsedMilliseconds < ResposeTimeout
                {
                    if (_resposeStopwatch.ElapsedMilliseconds > ResposeTimeout)
                    {
                        message = "ResponseTimeout";
                        //Trace.TraceWarning($"RenderBus {Name} Write Color Frame {frame.Length} bytes, Respose Timeout {ResposeTimeout} ms, Address:{address} Port:{frame.GetPort()}");
                        break;
                    }
                    if (Channel.Available <= 0) continue;
                    int count = Channel.Read(_resposeBuffer, 0, Channel.Available);
                    message += Encoding.UTF8.GetString(_resposeBuffer, 0, count);
                }
            }
            else if (frame[8] == 0x9B || frame[8] == 0x9C)
            {
                //RecvEnd SaveInsEnd/SaveInsErr 
                while ((!message.Contains("SaveInsEnd") || !message.Contains("SaveInsErr")) && _resposeStopwatch.ElapsedMilliseconds < ResposeTimeout)
                {
                    if (Channel.Available <= 0) continue;
                    int count = Channel.Read(_resposeBuffer, 0, Channel.Available);
                    message += Encoding.UTF8.GetString(_resposeBuffer, 0, count);
                }
            }

            Channel.ClearReadBuffer();
            //Debug.WriteLine($"RenderBus {Name} Write Frame {frame.Length} bytes, Respose Use Time:{_resposeStopwatch.ElapsedMilliseconds} ms");

            return message;
        }

        /// <summary>
        /// 渲染总线渲染线程
        /// </summary>
        /// <param name="state"></param>
        private static void RenderingBusThread(object state)
        {
            LedRenderBus renderBus = state as LedRenderBus;

            if (renderBus == null) return;
            string busName = renderBus.Name;

            int maxLedCount = renderBus.LedStrips.Values.Max(x => x.LedCount);
            int totalLedCount = renderBus.LedStrips.Values.Sum(x => x.LedCount);
            Trace.TraceInformation($"[{busName}] 开始同步渲染，线程ID:{Thread.CurrentThread.ManagedThreadId}，灯带数量：{renderBus.LedStrips.Count}条，灯珠总数量：{totalLedCount}颗，最长灯带灯珠数量：{maxLedCount}颗");

            renderBus.IsRendering = true;
            Stopwatch stopwatch = new Stopwatch();
            int renderInterval = renderBus.Interval > 0 ? renderBus.Interval : 40;
            IEnumerable<LedStripObject> ledStrips = renderBus.LedStrips.Values.OrderBy(x => x.Port);

            var exceptionFrameCount = 0;

            while (renderBus._isRendering)
            {
                stopwatch.Restart();
                if (renderBus.Channel == null) break;
                if (!renderBus.Channel.IsConnected)
                {
                    Thread.Sleep(1000);
                    continue;
                }
                if (renderBus.LedStrips.Count == 0)
                {
                    Thread.Sleep(10);
                    continue;
                }

                // 对端口进行排序，以便串行通道的效率提升
                if (ledStrips.Count() != renderBus.LedStrips.Count)
                    ledStrips = renderBus.LedStrips.Values.OrderBy(x => x.Port);

                // 总线上的帧队列数据
                while (!renderBus._frameQueue.IsEmpty && renderBus._frameQueue.TryDequeue(out var frame))
                {
                    if (renderBus._isRendering == false || renderBus.Channel == null) break;

                    try
                    {
                        var message = renderBus.WriteFrame(frame);
                        if (!string.IsNullOrWhiteSpace(message))
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

                        if (renderBus.Timeout > 0) Thread.Sleep(renderBus.Timeout);
                    }
                    catch (Exception ex)
                    {
                        exceptionFrameCount++;
                        Trace.TraceWarning($"RenderBus {busName} Render Exception: {ex}");
                    }
                }
                
                // 各灯带上的帧队列数据
                foreach (var ledStrip in ledStrips)
                {
                    if (renderBus._isRendering == false || renderBus.Channel == null) break;
                    if (!ledStrip.TryGetFrame(out var frame)) continue;
                    
                    try
                    {
                        var message = renderBus.WriteFrame(frame);
                        if (!string.IsNullOrWhiteSpace(message))
                        {
                            message = message.Trim().Replace("\r\n", " ");
                            if (FrameExceptionMessages.ContainsKey(message))
                            {
                                exceptionFrameCount++;
                                ledStrip.ResetLastFrame();
                                renderBus.Channel.ClearReadBuffer();
                                Trace.TraceError($"RenderBus {busName}(Address:{ledStrip.Address} Port:{ledStrip.Port}) Device Respose Error Message({message}): {FrameExceptionMessages[message]} ");
                            }
                            else
                            {
                                exceptionFrameCount = 0;
                                //Debug.WriteLine($"RenderBus {busName}(Address:{ledStrip.Address} Port:{ledStrip.Port}) Device Respose Message: {message}");
                                //Trace.WriteLine($"RenderBus {busName}(Address:{ledStrip.Address} Port:{ledStrip.Port}) Device Respose Message: {message}");
                            }
                        }

                        if (ledStrip.Timeout > 0) Thread.Sleep(ledStrip.Timeout);
                    }
                    catch (Exception ex)
                    {
                        exceptionFrameCount ++;
                        Trace.TraceWarning($"RenderBus [{busName}] {ledStrip} Render Exception: {ex}");
                    }
                }

                if (exceptionFrameCount > 16)
                {
                    Trace.TraceWarning($"RenderBus [{busName}] 超出指定数量的异常帧断开连接通道。");

                    exceptionFrameCount = 0;
                    renderBus.Channel.Close();
                }

                Interlocked.Increment(ref renderBus._renderFps);

                var elapsed = stopwatch.ElapsedMilliseconds;
                var timeout = (int)(renderInterval - elapsed);
                if (timeout > 5) Thread.Sleep(timeout - 3);
            }

            foreach (var ledStrip in renderBus.LedStrips.Values)
            {
                ledStrip.Fps = 0;
            }

            stopwatch.Stop();
            renderBus.Fps = 0;
            renderBus.IsRendering = false;
            Trace.TraceInformation($"[{busName}] 已停止渲染，线程ID:{Thread.CurrentThread.ManagedThreadId}");
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            BusCollections.Remove(this);

            if (_isRendering)
            {
                ClearRender(0, true);
                while (!_frameQueue.IsEmpty)
                {
                    Thread.Sleep(1);
                }
            }

            StopRender();
            ClearLedStrips();

            Channel?.Dispose();
            Channel = null;

            _resposeStopwatch.Stop();
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            return $"[({nameof(LedRenderBus)}] Channel:{Channel.Name}  LedStripCount:{LedStrips.Count}";
        }

        /// <summary>
        /// 试图创建 <see cref="LedRenderBus"/> 对象的实例
        /// </summary>
        /// <param name="element"></param>
        /// <param name="ledRenderBus"></param>
        /// <param name="createLedStrips">是否也创建 <see cref="LedStripObject"/> 对象</param>
        /// <returns></returns>
        public static bool TryCreateInstance(XElement element, out LedRenderBus ledRenderBus, bool createLedStrips = true)
        {
            ledRenderBus = null;
            if (element == null || element.Name != nameof(LedRenderBus))
            {
                Trace.TraceWarning($"{nameof(LedRenderBus)} 配置节点不存在或名称不正确");
                return false;
            }
            if (!Enum.TryParse<TransportType>(element.Attribute(nameof(TransportChannel.Type))?.Value, true, out var type))
            {
                Trace.TraceWarning($"{nameof(LedRenderBus)} 配置节点中 {nameof(TransportChannel.Type)} 属性值不正确");
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

            ledRenderBus = new LedRenderBus(type, element.Attribute("Params").Value);
            ledRenderBus.Comment = element.Attribute(nameof(Comment))?.Value;
            ledRenderBus.Timeout = int.TryParse(element.Attribute(nameof(Timeout))?.Value, out int timeout) ? timeout : DefaultTimeout;
            ledRenderBus.Interval = int.TryParse(element.Attribute(nameof(Interval))?.Value, out var interval) ? interval : 40;

            ledRenderBus.ResposeTimeout = int.TryParse(element.Attribute(nameof(ResposeTimeout))?.Value, out var resposeTimeout) ? resposeTimeout : DefaultResposeTimeout;
            
            ledRenderBus.DefaultLedType = Enum.TryParse<LedType>(element.Attribute(nameof(DefaultLedType))?.Value, true, out var ledType) ? ledType : LedType.WS2812B;
            ledRenderBus.DefaultColorFormat = Enum.TryParse<ColorFormat>(element.Attribute(nameof(DefaultColorFormat))?.Value, true, out var colorFormat) ? colorFormat : ColorFormat.RGB;

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
