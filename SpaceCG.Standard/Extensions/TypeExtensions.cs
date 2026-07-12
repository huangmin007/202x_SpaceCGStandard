using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;

namespace SpaceCG.Extensions
{
    /// <summary>
    /// Type Extensions
    /// </summary>
    public static partial class TypeExtensions
    {
        /// <summary>
        /// 缓存类型转换器，避免每次调用时反射带来的性能开销。
        /// </summary>
        internal static readonly ConcurrentDictionary<Type, TypeConverter> TypeConverterCache = new ConcurrentDictionary<Type, TypeConverter>();

        internal static readonly ConcurrentDictionary<Type, Type[]> CacheTypeInterfaces = new ConcurrentDictionary<Type, Type[]>();
        
        /// <summary>
        /// 获取类型的自定义签名。SVT:String and Value Type
        /// <para>用于反射 MothodInfo 的参数类型签名。</para>
        /// </summary>
        /// <param name="paramType"></param>
        /// <returns></returns>
        internal static string GetTypeSignature(Type paramType)
        {
            if (paramType == null) return "";
            if (paramType.IsEnum || paramType.IsValueType || paramType == typeof(string)) return "SVT";

            if (paramType.IsArray) return $"[{GetTypeSignature(paramType.GetElementType())}]";
            if (paramType.IsGenericType)
            {
                // 都是支持 数组 输入的参数类型
                var genericTypeDef = paramType.GetGenericTypeDefinition();
                if (genericTypeDef == typeof(IEnumerable<>) || 
                    genericTypeDef == typeof(IList<>) || genericTypeDef == typeof(IReadOnlyList<>) ||
                    genericTypeDef == typeof(ICollection<>) || genericTypeDef == typeof(IReadOnlyCollection<>))
                {
                    var genericArgs = paramType.GetGenericArguments();
                    if (genericArgs.Length == 1) return $"[{GetTypeSignature(genericArgs[0])}]";
                    else return $"[<REF...>]";
                }
            }

            return "REF";
        }
        /// <inheritdoc cref="GetTypeSignature(Type)"/> 
        internal static string GetTypesSignature(this IEnumerable<Type> paramTypes) => string.Join(",", paramTypes.Select(t => GetTypeSignature(t)));

        /// <summary>
        /// 获取参数值的自定义签名。SVT:String and Value Type
        /// <para>用于输入文本参数 objecct[] 的参数值签名。</para>
        /// </summary>
        /// <param name="paramValue"></param>
        /// <returns></returns>
        internal static string GetParameterSignature(object paramValue)
        {
            if (paramValue == null) return "";
            var valueType = paramValue.GetType();

            if (valueType.IsEnum || valueType.IsValueType || valueType == typeof(string)) return "SVT";
            if (valueType.IsArray)
            {
                if (paramValue is object[] array) return $"[{GetParameterSignature(array.GetValue(0))}]";
                else return $"[{GetParameterSignature(valueType.GetElementType())}]";
            }

            return "REF";
        }
        /// <inheritdoc cref="GetParameterSignature(object)"/> 
        internal static string GetParametersSignature(this IEnumerable<object> paramValues) => string.Join(",", paramValues.Select(t => GetParameterSignature(t)));

