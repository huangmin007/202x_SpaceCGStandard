using System;
using System.Collections.Generic;
using System.Linq;
using SpaceCG.Device;
using Trace = SpaceCG.Diagnostics.Trace;

namespace SpaceCG.Extensions
{
    /// <summary>
    /// <see cref="LedRenderBus"/> 集合扩展方法
    /// </summary>
    public static partial class LedRenderBusExtensions
    {        
        /// <summary>
        /// 释放 <see cref="LedRenderBus"/> 集合资源
        /// </summary>
        /// <param name="collections"></param>
        public static void Dispose(this IEnumerable<LedRenderBus> collections)
        {
            while (collections.Count() > 0)
            {
                collections.ElementAt(0).Dispose();
            }
        }
        
        /// <summary>
        /// 从 <see cref="LedRenderBus"/> 集合资源中获取所有登记的 Led 设备地址的集合
        /// </summary>
        /// <param name="collections"></param>
        /// <returns>返回非重复的设备地址的集合</returns>
        public static IEnumerable<ushort> GetLedDevices(this IEnumerable<LedRenderBus> collections)
        {
            var addresses = from renderBus in collections
                            from ledStrip in renderBus.LedStrips.Values
                            select ledStrip.Address;

            return addresses.Distinct();
        }

        #region LedStripObject
        /// <summary>
        /// 从 <see cref="LedRenderBus"/> 集合资源中获取指定 UID 的 <see cref="LedStripObject"/> 对象
        /// </summary>
        /// <param name="collections"></param>
        /// <param name="uid"></param>
        /// <returns></returns>
        public static LedStripObject GetLedStrip(this IEnumerable<LedRenderBus> collections, uint uid)
        {
            foreach (var bus in collections)
            {
                foreach (var strip in bus.LedStrips.Values)
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
        /// 从 <see cref="LedRenderBus"/> 集合资源中获取指定地址和端口的 <see cref="LedStripObject"/> 对象
        /// </summary>
        /// <param name="collections"></param>
        /// <param name="address"></param>
        /// <param name="port"></param>
        /// <returns></returns>
        public static LedStripObject GetLedStrip(this IEnumerable<LedRenderBus> collections, int address, int port)
        {
            foreach (var bus in collections)
            {
                foreach (var strip in bus.LedStrips.Values)
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
        /// 从 <see cref="LedRenderBus"/> 集合资源中获取所有登记的 <see cref="LedStripObject"/> 对象的集合
        /// </summary>
        /// <param name="collections"></param>
        /// <returns></returns>
        public static IEnumerable<LedStripObject> GetLedStrips(this IEnumerable<LedRenderBus> collections)
        {
            return from renderBus in collections
                   from ledStrip in renderBus.LedStrips.Values
                   select ledStrip;
        }
        #endregion

        /// <summary>
        /// 打开 <see cref="LedRenderBus"/> 集合资源中的所有 <see cref="LedRenderBus"/> 对象的通信通道
        /// </summary>
        /// <param name="collections"></param>
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
        /// 关闭 <see cref="LedRenderBus"/> 集合资源中的所有 <see cref="LedRenderBus"/> 对象的通信通道
        /// </summary>
        /// <param name="collections"></param>
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

        #region 渲染控制
        /// <summary>
        /// 将 <see cref="LedRenderBus"/> 集合资源中的所有 <see cref="LedRenderBus"/> 对象的启动渲染
        /// </summary>
        /// <param name="collections"></param>
        public static void StartRender(this IEnumerable<LedRenderBus> collections)
        {
            foreach (var renderBus in collections)
            {
                renderBus.StartRender();
            }
        }
        /// <summary>
        /// 将 <see cref="LedRenderBus"/> 集合资源中的所有 <see cref="LedRenderBus"/> 对象的停止渲染
        /// </summary>
        /// <param name="collections"></param>
        public static void StopRender(this IEnumerable<LedRenderBus> collections)
        {
            foreach (var renderBus in collections)
            {
                renderBus.StopRender();
            }
        }
        /// <summary>
        /// 将 <see cref="LedRenderBus"/> 集合资源中的所有 <see cref="LedStripObject"/> 对象的渲染暂停
        /// </summary>
        /// <param name="collections"></param>
        public static void PauseRender(this IEnumerable<LedRenderBus> collections)
        {
            foreach (var renderBus in collections)
            {
                renderBus.PauseRender(0);
            }
        }
        /// <summary>
        /// 将 <see cref="LedRenderBus"/> 集合资源中的所有 <see cref="LedStripObject"/> 对象的渲染恢复
        /// </summary>
        /// <param name="collections"></param>
        public static void ResumeRender(this IEnumerable<LedRenderBus> collections)
        {
            foreach (var renderBus in collections)
            {
                renderBus.ResumeRender(0);
            }
        }
        /// <summary>
        /// 对 <see cref="LedRenderBus"/> 集合资源中所有 <see cref="LedRenderBus"/> 对象清空待渲染数据
        /// </summary>
        /// <param name="collections"></param>
        /// <param name="off"></param>
        public static void ClearRender(this IEnumerable<LedRenderBus> collections, bool clear)
        {
            foreach (var renderBus in collections)
            {
                renderBus.ClearRender(0, clear);
            }
        }
        #endregion

        /// <summary>
        /// 检查 <see cref="LedRenderBus"/> 集合资源中所有 <see cref="LedRenderBus"/> 总线通道的连接状态
        /// </summary>
        /// <param name="collections"></param>
        public static void CheckChannelConnection(this IEnumerable<LedRenderBus> collections)
        {            
            foreach (var ledRenderBus in collections)
            {
                try
                {
                    if (!ledRenderBus.IsConnected)
                    {
                        Trace.TraceInformation($"LedRenderBus [{ledRenderBus.Name}] 通信连接异常，重新连接中 ......");
                        ledRenderBus.CloseChannel();
                        ledRenderBus.OpenChannel();
                    }
                }
                catch (Exception ex)
                {
                    Trace.TraceError($"LedRenderBus [{ledRenderBus.Name}] Exception: {ex.Message}");
                }
            }
        }

    }
}
