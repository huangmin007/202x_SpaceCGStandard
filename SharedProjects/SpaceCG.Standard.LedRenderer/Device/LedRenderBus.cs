using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
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
    /// Led 渲染总线对象，管理传输通道 + 灯带集合 + 渲染线程 + 帧调度
    /// </summary>
    public sealed partial class LedRenderBus : FrameRenderModel, IDisposable
    {
        /// <summary>
        /// 默认数据帧发送后等待的时间，单位：毫秒
        /// </summary>
        internal const int DefaultTimeout = 10;
        /// <summary>
        /// 默认设备响应超时时间，单位：毫秒
        /// </summary>
        internal const int DefaultResponseTimeout = 300;

        #region Public Property
        /// <summary>
        /// 设备响应超时时间，单位：毫秒；默认 300 ms
        /// </summary>
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

        /// <summary>
        /// 渲染总线(线程)是否处于渲染状态
        /// </summary>
        public bool IsRendering { get; private set; }
        /// <summary>
        /// 渲染线程循环频率（循环次数/秒），区别于 <see cref="Fps"/>（实际渲染帧率）。
        /// </summary>
        /// <remarks>
        /// 当队列为空或灯带暂停时，循环仍在运行但 FPS 为 0，此时 RenderLoopFps 反映线程活跃度。
        /// </remarks>
        public int LoopFps { get; private set; } = 0;

        /// <summary> Led 渲染总线 Id  </summary>
        public int BusId { get; private set; } = 0;
        /// <summary>  登记在总线上所有灯带的灯珠总数量  </summary>
        public int TotalLedCount { get; private set; } = 0;
        /// <summary>
        /// 总线上所有 Led 设备的地址集合(非重复的设备地址集合)
        /// </summary>
        public IEnumerable<ushort> LedDevices { get; private set; }
        #endregion

        #region TransportChannel
        /// <summary>
        /// 传输通道类型
        /// </summary>
        public ChannelType Type => Channel.Type;
        /// <summary>
        /// 传输通道对象
        /// </summary>
        private ITransportChannel Channel { get; set; }        
        /// <summary>
        /// 传输通道名称
        /// </summary>
        public string Name => Channel != null ? Channel.Name : string.Empty;
        /// <summary>
        /// 传输通道是否处于连接状态
        /// </summary>
        public bool IsConnected => Channel != null && Channel.IsConnected;
        #endregion
        
        private Task _renderingTask;
        private CancellationTokenSource _cts;

        private readonly byte[] _responseBuffer = new byte[1024];
        private readonly Stopwatch _responseStopwatch = new Stopwatch();

        /// <summary> 渲染总线关联的 <see cref="LedStripObject"/> 对象的集合  </summary>
        public IReadOnlyDictionary<uint, LedStripObject> LedStrips => _ledStrips;
        private readonly ConcurrentDictionary<uint, LedStripObject> _ledStrips = new ConcurrentDictionary<uint, LedStripObject>(2, 7);

        #region Static & Collections 静态集合
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
        private static IReadOnlyList<LedRenderBus> BusCollectionsReadOnly;
        private static readonly List<LedRenderBus> BusCollections = new List<LedRenderBus>(32);

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
        #endregion

        /// <summary>
        /// 初始化 Led 渲染总线实例
        /// </summary>
        /// <param name="channelType">传输通道类型</param>
        /// <param name="channelParams">传输通道参数，多个参数以逗号分隔</param>
        /// <param name="defaultLedType">默认没有登记的/统一灯珠类型</param>
        /// <param name="defaultColorFormat">默认没有登记的/统一灯珠颜色</param>
        public LedRenderBus(ChannelType channelType, string channelParams, LedType defaultLedType = LedType.WS2812B, ColorFormat defaultColorFormat = ColorFormat.GRB) : base(0,0, defaultLedType, defaultColorFormat)
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
            Timeout = DefaultTimeout;
            RenderingRepeatInterval = 0;
        }

        #region 辅助方法
        private void OnPointsChanged(object sender, EventArgs args)
        {
            LedCount = _ledStrips.Values.Max(x => x.LedCount);
            TotalLedCount = _ledStrips.Values.Sum(x => x.LedCount);
            LedDevices = LedStrips.Values.Select(x => x.Address).Distinct().ToArray();
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private FrameRenderModel GetRenderModel(ushort address)
        {
            if (address == 0) return this;
            return LedStrips.Values.FirstOrDefault(x => x.Address == address);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private FrameRenderModel GetRenderModel(ushort address, byte port)
        {
            if (address == 0 || port == 0) return this;
            return LedStrips.Values.FirstOrDefault(x => x.Address == address && x.Port == port);
        }
        #endregion

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
        /// 清空总线的所有灯带
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
        #endregion

        #region Channel / Render Contrl 渲染控制
        /// <summary> 打开总线传输通道  </summary>
        public void OpenChannel() => Channel.Open();
        /// <summary> 关闭总线传输通道  </summary>
        public void CloseChannel() => Channel.Close();
        
        /// <summary>
        /// 启动渲染线程
        /// </summary>
        public void StartRender()
        {
            if (IsRendering) return;
            if (_cts != null || _renderingTask != null) return;

            _cts = new CancellationTokenSource();
            _renderingTask = Task.Factory.StartNew(RenderingBusThread, this, _cts.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }        
        /// <summary>
        /// 停止渲染线程
        /// </summary>
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
        /// 暂停渲染指定的灯带（可用于使用其它方式插入渲染数据，例如：传感器数据、外部的交互数据等影响而临时插入的渲染数据）<br/>
        /// <para>暂停了使用 <see cref="RenderBitmap(Bitmap)"/> 和 <see cref="RenderPixels(byte*,int,int,int,ColorFormat)"/> 函数渲染的灯带，其它函数仍然可以渲染数据</para>
        /// </summary>
        /// <param name="address">设备地址(所有端口号)，0 表示所有设备</param>
        public void PauseRender(ushort address)
        {
            FrameRenderModel renderModel = GetRenderModel(address);
            if (renderModel == null)
                throw new ArgumentException($"设备地址(所有端口号)：{address} 不存在");

            renderModel.IsRenderEnabled = false;

            if (address == 0)
            {
                foreach (var ledStrip in LedStrips.Values)
                    ledStrip.IsRenderEnabled = false;
            }
        }
        /// <summary>
        /// 恢复渲染指定的灯带（可用于使用其它方式插入渲染数据，例如：传感器数据、外部的交互数据等影响而临时插入的渲染数据）<br/>
        /// <para>恢复了使用 <see cref="RenderBitmap(Bitmap)"/> 和 <see cref="RenderPixels(byte*,int,int,int,ColorFormat)"/> 函数渲染的灯带</para>
        /// </summary>
        /// <param name="address">设备地址(所有端口号)，0 表示所有设备</param>
        public void ResumeRender(ushort address)
        {
            FrameRenderModel renderModel = GetRenderModel(address);
            if (renderModel == null)
                throw new ArgumentException($"设备地址(所有端口号)：{address} 不存在");

            renderModel.IsRenderEnabled = true;

            if (address == 0)
            {
                foreach (var ledStrip in LedStrips.Values)
                    ledStrip.IsRenderEnabled = true;
            }
        }
        /// <summary>
        /// 清空总线中的指定灯带的待渲染数据
        /// </summary>
        /// <param name="address">设备地址(所有端口号)，0 表示所有设备</param>
        /// <param name="clear">是否关闭灯带</param>
        public void ClearRender(ushort address, bool clear)
        {
            FrameRenderModel renderModel = GetRenderModel(address);
            if (renderModel == null)
                throw new ArgumentException($"设备地址(所有端口号)：{address} 不存在");

            renderModel.ClearRenderingFrames();

            if (address == 0)
            {
                foreach (var ledStrip in LedStrips.Values)
                {
                    ledStrip.ClearRenderingFrames();
                }
            }

            if (clear)
            {
                renderModel.AddColorFrame(0x00000000, 0, renderModel.LedCount, ColorFormat.ARGB);
            }
        }
        #endregion

        #region RenderBitmap/Pixels
        /// <summary>
        /// 渲染 <see cref="Bitmap"/> 数据到总线的所有灯带中。二维渲染，参考 <see cref="LedStripObject.LedPoints"/> 集合的顺序及坐标数据。
        /// <para><see cref="LedStripObject"/> 对象会跟据灯珠的坐标位置在 <paramref name="bitmap"/> 上取数据进行渲染</para>
        /// </summary>
        /// <param name="bitmap"></param>
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
        /// 渲染像素数据到总线的所有灯带中。二维渲染，参考 <see cref="LedStripObject.LedPoints"/> 集合的顺序及坐标数据。
        /// <para><see cref="LedStripObject"/> 对象会跟据自身参数取 <paramref name="pixels"/> 的部份数据进行渲染</para>
        /// </summary>
        /// <param name="pixels">图像的像素数据的指针</param>
        /// <param name="width">图像的宽度</param>
        /// <param name="height">图像的高度</param>
        /// <param name="stride">图像的扫描宽度</param>
        /// <param name="pixelFormat">图像的颜色格式</param>
        /// <exception cref="ArgumentException"></exception>
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

        #region 设置上电显示颜色/设备波特率/设备数据处理超时时间
        /// <summary>
        /// 创建一个固定长度为 21 字节的数据帧。
        /// </summary>
        /// <param name="address"></param>
        /// <param name="group"></param>
        /// <param name="funCode"></param>
        /// <param name="value"></param>
        /// <returns></returns>
        private static byte[] CreateEmptyFrame(ushort address, ushort group, byte funCode, int value)
        {
            if (address > 4096) throw new ArgumentOutOfRangeException(nameof(address), "地址不能大于 4096");
            if (group > 1024) throw new ArgumentOutOfRangeException(nameof(group), "组地址不能大于 1024");

            byte[] frame = new byte[21];

            frame[0] = 0xDD;
            frame[1] = 0x55;
            frame[2] = 0xEE;

            frame[3] = (byte)(group >> 8);          // 组地址
            frame[4] = (byte)(group & 0xFF);

            frame[5] = (byte)(address >> 8);        // 设备地址
            frame[6] = (byte)(address & 0xFF);

            frame[7] = 0x00;                        // 端口号

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
        /// 设置总线上设备的波特率；<b>注意：请谨慎操作，修改设备波特率后，设备需要重新上电后生效</b>。
        /// <para>设备波特率只支持：9600、115200、230400、460800、921600，其它波特率设备不支持</para>
        /// </summary>
        /// <param name="address"></param>
        /// <param name="baudRate"></param>
        public void SetDeviceBaudRate(ushort address, ushort group = 0, int baudRate = 921600)
        {
            if (baudRate != 9600 && baudRate != 115200 && baudRate != 230400 && baudRate != 460800 && baudRate != 921600)
                throw new ArgumentException("设备波特率只支持：9600、115200、230400、460800、921600，其它波特率设备不支持");

            EnqueueFrame(CreateEmptyFrame(address, group, 0x95, baudRate));
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
        public void SetDeviceTimeout(ushort address, ushort group = 0, ushort timeout = 5)
        {
            if (timeout < 5 || timeout > 1000) throw new ArgumentException("超时时间范围必须在 5-1000 之间");

            EnqueueFrame(CreateEmptyFrame(address, group, 0x8E, timeout));
        }
        /// <summary>
        /// 设置上电显示颜色
        /// </summary>
        /// <param name="address"></param>
        /// <param name="port"></param>
        /// <param name="color"></param>
        /// <param name="isShow"></param>
        /// <param name="colorFormat"></param>
        /// <exception cref="InvalidOperationException"></exception>
        /// <exception cref="ArgumentException"></exception>
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

            renderModel.EnqueueFrame(frame);
        }
        #endregion

        #region AddColorFrame
        /// <summary>
        /// 添加待渲染的颜色数据帧
        /// </summary>
        /// <param name="address"></param>
        /// <param name="port"></param>
        /// <param name="r"></param>
        /// <param name="g"></param>
        /// <param name="b"></param>
        /// <param name="start"></param>
        /// <param name="repeat"></param>
        public void AddColorFrame(ushort address, byte port, byte r, byte g, byte b, int start, int repeat) => AddColorFrame(address, port, (uint)(0xFF << 24 | r << 16 | g << 8 | b), start, repeat, ColorFormat.ARGB);
        /// <summary>
        /// 添加待渲染的颜色数据帧
        /// </summary>
        /// <param name="address"></param>
        /// <param name="port"></param>
        /// <param name="color"></param>
        /// <param name="start"></param>
        /// <param name="repeat"></param>
        /// <param name="colorFormat"></param>
        /// <exception cref="ArgumentException"></exception>
        public void AddColorFrame(ushort address, byte port, uint color, int start, int repeat, ColorFormat colorFormat = ColorFormat.ARGB)
        {
            FrameRenderModel renderModel = GetRenderModel(address, port);
            if (renderModel == null)
                throw new ArgumentException($"未找到地址为 {address} 端口为 {port} 的设备", nameof(address));

            renderModel.AddColorFrame(color, start, repeat, colorFormat);
        }
        
        /// <inheritdoc cref="AddColorFrame(ushort, byte, IReadOnlyList{uint}, int, int, ColorFormat)"/> 
        public void AddColorFrame(ushort address, byte port, IReadOnlyList<byte> colors, int start, int repeat, ColorFormat colorFormat = ColorFormat.RGB)
        {
            FrameRenderModel renderModel = GetRenderModel(address, port);
            if (renderModel == null)
                throw new ArgumentException($"未找到地址为 {address} 端口为 {port} 的设备", nameof(address));

            renderModel.AddColorFrame(colors, start, repeat, colorFormat);
        }
        /// <summary>
        /// 添加待渲染的颜色数据帧。
        /// </summary>
        /// <param name="address"></param>
        /// <param name="port"></param>
        /// <param name="colors"></param>
        /// <param name="start">点亮灯珠 IC 的起始位置。值范围：[0, <see cref="LedCount"/>]。</param>
        /// <param name="repeat"></param>
        /// <param name="colorFormat"></param>
        /// <exception cref="ArgumentException"></exception>
        public void AddColorFrame(ushort address, byte port, IReadOnlyList<uint> colors, int start, int repeat, ColorFormat colorFormat = ColorFormat.ARGB)
        {
            FrameRenderModel renderModel = GetRenderModel(address, port);

            if (renderModel == null)
                throw new ArgumentException($"未找到地址为 {address} 端口为 {port} 的设备", nameof(address));

            renderModel.AddColorFrame(colors, start, repeat, colorFormat);
        }
        #endregion

        /// <summary>
        /// 将数据帧立即写入连接通道
        /// <para>按 921600 波特率发送数据，一个字节(10bit)需要 10.85us，发送 1024 字节需要 1024 * 10.85us ≈ 11.6ms </para>
        /// <para>点亮一颗灯珠的时间为 30us，要点亮 1024 颗灯珠需要 1024 * 30us + 50us(复位信号) ≈ 30.77ms </para>
        /// </summary>
        /// <param name="frame"></param>
        /// <returns>返回设备响应消息，无响应或组/广播地址返回空字符串</returns>
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

            var count = 0;
            var message = string.Empty;
            var responseTimeout = ResponseTimeout;
            _responseStopwatch.Restart();

            // 0x99 显示颜色数据
            // 0x98 从指定的 IC 显示颜色数据
            if (funCode == 0x98 || funCode == 0x99)
            {
                //RecvEnd DisplayEnd
                while (!message.Contains(DisplayEnd))
                {
                    if (_responseStopwatch.ElapsedMilliseconds > responseTimeout)
                    {
                        message = nameof(ResponseTimeout);
                        break;
                    }

                    if (Channel.Available <= 0)
                    {
                        Thread.Sleep(1);
                        //Thread.Sleep(0);
                        //Thread.Yield();
                        continue;
                    }

                    var bytesRead = Channel.Read(_responseBuffer, count, Channel.Available);
                    count += bytesRead;
                    message = Encoding.UTF8.GetString(_responseBuffer, 0, count);
                }
            }
            else if (funCode == 0x9B || funCode == 0x9C)
            {
                //RecvEnd SaveInsEnd/SaveInsErr 
                while (!message.Contains(SaveInsEnd) && !message.Contains(SaveInsErr))
                {
                    if (_responseStopwatch.ElapsedMilliseconds > responseTimeout)
                    {
                        message = nameof(ResponseTimeout);
                        break;
                    }

                    if (Channel.Available <= 0)
                    {
                        Thread.Sleep(1);
                        continue;
                    }

                    var bytesRead = Channel.Read(_responseBuffer, count, Channel.Available);
                    count += bytesRead;
                    message = Encoding.UTF8.GetString(_responseBuffer, 0, count);
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
                while (!renderBus.Channel.IsConnected)
                {
                    if (cancellationToken.IsCancellationRequested) break;
                    Thread.Sleep(2000);

                    try
                    {
                        renderBus.Channel.Close();
                        Thread.Sleep(500);

                        renderBus.Channel.Open();
                    }
                    catch (Exception ex)
                    {
                        Trace.TraceWarning($"渲染通道 {busName} 连接异常，{ex.Message}");
                    }

                    Thread.Sleep(500);
                    continue;
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
                var isRenderEnabled = renderBus.IsRenderEnabled;

                #region 总线上的帧队列数据
                while (renderBus.TryDequeueFrame(out var frame))
                {
                    if (cancellationToken.IsCancellationRequested || renderBus.Channel == null) break;
                    if (!isRenderEnabled) continue;

                    try
                    {
                        writeFrameCount++;
                        hasWrittenFrame = true;
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
                #endregion

                #region 各灯带上的帧队列数据
                foreach (var ledStrip in ledStrips)
                {
                    if (cancellationToken.IsCancellationRequested || renderBus.Channel == null) break;
                    if (!ledStrip.TryDequeueFrame(out var frame)) continue;
                    if (!ledStrip.IsRenderEnabled) continue;

                    try
                    {
                        writeFrameCount++;
                        hasWrittenFrame = true;
                        var message = renderBus.WriteFrame(frame);

                        if (!string.IsNullOrWhiteSpace(message))
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

                        if (ledStrip.Timeout > 0) Thread.Sleep(ledStrip.Timeout);
                    }
                    catch (Exception ex)
                    {
                        exceptionFrameCount ++;
                        Trace.TraceWarning($"RenderBus [{busName}] {ledStrip} Render Exception: {ex}");
                    }
                }
                #endregion

                // 计算帧频
                var elapsed = stopwatch.ElapsedMilliseconds;
                if (elapsed >= 1000)
                {
                    foreach (var ledStrip in ledStrips)
                    {
                        ledStrip.ResetRenderingState();
                    }

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

        /// <inheritdoc/>
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
                while (PendingFrameCount > 0)
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
            ledRenderBus.Timeout = int.TryParse(element.Attribute(nameof(Timeout))?.Value, out int timeout) ? timeout : DefaultTimeout;
            ledRenderBus.ResponseTimeout = int.TryParse(element.Attribute(nameof(ResponseTimeout))?.Value, out var resposeTimeout) ? resposeTimeout : DefaultResponseTimeout;

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