        /// <summary>
        /// 判断类型是否实现 IEnumerable&lt;T&gt;
        /// </summary>
        /// <param name="type"></param>
        public static bool IsIEnumerable(this Type type)
        {
            if (type == null) return false;

            Type iEnumerableType = typeof(IEnumerable<>);
            var typeInterfaces = CacheTypeInterfaces.GetOrAdd(type, t => t.GetInterfaces());

            // 检查类型是否直接实现 IEnumerable<T>
            foreach (var interfaceType in typeInterfaces)
            {
                if (interfaceType.IsGenericType && interfaceType.GetGenericTypeDefinition() == iEnumerableType)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 将 <see cref="StringExtensions.ToObjectArray"/> 产出的参数值转换为目标方法参数的强类型值，是本转换流水线的核心调度器。
        /// <para>处理三种分支：</para>
        /// <list type="number">
        /// <item><b>类型兼容</b>：值类型与目标类型一致或可赋值 → 直接返回。</item>
        /// <item><b>标量转换</b>：目标类型为值类型或 string → 委托给 <see cref="StringExtensions.TryConvertTo(string, Type, out object)"/> 将叶子 string 转为强类型值。</item>
        /// <item><b>数组/集合转换</b>：目标类型为数组或 IEnumerable&lt;T&gt; → 委托给 <see cref="TryConvertToArray"/> 逐元素递归转换。</item>
        /// </list>
        /// <para>典型调用链：<c>TryConvertTo(element, targetType, out result)</c> → 对叶子 string 调 <c>TryConvertTo</c>，
        /// 对嵌套数组调 <c>TryConvertToArray</c> → 内部再次调 <c>TryConvertTo</c> 处理每个子元素。</para>
        /// </summary>
        /// <param name="value">待转换的值（顶层来自 <see cref="StringExtensions.ToObjectArray"/> 的输出；叶子一定是 <see cref="string"/>）。</param>
        /// <param name="targetType">目标方法参数类型。</param>
        /// <param name="conversionValue">转换后的强类型值。</param>
        /// <returns>转换成功返回 <c>true</c>；否则返回 <c>false</c>。</returns>
        /// <seealso cref="TryConvertToArray"/>
        /// <seealso cref="StringExtensions.TryConvertTo(string, Type, out object)"/>
        public static bool TryConvertTo(object value, Type targetType, out object conversionValue)
        {
            conversionValue = null;
            if (targetType == null) return false;
            if (value == null || targetType == typeof(void)) return true;

            Type valueType = value.GetType();
            // 类型兼容检查（含接口实现关系）
            if (!targetType.IsArray && (valueType == targetType || targetType.IsAssignableFrom(valueType)))
            {
                conversionValue = value;
                return true;
            }
            // 标量：值类型 & 字符串 转换
            if ((targetType.IsValueType || targetType == typeof(string)) && (valueType.IsValueType || valueType == typeof(string)))
            {
                if (value is string stringValue && stringValue.TryConvertTo(targetType, out var targetValue)) conversionValue = targetValue;
                else conversionValue = value;
                return true;
            }

            // 数组：元素类型相同 → 直接赋值
            if (targetType.IsArray && valueType.IsArray && targetType.GetElementType() == valueType.GetElementType())
            {
                conversionValue = value;
                return true;
            }
            // 数组：元素类型不同 → 递归转换每个元素
            if (targetType.IsArray && valueType.IsArray && targetType.GetElementType() != valueType.GetElementType())
            {
                return TryConvertToArray((Array)value, targetType.GetElementType(), out conversionValue);
            }

            // 目标类型是：System.Collections.IEnumerable<T> 或实现、继承 IEnumerable<T> 接口的子类
            if (targetType.IsGenericType && valueType.IsArray && targetType.GetGenericArguments()?.Length == 1)
            {
                var genericTypeDefinition = targetType.GetGenericTypeDefinition();
                if (genericTypeDefinition == typeof(IEnumerable<>) || genericTypeDefinition.IsIEnumerable())
                {
                    return TryConvertToArray((Array)value, targetType.GetGenericArguments()[0], out conversionValue);
                }
            }

            return false;
        }

        [Obsolete("建议使用 TryConvertTo", false)]
        public static bool ConvertFrom(object value, Type destinationType, out object conversionValue) => TryConvertTo(value, destinationType, out conversionValue);

        //public static bool TryConvertParameters(IEnumerable<object> values, IEnumerable<ParameterInfo> parameters, out object[] conversionValues)
        //{        }

        /// <summary>
        /// 将 <c>object[]</c> 转换为指定元素类型的强类型数组，对每个元素递归调用 <see cref="TryConvertTo"/>。
        /// <para>由 <see cref="TryConvertTo"/> 在检测到目标类型为数组或 IEnumerable&lt;T&gt; 时调用。</para>
        /// <para>容错策略：单个元素转换失败时，不终止整个数组转换，而是用元素类型的默认值填充该位置，并继续处理后续元素。</para>
        /// <para>支持多层嵌套数组：因为 <see cref="TryConvertTo"/> 在遇到嵌套数组时会再次调入本方法。</para>
        /// </summary>
        /// <param name="valueArray">源 object[] 数组（叶子是 string，嵌套子数组也是 object[]）。</param>
        /// <param name="elementType">目标数组的元素类型。</param>
        /// <param name="conversionValue">输出的强类型数组（如 <c>int[]</c>、<c>bool[][]</c>）。</param>
        /// <returns>始终返回 <c>true</c>（失败元素填充默认值，不中断整体转换）。</returns>
        /// <seealso cref="TryConvertTo"/>
        public static bool TryConvertToArray(Array valueArray, Type elementType, out object conversionValue)
        {
            conversionValue = null;
            if (valueArray == null || elementType == null) return false;

            Array instanceValue = Array.CreateInstance(elementType, valueArray.Length);

            for (int i = 0; i < valueArray.Length; i++)
            {
                if (!TryConvertTo(valueArray.GetValue(i), elementType, out object cValue))
                {
                    // 转换失败时填充元素类型的默认值
                    cValue = elementType.IsValueType ? Activator.CreateInstance(elementType) : null;
                }
                instanceValue.SetValue(cValue, i);
            }

            conversionValue = instanceValue;
            return true;
        }

    }
}
