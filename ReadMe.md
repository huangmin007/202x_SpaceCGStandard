# SpaceCG 标准代码库

SpaceCG 常用的标准代码库，提供跨项目复用的基础组件、工具类和领域特定模块。

## 目录结构

```
202x_SpaceCGStandard/
├── 202x_SpaceCGStandard.sln          # 解决方案文件 (VS 2022)
├── ReadMe.md                          # 本文档
├── Builder/                           # 构建工程（生成 DLL）
│   ├── SpaceCG.Standard/              # 核心基础库 (.NET Standard 2.0)
│   ├── SpaceCG.Standard.LedRenderer/  # LED 渲染库 WPF 扩展 (.NET 4.8)
│   ├── SpaceCG.Standard.Wpf/          # WPF 通用扩展库 (.NET 4.8)
│   └── SpaceCG.WindowsApi/            # Windows API 封装库 (.NET 4.8)
├── SharedProjects/                    # 共享项目（代码共享，不独立生成 DLL）
│   ├── SpaceCG.Standard.LedRenderer/  # LED 渲染核心代码
│   └── SpaceCG.Standard.Wpf/          # WPF 共享代码（预留）
└── Z_TestWpfApp/                      # 集成测试 WPF 应用
```

## 模块说明

### Builder/SpaceCG.Standard — 核心基础库

- **目标框架**: .NET Standard 2.0
- **用途**: 跨平台基础工具库，支持 WPF、Unity3D 等任意 .NET 运行时
- **依赖**: 零第三方 NuGet 包，仅使用 .NET Standard 2.0 自带 API
- **模块**:
  - `Trace` — 日志跟踪
  - `Generic/` — 通用组件：环形缓冲区、校验和（CRC8/16/32/64、Sum/XOR/BCC/LRC）、对象池、协议解析器、异步日志
  - `Extensions/` — 扩展方法集：数组搜索、文件管理、HTTP 传输、反射、数学映射、网络工具、路径工具、Socket、字符串解析、类型转换、XML 模板
  - `Net/` — 网络通信框架：XML-RPC 服务端/客户端、增强 TCP 客户端、HTTP 静态文件服务、WebSocket 双向通信
- **详细文档**: 见各模块 `README.md`

### SharedProjects/SpaceCG.Standard.LedRenderer — LED 渲染共享代码

- **用途**: LED 灯带渲染核心逻辑，通过共享项目机制被 Builder 项目引用
- **模块**:
  - `Device/` — 颜色格式定义、帧渲染模型、LED 渲染总线、灯带对象
  - `Drawing/` — 实时绘制接口 (`IDrawingDisplay`)、桌面截屏绘制实现
  - `IO/` — 传输通道接口及串口/TCP/UDP 实现
  - `Extensions/` — 字节比较、颜色格式转换、绘制扩展、帧协议扩展、系统设备枚举
- **详细文档**: 见 `SharedProjects/SpaceCG.Standard.LedRenderer/ReadMe.md`

### Builder/SpaceCG.Standard.LedRenderer — LED 渲染库（WPF 构建）

- **目标框架**: .NET Framework 4.8
- **引用**:
  - `SpaceCG.Standard` (.NET Standard 2.0)
  - `SharedProjects/SpaceCG.Standard.LedRenderer` (共享代码)
  - WPF 程序集 (PresentationCore, PresentationFramework, WindowsBase)
- **模块**:
  - `Device/LedRenderControl.cs` — LED 渲染控制器（WPF 集成）
  - `Drawing/DrawingWpfElement.cs` — WPF 元素实时绘制
  - `Extensions/` — WPF 画刷颜色扩展、绘制扩展

### Builder/SpaceCG.WindowsApi — Windows API 封装

- **目标框架**: .NET Framework 4.8
- **用途**: Windows 原生 API 封装，设备枚举与热插拔监听
- **模块**:
  - `Extensions/SystemExtensions.cs` — 设备枚举查询（SetupAPI / CfgMgr32）
  - `Generic/DeviceWatcher.cs` — 设备热插拔监听（WM_DEVICECHANGE）
  - `Interop/NativeMethods.cs` — P/Invoke 声明
- **详细文档**: 见 `Builder/SpaceCG.WindowsApi/README.md`

### Builder/SpaceCG.Standard.Wpf — WPF 通用扩展

- **目标框架**: .NET Framework 4.8 (x64)
- **用途**: WPF 窗体、UI 控件等通用扩展（当前为预留空壳项目）
- **模块**: `Control/`, `Extensions/`（待实现）

### Z_TestWpfApp — 集成测试应用

- **目标框架**: .NET Framework 4.8
- **用途**: 集成测试 RPC 通信、LED 渲染、设备监听等功能

## 项目依赖关系

```
Z_TestWpfApp (WPF 测试应用)
  ├── SpaceCG.Standard.LedRenderer  (Builder, .NET 4.8)
  │     ├── SpaceCG.Standard  (.NET Standard 2.0)
  │     ├── SharedProjects/SpaceCG.Standard.LedRenderer
  │     ├── PresentationCore
  │     ├── PresentationFramework
  │     └── WindowsBase
  ├── SpaceCG.Standard  (.NET Standard 2.0)
  └── SpaceCG.WindowsApi  (.NET 4.8)

SpaceCG.Standard.Wpf  (Builder, .NET 4.8)
  └── 无项目引用（独立库）

SpaceCG.WindowsApi  (.NET 4.8)
  └── 无项目引用（链接引入 Trace.cs）
```

## 技术栈

| 项目 | 目标框架 | 语言版本 |
|------|---------|---------|
| SpaceCG.Standard | .NET Standard 2.0 | C# 7.3 |
| SpaceCG.Standard.LedRenderer | .NET Framework 4.8 | C# 7.3 |
| SpaceCG.Standard.Wpf | .NET Framework 4.8 | C# 7.3 |
| SpaceCG.WindowsApi | .NET Framework 4.8 | C# 7.3 |

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
| LED 渲染库完整 API | `SharedProjects/SpaceCG.Standard.LedRenderer/ReadMe.md` |
| Windows API 封装 | `Builder/SpaceCG.WindowsApi/README.md` |
| Generic 通用组件 | `Builder/SpaceCG.Standard/Generic/README.md` |
| Net 网络通信框架 | `Builder/SpaceCG.Standard/Net/README.md` |
| Extensions 扩展方法集 | `Builder/SpaceCG.Standard/Extensions/README.md` |

## 待补充 / 未来规划

- [ ] **SpaceCG.Standard.Wpf** — 完善 WPF 窗体、UI 控件扩展
- [ ] **SpaceCG.Standard.U3D** — Unity3D 适配层（预留）
- [ ] **SpaceCG.Standard.Device** — 设备抽象层（预留）
- [ ] **单元测试** — 为核心算法（CRC、环形缓冲、协议解析、帧渲染）补充单元测试
- [ ] **性能基准** — 对性能关键路径建立 BenchmarkDotNet 基准测试
- [ ] **NuGet 打包** — 建立 CI/CD 流水线，发布内部 NuGet 包
- [ ] **API 文档** — 基于 XML 文档注释生成 Sandcastle/DocFX 静态文档站点
