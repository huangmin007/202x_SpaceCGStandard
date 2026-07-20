using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Xml.Linq;
using Trace = SpaceCG.Diagnostics.Trace;

namespace SpaceCG.Extensions
{
    /// <summary>
    /// 提供对象实例的反射扩展方法。
    /// <para>支持动态获取/设置字段、属性，以及批量赋值和事件清理，方法反射调用等。</para>
    /// </summary>
    /// <remarks>
    /// <para><b>线程安全</b>：本类方法无状态；<see cref="MethodInfoCache"/> 为 <see cref="ConcurrentDictionary{TKey, TValue}"/>，可安全并发调用。</para>
    /// </remarks>
    public static partial class InstanceExtensions
    {
        /// <summary>
        /// 方法信息缓存。
        /// </summary>
        internal static readonly ConcurrentDictionary<string, MethodInfo> MethodInfoCache = new ConcurrentDictionary<string, MethodInfo>();


        #region Try Get/Set Instance Field Value
        /// <summary>
        /// 尝试动态获取实例对象的字段值。
        /// </summary>
        /// <param name="instance">目标实例对象。</param>
        /// <param name="fieldName">要获取的字段名称。</param>
        /// <param name="value">当方法返回 <c>true</c> 时，包含获取到的字段值；否则为 <c>null</c>。</param>
        /// <returns>如果成功找到字段并获取其值，则为 <c>true</c>；否则为 <c>false</c>。</returns>
        /// <remarks>支持访问 <c>public</c>、<c>private</c> 和 <c>protected</c> 实例字段。</remarks>
        public static bool TryGetFieldValue(object instance, string fieldName, out object value)
        {
            value = null;
            if (instance == null || string.IsNullOrWhiteSpace(fieldName)) return false;

            Type type = instance.GetType();
            FieldInfo fieldInfo = type.GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
            if (fieldInfo == null)
            {
                Trace.TraceWarning($"在类型 [{type.Name}] 的实例中未找到字段：{fieldName}");
                return false;
            }

            try
            {
                value = fieldInfo.GetValue(instance);
                return true;
            }
            catch (Exception ex)
            {
                Trace.TraceWarning($"在类型 [{type.Name}] 的实例中，获取字段 [{fieldName}] 的值时发生异常：({ex.GetType().Name}) {ex.Message}");
            }
            return false;
        }
        /// <summary>
        /// 尝试动态设置实例对象的字段值。
        /// </summary>
        /// <param name="instance">目标实例对象。</param>
        /// <param name="fieldName">要设置的字段名称。</param>
        /// <param name="newValue">要赋予的新值。</param>
        /// <returns>如果成功找到字段并设置其值，则为 <c>true</c>；否则为 <c>false</c>。</returns>
        /// <remarks>
        /// <para>支持访问 <c>public</c>、<c>private</c> 和 <c>protected</c> 实例字段。</para>
        /// <para>在赋值前，会尝试通过 <see cref="TypeExtensions.TryConvertParameter"/> 进行类型兼容转换。</para>
        /// </remarks>
        public static bool TrySetFieldValue(object instance, string fieldName, object newValue)
        {
            if (instance == null || string.IsNullOrWhiteSpace(fieldName)) return false;

            Type type = instance.GetType();
            FieldInfo fieldInfo = type.GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
            if (fieldInfo == null)
            {
                Trace.TraceWarning($"在类型 [{type.Name}] 的实例中未找到字段：{fieldName}");
                return false;
            }

            if (TypeExtensions.TryConvertParameter(newValue, fieldInfo.FieldType, out object convertValue))
            {
                try
                {
                    fieldInfo.SetValue(instance, convertValue);
                    return true;
                }
                catch (Exception ex)
                {
                    Trace.TraceError($"在类型 [{type.Name}] 的实例中，设置字段 [{fieldName}] 的值 {newValue} 时发生异常：({ex.GetType().Name}) {ex.Message}");
                }
            }

            return false;
        }
        #endregion


