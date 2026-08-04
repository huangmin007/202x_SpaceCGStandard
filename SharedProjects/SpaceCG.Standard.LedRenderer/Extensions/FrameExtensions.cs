using System;
using System.Runtime.CompilerServices;
using SpaceCG.Device;

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
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsValidColorFrame(this byte[] frame)
        {
            if (frame == null || frame.Length < 21 || frame.Length > 3072 + 18) return false;

            // 帧头 & 帧尾
            if (frame[0] != 0xDD || frame[1] != 0x55 || frame[2] != 0xEE || frame[frame.Length - 2] != 0xAA || frame[frame.Length - 1] != 0xBB) return false;

            // 组地址 0~1024
            var group = GetGroup(frame);
            if (group > 1024) return false;

            // 设备地址 0~4096
            var address = GetAddress(frame);
            if (address > 4096) return false;

            // 端口地址 0~6
            var port = GetPort(frame);
            if (port > 6) return false;

            // 功能码 0x98 & 0x99
            if (frame[8] != 0x98 && frame[8] != 0x99 && frame[8] != 0x9A) return false;

            // 数据长度 3~3072
            var dataLength = GetDataLength(frame);
            if (dataLength < 3 || dataLength > 3072 || dataLength + 18 != frame.Length) return false;

            // 扩展次数 1~1024
            var repeatCount = GetRepeatCount(frame);
            if (repeatCount == 0 || repeatCount > 1024) return false;

            return true;
        }

        /// <summary>
        /// 获取组地址
        /// </summary>
        /// <param name="frame"></param>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int GetGroup(this byte[] frame) => (frame[3] << 8) | frame[4];
        /// <summary>
        /// 设置组地址
        /// </summary>
        /// <param name="frame"></param>
        /// <param name="group"></param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void SetGroup(this byte[] frame, int group)
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
        public static int GetAddress(this byte[] frame) => (frame[5] << 8) | frame[6];
        /// <summary>
        /// 设置设备地址
        /// </summary>
        /// <param name="frame"></param>
        /// <param name="address"></param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void SetAddress(this byte[] frame, int address)
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
        public static void SetPort(this byte[] frame, byte port) => frame[7] = port;

        /// <summary>
        /// 获取功能码
        /// </summary>
        /// <param name="frame"></param>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static byte GetFunCode(this byte[] frame) => frame[8];
        /// <summary>
        /// 设置功能码
        /// </summary>
        /// <param name="frame"></param>
        /// <param name="funCode"></param>
        public static void SetFunCode(this byte[] frame, byte funCode) => frame[8] = funCode;
        /// <summary>
        /// 是否是颜色帧
        /// </summary>
        /// <param name="frame"></param>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsColorFrame(this byte[] frame) => frame[8] == 0x98 || frame[8] == 0x99 || frame[8] == 0x9A;


        /// <summary>
        /// 获取灯珠类型
        /// </summary>
        /// <param name="frame"></param>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static LedType GetLedType(this byte[] frame) => (LedType)frame[9];
        /// <summary>
        /// 设置灯珠类型
        /// </summary>
        /// <param name="frame"></param>
        /// <param name="ledType"></param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void SetLedType(this byte[] frame, LedType ledType) => frame[9] = (byte)ledType;

        /// <summary>
        /// 获取保留字段
        /// </summary>
        /// <param name="frame"></param>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int GetReserved(this byte[] frame) => (frame[10] << 8) | frame[11];
        /// <summary>
        /// 设置保留字段
        /// </summary>
        /// <param name="frame"></param>
        /// <param name="reserved"></param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void SetReserved(this byte[] frame, int reserved)
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
        public static int GetDataLength(this byte[] frame) => (frame[12] << 8) | frame[13];
        /// <summary>
        /// 设置数据长度(颜色数据)
        /// </summary>
        /// <param name="frame"></param>
        /// <param name="dataLength"></param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void SetDataLength(this byte[] frame, int dataLength)
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
        public static int GetRepeatCount(this byte[] frame) => (frame[14] << 8) | frame[15];
        /// <summary>
        /// 设置扩展次数
        /// </summary>
        /// <param name="frame"></param>
        /// <param name="repeatCount"></param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void SetRepeatCount(this byte[] frame, int repeatCount)
        {
            frame[14] = (byte)(repeatCount >> 8);
            frame[15] = (byte)(repeatCount & 0xFF);
        }

        /// <summary>
        /// 是否是广播帧。广播帧的定义：group 地址不为 0，或设备地址为 0，即为广播帧。
        /// </summary>
        /// <param name="frame"></param>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsBroadcastFrame(this byte[] frame) => ((frame[3] << 8) | frame[4]) != 0 || ((frame[5] << 8) | frame[6]) == 0;// || frame[7] == 0;

    }
}
