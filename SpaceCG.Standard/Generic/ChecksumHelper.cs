using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace SpaceCG.Generic
{
    /// <summary>
    /// 校验和计算工具类，提供 Sum、XOR/BCC、LRC 等常用校验算法。
    /// <para>所有方法均支持指定字节范围和完整数组两种调用方式。</para>
    /// <para>线程安全：所有方法为纯计算，无共享状态。</para>
    /// </summary>
    public static class ChecksumHelper
    {
        /// <summary>
        /// 校验 offset 和 length 参数是否在 bytes 的有效范围内。
        /// </summary>
        /// <exception cref="ArgumentNullException">bytes 为 null。</exception>
        /// <exception cref="ArgumentOutOfRangeException">offset 或 length 越界。</exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void ValidateCheck(IReadOnlyList<byte> bytes, int offset, int length)
        {
            if (bytes == null)
                throw new ArgumentNullException(nameof(bytes));
            if (offset < 0 || length < 0 || offset + length > bytes.Count)
                throw new ArgumentOutOfRangeException(offset < 0 ? nameof(offset) : nameof(length), $"offset={offset}, length={length} 超出字节数组范围（Count={bytes.Count}）");
        }

        #region Sum 累加和校验
        /// <summary>
        /// 计算字节数组的 8 位累加和校验。
        /// <para>算法：逐字节累加，溢出回绕（截取低 8 位）。</para>
        /// </summary>
        /// <param name="bytes">字节数组。</param>
        /// <param name="offset">起始偏移。</param>
        /// <param name="length">参与计算的字节数。</param>
        /// <returns>8 位累加和。</returns>
        /// <exception cref="ArgumentNullException">bytes 为 null。</exception>
        /// <exception cref="ArgumentOutOfRangeException">offset 或 length 越界。</exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static byte Sum8(this IReadOnlyList<byte> bytes, int offset, int length)
        {
            ValidateCheck(bytes, offset, length);

            byte sum = 0;
            int end = offset + length;

            for (int i = offset; i < end; i++)
            {
                unchecked { sum += bytes[i]; }
            }

            return sum;
        }
        /// <inheritdoc cref="Sum8(IReadOnlyList{byte}, int, int)"/>
        public static byte Sum8(this IReadOnlyList<byte> bytes) => Sum8(bytes, 0, bytes.Count);

        /// <summary>
        /// 计算字节数组的 16 位累加和校验。
        /// <para>算法：按 16 位字（big-endian）累加，最后折叠进位到低 16 位。</para>
        /// <para>若字节数为奇数，最后一个字节作为低 8 位（高 8 位补 0）参与累加。</para>
        /// </summary>
        /// <inheritdoc cref="Sum8(IReadOnlyList{byte}, int, int)" path="/param|exception"/>
        /// <returns>16 位累加和。</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ushort Sum16(this IReadOnlyList<byte> bytes, int offset, int length)
        {
            ValidateCheck(bytes, offset, length);

            uint sum = 0;
            int end = offset + length;
            int i = offset;

            // 按 16 位字累加（big-endian：高字节在前）
            while (i + 1 < end)
            {
                sum += (uint)((bytes[i] << 8) | bytes[i + 1]);
                i += 2;
            }

            // 剩余 1 个字节：低 8 位有效，高 8 位补 0
            if (i < end)
            {
                sum += (uint)(bytes[i] << 8);
            }

            // 折叠进位：将高于 16 位的进位加到低 16 位
            while ((sum >> 16) != 0)
            {
                sum = (sum & 0xFFFF) + (sum >> 16);
            }

            return (ushort)sum;
        }
        /// <inheritdoc cref="Sum16(IReadOnlyList{byte}, int, int)"/>
        public static ushort Sum16(this IReadOnlyList<byte> bytes) => Sum16(bytes, 0, bytes.Count);

        /// <summary>
        /// 计算字节数组的 32 位累加和校验。
        /// <para>算法：按 32 位字（big-endian）累加，最后折叠进位到低 32 位。</para>
        /// <para>剩余 1~3 个字节按 big-endian 拼接后参与累加（不足 4 字节高位补 0）。</para>
        /// </summary>
        /// <inheritdoc cref="Sum8(IReadOnlyList{byte}, int, int)" path="/param|exception"/>
        /// <returns>32 位累加和。</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint Sum32(this IReadOnlyList<byte> bytes, int offset, int length)
        {
            ValidateCheck(bytes, offset, length);

            ulong sum = 0;
            int end = offset + length;
            int i = offset;

            // 按 32 位字累加（big-endian：高字节在前）
            while (i + 3 < end)
            {
                sum += ((ulong)bytes[i] << 24) | ((ulong)bytes[i + 1] << 16) | ((ulong)bytes[i + 2] << 8) | bytes[i + 3];
                i += 4;
            }

            // 剩余 1~3 个字节：按 big-endian 拼接，不足位补 0
            if (i < end)
            {
                ulong remainder = 0;
                int shift = 24;
                while (i < end)
                {
                    remainder |= (ulong)bytes[i] << shift;
                    i++;
                    shift -= 8;
                }
                sum += remainder;
            }

            // 折叠进位：将高于 32 位的进位加到低 32 位
            while ((sum >> 32) != 0)
            {
                sum = (sum & 0xFFFFFFFF) + (sum >> 32);
            }

            return (uint)sum;
        }
        /// <inheritdoc cref="Sum32(IReadOnlyList{byte}, int, int)"/>
        public static uint Sum32(this IReadOnlyList<byte> bytes) => Sum32(bytes, 0, bytes.Count);
        #endregion

        /// <summary>
        /// 计算字节数组的 8 位异或校验（所有字节逐位异或）。
        /// <para>BCC（Block Check Character，块校验字符），因校验码由所有数据异或得出，俗称异或校验。</para>
        /// </summary>
        /// <inheritdoc cref="Sum8(IReadOnlyList{byte}, int, int)" path="/param|exception"/>
        /// <returns>8 位异或结果。</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static byte XOR8(this IReadOnlyList<byte> bytes, int offset, int length)
        {
            ValidateCheck(bytes, offset, length);

            byte xor = 0;
            int end = offset + length;

            for (int i = offset; i < end; i++)
            {
                xor ^= bytes[i];
            }

            return xor;
        }
        /// <inheritdoc cref="XOR8(IReadOnlyList{byte}, int, int)"/>
        public static byte XOR8(this IReadOnlyList<byte> bytes) => XOR8(bytes, 0, bytes.Count);

        /// <summary>
        /// BCC 校验（Block Check Character），与 <see cref="XOR8(IReadOnlyList{byte}, int, int)"/> 等效。
        /// </summary>
        /// <inheritdoc cref="XOR8(IReadOnlyList{byte}, int, int)"/>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static byte BCC8(this IReadOnlyList<byte> bytes, int offset, int length) => XOR8(bytes, offset, length);
        /// <inheritdoc cref="BCC8(IReadOnlyList{byte}, int, int)"/>
        public static byte BCC8(this IReadOnlyList<byte> bytes) => XOR8(bytes);

        /// <summary>
        /// 计算字节数组的 8 位 LRC 校验（Longitudinal Redundancy Check，纵向冗余校验）。
        /// <para>算法：<c>LRC = -(sum of all bytes) mod 256</c>，等价于 <c>(byte)(-sum)</c> 或 <c>((sum ^ 0xFF) + 1)</c>。</para>
        /// </summary>
        /// <inheritdoc cref="Sum8(IReadOnlyList{byte}, int, int)" path="/param|exception"/>
        /// <returns>8 位 LRC 校验值。</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static byte LRC8(this IReadOnlyList<byte> bytes, int offset, int length)
        {
            ValidateCheck(bytes, offset, length);

            byte sum = 0;
            int end = offset + length;

            for (int i = offset; i < end; i++)
            {
                unchecked { sum += bytes[i]; }
            }
            // LRC = 累加和的二进制补码（取负），等价于 ((sum ^ 0xFF) + 1)
            return (byte)(-sum);
        }
        /// <inheritdoc cref="LRC8(IReadOnlyList{byte}, int, int)"/>
        public static byte LRC8(IReadOnlyList<byte> bytes) => LRC8(bytes, 0, bytes.Count);

    }

}
