using System;
using System.Collections.Generic;
using System.Text;
using SpaceCG.Extensions;
using System.Threading;

namespace SpaceCG.Device
{
    public sealed partial class LedRenderBus : IDisposable
    {

        #region Static Collections 静态集合
        /// <summary>
        /// 所有渲染总线的集合
        /// </summary>
        public static IReadOnlyList<LedRenderBus> Collections
        {
            get
            {
                if (BusCollectionsReadOnly == null)
                    BusCollectionsReadOnly = BusCollections.AsReadOnly();
                return BusCollectionsReadOnly;
            }
        }

        internal static Timer FpsTimer;
        private static volatile int checkTick = 0;
        private static readonly List<LedRenderBus> BusCollections;
        private static IReadOnlyList<LedRenderBus> BusCollectionsReadOnly;

        static LedRenderBus()
        {
            BusCollections = new List<LedRenderBus>(32);
            FpsTimer = new Timer(OnTimerCallback, null, 300, 1000);
        }
        private static void OnTimerCallback(object state)
        {
            // 计时帧率计算法
            foreach (var bus in BusCollections)
            {
                foreach (var ledStrip in bus.LedStrips.Values)
                {
                    //ledStrip.ResetRenderFps();
                }
                //bus.Fps = Interlocked.Exchange(ref bus._renderFps, 0);
            }

            checkTick++;
            if (checkTick > 3)
            {
                checkTick = 0;
                BusCollections.CheckChannelConnection();
            }
        }
        #endregion

    }
}
