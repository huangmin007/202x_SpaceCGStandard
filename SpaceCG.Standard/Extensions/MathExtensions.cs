using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;

namespace SpaceCG.Extensions
{
    /// <summary>
    /// Math Extensions
    /// </summary>
    public static partial class MathExtensions
    {
        /// <summary>
        /// 映射一个值到另一个值区间
        /// <para>映射公式： (value - min) * (outputMax - outputMin) / (max - min) + outputMin </para>
        /// </summary>
        /// <param name="value"></param>
        /// <param name="min"></param>
        /// <param name="max"></param>
        /// <param name="outputMin"></param>
        /// <param name="outputMax"></param>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double Map(double value, double min, double max, double outputMin = 0.0, double outputMax = 1.0)
        {
            if (min > max) throw new ArgumentException($"最小值 {min} 应该小于等于最大值 {max}");
            if (outputMin > outputMax) throw new ArgumentException($"最小值 {outputMin} 应该小于等于最大值 {outputMax}");

            if (max - min == 0) return outputMin;
            return (value - min) * (outputMax - outputMin) / (max - min) + outputMin;
        }


        #region Clamp
#if NET5_0_OR_GREATER
        /// <summary>
        /// 通用类型 INumber 的 Clamp 方法。
        /// </summary>
        /// <param name="value"></param>
        /// <param name="min"></param>
        /// <param name="max"></param>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static T Clamp<T>(T value, T min, T max) where T : INumber<T>
        {
            if (min > max) throw new ArgumentException($"最小值 {min} 应该小于等于最大值 {max}");

            if (value < min) return min;
            if (value > max) return max;

            return value;
        }
#else
        /// <summary>
        /// 返回 value 固定到 min 和 max 的非独占范围。
        /// </summary>
        /// <param name="value"></param>
        /// <param name="min"></param>
        /// <param name="max"></param>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static byte Clamp(byte value, byte min, byte max)
        {
            if (min > max) throw new ArgumentException($"最小值 {min} 应该小于等于最大值 {max}");

            if (value < min) return min;
            if (value > max) return max;

            return value;
        }
        /// <inheritdoc cref="Clamp(byte, byte, byte)"/>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static short Clamp(short value, short min, short max)
        {
            if (min > max) throw new ArgumentException($"最小值 {min} 应该小于等于最大值 {max}");

            if (value < min) return min;
            if (value > max) return max;

            return value;
        }
        /// <inheritdoc cref="Clamp(byte, byte, byte)"/>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ushort Clamp(ushort value, ushort min, ushort max)
        {
            if (min > max) throw new ArgumentException($"最小值 {min} 应该小于等于最大值 {max}");

            if (value < min) return min;
            if (value > max) return max;

            return value;
        }
        /// <inheritdoc cref="Clamp(byte, byte, byte)"/>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Clamp(int value, int min, int max)
        {
            if (min > max) throw new ArgumentException($"最小值 {min} 应该小于等于最大值 {max}");

            if (value < min) return min;
            if (value > max) return max;

            return value;
        }
        /// <inheritdoc cref="Clamp(byte, byte, byte)"/>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint Clamp(uint value, uint min, uint max)
        {
            if (min > max) throw new ArgumentException($"最小值 {min} 应该小于等于最大值 {max}");

            if (value < min) return min;
            if (value > max) return max;

            return value;
        }
        /// <inheritdoc cref="Clamp(byte, byte, byte)"/>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static long Clamp(long value, long min, long max)
        {
            if (min > max) throw new ArgumentException($"最小值 {min} 应该小于等于最大值 {max}");

            if (value < min) return min;
            if (value > max) return max;

            return value;
        }
        /// <inheritdoc cref="Clamp(byte, byte, byte)"/>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong Clamp(ulong value, ulong min, ulong max)
        {
            if (min > max) throw new ArgumentException($"最小值 {min} 应该小于等于最大值 {max}");

            if (value < min) return min;
            if (value > max) return max;

            return value;
        }
        /// <inheritdoc cref="Clamp(byte, byte, byte)"/>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Clamp(float value, float min, float max)
        {
            if (min > max) throw new ArgumentException($"最小值 {min} 应该小于等于最大值 {max}");

            if (value < min) return min;
            if (value > max) return max;

            return value;
        }
        /// <inheritdoc cref="Clamp(byte, byte, byte)"/>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double Clamp(double value, double min, double max)
        {
            if (min > max) throw new ArgumentException($"最小值 {min} 应该小于等于最大值 {max}");

            if (value < min) return min;
            if (value > max) return max;

            return value;
        }
#endif
        #endregion


