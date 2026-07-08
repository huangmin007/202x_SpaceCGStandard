using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using SpaceCG.Extensions;

namespace SpaceCG.Generic
{
    /// <summary>
    /// CRC 即循环冗余校 (Cyclic Redundancy Check) 帮助类，提供 CRC8、CRC16、CRC32、CRC64 等常用 CRC 计算方法。
    /// </summary>
    public static class CRCheckHelper
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void ValidateCheck(IReadOnlyList<byte> bytes, int offset, int length)
        {
            if (bytes == null)
                throw new ArgumentNullException(nameof(bytes));
            if (offset < 0 || length < 0 || offset + length > bytes.Count)
                throw new ArgumentOutOfRangeException("offset,length", "超出集合的范围");
        }


        #region CRC8
        private static readonly ConcurrentDictionary<int, byte[]> _crc8TableCache = new ConcurrentDictionary<int, byte[]>();

        /// <summary>
        /// 生成 CRC8 256 项查找表。
        /// <para>内部使用 ConcurrentDictionary 进行缓存，相同参数只会生成一次。</para>
        /// </summary>
        /// <param name="poly">标准多项式 (例如: 0x07, 0x31, 0x1D)。注意：即使 refIn=true，也请传入标准多项式，代码内部会自动处理反转。</param>
        /// <param name="refIn">输入是否按位反转。true 表示 LSB-First (右移模式)，false 表示 MSB-First (左移模式)。</param>
        /// <returns>长度为 256 的 CRC8 查找表</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static byte[] GenerateCRC8Table(byte poly, bool refIn = false)
        {
            var key = (poly << 1) | (refIn ? 1 : 0);    //使用位运算生成唯一整数 Key
            return _crc8TableCache.GetOrAdd(key, value =>
            {
                byte[] table = new byte[256];

                if (refIn)
                {
                    byte reversedPoly = MathExtensions.ReverseBits(poly);
                    for (int i = 0; i < 256; i++)
                    {
                        byte crc = (byte)i;

                        // 在 LSB-First (右移) 模式下，多项式本身也必须进行位反转。
                        // 例如：MAXIM 标准 Poly 0x31 必须反转为 0x8C 才能用于右移计算。
                        for (int j = 0; j < 8; j++)
                        {
                            if ((crc & 0x01) != 0)
                            {
                                crc = (byte)((crc >> 1) ^ reversedPoly);
                            }
                            else
                            {
                                crc = (byte)(crc >> 1);
                            }
                        }
                        table[i] = crc;
                    }
                }
                else
                {
                    for (int i = 0; i < 256; i++)
                    {
                        byte crc = (byte)i;

                        // MSB First (左移模式)
                        for (int j = 0; j < 8; j++)
                        {
                            if ((crc & 0x80) != 0)
                            {
                                crc = (byte)((crc << 1) ^ poly);
                            }
                            else
                            {
                                crc = (byte)(crc << 1);
                            }
                        }
                        table[i] = crc;
                    }
                }

                return table;
            });
        }

        /// <summary>
        /// 通用 CRC8 计算 (基于 Rocksoft™ 通用模型)。注意：传入标准多项式，代码内部会自动处理反转。
        /// <para>常见 CRC 参数表参考：https://www.lddgo.net/encrypt/crc </para>
        /// </summary>
        /// <param name="bytes">需要计算 CRC 的字节数据源</param>
        /// <param name="offset">计算起始偏移量</param>
        /// <param name="length">需要计算的字节长度</param>
        /// <param name="poly">标准多项式 (如 0x07, 0x31, 0x1D)。注意：传入标准多项式，代码内部会自动处理反转。</param>
        /// <param name="init">寄存器初始值 (如 0x00, 0xFF)</param>
        /// <param name="refIn">输入数据是否按位反转 (true: LSB-First, false: MSB-First)</param>
        /// <param name="refOut">输出结果是否按位反转。通常与 refIn 保持一致。</param>
        /// <param name="xorOut">最终结果异或值</param>
        /// <returns>计算得出的 8 位 CRC 校验值 (纯数学值)</returns>
        public static byte ComputeCRC8(IReadOnlyList<byte> bytes, int offset, int length, byte poly = 0x07, byte init = 0x00, bool refIn = false, bool refOut = false, byte xorOut = 0x00)
        {
            ValidateCheck(bytes, offset, length);

            var table = GenerateCRC8Table(poly, refIn);
            byte crc = init;
            int end = offset + length;

            // 对于 8 位 CRC，无论 LSB-First 还是 MSB-First，
            // 因为寄存器宽度(8位)与输入字节宽度(8位)完全一致，查表索引逻辑完全相同！
            for (int i = offset; i < end; i++)
            {
                byte b = bytes[i];
                crc = table[crc ^ b];
            }

            // 【Rocksoft 模型标准】：如果输出反转标志与输入反转标志不一致，则对最终结果进行位反转。
            if (refOut != refIn)
                crc = MathExtensions.ReverseBits(crc);

            return (byte)(crc ^ xorOut);
        }
        /// <inheritdoc cref="ComputeCRC8(IReadOnlyList{byte}, int, int, byte, byte, bool, bool, byte)"/>
        /// <summary>
        /// 计算整个集合的 CRC8。
        /// </summary>
        public static byte ComputeCRC8(IReadOnlyList<byte> bytes, byte poly = 0x07, byte init = 0x00, bool refIn = false, bool refOut = false, byte xorOut = 0x00)
            => ComputeCRC8(bytes, 0, bytes.Count, poly, init, refIn, refOut, xorOut);

