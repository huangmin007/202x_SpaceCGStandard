using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO.Ports;
using System.Linq;

namespace SpaceCG.Device
{
    /// <summary>
    /// <see cref="LedRenderBus"/> 集合扩展方法
    /// </summary>
    public static class LedRenderBusExtensions
    {
        /// <summary>
        /// 释放 <see cref="LedRenderBus"/> 集合资源
        /// </summary>
        /// <param name="collections"></param>
        public static void Dispose(this IEnumerable<LedRenderBus> collections)
        {
            LedRenderBus.FpsTimer?.Dispose();
            LedRenderBus.FpsTimer = null;

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

        /// <summary>
        /// 从 <see cref="LedRenderBus"/> 集合资源中获取所有登记的 <see cref="LedStripObject"/> 对象的集合
        /// </summary>
        /// <param name="collections"></param>
        /// <returns></returns>
        public static IEnumerable<LedStripObject> GetLedStrips(this IEnumerable<LedRenderBus> collections)
        {
            var ledStrips = from renderBus in collections
                            from ledStrip in renderBus.LedStrips.Values
                            select ledStrip;

            return ledStrips;
        }

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
        /// <param name="off"></param>
        public static void PauseRender(this IEnumerable<LedRenderBus> collections, bool off = true)
        {
            foreach (var renderBus in collections)
            {
                foreach(var ledStrip in renderBus.LedStrips.Values)
                {
                    ledStrip.UseBitmapPixels = false;
                    ledStrip.ClearFrames(off);
                }
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
                foreach (var ledStrip in renderBus.LedStrips.Values)
                {
                    ledStrip.UseBitmapPixels = true;
                }
            }
        }

        /// <summary>
        /// 对 <see cref="LedRenderBus"/> 集合资源中所有 <see cref="LedRenderBus"/> 对象清空待渲染数据
        /// </summary>
        /// <param name="collections"></param>
        /// <param name="off"></param>
        public static void ClearRender(this IEnumerable<LedRenderBus> collections, bool off)
        {
            foreach (var renderBus in collections)
            {
                renderBus.ClearRender(0, off);
            }
        }

        /// <summary>
        /// 检查 <see cref="LedRenderBus"/> 集合资源中所有 <see cref="LedRenderBus"/> 对象的连接状态
        /// </summary>
        /// <param name="collections"></param>
        public static void CheckConnection(this IEnumerable<LedRenderBus> collections)
        {
            if (!SerialPort.GetPortNames().Any())
            {
                Trace.TraceWarning($"没有检测到串口设备......");
                return;
            }

            foreach (var ledRenderBus in collections)
            {
                try
                {
                    if (!ledRenderBus.IsConnected)
                    {
                        Trace.TraceInformation($"Reconnect....{ledRenderBus.Name}");
                        ledRenderBus.StopRender();
                        ledRenderBus.StartRender();
                    }
                }
                catch (Exception ex)
                {
                    Trace.TraceError($"StartRender ({ledRenderBus.Name}) Exception: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// 设置 <see cref="LedRenderBus"/> 集合资源中的所有设备上电时显示的颜色效果
        /// </summary>
        /// <param name="collections"></param>
        /// <param name="color"></param>
        /// <param name="isShow"></param>
        /// <param name="colorFormat"></param>
        public static void SetPowerOnColor(this IEnumerable<LedRenderBus> collections, uint color, bool isShow = true, ColorFormat colorFormat = ColorFormat.ARGB)
        {
            foreach (var renderBus in collections)
            {
                renderBus.SetPowerOnColor(0, color, isShow, colorFormat);
            }
        }

        /// <summary>
        /// 对 <see cref="LedRenderBus"/> 集合资源中所有 <see cref="LedRenderBus"/> 对象添加待渲染的颜色帧
        /// </summary>
        /// <param name="collections"></param>
        /// <param name="color"></param>
        /// <param name="colorFormat"></param>
        public static void AddColorFrame(this IEnumerable<LedRenderBus> collections, uint color, ColorFormat colorFormat = ColorFormat.ARGB)
        {
            foreach (var renderBus in collections)
            {
                renderBus.AddColorFrame(0, color, renderBus.MaxLedCount, colorFormat);
            }
        }

        /// <summary>
        /// 对 <see cref="LedRenderBus"/> 集合资源中所有 <see cref="LedRenderBus"/> 对象添加待渲染的颜色帧
        /// </summary>
        /// <param name="collections"></param>
        /// <param name="colors"></param>
        /// <param name="colorFormat"></param>
        public static void AddColorFrame(this IEnumerable<LedRenderBus> collections, uint[] colors, ColorFormat colorFormat = ColorFormat.ARGB)
        {
            foreach (var renderBus in collections)
            {
                renderBus.AddColorFrame(0, colors, renderBus.MaxLedCount, colorFormat);
            }
        }

        /// <summary>
        /// 对 <see cref="LedRenderBus"/> 集合资源中所有 <see cref="LedRenderBus"/> 对象添加待渲染的颜色帧
        /// </summary>
        /// <param name="collections"></param>
        /// <param name="colors"></param>
        /// <param name="colorFormat"></param>
        public static void AddColorFrame(this IEnumerable<LedRenderBus> collections, byte[] colors, ColorFormat colorFormat = ColorFormat.RGB)
        {
            foreach (var renderBus in collections)
            {
                renderBus.AddColorFrame(0, colors, renderBus.MaxLedCount, colorFormat);
            }
        }

        /// <summary>
        /// 对 <see cref="LedRenderBus"/> 集合资源中对指定的 <see cref="LedStripObject"/> 对象渲染颜色
        /// </summary>
        /// <param name="collections"></param>
        /// <param name="uids"></param>
        /// <param name="color"></param>
        /// <param name="colorFormat"></param>
        public static void AddColorFrame(this IEnumerable<LedRenderBus> collections, IEnumerable<uint> uids, uint color, ColorFormat colorFormat = ColorFormat.ARGB)
        {
            foreach (var renderBus in collections)
            {
                foreach (var kv in renderBus.LedStrips)
                {
                    if (uids.Contains(kv.Key))
                    {
                        var ledStrip = kv.Value;
                        ledStrip.AddColorFrame(color, ledStrip.LedCount, colorFormat);
                    }
                }
            }
        }
        /// <summary>
        /// 对 <see cref="LedRenderBus"/> 集合资源中对指定的 <see cref="LedStripObject"/> 对象渲染颜色
        /// </summary>
        /// <param name="collections"></param>
        /// <param name="uids"></param>
        /// <param name="colors"></param>
        /// <param name="colorFormat"></param>
        public static void AddColorFrame(this IEnumerable<LedRenderBus> collections, IEnumerable<uint> uids, IReadOnlyList<byte> colors, ColorFormat colorFormat = ColorFormat.ARGB)
        {
            foreach (var renderBus in collections)
            {
                foreach (var kv in renderBus.LedStrips)
                {
                    if (uids.Contains(kv.Key))
                    {
                        var ledStrip = kv.Value;
                        ledStrip.AddColorFrame(colors, ledStrip.LedCount, colorFormat);
                    }
                }
            }
        }
        /// <inheritdoc cref="AddColorFrame(IEnumerable{LedRenderBus}, IEnumerable{uint}, uint, ColorFormat)"/>
        public static void AddColorFrame(this IEnumerable<LedRenderBus> collections, IEnumerable<uint> uids, IReadOnlyList<uint> colors, ColorFormat colorFormat = ColorFormat.ARGB)
        {
            foreach (var renderBus in collections)
            {
                foreach (var kv in renderBus.LedStrips)
                {
                    if (uids.Contains(kv.Key))
                    {
                        var ledStrip = kv.Value;
                        ledStrip.AddColorFrame(colors, ledStrip.LedCount, colorFormat);
                    }
                }
            }
        }

    }
}
