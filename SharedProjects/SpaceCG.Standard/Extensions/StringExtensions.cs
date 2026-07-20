using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security;
using System.Text;
using Trace = SpaceCG.Diagnostics.Trace;

namespace SpaceCG.Extensions
{
    /// <summary>
    /// 字符串扩展方法
    /// </summary>
    public static partial class StringExtensions
    {
        /// <summary>
        /// XML 转义字符对照表，与 <see cref="SecurityElement.Escape(string)"/> 的转义规则一致。
        /// </summary>
        public static IReadOnlyDictionary<char, string> EscapeCharacters { get; } = new Dictionary<char, string>()
        {
            { '<', "&lt;" },
            { '>', "&gt;" },
            { '&', "&amp;" },
            { '"', "&quot;" },
            { '\'', "&apos;" },
        };
        /// <summary>
        /// 将 XML 转义字符还原为原始字符（<see cref="SecurityElement.Escape(string)"/> 的逆操作）。
        /// <para>注意：<c>&amp;amp;</c> 先还原为 <c>&amp;</c>，避免后续替换产生二次匹配。</para>
        /// </summary>
        /// <param name="value">包含 XML 转义字符的字符串。</param>
        /// <returns>还原后的原始字符串；null 或空白返回原值。</returns>
        public static string Unescape(this string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return value;
            if (value.IndexOf('&') < 0) return value;

            // &amp; 必须最先替换，否则 &lt; &gt; 等中的 & 被替换后会产生错误的二次匹配
            return value.Replace("&amp;", "&")
                        .Replace("&lt;", "<")
                        .Replace("&gt;", ">")
                        .Replace("&quot;", "\"")
                        .Replace("&apos;", "'");
        }

        /// <summary>
        /// 判断文件名（或路径）的扩展名是否为常见视频格式。
        /// <para>扩展名比较忽略大小写，支持：mp4, mkv, m4v, mov, avi, webm, ts, mts, m2ts, flv, wmv。</para>
        /// </summary>
        /// <param name="fileName">文件名或文件路径</param>
        /// <returns>是视频文件扩展名返回 <c>true</c>；null/空白/无扩展名返回 <c>false</c></returns>
        public static bool IsVideoExtension(this string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName)) return false;

