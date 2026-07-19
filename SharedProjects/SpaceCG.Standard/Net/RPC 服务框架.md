# RPC 服务框架

基于 .NET Standard 2.0 / C# 7.3 的轻量级远程过程调用（RPC）框架，面向局域网络下的 DEMO 交互控制场景。

---

## 目录

- [1. 概述](#1-概述)
- [2. 架构设计](#2-架构设计)
- [3. 核心类说明](#3-核心类说明)
- [4. 数据协议与扩展](#4-数据协议与扩展)
- [5. 处理流程](#5-处理流程)
- [6. 使用示例](#6-使用示例)
- [7. 关键设计决策](#7-关键设计决策)
- [8. 已知局限与改进方向](#8-已知局限与改进方向)

---

## 1. 概述

### 1.1 设计目标

| 维度 | 说明 |
|------|------|
| **使用场景** | 局域网内 DEMO 交互控制，PC 端应用程序远程操控 |
| **数据特征** | 数据量小，传输频率低（< 100 fps），指令/控制类型数据 |
| **协议理念** | 格式可读性高，可编辑，可调试；默认以 CRLF 作为分隔符，分隔字节数据块 |
| **性能目标** | 支持每秒 60~100 次/秒的方法调用，延迟稳定（< 16ms），*实际取决于程序性能* |
| **线程安全** | 服务端通过 `SynchronizationContext.Send` 将方法调用封送到 UI 线程或服务线程，保证注册对象的线程安全 |

### 1.2 RPC 的双重含义

本项目中的 RPC 有双重解读：

1. **Remote Procedure Call**（远程过程调用）：TCP 网络通信，跨进程反射调用
2. **Reflection Program Control**（反射程序控制）：通过反射动态调用已注册对象的方法

### 1.3 字节数据与消息

基类 `RpcServerBase` 默认以 **CRLF** 作为字节数据分隔符，对应一条数据消息：

```
┌────── 一条数据消息 (默认以 CRLF 结尾) ──────┐
│            <Message ... />                 │
│                 ↑ 一条消息                  │
└───────────────────────────────────────────┘
```

- **数据拆分**（基类统一处理）：TCP 字节流 → 字节拆分 → 环形缓冲 → 提取字节消息
- **协议解析**（子类协议实现）：数据消息字节 → `DeserializeInvokeMessage()` → `InvokeMessage`（单条）

---

## 2. 架构设计

### 2.1 整体架构

<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 680 400" width="100%" height="100%" style="max-width:680px;">
  <defs>
    <linearGradient id="gBase" x1="0%" y1="0%" x2="0%" y2="100%"><stop offset="0%" stop-color="#667eea"/><stop offset="100%" stop-color="#5a67d8"/></linearGradient>
    <linearGradient id="gXml" x1="0%" y1="0%" x2="0%" y2="100%"><stop offset="0%" stop-color="#38b2ac"/><stop offset="100%" stop-color="#319795"/></linearGradient>
    <linearGradient id="gJson" x1="0%" y1="0%" x2="0%" y2="100%"><stop offset="0%" stop-color="#ed8936"/><stop offset="100%" stop-color="#dd6b20"/></linearGradient>
  </defs>
  <!-- RpcServerBase box -->
  <rect x="60" y="20" width="560" height="210" rx="8" fill="url(#gBase)" opacity="0.15" stroke="#5a67d8" stroke-width="2"/>
  <text x="340" y="42" text-anchor="middle" font-size="14" font-weight="bold" fill="#4a5568">RpcServerBase（抽象基类）</text>
  <line x1="340" y1="50" x2="340" y2="50" stroke="#cbd5e0"/>
  <!-- Sub boxes -->
  <rect x="80" y="60" width="160" height="75" rx="4" fill="white" stroke="#a0aec0" stroke-width="1.5"/>
  <text x="160" y="80" text-anchor="middle" font-size="11" font-weight="bold" fill="#4a5568">TCP 连接管理</text>
  <text x="160" y="98" text-anchor="middle" font-size="10" fill="#718096">AcceptAsync</text>
  <text x="160" y="114" text-anchor="middle" font-size="10" fill="#718096">ClientList 管理</text>
  <text x="160" y="130" text-anchor="middle" font-size="10" fill="#718096">Read/Write/Send Buffer</text>
  <rect x="260" y="60" width="160" height="75" rx="4" fill="white" stroke="#a0aec0" stroke-width="1.5"/>
  <text x="340" y="80" text-anchor="middle" font-size="11" font-weight="bold" fill="#4a5568">数据解析</text>
  <text x="340" y="98" text-anchor="middle" font-size="10" fill="#718096">环形缓冲 RingBuffer</text>
  <text x="340" y="114" text-anchor="middle" font-size="10" fill="#718096">数据字节分割</text>
  <text x="340" y="130" text-anchor="middle" font-size="10" fill="#718096">白字符跳过</text>
  <rect x="440" y="60" width="160" height="75" rx="4" fill="white" stroke="#a0aec0" stroke-width="1.5"/>
  <text x="520" y="80" text-anchor="middle" font-size="11" font-weight="bold" fill="#4a5568">方法反射调用</text>
  <text x="520" y="98" text-anchor="middle" font-size="10" fill="#718096">SyncContext.Send</text>
  <text x="520" y="114" text-anchor="middle" font-size="10" fill="#718096">SemaphoreSlim 限流</text>
  <text x="520" y="130" text-anchor="middle" font-size="10" fill="#718096">MethodInfo.Invoke</text>
  <!-- Abstract methods label -->
  <rect x="80" y="150" width="520" height="28" rx="4" fill="#edf2f7" stroke="#cbd5e0" stroke-width="1" stroke-dasharray="4,2"/>
  <text x="340" y="165" text-anchor="middle" font-size="10" fill="#a0aec0">↓ 抽象方法：DeserializeInvokeMessage() / SerializeResponseMessage() ↓</text>
  <!-- Arrows to subclasses -->
  <line x1="200" y1="235" x2="200" y2="270" stroke="#38b2ac" stroke-width="2" marker-end="url(#arrowTeal)"/>
  <line x1="480" y1="235" x2="480" y2="270" stroke="#dd6b20" stroke-width="2" marker-end="url(#arrowOrange)"/>
  <defs>
    <marker id="arrowTeal" markerWidth="8" markerHeight="6" refX="8" refY="3" orient="auto"><path d="M0,0 L8,3 L0,6 Z" fill="#38b2ac"/></marker>
    <marker id="arrowOrange" markerWidth="8" markerHeight="6" refX="8" refY="3" orient="auto"><path d="M0,0 L8,3 L0,6 Z" fill="#dd6b20"/></marker>
  </defs>
  <!-- RpcServer4X -->
  <rect x="100" y="275" width="200" height="55" rx="6" fill="url(#gXml)" opacity="0.9"/>
  <text x="200" y="298" text-anchor="middle" font-size="13" font-weight="bold" fill="white">RpcServer4X</text>
  <text x="200" y="318" text-anchor="middle" font-size="10" fill="white" opacity="0.9">XML 协议（✅ 已实现）</text>
  <!-- RpcServer4J -->
  <rect x="380" y="275" width="200" height="55" rx="6" fill="url(#gJson)" opacity="0.75"/>
  <text x="480" y="298" text-anchor="middle" font-size="13" font-weight="bold" fill="white">RpcServer4J</text>
  <text x="480" y="318" text-anchor="middle" font-size="10" fill="white" opacity="0.9">JSON 协议（📋 规划中）</text>
  <!-- Labels -->
  <text x="60" y="300" text-anchor="end" font-size="9" fill="#a0aec0" transform="rotate(-90, 60, 300)">继承实现</text>
</svg>

### 2.2 分层职责

| 层次 | 类/组件 | 职责 |
|------|---------|------|
| **传输层** | `TcpListener` / `TcpClient` | TCP 连接生命周期管理，Accept/Read/Write |
| **数据拆分** | 环形缓冲 + 分割符扫描 | 字节流 → 字节拆分（基类统一实现） |
| **协议解析** | `DeserializeInvokeMessage()` (abstract) | 数据字节 → `InvokeMessage`（子类实现，单条消息） |
| **调度层** | `ProcessInvokeMessageAsync()` + `SemaphoreSlim` | 并发调度、消息校验、参数转换 |
| **执行层** | `SynchronizationContext.Send()` + `MethodInfo.Invoke` | 在目标线程上安全执行注册对象方法 |
| **响应层** | `WriteResponseMessageAsync()` + `SerializeResponseMessage()` (abstract) | 序列化响应并写回客户端 |

### 2.3 并发模型

<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 620 280" width="100%" height="100%" style="max-width:620px;">
  <defs>
    <marker id="arrowBlue" markerWidth="8" markerHeight="6" refX="8" refY="3" orient="auto"><path d="M0,0 L8,3 L0,6 Z" fill="#3182ce"/></marker>
    <marker id="arrowGreen" markerWidth="8" markerHeight="6" refX="8" refY="3" orient="auto"><path d="M0,0 L8,3 L0,6 Z" fill="#38a169"/></marker>
  </defs>
  <!-- Thread boxes -->
  <rect x="10" y="15" width="200" height="45" rx="4" fill="#ebf8ff" stroke="#90cdf4" stroke-width="1.5"/>
  <text x="110" y="32" text-anchor="middle" font-size="10" fill="#2b6cb0" font-weight="bold">读线程 1 (ThreadPool)</text>
  <text x="110" y="48" text-anchor="middle" font-size="9" fill="#4a5568">_ = ProcessInvokeMessageAsync()</text>
  <rect x="10" y="75" width="200" height="45" rx="4" fill="#ebf8ff" stroke="#90cdf4" stroke-width="1.5"/>
  <text x="110" y="92" text-anchor="middle" font-size="10" fill="#2b6cb0" font-weight="bold">读线程 2 (ThreadPool)</text>
  <text x="110" y="108" text-anchor="middle" font-size="9" fill="#4a5568">_ = ProcessInvokeMessageAsync()</text>
  <text x="110" y="145" text-anchor="middle" font-size="11" fill="#a0aec0">...</text>
  <rect x="10" y="160" width="200" height="45" rx="4" fill="#ebf8ff" stroke="#90cdf4" stroke-width="1.5"/>
  <text x="110" y="177" text-anchor="middle" font-size="10" fill="#2b6cb0" font-weight="bold">读线程 N (ThreadPool)</text>
  <text x="110" y="193" text-anchor="middle" font-size="9" fill="#4a5568">_ = ProcessInvokeMessageAsync()</text>
  <!-- Arrows from threads to semaphore -->
  <line x1="210" y1="37" x2="260" y2="80" stroke="#3182ce" stroke-width="1.5"/>
  <line x1="210" y1="97" x2="260" y2="115" stroke="#3182ce" stroke-width="1.5"/>
  <line x1="210" y1="182" x2="260" y2="160" stroke="#3182ce" stroke-width="1.5"/>
  <!-- Semaphore -->
  <rect x="260" y="70" width="140" height="110" rx="6" fill="#fefcbf" stroke="#d69e2e" stroke-width="2"/>
  <text x="330" y="95" text-anchor="middle" font-size="11" font-weight="bold" fill="#975a16">SemaphoreSlim</text>
  <text x="330" y="115" text-anchor="middle" font-size="10" fill="#975a16">(25, 60)</text>
  <text x="330" y="135" text-anchor="middle" font-size="9" fill="#a0aec0">并发限流</text>
  <text x="330" y="155" text-anchor="middle" font-size="9" fill="#a0aec0">防止线程爆炸</text>
  <!-- Arrow to SyncContext -->
  <line x1="400" y1="125" x2="440" y2="125" stroke="#38a169" stroke-width="2" marker-end="url(#arrowGreen)"/>
  <!-- SyncContext.Send -->
  <rect x="445" y="70" width="160" height="110" rx="6" fill="#f0fff4" stroke="#68d391" stroke-width="2"/>
  <text x="525" y="95" text-anchor="middle" font-size="11" font-weight="bold" fill="#276749">SyncContext.Send()</text>
  <text x="525" y="115" text-anchor="middle" font-size="9" fill="#276749">阻塞 ThreadPool</text>
  <text x="525" y="135" text-anchor="middle" font-size="9" fill="#276749">等待 UI 线程</text>
  <text x="525" y="155" text-anchor="middle" font-size="9" fill="#276749">执行 MethodInfo.Invoke</text>
  <!-- Bottom output -->
  <rect x="370" y="215" width="230" height="35" rx="4" fill="#edf2f7" stroke="#cbd5e0" stroke-width="1.5"/>
  <text x="485" y="235" text-anchor="middle" font-size="10" fill="#4a5568">NetworkStream.WriteAsync()</text>
  <line x1="525" y1="180" x2="485" y2="215" stroke="#38a169" stroke-width="1.5" marker-end="url(#arrowGreen)"/>
  <!-- Labels -->
  <text x="330" y="50" text-anchor="middle" font-size="9" fill="#a0aec0">← Fire-and-Forget，不阻塞读循环</text>
</svg>

- **Fire-and-Forget**：`_ = ProcessInvokeMessageAsync(...)` 不阻塞读循环
- **信号量限流**：`SemaphoreSlim(8, 64)` 防止 ThreadPool 线程因 `Send` 阻塞而过度膨胀
- **SyncContext.Send**：保证注册对象的方法在构造线程上执行

---

## 3. 核心类说明

### 3.1 `RpcServerBase`

抽象基类，提供 RPC 服务端的所有核心能力。

| 成员 | 类型 | 说明 |
|------|------|------|
| `IdentifierPattern` | `static Regex` | 对象/方法名称校验规则：`^[a-zA-Z_][a-zA-Z0-9_]*$` |
| `NewLine` | `static byte[]` | CRLF 分隔符 `{0x0D, 0x0A}` |
| `IsRunning` | `bool` | 服务是否正在运行 |
| `LocalEndPoint` | `IPEndPoint` | 监听地址 |
| `Clients` | `IReadOnlyList<TcpClient>` | 已连接客户端列表 |
| `SendBufferSize` | `int` | 发送缓冲区，默认 32KB |
| `ReceiveBufferSize` | `int` | 接收缓冲区，默认 64KB |
| `MessageDelimiter` | `byte[]` | 消息分隔符字节数组，默认为 `NewLine` |
| `MethodFilters` | `List<string>` | 禁止调用的方法列表，默认包含 `*.Dispose`、`*.Close` |
| `Start()` | 方法 | 启动服务监听 |
| `Stop()` | 方法 | 停止服务并断开所有客户端 |
| `RegisterObject(name, instance)` | 方法 | 注册可远程调用的对象 |
| `DeserializeInvokeMessage(...)` | **abstract** | 子类实现协议解析（数据字节 → `InvokeMessage`，单条请求消息） |
| `SerializeResponseMessage(...)` | **abstract** | 子类实现响应序列化（`ResponseMessage` → 数据字节，单条响应消息） |
| `ClientConnected` | 事件 | 客户端连接通知 |
| `ClientDisconnected` | 事件 | 客户端断开通知 |
| `ClientInvokeRequest` | 事件 | 客户端调用请求拦截/取消 |

### 3.2 `InvokeMessage`

客户端调用请求的数据对象。

| 属性 | 类型 | 必须 | 说明 |
|------|------|:--:|------|
| `ObjectName` | `string` | ✅ 必须 | 已注册目标对象的名称 |
| `MethodName` | `string` | ✅ 必须 | 目标方法名称 |
| `Id` | `int` | 可选 | 消息唯一标识，用于请求-响应匹配，默认 0；<br />当 `Id < 0` 时（如 0、-1）表示不进行 Id 匹配跟踪，即忽略请求消息的 Id 属性 |
| `Parameters` | `object[]` | 可选 | 方法参数列表，无参调用为 `null` |
| `ResponseMode` | `int` | 可选 | 消息的响应模式：<br />-1 表示不响应；<br />0 默认，调用异常响应、方法有返回值响应，其它(`void`)不响应；<br />1 需要响应，不关心是否有返回值 |
| `Description` | `string` | 可选 | 消息描述/注释 |
| `Timestamp` | `DateTimeOffset` | 可选 | 消息时间戳（ISO8601），默认 `UtcNow`，字符解析为 `O` 格式 |
| `Version` | `Version` | 只读 | 协议版本 2.0.0 |
| `TcpClient` | `TcpClient` | (internal) | 关联的 TCP 客户端 |
| `ClientEndPoint` | `IPEndPoint` | (internal) | 客户端远程端点 |

工厂方法：
```csharp
InvokeMessage.Create("Demo", "GetCurrentPage");                      // 无参
InvokeMessage.Create("Demo", "OpenPage", "2,\"en-US\"");             // 字符串参数
InvokeMessage.Create("Video", "Seek", new object[] { 5.6 });          // 强类型参数
```

### 3.3 `ResponseMessage`

方法调用结果的数据对象。

| 属性 | 类型 | 必须 | 说明 |
|------|------|:--:|------|
| `Code` | `int` | ✅ 必须 | 状态码：< 0 失败， ≥0 成功，=1 成功且有返回值 |
| `ObjectMethod` | `string` | ✅ 必须 |  被调用方法的完整名称 `{ObjectName}.{MethodName}` |
| `Id` | `int` | 可选 | 对应请求消息的 Id，当 `Id < 0` 时表示不进行 Id 跟踪匹配 |
| `Description` | `string` | 可选 |  结果描述或错误信息 |
| `ReturnType` | `Type` | 可选 | 返回值类型 |
| `ReturnValue` | `object` | 可选 | 返回值 |
| `Timestamp` | `DateTimeOffset` | 可选 | 响应时间戳 |
| `Version` | `Version` | 可选 | 协议版本 2.0.0 |

**状态码约定**：

| Code | 说明 |
|:----:|------|
| -20 | 处理调用消息时发生非预期异常 |
| -14 | 方法反射调用执行异常 |
| -13 | 方法参数类型转换失败 |
| -12 | 方法不存在或签名不匹配 |
| -11 | 方法被过滤/禁止调用 |
| -10 | 注册对象不存在 |
| -5 | 调用请求被事件拦截取消（ClientInvokeRequest） |
| 0 | 成功（void 方法，无返回值） |
| 1 | 成功（有返回值） |

### 3.4 `InvokeMessageEventArgs`

`ClientInvokeRequest` 事件的参数类，可通过设置 `Cancel = true` 可拦截取消本次调用；可读取 `InvokeMessage` 属性查看调用详情。

### 3.5 `RpcServer4X`

基于 XML 协议的 `RpcServerBase` 实现，可直接使用的子类。

**协议要点**：
- 默认以 CRLF 为数据边界，为一条 XML 格式消息
- `DeserializeInvokeMessage` 将一行解码为 UTF-8 字符串，解析为单条 `InvokeMessage`
- 请求格式：`<InvokeMessage ObjectName="xx" MethodName="xx" Parameters="xx" Id="xx" ResponseMode="xx" />\r\n`
- 响应格式：`<ResponseMessage Id="xx" Code="xx" ObjectMethod="xx" ReturnValue="xx" ... />\r\n`

### 3.6 `RpcClientBase`

客户端抽象基类，与 `RpcServerBase` 镜像对称设计。提供连接管理（`Connect()` / `Close()`）、环形缓冲 CRLF 数据拆分、请求/响应 Id 匹配、超时控制、自动重连等能力。<br />公共 API 包括 `InvokeFuncAsync()`（请求-响应，Func 语义）和 `InvokeActionAsync()`（单向通知，Action 语义）。

### 3.7 `RpcClient4X`

基于 XML 协议的 `RpcClientBase` 实现，与服务端 `RpcServer4X` 配套使用。

---

## 4. 数据协议与扩展

### 4.1 传输层

- **传输层**：TCP（可靠字节流）
- **数据层**：默认以 CRLF（`0x0D 0x0A`）分割数据字节
- **字符编码**：UTF-8

### 4.2 消息层协议（子类定义）

基类通过两个抽象方法支持任意消息协议：

```csharp
// 解析：数据字节(ArraySegment<byte>) → InvokeMessage（单条消息）
protected abstract InvokeMessage DeserializeInvokeMessage(ArraySegment<byte> dataLine, IPEndPoint remoteEndPoint);

// 序列化：ResponseMessage → byte[] 响应数据(数据消息)
protected abstract byte[] SerializeResponseMessage(ResponseMessage responseMessage, IPEndPoint remoteEndPoint);
```

子类只需实现这两个方法即可支持新的消息格式：

| 子类 | 消息格式 | 状态 |
|------|----------|:--:|
| `RpcServer4X` | XML（XML格式消息）| ✅ 已实现 |
| `RpcServer4J` | JSON（JSON格式消息） | 📋 规划中 |

### 4.3 XML 协议详细规范

详见 [远程过程调用(XML-RPC)消息协议.md](./远程过程调用(XML-RPC)消息协议.md)。

关键约定：
- 每条 XML 自闭合消息，以 CRLF 结尾
- 参数支持弱类型 `@Parameters` 属性（自动类型转换）
- 字符串用单/双引号包裹，数组用 `[...]`，十六进制用 `0x` 前缀
- `@ResponseMode` 控制是否响应：`-1` 不响应、`0` 默认、`1` 必须响应

---

## 5. 处理流程

### 5.1 全流程（连接 → 读取 → 数据拆分 → 消息解析 → 调用 → 响应）

<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 750 620" width="100%" height="100%" style="max-width:750px;">
  <defs>
    <marker id="aDn"  markerWidth="8" markerHeight="6" refX="8" refY="3" orient="auto"><path d="M0,0 L8,3 L0,6 Z" fill="#718096"/></marker>
    <marker id="aAcc" markerWidth="8" markerHeight="6" refX="8" refY="3" orient="auto"><path d="M0,0 L8,3 L0,6 Z" fill="#3182ce"/></marker>
    <marker id="aErr" markerWidth="8" markerHeight="6" refX="8" refY="3" orient="auto"><path d="M0,0 L8,3 L0,6 Z" fill="#e53e3e"/></marker>
  </defs>
  <!-- Phase 1: Start -->
  <rect x="250" y="5" width="200" height="20" rx="10" fill="#3182ce"/><text x="350" y="19" text-anchor="middle" font-size="11" fill="white" font-weight="bold">1. Start()</text>
  <line x1="350" y1="25" x2="350" y2="40" stroke="#3182ce" stroke-width="1.5" marker-end="url(#aDn)"/>
  <!-- Accept -->
  <rect x="150" y="42" width="400" height="32" rx="4" fill="#ebf8ff" stroke="#90cdf4" stroke-width="1.5"/>
  <text x="350" y="54" text-anchor="middle" font-size="11" fill="#2b6cb0">Task.Run → AcceptClientConnectAsync() 循环</text>
  <text x="350" y="68" text-anchor="middle" font-size="9" fill="#718096">TcpListener.AcceptClientConnectAsync()</text>
  <line x1="350" y1="74" x2="350" y2="90" stroke="#3182ce" stroke-width="1.5" marker-end="url(#aAcc)"/>
  <!-- Read -->
  <rect x="150" y="92" width="400" height="60" rx="4" fill="#f0fff4" stroke="#68d391" stroke-width="1.5"/>
  <text x="350" y="108" text-anchor="middle" font-size="11" fill="#276749" font-weight="bold">2. HandleClientSessionAsync(client)</text>
  <text x="350" y="124" text-anchor="middle" font-size="9" fill="#276749">环形缓冲 → 扫描分割符 → 提取数据字节</text>
  <text x="350" y="140" text-anchor="middle" font-size="9" fill="#276749">跳过尾部白字符 (0x00/0x20/0x09等)</text>
  <line x1="350" y1="152" x2="350" y2="168" stroke="#38a169" stroke-width="1.5" marker-end="url(#aDn)"/>
  <!-- Parse -->
  <rect x="150" y="170" width="400" height="45" rx="4" fill="#fffbeb" stroke="#d69e2e" stroke-width="1.5"/>
  <text x="350" y="186" text-anchor="middle" font-size="11" fill="#975a16" font-weight="bold">3. DeserializeInvokeMessage(行) → InvokeMessage（单条）</text>
  <text x="350" y="202" text-anchor="middle" font-size="9" fill="#718096">子类实现：数据消息，由子类解析为 InvokeMessage</text>
  <line x1="350" y1="215" x2="350" y2="232" stroke="#d69e2e" stroke-width="1.5" marker-end="url(#aDn)"/>
  <!-- Event -->
  <rect x="150" y="235" width="400" height="30" rx="4" fill="#edf2f7" stroke="#cbd5e0" stroke-width="1.5"/>
  <text x="350" y="254" text-anchor="middle" font-size="10" fill="#4a5568">触发 ClientInvokeRequest 事件（可拦截取消）</text>
  <line x1="350" y1="265" x2="350" y2="282" stroke="#718096" stroke-width="1.5" marker-end="url(#aDn)"/>
  <!-- Fire and Forget -->
  <rect x="150" y="285" width="400" height="30" rx="4" fill="#edf2f7" stroke="#cbd5e0" stroke-width="1.5"/>
  <text x="350" y="304" text-anchor="middle" font-size="10" fill="#4a5568">_ = ProcessInvokeMessageAsync(message)  ← Fire-and-Forget</text>
  <line x1="350" y1="315" x2="350" y2="332" stroke="#718096" stroke-width="1.5" marker-end="url(#aDn)"/>
  <!-- Call -->
  <rect x="100" y="335" width="500" height="105" rx="4" fill="#f0fff4" stroke="#68d391" stroke-width="2"/>
  <text x="350" y="352" text-anchor="middle" font-size="12" fill="#276749" font-weight="bold">4. ProcessInvokeMessageAsync 执行</text>
  <line x1="130" y1="360" x2="570" y2="360" stroke="#c6f6d5" stroke-width="1"/>
  <text x="130" y="376" text-anchor="start" font-size="9" fill="#276749">❶ await SemaphoreSlim.WaitAsync() — 并发限流</text>
  <text x="130" y="392" text-anchor="start" font-size="9" fill="#276749">❷ 基本检查：对象是否存在、方法是否过滤</text>
  <text x="130" y="408" text-anchor="start" font-size="9" fill="#276749">❸ 方法查找：InstanceCacheMethodInfos 缓存字典</text>
  <text x="130" y="424" text-anchor="start" font-size="9" fill="#276749">❹ 参数转换：TypeExtensions.TryConvertParameter()</text>
  <line x1="350" y1="440" x2="350" y2="458" stroke="#38a169" stroke-width="1.5" marker-end="url(#aDn)"/>
  <!-- Send -->
  <rect x="100" y="460" width="500" height="50" rx="4" fill="#fefcbf" stroke="#d69e2e" stroke-width="2"/>
  <text x="350" y="478" text-anchor="middle" font-size="11" fill="#975a16" font-weight="bold">❺ SyncContext.Send() → MethodInfo.Invoke()</text>
  <text x="350" y="498" text-anchor="middle" font-size="9" fill="#975a16">阻塞 ThreadPool 线程，在 UI 线程上同步执行目标方法</text>
  <line x1="350" y1="510" x2="350" y2="528" stroke="#d69e2e" stroke-width="1.5" marker-end="url(#aDn)"/>
  <!-- Write -->
  <rect x="150" y="531" width="400" height="30" rx="4" fill="#edf2f7" stroke="#cbd5e0" stroke-width="1.5"/>
  <text x="350" y="550" text-anchor="middle" font-size="10" fill="#4a5568">❻ WriteResponseMessageAsync() → NetworkStream</text>
  <line x1="350" y1="561" x2="350" y2="578" stroke="#718096" stroke-width="1.5" marker-end="url(#aDn)"/>
  <!-- Release -->
  <rect x="200" y="580" width="300" height="28" rx="14" fill="#38a169" opacity="0.85"/>
  <text x="350" y="598" text-anchor="middle" font-size="10" fill="white" font-weight="bold">❼ SemaphoreSlim.Release()</text>
</svg>

### 5.2 环形缓冲区（Ring Buffer）策略

<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 650 180" width="100%" height="100%" style="max-width:650px;">
  <!-- Buffer bar -->
  <rect x="20" y="50" width="600" height="35" rx="2" fill="#edf2f7" stroke="#cbd5e0" stroke-width="1.5"/>
  <!-- Consumed -->
  <rect x="20" y="50" width="140" height="35" rx="0" fill="#c6f6d5" opacity="0.7"/>
  <text x="90" y="72" text-anchor="middle" font-size="10" fill="#276749">已消费</text>
  <!-- Pending -->
  <rect x="160" y="50" width="280" height="35" rx="0" fill="#fefcbf" opacity="0.7"/>
  <text x="300" y="72" text-anchor="middle" font-size="10" fill="#975a16">待解析数据（有效载荷）</text>
  <!-- Free -->
  <rect x="440" y="50" width="180" height="35" rx="0" fill="#ebf8ff" opacity="0.7"/>
  <text x="530" y="72" text-anchor="middle" font-size="10" fill="#2b6cb0">空闲空间</text>
  <!-- Markers -->
  <line x1="20"  y1="40" x2="20"  y2="95" stroke="#4a5568" stroke-width="1"/><text x="20"  y="38" text-anchor="middle" font-size="9" fill="#4a5568">0</text>
  <line x1="160" y1="40" x2="160" y2="95" stroke="#e53e3e" stroke-width="1.5"/><text x="160" y="38" text-anchor="middle" font-size="9" fill="#e53e3e">pos</text>
  <line x1="440" y1="40" x2="440" y2="95" stroke="#e53e3e" stroke-width="1.5"/><text x="440" y="38" text-anchor="middle" font-size="9" fill="#e53e3e">len</text>
  <line x1="620" y1="40" x2="620" y2="95" stroke="#3182ce" stroke-width="1.5"/><text x="620" y="38" text-anchor="middle" font-size="9" fill="#3182ce">offset</text>
  <!-- Total label -->
  <text x="320" y="30" text-anchor="middle" font-size="10" fill="#718096">← bufferSize (ReceiveBufferSize / 2) →</text>
  <!-- Legend -->
  <rect x="20" y="105" width="12" height="12" rx="2" fill="#c6f6d5" opacity="0.7"/><text x="38" y="115" font-size="9" fill="#4a5568">已分析消费的数据</text>
  <rect x="200" y="105" width="12" height="12" rx="2" fill="#fefcbf" opacity="0.7"/><text x="218" y="115" font-size="9" fill="#4a5568">未解析的有效数据</text>
  <rect x="380" y="105" width="12" height="12" rx="2" fill="#ebf8ff" opacity="0.7"/><text x="398" y="115" font-size="9" fill="#4a5568">可写入的空闲空间</text>
  <!-- Tips -->
  <text x="320" y="140" text-anchor="middle" font-size="9" fill="#718096">三种收尾策略：(a) pos==len → 归零，(b) 空闲&lt;1/8 → 紧凑，(c) offset满 → 清空防死锁</text>
</svg>

---

## 6. 使用示例

### 6.1 服务端（使用 RpcServer4X）

```csharp
// 1. 定义可远程调用的业务对象
public class DemoController
{
    public string GetCurrentPage() => "/home";

    public void OpenPage(int pageId)
    {
        // UI 操作会通过 SyncContext.Send 安全执行
        App.Current.Dispatcher.Invoke(() => NavigateTo(pageId));
    }

    public float Seek(float position) => position * 2.0f;
}

// 2. 创建并启动服务
var server = new RpcServer4X(IPAddress.Loopback, 8080);

// 3. 注册业务对象
server.RegisterObject("Demo", new DemoController());

// 4. 可选：添加方法过滤
server.MethodFilters.Add("Demo.InternalMethod");

// 5. 可选：拦截调用请求
server.ClientInvokeRequest += (s, e) =>
{
    if (e.InvokeMessage.MethodName == "Shutdown")
    {
        e.Cancel = true; // 拒绝执行
    }
};

// 6. 启动服务
server.Start();

// 7. 停止服务
server.Stop();
```

### 6.2 客户端（使用 RpcClient4X）

```csharp
// 使用 RpcClient4X（XML 协议实现）
var client = new RpcClient4X(IPAddress.Loopback, 8080);
client.Connect();

// 请求-响应调用（Func 语义，有返回值，必须等待结果）
var response = await client.InvokeFuncAsync("Demo", "GetCurrentPage");
if (response.Code >= 0)
    Console.WriteLine(response.ReturnValue);  // "/home"

// 单向通知（Action 语义，无返回值，发射后即忘）
await client.InvokeActionAsync("Demo", "OpenPage", new object[] { 42 });

client.Close();
```

---

## 7. 关键设计决策

### 7.1 为什么使用 `SynchronizationContext.Send` 而不是 `Post`？

#### 用 `Send` 时的执行时序

<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 700 450" width="100%" height="100%" style="max-width:700px;">
  <defs>
    <marker id="saDn" markerWidth="8" markerHeight="6" refX="8" refY="3" orient="auto"><path d="M0,0 L8,3 L0,6 Z" fill="#a0aec0"/></marker>
  </defs>
  <!-- Title -->
  <text x="350" y="20" text-anchor="middle" font-size="12" font-weight="bold" fill="#4a5568">时间轴 →</text>
  <!-- ThreadPool column -->
  <rect x="15" y="35" width="310" height="15" rx="3" fill="#ebf8ff"/><text x="170" y="46" text-anchor="middle" font-size="10" fill="#2b6cb0" font-weight="bold">ThreadPool 线程</text>
  <!-- UI column -->
  <rect x="375" y="35" width="310" height="15" rx="3" fill="#f0fff4"/><text x="530" y="46" text-anchor="middle" font-size="10" fill="#276749" font-weight="bold">UI 线程（_syncContext）</text>
  <!-- Step 1 -->
  <rect x="15" y="58" width="310" height="30" rx="3" fill="#edf2f7" stroke="#cbd5e0" stroke-width="1"/>
  <text x="25" y="70" font-size="9" fill="#4a5568">ProcessInvokeMessageAsync()</text>
  <text x="25" y="82" font-size="8" fill="#a0aec0">├─ await SemaphoreSlim.WaitAsync() — 获取并发许可</text>
  <!-- Step 2 -->
  <rect x="15" y="95" width="310" height="30" rx="3" fill="#edf2f7" stroke="#cbd5e0" stroke-width="1"/>
  <text x="25" y="107" font-size="9" fill="#4a5568">├─ 基本校验：对象是否存在、方法是否过滤</text>
  <text x="25" y="119" font-size="8" fill="#a0aec0">├─ 方法查找 + 参数类型转换</text>
  <!-- Step 3 - Send -->
  <rect x="15" y="132" width="310" height="75" rx="4" fill="#fefcbf" stroke="#d69e2e" stroke-width="2"/>
  <text x="170" y="150" text-anchor="middle" font-size="9" fill="#975a16" font-weight="bold">SyncContext.Send(state => {</text>
  <text x="25" y="165" font-size="8" fill="#975a16">╔══════════════════════════════╗</text>
  <text x="25" y="178" font-size="8" fill="#975a16" font-weight="bold">║ 阻塞等待 UI 线程执行          ║</text>
  <text x="25" y="193" font-size="8" fill="#975a16">║ (当前线程挂起)              ║</text>
  <text x="25" y="205" font-size="8" fill="#975a16">╚══════════════════════════════╝</text>
  <!-- Arrow to UI -->
  <line x1="325" y1="170" x2="375" y2="170" stroke="#d69e2e" stroke-width="2" marker-end="url(#saDn)"/>
  <!-- UI step -->
  <rect x="375" y="145" width="310" height="50" rx="4" fill="#f0fff4" stroke="#68d391" stroke-width="2"/>
  <text x="530" y="165" text-anchor="middle" font-size="10" fill="#276749" font-weight="bold">MethodInfo.Invoke(obj, args)</text>
  <text x="530" y="182" text-anchor="middle" font-size="8" fill="#276749">在注册对象的构造线程上同步执行</text>
  <!-- Return arrow -->
  <line x1="375" y1="195" x2="325" y2="220" stroke="#38a169" stroke-width="2" marker-end="url(#saDn)"/>
  <text x="350" y="208" text-anchor="middle" font-size="8" fill="#38a169">结果+异常</text>
  <!-- Step 4 -->
  <rect x="15" y="230" width="310" height="30" rx="3" fill="#edf2f7" stroke="#cbd5e0" stroke-width="1"/>
  <text x="25" y="242" font-size="9" fill="#4a5568">├─ Send 返回，获得执行结果</text>
  <text x="25" y="254" font-size="8" fill="#a0aec0">├─ 构造 ResponseMessage (Code/ReturnValue)</text>
  <!-- Step 5 -->
  <rect x="15" y="265" width="310" height="30" rx="3" fill="#edf2f7" stroke="#cbd5e0" stroke-width="1"/>
  <text x="25" y="277" font-size="9" fill="#4a5568">├─ await WriteResponseMessageAsync()</text>
  <text x="25" y="289" font-size="8" fill="#a0aec0">├─ 异步写入 NetworkStream，不阻塞 UI</text>
  <!-- Step 6 -->
  <rect x="15" y="300" width="310" height="22" rx="3" fill="#38a169" opacity="0.85"/>
  <text x="170" y="315" text-anchor="middle" font-size="9" fill="white">└─ SemaphoreSlim.Release()</text>
  <!-- Key note -->
  <rect x="15" y="345" width="670" height="40" rx="4" fill="#fff5f5" stroke="#fed7d7" stroke-width="1.5"/>
  <text x="350" y="362" text-anchor="middle" font-size="10" fill="#c53030" font-weight="bold">关键特性</text>
  <text x="350" y="378" text-anchor="middle" font-size="9" fill="#c53030">Send 返回后，ResponseMessage 中的 Code、ReturnValue 等已完整填充，不存在竞态条件</text>
</svg>

#### 方案对比

| 方案 | 优点 | 缺点 | 结论 |
|------|------|------|:--:|
| `Post` (async) | 不阻塞调用线程 | 需要 `async void` lambda，异常不可捕获，Result 在响应前未填充（竞态Bug）| ❌ |
| `Send` (sync) | 调用结果立即可用，无竞态，异常可捕获 | 阻塞 ThreadPool 线程（60-100 QPS 下影响可忽略）| ✅ |

`Send` 阻塞的 ThreadPool 线程量：`100 QPS × 2ms/次 = 0.2 个等效线程`，远低于上限。加上 `SemaphoreSlim(25, 60)` 保护，不会导致 ThreadPool 饥饿。

### 7.2 为什么选择 Fire-and-Forget 而非消息队列？

| 方案 | 延迟 | 复杂度 | 背压控制 |
|------|:--:|:--:|:--:|
| 消息队列 (ConcurrentQueue + 单消费者) | 15ms+（轮询延迟） | 高 | 天然 |
| **Fire-and-Forget + SemaphoreSlim** | ~0ms | 低 | SemaphoreSlim |

Fire-and-Forget 消除了轮询延迟，信号量替代了队列的背压能力。但在同一 `NetworkStream` 上需要关注并发写入安全。

### 7.3 为什么选择环形缓冲区？

- 避免频繁分配临时 `byte[]`，减少 GC 压力
- 支持粘包/半包场景：数据字节可能跨越两次 `ReadAsync` 调用
- CRLF 扫描 + 紧凑化策略平衡了内存使用和性能

### 7.4 为什么预缓存 `MethodInfo`？

`RegisterObject` 时通过 `CacheObjectMethods` 一次性扫描所有公共方法和扩展方法，生成 `{ObjectName}.{MethodName}({ParameterSignature})` 缓存键存入 `CacheMethodInfos`。调用时直接 `TryGetValue`，避免每次调用都执行 `GetMethods()` + `GetParameters()` 的反射开销。

### 7.5 为什么禁止 `virtual` 和 `special-name` 方法？

- `virtual` 方法：反射无法安全判断子类重写行为的副作用
- `special-name` 方法：编译器生成的属性 getter/setter 等，不应被远程直接调用
- `ref` 参数方法：远程调用无法传递引用语义

---

## 附录 A：文件结构

```
SpaceCG.Standard/Net/
├── README.md                            ← Net 命名空间总览
├── RPC 服务框架.md                       ← 本文件（RpcServerBase 设计文档）
├── 远程过程调用(XML-RPC)消息协议.md       ← XML 协议规范（第三方对接参考）
├── RpcMessages.cs                        ← 请求/响应消息数据类 + 事件参数 + 消息池
├── RpcServerBase.cs                      ← RPC 服务端抽象基类
├── RpcServer4X.cs                        ← XML-RPC 服务端实现
├── RpcClientBase.cs                      ← RPC 客户端抽象基类
├── RpcClient4X.cs                        ← XML-RPC 客户端实现
└── AutoReconnectTcpClient.cs             ← 独立 TCP 客户端（自动重连）
```

## 附录 B：命名约定

| 约定 | 示例 |
|------|------|
| 注册对象名 | `Demo`、`Window`、`Video` 等简短 PascalCase 名称 |
| 方法名称 | `LoadItem(int)`、`HomeItem()`、`NextItem()`、`PrevItem`、`PlayPause()`、`LanguageChange()`、`LanguageChange(string)`、 `int GetItem()`、`Seek`、`VolumeUp(float)` 等等 |
| 方法缓存键 | `{ObjectName}.{MethodName}({ParameterSignature})`; `SVT` 表示 String&Value Type，`[]`表示集合数据；签名示例：`Video.Seek(SVT)`、`Video.Setting(SVT,SVT)`、`Demo.SetColor([SVT])`、`Demo.SetColors([[SVT]])` |
| 文本参数 | 只支持字符、值类型，集合类型中的元素也只支持字条类型、值类型，如 `12`、`[#FF00FF00,#FFFF00FF]`、`[[1,2,3],[4,5,6]],'hello',12` |

---

> 文档版本：v2.2  |  最后更新：2026-07-14  |  维护：SpaceCG 团队
