# SpaceCG.Standard.Windows

`SpaceCG.Standard.Windows` 项目封装 Windows 原生 API，提供**即插即用设备枚举查询**和**设备热插拔监听**两大核心功能，基于 SetupAPI 与 CfgMgr32 实现，并提供按 Win32 DLL 分组的 API 声明库（`SpaceCG.WindowsAPI.*`）。

- **目标框架**：.NET Framework 4.8（C# 7.3）
- **依赖**：`SpaceCG.Standard`（ProjectReference）
- **设计原则**：轻量查询与重量查询分离、基于事件的设备变更通知、优雅的线程生命周期管理

> 命名说明：本项目早期曾命名为 `SpaceCG.WindowsApi`，当前项目目录与程序集均为 `SpaceCG.Standard.Windows`。

---

## 命名空间与模块

| 命名空间 | 文件 | 职责 |
|:---|:---|:---|
| `SpaceCG.Extensions` | `Extensions/SystemExtensions.cs` | 设备枚举查询：USB、串口、磁盘、卷 |
| `SpaceCG.Device` | `Device/DeviceWatcher.cs` | 设备热插拔监听：基于 CfgMgr32 的 CM_Register_Notification（Windows 8+） |
| `SpaceCG.WindowsAPI` | `WinDef.cs`、`API_Template.cs` | Win32 公共基础类型（POINT/SIZE/RECT/FILETIME 等，WinDef.h） |
| `SpaceCG.WindowsAPI.Kernel32` | `Kernel32/*.cs` | kernel32.dll：模块加载、内存操作、文件 I/O、系统时间、FormatMessage |
| `SpaceCG.WindowsAPI.User32` | `User32/*.cs` | user32.dll：窗口管理、消息、输入（RawInput/SendInput/Pointer）、钩子、热键、DPI、设备通知（Dbt.h） |
| `SpaceCG.WindowsAPI.Gdi` | `GDI/WinGDI.cs` | gdi32/user32：显示设备枚举、DC |
| `SpaceCG.WindowsAPI.SetupAPI` | `SetupAPI/*.cs` | setupapi.dll：设备信息集枚举（SetupDi* / SP_* 结构 / DIGCF / SPDRP） |
| `SpaceCG.WindowsAPI.CfgMgr32` | `SetupAPI/CfgMgr32.cs` | cfgmgr32.dll：PnP 设备通知（CM_Register_Notification）、设备 ID 与接口列表查询 |
| `SpaceCG.WindowsAPI.HID` | `HID/*.cs` | hid.dll：HID 设备属性/能力/报文 |

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
| [`DeviceWatcher`](#devicewatcher) | sealed class | `SpaceCG.Device` | **设备热插拔监听器**：CfgMgr32 PnP 通知（Windows 8+） |
| [`DeviceChangedEventArgs`](#devicewatcher) | sealed class | `SpaceCG.Device` | 设备变更事件参数（不可变） |
| [`DeviceChangeType`](#devicewatcher) | enum | `SpaceCG.Device` | 设备变化类型（接入 / 移除） |
| [`DeviceCategory`](#devicewatcher) | enum | `SpaceCG.Device` | 设备类别（按接口类 GUID 归类） |
| [`DeviceBroadcastType`](#devicebroadcasttype) | enum | `SpaceCG.WindowsAPI.User32` | 设备广播类型枚举（WM_DEVICECHANGE wParam） |
| [`DeviceType`](#devicetype) | enum | `SpaceCG.WindowsAPI.User32` | 设备类型枚举（DEV_BROADCAST_HDR dbch_devicetype） |

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

基于 CfgMgr32 `CM_Register_Notification` 的设备热插拔监听器（纯 C#，仅 Windows，需 Windows 8+）。P/Invoke 声明位于 `SpaceCG.WindowsAPI.CfgMgr32`。

### 关键成员

| 成员 | 说明 |
|:---|:---|
| `DeviceChanged` | 事件：设备接入/移除时触发（`EventHandler<DeviceChangedEventArgs>`） |
| `VolumeInterfaceClass` 等静态 GUID | 常用设备接口类 GUID（Volume / ComPort / UsbDevice / Hid） |
| `DeviceWatcher(IEnumerable<Guid>)` | 构造：指定订阅的接口类；null/空 = 订阅全部接口类 |
| `Start()` | 启动监听；事件回调 marshal 回 Start() 调用线程（有 SynchronizationContext 时） |
| `Dispose()` | 停止监听并释放原生通知句柄；禁止在事件回调内调用（会死锁） |
| `GetPresentInterfaces(Guid)` | 静态：枚举当前已存在的接口符号链接（注册不会为已存在接口发通知，需先注册后枚举） |

### `DeviceChangedEventArgs` 属性

| 属性 | 说明 |
|:---|:---|
| `ChangeType` / `IsArrival` | 接入 / 移除 |
| `Category` | 设备类别（Serial / Disk / UsbDevice / Hid / Other） |
| `InterfaceClassGuid` | 设备接口类 GUID |
| `DeviceName` | 显示名称：COM3 / E:\ / 接口符号链接路径 |
| `DevicePath` | 设备接口符号链接（即时层原始信息） |

### 使用示例（WPF，在 UI 线程创建并 Start，事件自动回到 UI 线程）

```csharp
using SpaceCG.Device;

_watcher = new DeviceWatcher();
_watcher.DeviceChanged += (sender, e) =>
{
    LogText.AppendText($"[{e.Category}] {(e.IsArrival ? "接入" : "移除")} {e.DeviceName}\r\n");
};
_watcher.Start();
// 窗口关闭时：_watcher.Dispose();
```

### 设计特性

- **双层事件**：即时层（原生回调线程，符号链接路径）+ 识别层（延时快照 diff，补全 COM3 / 盘符等友好名称）；同一设备可能先后触发两次
- **回调非阻塞**：原生回调只做纯内存解析与事件派发，友好名称补全由线程池延时执行
- **多实例安全**：pContext 传入 GCHandle 还原实例；delegate 保存在实例字段防 GC
- **SafeHandle 管理**：HCMNOTIFICATION 句柄由 SafeHandle 派生类释放（CM_Unregister_Notification）

---

## `DeviceBroadcastType`

设备广播类型枚举（`SpaceCG.WindowsAPI.User32`，Dbt.h），对应 `WM_DEVICECHANGE` 消息的 `wParam` 值。

| 枚举值 | 值 | 说明 |
|:---|:---:|:---|
| `DBT_DEVICEARRIVAL` | 0x8000 | 设备已插入 |
| `DBT_DEVICEREMOVECOMPLETE` | 0x8004 | 设备已移除 |
| `DBT_DEVNODES_CHANGED` | 0x0007 | 设备节点已变更 |
| `DBT_DEVICEQUERYREMOVE` | 0x8001 | 请求移除设备（可拒绝） |
| `DBT_DEVICEQUERYREMOVEFAILED` | 0x8002 | 设备移除请求被取消 |
| `DBT_CONFIGCHANGED` | 0x0018 | 当前配置已更改 |
| `DBT_CUSTOMEVENT` | 0x8006 | 自定义事件 |
| `DBT_DEVINSTENUMERATED` | 0x8007 | 设备实例已枚举 |
| `DBT_DEVINSTSTARTED` | 0x8008 | 设备实例已启动 |
| `DBT_DEVINSTREMOVED` | 0x8009 | 设备实例已移除 |
| `DBT_DEVINSTPROPERTYCHANGED` | 0x800A | 设备实例属性已更改 |
| `DBT_USERDEFINED` | 0xFFFF | 用户自定义事件 |

> 注意：`DeviceWatcher` 仅派发设备接口的到达/移除事件，其余 DBT_* 事件不触发回调。

---

## `DeviceType`

设备类型枚举（`SpaceCG.WindowsAPI.User32`，Dbt.h），对应 `DEV_BROADCAST_HDR.dbch_devicetype` 字段。

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

## 项目结构

```
SpaceCG.Standard.Windows/
├── SpaceCG.Standard.Windows.csproj  # 项目文件（.NET Framework 4.8，SDK-style）
├── README.md
├── WinDef.cs                        # WinDef.h 公共基础类型（POINT/SIZE/RECT/FILETIME/委托）
├── API_Template.cs                  # WinAPI 声明模板（供新增 API 复制）
├── Device/
│   └── DeviceWatcher.cs             # 设备热插拔监听（CfgMgr32 CM_Register_Notification）
├── Extensions/
│   └── SystemExtensions.cs          # 设备枚举查询（SetupAPI/CfgMgr32）
├── Kernel32/                        # kernel32.dll（Kernel32、FileSystem、SystemTime、FormatMessage）
├── User32/                          # user32.dll（窗口/消息/输入/钩子/DPI/设备通知 Dbt.h）
├── SetupAPI/                        # setupapi.dll（SetupAPI、SetupAPIDef）+ CfgMgr32.cs
├── GDI/                             # gdi32（WinGDI）
├── HID/                             # hid.dll（HID、HIDExtension）
└── .hFiles/                         # Win32 头文件参考副本（不参与编译）
```

---

## 依赖

| 依赖 | 说明 |
|:---|:---|
| `SpaceCG.Standard` | 基础库，提供 `SpaceCG.Diagnostics.Trace`、`SpaceCG.Extensions`、`SpaceCG.Generic` 等 |

---

## 已知问题与注意事项

| # | 问题 | 说明 |
|:---:|:---|:---|
| 1 | **DiskNumber 为近似值** | `DiskNumber` 从 InstanceId 末尾数字提取，不保证与磁盘管理器编号一致。精确值需 `CreateFile(DevicePath) + IOCTL_STORAGE_GET_DEVICE_NUMBER` |
| 2 | **重量属性未纳入模型** | 磁盘的 SerialNumber、SizeBytes 和卷的 DriveLetter、LabelName、FileSystem 等属性未纳入数据模型，需调用方自行通过 CreateFile/DeviceIoControl 或 GetVolumePathNamesForVolumeName 等 API 填充 |
| 3 | **`GetSerialDevices` 无 DevicePath** | 当前使用 `GUID_DEVCLASS_PORTS` 设备类枚举（`SetupDiEnumDeviceInfo`），不走接口层，因此 `DevicePath` 始终为 null。旧版 `GUID_DEVINTERFACE_COMPORT` 接口枚举方案因不稳定已被废弃 |
| 4 | **`DeviceWatcher` 事件线程** | 事件回调在 Start() 调用线程触发（存在 SynchronizationContext 时，如 WPF UI 线程）；无同步上下文时在 CfgMgr32 原生回调线程触发，跨线程 UI 更新需自行 Dispatch。禁止在事件回调内调用 `Dispose()`（CM_Unregister_Notification 会等待未决回调，死锁） |
| 5 | **`GetPortName` 回退行为** | 若枚举未匹配到任何设备，`GetPortName` 返回原始 `searchPattern` 而非 `null`。调用方需自行判断返回值是否为有效 COM 端口 |
| 6 | **设备路径中 `&` 为 XML 转义** | `HardwareId` 和 `InstanceId` 中 `&` 为 Windows SetupAPI 原生的设备 ID 分隔符，非 XML 实体引用。在 XML 上下文中使用时需手动转义 |
| 7 | **`DeviceWatcher` 不支持取消设备移除** | 仅派发设备接口到达/移除事件，未实现 `DBT_DEVICEQUERYREMOVE` 的阻止移除功能 |
| 8 | **P/Invoke `Guid` 参数** | 所有 SetupAPI P/Invoke 的 `Guid` 参数均使用 `ref Guid` 编组为 `GUID*`，x86/x64 下均正确。注意调用处需显式加 `ref` 关键字 |
