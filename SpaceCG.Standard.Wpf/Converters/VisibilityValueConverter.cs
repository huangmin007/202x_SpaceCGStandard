using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Data;

namespace SpaceCG.Converters
{
    /// <summary>
    /// 反转 Visibility 值的转换器。
    /// </summary>
    [ValueConversion(typeof(Visibility), typeof(Visibility))]
    public class InverseVisibilityValueConverter : IValueConverter
    {
        /// <summary>
        /// 不可见时返回的 Visibility 值。默认 Collapsed
        /// </summary>
        public Visibility InvisibleVisibility { get; set; } = Visibility.Collapsed;

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (!(value is Visibility visibility)) return InvisibleVisibility;

            return visibility == Visibility.Visible ? InvisibleVisibility : Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (!(value is Visibility visibility)) return InvisibleVisibility;

            return visibility == Visibility.Visible ? InvisibleVisibility : Visibility.Visible;
        }
    }

    /// <summary>
    /// 将多种类型的值转换为 <see cref="Visibility"/>。只支持单向转换.
    /// </summary>
    [ValueConversion(typeof(object), typeof(Visibility))]
    public class VisibilityValueConverter : IValueConverter
    {
        /// <summary>
        /// 默认的 <see cref="Visibility.Visible"/> 值集合（忽略大小写）。
        /// <para>默认集合值包括：<c>{ "true", "1", "yes", "y", "on" }</c>。</para>
        /// </summary>
        public static readonly HashSet<string> DefaultValues = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "true", "1", "yes", "y", "on"
        };

        /// <summary>
        /// 获取或设置是否反转最终转换结果，默认值为 <c>false</c>。
        /// <para>若设置为 <c>true</c>，则转换结果为 <see cref="Visibility.Visible"/> 时返回 <see cref="Visibility.Hidden"/> 或 <see cref="Visibility.Collapsed"/>，反之亦然。</para>
        /// </summary>
        public bool Inverse { get; set; } = false;

        /// <summary>
        /// 不可见时是否使用 <see cref="Visibility.Collapsed"/> 而非 <see cref="Visibility.Hidden"/>，默认为 <c>false</c>。
        /// </summary>
        /// <value>
        /// <c>true</c> 表示使用 <see cref="Visibility.Collapsed"/>（元素不占据布局空间）；
        /// <c>false</c> 表示使用 <see cref="Visibility.Hidden"/>（元素占据布局空间但不可见）；
        /// 默认为 <c>false</c>。
        /// </value>
        /// <remarks>
        /// <para><see cref="Visibility.Hidden"/>：元素不可见，但仍占据布局空间（可能影响排版）。</para>
        /// <para><see cref="Visibility.Collapsed"/>：元素不可见，且不占据布局空间（推荐用于动态显示/隐藏）。</para>
        /// </remarks>
        public bool UseCollapsed { get; set; } = false;

        /// <summary>
        /// 获取或设置自定义的 <see cref="Visibility.Visible"/> 值集合。
        /// </summary>
        /// <value>
        /// 当输入字符串匹配集合中任意项时，对应值为 <see cref="Visibility.Visible"/>。
        /// 若未设置自定义集合，则使用默认值集合 <see cref="DefaultValues"/>。
        /// </value>
        /// <remarks></remarks>
        public Collection<object> CustomValues
        {
            set => _customValues = value;
            get
            {
                if (_customValues == null)
                    _customValues = new ObservableCollection<object>(DefaultValues.ToArray());
                return _customValues;
            }
        }
        private Collection<object> _customValues;

        /// <inheritdoc />
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (targetType != typeof(Visibility) && targetType != typeof(Visibility?))
                throw new NotSupportedException($"{nameof(VisibilityValueConverter)} 仅支持转换到 {nameof(Visibility)} 类型，当前目标类型: {targetType}");
            //return DependencyProperty.UnsetValue;

            // 不可见的值
            var invisibleValue = UseCollapsed ? Visibility.Collapsed : Visibility.Hidden;
            if (value == null) return invisibleValue;

            // bool
            if (value is bool boolValue)
            {
                return Inverse ? (boolValue ? invisibleValue : Visibility.Visible) : (boolValue ? Visibility.Visible : invisibleValue);
            }

            // Visibility
            if (value is Visibility visibility)
            {
                return Inverse ? (visibility == Visibility.Visible ? invisibleValue : Visibility.Visible) : visibility;
            }

            // string
            if (value is string stringValue)
            {
                foreach (var item in CustomValues)
                {
                    if (item is string itemValue && stringValue.Equals(itemValue, StringComparison.OrdinalIgnoreCase))
                    {
                        return Inverse ? invisibleValue : Visibility.Visible;
                    }
                }

                return Inverse ? Visibility.Visible : invisibleValue;
            }

            // other 自定义 Visible 值集合
            var isVisible = invisibleValue;
            var valueType = value.GetType();
            foreach (var item in CustomValues)
            {
                if (item == null) continue;
                if (item.GetType() == valueType && item.Equals(value))
                {
                    isVisible = Visibility.Visible;
                    break;
                }
            }

            return Inverse ? (isVisible == Visibility.Visible ? invisibleValue : Visibility.Visible) : isVisible;
        }

        /// <inheritdoc />
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
#if true
            if (value == null || targetType == null) return Binding.DoNothing;

            if (value.GetType() != typeof(Visibility))
                throw new NotSupportedException($"{nameof(VisibilityValueConverter)} 仅支持转换回 {nameof(Visibility)} 类型，当前源类型: {value.GetType()}");

            var visibility = (Visibility)value;

            // bool
            if (targetType == typeof(bool))
            {
                return Inverse ? (visibility != Visibility.Visible) : (visibility == Visibility.Visible);
            }

            // Visibility
            if (targetType == typeof(Visibility))
            {
                var invisibleValue = UseCollapsed ? Visibility.Collapsed : Visibility.Hidden;
                return Inverse ? (visibility == Visibility.Visible ? invisibleValue : Visibility.Visible) : visibility;
            }

            // string
            if (targetType == typeof(string))
            {
                var invisibleValue = UseCollapsed ? Visibility.Collapsed : Visibility.Hidden;
                var returnValue = Inverse ? (visibility == Visibility.Visible ? invisibleValue : visibility) : (visibility);

                return $"{invisibleValue}({(int)invisibleValue})";
            }
#endif
            return Binding.DoNothing;
        }

        /// <summary>
        /// 应用 Inverse 逻辑：反转可见性状态
        /// </summary>
        /// <param name="current">当前计算出的 Visibility</param>
        /// <param name="inverse">是否启用反转</param>
        /// <param name="invisibleValue">预设的不可见值（Hidden 或 Collapsed）</param>
        /// <returns>最终返回的 Visibility</returns>
        private static Visibility ApplyInverse(Visibility current, bool inverse, Visibility invisibleValue)
        {
            if (!inverse) return current;
            return current == Visibility.Visible ? invisibleValue : Visibility.Visible;
        }

    }

}
