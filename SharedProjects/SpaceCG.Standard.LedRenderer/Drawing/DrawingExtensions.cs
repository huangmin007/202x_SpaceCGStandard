using System;
using System.Linq;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Drawing;

namespace SpaceCG.Drawing
{
    /// <summary>
    /// 扫描方向
    /// </summary>
    public enum ScanAxis
    {
        /// <summary>
        /// 按行扫描（逐行）
        /// </summary>
        Horizontal,
        /// <summary>
        /// 按列扫描（逐列）
        /// </summary>
        Vertical
    }

    /// <summary>
    /// 扫描顺序
    /// </summary>
    public enum ScanOrder
    {
        /// <summary>
        /// 正向扫描（行或列同方向）
        /// </summary>
        Forward,
        /// <summary>
        /// 蛇形扫描（Z字形）
        /// </summary>
        ZigZag,
        
        // ... 待扩展
    }

    /// <summary>
    /// System.Drawing 扩展方法
    /// </summary>
    public static partial class DrawingExtensions
    {
        /// <summary>
        /// 点集字符串正则表达式
        /// </summary>
#if true
        public static readonly Regex PointsStringRegex = new Regex(@"^(\d+,\d+)(,(?:\d+,\d+|\.{3,6},\d+,\d+))*$", RegexOptions.Compiled);
#else        
        public static readonly Regex PointsStringRegex = new Regex(@"^(\s*\d+\s*,\s*\d+\s*)(\s*,\s*(\d+\s*,\s*\d+|\.{3,6}\s*,\s*\d+\s*,\s*\d+))*$", RegexOptions.Compiled); //包含空格格
#endif
        /// <summary>
        /// 将点集字符串解析为点集
        /// <para>支持格式示例："1,2,1,3,1,4,1,5,1,6"、"1,2, ... ,1,7,3,4, ... ,5,6"(会自动计算中间的点集)</para>
        /// </summary>
        /// <param name="pointsString"></param>
        /// <param name="points"></param>
        /// <returns></returns>
        public static bool TryParsePoints(string pointsString, out IEnumerable<Point> points)
        {
            points = null;

            if (string.IsNullOrEmpty(pointsString)) return false;
            if (pointsString.Contains(" ")) pointsString = pointsString.Replace(" ", "");   //移除空格
            if (pointsString.StartsWith(",")) pointsString = pointsString.Substring(1);     //移除开头和结尾的逗号
            if (pointsString.EndsWith(",")) pointsString = pointsString.Substring(0, pointsString.Length - 1);

            // 正则表达式验证点集字符串
            if (!PointsStringRegex.IsMatch(pointsString)) return false;

            var i = 0;
            var tokens = pointsString.Split(',');
            var results = new List<Point>(1024);
            
            while (i < tokens.Length)
            {
                if (tokens[i].StartsWith("..."))
                {
                    // 理论上不存在这种情况，因为正则表达式已经验证过了
                    // 插值段，前后至少各两个坐标点
                    //if (results.Count < 1 || i + 2 >= tokens.Length)
                    //    throw new FormatException("不合法的插值段位置");

                    var start = results[results.Count - 1];

                    var x = int.Parse(tokens[i + 1]);
                    var y = int.Parse(tokens[i + 2]);
                    var end = new Point(x, y);

                    var tempPoints = GetPoints(start, end);                    
                    results.AddRange(tempPoints.Skip(1));      // 要去掉第一个 start 点，因为它已经在 results 里了

                    i += 3;
                }
                else
                {
                    // 理论上不存在这种情况，因为正则表达式已经验证过了
                    //if (i + 1 >= tokens.Length)
                    //    throw new FormatException("坐标点数量不匹配");

                    int x = int.Parse(tokens[i]);
                    int y = int.Parse(tokens[i + 1]);
                    results.Add(new Point(x, y));

                    i += 2;
                }
            }

            points = results;
            return true;
        }