        /// <summary>
        /// 计算 CRC-8/ITU (常用于 ITU-T I.432.1 标准)。
        /// <para>参数特征：Poly=0x07, Init=0x00, RefIn=false, RefOut=false, XorOut=0x55</para>
        /// </summary>
        /// <param name="bytes"></param>
        /// <param name="offset"></param>
        /// <param name="length"></param>
        /// <returns></returns>
        public static byte ComputeCRC8_ITU(IReadOnlyList<byte> bytes, int offset, int length) => ComputeCRC8(bytes, offset, length, 0x07, 0x00, false, false, 0x55);
        /// <inheritdoc cref="ComputeCRC8_ITU(IReadOnlyList{byte}, int, int)"/> 
        public static byte ComputeCRC8_ITU(IReadOnlyList<byte> bytes) => ComputeCRC8(bytes, 0x07, 0x00, false, false, 0x55);

        /// <summary>
        /// 计算 CRC-8/ROHC (常用于 ROHC 协议)。
        /// <para>参数特征：Poly=0x07, Init=0xFF, RefIn=true, RefOut=true, XorOut=0x00</para>
        /// </summary>
        /// <param name="bytes"></param>
        /// <param name="offset"></param>
        /// <param name="length"></param>
        /// <returns></returns>
        public static byte ComputeCRC8_ROHC(IReadOnlyList<byte> bytes, int offset, int length) => ComputeCRC8(bytes, offset, length, 0x07, 0xFF, true, true, 0x00);
        /// <inheritdoc cref="ComputeCRC8_ROHC(IReadOnlyList{byte}, int, int)"/> 
        public static byte ComputeCRC8_ROHC(IReadOnlyList<byte> bytes) => ComputeCRC8(bytes, 0x07, 0xFF, true, true, 0x00);

        /// <summary>
        /// 计算 CRC-8/Dallas/Maxim (广泛应用于 Maxim 1-Wire 总线设备，如 DS18B20 温度传感器)。
        /// <para>参数特征：Poly=0x31, Init=0x00, RefIn=true, RefOut=true, XorOut=0x00</para>
        /// </summary>
        public static byte ComputeCRC8_MAXIM(IReadOnlyList<byte> bytes, int offset, int length) => ComputeCRC8(bytes, offset, length, 0x31, 0x00, true, true, 0x00);
        /// <inheritdoc cref="ComputeCRC8_MAXIM(IReadOnlyList{byte}, int, int)"/> 
        public static byte ComputeCRC8_MAXIM(IReadOnlyList<byte> bytes) => ComputeCRC8(bytes, 0x31, 0x00, true, true, 0x00);
        #endregion


        #region CRC16        
        private static readonly ConcurrentDictionary<int, ushort[]> _crc16TableCache = new ConcurrentDictionary<int, ushort[]>();
        