            var extension = Path.GetExtension(fileName)?.ToLowerInvariant();
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
        /// 判断文件名（或路径）的扩展名是否为常见图片格式。
        /// <para>扩展名比较忽略大小写，支持：jpg, jpeg, png, bmp, webp, gif, tif, tiff, ico, heic, heif。</para>
        /// </summary>
        /// <param name="fileName">文件名或文件路径</param>
        /// <returns>是图片文件扩展名返回 <c>true</c>；null/空白/无扩展名返回 <c>false</c></returns>
        public static bool IsImageExtension(this string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName)) return false;

            var extension = Path.GetExtension(fileName)?.ToLowerInvariant();
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
        /// 判断字符串是否仅包含十六进制字符（0-9, A-F, a-f）。
        /// <para>注意：会检查字符集合法性，以及验证长度是否为偶数。如需转换为字节数组请使用 <see cref="ToByteArray"/>。</para>
        /// </summary>
        /// <param name="value">待检查的字符串</param>
        /// <returns>全部字符均为十六进制字符返回 <c>true</c>；null/奇数/空白返回 <c>false</c></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsHexString(this string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;
            if (value.Length % 2 != 0) return false;

            return value.All(c => (c >= '0' && c <= '9') || (c >= 'A' && c <= 'F') || (c >= 'a' && c <= 'f'));
        }

        /// <summary>
        /// 将单个十六进制字符转换为 4 位整数值（nibble，0-15）。
        /// </summary>
        /// <param name="c">十六进制字符（0-9, A-F, a-f）</param>
        /// <returns>0-15 之间的整数</returns>
        /// <exception cref="FormatException">字符不是合法的十六进制字符</exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int HexCharToNibble(this char c)
        {
            if (c >= '0' && c <= '9') return c - '0';
            if (c >= 'A' && c <= 'F') return c - 'A' + 10;
            if (c >= 'a' && c <= 'f') return c - 'a' + 10;

            throw new FormatException($"格式错误：'{c}' 不是有效的十六进制字符");
        }
        /// <summary>
        /// 将十六进制字符串转换为字节数组。
        /// <para>示例：字符串 "0102030D0A" 转换为字节数组 {0x01, 0x02, 0x03, 0x0D, 0x0A}</para>
        /// <para>注意：不处理 "0x" 前缀，若字符串含前缀请先手动去除。</para>
        /// </summary>
        /// <param name="hex">十六进制字符串（不含前缀），字母不区分大小写，长度必须为偶数</param>
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
                var hi = HexCharToNibble(hex[i * 2]);        // high
                var lo = HexCharToNibble(hex[i * 2 + 1]);    // low
                array[i] = (byte)((hi << 4) | lo);
            }

            return array;
        }


        #region 自定义文本参数解析
        /// <summary>
        /// 将逗号分隔的参数文本解析为强类型嵌套数组树。
        /// <para>适用场景：配置文件参数解析、RPC 调试参数输入、命令行参数列表等高频调用路径。</para>
        /// <para>解析规则：</para>
        /// <list type="bullet">
        /// <item><b>逗号分隔</b>：顶层按逗号分割为多个元素。连续逗号之间的空 token 输出为空字符串 <c>""</c>，例如 <c>",,"</c> → <c>["", "", ""]</c>。空白行返回空数组。</item>
        /// <item><b>单引号保护</b>：单引号 <c>'...'</c> 包裹的文本视为一个整体，其内部的逗号和方括号被忽略，输出时自动剥离外层引号。不处理转义。</item>
        /// <item><b>方括号嵌套</b>：<c>[ ... ]</c> 包裹的文本视为子数组，递归解析内部内容。</item>
        /// <item><b>叶子节点</b>：非引号且非方括号的 token 经首尾去空白后作为 <c>string</c> 输出，后续由 <see cref="TryConvertTo(string, Type, out object)"/> 进行类型转换。</item>
        /// </list>
        /// <para>
        /// 输出结构：
        /// 顶层始终返回 <c>object[]</c>；
        /// 被 <c>[...]</c> 包裹且内部全为字符串的嵌套返回 <c>string[]</c>；
        /// 被 <c>[...]</c> 包裹且内部含更深层嵌套的返回 <c>object[]</c>；
        /// 叶子始终为 <c>string</c>。
        /// </para>
        /// <code>示例：
        /// "0x01,True,32,False"                        → ["0x01","True","32","False"]
        /// "0x01,3,[True,True,False]"                  → ["0x01","3", string[3]{"True","True","False"}]
        /// "'hello,world',0x01,[True,False]"            → ["hello,world","0x01", string[2]{"True","False"}]
        /// "['aaa,bb','ni,hao'],15"                     → [ string[2]{"aaa,bb","ni,hao"} ,"15"]
        /// "[[1,2],[3,4]]"                              → [ object[2]{string[2]{"1","2"}, string[2]{"3","4"}} ]
        /// ",,"                                         → ["", "", ""]
        /// </code>
        /// </summary>
        /// <param name="parameters">待解析的参数文本，逗号分隔的 token 序列。空或空白返回空数组。</param>
        /// <returns>解析后的 object[] 树。被 <c>[...]</c> 嵌套的全字符串子数组为 <c>string[]</c>，含更深层嵌套的为 <c>object[]</c>。</returns>
        /// <remarks>
        /// <para><b>性能特征</b>：单次扫描 O(n)，零反射、零 Regex、零 Split、零 StringBuilder，无中间临时分配。适用于 200fps+ 高频调用。</para>
        /// <para>仅支持 <c>[ ]</c> 方括号嵌套和 <c>'...'</c> 单引号，不支持 <c>()</c> / <c>{}</c> / 双引号 / 转义。</para>
        /// </remarks>
        /// <seealso cref="TryConvertTo(string, Type, out object)"/>
        /// <seealso cref="SerializeValue"/>
        public static object[] ParseParameters(this string parameters)
        {
            if (string.IsNullOrWhiteSpace(parameters))
                return Array.Empty<string>();

            int position = 0;
            int length = parameters.Length;
            var list = new List<object>(8);

            while (position < length)
            {
                SkipWhiteSpace(parameters, ref position, length);
                if (position >= length) break;

                if (parameters[position] == '[')
                {
                    position++;
                    list.Add(ParseList(parameters, ref position, length, ']'));
                    if (position < length && parameters[position] == ']') position++;
                }
                else if (parameters[position] == ',')
                {                    
                    position++; // 连续逗号：当前 token 为空字符串
                    list.Add(string.Empty);
                }
                else
                {
                    list.Add(ParseLeafString(parameters, ref position, length));
                }

                SkipWhiteSpace(parameters, ref position, length);
                if (position >= length) break;
                if (parameters[position] == ',') position++;
            }

            return list.ToArray();
        }
        /// <summary>
        /// 解析括号内部的逗号分隔列表。全 string → 返回 string[]；含嵌套 → 返回 object[]。
        /// </summary>
        private static Array ParseList(string text, ref int position, int length, char stopChar)
        {
            if (position >= length || text[position] == stopChar)
                return Array.Empty<string>();

            var list = new List<object>(8);
            bool allStrings = true;

            while (position < length)
            {
                SkipWhiteSpace(text, ref position, length);
                if (position >= length || text[position] == stopChar) break;

                if (text[position] == '[')
                {
                    position++;
                    list.Add(ParseList(text, ref position, length, ']'));
                    if (position < length && text[position] == ']') position++;
                    allStrings = false;
                }
                else if (text[position] == ',')
                {
                    position++; // 连续逗号：当前 token 为空字符串
                    list.Add(string.Empty);
                }
                else
                {
                    list.Add(ParseLeafString(text, ref position, length));
                }

                SkipWhiteSpace(text, ref position, length);
                if (position >= length) break;
                if (text[position] == stopChar) break;
                if (text[position] == ',') position++;
            }

            int count = list.Count;
            if (count == 0)
                return Array.Empty<string>();

            if (allStrings)
            {
                var result = new string[count];
                for (int i = 0; i < count; i++)
                    result[i] = (string)list[i];
                return result;
            }

            return list.ToArray();
        }
        /// <summary>
        /// 解析叶子字符串：'...' 或普通 token。
        /// </summary>
        private static string ParseLeafString(string text, ref int position, int length)
        {
            SkipWhiteSpace(text, ref position, length);
            if (position >= length) return string.Empty;

            // 单引号字符串
            if (text[position] == '\'')
            {
                position++;
                int start = position;
                while (position < length && text[position] != '\'') position++;
                int end = position;
                if (position < length) position++;
                return start >= end ? string.Empty : text.Substring(start, end - start);
            }

            // 普通 token
            int tStart = position;
            while (position < length)
            {
                char c = text[position];
                if (c == ',' || c == ']') break;
                position++;
            }
            int tEnd = position;

            while (tStart < tEnd && IsWhiteSpaceFast(text[tStart])) tStart++;
            while (tEnd > tStart && IsWhiteSpaceFast(text[tEnd - 1])) tEnd--;

            return tStart >= tEnd ? string.Empty : text.Substring(tStart, tEnd - tStart);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void SkipWhiteSpace(string text, ref int position, int length)
        {
            while (position < length && IsWhiteSpaceFast(text[position])) position++;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsWhiteSpaceFast(char c) => c == ' ' || c == '\r' || c == '\n' || c == '\t';
        #endregion


        #region TryConvertTo 将字符串转换为指定值类型
        /// <summary>
        /// 将单个字符串标量转换为指定值类型，"string→强类型值" 的标量转换器。
        /// <para>被 <see cref="TypeExtensions.TryConvertParameter"/> 调用，处理 <see cref="ParseParameters"/> 产出的每个叶子节点字符串。</para>
        /// <para>支持的类型：</para>
        /// <list type="bullet">
        /// <item>string  → 直接返回原值。</item>
        /// <item>bool / float / double / decimal → 对应 TryParse。</item>
        /// <item>byte / sbyte / short / ushort / int / uint / long / ulong → 支持十进制和 0x 十六进制前缀（十六进制仅无符号语义）。</item>
        /// <item>枚举 → Enum.Parse（忽略大小写）。</item>
        /// <item>Guid / TimeSpan / DateTime / DateTimeOffset → 标准 TryParse。</item>
        /// <item>其他值类型 → 通过 <see cref="TypeDescriptor.GetConverter(object)"/> 尝试转换。</item>
        /// </list>
        /// <para>null 或空白字符串始终返回 <c>false</c>（包括目标类型为 string 时）。</para>
        /// </summary>
        /// <param name="value">待转换的字符串（来自 <see cref="ParseParameters"/> 的叶子节点）。</param>
        /// <param name="targetType">目标值类型（必须为值类型或 string）。</param>
        /// <param name="targetValue">转换成功时输出强类型值；否则为 <c>null</c>。</param>
        /// <returns>转换成功返回 <c>true</c>；目标类型为引用类型（非 string）或转换失败返回 <c>false</c>。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="targetType"/> 为 <c>null</c> 或 <c>void</c>。</exception>
        /// <seealso cref="ParseParameters"/>
        /// <seealso cref="TypeExtensions.TryConvertParameter"/>
        public static bool TryConvertTo(this string value, Type targetType, out object targetValue)
        {
            targetValue = null;
            if (string.IsNullOrWhiteSpace(value)) return false;
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
            
            // 枚举类型
            if (targetType.IsEnum)
            {
                try
                {
                    var boolValue = Enum.Parse(targetType, value, true);
                    targetValue = boolValue;
                    return true;
                }
                catch (Exception) { }
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

            #region float / double / decimal
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
            #endregion

            #region Guid,TimeSpan,DateTime,DateTimeOffset
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
            #endregion

            var valueTrim = value.Trim();
            var isHexNumber = value.StartsWith("0X", StringComparison.OrdinalIgnoreCase);
            var numberStyles = isHexNumber ? NumberStyles.HexNumber : NumberStyles.Integer;
            if (isHexNumber) valueTrim = valueTrim.Substring(2);
            
            #region Integer
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
            #endregion
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
            catch (Exception ex) when (ex is NotSupportedException || ex is FormatException)
            {
                Trace.TraceWarning($"将字符 '{value}' 转换为指定类型 {targetType} 异常：{ex.Message}");
            }

            return false;
        }
        /// <summary>
        /// 将字符串转换为指定值类型的泛型版本。
        /// <para>null 或空白字符串返回 <c>false</c>，<paramref name="targetValue"/> 为 <c>default(T)</c>。</para>
        /// </summary>
        /// <typeparam name="T">目标值类型</typeparam>
        /// <param name="value">待转换的字符串</param>
        /// <param name="targetValue">转换成功时输出强类型值；否则为 <c>default(T)</c></param>
        /// <returns>转换成功返回 <c>true</c>；否则返回 <c>false</c></returns>
        public static bool TryConvertTo<T>(this string value, out T targetValue) where T : struct
        {
            targetValue = default;
            if (string.IsNullOrWhiteSpace(value)) return false;

            if (TryConvertTo(value, typeof(T), out object objectValue))
            {
                targetValue = (T)objectValue;
                return true;
            }

            return false;
        }
        #endregion


        /// <summary>
        /// 将对象序列化为字符串表示形式，是 <see cref="ParseParameters"/> 的反向操作（强类型值→可传输文本）。
        /// <para>转换规则：</para>
        /// <list type="bullet">
        /// <item><c>null</c> → 字符串 <c>"null"</c>。</item>
        /// <item>string → 原样返回。</item>
        /// <item>值类型 → 调用 <c>ToString()</c>。</item>
        /// <item>数组 / IEnumerable&lt;T&gt; → 委托给 <see cref="SerializeEnumerable"/>，输出 <c>[elem1,elem2,...]</c> 格式。</item>
        /// <item>其他引用类型 → 调用 <c>ToString()</c> 兜底。</item>
        /// </list>
        /// </summary>
        /// <param name="value">要序列化的值。</param>
        /// <returns>字符串表示形式。</returns>
        /// <seealso cref="ParseParameters"/>
        /// <seealso cref="TypeExtensions.TryConvertParameter"/>
        public static string SerializeValue(object value)
        {
            if (value == null) return "null";
            if (value is string stringValue)
            {
                if (stringValue.StartsWith("'") && stringValue.EndsWith("'"))
                    return stringValue.Substring(1, stringValue.Length - 2);

                if (stringValue.IndexOf(',') != -1) return $"\'{stringValue}\'";

                return stringValue;
            }

            var valueType = value.GetType();

            // 值类型：针对常见类型做专门优化
            if (valueType.IsValueType)
            {
                if (valueType == typeof(bool))
                    return ((bool)value) ? "True" : "False";
                if (valueType == typeof(int))
                    return ((int)value).ToString(CultureInfo.InvariantCulture);
                if (valueType == typeof(long))
                    return ((long)value).ToString(CultureInfo.InvariantCulture);
                if (valueType == typeof(float))
                    return ((float)value).ToString("R", CultureInfo.InvariantCulture);
                if (valueType == typeof(double))
                    return ((double)value).ToString("R", CultureInfo.InvariantCulture);
                if (valueType == typeof(decimal))
                    return ((decimal)value).ToString(CultureInfo.InvariantCulture);
                if (valueType == typeof(DateTime))
                    return ((DateTime)value).ToString("O", CultureInfo.InvariantCulture);
                if (valueType == typeof(DateTimeOffset))
                    return ((DateTimeOffset)value).ToString("O", CultureInfo.InvariantCulture);

                if (valueType.IsEnum)
                    return value.ToString();

                return value.ToString();
            }
            
            // 数组类型
            if (valueType.IsArray) return SerializeEnumerable((IEnumerable)value);
            
            // IEnumerable<T> 类型
            if (valueType.IsEnumerableOfT() && value is IEnumerable enumerable) return SerializeEnumerable(enumerable);

            return value.ToString();
        }
        /// <summary>
        /// 将 <see cref="IEnumerable"/> 集合序列化为括号包裹的逗号分隔字符串。
        /// <para>由 <see cref="SerializeValue"/> 在处理数组或 IEnumerable&lt;T&gt; 时调用。</para>
        /// <para>格式：<c>[elem1,elem2,...]</c>，每个元素通过 <see cref="SerializeValue"/> 递归转换，因此支持多层嵌套。</para>
        /// </summary>
        /// <param name="enumerable">要序列化的集合。</param>
        /// <returns>如 <c>"[1,2,3]"</c> 或嵌套 <c>"[[1,2],[3,4]]"</c> 格式的字符串。</returns>
        public static string SerializeEnumerable(IEnumerable enumerable)
        {
            if (enumerable == null) return "null";

            var builder = new StringBuilder(128);
            builder.Append('[');

            var hasItems = false;
            foreach (var item in enumerable)
            {
                if (hasItems) builder.Append(',');
                builder.Append(SerializeValue(item));
                hasItems = true;
            }

            builder.Append(']');
            return builder.ToString();
        }
    }
}
