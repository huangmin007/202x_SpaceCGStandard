# 远程过程调用(XML-RPC)消息协议 v3.0

> **文档定位**：面向第三方团队或公司的通信接口参考文档。
>
> 本文档描述 `RpcServer4X` 服务的 XML 消息协议，第三方开发人员可依据本文档实现兼容的 RPC 客户端。
>
> `RpcServer4X` 是 SpaceCG.Net RPC 框架中基于 XML 协议的服务端实现，继承自 `RpcServerBase`（一行一条消息）。

---

| 发布版本 | 发布日期 | 说明 |
|:---|:---|:----|
| 1.0 | 2023-11 | 初版 |
| 1.1 | 2025-04 | 增加响应超时状态，细节优化 |
| 1.2 | 2026-06 | 优化协议，移除多消息 InvokeMessages 格式 |
| 2.0 | 2026-07 | 重构：数据行层与消息层分离 |
| 3.0 | 2026-07 | **突破性变更**：一行一消息，客户端与服务端保持对称一致，简化协议实现 |

---

## 1. 协议总览

### 1.1 分层模型

```
┌─────────────────────────────────┐
│         消息层（Message）        │  ← XML 自闭合元素（以 XML '/>' 结束，不可以有子节点）
│   每行一条消息                   │
├─────────────────────────────────┤
│          数据行层（Line）        │  ← 以 CRLF（\r\n, 0x0D 0x0A）为行结束符
│     一行一条消息                 │
├─────────────────────────────────┤
│        传输层（Transport）       │  ← TCP 字节流，UTF-8 编码
└─────────────────────────────────┘
```

**关键约定**：

- 服务端以 **CRLF** 为行边界，每次从 TCP 流中读取一个完整的数据行
- 一行对应一条 XML 自闭合消息（v3.0 起，一行一消息）
- XML 自闭合元素不允许有子节点
- 每条消息元素对应一次独立的方法调用请求

### 1.2 通信模式

| 项目 | 说明 |
|------|------|
| 传输协议 | TCP（可靠字节流） |
| 数据行定界 | CRLF `\r\n`（`0x0D 0x0A`）|
| 消息格式 | XML 自闭合元素（以 `/>` 结束），每行一条消息 |
| 字符编码 | UTF-8 |
| 连接模式 | 长连接，单连接可发送多行 |
| 调用模式 | 请求-响应（同步），可通过 `ResponseMode` 控制是否响应 |

### 1.3 消息行示例

```
<InvokeMessage ObjectName="Demo" MethodName="Show" />
```

> 以上为一个完整的数据行（以 CRLF 结尾），行内包含一条调用消息。

---

## 2. 请求消息格式

### 2.1 基本格式

```
<InvokeMessage 属性列表 />
```

消息为 **XML 自闭合元素**，以 `/>` 结束。**不支持**传统的成对开始/结束标签。

### 2.2 属性定义

| 属性 | 类型 | 必须 | 说明 |
|------|------|:--:|------|
| `ObjectName` | String | **是** | 目标对象名称，需符合命名规则 `^[a-zA-Z_][a-zA-Z0-9_]*$` |
| `MethodName` | String | **是** | 目标方法名称，需符合命名规则 `^[a-zA-Z_][a-zA-Z0-9_]*$` |
| `Id` | Int32 | 否 | 消息标识，用于请求与响应的匹配。默认值为 `0` |
| `Parameters` | String | 否 | 方法参数，弱类型格式（见 §2.3）。无参调用可省略 |
| `ResponseMode` | Int32 | 否 | 响应策略：`-1`=不响应，`0`=默认，`1`=必须响应。默认为 `0` |
| `Description` | String | 否 | 消息注释或描述信息 |
| `Timestamp` | DateTime | 否 | 消息时间戳，建议使用 ISO8601 格式（如 `2026-07-11T12:00:00Z`） |

### 2.3 参数传递方式 `@Parameters` 属性（弱类型）

多个参数使用英文逗号 `,` 分隔，参数只支持 `值类型`、简单的 `字符类型` 和简单的 `集合类型`(元素必须是`值类型`或`字符类型`)，解析规则如下：