        /// <summary>
        /// 生成 CRC16 256 项查找表。
        /// <para>内部使用 ConcurrentDictionary 进行缓存，相同参数只会生成一次。</para>
        /// </summary>
        /// <param name="poly">标准多项式 (例如: 0x8005, 0x1021)。注意：即使 refIn=true，也请传入标准多项式，代码内部会自动处理反转。</param>
        /// <param name="refIn">输入是否按位反转。true 表示 LSB-First (右移模式)，false 表示 MSB-First (左移模式)。</param>
        /// <returns>长度为 256 的 CRC16 查找表</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ushort[] GenerateCRC16Table(ushort poly, bool refIn = false)
        {
            var key = (poly << 1) | (refIn ? 1 : 0);    //使用位运算生成唯一整数 Key
            return _crc16TableCache.GetOrAdd(key, value =>
            {
                ushort[] table = new ushort[256];
                if (refIn)
                {
                    // 在 LSB-First (右移) 模式下，多项式本身也必须进行位反转。
                    // 例如：标准 Poly 0x8005 必须反转为 0xA001 才能用于右移计算。
                    var reversedPoly = MathExtensions.ReverseBits(poly);

                    for (int i = 0; i < 256; i++)
                    {
                        ushort crc = (ushort)i;

                        for (int j = 0; j < 8; j++)
                        {
                            if ((crc & 0x0001) != 0)
                                crc = (ushort)((crc >> 1) ^ reversedPoly);
                            else
                                crc >>= 1;
                        }

                        table[i] = crc;
                    }
                }
                else
                {
                    // MSB-First (左移) 模式，直接使用标准多项式
                    for (int i = 0; i < 256; i++)
                    {
                        ushort crc = (ushort)(i << 8);  // 初始值放在高 8 位

                        for (int j = 0; j < 8; j++)
                        {
                            if ((crc & 0x8000) != 0)
                                crc = (ushort)((crc << 1) ^ poly);
                            else
                                crc <<= 1;
                        }

                        table[i] = crc;
                    }
                }
                return table;
            });
        }

        /// <summary>
        /// 通用 CRC16 计算 (基于 Rocksoft™ 通用模型)。注意：传入标准多项式，代码内部会自动处理反转。
        /// <para>常见 CRC 参数表参考：https://www.lddgo.net/encrypt/crc </para>
        /// </summary>
        /// <param name="bytes">需要计算 CRC 的字节数据源</param>
        /// <param name="offset">计算起始偏移量</param>
        /// <param name="length">需要计算的字节长度</param>
        /// <param name="poly">标准多项式 (如 0x8005, 0x1021)。注意：传入标准多项式，代码内部会自动处理反转。</param>
        /// <param name="init">寄存器初始值 (如 0x0000, 0xFFFF)</param>
        /// <param name="refIn">输入数据是否按位反转 (true: LSB-First, false: MSB-First)</param>
        /// <param name="refOut">输出结果是否按位反转。通常与 refIn 保持一致。</param>
        /// <param name="xorOut">最终结果异或值</param>
        /// <returns>计算得出的 16 位 CRC 校验值 (纯数学值)</returns>
        public static ushort ComputeCRC16(IReadOnlyList<byte> bytes, int offset, int length, ushort poly, ushort init = 0x0000, bool refIn = true, bool refOut = true, ushort xorOut = 0x0000)
        {
            ValidateCheck(bytes, offset, length);

            // 1.获取或生成查找表
            var table = GenerateCRC16Table(poly, refIn);

            ushort crc = init;
            int end = offset + length;

            // 2. 循环范围从 offset 开始，到 end 结束
            if (refIn)
            {
                // LSB-First (右移模式) 查表逻辑
                for (int i = offset; i < end; i++)
                {
                    byte b = bytes[i];
                    crc = (ushort)((crc >> 8) ^ table[(crc ^ b) & 0xFF]);
                }
            }
            else
            {
                // MSB-First (左移模式) 查表逻辑
                for (int i = offset; i < end; i++)
                {
                    byte b = bytes[i];
                    crc = (ushort)((crc << 8) ^ table[((crc >> 8) ^ b) & 0xFF]);
                }
            }

            // 3. 处理输出反转和最终异或
            // 【Rocksoft 模型标准】：如果输出反转标志与输入反转标志不一致，则对最终结果进行位反转。
            // 绝大多数标准协议中 refIn 和 refOut 是相同的，此时此判断为 false，不会执行反转。
            if (refOut != refIn)
                crc = MathExtensions.ReverseBits(crc);

            return (ushort)(crc ^ xorOut);
        }
        /// <inheritdoc cref="ComputeCRC16(IReadOnlyList{byte}, int, int, ushort, ushort, bool, bool, ushort)"/> 
        /// <summary> 计算整个集合的 CRC16。 </summary>
        public static ushort ComputeCRC16(IReadOnlyList<byte> bytes, ushort poly, ushort init = 0x0000, bool refIn = true, bool refOut = true, ushort xorOut = 0x0000)
            => ComputeCRC16(bytes, 0, bytes.Count, poly, init, refIn, refOut, xorOut);

        /// <summary>
        /// 计算 CRC-16/IBM 校验值。
        /// <para>参数特征：Poly=0x8005, Init=0x0000, RefIn=true, RefOut=true, XorOut=0x0000</para>
        /// </summary>
        /// <param name="bytes"></param>
        /// <param name="offset"></param>
        /// <param name="length"></param>
        /// <returns></returns>
        public static ushort ComputeCRC16_IBM(IReadOnlyList<byte> bytes, int offset, int length) => ComputeCRC16(bytes, offset, length, 0x8005, 0x0000, true, true, 0x0000);
        /// <inheritdoc cref="ComputeCRC16_IBM(IReadOnlyList{byte}, int, int)"/>
        public static ushort ComputeCRC16_IBM(IReadOnlyList<byte> bytes) => ComputeCRC16(bytes, 0x8005, 0x0000, true, true, 0x0000);

