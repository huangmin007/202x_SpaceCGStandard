using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NModbus;
using NModbus.Data;
using SpaceCG.Extensions;
using SpaceCG.NModbus.Data;
using Trace = SpaceCG.Diagnostics.Trace;

namespace SpaceCG.NModbus
{
    public class ModbusSynchronizer
    {
        public string Comment { get; set; } = string.Empty;
        /// <summary>设备标识，格式 "{链路类型}:{连接参数}"，用于日志。</summary>
        public string DisplayName { get; set; } = string.Empty;
        
        /// <summary>Modbus 主站，随客户端 / 串口重建而替换。</summary>
        private IModbusMaster _modbusMaster;
        /// <summary>本地数据存储，读到的远端数据回写到这里。</summary>
        private readonly ISlaveDataStore _localDataStore;

        /// <summary>待下发的线圈写入请求队列。</summary>
        private readonly ConcurrentQueue<RegisterPoint<bool>> _writeCoilQueue;
        /// <summary>待下发的保持寄存器写入请求队列。</summary>
        private readonly ConcurrentQueue<RegisterPoint<ushort>> _writeHoldingQueue;
        /// <summary>待同步寄存器块，键为 (PointType, SlaveAddress, StartAddress) 打包的 32 位标识。</summary>
        private readonly ConcurrentDictionary<uint, RegisterPoint> _readRegistersQueue;

        //private readonly ConcurrentDictionary<uint, RegisterPoint> _readRegisterBlockQueue;

        /// <summary>离散输入值变化时触发。</summary>
        public event EventHandler<RegisterPointChangedEventArgs<bool>> CoilInputsValueChanged;
        /// <summary>线圈值变化时触发。</summary>
        public event EventHandler<RegisterPointChangedEventArgs<bool>> CoilDiscretesValueChanged;
        /// <summary>输入寄存器值变化时触发。</summary>
        public event EventHandler<RegisterPointChangedEventArgs<ushort>> InputRegistersValueChanged;
        /// <summary>保持寄存器值变化时触发。</summary>
        public event EventHandler<RegisterPointChangedEventArgs<ushort>> HoldingRegistersValueChanged;

        /// <summary> 使用接口类型写入寄存器值。 </summary>
        public bool UseInterfaceWritePoints { get; set; } = false;

        /// <summary> 采样频率/秒 </summary>
        public int SampleRate { get; private set; } = 0;
        /// <summary>本地数据存储，读到的远端数据回写到这里。</summary>
        public ISlaveDataStore LocalDataStore => _localDataStore;
        /// <summary>已注册的待同步寄存器块。</summary>
        public IEnumerable<RegisterPoint> SyncRegisters => _readRegistersQueue.Values;

        private bool _disposed;
        /// <summary>同步线程任务；未启动时为 null。</summary>
        private Task _syncTask;
        /// <summary>同步线程取消令牌源；StopSync 后为 null。</summary>
        private CancellationTokenSource _syncCts;
        /// <summary>构造时捕获的同步上下文，用于把变更事件 Post 回创建线程。</summary>
        private SynchronizationContext _syncContext;

        public ModbusSynchronizer(IModbusMaster modbusMaster, ISlaveDataStore localDataStore = null)
        {
            _modbusMaster = modbusMaster;
            _localDataStore = localDataStore;

            _syncContext = SynchronizationContext.Current;
            _localDataStore = localDataStore ?? new LocalDataStore(256);

            _writeCoilQueue = new ConcurrentQueue<RegisterPoint<bool>>();
            _writeHoldingQueue = new ConcurrentQueue<RegisterPoint<ushort>>();
            _readRegistersQueue = new ConcurrentDictionary<uint, RegisterPoint>();
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
        /// 设备同步线程主体（每设备一个）：先下发写入队列中的请求，再轮询读取已注册的寄存器块并回写本地数据存储，直到收到取消信号。
        /// </summary>
        /// <param name="state">线程启动参数，实际类型为 <see cref="ModbusBusSynchronizer"/>。</param>
        private static void SyncLoop(object state)
        {
            var busSync = (state as ModbusSynchronizer);
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

                sampleRate++;
                if (exceptionCount > 3)
                {
                    exceptionCount = 0;
                    busSync._writeCoilQueue.Clear();
                    busSync._writeHoldingQueue.Clear();
                    Thread.Sleep(100);
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
            if (_syncTask == null || _syncTask.IsCanceled || _syncTask.IsCompleted)
            {
                throw new InvalidOperationException("同步线程未启动，请先启动同步线程。");
            }

            _writeHoldingQueue.Enqueue(new RegisterPoint<ushort>(slaveAddress, RegisterType.HoldingRegisters, registerAddress, new ushort[] { value }, 0));
        }
        /// <summary>写多个保持寄存器（FC16）到写入队列。</summary>
        /// <param name="slaveAddress">目标从站地址（UnitId）。</param>
        /// <param name="startAddress">目标起始地址。</param>
        /// <param name="data">待写入值，顺序与连续地址一一对应。</param>
        public void WriteMultipleRegisters(byte slaveAddress, ushort startAddress, ushort[] data)
        {
            if (_syncTask == null || _syncTask.IsCanceled || _syncTask.IsCompleted)
            {
                throw new InvalidOperationException("同步线程未启动，请先启动同步线程。");
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
            if (_syncTask == null || _syncTask.IsCanceled || _syncTask.IsCompleted)
            {
                throw new InvalidOperationException("同步线程未启动，请先启动同步线程。");
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
            if (_syncTask == null || _syncTask.IsCanceled || _syncTask.IsCompleted)
            {
                throw new InvalidOperationException("同步线程未启动，请先启动同步线程。");
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

            StopSync();

            _writeCoilQueue.Clear();
            _writeHoldingQueue.Clear();
            _readRegistersQueue.Clear();

            try { _modbusMaster?.Dispose(); }
            finally { _modbusMaster = null; }
        }

        /// <inheritdoc /> 
        public override string ToString()
        {
            return $"[Modbus Synchronizer({(!string.IsNullOrWhiteSpace(Comment) ? Comment : DisplayName)})]";
        }
    }
}