| 数据类型 | 格式 | 示例 |
|------|------|------|
| 整数 | 直接书写 | `12` |
| 浮点数 | 直接书写 | `5.6` |
| 字符串 | 单引号 `'` 或双引号 `"` 包裹 | `'hello'` 或 `"world"` |
| 布尔 | `true` / `false` | `true` |
| 字节/十六进制 | `0x` 前缀 | `0xFF`、`0x0A` |
| 数组 | `[]` 包裹，逗号分隔 | `[1,2,3]` `[[#FFFF0000,#FF00FFFF],[#FF0000FF,#FF0F0F0F]]` |
| 字节数组 | `[]` 包裹，元素使用 `0x` 前缀 | `[0x08,0x09,0x0A]` |

示例：
```
Parameters="12,play,1024,[0x01,0xA0,0xAA],'this is string'"
Parameters="[#FFFF0000,#FF00FF00,#FF0000FF],[12,30]"
```

> **注意**：字符串内容不应包含未转义的逗号、引号，长度建议控制在 256 字符以内。

### 2.4 响应策略（ResponseMode）

`ResponseMode` 控制服务端是否回复响应消息：

| 值 | 行为 | 适用场景 |
|:--:|------|------|
| `-1` | **不响应** | 单向通知（fire-and-forget），如日志上报、状态同步 |
| `0` | **默认规则** | `void` 方法且执行成功时不响应；有返回值或失败时响应 |
| `1` | **必须响应** | 无论调用结果如何都返回 `ResponseMessage` |

---

## 3. 响应消息格式

### 3.1 基本格式

```
<ResponseMessage 属性列表 />
```

响应同样为 XML 自闭合元素，以 `/>` 结束，每条响应消息后紧跟 CRLF。一行一条响应。

### 3.2 属性定义

| 属性 | 类型 | 必须 | 说明 |
|------|------|:--:|------|
| `Id` | Int32 | 否 | 对应请求消息的 `Id`，用于匹配 |
| `Code` | Int32 | **是** | 执行状态码，见 §3.3 |
| `ObjectMethod` | String | **是** | 被调用方法的完整名称，格式：`{ObjectName}.{MethodName}` |
| `ReturnValue` | String | 否 | 返回值，`void` 方法不返回此属性 |
| `ReturnType` | String | 否 | 返回值类型名称（如 `System.Float`） |
| `Description` | String | 否 | 失败时为错误描述信息 |
| `Version` | String | 否 | 协议版本号 |
| `Timestamp` | DateTime | 否 | 响应生成时间戳（ISO8601） |

### 3.3 状态码（Code）

#### 服务端状态码（客户端收到的）

| Code | 含义 | 说明 |
|:----:|------|------|
| **0** | 成功，无返回值 | 方法返回类型为 `void` |
| **1** | 成功，有返回值 | 返回值见 `ReturnValue` / `ReturnType` |
| **-3** | 调用请求被拦截取消 | 服务端 ClientInvokeRequest 事件处理中设置了 Cancel=true |
| **-10** | 目标对象未注册 | 服务端未找到 ObjectName 对应的注册对象 |
| **-11** | 方法被禁止调用 | 方法名匹配了服务端配置的过滤规则（MethodFilters） |
| **-12** | 方法不存在 | 方法名或参数签名不匹配任何已注册方法 |
| **-13** | 参数转换失败 | 传入的参数无法转换为目标方法要求的类型 |
| **-14** | 方法执行异常 | 方法在服务端执行过程中抛出异常（InnerException 信息见 Description） |
| **-15** | 内部处理异常 | 服务端处理调用消息时发生非预期异常 |

#### 客户端本地状态码（客户端自生成，不经过网络传输）

| Code | 含义 | 说明 |
|:----:|------|------|
| **-96** | 消息 Id 冲突 | 待响应字典中已存在相同 Id |
| **-97** | 响应超时 | 在指定超时时间内未收到服务端响应 |
| **-100** | 客户端未连接 | 调用 InvokeFuncAsync 时未连接到服务端 |
| **-101** | 连接已关闭 | 序列化后发送前检测到连接断开 |
| **-102** | 连接关闭 | 接收循环断开，取消所有待响应调用 |
| **-105** | 消息序列化失败 | 客户端序列化 InvokeMessage 时发生异常 |
| **-106** | 序列化结果为空 | 序列化后的字节数组为空 |
| **-107** | 写入失败 | 发送消息到网络流时发生异常 |

