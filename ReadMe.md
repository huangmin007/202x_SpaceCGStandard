# SpaceCG 标准代码库

SpaceCG 常用的标准代码库，提供跨项目复用的基础组件、工具类和领域特定模块（设备通信、LED 灯带渲染、WPF 控件、Windows 设备枚举）。

> **SP 前缀 = SharedProject（共享项目）**：不产生程序集，源文件在编译期直接合并进导入它的项目。

## 解决方案结构

```
202x_SpaceCGStandard/
├── 202x_SpaceCGStandard.sln                    # 解决方案文件
├── ReadMe.md                                   # 本文档
├── SpaceCG.Standard/                           # 核心基础库 (netstandard2.0)
├── SpaceCG.Standard.Windows/                   # Windows 设备枚举与热插拔 (net48)
├── SpaceCG.Standard.LedRenderer/               # 全彩 LED 灯带渲染（设备层，net48）
├── SpaceCG.Standard.LedRenderer.Wpf/           # 灯带渲染 WPF 层 (net48)
├── SpaceCG.Standard.NModbus/                   # NModbus 扩展与总线同步器 (net48)
├── SpaceCG.Standard.Wpf/                       # WPF 控件与集成库 (net48)
├── Z_TestWpfApp/                               # 库功能集成测试台 (net48, WinExe)
├── SP_SpaceCG.Standard.Trace/                  # [共享项目] 跨平台日志（SpaceCG.Diagnostics.Trace）
├── SP_SpaceCG.Interop.SerialPortResolver/      # [共享项目] Windows 串口号解析（SetupAPI）
└── packages/
    └── NModbus.3.0.83/                         # 本地 NuGet 包目录
```

## 模块说明

### SpaceCG.Standard — 核心基础库

- **目标框架**：.NET Standard 2.0（C# 7.3，零第三方 NuGet 依赖，`AllowUnsafeBlocks`）
- **用途**：可被所有兼容 .NET Standard 2.0 的项目（net48 / .NET Core / Unity3D）引用
- **模块**：
  - `Diagnostics/` — `LoggerTraceListener`（异步日志侦听器）；跨平台 `Trace` 来自共享项目 `SP_SpaceCG.Standard.Trace`
  - `Generic/` — 校验和、CRC8/16/32/64、环形缓冲区、对象池、协议解析器
  - `Extensions/` — 12 个扩展方法类（数组、集合、文件、HTTP、反射、数学、网络、路径、字符串、Socket、类型、XML）
  - `Net/` — XML-RPC 服务端/客户端、HTTP 静态文件服务、WebSocket 双向通信
- **详细文档**：[SpaceCG.Standard/README.md](./SpaceCG.Standard/README.md)

### SpaceCG.Standard.Windows — Windows 平台能力

- **目标框架**：.NET Framework 4.8
- **依赖**：`SpaceCG.Standard`
- **模块**：
  - `Extensions/SystemExtensions.cs` — 设备枚举查询（SetupAPI + CfgMgr32）：USB、串口、磁盘、卷
  - `Generic/DeviceWatcher.cs` — 设备热插拔监听（WM_DEVICECHANGE 消息窗口）
  - `Interop/NativeMethods.cs` — P/Invoke 声明占位 partial class（实际 P/Invoke 内嵌于上述两个文件）
- **详细文档**：[SpaceCG.Standard.Windows/README.md](./SpaceCG.Standard.Windows/README.md)

### SpaceCG.Standard.LedRenderer — LED 灯带渲染（设备层）

- **目标框架**：.NET Framework 4.8
- **依赖**：无项目引用；导入共享项目 `SP_SpaceCG.Standard.Trace`、`SP_SpaceCG.Interop.SerialPortResolver`
- **模块**：
  - `Device/` — 颜色格式、帧渲染模型、灯带对象、渲染总线
  - `Drawing/` — `IDrawingDisplay` 接口与桌面截屏实现
  - `IO/` — 串口/TCP/UDP 传输通道
  - `Extensions/` — 字节比较、颜色格式转换、绘制扩展、帧协议扩展
- **说明**：本项目不依赖 WPF；WPF 截帧/编排/笔刷在 `SpaceCG.Standard.LedRenderer.Wpf`
- **详细文档**：[SpaceCG.Standard.LedRenderer/README.md](./SpaceCG.Standard.LedRenderer/README.md)

### SpaceCG.Standard.LedRenderer.Wpf — 灯带渲染 WPF 层