        /// <summary>
        /// 计算 CRC-16/MAXIM 校验值。
        /// <para>参数特征：Poly=0x8005, Init=0x0000, RefIn=true, RefOut=true, XorOut=0xFFFF</para>
        /// </summary>
        /// <param name="bytes"></param>
        /// <param name="offset"></param>
        /// <param name="length"></param>
        /// <returns></returns>
        public static ushort ComputeCRC16_MAXIM(IReadOnlyList<byte> bytes, int offset, int length) => ComputeCRC16(bytes, offset, length, 0x8005, 0x0000, true, true, 0xFFFF);
        /// <inheritdoc cref="ComputeCRC16_MAXIM(IReadOnlyList{byte}, int, int)"/>
        public static ushort ComputeCRC16_MAXIM(IReadOnlyList<byte> bytes) => ComputeCRC16(bytes, 0x8005, 0x0000, true, true, 0xFFFF);

        /// <summary>
        /// 计算 CRC-16/USB 校验值。
        /// <para>参数特征：Poly=0x8005, Init=0xFFFF, RefIn=true, RefOut=true, XorOut=0xFFFF</para>
        /// </summary>
        /// <param name="bytes"></param>
        /// <param name="offset"></param>
        /// <param name="length"></param>
        /// <returns></returns>
        public static ushort ComputeCRC16_USB(IReadOnlyList<byte> bytes, int offset, int length) => ComputeCRC16(bytes, offset, length, 0x8005, 0xFFFF, true, true, 0xFFFF);
        /// <inheritdoc cref="ComputeCRC16_USB(IReadOnlyList{byte}, int, int)"/>
        public static ushort ComputeCRC16_USB(IReadOnlyList<byte> bytes) => ComputeCRC16(bytes, 0x8005, 0xFFFF, true, true, 0xFFFF);

        /// <summary>
        /// 计算 CRC-16/MODBUS 校验值。
        /// <para>参数特征：Poly=0x8005, Init=0xFFFF, RefIn=true, RefOut=true, XorOut=0x0000</para>
        /// </summary>
        /// <param name="bytes">数据源</param>
        /// <param name="offset">起始偏移</param>
        /// <param name="length">计算长度</param>
        /// <returns>Modbus CRC16 校验值</returns>
        public static ushort ComputeCRC16_MODBUS(IReadOnlyList<byte> bytes, int offset, int length) => ComputeCRC16(bytes, offset, length, 0x8005, 0xFFFF, true, true, 0x0000);
        /// <inheritdoc cref="ComputeCRC16_MODBUS(IReadOnlyList{byte}, int, int)" />
        public static ushort ComputeCRC16_MODBUS(IReadOnlyList<byte> bytes) => ComputeCRC16(bytes, 0x8005, 0xFFFF, true, true, 0x0000);

        /// <summary>
        /// 计算 CRC-16/CCITT-KERMIT 校验值。
        /// <para>参数特征：Poly=0x1021, Init=0x0000, RefIn=true, RefOut=true, XorOut=0x0000</para>
        /// </summary>
        /// <param name="bytes"></param>
        /// <param name="offset"></param>
        /// <param name="length"></param>
        /// <returns></returns>
        public static ushort ComputeCRC16_CCITT(IReadOnlyList<byte> bytes, int offset, int length) => ComputeCRC16(bytes, offset, length, 0x1021, 0x0000, true, true, 0x0000);
        /// <inheritdoc cref="ComputeCRC16_CCITT(IReadOnlyList{byte}, int, int)"/>
        public static ushort ComputeCRC16_CCITT(IReadOnlyList<byte> bytes) => ComputeCRC16(bytes, 0x1021, 0x0000, true, true, 0x0000);

        /// <summary>
        /// 计算 CRC-16/CCITT-FALSE 校验值。
        /// <para>参数特征：Poly=0x1021, Init=0xFFFF, RefIn=false, RefOut=false, XorOut=0x0000</para>
        /// </summary>
        /// <param name="bytes"></param>
        /// <param name="offset"></param>
        /// <param name="length"></param>
        /// <returns></returns>
        public static ushort ComputeCRC16_CCITTFALSE(IReadOnlyList<byte> bytes, int offset, int length) => ComputeCRC16(bytes, offset, length, 0x1021, 0xFFFF, false, false, 0x0000);
        /// <inheritdoc cref="ComputeCRC16_CCITTFALSE(IReadOnlyList{byte}, int, int)"/>
        public static ushort ComputeCRC16_CCITTFALSE(IReadOnlyList<byte> bytes) => ComputeCRC16(bytes, 0x1021, 0xFFFF, false, false, 0x0000);

