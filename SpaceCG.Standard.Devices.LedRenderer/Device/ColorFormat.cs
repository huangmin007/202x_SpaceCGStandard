using System;

namespace SpaceCG.Device
{
    /// <summary>
    /// 颜色通道
    /// </summary>
    public enum ColorChannel : byte
    {
        /// <summary>  红色通道  </summary>
        R = 0x00,
        /// <summary>  绿色通道  </summary>
        G = 0x01,
        /// <summary>  蓝色通道  </summary>
        B = 0x02,
        /// <summary>  Alpha 通道  </summary>
        A = 0x03,
    }

    /// <summary>
    /// 颜色/像素格式
    /// <para>颜色在内存中存储的顺序</para>
    /// <para>例如：<see cref="RGB"/> 存储顺序为：RGBRGBRGB...，<see cref="BGR"/> 存储顺序为：BGRBGRBGR...，<see cref="ARGB"/> 存储顺序为：ARGBARGBARGB... </para>
    /// </summary>
    public enum ColorFormat : uint
    {
        // 3通道模式
        /// <summary> 3 通道 24 位 RGB 格式  </summary>
        RGB,
        /// <summary> 3 通道 24 位 RBG 格式  </summary>
        RBG,
        /// <summary> 3 通道 24 位 GRB 格式  </summary>
        GRB,
        /// <summary> 3 通道 24 位 GBR 格式  </summary>
        GBR,
        /// <summary> 3 通道 24 位 BRG 格式  </summary>
        BRG,
        /// <summary> 3 通道 24 位 BGR 格式  </summary>
        BGR,

        // 4通道模式
        /// <summary> 4 通道 32 位 RGBA 格式  </summary>
        RGBA,
        /// <summary> 4 通道 32 位 BGRA 格式  </summary>
        BGRA,
        /// <summary> 4 通道 32 位 ARGB 格式  </summary>
        ARGB,
        /// <summary> 4 通道 32 位 ABGR 格式  </summary>
        ABGR,

        /// <summary> 4 通道 32 位 灯带的颜色格式，与 <see cref="RGBA"/> 相同  </summary>
        RGBW, // = RGBA,
        /// <summary> 4 通道 32 位 灯带的颜色格式，与 <see cref="ARGB"/> 相同  </summary>
        WRGB, // = ARGB,
    }

    /// <summary>
    /// Led 灯珠芯片型号
    /// </summary>
    public enum LedType : byte
    {
        /// <summary> 未知类型 </summary>
        UNKNOWN = 0x00,
        /// <summary> WS2812_RGB </summary>
        WS2812B = 0x01,
        /// <summary> WS2811_RGB </summary>
        WS2811,
        /// <summary> WS2813B_RGB </summary>
        WS2813B,
        /// <summary> SK6812_RGBW </summary>
        SK6812_RGBW = 0x04,
        /// <summary> SK6812_RGB </summary>
        SK6812_RGB = 0x05,
        /// <summary> WS2818B_RGB </summary>
        WS2818B = 0x06,
        /// <summary> SM16703P_RGB </summary>
        SM16703P,
        /// <summary> WS2815_RGB </summary>
        WS2815,
        /// <summary> SK9822_RGBW </summary>
        SK9822,
        /// <summary> DMX512_RGB </summary>
        DMX512_RGB,
        /// <summary> DMX512_RGBW </summary>
        DMX512_RGBW,
        ///<summary> GS8208_RGB </summary>
        GS8208,
        ///<summary> UCS1903_RGB </summary>
        UCS1903,
        ///<summary> MT1815_RGB </summary>
        MT1815,
        ///<summary> TM1913_RGB </summary>
        TM1913,
        ///<summary> TM1914_RGB </summary>
        TM1914A
    }

}
