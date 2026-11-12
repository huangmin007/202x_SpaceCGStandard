# SpaceCG.Standard.Wpf — WPF 控件与集成库

## 项目信息

| 项 | 值 |
|---|---|
| 目标框架 | .NET Framework 4.8（C# 7.3，SDK 风格 csproj） |
| 类型 | WPF 类库（`UseWPF=true`，`AllowUnsafeBlocks`） |
| 项目引用 | `SpaceCG.Standard` |
| NuGet 依赖 | 无 |
| RootNamespace | `SpaceCG`（代码命名空间为 `SpaceCG.Controls` / `SpaceCG.Converters` / `SpaceCG.Extensions` / `SpaceCG.ComponentModel`） |

## XAML 命名空间（XmlnsDefinition）

程序集已注册 XML 命名空间 URI：**`http://schemas.spacecg.cn/xaml`**，推荐前缀 `sg`：

```xml
xmlns:sg="http://schemas.spacecg.cn/xaml"
```

当前映射 `SpaceCG.Controls` 与 `SpaceCG.Converters` 两个 CLR 命名空间；其余（Extensions/ComponentModel 等）使用 `clr-namespace:` 写法。默认控件样式经 `[assembly: ThemeInfo]` 走标准 `Themes/Generic.xaml` 机制（消费端 App.xaml 无需合并）。

## 目录结构

```
SpaceCG.Standard.Wpf/
├── XmlnsDefinitions.cs        # ThemeInfo 程序集特性
├── ComponentModel/
│   └── NotifyPropertyChangedBase.cs   # INPC 基类
├── Controls/
│   ├── DemoBaseWindow.cs      # 演示窗口基类（抽象，DP 化资源导航）
│   ├── DemoControlManager.cs  # 演示控制管理器（组合式，RPC 远控）
│   ├── ImageButtons.cs / .xaml# 图片按钮族（基类 + Toggle + Radio）
│   ├── ImageSequence.cs       # 序列帧播放控件
│   └── OverlayContentWindow.cs# 悬浮内容窗口
├── Converters/
│   ├── BooleanValueConverter.cs
│   ├── BooleanVisibilityConverter.cs
│   └── VisibilityValueConverter.cs
└── Extensions/
    ├── ApplicationExtensions.cs
    ├── BitmapExtensions.cs
    ├── UIElementExtensions.cs
    └── WindowExtensions.cs
```

## 主要类型

### Controls

**ImageButtons 族**（模板共享 `ImageButtonTemplate`，样式内置）
| 类型 | 说明 |
|---|---|
| `ImageButtonBase : ToggleButton` | 基类；DP `NormalSource` / `CheckedSource`（ImageSource），右键按下拦截（`SuppressRightMouseDown` 共享实现） |
| `ImageToggleButton`（sealed） | 图片开关按钮 |
| `ImageRadioButton`（sealed : RadioButton） | 图片单选按钮，DP 经 `AddOwner` 复用基类定义 |

悬停/按下/禁用的视觉反馈由模板 Trigger 调整 `Opacity`，不引入 `HoverSource`（Source 替换与选中态组合会导致 MultiTrigger 组合爆炸，YAGNI 定案）。

**ImageSequence（sealed : Image）— 序列帧播放控件**
- 可写 DP：`FilePath`（目录或 zip）、`ABLoopStart` / `ABLoopEnd`（A-B 循环区间）、`AutoPlay`、`Fps`（Coerce 1~60，默认 25）、`CacheStep`（0=不缓存）
- 只读 DP：`IsPlaying` / `FrameIndex` / `FrameCount` / `AverageDecodeTime`
- 事件：`RenderChanged`；方法：`Play` / `Pause` / `Seek(int)` / `Unload` / `ClearCache`
- 帧缓存谓词：`CacheFramePredicate` 委托 + `CacheAll` / `CacheEvery(step, offset)` 静态策略
- 数据源：`GetImageFileNames(folder, extensions)` 或 `GetImageFileNames(ZipArchive, extensions)`

**OverlayContentWindow**
- 始终对齐覆盖在参考 `FrameworkElement` 之上、跟随参考窗口移动缩放的悬浮窗口；不抢焦点（`WM_MOUSEACTIVATE` → `MA_NOACTIVATE`）；参考窗口最小化/关闭时自动停止更新
- 构造：`new OverlayContentWindow(FrameworkElement refElement)`；线程不安全，UI 线程使用

