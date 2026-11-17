using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace SpaceCG.Controls
{
    /// <summary>
    /// 元素悬浮窗口 —— 始终对齐并覆盖在参考元素之上，跟随参考主窗口移动、缩放。
    /// <para>
    /// 典型用途：为 <see cref="System.Windows.Controls.WebBrowser"/> / WebView2 等无法用 WPF 元素 覆盖的空域（airspace）控件叠加可交互的悬浮按钮。
    /// 悬浮内容位于独立的顶层 HWND 中， 内容可交互（接收鼠标），同时通过焦点返还机制保留宿主窗口的键盘焦点。
    /// </para>
    /// <para>焦点策略：不抢焦点 —— WndProc 对 WM_MOUSEACTIVATE 返回 MA_NOACTIVATE，若意外获得键盘焦点则立即返还给宿主窗口。 </para>
    /// <para>
    /// 更新策略：事件驱动（宿主 LocationChanged + 元素 SizeChanged/LayoutUpdated）与 100ms DispatcherTimer 轮询兜底双路径；
    /// 两路最终都收敛到 <see cref="UpdatePosition"/>，其中以设备像素坐标做变化过滤，无变化时零开销返回。
    /// </para>
    /// <para>当宿主窗口最小化、关闭或参考元素不可见时自动停止对齐更新。</para>
    /// <para>线程安全级别：线程不安全，必须在 UI 线程上创建和操作。</para>
    /// </summary>
    public class ElementOverlayWindow : Window
    {
        /// <summary> 不激活窗口（WM_MOUSEACTIVATE 处理返回值） </summary>
        private const int MA_NOACTIVATE = 3;
        /// <summary> 鼠标激活消息，点击本窗口时由 DefWindowProc 发出 </summary>
        private const int WM_MOUSEACTIVATE = 0x0021;

        /// <summary> 元素局部坐标原点 (0,0)，复用避免每次分配 </summary>
        private static readonly Point ZeroPoint = new Point(0, 0);
        /// <summary> WM_MOUSEACTIVATE 的返回值：不激活、仍把消息交给窗口处理 </summary>
        private static readonly IntPtr NoActivateIntPtr = new IntPtr(MA_NOACTIVATE);

        /// <summary> 参考元素所在的宿主窗口（Loaded 后解析） </summary>
        private Window _ownerWindow;
        /// <summary> 本窗口的 HWND 源，用于挂接 WndProc </summary>
        private HwndSource _hwndSource;
        /// <summary> 位置轮询兜底定时器（事件遗漏时 100ms 内自愈） </summary>
        private DispatcherTimer _dispatcherTimer;

        /// <summary> 悬浮内容的承载容器（窗口 Content） </summary>
        private readonly Grid _grid = new Grid();
        /// <summary> 参考背景元素（对齐目标） </summary>
        private readonly FrameworkElement _background;

        /// <summary> 上次参考元素右下角在屏幕上的设备像素坐标，用于变化过滤 </summary>
        private Point _endLocationFromScreen = ZeroPoint;
        /// <summary> 上次参考元素左上角在屏幕上的设备像素坐标，用于变化过滤 </summary>
        private Point _startLocationFromScreen = ZeroPoint;

        private UIElement _overlayContent;
        /// <summary>
        /// 获取或设置悬浮窗口内容。
        /// <para>设置后替换承载容器中的现有内容；设为 null 即清空。</para>
        /// </summary>
        public UIElement OverlayContent
        {
            get => _overlayContent;
            set
            {
                _overlayContent = value;
                _grid.Children.Clear();
                if (_overlayContent != null)
                {
                    _grid.Children.Add(_overlayContent);
                }
            }
        }

        /// <summary>
        /// 构造函数。仅做轻量初始化与事件挂接，重量级操作（解析宿主窗口、显示）推迟到参考元素 Loaded 之后进行。
        /// </summary>
        /// <param name="refBackground">背景参考元素（对齐目标），必须在窗口加载后位于可视树中</param>
        /// <exception cref="ArgumentNullException"><paramref name="refBackground"/> 为 null</exception>
        public ElementOverlayWindow(FrameworkElement refBackground)
        {
            if (refBackground == null)
                throw new ArgumentNullException(nameof(refBackground));

            Width = 300;
            Height = 300;
            Title = "Element Overlay Window";

            Focusable = false;
            ShowInTaskbar = false;
            ShowActivated = false;
            AllowsTransparency = true;

            // 说明：AllowsTransparency 下 alpha=0 的区域不可命中（鼠标穿透），
            // 使用 0x01 使窗口整体可命中、可接收鼠标事件（悬浮按钮场景需要）， 而视觉上仍然透明。
#if DEBUG
            Background = new SolidColorBrush(Color.FromArgb(0x66, 0x00, 0xFF, 0x00));
#else
            Background = new SolidColorBrush(Color.FromArgb(0x01, 0x00, 0x00, 0x00));
#endif

            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;

            Content = _grid;
            // 初始继承参考元素的 DataContext，后续通过事件跟随变化
            DataContext = refBackground.DataContext;

            _background = refBackground;
            _background.Unloaded += Background_Unloaded;
            _background.DataContextChanged += Background_DataContextChanged;

            // 参考元素已加载（如来自已显示的窗口）则立即初始化；否则等其 Loaded
            if (_background.IsLoaded) Background_Loaded(this, null);
            else _background.Loaded += Background_Loaded;
        }
        /// <summary> 窗口源已创建：挂接 WndProc（不抢焦点），并启动轮询兜底定时器。 </summary>
        /// <inheritdoc />
        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            _hwndSource = (HwndSource)PresentationSource.FromVisual(this);
            _hwndSource.AddHook(WndProc);

            _dispatcherTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(100), DispatcherPriority.Normal, Dispatcher_TimerCallback, Dispatcher);
        }
        /// <summary> WndProc 钩子：拦截 WM_MOUSEACTIVATE 返回 MA_NOACTIVATE，使点击本窗口不会夺取激活状态，保持宿主窗口持续激活。</summary>
        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_MOUSEACTIVATE)
            {
                handled = true;
                return NoActivateIntPtr;
            }
            return IntPtr.Zero;
        }

        /// <summary> 轮询兜底：事件路径遗漏的位置变化（如祖先 RenderTransform 动画）由此自愈 </summary>
        private void Dispatcher_TimerCallback(object sender, EventArgs e) { UpdatePosition(); }
        /// <summary> 跟随参考元素的 DataContext，保持悬浮内容的数据绑定一致 </summary>
        private void Background_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e) { DataContext = e.NewValue; }

        /// <summary>
        /// 参考元素已加载：解析宿主窗口、建立 Owner 关系、挂接跟随事件并显示本窗口。
        /// <para>幂等：仅在首次（或 Hide 后重新 Loaded）时执行；事件采用"先解绑后绑定"避免重复订阅。</para>
        /// </summary>
        private void Background_Loaded(object sender, RoutedEventArgs e)
        {
            if (_ownerWindow != null && IsVisible) return;

            _ownerWindow = GetWindow(_background);
            _background.Loaded -= Background_Loaded;

            Trace.Assert(_ownerWindow != null);
            if (_ownerWindow == null) return;

            // Owner 关系保证 owned window 恒位于 owner 之上（Z 序托底）
            Owner = _ownerWindow;
            _ownerWindow.Closing += OwnerWindow_Closing;
            _ownerWindow.LocationChanged += OwnerWindow_LocationChanged;

            _background.SizeChanged += Background_SizeChanged;
            _background.LayoutUpdated += Background_LayoutUpdated;

            try
            {
                UpdatePosition();
                Show();
                _ownerWindow.Focus();
            }
            catch (Exception ex)
            {
                Hide();
                throw new InvalidOperationException("对齐窗口失败", ex);
            }
        }
        /// <summary> 参考元素卸载：解绑跟随事件、隐藏窗口（保留 Loaded 订阅以便重新加载时恢复跟随）。 </summary>
        private void Background_Unloaded(object sender, RoutedEventArgs e)
        {
            _background.SizeChanged -= Background_SizeChanged;
            _background.LayoutUpdated -= Background_LayoutUpdated;

            if (_ownerWindow != null)
            {
                _ownerWindow.Closing -= OwnerWindow_Closing;
                _ownerWindow.LocationChanged -= OwnerWindow_LocationChanged;
            }

            Hide();
        }

        /// <summary>
        /// 宿主窗口关闭：同步关闭本窗口并释放全部资源（定时器、WndProc 钩子、HwndSource、事件订阅）。
        /// </summary>
        private void OwnerWindow_Closing(object sender, CancelEventArgs e)
        {
            if (e.Cancel) return;

            Close();

            _dispatcherTimer?.Stop();
            _hwndSource?.RemoveHook(WndProc);
            _hwndSource?.Dispose();

            _background.Loaded -= Background_Loaded;
            _background.Unloaded -= Background_Unloaded;
            _background.DataContextChanged -= Background_DataContextChanged;
        }
        private void OwnerWindow_LocationChanged(object sender, EventArgs e) { UpdatePosition(); }

        private void Background_LayoutUpdated(object sender, EventArgs e) { UpdatePosition(); }
        private void Background_SizeChanged(object sender, SizeChangedEventArgs e) { UpdatePosition(); }

        /// <summary>
        /// 更新窗口位置与尺寸，使其精确覆盖参考元素的屏幕边界。
        /// <para>
        /// 复杂度：O(1)；无变化时（两次 PointToScreen + 相等比较）即返回，避免触发布局振荡。由事件路径与轮询路径共同调用，幂等收敛。
        /// </para>
        /// </summary>
        protected void UpdatePosition()
        {
            if (_ownerWindow == null) return;

            // 宿主窗口的呈现源不可用（关闭中/跨线程）时跳过
            var source = PresentationSource.FromVisual(_ownerWindow);
            if (source == null) return;

            if (PresentationSource.FromVisual(_background) == null) return;
            // 尺寸无效（未完成首次布局）时跳过
            if (double.IsNaN(_background.ActualWidth) || double.IsNaN(_background.ActualHeight) ||
                _background.ActualWidth == 0 || _background.ActualHeight == 0) return;

            /*
             * 注意：_background.ActualWidth 和 _background.ActualHeight 是局部坐标系的值，而不是屏幕坐标系的值。
             *
             * 当对 _background 本身或其某个上级元素（直至父窗口）应用了缩放变换（Scale 变换）时，局部坐标与屏幕坐标下的尺寸就会不一致。
             * 当参考元素或其某个上级元素被放置在 Viewbox 中时，同样会出现这种情况。
             *
             * 为了正确设置本窗口的位置和大小，我们需要将 _background 的边界范围（左上角点和右下角点）
             * 从 _background 的坐标系变换到屏幕坐标系，并将变换后的结果用作本窗口的边界范围。
             *
             * 如果检测到存在缩放，我们还应把该缩放比例应用到本窗口的内容上，
             * 使其能够随其他父级控件自然地放大和缩小。
             */

            // PointToScreen 返回屏幕物理像素坐标（已含宿主侧 DPI 与缩放变换）
            var startLocationFromScreen = _background.PointToScreen(ZeroPoint);
            var endLocationFromScreen = _background.PointToScreen(new Point(_background.ActualWidth, _background.ActualHeight));

            // 如果位置尺寸没有变化，则不用更新（以设备像素比较，置于矩阵变换之前以节省开销）
            if (_startLocationFromScreen == startLocationFromScreen && _endLocationFromScreen == endLocationFromScreen) return;

            _endLocationFromScreen = endLocationFromScreen;
            _startLocationFromScreen = startLocationFromScreen;

            // 物理像素 → DIP（按宿主窗口的 DPI 矩阵换算），供 WPF 窗口属性使用
            var startLocationPoint = source.CompositionTarget.TransformFromDevice.Transform(startLocationFromScreen);
            var endLocationPoint = source.CompositionTarget.TransformFromDevice.Transform(endLocationFromScreen);

            // 支持任意旋转/翻转变换：取两点包围盒
            Left = Math.Min(startLocationPoint.X, endLocationPoint.X);
            Top = Math.Min(startLocationPoint.Y, endLocationPoint.Y);
            Width = Math.Abs(endLocationPoint.X - startLocationPoint.X);
            Height = Math.Abs(endLocationPoint.Y - startLocationPoint.Y);

            // 局部尺寸与屏幕尺寸差异超过 0.5 DIP，说明宿主侧存在缩放（ScaleTransform / Viewbox）
            if (Math.Abs(Width - _background.ActualWidth) + Math.Abs(Height - _background.ActualHeight) > 0.5)
            {
                ScaleWindowContent(Width / _background.ActualWidth, Height / _background.ActualHeight);
            }
        }
        /// <summary>
        /// 将宿主侧检测到的缩放比例应用到悬浮内容上，使内容随父级控件自然缩放。
        /// <para> 缩放变化小于 0.01 时不更新，避免每次布局都 new ScaleTransform 触发无限循环的布局无效化（布局更新循环）。</para>
        /// </summary>
        /// <param name="scaleX">水平缩放比例（窗口宽度 / 元素局部宽度）</param>
        /// <param name="scaleY">垂直缩放比例（窗口高度 / 元素局部高度）</param>
        private void ScaleWindowContent(double scaleX, double scaleY)
        {
            if (VisualChildrenCount <= 0) return;

            var child = (FrameworkElement)GetVisualChild(0);

            // 如果已经应用过缩放，则不要重复创建并应用 ScaleTransform
            // 否则会导致无限循环的布局更新（布局无效化循环）
            if (child.LayoutTransform is ScaleTransform scaleTransform &&
                Math.Abs(scaleTransform.ScaleX - scaleX) < 0.01 && Math.Abs(scaleTransform.ScaleY - scaleY) < 0.01) return;

            child.LayoutTransform = new ScaleTransform(scaleX, scaleY);
        }

        /// <summary>
        /// 被激活时立即把激活状态返还给宿主窗口，保证键盘输入始终由宿主接收。
        /// </summary>
        /// <inheritdoc />
        protected override void OnActivated(EventArgs e)
        {
            base.OnActivated(e);
            _ownerWindow?.Activate();
        }
        /// <summary> 拦截 Alt+F4：把焦点返还宿主窗口，避免悬浮窗口被系统快捷键关闭。</summary>
        /// <inheritdoc />
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.Key == Key.System && e.SystemKey == Key.F4)
            {
                e.Handled = true;
                _ownerWindow?.Focus();
            }
        }
        /// <summary> DPI 变化（跨显示器拖动/系统缩放调整）时立即重新对齐。</summary>
        /// <inheritdoc />
        protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
        {
            base.OnDpiChanged(oldDpi, newDpi);
            UpdatePosition();
        }
        /// <summary> 意外获得键盘焦点时立即返还给宿主窗口。</summary>
        /// <inheritdoc />
        protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
        {
            base.OnGotKeyboardFocus(e);

            e.Handled = true;
            _ownerWindow?.Activate();
        }

    }
}
