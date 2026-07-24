### 零、概要
>《全彩彩 LED 灯带控制软件》 的第二个版本设计，Dll库文件 <br/>
>实时同步渲染可视桌面，实时渲染程序中的 UI 元素或动画 <br/>
>自定义灯珠的二维排列、布局

#### 实际帧率计算：
- ##### 数据发送时间计算
>串口通信通常会有 起始位 + 数据位 + 偶尔校验位 + 停止位，标准串口格式一般是 1位起始 + 8位数据 + 1位停止 = 10 bit/byte，发送一字节时间 = 10 / 波特率 <br/>
>按 921600 波特率发送数据，一个字节(10bit)需要 10/921600 = 10.85us，发送 1024 字节需要 1024 * 10.85us ≈ 11.6ms；<br/>
>如果要发送1024颗灯带的颜色数据(不包括协议头/尾字节数据)，则为：1024 * 3(3通道颜色) * 10.85us ≈ 33.33ms
- ##### 点亮灯珠时间计算
>WS2812(固定 800KHz 三线) 时序，一个逻辑位时间为 1.25us，一颗灯珠(3通道)需要 24bit * 1.25us = 30us <br />
>点亮一颗 WS2812 灯珠的时间为 30us，要点亮 1024 颗灯珠需要 1024 * 30us + 50us(复位信号) ≈ 30.77ms <br />
>APA102/SK9822(四线，数据线+时钟线)，可通过 SPI 高速传输，常见 SPI 速率可达 1–10 Mbps 或更高，点亮一颗灯珠约(8Mbps) 4us, 相比 WS8212B 点亮速度/刷新率更快。


### 设计
- #### IDrawingDisplay  实时绘制对象，主要是绘制/复制源的像素数据
	-  **重要属性和方法**
		- **Rectangle** 绘制区域，限制在 (0,0,1024,1024) 的范围内
		- **Interval** 每帧限制的绘制时间 16ms~1000ms 的范围内
		- **DrawingElement** 要绘制的显示元素对象
		- **StartDrawing()** 启动实时绘制线程
		- **StopDrawing()** 停止实时绘制线程
		- **NewDrawingFrame** 新绘制的数据帧事件
	- 实现类
		- **DrawingDesktop** 绘制桌面
		- **DrawingWpfElement** 绘制 WPF 元素内容
		- **DrawingU3dElement** 绘制 U3D 元素内容（没有实现）
		- ... 可选自定义实现 IDrawingDisplay
- #### LedStripObject 对象(仅支持二维坐标的灯带，如果想支持三维的可以参考此思路重构)
	- **重要属性和方法**
		- 只读属性：Group/Address/Port/Type/ColorFormat/LedPoints/Count/FrameCount/UID/Fps
		- 读写属性：FillCount/RepeatCount/Timeout/QueueMaxCount/UseBitmapPixels
		- 方法：AddPoint/AddPoints/RemovePoint/RemovePoints/ClearPoints
- #### LedRenderBus 渲染总线
	- **重要属性和方法**
		- **Interval** 每帧的渲染处理时间，单位毫秒
		- **AddLedStrip/RemoveLedStrip** 添加/移除灯带对象
		- **StartRender/StopRender/PauseRender/ResumeRender** 渲染控制
		- **RenderBitmap/RenderPixels** 渲染位图或像素(使用二维数据渲染)
		- **RenderColor/RenderColors** 渲染颜色(使用一维数据渲染)
		- **RenderClear** 渲染清除
### 示例

```C#
// 创建一个实时绘图对象
var drawingDisplay = new DrawingDesktop();
drawingDisplay.NewDrawingFrame += DrawingDisplay_NewDrawigFrame;
drawingDisplay.StartDrawing(new System.Drawing.Rectangle(0,0,600,30), 40);

private void DrawingDisplay_NewDrawigFrame(object sender, DrawingEventArgs frame)
{
	renderBus?.RenderPixels(frame.Pixels, frame.Stride, frame.Width, frame.Height, frame.PixelFormat);
}

// 创建 Led 灯带对象
var ledStrip = new LedStripObject(0x0001, 0x01);
// 添加灯珠(灯珠坐标映射到位图像素，灯珠的顺序表示物理世界灯珠的顺序位置)
// 灯珠的坐标位置 会取 实时绘图的像素位置的颜色数据
ledStrip.AddPoints(new System.Drawing.Point(0,0), new System.Drawing.Point(15,0));	

// 创建渲染 Bus 总线
var renderBus = new LedRenderBus(TransportType.SERIAL, "COM3,921600");
//var renderBus = new LedRenderBus(TransportType.UDP, "192.168.1.101,921600");
renderBus.Interval = 40;
renderBus.AddLedStrip(ledStrip);	//添加灯带对象
renderBus.StartRender();

```