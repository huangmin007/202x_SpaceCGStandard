using System;
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
        /// 缓存类型转换器，避免每次调用时反射带来的性能开销。
        /// </summary>
        internal static readonly ConcurrentDictionary<Type, TypeConverter> TypeConverterCache = new ConcurrentDictionary<Type, TypeConverter>();

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
        /// 将逗号分隔的参数列表字符串解析为 object[] 树结构。支持引号字符串、嵌套括号（[] / () / {}），叶子节点保留为原始字符串。
        /// <code>示例：
        /// 输入字符串："0x01,True,32,False"，输出数组：["0x01","True","32","False"]
        /// 输入字符串："0x01,3,[True,True,False]"，输出数组：["0x01","3",["True","True","False"]]
        /// 输入字符串："0x01,[0,3,4,7],[True,True,False,True]"，输出数组：["0x01",["0","3","4","7"],["True","True","False","True"]]
        /// 输入字符串："'hello,world',0x01,3,'ni?,hao,[aa,bb]', [True,True,False],['aaa,bb,c','ni,hao'],15,\"aa,aaa\",15"
        /// 输出数组：["hello,world","0x01","3","ni?,hao,[aa,bb]",["True","True","False","True"],["aaa,bb,c","ni,hao"],"15","aa,aaa","15"]
        /// </code>
        /// </summary>
        /// <param name="parameters">逗号分隔的参数列表字符串</param>
        /// <returns>析后的 object 数组；空输入返回空数组</returns>
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
        /// 尝试将字符串转换为指定类型的值，返回转换结果和是否成功的标志。支持基本类型、枚举、结构体等值类型转换。
        /// </summary>
        /// <param name="value"></param>
        /// <param name="targetType"></param>
        /// <param name="targetValue"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentNullException"></exception>
        public static bool TryConvertTo(this string value, Type targetType, out object targetValue)
        {
            targetValue = null;
            if (targetType == null) throw new ArgumentNullException(nameof(targetType));

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

            if (targetType == typeof(bool))
            {
                if (bool.TryParse(value, out var boolValue))
                {
                    targetValue = boolValue;
                    return true;
                }
                return false;
            }
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

            // byte,ushort,int,long
            var valueTrim = value.Trim();
            var isHexNumber = value.StartsWith("0X", StringComparison.OrdinalIgnoreCase);
            var numberStyles = isHexNumber ? NumberStyles.HexNumber : NumberStyles.Integer;
            if (isHexNumber) valueTrim = valueTrim.Substring(2);

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

            // 通用性值类型转换
            try
            {
                var converter = TypeConverterCache.GetOrAdd(targetType, type => TypeDescriptor.GetConverter(type));
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


        /// <summary>
        /// 尝试将字符串转换为指定类型的值，返回转换结果和是否成功的标志。支持基本类型、枚举、结构体等值类型转换。
        /// </summary>
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

    }
}
