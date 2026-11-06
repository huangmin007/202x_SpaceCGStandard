using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using SharpDX.Direct3D9;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;

namespace SpaceCG.Extensions
{
    /// <summary>
    /// System.Drawing.Bitmap 扩展方法
    /// </summary>
    public static class BitmapExtensions
    {
#if true
        /// <summary>
        /// 将 <paramref name="bitmap"/> 数据复制到 <paramref name="surface"/> 对象中
        /// </summary>
        /// <param name="bitmap"></param>
        /// <param name="surface"></param>
        /// <returns></returns>
        public static bool CopyTo(this System.Drawing.Bitmap bitmap, Surface surface)
        {
            if (bitmap == null || surface == null || surface.NativePointer == IntPtr.Zero) return false;

            var description = surface.Description;
            if (description.Format != Format.A8R8G8B8 && description.Format != Format.X8R8G8B8) // description.Usage = Usage.Dynamic
            {
                Trace.TraceWarning($"Surface Format({Enum.GetName(typeof(Format), description.Format)}) 与 Bitmap Format(Format32bppArgb) 不匹配。");
                return false;
            }
            if (description.Width < bitmap.Width || description.Height < bitmap.Height)
            {
                Trace.TraceWarning($"位图尺寸({bitmap.Width}x{bitmap.Height}) 大于 Surface 尺寸({description.Width}x{description.Height}) 会存在部份不可见的区域。");
            }

            var surfaceData = surface.LockRectangle(LockFlags.Discard);
            var bitmapData = bitmap.LockBits(new System.Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height),
                System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);

            /*
             * GDI+ 的 stride 默认对齐到 4、8、16 字节
             * D3D9 的 pitch 默认对齐到 32、64 字节(与显卡相关)
             * 先检查纹理内存与位图内存是否是对齐的，如果对齐的，就按块复制内存，否则按行复制
             * 注意 Alpha 的问题，Premultiplied 格式的 Alpha D3D9 是支持的；Straight 格式的 Alpha D3D9 不支持，会是白色背景。
             */
            try
            {
                unsafe
                {
#if false
                    // 把 Bitmap 转成 Premultiplied Alpha 再拷贝，有些带Alpha 的格式是 Straight Alpha ,,, 使用 D3D9 PixelShader 着色器处理了，OK
                    byte* p = (byte*)bitmapData.Scan0;
                    for (int y = 0; y < bitmap.Height; y++)
                    {
                        byte* row = p + y * bitmapData.Stride;
                        for (int x = 0; x < bitmap.Width; x++)
                        {
                            byte a = row[x * 4 + 3];
                            row[x * 4 + 0] = (byte)(row[x * 4 + 0] * a / 255); // B
                            row[x * 4 + 1] = (byte)(row[x * 4 + 1] * a / 255); // G
                            row[x * 4 + 2] = (byte)(row[x * 4 + 2] * a / 255); // R
                        }
                    }
#endif

                    var rowBytes = bitmap.Width * 4;
                    if (bitmapData.Stride == rowBytes && surfaceData.Pitch == rowBytes)
                    {
                        var blockBytes = rowBytes * bitmap.Height;
                        Buffer.MemoryCopy((void*)bitmapData.Scan0, (void*)surfaceData.DataPointer, blockBytes, blockBytes);
                    }
                    else
                    {
                        byte* src = (byte*)bitmapData.Scan0;
                        byte* dst = (byte*)surfaceData.DataPointer;

                        for (var y = 0; y < bitmap.Height; y++)
                        {
                            Buffer.MemoryCopy(src + y * bitmapData.Stride, dst + y * surfaceData.Pitch, rowBytes, rowBytes);
                        }
                    }
                }

                surface.Tag = bitmap.Tag;
                return true;
            }
            catch (Exception ex)
            {
                Trace.TraceWarning($"Bitmap CopyTo Exception: {ex.Message}");
            }
            finally
            {
                surface.UnlockRectangle();
                bitmap.UnlockBits(bitmapData);
            }

            return false;
        }

        /// <summary>
        /// 将 <paramref name="bitmap"/> 数据复制到 <paramref name="texture"/> 对象中
        /// </summary>
        /// <param name="bitmap"></param>
        /// <param name="texture"></param>
        /// <exception cref="ArgumentNullException"></exception>
        public static bool CopyTo(this System.Drawing.Bitmap bitmap, Texture texture)
        {
            using (var surface = texture.GetSurfaceLevel(0))
            {
                return CopyTo(bitmap, surface);
            }
        }
#endif

        /// <summary>
        /// 将 Bitmap 对象转换为 BitmapSource 对象
        /// </summary>
        /// <param name="bitmap"></param>
        /// <returns></returns>
        public static BitmapSource ToBitmapSource(this System.Drawing.Bitmap bitmap)
        {
            if (bitmap == null) return null;

            var bitmapData = bitmap.LockBits(new System.Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height),
                            System.Drawing.Imaging.ImageLockMode.ReadOnly,
                            System.Drawing.Imaging.PixelFormat.Format32bppArgb);

            try
            {
                var source = BitmapSource.Create(
                    bitmap.Width,
                    bitmap.Height,
                    96, 96,
                    System.Windows.Media.PixelFormats.Bgra32,
                    null,
                    bitmapData.Scan0,
                    bitmapData.Stride * bitmapData.Height,
                    bitmapData.Stride);

                source.Freeze(); // 允许跨线程使用
                return source;
            }
            finally
            {
                bitmap.UnlockBits(bitmapData);
            }
        }

        /// <summary>
        /// 将 Bitmap 对象转换为 BitmapSource 对象
        /// </summary>
        /// <param name="bitmap"></param>
        /// <returns></returns>
        public static BitmapSource ToBitmapSource2(this System.Drawing.Bitmap bitmap)
        {
            if (bitmap == null) return null;

            var hBitmap = bitmap.GetHbitmap();

            try
            {
                return System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(hBitmap,
                    IntPtr.Zero,
                    System.Windows.Int32Rect.Empty,
                    BitmapSizeOptions.FromWidthAndHeight(bitmap.Width, bitmap.Height));
            }
            finally
            {
                DeleteObject(hBitmap);
            }
        }

        [System.Runtime.InteropServices.DllImport("Gdi32.dll")]
        private static extern bool DeleteObject(System.IntPtr hObject);
    }

}