        #region Try Get/Set Instance Property Value
        /// <summary>
        /// 动态获取实例对象的属性值。
        /// </summary>
        /// <param name="instance">目标实例对象。</param>
        /// <param name="propertyName">要获取的属性名称。</param>
        /// <param name="value">当方法返回 <c>true</c> 时，包含获取到的属性值；否则为 <c>null</c>。</param>
        /// <returns>如果成功找到可读属性并获取其值，则为 <c>true</c>；否则为 <c>false</c>。</returns>
        public static bool TryGetPropertyValue(object instance, string propertyName, out object value)
        {
            value = null;
            if (instance == null || string.IsNullOrWhiteSpace(propertyName)) return false;

            Type type = instance.GetType();
            PropertyInfo property = type.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
            if (property == null || !property.CanRead)
            {
                Trace.TraceWarning($"在类型 [{type.Name}] 的实例中未找到可读属性：{propertyName}");
                return false;
            }

            try
            {
                value = property.GetValue(instance);
                return true;
            }
            catch (Exception ex)
            {
                Trace.TraceWarning($"在类型 [{type.Name}] 的实例中，获取属性 [{propertyName}] 的值时发生异常：({ex.GetType().Name}){ex.Message}");
            }

            return false;
        }

        /// <summary>
        /// 动态设置实例对象的属性值。
        /// </summary>
        /// <param name="instance">目标实例对象。</param>
        /// <param name="propertyName">要设置的属性名称。</param>
        /// <param name="newValue">要赋予的新值。</param>
        /// <returns>如果成功找到可写属性并设置其值，则为 <c>true</c>；否则为 <c>false</c>。</returns>
        /// <remarks>在赋值前，会尝试通过 <see cref="TypeExtensions.TryConvertParameter"/> 进行类型兼容转换。</remarks>
        public static bool TrySetPropertyValue(object instance, string propertyName, object newValue)
        {
            if (instance == null || string.IsNullOrWhiteSpace(propertyName)) return false;

            Type type = instance.GetType();
            PropertyInfo property = type.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
            if (property == null || !property.CanWrite /*|| !property.CanRead*/)
            {
                Trace.TraceWarning($"在类型 [{type.Name}] 的实例中未找到可写属性：{propertyName}");
                return false;
            }

            if (TypeExtensions.TryConvertParameter(newValue, property.PropertyType, out object convertValue))
            {
                try
                {
                    property.SetValue(instance, convertValue, null);
                    return true;
                }
                catch (Exception ex)
                {
                    Trace.TraceWarning($"在类型 [{type.Name}] 的实例中，设置属性 [{propertyName}] 的值时发生异常：({ex.GetType().Name}){ex.Message}");
                }
            }

            return false;
        }
        #endregion


        #region Try Set Instance Properties Values
        /// <summary>
        /// 批量设置实例对象的属性值。
        /// <para>单个属性设置失败仅记录日志并被忽略，不会抛出。</para>
        /// </summary>
        /// <param name="instance">目标实例对象。</param>
        /// <param name="attributes">包含属性名与对应值的字典。</param>
        /// <exception cref="ArgumentNullException"><paramref name="instance"/> 或 <paramref name="attributes"/> 为 <c>null</c>。</exception>
        public static void SetPropertyValues(object instance, IReadOnlyDictionary<string, object> attributes)
        {
            if (instance == null) throw new ArgumentNullException(nameof(instance));
            if (attributes == null) throw new ArgumentNullException(nameof(attributes));

            if (attributes.Count == 0) return;
            foreach (KeyValuePair<string, object> kv in attributes)
            {
                TrySetPropertyValue(instance, kv.Key, kv.Value);
            }
        }
        /// <summary>
        /// 从 XML 属性集合中批量设置实例对象的属性值。
        /// <para>单个属性设置失败仅记录日志并被忽略，不会抛出。</para>
        /// </summary>
        /// <param name="instance">目标实例对象。</param>
        /// <param name="attributes">XML 属性集合。</param>
        /// <exception cref="ArgumentNullException"><paramref name="instance"/> 或 <paramref name="attributes"/> 为 <c>null</c>。</exception>
        public static void SetPropertyValues(object instance, IEnumerable<XAttribute> attributes)
        {
            if (instance == null) throw new ArgumentNullException(nameof(instance));
            if (attributes == null) throw new ArgumentNullException(nameof(attributes));

            if (!attributes.Any()) return;
            foreach (XAttribute attribute in attributes)
            {
                TrySetPropertyValue(instance, attribute.Name.LocalName, attribute.Value);
            }
        }
        /// <summary>
        /// 从 XML 元素中批量设置其<em>对应字段对象</em>的属性值。
        /// <para>单个属性设置失败仅记录日志并被忽略，不会抛出。</para>
        /// </summary>
        /// <param name="instanceParent">包含目标字段的父级实例对象。</param>
        /// <param name="element">XML 元素，其名称将用于在父对象中查找同名字段。</param>
        /// <exception cref="ArgumentNullException"><paramref name="instanceParent"/> 或 <paramref name="element"/> 为 <c>null</c>。</exception>
        /// <remarks>
        /// <b>注意</b>：此方法会在 <paramref name="instanceParent"/> 中查找与 <paramref name="element"/> 名称相同的<b>字段 (Field)</b>，
        /// 而非属性 (Property)。如果找到该字段且不为 null，则将 XML 元素的属性批量赋值给该字段对象。
        /// </remarks>
        public static void SetPropertyValues(object instanceParent, XElement element)
        {
            if (element == null) throw new ArgumentNullException(nameof(element));
            if (instanceParent == null) throw new ArgumentNullException(nameof(instanceParent));

            if (TryGetFieldValue(instanceParent, element.Name.LocalName, out object instanceObj) && instanceObj != null)
            {
                SetPropertyValues(instanceObj, element.Attributes());
            }
        }
        #endregion


