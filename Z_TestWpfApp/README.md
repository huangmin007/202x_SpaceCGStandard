# Z_TestWpfApp — 库功能集成测试台

> 旧式 csproj 的 WPF 可执行程序（`OutputType=WinExe`），用于对解决方案内各库做**人工冒烟测试**，不是自动化测试工程（无 NUnit/MSTest）。代码中保留大量硬编码绝对路径与注释掉的历史测试代码，属测试台性质，不以生产标准要求。

## 项目信息

| 项 | 值 |
|---|---|
| 目标框架 | .NET Framework 4.8（旧式 csproj，packages.config 还原） |
| 平台 | AnyCPU，`AllowUnsafeBlocks=true` |
| 项目引用 | `SpaceCG.Standard`、`SpaceCG.Standard.Wpf`、`SpaceCG.Standard.LedRenderer`、`SpaceCG.Standard.NModbus` |
| 外部引用 | NuGet `NModbus 3.0.83`、标准 WPF 程序集 |

> 注意：只能用桌面 MSBuild（VS 的 MSBuild.exe）构建；`dotnet build` 缺标记编译支持会误报 CS5001 / CS0103。

## 文件与用途

```
Z_TestWpfApp/
├── App.xaml / App.xaml.cs           # 标准入口，StartupUri=MainWindow
├── MainWindow.xaml / .cs            # 主测试窗口
├── SpaceCG/RfidDevice.cs            # RFID 读卡器串口轮询测试类（当前未接线）
├── Resources/Config.xml             # 灯带场景 + RFID 联动完整示例配置
└── Properties/                      # AssemblyInfo / 模板 Resources、Settings
```

## 测试内容

### MainWindow（800×450）
- **左列 Canvas**：灯带式 Rectangle 渲染布局（LedRenderer 视觉验证）
- **右列**：
  - `sg:ImageToggleButton` + 4 个 `sg:ImageRadioButton`（A/B 两组）——验证 `ImageButtons` 族控件的 NormalSource/CheckedSource 与分组互斥
  - `sg:ImageSequence`——验证序列帧控件：zip / 目录两种数据源、Seek、播放暂停
- **键盘操作**：
  - `D0`/`D1` → `imageSeq.Seek(99)` / `Seek(3)`
  - `D2` → 目录模式加载（AutoPlay=false）
  - `D3` → zip 模式加载（AutoPlay=true）
  - `Space` → 播放/暂停切换
  - `D5` → 窗口语言切 zh-CN
- 启动时挂接 `LoggerTraceListener`（验证 SpaceCG.Diagnostics 日志）并创建 `DemoControlManager`

### RfidDevice（未接线）
RFID 读卡器串口轮询：`LongRunning` 线程内断线重连 + `TransceiveAsync` 发送 Modbus RTU 读保持寄存器帧（`02 03 00 00 00 02 C4 38`），每秒统计通信次数。

### Resources/Config.xml
`LedRenderManager` 配置示例：`DrawingDisplay`（截帧区域/间隔）、`LedRenderBus`（CH343,921600 串口总线，6 个 Port 各 171 灯珠 GRB）、6 个场景（Id 0–5，绑定 RFID 卡号，Action 使用 `FlowLinearBrush` / `GroupBlackAndFlowLinearBrush` / `ColorBrush` 笔刷工厂）。刷卡切换灯带流光场景的完整端到端示例。

## 运行前提

1. 资源文件：XAML 中 `ImageSequence`/按钮图片引用**硬编码绝对路径**（外部项目 bin 目录与 `D:\SpaceCG\...\Car_KFs.zip`），换机器需改路径或自备资源
2. 灯带硬件：Config.xml 中串口（CH343）需真实存在；纯软仿真可注释掉 LedRenderBus 配置
3. RPC 端口 2000 被 `DemoControlManager` 占用

## 已知问题

1. 多处硬编码绝对路径（可移植性差）
2. `RfidDevice` 存在若干实现问题（详见代码内注释；该类当前未被调用）
3. `MainWindow` 内 `zipArchive`、`BitmapFiles` 等死代码字段
4. packages.config 旧式还原，建议迁移 PackageReference

---
最后更新：2026-11-12
