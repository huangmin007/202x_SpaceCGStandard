using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace SpaceCG.Extensions
{
    /// <summary>
    /// 类型反射与参数转换扩展方法。
    /// <para>提供类型签名生成、接口检测、以及 <see cref="StringExtensions.ParseParameters"/> 强类型方法参数的完整转换流水线。</para>
    /// </summary>
    public static partial class TypeExtensions
    {
        /// <summary>
        /// 缓存类型实现的接口数组，避免每次调用 <see cref="Type.GetInterfaces"/> 的反射开销。
        /// </summary>
        internal static readonly ConcurrentDictionary<Type, bool> EnumerableOfTCache = new ConcurrentDictionary<Type, bool>();
        /// <summary>
        /// 缓存类型的 <see cref="TypeConverter"/> 实例，避免每次调用 <see cref="TypeDescriptor.GetConverter(Type)"/> 的反射开销。
        /// </summary>
        internal static readonly ConcurrentDictionary<Type, TypeConverter> TypeConverterCache = new ConcurrentDictionary<Type, TypeConverter>();

        #region Type & Value Signature
        /// <summary>
        /// 获取参数类型的自定义签名，用于 RPC 方法重载匹配。
        /// <para>签名规则：</para>
        /// <list type="bullet">
        /// <item><c>"SVT"</c> — 所有值类型（含枚举）以及 <see cref="string"/>。</item>
        /// <item><c>"[SVT]"</c> — 元素为 SVT 的数组。</item>
        /// <item><c>"[REF]"</c> — 元素为引用类型的数组。</item>
        /// <item><c>"[&lt;REF...&gt;]"</c> — 多泛型参数的集合类型（极少出现）。</item>
        /// <item><c>"REF"</c> — 其他引用类型。</item>
        /// <item><c>""</c> — null 类型。</item>
        /// </list>
        /// </summary>
        /// <param name="paramType">参数类型，来自 <see cref="ParameterInfo.ParameterType"/>。</param>
        /// <returns>类型签名字符串。</returns>
        internal static string GetTypeSignature(Type paramType)
        {
            if (paramType == null) return "";
            if (paramType.IsValueType || paramType == typeof(string)) return "SVT";

            if (paramType.IsArray) return $"[{GetTypeSignature(paramType.GetElementType())}]";
            if (paramType.IsGenericType)
            {
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
        /// <summary>
        /// 获取参数值的自定义签名，用于与 <see cref="GetTypeSignature(Type)"/> 比对以匹配方法重载。
        /// <para>签名规则与 <see cref="GetTypeSignature(Type)"/> 一致，但基于运行时值的实际类型。</para>
        /// <para>数组取首个元素的签名作为元素类型签名；空数组返回 <c>"[]"</c>。</para>
        /// </summary>
        /// <param name="paramValue">参数值，来自 <see cref="StringExtensions.ParseParameters"/> 的输出。</param>
        /// <returns>值签名字符串。</returns>
        internal static string GetValueSignature(object paramValue)
        {
            if (paramValue == null) return "";
            var valueType = paramValue.GetType();

            if (valueType.IsValueType || valueType == typeof(string)) return "SVT";
            if (valueType.IsArray)
            {
                var array = (Array)paramValue;
                if (array.Length == 0) return "[]";

                var firstElement = array.GetValue(0);
                if (firstElement == null) return "[REF]";

                var elementType = firstElement.GetType();
                if (valueType.IsValueType || elementType == typeof(string)) return "[SVT]";
                if (elementType.IsArray) return $"[{GetValueSignature(firstElement)}]";

                return $"[REF]";
            }

            return "REF";
        }
        /// <summary>
        /// 获取目标方法的参数类型签名列表，用于 RPC 方法重载匹配。
        /// </summary>
        /// <param name="parameters">方法的参数信息集合。</param>
        /// <returns>逗号分隔的签名列表，如 <c>"SVT,[SVT]"</c>。</returns>
        public static string GetParameterSignature(this IEnumerable<ParameterInfo> parameters)
        {
            if (parameters == null) return string.Empty;
            return string.Join(",", parameters.Select(p => GetTypeSignature(p.ParameterType)));
        }
        /// <summary>
        /// 获取传入参数值的签名列表，用于与 <see cref="GetParameterSignature(IEnumerable{ParameterInfo})"/> 比对以匹配方法重载。
        /// </summary>
        /// <param name="parameters">传入的参数值集合。</param>
        /// <returns>逗号分隔的签名列表。</returns>
        public static string GetParameterSignature(this IEnumerable<object> parameters)
        {
            if (parameters == null) return string.Empty;
            return string.Join(",", parameters.Select(t => GetValueSignature(t)));
        }
        #endregion

        /// <summary>
        /// 判断类型是否实现了 <see cref="IEnumerable{T}"/> 接口。
        /// <para>使用缓存避免重复反射，适用于方法签名匹配等中频调用路径。</para>
        /// </summary>
        /// <param name="type">待检查的类型。</param>
        /// <returns>实现了 IEnumerable&lt;T&gt; 返回 <c>true</c>；null 返回 <c>false</c>。</returns>
        public static bool IsEnumerableOfT(this Type type)
        {
            if (type == null) return false;

            return EnumerableOfTCache.GetOrAdd(type, t =>
            {
                foreach (var i in t.GetInterfaces())
                {
                    if (i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                        return true;
                }
                return false;
            });
        }

        /// <summary>
        /// 将 <see cref="StringExtensions.ParseParameters"/> 产出的参数值转换为目标方法参数的强类型值，是本转换流水线的核心调度器。
        /// <para>处理三种分支：</para>
        /// <list type="number">
        /// <item><b>类型兼容</b>：值类型与目标类型一致或可赋值 → 直接返回。</item>
        /// <item><b>标量转换</b>：目标类型为值类型或 string → 委托给 <see cref="StringExtensions.TryConvertTo(string, Type, out object)"/> 将叶子 string 转为强类型值。</item>
        /// <item><b>数组/集合转换</b>：目标类型为数组或 IEnumerable&lt;T&gt; → 委托给 <see cref="ConvertToArray"/> 逐元素递归转换。</item>
        /// </list>
        /// <para>典型调用链：<c>TryConvertTo(element, targetType, out result)</c> → 对叶子 string 调 <c>TryConvertTo</c>，
        /// 对嵌套数组调 <c>TryConvertToArray</c> → 内部再次调 <c>TryConvertTo</c> 处理每个子元素。</para>
        /// </summary>
        /// <param name="value">待转换的值（顶层来自 <see cref="StringExtensions.ParseParameters"/> 的输出；叶子一定是 <see cref="string"/>、string[]、object[]"/>）。</param>
        /// <param name="targetType">目标方法参数类型。</param>
        /// <param name="convertedParameter">转换后的强类型值。</param>
        /// <returns>转换成功返回 <c>true</c>；否则返回 <c>false</c>。</returns>
        /// <seealso cref="ConvertToArray"/>
        /// <seealso cref="StringExtensions.TryConvertTo(string, Type, out object)"/>
        public static bool TryConvertParameter(object value, Type targetType, out object convertedParameter)
        {
            convertedParameter = null;
            if (targetType == null) return false;
            if (value == null || targetType == typeof(void)) return false;

            Type valueType = value.GetType();
            // 类型兼容检查（含接口实现关系）
            if (!targetType.IsArray && (valueType == targetType || targetType.IsAssignableFrom(valueType)))
            {
                convertedParameter = value;
                return true;
            }
            // 标量：值类型 & 字符串 转换
            if ((targetType.IsValueType || targetType == typeof(string)) && (valueType.IsValueType || valueType == typeof(string)))
            {
                if (value is string stringValue)
                {
                    if (stringValue.TryConvertTo(targetType, out var targetValue))
                    {
                        convertedParameter = targetValue;
                        return true;
                    }
                    return false;
                }
                convertedParameter = value;
                return true;
            }

            // 数组：元素类型相同 → 直接赋值
            if (targetType.IsArray && valueType.IsArray && targetType.GetElementType() == valueType.GetElementType())
            {
                convertedParameter = value;
                return true;
            }
            // 数组：元素类型不同 → 递归转换每个元素
            if (targetType.IsArray && valueType.IsArray && targetType.GetElementType() != valueType.GetElementType())
            {
                convertedParameter = ConvertToArray((Array)value, targetType.GetElementType());
                return true;
            }

            // 目标类型是：System.Collections.IEnumerable<T> 或实现、继承 IEnumerable<T> 接口的子类
            if (targetType.IsGenericType && valueType.IsArray && targetType.GetGenericArguments()?.Length == 1)
            {
                var genericTypeDefinition = targetType.GetGenericTypeDefinition();
                if (genericTypeDefinition == typeof(IEnumerable<>) || genericTypeDefinition.IsEnumerableOfT())
                {
                    convertedParameter = ConvertToArray((Array)value, targetType.GetGenericArguments()[0]);
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 将 <see cref="StringExtensions.ParseParameters"/> 产出的参数值数组按目标方法签名转换为强类型参数数组。
        /// <para>每个输入值通过 <see cref="TryConvertParameter(object, Type, out object)"/> 独立转换，
        /// 任一转换失败即整体返回 <c>false</c>。</para>
        /// <para>自动识别扩展方法：扩展方法的 <c>this</c> 参数不参与转换，以 <c>null</c> 占位
        /// （调用方负责在 <see cref="MethodBase.Invoke(object, object[])"/> 前替换为实例对象）。</para>
        /// </summary>
        /// <param name="parameters">输入参数值数组（来自 <see cref="StringExtensions.ParseParameters"/>）。</param>
        /// <param name="methodInfo">目标方法信息。</param>
        /// <param name="convertedParameters">
        /// 转换后的强类型参数数组，长度等于方法参数总数（含扩展方法的 this 占位），可直接用于 <c>MethodInfo.Invoke</c>。
        /// 转换失败时为 <c>null</c>。
        /// </param>
        /// <returns>全部参数转换成功返回 <c>true</c>；参数为 null、方法为 null、参数数量不匹配或任一转换失败返回 <c>false</c>。</returns>
        public static bool TryConvertParameters(object[] parameters, MethodInfo methodInfo, out object[] convertedParameters)
        {
            convertedParameters = null;
            if (methodInfo == null || parameters == null) return false;

            var methodParams = methodInfo.GetParameters();
            var isExtension = methodInfo.IsDefined(typeof(ExtensionAttribute), false);
            var offsetParams = isExtension ? 1 : 0;

            if (methodParams.Length - offsetParams != parameters.Length) return false;

            convertedParameters = new object[methodParams.Length];
            // 扩展方法：首个参数（this）以 null 占位，调用方负责在 Invoke 前替换为实例
            if (isExtension)
                convertedParameters[0] = null;

            for (int i = 0; i < parameters.Length; i++)
            {
                if (!TryConvertParameter(parameters[i], methodParams[i + offsetParams].ParameterType, out var converted))
                    return false;

                convertedParameters[i + offsetParams] = converted;
            }

            return true;
        }

        /// <summary>
        /// 将源数组转换为指定元素类型的强类型数组。
        /// <para>对每个元素递归调用 <see cref="TryConvertParameter(object, Type, out object)"/> 进行类型转换，
        /// 因此支持多层嵌套（如 <c>string[][] → int[][]</c>）。</para>
        /// <para><b>容错策略</b>：单个元素转换失败时不中断整体流程，以元素类型的默认值填充
        /// （值类型为 <c>default</c>，引用类型为 <c>null</c>），继续处理后续元素。</para>
        /// </summary>
        /// <param name="valueArray">
        /// 源数组。来自 <see cref="StringExtensions.ParseParameters"/> 的输出：
        /// 叶子是 <see cref="string"/>，嵌套子数组为 <c>string[]</c>（全字符串）或 <c>object[]</c>（含更深层嵌套）。
        /// </param>
        /// <param name="elementType">目标数组的元素类型。</param>
        /// <returns>强类型数组（如 <c>int[]</c>、<c>bool[][]</c>）；任一参数为 null 返回 <c>null</c>。</returns>
        /// <seealso cref="TryConvertParameter(object, Type, out object)"/>
        /// <seealso cref="StringExtensions.ParseParameters"/>
        public static Array ConvertToArray(Array valueArray, Type elementType)
        {
            if (valueArray == null || elementType == null) return null;

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

            return instanceValue;
        }

    }
}