        #region Remove Instance Events
        /// <summary>
        /// 动态移除实例对象上指定的事件的所有订阅者。
        /// </summary>
        /// <param name="instance">目标实例对象。</param>
        /// <param name="eventName">要移除的事件名称。</param>
        /// <returns>如果成功找到事件并清空其订阅，则为 <c>true</c>；否则为 <c>false</c>。</returns>
        /// <remarks>支持移除 <c>public</c>、<c>private</c> 和 <c>protected</c> 实例事件。</remarks>
        public static bool RemoveEvent(object instance, string eventName)
        {
            if (instance == null || string.IsNullOrWhiteSpace(eventName)) return false;

            BindingFlags fieldBindingFlags = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance;
            BindingFlags eventBindingFlags = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance;

            // 沿继承链向上查找事件对应的委托字段
            FieldInfo fieldInfo = null;
            Type currentType = instance.GetType();
            while (currentType != null && fieldInfo == null)
            {
                fieldInfo = currentType.GetField(eventName, fieldBindingFlags);
                currentType = currentType.BaseType;
            }

            if (fieldInfo == null) return false;

            try
            {
                var type = instance.GetType();
                var value = fieldInfo.GetValue(instance);
                if (value == null || !(value is Delegate multicast)) return false;

                // 从声明该字段的类型中查找 EventInfo，并使用相同的 bindingAttr
                // 这确保了即使是基类的 private/protected 事件也能被正确找到并移除
                EventInfo eventInfo = fieldInfo.DeclaringType.GetEvent(eventName, eventBindingFlags);
                if (eventInfo == null) return false;

                // 遍历并移除所有订阅者
                foreach (Delegate handler in multicast.GetInvocationList())
                {
                    // 检查该方法是否是编译器生成的匿名方法/Lambda
                    // 匿名方法的名称通常以 '<' 开头，或者带有 CompilerGeneratedAttribute
                    // bool isAnonymous = handler.Method.IsDefined(typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute), true) || handler.Method.Name.StartsWith("<");

                    eventInfo.RemoveEventHandler(instance, handler);
                    Trace.TraceInformation($"已从 [{type.Name}] 移除事件订阅：{eventName} -> {handler.Method.Name}");
                }
                return true;
            }
            catch (Exception ex)
            {
                Trace.TraceError($"移除事件 [{eventName}] 时发生异常：({ex.GetType().Name}) {ex.Message}");
            }
            return false;
        }
        /// <summary>
        /// 动态移除实例对象上定义的所有事件的所有订阅者。
        /// </summary>
        /// <param name="instance">目标实例对象。</param>
        /// <exception cref="ArgumentNullException"><paramref name="instance"/> 为 <c>null</c>。</exception>
        public static void RemoveEvents(object instance)
        {
            if (instance == null)
                throw new ArgumentNullException(nameof(instance));

            EventInfo[] events = instance.GetType().GetEvents(/*BindingFlags.Public | BindingFlags.Instance*/);
            foreach (EventInfo info in events)
            {
                RemoveEvent(instance, info.Name);
            }

#if false    // 如需同时清理基类的公共事件，可取消下方注释
            Type baseType = instanceObj.GetType().BaseType;
            if (baseType == null || baseType == typeof(object)) return;

            EventInfo[] baseEvents = baseType.GetEvents(/*BindingFlags.Public | BindingFlags.Instance*/);
            foreach (EventInfo info in baseEvents)
            {
                RemoveInstanceEvent(instanceObj, info.Name);
            }
#endif
        }
        #endregion


