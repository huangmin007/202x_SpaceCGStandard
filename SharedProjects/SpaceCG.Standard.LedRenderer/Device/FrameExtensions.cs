using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace SpaceCG.Device
{
    /// <summary>
    /// 数据帧扩展方法
    /// </summary>
    internal static class FrameExtensions
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

            // 功能码 0x99
            if (frame[8] != 0x99) return false;

            // 数据长度 3~3072
            int dataLength = GetDataLength(frame);
            if (dataLength < 3 || dataLength > 3072 || dataLength + 18 != frame.Length) return false;

            // 扩展次数 1~1024
            int repeatCount = GetRepeatCount(frame);
            if (repeatCount == 0 || repeatCount > 1024) return false;

            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ushort GetGroup(this byte[] frame) => (ushort)((frame[3] << 8) | frame[4]);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ushort GetAddress(this byte[] frame) => (ushort)((frame[5] << 8) | frame[6]);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static byte GetPort(this byte[] frame) => frame[7];

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static byte GetFuncCode(this byte[] frame) => frame[8];

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static byte GetLedType(this byte[] frame) => frame[9];

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ushort GetDataLength(this byte[] frame) => (ushort)((frame[12] << 8) | frame[13]);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ushort GetRepeatCount(this byte[] frame) => (ushort)((frame[14] << 8) | frame[15]);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        //public static byte[] GetData(byte[] frame) => frame[16..^2];
        public static IEnumerable<byte> GetFrameData(byte[] frame) => frame.Skip(16).Take(frame.Length - 18);
    }
}
