using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace SpaceCG.Device
{
    /// <summary>
    /// 颜色格式的扩展方法
    /// </summary>
    public static class ColorFormatExtensions
    {
        /// <summary>
        /// 颜色格式对应的通道索引表
        /// </summary>
        public static readonly IReadOnlyDictionary<ColorFormat, byte[]> ColorChannelIndices = new Dictionary<ColorFormat, byte[]>
        {
            { ColorFormat.RGB, new byte[] { (byte)ColorChannel.R, (byte)ColorChannel.G, (byte)ColorChannel.B } },
            { ColorFormat.RBG, new byte[] { (byte)ColorChannel.R, (byte)ColorChannel.B, (byte)ColorChannel.G } },
            { ColorFormat.GRB, new byte[] { (byte)ColorChannel.G, (byte)ColorChannel.R, (byte)ColorChannel.B } },
            { ColorFormat.GBR, new byte[] { (byte)ColorChannel.G, (byte)ColorChannel.B, (byte)ColorChannel.R } },
            { ColorFormat.BRG, new byte[] { (byte)ColorChannel.B, (byte)ColorChannel.R, (byte)ColorChannel.G } },
            { ColorFormat.BGR, new byte[] { (byte)ColorChannel.B, (byte)ColorChannel.G, (byte)ColorChannel.R } },

            { ColorFormat.RGBA, new byte[] { (byte)ColorChannel.R, (byte)ColorChannel.G, (byte)ColorChannel.B, (byte)ColorChannel.A } },
            { ColorFormat.BGRA, new byte[] { (byte)ColorChannel.B, (byte)ColorChannel.G, (byte)ColorChannel.R, (byte)ColorChannel.A } },
            { ColorFormat.ARGB, new byte[] { (byte)ColorChannel.A, (byte)ColorChannel.R, (byte)ColorChannel.G, (byte)ColorChannel.B } },
            { ColorFormat.ABGR, new byte[] { (byte)ColorChannel.A, (byte)ColorChannel.B, (byte)ColorChannel.G, (byte)ColorChannel.R } },

            { ColorFormat.RGBW, new byte[] { (byte)ColorChannel.R, (byte)ColorChannel.G, (byte)ColorChannel.B, (byte)ColorChannel.A } },
            { ColorFormat.WRGB, new byte[] { (byte)ColorChannel.A, (byte)ColorChannel.R, (byte)ColorChannel.G, (byte)ColorChannel.B } },
        };

        /// <summary>
        /// 获取颜色格式对应的通道数量
        /// <para>如果格式不支持 Alpha 通道，则返回 3，否则返回 4</para>
        /// </summary>
        /// <param name="format">颜色格式</param>
        /// <returns>通道数量，三通道格式返回 3，四通道格式返回 4</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int GetChannelCount(this ColorFormat format) => ColorChannelIndices[format].Length;

        /// <summary>
        /// 获取颜色格式支持的最大 LED 灯珠数量
        /// <para>三通道格式最大支持 1024 颗灯珠，四通道格式最大支持 768 颗灯珠</para>
        /// </summary>
        /// <param name="format">颜色格式</param>
        /// <returns>最大 LED 灯珠数量</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int GetMaxLedCount(this ColorFormat format) => ColorChannelIndices[format].Length == 3 ? 1024 : 768;

        /// <summary>
        /// 获取颜色格式对应的通道索引表
        /// <para>返回的数组的索引对应于 <see cref="ColorChannel"/> 枚举</para>
        /// </summary>
        /// <param name="format">颜色格式</param>
        /// <returns>通道索引表，数组元素为 <see cref="ColorChannel"/> 枚举值</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static byte[] GetChannelIndices(this ColorFormat format) => ColorChannelIndices[format];
        

        /// <summary>
        /// 转换颜色数据的格式（分配新数组返回结果）
        /// <para>如果输入格式不带 Alpha 通道，输出格式带 Alpha 通道，则参数 <paramref name="outputAlphaValue"/> 的值有效 </para>
        /// </summary>
        /// <param name="inputColors">输入颜色数据，每个像素按 <paramref name="inputFormat"/> 排列的连续字节序列</param>
        /// <param name="inputFormat">输入颜色数据的格式</param>
        /// <param name="outputFormat">输出颜色数据的格式</param>
        /// <param name="outputAlphaValue">如果输出格式中包括 Alpha 通道，需要指定 Alpha 通道的值，默认为 0xFF</param>
        /// <returns>转换后的颜色数据字节数组</returns>
        /// <exception cref="ArgumentNullException">当 <paramref name="inputColors"/> 为 null 或空时抛出</exception>
        /// <exception cref="ArgumentException">当输入数据长度不足或与格式不匹配时抛出</exception>
        public static byte[] ConvertColor(IReadOnlyList<byte> inputColors, ColorFormat inputFormat, ColorFormat outputFormat, byte outputAlphaValue = 0xFF)
        {
            if (inputColors == null || inputColors.Count == 0)
                throw new ArgumentNullException("输入颜色数据为空");

            // 通道索引表
            var inputIndices = ColorChannelIndices[inputFormat];
            int inputChannelCount = inputIndices.Length;

            if (inputColors.Count < inputChannelCount || inputColors.Count % inputChannelCount != 0)
                throw new ArgumentException("输入颜色数据格式无效");

            if (inputFormat == outputFormat) return inputColors.ToArray();

            var outputIndices = ColorChannelIndices[outputFormat];
            int outputChannelCount = outputIndices.Length;

            // 计算输出的像素数量
            int pixelCount = inputColors.Count / inputChannelCount;
            byte[] results = new byte[pixelCount * outputChannelCount];

            ConvertColor(inputColors, inputFormat, ref results, 0, outputFormat, outputAlphaValue);

            return results;

        }

        /// <summary>
        /// 转换颜色数据的格式（写入引用数组，避免内存分配）
        /// <para>当输入格式与输出格式相同时，使用 <see cref="Array.Copy(Array, int, Array, int, int)"/> 快速复制</para>
        /// </summary>
        /// <param name="inputColors">输入颜色数据</param>
        /// <param name="inputFormat">输入颜色数据的格式</param>
        /// <param name="outputColors">为了减少内存分配，可以指定输出颜色数据的数组</param>
        /// <param name="outputOffset"><paramref name="outputColors"/> 输出的起始偏移量</param>
        /// <param name="outputFormat">输出颜色数据的格式</param>
        /// <param name="outputAlphaValue">如果输出格式中包括 Alpha 通道，需要指定 Alpha 通道的值，默认为 0xFF</param>
        /// <exception cref="ArgumentException">当输入/输出数据为空、长度不足或格式无效时抛出</exception>
        public static void ConvertColor(IReadOnlyList<byte> inputColors, ColorFormat inputFormat, ref byte[] outputColors, int outputOffset, ColorFormat outputFormat, byte outputAlphaValue = 0xFF)
        {
            if (inputColors == null || inputColors.Count == 0)
                throw new ArgumentException("输入颜色数据为空");
            if (outputColors == null || outputColors.Length == 0 || outputColors.Length < outputOffset + inputColors.Count)
                throw new ArgumentException("输出颜色数据为空或长度不足，小于 outputOffset + inputColors 长度");

            // 通道索引表
            var inputIndices = ColorChannelIndices[inputFormat];
            var outputIndices = ColorChannelIndices[outputFormat];

            // 颜色的通道数量
            int inputChannelCount = inputIndices.Length;
            int outputChannelCount = outputIndices.Length;

            if (inputColors.Count < inputChannelCount || inputColors.Count % inputChannelCount != 0)
                throw new ArgumentException("输入颜色数据格式无效");

            if (inputFormat == outputFormat && inputColors is Array inputColorsArray)
            {
                Array.Copy(inputColorsArray, 0, outputColors, outputOffset, inputColors.Count);
                return;
            }

            // 计算输出的像素数量
            int pixelCount = inputColors.Count / inputChannelCount;
            
            int i = 0, j = 0, index = -1;
            int _inputOffset = 0, _outputOffset = 0;

            // 预计算索引映射，如果存在 -1, 则表示需要补充 Alpha 通道
            int[] channelMap = new int[outputChannelCount];
            for (i = 0; i < outputChannelCount; i++)
            {
                channelMap[i] = Array.IndexOf(inputIndices, outputIndices[i]);
            }

            for (i = 0; i < pixelCount; i++)
            {
                _inputOffset = i * inputChannelCount;
                _outputOffset = i * outputChannelCount + outputOffset;

                for (j = 0; j < outputChannelCount; j++)
                {
                    index = channelMap[j];
                    outputColors[_outputOffset + j] = (index >= 0) ? inputColors[_inputOffset + index] : outputAlphaValue;
                }
            }
        }

        /// <summary>
        /// 转换颜色数据的格式（uint 像素输入，分配新数组返回结果）
        /// </summary>
        /// <param name="inputColors">带 Alpha 通道的颜色数据，每个元素为一个 uint 像素值</param>
        /// <param name="inputFormat">输入颜色数据的格式，必须为四通道的颜色格式</param>
        /// <param name="outputFormat">输出颜色数据的格式</param>
        /// <returns>转换后的颜色数据字节数组</returns>
        /// <exception cref="ArgumentNullException">当 <paramref name="inputColors"/> 为 null 或空时抛出</exception>
        /// <exception cref="ArgumentException">当输入格式不是四通道格式时抛出</exception>
        public static byte[] ConvertColor(IReadOnlyList<uint> inputColors, ColorFormat inputFormat, ColorFormat outputFormat)
        {
            if (inputColors == null || inputColors.Count == 0)
                throw new ArgumentNullException("输入颜色数据为空");

            var inputIndices = ColorChannelIndices[inputFormat];
            int inputChannelCount = inputIndices.Length;

            if (inputChannelCount != 4)
                throw new ArgumentException("输入颜色数据格式无效，输入格式必须为四通道");

            var outputIndices = ColorChannelIndices[outputFormat];
            int outputChannelCount = outputIndices.Length;

            // 计算输出的像素数量
            int pixelCount = inputColors.Count;
            byte[] results = new byte[pixelCount * outputChannelCount];

            ConvertColor(inputColors, inputFormat, ref results, 0, outputFormat);

            return results;
        }

        /// <summary>
        /// 转换颜色数据的格式（uint 像素输入，写入引用数组）
        /// </summary>
        /// <param name="inputColors">带 Alpha 通道的颜色数据，每个元素为一个 uint 像素值</param>
        /// <param name="inputFormat">输入颜色数据的格式，必须为四通道的颜色格式</param>
        /// <param name="outputColors">输出颜色数据的数组</param>
        /// <param name="outputOffset"><paramref name="outputColors"/> 输出的起始偏移量</param>
        /// <param name="outputFormat">输出颜色数据的格式</param>
        /// <exception cref="ArgumentException">当输入/输出数据为空、长度不足或格式无效时抛出</exception>
        public static void ConvertColor(IReadOnlyList<uint> inputColors, ColorFormat inputFormat, ref byte[] outputColors, int outputOffset, ColorFormat outputFormat)
        {
            if (inputColors == null || inputColors.Count == 0)
                throw new ArgumentException("输入颜色数据为空");
            if (outputColors == null || outputColors.Length == 0 || outputColors.Length < outputOffset + inputColors.Count * 4)
                throw new ArgumentException("输出颜色数据为空或长度不足，小于 outputOffset + inputColors 长度");

            // 通道索引表
            var inputIndices = ColorChannelIndices[inputFormat];
            var outputIndices = ColorChannelIndices[outputFormat];

            // 颜色的通道数量
            int inputChannelCount = inputIndices.Length;
            int outputChannelCount = outputIndices.Length;

            if (inputChannelCount != 4)
                throw new ArgumentException("输入颜色数据格式无效，输入格式必须为四通道");

            // 计算输出的像素数量
            int pixelCount = inputColors.Count;

            int i = 0, j = 0, index = -1, _outputOffset = 0;

            // 预计算索引映射，如果存在 -1, 则表示需要补充 Alpha 通道
            int[] channelMap = new int[outputChannelCount];
            for (i = 0; i < outputChannelCount; i++)
            {
                channelMap[i] = Array.IndexOf(inputIndices, outputIndices[i]);
            }

            for (i = 0; i < pixelCount; i++)
            {
                _outputOffset = i * outputChannelCount + outputOffset;

                for (j = 0; j < outputChannelCount; j++)
                {
                    index = channelMap[j];
                    outputColors[_outputOffset + j] = (byte)((inputColors[i] >> (24 - index * 8)) & 0xFF);
                }
            }
        }

        /// <summary>
        /// 转换颜色数据的格式（unsafe 指针版本，直接从内存中读取像素数据）
        /// <para>使用指针直接操作内存，适用于高性能场景</para>
        /// </summary>
        /// <param name="pixels">输入像素数据的指针</param>
        /// <param name="pixelFormat">输入像素数据的格式</param>
        /// <param name="width">输入像素的宽度</param>
        /// <param name="height">输入像素的高度</param>
        /// <param name="stride">输入像素数据的步长（每行字节数）</param>
        /// <param name="outputFormat">输出像素数据的格式</param>
        /// <param name="outputRectangle">输出像素的范围区域（相对输入像素数据）</param>
        /// <param name="outputAlphaValue">输出像素数据的 Alpha 通道值，如果输入没有 Alpha 通道，则使用该值填充</param>
        /// <returns>转换后的像素数据字节数组，按行优先排列</returns>
        /// <exception cref="ArgumentException">当像素数据为空、尺寸无效、stride 不足或输出区域越界时抛出</exception>
        public static unsafe byte[] ConvertColor(byte* pixels, ColorFormat pixelFormat, int width, int height, int stride, ColorFormat outputFormat, System.Drawing.Rectangle outputRectangle, byte outputAlphaValue = 0xFF)
        {
            if (pixels == null || width <= 0 || height <= 0)
                throw new ArgumentException("像素数据为空或宽度或高度小于等于 0");

            var inputIndices = ColorChannelIndices[pixelFormat];     // 输入像素排列的通道索引表            
            var inputChannelCount = inputIndices.Length;            // 颜色的通道数量

            if (stride < width * inputChannelCount)
                throw new ArgumentException("stride 必须大于等于 width * inputChannelCount");

            var inputRectangle = new System.Drawing.Rectangle(0, 0, width, height);
            if (outputRectangle.X + outputRectangle.Width > width || outputRectangle.Y + outputRectangle.Height > height || inputRectangle.Contains(outputRectangle) == false)
                throw new ArgumentException($"指定的输出区域 {outputRectangle} 超出了图像输入边界 {inputRectangle}");

            // 输出像素排列的通道索引表
            var outputIndices = ColorChannelIndices[outputFormat];            
            var outputChannelCount = outputIndices.Length;
            byte[] results = new byte[outputRectangle.Width * outputRectangle.Height * outputChannelCount];

            // 预计算索引映射，如果存在 -1, 则表示需要补充 Alpha 通道
            int[] channelMap = new int[outputChannelCount];
            for (var i = 0; i < outputChannelCount; i++)
            {
                channelMap[i] = Array.IndexOf(inputIndices, outputIndices[i]);
            }

            var frameOffset = 0;
            for (var y = outputRectangle.Y; y < outputRectangle.Height + outputRectangle.Y; y++)
            {
                for (var x = outputRectangle.X; x < outputRectangle.Width + outputRectangle.X; x++)
                {
                    byte* pixelOffset = pixels + y * stride + x * inputChannelCount;

                    for (int j = 0; j < outputChannelCount; j++)
                    {
                        var index = channelMap[j];
                        byte* pixel = pixelOffset + index;
                        results[frameOffset++] = (index >= 0) ? *pixel : outputAlphaValue;
                    }
                }
            }

            return results;
        }

        /// <inheritdoc cref="ConvertColor(byte*, ColorFormat, int, int, int, ColorFormat, System.Drawing.Rectangle, byte)"/>
        public static unsafe byte[] ConvertColor(IntPtr pixels, ColorFormat pixelFormat, int width, int height, int stride, ColorFormat outputFormat, System.Drawing.Rectangle outputRectangle, byte outputAlphaValue = 0xFF)
            => ConvertColor((byte*)pixels, pixelFormat, width, height, stride, outputFormat, outputRectangle, outputAlphaValue);
    }

}