        #region Invoke Instance Method & Instance Extension Method
        /// <summary>
        /// 尝试通过指定的 <see cref="MethodInfo"/> 动态调用 <b>实例方法</b> 或 <b>实例扩展方法</b>。
        /// </summary>
        /// <param name="instance">目标实例对象。<b>不可为 null</b>。</param>
        /// <param name="methodInfo">要调用的方法元数据。必须是 实例方法 或 实例扩展方法，不可为 <c>null</c>。</param>
        /// <param name="parameters">传递给方法的业务参数数组（不包含扩展方法的 this 参数）。</param>
        /// <param name="returnResult">当方法返回 <c>true</c> 时，包含方法的返回值，方法无返回值时为 <c>null</c>。</param>
        /// <returns>如果成功执行方法，则为 <c>true</c>；否则为 <c>false</c>。</returns>
        /// <remarks>
        /// <b>严格约束</b>：此方法不能调用纯静态方法。若 <paramref name="methodInfo"/> 为纯静态方法，将直接返回 <c>false</c>。
        /// </remarks>
        public static bool TryInvokeMethod(object instance, MethodInfo methodInfo, object[] parameters, out object returnResult)
        {
            returnResult = null;
            if (instance == null || methodInfo == null) return false;

            var isExtensionMethod = methodInfo.IsDefined(typeof(ExtensionAttribute), false);
            // 不支持纯静态方法
            if (methodInfo.IsStatic && !isExtensionMethod) return false;

            if (!TypeExtensions.TryConvertParameters(parameters, methodInfo, out var convertedParameters))
            {
                Trace.TraceWarning($"实例对象 ({instance}) 的方法 ({methodInfo.Name}) 参数类型转换不匹配");
                return false;
            }

            if (isExtensionMethod && convertedParameters?.Length > 0)
            {
                convertedParameters[0] = instance;
            }

            try
            {
                returnResult = methodInfo.Invoke(instance, convertedParameters);
                return true;
            }
            catch (Exception ex)
            {
                Trace.TraceWarning($"调用实例对象 ({instance}) 的方法 ({methodInfo.Name}) 时发生异常：({ex.GetType().Name}) {ex.Message}");
            }

            return false;
        }
        /// <inheritdoc cref="TryInvokeMethod(object, MethodInfo, object[], out object)"/> 
        public static bool TryInvokeMethod(object instance, MethodInfo methodInfo, out object returnResult) => TryInvokeMethod(instance, methodInfo, null, out returnResult);

