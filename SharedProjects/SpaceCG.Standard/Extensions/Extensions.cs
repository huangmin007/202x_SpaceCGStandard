using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SpaceCG.Extensions
{
    /// <summary>
    /// 扩展方法
    /// </summary>
    public static partial class Extensions
    {
        /// <summary>
        /// 将字节集合转换为格式化的十六进制字符串，用于调试/测试输出（非高性能路径，内部使用 <see cref="StringBuilder"/> 构建）。
        /// <para>输出格式：每行 8 个字节，用空格分隔，每字节按 <paramref name="format"/> 格式化。</para>
        /// </summary>
        /// <param name="bytes">字节只读列表，支持 <see cref="byte"/>[]、<see cref="List{T}"/> 等</param>
        /// <param name="count">最大输出字节数，默认值 64，仅控制首行起截断</param>
        /// <param name="format">每字节的格式化字符串，{0} 为字节值占位，默认 "0x{0:X2}"</param>
        /// <returns>格式化的十六进制字符串；null 或空集合返回 <see cref="string.Empty"/>。</returns>
        public static string ToHexString(this IReadOnlyList<byte> bytes, int count = 64, string format = "0x{0:X2}")
        {
            if (bytes == null || bytes.Count == 0 || count <= 0) return string.Empty;

            var length = Math.Min(count, bytes.Count);
            var builder = new StringBuilder(count * 8);

            for (int i = 0; i < length; i += 8)
            {
                var lineEnd = Math.Min(i + 8, length);
                for (int j = i; j < lineEnd; j++)
                {
                    if (j > i) builder.Append(' ');
                    builder.AppendFormat(format, bytes[j]);
                }

                builder.AppendLine();
            }

            return builder.ToString().TrimEnd();
        }

        /// <summary>
        /// 在字节数组中查找指定字节模式首次出现的位置。
        /// <para>采用头尾双过滤策略：先同时比对首尾字节，只有两者都命中才进入内循环逐字节比对。
        /// 随机数据下误入内循环概率约 1/65536，适合实时数据流分析等高性能场景。</para>
        /// <para>与 <see cref="IndexOf{T}"/> 泛型版本不同，本重载直接使用 CPU 原生的
        /// <c>byte == byte</c> 比较（单条 CMP 指令），避免虚调用开销，
        /// 且在 .NET Framework 4.8 下可享受 JIT 的边界检查消除和自动向量化优化。</para>
        /// <para>时间复杂度：平均 O(n)；空间复杂度 O(1)。</para>
        /// </summary>
        /// <param name="source">源字节数组</param>
        /// <param name="pattern">待查找的字节模式</param>
        /// <param name="index">搜索起始索引（从 0 开始），默认为 0</param>
        /// <param name="count">要搜索的最大元素数量。-1 表示搜索到 <paramref name="source"/> 末尾</param>
        /// <returns>找到时返回首次出现的起始索引；未找到或参数无效返回 -1。</returns>
        public static int IndexOf(this byte[] source, byte[] pattern, int index = 0, int count = -1)
        {
            if (source == null || pattern == null) return -1;

            var sCount = source.Length;
            var pCount = pattern.Length;

            // 边界检查：空模式、模式过长、index 越界
            if (sCount == 0 || pCount == 0 || pCount > sCount) return -1;
            if (index < 0 || index >= sCount) return -1;

            // 计算有效搜索上界：若 count=-1 则搜索到 source 末尾，否则取 index+count
            var searchEnd = count < 0 ? sCount : index + count;
            if (searchEnd > sCount) searchEnd = sCount;       // 截断到 source 长度
            if (searchEnd <= index) return -1;                // 无有效搜索范围

            var limit = searchEnd - pCount;     
            if (index > limit) return -1;

            var head = pattern[0];              // 首字节（缓存成员访问）

            // 单字节模式
            if (pCount == 1)
            {
                var searchLength = count < 0 ? sCount - index : count;
                return Array.IndexOf(source, head, index, searchLength);
            }

            var pLast = pCount - 1;             // 尾部索引
            var tail = pattern[pLast];          // 尾字节（缓存成员访问）

            // 双字节模式，直接比对两个字节
            if (pCount == 2)
            {
                for (int i = index; i <= limit; i++)
                {
                    if (source[i] == head && source[i + 1] == tail) return i;
                }
                return -1;
            }

            // 头尾双过滤 + 中间逐字节比对
            // 策略：先同时检查首尾两个字节
            // 随机数据下同时命中的概率 ≈ 1/65536，第一步几乎全部排除
            // 若首尾都命中，再从头到中间逐字节确认（尾部已验过，跳过）
            for (int i = index; i <= limit; i++)
            {
                // 头尾同时比对（两次比较，短路求值：头不命中直接跳过尾比较）
                if (source[i] != head || source[i + pLast] != tail) continue;

                // 逐字节比对中间部分，发现不匹配立即 break
                int j = 1;
                for (j = 1; j < pLast; j++)
                {
                    if (source[i + j] != pattern[j]) break;
                }

                if (j == pLast) return i;
            }

            return -1;
        }


        /// <summary>
        /// 在泛型数组中查找指定模式首次出现的位置。
        /// <para>采用头尾双过滤策略：先同时比对首尾元素，只有两者都命中才进入内循环逐元素比对。</para>
        /// <para>时间复杂度：平均 O(n)；空间复杂度 O(1)。</para>
        /// </summary>
        /// <typeparam name="T">数组元素类型，必须实现 <see cref="IEquatable{T}"/></typeparam>
        /// <param name="source">源数组</param>
        /// <param name="pattern">待查找的元素模式</param>
        /// <param name="index">搜索起始索引，默认为 0</param>
        /// <param name="count">要搜索的元素数量。-1 表示搜索到 source 末尾</param>
        /// <returns>找到时返回首次出现的起始索引；未找到或参数无效返回 -1。</returns>
        public static int IndexOf<T>(this T[] source, T[] pattern, int index = 0, int count = -1) where T : IEquatable<T>
        {
            if (source == null || pattern == null) return -1;

            var sCount = source.Length;
            var pCount = pattern.Length;

            // 边界检查：空模式、模式过长、index 越界
            if (sCount == 0 || pCount == 0 || pCount > sCount) return -1;
            if (index < 0 || index >= sCount) return -1;

            // 计算有效搜索上界：若 count=-1 则搜索到 source 末尾，否则取 index+count
            var searchEnd = count < 0 ? sCount : index + count;
            if (searchEnd > sCount) searchEnd = sCount;       // 截断到 source 长度
            if (searchEnd <= index) return -1;                // 无有效搜索范围

            var limit = searchEnd - pCount;
            if (index > limit) return -1;

            var head = pattern[0];              // 首元素（缓存成员访问）
            var comparer = EqualityComparer<T>.Default;

            // 单元素模式：委托给 Array.IndexOf，享受 JIT 内在优化
            if (pCount == 1)
            {
                var searchLength = count < 0 ? sCount - index : count;
                return Array.IndexOf(source, head, index, searchLength);
            }

            var pLast = pCount - 1;             // 尾部索引
            var tail = pattern[pLast];          // 尾元素（缓存成员访问）

            // 双元素模式，直接比对两个元素
            if (pCount == 2)
            {
                for (int i = index; i <= limit; i++)
                {
                    if (comparer.Equals(source[i], head) && comparer.Equals(source[i + 1], tail)) return i;
                }
                return -1;
            }

            // 头尾双过滤 + 中间逐元素比对
            // 策略：先同时检查首尾两个元素
            // 随机数据下同时命中的概率 ≈ 1/n²（n 为候选值数量），第一步几乎全部排除
            // 若首尾都命中，再从第2到pLast-1逐元素确认（尾部已验过，跳过）
            for (int i = index; i <= limit; i++)
            {
                // 头尾同时比对（两次比较，短路求值：头不命中直接跳过尾比较）
                if (!comparer.Equals(source[i], head) || !comparer.Equals(source[i + pLast], tail)) continue;

                // 逐元素比对中间部分，发现不匹配立即 break
                int j = 1;
                for (j = 1; j < pLast; j++)
                {
                    if (!comparer.Equals(source[i + j], pattern[j])) break;
                }

                if (j == pLast) return i;
            }

            return -1;
        }

    }
}
