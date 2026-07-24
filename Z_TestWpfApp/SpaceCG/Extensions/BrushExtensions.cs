using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace SpaceCG.Extensions
{
    /// <summary>
    /// Brush 扩展类
    /// </summary>
    public static partial class BrushExtensions
    {
        public const double ElementHeight = 20.0;

        /// <summary>
        /// 淡入动画，渐渐显示
        /// </summary>
        public static DoubleAnimation OpacityFadeInAnimation = new DoubleAnimation(1.0, new Duration(TimeSpan.FromSeconds(0.5)));
        /// <summary>
        /// 淡出动画，渐渐消失
        /// </summary>
        public static DoubleAnimation OpacityFadeOutAnimation = new DoubleAnimation(0.0, new Duration(TimeSpan.FromSeconds(0.5)));

        #region 静态效果
        /// <summary>
        /// 【静态效果】纯颜色笔刷
        /// </summary>
        /// <param name="color"></param>
        /// <returns></returns>
        public static Brush ColorBrush(Color color)
        {
            var solidBrush = new SolidColorBrush(color);
            return solidBrush;
        }
        /// <summary>
        /// 【静态效果】纯颜色笔刷
        /// </summary>
        /// <param name="color"></param>
        /// <param name="width"></param>
        /// <returns></returns>
        public static Brush ColorBrush(Color color, int width)
        {
            var brush = new SolidColorBrush(color);
            var rectangle = new Rect(0, 0, width, ElementHeight);
            var drawingGeometry = new GeometryDrawing(brush, null, new RectangleGeometry(rectangle));

            var drawingBrush = new DrawingBrush()
            {
                Viewport = rectangle,
                ViewportUnits = BrushMappingMode.Absolute,

                Stretch = Stretch.None,
                TileMode = TileMode.Tile,
                AlignmentY = AlignmentY.Top,
                AlignmentX = AlignmentX.Left,

                Drawing = drawingGeometry,
            };

            return drawingBrush;
        }
        /// <summary>
        /// 【静态效果】多段颜色笔刷，从左到右排列
        /// </summary>
        /// <param name="colors"></param>
        /// <param name="widths"></param>
        /// <returns></returns>
        public static Brush ColorBrush(IEnumerable<Color> colors, IEnumerable<int> widths)
        {
            if (colors == null || widths == null) return null;
            if (colors.Count() == 0 || colors.Count() != widths.Count())
            {
                Trace.TraceWarning($"ColorBrush: colors.Count() != widths.Count()");
                return null;
            }

            DrawingGroup drawingGroup = new DrawingGroup();
            for (int i = 0; i < colors.Count(); i++)
            {
                var brush = new SolidColorBrush(colors.ElementAt(i));
                var rect = new Rect(widths.Take(i).Sum(), 0, widths.ElementAt(i), ElementHeight);

                var geometry = new RectangleGeometry(rect);
                var drawingGeometry = new GeometryDrawing(brush, null, geometry);

                drawingGroup.Children.Add(drawingGeometry);
            }

            var drawingBrush = new DrawingBrush()
            {
                Viewport = new Rect(0, 0, widths.Sum(), ElementHeight),
                ViewportUnits = BrushMappingMode.Absolute,

                Drawing = drawingGroup,
                Stretch = Stretch.None,
                TileMode = TileMode.Tile,
                AlignmentY = AlignmentY.Top,
                AlignmentX = AlignmentX.Left,
            };

            return drawingBrush;
        }

        /// <summary>
        /// 【静态效果】线性渐变色笔刷，从左到右平均渐变
        /// </summary>
        /// <param name="colors"></param>
        /// <returns></returns>
        public static Brush LinearBrush(IEnumerable<Color> colors)
        {
            if (colors == null || colors.Count() == 0) return null;

            var gradient = new LinearGradientBrush()
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(1, 0),
            };
            double offset = 0.0, interval = 1.0 / (colors.Count() - 1);
            foreach (var color in colors)
            {
                var stop = new GradientStop(color, offset);
                gradient.GradientStops.Add(stop);
                offset += interval;
            }

            return gradient;
        }
        /// <summary>
        /// 【静态效果】线性渐变色笔刷，从左到右平均渐变
        /// </summary>
        /// <param name="colors"></param>
        /// <param name="width"></param>
        /// <returns></returns>
        public static Brush LinearBrush(IEnumerable<Color> colors, int width)
        {
            var brush = LinearBrush(colors);
            var rectangle = new Rect(0, 0, width, ElementHeight);
            var drawingGeometry = new GeometryDrawing(brush, null, new RectangleGeometry(rectangle));

            var drawingBrush = new DrawingBrush()
            {
                Viewport = rectangle,
                ViewportUnits = BrushMappingMode.Absolute,

                Stretch = Stretch.None,
                TileMode = TileMode.Tile,
                AlignmentX = AlignmentX.Left,
                AlignmentY = AlignmentY.Top,

                Drawing = drawingGeometry,
            };

            return drawingBrush;
        }
        /// <summary>
        /// 【静态效果】线性渐变色笔刷，从左到右渐变
        /// </summary>
        /// <param name="colors"></param>
        /// <param name="offsets"></param>
        /// <returns></returns>
        public static Brush LinearBrush(IEnumerable<Color> colors, IEnumerable<double> offsets)
        {
            if (colors == null || offsets == null) return null;
            if (colors.Count() == 0 || colors.Count() != offsets.Count())
            {
                Trace.TraceWarning($"LinearBrush: colors.Count() != offsets.Count()");
                return null;
            }

            var gradient = new LinearGradientBrush()
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(1, 0),
            };

            for (int i = 0; i < colors.Count(); i++)
            {
                gradient.GradientStops.Add(new GradientStop(colors.ElementAt(i), offsets.ElementAt(i)));
            }

            return gradient;
        }
        /// <summary>
        /// 【静态效果】线性渐变色笔刷，从左到右渐变
        /// </summary>
        /// <param name="colors"></param>
        /// <param name="offsets"></param>
        /// <param name="width"></param>
        /// <returns></returns>
        public static Brush LinearBrush(IEnumerable<Color> colors, IEnumerable<double> offsets, int width)
        {
            var gradient = LinearBrush(colors, offsets);
            var rectangle = new Rect(0, 0, width, ElementHeight);
            var drawingGeometry = new GeometryDrawing(gradient, null, new RectangleGeometry(rectangle));

            var drawingBrush = new DrawingBrush()
            {
                Viewport = rectangle,
                ViewportUnits = BrushMappingMode.Absolute,

                Stretch = Stretch.None,
                TileMode = TileMode.Tile,
                AlignmentX = AlignmentX.Left,
                AlignmentY = AlignmentY.Top,

                Drawing = drawingGeometry,
            };

            return drawingBrush;
        }
        /// <summary>
        /// 【静态效果】多段线性渐变色笔刷，从左到右排列，颜色由多段渐变颜色组成
        /// </summary>
        /// <param name="colors"></param>
        /// <param name="widths"></param>
        /// <returns></returns>
        public static Brush LinearBrush(IEnumerable<IEnumerable<Color>> colors, IEnumerable<int> widths)
        {
            if (colors == null || widths == null) return null;
            if (colors.Count() == 0 || colors.Count() != widths.Count())
            {
                Trace.TraceWarning($"LinearBrush: colors.Count() != widths.Count()");
                return null;
            }

            DrawingGroup drawingGroup = new DrawingGroup();
            for (int i = 0; i < colors.Count(); i++)
            {
                var brush = LinearBrush(colors.ElementAt(i));
                var rectangle = new Rect(widths.Take(i).Sum(), 0, widths.ElementAt(i), ElementHeight);

                var geometry = new RectangleGeometry(rectangle);
                var drawingGeometry = new GeometryDrawing(brush, null, geometry);

                drawingGroup.Children.Add(drawingGeometry);
            }

            var drawingBrush = new DrawingBrush()
            {
                Viewport = new Rect(0, 0, widths.Sum(), ElementHeight),
                ViewportUnits = BrushMappingMode.Absolute,

                Drawing = drawingGroup,
                Stretch = Stretch.None,
                TileMode = TileMode.Tile,
                AlignmentY = AlignmentY.Top,
                AlignmentX = AlignmentX.Left,
            };

            return drawingBrush;
        }
        /// <summary>
        /// 【静态效果】多段线性渐变色笔刷，从左到右排列，颜色由多段渐变颜色组成
        /// </summary>
        /// <param name="colors"></param>
        /// <param name="offsets"></param>
        /// <param name="widths"></param>
        /// <returns></returns>
        public static Brush LinearBrush(IEnumerable<IEnumerable<Color>> colors, IEnumerable<IEnumerable<double>> offsets, IEnumerable<int> widths)
        {
            if (colors == null || offsets == null || widths == null) return null;
            if (colors.Count() == 0 || colors.Count() != offsets.Count() || colors.Count() != widths.Count())
            {
                Trace.TraceWarning($"LinearBrush: colors.Count() != offsets.Count() || colors.Count() != widths.Count()");
            }

            DrawingGroup drawingGroup = new DrawingGroup();
            for (int i = 0; i < colors.Count(); i++)
            {
                var brush = LinearBrush(colors.ElementAt(i), offsets.ElementAt(i));
                var rectangle = new Rect(widths.Take(i).Sum(), 0, widths.ElementAt(i), ElementHeight);

                var geometry = new RectangleGeometry(rectangle);
                var drawingGeometry = new GeometryDrawing(brush, null, geometry);

                drawingGroup.Children.Add(drawingGeometry);
            }

            var drawingBrush = new DrawingBrush()
            {
                Viewport = new Rect(0, 0, widths.Sum(), ElementHeight),
                ViewportUnits = BrushMappingMode.Absolute,

                Drawing = drawingGroup,
                Stretch = Stretch.None,
                TileMode = TileMode.Tile,
                AlignmentY = AlignmentY.Top,
                AlignmentX = AlignmentX.Left,
            };

            return drawingBrush;
        }
        #endregion


        /// <summary>
        /// 设置笔刷的 Blink 动画效果
        /// </summary>
        /// <param name="brush"></param>
        /// <param name="from"></param>
        /// <param name="to"></param>
        /// <param name="duration"></param>
        /// <returns></returns>
        public static Brush SetBrushBlinkAnimation(Brush brush, double from = -0.2, double to = 1.2, double duration = 1.5)
        {
            if (brush == null) return null;

            DoubleAnimation blinkAnimation = new DoubleAnimation()
            {
                To = to,
                From = from,
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                Duration = TimeSpan.FromSeconds(duration),
            };

            brush.BeginAnimation(Brush.OpacityProperty, blinkAnimation);
            return brush;
        }
        /// <summary>
        /// 设置笔刷的 Flow 动画效果
        /// </summary>
        /// <param name="brush"></param>
        /// <param name="flowStep"></param>
        /// <param name="duration"></param>
        /// <returns></returns>
        public static Brush SetBrushFlowAnimation(Brush brush, double flowStep, double duration = 1.5)
        {
            if (brush == null) return null;

            var flowAnaimation = new DoubleAnimation()
            {
                By = flowStep,
                RepeatBehavior = RepeatBehavior.Forever,
                Duration = new Duration(TimeSpan.FromSeconds(duration))
            };

            var transform = new TranslateTransform();
            transform.BeginAnimation(TranslateTransform.XProperty, flowAnaimation);
            brush.Transform = transform;

            return brush;
        }


        #region 动态效果
        /// <summary>
        /// 【动态效果】单色纯色笔刷，闪烁/呼吸颜色刷
        /// </summary>
        /// <param name="color"></param>
        /// <param name="blinkTime"></param>
        /// <returns></returns>
        public static Brush BlinkColorBrush(Color color, double blinkTime = 1.5)
        {
            DoubleAnimation blinkAnimation = new DoubleAnimation()
            {
                From = -0.2,
                To = 1.2,
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                Duration = TimeSpan.FromSeconds(blinkTime),
            };

            var solidBrush = ColorBrush(color);
            solidBrush.BeginAnimation(SolidColorBrush.OpacityProperty, blinkAnimation);

            return solidBrush;
        }
        /// <summary>
        /// 【动态效果】多段颜色刷, 闪烁/呼吸效果
        /// </summary>
        /// <param name="colors">多段颜色</param>
        /// <param name="widths"></param>
        /// <param name="blinkTime"></param>
        /// <returns></returns>
        public static Brush BlinkColorBrush(IEnumerable<Color> colors, IEnumerable<int> widths, double blinkTime = 1.5)
        {
            var brush = ColorBrush(colors, widths);
            if (brush == null) return null;

            DoubleAnimation blinkAnimation = new DoubleAnimation()
            {
                From = -0.2,
                To = 1.2,
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                Duration = TimeSpan.FromSeconds(blinkTime),
            };

            brush.BeginAnimation(SolidColorBrush.OpacityProperty, blinkAnimation);
            return brush;
        }

        /// <summary>
        /// 【动态效果】单段渐变色刷，闪烁/呼吸渐变色刷
        /// </summary>
        /// <param name="colors"></param>
        /// <param name="blinkTime"></param>
        /// <returns></returns>
        public static Brush BlinkLinearBrush(IEnumerable<Color> colors, double blinkTime = 1.5)
        {
            var brush = LinearBrush(colors);
            if (brush == null) return null;

            DoubleAnimation blinkAnimation = new DoubleAnimation()
            {
                From = -0.2,
                To = 1.2,
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                Duration = TimeSpan.FromSeconds(blinkTime),
            };

            brush.BeginAnimation(SolidColorBrush.OpacityProperty, blinkAnimation);
            return brush;
        }
        /// <summary>
        /// 【动态效果】多段渐变色刷，闪烁/呼吸渐变色刷
        /// </summary>
        /// <param name="colors"></param>
        /// <param name="widths"></param>
        /// <param name="blinkTime"></param>
        /// <returns></returns>
        public static Brush BlinkLinearBrush(IEnumerable<IEnumerable<Color>> colors, IEnumerable<int> widths, double blinkTime = 1.5)
        {
            var brush = LinearBrush(colors, widths);
            if (brush == null) return null;

            DoubleAnimation blinkAnimation = new DoubleAnimation()
            {
                From = -0.2,
                To = 1.2,
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                Duration = TimeSpan.FromSeconds(blinkTime),
            };

            brush.BeginAnimation(SolidColorBrush.OpacityProperty, blinkAnimation);
            return brush;
        }

        /// <summary>
        /// 【动态效果】多段纯色/颜色流动效果
        /// </summary>
        /// <param name="colors"></param>
        /// <param name="widths"></param>
        /// <param name="flowTime">流动时间</param>
        /// <param name="flowStep">流动速度与方向，大于0时从左到右流动，小于0时从右到左流动</param>
        /// <returns></returns>
        public static Brush FlowColorBrush(IEnumerable<Color> colors, IEnumerable<int> widths, double flowTime = 1.5, double flowStep = 1)
        {
            var brush = ColorBrush(colors, widths);
            if (brush == null) return null;

            var flowAnaimation = new DoubleAnimation()
            {
                By = widths.Sum() * flowStep,
                RepeatBehavior = RepeatBehavior.Forever,
                Duration = new Duration(TimeSpan.FromSeconds(flowTime))
            };

            var transform = new TranslateTransform();
            transform.BeginAnimation(TranslateTransform.XProperty, flowAnaimation);
            brush.Transform = transform;

            return brush;
        }

        /// <summary>
        /// 【动态效果】单段渐变色流动效果
        /// </summary>
        /// <param name="colors"></param>
        /// <param name="width"></param>
        /// <param name="flowTime">流动时间</param>
        /// <param name="flowStep">流动速度与方向，大于0时从左到右流动，小于0时从右到左流动</param>
        /// <returns></returns>
        public static Brush FlowLinearBrush(IEnumerable<Color> colors, int width, double flowTime = 1.5, double flowStep = 1)
        {
            var brush = LinearBrush(colors, width);
            if (brush == null) return null;

            var flowAnaimation = new DoubleAnimation()
            {
                By = width * flowStep,
                RepeatBehavior = RepeatBehavior.Forever,
                Duration = new Duration(TimeSpan.FromSeconds(flowTime))
            };

            var transform = new TranslateTransform();
            transform.BeginAnimation(TranslateTransform.XProperty, flowAnaimation);
            brush.Transform = transform;

            return brush;
        }
        /// <summary>
        /// 【动态效果】多段渐变色流动效果
        /// </summary>
        /// <param name="colors"></param>
        /// <param name="widths"></param>
        /// <param name="flowTime">流动时间</param>
        /// <param name="flowStep">流动速度与方向，大于0时从左到右流动，小于0时从右到左流动</param>
        /// <returns></returns>
        public static Brush FlowLinearBrush(IEnumerable<IEnumerable<Color>> colors, IEnumerable<int> widths, double flowTime = 1.5, double flowStep = 1)
        {
            var brush = LinearBrush(colors, widths);
            if (brush == null) return null;

            var flowAnaimation = new DoubleAnimation()
            {
                By = widths.Sum() * flowStep,
                RepeatBehavior = RepeatBehavior.Forever,
                Duration = new Duration(TimeSpan.FromSeconds(flowTime))
            };

            var transform = new TranslateTransform();
            transform.BeginAnimation(TranslateTransform.XProperty, flowAnaimation);
            brush.Transform = transform;

            return brush;
        }

        /// <summary>
        /// 【动态效果】从起始位置到终止位置的颜色填充流动效果
        /// </summary>
        /// <param name="brush"></param>
        /// <param name="from"></param>
        /// <param name="to"></param>
        /// <param name="flowTime"></param>
        /// <param name="repeatCount"></param>
        /// <param name="alignmentX"></param>
        /// <returns></returns>
        public static Brush FlowBrushFromTo(Brush brush, double from, double to, double flowTime = 1.5, double repeatCount = 1, AlignmentX alignmentX = AlignmentX.Left)
        {
            if (brush == null) return null;

            var geometry = new RectangleGeometry(new Rect(0, 0, 0, 20));
            var drawingGeometry = new GeometryDrawing(brush, null, geometry);

            var rectAnimation = new RectAnimation()
            {
                To = new Rect(0, 0, to, 20),
                From = new Rect(0, 0, from, 20),
                Duration = TimeSpan.FromSeconds(flowTime),
                RepeatBehavior = new RepeatBehavior(repeatCount),
            };
            geometry.BeginAnimation(RectangleGeometry.RectProperty, rectAnimation);

            var drawingBrush = new DrawingBrush()
            {
                Stretch = Stretch.None,
                TileMode = TileMode.Tile,
                AlignmentY = AlignmentY.Top,
                AlignmentX = alignmentX,

                Drawing = drawingGeometry,
            };

            return drawingBrush;
        }
        /// <inheritdoc cref="FlowBrushFromTo(Brush, double, double, double, double, AlignmentX)"/>
        public static Brush FlowBrushFromTo(Color color, double from, double to, double flowTime = 1.5, double repeatCount = 1, AlignmentX alignmentX = AlignmentX.Left) => FlowBrushFromTo(new SolidColorBrush(color), from, to, flowTime, repeatCount, alignmentX);
        #endregion


        #region 自定义组合笔刷
        /// <summary>
        /// 【自定义组合笔刷】将多个笔刷按顺序组合在一起
        /// </summary>
        /// <param name="brushes"></param>
        /// <param name="widths"></param>
        /// <returns></returns>
        public static Brush GroupBrushBase(IEnumerable<Brush> brushes, IEnumerable<int> widths)
        {
            if (brushes == null || widths == null) return null;
            if (brushes.Count() == 0 || brushes.Count() != widths.Count())
            {
                Trace.TraceWarning($"GroupBrushBase: brushes.Count() != widths.Count()");
                return null;
            }

            const double Height = 20.0;

            DrawingGroup drawingGroup = new DrawingGroup();
            for (int i = 0; i < brushes.Count(); i++)
            {
                var rect = new Rect(widths.Take(i).Sum(), 0, widths.ElementAt(i), Height);

                var geometry = new RectangleGeometry(rect);
                var drawingGeometry = new GeometryDrawing(brushes.ElementAt(i), null, geometry);
                drawingGroup.Children.Add(drawingGeometry);
            }

            var drawingBrush = new DrawingBrush()
            {
                Viewport = new Rect(0, 0, widths.Sum(), Height),
                ViewportUnits = BrushMappingMode.Absolute,

                Stretch = Stretch.None,
                TileMode = TileMode.Tile,
                AlignmentY = AlignmentY.Top,
                AlignmentX = AlignmentX.Left,

                Drawing = drawingGroup,
            };

            return drawingBrush;
        }

        /// <summary>
        /// 【测试】自定义组合效果
        /// </summary>
        /// <returns></returns>
        public static Brush GroupBrushTest()
        {
            var brush0 = ColorBrush(new[] { Colors.Red, Colors.Green }, new[] { 30, 30 });
            var brush1 = LinearBrush(new[] { Colors.Red, Colors.Green, Colors.Blue });

            return GroupBrushBase(new[] { brush0, brush1, null }, new[] { 100, 40, 50 });
        }
        /// <summary>
        /// 【测试】自定义组合效果
        /// </summary>
        /// <returns></returns>
        public static Brush GroupBrushTestAnimation()
        {
            var brush0 = BlinkColorBrush(Colors.Red, 1.0);
            var brush1 = FlowColorBrush(new[] { Colors.Red, Colors.Green, Colors.Blue }, new[] { 30, 30, 30 }, 1.5, -1.0);
            var brush2 = FlowLinearBrush(new[] { new[] { Colors.Black, Colors.Green }, new[] { Colors.Black, Colors.Red } }, new[] { 30, 50 }, 1.5, 1.0);

            return GroupBrushBase(new[] { brush0, brush2 }, new[] { 60, 100 });
        }
        #endregion


        #region 杭州项目上的自定义组合效果
        /// <summary>
        /// 【静态+动态流动效果】左侧黑色，右侧渐变色流动效果
        /// </summary>
        /// <param name="leftWidth"></param>
        /// <param name="rightWidth"></param>
        /// <param name="colors"></param>
        /// <param name="flowWidth"></param>
        /// <param name="flowTime"></param>
        /// <param name="flowStep"></param>
        /// <returns></returns>
        public static Brush GroupBlackAndFlowLinearBrush(int leftWidth, int rightWidth, IEnumerable<IEnumerable<Color>> colors, IEnumerable<int> widths, double flowTime = 1.5, double flowStep = 1)
        {
            var blackBrush = ColorBrush(Colors.Black);
            var flowBrush = FlowLinearBrush(colors, widths, flowTime, flowStep);

            return GroupBrushBase(new[] { blackBrush, flowBrush }, new[] { leftWidth, rightWidth });
        }
        /// <summary>
        /// 【动态流动效果+静态】左侧渐变色，右侧黑色流动效果
        /// </summary>
        /// <param name="leftWidth"></param>
        /// <param name="rightWidth"></param>
        /// <param name="colors"></param>
        /// <param name="flowWidth"></param>
        /// <param name="flowTime"></param>
        /// <param name="flowStep"></param>
        /// <returns></returns>
        public static Brush GroupFlowLinearAndBlackBrush(int leftWidth, int rightWidth, IEnumerable<IEnumerable<Color>> colors, IEnumerable<int> widths, double flowTime = 1.5, double flowStep = 1)
        {
            var blackBrush = ColorBrush(Colors.Black);
            var flowBrush = FlowLinearBrush(colors, widths, flowTime, flowStep);

            return GroupBrushBase(new[] { flowBrush, blackBrush }, new[] { leftWidth, rightWidth });
        }
        #endregion


        /// <summary>
        /// 设置 Shape 的笔刷颜色，不改变 Shape 的笔刷类型
        /// </summary>
        /// <param name="shape"></param>
        /// <param name="color"></param>
        public static void SetShapeColor(Shape shape, Color color)
        {
            if (color == null || color.A == 0)
            {
                color = Colors.Black;
                shape.BeginAnimation(Shape.OpacityProperty, OpacityFadeOutAnimation);
                return;
            }
            else
            {
                shape.BeginAnimation(Shape.OpacityProperty, OpacityFadeInAnimation);
            }

            if (shape.Fill is SolidColorBrush colorBrush)
            {
                colorBrush.Color = color;
            }
            else if (shape.Fill is DrawingBrush drawingBrush && drawingBrush.Drawing is GeometryDrawing geometryDrawing)
            {
                if (geometryDrawing.Brush is SolidColorBrush solidBrush)
                {
                    solidBrush.Color = color;
                }
                else if (geometryDrawing.Brush is LinearGradientBrush linearGradientBrush)
                {
                    var brush = new LinearGradientBrush();
                    brush.EndPoint = linearGradientBrush.EndPoint;
                    brush.StartPoint = linearGradientBrush.StartPoint;

                    foreach (var stop in linearGradientBrush.GradientStops)
                    {
                        var nColor = Color.FromArgb(stop.Color.A, color.R, color.G, color.B);
                        brush.GradientStops.Add(new GradientStop(nColor, stop.Offset));
                    }

                    geometryDrawing.Brush = brush;
                }
            }

            //shape.Visibility = color == Colors.Transparent || color.A == 0 ? Visibility.Hidden : Visibility.Visible;
        }
        /// <summary>
        /// 设置 Shape 颜色
        /// </summary>
        /// <param name="shape"></param>
        /// <param name="color"></param>
        public static void SetShapeColor(Shape shape, string color)
        {
            Color nColor = Colors.Transparent;

            try
            {
                nColor = (Color)ColorConverter.ConvertFromString(color);
            }
            catch (Exception ex)
            {
                nColor = Colors.Transparent;
            }

            SetShapeColor(shape, nColor);
        }
    }
}
