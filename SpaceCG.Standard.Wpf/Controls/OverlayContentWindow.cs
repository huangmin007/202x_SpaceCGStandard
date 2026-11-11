using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using SpaceCG.Extensions;

namespace SpaceCG.Controls
{
    /// <summary>
    /// 悬浮内容窗口 —— 始终对齐并覆盖在参考元素之上，跟随参考窗口移动、缩放。
    /// <para>
    /// 该窗口不抢焦点（WM_MOUSEACTIVATE 返回 MA_NOACTIVATE），始终保持与参考元素相同的
    /// 位置和尺寸。当参考窗口最小化、关闭或元素不可见时自动停止更新。
    /// </para>
    /// <para>线程安全级别：线程不安全，必须在 UI 线程上创建和操作。</para>
    /// </summary>
    public class OverlayContentWindow : Window
    {
        private readonly Window referenceWindow;
        private readonly FrameworkElement referenceElement;
        private readonly IntPtr NoActivateIntPtr = new IntPtr(MA_NOACTIVATE);

        private IntPtr selfWindowPtr;
        private IntPtr referenceWindowPtr;
        private HwndSource selfHwndSource;

        private Rect lastElementRect = Rect.Empty;
        private DispatcherTimer dispatcherTimer;

        #region Dll Imports
        private const int MA_NOACTIVATE = 3;
        private const int WM_MOUSEACTIVATE = 0x0021;
        /// <summary>
        /// 改变窗口的大小、位置和 Z 序。
        /// </summary>
        /// <param name="hWnd">窗口句柄</param>
        /// <param name="hWndInsertAfter">Z 序插入位置（HWND_TOP / HWND_BOTTOM / 窗口句柄）</param>
        /// <param name="x">新 X 坐标</param>
        /// <param name="y">新 Y 坐标</param>
        /// <param name="cx">新宽度</param>
        /// <param name="cy">新高度</param>
        /// <param name="uFlags">标志位</param>
        /// <returns>成功返回 true</returns>
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);
        #endregion

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="refElement">参考元素</param>
        /// <exception cref="ArgumentNullException"></exception>
        public OverlayContentWindow(FrameworkElement refElement)
        {
            if (refElement == null)
                throw new ArgumentNullException(nameof(refElement));

            this.Width = 100;
            this.Height = 100;
            this.Title = "Content Window";

            // 设置默认属性
            //this.Topmost = true;      // 可以不需要设置
            this.Focusable = false;
            this.ShowInTaskbar = false;
            this.ShowActivated = false;
            this.AllowsTransparency = true;
            this.Background = new SolidColorBrush(Color.FromArgb(0x01, 0x00, 0x00, 0x00));

            this.WindowStyle = WindowStyle.None;
            this.ResizeMode = ResizeMode.NoResize;

            referenceElement = refElement;
            referenceWindow = Window.GetWindow(refElement);

            this.Loaded += Window_Loaded;
            this.Closing += Window_Closing;
            this.Unloaded += Window_Unloaded;
        }

