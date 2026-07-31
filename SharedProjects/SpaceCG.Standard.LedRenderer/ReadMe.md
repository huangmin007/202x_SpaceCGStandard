# SpaceCG.Standard.LedRenderer

全彩 LED 灯带控制库（第二版），支持实时同步渲染、自定义灯珠二维排列布局、多种传输通道（串口/TCP/UDP）。

---

## 目录

- [1. 快速开始](#1-快速开始)
- [2. 核心概念](#2-核心概念)
- [3. 架构设计](#3-架构设计)
- [4. API 参考](#4-api-参考)
  - [4.1 枚举类型](#41-枚举类型)
  - [4.2 数据帧协议](#42-数据帧协议)
  - [4.3 FrameRenderModel（抽象基类）](#43-framerendermodel抽象基类)
  - [4.4 LedStripObject（灯带对象）](#44-ledstripobject灯带对象)
  - [4.5 LedRenderBus（渲染总线）](#45-ledrenderbus渲染总线)
  - [4.6 IDrawingDisplay（实时绘制接口）](#46-idrawingdisplay实时绘制接口)
  - [4.7 ITransportChannel（传输通道接口）](#47-itransportchannel传输通道接口)
  - [4.8 扩展方法](#48-扩展方法)
- [5. 帧率计算参考](#5-帧率计算参考)
- [6. 示例代码](#6-示例代码)
- [7. 已知问题与建议](#7-已知问题与建议)

---

## 1. 快速开始

### 安装依赖

本项目为 **Shared Project**（共享项目），直接引用 `.shproj` 文件即可，无需 NuGet 包。

依赖项：
- .NET Framework 4.8
- System.Drawing
- System.IO.Ports
- 项目内共享的 `SpaceCG.Standard` 基础库（提供 `ArrayPool`、`Trace`、`Extensions` 等）

### 最小示例

```csharp
using SpaceCG.Device;
using SpaceCG.IO;

// 1. 创建灯带对象（地址 0x0001，端口 1，WS2812B 灯珠，GRB 颜色格式）
var ledStrip = new LedStripObject(0x0001, 0x01, LedType.WS2812B, ColorFormat.GRB);

// 2. 添加灯珠坐标（灯珠在 LED 面板上的物理位置，顺序必须与实际布线一致）
ledStrip.AddPoints(
    new System.Drawing.Point(0, 0),
    new System.Drawing.Point(15, 0)
);

// 3. 创建渲染总线（串口通道）
var renderBus = new LedRenderBus(ChannelType.SERIAL, "COM3,921600");
renderBus.AddLedStrip(ledStrip);

// 4. 启动渲染
renderBus.OpenChannel();
renderBus.StartRender();

// 5. 渲染纯色
renderBus.AddColorFrame(0x0001, 0x01, 255, 0, 0, start: 0, repeat: 1); // 红色

// 6. 停止与清理
renderBus.StopRender();
renderBus.CloseChannel();
renderBus.Dispose();
```

---

## 2. 核心概念

| 概念 | 说明 |
|------|------|
| **灯珠（LED Point）** | LED 灯带上的单颗可寻址灯珠，通过 `(X,Y)` 坐标映射到位图像素位置 |
| **灯带（LedStripObject）** | 一组物理上串联的灯珠集合，灯珠在集合中的顺序即为物理信号顺序 |
| **渲染总线（LedRenderBus）** | 管理一个传输通道 + 多个灯带 + 渲染线程 + 帧调度 |
| **传输通道（ITransportChannel）** | 底层通信抽象，支持串口（SerialPort）、TCP、UDP |
| **数据帧（Frame）** | 遵循协议格式的字节数组，携带灯珠颜色数据 |
| **实时绘制（IDrawingDisplay）** | 从屏幕/桌面/WPF元素捕获像素数据，供灯带渲染使用 |

---

## 3. 架构设计

```
┌─────────────────────────────────────────────────────────┐
│                    IDrawingDisplay                      │
│  (DrawingDesktop / DrawingWpfElement / 自定义)          │
│  捕获像素数据 → NewDrawingFrame 事件                     │
└─────────────────────┬───────────────────────────────────┘
                      │ pixels
                      ▼
┌─────────────────────────────────────────────────────────┐
│                    LedRenderBus                         │
│  ┌─────────────────────────────────────────────────┐    │
│  │  渲染线程 (RenderingBusThread)                    │    │
│  │  1. 消费总线级帧队列                              │    │
│  │  2. 遍历各 LedStripObject 消费灯带级帧队列         │    │
│  │  3. WriteFrame → ITransportChannel.Write()       │    │
│  │  4. 读取设备响应 & 异常处理                        │    │
│  └─────────────────────────────────────────────────┘    │
│  ┌──────────┐  ┌──────────┐  ┌──────────┐              │
│  │LedStrip 1│  │LedStrip 2│  │LedStrip N│   ...        │
│  │FrameQueue│  │FrameQueue│  │FrameQueue│              │
│  │LedPoints │  │LedPoints │  │LedPoints │              │
│  └──────────┘  └──────────┘  └──────────┘              │
│                         │                               │
│                 ITransportChannel                       │
│          (SerialPort / TCP / UDP)                       │
└─────────────────────────────────────────────────────────┘
```

### 帧数据流

```
RenderPixels()/AddColorFrame()
  → LedStripObject.CreateEmptyColorFrame()   // 创建帧 + 填充协议头尾
  → LedStripObject.EnqueueFrame()            // 入队 + 溢出清理
  → RenderingBusThread 消费循环               // TryDequeueFrame() + 重复帧去重
  → WriteFrame()                              // 写入传输通道
  → 设备响应处理                               // 超时/异常检测
```

### 帧去重优化

渲染线程通过 `TryDequeueFrame()` 检测连续相同帧，由 `RenderingRepeatInterval` 控制去重力度：

- **LedRenderBus**：`RenderingRepeatInterval = 0`（总线级不去重，因为总线帧通常是命令帧或广播帧）
- **LedStripObject**：`RenderingRepeatInterval = 8`（灯带级：连续 8 帧相同才渲染 1 次）

---

## 4. API 参考

### 4.1 枚举类型

#### ColorChannel（颜色通道）

| 值 | 说明 |
|----|------|
| `R = 0x00` | 红色通道 |
| `G = 0x01` | 绿色通道 |
| `B = 0x02` | 蓝色通道 |
| `A = 0x03` | Alpha 通道 |

#### ColorFormat（颜色/像素格式）

描述颜色在内存中的存储顺序。

**三通道（24位）：**

| 值 | 内存顺序 | 说明 |
|----|----------|------|
| `RGB` | R,G,B,R,G,B,... | 标准 RGB |
| `RBG` | R,B,G,R,B,G,... | |
| `GRB` | G,R,B,G,R,B,... | WS2812B 常用 |
| `GBR` | G,B,R,G,B,R,... | |
| `BRG` | B,R,G,B,R,G,... | |
| `BGR` | B,G,R,B,G,R,... | GDI+ 24bpp 位图格式 |

**四通道（32位）：**

| 值 | 内存顺序 | 说明 |
|----|----------|------|
| `RGBA` | R,G,B,A,... | |
| `BGRA` | B,G,R,A,... | |
| `ARGB` | A,R,G,B,... | GDI+ 32bpp 位图格式 |
| `ABGR` | A,B,G,R,... | |
| `RGBW` | R,G,B,W,... | 灯带专用，等价于 RGBA |
| `WRGB` | W,R,G,B,... | 灯带专用，等价于 ARGB |

#### LedType（LED 芯片型号）

| 值 | 芯片 | 通道数 |
|----|------|--------|
| `WS2812B = 0x01` | WS2812B | RGB (3) |
| `WS2811` | WS2811 | RGB (3) |
| `WS2813B` | WS2813B | RGB (3) |
| `SK6812_RGBW = 0x04` | SK6812 | RGBW (4) |
| `SK6812_RGB = 0x05` | SK6812 | RGB (3) |
| `WS2818B = 0x06` | WS2818B | RGB (3) |
| `SM16703P` | SM16703P | RGB (3) |
| `WS2815` | WS2815 | RGB (3) |
| `SK9822` | SK9822/APA102 | RGBW (4) |
| `DMX512_RGB` | DMX512 | RGB (3) |
| `DMX512_RGBW` | DMX512 | RGBW (4) |
| `GS8208` | GS8208 | RGB (3) |
| `UCS1903` | UCS1903 | RGB (3) |
| `MT1815` | MT1815 | RGB (3) |
| `TM1913` | TM1913 | RGB (3) |
| `TM1914A` | TM1914A | RGB (3) |

#### ChannelType（传输通道类型）

| 值 | 说明 | 参数格式 |
|----|------|----------|
| `SERIAL` | 串口通信 | `"COM3,921600"` 或 `"COM3,921600,None,8,One"` |
| `TCP` | TCP 客户端 | `"192.168.1.100,8080"` |
| `UDP` | UDP 客户端 | `"192.168.1.100,8080"` |

---

### 4.2 数据帧协议

数据帧为二进制格式，Big-Endian 字节序。

| 偏移 | 长度 | 字段 | 值范围 | 说明 |
|------|------|------|--------|------|
| [0-2] | 3 | 帧头 | 0xDD 0x55 0xEE | 固定帧头 |
| [3-4] | 2 | 组地址 | 0~1024 | ushort, Big-Endian |
| [5-6] | 2 | 设备地址 | 0~4096 | ushort, Big-Endian；0=广播 |
| [7] | 1 | 端口号 | 0~6 | 0=所有端口 |
| [8] | 1 | 功能码 | 0x98/0x99/... | 0x99=颜色帧(从头)，0x98=颜色帧(指定偏移)，0x9B=上电显示，0x9C=关闭上电显示，0x95=设置波特率，0x8E=设置超时 |
| [9] | 1 | 灯带类型 | 见 LedType 枚举 | |
| [10-11] | 2 | 保留/IC起始 | 0~1024 | 功能码 0x99 时为保留字段；0x98 时为 IC 起始位置(1-based) |
| [12-13] | 2 | 颜色数据长度 | 3~3072 | ushort, Big-Endian；须满足 `dataLength + 18 == frame.Length` |
| [14-15] | 2 | 扩展/重复次数 | 1~1024 | ushort, Big-Endian；颜色数据重复点亮次数 |
| [16..^2] | N | 颜色数据 | | 按 ColorFormat 排列的像素字节 |
| [^2-^1] | 2 | 帧尾 | 0xAA 0xBB | 固定帧尾 |

**帧长度约束：**

| 类型 | 最小帧长 | 最大帧长 |
|------|----------|----------|
| RGB (3通道) | 21 字节 | 3090 字节 |
| WRGB (4通道) | 22 字节 | 3090 字节 |

**设备响应消息：**

设备在收到颜色帧后会返回状态字符串，常见错误码：

| 响应码 | 含义 |
|--------|------|
| `HERR` | 指令头错误 |
| `GERR` | 组地址错误 |
| `AERR` | 设备地址错误 |
| `PERR` | 端口地址错误 |
| `CERR` | 功能码错误 |
| `IERR` | LED 灯带类型错误 |
| `LERR` | 数据长度错误 |
| `RERR` | 扩展次数错误 |
| `TERR` | 指令尾部错误 |
| `DERR` | 数据长度与颜色数据字节数不符 |
| `Timeout` | 接收不完整或超时 |
| `RecvEnd` + `DisplayEnd` | 正常接收并显示完成 |

---

### 4.3 FrameRenderModel（抽象基类）

`namespace SpaceCG.Device`

管理帧队列和重复帧去重的抽象基类，`LedRenderBus` 和 `LedStripObject` 均继承自它。

#### 常量

| 常量 | 值 | 说明 |
|------|-----|------|
| `MaxRGBLedCount` | 1024 | RGB 最大灯珠数 |
| `MaxWRGBLedCount` | 768 | WRGB 最大灯珠数 |
| `FrameHeaderLength` | 16 | 帧头字节数 |
| `FrameFooterLength` | 2 | 帧尾字节数 |
| `MaxRenderingFrameCount` | 3 | 渲染队列最大容量（超出时丢弃最旧颜色帧） |

#### 属性

| 属性 | 类型 | 说明 |
|------|------|------|
| `Group` | `ushort` | 组地址，0~1024 |
| `Address` | `ushort` | 设备地址，0~4096（只读） |
| `Port` | `byte` | 端口号，0~6（只读） |
| `Reserved` | `ushort` | 保留数据 |
| `LedType` | `LedType` | 灯带芯片类型（只读） |
| `ColorFormat` | `ColorFormat` | 颜色格式（只读） |
| `LedCount` | `int` | 灯珠数量 |
| `CurrentMaxLedCount` | `ushort` | 当前颜色格式支持的最大灯珠数（protected） |
| `RenderingRepeatInterval` | `int` | 连续相同帧渲染间隔（0=禁用去重） |
| `PendingFrameCount` | `int` | 渲染队列中待处理的帧数量（近似值） |
| `Fps` | `int` | 当前渲染帧率（帧/秒） |
| `Timeout` | `int` | 帧发送后(无响应的帧)等待时间（0~1000 ms） |
| `IsRenderEnabled` | `bool` | 是否允许渲染（默认 true） |
| `Tag` | `object` | 自定义数据 |
| `Comment` | `string` | 备注信息 |

#### 方法

| 方法 | 说明 |
|------|------|
| `AddFrame(byte[] frame)` | 添加通用数据帧（如上电显示指令） |
| `AddColorFrame(byte[] frame)` | 添加颜色数据帧（校验功能码和灯带类型） |
| `AddColorFrame(byte r, byte g, byte b, int start, int repeat)` | 添加纯色帧（RGB 分量） |
| `AddColorFrame(uint color, int start, int repeat, ColorFormat)` | 添加纯色帧（uint 颜色值，需四通道格式） |
| `AddColorFrame(IReadOnlyList<byte> colors, int start, int repeat, ColorFormat)` | 添加颜色帧（字节数组） |
| `AddColorFrame(IReadOnlyList<uint> colors, int start, int repeat, ColorFormat)` | 添加颜色帧（uint 数组） |
| `CreateEmptyColorFrame(int fromPosition, int fillCount, int repeatCount)` | 创建空颜色帧（填充协议头尾，颜色区全零） |
| `ClearRenderingFrames()` | 清空渲染队列并重置状态 |
| `ResetRenderingState()` | 重置渲染计数器和重复帧状态 |

#### 线程安全

- 渲染队列使用 `ConcurrentQueue<byte[]>`，线程安全
- 渲染状态（`_renderingCount`、`_lastRenderingFrame`、`_renderingRepeatCount`）通过 `_renderingLock` 保护
- 属性在初始化后不变，无额外同步开销

---

### 4.4 LedStripObject（灯带对象）

`namespace SpaceCG.Device` | 继承 `FrameRenderModel`

管理灯珠物理坐标映射和渲染优化参数。

#### 属性

| 属性 | 类型 | 说明 |
|------|------|------|
| `UID` | `uint` | 唯一标识，计算方式：`(Port << 16) | Address` |
| `FillCount` | `int` | 渲染填充数量，0=填充所有灯珠；配合 `RepeatCount` 实现渲染优化 |
| `RepeatCount` | `int` | 数据重复次数，默认 1；配合 `FillCount` 实现渲染优化 |
| `LedPoints` | `IReadOnlyList<Point>` | 灯珠坐标列表，顺序即为物理信号顺序 |

#### 事件

| 事件 | 说明 |
|------|------|
| `LedPointsChanged` | 灯珠坐标集合发生变化时触发（增删改），`LedRenderBus` 订阅此事件以更新总线统计信息 |

#### 方法

| 方法 | 说明 |
|------|------|
| `AddPoint(Point point)` | 在末尾添加一颗灯珠 |
| `AddPoint(int index, Point point)` | 在指定索引处插入一颗灯珠 |
| `AddPoints(IEnumerable<Point> points)` | 在末尾添加一组灯珠 |
| `AddPoints(int index, IEnumerable<Point> points)` | 在指定索引处插入一组灯珠 |
| `AddPoints(Point start, Point end)` | 添加从 start 到 end 的直线点集（Bresenham 算法） |
| `RemovePoint(int index)` | 移除指定索引的灯珠 |
| `RemovePoint(Point point)` | 移除指定坐标的灯珠 |
| `RemovePoints(int index, int count)` | 移除指定范围的灯珠 |
| `RemovePoints(IEnumerable<Point> points)` | 移除指定坐标集合的灯珠 |
| `ClearPoints()` | 移除所有灯珠 |
| `ContainsPoint(Point point)` | 判断是否包含指定坐标的灯珠 |

#### 渲染优化参数说明

`FillCount` 和 `RepeatCount` 配合使用可减小数据帧大小：

- **呼吸灯效果**：`FillCount=1, RepeatCount=灯珠数` → 只发送 1 颗灯珠的颜色，硬件自动重复填充所有灯珠
- **对称流水效果**：`FillCount=N, RepeatCount=M` → 发送 N 颗灯珠颜色，硬件重复 M 次

```csharp
// 示例：30 颗灯珠整体呼吸效果
ledStrip.FillCount = 1;     // 只填充 1 颗灯珠的颜色
ledStrip.RepeatCount = 30;  // 硬件自动重复 30 次，覆盖全部灯珠
// 此时数据帧只携带 1 颗灯珠的颜色数据，大幅减小帧大小
```

#### 从 XML 配置创建

```csharp
// XML 格式示例：
// <LedStripObject Address="1" Port="1" LedType="WS2812B" ColorFormat="GRB"
//                 LedPoints="0,0,1,0,2,0,3,0" Group="0" FillCount="0" RepeatCount="1" />

if (LedStripObject.TryCreateInstance(xElement, out var ledStrip))
{
    renderBus.AddLedStrip(ledStrip);
}
```

---

### 4.5 LedRenderBus（渲染总线）

`namespace SpaceCG.Device` | 继承 `FrameRenderModel` | 实现 `IDisposable`

管理传输通道 + 灯带集合 + 渲染线程 + 帧调度。

#### 属性

| 属性 | 类型 | 说明 |
|------|------|------|
| `Type` | `ChannelType` | 传输通道类型（只读） |
| `Name` | `string` | 通道名称，如 `"SERIAL_COM3_921600"`（只读） |
| `IsConnected` | `bool` | 通道是否已连接（只读） |
| `IsRendering` | `bool` | 渲染线程是否运行中（只读） |
| `BusId` | `int` | 总线编号，按创建顺序自动分配 |
| `TotalLedCount` | `int` | 总线上所有灯带的灯珠总数 |
| `LedDevices` | `IEnumerable<ushort>` | 总线上非重复的设备地址集合 |
| `LedStrips` | `IReadOnlyDictionary<uint, LedStripObject>` | 总线上登记的灯带集合（按 UID 索引） |
| `LoopFps` | `int` | 渲染线程循环频率（次/秒），区别于 `Fps`（实际渲染帧率） |
| `ResponseTimeout` | `int` | 设备响应超时时间（10~1000 ms），默认 300ms |
| `Timeout` | `int` | 帧发送后(无响应的帧)等待时间，默认 10ms |

#### 静态属性

| 属性 | 类型 | 说明 |
|------|------|------|
| `Collections` | `IReadOnlyList<LedRenderBus>` | 所有渲染总线实例的全局集合 |
| `FrameExceptionMessages` | `IReadOnlyDictionary<string, string>` | 设备响应错误码 → 中文描述的映射字典 |

#### 方法

**通道管理：**

| 方法 | 说明 |
|------|------|
| `OpenChannel()` | 打开传输通道 |
| `CloseChannel()` | 关闭传输通道 |

**灯带管理：**

| 方法 | 说明 |
|------|------|
| `AddLedStrip(LedStripObject)` | 添加灯带到总线 |
| `RemoveLedStrip(uint uid)` | 按 UID 移除灯带 |
| `RemoveLedStrip(LedStripObject)` | 移除指定灯带 |
| `ClearLedStrips()` | 清空所有灯带 |

**渲染控制：**

| 方法 | 说明 |
|------|------|
| `StartRender()` | 启动渲染线程 |
| `StopRender()` | 停止渲染线程 |
| `PauseRender(ushort address)` | 暂停指定设备的渲染（address=0 暂停所有） |
| `ResumeRender(ushort address)` | 恢复指定设备的渲染 |
| `ClearRender(ushort address, bool clear)` | 清空指定设备的渲染队列；clear=true 时发送黑色帧关闭灯带 |

**二维渲染（按灯珠坐标取像素颜色）：**

| 方法 | 说明 |
|------|------|
| `RenderBitmap(Bitmap bitmap)` | 渲染 GDI+ 位图（自动 LockBits） |
| `RenderPixels(IntPtr pixels, int width, int height, int stride, ColorFormat)` | 渲染像素数据（IntPtr） |
| `RenderPixels(byte* pixels, int width, int height, int stride, ColorFormat)` | 渲染像素数据（unsafe 指针） |

**一维渲染（直接指定颜色值）：**

| 方法 | 说明 |
|------|------|
| `AddColorFrame(ushort address, byte port, byte r, byte g, byte b, int start, int repeat)` | 纯色渲染 |
| `AddColorFrame(ushort address, byte port, uint color, int start, int repeat, ColorFormat)` | uint 颜色值渲染 |
| `AddColorFrame(ushort address, byte port, IReadOnlyList<byte> colors, int start, int repeat, ColorFormat)` | 字节数组渲染 |
| `AddColorFrame(ushort address, byte port, IReadOnlyList<uint> colors, int start, int repeat, ColorFormat)` | uint 数组渲染 |

**设备配置：**

| 方法 | 说明 |
|------|------|
| `SetDeviceBaudRate(ushort address, ushort group, int baudRate)` | 设置设备波特率（需重启生效） |
| `SetDeviceTimeout(ushort address, ushort group, ushort timeout)` | 设置设备通信超时时间 |
| `SetPowerOnColor(ushort address, byte port, uint color, bool isShow, ColorFormat)` | 设置/关闭上电显示颜色 |

**静态工厂方法：**

| 方法 | 说明 |
|------|------|
| `TryCreateInstance(XElement, out LedRenderBus, bool)` | 从 XML 配置节点创建总线实例（含灯带） |

#### 从 XML 配置创建

```csharp
// XML 格式示例：
// <LedRenderBus Type="SERIAL" Params="COM3,921600" LedType="WS2812B" ColorFormat="GRB"
//               Timeout="10" ResponseTimeout="300" Comment="主灯带">
//   <LedStripObject Address="1" Port="1" LedPoints="0,0,1,0,2,0,3,0" />
//   <LedStripObject Address="1" Port="2" LedPoints="0,1,1,1,2,1,3,1" />
// </LedRenderBus>

if (LedRenderBus.TryCreateInstance(xElement, out var renderBus, createLedStrips: true))
{
    renderBus.OpenChannel();
    renderBus.StartRender();
}
```

---

### 4.6 IDrawingDisplay（实时绘制接口）

`namespace SpaceCG.Drawing` | 实现 `IDisposable`

用于从桌面、WPF 元素等源捕获像素数据，通过 `NewDrawingFrame` 事件传递给渲染总线。

#### 属性

| 属性 | 类型 | 说明 |
|------|------|------|
| `Rectangle` | `Rectangle` | 绘制区域，限制在 (0,0,1024,1024) |
| `Interval` | `int` | 每帧绘制间隔，16~1000 ms |
| `DrawingElement` | `object` | 要绘制的显示元素对象 |
| `Fps` | `double` | 实时绘制帧率 |
| `IsDrawing` | `bool` | 是否正在绘制 |

#### 方法

| 方法 | 说明 |
|------|------|
| `StartDrawing()` | 启动实时绘制线程 |
| `StartDrawing(Rectangle, int interval)` | 指定区域和间隔启动 |
| `StopDrawing()` | 停止实时绘制线程 |

#### 事件

| 事件 | 说明 |
|------|------|
| `NewDrawingFrame` | 新帧就绪事件，参数 `DrawingEventArgs` 包含像素数据 |

#### DrawingEventArgs

| 属性 | 类型 | 说明 |
|------|------|------|
| `Source` | `object` | 源对象（如 Bitmap） |
| `Pixels` | `IntPtr` | 像素数据指针 |
| `Stride` | `int` | 扫描宽度（每行字节数） |
| `Width` | `int` | 宽度 |
| `Height` | `int` | 高度 |
| `PixelFormat` | `ColorFormat` | 像素颜色格式 |
| `ElapsedMilliseconds` | `long` | 当前帧绘制耗时（ms） |

#### 实现类

| 类 | 说明 |
|----|------|
| `DrawingDesktop` | 实时捕获桌面指定区域（`Graphics.CopyFromScreen`），输出 BGR 格式 |
| `DrawingWpfElement` | 渲染 WPF 元素内容（在 `Builder` 项目中实现） |
| 自定义 | 实现 `IDrawingDisplay` 接口即可扩展 |

---

### 4.7 ITransportChannel（传输通道接口）

`namespace SpaceCG.IO` | 实现 `IDisposable`

抽象底层 I/O 通道，支持串口、TCP、UDP 三种传输方式。

#### 属性

| 属性 | 类型 | 说明 |
|------|------|------|
| `Type` | `ChannelType` | 通道类型 |
| `Name` | `string` | 通道标识名，如 `"SERIAL_COM3_921600"` |
| `IsConnected` | `bool` | 是否已连接 |
| `Available` | `int` | 接收缓冲区可读字节数 |
| `ReadTimeout` | `int` | 读取超时（ms） |
| `WriteTimeout` | `int` | 写入超时（ms） |
| `Tag` | `object` | 自定义数据 |

#### 方法

| 方法 | 说明 |
|------|------|
| `Open()` | 打开连接 |
| `Close()` | 关闭连接 |
| `ClearReadBuffer()` | 清空接收缓冲区 |
| `ClearWriteBuffer()` | 清空发送缓冲区 |
| `Read(byte[], int, int)` | 同步读取数据 |
| `Write(byte[], int, int)` | 同步写入数据 |

#### 实现类

| 类 | 说明 |
|----|------|
| `SerialPortTransport` | 串口通道（内部使用 `System.IO.Ports.SerialPort`），支持自动匹配串口号 |
| `TcpClientTransport` | TCP 客户端通道（内部使用 `System.Net.Sockets.TcpClient`），含连接状态即时检测 |
| `UdpClientTransport` | UDP 客户端通道（内部使用 `System.Net.Sockets.UdpClient`） |

---

### 4.8 扩展方法

`namespace SpaceCG.Extensions`

#### ColorFormatExtensions

| 方法 | 说明 |
|------|------|
| `GetChannelCount(this ColorFormat)` | 获取通道数量（3 或 4） |
| `GetMaxLedCount(this ColorFormat)` | 获取最大灯珠数（1024 或 768） |
| `GetChannelIndices(this ColorFormat)` | 获取通道索引表 |
| `ConvertColor(byte[], ColorFormat, ColorFormat, byte)` | 颜色格式转换（分配新数组） |
| `ConvertColor(byte[], ColorFormat, ref byte[], int, ColorFormat, byte)` | 颜色格式转换（写入引用数组，减少分配） |
| `ConvertColor(uint[], ColorFormat, ColorFormat)` | uint 颜色格式转换（分配新数组） |
| `ConvertColor(uint[], ColorFormat, ref byte[], int, ColorFormat)` | uint 颜色格式转换（写入引用数组） |
| `ConvertColor(byte*, ColorFormat, int, int, int, ColorFormat, Rectangle, byte)` | unsafe 指针版本颜色格式转换 |

#### DrawingExtensions

| 方法 | 说明 |
|------|------|
| `TryParsePoints(string, out IEnumerable<Point>)` | 解析点集字符串。支持格式：`"1,2,1,3,1,4"` 或 `"1,2,...,1,7"`（自动插值） |
| `GetPoints(Point start, Point end)` | 使用 Bresenham 算法计算两点间直线点集 |
| `GetPoints(Rectangle, ScanAxis, ScanOrder, int stepX, int stepY)` | 按矩形区域生成扫描点集（支持水平/垂直、正向/蛇形） |
| `TryParseRectangle(string[], out Rectangle)` | 解析矩形字符串数组 |

#### FrameExtensions

| 方法 | 说明 |
|------|------|
| `IsValidColorFrame(this byte[])` | 验证是否为有效颜色帧（帧头尾、地址范围、长度一致性） |
| `GetGroup(this byte[])` | 获取组地址 |
| `GetAddress(this byte[])` | 获取设备地址 |
| `GetPort(this byte[])` | 获取端口号 |
| `GetFuncCode(this byte[])` | 获取功能码 |
| `GetLedType(this byte[])` | 获取灯带类型 |
| `GetDataLength(this byte[])` | 获取颜色数据长度 |
| `GetRepeatCount(this byte[])` | 获取扩展次数 |

#### ArrayExtensions

| 方法 | 说明 |
|------|------|
| `FastSequenceEqual(this byte[], byte[])` | 快速字节数组相等比较（P/Invoke `memcmp`） |
| `IndexOf(this IReadOnlyList<byte>, byte)` | 在一维数组中查找指定元素位置 |

#### LedRenderBusExtensions

| 方法 | 说明 |
|------|------|
| `Dispose(this IEnumerable<LedRenderBus>)` | 批量释放总线集合 |
| `GetLedDevices(this IEnumerable<LedRenderBus>)` | 获取所有设备地址集合 |
| `GetLedStrip(this IEnumerable<LedRenderBus>, uint uid)` | 按 UID 查找灯带 |
| `GetLedStrip(this IEnumerable<LedRenderBus>, int address, int port)` | 按地址和端口查找灯带 |
| `GetLedStrips(this IEnumerable<LedRenderBus>)` | 获取所有灯带集合 |
| `StartRender(this IEnumerable<LedRenderBus>)` | 批量启动渲染 |
| `StopRender(this IEnumerable<LedRenderBus>)` | 批量停止渲染 |
| `PauseRender(this IEnumerable<LedRenderBus>)` | 批量暂停渲染 |
| `ResumeRender(this IEnumerable<LedRenderBus>)` | 批量恢复渲染 |
| `ClearRender(this IEnumerable<LedRenderBus>, bool)` | 批量清空渲染队列 |
| `CheckChannelConnection(this IEnumerable<LedRenderBus>)` | 批量检查并重连通道 |

#### SystemExtensions

| 方法 | 说明 |
|------|------|
| `GetSerialDevices()` | 枚举系统中所有可用串口设备（SetupAPI + CfgMgr32） |
| `GetPortName(string searchPattern)` | 根据模糊名称匹配串口号（如 `"CH340"` → `"COM3"`） |

---

## 5. 帧率计算参考

### 串口发送时间

标准串口格式：1 起始位 + 8 数据位 + 1 停止位 = 10 bit/byte。

| 波特率 | 每字节时间 | 1024 字节时间 | 1024 颗 RGB 灯珠时间 (3072 字节) |
|--------|-----------|--------------|----------------------------------|
| 921600 | ~10.85 μs | ~11.6 ms | ~33.33 ms |
| 460800 | ~21.7 μs | ~23.2 ms | ~66.67 ms |
| 230400 | ~43.4 μs | ~46.4 ms | ~133.33 ms |
| 115200 | ~86.8 μs | ~92.8 ms | ~266.67 ms |

### 灯珠点亮时间

| 芯片类型 | 单颗灯珠时间 | 1024 颗时间 |
|----------|-------------|------------|
| WS2812B (800KHz) | ~30 μs | ~30.77 ms |
| APA102/SK9822 (SPI 8Mbps) | ~4 μs | ~4.1 ms |

### 实际帧率

```
实际帧率 ≈ 1 / max(串口发送时间, 灯珠点亮时间)

WS2812B + 921600 + 1024 颗 RGB：1 / max(33.33ms, 30.77ms) ≈ 30 FPS
WS2812B + 921600 +  512 颗 RGB：1 / max(16.67ms, 15.39ms) ≈ 60 FPS
SK9822   + 921600 + 1024 颗 RGB：1 / max(33.33ms, 4.1ms)  ≈ 30 FPS（串口瓶颈）
```

---

## 6. 示例代码

### 示例 1：桌面实时同步到 LED 灯带

```csharp
using SpaceCG.Device;
using SpaceCG.Drawing;
using SpaceCG.IO;

// 1. 创建灯带并配置坐标（灯珠映射到桌面的像素位置）
var ledStrip = new LedStripObject(0x0001, 0x01, LedType.WS2812B, ColorFormat.GRB);
// 添加 32 颗灯珠，水平排列，映射到桌面 (0,0) 到 (31,0) 的像素区域
ledStrip.AddPoints(new System.Drawing.Point(0, 0), new System.Drawing.Point(31, 0));

// 2. 创建渲染总线
var renderBus = new LedRenderBus(ChannelType.SERIAL, "COM3,921600");
renderBus.AddLedStrip(ledStrip);

// 3. 创建桌面实时绘制对象（捕获 32x1 的桌面区域，每 40ms 一帧）
var drawingDesktop = new DrawingDesktop(
    new System.Drawing.Rectangle(0, 0, 32, 1),
    interval: 40
);

// 4. 订阅绘制事件，将像素数据送入渲染总线
drawingDesktop.NewDrawingFrame += (sender, e) =>
{
    renderBus.RenderPixels(e.Pixels, e.Width, e.Height, e.Stride, e.PixelFormat);
};

// 5. 启动
renderBus.OpenChannel();
renderBus.StartRender();
drawingDesktop.StartDrawing();

// 6. 停止
drawingDesktop.StopDrawing();
renderBus.StopRender();
renderBus.CloseChannel();
renderBus.Dispose();
```

### 示例 2：多条灯带 + 二维面板布局

```csharp
// 创建 4 条灯带，组成 16x16 的 LED 面板
var strips = new List<LedStripObject>();
for (byte port = 1; port <= 4; port++)
{
    var strip = new LedStripObject(0x0001, port, LedType.WS2812B, ColorFormat.GRB);
    strips.Add(strip);
}

// 面板布局：4 行 × 16 列，蛇形扫描
// 第 1 行 (port 1): (0,0)→(15,0)  正向
// 第 2 行 (port 2): (15,1)→(0,1)  反向（蛇形）
// 第 3 行 (port 3): (0,2)→(15,2)  正向
// 第 4 行 (port 4): (15,3)→(0,3)  反向（蛇形）
for (int y = 0; y < 4; y++)
{
    var points = new List<System.Drawing.Point>();
    for (int x = 0; x < 16; x++)
    {
        int actualX = (y % 2 == 0) ? x : (15 - x); // 蛇形方向
        points.Add(new System.Drawing.Point(actualX, y));
    }
    strips[y].AddPoints(points);
}

// 创建渲染总线并添加所有灯带
var renderBus = new LedRenderBus(ChannelType.SERIAL, "COM3,921600");
foreach (var strip in strips)
{
    renderBus.AddLedStrip(strip);
}

// 渲染 GDI+ 位图
using (var bitmap = new System.Drawing.Bitmap(16, 4))
{
    using (var g = System.Drawing.Graphics.FromImage(bitmap))
    {
        g.Clear(System.Drawing.Color.Blue);
        g.FillEllipse(System.Drawing.Brushes.Red, 0, 0, 8, 4);
    }
    renderBus.RenderBitmap(bitmap);
}
```

### 示例 3：动态纯色 + 暂停/恢复渲染

```csharp
var renderBus = new LedRenderBus(ChannelType.SERIAL, "COM3,921600");
var ledStrip = new LedStripObject(0x0001, 0x01);
ledStrip.AddPoints(new System.Drawing.Point(0, 0), new System.Drawing.Point(29, 0)); // 30 颗灯珠
renderBus.AddLedStrip(ledStrip);

renderBus.OpenChannel();
renderBus.StartRender();

// 渲染优化：呼吸灯效果（FillCount=1, RepeatCount=30）
ledStrip.FillCount = 1;
ledStrip.RepeatCount = 30;

// 颜色渐变循环
var colors = new uint[] { 0xFFFF0000, 0xFF00FF00, 0xFF0000FF, 0xFFFFFF00 }; // ARGB
int colorIndex = 0;

var timer = new System.Timers.Timer(1000);
timer.Elapsed += (s, e) =>
{
    renderBus.AddColorFrame(0x0001, 0x01, colors[colorIndex], start: 0, repeat: 1, ColorFormat.ARGB);
    colorIndex = (colorIndex + 1) % colors.Length;
};
timer.Start();

// 暂停/恢复渲染
renderBus.PauseRender(0);   // 暂停所有设备（队列仍消费，但不发送）
renderBus.ResumeRender(0);  // 恢复所有设备

// 清理
timer.Stop();
renderBus.StopRender();
renderBus.CloseChannel();
renderBus.Dispose();
```

### 示例 4：多条总线 + 全局管理

```csharp
// 创建多条总线
var bus1 = new LedRenderBus(ChannelType.SERIAL, "COM3,921600");
var bus2 = new LedRenderBus(ChannelType.TCP, "192.168.1.100,8080");

// ... 添加灯带、配置 ...

// 使用扩展方法批量操作
var allBuses = LedRenderBus.Collections;

allBuses.OpenChannel();        // 需自行实现扩展
allBuses.StartRender();        // 批量启动
allBuses.PauseRender();        // 批量暂停
allBuses.ResumeRender();       // 批量恢复
allBuses.ClearRender(clear: true);  // 批量清空并关闭灯带
allBuses.StopRender();         // 批量停止

// 查找指定灯带
var strip = allBuses.GetLedStrip(uid: 0x00010001);
var strip2 = allBuses.GetLedStrip(address: 1, port: 1);

// 批量释放
allBuses.Dispose();
```

### 示例 5：从 XML 配置文件加载

```xml
<!-- led_config.xml -->
<LedRenderBus Type="SERIAL" Params="COM3,921600" LedType="WS2812B" ColorFormat="GRB"
              Timeout="10" ResponseTimeout="300" Comment="主灯带面板">
  <LedStripObject Address="1" Port="1" LedPoints="0,0,1,0,2,0,3,0,4,0,5,0,6,0,7,0"
                  Group="0" FillCount="0" RepeatCount="1" Comment="第1行" />
  <LedStripObject Address="1" Port="2" LedPoints="0,1,1,1,2,1,3,1,4,1,5,1,6,1,7,1"
                  Group="0" FillCount="0" RepeatCount="1" Comment="第2行" />
</LedRenderBus>
```

```csharp
var doc = System.Xml.Linq.XDocument.Load("led_config.xml");
if (LedRenderBus.TryCreateInstance(doc.Root, out var renderBus, createLedStrips: true))
{
    renderBus.OpenChannel();
    renderBus.StartRender();
}
```

---

## 7. 已知问题与建议

### 7.1 IsRenderEnabled 语义

- **当前行为**：`IsRenderEnabled=false` 时，渲染线程仍然从队列取出帧（防止队列堆积），但不发送到设备。
- **注意**：如果需要在暂停期间保留队列数据，应在外部层控制帧的入队。

### 7.2 UdpClientTransport.Write 偏移量处理

- 当 `offset > 0` 时，当前实现使用 `buffer.Skip(offset).ToArray()` 产生额外数组分配。
- 建议：直接使用 `_udpClient.Send(buffer, offset, count, ...)` 重载以避免分配。

### 7.3 WriteFrame 中的响应读取

- 颜色帧（0x98/0x99）和设备配置帧（0x9B/0x9C）的响应读取循环使用 `Thread.Sleep(1)`，在高速场景下可能引入约 15ms 的调度延迟。
- 已评估 `Thread.Sleep(0)`（仅让出当前时间片）和 `Thread.Yield()` 作为替代方案。

### 7.4 LedStripObject 线程安全

- 灯珠坐标的增删操作（`AddPoint`、`RemovePoint` 等）非线程安全，应在初始化阶段完成。
- 运行时修改灯珠坐标需自行加锁。

### 7.5 内存分配

- `CreateEmptyColorFrame` 每次调用 `new byte[frameSize]` 分配新数组。对于 20-60 条灯带的场景，此分配量可接受（~2.8MB/s，GC 开销约 0.2-0.6%）。
- 对于更极端的性能要求，可考虑实现 `ArrayPool<byte>` 帧池。

### 7.6 无连接自动重连

- 渲染线程在检测到通道断开后自动尝试重连（2 秒间隔），但不保证重连成功。
- 对于关键应用，建议在外部实现健康检查 + 告警机制。

### 7.7 异常帧断连

- 连续 8 帧异常时渲染线程主动断开连接，这是为了防止错误数据持续发送。
- 阈值（8）目前硬编码，可根据需要修改为可配置参数。

---

*项目地址：`SharedProjects/SpaceCG.Standard.LedRenderer`*
*目标框架：.NET Framework 4.8 | C# 7.3*
