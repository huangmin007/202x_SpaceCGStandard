using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace SpaceCG.Generic
{
    /// <summary>
    /// Checksum 校验和帮助类，提供多种校验和计算方法。
    /// </summary>
    public static class ChecksumHelper
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void ValidateCheck(IReadOnlyList<byte> bytes, int offset, int length)
        {
            if (bytes == null)
                throw new ArgumentNullException(nameof(bytes));
            if (offset < 0 || length < 0 || offset + length > bytes.Count)
                throw new ArgumentOutOfRangeException("offset,length", "超出字节数组范围");
        }

        /// <summary>
        /// 计算字节数组的 8位 校验和
        /// </summary>
        /// <param name="bytes"></param>
        /// <param name="offset"></param>
        /// <param name="length"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static byte Sum8(IReadOnlyList<byte> bytes, int offset, int length)
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

        /// <summary>
        /// 计算字节数组的 16位 校验和
        /// </summary>
        /// <param name="bytes"></param>
        /// <param name="offset"></param>
        /// <param name="length"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ushort Sum16(IReadOnlyList<byte> bytes, int offset, int length)
        {
            ValidateCheck(bytes, offset, length);

            ushort sum = 0;
            int end = offset + length;

            for (int i = offset; i < end; i++)
            {
                unchecked { sum += bytes[i]; }
            }

            return sum;
        }

        /// <summary>
        /// 计算字节数组的 32位 校验和
        /// </summary>
        /// <param name="bytes"></param>
        /// <param name="offset"></param>
        /// <param name="length"></param>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong Sum32(IReadOnlyList<byte> bytes, int offset, int length)
        {
            ValidateCheck(bytes, offset, length);

            ulong sum = 0;
            int end = offset + length;

            for (int i = offset; i < end; i++)
            {
                unchecked { sum += bytes[i]; }
            }

            return sum;
        }

        /// <summary>
        /// 计算字节数组的 8位 异或校验(BCC校验)
        /// <para>BCC(Block Check Character/信息组校验码)，因校验码是将所有数据异或得出，故俗称异或校验。</para>
        /// </summary>
        /// <param name="bytes"></param>
        /// <param name="offset"></param>
        /// <param name="length"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static byte XOR8(IReadOnlyList<byte> bytes, int offset, int length)
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
        /// <inheritdoc cref="XOR8" />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static byte BCC8(IReadOnlyList<byte> bytes, int offset, int length) => XOR8(bytes, offset, length);

        /// <summary>
        /// 计算字节数组的 8位 LRC 校验
        /// <para>纵向冗余校验（Longitudinal Redundancy Check，简称：LRC）</para>
        /// </summary>
        /// <param name="bytes"></param>
        /// <param name="offset"></param>
        /// <param name="length"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static byte LRC8(IReadOnlyList<byte> bytes, int offset, int length)
        {
            ValidateCheck(bytes, offset, length);

            byte sum = 0;
            int end = offset + length;

            for (int i = offset; i < end; i++)
            {
                unchecked { sum += bytes[i]; }
            }

            return (byte)(-sum);
        }

    }

}