        /// <inheritdoc /> 
        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            selfHwndSource = (HwndSource)PresentationSource.FromVisual(this);
            selfHwndSource.AddHook(WndProc);
        }
        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_MOUSEACTIVATE)
            {
                handled = true;
                return NoActivateIntPtr;
            }
            return IntPtr.Zero;
        }

        /// <inheritdoc /> 
        protected override void OnActivated(EventArgs e)
        {
            base.OnActivated(e);

            referenceWindow?.Activate();
            UpdatePosition(referenceElement);
        }
        /// <inheritdoc /> 
        protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
        {
            base.OnDpiChanged(oldDpi, newDpi);

            UpdatePosition(referenceElement);
        }
        /// <inheritdoc /> 
        protected override void OnGotKeyboardFocus(System.Windows.Input.KeyboardFocusChangedEventArgs e)
        {
            base.OnGotKeyboardFocus(e);

            e.Handled = true;
            referenceWindow?.Activate();
            UpdatePosition(referenceElement);
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            selfWindowPtr = selfHwndSource.Handle;

            if (referenceWindow == null)
                throw new InvalidOperationException("未找到Owner窗口");

            referenceWindow.Closing += (_, __) => this.Close();
            referenceWindow.Activated += ReferenceWindow_StateChanged;
            referenceWindow.SizeChanged += ReferenceWindow_StateChanged;
            referenceWindow.StateChanged += ReferenceWindow_StateChanged;
            referenceWindow.LocationChanged += ReferenceWindow_StateChanged;

            if (!referenceWindow.IsLoaded)
            {
                referenceWindow.Loaded += (_, __) =>
                {
                    referenceWindowPtr = ((HwndSource)PresentationSource.FromVisual(referenceWindow)).Handle;
                    UpdatePosition();
                };
            }
            else
            {
                referenceWindowPtr = ((HwndSource)PresentationSource.FromVisual(referenceWindow)).Handle;
            }

            UpdatePosition();
            dispatcherTimer = new DispatcherTimer(TimeSpan.FromSeconds(0.1), DispatcherPriority.Normal, DispatcherTimerCallback, Dispatcher);
        }
        private void DispatcherTimerCallback(object sender, EventArgs e)
        {
            UpdatePosition();
        }

        private void Window_Closing(object sender, CancelEventArgs e) { Cleanup(); }
        private void Window_Unloaded(object sender, RoutedEventArgs e) { Cleanup(); }
        private void ReferenceWindow_StateChanged(object sender, EventArgs e) { UpdatePosition(); }

        /// <summary>
        /// 清理资源：停止定时器、移除 WndProc 钩子、释放 HwndSource、解绑参考窗口事件。
        /// <para>在 Closing 和 Unloaded 事件中自动调用。</para>
        /// </summary>
        protected void Cleanup()
        {
            dispatcherTimer?.Stop();
            dispatcherTimer = null;

            selfHwndSource?.RemoveHook(WndProc);
            selfHwndSource?.Dispose();
            selfHwndSource = null;

            if (referenceWindow != null)
            {
                referenceWindow.Activated -= ReferenceWindow_StateChanged;
                referenceWindow.SizeChanged -= ReferenceWindow_StateChanged;
                referenceWindow.StateChanged -= ReferenceWindow_StateChanged;
                referenceWindow.LocationChanged -= ReferenceWindow_StateChanged;
            }
        }

        /// <summary>
        /// 更新窗口位置，让其永远在 referenceWindow 的上方
        /// </summary>
        public void UpdatePosition()
        {
            UpdatePosition(referenceElement);

            if (referenceWindowPtr != IntPtr.Zero)
            {
                // 将 selfWindow 插入到 referenceWindow 之前（Z 序紧随其下方）
                // SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE：仅调整 Z 序，不改变位置、大小、不激活
                const uint flags = 0x0002 | 0x0001 | 0x0010;
                SetWindowPos(referenceWindowPtr, selfWindowPtr, 0, 0, 0, 0, flags);
            }
        }

        /// <summary>
        /// 更新窗口位置
        /// </summary>
        /// <param name="element"></param>
        protected void UpdatePosition(FrameworkElement element)
        {
            if (element == null) return;
            if (!this.IsLoaded || !this.IsVisible || referenceWindow.WindowState == WindowState.Minimized) return;

            // 同步 referenceWindow 的 Topmost 状态（构造函数中已设置其余不可变属性）
            this.Topmost = referenceWindow.Topmost;

            try
            {
                // GetScreenRect 扩展方法返回物理像素，直接用于 SetWindowPos
                var elementRect = element.GetScreenRect();
                if (elementRect.IsEmpty || elementRect == lastElementRect) return;
                // 过滤异常坐标：窗口最小化或元素超出屏幕左上角时，GetScreenRect 可能返回极端负值
                if (elementRect.Left < -element.ActualWidth * 2 && elementRect.Top < -element.ActualHeight * 2) return;

                lastElementRect = elementRect;

                // 直接用 SetWindowPos 设置物理像素坐标，绕过 WPF DIP 转换
                // SWP_NOZORDER | SWP_NOACTIVATE：不改变 Z 序、不激活窗口
                const uint flags = 0x0004 | 0x0010;
                SetWindowPos(selfWindowPtr, IntPtr.Zero, (int)Math.Round(elementRect.X), (int)Math.Round(elementRect.Y), (int)Math.Round(elementRect.Width), (int)Math.Round(elementRect.Height), flags);
            }
            catch (Exception ex)
            {
                Trace.TraceWarning($"[OverlayContentWindow] UpdatePosition(UIElement) 异常：{ex.Message}");
            }
        }
    }
}
