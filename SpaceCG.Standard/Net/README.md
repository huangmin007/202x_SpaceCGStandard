# SpaceCG.Net

`SpaceCG.Net` 命名空间提供轻量级远程过程调用（RPC）框架，面向局域网络下的 DEMO 交互控制场景。

- **目标框架**：.NET Standard 2.0
- **传输**：TCP 字节流
- **行层**：CRLF（`\r\n`）行分隔
- **消息层**：由子类定义（当前支持 XML）

---

## 类列表

| 类 | 类型 | 说明 |
|------|------|------|
| [`RpcServerBase`](./RPC%20服务框架.md) | abstract class | RPC 服务端抽象基类，提供 TCP 连接管理、数据行解析（一行一消息）、方法反射调用等核心能力 |
| [`RpcServer4X`](./RPC%20服务框架.md#35-rpcserver4x) | sealed class | 基于 XML 协议的 RPC 服务端实现，可直接使用 |
| [`RpcClientBase`](./RPC%20服务框架.md) | abstract class | RPC 客户端抽象基类，与服务端镜像对称，提供连接、请求/响应匹配、超时、自动重连等能力 |
| [`RpcClient4X`](./RPC%20服务框架.md) | sealed class | 基于 XML 协议的 RPC 客户端实现 |
| [`InvokeMessage`](./RPC%20服务框架.md#32-invokemessage) | class | 客户端调用请求的数据对象 |
| [`ResponseMessage`](./RPC%20服务框架.md#33-responsemessage) | class | 方法调用结果的数据对象 |
| [`InvokeMessageEventArgs`](./RPC%20服务框架.md#34-invokemessageeventargs) | class | 消息拦截事件参数（继承 `CancelEventArgs`） |
| [`TcpClientExtensions`](#tcpclientextensions) | static class | `TcpClient` 扩展方法 |
| [`AutoReconnectTcpClient`](#autoreconnecttcpclient) | sealed class | 带自动重连功能的独立 TCP 客户端封装 |

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
await client.ConnectAsync();

// 请求-响应调用
var response = await client.InvokeAsync("Demo", "GetCurrentPage");
if (response.Code >= 0)
    Console.WriteLine(response.ReturnValue);  // "/home"

// 单向通知（fire-and-forget）
await client.NotifyAsync("Demo", "OpenPage", new object[] { 42 });
```

---

## 文档索引

| 文档 | 说明 | 适用对象 |
|------|------|------|
| [RPC 服务框架.md](./RPC%20服务框架.md) | RpcServerBase 设计文档：架构、流程、设计决策、使用示例 | 内部开发人员 |
| [远程过程调用(XML-RPC)消息协议.md](./远程过程调用\(XML-RPC\)消息协议.md) | XML 协议规范：行格式、消息格式、参数约定、状态码 | **第三方团队/公司** |

---

## `RpcServerBase`

抽象基类，RPC 服务端的核心。

```
行层（CRLF 行分隔，一行一消息）→ 消息层（子类协议解析）→ 调度层（并发+校验）→ 执行层（反射调用）→ 响应层（序列化回写）
```

> 详见 [RPC 服务框架.md](./RPC%20服务框架.md)

### 关键 API

```csharp
public abstract class RpcServerBase : IDisposable
{
    // 属性
    public bool IsRunning { get; }
    public IPEndPoint LocalEndPoint { get; }
    public IReadOnlyList<TcpClient> Clients { get; }
    public int SendBufferSize { get; set; }     // 默认 32KB
    public int ReceiveBufferSize { get; set; }  // 默认 64KB
    public List<string> MethodFilters { get; }  // 方法过滤列表

    // 方法
    public void Start();
    public void Stop();
    public void RegisterObject(string objectName, object objectInstance);

    // 子类实现（一行一条消息）
    protected abstract InvokeMessage DeserializeInvokeMessage(ArraySegment<byte> dataLine, IPEndPoint remoteEndPoint);
    protected abstract byte[] SerializeResponseMessage(ResponseMessage responseMessage, IPEndPoint remoteEndPoint);

    // 事件
    public event EventHandler<IPEndPoint> ClientConnected;
    public event EventHandler<IPEndPoint> ClientDisconnected;
    public event EventHandler<InvokeMessageEventArgs> ClientMessageInvoking;
}
```

---

## `RpcServer4X`

基于 XML 协议的 `RpcServerBase` 实现，可直接实例化使用。

- 每行一条 XML 自闭合消息，以 CRLF 为行边界
- `DeserializeInvokeMessage` 反序列化单条 XML 消息，返回一个 `InvokeMessage`
- 响应使用 `StringBuilder` 直拼 XML + CRLF（性能优化）

```csharp
// 创建实例
var server = new RpcServer4X(8080);                           // 监听所有网卡
var server = new RpcServer4X(IPAddress.Loopback, 8080);      // 仅本地

server.RegisterObject("Demo", new DemoController());
server.Start();
```

> 第三方通信协议详见 [远程过程调用(XML-RPC)消息协议.md](./远程过程调用\(XML-RPC\)消息协议.md)

---

## `InvokeMessage`

请求消息数据类，通过工厂方法创建。

| 属性 | 类型 | 必须 | 说明 |
|------|------|:--:|------|
| `ObjectName` | string | ✅ | 目标对象名称 |
| `MethodName` | string | ✅ | 目标方法名称 |
| `Id` | int | 否 | 消息标识（默认 0） |
| `Parameters` | object[] | 否 | 方法参数 |
| `ResponseMode` | int | 否 | -1=不响应，0=默认，1=强制响应 |
| `Version` | Version | 只读 | 协议版本 2.0.0 |

```csharp
InvokeMessage.Create("Demo", "GetCurrentPage");
InvokeMessage.Create("Demo", "OpenPage", "2,\"en-US\"");
InvokeMessage.Create("Video", "Seek", 5.6f);
```

---

## `ResponseMessage`

响应消息数据类，由服务端构造。

| 属性 | 类型 | 说明 |
|------|------|------|
| `Id` | int | 对应请求 Id |
| `Code` | int | ≥0 成功，<0 失败 |
| `ObjectMethod` | string | `{ObjectName}.{MethodName}` |
| `Description` | string | 结果描述/错误信息 |
| `ReturnValue` | object | 返回值 |
| `ReturnType` | Type | 返回值类型 |

**状态码**：

| Code | 含义 |
|:----:|------|
| 0 | 成功（void） |
| 1 | 成功（有返回值） |
| -1 | 对象未注册 |
| -2 | 方法被过滤 |
| -3 | 方法不存在 |
| -4 | 参数转换失败 |
| -5 | 执行异常 |

---

## `RpcClientBase`

客户端抽象基类，与 `RpcServerBase` 镜像对称设计。

- 以 CRLF 拆分数据行，每行一条响应消息
- 请求/响应通过 `Id` 匹配（`ConcurrentDictionary` + `TaskCompletionSource`）
- 发送使用 `SemaphoreSlim(1,1)` 序列化防止字节交错
- 意外断开时自动重连（固定 3 秒间隔），手动 `Close()` 不重连
- 默认超时 3 秒，可通过 `DefaultTimeout` 配置

```csharp
public abstract class RpcClientBase : IDisposable
{
    // 公共 API
    public Task ConnectAsync();
    public void Close();
    public Task<ResponseMessage> InvokeAsync(string objectName, string methodName, object[] parameters = null, TimeSpan? timeout = null);
    public Task<bool> NotifyAsync(string objectName, string methodName, object[] parameters = null);

    // 子类实现
    protected abstract byte[] SerializeInvokeMessage(InvokeMessage invokeMessage);
    protected abstract ResponseMessage DeserializeResponseMessage(ArraySegment<byte> dataLine);
}
```

## `RpcClient4X`

基于 XML 协议的 `RpcClientBase` 实现，与服务端 `RpcServer4X` 配套使用。

```csharp
var client = new RpcClient4X(IPAddress.Loopback, 8080);
await client.ConnectAsync();
var response = await client.InvokeAsync("Demo", "GetCurrentPage");

---

## `TcpClientExtensions`

```csharp
public static class TcpClientExtensions
{
    /// <summary>非阻塞检测连接状态</summary>
    public static bool IsConnected(this TcpClient client);
}
```

---

## 文件结构

```
SpaceCG.Standard/Net/
├── README.md                            ← 本文件（命名空间总览）
├── RPC 服务框架.md                       ← RpcServerBase 设计文档
├── 远程过程调用(XML-RPC)消息协议.md       ← XML 协议规范（第三方对接）
├── InvokeMessage.cs                      ← 请求消息 + 事件参数 + 对象池
├── ResponseMessage.cs                    ← 响应消息
├── RpcServerBase.cs                      ← 服务端抽象基类
├── RpcServer4X.cs                        ← 服务端 XML 协议实现
├── RpcClientBase.cs                      ← 客户端抽象基类
├── RpcClient4X.cs                        ← 客户端 XML 协议实现
└── AutoReconnectTcpClient.cs             ← 独立 TCP 客户端（自动重连，非 RPC 框架核心）
```

---

> 文档版本：v1.2  |  最后更新：2026-07-12  |  维护：SpaceCG 团队
