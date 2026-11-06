using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using SharpDX.Direct3D9;
using SharpDX.Mathematics.Interop;

namespace SpaceCG.Controls
{
    /// <summary>
    /// RawColors
    /// </summary>
    public static class RawColors
    {
        public static readonly RawColorBGRA Transparent = new RawColorBGRA(255, 255, 255, 0);

        public static readonly RawColorBGRA Red = new RawColorBGRA(0, 0, 255, 255);
        public static readonly RawColorBGRA Green = new RawColorBGRA(0, 255, 0, 255);
        public static readonly RawColorBGRA Blue = new RawColorBGRA(255, 0, 0, 255);

        public static readonly RawColorBGRA Black = new RawColorBGRA(0, 0, 0, 255);
        public static readonly RawColorBGRA White = new RawColorBGRA(255, 255, 255, 255);
    }

    /// <summary>
    /// D3D9 共享设备
    /// <para>SharpDX, SharpDX.Direct3D9</para>
    /// </summary>
    public static class D3D9Shared
    {
        [DllImport("user32.dll")]
        private static extern IntPtr GetDesktopWindow();

        /**
         * https://learn.microsoft.com/zh-cn/windows/win32/medfound/recommended-8-bit-yuv-formats-for-video-rendering
         * https://www.fourcc.org/yuv.php
         * 跟据上面链接的资料，D3D9 推荐的 8 位 YUV 格式，只有 420 支持平面格式(planar)，而 D3D9Ex 只支持 NV12 和 YV12 两种 420 的平面格式(planar)?
         * 所以，这里的 D3D9Ex 格式，暂时只支持 NV12 和 YV12 两种 420 的平面格式(planar)；其它都是打包格式(packed)。
         */
        internal static readonly SharpDX.Direct3D9.Format YV12 = D3DX.MakeFourCC((byte)'Y', (byte)'V', (byte)'1', (byte)'2');
        internal static readonly SharpDX.Direct3D9.Format NV12 = D3DX.MakeFourCC((byte)'N', (byte)'V', (byte)'1', (byte)'2');
        internal static readonly SharpDX.Direct3D9.Format AYUV = D3DX.MakeFourCC((byte)'A', (byte)'Y', (byte)'U', (byte)'V');  //不支持？
        internal static readonly SharpDX.Direct3D9.Format Y210 = D3DX.MakeFourCC((byte)'Y', (byte)'2', (byte)'1', (byte)'0');  //

        public static readonly RawColorBGRA AlphaColor = new RawColorBGRA(0, 0, 0, 255);
        public static readonly RawColorBGRA WhiteColor = new RawColorBGRA(255, 255, 255, 255);

        /// <summary>
        /// 预乘 Alpha 着色器源代码。有些 png 图片的 Alpah 通道不是 Premultiplied 状态，需要预乘。
        /// </summary>
        public static readonly string PremultiplyAlphaShaderSource = @"
            sampler input : register(s0);

            float4 main(float2 texCoord : TEXCOORD) : COLOR
            {
                float4 color = tex2D(input, texCoord);

                color.rgb *= color.a;
                return color;

                //return float4(color.r * color.a / 1.0f, color.g * color.a / 1.0f, color.b * color.a / 1.0f, color.a);
            }
        ";
        /// <summary>
        /// 颜色交换着色器源代码。将 BGRA 格式的纹理转换为 RGBA 格式的纹理。
        /// </summary>
        public static readonly string SwapRBChannelShaderSource = @"
            sampler2D input : register(s0);
            
            float4 main(float2 texCoord : TEXCOORD) : COLOR
            {
                float4 color = tex2D(input, texCoord);

                return float4(color.b, color.g, color.r, color.a);
            }
        ";

        private static Direct3DEx _direct3DEx;
        private static readonly object _directLock = new object();
        /// <summary>  Direct3DEx </summary>
        internal static Direct3DEx Direct3D
        {
            get
            {
                lock (_directLock)
                {
                    if (_direct3DEx == null) Initialize();
                    return _direct3DEx;
                }
            }
        }

        private static DeviceEx _deviceEx;
        private static readonly object _deviceLock = new object();
        /// <summary>  获取共享 DeviceEx 对象  </summary>
        public static DeviceEx Device
        {
            get
            {
                lock (_deviceLock)
                {
                    if (_deviceEx == null) Initialize();
                    return _deviceEx;
                }
            }
        }

        private static Sprite _sprite;
        private static readonly object _spriteLock = new object();
        /// <summary>  获取共享 Sprite 对象  </summary>
        public static Sprite Sprite
        {
            get
            {
                lock (_spriteLock)
                {
                    if (_sprite == null) Initialize();
                    return _sprite;
                }
            }
        }

        // Shader 对象
        private static PixelShader _premultiplyShader;
        //private static readonly ConcurrentDictionary<string, Texture> RenderTargetPool = new ConcurrentDictionary<string, Texture>();