---

## 4. 请求-响应 消息示例

### 4.1 无参调用

```
→ <InvokeMessage ObjectName="Demo" MethodName="GetCurrentPage" Id="1" ResponseMode="1" />\r\n
← <ResponseMessage Id="1" Code="1" ObjectMethod="Demo.GetCurrentPage" ReturnValue="/home" ReturnType="String" Description="OK" Version="2.0.0" Timestamp="2026-07-11T12:00:01Z" />\r\n
```

### 4.2 带参数调用

```
→ <InvokeMessage ObjectName="Video" MethodName="Seek" Id="2" Parameters="0.6" ResponseMode="1" />\r\n
← <ResponseMessage Id="2" Code="0" ObjectMethod="Video.Seek" ReturnType="Void" Description="OK" Version="2.0.0" Timestamp="2026-07-11T12:00:02Z" />\r\n
```

### 4.3 单向通知（无响应）

```
→ <InvokeMessage ObjectName="Logger" MethodName="Log" Parameters="'app started'" ResponseMode="-1" />\r\n
← （无响应）
```

### 4.4 错误场景

```
→ <InvokeMessage ObjectName="Unknown" MethodName="DoSomething" Id="3" />\r\n
← <ResponseMessage Id="3" Code="-10" ObjectMethod="Unknown.DoSomething" Description="Object (Unknown) not register" Version="2.0.0" Timestamp="..." />\r\n
```

### 4.5 连续多条消息

```
→ <InvokeMessage ObjectName="Demo" MethodName="Show" Id="10" ResponseMode="1" />
→ <InvokeMessage ObjectName="Video" MethodName="Play" Id="11" ResponseMode="1" />
← <ResponseMessage Id="10" Code="0" ObjectMethod="Demo.Show" ... />
← <ResponseMessage Id="11" Code="0" ObjectMethod="Video.Play" ... />
```

> 每行为一条独立的消息，服务端依次处理并响应。多条消息需分多行发送。

---

## 5. 数据类型映射参考

| .NET 类型 | `@Parameters` 表示示例 | 说明 |
|------|------|------|
| `System.Int32` | `42` | 整数 |
| `System.Single` | `3.14` | 单精度浮点 |
| `System.Double` | `2.71828` | 双精度浮点 |
| `System.String` | `'hello'` 或 `"world"` | 需引号包裹 |
| `System.Boolean` | `true` / `false` | 小写 |
| `System.Byte` | `0xFF` | 十六进制 |
| `System.Int32[]` | `[1,2,3]` | 数组 |
| `System.Byte[]` | `[0x01,0x02,0x03]` | 字节数组 |
| `System.Enum` | `'OptionA'` | 枚举值按名称解析 |

---

## 6. 集成开发注意事项

### 6.1 客户端实现要点

1. **数据行定界**：以 `CRLF`(`0D0A`，字符是`\r\n`) 拆分数据行。TCP 粘包/半包需自行处理，建议使用缓冲区拼接。**每行一条消息**。
2. **字符编码**：统一使用 UTF-8 编解码。
3. **响应匹配**：通过 `Id` 字段进行 请求-响应 配对。建议客户端生成递增的正整数 `Id`。
4. **超时处理**：建议设置合理的读写超时（如 3 秒），超时后客户端按本地 Code `-97`（响应超时）处理。
5. **连接保活**：TCP 长连接，闲置时无需发送心跳，服务端不会主动断开。
6. **心跳策略**：建议每隔 3 秒调用一次服务端设计的心跳函数，消息响应模式 `ResponseMode` 设为 `1`。
7. **重连策略**：建议在连接断开后按指数退避重连，避免频繁重连。

### 6.2 支持的平台/语言

协议基于 TCP + UTF-8 + XML，任何支持这些基础能力的语言均可实现客户端：

- C# (.NET / Unity3D)
- Java / Kotlin
- Python
- JavaScript / Node.js
- C++（libxml2/tinyxml2 等）

### 6.3 限制与约束

