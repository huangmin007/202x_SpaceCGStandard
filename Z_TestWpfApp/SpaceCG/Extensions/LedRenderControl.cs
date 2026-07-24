using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using System.Xml.Linq;
using SpaceCG.Device;
using SpaceCG.Drawing;
using SpaceCG.IO;

namespace SpaceCG.Extensions
{
    /// <summary>
    /// LedRenderControl 类用于在 WPF 应用程序中实现 LED 灯带的渲染功能。
    /// 在这里加个使用示例：
    /// <code>
    /// var config = XElementExtensions.Load("Resources/Config.xml");
    /// LedRenderControl ledRenderControl = new LedRenderControl(canvas);
    /// ledRenderControl.InitializeComponent(config.Element("DrawingDisplay"), config.Element("LedDevices"), config.Element("Scenes"));
    /// ledRenderControl.StartRender();
    /// //ledRenderControl.StopRender();
    /// //ledRenderControl.CheckConnectStatus();
    /// //ledRenderControl.RenderSceneId(2);
    /// </code>
    /// </summary>
    public class LedRenderControl
    {
        private Canvas _canvas;
        private TextBlock _textBlock_info;

        private DrawingWpfElement drawingDisplay;
        private IEnumerable<XElement> sceneElements;
        private CancellationTokenSource cancelTokenSource;

        private readonly StringBuilder FpsBuilder = new StringBuilder();
        private readonly StringBuilder InfoBuilder = new StringBuilder();
        private readonly List<Task> LedStripRenderTasks = new List<Task>();

        /// <summary> 当前渲染场景的编号 </summary>
        public int SceneId { get; private set; } = -1;

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="canvas"></param>
        /// <exception cref="ArgumentNullException"></exception>
        public LedRenderControl(Canvas canvas)
        {
            if (canvas == null) throw new ArgumentNullException(nameof(canvas));

            this._canvas = canvas;
            this._textBlock_info = new TextBlock();
            this._textBlock_info.Padding = new Thickness(10);
            this._textBlock_info.FontSize = 14;
            this._textBlock_info.LineHeight = 18;
            this._textBlock_info.Foreground = new SolidColorBrush(Colors.White);
            this._textBlock_info.Background = new SolidColorBrush(Color.FromArgb(0x88, 0x00, 0x00, 0x00));
            _canvas.Children.Add(_textBlock_info);

            var window = Window.GetWindow(_canvas);
            window.Closing += Window_Closing;
            window.PreviewKeyDown += Window_PreviewKeyDown;

            _canvas.PreviewMouseLeftButtonDown += Canvas_PreviewMouseLeftButtonDown;
        }
        private void Window_Closing(object sender, CancelEventArgs e) { LedRenderBus.Collections.Dispose(); }
        /// <summary>
        /// 【调试】数灯珠
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            var shapes = GetSelectedShapes();
            if (!shapes.Any()) return;
            Trace.WriteLine($"Selected shapes: {shapes.Count()}");

            var key = e.Key == Key.System ? e.SystemKey : e.Key == Key.ImeProcessed ? e.ImeProcessedKey : e.Key;

            switch (key)
            {
                case Key.Left:
                case Key.Right:
                    for (int i = 0; i < shapes.Count(); i++)
                    {
                        var offset = 1;
                        var shape = shapes.ElementAt(i);

                        if (e.KeyboardDevice.Modifiers == ModifierKeys.Control) offset = 5;
                        else if (e.KeyboardDevice.Modifiers == (ModifierKeys.Control | ModifierKeys.Alt)) offset = 10;
                        else if (e.KeyboardDevice.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift)) offset = 15;
                        else if (e.KeyboardDevice.Modifiers == (ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Shift)) offset = 30;

                        if (key == Key.Left) offset *= -1;

                        var left = Canvas.GetLeft(shape) + offset;
                        left = Math.Max(0, Math.Min(left, 1024 - shape.ActualWidth));

                        Canvas.SetLeft(shape, left);
                    }
                    break;

                case Key.Enter:
                case Key.Escape:
                    CancelSelected();
                    break;
            }

            e.Handled = true;
            //e.Handled = shapes.Any();