        /// <summary>
        /// 尝试通过方法名和对象参数数组，动态查找并调用 <b>实例方法</b> 或 <b>实例扩展方法</b>。
        /// </summary>
        /// <param name="instance">目标实例对象。<b>不可为 null</b>。</param>
        /// <param name="methodName">要调用的方法的名称。必须是 实例方法 或 实例扩展方法 的名称，不可为 <c>null</c>。</param>
        /// <param name="parameters">传递给方法的业务参数数组（不包含扩展方法的 this 参数）。</param>
        /// <param name="returnResult">当方法返回 <c>true</c> 时，包含方法的返回值，方法无返回值时为 <c>null</c>。</param>
        /// <returns>如果成功执行方法，则为 <c>true</c>；否则为 <c>false</c>。</returns>
        public static bool TryInvokeMethod(object instance, string methodName, object[] parameters, out object returnResult)
        {
            returnResult = null;
            if (instance == null || string.IsNullOrWhiteSpace(methodName)) return false;

            var instanceType = instance.GetType();
            var paramSignature = parameters.GetParameterSignature();
            var instanceMethodKey = $"{instanceType.FullName}.{methodName}({paramSignature})";

            var methodInfo = MethodInfoCache.GetOrAdd(instanceMethodKey, (methodKey) =>
            {
                #region 查找实例方法
                var methods = instanceType.GetMethods();
                foreach (var method in methods)
                {
                    if (method.Name != methodName) continue;

                    var methodParameters = method.GetParameters();
                    if (methodParameters.Length != parameters.Length) continue;

                    var tempSignature = methodParameters.GetParameterSignature();
                    var tempMethodKey = $"{instanceType.FullName}.{methodName}({tempSignature})";
                    if (tempMethodKey != methodKey) continue;

                    return method;
                }
                #endregion

                #region 查找扩展方法
                var extensionType = typeof(ExtensionAttribute);
                var extensionBindingFlags = BindingFlags.Static | BindingFlags.Public | BindingFlags.DeclaredOnly;
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (assembly.IsDynamic || assembly.GlobalAssemblyCache) continue;

                    Type[] exportedTypes;
                    try
                    {
                        exportedTypes = assembly.GetExportedTypes();
                    }
                    catch (Exception ex)
                    {
                        Trace.TraceWarning($"程序集 [{assembly.FullName}] 获取导出类型失败: {ex.Message}");
                        continue;
                    }

                    foreach (var type in exportedTypes)
                    {
                        // 严格的静态类判断：必须是 abstract 且 sealed，且非泛型，且非嵌套；跳过非静态类、泛型类、嵌套类
                        if (!type.IsAbstract || !type.IsSealed || type.IsGenericType || type.IsNested) continue;
                        foreach (var method in type.GetMethods(extensionBindingFlags))
                        {
                            if (method.Name != methodName) continue;
                            if (!method.IsDefined(extensionType, false)) continue;

                            var methodParameters = method.GetParameters();
                            if (methodParameters == null || methodParameters.Length == 0) continue;
                            if (methodParameters.Length - 1 != parameters.Length) continue;
                            if (methodParameters.Any(p => p.ParameterType.IsByRef)) continue;  // ref out 参数不支持

                            // 使用 IsAssignableFrom 支持基类和接口类型的扩展方法
                            if (methodParameters[0].ParameterType.IsAssignableFrom(instanceType))
                            {
                                var tempSignature = methodParameters.Skip(1).GetParameterSignature();
                                var tempMethodKey = $"{instanceType.FullName}.{method.Name}({tempSignature})";
                                if (tempMethodKey != methodKey) continue;

                                return method;
                            }
                        }
                    }
                }
                #endregion

                return null;
            });

            if (methodInfo == null)
            {
                Trace.TraceWarning($"未找到实例对象 ({instance}) 的方法 ({methodName})");
                return false;
            }

            return TryInvokeMethod(instance, methodInfo, parameters, out returnResult);
        }
        /// <inheritdoc cref="TryInvokeMethod(object, string, object[], out object)"/> 
        public static bool TryInvokeMethod(object instance, string methodName, out object returnResult) => TryInvokeMethod(instance, methodName, Array.Empty<object>(), out returnResult);

        /// <summary>
        /// 尝试通过方法名和字符串形式的参数，动态查找并调用 <b>实例方法</b> 或 <b>实例扩展方法</b>。
        /// </summary>
        /// <param name="instance">目标实例对象。<b>不可为 null</b>。</param>
        /// <param name="methodName">要调用的方法的名称。必须是 实例方法 或 实例扩展方法 的名称，不可为 <c>null</c>。</param>
        /// <param name="parameters">传递给方法的业务参数数组的字符串形式（不包含扩展方法的 this 参数）。
        /// <list type="bullet">
        /// <item>多个参数使用 ',' 隔开</item>
        /// <item>基本元素只支持，String &amp; Value Type</item>
        /// <item>支持集合类型，使用 '[]' 包裹，如：[1,2,3] </item>
        /// <item>示例："1.6,True,#FFFF0000"、"[1,2,3],[4,5,6],2.5,True"、"[[1,2,3],[4,5,6]]]......" </item>
        /// </list>
        /// </param>
        /// <param name="returnResult">当方法返回 <c>true</c> 时，包含方法的返回值，方法无返回值时为 <c>null</c>。</param>
        /// <returns>如果成功执行方法，则为 <c>true</c>；否则为 <c>false</c>。</returns>
        public static bool TryInvokeMethod(object instance, string methodName, string parameters, out object returnResult)
        {
            returnResult = null;
            if (instance == null || string.IsNullOrWhiteSpace(methodName)) return false;

            return TryInvokeMethod(instance, methodName, parameters.ParseParameters(), out returnResult);
        }