| 限制项 | 说明 |
|------|------|
| 方法名格式 | 仅允许 `^[a-zA-Z_][a-zA-Z0-9_]*$`，不能含空格、中文、特殊符号 |
| 参数数量 | 无硬性上限，但建议 ≤ 16 个 |
| 字符串长度 | `@Parameters` 内字符串建议 ≤ 256 字符 |
| 不支持 ref/out | 远程调用无法传递引用语义参数 |
| 不支持重载歧义 | 若方法名相同、参数类型签名也相同，仅匹配第一个 |
| 不支持属性/索引器 | 仅支持方法调用，不支持属性 get/set |

### 6.4 安全与加密

当前 `RpcServer4X` 为明码传输。如需安全传输，请在应用层自行实现加密包装：

| 等级 | 说明 | 参考方案 |
|:--:|------|------|
| 0 | 明码（当前） | — |
| 1 | 二次编码 | Base64 编码传输 |
| 2 | 对称加密 | AES 加密后传输 |
| 3 | 非对称加密 | RSA + AES 混合加密 |

---

## 7. 客户端参考实现（C#）

### 7.1 使用 SpaceCG.Net 内置客户端（推荐）

```csharp
using SpaceCG.Net;
using System.Net;

// 使用 RpcClient4X（XML 协议实现）
var client = new RpcClient4X(IPAddress.Loopback, 8080);
client.Connect();

// 请求-响应调用（Func 语义，有返回值，必须等待结果）
var response = await client.InvokeFuncAsync("Demo", "GetCurrentPage");
if (response.Code >= 0)
    Console.WriteLine(response.ReturnValue);

// 带参调用
var response2 = await client.InvokeFuncAsync("Video", "Seek", new object[] { 5.6f });

// 单向通知（Action 语义，无返回值，发射后即忘）
await client.InvokeActionAsync("Logger", "Log", new object[] { "'app started'" });

client.Close();
```

### 7.2 手动实现简易客户端

```csharp
using System;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

public class SimpleRPCClient
{
    private TcpClient _client;
    private NetworkStream _stream;

    public async Task ConnectAsync(string host, int port)
    {
        _client = new TcpClient();
        await _client.ConnectAsync(host, port);
        _stream = _client.GetStream();
    }

    /// <summary>发送一条调用消息（每行一条消息）</summary>
    public async Task SendAsync(string objectName, string methodName,
        string parameters = null, int id = 0, int responseMode = 0)
    {
        var sb = new StringBuilder();
        sb.Append($"<InvokeMessage ObjectName=\"{objectName}\" MethodName=\"{methodName}\"");
        if (id != 0) sb.Append($" Id=\"{id}\"");
        if (parameters != null) sb.Append($" Parameters=\"{parameters}\"");
        if (responseMode != 0) sb.Append($" ResponseMode=\"{responseMode}\"");
        sb.Append(" />\r\n");

        var bytes = Encoding.UTF8.GetBytes(sb.ToString());
        await _stream.WriteAsync(bytes, 0, bytes.Length);
        await _stream.FlushAsync();
    }

    /// <summary>读取响应（简化版，仅读一行）</summary>
    public async Task<string> ReadResponseAsync()
    {
        var buffer = new byte[4096];
        var sb = new StringBuilder();
        while (true)
        {
            var count = await _stream.ReadAsync(buffer, 0, buffer.Length);
            if (count == 0) break;
            var text = Encoding.UTF8.GetString(buffer, 0, count);
            sb.Append(text);
            if (text.Contains("\n")) break;
        }
        return sb.ToString();
    }

    public void Close()
    {
        _stream?.Dispose();
        _client?.Dispose();
    }
}

// 使用示例
var client = new SimpleRPCClient();
await client.ConnectAsync("127.0.0.1", 8080);

// 无参调用
await client.SendAsync("Demo", "GetCurrentPage", id: 1, responseMode: 1);

// 带参调用
await client.SendAsync("Video", "Seek", parameters: "5.6", id: 2, responseMode: 1);

// 单向通知
await client.SendAsync("Logger", "Log", parameters: "'app started'", responseMode: -1);

var response = await client.ReadResponseAsync();
Console.WriteLine(response);

client.Close();
```

---

> 文档版本：v3.1  |  最后更新：2026-07-14  |  维护：SpaceCG 团队
