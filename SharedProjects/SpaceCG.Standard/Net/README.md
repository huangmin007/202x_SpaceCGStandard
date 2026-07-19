# SpaceCG.Net

`SpaceCG.Net` 命名空间提供轻量级网络通信框架，涵盖远程过程调用（RPC）、HTTP 服务、WebSocket 双向通信和增强型 TCP 客户端。

- **目标框架**：.NET Framework 4.8（C# 7.3）
- **传输层**：TCP 字节流
- **数据层**：CRLF（`\r\n`）按行分隔数据（默认）
- **消息层**：由子类定义（当前支持 XML）

---

## 类列表

| 类 | 类型 | 说明 |
|------|------|------|
| [`RpcServerBase`](#rpcserverbaseserver) | abstract class | RPC 服务端抽象基类，TCP 连接管理、环形缓冲数据行解析、方法反射调用 |
| [`RpcServer4X`](#rpcserver4x) | sealed class | 基于 XML 协议的 RPC 服务端实现，可直接使用 |
| [`RpcClientBase`](#rpcclientbase) | abstract class | RPC 客户端抽象基类，连接/重连、请求-响应 Id 匹配、超时管理 |
| [`RpcClient4X`](#rpcclient4x) | sealed class | 基于 XML 协议的 RPC 客户端实现 |
| [`InvokeMessage`](#invokemessage--responsemessage) | class | 客户端调用请求数据对象 |
| [`ResponseMessage`](#invokemessage--responsemessage) | class | 方法调用结果数据对象 |
| [`InvokeMessageEventArgs`](#invokemessageeventargs) | class | 消息拦截事件参数 |
| [`TcpClientEx`](#tcpclientex) | sealed class | 带自动重连的增强型 TCP 客户端，事件驱动接收数据 |
| [`DataReceivedEventArgs`](#tcpclientex) | class | TCP 数据接收事件参数 |
| [`HttpServerBase`](#httpserverbase) | abstract class | HTTP 服务抽象基类（HttpListener） |
| [`HttpWebServer`](#httpwebserver) | class | 静态文件 Web 服务 |
| [`WebSocketAPI`](#websocketapi) | abstract class | WebSocket 双向通信基类（ClientWebSocket） |

---

## 快速开始

### 服务端

```csharp
using SpaceCG.Net;
using System.Net;

// 1. 定义业务对象
public class DemoController
{
    public string GetCurrentPage() => "/home";
    public void OpenPage(int pageId) { /* 处理 */ }
}

// 2. 启动服务
var server = new RpcServer4X(IPAddress.Loopback, 8080);
server.RegisterObject("Demo", new DemoController());
server.Start();
```

### 客户端

```csharp
using SpaceCG.Net;

// 使用 RpcClient4X（XML 协议实现）
var client = new RpcClient4X(IPAddress.Loopback, 8080);
client.Connect();

// 请求-响应调用（Func 语义，有返回值，必须等待结果）
var response = await client.InvokeFuncAsync("Demo", "GetCurrentPage");
if (response.Code >= 0)
    Console.WriteLine(response.ReturnValue);  // "/home"

// 单向通知（Action 语义，无返回值，发射后即忘）
await client.InvokeActionAsync("Demo", "OpenPage", new object[] { 42 });
```

---

## 文档索引

| 文档 | 说明 | 适用对象 |
|------|------|------|
| [RPC 服务框架.md](./RPC%20服务框架.md) | Rpc 设计文档：架构、流程、设计决策、使用示例 | 内部开发人员 |
| [远程过程调用(XML-RPC)消息协议.md](./远程过程调用\(XML-RPC\)消息协议.md) | XML 协议规范：行格式、消息格式、参数约定、状态码 | **第三方团队/公司** |

---

## RpcServerBase（Server）

RPC 服务端抽象基类，核心组件。

### 处理流水线

```
行层（环形缓冲 + Delimiters 分隔，一行一条消息）
  → 消息层（子类 DeserializeInvokeMessage 协议解析）
  → 调度层（并发信号量 ProcessInvokeSemaphore + 事件拦截 ClientInvokeRequest）
  → 执行层（SynchronizationContext.Send 封送到构造线程反射调用）
  → 响应层（子类 SerializeResponseMessage 序列化回写）
```

### 关键 API

```csharp
public abstract class RpcServerBase : IDisposable
{
    // 静态
    public static readonly byte[] NewLine;          // CRLF 分隔符 {0x0D, 0x0A}
    public static readonly Regex IdentifierPattern; // 名称校验正则

    // 属性
    public bool IsRunning { get; }                  // 服务是否正在运行
    public IPEndPoint LocalEndPoint { get; }        // 监听地址
    public IEnumerable<TcpClient> Clients { get; }  // 已连接客户端集合
    public IEnumerable<string> AvailableMethods { get; } // 可调用方法列表
    public int SendBufferSize { get; set; }         // 发送缓冲区 默认 32KB
    public int ReceiveBufferSize { get; set; }      // 接收缓冲区 默认 64KB
    public byte[] Delimiters { get; protected set; }// 消息分隔符 默认 CRLF
    public readonly List<string> MethodFilters;     // 方法过滤列表（默认含 *.Dispose, *.Close）

    // 方法
    public void Start();
    public void Stop();
    public void RegisterObject(string objectName, object objectInstance);

    // 子类实现（协议层，一行一条消息）
    protected abstract InvokeMessage DeserializeInvokeMessage(ArraySegment<byte> dataLine, IPEndPoint remoteEndPoint);
    protected abstract byte[] SerializeResponseMessage(ResponseMessage responseMessage, IPEndPoint remoteEndPoint);

    // 事件
    public event EventHandler<IPEndPoint> ClientConnected;
    public event EventHandler<IPEndPoint> ClientDisconnected;
    public event EventHandler<InvokeMessageEventArgs> ClientInvokeRequest;  // 可取消调用
}
```

### 构造函数

```csharp
// 监听所有网卡
var server = new RpcServer4X(8080);

// 仅监听本地回环
var server = new RpcServer4X(IPAddress.Loopback, 8080);

// 指定 IP 字符串
var server = new RpcServer4X("192.168.1.100", 8080);
```

### 设计要点

- **环形缓冲区**：每客户端独立 32KB 环形缓冲（`ReceiveBufferSize / 2`），紧凑阈值 `bufferSize / 8`
- **并发控制**：`ProcessInvokeSemaphore` 初始许可 8、最大 64，防止 `SynchronizationContext.Send` 阻塞导致线程池膨胀
- **写入序列化**：每客户端独立 `SemaphoreSlim(1,1)`，防止并发响应字节交错
- **方法缓存**：`RegisterObject` 时预缓存所有公共实例方法 + 扩展方法到 `RegisteredMethods`
- **自定义分隔符**：`Delimiters` 属性支持修改消息边界标记（如 LF、NULL 字节序列）
- **事件拦截**：通过 `ClientInvokeRequest` 事件可取消调用（返回 Code=-5）

---

## RpcServer4X

基于 XML 协议的 `RpcServerBase` 实现（XML-RPC v2.0），可直接实例化。

### 特性

- 每行一条 XML 格式消息，以 CRLF 为行边界
- 使用 `XElement.Parse` 反序列化，`StringBuilder` 直拼 XML 响应（性能优化）
- 内置备用的 `XAttributeParse` 正则解析路径（`#if true` 条件编译切换）
- 响应使用 `SecurityElement.Escape` 进行 XML 转义

### 静态成员

```csharp
public static readonly byte[] XmlTerminate = { 0x2F, 0x3E }; // "/>" 分隔符
```

---

## RpcClientBase

客户端抽象基类，与 `RpcServerBase` 镜像对称设计。

### 关键 API

```csharp
public abstract class RpcClientBase : IDisposable
{
    // 属性
    public bool IsConnected { get; }                // 连接状态
    public IPEndPoint RemoteEndPoint { get; }       // 服务端地址
    public IPEndPoint LocalEndPoint { get; }        // 本地地址
    public int SendBufferSize { get; set; }         // 发送缓冲区 默认 32KB
    public int ReceiveBufferSize { get; set; }      // 接收缓冲区 默认 64KB
    public TimeSpan ResponseTimeout { get; set; }   // 请求超时 默认 3 秒
    public TimeSpan ReconnectDelay { get; set; }    // 重连延迟 默认 3 秒（MaxValue=禁用）
    public byte[] Delimiters { get; protected set; }// 消息分隔符 默认 CRLF

    // 连接管理
    public void Connect();
    public void Connect(IPAddress address, int port);
    public void Connect(string address, int port);
    public void Close();

    // 远程调用
    public Task<ResponseMessage> InvokeFuncAsync(string objectName, string methodName,
        object[] parameters = null, TimeSpan? timeout = null);
    public Task<bool> InvokeActionAsync(string objectName, string methodName,
        object[] parameters = null);

    // 底层写入
    public Task WriteAsync(byte[] data, int offset, int length, CancellationToken ct);

    // 子类实现（协议层）
    protected abstract byte[] SerializeInvokeMessage(InvokeMessage requestMessage);
    protected abstract ResponseMessage DeserializeResponseMessage(ArraySegment<byte> responseMessage);
}
```

### 核心机制

| 机制 | 实现 |
|------|------|
| **请求-响应匹配** | `ConcurrentDictionary<int, PendingCall>` + `TaskCompletionSource<ResponseMessage>` |
| **发送序列化** | `SemaphoreSlim(1,1)` 防止并发写入导致字节交错 |
| **超时处理** | `Task.WhenAny` 竞速模式，超时后从字典移除并返回 Code=-97 |
| **自动重连** | `ConnectWithRetryAsync` 循环，意外断开后按 `ReconnectDelay` 间隔重连 |
| **手动关闭** | `_isManualClosed = true`，手动 `Close()` 后不重连 |
| **环形缓冲** | 每会话 32KB 环形缓冲 + 紧凑阈值 `bufferSize / 8`，与 `RpcServerBase` 镜像 |

### 客户端本地错误码

| Code | 含义 |
|:----:|------|
| -96 | Id 冲突（已存在于待响应字典） |
| -97 | 响应超时 |
| -100 | 未连接到服务端 |
| -101 | 连接已关闭 |
| -102 | 连接断开（`CancelAllPendingCalls`） |
| -105 | 消息序列化失败 |
| -106 | 序列化结果为空 |
| -107 | 写入失败 |

---

## RpcClient4X

基于 XML 协议的 `RpcClientBase` 实现，与服务端 `RpcServer4X` 配套使用。

### 特性

- 使用 `StringBuilder` 直拼 XML 请求（性能优化）
- 使用 `XElement.Parse` 解析 XML 响应
- 返回值反序列化通过 `ParseParameters` + `TryConvertParameter` 流水线还原强类型

```csharp
var client = new RpcClient4X(IPAddress.Loopback, 8080);
client.Connect();
var response = await client.InvokeFuncAsync("Demo", "GetCurrentPage");
```

---

## InvokeMessage / ResponseMessage

RPC 消息数据类，通过工厂方法创建。

### InvokeMessage

| 属性 | 类型 | 必须 | 说明 |
|------|------|:--:|------|
| `Id` | `int` | 可选 | 消息唯一标识，Id<0 时不进行匹配跟踪，默认 0 |
| `ObjectName` | `string` | ✅ | 已注册目标对象名称 |
| `MethodName` | `string` | ✅ | 目标方法名称 |
| `Parameters` | `object[]` | 可选 | 方法参数列表 |
| `ResponseMode` | `int` | 可选 | -1=不响应，0=默认规则，1=必须响应 |
| `Description` | `string` | 可选 | 消息描述/注释 |
| `Timestamp` | `DateTimeOffset` | 可选 | 消息时间戳（ISO8601），默认 UtcNow |
| `Version` | `Version` | 只读 | 协议版本 2.0.0 |

```csharp
// 工厂方法
InvokeMessage.Create("Demo", "GetCurrentPage");                  // 无参
InvokeMessage.Create("Demo", "OpenPage", "2,\"en-US\"");        // 字符串参数
InvokeMessage.Create("Demo", "OpenPage", new object[] { 2, "en-US" }); // 对象参数
InvokeMessage.Create("Video", "Seek", 5.6f);                    // 值类型参数
```

### ResponseMessage

| 属性 | 类型 | 必须 | 说明 |
|------|------|:--:|------|
| `Code` | `int` | ✅ | 状态码：<0 失败，≥0 成功，=1 有返回值 |
| `ObjectMethod` | `string` | ✅ | `"{ObjectName}.{MethodName}"` |
| `Id` | `int` | 可选 | 对应请求 Id |
| `Description` | `string` | 可选 | 结果描述或错误信息 |
| `ReturnType` | `Type` | 可选 | 返回值类型 |
| `ReturnValue` | `object` | 可选 | 返回值 |
| `HasReturnValue` | `bool` | 只读 | Code==1 且 ReturnType 非 void |
| `Timestamp` | `DateTimeOffset` | 可选 | 响应时间戳 |
| `Version` | `Version` | 可选 | 协议版本 2.0.0 |

```csharp
// 获取强类型返回值
var response = await client.InvokeFuncAsync("Demo", "GetPageCount");
int count = response.GetReturnValue<int>();
```

### 服务端错误码

| Code | 含义 |
|:----:|------|
| 0 | 成功（void 方法） |
| 1 | 成功（有返回值） |
| -5 | 调用被 ClientInvokeRequest 事件拦截取消 |
| -10 | 对象未注册 |
| -11 | 方法被 MethodFilters 过滤 |
| -12 | 方法不存在（缓存未命中） |
| -13 | 参数转换失败 |
| -14 | 方法执行异常 |
| -15 | 内部处理异常 |

### ResponseMode 响应模式

| 值 | 含义 |
|:--:|------|
| -1 | 不响应（单向通知 / fire-and-forget） |
| 0 | 默认规则：异常必响应、有返回值必响应、void 成功不响应 |
| 1 | 必须响应（无论成功与否） |

---

## InvokeMessageEventArgs

用于 `RpcServerBase.ClientInvokeRequest` 事件的消息拦截参数。

```csharp
public class InvokeMessageEventArgs : EventArgs
{
    public bool Cancel { get; set; }              // 设为 true 取消调用
    public IPEndPoint RemoteEndPoint { get; }     // 客户端端点
    public InvokeMessage InvokeMessage { get; }   // 调用消息
}

// 使用示例
server.ClientInvokeRequest += (sender, args) =>
{
    if (args.InvokeMessage.MethodName == "Shutdown")
        args.Cancel = true;  // 拦截关机调用
};
```

---

## TcpClientEx

增强型 TCP 客户端，提供自动重连、异步收发和事件通知。

### 关键 API

```csharp
public sealed class TcpClientEx : IDisposable
{
    // 属性
    public bool IsConnected { get; }
    public IPEndPoint RemoteEndPoint { get; }
    public IPEndPoint LocalEndPoint { get; }
    public int SendBufferSize { get; set; }        // 默认 32KB
    public int ReceiveBufferSize { get; set; }     // 默认 64KB
    public TimeSpan DefaultTimeout { get; set; }   // 默认 3 秒
    public TimeSpan ReconnectDelay { get; set; }   // 默认 3 秒

    // 连接管理
    public void Connect();
    public void Connect(IPAddress address, int port);
    public void Close();

    // 发送
    public Task WriteAsync(byte[] data, int offset, int length, CancellationToken ct);
    public Task WriteAsync(byte[] data);

    // 事件
    public event EventHandler<EventArgs> Connected;
    public event EventHandler<EventArgs> Disconnected;
    public event EventHandler<DataReceivedEventArgs> DataReceived;
}
```

### 使用示例

```csharp
var client = new TcpClientEx("127.0.0.1", 8888);
client.DataReceived += (sender, e) =>
{
    Console.WriteLine($"收到 {e.Data.Length} 字节");
};
client.Connected += (sender, e) => Console.WriteLine("已连接");
client.Disconnected += (sender, e) => Console.WriteLine("已断开");
client.Connect();
await client.WriteAsync(Encoding.UTF8.GetBytes("Hello"));
client.Close();
```

### 与 RpcClientBase 的区别

| 特性 | TcpClientEx | RpcClientBase |
|------|:--:|:--:|
| 数据消费 | `DataReceived` 事件通知（原始字节） | 内部环形缓冲 + Delimiters 分隔 + 协议解析 |
| 用途 | 通用 TCP 客户端 | RPC 框架专用 |
| 协议层 | 无（调用方自行解析） | 子类实现 Serialize/Deserialize |

---

## HttpServerBase

基于 `HttpListener` 的 HTTP 服务抽象基类。

**文件**：`Http/HttpServerBase.cs`

### 关键 API

```csharp
public abstract class HttpServerBase : IDisposable
{
    public ushort LocalPort { get; }              // 监听端口
    public bool IsRunning { get; }                // 运行状态
    protected HttpListener Listener { get; }      // 底层侦听器

    public virtual void Start(ushort localPort = 8080);
    public virtual void Stop();
    public virtual void Dispose();

    // 子类实现
    protected abstract Task HandleContextSessionAsync(
        HttpListenerContext context, CancellationToken cancellationToken);
}
```

### 使用示例

```csharp
public class MyApiServer : HttpServerBase
{
    protected override async Task HandleContextSessionAsync(
        HttpListenerContext context, CancellationToken ct)
    {
        var response = context.Response;
        if (context.Request.Url.AbsolutePath == "/api/status")
        {
            byte[] data = Encoding.UTF8.GetBytes("{\"status\":\"ok\"}");
            response.ContentType = "application/json";
            response.ContentLength64 = data.Length;
            await response.OutputStream.WriteAsync(data, 0, data.Length, ct);
        }
        else
            response.StatusCode = 404;
        response.Close();
    }
}

var server = new MyApiServer();
server.Start(8080);
```

### 设计特性

- 自动绑定 `localhost`、`127.0.0.1` 及本机所有 IPv4 地址
- 子类异常自动捕获并返回 500 响应
- Start/Stop 幂等，端口 ≤ 1024 时警告需要管理员权限
- 监听循环中使用 `SafeHandleContextSessionAsync` 异常隔离层

---

## HttpWebServer

基于 `HttpServerBase` 的静态文件 Web 服务。

**文件**：`Http/HttpWebServer.cs`

### 关键 API

```csharp
public class HttpWebServer : HttpServerBase
{
    public DirectoryInfo RootDirectory { get; set; }
    public static ConcurrentDictionary<string, string> ContentMimeTypes { get; }
}
```

### 使用示例

```csharp
using SpaceCG.Net.Http;

// 以当前目录为根目录
var server = new HttpWebServer();
server.Start(8080);

// 自定义根目录
var server2 = new HttpWebServer(new DirectoryInfo(@"D:\wwwroot"));

// 注册自定义 MIME 类型
HttpWebServer.ContentMimeTypes[".webp"] = "image/webp";
```

### 安全特性

- 目录遍历防护（`fullPath.StartsWith(RootDirectory)`）
- 目录请求自动追加 `index.html`
- 流式文件响应 80KB 缓冲区，大文件不耗尽内存
- 非法路径 → 400，目录遍历企图 → 403，文件不存在 → 404

---

## WebSocketAPI

基于 `ClientWebSocket` 的双向通信抽象基类。

**文件**：`WebSockets/WebSocketAPI.cs`

### 关键 API

```csharp
public abstract class WebSocketAPI : IDisposable
{
    public bool IsConnected { get; }
    public Dictionary<string, string> RequestHeader { get; }
    public TimeSpan IdleTimeOut { get; set; }      // 默认 3 分钟

    // 子类实现
    protected abstract Uri GetAuthenticationUri();
    protected abstract void AnalyseResponseResult(string text);
    protected abstract void AnalyseResponseResult(byte[] binary);
}
```

### 使用示例

```csharp
public class MyClient : WebSocketAPI
{
    protected override Uri GetAuthenticationUri()
        => new Uri("ws://localhost:8080/ws");

    protected override void AnalyseResponseResult(string text)
    {
        Console.WriteLine($"收到文本: {text}");
    }

    protected override void AnalyseResponseResult(byte[] data)
    {
        Console.WriteLine($"收到二进制: {data.Length} bytes");
    }
}

var client = new MyClient();
// 发送文本：通过子类调用 TextRequestQueue 入队
// 发送二进制：通过子类调用 BinaryRequestQueue 入队
```

### 设计特性

- 独立发送/接收后台线程
- 发送线程自动建立连接（`GetAuthenticationUri`）
- 空闲超时自动断开（`IdleTimeOut`）
- 接收线程通过 `MemoryStream` 拼接分片消息，支持大消息

---

## 文件结构

```
SpaceCG.Standard/Net/
├── README.md                            ← 本文件（命名空间总览）
├── RPC 服务框架.md                       ← RpcServerBase 设计文档
├── 远程过程调用(XML-RPC)消息协议.md       ← XML 协议规范（第三方对接）
├── RpcMessages.cs                        ← InvokeMessage / ResponseMessage / InvokeMessageEventArgs / RpcMessagePool
├── RpcServerBase.cs                      ← 服务端抽象基类
├── RpcServer4X.cs                        ← 服务端 XML 协议实现
├── RpcClientBase.cs                      ← 客户端抽象基类
├── RpcClient4X.cs                        ← 客户端 XML 协议实现
├── TcpClientEx.cs                        ← 增强型 TCP 客户端（自动重连+事件驱动）
├── Http/
│   ├── HttpServerBase.cs                 ← HTTP 服务抽象基类
│   └── HttpWebServer.cs                  ← 静态文件 Web 服务
└── WebSockets/
    └── WebSocketAPI.cs                   ← WebSocket 双向通信基类
```

---

> 文档版本：v2.0  |  最后更新：2026-07-19  |  维护：SpaceCG 团队