            // Update Selected Shapes Info
            InfoBuilder.Clear();
            for (int i = 0; i < shapes.Count(); i++)
            {
                var shape = shapes.ElementAt(i);
                var left = (double)Canvas.GetLeft(shape);
                InfoBuilder.AppendLine($"{shape.ToolTip}_{shape.Name}  Width:{shape.Width}  Left:{left}");
            }
        }
        /// <summary>
        /// 【调试】鼠标操作，选中元素
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Canvas_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.Source is Label label)
            {
                label.FontStyle = label.FontStyle == FontStyles.Normal ? FontStyles.Italic : FontStyles.Normal;
                label.FontWeight = label.FontWeight == FontWeights.Normal ? FontWeights.Bold : FontWeights.Normal;
            }
            else if (e.Source is Shape shape)
            {
                var name = shape.Name.Replace(nameof(Rectangle), nameof(Label));
                var sLabel = _canvas.FindName(name) as Label;
                if (sLabel == null) return;

                sLabel.FontStyle = sLabel.FontStyle == FontStyles.Normal ? FontStyles.Italic : FontStyles.Normal;
                sLabel.FontWeight = sLabel.FontWeight == FontWeights.Normal ? FontWeights.Bold : FontWeights.Normal;
            }

            // Update Selected Shapes Info
            InfoBuilder.Clear();
            var shapes = GetSelectedShapes();
            for (int i = 0; i < shapes.Count(); i++)
            {
                var shape = shapes.ElementAt(i);
                var left = (double)Canvas.GetLeft(shape);
                InfoBuilder.AppendLine($"{shape.ToolTip}_{shape.Name}  Width:{shape.Width}  Left:{left}");
            }
        }

        /// <summary>
        /// 初始化控件，加载场景元素和 LED 灯带对象
        /// </summary>
        /// <param name="drawingDisplay"></param>
        /// <param name="ledDevices"></param>
        /// <param name="sceneElement"></param>
        /// <exception cref="ArgumentNullException"></exception>
        public void InitializeComponent(XElement drawingDisplay, XElement ledDevices, XElement sceneElement)
        {
            if (ledDevices == null || drawingDisplay == null || sceneElement == null)
                throw new ArgumentNullException($"{nameof(ledDevices)},{nameof(drawingDisplay)},{nameof(sceneElement)}");

            var interval = int.TryParse(drawingDisplay.Attribute("Interval")?.Value, out int _interval) ? _interval : 40;
            var rectangle = DrawingExtensions.TryParseRectangle(drawingDisplay.Attribute("Rectangle")?.Value.Split(','), out var rect) ? rect : new System.Drawing.Rectangle(0, 0, 512, 32);

            this.drawingDisplay = new DrawingWpfElement(_canvas);
            this.drawingDisplay.Interval = interval;
            this.drawingDisplay.Rectangle = rectangle;
            this.drawingDisplay.NewDrawingFrame += DrawingDisplay_NewDrawingFrame;

            int y = 10;                     // 绘制显示元素的 Y 坐标
            const int DefaultCount = 200;    // 默认 LED 灯珠数量
            const int DefaultHeight = 10;
            foreach (var busElement in ledDevices.Elements("LedRenderBus"))
            {
                if (!Enum.TryParse<TransportType>(busElement.Attribute(nameof(TransportChannel.Type))?.Value, true, out var type)) continue;

                var ledStripElements = busElement.Elements(nameof(LedStripObject));
                if (!ledStripElements.Any()) continue;

                // 创建渲染总线通道
                if (LedRenderBus.TryCreateInstance(busElement, out var renderBus, false))
                {
                    foreach (var ledStripElement in ledStripElements)
                    {
#if true // 自动计算并添加 LED 点坐标，纵坐标间隔 20 像素
                        if (string.IsNullOrWhiteSpace(ledStripElement.Attribute("LedPoints")?.Value))
                        {
                            // 如果配置中设置了灯珠数量，则按设置数量，否则按默认数量
                            var ledCount = int.TryParse(ledStripElement.Attribute("Count")?.Value, out int _count) ? _count : DefaultCount;
                            ledStripElement.SetAttributeValue("LedPoints", $"0,{y + 2}, ... ,{ledCount},{y + 2}");
                        }
                        y += 20;
#endif
                        // 创建 LED 灯带对象
                        if (!LedStripObject.TryCreateInstance(ledStripElement, out var ledStrip)) continue;

                        ledStrip.Tag = ledStripElement;
                        renderBus.AddLedStrip(ledStrip);
                        int displayWidth = (int)(ledStrip.LedCount * 1.2);
                        //int displayWidth = (int)((int.TryParse(ledStripElement.Attribute("Count")?.Value, out int count) ? count : ledStrip.LedCount) * 1.2);

                        #region 创建同步渲染的显示元素
                        var elementObject = new Rectangle();
                        elementObject.Fill = new SolidColorBrush(Colors.Black);
                        elementObject.Width = displayWidth;
                        elementObject.Height = DefaultHeight;
                        //elementObject.Tag = ledStrip.UID;
                        elementObject.ToolTip = ledStrip.Comment;
                        elementObject.Name = $"{nameof(Rectangle)}_{ledStrip.Address}_{ledStrip.Port}";
                        elementObject.SetValue(Canvas.LeftProperty, 0.0);
                        elementObject.SetValue(Canvas.TopProperty, (double)ledStrip.LedPoints[0].Y);

                        _canvas.Children.Add(elementObject);
                        _canvas.RegisterName(elementObject.Name, elementObject);
                        #endregion

                        #region  创建 Label 用于显示注释
                        Label label = new Label();
                        label.FontSize = 12;
                        label.FontWeight = FontWeights.Normal;
                        label.Foreground = new SolidColorBrush(Colors.White);
                        label.Name = $"{nameof(Label)}_{ledStrip.Address}_{ledStrip.Port}";
                        label.Content = $"_{ledStrip.Comment}_0x{ledStrip.Address:00}{ledStrip.Port:00}";
                        label.SetValue(Canvas.LeftProperty, elementObject.Width + 10.0);
                        label.SetValue(Canvas.TopProperty, (double)ledStrip.LedPoints[0].Y - 8.0);

                        _canvas.Children.Add(label);
                        _canvas.RegisterName(label.Name, label);
                        #endregion

                        _textBlock_info.SetValue(Canvas.TopProperty, (double)ledStrip.LedPoints[0].Y + 40.0);

                        if (!string.IsNullOrWhiteSpace(ledStripElement.Attribute("Action")?.Value))
                        {
                            _ = RenderUIElement(new[] { ledStripElement }, 0, CancellationToken.None);
                        }
                    }
                }
            }

            // 启动默认场景
            sceneElements = sceneElement.Elements("Scene");
            var defaultStartupSceneId = sceneElement.Attribute("StartupSceneId")?.Value;
            if (!string.IsNullOrWhiteSpace(defaultStartupSceneId) && int.TryParse(defaultStartupSceneId, out int index))
            {
                RenderSceneId(index);
            }
        }
        /// <summary>
        /// 绘图显示新帧事件
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="frame"></param>
        private void DrawingDisplay_NewDrawingFrame(object sender, DrawingEventArgs frame)
        {
            foreach (var ledRenderBus in LedRenderBus.Collections)
            {
                ledRenderBus.RenderPixels(frame.Pixels, frame.Stride, frame.Width, frame.Height, frame.PixelFormat);
            }

            FpsBuilder.Clear();
            FpsBuilder.AppendLine($"Drawing FPS:{drawingDisplay.Fps} ");
            foreach (var ledRenderBus in LedRenderBus.Collections)
            {
                FpsBuilder.Append($"{ledRenderBus.Name} FPS:{ledRenderBus.Fps} ");
                foreach (var strip in ledRenderBus.LedStrips.Values)
                {
                    FpsBuilder.Append($"{strip.Address}_{strip.Port}:{strip.Fps} ");
                }
                FpsBuilder.AppendLine();
            }

            var infoContent = $"{FpsBuilder.ToString()}\r\n{InfoBuilder.ToString()}";
            _canvas.Dispatcher.InvokeAsync(() =>
            {
                _textBlock_info.Text = infoContent;
            });
        }

        /// <summary>
        /// 重置选中的形状元素
        /// </summary>
        public void CancelSelected()
        {
            InfoBuilder.Clear();
            foreach (var child in _canvas.Children)
            {
                if (child is Shape shape)
                {
                    Canvas.SetLeft(shape, 0);
                }
                else if (child is Label label && !string.IsNullOrWhiteSpace(label.Name))
                {
                    label.FontStyle = FontStyles.Normal;
                    label.FontWeight = FontWeights.Normal;
                }
            }
        }
        /// <summary>
        /// 获取选中的形状元素
        /// </summary>
        /// <returns></returns>
        public IEnumerable<Shape> GetSelectedShapes()
        {
            var shapes = new List<Shape>();

            foreach (var children in _canvas.Children)
            {
                if (children is Label label && !string.IsNullOrWhiteSpace(label.Name) && label.FontWeight == FontWeights.Bold)
                {
                    var name = label.Name.Replace(nameof(Label), nameof(Rectangle));
                    var shape = _canvas.FindName(name) as Shape;

                    if (shape != null)
                    {
                        shapes.Add(shape);
                    }
                }
            }

            return shapes;
        }

        /// <summary>
        /// 更新场景元素
        /// </summary>
        /// <param name="sceneElement"></param>
        public void UpdateScenes(XElement sceneElement)
        {
            if (sceneElement == null) return;
            sceneElements = sceneElement.Elements("Scene");

            var scene = _canvas.Tag as XElement;
            if (scene != null && scene.TryGetValue("Id", out int id))
            {
                RenderSceneId(id);
            }
        }

        /// <summary>
        /// 渲染场景
        /// </summary>
        /// <param name="sceneElement"></param>
        protected void RenderScene(XElement sceneElement)
        {
            CancelRenderTask();
            SetShapesColor(Colors.Black);
            if (sceneElement == null) return;

            _canvas.Tag = sceneElement;
            if (sceneElement.TryGetValue("Id", out int id))
            {
                SceneId = id;
            }

            #region 需要立即更新显示的对象，无 Delay 属性，创建立即渲染任务
            var notDelayElements = from ledStripElement in sceneElement.Elements(nameof(LedStripObject))
                                   where string.IsNullOrWhiteSpace(ledStripElement.Attribute("Delay")?.Value)
                                   select ledStripElement;
            _ = RenderUIElement(notDelayElements);
            #endregion

            #region 需要延迟更新显示的对象，创建延迟任务；跟据延迟时间(key-delay)，创建任务（相同延迟时间的创建一个渲染任务）
            var delayGroupElements = from ledStripElement in sceneElement.Elements(nameof(LedStripObject))
                                     where !string.IsNullOrWhiteSpace(ledStripElement.Attribute("Delay")?.Value)
                                     group ledStripElement by int.Parse(ledStripElement.Attribute("Delay")?.Value) into g
                                     orderby g.Key    // 按延迟时间排序
                                     select g;
            if (!delayGroupElements.Any()) return;

            cancelTokenSource = new CancellationTokenSource();
            CancellationToken cancelToken = cancelTokenSource.Token;

            foreach (var delayGroup in delayGroupElements)
            {
                var delayMs = delayGroup.Key;
                var delayLedStripElements = delayGroup.ToList();

                //Trace.WriteLine($"Delay Group: {delayMs}ms, Elements: {delayLedStripElements.Count}");
                //Trace.WriteLine($"\t{string.Join("\r\n\t", delayLedStripElements.Select(element => element.ToString()))}");

                LedStripRenderTasks.Add(RenderUIElement(delayLedStripElements, delayMs, cancelToken));
            }
            #endregion

            GC.Collect();
        }
        /// <summary>
        /// 渲染指定的场景
        /// </summary>
        /// <param name="propertyName"></param>
        /// <param name="propertyValue"></param>
        public void RenderScene(string propertyName, int propertyValue)
        {
            if (sceneElements == null || _canvas == null) return;
            if (string.IsNullOrWhiteSpace(propertyName)) return;

            var elements = from scene in sceneElements
                           where !string.IsNullOrWhiteSpace(scene.Attribute(propertyName)?.Value)
                           where int.TryParse(scene.Attribute(propertyName)?.Value, out int value) && value == propertyValue
                           select scene;

            if (elements.Count() != 1) return;
            RenderScene(elements.FirstOrDefault());
        }
        /// <summary>
        /// 渲染指定场景
        /// </summary>
        /// <param name="propertyName"></param>
        /// <param name="propertyValue"></param>
        public void RenderScene(string propertyName, string propertyValue)
        {
            if (sceneElements == null || _canvas == null) return;
            if (string.IsNullOrWhiteSpace(propertyName) || string.IsNullOrWhiteSpace(propertyValue)) return;

            var elements = from scene in sceneElements
                           where !string.IsNullOrWhiteSpace(scene.Attribute(propertyName)?.Value)
                           where string.Equals(scene.Attribute(propertyName)?.Value, propertyValue, StringComparison.OrdinalIgnoreCase)
                           select scene;

            if (elements.Count() != 1) return;
            RenderScene(elements.FirstOrDefault());
        }
        /// <summary>
        /// 渲染指定场景
        /// </summary>
        /// <param name="id"></param>
        public void RenderSceneId(int id) => RenderScene("Id", id);

        /// <summary>
        /// 设置所有 Shape 元素的颜色
        /// </summary>
        /// <param name="color"></param>
        public void SetShapesColor(Color color)
        {
            var colorBrush = new SolidColorBrush(color);

            foreach (var children in _canvas.Children)
            {
                if (children is Shape shape)
                {
                    shape.Fill = colorBrush;
                }
            }
        }

        /// <summary> 启动渲染  </summary>
        public void StartRender()
        {
            foreach (var ledRenderBus in LedRenderBus.Collections)
            {
                try
                {
                    ledRenderBus.StartRender();
                }
                catch (Exception ex)
                {
                    Trace.TraceError($"StartRender ({ledRenderBus.Name}) Exception: {ex.Message}");
                }
            }

            this.drawingDisplay.StartDrawing();
        }
        /// <summary> 停止渲染  </summary>
        public void StopRender()
        {
            this.drawingDisplay.StopDrawing();
            foreach (var ledRenderBus in LedRenderBus.Collections)
            {
                try
                {
                    ledRenderBus.StopRender();
                }
                catch (Exception ex)
                {
                    Trace.TraceError($"StopRender ({ledRenderBus.Name}) Exception: {ex.Message}");
                }
            }
        }
        /// <summary> 检查连接状态  </summary>
        public void CheckConnectStatus()
        {
            foreach (var ledRenderBus in LedRenderBus.Collections)
            {
                if (ledRenderBus.IsConnected) continue;

                Trace.TraceInformation($"Reconnect....{ledRenderBus.Name}");

                try
                {
                    ledRenderBus.StopRender();
                    ledRenderBus.StartRender();
                }
                catch (Exception ex)
                {
                    Trace.TraceError($"StartRender ({ledRenderBus.Name}) Exception: {ex.Message}");
                }
            }
        }

        /// <summary>  取消延时渲染任务  </summary>
        private void CancelRenderTask()
        {
            cancelTokenSource?.Cancel();

            foreach (var task in LedStripRenderTasks)
            {
                try
                {
                    if (task.IsCompleted) continue;
                    task?.Dispose();
                }
                catch (Exception ex)
                {
                    Trace.TraceWarning($"Render Task Canceled Exception: {ex.Message}");
                }
            }
            LedStripRenderTasks.Clear();

            try
            {
                cancelTokenSource?.Dispose();
            }
            catch (Exception) { }
            finally
            {
                cancelTokenSource = null;
            }
        }
        /// <summary>
        /// 渲染 UI 元素
        /// </summary>
        /// <param name="ledStripObjects"></param>
        /// <param name="delay"></param>
        /// <param name="cancelToken"></param>
        /// <returns></returns>
        private async Task RenderUIElement(IEnumerable<XElement> ledStripObjects, int delay = 0, CancellationToken cancelToken = default)
        {
            if (delay > 0)
            {
                var count = delay / 10;
                try
                {
                    while (!cancelToken.IsCancellationRequested && count > 0)
                    {
                        count--;
                        await Task.Delay(10, cancelToken);
                    }
                    if (cancelToken.IsCancellationRequested) return;
                }
                catch (Exception ex) when (ex is OperationCanceledException || ex is TaskCanceledException || ex is ObjectDisposedException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    Trace.TraceWarning($"Delay Group Task Canceled, delay:{count * 100}/{delay} Exception: {ex.Message}");
                    return;
                }
            }

            await _canvas.Dispatcher.InvokeAsync(() =>
            {
                foreach (var ledStripElement in ledStripObjects)
                {
                    if (!ledStripElement.TryGetValue("Address", out int address) || !ledStripElement.TryGetValue("Port", out int port)) return;

                    //Trace.WriteLine($"Delay:{delay}, RenderUIElement: {ledStripElement}");
                    var elementName = $"{nameof(Rectangle)}_{address}_{port}";
                    var elementObject = _canvas.FindName(elementName) as Shape;
                    if (elementObject == null) continue;

#if false
                    ushort fillCount = 0, repeatCount = 1;
                    if (ledStripElement.TryGetValue("FillCount", out fillCount) || ledStripElement.TryGetValue("RepeatCount", out repeatCount))
                    {
                        var ledStripObject = LedRenderBus.Collections.GetLedStrips().Where(s => s.Address == address && s.Port == port).Select(s => s).FirstOrDefault();
                        if (ledStripObject != null)
                        {
                            ledStripObject.FillCount = fillCount;
                            ledStripObject.RepeatCount = repeatCount;
                        }
                    }
#endif
                    var actionValue = ledStripElement.Attribute("Action")?.Value;
                    if (string.IsNullOrWhiteSpace(actionValue)) continue;
                    if (!StringExtensions.TryParseParameters(ledStripElement.Attribute("Params")?.Value, out var paramArray)) continue;

                    // 优化渲染参数
                    foreach (var ledStripObject in LedRenderBus.Collections.GetLedStrips())
                    {
                        if (ledStripObject.Address == address && ledStripObject.Port == port)
                        {
                            ledStripObject.UseBitmapPixels = true;
                            var element = ledStripObject.Tag as XElement;

                            if (ledStripElement.TryGetValue("Timeout", out int timeout))
                            {
                                ledStripObject.Timeout = timeout;
                            }
                            else
                            {
                                if (element != null && element.TryGetValue("Timeout", out int _timeout))
                                {
                                    ledStripObject.Timeout = _timeout;
                                }
                            }

                            if (ledStripElement.TryGetValue("FillCount", out ushort fillCount))
                            {
                                ledStripObject.FillCount = fillCount;
                            }
                            else
                            {
                                if (element != null && element.TryGetValue("FillCount", out ushort _fillCount))
                                {
                                    ledStripObject.FillCount = _fillCount;
                                }
                            }

                            if (ledStripElement.TryGetValue("RepeatCount", out ushort repeatCount))
                            {
                                ledStripObject.RepeatCount = repeatCount;
                            }
                            else
                            {
                                if (element != null && element.TryGetValue("RepeatCount", out ushort _repeatCount))
                                {
                                    ledStripObject.RepeatCount = _repeatCount;
                                }
                            }
                            break;
                        }
                    }

                    if (InstanceExtensions.TryInvokeMethod("SpaceCG.Extensions.BrushExtensions", actionValue, paramArray, out object result) && result is Brush newBrush)
                    {
#if true
                        elementObject.Fill = newBrush;
                        //elementObject.Tag = ledStripElement;
#else
                        //ChangeBrushAnimation(elementObject, newBrush, ledStripElement, 0.3);
#endif
                    }
                    else
                    {
                        Trace.TraceWarning($"Call Local Method:{actionValue}({(paramArray != null ? string.Join(",", paramArray) : "")}) Failed. ReturnResult:{result}");
                    }
                }

            }, DispatcherPriority.Normal, cancelToken);
        }

    }
}