- **目标框架**：.NET Framework 4.8（`UseWPF`，`GeneratePackageOnBuild`）
- **依赖**：`SpaceCG.Standard.LedRenderer`、`SpaceCG.Standard`
- **模块**：
  - `Drawing/DrawingWpfElement.cs` — `IDrawingDisplay` 的 WPF 实现（RenderTargetBitmap 截帧 → 像素分发）
  - `Controls/LedRenderManager.cs` — XML 配置驱动的场景渲染编排器
  - `Extensions/BrushExtensions.cs` — 动画笔刷工厂（渐变/闪烁/流光，供场景配置反射调用）
- **详细文档**：[SpaceCG.Standard.LedRenderer.Wpf/README.md](./SpaceCG.Standard.LedRenderer.Wpf/README.md)

### SpaceCG.Standard.NModbus — NModbus 扩展与总线同步器

- **目标框架**：.NET Framework 4.8
- **依赖**：`SpaceCG.Standard`、NuGet `NModbus 3.0.83`；导入两个共享项目
- **模块**：`ModbusBusSynchronizer`/`ModbusSynchronizer`（寄存器同步）、`LocalDataStore` + `RegistersPointSource<T>`（数据区/点源）、串口扩展（收发/解析/配置创建）、IO 适配
- **说明**：基于 NModbus NuGet 包的扩展层，`NModbus/` 目录不是上游源码 fork
- **详细文档**：[SpaceCG.Standard.NModbus/README.md](./SpaceCG.Standard.NModbus/README.md)

### SpaceCG.Standard.Wpf — WPF 控件与集成库

- **目标框架**：.NET Framework 4.8（SDK 风格 csproj，`UseWPF`）
- **依赖**：`SpaceCG.Standard`
- **模块**：
  - `Controls/` — ImageButtons 族（Toggle/Radio）、ImageSequence（序列帧播放）、OverlayContentWindow（悬浮窗口）、DemoBaseWindow / DemoControlManager（演示导航框架）
  - `Converters/` — Boolean/Visibility 转换器
  - `Extensions/` — Window/UIElement/Bitmap/Application 扩展
  - `ComponentModel/` — NotifyPropertyChangedBase（INPC 基类）
  - XAML URI：`http://schemas.spacecg.cn/xaml`（前缀 `sg`）
- **详细文档**：[SpaceCG.Standard.Wpf/README.md](./SpaceCG.Standard.Wpf/README.md)

### 共享项目（SharedProject）

| 项目 | 内容 | 导入方 |
|---|---|---|
| `SP_SpaceCG.Standard.Trace` | `SpaceCG.Diagnostics.Trace` 跨平台日志（.NET 转发 + Unity 运行时适配） | SpaceCG.Standard、NModbus、LedRenderer |
| `SP_SpaceCG.Interop.SerialPortResolver` | `SerialPortResolver`：设备描述 → 串口号解析（SetupAPI，仅 Windows） | NModbus、LedRenderer |

- **详细文档**：[SP_SpaceCG.Standard.Trace/README.md](./SP_SpaceCG.Standard.Trace/README.md)、[SP_SpaceCG.Interop.SerialPortResolver/README.md](./SP_SpaceCG.Interop.SerialPortResolver/README.md)

### Z_TestWpfApp — 库功能集成测试台

- **目标框架**：.NET Framework 4.8（旧式 csproj，需桌面 MSBuild 构建）
- **用途**：人工冒烟测试 ImageSequence / ImageButtons / 日志监听 / 串口收发 / 灯带场景配置（`Resources/Config.xml` 为 RFID 刷卡切换灯带场景的端到端示例）
- **依赖**：`SpaceCG.Standard`、`SpaceCG.Standard.Wpf`、`SpaceCG.Standard.LedRenderer`、`SpaceCG.Standard.NModbus`
- **详细文档**：[Z_TestWpfApp/README.md](./Z_TestWpfApp/README.md)

## 项目依赖关系

```
Z_TestWpfApp (net48, WinExe)
  ├── SpaceCG.Standard.Wpf ──────────────? SpaceCG.Standard
  ├── SpaceCG.Standard.LedRenderer.Wpf ──? SpaceCG.Standard.LedRenderer + SpaceCG.Standard
  ├── SpaceCG.Standard.LedRenderer        （无项目引用；仅导入共享项目）
  ├── SpaceCG.Standard.NModbus ──────────? SpaceCG.Standard + NuGet NModbus 3.0.83
  └── SpaceCG.Standard

SpaceCG.Standard.Windows ─────────────────? SpaceCG.Standard

SpaceCG.Standard                           （无项目引用、无 NuGet 依赖）
  └── 导入 SP_SpaceCG.Standard.Trace（共享项目）

共享项目源码合并（编译期）：
  SP_SpaceCG.Standard.Trace             → SpaceCG.Standard / NModbus / LedRenderer
  SP_SpaceCG.Interop.SerialPortResolver → NModbus / LedRenderer
```

