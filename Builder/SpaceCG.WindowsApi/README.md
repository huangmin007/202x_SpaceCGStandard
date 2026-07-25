# SpaceCG.WindowsApi

`SpaceCG.WindowsApi` 项目封装 Windows 原生 API，提供**即插即用设备枚举查询**和**设备热插拔监听**两大核心功能，基于 SetupAPI、CfgMgr32 和 Win32 消息窗口实现。

- **目标框架**：.NET Framework 4.8（C# 7.3）
- **设计原则**：轻量查询与重量查询分离、基于事件的设备变更通知、优雅的线程生命周期管理

---

## 命名空间与模块

| 命名空间 | 文件 | 职责 |
|:---|:---|:---|
| `SpaceCG.Extensions` | `Extensions/SystemExtensions.cs` | 设备枚举查询：USB、串口、磁盘、卷 |
| `SpaceCG.Generic` | `Generic/DeviceWatcher.cs` | 设备热插拔监听：WM_DEVICECHANGE 事件通知 |
| `SpaceCG.Interop` | `Interop/NativeMethods.cs` | P/Invoke 声明占位 partial class |

---

## 类列表

| 类 | 类型 | 命名空间 | 说明 |
|:---|:---|:---|:---|
| [`SystemExtensions`](#systemextensions) | static class | `SpaceCG.Extensions` | **核心入口**：设备枚举查询工具类 |
| [`DeviceInfo`](#设备信息数据模型) | class | `SpaceCG.Extensions` | 设备基础信息基类 |
| [`UsbDeviceInfo`](#设备信息数据模型) | class | `SpaceCG.Extensions` | USB 设备信息（VID/PID/序列号） |
| [`SerialDeviceInfo`](#设备信息数据模型) | sealed class | `SpaceCG.Extensions` | 串口设备信息（COM 端口号） |
| [`DiskDeviceInfo`](#设备信息数据模型) | sealed class | `SpaceCG.Extensions` | 物理磁盘设备信息 |
| [`VolumeDeviceInfo`](#设备信息数据模型) | sealed class | `SpaceCG.Extensions` | 逻辑卷设备信息 |
| [`DeviceWatcher`](#devicewatcher) | sealed class | `SpaceCG.Generic` | **设备热插拔监听器**：后台消息窗口 |
| [`DeviceChangedEventArgs`](#设备变更事件参数) | abstract class | `SpaceCG.Generic` | 设备变更事件参数抽象基类 |
| [`VolumeDeviceChangedEventArgs`](#设备变更事件参数) | sealed class | `SpaceCG.Generic` | 卷设备变更事件参数（含驱动器号） |
| [`PortDeviceChangedEventArgs`](#设备变更事件参数) | sealed class | `SpaceCG.Generic` | 端口设备变更事件参数（含 COM 号） |
| [`InterfaceDeviceChangedEventArgs`](#设备变更事件参数) | sealed class | `SpaceCG.Generic` | 设备接口变更事件参数（含设备路径） |
| [`DeviceBroadcastType`](#devicebroadcasttype) | enum | `SpaceCG.Generic` | 设备广播类型枚举（WM_DEVICECHANGE wParam） |
| [`DeviceType`](#devicetype) | enum | `SpaceCG.Generic` | 设备类型枚举（DEV_BROADCAST_HDR dbch_devicetype） |

---

## `SystemExtensions`

Windows 设备信息查询辅助类（**核心入口**）。使用 SetupAPI 枚举系统中的串口、USB、磁盘、卷等即插即用设备。

### 公共方法

| 方法 | 返回类型 | 说明 |
|:---|:---|:---|
| `GetUsbDevices()` | `IReadOnlyList<UsbDeviceInfo>` | 枚举所有 USB 设备（基于 GUID_DEVINTERFACE_USB_DEVICE） |
| `GetSerialDevices()` | `IReadOnlyList<SerialDeviceInfo>` | 枚举所有串口设备（基于 GUID_DEVCLASS_PORTS，设备类枚举） |
| `GetDiskDevices()` | `IReadOnlyList<DiskDeviceInfo>` | 枚举所有物理磁盘设备（基于 GUID_DEVINTERFACE_DISK，仅轻量属性） |
| `GetVolumeDevices()` | `IReadOnlyList<VolumeDeviceInfo>` | 枚举所有逻辑卷设备（基于 GUID_DEVINTERFACE_VOLUME，仅轻量属性） |
| `GetPortName(string)` | `string` | 根据模式匹配查找串口号（如 "CH340" → "COM3"） |

### 使用示例

```csharp
using SpaceCG.Extensions;
using System;

// 1. 枚举 USB 设备
var usbDevices = SystemExtensions.GetUsbDevices();
foreach (var dev in usbDevices)
{
    Console.WriteLine($"USB: {dev.FriendlyName}  VID={dev.Vid}  PID={dev.Pid}  SN={dev.SerialNumber}");
}

// 2. 枚举串口设备
var serialDevices = SystemExtensions.GetSerialDevices();
foreach (var dev in serialDevices)
{
    Console.WriteLine($"串口: {dev.PortName}  {dev.FriendlyName}  VID={dev.Vid}  PID={dev.Pid}");
}

// 3. 通过模式匹配查找串口号（常用场景）
string comPort = SystemExtensions.GetPortName("CH340");
// 如果 FriendlyName 包含 "CH340"，返回对应端口号如 "COM3"
// 若未匹配到，返回原始字符串 "CH340"

// 4. 枚举磁盘设备
var diskDevices = SystemExtensions.GetDiskDevices();
foreach (var disk in diskDevices)
{
    Console.WriteLine($"磁盘: {disk.DiskNumber}  {disk.FriendlyName}  Path={disk.DevicePath}  HWID={disk.HardwareId}");
}
// DiskNumber 已自动从 InstanceId 解析（近似值）。SerialNumber、SizeBytes 等重量属性需额外 API 调用填充

// 5. 枚举卷设备
var volumeDevices = SystemExtensions.GetVolumeDevices();
foreach (var vol in volumeDevices)
{
    Console.WriteLine($"卷: {vol.FriendlyName}  VolumeName={vol.VolumeName}  Path={vol.DevicePath}");
}
// VolumeName 已自动从 DevicePath 填充。DriveLetter、LabelName、FileSystem 等重量属性需额外 API 调用填充
```

### 设计特性

- **线程安全**：`SystemExtensions` 为静态工具类，不维护可变状态，可在多线程环境中并发调用
- **轻量/重量属性分离**：枚举阶段仅填充 SetupAPI/CfgMgr32 可轻量获取的属性，重量属性（磁盘序列号、大小、文件系统等）需额外调用 `CreateFile` + `DeviceIoControl` 等 API
- **SetupAPI 句柄安全**：内部使用 `try-finally` + `SetupDiDestroyDeviceInfoList` 确保句柄释放
- **`GetPortName` 回退策略**：若 searchPattern 已是 "COM" + 数字格式则直接返回；否则枚举匹配，未匹配时返回原始字符串
- **P/Invoke 平台兼容**：`Guid` 参数使用 `ref` 编组（`GUID*`），x86/x64 下均正确工作

### 数据模型

#### `DeviceInfo`（基类）

| 属性 | 类型 | 来源 | 说明 |
|:---|:---|:---|:---|
| `DevInst` | `uint` | SP_DEVINFO_DATA.DevInst | 设备实例句柄 |
| `InstanceId` | `string` | CM_Get_Device_ID | 设备实例 ID |
| `FriendlyName` | `string` | SPDRP_FRIENDLYNAME | 设备管理器显示的名称 |
| `DevicePath` | `string` | SetupDiGetDeviceInterfaceDetail | 可用于 CreateFile 的设备路径 |
| `ClassGuid` | `Guid` | SP_DEVINFO_DATA.ClassGuid | 设备接口类 GUID |
| `ClassName` | `string` | SPDRP_CLASS | 设备类名（如 "Ports"、"USB"、"DiskDrive"） |
| `HardwareIds` | `string[]` | SPDRP_HARDWAREID（REG_MULTI_SZ） | 设备硬件 ID 列表 |
| `HardwareId` | `string` | HardwareIds 首项（计算属性） | 首选硬件 ID，如 "USB\VID_1A86&PID_7523" |
| `DisplayName` | `string` | 计算属性（virtual） | 优先级：FriendlyName > DevicePath > InstanceId |

#### `UsbDeviceInfo : DeviceInfo`

增加 USB 特有属性，从 `HardwareId` 和 `InstanceId` 字符串解析：

| 属性 | 类型 | 来源 | 说明 |
|:---|:---|:---|:---|
| `Vid` | `string` | 解析 `HardwareId` 中 "VID_XXXX" | 供应商 ID（不含前缀），非 USB 设备为 null |
| `Pid` | `string` | 解析 `HardwareId` 中 "PID_XXXX" | 产品 ID（不含前缀），非 USB 设备为 null |
| `SerialNumber` | `string` | 解析 `InstanceId` 尾部 | 含 '&' 则为 Windows 伪序列号，返回 null |

#### `SerialDeviceInfo : DeviceInfo`

增加串口端口号及 USB 属性（VID/PID/SerialNumber）。

| 属性 | 类型 | 来源 | 说明 |
|:---|:---|:---|:---|
| `PortName` | `string` | 从 FriendlyName 解析 | 如 "COM3"、"COM10" |
| `Vid` | `string` | 解析 HardwareId 中 "VID_XXXX" | 供应商 ID（不含前缀） |
| `Pid` | `string` | 解析 HardwareId 中 "PID_XXXX" | 产品 ID（不含前缀） |
| `SerialNumber` | `string` | 解析 InstanceId 尾部 | 含 '&' 则为 Windows 伪序列号，返回 null |

#### `DiskDeviceInfo : DeviceInfo`

物理磁盘设备信息。

| 属性 | 类型 | 来源 | 说明 |
|:---|:---|:---|:---|
| `DiskNumber` | `int` | 自动解析（InstanceId 末尾数字） | 物理磁盘编号（近似值，如 0、1） |

> **注意**：`DiskNumber` 为从 InstanceId 末尾数字提取的近似值，不保证与磁盘管理器编号完全一致。精确磁盘编号需通过 `CreateFile(DevicePath) + IOCTL_STORAGE_GET_DEVICE_NUMBER` 获取。以下重量属性当前未纳入数据模型，需额外 API 调用填充：SerialNumber（`IOCTL_STORAGE_QUERY_PROPERTY`）、SizeBytes（`IOCTL_DISK_GET_DRIVE_GEOMETRY_EX`）、Volumes（反向关联查询）。

#### `VolumeDeviceInfo : DeviceInfo`

逻辑卷设备信息。

| 属性 | 类型 | 来源 | 说明 |
|:---|:---|:---|:---|
| `VolumeName` | `string` | 自动填充（DevicePath 去末尾 `\`） | 卷 GUID 路径，如 `\\?\Volume{...}` |

> **注意**：以下重量属性当前未纳入数据模型，需额外 API 调用填充：DriveLetter（`GetVolumePathNamesForVolumeName`）、LabelName（`GetVolumeInformation`/`DriveInfo`）、FileSystem（`GetVolumeInformation`/`DriveInfo`）、IsNetworkDrive（`GetDriveType`）。

---

## `DeviceWatcher`

基于独立后台消息窗口（Message-only Window）的设备热插拔监听器。在独立 STA 线程中创建隐藏窗口，注册三类设备通知，通过事件回调通知设备插入/移除。

### 关键成员

| 成员 | 说明 |
|:---|:---|
| `DeviceArrived` | 事件：设备插入时触发（`EventHandler<DeviceChangedEventArgs>`） |
| `DeviceRemoved` | 事件：设备移除时触发（`EventHandler<DeviceChangedEventArgs>`） |
| `Handle` | 窗口句柄（`IntPtr`），启动后可用 |
| `Start()` | 创建消息窗口并启动消息循环（后台线程） |
| `Dispose()` | 优雅关闭：PostMessage(WM_CLOSE) + 等待线程退出 |

### 使用示例

```csharp
using SpaceCG.Generic;
using System;

var watcher = new DeviceWatcher();

// 订阅设备插入事件
watcher.DeviceArrived += (sender, e) =>
{
    Console.WriteLine($"设备插入: {e.EventType}");

    if (e is PortDeviceChangedEventArgs portArgs)
    {
        Console.WriteLine($"  串口: {portArgs.PortName} ({portArgs.PortFriendlyName})");
    }
    else if (e is VolumeDeviceChangedEventArgs volumeArgs)
    {
        Console.WriteLine($"  卷: {volumeArgs.DriveLetter}  {(volumeArgs.IsNetworkDrive ? "网络" : "本地")}");
        if (volumeArgs.Drive != null)
            Console.WriteLine($"    标签={volumeArgs.Drive.VolumeLabel}  文件系统={volumeArgs.Drive.DriveFormat}");
    }
    else if (e is InterfaceDeviceChangedEventArgs interfaceArgs)
    {
        Console.WriteLine($"  设备接口: {interfaceArgs.DevicePath}  GUID={interfaceArgs.ClassGuid}");
    }
};

// 订阅设备移除事件
watcher.DeviceRemoved += (sender, e) =>
{
    Console.WriteLine($"设备移除: {e.EventType}");

    if (e is PortDeviceChangedEventArgs portArgs)
    {
        Console.WriteLine($"  串口已拔出: {portArgs.PortName}");
    }
    else if (e is VolumeDeviceChangedEventArgs volumeArgs)
    {
        Console.WriteLine($"  卷已移除: {volumeArgs.DriveLetter}");
    }
    else if (e is InterfaceDeviceChangedEventArgs interfaceArgs)
    {
        Console.WriteLine($"  设备接口已移除: {interfaceArgs.DevicePath}");
    }
};

// 启动监听
watcher.Start();
Console.WriteLine("设备监听已启动，按任意键退出...");
Console.ReadKey();

// 优雅关闭
watcher.Dispose();
```

### 设计特性

- **独立线程**：在后台 STA 线程中创建消息窗口，不阻塞调用方线程
- **自动注册通知**：启动时自动注册磁盘（`GUID_DEVINTERFACE_DISK`）、串口（`GUID_DEVINTERFACE_COMPORT`）、所有设备接口（`DEVICE_NOTIFY_ALL_INTERFACE_CLASSES`）三类通知
- **优雅关闭**：`Dispose()` 通过 `PostMessage(WM_CLOSE)` 通知消息循环退出，等待后台线程结束后释放资源
- **类型化事件参数**：通过 `DeviceChangedEventArgs` 多态体系，根据设备类型提供不同的事件参数子类
- **IDisposable 模式**：实现 `IDisposable`，建议使用 `using` 语句管理生命周期

---

## `DeviceBroadcastType`

设备广播类型枚举，对应 `WM_DEVICECHANGE` 消息的 `wParam` 值。

| 枚举值 | 值 | 说明 |
|:---|:---:|:---|
| `DBT_DEVICEARRIVAL` | 0x8000 | 设备已插入 |
| `DBT_DEVICEREMOVECOMPLETE` | 0x8004 | 设备已移除 |
| `DBT_DEVNODES_CHANGED` | 0x0007 | 设备节点已变更 |
| `DBT_DEVICEQUERYREMOVE` | 0x8001 | 请求移除设备（可拒绝） |
| `DBT_DEVICEQUERYREMOVEFAILED` | 0x8002 | 设备移除请求被取消 |
| `DBT_CONFIGCHANGED` | 0x0018 | 当前配置已更改 |
| `DBT_CUSTOMEVENT` | 0x8006 | 自定义事件 |
| `DBT_DEVINSTENUMERATED` | 0x8012 | 设备实例已枚举 |
| `DBT_DEVINSTSTARTED` | 0x8013 | 设备实例已启动 |
| `DBT_DEVINSTREMOVED` | 0x8014 | 设备实例已移除 |
| `DBT_DEVINSTPROPERTYCHANGED` | 0x8015 | 设备实例属性已更改 |

> 注意：`DeviceWatcher` 当前仅通过事件暴露 `DBT_DEVICEARRIVAL` 和 `DBT_DEVICEREMOVECOMPLETE` 两种类型。

---

## `DeviceType`

设备类型枚举，对应 `DEV_BROADCAST_HDR.dbch_devicetype` 字段。

| 枚举值 | 值 | 说明 |
|:---|:---:|:---|
| `DBT_DEVTYP_VOLUME` | 0x00000002 | 逻辑卷 |
| `DBT_DEVTYP_PORT` | 0x00000003 | 端口（串口/并口） |
| `DBT_DEVTYP_DEVICEINTERFACE` | 0x00000005 | 设备接口 |
| `DBT_DEVTYP_HANDLE` | 0x00000006 | 文件系统句柄 |
| `DBT_DEVTYP_DEVINST` | 0x00000007 | 设备实例 |
| `DBT_DEVTYP_OEM` | 0x00000000 | OEM 定义 |
| `DBT_DEVTYP_DEVNODE` | 0x00000001 | 设备节点 |
| `DBT_DEVTYP_NET` | 0x00000004 | 网络资源 |

---

## 设备变更事件参数

事件参数通过 `is` 模式匹配判断具体设备类型：

```csharp
watcher.DeviceArrived += (sender, e) =>
{
    // e.EventType — DeviceBroadcastType（插入/移除）
    // e.DeviceType — DeviceType（卷/端口/接口）

    switch (e)
    {
        case VolumeDeviceChangedEventArgs vol:
            // vol.DriveLetter — 驱动器号（如 "C:"）
            // vol.IsNetworkDrive — 是否为网络驱动器
            // vol.Drive — DriveInfo 对象（插入时有值，拔出时可能为 null）
            break;

        case PortDeviceChangedEventArgs port:
            // port.PortName — 串口号（如 "COM3"）
            // port.PortFriendlyName — 端口友好名称
            break;

        case InterfaceDeviceChangedEventArgs iface:
            // iface.DevicePath — 设备路径（可用于 CreateFile）
            // iface.ClassGuid — 设备接口类 GUID
            break;
    }
};
```

| 事件参数类 | DeviceType | 关键属性 | 说明 |
|:---|:---|:---|:---|
| `VolumeDeviceChangedEventArgs` | `DBT_DEVTYP_VOLUME` | `DriveLetter`, `IsNetworkDrive`, `Drive` | 卷插入/移除事件 |
| `PortDeviceChangedEventArgs` | `DBT_DEVTYP_PORT` | `PortName`, `PortFriendlyName` | 端口插入/移除事件 |
| `InterfaceDeviceChangedEventArgs` | `DBT_DEVTYP_DEVICEINTERFACE` | `DevicePath`, `ClassGuid` | 设备接口插入/移除事件 |

---

## 项目结构

```
SpaceCG.WindowsApi/
├── SpaceCG.WindowsApi.csproj    # 项目文件（.NET Framework 4.8, AnyCPU, 允许 unsafe）
├── Properties/
│   └── AssemblyInfo.cs
├── Interop/
│   └── NativeMethods.cs         # P/Invoke 声明占位 partial class
├── Extensions/
│   └── SystemExtensions.cs      # 设备枚举查询（SetupAPI/CfgMgr32）
├── Generic/
│   └── DeviceWatcher.cs         # 设备热插拔监听（Win32 消息窗口 + WM_DEVICECHANGE）
└── (Linked)
    └── ../../SharedProjects/SpaceCG.Standard/Trace.cs  # 日志模块（链接引入）
```

---

## 已知问题与注意事项

| # | 问题 | 说明 |
|:---:|:---|:---|
| 1 | **DiskNumber 为近似值** | `DiskNumber` 从 InstanceId 末尾数字提取，不保证与磁盘管理器编号一致。精确值需 `CreateFile(DevicePath) + IOCTL_STORAGE_GET_DEVICE_NUMBER` |
| 2 | **重量属性未纳入模型** | 磁盘的 SerialNumber、SizeBytes 和卷的 DriveLetter、LabelName、FileSystem 等属性未纳入数据模型，需调用方自行通过 CreateFile/DeviceIoControl 或 GetVolumePathNamesForVolumeName 等 API 填充 |
| 3 | **`GetSerialDevices` 无 DevicePath** | 当前使用 `GUID_DEVCLASS_PORTS` 设备类枚举（`SetupDiEnumDeviceInfo`），不走接口层，因此 `DevicePath` 始终为 null。旧版 `GUID_DEVINTERFACE_COMPORT` 接口枚举方案因不稳定已被废弃 |
| 4 | **`DeviceWatcher` 需 STA 线程** | `DeviceWatcher` 内部创建 STA 线程运行消息循环，调用方无需关心。但如果在 UI 线程（WPF/WinForms）中使用，事件回调将切换到后台线程，更新 UI 需使用 `Dispatcher.Invoke` 或 `Control.Invoke` |
| 5 | **`GetPortName` 回退行为** | 若枚举未匹配到任何设备，`GetPortName` 返回原始 `searchPattern` 而非 `null`。调用方需自行判断返回值是否为有效 COM 端口 |
| 6 | **设备路径中 `&` 为 XML 转义** | `HardwareId` 和 `InstanceId` 中 `&` 为 Windows SetupAPI 原生的设备 ID 分隔符，非 XML 实体引用。在 XML 上下文中使用时需手动转义 |
| 7 | **`DeviceWatcher` 不支持取消设备移除** | 当前仅处理 `DBT_DEVICEARRIVAL` 和 `DBT_DEVICEREMOVECOMPLETE`，未实现 `DBT_DEVICEQUERYREMOVE` 的阻止移除功能 |
| 8 | **P/Invoke `Guid` 参数** | 所有 SetupAPI P/Invoke 的 `Guid` 参数均使用 `ref Guid` 编组为 `GUID*`，x86/x64 下均正确。注意调用处需显式加 `ref` 关键字 |