        #region ReverseBits
        /// <summary>
        /// 8位 数据按位反转。
        /// 例如：0x31 (0011 0001) -> 0x8C (1000 1100)
        /// </summary>
        /// <param name="value">需要反转的 8 位无符号整数</param>
        /// <returns>反转后的 8 位无符号整数</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static byte ReverseBits(byte value)
        {
            value = (byte)(((value & 0xF0) >> 4) | ((value & 0x0F) << 4));      // 交换相邻的 4 位
            value = (byte)(((value & 0xCC) >> 2) | ((value & 0x33) << 2));      // 交换相邻的 2 位
            value = (byte)(((value & 0xAA) >> 1) | ((value & 0x55) << 1));      // 交换相邻的 1 位

            return value;
        }
        /// <summary>
        /// 16位 数据按位反转。
        /// 例如：0x8005 (1000 0000 0000 0101) -> 0xA001 (1010 0000 0000 0001)
        /// </summary>
        /// <param name="value">需要反转的 16 位无符号整数</param>
        /// <returns>反转后的 16 位无符号整数</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ushort ReverseBits(ushort value)
        {
            value = (ushort)(((value & 0xFF00) >> 8) | ((value & 0x00FF) << 8));    // 交换相邻的 8 位
            value = (ushort)(((value & 0xF0F0) >> 4) | ((value & 0x0F0F) << 4));    // 交换相邻的 4 位
            value = (ushort)(((value & 0xCCCC) >> 2) | ((value & 0x3333) << 2));    // 交换相邻的 2 位
            value = (ushort)(((value & 0xAAAA) >> 1) | ((value & 0x5555) << 1));    // 交换相邻的 1 位

            return value;
        }
        /// <summary>
        /// 32位 数据按位反转。
        /// </summary>
        /// <param name="value">需要反转的 32 位无符号整数</param>
        /// <returns>反转后的 32 位无符号整数</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint ReverseBits(uint value)
        {
            value = ((value & 0xFFFF0000) >> 16) | ((value & 0x0000FFFF) << 16);    // 交换相邻的 16 位            
            value = ((value & 0xFF00FF00) >> 8) | ((value & 0x00FF00FF) << 8);      // 交换相邻的 8 位            
            value = ((value & 0xF0F0F0F0) >> 4) | ((value & 0x0F0F0F0F) << 4);      // 交换相邻的 4 位            
            value = ((value & 0xCCCCCCCC) >> 2) | ((value & 0x33333333) << 2);      // 交换相邻的 2 位            
            value = ((value & 0xAAAAAAAA) >> 1) | ((value & 0x55555555) << 1);      // 交换相邻的 1 位

            return value;
        }
        /// <summary>
        /// 64位 数据按位反转。
        /// </summary>
        /// <param name="value">需要反转的 64 位无符号整数</param>
        /// <returns>反转后的 64 位无符号整数</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong ReverseBits(ulong value)
        {
            value = ((value & 0xFFFFFFFF00000000UL) >> 32) | ((value & 0x00000000FFFFFFFFUL) << 32);    // 交换相邻的 32 位
            value = ((value & 0xFFFF0000FFFF0000UL) >> 16) | ((value & 0x0000FFFF0000FFFFUL) << 16);    // 交换相邻的 16 位
            value = ((value & 0xFF00FF00FF00FF00UL) >> 8) | ((value & 0x00FF00FF00FF00FFUL) << 8);      // 交换相邻的 8 位
            value = ((value & 0xF0F0F0F0F0F0F0F0UL) >> 4) | ((value & 0x0F0F0F0F0F0F0F0FUL) << 4);      // 交换相邻的 4 位
            value = ((value & 0xCCCCCCCCCCCCCCCCUL) >> 2) | ((value & 0x3333333333333333UL) << 2);      // 交换相邻的 2 位
            value = ((value & 0xAAAAAAAAAAAAAAAAUL) >> 1) | ((value & 0x5555555555555555UL) << 1);      // 交换相邻的 1 位

            return value;
        }
        #endregion


        #region ReverseBytes
        /// <summary>
        /// 16位 数据字节顺序反转 (大小端转换)。
        /// 例如：0x1234 -> 0x3412
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ushort ReverseBytes(ushort value)
        {
            return (ushort)(((value & 0x00FF) << 8) | ((value & 0xFF00) >> 8));
        }
        /// <summary>
        /// 32位 数据字节顺序反转 (大小端转换)。
        /// 例如：0x12345678 -> 0x78563412
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint ReverseBytes(uint value)
        {
            return ((value & 0x000000FF) << 24) | ((value & 0x0000FF00) << 8) | ((value & 0x00FF0000) >> 8) | ((value & 0xFF000000) >> 24);
        }
        /// <summary>
        /// 64位 数据字节顺序反转 (大小端转换)。
        /// 例如：0x1122334455667788 -> 0x8877665544332211
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong ReverseBytes(ulong value)
        {
            value = ((value & 0x00000000000000FFUL) << 56) | ((value & 0x000000000000FF00UL) << 40) | ((value & 0x0000000000FF0000UL) << 24) | ((value & 0x00000000FF000000UL) << 8) |
                    ((value & 0x000000FF00000000UL) >> 8) | ((value & 0x0000FF0000000000UL) >> 24) | ((value & 0x00FF000000000000UL) >> 40) | ((value & 0xFF00000000000000UL) >> 56);
            return value;
        }
        #endregion

    }
}