## 技术栈

| 项目 | 目标框架 | 类型 |
|------|---------|------|
| SpaceCG.Standard | .NET Standard 2.0 | 类库（NuGet 打包） |
| SpaceCG.Standard.Windows | .NET Framework 4.8 | 类库 |
| SpaceCG.Standard.LedRenderer | .NET Framework 4.8 | 类库（NuGet 打包） |
| SpaceCG.Standard.LedRenderer.Wpf | .NET Framework 4.8 | WPF 类库（NuGet 打包） |
| SpaceCG.Standard.NModbus | .NET Framework 4.8 | 类库 |
| SpaceCG.Standard.Wpf | .NET Framework 4.8 | WPF 类库 |
| Z_TestWpfApp | .NET Framework 4.8 | WPF 可执行程序 |
| SP_SpaceCG.Standard.Trace / SP_SpaceCG.Interop.SerialPortResolver | （跟随消费项目） | 共享项目 |

语言版本统一 C# 7.3。

## 编码规范摘要

- 命名：PascalCase（类/方法/属性/常量）、\_camelCase（私有字段）、camelCase（参数/局部变量）、I 前缀（接口）
- 缩进：4 空格，禁止 Tab；大括号 Allman 风格；编码 UTF-8 with BOM
- 资源管理：实现 IDisposable，using 语句释放非托管资源
- 性能：热路径避免分配，ArrayPool 池化管理，Buffer.BlockCopy 高效复制
- 线程安全：ConcurrentCollection、lock、CancellationToken 协作取消
- 异常：不捕获 Exception 基类，使用 Try 模式避免性能关键路径异常
- 完整规范见：用户规则 > C# 编码规范

## 文档索引

| 文档 | 路径 |
|------|------|
| 核心基础库总览 | `SpaceCG.Standard/README.md` |
| 扩展方法集 | `SpaceCG.Standard/Extensions/README.md` |
| 通用组件 | `SpaceCG.Standard/Generic/README.md` |
| 网络与 RPC | `SpaceCG.Standard/Net/README.md` |
| RPC 设计文档 | `SpaceCG.Standard/Net/RPC 服务框架.md` |
| XML-RPC 协议规范 | `SpaceCG.Standard/Net/远程过程调用(XML-RPC)消息协议.md` |
| LED 渲染库（设备层） | `SpaceCG.Standard.LedRenderer/README.md` |
| LED 渲染 WPF 层 | `SpaceCG.Standard.LedRenderer.Wpf/README.md` |
| NModbus 扩展 | `SpaceCG.Standard.NModbus/README.md` |
| Windows 设备枚举/监听 | `SpaceCG.Standard.Windows/README.md` |
| WPF 控件库 | `SpaceCG.Standard.Wpf/README.md` |
| 集成测试台 | `Z_TestWpfApp/README.md` |
| 共享项目：日志 | `SP_SpaceCG.Standard.Trace/README.md` |
| 共享项目：串口解析 | `SP_SpaceCG.Interop.SerialPortResolver/README.md` |

## 待补充 / 未来规划

- [x] **NuGet 打包** — `SpaceCG.Standard`、`SpaceCG.Standard.LedRenderer`、`SpaceCG.Standard.LedRenderer.Wpf` 已启用 `GeneratePackageOnBuild`
- [x] **核心库顶层文档** — `SpaceCG.Standard/README.md`
- [x] **SpaceCG.Standard.NModbus README** — 已补
- [x] **SpaceCG.Standard.Wpf README** — 已补
- [ ] **单元测试** — 为核心算法（CRC、环形缓冲、协议解析、帧渲染）补充单元测试
- [ ] **性能基准** — 对性能关键路径建立 BenchmarkDotNet 基准测试
- [ ] **CI/CD** — 建立流水线，发布内部 NuGet 包
- [ ] **API 文档** — 基于 XML 文档注释生成 Sandcastle/DocFX 静态文档站点

---
最后更新：2026-11-12
