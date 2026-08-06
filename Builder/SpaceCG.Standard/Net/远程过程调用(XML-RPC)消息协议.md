# 远程过程调用(XML-RPC)消息协议 v2.0

>
> 本文档描述 `XML-RPC` 服务的 XML 消息协议，第三方开发人员可依据本文档实现兼容的 RPC 客户端。
>
> `RpcServer4X` 是 SpaceCG.Net RPC 框架中基于 XML 协议的服务端实现，继承自 `RpcServerBase`。

---

| 发布版本 | 发布日期 | 说明 |
|:---|:---|:----|
| 1.0 | 2023-11 | 初版 |
| 1.1 | 2025-04 | 增加响应超时状态，细节优化 |
| 1.2 | 2026-06 | 优化协议，移除多消息 InvokeMessages 格式 |
| 2.0 | 2026-07 | **重构**：以 XML 元素结束标记 `/>` 为默认消息分隔符（兼容早期版本），简化协议实现，XML 不支持子节点，优化缓存，提高序列与反序列性能 |

---

## 1. 协议总览

### 1.1 关键约定

- 默认以 XML 元素结束标记 **`/>`**（`0x2F 0x3E`）为消息分隔符，服务端每次从 TCP 流中扫描到 `/>` 即视为一条完整消息的边界
- 每个 XML 自闭合元素对应一条消息，**不允许有子节点**，减少解析复杂度与数据长度
- 每条消息元素对应一次独立的方法调用请求
- 消息尾部附带 `\r\n`（CRLF）换行，用于提高可读性，**不作为分隔符使用**

> **关于分隔符的说明**：`RpcServer4X` 继承自 `RpcServerBase`，基类默认使用 CRLF（`0x0D 0x0A`）作为消息分隔符。为兼容 XML-RPC 早期版本，`RpcServer4X` 的构造函数参数 `useLegacyDelimiter` 默认为 `true`，即使用 `/>`（`XmlTerminate`）作为分隔符；设为 `false` 时使用 CRLF，与基类行为一致。第三方客户端实现时，需根据服务端的 `useLegacyDelimiter` 配置选择对应的分隔符策略。

### 1.2 通信模式

| 项目 | 说明 |
|------|------|
| 传输协议 | TCP（可靠字节流） |
| 数据定界 | 默认 `/>`（`0x2F 0x3E`），可通过构造函数 `useLegacyDelimiter` 参数切换为 CRLF |
| 消息格式 | XML 自闭合元素，以 `/>` 结束，尾部附带 `\r\n` 换行 |
| 字符编码 | UTF-8 |
| 连接模式 | 长连接，单连接可连续发送多条消息 |
| 调用模式 | 请求-响应（异步），可通过 `ResponseMode` 控制是否响应 |

### 1.3 消息示例

```
<InvokeMessage ObjectName="Demo" MethodName="Show" />\r\n
```

> 以上为一个完整的 XML 消息：以 `/>` 标记消息结束边界，尾部 `\r\n` 为换行符（提高可读性，非分隔符）。

---

## 2. 请求消息格式

### 2.1 基本格式

```
<InvokeMessage 属性列表 />\r\n
```

消息为 **XML 自闭合元素**，以 `/>` 标记结束，尾部附带 `\r\n` 换行。**不支持**、**不允许** 有子节点。

### 2.2 属性定义

| 属性 | 类型 | 必须 | 说明 |
|------|------|:--:|------|
| `ObjectName` | String | **是** | 目标对象名称，需符合命名规则 `^[a-zA-Z_][a-zA-Z0-9_]*$` |
| `MethodName` | String | **是** | 目标方法名称，需符合命名规则 `^[a-zA-Z_][a-zA-Z0-9_]*$` |
| `Id` | Int32 | 否 | 消息标识，用于请求与响应的匹配。默认值为 `0` |
| `Parameters` | String | 否 | 方法参数，弱类型格式（见 §2.3）。无参调用可省略 |
| `ResponseMode` | Int32 | 否 | 响应策略：`-1`=不响应；`0`=默认，异常响应，有返回值响应；`1`=必须响应。默认为 `0` |
| `Description` | String | 否 | 消息注释或描述信息 |
| `Timestamp` | DateTimeOffset | 否 | 消息时间戳，使用 ISO8601 格式（如 `2026-07-15T06:36:15.7375595+00:00`） |

