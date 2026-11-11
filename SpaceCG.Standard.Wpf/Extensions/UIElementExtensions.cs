using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Xml.Linq;

namespace SpaceCG.Extensions
{
    /// <summary>
    /// UIElement 扩展方法
    /// </summary>
    public static partial class UIElementExtensions
    {        
        /// <summary>
        /// 动态加载 XAML 标记元素到容器中。
        /// <para>先清空容器中的所有子元素，然后逐个解析 XAML 标记，并添加到容器中。</para>
        /// </summary>
        /// <param name="container"></param>
        /// <param name="xamlElements"></param>
        public static void LoadDynamicElements(this Panel container, IEnumerable<XElement> xamlElements)
        {
            if (xamlElements == null || !xamlElements.Any()) return;

            var window = Window.GetWindow(container);
            var local = window.GetType().Namespace;

            ParserContext parserContext = new ParserContext();
            parserContext.BaseUri = new Uri("pack://siteoforigin:,,,/");
            parserContext.XmlnsDictionary.Add(nameof(local), local);
            parserContext.XmlnsDictionary.Add("x", "http://schemas.microsoft.com/winfx/2006/xaml");
            parserContext.XmlnsDictionary.Add("", "http://schemas.microsoft.com/winfx/2006/xaml/presentation");
            
            container.Children.Clear();
            foreach (XElement element in xamlElements)
            {
                try
                {                   
                    var uiElement = XamlReader.Parse(element.ToString(), parserContext) as FrameworkElement;
                    if (uiElement == null) continue;

                    uiElement.DataContext = container.DataContext;
                    container.Children.Add(uiElement);
                }
                catch (Exception ex)
                {
                    Trace.TraceError($"解析 XAML 标记异常：{ex.Message}");
                    MessageBox.Show($"解析 XAML 标记异常：{ex.Message}", "解析异常", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
            }
        }

        /// <summary>
        /// 更新容器中的子元素的可见性，子元素的 Tag 值与 currentTag 值匹配则显示，否则隐藏。
        /// <para>跟据子元素的 Tag 属性值来判断是否显示，Tag 属性值可以是数字或逗号分隔的数字列表。</para>
        /// </summary>
        /// <param name="container"></param>
        /// <param name="itemTag"></param>
        public static void UpdateChildrenVisibility(this Panel container, int itemTag)
        {
            char[] separator = new char[] { ',' };
            for (var i = 0; i < container.Children.Count; i++)
            {
                if (container.Children[i] is FrameworkElement uiElement)
                {
                    var tagContent = uiElement.Tag?.ToString();
                    if (string.IsNullOrWhiteSpace(tagContent)) continue;

                    uiElement.Visibility = ContainsTag(tagContent, itemTag) ? Visibility.Visible : Visibility.Hidden;
                }
            }
        }

        /// <summary>
        /// 检查指定的 ItemTag 是否包含在给定的字符串中。
        /// </summary>
        /// <param name="tags"></param>
        /// <param name="itemTag"></param>
        /// <returns></returns>
        public static bool ContainsTag(string tags, int itemTag)
        {
            if (string.IsNullOrWhiteSpace(tags)) return false;

            foreach (var item in tags.Split(','))
            {
                if (int.TryParse(item, out int tag) && tag == itemTag)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 查找指定类型的子元素
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="parent"></param>
        /// <param name="predicate"></param>
        /// <returns></returns>
        public static T FindElement<T>(this Panel parent, Func<T, bool> predicate = null) where T : FrameworkElement
        {
            if (parent == null) return null;

            foreach (UIElement child in parent.Children)
            {
                if (child is T element)
                {
                    if (predicate == null || predicate(element))
                        return element;
                }

                if (child is Panel subPanel)
                {
                    T result = FindElement(subPanel, predicate);

                    if (result != null)
                        return result;
                }
            }

            return null;
        }
        /// <summary>
        /// 查找指定类型的子元素集合
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="parent"></param>
        /// <param name="predicate"></param>
        /// <returns></returns>
        public static IEnumerable<T> FindElements<T>(this Panel parent, Func<T, bool> predicate = null) where T : FrameworkElement
        {
            if (parent == null) yield break;

            foreach (UIElement child in parent.Children)
            {
                if (child is T element)
                {
                    if (predicate == null || predicate(element))
                        yield return element;
                }

                if (child is Panel subPanel)
                {
                    foreach (var item in FindElements<T>(subPanel, predicate))
                    {
                        yield return item;
                    }
                }
            }
        }

        /// <summary>
        /// 获取 UIElement 在屏幕上的实际矩形区域（物理像素，与屏幕分辨率一致）。
        /// <para>
        /// 此方法先通过 TransformToAncestor 获取元素在根窗口中的 DIP 矩形，
        /// 再通过根窗口的 PointToScreen（物理像素）和 DPI 转换统一为物理像素坐标。
        /// 能正确处理 Viewbox、RenderTransform 等变换场景。
        /// </para>
        /// <para>返回的坐标与 Win32 GetWindowRect 在同一坐标系，可直接用于 SetWindowPos。</para>
        /// </summary>
        /// <param name="element">目标 UI 元素</param>
        /// <returns>元素在屏幕上的物理像素矩形区域；若元素未加载或不可见则返回 <see cref="Rect.Empty"/></returns>
        public static Rect GetScreenRect(this UIElement element)
        {
            if (element == null) return Rect.Empty;

            try
            {
                var source = PresentationSource.FromVisual(element);
                if (source == null) return Rect.Empty;

                var rootVisual = source.RootVisual;
                if (rootVisual == null) return Rect.Empty;

                // 1. 获取元素在根窗口坐标系中的 DIP 矩形
                //    TransformToAncestor 自动累积所有变换（Viewbox、RenderTransform 等）
                var transform = element.TransformToAncestor(rootVisual);
                var elementRectDip = transform.TransformBounds(new Rect(element.RenderSize));

                // 2. 获取根窗口左上角的屏幕物理像素坐标
                var rootScreenPixel = rootVisual.PointToScreen(new Point(0, 0));

                // 3. 获取该元素的 DPI 缩放因子（用于 DIP → 物理像素）
                var dpi = source.CompositionTarget?.TransformToDevice ?? Matrix.Identity;
                var dpiScaleX = dpi.M11;
                var dpiScaleY = dpi.M22;

                // 4. 统一转换为物理像素
                return new Rect(
                    rootScreenPixel.X + elementRectDip.X * dpiScaleX,
                    rootScreenPixel.Y + elementRectDip.Y * dpiScaleY,
                    elementRectDip.Width * dpiScaleX,
                    elementRectDip.Height * dpiScaleY);
            }
            catch (Exception ex)
            {
                Trace.TraceWarning($"[GetScreenRect] 获取元素屏幕位置异常：{ex.Message}");
                return Rect.Empty;
            }
        }



    }
}