        /// <summary>
        /// 初始化 D3D9 设备
        /// </summary>
        /// <param name="windowHandle"></param>
        /// <exception cref="ArgumentNullException"></exception>
        internal static void Initialize(IntPtr windowHandle)
        {
            if (windowHandle == null || windowHandle == IntPtr.Zero)
                throw new ArgumentNullException($"windowHandle is null.");

            if (_direct3DEx != null) return;
            Trace.TraceInformation($"D3D9Shared Initialize.");

            _direct3DEx = new Direct3DEx();
            var displayMode = _direct3DEx.GetAdapterDisplayMode(0);

            var parameters = new PresentParameters
            {
                Windowed = true,
                SwapEffect = SwapEffect.Discard,
                //PresentFlags = PresentFlags.None,
                PresentFlags = PresentFlags.Video | PresentFlags.OverlayYCbCrBt709,

                DeviceWindowHandle = windowHandle,
                PresentationInterval = PresentInterval.Immediate,

                //BackBufferWidth = displayMode.Width,
                //BackBufferHeight = displayMode.Height,
                //BackBufferFormat = displayMode.Format,
            };

            _deviceEx = new DeviceEx(_direct3DEx, 0, DeviceType.Hardware, IntPtr.Zero, CreateFlags.HardwareVertexProcessing | CreateFlags.Multithreaded | CreateFlags.FpuPreserve, parameters);
            _deviceEx.TestCooperativeLevel();

#if false   // 开启 AlphaBlend
            _deviceEx.SetRenderState(RenderState.AlphaBlendEnable, true);
            _deviceEx.SetRenderState(RenderState.SourceBlend, Blend.One);
            _deviceEx.SetRenderState(RenderState.DestinationBlend, Blend.InverseSourceAlpha);
#endif
            _sprite = new Sprite(_deviceEx);

#if true
            // 像素着色器
            //var pixelShaderCode = System.IO.File.ReadAllText("PremultiplyAlpha.fx");
            //var shaderBytecode = ShaderBytecode.Compile(PremultiplyAlphaShaderSource, "main", "ps_2_0", ShaderFlags.None);
            //_premultiplyShader = new PixelShader(_deviceEx, shaderBytecode);           
#endif
            Application.Current.Exit += (s, e) => Dispose();
            AppDomain.CurrentDomain.ProcessExit += (s, e) => Dispose();

#if false
            var formats = new List<Format>();
            var array = Enum.GetValues(typeof(Format));            
            foreach (var format in array)
            {
                formats.Add((Format)format);
            }

            formats.Add(D3DFormatNV12);
            formats.Add(D3DFormatYV12);
            formats.Add(D3DFormatAYUV);
            formats.Add(D3DFormatY210);

            var adapterId = 1;
            var support = new List<Format>();
            StringBuilder builder = new StringBuilder(1024);
            foreach (var format in formats)
            {
                bool isSupportedFormat = _direct3DEx.CheckDeviceFormat(adapterId, DeviceType.Hardware, displayMode.Format, Usage.None, ResourceType.Texture, format);
                if (!isSupportedFormat) continue;

                bool IsSupportedFormatConversion = _direct3DEx.CheckDeviceFormatConversion(adapterId, DeviceType.Hardware, format, Format.X8R8G8B8);

                if (IsSupportedFormatConversion) support.Add(format);

                builder.AppendLine($"{Enum.GetName(typeof(Format), format)}({(int)format}) {(isSupportedFormat ? "√" : "×")}  {(IsSupportedFormatConversion ? "√" : "×")}");
            }
            Trace.WriteLine($"D3D9Renderer SupportedFormat: {builder.ToString()}");

            foreach (var foramt in support)
            {
                Trace.WriteLine($"D3D9Renderer SupportedFormat: {Enum.GetName(typeof(Format), foramt)}({(int)foramt})");
            }
#endif
        }
        internal static void Initialize() => Initialize(GetDesktopWindow());

        /// <summary>
        /// 创建 像素着色器
        /// </summary>
        /// <param name="shaderSource"></param>
        /// <returns></returns>
        public static PixelShader CreatePixelShader(string shaderSource)
        {
            if (_deviceEx == null) return null;

            var shaderBytecode = ShaderBytecode.Compile(shaderSource, "main", "ps_2_0", ShaderFlags.None);
            var pixelShader = new PixelShader(_deviceEx, shaderBytecode);

            return pixelShader;
        }