        /// <summary>
        /// 计算 CRC-16/CCITT-TRUE 校验值。
        /// <para>参数特征：Poly=0x1021, Init=0xFFFF, RefIn=true, RefOut=true, XorOut=0xFFFF</para>
        /// </summary>
        /// <param name="bytes"></param>
        /// <param name="offset"></param>
        /// <param name="length"></param>
        /// <returns></returns>
        public static ushort ComputeCRC16_X25(IReadOnlyList<byte> bytes, int offset, int length) => ComputeCRC16(bytes, offset, length, 0x1021, 0xFFFF, true, true, 0xFFFF);
        /// <inheritdoc cref="ComputeCRC16_X25(IReadOnlyList{byte}, int, int)"/>
        public static ushort ComputeCRC16_X25(IReadOnlyList<byte> bytes) => ComputeCRC16(bytes, 0x1021, 0xFFFF, true, true, 0xFFFF);

        /// <summary>
        /// 计算 CRC-16/CCITT-XMODEM 校验值。
        /// <para>参数特征：Poly=0x1021, Init=0x0000, RefIn=false, RefOut=false, XorOut=0x0000</para>
        /// </summary>
        /// <param name="bytes">数据源</param>
        /// <param name="offset">起始偏移</param>
        /// <param name="length">计算长度</param>
        /// <returns>CCITT-FALSE CRC16 校验值</returns>
        public static ushort ComputeCRC16_XMODEM(IReadOnlyList<byte> bytes, int offset, int length) => ComputeCRC16(bytes, offset, length, 0x1021, 0x0000, false, false, 0x0000);
        /// <inheritdoc cref="ComputeCRC16_XMODEM(IReadOnlyList{byte}, int, int)"/>
        public static ushort ComputeCRC16_XMODEM(IReadOnlyList<byte> bytes) => ComputeCRC16(bytes, 0x1021, 0x0000, false, false, 0x0000);

        /// <summary>
        /// 计算 CRC-16/CCITT-XMODEM2 校验值。
        /// <para>参数特征：Poly=0x8408, Init=0x0000, RefIn=true, RefOut=true, XorOut=0x0000</para>
        /// </summary>
        /// <param name="bytes"></param>
        /// <param name="offset"></param>
        /// <param name="length"></param>
        /// <returns></returns>
        public static ushort ComputeCRC16_XMODEM2(IReadOnlyList<byte> bytes, int offset, int length) => ComputeCRC16(bytes, offset, length, 0x8408, 0x0000, true, true, 0x0000);
        /// <inheritdoc cref="ComputeCRC16_XMODEM2(IReadOnlyList{byte}, int, int)"/>
        public static ushort ComputeCRC16_XMODEM2(IReadOnlyList<byte> bytes) => ComputeCRC16(bytes, 0x8408, 0x0000, true, true, 0x0000);

        /// <summary>
        /// 计算 CRC-16/DNP 校验值。
        /// <para>参数特征：Poly=0x3D65, Init=0x0000, RefIn=true, RefOut=true, XorOut=0xFFFF</para>
        /// </summary>
        /// <param name="bytes"></param>
        /// <param name="offset"></param>
        /// <param name="length"></param>
        /// <returns></returns>
        public static ushort ComputeCRC16_DNP(IReadOnlyList<byte> bytes, int offset, int length) => ComputeCRC16(bytes, offset, length, 0x3D65, 0x0000, true, true, 0xFFFF);
        /// <inheritdoc cref="ComputeCRC16_DNP(IReadOnlyList{byte}, int, int)"/>
        public static ushort ComputeCRC16_DNP(IReadOnlyList<byte> bytes) => ComputeCRC16(bytes, 0x3D65, 0x0000, true, true, 0xFFFF);
        #endregion


        #region CRC32        
        private static readonly ConcurrentDictionary<ulong, uint[]> _crc32TableCache = new ConcurrentDictionary<ulong, uint[]>();
        
