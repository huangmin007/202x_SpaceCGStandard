# SpaceCG.Standard

SpaceCG 跨项目复用的 **.NET 基础库**：日志跟踪、通用数据结构、校验算法、协议解析、扩展方法集与轻量级网络通信框架。

| 项 | 值 |
|---|---|
| 目标框架 | `netstandard2.0`（可被 .NET Framework 4.8 / .NET Core / Unity3D 引用） |
| 语言版本 | C# 7.3 |
| 根命名空间 | `SpaceCG` |
| 第三方依赖 | **零**（仅使用 .NET Standard 2.0 自带 API） |
| 版本 | `1.0.3`（`GeneratePackageOnBuild`，构建即产出 NuGet 包） |
| 其它开关 | `AllowUnsafeBlocks`、`GenerateDocumentationFile` |

---

## 目录

- [1. 项目定位](#1-项目定位)
- [2. 快速开始](#2-快速开始)
- [3. 项目结构](#3-项目结构)
- [4. 模块一览](#4-模块一览)
- [5. 核心能力速查](#5-核心能力速查)
  - [5.1 Diagnostics：日志](#51-diagnostics日志)
  - [5.2 Generic：通用组件](#52-generic通用组件)
  - [5.3 Extensions：扩展方法集](#53-extensions扩展方法集)
  - [5.4 Net：网络通信](#54-net网络通信)
- [6. 设计约定](#6-设计约定)
- [7. 构建与打包](#7-构建与打包)
- [8. 已知问题与建议](#8-已知问题与建议)
- [9. 相关项目](#9-相关项目)
- [10. 文档索引](#10-文档索引)

---

## 1. 项目定位

**做什么**：为上层业务项目（WPF 上位机、设备服务、Unity3D 客户端等）提供稳定、无依赖的基础设施，
避免每个项目重复实现日志、字节流解析、校验、对象池、配置加载、RPC 等能力。

**不做什么**：不涉及具体业务领域（设备协议、渲染、UI 控件），这些由兄弟项目承担（见 [第 9 节](#9-相关项目)）。

**核心特征**：

- **零第三方依赖**：不引入任何 NuGet 包，可安全被任意项目引用，不会产生依赖冲突。
- **跨平台**：`netstandard2.0` + 运行时环境检测，同一份 DLL 在 .NET Framework、.NET Core、Unity3D 下均可用。
- **性能优先**：热路径零分配或池化（`ArrayPool` / `ObjectPool<T>` / 环形缓冲 / 零拷贝 `ArraySegment`）。
- **完整 XML 文档**：`GenerateDocumentationFile=True`，所有公有 API 均有 `///` 注释，IDE 智能提示可直接使用。

---

## 2. 快速开始

### 引用方式

```xml
<!-- 方式一：项目引用（推荐，本仓库内） -->
<ProjectReference Include="..\SpaceCG.Standard\SpaceCG.Standard.csproj" />

<!-- 方式二：本地 NuGet 源（bin/Debug|Release/*.nupkg） -->
<PackageReference Include="SpaceCG.Standard" Version="1.0.3" />
```

### 最小示例

```csharp
using SpaceCG.Diagnostics;   // Trace / LoggerTraceListener
using SpaceCG.Generic;       // CRC / 校验和 / 对象池 / 环形缓冲 / 协议解析

// 1. 日志：非 Unity 环境转发到 System.Diagnostics.Trace，Unity 下自动走 Debug.Log
Trace.TraceInformation("服务已启动");

// 2. CRC：预设协议一行搞定
byte[] frame = { 0x01, 0x03, 0x00, 0x00, 0x00, 0x02 };
ushort crc = frame.ComputeCRC16_MODBUS();      // Modbus CRC16

// 3. 对象池：复用短生命周期对象，减少 GC 压力
var pool = new ObjectPool<System.Text.StringBuilder>(initialCount: 16, maxCount: 64);
var sb = pool.Rent();
sb.Clear();
sb.Append("hello");
pool.Return(sb);
```

### 接入文件日志（可选）

```csharp
using System.Diagnostics;
using SpaceCG.Diagnostics;

var listener = new LoggerTraceListener($"logs/{DateTime.Now:yyyy-MM-dd}.log", "MyLogger");
listener.FileSizeLimit = 1024 * 1024;   // 1 MB 轮转
listener.FileBackupDays = 7;            // 备份保留 7 天
listener.AutoFlushInterval = 1;         // 每条立即 Flush
Trace.Listeners.Add(listener);

Trace.TraceInformation("服务已启动");   // 异步写文件 + 控制台彩色输出
```

---

## 3. 项目结构

```
SpaceCG.Standard/
├── SpaceCG.Standard.csproj          # netstandard2.0 / RootNamespace=SpaceCG / Version=1.0.3
├── Diagnostics/                     # 日志跟踪（命名空间 SpaceCG.Diagnostics）
│   ├── Trace.cs                     #   跨平台日志静态方法（Unity 自动适配）
│   └── LoggerTraceListener.cs       #   异步日志侦听器 + TraceMessage
├── Generic/                         # 通用组件（命名空间 SpaceCG.Generic）
│   ├── ChecksumHelper.cs            #   Sum / XOR / BCC / LRC 校验和
│   ├── CircularBuffer.cs            #   环形缓冲区（双端增删、零拷贝分段）
│   ├── CRCCheckHelper.cs            #   CRC8/16/32/64（Rocksoft 通用模型）
│   ├── ObjectPool.cs                #   泛型对象池
│   ├── ProtocolParser.cs            #   协议解析器基类 + 3 个常用实现
│   └── README.md                    #   Generic 模块文档
├── Extensions/                      # 扩展方法集（命名空间 SpaceCG.Extensions）
│   ├── ArrayExtensions.cs           #   数组模式搜索、快速内存比较
│   ├── EnumerableExtensions.cs      #   ConcurrentQueue<T> 清空并释放
│   ├── FileExtensions.cs            #   文件数量保留、正则/扩展名搜索
│   ├── HttpClientExtensions.cs      #   带进度的上传/下载、MIME 查询
│   ├── InstanceExtensions.cs        #   反射：字段/属性/事件/方法调用
│   ├── MathExtensions.cs            #   数值映射、Clamp、位/字节反转
│   ├── NetworkExtensions.cs         #   本机 IPv4、子网掩码、广播地址
│   ├── PathExtensions.cs            #   路径分隔符检测、相对路径
│   ├── StringExtensions.cs          #   XML 转义、十六进制、参数文本解析
│   ├── SocketExtensions.cs          #   TCP/UDP/串口连接管理与接收解析
│   ├── TypeExtensions.cs            #   类型签名、参数转换、类型查找
│   ├── XElementExtensions.cs        #   XML 模板引用 / 占位符 / 配置加载
│   ├── XElementConfigExample.xml    #   XElementExtensions 配置示例
│   └── README.md                    #   Extensions 模块文档
└── Net/                             # 网络通信（命名空间 SpaceCG.Net）
    ├── RpcServerBase.cs             #   RPC 服务端抽象基类
    ├── RpcServer4X.cs               #   XML-RPC 服务端实现
    ├── RpcClientBase.cs             #   RPC 客户端抽象基类
    ├── RpcClient4X.cs               #   XML-RPC 客户端实现
    ├── RpcMessages.cs               #   InvokeMessage / ResponseMessage / 事件参数
    ├── HttpServerBase.cs            #   HTTP 服务基类（HttpListener）
    ├── HttpWebServer.cs             #   静态文件 Web 服务
    ├── WebSockets/WebSocketAPI.cs   #   WebSocket 双向通信基类
    ├── README.md                    #   Net 模块文档
    ├── RPC 服务框架.md               #   RPC 设计文档
    └── 远程过程调用(XML-RPC)消息协议.md  # XML 协议规范（对第三方）
```

---

## 4. 模块一览

| 目录 / 命名空间 | 主要类型 | 说明 | 详细文档 |
|---|---|---|---|
| `Diagnostics` / `SpaceCG.Diagnostics` | `Trace`、`LoggerTraceListener`、`TraceMessage` | 跨平台日志跟踪与异步日志侦听器 | 见 [5.1](#51-diagnostics日志) |
| `Generic` / `SpaceCG.Generic` | `ChecksumHelper`、`CRCCheckHelper`、`CircularBuffer<T>`、`ObjectPool<T>`、`ProtocolParser` | 校验算法、缓冲区、对象池、协议解析 | [Generic/README.md](./Generic/README.md) |
| `Extensions` / `SpaceCG.Extensions` | 12 个 `static partial class` + `ProgressableStreamContent` | 对 BCL 类型的扩展方法集 | [Extensions/README.md](./Extensions/README.md) |
| `Net` / `SpaceCG.Net` | `RpcServerBase`/`RpcServer4X`、`RpcClientBase`/`RpcClient4X`、`HttpServerBase`/`HttpWebServer`、`WebSocketAPI` | RPC、HTTP、WebSocket | [Net/README.md](./Net/README.md) |

> 各模块的完整 API 表、示例与设计说明都在对应子 `README.md` 中，本文档只做总览与速查。

---

## 5. 核心能力速查

### 5.1 Diagnostics：日志

#### `Trace`（`SpaceCG.Diagnostics`）

与 `System.Diagnostics.Trace` 兼容的静态日志入口，**无需条件编译宏**：

- .NET Framework / .NET Core：直接转发到 `System.Diagnostics.Trace`；
- Unity3D：静态构造中通过 `Type.GetType("UnityEngine.Application, UnityEngine")` 检测运行时，
  `WriteLine`/`TraceInformation` → `Debug.Log`，`TraceWarning` → `LogWarning`，`TraceError` → `LogError`。

```csharp
Trace.WriteLine("message");
Trace.WriteLine("value = {0}", 42);
Trace.TraceInformation("客户端 {0} 已连接", endPoint);
Trace.TraceWarning("信号量已存在（异常情况）");
Trace.TraceError("启动失败：{0}", ex.Message);
```

> 本项目内部统一使用 `using Trace = SpaceCG.Diagnostics.Trace;`，避免与 BCL 同名类冲突。

#### `LoggerTraceListener`（`SpaceCG.Diagnostics`）

| 能力 | 说明 |
|---|---|
| 异步写入 | 无锁队列消费，不阻塞调用线程 |
| 批量 Flush | `AutoFlushInterval`（默认 4 条） |
| 文件轮转 | `FileSizeLimit`（默认 2 MB）、`FileBackupDays`（默认 30 天） |
| 控制台输出 | 按级别着色（DEBUG/INFO 灰绿、WARN 黄、ERROR 红） |
| 调用者信息 | `EnableCallerInfo`（DEBUG 默认 true，有栈开销） |
| 实时监控 | `TraceMessageEnqueued` 事件 |

输出格式：`[12:01:15.234] [ INFO] [ 1] [MyClass.MyMethod](42) - 日志内容`

构造函数：`LoggerTraceListener(bool)`、`(string fileName, string name, bool)`、`(Stream, ...)`、`(TextWriter, ...)`。

---

### 5.2 Generic：通用组件

#### 校验

```csharp
byte[] data = { 0x01, 0x02, 0x03, 0x04, 0x05 };

// 校验和（SpaceCG.Generic.ChecksumHelper）
byte sum8 = data.Sum8();
byte xor = data.XOR8();
byte bcc = data.BCC8();
byte lrc = data.LRC8();

// CRC（SpaceCG.Generic.CRCCheckHelper，Rocksoft 通用模型）
ushort modbus = data.ComputeCRC16_MODBUS();
uint crc32 = data.ComputeCRC32();
byte crc8 = data.ComputeCRC8(offset: 0, length: data.Length, poly: 0x07, init: 0x00,
                             refIn: false, refOut: false, xorOut: 0x55);
```

- 预设协议：`CRC8_ITU / ROHC / MAXIM`、`CRC16_IBM / MAXIM / USB / MODBUS / CCITT / CCITT-FALSE / X25 / XMODEM / XMODEM2 / DNP`、`CRC32 / CRC32_C / CRC32_MPEG2`、`CRC64_ISO / ECMA`。
- 查找表按 `(poly, refIn)` 组合缓存（`ConcurrentDictionary`），只生成一次。
- 即使 `refIn = true`，也传入**标准多项式**，内部自动处理反转。

#### `CircularBuffer<T>`

```csharp
var buffer = new CircularBuffer<int>(5);
buffer.AddBack(1); /* ... */ buffer.AddBack(6);   // 满时自动淘汰最旧元素
int first = buffer.PeekFront();
int last = buffer.PeekBack();
var segments = buffer.GetSegments();              // 零拷贝分段视图（最多 2 段）
```

所有操作 O(1)，实现 `IEnumerable<T>` / `IReadOnlyList<T>`；**非线程安全**。

#### `ObjectPool<T>`

```csharp
var pool = new ObjectPool<MyBuffer>(initialCount: 16, maxCount: 64);
var item = pool.Rent();      // 池空时 new T()
item.Reset();                // 归还前建议重置状态
pool.Return(item);           // 池满时丢弃；若 T : IDisposable 则自动 Dispose
pool.Clear();                // 清空并释放
```

线程安全（`ConcurrentQueue<T>` + `Interlocked`），`Count` 为**近似值**，仅用于监控。

#### `ProtocolParser`

生产者-消费者模式解析字节流（线性缓冲 + 读写双指针，零拷贝）：

| 子类 | 适用协议 |
|---|---|
| `FixedLengthProtocolParser` | 固定长度帧 |
| `FooterProtocolParser` | 尾标记（如 `\r\n` 行协议） |
| `HeaderFooterProtocolParser` | 头尾标记（如串口帧） |

成员速查：

| 成员 | 说明 |
|---|---|
| `Write(byte[], int, int)` / `Write(ArraySegment<byte>)` | 写入字节并**立即解析**，返回实际写入字节数 |
| `ProcessBuffer()` | 手动触发一次解析循环（通常与 `Write` 二选一，或先写裸数据后调用） |
| `FrameReceived` 事件 | 解析出完整帧时触发，`FrameEventArgs.FrameView` 为内部缓冲区的零拷贝视图 |
| `Capacity` / `Buffer` | 缓冲容量与内部缓冲区 |
| `ReadPosition` / `WritePosition` / `ReadableBytes` / `WritableBytes` | 读写指针与可用空间 |
| `protected abstract ArraySegment<byte> TryParseFrame(...)` | 子类实现帧匹配逻辑，返回 `default` 表示数据不完整 |

```csharp
var parser = new FooterProtocolParser(new byte[] { 0x0D, 0x0A });   // 默认缓冲区 1024 字节
parser.FrameReceived += (s, e) =>
{
    string line = Encoding.UTF8.GetString(e.FrameView.Array, e.FrameView.Offset, e.FrameView.Count);
    Trace.WriteLine(line);
};

byte[] data = Encoding.UTF8.GetBytes("hello\r\n");
int written = parser.Write(data, 0, data.Length);   // 写入即解析，返回实际写入字节数
```

要点：

- `Write` 内部顺序为 `CompactBuffer()` → `Buffer.BlockCopy` → `ProcessBuffer()`，因此一般无需手动调用 `ProcessBuffer`。
- 尾部空间不足时按 `WritableBytes` **截断写入**，调用方需根据返回值处理剩余数据（循环重发）。
- **非线程安全**，请勿多线程并发写入；`FrameReceived` 回调中的异常会被内部捕获并记录，不会中断解析循环。

---

### 5.3 Extensions：扩展方法集

全部为 **`static partial class`**（便于按主题拆文件、跨项目追加），命名空间统一为 `SpaceCG.Extensions`。

| 类 | 典型用途 |
|---|---|
| `ArrayExtensions` | `IndexOf` 模式搜索（`byte[]` / `ArraySegment<byte>` / 泛型 `T[]`）、`LastIndexOf`、`SequenceEqual` 快速内存比较（32 字节展开，含 `byte*` unsafe 重载） |
| `EnumerableExtensions` | `ConcurrentQueue<T>.Clear()` 出队并释放 `IDisposable` 元素 |
| `FileExtensions` | 按数量保留文件、正则/扩展名搜索、系列文件分组 |
| `HttpClientExtensions` | 带进度的上传/下载（`ProgressableStreamContent`）、MIME 类型查询 |
| `InstanceExtensions` | 反射读写字段/属性、事件订阅清理、动态方法调用 |
| `MathExtensions` | `Map` 区间映射、`Clamp`（全数值类型）、`GetBit`/`SetBit`、`GetBits`/`SetBits` 位域提取与写入、`ReverseBits`/`ReverseBytes` |
| `NetworkExtensions` | 本机 IPv4、子网掩码、广播地址计算 |
| `PathExtensions` | 路径分隔符检测、相对路径计算 |
| `StringExtensions` | XML 转义、十六进制互转、`TryParseParameters` 参数文本解析、类型转换 |
| `SocketExtensions` | TCP 连接状态检测、自动重连、TCP/UDP/串口接收解析 |
| `TypeExtensions` | 类型签名生成、参数转换流水线（带内部缓存）、类型查找 |
| `XElementExtensions` | `<RefTemplate>` 模板展开、`{Key}` 占位符替换、`LoadConfig` 一键加载 |

XML 配置一键加载：

```csharp
using SpaceCG.Extensions;

// 自动完成 ApplyTemplates（模板引用展开）+ ApplyDictionary（{Key} 占位符替换）
XElement config = XElementExtensions.LoadConfig("config.xml");
if (config.TryGetValue<string>("Title", out string title))
    Trace.WriteLine(title);
```

---

### 5.4 Net：网络通信

**传输层** TCP 字节流，**数据层** 以 CRLF 按行分隔，**消息层** 由子类定义（当前支持 XML）。

```csharp
// 服务端
var server = new RpcServer4X(IPAddress.Loopback, 8080);
server.RegisterObject("Demo", new DemoController());
server.Start();

// 客户端
var client = new RpcClient4X(IPAddress.Loopback, 8080);
client.Connect();

// 请求-响应（Func 语义，有返回值，必须等待）
var response = await client.InvokeFuncAsync("Demo", "GetCurrentPage");
// 单向通知（Action 语义，无返回值，发射后即忘）
await client.InvokeActionAsync("Demo", "OpenPage", new object[] { 42 });
```

服务端/客户端为**镜像对称设计**：同样的环形缓冲 + CRLF 行解析 + 抽象方法（`SerializeInvokeMessage` / `DeserializeResponseMessage` 等），
新增协议只需实现两个序列化方法。

其它组件：

- `HttpServerBase` / `HttpWebServer`：基于 `HttpListener` 的静态文件 Web 服务。
- `WebSocketAPI`：基于 `ClientWebSocket` 的双向通信基类。

错误码约定（详见 [Net/README.md](./Net/README.md)）：客户端本地 `-96 ~ -107`，服务端 `0/1/-5/-10 ~ -14/-20`。

---

## 6. 设计约定

| 主题 | 约定 |
|---|---|
| 扩展方法 | 统一使用 `static partial class`，按主题拆分文件，命名空间 `SpaceCG.Extensions` |
| 日志 | 全库统一 `using Trace = SpaceCG.Diagnostics.Trace;`，不使用 `System.Diagnostics.Trace` 直接调用 |
| 线程安全 | 每个类在 XML 注释的 **线程安全** 段落中明确标注（线程安全 / 非线程安全 / 需外部同步） |
| 性能 | 热路径避免分配；大块数据优先池化（`ObjectPool<T>`）或零拷贝（`ArraySegment<T>`） |
| 异常 | 不捕获 `Exception` 基类；性能关键路径提供 `Try*` 模式 |
| 取消 | 异步方法统一接受 `CancellationToken`，不使用强制线程终止 |
| 文档 | 所有公有 API 必须有 `///` 注释（含参数、返回值、异常、示例、线程安全） |
| 兼容 | 不做 Unity 条件编译，统一用运行时类型检测（见 `Trace`） |

---

## 7. 构建与打包

```powershell
dotnet build SpaceCG.Standard\SpaceCG.Standard.csproj -c Release
```

- 输出：`bin/Release/netstandard2.0/SpaceCG.Standard.dll` + `SpaceCG.Standard.xml`（XML 文档）。
- `GeneratePackageOnBuild=True`：同时生成 `bin/Release/SpaceCG.Standard.<Version>.nupkg`，可推送到内部 NuGet 源。
- 版本号在 `SpaceCG.Standard.csproj` 的 `<Version>` 中维护（当前 `1.0.3`）。

---

## 8. 已知问题与建议

1. **子 README 的「目标框架」标注曾为 `.NET Framework 4.8`**（历史遗留）：`Extensions/README.md`、`Generic/README.md`、
   `Net/README.md` 已于 2026-10-13 修正为 `netstandard2.0`，与 csproj 一致。
2. **`LoggerTraceListener` 命名空间**：类型定义在 `Diagnostics/` 目录、命名空间为 `SpaceCG.Diagnostics`；
   `Generic/README.md` 因归类方便仍收录该类，但已标注真实命名空间并把示例改为 `using SpaceCG.Diagnostics;`。
3. **`Extensions/README.md` 曾遗漏 `EnumerableExtensions`**（源码 12 个扩展类，文档只列 11 个），已补齐类列表与独立小节。
4. **`Generic/README.md` 中 `ProtocolParser` 的 API 曾与源码不符**：文档原列有 `ReadFromAsync(Stream, CancellationToken)`、
   `Clear()` / `ClearAsync(CancellationToken)` / `Dispose()`，这些在当前源码中**不存在**，已按源码重写为
   `Write` / `ProcessBuffer` / `FrameReceived` 与容量/指针属性（见 [5.2](#52-generic通用组件)）。
   若确实需要「从流异步读取」，应在上层自行循环 `Stream.ReadAsync` + `Write`，或给 `ProtocolParser` 补该方法。
5. **`ObjectPool<T>.Count` 是近似值**：基于 `Interlocked` 自增自减，高并发下可能略微超出 `MaxCount`，仅作监控用，不要用于精确的容量判定。
6. **`CircularBuffer<T>` 与 `ProtocolParser` 非线程安全**：多线程场景需外部加锁或收敛到单一读写线程。
7. **`ProtocolParser` 满载丢数据**：缓冲区写满且未解析出完整帧时会整体清空（并输出 Warning 日志）以防内存无限增长，超大帧需调大容量。
8. **`EnableCallerInfo` 有开销**：`LoggerTraceListener` 捕获调用栈（方法名/行号）会明显降低日志吞吐，生产环境建议关闭。
9. **`ChecksumHelper.LRC8(IReadOnlyList<byte>)` 漏写 `this`**：该无参重载不是扩展方法，`data.LRC8()` 会报 CS1061；
   可用 `data.LRC8(0, data.Length)`。建议源码补上 `this` 修饰符（同类 `Sum8/XOR8/BCC8` 均带 `this`）。
10. **缺少自动化测试与基准**：CRC、环形缓冲、协议解析、数组搜索等核心算法尚未配备单元测试与 BenchmarkDotNet 基准（见仓库根 `ReadMe.md` 的未来规划）。

---

## 9. 相关项目

| 项目 | 目标框架 | 说明 |
|---|---|---|
| `SpaceCG.Standard.Devices` | net48 | 设备通信基础：`ITransportChannel` 抽象 + 串口/TCP/UDP 实现、Modbus RTU |
| `SpaceCG.Standard.Devices.LedRenderer` | net48 | 全彩 LED 灯带渲染（帧协议、灯带/总线模型、实时绘制） |
| `SpaceCG.Standard.Devices.NModbus` | net48 | NModbus 从站/同步器 |
| `SpaceCG.Standard.Windows` | net48 | Windows API 封装：设备枚举、热插拔监听 |
| `SpaceCG.Standard.Wpf` | net48 | WPF 扩展（画刷/位图/LED 渲染控件/WPF 元素绘制） |
| `Z_TestWpfApp` | net48 | 集成测试应用（RPC、LED 渲染、设备监听） |

依赖方向：上述项目均**单向依赖** `SpaceCG.Standard`，本库不反向依赖任何兄弟项目。

---

## 10. 文档索引

| 文档 | 说明 |
|---|---|
| [Extensions/README.md](./Extensions/README.md) | 扩展方法集完整 API、示例、性能特性、已知问题 |
| [Generic/README.md](./Generic/README.md) | 校验、环形缓冲、CRC、日志侦听器、对象池、协议解析 |
| [Net/README.md](./Net/README.md) | RPC 服务端/客户端、HTTP 服务、WebSocket |
| [Net/RPC 服务框架.md](./Net/RPC%20服务框架.md) | RPC 架构设计文档（内部开发） |
| [Net/远程过程调用(XML-RPC)消息协议.md](<./Net/远程过程调用(XML-RPC)消息协议.md>) | XML-RPC 协议规范（对第三方团队） |
| [../ReadMe.md](../ReadMe.md) | 解决方案总览、依赖关系、编码规范摘要 |

---

*项目路径：`SpaceCG.Standard`*
*目标框架：.NET Standard 2.0 | C# 7.3 | 零第三方依赖*
*命名空间：`SpaceCG.Diagnostics`、`SpaceCG.Generic`、`SpaceCG.Extensions`、`SpaceCG.Net`*
