# SpaceCG.Extensions

`SpaceCG.Extensions` 命名空间提供对 .NET 标准类型的一系列扩展方法，涵盖数组操作、文件管理、HTTP 客户端、反射调用、数学计算、网络工具、路径处理、字符串解析、TCP 连接检测、类型转换、XML 配置等领域。

- **目标框架**：.NET Framework 4.8（C# 7.3）
- **设计原则**：无状态纯函数、零分配高性能路径、Try 模式错误处理

---

## 类列表

| 类 | 类型 | 说明 |
|------|------|------|
| [`ArrayExtensions`](#arrayextensions) | static partial class | 字节数组与泛型数组的模式搜索扩展 |
| [`FileExtensions`](#fileextensions) | static partial class | 文件数量保留、正则/扩展名搜索、系列文件分组 |
| [`HttpClientExtensions`](#httpclientextensions) | static partial class | 带进度的文件上传/下载、MIME 类型查询 |
| [`InstanceExtensions`](#instanceextensions) | static partial class | 反射：动态获取/设置字段属性、事件清理、方法调用 |
| [`MathExtensions`](#mathextensions) | static partial class | 数值映射、Clamp、位/字节反转 |
| [`NetworkExtensions`](#networkextensions) | static partial class | 本机 IPv4 地址、子网掩码、广播地址计算 |
| [`PathExtensions`](#pathextensions) | static partial class | 路径分隔符检测、相对路径计算 |
| [`StringExtensions`](#stringextensions) | static partial class | XML 转义、十六进制转换、参数文本解析、类型转换 |
| [`SocketExtensions`](#socketextensions) | static partial class | TCP/UDP/串口的连接管理、数据接收与协议解析 |
| [`TypeExtensions`](#typeextensions) | static partial class | 类型签名生成、参数转换流水线、类型查找 |
| [`XElementExtensions`](#xelementextensions) | static partial class | XML 模板引用替换、字典占位符、配置加载 |

---

## `ArrayExtensions`

字节数组的**模式搜索**（Pattern Search / IndexOf）与**高效内存比较**（SequenceEqual）扩展。

### 核心方法

| 方法 | 说明 |
|------|------|
| `ToHexString(this IReadOnlyList<byte>, int count, string format)` | 字节集合转格式化十六进制字符串（调试用，非高性能路径） |
| `IndexOf(this byte[], byte[], int, int)` | 在字节数组中查找模式首次出现位置，头尾双过滤+逐字节比对 |
| `IndexOf(this byte[], byte[])` | 简化重载，搜索整个数组 |
| `IndexOf(this ArraySegment<byte>, byte[], int, int)` | ArraySegment 版本，返回相对于视图起始的偏移索引 |
| `IndexOf(this ArraySegment<byte>, byte[])` | 简化重载 |
| `IndexOf<T>(this T[], T[], int, int) where T : IEquatable<T>` | 泛型版本，适用于值类型数组的模式搜索 |
| `SequenceEqual(this byte[], byte[])` | 比较两个字节数组的全部内容是否相等（扩展方法版本） |
| `SequenceEqual(this byte[], int, int, byte[], int, int)` | 比较两个字节数组中指定区域的内容是否相等 |
| `SequenceEqual(byte*, int, int, byte*, int, int)` | unsafe 指针版本，带偏移和长度的内存区域比较 |
| `SequenceEqual(byte*, byte*, int)` | unsafe 指针版本，直接比较两块内存区域 |

### 使用示例

```csharp
using SpaceCG.Extensions;

byte[] data = { 0x01, 0x02, 0x03, 0x04, 0x05, 0x02, 0x03, 0x06 };

// 字节模式搜索
int pos = data.IndexOf(new byte[] { 0x02, 0x03 });          // 返回 1
int pos2 = data.IndexOf(new byte[] { 0x02, 0x03 }, 3, 5);   // 返回 5

// ArraySegment 搜索
var seg = new ArraySegment<byte>(data, 2, 6);
int relPos = seg.IndexOf(new byte[] { 0x02, 0x03 });        // 返回 3（相对于视图起始）

// 十六进制输出
Console.WriteLine(data.ToHexString(16));  // 前 16 字节的格式化十六进制

// 字节数组相等比较
byte[] buf1 = { 0x01, 0x02, 0x03, 0x04 };
byte[] buf2 = { 0x01, 0x02, 0x03, 0x04 };
bool equal = buf1.SequenceEqual(buf2);                       // true

// 指定区域比较
bool regionEqual = buf1.SequenceEqual(0, 2, buf2, 2, 2);     // 比较 buf1[0..2] 与 buf2[2..2]

// unsafe 指针比较（高性能路径）
unsafe
{
    fixed (byte* p1 = buf1, p2 = buf2)
    {
        bool ptrEqual = ArrayExtensions.SequenceEqual(p1, p2, 4);  // true
    }
}
```

### 性能特性

- byte[] IndexOf 重载使用 CPU 原生的 `byte == byte` 比较（单条 CMP 指令），避免虚调用
- 头尾双过滤策略：先同时比对首尾字节，随机数据下误入内循环概率约 1/65536
- SequenceEqual 采用 32 字节展开比较策略（4 个 ulong 一组），充分利用 64 位寄存器与指令级并行
- 时间复杂度 O(n)，空间复杂度 O(1)

---

## `FileExtensions`

文件系统管理扩展方法，用于日志轮转、媒体文件管理等场景。

### 核心方法

| 方法 | 说明 |
|------|------|
| `ReserveFileCount(string dirPath, int count, string searchPattern)` | 按创建时间保留最新 N 个文件，删除多余旧文件 |
| `ReserveFileDays(string dirPath, int days, string searchPattern)` | 按修改时间保留最近 N 天内的文件 |
| `GetPatternFiles(string directory, Regex pattern)` | 正则表达式匹配文件名，按创建时间降序排序 |
| `GetPatternFiles(string directory, IEnumerable<string> extensions)` | 扩展名集合匹配，使用 HashSet O(1) 查找 |
| `GetSeriesFiles(string directory, IEnumerable<string> extensions)` | 按文件名中第一组数字分组，用于序列帧/系列文件 |

### 使用示例

```csharp
using SpaceCG.Extensions;
using System.Text.RegularExpressions;

// 保留最新 100 个日志文件
FileExtensions.ReserveFileCount(@"D:\logs", 100, "*.log");

// 删除 30 天前的临时文件
FileExtensions.ReserveFileDays(@"D:\temp", 30, "*.tmp");

// 获取所有图片文件
var images = FileExtensions.GetPatternFiles(@"D:\photos",
    new[] { ".jpg", ".jpeg", ".png", ".webp" });

// 按文件名中数字分组（如 frame_0001.png, frame_0002.png）
var series = FileExtensions.GetSeriesFiles(@"D:\frames",
    new[] { ".png", ".jpg" });
// Key=1 → [frame_0001.png], Key=2 → [frame_0002.png], ...
```

---

## `HttpClientExtensions`

针对 `HttpClient` 的扩展方法，支持**带进度报告**的文件上传和下载。

### 核心方法

| 方法 | 说明 |
|------|------|
| `DownloadAsync(this HttpClient, string url, string filePath, ...)` | 异步下载文件，支持进度报告、取消、临时文件安全替换 |
| `UploadAsync(this HttpClient, string url, string filePath, string formFieldName, ...)` | 异步上传文件（multipart/form-data），支持进度报告 |
| `GetMimeType(string file)` | 根据文件扩展名获取 MIME 类型 |

### 辅助类

| 类 | 说明 |
|------|------|
| `ProgressableStreamContent` | 自定义 `HttpContent`，在上传时报告进度（0.0 ~ 1.0） |

### 使用示例

```csharp
using System.Net.Http;
using SpaceCG.Extensions;

var client = new HttpClient();
var progress = new Progress<double>(p => Console.WriteLine($"下载进度: {p:P1}"));

// 下载文件
string result = await client.DownloadAsync(
    "https://example.com/video.mp4",
    @"D:\downloads\video.mp4",
    bufferSize: 1024 * 256,     // 256KB 缓冲区
    progress: progress);

// 上传文件
var response = await client.UploadAsync(
    "https://example.com/upload",
    @"D:\files\data.zip",
    "file",
    progress: progress);
```

### MIME 类型支持

`GetMimeType` 内置支持 40+ 种常见格式：html/css/js/json/xml/pdf/zip/rar/7z、png/jpg/webp/gif/svg、mp3/wav/ogg/aac、mp4/webm/mov/avi、ttf/woff2 等，未知类型返回 `application/octet-stream`。

---

## `InstanceExtensions`

提供对象实例的**反射扩展方法**，支持动态获取/设置字段和属性、批量赋值、事件清理和方法调用。

### 核心方法

| 方法 | 说明 |
|------|------|
| `TryGetFieldValue(object, string, out object)` | 动态获取实例字段值（支持 private/protected） |
| `TrySetFieldValue(object, string, object)` | 动态设置实例字段值，自动类型转换 |
| `TryGetPropertyValue(object, string, out object)` | 动态获取实例属性值 |
| `TrySetPropertyValue(object, string, object)` | 动态设置实例属性值，自动类型转换 |
| `SetPropertyValues(object, IReadOnlyDictionary<string, object>)` | 批量设置属性值（字典版本） |
| `SetPropertyValues(object, IEnumerable<XAttribute>)` | 批量设置属性值（XML 属性版本） |
| `SetPropertyValues(object, XElement)` | 从 XML 元素查找同名字段对象并赋值 |
| `RemoveEvent(object, string)` | 移除指定事件的所有订阅者 |
| `RemoveEvents(object)` | 移除实例上所有事件的所有订阅者 |
| `TryInvokeMethod(object, MethodInfo, object[], out object)` | 通过 MethodInfo 动态调用实例/扩展方法 |
| `TryInvokeMethod(object, string, object[], out object)` | 通过方法名动态调用（带缓存） |
| `TryInvokeMethod(object, string, string, out object)` | 通过方法名+字符串参数调用（支持嵌套数组语法） |

### 使用示例

```csharp
using SpaceCG.Extensions;
using System.Reflection;

var obj = new MyClass();

// 获取/设置私有字段
if (obj.TryGetFieldValue("_counter", out object count))
    Console.WriteLine(count);
obj.TrySetFieldValue("_counter", 42);

// 批量设置属性
obj.SetPropertyValues(new Dictionary<string, object>
{
    { "Name", "test" },
    { "Enabled", true }
});

// 清理所有事件订阅
obj.RemoveEvents();

// 动态方法调用
obj.TryInvokeMethod("ProcessData", new object[] { 42, "hello" }, out object result);

// 字符串参数调用（支持嵌套数组）
obj.TryInvokeMethod("ProcessMatrix", "[[1,2],[3,4]],True", out object result2);
```

### 设计注意

- `MethodInfoCache` 使用 `ConcurrentDictionary` 缓存方法信息，避免重复反射
- 扩展方法查找遍历所有已加载程序集，支持 `IsAssignableFrom` 基类/接口匹配
- 所有方法采用 Try 模式，失败返回 false 而不抛异常

---

## `MathExtensions`

数值计算的扩展方法，涵盖区间映射、Clamp、位操作和字节序转换。

### 核心方法

| 方法 | 说明 |
|------|------|
| `Map(this double, double, double, double, double)` | 将值从源区间线性映射到目标区间 |
| `Clamp(this byte/short/int/long/float/double, ...)` | 将值限制在 [min, max] 闭区间内（8 个数值类型重载） |
| `ReverseBits(this byte/ushort/uint/ulong)` | 按位反转（bit-reverse），分治法实现 |
| `ReverseBytes(this ushort/uint/ulong)` | 字节序反转（大端/小端转换，endian swap） |

### 使用示例

```csharp
using SpaceCG.Extensions;

// 区间映射：将 0.5 从 [0, 1] 映射到 [0, 100]
double result = 0.5.Map(0.0, 1.0, 0.0, 100.0);  // 50.0

// 限制值范围
int clamped = 150.Clamp(0, 100);                    // 100

// 位反转
byte reversed = ((byte)0x31).ReverseBits();          // 0x8C

// 字节序反转（大端 ↔ 小端）
uint swapped = 0x12345678U.ReverseBytes();           // 0x78563412
```

---

## `NetworkExtensions`

本机网络接口信息查询，用于获取 IPv4 地址、子网掩码、广播地址等。

### 核心方法

| 方法 | 说明 |
|------|------|
| `GetLocalIPv4Addresses()` | 获取本机所有活动 IPv4 地址（排除回环） |
| `GetLocalInterfaceAddresses()` | 获取 MAC 地址到 IPv4 地址列表的映射 |
| `GetSubnetMasks()` | 获取本机所有活动网络接口的 IPv4 子网掩码 |
| `GetBroadcastAddresses()` | 获取本机所有活动接口的 IPv4 广播地址 |
| `GetBroadcastAddress(IPAddress)` | 根据指定 IP 计算其所在网络的广播地址 |
| `GetBroadcastAddress(string)` | 根据 IP 字符串计算广播地址 |

### 使用示例

```csharp
using SpaceCG.Extensions;

// 获取本机所有 IPv4 地址
foreach (var ip in NetworkExtensions.GetLocalIPv4Addresses())
    Console.WriteLine(ip);  // 192.168.1.100, 10.0.0.5, ...

// 按网卡分组查看
var interfaces = NetworkExtensions.GetLocalInterfaceAddresses();
foreach (var kv in interfaces)
{
    Console.WriteLine($"MAC: {BitConverter.ToString(kv.Key.GetAddressBytes())}");
    foreach (var ip in kv.Value)
        Console.WriteLine($"  IP: {ip}");
}

// 获取广播地址
var broadcasts = NetworkExtensions.GetBroadcastAddresses();
// 根据特定 IP 获取其广播地址
var broadcast = NetworkExtensions.GetBroadcastAddress(IPAddress.Parse("192.168.1.100"));
```

---

## `PathExtensions`

为 .NET Framework 4.8 提供现代 .NET 中 `System.IO.Path` 缺失的等效功能。

### 核心方法

| 方法 | 说明 |
|------|------|
| `EndsInDirectorySeparator(string)` | 检查路径是否以目录分隔符结尾 |
| `TrimEndingDirectorySeparator(string)` | 移除路径末尾的目录分隔符（保留根目录分隔符） |
| `IsRootPath(string)` | 纯字符串操作判断路径是否为根目录 |
| `GetRelativePath(string, string)` | 计算两个路径之间的相对路径 |

### 使用示例

```csharp
using SpaceCG.Extensions;

// 路径分隔符检测
bool ends = PathExtensions.EndsInDirectorySeparator(@"C:\Users\");  // true

// 移除尾部分隔符
string trimmed = PathExtensions.TrimEndingDirectorySeparator(@"C:\Users\"); // "C:\Users"

// 根目录判断
bool isRoot = PathExtensions.IsRootPath(@"C:\");          // true
bool isRoot2 = PathExtensions.IsRootPath(@"\\server\share\"); // true

// 相对路径计算
string relative = PathExtensions.GetRelativePath(
    @"C:\Project\src\",
    @"C:\Project\src\Utils\Helper.cs");
// 输出: "Utils\Helper.cs"
```

---

## `StringExtensions`

字符串处理的核心工具箱，包含 XML 转义、十六进制转换、自定义参数文本解析器和类型转换器。

### 核心方法

| 方法 | 说明 |
|------|------|
| `Unescape(this string)` | 将 XML 转义字符（`&lt;` 等）还原为原始字符 |
| `IsVideoExtension(this string)` | 判断扩展名是否为常见视频格式 |
| `IsImageExtension(this string)` | 判断扩展名是否为常见图片格式 |
| `IsHexString(this string)` | 判断字符串是否仅含十六进制字符且长度为偶数 |
| `HexCharToNibble(this char)` | 单个十六进制字符 → 4 位整数值 |
| `ToByteArray(this string)` | 十六进制字符串 → 字节数组 |
| `TryParseParameters(this string, out object[])` | **核心解析器**：逗号分隔参数文本 → 嵌套数组树 |
| `TryConvertTo(this string, Type, out object)` | 字符串 → 强类型值（支持 20+ 种类型） |
| `TryConvertTo<T>(this string, out T)` | 泛型版本 |
| `SerializeValue(object)` | 对象 → 字符串（ParseParameters 的反向操作） |
| `SerializeEnumerable(IEnumerable)` | 集合序列化为 `[elem1,elem2,...]` 格式 |

### TryParseParameters 详解

这是整个 RPC 框架参数解析的核心，支持以下语法：

```csharp
using SpaceCG.Extensions;

// 简单标量
"0x01,True,32,False" → ["0x01", "True", "32", "False"]

// 嵌套数组
"0x01,3,[True,True,False]" → ["0x01", "3", string[3]{"True","True","False"}]

// 单引号保护（含逗号的字符串）
"'hello,world',0x01,[True,False]" → ["hello,world", "0x01", string[2]{"True","False"}]

// 多层嵌套
"[[1,2],[3,4]]" → [object[2]{string[2]{"1","2"}, string[2]{"3","4"}}]

// 连续逗号（空 token 跳过）
",," → []
```

**性能**：单次扫描 O(n)，零反射、零 Regex、零 Split、零 StringBuilder，适用于 200fps+ 高频调用。

### 类型转换

`TryConvertTo` 支持的类型：
- string、bool、float、double、decimal
- byte/sbyte/short/ushort/int/uint/long/ulong（支持十进制和 0x 十六进制前缀）
- 枚举、Guid、TimeSpan、DateTime、DateTimeOffset
- 其他值类型通过 `TypeDescriptor.GetConverter` 兜底

---

## `SocketExtensions`

为 `TcpClient`、`UdpClient` 和 `SerialPort` 提供连接管理、数据接收与协议解析的扩展方法。该类为 `partial class`，跨 `SpaceCG.Standard` 和 `SpaceCG.Standard.Wpf` 两个项目实现。

### 核心方法

| 方法 | 说明 |
|------|------|
| `IsConnected(this Socket)` | 检查 Socket 是否处于已连接状态（Poll + Available + Connected 三条件判断） |
| `IsConnected(this TcpClient)` | 检查 TcpClient 是否处于已连接状态 |
| `ConnectAsync(this TcpClient, ...)` | TCP 自动重连循环，断开时自动清理并重连（连接正常 200ms 检查，断开后 3s 重试） |
| `ReceiveParseAsync(this TcpClient, ...)` | 带自动重连的 TCP 异步接收解析循环，整合连接管理、数据读取、协议解析 |
| `ReceiveParseAsync(this UdpClient, ...)` | UDP 异步接收解析循环，从已绑定 UdpClient 循环接收数据报并解析 |
| `ReceiveParseAsync(this SerialPort, ...)` | 串口异步接收解析循环，自动打开串口，读取数据并解析（Wpf 项目） |

### 使用示例

#### TCP 连接状态检测

```csharp
using System.Net.Sockets;
using SpaceCG.Extensions;

var client = new TcpClient();
await client.ConnectAsync("localhost", 8080);

// 非阻塞检查
if (client.IsConnected())
{
    // 读写操作...
}
```

#### TCP 自动重连

```csharp
using System.Net.Sockets;
using SpaceCG.Extensions;

var client = new TcpClient();
var cts = new CancellationTokenSource();

// 启动自动重连（仅管理连接，不读取数据）
var task = client.ConnectAsync("127.0.0.1", 2001,
    newClient => client = newClient, cts.Token);

// 通过 client 发送数据（需先判断 IsConnected）
if (client.IsConnected())
{
    var stream = client.GetStream();
    // ... 发送数据 ...
}

// 退出时取消
cts.Cancel();
await task;
client?.Dispose();
```

#### TCP 带自动重连的接收解析

```csharp
using System.Net.Sockets;
using System.Text;
using SpaceCG.Extensions;
using SpaceCG.Generic;

// 创建协议解析器（以 CRLF 结尾的文本协议为例）
var parser = new FooterProtocolParser(new byte[] { 0x0D, 0x0A });

// 订阅数据帧事件
parser.FrameReceived += (sender, e) =>
{
    var message = Encoding.UTF8.GetString(e.FrameView.Array, e.FrameView.Offset, e.FrameView.Count);
    Console.WriteLine($"收到: {message}");
};

// 启动自动重连接收循环
TcpClient client = new TcpClient();
var cts = new CancellationTokenSource();
var task = client.ReceiveParseAsync("127.0.0.1", 2001, parser,
    newClient => client = newClient, cts.Token);

// 退出时取消
cts.Cancel();
await task;
client?.Dispose();
```

#### UDP 接收解析

```csharp
using System.Net;
using System.Net.Sockets;
using SpaceCG.Extensions;
using SpaceCG.Generic;

// 创建并绑定 UdpClient
var client = new UdpClient();
client.Client.Bind(new IPEndPoint(IPAddress.Any, 2001));

// 创建协议解析器（固定长度 128 字节数据报）
var parser = new FixedLengthProtocolParser(128);
parser.FrameReceived += (sender, e) =>
{
    Console.WriteLine($"收到 {e.FrameView.Count} 字节");
};

// 启动接收解析循环
var cts = new CancellationTokenSource();
var task = client.ReceiveParseAsync(parser, cts.Token);

// 退出时取消
cts.Cancel();
await task;
client?.Dispose();
```

#### 串口接收解析（Wpf 项目）

```csharp
using System.IO.Ports;
using System.Text;
using SpaceCG.Extensions;
using SpaceCG.Generic;

// 配置串口
var serialPort = new SerialPort("COM3", 115200);

// 创建协议解析器（以 CRLF 结尾的文本协议为例）
var parser = new FooterProtocolParser(new byte[] { 0x0D, 0x0A });
parser.FrameReceived += (sender, e) =>
{
    var text = Encoding.UTF8.GetString(e.FrameView.Array, e.FrameView.Offset, e.FrameView.Count);
    Console.WriteLine($"收到: {text}");
};

// 启动接收解析循环
var cts = new CancellationTokenSource();
var task = serialPort.ReceiveParseAsync(parser, cts.Token);

// 退出时取消
cts.Cancel();
await task;
serialPort.Dispose();
```

### 设计注意

- 所有 `ReceiveParseAsync` 方法均将数据写入 `ProtocolParser` 并调用 `ProcessBuffer()`，匹配的帧通过 `FrameReceived` 事件抛出。
- TCP 方法通过 `CancellationToken` 控制生命周期，取消时自动关闭连接并退出循环。
- UDP 方法要求调用方预先绑定 `UdpClient`，`ReceiveAsync` 在 `Task.Run` 内执行同步 IO（`Socket.ReceiveFrom`）。
- 串口方法自动管理 `Open`/`Close`，无数据时等待 10ms，未打开时等待 3s 后重试。

---

## `TypeExtensions`

类型反射与参数转换的完整流水线，为 RPC 框架的方法调度提供基础设施。

### 核心方法

| 方法 | 说明 |
|------|------|
| `GetParameterSignature(this IEnumerable<ParameterInfo>)` | 获取方法参数的类型签名列表（如 `"SVT,[SVT]"`） |
| `GetParameterSignature(this IEnumerable<object>)` | 获取运行时参数值的签名列表 |
| `IsEnumerableOfT(this Type)` | 判断类型是否实现 `IEnumerable<T>` 接口（带缓存） |
| `GetType(string, bool)` | 通过类型名获取 Type 对象（带缓存，遍历已加载程序集） |
| `TryConvertParameter(object, Type, out object)` | 核心调度器：将 ParseParameters 输出转为强类型参数 |
| `TryConvertParameters(object[], MethodInfo, out object[])` | 批量参数转换，支持扩展方法识别 |
| `ConvertToArray(Array, Type)` | 源数组 → 指定元素类型的强类型数组（递归） |

### 内部缓存

| 缓存 | 说明 |
|------|------|
| `TypeNameCache` | `ConcurrentDictionary<string, Type>`，类型名称缓存 |
| `EnumerableOfTCache` | `ConcurrentDictionary<Type, bool>`，IEnumerable<T> 检测缓存 |
| `TypeConverterCache` | `ConcurrentDictionary<Type, TypeConverter>`，类型转换器缓存 |

### 类型签名体系

```
SVT     → 值类型（含枚举）或 string
[SVT]   → SVT 数组
[REF]   → 引用类型数组
[<REF...>] → 多泛型参数集合类型
REF     → 其他引用类型
```

---

## `XElementExtensions`

XML 配置文件的预处理工具，支持模板引用替换和字典占位符替换。

### 核心方法

| 方法 | 说明 |
|------|------|
| `ApplyTemplates(this XElement)` | 递归展开 `<RefTemplate Name="..."/>` 引用为模板内容 |
| `ApplyDictionary(this XElement)` | 递归替换 `{Key}` 占位符为字典值 |
| `TryGetValue<T>(this XElement, string, out T)` | 从 XML 元素读取属性/子元素值并转换为强类型 |
| `UpdateAttribute(this XElement, string, object)` | 设置 XML 属性（存在则更新，不存在则添加） |
| `LoadConfig(string configFile)` | 加载 XML 配置文件并自动执行模板+字典替换 |

### 使用示例

```xml
<!-- config.xml -->
<AppConfig>
  <AppConfig.Dictionary>
    <Item Key="AppName" Value="MyApp"/>
    <Item Key="Version" Value="1.0.0"/>
  </AppConfig.Dictionary>
  <AppConfig.Templates>
    <Template Name="Window">
      <Window Title="{AppName}" Width="800" Height="600"/>
    </Template>
  </AppConfig.Templates>
  <RefTemplate Name="Window"/>
</AppConfig>
```

```csharp
using SpaceCG.Extensions;

// 一键加载并预处理
XElement config = XElementExtensions.LoadConfig("config.xml");
// 自动完成：ApplyTemplates + ApplyDictionary

// 读取值
if (config.TryGetValue<string>("Title", out string title))
    Console.WriteLine(title);  // "MyApp"
```

### 安全特性

- 自动检测并移除循环引用的模板（DFS 依赖图分析）
- 父级字典/模板先处理，子级同名 Key/模板不会被匹配
- `LoadConfig` 抛出明确的 `ArgumentException`/`FileNotFoundException`

---


> 文档版本：v1.1  |  最后更新：2026-08-06  |  维护：SpaceCG 团队