        /// <summary>
        /// 生成 CRC32 256 项查找表。
        /// <para>内部使用 ConcurrentDictionary 进行缓存，相同参数只会生成一次。</para>
        /// </summary>
        /// <param name="poly">标准多项式 (例如: 0x04C11DB7)。注意：即使 refIn=true，也请传入标准多项式，代码内部会自动处理反转。</param>
        /// <param name="refIn">输入是否按位反转。true 表示 LSB-First (右移模式)，false 表示 MSB-First (左移模式)。</param>
        /// <returns>长度为 256 的 CRC32 查找表</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint[] GenerateCRC32Table(uint poly, bool refIn = false)
        {
            // 使用位运算生成唯一 long 型 Key
            var key = ((ulong)poly << 1) | (refIn ? 1UL : 0UL);
            return _crc32TableCache.GetOrAdd(key, k =>
            {
                uint[] table = new uint[256];

                if (refIn)
                {
                    // 在 LSB-First (右移) 模式下，多项式本身也必须进行位反转。
                    // 例如：标准 Poly 0x04C11DB7 必须反转为 0xEDB88320 才能用于右移计算。
                    uint reversedPoly = MathExtensions.ReverseBits(poly);

                    for (int i = 0; i < 256; i++)
                    {
                        uint crc = (uint)i;
                        for (int j = 0; j < 8; j++)
                        {
                            if ((crc & 0x00000001) != 0)
                                crc = (crc >> 1) ^ reversedPoly;
                            else
                                crc >>= 1;
                        }
                        table[i] = crc;
                    }
                }
                else
                {
                    // MSB-First (左移) 模式，直接使用标准多项式
                    for (int i = 0; i < 256; i++)
                    {
                        uint crc = (uint)i << 24;
                        for (int j = 0; j < 8; j++)
                        {
                            if ((crc & 0x80000000) != 0)
                                crc = (crc << 1) ^ poly;
                            else
                                crc <<= 1;
                        }
                        table[i] = crc;
                    }
                }
                return table;
            });
        }

        /// <summary>
        /// 通用 CRC32 计算 (基于 Rocksoft™ 通用模型)。注意：传入标准多项式，代码内部会自动处理反转。
        /// <para>常见 CRC 参数表参考：https://www.lddgo.net/encrypt/crc </para>
        /// </summary>
        /// <param name="bytes">需要计算 CRC 的字节数据源</param>
        /// <param name="offset">计算起始偏移量</param>
        /// <param name="length">需要计算的字节长度</param>
        /// <param name="poly">标准多项式 (如 0x04C11DB7)。注意：传入标准多项式，代码内部会自动处理反转。</param>
        /// <param name="init">寄存器初始值 (如 0xFFFFFFFF, 0x00000000)</param>
        /// <param name="refIn">输入数据是否按位反转 (true: LSB-First, false: MSB-First)</param>
        /// <param name="refOut">输出结果是否按位反转。通常与 refIn 保持一致。</param>
        /// <param name="xorOut">最终结果异或值 (注意：最常用的 ISO-HDLC 标准此处为 0xFFFFFFFF)</param>
        /// <returns>计算得出的 32 位 CRC 校验值 (纯数学值)</returns>
        public static uint ComputeCRC32(IReadOnlyList<byte> bytes, int offset, int length, uint poly = 0x04C11DB7, uint init = 0xFFFFFFFF, bool refIn = true, bool refOut = true, uint xorOut = 0xFFFFFFFF)
        {
            ValidateCheck(bytes, offset, length);

            var table = GenerateCRC32Table(poly, refIn);
            uint crc = init;
            int end = offset + length;

            if (refIn)
            {
                // LSB-First (右移模式) 查表逻辑
                for (int i = offset; i < end; i++)
                {
                    byte b = bytes[i];
                    crc = (crc >> 8) ^ table[(crc ^ b) & 0xFF];
                }
            }
            else
            {
                // MSB-First (左移模式) 查表逻辑
                for (int i = offset; i < end; i++)
                {
                    byte b = bytes[i];
                    crc = (crc << 8) ^ table[((crc >> 24) ^ b) & 0xFF];
                }
            }

            // 【Rocksoft 模型标准】：如果输出反转标志与输入反转标志不一致，则对最终结果进行位反转。
            if (refOut != refIn)
                crc = MathExtensions.ReverseBits(crc);

            return crc ^ xorOut;
        }
        /// <inheritdoc cref="ComputeCRC32(IReadOnlyList{byte}, int, int, uint, uint, bool, bool, uint)"/>
        /// <summary>
        /// 计算整个集合的 CRC32。
        /// </summary>
        public static uint ComputeCRC32(IReadOnlyList<byte> bytes, uint poly = 0x04C11DB7, uint init = 0xFFFFFFFF, bool refIn = true, bool refOut = true, uint xorOut = 0xFFFFFFFF)
            => ComputeCRC32(bytes, 0, bytes.Count, poly, init, refIn, refOut, xorOut);

