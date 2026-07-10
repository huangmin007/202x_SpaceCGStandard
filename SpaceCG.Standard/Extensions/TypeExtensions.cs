using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;

namespace SpaceCG.Extensions
{
    /// <summary>
    /// Type Extensions
    /// </summary>
    public static partial class TypeExtensions
    {
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
        internal static string GetParamSignature(object paramValue)
        {
            if (paramValue == null) return "";
            var valueType = paramValue.GetType();

            if (valueType.IsEnum || valueType.IsValueType || valueType == typeof(string)) return "SVT";
            if (valueType.IsArray)
            {
                if (paramValue is object[] array) return $"[{GetParamSignature(array.GetValue(0))}]";
                else return $"[{GetParamSignature(valueType.GetElementType())}]";
            }

            return "REF";
        }
        /// <inheritdoc cref="GetParamSignature(object)"/> 
        internal static string GetParamsSignature(this IEnumerable<object> paramValues) => string.Join(",", paramValues.Select(t => GetParamSignature(t)));

        /// <summary>
        /// 判断类型是否实现 IEnumerable&lt;T&gt;
        /// </summary>
        /// <param name="type"></param>
        internal static bool IsIEnumerable(this Type type)
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
        /// 尝试将调用参数值转换为目标方法参数类型。
        /// <para>支持标量值类型、字符串、数组和 IEnumerable&lt;T&gt; 多层集合类型的递归转换。</para>
        /// </summary>
        /// <param name="value">原始参数值（来自 <see cref="StringExtensions.ToObjectArray"/> 输出，叶子节点均为 <see cref="string"/>）。</param>
        /// <param name="targetType">目标参数类型。</param>
        /// <param name="conversionValue">输出的转换后值。</param>
        /// <returns>转换成功返回 <c>true</c>；否则返回 <c>false</c>。</returns>
        internal static bool TryConvertParameter(object value, Type targetType, out object conversionValue)
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

        /// <summary>
        /// 将 object[] 转换为强类型数组，递归转换每个元素。
        /// <para>单个元素转换失败时，使用元素类型的默认值填充，不终止整个数组的转换。</para>
        /// </summary>
        /// <param name="valueArray">源 object[] 数组。</param>
        /// <param name="elementType">目标元素类型。</param>
        /// <param name="conversionValue">输出的强类型数组。</param>
        /// <returns>始终返回 <c>true</c>（元素转换失败时填充默认值）。</returns>
        internal static bool TryConvertToArray(Array valueArray, Type elementType, out object conversionValue)
        {
            conversionValue = null;
            if (valueArray == null || elementType == null) return false;

            Array instanceValue = Array.CreateInstance(elementType, valueArray.Length);

            for (int i = 0; i < valueArray.Length; i++)
            {
                if (!TryConvertParameter(valueArray.GetValue(i), elementType, out object cValue))
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
