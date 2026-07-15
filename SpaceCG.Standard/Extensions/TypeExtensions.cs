using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;

namespace SpaceCG.Extensions
{
    /// <summary>
    /// Type Extensions
    /// </summary>
    public static partial class TypeExtensions
    {
        /// <summary>
        /// 缓存类型接口，避免每次调用时反射带来的性能开销。
        /// </summary>
        internal static readonly ConcurrentDictionary<Type, Type[]> CacheTypeInterfaces = new ConcurrentDictionary<Type, Type[]>();
        /// <summary>
        /// 缓存类型转换器，避免每次调用时反射带来的性能开销。
        /// </summary>
        internal static readonly ConcurrentDictionary<Type, TypeConverter> TypeConverterCache = new ConcurrentDictionary<Type, TypeConverter>();

        #region Type & Value Signature
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
        /// <summary>
        /// 获取参数值的自定义签名。SVT:String and Value Type
        /// <para>用于输入文本参数 objecct[] 的参数值签名。</para>
        /// </summary>
        /// <param name="paramValue"></param>
        /// <returns></returns>
        internal static string GetValueSignature(object paramValue)
        {
            if (paramValue == null) return "";
            var valueType = paramValue.GetType();

            if (valueType.IsEnum || valueType.IsValueType || valueType == typeof(string)) return "SVT";
            if (valueType.IsArray)
            {
                var array = (Array)paramValue;
                if (array.Length == 0) return "[]";
                return $"[{GetValueSignature(array.GetValue(0))}]";
            }

            return "REF";
        }
        /// <summary>
        /// 目标方法的参数属性签名。
        /// </summary>
        /// <param name="parameters"></param>
        /// <returns></returns>
        public static string GetParameterSignature(this IEnumerable<ParameterInfo> parameters)
        {
            if (parameters == null) return string.Empty;
            return string.Join(",", parameters.Select(p => GetTypeSignature(p.ParameterType)));
        }
        /// <summary>
        /// 目标方法的参数值的签名。
        /// </summary>
        /// <param name="parameters"></param>
        /// <returns></returns>
        public static string GetParameterSignature(this IEnumerable<object> parameters)
        {
            if (parameters == null) return string.Empty;
            return string.Join(",", parameters.Select(t => GetValueSignature(t)));
        }
        #endregion

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
        /// <param name="value">待转换的值（顶层来自 <see cref="StringExtensions.ParseParameters"/> 的输出；叶子一定是 <see cref="string"/>）。</param>
        /// <param name="targetType">目标方法参数类型。</param>
        /// <param name="convertedParameter">转换后的强类型值。</param>
        /// <returns>转换成功返回 <c>true</c>；否则返回 <c>false</c>。</returns>
        /// <seealso cref="ConvertToArray"/>
        /// <seealso cref="StringExtensions.TryConvertTo(string, Type, out object)"/>
        public static bool TryConvertParameter(object value, Type targetType, out object convertedParameter)
        {
            convertedParameter = null;
            if (targetType == null) return false;
            if (value == null || targetType == typeof(void)) return true;

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
                if (value is string stringValue && stringValue.TryConvertTo(targetType, out var targetValue)) convertedParameter = targetValue;
                else convertedParameter = value;
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
                if (genericTypeDefinition == typeof(IEnumerable<>) || genericTypeDefinition.IsIEnumerable())
                {
                    convertedParameter = ConvertToArray((Array)value, targetType.GetGenericArguments()[0]);
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 将输入参数值按目标方法签名转换为强类型数组。
        /// <para>每个输入值通过 <see cref="TryConvertParameter(object, Type, out object)"/> 转换为对应参数类型。</para>
        /// <para>自动识别扩展方法：扩展方法的 <c>this</c> 参数不参与转换，直接以 <c>null</c> 占位（调用方负责在 Invoke 前替换）。</para>
        /// </summary>
        /// <param name="parameters">输入参数值列表（来自 <see cref="StringExtensions.ParseParameters"/>）。</param>
        /// <param name="methodInfo">目标方法信息。</param>
        /// <param name="convertedParameters">转换后的强类型参数数组，可直接用于 <c>MethodInfo.Invoke</c>。
        /// 扩展方法的首个元素为 <c>null</c>，调用方需替换为实例对象。</param>
        /// <returns>全部参数转换成功返回 <c>true</c>；参数数量不匹配或任一转换失败返回 <c>false</c>。</returns>
        public static bool TryConvertParameters(object[] parameters, MethodInfo methodInfo, out object[] convertedParameters)
        {
            convertedParameters = null;
            if (methodInfo == null || parameters == null) return false;

            var methodParams = methodInfo.GetParameters();
            var isExtension = methodInfo.IsDefined(typeof(ExtensionAttribute), false);
            var offsetParams = isExtension ? 1 : 0;

            if (methodParams.Length - offsetParams != parameters.Length) return false;

            convertedParameters = new object[methodParams.Length];
            // 扩展方法首个参数占位，调用方负责替换为实例
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
        /// 将源数组转换为指定元素类型的强类型数组，对每个元素递归调用 <see cref="TryConvertParameter(object, Type, out object)"/>。
        /// <para>由 <see cref="TryConvertParameter(object, Type, out object)"/> 在检测到目标类型为数组或 IEnumerable&lt;T&gt; 且元素类型不匹配时调用。</para>
        /// <para>支持多层嵌套：当元素类型本身也是数组或集合时，递归再次调入 <see cref="TryConvertParameter(object, Type, out object)"/>。</para>
        /// <para>容错策略：单个元素转换失败时不中断，以元素类型的默认值填充，继续处理后续元素。</para>
        /// </summary>
        /// <param name="valueArray">
        /// 源数组。来自 <see cref="StringExtensions.ParseParameters"/> 的输出：
        /// 叶子是 <see cref="string"/>，嵌套子数组为 <c>string[]</c>（全字符串）或 <c>object[]</c>（含更深层嵌套）。
        /// </param>
        /// <param name="elementType">目标数组的元素类型。</param>
        /// <returns>强类型数组（如 <c>int[]</c>、<c>bool[][]</c>）。</returns>
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