        /// <summary>
        /// 计算 CRC-32/Castagnoli (硬件加速常用，如 SSE4.2, Btrfs, SCTP 等)。 通常大家口中说的 "CRC32C" 就是指这个。
        /// <para>参数特征：Poly=0x1EDC6F41, Init=0xFFFFFFFF, RefIn=true, RefOut=true, XorOut=0xFFFFFFFF</para>
        /// </summary>
        /// <param name="bytes"></param>
        /// <param name="offset"></param>
        /// <param name="length"></param>
        /// <returns></returns>
        public static uint ComputeCRC32_C(IReadOnlyList<byte> bytes, int offset, int length) => ComputeCRC32(bytes, offset, length, 0x1EDC6F41, 0xFFFFFFFF, true, true, 0xFFFFFFFF);
        /// <inheritdoc cref="ComputeCRC32_C(IReadOnlyList{byte}, int, int)"/>
        public static uint ComputeCRC32_C(IReadOnlyList<byte> bytes) => ComputeCRC32(bytes, 0x1EDC6F41, 0xFFFFFFFF, true, true, 0xFFFFFFFF);

        /// <summary>
        /// 计算 CRC-32/MPEG-2 (常用于视频流、DVB、DVD 等)。
        /// <para>参数特征：Poly=0x04C11DB7, Init=0xFFFFFFFF, RefIn=false, RefOut=false, XorOut=0x00000000</para>
        /// <para>注意：这是大端序 (MSB-First) 协议，通常用于网络传输或大端文件系统。</para>
        /// </summary>
        /// <param name="bytes">数据源</param>
        /// <param name="offset">起始偏移</param>
        /// <param name="length">计算长度</param>
        /// <returns>MPEG-2 CRC32 校验值</returns>
        public static uint ComputeCRC32_MPEG2(IReadOnlyList<byte> bytes, int offset, int length) => ComputeCRC32(bytes, offset, length, 0x04C11DB7, 0xFFFFFFFF, false, false, 0x00000000);
        /// <inheritdoc cref="ComputeCRC32_MPEG2(IReadOnlyList{byte}, int, int)"/>
        public static uint ComputeCRC32_MPEG2(IReadOnlyList<byte> bytes) => ComputeCRC32(bytes, 0x04C11DB7, 0xFFFFFFFF, false, false, 0x00000000);
        #endregion


        #region CRC64 
        private static readonly ConcurrentDictionary<(ulong Poly, bool RefIn), ulong[]> _crc64TableCache = new ConcurrentDictionary<(ulong Poly, bool RefIn), ulong[]>();

        /// <summary>
        /// 生成 CRC64 256 项查找表。
        /// <para>内部使用 ConcurrentDictionary 进行缓存，相同参数只会生成一次。</para>
        /// </summary>
        /// <param name="poly">标准多项式 (例如: 0x000000000000001BUL)。注意：即使 refIn=true，也请传入标准多项式，代码内部会自动处理反转。</param>
        /// <param name="refIn">输入是否按位反转。true 表示 LSB-First (右移模式)，false 表示 MSB-First (左移模式)</param>
        /// <returns>长度为 256 的 CRC64 查找表</returns>
        public static ulong[] GenerateCRC64Table(ulong poly, bool refIn = false)
        {
            var key = (poly, refIn);
            return _crc64TableCache.GetOrAdd(key, k =>
            {
                ulong[] table = new ulong[256];
                if (refIn)
                {
                    ulong reversedPoly = MathExtensions.ReverseBits(poly);
                    for (int i = 0; i < 256; i++)
                    {
                        ulong crc = (ulong)i;
                        for (int j = 0; j < 8; j++)
                            crc = (crc & 0x0000000000000001UL) != 0 ? (crc >> 1) ^ reversedPoly : crc >> 1;
                        table[i] = crc;
                    }
                }
                else
                {
                    for (int i = 0; i < 256; i++)
                    {
                        ulong crc = (ulong)i << 56;
                        for (int j = 0; j < 8; j++)
                            crc = (crc & 0x8000000000000000UL) != 0 ? (crc << 1) ^ poly : crc << 1;
                        table[i] = crc;
                    }
                }
                return table;
            });
        }

