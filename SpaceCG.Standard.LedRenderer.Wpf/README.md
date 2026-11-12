# SpaceCG.Standard.LedRenderer.Wpf — LED 灯带渲染 WPF 层

> LED 灯带渲染库的 WPF 集成层：屏幕截帧 → 像素分发 → 灯带总线发送，并提供场景配置驱动的 UI 编排与动画笔刷工厂。
> 核心渲染/传输/设备层见 [SpaceCG.Standard.LedRenderer](../SpaceCG.Standard.LedRenderer/README.md)。

## 项目信息

| 项 | 值 |
|---|---|
| 目标框架 | .NET Framework 4.8（C# 7.3，SDK 风格 csproj） |
| 类型 | WPF 类库（`UseWPF=true`，`AllowUnsafeBlocks`，`GeneratePackageOnBuild`） |
| 项目引用 | `SpaceCG.Standard.LedRenderer`、`SpaceCG.Standard` |
| NuGet 依赖 | 无 |
| RootNamespace | `SpaceCG`（代码命名空间 `SpaceCG.Controls` / `SpaceCG.Drawing` / `SpaceCG.Extensions`） |
| 无 XAML | 纯 code-behind 类库，全部 UI 元素由代码动态创建 |

## 目录结构

```
SpaceCG.Standard.LedRenderer.Wpf/
├── Controls/
│   ├── LedRenderManager.cs                    # 场景渲染编排器（核心）
│   └── LedRenderManager_Example_Config.xml    # 示例配置
├── Drawing/
│   └── DrawingWpfElement.cs                   # IDrawingDisplay 的 WPF 实现（截帧引擎）
└── Extensions/
    └── BrushExtensions.cs                     # 动画笔刷工厂（约 20 个工厂方法）
```

## 核心类型

### DrawingWpfElement — 截帧引擎（SpaceCG.Drawing）

`IDrawingDisplay<FrameworkElement>` 的 WPF 实现：用 `RenderTargetBitmap`（Pbgra32, 96dpi）周期渲染参考 `FrameworkElement`，`CopyPixels` 到钉住缓冲后经 `NewDrawingFrame` 事件分发像素，供 `LedRenderBus` 读取并下发灯带。

| 成员 | 说明 |
|---|---|
| `int Interval` | 截帧间隔 ms，默认 40，合法范围 [16,1000]，越界抛异常 |
| `Rectangle Rectangle` | 截帧区域（默认 0,0,600,32，上限 1024×1024） |
| `double Fps { get; }` | 1 秒滑动窗口实测帧率 |
| `FrameworkElement DrawingElement { get; }` | 被截帧的宿主元素 |
| `StartDrawing()` / `StartDrawing(rect, interval)` / `StopDrawing()` / `Dispose()` | 生命周期管理；内部为 DispatcherTimer，**必须在 UI 线程构造与操作** |

### LedRenderManager — 场景编排器（SpaceCG.Controls）

XML 配置驱动的渲染编排器：解析 `DrawingDisplay` / `LedRenderBus` / `LedStripObject` / `Scenes` 配置节点，创建 `DrawingWpfElement`、`LedStripObject` 及 Canvas 上的 Rectangle/Label 显示元素，按场景定义驱动渲染任务。

| 成员 | 说明 |
|---|---|
| `LedRenderManager(Canvas canvas)` | 构造；挂接宿主 Window 的 Closing/PreviewKeyDown 与 Canvas 的鼠标选择事件 |
| `InitializeComponent(XElement drawingDisplay, XElement ledDevices, XElement sceneElement)` | 解析配置并初始化（重量级初始化与构造分离） |
| `UpdateScenes(XElement sceneElement)` | 更新场景定义 |
| `RenderSceneId(int id)` / `RenderScene(propertyName, value)` | 按场景 Id 或属性匹配切换场景；按 `Delay` 属性分组创建延时渲染任务 |
| `StartRender()` / `StopRender()` | 启停所有灯带总线通道 |
| `SetShapesColor(Color)` / `CancelSelected()` / `GetSelectedShapes()` | 显示元素着色与选中控制 |
| `int SceneId { get; }` | 当前场景 Id（-1 = 未设置） |

内置调试交互（键盘移动灯珠、鼠标点选形状），适合开发期标定灯带布局。

### BrushExtensions — 动画笔刷工厂（SpaceCG.Extensions）

供场景配置中的 `Action` 节点按方法名**反射调用**（`InstanceExtensions.TryInvokeMethod`），生成带 WPF 动画的 `Brush` 赋给 `shape.Fill`：

| 家族 | 方法 |
|---|---|
| 静态填充 | `ColorBrush(Color [, width])`、`ColorBrush(colors, widths)` |
| 渐变 | `LinearBrush(...)` 多重载（多组颜色/偏移/宽度，多段笔刷） |
| 闪烁 | `BlinkColorBrush` / `BlinkLinearBrush`（`blinkTime` 默认 1.5s） |
| 流光 | `FlowColorBrush` / `FlowLinearBrush`（`flowTime`/`flowStep`） |
| 组合 | `GroupBlackAndFlowLinearBrush` / `GroupFlowLinearAndBlackBrush`（左右分区：黑色 + 流光） |

## 配置示例

完整示例见 `Controls/LedRenderManager_Example_Config.xml`；端到端消费示例见 `Z_TestWpfApp/Resources/Config.xml`（RFID 刷卡切换灯带场景）。

```xml
<DrawingDisplay Rectangle="0,0,400,600" Interval="30"/>
<LedRenderBus Type="SERIAL" Value="CH343,921600" Address="1">
  <Port Count="171" ColorFormat="GRB"/>
</LedRenderBus>
<Scenes>
  <Scene Id="0" PropertyName="RFIDCard" PropertyValue="0453875215">
    <Scene.Dictionary Key="Brush" Value="FlowLinearBrush"/>
    ...
  </Scene>
</Scenes>
```

## 使用示例

```csharp
var manager = new LedRenderManager(canvas);
manager.InitializeComponent(configDrawingDisplay, configLedDevices, configScenes);
manager.StartRender();
manager.RenderSceneId(0);
```

## 已知问题 / 设计注意

1. `LedRenderManager` 是 UI 依赖最重的编排类（Canvas/Window/Shape/Dispatcher），职责上属于视图侧协调者；UI 调试交互与渲染编排耦合在同一类
2. `DrawingWpfElement` 截帧使用非托管缓冲 + unsafe `CopyPixels`，修改截帧区域逻辑时注意缓冲尺寸与矩形一致性
3. 场景 Action 反射调用笔刷工厂依赖 `BrushExtensions` 的公开静态方法名，重命名方法会导致既有配置失效

---
最后更新：2026-11-12