### 2.3 参数传递方式 `Parameters` 属性（弱类型）

多个参数使用英文逗号 `,` 分隔，参数只支基础类型 `值类型`、`字符类型` 和简单的 `集合类型`(集合元素必须是`值类型`或`字符类型`)，解析规则如下：

| 数据类型 | 格式 | 示例 |
|------|------|------|
| 整数 | 直接书写 | `12` |
| 浮点数 | 直接书写 | `5.6` |
| 布尔 | `true` / `false` | `true` |
| 字节/十六进制 | `0x` 前缀 | `0xFF`、`0x0A`、`0xFF00FF00` |
| 集合/数组 | `[]` 包裹，逗号分隔 | `[1,2,3]`、`[0x08,0x09,0x0A]`、`[[#FFFF0000,#FF00FFFF],[#FF0000FF,#FF0F0F0F]]` |

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
| `0` | **默认规则** | `void` 方法执行成功时不响应；有返回值 或 调用异常时响应 |
| `1` | **必须响应** | 无论调用结果如何都返回 `ResponseMessage` |

---

## 3. 响应消息格式

### 3.1 基本格式

```
<ResponseMessage 属性列表 />\r\n
```

响应同样为 XML 自闭合元素，以 `/>` 标记结束，尾部附带 `\r\n` 换行。

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
| `Timestamp` | DateTimeOffset | 否 | 响应生成时间戳（ISO8601） |

### 3.3 状态码（Code）

#### 服务端状态码（客户端收到的）

| Code | 含义 | 说明 |
|:----:|------|------|
| **0** | 成功，无返回值 | 方法返回类型为 `void` |
| **1** | 成功，有返回值 | 返回值见 `ReturnValue` / `ReturnType` |
| **-5** | 调用请求被拦截取消 | 服务端 ClientInvokeRequest 事件处理中设置了 Cancel=true |
| **-10** | 目标对象未注册 | 服务端未找到 ObjectName 对应的注册对象 |
| **-11** | 方法被禁止调用 | 方法名匹配了服务端配置的过滤规则（MethodFilters） |
| **-12** | 方法不存在 | 方法名或参数签名不匹配任何已注册方法 |
| **-13** | 参数转换失败 | 传入的参数无法转换为目标方法要求的类型 |
| **-14** | 方法执行异常 | 方法在服务端执行过程中抛出异常（InnerException 信息见 Description） |
| **-20** | 内部处理异常 | 服务端处理调用消息时发生非预期异常 |

#### 客户端本地状态码（客户端自生成，不经过网络传输）

| Code | 含义 | 说明 |
|:----:|------|------|
| **-96** | 消息 Id 冲突 | 待响应字典中已存在相同 Id |
| **-97** | 响应超时 | 在指定超时时间内未收到服务端响应 |
| **-100** | 客户端未连接 | 调用 InvokeFuncAsync 时未连接到服务端 |
| **-101** | 连接已关闭 | 序列化后发送前检测到连接断开 |
| **-102** | 连接关闭 | 循环接收时检测到断开，取消所有待响应调用 |
| **-105** | 消息序列化失败 | 客户端序列化 InvokeMessage 时发生异常 |
| **-106** | 序列化结果为空 | 序列化后的字节数组为空 |
| **-107** | 写入失败 | 发送消息到网络流时发生异常 |

---

## 4. 请求-响应 消息示例

### 4.1 无参调用

