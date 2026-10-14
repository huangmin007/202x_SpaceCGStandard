using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Ports;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using NModbus;
using NModbus.Data;
using NModbus.Serial;
using SpaceCG.Extensions;
using SpaceCG.NModbus.Data;
using Trace = SpaceCG.Diagnostics.Trace;

namespace SpaceCG.NModbus
{
    /// <summary>物理链路类型：TCP（Modbus TCP）或 Serial（Modbus RTU 串口）。</summary>
    public enum TransportType : byte
    {
        /// <summary>Modbus TCP，连接参数为 "IP,端口"。</summary>
        Tcp,
        /// <summary>Modbus UDP，连接参数为 "IP,端口"。</summary>
        //Udp,
        /// <summary>Modbus RTU 串口，连接参数为 "端口名,波特率"。</summary>
        Serial,
    }

    /// <summary>
    /// Modbus 总线同步对象：一条链路（TCP / RTU）+ 多个从站设备 + 多个待同步的寄存器块。
    /// 生命周期：构造 → <see cref="AddRegisters"/> 注册块 → <see cref="StartSync"/> 启动同步线程 → <see cref="StopSync"/> / <see cref="Dispose"/> 结束。
    /// 职责：①维持链路连接并自动重连；②周期读取远端寄存器回写本地数据存储；③把外部投递的写入请求异步下发到设备。
    /// 线程安全：寄存器集合与写入队列为并发集合，AddRegisters / Write* 可从任意线程调用；StartSync / StopSync / Dispose 需串行调用。
    /// </summary>
    public class ModbusBusSynchronizer : IDisposable
    {
        /// <summary>全局 NModbus 工厂（含日志适配器），用于创建主站 / 从站 / 从站网络。</summary>
        public static readonly IModbusFactory Factory = new ModbusFactory(null, true, new ModbusLogger());

        /// <summary>设备标识，格式 "{链路类型}:{连接参数}"，用于日志。</summary>
        public string DisplayName => _displayName;
        public string Comment { get; set; } = string.Empty;

        /// <summary>连接参数第一段：TCP 为 IP / 主机名，串口为端口名。</summary>
        private readonly string _firstArgs;
        /// <summary>连接参数第二段：TCP 为端口，串口为波特率。</summary>
        private readonly int _secondArgs;

        private readonly string _displayName;
        private readonly TransportType _transportType;

        /// <summary>本地数据存储，读到的远端数据回写到这里。</summary>
        private readonly ISlaveDataStore _localDataStore;
        /// <summary>待下发的线圈写入请求队列。</summary>
        private readonly ConcurrentQueue<RegisterPoint<bool>> _writeCoilQueue;
        /// <summary>待下发的保持寄存器写入请求队列。</summary>
        private readonly ConcurrentQueue<RegisterPoint<ushort>> _writeHoldingQueue;
        /// <summary>待同步寄存器块，键为 (PointType, SlaveAddress, StartAddress) 打包的 32 位标识。</summary>
        private readonly ConcurrentDictionary<uint, RegisterPoint> _readRegistersQueue;

        private volatile bool _isConnected = false;
        /// <summary> 同步器是否是连接状态。 </summary>
        public bool IsConnected => _isConnected;

        /// <summary> 使用接口类型写入寄存器值，默认为 true。 </summary>
        public bool UseInterfaceWritePoints { get; set; } = true;

        /// <summary> 采样频率/秒 </summary>
        public int SampleRate { get; private set; } = 0;
        /// <summary>本地数据存储，读到的远端数据回写到这里。</summary>
        public ISlaveDataStore LocalDataStore => _localDataStore;
        /// <summary>已注册的待同步寄存器块。</summary>
        public IEnumerable<RegisterPoint> SyncRegisters => _readRegistersQueue.Values;

        /// <summary>TCP 客户端，重连时会被替换。</summary>
        private TcpClient _tcpClient;
        /// <summary>串口，仅 Serial 链路使用。</summary>
        private SerialPort _serialPort;
        /// <summary>Modbus 主站，随客户端 / 串口重建而替换。</summary>
        private IModbusMaster _modbusMaster;
        /// <summary>上次尝试重连的时刻，用于 3 秒重连节流。</summary>
        private int _lastReconnectTimestamp;

        private bool _disposed;
        /// <summary>同步线程任务；未启动时为 null。</summary>
        private Task _syncTask;
        /// <summary>同步线程取消令牌源；StopSync 后为 null。</summary>
        private CancellationTokenSource _syncCts;
        /// <summary>构造时捕获的同步上下文，用于把变更事件 Post 回创建线程。</summary>
        private SynchronizationContext _syncContext;

