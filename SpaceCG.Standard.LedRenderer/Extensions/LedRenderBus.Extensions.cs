using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using SpaceCG.Device;
using Trace = SpaceCG.Diagnostics.Trace;

namespace SpaceCG.Extensions
{
    /// <summary>
    /// <see cref="LedRenderBus"/> 集合批量操作扩展方法。
    /// </summary>
    /// <remarks>
    /// <para>提供对 <see cref="IEnumerable{LedRenderBus}"/> 的便捷批量操作，
    /// 包括通道管理、渲染控制、灯带查询和资源释放。</para>
    /// <para>所有方法遍历集合时对单个总线的异常做 try-catch（通道操作）或透传（渲染控制），
    /// 确保一个总线失败不影响其他总线的操作。</para>
    /// </remarks>
    public static partial class LedRenderBusExtensions
    {
        /// <summary>
        /// 释放集合中所有 <see cref="LedRenderBus"/> 实例。
        /// </summary>
        /// <param name="collections">渲染总线集合。</param>
        /// <remarks>
        /// <para>通过重复获取第一个元素并调用 <see cref="LedRenderBus.Dispose"/> 的方式逐个释放，
        /// 因为 <see cref="LedRenderBus.Dispose"/> 内部会从全局集合中移除自身。</para>
        /// <para><b>注意：</b>此方法会修改源集合，遍历期间集合内容会动态变化。</para>
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Dispose(this IEnumerable<LedRenderBus> collections)
        {
            while (collections.Count() > 0)
            {
                collections.ElementAt(0).Dispose();
            }
        }

        #region 总线&设备
        /// <summary>
        /// 获取集合中指定 ID 的 <see cref="LedRenderBus"/> 实例。
        /// </summary>
        /// <param name="collections"></param>
        /// <param name="busId"></param>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static LedRenderBus GetRenderBus(this IEnumerable<LedRenderBus> collections, int busId)
        {
            return collections.FirstOrDefault(x => x.BusId == busId);
        }

        /// <summary>
        /// 获取集合中所有总线上的非重复设备地址。
        /// </summary>
        /// <param name="collections">渲染总线集合。</param>
        /// <returns>去重后的设备地址集合。</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static IEnumerable<ushort> GetDevices(this IEnumerable<LedRenderBus> collections)
        {
            return (from renderBus in collections
                    from ledStrip in renderBus.LedStrips
                    select ledStrip.Address).Distinct();
        }
        #endregion

        #region 灯带查询
        /// <summary>
        /// 按 UID 在集合中查找指定的 <see cref="LedStripObject"/>。
        /// </summary>
        /// <param name="collections">渲染总线集合。</param>
        /// <param name="uid">灯带唯一标识，计算公式：<c>(Port &lt;&lt; 16) | Address</c>。</param>
        /// <returns>找到的灯带实例，未找到返回 <c>null</c>。</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static LedStripObject GetLedStrip(this IEnumerable<LedRenderBus> collections, uint uid)
        {
            foreach (var bus in collections)
            {
                foreach (var strip in bus.LedStrips)
                {
                    if (strip.UID == uid)
                    {
                        return strip;
                    }
                }
            }
            return null;
        }
        /// <summary>
        /// 按设备地址和端口在集合中查找指定的 <see cref="LedStripObject"/>。
        /// </summary>
        /// <param name="collections">渲染总线集合。</param>
        /// <param name="address">设备地址，范围 [0, 4096]。</param>
        /// <param name="port">端口号，范围 [0, 6]。</param>
        /// <returns>找到的灯带实例，未找到返回 <c>null</c>。</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static LedStripObject GetLedStrip(this IEnumerable<LedRenderBus> collections, int address, int port)
        {
            foreach (var bus in collections)
            {
                foreach (var strip in bus.LedStrips)
                {
                    if (strip.Address == address && strip.Port == port)
                    {
                        return strip;
                    }
                }
            }
            return null;
        }
        /// <summary>
        /// 获取集合中所有总线上登记的全部灯带。
        /// </summary>
        /// <param name="collections">渲染总线集合。</param>
        /// <returns>所有灯带的扁平化集合。</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static IEnumerable<LedStripObject> GetLedStrips(this IEnumerable<LedRenderBus> collections)
        {
            return from renderBus in collections
                   from ledStrip in renderBus.LedStrips
                   select ledStrip;
        }
        #endregion

#if false
        #region 通道管理
        /// <summary>
        /// 打开集合中所有总线的通信通道。
        /// </summary>
        /// <param name="collections">渲染总线集合。</param>
        /// <remarks>单个通道打开失败不影响后续通道的操作，异常会记录到 Trace 日志。</remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void OpenChannel(this IEnumerable<LedRenderBus> collections)
        {
            foreach (var renderBus in collections)
            {
                try
                {
                    renderBus.OpenChannel();
                }
                catch (Exception ex)
                {
                    Trace.TraceError($"OpenChannel ({renderBus.Name}) Exception: {ex.Message}");
                }
            }
        }
        /// <summary>
        /// 关闭集合中所有总线的通信通道。
        /// </summary>
        /// <param name="collections">渲染总线集合。</param>
        /// <remarks>单个通道关闭失败不影响后续通道的操作，异常会记录到 Trace 日志。</remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void CloseChannel(this IEnumerable<LedRenderBus> collections)
        {
            foreach (var renderBus in collections)
            {
                try
                {
                    renderBus.CloseChannel();
                }
                catch (Exception ex)
                {
                    Trace.TraceError($"CloseChannel ({renderBus.Name}) Exception: {ex.Message}");
                }
            }
        }
        #endregion
#endif

