# SpaceCG.Standard.NModbus — NModbus 扩展与总线同步器

> 说明：本项目 **不是** NModbus 上游源码的 fork。`NModbus/` 文件夹只是扩展代码的目录名，全部代码位于 `SpaceCG.NModbus*` 命名空间，依赖 NuGet 包 `NModbus 3.0.83` 的公开 API。
> 历史沿革：项目曾命名为 `SpaceCG.Standard.Devices.NModbus`，后更名为 `SpaceCG.Standard.NModbus`。

## 项目信息

| 项 | 值 |
|---|---|
| 目标框架 | .NET Framework 4.8（C# 7.3） |
| 第三方依赖 | NuGet `NModbus 3.0.83` |
| 项目引用 | `SpaceCG.Standard` |
| 共享项目导入 | `SP_SpaceCG.Standard.Trace`（日志）、`SP_SpaceCG.Interop.SerialPortResolver`（串口号解析） |
| 命名空间 | `SpaceCG.NModbus` / `SpaceCG.NModbus.Data` / `SpaceCG.NModbus.IO` / `SpaceCG.NModbus.Extensions`（RootNamespace 为 `SpaceCG`） |

## 目录结构

```
SpaceCG.Standard.NModbus/
└── NModbus/
    ├── ModbusLogger.cs               # IModbusLogger → Trace 日志桥接
    ├── ModbusBusSynchronizer.cs      # 总线同步器（核心，≈30KB）
    ├── ModbusSynchronizer.cs         # 单通道同步器
    ├── RegisterPointChangedEventArgs.cs
    ├── Data/
    │   ├── LocalDataStore.cs         # ISlaveDataStore 本地数据区实现
    │   ├── RegisterPoint.cs          # 同步点描述（区/从站/地址/数量）
    │   └── RegistersPointSource.cs   # IPointSource<T> 点源实现
    ├── Extensions/
    │   ├── ModbusFactoryExtensions.cs    # SerialPort → RTU/ASCII Master/SlaveNetwork 工厂扩展
    │   └── SerialPortExtensions.cs       # 串口创建、收发（Transceive/TransceiveAsync）、接收解析
    └── IO/
        ├── SerialPortAdapter.cs      # SerialPort → IStreamResource 适配
        └── TcpClientAdapterEx.cs     # TcpClient → IStreamResource 适配
```

## 核心类型

### 同步器（ModbusBusSynchronizer / ModbusSynchronizer）

按 `RegisterPoint` 配置周期性轮询远端从站寄存器区，同步到本地数据区，并在值变化时触发事件。两者 API 对称：

| 成员 | 说明 |
|---|---|
| `void AddRegisters(RegisterPoint registers)` | 添加同步点（重复/重叠源会拒绝） |
| `void StartSync()` / `void StopSync()` | 启动/停止同步线程 |
| `ushort[] ReadHoldingRegisters(ushort startAddress, ushort numberOfPoints)` | 读本地保持寄存器数据区 |
| `WriteSingleRegister` / `WriteMultipleRegisters` | 写远端从站保持寄存器（下行命令） |
| `WriteSingleCoil` / `WriteMultipleCoils` | 写远端从站线圈 |
| `Dispose()` | 停止同步并释放资源 |

### 数据区（Data/）

| 类型 | 说明 |
|---|---|
| `RegisterPoint` | 同步点描述：`SlaveAddress` + `PointType`（CoilInputs/CoilDiscretes/InputRegisters/HoldingRegisters）+ `StartAddress` + `NumberOfPoints` + `LocalStartAddress`；提供 `IsSameSource`/`IsOverlap`/`FindOverlaps`（O(n²) 重叠校验）/`TryParse(XElement)`（XML 配置解析） |
| `RegistersPointSource<T>` | `sealed class : IPointSource<T>`，容量上限 `Capacity`；事件 `BeforeRead` / `BeforeWrite` / `AfterWrite`（`PointEventArgs<T>` 携带 `Points` 数组） |
| `LocalDataStore` | `sealed class : ISlaveDataStore`，聚合四个 `RegistersPointSource<T>`：`CoilInputs` / `CoilDiscretes` / `InputRegisters` / `HoldingRegisters` |
| `RegisterType` | 枚举：`CoilInputs / CoilDiscretes / InputRegisters / HoldingRegisters` |
| `RegisterPointChangedEventArgs<T>` | 值变化事件参数：`RegisterPoint` + `NewValue` + `OldValue` |

### IO 适配与扩展

| 类型/方法 | 说明 |
|---|---|
| `SerialPortAdapter` / `TcpClientAdapterEx` | 将 `SerialPort` / `TcpClient` 适配为 NModbus 的 `IStreamResource`（含 `DiscardInBuffer`） |
| `ModbusFactoryExtensions` | `CreateRtuMaster` / `CreateRtuTransport` / `CreateRtuSlaveNetwork` / `CreateAsciiMaster`（基于 `SerialPort`） |
| `SerialPortExtensions.CreateInstance` | 从 `string` / `IEnumerable<string>` / 字典 / `XElement` 配置创建 `SerialPort`（配置格式如 `"CH343,921600,8,N,1"`，串口号经 `SerialPortResolver.GetPortName` 解析） |
| `SerialPortExtensions.Transceive / TransceiveAsync` | 单次请求-响应收发（发送字节 → 等待指定长度响应），配合 `CancellationToken` |
| `SerialPortExtensions.ReceiveParseAsync` | 持续接收并喂给 `ProtocolParser`（`SpaceCG.Generic`）做协议切帧 |
| `ModbusLogger` | `IModbusLogger` 实现，Debug 构建全量输出，Release 仅 Information 及以上 |

## 关键调用约定（使用时必须遵守）

1. **事件订阅与本地存储写入解耦**：`ModbusBusSynchronizer.UpdateAndDispatcherChanged` 无论是否有订阅者都会先把新值写入本地数据区，仅在值变化时触发事件——订阅变化事件不会阻断本地数据更新
2. **公有 ReadPoints/WritePoints 不触发事件**：`RegistersPointSource<T>` 的公有重载不触发 `Before/After` 事件，只有 NModbus 框架经 `IPointSource<T>` 显式接口实现的调用才触发——设备侧回写走公有重载可避免自激回环
3. **写事件在 NModbus 网络线程触发**：`BeforeWrite/AfterWrite` 回调里只应入队，禁止同步等待 UI 线程或阻塞网络线程
4. **硬拒绝写入**：在 `BeforeWrite` 中抛 `InvalidModbusRequestException(SlaveExceptionCodes.IllegalFunction)` 会转为 0x01 异常响应帧；抛其它异常一律变成 0x04 SlaveDeviceFailure

## 使用示例

```csharp
var factory = new ModbusFactory();
var serialPort = SerialPortExtensions.CreateInstance("CH343,921600");
var master = factory.CreateRtuMaster(serialPort);

var dataStore = new LocalDataStore();
var sync = new ModbusBusSynchronizer(/* 传输通道等 */);
sync.AddRegisters(new RegisterPoint(1, RegisterType.HoldingRegisters, 0, 16, 0));
sync.RegisterPointChanged += (s, e) => { /* e.RegisterPoint / e.NewValue / e.OldValue */ };
sync.StartSync();
```

## 已知问题 / 待办

1. `ModbusBusSynchronizer` 的轮询与写命令队列的优先级调度尚无文档化的延迟保证；实时性要求高的场景需自行压测
2. 缺少单元测试（Modbus 模拟从站压测、同步点重叠校验、断线重连）
3. 未提供 Modbus TCP 从站网络（`ModbusTcpSlaveServer`）的高层封装

---
最后更新：2026-11-12