        /// <summary>离散输入值变化时触发。</summary>
        public event EventHandler<RegisterPointChangedEventArgs<bool>> CoilInputsValueChanged;
        /// <summary>线圈值变化时触发。</summary>
        public event EventHandler<RegisterPointChangedEventArgs<bool>> CoilDiscretesValueChanged;
        /// <summary>输入寄存器值变化时触发。</summary>
        public event EventHandler<RegisterPointChangedEventArgs<ushort>> InputRegistersValueChanged;
        /// <summary>保持寄存器值变化时触发。</summary>
        public event EventHandler<RegisterPointChangedEventArgs<ushort>> HoldingRegistersValueChanged;

        /// <summary>构造同步器实例，并按链路类型创建对应的 Modbus 主站（不建立实际连接）。</summary>
        /// <remarks>首次连接在同步线程中由 <see cref="TryEnsureConnected"/> 完成。</remarks>
        /// <param name="type">链路类型。</param>
        /// <param name="arguments">连接参数，格式 "第一段,第二段"。</param>
        /// <param name="localDataStore">数据存储实例，读取到的数据回写至此。</param>
        /// <exception cref="ArgumentNullException">arguments 或 slaveDataStore 为空。</exception>
        /// <exception cref="ArgumentException">参数段数不为 2，或第二段无法解析为 int。</exception>
        /// <exception cref="NotSupportedException">链路类型未支持。</exception>
        public ModbusBusSynchronizer(TransportType type, string arguments, ISlaveDataStore localDataStore = null)
        {
            if (string.IsNullOrWhiteSpace(arguments)) throw new ArgumentNullException(nameof(arguments));

            var argArray = arguments.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            if (argArray.Length != 2) throw new ArgumentException(nameof(arguments));
            if (!int.TryParse(argArray[1], out var secondValue)) throw new ArgumentException(nameof(arguments));

            _transportType = type;
            _firstArgs = argArray[0].Trim();
            _secondArgs = secondValue;
            _displayName = $"{type}:{arguments}";

            _syncContext = SynchronizationContext.Current;
            _localDataStore = localDataStore ?? new LocalDataStore(256);

            _writeCoilQueue = new ConcurrentQueue<RegisterPoint<bool>>();
            _writeHoldingQueue = new ConcurrentQueue<RegisterPoint<ushort>>();
            _readRegistersQueue = new ConcurrentDictionary<uint, RegisterPoint>();

            if (type == TransportType.Tcp)
            {
                _tcpClient = new TcpClient();
                _tcpClient.SendTimeout = 2000;
                _tcpClient.ReceiveTimeout = 2000;

                _modbusMaster = Factory.CreateMaster(_tcpClient);
                _modbusMaster.Transport.Retries = 0;
                _modbusMaster.Transport.RetryOnOldResponseThreshold = 8;
            }
            else if (type == TransportType.Serial)
            {
                _serialPort = new SerialPort(SerialPortExtensions.GetPortName(_firstArgs), _secondArgs);
                _serialPort.ReadTimeout = 2000;
                _serialPort.WriteTimeout = 2000;
                _modbusMaster = Factory.CreateRtuMaster(_serialPort);
            }
            else
            {
                throw new NotSupportedException();
            }
        }

        /// <summary>注册一个待同步的寄存器块；相同标识的块会被忽略。</summary>
        /// <remarks>标识 = (PointType &lt;&lt; 24) | (SlaveAddress &lt;&lt; 16) | StartAddress。</remarks>
        /// <param name="registers">寄存器块配置。</param>
        public void AddRegisters(RegisterPoint registers)
        {
            if (registers == null) throw new ArgumentNullException(nameof(registers));
            if (_readRegistersQueue.TryAdd(registers.Key, registers))
            {
                Trace.TraceInformation($"{this} 添加寄存器块：{registers}");
            }
        }

