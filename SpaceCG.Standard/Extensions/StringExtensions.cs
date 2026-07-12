using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;

namespace SpaceCG.Extensions
{
    /// <summary>
    /// 字符串扩展方法
    /// </summary>
    public static partial class StringExtensions
    {        
        /// <summary>
        /// 是否为视频文件扩展名
        /// </summary>
        /// <param name="fileName"></param>
        /// <returns></returns>
        public static bool IsVideoExtension(this string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName)) return false;

            var extension = Path.GetExtension(fileName)?.ToLower();
            if (string.IsNullOrWhiteSpace(extension)) return false;

            switch (extension)
            {
                case ".mp4":
                case ".mkv":
                case ".m4v":
                case ".mov":
                case ".avi":
                case ".webm":
                case ".ts":
                case ".mts":
                case ".m2ts":
                case ".flv":
                case ".wmv":
                    return true;
            }

            return false;
        }
        /// <summary>
        /// 是否为图片文件扩展名
        /// </summary>
        /// <param name="fileName"></param>
        /// <returns></returns>
        public static bool IsImageExtension(this string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName)) return false;

            var extension = Path.GetExtension(fileName)?.ToLower();
            if (string.IsNullOrWhiteSpace(extension)) return false;

            switch (extension)
            {
                case ".jpg":
                case ".jpeg":
                case ".png":
                case ".bmp":
                case ".webp":
                case ".gif":   // 虽然支持动画，但大量还是单帧
                case ".tif":
                case ".tiff":
                case ".ico":
                case ".heic":
                case ".heif":
                    return true;
            }

            return false;
        }

        /// <summary>
        /// 判断字符串是否为合法的十六进制字符串（仅包含 0-9, A-F, a-f 字符）。
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsHexString(this string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;
            return value.All(c => (c >= '0' && c <= '9') || (c >= 'A' && c <= 'F') || (c >= 'a' && c <= 'f'));
        }

        /// <summary>
        /// 将单个十六进制字符转换为 4 位整数值（nibble）。
        /// </summary>
        /// <param name="c">十六进制字符（0-9, A-F, a-f）</param>
        /// <returns>0-15 之间的整数</returns>
        /// <exception cref="FormatException">字符不是合法的十六进制字符</exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int ToByte(this char c)
        {
            if (c >= '0' && c <= '9') return c - '0';
            if (c >= 'A' && c <= 'F') return c - 'A' + 10;
            if (c >= 'a' && c <= 'f') return c - 'a' + 10;

            throw new FormatException($"格式错误：'{c}' 不是有效的十六进制字符");
        }
        /// <summary>
        /// 将十六进制字符串转换为字节数组
        /// <para>示例：字符串 "0102030D0A" 转换为字节数组 {0x01, 0x02, 0x03，0x0D, 0x0A}</para>
        /// </summary>
        /// <param name="hex">十六进制字符串，字母不区分大小写</param>
        /// <returns>对应的字节数组</returns>
        /// <exception cref="ArgumentNullException">hex 为 null 或空白</exception>
        /// <exception cref="FormatException">hex 长度非偶数，或包含非十六进制字符</exception>
        public static byte[] ToByteArray(this string hex)
        {
            if (string.IsNullOrWhiteSpace(hex))
                throw new ArgumentNullException(nameof(hex), "参数不能为空");

            if (hex.Length % 2 != 0)
                throw new FormatException("格式错误：十六进制字符串长度必须为偶数");

            var array = new byte[hex.Length / 2];
            for (int i = 0; i < array.Length; i++)
            {
                var hi = ToByte(hex[i * 2]);        // high
                var lo = ToByte(hex[i * 2 + 1]);    // low
                array[i] = (byte)((hi << 4) | lo);
            }

            return array;
        }

        /// <summary>
        /// 将 RPC 参数文本解析为 <see cref="object"/>[] 树结构，是本转换流水线的入口。
        /// <para>解析规则：</para>
        /// <list type="bullet">
        /// <item><b>逗号分隔</b>：顶层按逗号分割为多个元素。</item>
        /// <item><b>引号保护</b>：单引号或双引号包裹的文本视为一个整体，其内部的逗号和括号被忽略；输出时自动剥离外层引号。</item>
        /// <item><b>括号嵌套</b>：方括号/圆括号/花括号包裹的文本视为子数组，递归调用本方法解析内部内容。</item>
        /// <item><b>叶子节点</b>：非引号且非括号的 token 作为原始字符串保留，后续由 <see cref="TryConvertTo(string, Type, out object)"/> 进行类型转换。</item>
        /// </list>
        /// <para>输出结构：顶层始终是 <c>object[]</c>；被括号包裹的部分是嵌套 <c>object[]</c>；叶子是 <c>string</c>。</para>
        /// <code>示例：
        /// "0x01,True,32,False"                        → ["0x01","True","32","False"]
        /// "0x01,3,[True,True,False]"                  → ["0x01","3",["True","True","False"]]
        /// "'hello,world',0x01,[True,False]"            → ["hello,world","0x01",["True","False"]]
        /// "['aaa,bb','ni,hao'],15"                     → [["aaa,bb","ni,hao"],"15"]
        /// </code>
        /// </summary>
        /// <param name="parameters">RPC 参数列表字符串，格式为逗号分隔的 token 序列。</param>
        /// <returns>解析后的 object[] 树；<c>null</c> 或空白输入返回空数组。</returns>
        /// <seealso cref="TryConvertTo(string, Type, out object)"/>
        /// <seealso cref="ConvertToString"/>
        public static object[] ToObjectArray(this string parameters)
        {
            if (string.IsNullOrWhiteSpace(parameters)) return Array.Empty<object>();

            var depth = 0;
            var start = 0;
            var inString = false;
            var stringChar = '\0';
            var result = new List<object>(8);   // 预分配容量，减少小数组扩容

            for (int i = 0; i < parameters.Length; i++)
            {
                char c = parameters[i];

                // ── 引号字符串：忽略内部逗号和括号 ──
                if (c == '\'' || c == '"')
                {
                    if (!inString)
                    {
                        inString = true;
                        stringChar = c;
                    }
                    else if (stringChar == c)
                    {
                        inString = false;
                    }
                    continue;
                }

                if (inString)
                    continue;

                // ── 括号嵌套深度 ──
                if (IsOpenBracket(c))
                {
                    depth++;
                    continue;
                }

                if (IsCloseBracket(c))
                {
                    if (depth > 0)
                        depth--;
                    continue;
                }

                // ── 顶层逗号：分割元素 ──
                if (c == ',' && depth == 0)
                {
                    AddTokenOptimized(parameters, start, i, result);
                    start = i + 1;
                }
            }

            // 最后一个元素
            AddTokenOptimized(parameters, start, parameters.Length, result);

            return result.ToArray();
        }
        /// <summary>
        /// 从源字符串中提取指定范围的 token，并解析其类型（普通值 / 字符串 / 嵌套数组）。
        /// 使用索引范围替代 Substring 减少中间分配。
        /// </summary>
        private static void AddTokenOptimized(string source, int tokenStart, int tokenEnd, List<object> result)
        {
            // 跳过前导空白
            while (tokenStart < tokenEnd && char.IsWhiteSpace(source[tokenStart])) tokenStart++;

            // 跳过尾随空白
            while (tokenEnd > tokenStart && char.IsWhiteSpace(source[tokenEnd - 1])) tokenEnd--;

            // 空 token
            if (tokenStart >= tokenEnd)
            {
                result.Add("");
                return;
            }

            var firstChar = source[tokenStart];
            var lastChar = source[tokenEnd - 1];

            // ── 引号字符串：剥离外层引号 ──
            if ((firstChar == '\'' && lastChar == '\'') || (firstChar == '"' && lastChar == '"'))
            {
                result.Add(source.Substring(tokenStart + 1, tokenEnd - tokenStart - 2));
                return;
            }

            // ── 嵌套括号数组：递归解析内部内容 ──
            if (IsOpenBracket(firstChar) && IsCloseBracket(lastChar))
            {
                // 仅剥离最外层括号，递归解析内部逗号列表
                result.Add(ToObjectArray(source.Substring(tokenStart + 1, tokenEnd - tokenStart - 2)));
                return;
            }

            // ── 普通值 ──
            result.Add(source.Substring(tokenStart, tokenEnd - tokenStart));
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsOpenBracket(char c) => c == '[' || c == '(' || c == '{';
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsCloseBracket(char c) => c == ']' || c == ')' || c == '}';

        /// <summary>
        /// 将单个字符串标量转换为指定值类型，是流水线中负责做 "string→强类型值" 的标量转换器。
        /// <para>被 <see cref="TypeExtensions.TryConvertTo"/> 调用，处理 <see cref="ToObjectArray"/> 产出的每个叶子节点字符串。</para>
        /// <para>支持的类型：</para>
        /// <list type="bullet">
        /// <item>string  → 直接返回。</item>
        /// <item>bool / float / double / decimal → 对应 TryParse。</item>
        /// <item>byte / sbyte / short / ushort / int / uint / long / ulong → 支持十进制和 0x 十六进制前缀。</item>
        /// <item>枚举 → Enum.Parse（忽略大小写）。</item>
        /// <item>Guid / TimeSpan / DateTime / DateTimeOffset → 标准 TryParse。</item>
        /// <item>其他值类型 → 通过 <see cref="TypeDescriptor.GetConverter(object)"/> 尝试转换。</item>
        /// </list>
        /// <para>空或空白字符串视为默认值，转换成功返回对应类型的默认实例。</para>
        /// </summary>
        /// <param name="value">待转换的字符串（来自 <see cref="ToObjectArray"/> 的叶子节点）。</param>
        /// <param name="targetType">目标值类型（必须为值类型或 string）。</param>
        /// <param name="targetValue">转换成功时输出强类型值；否则为 <c>null</c>。</param>
        /// <returns>转换成功返回 <c>true</c>；目标类型为引用类型（非 string）或转换失败返回 <c>false</c>。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="targetType"/> 为 <c>null</c> 或 <c>void</c>。</exception>
        /// <seealso cref="ToObjectArray"/>
        /// <seealso cref="TypeExtensions.TryConvertTo"/>
        public static bool TryConvertTo(this string value, Type targetType, out object targetValue)
        {
            targetValue = null;
            if (targetType == null || targetType == typeof(void)) 
                throw new ArgumentNullException(nameof(targetType));

            // 字符串类型直接返回
            if (targetType == typeof(string))
            {
                targetValue = value;
                return true;
            }
            // 只支持值类型转换，引用类型不支持(除字符本身)
            if (!targetType.IsValueType) return false;
            if (string.IsNullOrWhiteSpace(value))
            {
                targetValue = Activator.CreateInstance(targetType);
                return true;
            }

            // 枚举类型
            if (targetType.IsEnum)
            {
                try
                {
                    var boolValue = Enum.Parse(targetType, value, true);
                    targetValue = boolValue;
                    return true;
                }
                catch { }
                return false;
            }
            if (targetType == typeof(bool))
            {
                if (bool.TryParse(value, out var boolValue))
                {
                    targetValue = boolValue;
                    return true;
                }
                return false;
            }

            // float / double / decimal
            if (targetType == typeof(float))
            {
                if (float.TryParse(value, out var floatValue))
                {
                    targetValue = floatValue;
                    return true;
                }
                return false;
            }
            if (targetType == typeof(double))
            {
                if (double.TryParse(value, out var doubleValue))
                {
                    targetValue = doubleValue;
                    return true;
                }
                return false;
            }
            if (targetType == typeof(decimal))
            {
                if (decimal.TryParse(value, out var decimalValue))
                {
                    targetValue = decimalValue;
                    return true;
                }
                return false;
            }
            
            var valueTrim = value.Trim();
            var isHexNumber = value.StartsWith("0X", StringComparison.OrdinalIgnoreCase);
            var numberStyles = isHexNumber ? NumberStyles.HexNumber : NumberStyles.Integer;
            if (isHexNumber) valueTrim = valueTrim.Substring(2);
            // Integer
            if (targetType == typeof(byte))
            {
                if (byte.TryParse(valueTrim, numberStyles, CultureInfo.InvariantCulture, out var byteValue))
                {
                    targetValue = byteValue;
                    return true;
                }
                return false;
            }
            if (targetType == typeof(sbyte))
            {
                if (sbyte.TryParse(valueTrim, numberStyles, CultureInfo.InvariantCulture, out var sbyteValue))
                {
                    targetValue = sbyteValue;
                    return true;
                }
                return false;
            }
            if (targetType == typeof(short))
            {
                if (short.TryParse(valueTrim, numberStyles, CultureInfo.InvariantCulture, out var shortValue))
                {
                    targetValue = shortValue;
                    return true;
                }
                return false;
            }
            if (targetType == typeof(ushort))
            {
                if (ushort.TryParse(valueTrim, numberStyles, CultureInfo.InvariantCulture, out var ushortValue))
                {
                    targetValue = ushortValue;
                    return true;
                }
                return false;
            }
            if (targetType == typeof(int))
            {
                if (int.TryParse(valueTrim, numberStyles, CultureInfo.InvariantCulture, out var intValue))
                {
                    targetValue = intValue;
                    return true;
                }
                return false;
            }
            if (targetType == typeof(uint))
            {
                if (uint.TryParse(valueTrim, numberStyles, CultureInfo.InvariantCulture, out var uintValue))
                {
                    targetValue = uintValue;
                    return true;
                }
                return false;
            }
            if (targetType == typeof(long))
            {
                if (long.TryParse(valueTrim, numberStyles, CultureInfo.InvariantCulture, out var longValue))
                {
                    targetValue = longValue;
                    return true;
                }
                return false;
            }
            if (targetType == typeof(ulong))
            {
                if (ulong.TryParse(valueTrim, numberStyles, CultureInfo.InvariantCulture, out var ulongValue))
                {
                    targetValue = ulongValue;
                    return true;
                }
                return false;
            }

            // Guid,TimeSpan,DateTime,DateTimeOffset
            if (targetType == typeof(Guid))
            {
                if (Guid.TryParse(value, out var guidValue))
                {
                    targetValue = guidValue;
                    return true;
                }
                return false;
            }
            if (targetType == typeof(TimeSpan))
            {
                if (TimeSpan.TryParse(value, out var timeSpanValue))
                {
                    targetValue = timeSpanValue;
                    return true;
                }
                return false;
            }
            if (targetType == typeof(DateTime))
            {
                if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dateTimeValue))
                {
                    targetValue = dateTimeValue;
                    return true;
                }
                return false;
            }
            if (targetType == typeof(DateTimeOffset))
            {
                if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dateTimeOffsetValue))
                {
                    targetValue = dateTimeOffsetValue;
                    return true;
                }
                return false;
            }
            
            // 通用性值类型转换
            try
            {
                var converter = TypeExtensions.TypeConverterCache.GetOrAdd(targetType, type => TypeDescriptor.GetConverter(type));
                if (converter != null && converter.CanConvertFrom(typeof(string)))
                {
                    targetValue = converter.ConvertFromString(value);
                    return true;
                }
            }
            catch (Exception ex)
            {
                Trace.TraceWarning($"将字符 '{value}' 转换为指定类型 {targetType} 异常：{ex.Message}");
            }

            return false;
        }

        /// <inheritdoc cref="TryConvertTo(string, Type, out object)"/>
        /// <typeparam name="T">值类型</typeparam>
        /// <param name="value"></param>
        /// <param name="targetValue"></param>
        /// <returns></returns>
        public static bool TryConvertTo<T>(this string value, out T targetValue) where T : struct
        {
            targetValue = default;
            if (string.IsNullOrWhiteSpace(value)) return true;

            if (TryConvertTo(value, typeof(T), out object objectValue))
            {
                targetValue = (T)objectValue;
                return true;
            }

            return false;
        }

        /// <summary>
        /// 将对象序列化为字符串表示形式，是 <see cref="ToObjectArray"/> 的反向操作（”强类型值→可传输文本“）。
        /// <para>转换规则：</para>
        /// <list type="bullet">
        /// <item><c>null</c> → 字符串 <c>"null"</c>。</item>
        /// <item>string → 原样返回。</item>
        /// <item>值类型 → 调用 <c>ToString()</c>。</item>
        /// <item>数组 / IEnumerable&lt;T&gt; → 委托给 <see cref="ConvertEnumerableToString"/>，输出 <c>[elem1,elem2,...]</c> 格式。</item>
        /// <item>其他引用类型 → 调用 <c>ToString()</c> 兜底。</item>
        /// </list>
        /// <para>注意：本方法不保证产物一定能被 <see cref="ToObjectArray"/> 无损还原（例如值内含逗号或括号的场景）。
        /// 如需可靠往返，调用方应确保值内容不包含分隔符。</para>
        /// </summary>
        /// <param name="value">要序列化的对象。</param>
        /// <returns>字符串表示形式。</returns>
        /// <seealso cref="ToObjectArray"/>
        /// <seealso cref="TypeExtensions.TryConvertTo"/>
        public static string ConvertToString(object value)
        {
            if (value == null) return "null";

            // 字符串类型
            if (value is string stringValue) return stringValue;

            var valueType = value.GetType();
            // 基本值类型
            if (valueType.IsValueType) return value.ToString();
            // 数组类型
            if (valueType.IsArray) return ConvertEnumerableToString((System.Collections.IEnumerable)value);
            // IEnumerable<T> 类型
            if (valueType.IsIEnumerable() && value is System.Collections.IEnumerable enumerable) return ConvertEnumerableToString(enumerable);

            return value.ToString();
        }
        /// <summary>
        /// 将 <see cref="IEnumerable"/> 集合序列化为括号包裹的逗号分隔字符串。
        /// <para>由 <see cref="ConvertToString"/> 在处理数组或 IEnumerable&lt;T&gt; 时调用。</para>
        /// <para>格式：<c>[elem1,elem2,...]</c>，每个元素通过 <see cref="ConvertToString"/> 递归转换，因此支持多层嵌套。</para>
        /// </summary>
        /// <param name="enumerable">要序列化的集合。</param>
        /// <returns>如 <c>"[1,2,3]"</c> 或嵌套 <c>"[[1,2],[3,4]]"</c> 格式的字符串。</returns>
        public static string ConvertEnumerableToString(IEnumerable enumerable)
        {
            if (enumerable == null) return "null";

            var builder = new StringBuilder(128);
            builder.Append('[');

            var hasItems = false;
            foreach (var item in enumerable)
            {
                if (hasItems) builder.Append(',');
                builder.Append(ConvertToString(item));
                hasItems = true;
            }

            builder.Append(']');
            return builder.ToString();
        }
    }
}