        /// <summary>
        /// 获取点集。使用 Bresenham 算法计算两点之间的点集
        /// <para>从 <paramref name="start"/> 到 <paramref name="end"/> 的直线点集</para>
        /// </summary>
        /// <param name="start"></param>
        /// <param name="end"></param>
        /// <returns>返回包含 <paramref name="start"/> 和 <paramref name="end"/> 的点集</returns>
        public static IEnumerable<Point> GetPoints(Point start, Point end)
        {
            if(start == null || end == null) return Array.Empty<Point>();

            int dx = Math.Abs(end.X - start.X);
            int dy = Math.Abs(end.Y - start.Y);
            int sx = (start.X < end.X) ? 1 : -1;
            int sy = (start.Y < end.Y) ? 1 : -1;
            int err = dx - dy;

            int x = start.X, y = start.Y;
            List<Point> points = new List<Point>();

            while (true)
            {
                points.Add(new Point(x, y));

                if (x == end.X && y == end.Y) break;

                int e2 = 2 * err;
                if (e2 > -dy)
                {
                    err -= dy;
                    x += sx;
                }

                if (e2 < dx)
                {
                    err += dx;
                    y += sy;
                }
            }

            return points;
        }

        /// <summary>
        /// 获取点集。按矩形的给定的方向、顺序、步长获取矩形的点集
        /// <para>默认按水平方向逐行扫描，正向顺序</para>
        /// </summary>
        /// <param name="rectangle"></param>
        /// <param name="axis"></param>
        /// <param name="order"></param>
        /// <param name="stepX"></param>
        /// <param name="stepY"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentException"></exception>
        public static IEnumerable<Point> GetPoints(Rectangle rectangle, ScanAxis axis = ScanAxis.Horizontal, ScanOrder order = ScanOrder.Forward, int stepX = 1, int stepY = 1)
        {
            if (rectangle == null || rectangle.IsEmpty) throw new ArgumentException("矩形不能为空");
            if (stepX <= 0 || stepY <= 0) throw new ArgumentException("步长必须大于 0");

            List<Point> results = new List<Point>(512);

            if (axis == ScanAxis.Horizontal)
            {
                for (int y = rectangle.Top, row = 0; y < rectangle.Bottom; y += stepY, row++)
                {
                    bool leftToRight = (order == ScanOrder.Forward) || (order == ScanOrder.ZigZag && row % 2 == 0);

                    if (leftToRight)
                    {
                        for (int x = rectangle.Left; x < rectangle.Right; x += stepX)
                            results.Add(new Point(x, y));
                    }
                    else
                    {
                        for (int x = rectangle.Right - 1; x >= rectangle.Left; x -= stepX)
                            results.Add(new Point(x, y));
                    }
                }
            }
            else // axis == Vertical
            {
                for (int x = rectangle.Left, col = 0; x < rectangle.Right; x += stepX, col++)
                {
                    bool topToBottom = (order == ScanOrder.Forward) || (order == ScanOrder.ZigZag && col % 2 == 0);

                    if (topToBottom)
                    {
                        for (int y = rectangle.Top; y < rectangle.Bottom; y += stepY)
                            results.Add(new Point(x, y));
                    }
                    else
                    {
                        for (int y = rectangle.Bottom - 1; y >= rectangle.Top; y -= stepY)
                            results.Add(new Point(x, y));
                    }
                }
            }

            return results;
        }

        /// <summary>
        /// 尝试获取字符串数组中的矩形数据
        /// <para> 字符串数组的格式为：x,y,width,height </para>
        /// </summary>
        /// <param name="rectString"></param>
        /// <param name="rectangle"></param>
        /// <returns></returns>
        public static bool TryParseRectangle(string[] rectString, out Rectangle rectangle)
        {
            rectangle = Rectangle.Empty;
            if (rectString == null || rectString.Length != 4) return false;

            if (int.TryParse(rectString[0], out int x) && int.TryParse(rectString[1], out int y) &&
                int.TryParse(rectString[2], out int width) && int.TryParse(rectString[3], out int height))
            {
                rectangle = new Rectangle(x, y, width, height);
                return true;
            }

            return false;
        }

    }

}