```
→ <InvokeMessage ObjectName="Demo" MethodName="GetCurrentPage" Id="1" ResponseMode="1" />\r\n
← <ResponseMessage Id="1" Code="1" ObjectMethod="Demo.GetCurrentPage" ReturnValue="1" ReturnType="System.Int32" Description="Success" Version="2.0.0" Timestamp="2026-07-11T12:00:01Z" />\r\n
```

### 4.2 带参数调用

```
→ <InvokeMessage ObjectName="Video" MethodName="Seek" Id="2" Parameters="0.6" ResponseMode="1" />\r\n
← <ResponseMessage Id="2" Code="0" ObjectMethod="Video.Seek" ReturnType="Void" Description="Success" Version="2.0.0" Timestamp="2026-07-11T12:00:02Z" />\r\n
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

> 每个 XML 元素为一条独立的消息，服务端依次处理异步响应。多条消息可一次发送，也可分次发送。

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
| `......` | `[......]` | 其它集合接口类型 |

---

## 6. 集成开发注意事项

### 6.1 客户端实现要点

1. **数据定界**：默认以 XML 元素结束标记 `/>`（`0x2F 0x3E`）拆分消息，注意尾部 `\r\n` 为换行符，**不作为分隔符**。若服务端以 `useLegacyDelimiter=false` 启动，则使用 CRLF（`0x0D 0x0A`）作为分隔符。TCP 粘包/半包需自行处理，建议使用缓冲区拼接。**每个 XML 自闭合元素对应一条消息**。
2. **字符编码**：统一使用 UTF-8 编解码。
3. **响应匹配**：通过 `Id` 字段进行 请求-响应 配对。建议客户端生成递增的正整数 `Id`。
4. **超时处理**：建议设置合理的读写超时（如 3 秒），超时后客户端按本地 Code `-97`（响应超时）处理。
5. **连接保活**：TCP 长连接，闲置时无需发送心跳，服务端不会主动断开。
6. **心跳策略**：建议每隔 3 秒调用一次服务端设计的心跳函数，消息响应模式 `ResponseMode` 设为 `1`。
7. **重连策略**：建议在连接断开后按指数退避重连，避免频繁重连，例如：等待 3 秒后重连。

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
| 字符串长度 | `Parameters` 内字符串建议 ≤ 256 字符 |
| 不支持 ref/out | 远程调用无法传递引用语义参数 |
| 不支持重载歧义 | 若方法名相同、参数类型签名也相同，仅匹配第一个 |
| 不支持属性/索引器 | 仅支持方法调用，不支持属性 get/set |

### 6.4 安全与加密

当前 `RpcServer4X` 为明码传输。如需安全传输，请在应用层对数据自行实现加密包装：

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
if (response.Code >= 1)
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

    /// <summary>发送一条调用消息（每个 XML 元素一条消息）</summary>
    public async Task SendAsync(string objectName, string methodName, string parameters = null, int id = 0, int responseMode = 0)
    {
        var sb = new StringBuilder();
        sb.Append($"<InvokeMessage ObjectName=\"{objectName}\" MethodName=\"{methodName}\"");
        if (id != 0) sb.Append($" Id=\"{id}\"");
        if (parameters != null) sb.Append($" Parameters=\"{parameters}\"");
        if (responseMode != 0) sb.Append($" ResponseMode=\"{responseMode}\"");
        sb.AppendLine(" />");

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

## 附录 A：注册对象与方法名称命名约定

> 本附录定义的方法命名约定将逐步演进为协议规范要求，第三方客户端实现应遵循以下约定。

### A.1 注册对象名

注册对象名用于客户端通过 `ObjectName` 属性定位服务端已注册的实例。

| 约定 | 说明 | 示例 |
|------|------|------|
| 命名格式 | `^[a-zA-Z_][a-zA-Z0-9_]*$`，字母或下划线开头，后跟字母、数字、下划线 | `Demo`、`Window`、`Video` |
| 大小写 | PascalCase（首字母大写） | `MediaPlayer`、`SceneManager` |
| 长度 | 建议 ≤ 32 字符 | `Config` 优于 `AppConfigurationManager` |

### A.2 方法命名约定

| 约定 | 说明 | 示例 |
|------|------|------|
| 命名格式 | `^[a-zA-Z_][a-zA-Z0-9_]*$`，字母或下划线开头 | `LoadItem`、`HomeItem` |
| 大小写 | PascalCase（首字母大写） | `Seek`、`PlayPause`、`VolumeUp` |
| 长度 | 建议 ≤ 64 字符 | — |

### A.3 常用方法名称约定（协议规范要求）

以下方法名称为推荐约定，建议保持一致以降低对接成本。

| 方法签名 | 参数 | 说明 |
|------|------|------|
| `LoadItem(int tag)` | `tag`：项/页标签（从 0 开始，0 表示默认主页/'屏保内容'） | 加载指定的内容项/页 |
| `NextItem()` | 无 | 切换到下一项/页 |
| `PrevItem()` | 无 | 切换到上一项/页 |
| `HomeItem()` | 无 | 回到第一项/首页 |
| `BackItem()` | 无 | 回到上一项/级、返回 |
| `PlayPause()` | 无 | 播放/暂停切换 |
| `Replay()` | 无 | 从头开始播放 |
| `LanguageChange()` | 无 | 双语/多语自动语言切换 |
| `LanguageChange(string lang)` | `lang`：语言代码（如 `zh-CN`、`en-US`） | 切换显示指定语言 |
| `Seek(double position)` | `position`：播放位置（0.0 ~ 1.0） | 跳转到指定播放位置 |
| `VolumeUp(double step)` | `step`：音量增量 | 调整音量 |
| `VolumeDown(double step)` | `step`：音量减量 | 调整音量 |
| `AdjustVolume(double volume)` | `volume`：音量 | 调整音量 |

### A.4 参数格式约定

方法参数通过 `Parameters` 属性以字符串形式传递，仅支持值类型和简单的集合类型。

| 类型 | 格式 | 示例 |
|------|------|------|
| 整数 | 直接书写 | `12` |
| 浮点数 | 直接书写 | `5.6`、`3.14` |
| 布尔 | `true` / `false` | `true` |
| 字符串 | 直接书写（不建议包含 `,`、`[`、`]` 等特殊字符） | `hello`、`zh-CN`、`\'hi, code\'` |
| 十六进制 | `0x` 前缀支持 | `0xFF`、`0xFF00FF00` |
| 一维数组 | `[]` 包裹，逗号分隔 | `[1,2,3]`、`[0x08,0x09,0x0A]` |
| 嵌套数组 | `[[]]` 包裹 | `[[1,2],[3,4]]` |
| 混合参数 | 逗号分隔多种类型 | `12,hello,[0x01,0xA0]` |

> **注意**：字符串参数不建议包含 `,`、`[`、`]` 等协议保留字符。如需传递含特殊字符的内容，建议通过业务层转义或改用其他传递方式，或者使用 `'` 包裹。

### A.5 方法缓存键

`RegisterObject` 时通过反射扫描生成缓存键，格式为 `{ObjectName}.{MethodName}({ParameterSignature})`。

| 签名简写 | 含义 | 示例 |
|------|------|------|
| `()` | 无参数 | `Demo.GetCurrentPage()` |
| `(SVT)` | 单个 String 或 ValueType 参数 | `Video.Seek(SVT)` |
| `(SVT,SVT)` | 两个参数 | `Video.Setting(SVT,SVT)` |
| `([SVT])` | 一维数组参数 | `Demo.SetColor([SVT])` |
| `([[SVT]])` | 二维数组/嵌套集合参数 | `Demo.SetColors([[SVT]])` |

---

> 文档版本：v2.2  |  最后更新：2026-08-06  |  维护：SpaceCG 团队