        /// <summary>
        /// 从方法反射调用结果中提取实际返回值。
        /// <para>如果 <paramref name="returnResult"/> 是 <see cref="Task"/> 或 <see cref="Task{TResult}"/>，
        /// 则异步等待任务完成并提取最终结果；否则直接返回原值。</para>
        /// </summary>
        /// <param name="returnResult">反射调用 <see cref="MethodBase.Invoke(object, object[])"/> 的原始返回值。</param>
        /// <returns>
        /// <list type="bullet">
        /// <item><c>null</c>：<paramref name="returnResult"/> 为 <c>null</c>、或为已完成的 <see cref="Task"/>（无返回值）。</item>
        /// <item><c>T</c>：<paramref name="returnResult"/> 为已完成的 <see cref="Task{TResult}"/>，提取 <c>TResult</c> 作为返回值。</item>
        /// <item>原始对象：<paramref name="returnResult"/> 为非 Task 类型，直接返回原值。</item>
        /// </list>
        /// </returns>
        /// <exception cref="Exception">等待 <see cref="Task"/> 或 <see cref="Task{TResult}"/> 时，若任务内部抛出异常，将重新抛出。</exception>
        /// <remarks>
        /// <para>此方法通常用于 <see cref="TryInvokeMethod(object, MethodInfo, object[], out object)"/> 调用后，
        /// 处理异步方法（返回 <see cref="Task"/> 或 <see cref="Task{TResult}"/>）的返回值提取。</para>
        /// <para>注意：对于非泛型 <see cref="Task"/>（即 <c>async Task</c> 无返回值方法），await 完成后返回 <c>null</c>。</para>
        /// </remarks>
        public static async Task<object> GetReturnValue(object returnResult)
        {
            if (returnResult == null) return null;

            // 非 Task 类型 → 直接返回原始结果
            if (!(returnResult is Task task)) return returnResult;

            try
            {
                await task.ConfigureAwait(false);
                var returnType = returnResult.GetType();
                if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(Task<>))
                {
                    var resultProperty = returnType.GetProperty("Result");
                    return resultProperty?.GetValue(returnResult);
                }
            }
            catch (Exception ex)
            {
                Trace.TraceError(ex.ToString());
            }

            return null;
        }
        /// <summary>
        /// 从方法反射调用结果中提取指定类型的返回值。
        /// <para>功能与 <see cref="GetReturnValue(object)"/> 相同，并尝试将结果转换为 <typeparamref name="T"/> 类型。</para>
        /// </summary>
        /// <typeparam name="T">期望的返回值类型。</typeparam>
        /// <param name="returnResult">反射调用 <see cref="MethodBase.Invoke(object, object[])"/> 的原始返回值。</param>
        /// <returns>
        /// 如果提取成功且类型兼容，返回转换后的 <typeparamref name="T"/> 值；
        /// 如果 <paramref name="returnResult"/> 为 <c>null</c>、任务无返回值、或类型不兼容，则返回 <c>default(T)</c>。
        /// </returns>
        /// <remarks>
        /// <para>注意：对于值类型 <typeparamref name="T"/>，<c>default(T)</c> 可能是有效的业务值（如 0、false）。
        /// 调用方若需区分"无返回值"与"返回了默认值"，应使用 <see cref="GetReturnValue(object)"/> 先获取 <c>object</c> 结果再自行判断。</para>
        /// </remarks>
        public static async Task<T> GetReturnValue<T>(object returnResult)
        {
            var rawResult = await GetReturnValue(returnResult).ConfigureAwait(false);
            return rawResult is T typedResult ? typedResult : default;
        }
        
        #endregion


    }

}
