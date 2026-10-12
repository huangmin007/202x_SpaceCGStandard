# SpaceCG.Standard.Devices

`SpaceCG.Standard.Devices` 项目提供**设备 I/O 传输通道抽象**与**串口扩展方法**，是设备体系（LED 渲染器、RFID、传感器、控制器等）的底层通信底座。

- **目标框架**：.NET Framework 4.8（C# 7.3）
- **设计原则**：传输通道与具体设备解耦、同步/异步收发统一契约、可替换的传输方式（串口 / TCP / UDP）

> 说明：本项目聚焦「设备如何传输数据」，不包含设备枚举、热插拔监听等平台能力；那些能力已迁出至独立的 Windows 平台库。

---

## 命名空间与模块

| 命名空间 | 文件 | 职责 |
|:---|:---|:---|
| `SpaceCG.IO` | `IO/ITransportChannel.cs` | 同步传输通道抽象接口 + 通道类型枚举 |
| `SpaceCG.IO` | `IO/IRequestResponseClient.cs` | 请求-响应通信契约 + 响应帧判定委托 |
| `SpaceCG.IO` | `IO/SerialPortTransport.cs` | 串口传输实现 |
| `SpaceCG.IO` | `IO/TcpClientTransport.cs` | TCP 客户端传输实现 |
| `SpaceCG.IO` | `IO/UdpClientTransport.cs` | UDP 客户端传输实现 |
| `SpaceCG.Extensions` | `Extensions/SerialPortExtensions.cs` | 串口扩展方法（`ReceiveParseAsync`/`Transceive`/`TransceiveAsync`） |
| `SpaceCG.Extensions` | `Extensions/SerialPortExtensions.GetPortName.cs` | 串口号解析（`GetPortName`/`GetPortFriendlyNames`） |

---

## 类与接口列表

