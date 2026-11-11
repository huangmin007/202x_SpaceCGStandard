using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace SpaceCG.Converters
{
    /// <summary>
    /// 实现 <see cref="bool"/> 与 <see cref="Visibility"/> 之间的双向转换。
    /// </summary>
    /// <remarks>
    /// <para><strong>默认转换规则：</strong></para>
    /// <list type="bullet">
    ///   <item><description><c>true</c> → <see cref="Visibility.Visible"/></description></item>
    ///   <item><description><c>false</c> → <see cref="Visibility.Hidden"/>（默认）或 <see cref="Visibility.Collapsed"/>（当 <see cref="UseCollapsed"/> 为 <c>true</c>）</description></item>
    /// </list>
    /// <para>当 <see cref="Inverse"/> 为 <c>true</c> 时，转换逻辑取反。</para>
    /// <para><strong>XAML 使用示例：</strong></para>
    /// <code>
    /// &lt;Window.Resources&gt;
    ///   &lt;!-- 默认：false → Hidden --&gt;
    ///   &lt;local:BoolToVisibilityConverter x:Key="BoolToVis"/&gt;
    ///   &lt;!-- 配置：false → Collapsed --&gt;
    ///   &lt;local:BoolToVisibilityConverter x:Key="BoolToVisCollapsed" UseCollapsed="True"/&gt;
    ///   &lt;!-- 反转：true → Hidden/Collapsed --&gt;
    ///   &lt;local:BoolToVisibilityConverter x:Key="BoolToVisInverse" Inverse="True"/&gt;
    /// &lt;/Window.Resources&gt;
    /// 
    /// &lt;!-- 绑定使用 --&gt;
    /// &lt;TextBlock Visibility="{Binding IsVisible, Converter={StaticResource BoolToVis}}"/&gt;
    /// &lt;Button Visibility="{Binding IsLoading, Converter={StaticResource BoolToVisCollapsed}, ConverterParameter=Inverse}"/&gt;
    /// </code>
    /// </remarks>
    [ValueConversion(typeof(bool), typeof(Visibility))]
    public class BooleanVisibilityConverter : IValueConverter
    {
        /// <summary>
        /// 获取或设置是否 反转 转换 逻辑。
        /// </summary>
        /// <value>
        /// <c>true</c> 表示反转：输入 <c>true</c> 时返回 <see cref="Visibility.Hidden"/>/<see cref="Visibility.Collapsed"/>，
        /// 输入 <c>false</c> 时返回 <see cref="Visibility.Visible"/>；默认为 <c>false</c>。
        /// </value>
        public bool Inverse { get; set; } = false;
        
        /// <summary>
        /// 获取或设置当布尔值为 <c>false</c>（或反转后为 <c>false</c>）时，返回 <see cref="Visibility.Collapsed"/> 而非 <see cref="Visibility.Hidden"/>。
        /// </summary>
        /// <value>
        /// <c>true</c> 表示使用 <see cref="Visibility.Collapsed"/>（元素不占据布局空间）；
        /// <c>false</c> 表示使用 <see cref="Visibility.Hidden"/>（元素占据布局空间但不可见）；默认为 <c>false</c>。
        /// </value>
        /// <remarks>
        /// <para><see cref="Visibility.Hidden"/>：元素不可见，但仍占据布局空间（可能影响排版）。</para>
        /// <para><see cref="Visibility.Collapsed"/>：元素不可见，且不占据布局空间（推荐用于动态显示/隐藏）。</para>
        /// </remarks>
        public bool UseCollapsed { get; set; } = false;

        /// <summary>
        /// 将 <see cref="bool"/> 值转换为 <see cref="Visibility"/> 值。
        /// </summary>
        /// <param name="value">源值（期望为 <see cref="bool"/> 或 <see cref="bool?"/>）。</param>
        /// <param name="targetType">目标类型（应为 <see cref="Visibility"/>）。</param>
        /// <param name="parameter">可选参数（本转换器未使用，保留接口兼容）。</param>
        /// <param name="culture">文化信息（本转换器未使用，保留接口兼容）。</param>
        /// <returns>
        /// 转换后的 <see cref="Visibility"/> 值。
        /// </returns>
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool inputValue = false;
            if (value is bool boolValue)
            {
                inputValue = boolValue;
            }
            else if (value is bool?)
            {
                var nullableBool = (bool?)value;
                inputValue = nullableBool.HasValue && nullableBool.Value;
            }
            
            // 根据 UseCollapsed 决定返回 Hidden 还是 Collapsed
            var invisibleValue = UseCollapsed ? Visibility.Collapsed : Visibility.Hidden;

            return Inverse ? (inputValue ? invisibleValue : Visibility.Visible) : (inputValue ? Visibility.Visible : invisibleValue);
        }

        /// <summary>
        /// 将 <see cref="Visibility"/> 值转换回 <see cref="bool"/> 值。
        /// </summary>
        /// <param name="value">源值（期望为 <see cref="Visibility"/>）。</param>
        /// <param name="targetType">目标类型（应为 <see cref="bool"/>）。</param>
        /// <param name="parameter">可选参数（本转换器未使用，保留接口兼容）。</param>
        /// <param name="culture">文化信息（本转换器未使用，保留接口兼容）。</param>
        /// <returns>
        /// 转换后的 <see cref="bool"/> 值：<see cref="Visibility.Visible"/> → <c>true</c>，
        /// <see cref="Visibility.Hidden"/> 或 <see cref="Visibility.Collapsed"/> → <c>false</c>；
        /// </returns>
        /// <remarks>
        /// <para>双向绑定场景下，<see cref="Visibility.Hidden"/> 和 <see cref="Visibility.Collapsed"/> 均映射为 <c>false</c>。</para>
        /// <para>若需区分 Hidden/Collapsed，请继承此类并重写此方法。</para>
        /// </remarks>
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool isVisible = false;
            if (value is Visibility visibility)
            {
                isVisible = (visibility == Visibility.Visible);
            }

            return Inverse ? !isVisible : isVisible;
        }

        #region 静态预置实例（XAML 资源复用）
        /// <summary>
        /// 获取默认配置的转换器实例（<see cref="Inverse"/> 和 <see cref="UseCollapsed"/> 均为 <c>false</c>）。
        /// </summary>
        /// <remarks>
        /// 适用于 XAML 中静态资源复用，避免重复创建实例。
        /// <para>⚠️ 注意：该实例为共享状态，若需修改属性请使用新实例，避免影响其他绑定。</para>
        /// </remarks>
        public static BooleanVisibilityConverter Default { get; } = new BooleanVisibilityConverter();

        /// <summary>
        /// 获取 <see cref="UseCollapsed"/> 为 <c>true</c> 的转换器实例（false → Collapsed）。
        /// </summary>
        public static BooleanVisibilityConverter CollapseWhenFalse { get; } = new BooleanVisibilityConverter { UseCollapsed = true };

        /// <summary>
        /// 获取 <see cref="Inverse"/> 为 <c>true</c> 的转换器实例（逻辑反转）。
        /// </summary>
        public static BooleanVisibilityConverter InverseDefault { get; } = new BooleanVisibilityConverter { Inverse = true };
        #endregion
    }

}