        /// <summary>
        /// 通用 CRC64 计算 (基于 Rocksoft™ 通用模型)。注意：传入标准多项式，代码内部会自动处理反转。
        /// <para>常见 CRC 参数表参考：https://www.lddgo.net/encrypt/crc </para>
        /// </summary>
        /// <param name="bytes">需要计算 CRC 的字节数据源</param>
        /// <param name="offset">计算起始偏移量</param>
        /// <param name="length">需要计算的字节长度</param>
        /// <param name="poly">标准多项式 (如 0x000000000000001BUL)。注意：传入标准多项式，代码内部会自动处理反转。</param>
        /// <param name="init">寄存器初始值 (如 0xFFFFFFFFFFFFFFFFUL, 0x0000000000000000UL)</param>
        /// <param name="refIn">输入数据是否按位反转 (true: LSB-First, false: MSB-First)</param>
        /// <param name="refOut">输出结果是否按位反转。通常与 refIn 保持一致。</param>
        /// <param name="xorOut">最终结果异或值 (注意：最常用的 ISO-HDLC 标准此处为 0xFFFFFFFF)</param>
        /// <returns>计算得出的 64 位 CRC 校验值 (纯数学值)</returns>
        public static ulong ComputeCRC64(IReadOnlyList<byte> bytes, int offset, int length, ulong poly, ulong init = 0xFFFFFFFFFFFFFFFFUL, bool refIn = true, bool refOut = true, ulong xorOut = 0xFFFFFFFFFFFFFFFFUL)
        {
            ValidateCheck(bytes, offset, length);

            var table = GenerateCRC64Table(poly, refIn);
            ulong crc = init;
            int end = offset + length;

            if (refIn)
            {
                // LSB-First (右移模式) 查表逻辑
                for (int i = offset; i < end; i++)
                {
                    byte b = bytes[i];
                    crc = (crc >> 8) ^ table[(crc ^ b) & 0xFF];
                }
            }
            else
            {
                // MSB-First (左移模式) 查表逻辑
                for (int i = offset; i < end; i++)
                {
                    byte b = bytes[i];
                    crc = (crc << 8) ^ table[((crc >> 56) ^ b) & 0xFF];
                }
            }

            if (refOut != refIn)
                crc = MathExtensions.ReverseBits(crc);

            return crc ^ xorOut;
        }
        /// <inheritdoc cref="ComputeCRC64(IReadOnlyList{byte}, int, int, ulong, ulong, bool, bool, ulong)"/>
        public static ulong ComputeCRC64(IReadOnlyList<byte> bytes, ulong poly, ulong init = 0xFFFFFFFFFFFFFFFFUL, bool refIn = true, bool refOut = true, ulong xorOut = 0xFFFFFFFFFFFFFFFFUL)
            => ComputeCRC64(bytes, 0, bytes.Count, poly, init, refIn, refOut, xorOut);

        /// <summary>
        /// 计算 CRC-64/ISO 校验值。
        /// <para>参数特征：Poly=0x000000000000001B, Init=0xFFFFFFFFFFFFFFFF, RefIn=true, RefOut=true, XorOut=0xFFFFFFFFFFFFFFFF</para>
        /// </summary>
        /// <param name="bytes"></param>
        /// <param name="offset"></param>
        /// <param name="length"></param>
        /// <returns></returns>
        public static ulong ComputeCRC64_ISO(IReadOnlyList<byte> bytes, int offset, int length)
            => ComputeCRC64(bytes, offset, length, 0x000000000000001BUL, 0xFFFFFFFFFFFFFFFFUL, true, true, 0xFFFFFFFFFFFFFFFFUL);
        /// <inheritdoc cref="ComputeCRC64_ISO(IReadOnlyList{byte}, int, int)"/>
        public static ulong ComputeCRC64_ISO(IReadOnlyList<byte> bytes)
            => ComputeCRC64(bytes, 0x000000000000001BUL, 0xFFFFFFFFFFFFFFFFUL, true, true, 0xFFFFFFFFFFFFFFFFUL);

        /// <summary>
        /// 计算 CRC-64/ECMA 校验值。
        /// <para>参数特征：Poly=0x42F0E1EBA9EA3693, Init=0xFFFFFFFFFFFFFFFF, RefIn=true, RefOut=true, XorOut=0xFFFFFFFFFFFFFFFF</para>
        /// </summary>
        /// <param name="bytes"></param>
        /// <param name="offset"></param>
        /// <param name="length"></param>
        /// <returns></returns>
        public static ulong ComputeCRC64_ECMA(IReadOnlyList<byte> bytes, int offset, int length)
            => ComputeCRC64(bytes, offset, length, 0x42F0E1EBA9EA3693UL, 0xFFFFFFFFFFFFFFFFUL, true, true, 0xFFFFFFFFFFFFFFFFUL);
        /// <inheritdoc cref="ComputeCRC64_ECMA(IReadOnlyList{byte}, int, int)"/>
        public static ulong ComputeCRC64_ECMA(IReadOnlyList<byte> bytes)
            => ComputeCRC64(bytes, 0x42F0E1EBA9EA3693UL, 0xFFFFFFFFFFFFFFFFUL, true, true, 0xFFFFFFFFFFFFFFFFUL);

        #endregion

    }
}