        #region 渲染控制
        /// <summary>
        /// 启动集合中所有总线的渲染线程。
        /// </summary>
        /// <param name="collections">渲染总线集合。</param>
        /// <remarks>调用 <see cref="LedRenderBus.StartRender"/>，若已启动则静默返回。</remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void StartRender(this IEnumerable<LedRenderBus> collections)
        {
            foreach (var renderBus in collections)
            {
                renderBus.StartRender();
            }
        }
        /// <summary>
        /// 停止集合中所有总线的渲染线程。
        /// </summary>
        /// <param name="collections">渲染总线集合。</param>
        /// <remarks>调用 <see cref="LedRenderBus.StopRender"/>，等待线程退出并释放资源。</remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void StopRender(this IEnumerable<LedRenderBus> collections)
        {
            foreach (var renderBus in collections)
            {
                renderBus.StopRender();
            }
        }
        /// <summary>
        /// 暂停集合中所有总线上全部灯带的颜色帧渲染。
        /// </summary>
        /// <param name="collections">渲染总线集合。</param>
        /// <remarks>
        /// <para>实际调用 <c>PauseRender(0)</c>（address=0 表示所有设备），将每个总线下所有 <see cref="LedStripObject.IsRenderEnabled"/> 设为 <c>false</c>。</para>
        /// <para>暂停期间总线队列上的所有帧不受影响，灯带队列上的颜色帧将被跳过。</para>
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void PauseRender(this IEnumerable<LedRenderBus> collections)
        {
            foreach (var renderBus in collections)
            {
                renderBus.PauseRender(0);
            }
        }
        /// <summary>
        /// 恢复集合中所有总线上全部灯带的颜色帧渲染。
        /// </summary>
        /// <param name="collections">渲染总线集合。</param>
        /// <remarks>实际调用 <c>ResumeRender(0)</c>（address=0 表示所有设备），将每个总线下所有 <see cref="LedStripObject.IsRenderEnabled"/> 设为 <c>true</c>。</remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ResumeRender(this IEnumerable<LedRenderBus> collections)
        {
            foreach (var renderBus in collections)
            {
                renderBus.ResumeRender(0);
            }
        }
        /// <summary>
        /// 清空集合中所有总线的灯带渲染队列，并可选择发送全黑帧关闭灯带。
        /// </summary>
        /// <param name="collections">渲染总线集合。</param>
        /// <param name="turnOff">清空后是否发送全黑帧关闭所有灯带显示，该帧会进入总线渲染队列。</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ClearRender(this IEnumerable<LedRenderBus> collections, bool turnOff)
        {
            foreach (var renderBus in collections)
            {
                renderBus.ClearRender(0, turnOff);
            }
        }
        #endregion

    }
}
