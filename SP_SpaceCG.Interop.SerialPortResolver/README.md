# SP_SpaceCG.Interop.SerialPortResolver — Windows 串口号解析共享项目

> **SP 前缀 = SharedProject（共享项目）**。共享项目不产生程序集，其源文件在编译期直接合并进所有导入它的项目（源码级复用，无 DLL 依赖）。

## 项目信息

| 项 | 值 |
|---|---|
| 项目类型 | 共享项目（`.shproj` + `.projitems`） |
| 文件 | `Interop/SerialPortResolver.cs` |
| 命名空间 | `SpaceCG.Interop` |
| 公开类型 | `public static partial class SerialPortResolver` |
| 内部类型 | `internal static partial class NativeMethods`（SetupAPI P/Invoke 声明） |
| 平台 | **仅 Windows**（依赖 setupapi.dll），已标注 `[SuppressUnmanagedCodeSecurity]` |

## 功能概述

基于 **Windows SetupAPI** 枚举系统中的端口类设备（`GUID_DEVCLASS_PORTS`），实现两类能力：

1. **枚举**：列出所有串口设备的友好名称（如 `"USB-SERIAL CH340 (COM3)"`）
2. **解析**：把"设备描述 / 模糊模式"解析为实际串口号（`"CH343,921600"` / `"CH340"` → `"COM3"`），支持直接传入已是 `COMx` 格式的值（正则 `^COM[0-9]{1,3}$` 校验后原样返回）

## 公开 API

| 成员 | 说明 |
|---|---|
| `Regex PortNameRegexForWindows` | Windows 串口名正则（忽略大小写） |
| `IEnumerable<string> GetPortFriendlyNames()` | 枚举全部端口类设备的友好名称 |
| `string GetPortName(string searchPattern)` | 解析搜索模式为串口号；无法匹配返回 `string.Empty` |

内部引擎 `EnumerateDeviceInfos`（`SetupDiGetClassDevs` → `SetupDiEnumDeviceInfo` → `SetupDiGetDeviceRegistryProperty` → `SetupDiDestroyDeviceInfoList` 完整生命周期）设计为可扩展到其它设备类（Net、Bluetooth 等），当前仅服务于端口类设备。

## 消费项目（编译期导入）

| 项目 | 调用点 |
|---|---|
| `SpaceCG.Standard.NModbus` | `SerialPortExtensions.CreateInstance` 解析串口配置 |
| `SpaceCG.Standard.LedRenderer` | `SerialPortTransport` 解析灯带总线串口 |

> 历史说明：本文件早期名为 `SerialPortExtensions`（`partial` 合并进各消费项目的同名扩展类）。已更名为独立的 `SerialPortResolver`，不再依赖与消费项目的类名合并，消除 CS0262（partial 访问修饰符不一致）隐患。

## 使用示例

```csharp
using SpaceCG.Interop;

// 从设备描述解析串口号
var portName = SerialPortResolver.GetPortName("CH343,921600");   // → "COM7"
var portName2 = SerialPortResolver.GetPortName("COM3");          // → "COM3"（已是标准格式）

// 枚举所有串口设备友好名
foreach (var name in SerialPortResolver.GetPortFriendlyNames())
{
    Console.WriteLine(name);
}
```

## 已知注意事项

1. 仅 Windows 平台；跨平台调用方需自行做 `Environment.OSVersion` 守卫
2. `SetupDiGetClassDevs` 的 `Guid` 参数使用 `ref Guid` 编组（x86 下按值传递 16 字节 struct 与原生 `GUID*` 不匹配会导致 PInvokeStackImbalance，此为历史踩坑已修正，修改签名时勿回退）
3. 枚举每次调用都会创建/销毁设备信息集句柄，频繁轮询场景建议调用方缓存结果

---
最后更新：2026-11-12