        /// <summary>
        /// 将 <paramref name="texture"/> 纹理绘制到 <paramref name="renderTarget"/> 目标中
        /// </summary>
        /// <param name="texture">需要渲染的动态纹理</param>
        /// <param name="renderTarget">RenderTarget </param>
        /// <param name="pixelSharer"></param>
        public static void SpriteDrawTexture(Texture texture, Surface renderTarget, PixelShader pixelSharer = null)
        {
            if (texture == null) throw new ArgumentNullException(nameof(texture));
            if (renderTarget == null) throw new ArgumentNullException(nameof(renderTarget));

#if true
            var device = Device;
            var sprite = Sprite;

            device.SetRenderTarget(0, renderTarget);            // 设置 RenderTarget
            device.Clear(ClearFlags.Target, AlphaColor, 1, 0);
            device.BeginScene();

            var lastPixelSharer = device.PixelShader; // 保存原像素着色器

            sprite.Begin(SpriteFlags.None);
            if (pixelSharer != null)
            {
                // 设置 PixelShader
                device.PixelShader = pixelSharer;
                //device.SetTexture(0, texture);    // 绑定纹理  sampler s0 ← device.SetTexture(0, xxx)
            }

            sprite.Draw(texture, WhiteColor);
            sprite.End();

            device.PixelShader = lastPixelSharer;
            device.EndScene();

#else
            Device.StretchRectangle(texture.GetSurfaceLevel(0), renderTarget, TextureFilter.Linear);
#endif
        }

        /// <summary>
        /// 清理 D3D9 设备
        /// </summary>
        internal static void Dispose()
        {
            _sprite?.Dispose();
            _sprite = null;

            _deviceEx?.Dispose();
            _deviceEx = null;

            _direct3DEx?.Dispose();
            _direct3DEx = null;

            Trace.TraceInformation($"D3D9Shared Dispose Finished.");
        }
        
        /// <summary>
        /// 测试
        /// </summary>
        /// <param name="yuv"></param>
        /// <param name="linesize"></param>
        /// <param name="surface"></param>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="ArgumentException"></exception>
        /// <exception cref="InvalidOperationException"></exception>
        public static void CopyYuv420ToSurface(IEnumerable<IntPtr> yuv, IEnumerable<int> linesize, Surface surface)
        {
            if (surface == null || surface.NativePointer == IntPtr.Zero)
                throw new ArgumentNullException(nameof(surface), "yuv or surface is null.");
            if (yuv.Count() != linesize.Count()) throw new ArgumentException("yuv 和 linesize 长度必须相同");

            var description = surface.Description;
            if (description.Format != YV12)
            {
                throw new InvalidOperationException($"surface 必须是 YV12 格式");
            }

            var surfaceData = surface.LockRectangle(LockFlags.Discard);

            try
            {
                unsafe
                {
                    if (linesize.ElementAt(0) == surfaceData.Pitch || surfaceData.Pitch % linesize.ElementAt(0) == 0)
                    {
                        var panle_y_size = linesize.ElementAt(0) * description.Height;
                        var panle_u_size = panle_y_size / 2;
                        var panle_v_size = panle_y_size / 2;

                        // Y
                        Buffer.MemoryCopy((byte*)yuv.ElementAt(0), (byte*)surfaceData.DataPointer, panle_y_size, panle_y_size);
                        // V
                        Buffer.MemoryCopy((byte*)yuv.ElementAt(2), (byte*)(surfaceData.DataPointer + panle_y_size), panle_v_size, panle_v_size);
                        // U
                        Buffer.MemoryCopy((byte*)yuv.ElementAt(1), (byte*)(surfaceData.DataPointer + panle_y_size + panle_v_size), panle_u_size, panle_u_size);
                    }
                    else
                    {
                        var dataPtr = surfaceData.DataPointer;
                        var panle_y_size = surfaceData.Pitch < linesize.ElementAt(0) ? surfaceData.Pitch : linesize.ElementAt(0);
                        var panle_u_size = surfaceData.Pitch < linesize.ElementAt(1) ? surfaceData.Pitch : linesize.ElementAt(1);
                        var panle_v_size = surfaceData.Pitch < linesize.ElementAt(2) ? surfaceData.Pitch : linesize.ElementAt(2);

                        // Y
                        for (int i = 0; i < description.Height; i++)
                        {
                            Buffer.MemoryCopy((byte*)(yuv.ElementAt(0) + i * linesize.ElementAt(0)), (byte*)dataPtr, panle_y_size, panle_y_size);
                            dataPtr += surfaceData.Pitch;
                        }

                        var uv_height = description.Height / 2;
                        var align_offset = surfaceData.Pitch / 2;
                        // V
                        for (int i = 0; i < uv_height; i++)
                        {
                            Buffer.MemoryCopy((byte*)(yuv.ElementAt(2) + i * linesize.ElementAt(2)), (byte*)dataPtr, panle_v_size, panle_v_size);
                            dataPtr += align_offset;
                        }
                        // U
                        for (int i = 0; i < uv_height; i++)
                        {
                            Buffer.MemoryCopy((byte*)(yuv.ElementAt(1) + i * linesize.ElementAt(1)), (byte*)dataPtr, panle_u_size, panle_u_size);
                            dataPtr += align_offset;
                        }
                    }
                }
            }
            finally
            {
                surface.UnlockRectangle();
            }
        }

    }
}
