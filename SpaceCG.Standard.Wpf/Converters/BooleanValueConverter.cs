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
    /// 将多种类型的值转换为 <see cref="bool"/> (只支持正向转换，单向转换)，支持通过 <see cref="CustomValues"/> 自定义字符串真值映射。
    /// </summary>
    /// <remarks>
    /// <para>当配置 <see cref="CustomValues"/> 时，字符串匹配优先级：<see cref="CustomValues"/> > 默认规则。</para>
    /// <para><strong>XAML 使用示例：</strong></para>
    /// <code>
    /// &lt;Window.Resources&gt;
    ///   &lt;!-- 默认规则 --&gt;
    ///   &lt;conv:ObjectToBooleanConverter x:Key="ObjToBool"/&gt;
    ///   
    ///   &lt;!-- 自定义真值：仅 "enable"/"active" 视为 true --&gt;
    ///   &lt;conv:ObjectToBooleanConverter x:Key="StrictTrue"&gt;
    ///     &lt;conv:ObjectToBooleanConverter.CustomValues&gt;
    ///       &lt;x:String&gt;enable&lt;/x:String&gt;
    ///       &lt;x:String&gt;active&lt;/x:String&gt;
    ///     &lt;/conv:ObjectToBooleanConverter.CustomValues&gt;
    ///   &lt;/conv:ObjectToBooleanConverter&gt;
    /// &lt;/Window.Resources&gt;
    /// 
    /// &lt;Button IsEnabled="{Binding Status, Converter={StaticResource StrictTrue}}"/&gt;
    /// </code>
    /// </remarks>
    [ValueConversion(typeof(object), typeof(bool))]
    public class BooleanValueConverter : IValueConverter
    {
        /// <summary>
        /// 默认的 <c>true</c> 值集合。
        /// <para>默认集合值包括：<c>{ "true", "1", "yes", "y", "on" }</c>。</para>
        /// </summary>
        public static readonly HashSet<string> DefaultValues = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "true", "1", "yes", "y", "on"
        };

        /// <summary>
        /// 获取或设置是否反转最终转换结果。
        /// </summary>
        public bool Inverse { get; set; } = false;

        /// <summary>
        /// 获取或设置自定义的 <c>true</c> 值集合。
        /// <para>当要转换的值匹配集合中任意项时，对应映射值为 <c>true</c>。</para>
        /// </summary>
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
            if (targetType != typeof(bool) && targetType != typeof(bool?))
                throw new NotSupportedException($"{nameof(VisibilityValueConverter)} 仅支持转换到 {nameof(Boolean)} 类型，当前目标类型: {targetType}");
            //return DependencyProperty.UnsetValue;

            if (value is null) return Inverse ? true : false;

            // bool
            if (value is bool boolValue) return Inverse ? !boolValue : boolValue;
            
            // Visibility
            if (value is Visibility visibility) return Inverse ? (visibility != Visibility.Visible) : (visibility == Visibility.Visible);

            // string
            if (value is string stringValue)
            {
                foreach (var item in CustomValues)
                {
                    if (item is string itemValue && stringValue.Equals(itemValue, StringComparison.OrdinalIgnoreCase))
                    {
                        return Inverse ? false : true;
                    }
                }

                return Inverse ? true : false;
            }
            
            // 自定义 True 值集合
            var isTrueValue = false;
            var valueType = value.GetType();
            foreach (var item in CustomValues)
            {
                if (item == null) continue;
                if (item.GetType() == valueType && item.Equals(value))
                {
                    isTrueValue = true;
                    break;
                }
            }

            return Inverse ? !isTrueValue : isTrueValue;
        }

        /// <inheritdoc /> 
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
#if true
            if (value == null || targetType == null) return Binding.DoNothing;
            if (value.GetType() != typeof(bool))
                throw new NotSupportedException($"{nameof(BooleanValueConverter)} 仅支持转换回 {nameof(Boolean)} 类型，当前源类型: {value.GetType()}");

            var boolValue = (bool)value;

            // bool
            if (targetType == typeof(bool))
            {
                return Inverse ? !boolValue : boolValue;
            }

            // Visibility
            if (targetType == typeof(Visibility))
            {
                return Inverse ? (boolValue ? Visibility.Collapsed : Visibility.Visible) : (boolValue ? Visibility.Visible : Visibility.Collapsed);
            }

            // string
            if (targetType == typeof(string))
            {
                return (Inverse ? !boolValue : boolValue).ToString().ToUpperInvariant();
            }
#endif
            return Binding.DoNothing;
        }

    }
}
