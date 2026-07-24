using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Trace = SpaceCG.Diagnostics.Trace;

namespace SpaceCG.Extensions
{
    /// <summary>
    /// Array Extensions
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

        #region IndexOf
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
        #endregion

        #region SequenceEqual 优化
        /// <summary>
        /// 原生 memcmp 函数声明，用于逐字节比较两块内存区域。
        /// <para>来自 Universal CRT（ucrtbase.dll），调用约定为 Cdecl。</para>
        /// <para>注意：当前代码使用自实现的 32 字节展开比较算法，此声明仅作为备选方案保留。</para>
        /// </summary>
        [System.Security.SuppressUnmanagedCodeSecurity]
        [DllImport("ucrtbase.dll", CallingConvention = CallingConvention.Cdecl, SetLastError = false)]
        //[DllImport("libc", CallingConvention = CallingConvention.Cdecl, SetLastError = false)] // Linux / macOS 回退方案：调用标准 C 库
        public unsafe static extern int memcmp(byte* b1, byte* b2, int count);

        /// <summary>
        /// 比较两块内存区域中的字节序列是否相等（指针版本）。
        /// <para>采用 32 字节展开比较策略：以 4 个 <c>ulong</c>（共 32 字节）为一块，
        /// 使用 64 位寄存器一次比较 8 字节，充分利用 CPU 的宽数据路径和指令级并行。
        /// 尾部不足 32 字节的部分按 8 字节 → 单字节两级收尾。</para>
        /// <para>时间复杂度：O(n)，空间复杂度 O(1)。零分配、无虚调用，适合热路径。</para>
        /// </summary>
        /// <param name="source">源数据指针，不可为 null。</param>
        /// <param name="destination">目标数据指针，不可为 null。</param>
        /// <param name="count">要比较的字节数。</param>
        /// <returns>
        /// <c>true</c>：两块内存区域的 <paramref name="count"/> 个字节完全相等；
        /// <c>false</c>：指针为 null、字节数无效或内容不匹配。
        /// </returns>
        public unsafe static bool SequenceEqual(byte* source, byte* destination, int count)
        {
            // 空指针检查：任一指针为 null 则无法比较
            if (source == null || destination == null) return false;

            // 表示空序列，返回 false
            if (count <= 0) return false;
            // 同一指针快速路径：指向相同地址则无需逐字节比较
            if (source == destination) return true;

            // ===== 32 字节展开比较 =====
            // 以 4 个 ulong（32 字节）为单位进行块比较，减少循环分支次数
            int blocks = count >> 5;        // count / 32，完整 32 字节块的数量
            ulong* src64 = (ulong*)source;
            ulong* dst64 = (ulong*)destination;

            for (int i = 0; i < blocks; i++)
            {
                // 一次比较 4 个 ulong，任一不匹配即返回 false
                if (src64[0] != dst64[0] || 
                    src64[1] != dst64[1] ||
                    src64[2] != dst64[2] || 
                    src64[3] != dst64[3])
                {
                    return false;
                }

                src64 += 4;
                dst64 += 4;
            }
            
            // ===== 尾部收尾 =====
            // 第一步：处理尾部数据中完整的 ulong（8 字节对齐部分）
            int tailBytes = count & 31;             // 尾部不足 32 字节的剩余字节数
            int tailUlongs = tailBytes >> 3;        // 尾部中完整 ulong 的数量（tailBytes / 8）
            for (int i = 0; i < tailUlongs; i++)
            {
                if (src64[i] != dst64[i])
                    return false;
            }

            // 第二步：处理不足 8 字节的尾部零散字节
            int offset = blocks * 32 + tailUlongs * 8;
            for (int i = offset; i < count; i++)
            {
                if (source[i] != destination[i])
                    return false;
            }

            return true;
        }
        /// <summary>
        /// 比较两块内存区域中指定偏移处的字节序列是否相等（指针版本）。
        /// <para>通过指针偏移定位后，委托给基础指针重载执行 32 字节展开比较。</para>
        /// <para><b>unsafe 调用方责任</b>：<paramref name="srcOffset"/> 和 <paramref name="destOffset"/> 
        /// 必须为非负值，且 <c>source + srcOffset</c> 与 <c>destination + destOffset</c> 指向的内存区域
        /// 至少包含 <paramref name="srcCount"/> 个可读字节，否则将导致访问违例（Access Violation）。</para>
        /// </summary>
        /// <param name="source">源数据指针，不可为 null。</param>
        /// <param name="srcOffset">源数据的起始偏移量（字节），必须 ≥ 0。</param>
        /// <param name="srcCount">源数据中要比较的字节数，必须 ≥ 0。</param>
        /// <param name="destination">目标数据指针，不可为 null。</param>
        /// <param name="destOffset">目标数据的起始偏移量（字节），必须 ≥ 0。</param>
        /// <param name="destCount">目标数据中要比较的字节数，必须与 <paramref name="srcCount"/> 相等。</param>
        /// <returns>
        /// <c>true</c>：两块内存指定区域的内容完全相等；
        /// <c>false</c>：指针为 null、长度不匹配或内容不相等。
        /// </returns>
        public unsafe static bool SequenceEqual(byte* source, int srcOffset, int srcCount, byte* destination, int destOffset, int destCount)
        {
            if (source == null || destination == null) return false;
            if (srcCount != destCount || srcCount <= 0) return false;

#if true
            // 指针偏移后委托给基础比较方法
            return SequenceEqual(source + srcOffset, destination + destOffset, srcCount);
#else
            // 备选方案：使用原生 memcmp，代码更简洁但跨平台一致性依赖 ucrtbase.dll
            return memcmp(source, destination, srcCount) == 0;
#endif
        }
        /// <summary>
        /// 比较两个托管字节数组中指定区域的字节序列是否相等。
        /// <para>对数组执行 <c>fixed</c> 固定后，委托给指针重载进行 32 字节展开比较。</para>
        /// </summary>
        /// <param name="source">源字节数组，不可为 null。</param>
        /// <param name="srcOffset">源数组中的起始偏移量。</param>
        /// <param name="srcCount">源数组中要比较的字节数。</param>
        /// <param name="destination">目标字节数组，不可为 null。</param>
        /// <param name="destOffset">目标数组中的起始偏移量。</param>
        /// <param name="destCount">目标数组中要比较的字节数，必须与 <paramref name="srcCount"/> 相等。</param>
        /// <returns>
        /// <c>true</c>：两个数组指定区域的内容完全相等；
        /// <c>false</c>：数组为 null、长度不匹配或内容不相等。
        /// </returns>
        /// <exception cref="ArgumentOutOfRangeException">
        /// 当 <paramref name="srcOffset"/> 或 <paramref name="destOffset"/> 超出数组边界时抛出。
        /// </exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe static bool SequenceEqual(this byte[] source, int srcOffset, int srcCount, byte[] destination, int destOffset, int destCount)
        {
            if (source == null || destination == null) return false;
            if (srcCount != destCount || srcCount <= 0) return false;

            // 边界检查：确保偏移 + 计数不超出数组末尾
            if (srcOffset < 0 || srcOffset + srcCount > source.Length)
                throw new ArgumentOutOfRangeException(nameof(srcOffset), srcOffset,  $"源数组偏移越界：offset={srcOffset} + count={srcCount} > length={source.Length}");

            if (destOffset < 0 || destOffset + destCount > destination.Length)
                throw new ArgumentOutOfRangeException(nameof(destOffset), destOffset, $"目标数组偏移越界：offset={destOffset} + count={destCount} > length={destination.Length}");

            fixed (byte* ps = source)
            fixed (byte* pd = destination)
            {
                return SequenceEqual(ps, srcOffset, srcCount, pd, destOffset, destCount);
            }
        }
        /// <summary>
        /// 比较两个托管字节数组的全部内容是否相等（扩展方法版本）。
        /// <para>对两个数组执行 <c>fixed</c> 固定后，委托给指针重载进行 32 字节展开比较。
        /// 当两个数组均为 null 或长度不相等时返回 <c>false</c>。</para>
        /// </summary>
        /// <param name="source">源字节数组，不可为 null。</param>
        /// <param name="destination">目标字节数组，不可为 null。</param>
        /// <returns>
        /// <c>true</c>：两个数组内容完全相同（包括均为 null 或长度均为 0）；
        /// <c>false</c>：任一数组为 null、长度不相等或内容不匹配。
        /// </returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe static bool SequenceEqual(this byte[] source, byte[] destination)
        {
            if (source == null || destination == null) return false;
            if (source.Length != destination.Length) return false;

            fixed (byte* ps = source)
            fixed (byte* pd = destination)
            {
                return SequenceEqual(ps, 0, source.Length, pd, 0, source.Length);
            }
        }
        #endregion

    }
}
