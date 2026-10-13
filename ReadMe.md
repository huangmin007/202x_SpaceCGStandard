# SpaceCG 标准代码库

SpaceCG 常用的标准代码库，提供跨项目复用的基础组件、工具类和领域特定模块。

## 目录结构

```
202x_SpaceCGStandard/
├── 202x_SpaceCGStandard.sln              # 解决方案文件
├── ReadMe.md                             # 本文档
├── SpaceCG.Standard/                     # 核心基础库 (netstandard2.0)
├── SpaceCG.Standard.Devices/             # 设备 I/O 传输通道 (net48)
├── SpaceCG.Standard.Devices.LedRenderer/ # 全彩 LED 灯带渲染 (net48)
├── SpaceCG.Standard.Devices.NModbus/     # NModbus 从站/同步器 (net48)
├── SpaceCG.Standard.Windows/             # Windows 设备枚举与热插拔 (net48)
├── SpaceCG.Standard.Wpf/                 # WPF 集成与扩展 (net48)
├── Z_TestWpfApp/                         # 集成测试 WPF 应用 (net48)
└── packages/
    └── NModbus.3.0.83/                   # 本地包目录
```

## 模块说明

### SpaceCG.Standard — 核心基础库

- **目标框架**：.NET Standard 2.0（C# 7.3，零第三方 NuGet 依赖，`AllowUnsafeBlocks`）
- **用途**：可被所有兼容 .NET Standard 2.0 的项目（net48 / .NET Core / Unity3D）引用
- **模块**：
  - `Diagnostics/` — `Trace`（跨平台日志，Unity 运行时自动适配）、`LoggerTraceListener`（异步文件/控制台日志）
  - `Generic/` — 校验和、CRC8/16/32/64、环形缓冲区、对象池、协议解析器
  - `Extensions/` — 12 个扩展方法类（数组、集合、文件、HTTP、反射、数学、网络、路径、字符串、Socket、类型、XML）
  - `Net/` — XML-RPC 服务端/客户端、HTTP 静态文件服务、WebSocket 双向通信
- **详细文档**：[SpaceCG.Standard/README.md](./SpaceCG.Standard/README.md)

### SpaceCG.Standard.Devices — 设备通信底座

- **目标框架**：.NET Framework 4.8
- **依赖**：`SpaceCG.Standard`
- **模块**：
  - `IO/` — `ITransportChannel` 抽象 + 串口/TCP/UDP 实现、`RequestResponseSession`（请求-响应会话）、`ModbusRtu`
  - `Extensions/` — 串口扩展方法（收发解析、串口号模糊匹配）
- **详细文档**：[SpaceCG.Standard.Devices/README.md](./SpaceCG.Standard.Devices/README.md)

### SpaceCG.Standard.Devices.LedRenderer — LED 灯带渲染

- **目标框架**：.NET Framework 4.8
- **依赖**：`SpaceCG.Standard.Devices`
- **模块**：
  - `Device/` — 颜色格式、帧渲染模型、灯带对象、渲染总线
  - `Drawing/` — `IDrawingDisplay` 接口与桌面截屏实现
  - `Extensions/` — 字节比较、颜色格式转换、绘制扩展、帧协议扩展
- **说明**：本项目不依赖 WPF；WPF 控件与元素绘制位于 `SpaceCG.Standard.Wpf`
- **详细文档**：[SpaceCG.Standard.Devices.LedRenderer/README.md](./SpaceCG.Standard.Devices.LedRenderer/README.md)

### SpaceCG.Standard.Devices.NModbus — Modbus 从站

- **目标框架**：.NET Framework 4.8
- **依赖**：`SpaceCG.Standard`、NuGet `NModbus 3.0.83`
- **状态**：开发中（Modbus TCP 从站监听、寄存器点源、同步器），暂无独立 README

### SpaceCG.Standard.Windows — Windows 平台能力

- **目标框架**：.NET Framework 4.8
- **依赖**：`SpaceCG.Standard`
- **模块**：
  - `Extensions/SystemExtensions.cs` — 设备枚举查询（SetupAPI + CfgMgr32）：USB、串口、磁盘、卷
  - `Generic/DeviceWatcher.cs` — 设备热插拔监听（WM_DEVICECHANGE 消息窗口）
  - `Interop/NativeMethods.cs` — P/Invoke 声明占位 partial class（实际 P/Invoke 内嵌于上述两个文件）