        #region Start & Stop Sync
        /// <summary>启动同步线程（每设备一个长任务）；已启动时直接返回。</summary>
        public void StartSync()
        {
            if (_syncCts != null || _syncTask != null) return;

            _syncCts = new CancellationTokenSource();
            _syncTask = Task.Factory.StartNew(SyncLoop, this, _syncCts.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }
        /// <summary>停止同步线程并释放取消令牌。</summary>
        public void StopSync()
        {
            try { _syncCts?.Cancel(); }
            catch { }

            try
            {
                _syncTask?.Wait(300);
                _syncTask?.Dispose();
            }
            finally
            {
                _syncTask = null;
            }

            try { _syncCts?.Dispose(); }
            finally { _syncCts = null; }
        }
        #endregion

        #region 特殊写入数据存储，不能产生事件
        /// <summary>
        /// 使用 Public WritePoints 方法，不能产生 Write 事件。
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="pointSource"></param>
        /// <param name="localAddress"></param>
        /// <param name="values"></param>
        private static void WritePoints<T>(IPointSource<T> pointSource, ushort localAddress, T[] values) where T : struct
        {
            if (pointSource is PointSource<T> pSource)
            {
                pSource.WritePoints(localAddress, values);
            }
            else if (pointSource is RegistersPointSource<T> rSource)
            {
                rSource.WritePoints(localAddress, values);
            }
            else
            {
                pointSource.WritePoints(localAddress, values);
            }
        }
        #endregion

        /// <summary>
        /// 把读回的值写入本地存储；无订阅者时直接写，有订阅者时比较新旧值并在变化时触发事件。
        /// </summary>
        /// <typeparam name="T">点值类型：bool 对应线圈 / 离散量，ushort 对应寄存器。</typeparam>
        /// <param name="eventHandler">变更事件；为 null 时跳过比较。</param>
        /// <param name="pointSource">目标点源。</param>
        /// <param name="registers">寄存器块配置。</param>
        /// <param name="newValues">本次读回的新值。</param>
        private void UpdateAndDispatcherChanged<T>(EventHandler<RegisterPointChangedEventArgs<T>> eventHandler, IPointSource<T> pointSource, RegisterPoint registers, T[] newValues) where T : struct
        {
            if (eventHandler == null)
            {
                if (UseInterfaceWritePoints)
                    pointSource.WritePoints(registers.LocalStartAddress, newValues);
                else
                    WritePoints(pointSource, registers.LocalStartAddress, newValues);
                return;
            }

            var oldValues = pointSource.ReadPoints(registers.LocalStartAddress, registers.NumberOfPoints);
            if (UseInterfaceWritePoints)
                pointSource.WritePoints(registers.LocalStartAddress, newValues);
            else
                WritePoints(pointSource, registers.LocalStartAddress, newValues);

            var nLength = newValues.Length;
            var oLength = oldValues.Length;
            if (nLength == oLength)
            {
                var isSame = true;
                var comparer = EqualityComparer<T>.Default;
                for (int i = 0; i < nLength; i++)
                {
                    if (!comparer.Equals(newValues[i], oldValues[i])) { isSame = false; break; }
                }
                if (isSame) return;
            }

            var eventArgs = new RegisterPointChangedEventArgs<T>(registers, newValues, oldValues);
            var context = _syncContext;
            if (context == null)
            {
                eventHandler.Invoke(this, eventArgs);
                return;
            }

            context.Post(state => eventHandler.Invoke(this, state as RegisterPointChangedEventArgs<T>), eventArgs);
        }
        /// <summary>
        /// 主动关闭连接
        /// </summary>
        private void CloseConnection()
        {
            if (_transportType == TransportType.Tcp)
            {
                try { _modbusMaster?.Dispose(); }
                finally { _modbusMaster = null; }
            }

            try { _tcpClient?.Dispose(); }
            finally { _tcpClient = null; }

            try { _serialPort?.Close(); }
            catch { }

            _isConnected = false;

            // 链路已不可用：按既定约束丢弃未下发的写命令，严禁重连后补发"迟到"动作
            while (_writeCoilQueue.TryDequeue(out var _)) { }
            while (_writeHoldingQueue.TryDequeue(out var _)) { }
        }
        /// <summary>
        /// 确保链路可用：已连接返回 true；未连接则按 3 秒节流尝试重连一次（阻塞调用）。
        /// </summary>
        /// <returns>链路可用返回 true；处于重连冷却期或重连失败返回 false。</returns>
        private bool TryEnsureConnected()
        {
            const int ReconnectionInterval = 3000; // 重连间隔 3 秒

            #region 连接检查
            if (_transportType == TransportType.Tcp)
            {
                var isConnected = _tcpClient?.IsConnected() ?? false;
                _isConnected = isConnected;
                if (isConnected) return true;

                // 未连接且距上次重连超过 3 秒才重试，避免断线时每个轮询周期都阻塞一次连接超时
                if (Environment.TickCount - _lastReconnectTimestamp < ReconnectionInterval) return false;
                _lastReconnectTimestamp = Environment.TickCount;

                CloseConnection();

                try
                {
                    var newTcpClient = new TcpClient();
                    newTcpClient.SendTimeout = 2000;
                    newTcpClient.ReceiveTimeout = 2000;
                    newTcpClient.Connect(_firstArgs, _secondArgs);
                    if (!newTcpClient.IsConnected())
                    {
                        newTcpClient.Dispose();
                        return false;
                    }

                    _tcpClient = newTcpClient;
                    _modbusMaster = Factory.CreateMaster(_tcpClient);
                    _modbusMaster.Transport.Retries = 0;
                    _modbusMaster.Transport.ReadTimeout = 2000;
                    _modbusMaster.Transport.WriteTimeout = 2000;
                    _modbusMaster.Transport.RetryOnOldResponseThreshold = 8; //允许"响应比请求落后的最大帧数"，超过则该响应被丢弃并再读一帧。

                    _isConnected = true;
                    Trace.TraceInformation($"{this} 连接 {_firstArgs}:{_secondArgs} 成功");
                    return true;
                }
                catch (Exception ex)
                {
                    _isConnected = false;
                    Trace.TraceWarning($"{this} 连接失败：{ex.Message}");
                    return false;
                }
            }
            else if (_transportType == TransportType.Serial)
            {
                if (_serialPort == null)
                {
                    _isConnected = false;
                    return false;
                }
                if (_serialPort.IsOpen)
                {
                    _isConnected = true;
                    return true;
                }

                if (Environment.TickCount - _lastReconnectTimestamp < ReconnectionInterval) return false;
                _lastReconnectTimestamp = Environment.TickCount;

                // 串口与主站在构造时创建，此处只重新配置并打开，不重建 RTU 主站
                try
                {
                    _serialPort.Close();
                    _serialPort.BaudRate = _secondArgs;
                    _serialPort.PortName = SerialPortExtensions.GetPortName(_firstArgs);

                    _serialPort.Open();
                    _isConnected = true;
                    return true;
                }
                catch (Exception ex)
                {
                    _isConnected = false;
                    Trace.TraceWarning($"{this} 连接失败：{ex.Message}");
                    return false;
                }
            }
            #endregion

            _isConnected = false;
            return false;
        }

        /// <summary>
        /// 设备同步线程主体（每设备一个）：先下发写入队列中的请求，再轮询读取已注册的寄存器块并回写本地数据存储，直到收到取消信号。
        /// </summary>
        /// <param name="state">线程启动参数，实际类型为 <see cref="ModbusBusSynchronizer"/>。</param>
        private static void SyncLoop(object state)
        {
            var busSync = (state as ModbusBusSynchronizer);
            if (busSync == null) return;

            var labelName = $"{busSync}";
            var localStore = busSync._localDataStore;
            var cancellationToken = busSync._syncCts.Token;
            Trace.TraceInformation($"{labelName} 启动同步线程({Thread.CurrentThread.ManagedThreadId})");

            var sampleRate = 0;
            var exceptionCount = 0;
            var stopwatch = Stopwatch.StartNew();

            while (!cancellationToken.IsCancellationRequested)
            {
                var elapsedMilliseconds = stopwatch.ElapsedMilliseconds;
                if (elapsedMilliseconds >= 1000)
                {
                    busSync.SampleRate = sampleRate;
                    //Trace.WriteLine($"{labelName} > {sampleRate}/({elapsedMilliseconds})");
                    sampleRate = 0;
                    stopwatch.Restart();
                }

                if (!busSync.TryEnsureConnected())
                {
                    Thread.Sleep(1);
                    continue;
                }

                sampleRate++;
                if (exceptionCount > 3)
                {
                    exceptionCount = 0;
                    busSync.CloseConnection();
                    continue;
                }

                var master = busSync._modbusMaster;

                #region Write Bool Queue
                var writeBQueue = busSync._writeCoilQueue;
                while (!writeBQueue.IsEmpty && !cancellationToken.IsCancellationRequested)
                {
                    if (!writeBQueue.TryDequeue(out var regB))
                    {
                        Thread.Sleep(1);
                        continue;
                    }

                    try
                    {
                        if (regB.PointType == RegisterType.CoilDiscretes)
                        {
                            master.WriteMultipleCoils(regB.SlaveAddress, regB.StartAddress, regB.Values);
                            exceptionCount = 0;
                        }
                    }
                    catch (Exception ex)
                    {
                        exceptionCount++;
                        Trace.TraceWarning($"{labelName} 写寄存器 {regB.PointType}/{regB.StartAddress} 异常：({ex.GetType()}){ex.Message}");
                    }
                }
                #endregion

                #region Write Ushort Queue
                var writeUQueue = busSync._writeHoldingQueue;
                while (!writeUQueue.IsEmpty && !cancellationToken.IsCancellationRequested)
                {
                    if (!writeUQueue.TryDequeue(out var regU))
                    {
                        Thread.Sleep(1);
                        continue;
                    }

                    try
                    {
                        if (regU.PointType == RegisterType.HoldingRegisters)
                        {
                            master.WriteMultipleRegisters(regU.SlaveAddress, regU.StartAddress, regU.Values);
                            exceptionCount = 0;
                        }
                    }
                    catch (Exception ex)
                    {
                        exceptionCount++;
                        Trace.TraceWarning($"{labelName} 写寄存器 {regU.PointType}/{regU.StartAddress} 异常：({ex.GetType()}){ex.Message}");
                        continue;
                    }
                }
                #endregion

                #region Read Registers Queue
                var tickCount = Environment.TickCount;
                var readRCount = busSync._readRegistersQueue.Count;
                foreach (var kv in busSync._readRegistersQueue)
                {
                    if (cancellationToken.IsCancellationRequested) break;

                    var registers = kv.Value;
                    // 如果上次异常时间在2秒内，则跳过
                    if (registers.LastExceptionTime > 0 && tickCount - registers.LastExceptionTime < 2000) continue;

                    try
                    {
                        switch (registers.PointType)
                        {
                            case RegisterType.HoldingRegisters:
                                var holdingValues = master.ReadHoldingRegisters(registers.SlaveAddress, registers.StartAddress, registers.NumberOfPoints);
                                busSync.UpdateAndDispatcherChanged(busSync.HoldingRegistersValueChanged, localStore.HoldingRegisters, registers, holdingValues);
                                break;

                            case RegisterType.InputRegisters:
                                var inputUValues = master.ReadInputRegisters(registers.SlaveAddress, registers.StartAddress, registers.NumberOfPoints);
                                busSync.UpdateAndDispatcherChanged(busSync.InputRegistersValueChanged, localStore.InputRegisters, registers, inputUValues);
                                break;

                            case RegisterType.CoilDiscretes:
                                var coilValues = master.ReadCoils(registers.SlaveAddress, registers.StartAddress, registers.NumberOfPoints);
                                busSync.UpdateAndDispatcherChanged(busSync.CoilDiscretesValueChanged, localStore.CoilDiscretes, registers, coilValues);
                                break;

                            case RegisterType.CoilInputs:
                                var inputValues = master.ReadInputs(registers.SlaveAddress, registers.StartAddress, registers.NumberOfPoints);
                                busSync.UpdateAndDispatcherChanged(busSync.CoilInputsValueChanged, localStore.CoilInputs, registers, inputValues);
                                break;
                        }
                        exceptionCount = 0;
                        registers.LastExceptionTime = 0;
                    }
                    catch (IOException ex)
                    {
                        exceptionCount++;
                        registers.LastExceptionTime = Environment.TickCount;
                        Trace.TraceError($"{labelName}({registers}) 同步寄存器异常：(IOException){ex.Message}");
                        Thread.Sleep(10);
                        break;
                    }
                    catch (Exception ex)
                    {
                        exceptionCount++;
                        registers.LastExceptionTime = Environment.TickCount;
                        Trace.TraceWarning($"{labelName}({registers}) 同步寄存器 {registers.PointType}/{registers.StartAddress} 异常：({ex.GetType()}){ex.Message}");
                        continue;
                    }
                    if (registers.Sleep > 0) Thread.Sleep(registers.Sleep);
                }
                #endregion

                //Thread.Sleep(1);
                if (sampleRate % 2 == 0) Thread.Sleep(1);
            }

            Trace.TraceInformation($"{labelName} 退出同步线程({Thread.CurrentThread.ManagedThreadId})");
        }

        #region Read Registers
        /// <summary>
        /// 读取多个保持寄存器（FC03）到本地缓存。
        /// </summary>
        /// <param name="startAddress"></param>
        /// <param name="numberOfPoints"></param>
        /// <returns></returns>
        public ushort[] ReadHoldingRegisters(ushort startAddress, ushort numberOfPoints)
        {
            var lastValues = _localDataStore.HoldingRegisters.ReadPoints(startAddress, numberOfPoints);
            return lastValues;
        }
        #endregion

        #region Write Registers
        /// <summary>写单个保持寄存器（FC16）到写入队列。</summary>
        /// <param name="slaveAddress">目标从站地址（UnitId）。</param>
        /// <param name="registerAddress">目标寄存器地址。</param>
        /// <param name="value">待写入值。</param>
        public void WriteSingleRegister(byte slaveAddress, ushort registerAddress, ushort value)
        {
            if (!IsConnected)
            {
                Trace.TraceWarning($"{this} 连接异常，无法写寄存器 {slaveAddress}/{registerAddress}");
                return;
            }
            _writeHoldingQueue.Enqueue(new RegisterPoint<ushort>(slaveAddress, RegisterType.HoldingRegisters, registerAddress, new ushort[] { value }, 0));
        }
        /// <summary>写多个保持寄存器（FC16）到写入队列。</summary>
        /// <param name="slaveAddress">目标从站地址（UnitId）。</param>
        /// <param name="startAddress">目标起始地址。</param>
        /// <param name="data">待写入值，顺序与连续地址一一对应。</param>
        public void WriteMultipleRegisters(byte slaveAddress, ushort startAddress, ushort[] data)
        {
            if (!IsConnected)
            {
                Trace.TraceWarning($"{this} 连接异常，无法写寄存器 {slaveAddress}/{startAddress}");
                return;
            }

            Trace.TraceInformation($"{this} > WriteMultipleRegisters SlaveAddress:{slaveAddress} StartAddress:{startAddress} NumberOfPoints:{data.Length}");
            _writeHoldingQueue.Enqueue(new RegisterPoint<ushort>(slaveAddress, RegisterType.HoldingRegisters, startAddress, data, 0));
        }
        /// <summary>写单个线圈（FC15）到写入队列。</summary>
        /// <param name="slaveAddress">目标从站地址（UnitId）。</param>
        /// <param name="coilAddress">目标线圈地址。</param>
        /// <param name="value">待写入值。</param>
        public void WriteSingleCoil(byte slaveAddress, ushort coilAddress, bool value)
        {
            if (!IsConnected)
            {
                Trace.TraceWarning($"{this} 连接异常，无法写寄存器 {slaveAddress}/{coilAddress}");
                return;
            }

            Trace.TraceInformation($"{this} > WriteSingleCoil SlaveAddress:{slaveAddress} CoilAddress:{coilAddress}");
            _writeCoilQueue.Enqueue(new RegisterPoint<bool>(slaveAddress, RegisterType.CoilDiscretes, coilAddress, new bool[] { value }, 0));
        }
        /// <summary>写多个线圈（FC15）到写入队列。</summary>
        /// <param name="slaveAddress">目标从站地址（UnitId）。</param>
        /// <param name="startAddress">目标起始地址。</param>
        /// <param name="data">待写入值，顺序与连续地址一一对应。</param>
        public void WriteMultipleCoils(byte slaveAddress, ushort startAddress, bool[] data)
        {
            if (!IsConnected)
            {
                Trace.TraceWarning($"{this} 连接异常，无法写寄存器 {slaveAddress}/{startAddress}");
                return;
            }

            Trace.TraceInformation($"{this} > WriteMultipleCoils SlaveAddress:{slaveAddress} StartAddress:{startAddress}");
            _writeCoilQueue.Enqueue(new RegisterPoint<bool>(slaveAddress, RegisterType.CoilDiscretes, startAddress, data, 0));
        }
        #endregion

        /// <summary>停止同步线程，并释放主站与传输层资源（客户端/串口）。</summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _isConnected = false;

            StopSync();

            _writeCoilQueue.Clear();
            _writeHoldingQueue.Clear();
            _readRegistersQueue.Clear();

            try { _modbusMaster?.Dispose(); }
            finally { _modbusMaster = null; }

            try { _tcpClient?.Dispose(); }
            finally { _tcpClient = null; }

            try { _serialPort?.Dispose(); }
            finally { _serialPort = null; }
        }

        /// <inheritdoc /> 
        public override string ToString()
        {
            return $"[Modbus Synchronizer({(!string.IsNullOrWhiteSpace(Comment) ? Comment : _displayName)})]";
        }

    }
}
