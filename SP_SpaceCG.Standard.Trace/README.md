# SP_SpaceCG.Standard.Trace — 跨平台日志共享项目

> **SP 前缀 = SharedProject（共享项目）**。共享项目不产生程序集，其源文件在编译期直接合并进所有导入它的项目（partial class / 源码级复用）。

## 项目信息

| 项 | 值 |
|---|---|
| 项目类型 | 共享项目（`.shproj` + `.projitems`） |
| 文件 | `Diagnostics/Trace.cs` |
| 命名空间 | `SpaceCG.Diagnostics` |
| 类型 | `internal static partial class Trace`（单文件，约 400 行） |
| 目标约束 | 跟随消费项目的目标框架（netstandard2.0 / net48 / Unity 均可） |

## 功能概述

`SpaceCG.Diagnostics.Trace` 是与 `System.Diagnostics.Trace` API 兼容的**跨平台日志门面**，核心价值是让同一份代码无需条件编译即可同时运行在 .NET 与 Unity3D 环境：

- **.NET Framework / .NET Core**：所有方法直接转发到 `System.Diagnostics.Trace`，行为与标准 Trace 完全一致
- **Unity3D**：静态构造函数中通过 `Type.GetType("UnityEngine.Application, UnityEngine")` 运行时检测 Unity 环境（非编译期宏），映射到：
  - `WriteLine` / `TraceInformation` → `UnityEngine.Debug.Log`
  - `TraceWarning` → `UnityEngine.Debug.LogWarning`
  - `TraceError` → `UnityEngine.Debug.LogError`
- 反射获取的 Unity 委托已缓存为静态字段，调用无反射开销
- 提供 `IsUnityRuntime` 公开字段供外部判断当前平台

## 消费项目（编译期导入）

| 项目 | 用途 |
|---|---|
| `SpaceCG.Standard` | 全库统一日志出口 |
| `SpaceCG.Standard.NModbus` | `ModbusLogger` 将 NModbus 日志桥接到 Trace |
| `SpaceCG.Standard.LedRenderer` | 传输通道与渲染总线日志 |

对应的 `GlobalSection(SharedMSBuildProjectFiles)` 导入关系见 `202x_SpaceCGStandard.sln`。

## 使用约定

全库统一使用命名空间别名，避免与 `System.Diagnostics.Trace` 冲突：

```csharp
using Trace = SpaceCG.Diagnostics.Trace;

Trace.TraceInformation($"客户端 {endPoint} 已连接");
Trace.TraceWarning($"信号量已存在（异常情况）");
Trace.TraceError($"启动失败：({ex.GetType().Name}) {ex.Message}");
```

## 相关组件

- `LoggerTraceListener`（位于 `SpaceCG.Standard/Diagnostics/`）：异步日志侦听器，追加到 `Trace.Listeners` 后可落盘文件/控制台
- `ModbusLogger`（位于 `SpaceCG.Standard.NModbus`）：`IModbusLogger` → Trace 桥接

## 已知注意事项

1. `Trace` 声明为 `internal`，随消费程序集编译，**不可跨程序集使用**——每个需要日志的程序集都应导入本共享项目
2. Unity 环境检测依赖 `UnityEngine` 程序集已加载；延迟加载场景下首次访问静态成员时检测，若检测失败自动回退 .NET 行为

---
最后更新：2026-11-12
