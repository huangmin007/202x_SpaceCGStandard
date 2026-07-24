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
    internal static partial class ArrayExtensions
    {
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