| 类型 | 类型 | 命名空间 | 说明 |
|:---|:---|:---|:---|
| [`ITransportChannel`](#itransportchannel) | interface | `SpaceCG.IO` | 同步传输通道抽象，与底层 I/O 无关的数据读写契约 |
| [`ChannelType`](#itransportchannel) | enum | `SpaceCG.IO` | 通道类型：`SERIAL` / `TCP` / `UDP` |
| [`IRequestResponseClient`](#irequestresponseclient) | interface | `SpaceCG.IO` | 请求-响应通信契约 |
| [`ResponseFramePredicate`](#irequestresponseclient) | delegate | `SpaceCG.IO` | 响应帧完整性判定委托 |
| [`SerialPortTransport`](#传输实现类) | sealed class | `SpaceCG.IO` | 串口传输实现 |
| [`TcpClientTransport`](#传输实现类) | sealed class | `SpaceCG.IO` | TCP 客户端传输实现 |
| [`UdpClientTransport`](#传输实现类) | sealed class | `SpaceCG.IO` | UDP 客户端传输实现 |
| [`SerialPortExtensions`](#serialportextensions) | static class | `SpaceCG.Extensions` | 串口扩展方法 |

---

## `ITransportChannel`

同步传输通道抽象接口，定义与底层 I/O 通道无关的数据读写契约。实现类支持串口（`SerialPort`）、TCP（`TcpClient`）、UDP（`UdpClient`）多种传输方式。

### 成员

| 成员 | 类型 | 说明 |
|:---|:---|:---|
| `Type` | `ChannelType` | 通道类型（串口 / TCP / UDP） |
| `Name` | `string` | 通道标识名称，如 `"SERIAL_COM3_115200"`、`"TCP_192.168.1.100_8080"` |
| `IsConnected` | `bool` | 当前是否处于连接状态 |
| `Available` | `int` | 接收缓冲区中可立即读取的字节数 |
| `ReadTimeout` | `int` | 读取超时（毫秒） |
| `WriteTimeout` | `int` | 写入超时（毫秒） |
| `Open()` | `void` | 打开传输连接 |
| `Close()` | `void` | 关闭连接并释放底层资源，可再次 `Open()` 重连 |
| `ClearReadBuffer()` | `void` | 丢弃接收缓冲区中的所有数据 |
| `ClearWriteBuffer()` | `void` | 丢弃发送缓冲区中的所有数据 |
| `Read(byte[], int, int)` | `int` | 同步读取，返回实际读取字节数（连接关闭返回 0） |
| `Write(byte[], int, int)` | `void` | 同步写入 |

> **线程安全**：此接口不保证线程安全，调用方需自行同步对同一实例的读写操作。

---

## `IRequestResponseClient`

表示支持「请求-响应」通信模式的客户端对象：一次完整通信 = 发送一条请求 + 等待一条对应响应。响应边界由固定长度或 `ResponseFramePredicate` 判定器确定。

### 成员

| 成员 | 类型 | 说明 |
|:---|:---|:---|
| `ResponseTimeout` | `int` | 单次请求-响应的等待超时，超时抛 `TimeoutException` |
| `Transceive(byte[], ResponseFramePredicate)` | `byte[]` | 发送请求，按判定器等待完整响应 |
| `Transceive(byte[], int, int, ResponseFramePredicate)` | `byte[]` | 发送请求指定区段，按判定器等待响应 |
| `Transceive(byte[], int)` | `byte[]` | 发送请求，等待固定长度响应 |
| `Transceive(byte[], int, int, int)` | `byte[]` | 发送请求指定区段，等待固定长度响应 |
| `TransceiveAsync(byte[], ResponseFramePredicate, CancellationToken)` | `Task<byte[]>` | 异步版（判定器边界） |
| `TransceiveAsync(byte[], int, CancellationToken)` | `Task<byte[]>` | 异步版（固定长度） |

### `ResponseFramePredicate` 委托

```csharp
public delegate int ResponseFramePredicate(ArraySegment<byte> buffer);
```

用于在接收缓冲中判断一条响应是否已完整接收：返回完整响应的字节长度（`> 0`），数据尚未完整时返回 `-1`。该方法在每次收到新数据后重复调用，应保持轻量、无副作用。

---

## 传输实现类

三个实现类均实现 `ITransportChannel`，且都提供**两种构造方式**：强类型参数构造 + `params string[]` 字符串参数构造（便于从配置文件/命令行解析）。

| 类 | 通道类型 | 构造参数（强类型） | 构造参数（字符串数组） |
|:---|:---|:---|:---|
| `SerialPortTransport` | `SERIAL` | `portName, baudRate, parity, dataBits, stopBits` | `"COM3", "115200", "None", "8", "One"` |
| `TcpClientTransport` | `TCP` | `hostname, port` | `"192.168.1.100", "8080"` |
| `UdpClientTransport` | `UDP` | `hostname, port` | `"192.168.1.100", "8080"` |

### 使用示例

```csharp
using SpaceCG.IO;
using System;

// 1. 串口传输
using (var channel = new SerialPortTransport("CH340", 115200))
{
    channel.Open();
    Console.WriteLine($"{channel.Name} 连接状态: {channel.IsConnected}");

    var data = new byte[] { 0x01, 0x03, 0x00, 0x00, 0x00, 0x01 };
    channel.Write(data, 0, data.Length);

    var buffer = new byte[256];
    int read = channel.Read(buffer, 0, buffer.Length);
}

// 2. TCP 传输
using (var tcp = new TcpClientTransport("192.168.1.100", 8080))
{
    tcp.Open();
    // ... 读写
}

// 3. UDP 传输
using (var udp = new UdpClientTransport("192.168.1.100", 8080))
{
    udp.Open();
    // ... 读写
}

// 4. 字符串参数构造（便于从配置解析）
using (var channel = new SerialPortTransport("COM3", "115200", "None", "8", "One"))
{
    channel.Open();
}
```

### 设计特性

- **统一契约**：三种传输方式通过 `ITransportChannel` 抽象，业务层无需关心底层是串口、TCP 还是 UDP
- **懒连接**：构造函数仅保存参数并初始化底层对象，`Open()` 才真正建立连接
- **可重连**：`Close()` 后可再次 `Open()`；TCP/UDP 在 `Open()` 时会重建底层套接字
- **缓冲区可调**：各实现均预分配了较大的收发缓冲区（如串口 `ReadBufferSize = 32 KB`、`WriteBufferSize = 128 KB`），适配大数据帧场景
- **串口智能解析**：`SerialPortTransport` 构造时若传入的是友好名称（如 `"CH340"`），会通过 `SerialPortExtensions.GetPortName` 自动解析为实际串口号

---

## `SerialPortExtensions`

串口扩展方法（`SpaceCG.Extensions` 命名空间）。跨两个文件组织：`SerialPortExtensions.GetPortName.cs` 承载串口号解析，`SerialPortExtensions.cs` 承载收发方法。

### 公共方法

| 方法 | 返回类型 | 说明 |
|:---|:---|:---|
| `GetPortName(string)` | `string` | 根据模式匹配查找串口号（如 `"CH340"` → `"COM3"`） |
| `GetPortFriendlyNames()` | `IEnumerable<string>` | 枚举所有串口设备的友好名称 |
| `ReceiveParseAsync(SerialPort, ProtocolParser, CancellationToken)` | `Task` | 串口异步接收解析循环 |
| `Transceive(SerialPort, byte[], int, int, int)` | `byte[]` | 同步发送并阻塞等待指定长度响应 |
| `Transceive(SerialPort, byte[], int)` | `byte[]` | 同步发送全部数据并等待指定长度响应 |
| `TransceiveAsync(SerialPort, byte[], int, int, int, CancellationToken)` | `Task<byte[]>` | 异步发送并等待指定长度响应 |
| `TransceiveAsync(SerialPort, byte[], int, CancellationToken)` | `Task<byte[]>` | 异步发送全部数据并等待指定长度响应 |

### 串口号解析

`GetPortName` 匹配规则：

1. 若 `searchPattern` 本身已是 `"COM"` + 数字格式（如 `"COM3"`、`"COM14"`），直接返回，无需枚举设备。
2. 否则枚举所有串口设备的友好名称，做**不区分大小写的包含匹配**，命中后从友好名称中提取串口号（如 `"USB-SERIAL CH340 (COM3)"` → `"COM3"`）。
3. 未匹配到或输入为空白时返回 `string.Empty`。

> **注意**：`GetPortFriendlyNames` 返回的是设备**友好名称**（如 `"USB-SERIAL CH340 (COM3)"`），而非串口号；需要串口号请使用 `GetPortName`。

### 使用示例

```csharp
using System.IO.Ports;
using System.Threading;
using System.Threading.Tasks;
using SpaceCG.Extensions;

// 1. 根据友好名称查找串口号
string portName = SerialPortExtensions.GetPortName("CH340");   // → "COM3"
string direct   = SerialPortExtensions.GetPortName("COM14");   // → "COM14"（原样返回）

// 2. 同步收发：发送命令，等待固定长度响应
var port = new SerialPort(portName, 115200);
port.Open();
byte[] request = { 0x01, 0x03, 0x00, 0x00, 0x00, 0x01 };
byte[] response = port.Transceive(request, 6);   // 阻塞等待 6 字节响应

// 3. 异步收发
var cts = new CancellationTokenSource();
byte[] asyncResponse = await port.TransceiveAsync(request, 6, cts.Token);
```

### 设计特性

- **线程安全**：`SerialPortExtensions` 为静态工具类，不维护可变状态，可在多线程环境中并发调用
- **同步/异步双版本**：`Transceive` 与 `TransceiveAsync` 语义一致，分别适配阻塞式与 `async/await` 场景
- **.NET Framework 串口异步兼容**：串口底层在 .NET Framework 4.8 下不支持真正的异步 I/O，`TransceiveAsync` 通过 `BytesToRead` 预检 + 异步轮询实现，避免 `ReadAsync` 在无数据时永久阻塞
- **超时控制**：收发超时由 `SerialPort.ReadTimeout` 决定（默认回退 300ms），超时抛 `TimeoutException`
- **缓冲区清理**：收发前自动 `DiscardInBuffer`，确保响应对应当前命令，避免残留数据干扰

---

## 项目结构

```
SpaceCG.Standard.Devices/
├── SpaceCG.Standard.Devices.csproj     # 项目文件（.NET Framework 4.8, AnyCPU）
├── README.md
├── Properties/
│   └── AssemblyInfo.cs
├── IO/
│   ├── ITransportChannel.cs            # 传输通道抽象接口 + ChannelType 枚举
│   ├── IRequestResponseClient.cs       # 请求-响应契约 + ResponseFramePredicate 委托
│   ├── SerialPortTransport.cs          # 串口传输实现
│   ├── TcpClientTransport.cs           # TCP 客户端传输实现
│   └── UdpClientTransport.cs           # UDP 客户端传输实现
└── Extensions/
    ├── SerialPortExtensions.cs         # 串口收发扩展方法
    └── SerialPortExtensions.GetPortName.cs  # 串口号解析扩展方法
```

---

## 依赖

| 依赖 | 说明 |
|:---|:---|
| `SpaceCG.Standard` | 基础库，提供 `Extensions`、`Generic`（`ProtocolParser`）、`Diagnostics.Trace` 等 |

---

## 已知问题与注意事项

| # | 问题 | 说明 |
|:---:|:---|:---|
| 1 | **`ITransportChannel` 不保证线程安全** | 同一实例的并发读写需调用方自行同步，接口未内置锁 |
| 2 | **UDP 无真正连接语义** | `UdpClientTransport` 的 `ClearReadBuffer`/`ClearWriteBuffer` 为空实现，UDP 无缓冲可清 |
| 3 | **串口异步非原生** | .NET Framework 4.8 下串口无真正异步 I/O，`TransceiveAsync` 采用轮询实现，高频调用下存在轻微 CPU 开销 |
| 4 | **异常使用 `Exception` 基类** | 部分传输实现（如串口 `Open()`）抛出 `Exception` 基类，未使用特定异常类型，后续可优化为自定义 `TransportException` |
| 5 | **`GetPortName` 返回空而非原始值** | 未匹配时返回 `string.Empty`（而非原始 `searchPattern`），调用方需判断空串再决定是否使用 |
