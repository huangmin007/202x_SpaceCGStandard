using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Trace = SpaceCG.Diagnostics.Trace;

namespace SpaceCG.Extensions
{
    /// <summary>
    /// Array Extensions
    /// </summary>
    internal static partial class ArrayExtensions
    {
        /// <summary>
        /// 原生 C 运行时内存比较函数（ucrtbase.dll）。
        /// </summary>
        /// <param name="b1">第一个内存块指针。</param>
        /// <param name="b2">第二个内存块指针。</param>
        /// <param name="count">比较的字节数。</param>
        /// <returns>0 表示内容完全相同；非 0 表示不相等。</returns>
        [System.Security.SuppressUnmanagedCodeSecurity]
        [DllImport("ucrtbase.dll", CallingConvention = CallingConvention.Cdecl, SetLastError = false)]
        public unsafe static extern int memcmp(byte* b1, byte* b2, int count);

        /// <summary>
        /// 比较两个托管字节数组的全部内容是否相等（扩展方法版本）。
        /// <para>对两个数组执行 <c>fixed</c> 固定后，委托给指针重载进行 32 字节展开比较。
        /// 当两个数组均为 null 或长度不相等时返回 <c>false</c>。</para>
        /// </summary>
        /// <param name="source">源字节数组，不可为 null。</param>
        /// <param name="destination">目标字节数组，不可为 null。</param>
        /// <returns>
        /// <c>true</c>：两个数组长度相同且内容完全一致；
        /// <c>false</c>：任一数组为 null、长度不同或内容不同。
        /// </returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe static bool FastSequenceEqual(this byte[] source, byte[] destination)
        {
            if (ReferenceEquals(source, destination)) return true;
            if (source == null || destination == null) return false;

            var sLength = source.Length;
            if (sLength != destination.Length) return false;
            if (sLength == 0) return true;

            fixed (byte* ps = source)
            fixed (byte* pd = destination)
            {
                return memcmp(ps, pd, sLength) == 0;
            }
        }


        /// <summary>
        /// 在一个一维数组中查找指定元素的位置（扩展方法版本）。
        /// </summary>
        /// <param name="list"></param>
        /// <param name="value"></param>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int IndexOf(this IReadOnlyList<byte> list, byte value)
        {
            if (list is byte[] array)
            {
                return Array.IndexOf(array, value);
            }

            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] == value) return i;
            }
            return -1;
        }
    }
}
