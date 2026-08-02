using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace SpaceCG.Extensions
{
    /// <summary>
    /// 数据帧扩展方法
    /// </summary>
    public static partial class FrameExtensions
    {
        /// <summary>
        /// 判断颜色数据帧是否有效
        /// </summary>
        /// <param name="frame"></param>
        /// <returns>如果有效，返回 true，否则返回 false</returns>
        public static bool IsValidColorFrame(this byte[] frame)
        {
            if (frame == null || frame.Length < 21) return false;

            // 帧头 & 帧尾
            if (frame[0] != 0xDD || frame[1] != 0x55 || frame[2] != 0xEE || frame[frame.Length - 2] != 0xAA || frame[frame.Length - 1] != 0xBB) return false;

            // 组地址 0~1024
            ushort group = GetGroup(frame);
            if (group > 1024) return false;

            // 设备地址 0~4096
            ushort address = GetAddress(frame);
            if (address > 4096) return false;

            // 端口地址 0~30
            byte port = GetPort(frame);
            if (port > 30) return false;

            // 功能码 0x98 & 0x99
            if (frame[8] != 0x98 && frame[8] != 0x99) return false;

            // 数据长度 3~3072
            int dataLength = GetDataLength(frame);
            if (dataLength < 3 || dataLength > 3072 || dataLength + 18 != frame.Length) return false;

            // 扩展次数 1~1024
            int repeatCount = GetRepeatCount(frame);
            if (repeatCount == 0 || repeatCount > 1024) return false;

            return true;
        }

        /// <summary>
        /// 获取组地址
        /// </summary>
        /// <param name="frame"></param>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ushort GetGroup(this byte[] frame) => (ushort)((frame[3] << 8) | frame[4]);
        /// <summary>
        /// 设置组地址
        /// </summary>
        /// <param name="frame"></param>
        /// <param name="group"></param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void SetGroup(this byte[] frame, ushort group)
        {
            frame[3] = (byte)(group >> 8);
            frame[4] = (byte)(group & 0xFF);
        }

        /// <summary>
        /// 获取设备地址
        /// </summary>
        /// <param name="frame"></param>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ushort GetAddress(this byte[] frame) => (ushort)((frame[5] << 8) | frame[6]);

        /// <summary>
        /// 设置设备地址
        /// </summary>
        /// <param name="frame"></param>
        /// <param name="address"></param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void SetAddress(this byte[] frame, ushort address)
        {
            frame[5] = (byte)(address >> 8);
            frame[6] = (byte)(address & 0xFF);
        }

        /// <summary>
        /// 获取端口地址
        /// </summary>
        /// <param name="frame"></param>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static byte GetPort(this byte[] frame) => frame[7];

        /// <summary>
        /// 设置端口地址
        /// </summary>
        /// <param name="frame"></param>
        /// <param name="port"></param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void SetPort(this byte[] frame, byte port)
        {
            frame[7] = port;
        }

        /// <summary>
        /// 获取功能码
        /// </summary>
        /// <param name="frame"></param>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static byte GetFunCode(this byte[] frame) => frame[8];

        /// <summary>
        /// 获取灯珠类型
        /// </summary>
        /// <param name="frame"></param>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static byte GetLedType(this byte[] frame) => frame[9];

        /// <summary>
        /// 获取保留字段
        /// </summary>
        /// <param name="frame"></param>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ushort GetReserved(this byte[] frame) => (ushort)((frame[10] << 8) | frame[11]);

        /// <summary>
        /// 设置保留字段
        /// </summary>
        /// <param name="frame"></param>
        /// <param name="reserved"></param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void SetReserved(this byte[] frame, ushort reserved)
        {
            frame[10] = (byte)(reserved >> 8);
            frame[11] = (byte)(reserved & 0xFF);
        }

        /// <summary>
        /// 获取数据长度(颜色数据)
        /// </summary>
        /// <param name="frame"></param>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ushort GetDataLength(this byte[] frame) => (ushort)((frame[12] << 8) | frame[13]);

        /// <summary>
        /// 设置数据长度(颜色数据)
        /// </summary>
        /// <param name="frame"></param>
        /// <param name="dataLength"></param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void SetDataLength(this byte[] frame, ushort dataLength)
        {
            frame[12] = (byte)(dataLength >> 8);
            frame[13] = (byte)(dataLength & 0xFF);
        }

        /// <summary>
        /// 获取扩展次数
        /// </summary>
        /// <param name="frame"></param>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ushort GetRepeatCount(this byte[] frame) => (ushort)((frame[14] << 8) | frame[15]);

        /// <summary>
        /// 设置扩展次数
        /// </summary>
        /// <param name="frame"></param>
        /// <param name="repeatCount"></param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void SetRepeatCount(this byte[] frame, ushort repeatCount)
        {
            frame[14] = (byte)(repeatCount >> 8);
            frame[15] = (byte)(repeatCount & 0xFF);
        }

    }
}