- **详细文档**：[SpaceCG.Standard.Windows/README.md](./SpaceCG.Standard.Windows/README.md)

### SpaceCG.Standard.Wpf — WPF 集成库

- **目标框架**：.NET Framework 4.8
- **依赖**：`SpaceCG.Standard.Devices.LedRenderer`、`SpaceCG.Standard`；WPF 程序集（PresentationCore / PresentationFramework / WindowsBase）
- **模块**：
  - `Device/LedRenderControl.cs` — LED 渲染控制器
  - `Drawing/DrawingWpfElement.cs` — WPF 元素实时绘制
  - `Extensions/` — 画刷、位图扩展

### Z_TestWpfApp — 集成测试应用

- **目标框架**：.NET Framework 4.8（旧式 csproj）
- **用途**：集成验证 RPC、LED 渲染、串口/设备监听等能力
- **依赖**：`SpaceCG.Standard.Devices.LedRenderer`、`SpaceCG.Standard.Devices`、`SpaceCG.Standard.Windows`、`SpaceCG.Standard.Wpf`、`SpaceCG.Standard`

## 项目依赖关系

```
Z_TestWpfApp (net48)
  ├── SpaceCG.Standard.Devices.LedRenderer (net48)
  │     └── SpaceCG.Standard.Devices (net48)
  │           └── SpaceCG.Standard (netstandard2.0)
  ├── SpaceCG.Standard.Wpf (net48)
  │     ├── SpaceCG.Standard.Devices.LedRenderer
  │     └── SpaceCG.Standard
  ├── SpaceCG.Standard.Windows (net48)
  │     └── SpaceCG.Standard
  └── SpaceCG.Standard

SpaceCG.Standard.Devices.NModbus (net48)
  ├── SpaceCG.Standard
  └── NModbus 3.0.83 (NuGet)

SpaceCG.Standard (netstandard2.0)  → 无项目引用、无 NuGet 依赖
```

## 技术栈

| 项目 | 目标框架 | 语言版本 |
|------|---------|---------|
| SpaceCG.Standard | .NET Standard 2.0 | C# 7.3 |
| SpaceCG.Standard.Devices | .NET Framework 4.8 | C# 7.3 |
| SpaceCG.Standard.Devices.LedRenderer | .NET Framework 4.8 | C# 7.3 |
| SpaceCG.Standard.Devices.NModbus | .NET Framework 4.8 | C# 7.3 |
| SpaceCG.Standard.Windows | .NET Framework 4.8 | C# 7.3 |
| SpaceCG.Standard.Wpf | .NET Framework 4.8 | C# 7.3 |
| Z_TestWpfApp | .NET Framework 4.8 | C# 7.3 |

## 编码规范摘要

- 命名：PascalCase（类/方法/属性/常量）、\_camelCase（私有字段）、camelCase（参数/局部变量）、I 前缀（接口）
- 缩进：4 空格，禁止 Tab
- 大括号：Allman 风格（换行）
- 编码：UTF-8 with BOM
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
| 设备传输通道 | `SpaceCG.Standard.Devices/README.md` |
| LED 渲染库 | `SpaceCG.Standard.Devices.LedRenderer/README.md` |
| Windows 设备枚举/监听 | `SpaceCG.Standard.Windows/README.md` |

## 待补充 / 未来规划

- [x] **NuGet 打包** — `SpaceCG.Standard` 与 `SpaceCG.Standard.Devices.LedRenderer` 已启用 `GeneratePackageOnBuild`
- [x] **核心库顶层文档** — 新增 `SpaceCG.Standard/README.md`
- [ ] **SpaceCG.Standard.Devices.NModbus** — 补充独立 README 与单元测试
- [ ] **SpaceCG.Standard.Wpf** — 补充 README
- [ ] **单元测试** — 为核心算法（CRC、环形缓冲、协议解析、帧渲染）补充单元测试
- [ ] **性能基准** — 对性能关键路径建立 BenchmarkDotNet 基准测试
- [ ] **CI/CD** — 建立流水线，发布内部 NuGet 包
- [ ] **API 文档** — 基于 XML 文档注释生成 Sandcastle/DocFX 静态文档站点
