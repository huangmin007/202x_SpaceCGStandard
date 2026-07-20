using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Trace = SpaceCG.Diagnostics.Trace;

namespace SpaceCG.Extensions
{
    /// <summary>
    /// 扩展方法
    /// </summary>
    public static partial class ArrayExtensions
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
        /// <para>搜索区间为 [<paramref name="index"/>, <paramref name="index"/> + <paramref name="count"/>)。
        /// 采用头尾双过滤策略：先同时比对首尾字节，只有两者都命中才进入内循环逐字节比对。
        /// 随机数据下误入内循环概率约 1/65536，适合实时数据流分析等高性能场景。</para>
        /// <para>与 <see cref="IndexOf{T}"/> 泛型版本不同，本重载直接使用 CPU 原生的
        /// <c>byte == byte</c> 比较（单条 CMP 指令），避免虚调用开销，
        /// 且在 .NET Framework 4.8 下可享受 JIT 的边界检查消除和自动向量化优化。</para>
        /// <para>时间复杂度：平均 O(n)；空间复杂度 O(1)。</para>
        /// </summary>
        /// <param name="source">源字节数组。</param>
        /// <param name="pattern">待查找的字节模式，不能为空数组。</param>
        /// <param name="index">搜索起始索引（从 0 开始）。</param>
        /// <param name="count">
        /// 要搜索的元素数量，必须大于 0。
        /// 若 <c>index + count</c> 超出 <paramref name="source"/> 末尾，则自动截断到数组末尾。
        /// </param>
        /// <returns>找到时返回首次出现的起始索引；未找到或参数无效返回 -1。</returns>
        public static unsafe int IndexOf(this byte[] source, byte[] pattern, int index, int count)
        {
            // 参数有效性检查：null 或空数组直接返回 -1
            if (source == null || source.Length == 0) return -1;
            if (pattern == null || pattern.Length == 0) return -1;

            // count 必须为正数
            if (count <= 0) return -1;

            var sCount = source.Length;
            var pCount = pattern.Length;

            // 模式比要搜索的范围还长，直接返回 -1
            if (pCount > count || pCount > sCount) return -1;

            // 起始索引越界检查
            if (index < 0 || index >= sCount) return -1;

            // 防止 index + count 整型溢出，安全且精准地实现“自动截断”
            var searchEnd = (count > sCount - index) ? sCount : index + count;
            if (searchEnd <= index) return -1;

            var limit = searchEnd - pCount;
            if (index > limit) return -1;

            // 缓存首字节到局部变量
            var head = pattern[0];

            // 单字节模式，委托给 Array.IndexOf（JIT 内在优化）
            if (pCount == 1)
            {
                var searchLength = searchEnd - index;
                return Array.IndexOf(source, head, index, searchLength);
            }

            var pLast = pCount - 1;             // 尾部索引
            var tail = pattern[pLast];          // 尾字节（缓存成员访问）

#if true
            // 双字节模式，直接比对两个字节
            if (pCount == 2)
            {
                for (int i = index; i <= limit; i++)
                {
                    if (source[i] == head && source[i + 1] == tail) 
                        return i;
                }
                return -1;
            }

            // 头尾双过滤 + 中间逐字节比对
            // 策略：先同时检查首尾两个字节
            // 随机数据下同时命中的概率 ≈ 1/65536，第一步几乎全部排除
            // 若首尾都命中，再从第 2 到 pLast-1 逐字节确认（尾部已验过，跳过）
            for (int i = index; i <= limit; i++)
            {
                // 头尾同时比对（两次比较，短路求值：头不命中直接跳过尾比较）
                if (source[i] != head || source[i + pLast] != tail) continue;

                // 逐字节比对中间部分，发现不匹配立即 break
                int j;
                for (j = 1; j < pLast; j++)
                {
                    if (source[i + j] != pattern[j]) break;
                }

                if (j == pLast) return i;
            }
#else
            // 固定内存，获取指针
            fixed (byte* pSrc = source, pPat = pattern)
            {
                // 双字节模式：指针直接比较，无边界检查开销
                if (pCount == 2)
                {
                    for (int i = index; i <= limit; i++)
                    {
                        if (pSrc[i] == head && pSrc[i + 1] == tail)
                            return i;
                    }
                    return -1;
                }

                // 多字节模式：头尾过滤 + 指针内层块比较
                for (int i = index; i <= limit; i++)
                {
                    // 1. 头尾过滤（使用指针，无边界检查）
                    if (pSrc[i] != head || pSrc[i + pLast] != tail)
                        continue;

                    // 2. 内层逐字节比对（指针遍历）
                    int j = 1;
                    for (; j < pLast; j++)
                    {
                        if (pSrc[i + j] != pPat[j])
                            break;
                    }

                    if (j == pLast) return i;
                }
            }
#endif

            return -1;
        }
        /// <inheritdoc cref="IndexOf(byte[], byte[], int, int)"/> 
        public static int IndexOf(this byte[] source, byte[] pattern) => IndexOf(source, pattern, 0, source.Length);
        
