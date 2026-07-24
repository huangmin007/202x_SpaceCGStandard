using System;
using System.Drawing;
using System.IO;
using System.Windows.Media.Imaging;

namespace SpaceCG.Drawing
{
    
    /// <summary>
    /// System.Drawing 扩展方法
    /// </summary>
    public static partial class DrawingExtensions
    {        
        /// <summary>
        /// 将 <see cref="System.Drawing.Bitmap"/> 转换为 <see cref="System.Windows.Media.Imaging.BitmapSource"/>
        /// </summary>
        /// <param name="bitmap"></param>
        /// <returns></returns>
        public static BitmapSource ToBitmapSource(this Bitmap bitmap)
        {
            if (bitmap == null) return null;

            using (var memoryStream = new MemoryStream())
            {
                bitmap.Save(memoryStream, System.Drawing.Imaging.ImageFormat.Bmp);
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
