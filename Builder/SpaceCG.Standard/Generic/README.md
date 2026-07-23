# SpaceCG.Generic

`SpaceCG.Generic` 命名空间提供通用基础设施类，涵盖数据校验、环形缓冲区、CRC 校验计算、异步日志记录、对象池和协议解析。

- **目标框架**：.NET Framework 4.8（C# 7.3）
- **设计原则**：高性能、低分配、线程安全

---

## 类列表

| 类 | 类型 | 说明 |
|------|------|------|
| [`ChecksumHelper`](#checksumhelper) | static class | Sum/XOR/BCC/LRC 等常用校验和算法 |
| [`CircularBuffer<T>`](#circularbuffert) | class | 环形缓冲区（循环缓冲区），双端添加/移除 |
| [`CRCCheckHelper`](#crccheckhelper) | static class | CRC8/16/32/64 循环冗余校验（Rocksoft 通用模型） |
| [`LoggerTraceListener`](#loggertracelistener) | class | 异步日志跟踪侦听器（文件+控制台） |
| [`ObjectPool<T>`](#objectpoolt) | class | 泛型对象池，减少 GC 压力 |
| [`ProtocolParser`](#protocolparser) | abstract class | 数据协议解析器基类（生产者-消费者模式） |
| [`TraceMessage`](#tracemessage) | class | 日志消息实体 |

---

## `ChecksumHelper`

提供 **Sum（累加和）、XOR/BCC（异或校验）、LRC（纵向冗余校验）** 等常用校验算法。

### 核心方法

| 方法 | 位宽 | 说明 |
|------|:---:|------|
| `Sum8(bytes, offset, length)` / `Sum8(bytes)` | 8 | 逐字节累加，溢出回绕 |
| `Sum16(bytes, offset, length)` / `Sum16(bytes)` | 16 | 按 16 位字（big-endian）累加，折叠进位 |
| `Sum32(bytes, offset, length)` / `Sum32(bytes)` | 32 | 按 32 位字（big-endian）累加，折叠进位 |
| `XOR8(bytes, offset, length)` / `XOR8(bytes)` | 8 | 所有字节逐位异或 |
| `BCC8(bytes, offset, length)` / `BCC8(bytes)` | 8 | 块校验字符，与 XOR8 等效 |
| `LRC8(bytes, offset, length)` / `LRC8(bytes)` | 8 | 纵向冗余校验，`-(累加和) mod 256` |

### 使用示例

```csharp
using SpaceCG.Generic;

byte[] data = { 0x01, 0x02, 0x03, 0x04, 0x05 };

byte sum8 = data.Sum8();           // 累加和（8位）
ushort sum16 = data.Sum16();       // 累加和（16位）
byte xor = data.XOR8();            // 异或校验
byte bcc = data.BCC8();            // 块校验
byte lrc = data.LRC8();            // LRC 纵向冗余校验

// 指定范围
byte partial = data.Sum8(1, 3);    // 从索引1开始，计算3个字节
```

---

## `CircularBuffer<T>`

环形缓冲区（循环缓冲区），支持**双端添加和移除**元素。当缓冲区已满时，从任意一端添加新元素会自动从另一端移除最旧的元素。

### 关键属性与方法

| 成员 | 说明 |
|------|------|
| `Count` | 当前缓冲区中的元素数量 |
| `Capacity` | 缓冲区最大容量 |
| `IsEmpty` | 缓冲区是否为空 |
| `IsFull` | 缓冲区是否已满 |
| `this[int index]` | 按逻辑索引随机访问 |
| `AddBack(T)` | 向尾部添加元素（满时移除头部） |
| `AddFront(T)` | 向头部添加元素（满时移除尾部） |
| `PeekBack()` | 查看尾部元素，不移除 |
| `PeekFront()` | 查看头部元素，不移除 |
| `RemoveBack()` | 移除并返回尾部元素 |
| `RemoveFront()` | 移除并返回头部元素 |
| `Clear()` | 清空缓冲区 |
| `ToArray()` | 复制到新数组 |
| `GetSegments()` | 获取零拷贝 ArraySegment 视图（最多 2 段） |

### 使用示例

```csharp
using SpaceCG.Generic;

// 创建容量为 5 的环形缓冲区
var buffer = new CircularBuffer<int>(5);

buffer.AddBack(1);
buffer.AddBack(2);
buffer.AddBack(3);
buffer.AddBack(4);
buffer.AddBack(5);  // 满
buffer.AddBack(6);  // 自动移除 1，缓冲区: [2,3,4,5,6]

int first = buffer.PeekFront();  // 2
int last = buffer.PeekBack();    // 6

// 零拷贝分段视图（适用于网络发送）
var segments = buffer.GetSegments();
// socket.Send(segments);  -- 无需复制数据
```

### 性能特性

- 所有操作 O(1)
- `GetSegments()` 零拷贝，返回内部数组的 `ArraySegment<T>` 视图
- 实现了 `IEnumerable<T>` 和 `IReadOnlyList<T>`
- **非线程安全**，多线程需要外部同步

---

## `CRCCheckHelper`

CRC（Cyclic Redundancy Check，循环冗余校验）计算工具类，基于 **Rocksoft 通用模型**。

### 通用方法

| 方法 | 位宽 | 说明 |
|------|:---:|------|
| `ComputeCRC8(bytes, offset, length, poly, init, refIn, refOut, xorOut)` | 8 | 通用 CRC8 |
| `ComputeCRC16(bytes, offset, length, poly, init, refIn, refOut, xorOut)` | 16 | 通用 CRC16 |
| `ComputeCRC32(bytes, offset, length, poly, init, refIn, refOut, xorOut)` | 32 | 通用 CRC32 |
| `ComputeCRC64(bytes, offset, length, poly, init, refIn, refOut, xorOut)` | 64 | 通用 CRC64 |

### 预设协议方法

| 方法 | 标准协议 | Poly | Init | RefIn/Out | XorOut |
|------|------|:---:|:---:|:---:|:---:|
| `ComputeCRC8_ITU` | ITU-T I.432.1 | 0x07 | 0x00 | false | 0x55 |
| `ComputeCRC8_ROHC` | ROHC | 0x07 | 0xFF | true | 0x00 |
| `ComputeCRC8_MAXIM` | Dallas/Maxim 1-Wire | 0x31 | 0x00 | true | 0x00 |
| `ComputeCRC16_IBM` | IBM | 0x8005 | 0x0000 | true | 0x0000 |
| `ComputeCRC16_MAXIM` | Maxim | 0x8005 | 0x0000 | true | 0xFFFF |
| `ComputeCRC16_USB` | USB | 0x8005 | 0xFFFF | true | 0xFFFF |
| `ComputeCRC16_MODBUS` | Modbus | 0x8005 | 0xFFFF | true | 0x0000 |
| `ComputeCRC16_CCITT` | CCITT (Kermit) | 0x1021 | 0x0000 | true | 0x0000 |
| `ComputeCRC16_CCITTFALSE` | CCITT-FALSE | 0x1021 | 0xFFFF | false | 0x0000 |
| `ComputeCRC16_X25` | X.25 | 0x1021 | 0xFFFF | true | 0xFFFF |
| `ComputeCRC16_XMODEM` | XMODEM | 0x1021 | 0x0000 | false | 0x0000 |
| `ComputeCRC16_XMODEM2` | XMODEM-2 | 0x8408 | 0x0000 | true | 0x0000 |
| `ComputeCRC16_DNP` | DNP | 0x3D65 | 0x0000 | true | 0xFFFF |
| `ComputeCRC32` | ISO-HDLC (默认) | 0x04C11DB7 | 0xFFFFFFFF | true | 0xFFFFFFFF |
| `ComputeCRC32_C` | Castagnoli (SSE4.2) | 0x1EDC6F41 | 0xFFFFFFFF | true | 0xFFFFFFFF |
| `ComputeCRC32_MPEG2` | MPEG-2 | 0x04C11DB7 | 0xFFFFFFFF | false | 0x00000000 |
| `ComputeCRC64_ISO` | ISO | 0x1B | 0xFFFFFFFFFFFFFFFF | true | 0xFFFFFFFFFFFFFFFF |
| `ComputeCRC64_ECMA` | ECMA | 0x42F0E1EBA9EA3693 | 0xFFFFFFFFFFFFFFFF | true | 0xFFFFFFFFFFFFFFFF |

### 使用示例

```csharp
using SpaceCG.Generic;

byte[] data = { 0x01, 0x02, 0x03, 0x04, 0x05 };

// 预设协议（最常用）
ushort modbus = data.ComputeCRC16_MODBUS();     // Modbus CRC16
uint crc32 = data.ComputeCRC32();               // ISO-HDLC CRC32
uint crc32c = data.ComputeCRC32_C();            // Castagnoli (SSE4.2 硬件)

// 通用计算（自定义参数）
byte customCrc8 = data.ComputeCRC8(
    offset: 0, length: data.Length,
    poly: 0x07, init: 0x00,
    refIn: false, refOut: false, xorOut: 0x55);

// 指定范围
ushort partial = data.ComputeCRC16_MODBUS(1, 3); // 从索引1开始计算3个字节
```

### 设计要点

- **传入标准多项式**：即使 `refIn=true`，也请传入标准多项式，代码内部自动处理反转
- **查找表缓存**：每种 (poly, refIn) 组合的查找表仅生成一次，使用 `ConcurrentDictionary` 缓存
- **Rocksoft 模型**：当 `refOut != refIn` 时自动对结果进行位反转

---

## `LoggerTraceListener`

将 `System.Diagnostics.Trace` 输出写入文件和控制台的**异步日志侦听器**。

### 核心特性

- **异步写入**：无锁队列消费，不阻塞调用线程
- **批量 Flush**：通过 `AutoFlushInterval` 控制刷新频率
- **文件轮转**：自动按 `FileSizeLimit` 分割日志，按 `FileBackupDays` 清理
- **控制台彩色输出**：按日志级别显示不同颜色
- **调用者信息**：可选捕获方法名/类型名/行号

### 关键属性

| 属性 | 默认值 | 说明 |
|------|:---:|------|
| `FileSizeLimit` | 2 MB | 文件大小上限，超过则备份并创建新文件 |
| `FileBackupDays` | 30 天 | 备份文件最大保留天数 |
| `AutoFlushInterval` | 4 条 | 写入 N 条后自动 Flush |
| `EnableCallerInfo` | DEBUG=true | 是否捕获调用栈（有性能开销） |

### 构造函数

| 构造函数 | 说明 |
|------|------|
| `LoggerTraceListener(bool enabledCallerInfo)` | 自动以模块名+日期生成日志路径 |
| `LoggerTraceListener(string fileName, string name, bool enabledCallerInfo)` | 指定日志文件路径 |
| `LoggerTraceListener(Stream stream, string name, bool enabledCallerInfo)` | 写入指定流 |
| `LoggerTraceListener(TextWriter writer, string name, bool enabledCallerInfo)` | 写入指定 TextWriter |

### 使用示例

```csharp
using SpaceCG.Generic;
using System.Diagnostics;

// 简单示例：默认配置
Trace.Listeners.Add(new LoggerTraceListener());

// 控制台输出
Trace.Listeners.Add(new LoggerTraceListener(Console.Out));

// 完整配置
var listener = new LoggerTraceListener(
    $"logs/{DateTime.Now:yyyy-MM-dd}.log",
    "MyLogger");
listener.EnableCallerInfo = true;        // 启用调用者信息
listener.FileSizeLimit = 1024 * 1024;    // 1MB 轮转
listener.FileBackupDays = 7;             // 保留 7 天
listener.AutoFlushInterval = 1;          // 每条立即 Flush
listener.Filter = new EventTypeFilter(SourceLevels.Warning);

// 实时监控日志
listener.TraceMessageEnqueued += (sender, msg) =>
{
    Console.WriteLine(msg.ToFormatString());
};

Trace.Listeners.Add(listener);
Trace.TraceInformation("服务已启动");
```

### 日志输出格式

```
[12:01:15.234] [ INFO] [ 1] [MyClass.MyMethod](42) - 日志内容
 时间戳         级别    线程ID  类型.方法        行号   消息
```

### 颜色映射

| 级别 | 颜色 |
|------|------|
| DEBUG | Green |
| INFO | Gray |
| WARN | Yellow |
| ERROR | Red |
| FATAL | DarkRed |

---

## `ObjectPool<T>`

泛型对象池，用于复用频繁创建/销毁的短生命周期对象，减少 GC 压力。

### 核心方法

| 方法 | 说明 |
|------|------|
| `Rent()` | 从池中租用一个实例（池空时创建新实例） |
| `Return(T)` | 归还实例到池中（池满时丢弃，若实现 IDisposable 则释放） |
| `Clear()` | 清空池中所有缓存实例 |

### 使用示例

```csharp
using SpaceCG.Generic;

// 创建容量为 64 的对象池，预创建 16 个实例
var pool = new ObjectPool<MyBuffer>(initialCount: 16, maxCount: 64);

// 租用
var buffer = pool.Rent();
buffer.Reset();

// 使用...

// 归还
pool.Return(buffer);
```

### 设计要点

- **线程安全**：使用 `ConcurrentQueue<T>` + `Interlocked` 实现
- **容量近似控制**：高并发下 `Count` 可能略微超出 `MaxCount`，对内存影响可控
- **自动释放**：归还失败时若对象实现了 `IDisposable`，自动调用 `Dispose()`

---

## `ProtocolParser`

数据协议解析器的抽象基类，采用**生产者-消费者模式**处理字节流。

### 架构

```
外部写入字节 → [内部线性缓冲区] → Parse() 匹配完整包 → PacketReceived 事件
```

- 使用线性缓冲区（byte[] + 读写双指针）实现零拷贝
- 尾部空间不足时自动紧凑（Compact）
- 缓冲区满载且无完整包时清空防止内存无限增长
- 通过 `SemaphoreSlim` 保证线程安全

### 子类实现

| 类 | 说明 |
|------|------|
| `FixedLengthProtocolParser` | 固定长度协议（如传感器数据帧） |
| `FooterProtocolParser` | 尾部标记协议（如换行符 `\r\n` 结尾的文本协议） |
| `HeaderFooterProtocolParser` | 头尾标记协议（如串口通信帧格式） |

### 核心方法

| 方法 | 说明 |
|------|------|
| `Write(byte[], int, int)` | 同步写入字节数据，返回实际写入字节数 |
| `ReadFromAsync(Stream, CancellationToken)` | 从流中异步循环读取并解析 |
| `Clear()` / `ClearAsync(CancellationToken)` | 清空缓冲区 |
| `Dispose()` | 释放资源 |

### 使用示例

```csharp
using SpaceCG.Generic;

// 换行符分隔的文本协议
var parser = new FooterProtocolParser(
    new byte[] { 0x0D, 0x0A });  // \r\n

parser.PacketReceived += (sender, args) =>
{
    string line = Encoding.UTF8.GetString(
        args.Packet.Array,
        args.Packet.Offset,
        args.Packet.Count);
    Console.WriteLine($"收到: {line}");
};

// 从流中异步读取
using (var stream = tcpClient.GetStream())
{
    await parser.ReadFromAsync(stream, cancellationToken);
}

// 或手动写入
byte[] data = Encoding.UTF8.GetBytes("hello\r\n");
int written = parser.Write(data, 0, data.Length);
```

### 自定义协议

```csharp
public class MyProtocolParser : ProtocolParser
{
    protected override ArraySegment<byte> Parse(ArraySegment<byte> pendingView)
    {
        // 实现自定义协议逻辑
        // 返回匹配到的数据包视图，或 default 表示需要更多数据
    }
}
```

### 重要约束

> ⚠ **PacketReceived 事件在锁内触发**，回调中**绝对不可**再次调用 `Write`、`ReadFromAsync`、`Clear` 或 `ClearAsync`，否则会因 `SemaphoreSlim` 不支持重入而**死锁**。

---

> 文档版本：v1.0  |  最后更新：2026-07-19  |  维护：SpaceCG 团队