**DemoBaseWindow（abstract partial : Window）与 DemoControlManager（: NotifyPropertyChangedBase）**
- 一套"演示/展示程序"的资源导航框架，两者能力重叠（历史演进：先有基类后有组合式管理器），核心功能一致：
  - 按 `Resources/{语言}` 目录加载媒体资源（`FileResources`：Tag → FileInfo 集合）
  - `Resources/ItemTags.txt` 定义 Tag 层级（`父Tag: 子Tag列表`，支持 `1,2,5~9` 区间格式）
  - 导航属性：`ItemTag` / `HomeItemTag` / `HasNextItem` / `NextItemTag` / `HasPrevItem` / `PrevItemTag` / `HasBackItem` / `BackItemTag`
  - 接口函数（同时注册为 RPC 远程方法）：`LoadItem` / `HomeItem` / `NextItem` / `PrevItem` / `BackItem` / `PlayPause` / `FullScreen` / `ChangeLanguage`
  - 语言变化监听（`LanguageProperty`）与空闲回首页计时（`IdleTime` 秒）
- 差异：`DemoBaseWindow` 把导航状态做成依赖属性（可绑定），`DemoControlManager` 用 INPC 通知 + 内置 `RpcServer4X` 远控（默认端口 2000，`ClientInvokeRequest` 刷新空闲计时）
- 事件：`LanguageChanged`、`ItemTagChanged`（参数含 `Resources`）

### ComponentModel

| 类型 | 说明 |
|---|---|
| `NotifyPropertyChangedBase` | INPC 基类；`OnPropertyChanged([CallerMemberName])` 与 `SetFieldValue<T>(ref field, value, ...)`（支持附加关联属性名批量通知） |

### Converters（均映射进 sg: URI）

| 类型 | 说明 |
|---|---|
| `BooleanVisibilityConverter` | bool → Visibility；`Inverse` / `UseCollapsed`；预置单例 `Default` / `CollapseWhenFalse` / `InverseDefault` |
| `BooleanValueConverter` | bool ↔ 文本值（"Yes/No"、"True/False" 等可扩展 `DefaultValues`）；`Inverse` |
| `VisibilityValueConverter` / `InverseVisibilityValueConverter` | Visibility ↔ 值/bool；`Inverse` / `UseCollapsed` |

### Extensions（`SpaceCG.Extensions`，与核心库同名空间互补）

| 类型 | 关键成员 |
|---|---|
| `WindowExtensions` | `InitializeStandard`（标准窗体初始化/消息注册）、`FullScreen` / `IsFullScreen`、`ChangeLanguage(window, tag)`（切换 UI 语言，含多语言字典应用）；另有两个字符串扩展 `SplitValues`（逗号/分号/冒号择一分割）与 `ParseIntRanges`（解析 `"1,2,5~9,20"` 为 `IEnumerable<int>`，惰性） |
| `UIElementExtensions` | `LoadDynamicElements`（从 XElement 动态构建 XAML）、`UpdateChildrenVisibility`、`FindElement<T>` / `FindElements<T>`、`GetScreenRect` |
| `BitmapExtensions` | `System.Drawing.Bitmap` → `BitmapSource`（两版实现，GDI 句柄生命周期不同，用前对比） |
| `ApplicationExtensions` | `InitializeStandard`（全局异常捕获/主题初始化等） |

## 使用示例

```xml
<Window xmlns:sg="http://schemas.spacecg.cn/xaml" ...>
    <StackPanel>
        <sg:ImageToggleButton x:Name="Btn" Width="64" Height="64"
                              NormalSource="/Images/btn.png" CheckedSource="/Images/btn_on.png"/>
        <sg:ImageSequence x:Name="Seq" FilePath="D:/Frames/Car_KFs.zip" Fps="25" AutoPlay="True"/>
        <CheckBox IsChecked="{Binding IsChecked, ElementName=Btn}"
                  Visibility="{Binding IsChecked, ElementName=Btn, Converter={x:Static sg:BooleanVisibilityConverter.Default}}"/>
    </StackPanel>
</Window>
```

```csharp
// 演示窗口组合式用法
public partial class MainWindow : Window
{
    private readonly DemoControlManager demo;

    public MainWindow()
    {
        InitializeComponent();
        demo = new DemoControlManager(this, defaultLanguage: "zh-CN", localPort: 2000, idleTimeSeconds: 300);
        demo.ItemTagChanged += (s, e) => { /* 加载 e.Resources */ };
    }
}
```

## 已知问题 / 设计注意

1. `WindowExtensions.ParseIntRanges` 为惰性 `IEnumerable<int>`：调用 `Any()` 后再 `ToList()` 会解析两遍（`DemoControlManager.ReadItemTags` 当前即如此）；热路径建议先物化
2. `DemoControlManager` 的语言监听使用 `DependencyPropertyDescriptor.AddValueChanged`，描述符内部强引用 component；未提供 `RemoveValueChanged` 的解除路径（窗口生命周期内常驻场景可接受，短生命周期宿主需注意泄漏）
3. `DemoBaseWindow` 与 `DemoControlManager` 职责重叠、`RootResources` 等常量重复定义，长期应收敛为一处（组合式为方向）
4. `SplitValues` 的"择一分割"语义（命中第一个出现的分隔符类型后仅按其分割）与直觉不符，详见其 XML 注释

---
最后更新：2026-11-12
