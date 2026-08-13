using System;
using System.IO;
using System.Windows.Media.Imaging;
using Bitmap = System.Drawing.Bitmap;
using ImageFormat = System.Drawing.Imaging.ImageFormat;
using BitmapSource = System.Windows.Media.Imaging.BitmapSource;

namespace SpaceCG.Extensions
{
    
    /// <summary>
    /// System.Drawing 扩展方法
    /// </summary>
    public static partial class DrawingExtensions
    {        
        /// <summary>
        /// 将 <see cref="Bitmap"/> 转换为 <see cref="BitmapSource"/>
        /// </summary>
        /// <param name="bitmap"></param>
        /// <returns></returns>
        public static BitmapSource ToBitmapSource(this Bitmap bitmap)
        {
            if (bitmap == null) return null;

            using (var memoryStream = new MemoryStream())
            {
                bitmap.Save(memoryStream, ImageFormat.Bmp);
                memoryStream.Position = 0;

                var bitmapImage = new BitmapImage();
                bitmapImage.BeginInit();
                bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                bitmapImage.StreamSource = memoryStream;
                bitmapImage.EndInit();
                bitmapImage.Freeze();

                return bitmapImage;
            }
        }

    }
}
