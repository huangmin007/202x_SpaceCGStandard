using System;
using System.Diagnostics;
using System.Windows.Markup;
using System.Windows;
using System.Windows.Input;
using System.Configuration;
using System.Windows.Controls;
using System.Linq;

namespace SpaceCG.Extensions
{
    /// <summary>
    /// Window 扩展方法
    /// </summary>
    public static partial class WindowExtensions
    {
        /// <summary>
        /// 初始化应用程序主窗口的基本设置
        /// <para>1. 设置默认语言为 zh-CN </para>
        /// <para>2. 读取/设置配置文件中的基本属性值</para>
        /// <para>3. 基本键盘快捷键操作；C键切换中文，E键切换英文，F9、F10键切换语言CN/EN，F11键切换全屏/非全屏，F12键切换显示/隐藏鼠标指针</para>
        /// <para>4. 鼠标右键切换鼠标指针隐藏/显示</para>
        /// <para>5. 元素的基本交互规则， Name、Tag 属性的处理</para>
        /// <para>6. 添加隐藏的关闭按钮</para>
        /// </summary>
        /// <param name="window"></param>
        public static void InitializeStandard(this Window window)
        {
            // 默认属性值
            window.Language = XmlLanguage.GetLanguage("zh-CN");
#if DEBUG
            window.Title = $"[DEBUG] {window.Title}";
            window.ResizeMode = ResizeMode.CanResizeWithGrip;
#else
            window.FullScreen();
#endif
            // 【优先级最低】App.config 中的配置项
            foreach (var key in ConfigurationManager.AppSettings.AllKeys)
            {
                var value = ConfigurationManager.AppSettings[key];
                if (string.IsNullOrWhiteSpace(value)) continue;
                if (InstanceExtensions.TrySetPropertyValue(window, key, value))
                {
                    Trace.TraceInformation($"ConfigurationManager.AppSettings Instance Property: {key}={value}");
                }
            }

            // 基本键盘快捷键操作
            window.PreviewKeyDown += (sender, e) =>
            {
                if (e.IsRepeat) return;
                if (Keyboard.FocusedElement is TextBox) return;

                var key = e.Key == Key.System ? e.SystemKey : e.Key == Key.ImeProcessed ? e.ImeProcessedKey : e.Key;
                Trace.TraceInformation($"Key Down: Key:{e.Key}, SystemKey:{e.SystemKey}, ImeProcessedKey:{e.ImeProcessedKey}");

                switch (key)
                {
                    case Key.D0:
                    case Key.D1:
                    case Key.D2:
                    case Key.D3:
                    case Key.D4:
                    case Key.D5:
                    case Key.D6:
                    case Key.D7:
                    case Key.D8:
                    case Key.D9:
                        {
                            var index = key - Key.D0;

                            var isAlt = (e.KeyboardDevice.Modifiers & ModifierKeys.Alt) == ModifierKeys.Alt;
                            var isShift = (e.KeyboardDevice.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;
                            var isControl = (e.KeyboardDevice.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;

                            if (isAlt) index += 10;
                            if (isShift) index += 10;
                            if (isControl) index += 10;

                            InstanceExtensions.TryInvokeMethod(window, "LoadItem", new object[] { index }, out _);
                        }
                        break;
                    case Key.NumPad0:
                    case Key.NumPad1:
                    case Key.NumPad2:
                    case Key.NumPad3:
                    case Key.NumPad4:
                    case Key.NumPad5:
                    case Key.NumPad6:
                    case Key.NumPad7:
                    case Key.NumPad8:
                    case Key.NumPad9:
                        {
                            var index = key - Key.NumPad0;

                            var isAlt = (e.KeyboardDevice.Modifiers & ModifierKeys.Alt) == ModifierKeys.Alt;
                            var isShift = (e.KeyboardDevice.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;
                            var isControl = (e.KeyboardDevice.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;

                            if (isAlt) index += 10;
                            if (isShift) index += 10;
                            if (isControl) index += 10;

                            InstanceExtensions.TryInvokeMethod(window, "LoadItem", new object[] { index }, out _);
                        }
                        break;

                    case Key.Back: { InstanceExtensions.TryInvokeMethod(window, "BackItem", out _); } break;
                    case Key.Home: { InstanceExtensions.TryInvokeMethod(window, "HomeItem", out _); } break;

                    case Key.Down:
                    case Key.Right:
                    case Key.PageDown: { InstanceExtensions.TryInvokeMethod(window, "NextItem", out _); } break;

                    case Key.Up:
                    case Key.Left:
                    case Key.PageUp: { InstanceExtensions.TryInvokeMethod(window, "PrevItem", out _); } break;

                    case Key.Enter:
                    case Key.Space: { InstanceExtensions.TryInvokeMethod(window, "PlayPause", out _); } break;

                    case Key.F9: window.ChangeLanguage(string.Empty); break;
                    case Key.C: window.Language = XmlLanguage.GetLanguage("zh-CN"); break;
                    case Key.E: window.Language = XmlLanguage.GetLanguage("en-US"); break;

                    // 窗口置顶或取消置顶
                    case Key.F10: window.Topmost = !window.Topmost; break;
                    // 全屏切换
                    case Key.F11: window.FullScreen(); break;
                    // 鼠标指针切换
                    case Key.F12: window.Cursor = window.Cursor == Cursors.None ? Cursors.Arrow : Cursors.None; break;
                }

                //e.Handled = true;
            };
            // 鼠标右键切换鼠标指针隐藏/显示，当鼠标右键点击三次时切换全屏/非全屏
            window.PreviewMouseRightButtonDown += (sender, e) =>
            {
                //if (e.ClickCount >= 3) window.FullScreen();
                window.Cursor = window.Cursor == Cursors.None ? Cursors.Arrow : Cursors.None;
            };

            // 基本的鼠标交互操作
            char[] underline = new char[] { '_' };
            char[] semicolon = new char[] { ';' };
            window.PreviewMouseDown += (s, e) =>
            {
                if (e.Source is FrameworkElement element && element.Cursor == Cursors.Hand)
                {
                    Debug.WriteLine($"【Default】 OnPreviewMouseDown FrameworkElement: {element.GetType()}  Name:{element.Name}  Tag:{element.Tag}");

                    var elementName = element.Name;
                    if (!string.IsNullOrWhiteSpace(elementName))
                    {
                        var elementTypeName = element.GetType().Name;
                        // Format: ElementType_Method_Parameter1_Parameter2_Parameter3
                        if (elementName.StartsWith($"{elementTypeName}_"))
                        {
                            var methodInfo = elementName.Substring(elementTypeName.Length + 1);
                            var array = methodInfo.Split(underline, StringSplitOptions.RemoveEmptyEntries);
                            if (array.Length < 1) return;

                            var methodName = array[0];
                            var parameters = array.Length > 1 ? array.Skip(1).ToArray() : null;
                            var result = InstanceExtensions.TryInvokeMethod(window, methodName, parameters, out _);
                            //e.Handled = result;
                        }
                    }

                    if (element.Tag != null && element.Tag is string elementTag && !string.IsNullOrWhiteSpace(elementTag) &&
                        !double.TryParse(elementTag, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out _))
                    {
                        // Format: Method_parameter1;Method_parameter1_parameter2;Method_parameter1_parameter2_parameter3
                        var methodInfos = elementTag.Split(semicolon, StringSplitOptions.RemoveEmptyEntries);
                        if (methodInfos.Length <= 0) return;

                        foreach (var methodInfo in methodInfos)
                        {
                            var array = methodInfo.Split(underline, StringSplitOptions.RemoveEmptyEntries);
                            if (array.Length < 1) continue;

                            var methodName = array[0];
                            var parameters = array.Length > 1 ? array.Skip(1).ToArray() : null;
                            var result = InstanceExtensions.TryInvokeMethod(window, methodName, parameters, out _);
                            //e.Handled = result;
                        }
                    }

                }
            };
        }

        /// <summary>
        /// 检查窗体是否处于全屏状态
        /// <para>如果窗体的 <see cref="Window.WindowState"/> 属性为 <see cref="WindowState.Maximized"/> 且 <see cref="Window.WindowStyle"/> 属性为 <see cref="WindowStyle.None"/>，则认为窗体处于全屏状态。</para>
        /// </summary>
        /// <param name="window"></param>
        /// <returns></returns>
        public static bool IsFullScreen(this Window window)
            => window.WindowState == WindowState.Maximized && window.WindowStyle == WindowStyle.None;

        /// <summary>
        /// 切换窗体程序的全屏与非全屏状态
        /// </summary>
        /// <param name="window"></param>
        public static void FullScreen(this Window window)
        {
            if (window.WindowState == WindowState.Maximized && window.WindowStyle == WindowStyle.None)
            {
                window.Cursor = Cursors.Arrow;
                window.WindowState = WindowState.Normal;
                window.ResizeMode = ResizeMode.CanResizeWithGrip;
                window.WindowStyle = WindowStyle.SingleBorderWindow;
            }
            else
            {
                window.Focus();
                window.Activate();
                window.Cursor = Cursors.None;
                window.WindowStyle = WindowStyle.None;
                window.ResizeMode = ResizeMode.NoResize;    // 需要支持 华为展厅灵犀中控
                window.WindowState = WindowState.Maximized;
            }
        }

        /// <summary>
        /// 切换窗体语言
        /// <para>如果 <paramref name="language"/> 参数为空，则在 zh-CN 和 en-US 之间切换；否则切换为指定的语言。</para>
        /// <para>窗体程序在 <see cref="FrameworkElement.Language"/> 属性更改时能得到通知：
        /// <code>DependencyPropertyDescriptor.FromProperty(FrameworkElement.LanguageProperty, typeof(FrameworkElement)).AddValueChanged(this, OnLanguageChanged);
        /// //或者在静态构造函数中注册
        /// LanguageProperty.OverrideMetadata(typeof(DemoBaseWindow), new FrameworkPropertyMetadata(XmlLanguage.GetLanguage("zh-CN"), OnLanguageChanged));
        /// </code>
        /// </para>
        /// </summary>
        /// <param name="window"></param>
        /// <param name="language"></param>
        public static void ChangeLanguage(this Window window, string language)
        {
            if (string.IsNullOrWhiteSpace(language) || 
                language.Equals("null", StringComparison.OrdinalIgnoreCase) || 
                language.Equals("auto", StringComparison.OrdinalIgnoreCase))
            {
                if (window.Language == XmlLanguage.GetLanguage("zh-CN"))
                {
                    window.Language = XmlLanguage.GetLanguage("en-US");
                }
                else
                {
                    window.Language = XmlLanguage.GetLanguage("zh-CN");
                }
            }
            else
            {
                switch (language.ToLower())
                {
                    case "0":
                    case "zh":
                    case "cn":
                    case "zh_cn": language = "zh-CN"; break;

                    case "1":
                    case "en":
                    case "us":
                    case "en_us": language = "en-US"; break;
                }

                try
                {
                    var xmlLanguage = XmlLanguage.GetLanguage(language);
                    window.Language = xmlLanguage;
                }
                catch (Exception ex)
                {
                    Trace.TraceError($"Failed to change language: {ex.Message}");
                    return;
                }
            }
        }
        /// <inheritdoc cref="ChangeLanguage(Window, string)"/> 
        public static void ChangeLanguage(this Window window) => ChangeLanguage(window, string.Empty);

        /// <summary>
        /// 获取配置(ConfigurationManager.AppSettings)的值分割参数数组，将字符串按照逗号、分号、冒号的顺序进行分割
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public static string[] SplitValues(this string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return Array.Empty<string>();

            string[] config;
            if (value.IndexOf(',') != -1) config = value.Split(',');
            else if (value.IndexOf(';') != -1) config = value.Split(';');
            else if (value.IndexOf(':') != -1) config = value.Split(':');
            else return Array.Empty<string>();

            return config;
        }

        /// <summary>
        /// 获取最顶层的可容纳多个子元素的容器。
        /// </summary>
        /// <param name="element"></param>
        /// <returns></returns>
        public static Panel FindFirstContainer(DependencyObject element)
        {
            while (element != null)
            {
                switch (element)
                {
                    // 命中 Panel（含所有子类：Grid, StackPanel, WrapPanel, DockPanel 等）
                    case Panel panel:
                        return panel;

                    // ContentControl 也可能包裹容器：ContentControl, Button, ListBoxItem 等
                    case ContentControl contentControl:
                        element = contentControl.Content as DependencyObject;
                        continue;

                    case Viewbox viewbox:
                        element = viewbox.Child;
                        continue;

                    // Decorator 是所有单内容包装器的基类：Viewbox, Border, AdornerDecorator, InkCanvas 等
                    case Decorator decorator:   
                        element = decorator.Child;
                        continue;

                    default:
                        return null;
                }
            }

            return null;
        }

        
    }
}