        /// <summary>
        /// 在 <see cref="ArraySegment{T}"/> 的字节序列中查找指定字节模式的首次出现位置。
        /// <para>搜索区间为视图内 [<paramref name="index"/>, <paramref name="index"/> + <paramref name="count"/>)。
        /// 返回相对于视图起始（即 <see cref="ArraySegment{T}.Offset"/>）的偏移索引。</para>
        /// </summary>
        /// <param name="source">要搜索的源字节序列视图。</param>
        /// <param name="pattern">待查找的字节模式，不能为空数组。</param>
        /// <param name="index">
        /// 搜索起始位置，相对于视图起始的偏移（从 0 开始）。
        /// 传入 0 表示从视图的第一个字节开始搜索。
        /// </param>
        /// <param name="count">要搜索的元素数量，必须大于 0。</param>
        /// <returns>
        /// 模式首次出现相对于视图起始的偏移索引；未找到或参数无效返回 -1。
        /// </returns> 
        public static int IndexOf(this ArraySegment<byte> source, byte[] pattern, int index, int count)
        {
            // 参数有效性检查
            if (pattern == null || pattern.Length == 0) return -1;
            if (source.Array == null || source.Count == 0) return -1;

            var viewCount = source.Count;
            if (viewCount == 0 || count <= 0) return -1;

            // index 相对于视图的越界检查
            if (index < 0 || index >= viewCount) return -1;

            if (count > viewCount - index) return -1;

            // 将视图内的相对偏移转换为底层数组的绝对索引
            int absoluteIndex = source.Offset + index;
            // 委托给 byte[] 重载（返回绝对索引）
            int absoluteResult = IndexOf(source.Array, pattern, absoluteIndex, count);

            // 转换回相对于视图的偏移
            if (absoluteResult < 0) return -1;
            return absoluteResult - source.Offset;
        }
        /// <inheritdoc cref="IndexOf(byte[], byte[], int, int)"/> 
        public static int IndexOf(this ArraySegment<byte> source, byte[] pattern)
        {
            if (pattern == null || pattern.Length == 0) return -1;
            if (source.Array == null || source.Count == 0) return -1;

            int absoluteIndex = IndexOf(source.Array, pattern, source.Offset, source.Count);
            if (absoluteIndex < 0) return -1;
            return absoluteIndex - source.Offset;  // 转换为相对偏移
        }

        /// <summary>
        /// 在泛型数组中查找指定模式首次出现的位置。
        /// <para>采用头尾双过滤策略：先同时比对首尾元素，只有两者都命中才进入内循环逐元素比对。</para>
        /// <para>时间复杂度：平均 O(n)；空间复杂度 O(1)。</para>
        /// </summary>
        /// <typeparam name="T">数组元素类型，必须实现 <see cref="IEquatable{T}"/></typeparam>
        /// <param name="source">源数组</param>
        /// <param name="pattern">待查找的元素模式</param>
        /// <param name="index">搜索起始索引</param>
        /// <param name="count">要搜索的元素数量</param>
        /// <returns>找到时返回首次出现的起始索引；未找到或参数无效返回 -1。</returns>
        public static int IndexOf<T>(this T[] source, T[] pattern, int index, int count) where T : IEquatable<T>
        {
            // 参数有效性检查：null 或空数组直接返回 -1
            if (source == null || source.Length == 0) return -1;
            if (pattern == null || pattern.Length == 0) return -1;
            if (count <= 0) return -1;

            var sCount = source.Length;
            var pCount = pattern.Length;

            // 模式比要搜索的范围还长，直接返回 -1
            if (pCount > count || pCount > sCount) return -1;

            // 起始索引越界检查
            if (index < 0 || index >= sCount) return -1;

            // 防止 index + count 整型溢出，安全且精准地实现“自动截断”
            var searchEnd = (count > sCount - index) ? sCount : index + count;
            if (searchEnd <= index) return -1;

            var limit = searchEnd - pCount;
            if (index > limit) return -1;

            // 缓存首字节到局部变量
            var head = pattern[0];
            var comparer = EqualityComparer<T>.Default;

            // 单元素模式：委托给 Array.IndexOf，JIT 内在优化
            if (pCount == 1)
            {
                var searchLength = searchEnd - index;
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
                int j;
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
