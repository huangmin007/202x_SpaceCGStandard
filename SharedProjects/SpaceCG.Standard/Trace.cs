using System;
using System.Runtime.CompilerServices;

namespace SpaceCG
{
    /// <summary>
    /// 跨平台日志跟踪类，提供与 <see cref="System.Diagnostics.Trace"/> 兼容的静态日志方法。
    /// </summary>
    /// <remarks>
    /// <para><b>平台行为：</b></para>
    /// <list type="bullet">
    /// <item><b>.NET Framework / .NET Core</b>：所有方法直接转发到 <see cref="System.Diagnostics.Trace"/>，行为与标准 Trace API 完全一致。</item>
    /// <item><b>Unity3D</b>：通过运行时类型检测自动适配——<see cref="WriteLine(string)"/> / <see cref="TraceInformation"/> 映射到 <c>UnityEngine.Debug.Log</c>，
    /// <see cref="TraceWarning"/> 映射到 <c>UnityEngine.Debug.LogWarning</c>，<see cref="TraceError"/> 映射到 <c>UnityEngine.Debug.LogError</c>。</item>
    /// </list>
    /// <para><b>Unity 运行时检测：</b></para>
    /// <para>
    /// 静态构造函数中通过 <c>Type.GetType("UnityEngine.Application, UnityEngine")</c> 判断当前是否在 Unity 环境中运行。
    /// 无需条件编译宏（如 <c>#if UNITY_2018_1_OR_NEWER</c>），同一份 DLL 在所有平台均可正常工作。
    /// </para>
    /// <para><b>使用方式：</b></para>
    /// <para>
    /// 直接在代码中使用 <c>Trace.WriteLine("message")</c>、<c>Trace.TraceWarning("message")</c>、<c>Trace.TraceError("message")</c>，
    /// 无需关心底层平台差异。所有调用行为与 <see cref="System.Diagnostics.Trace"/> 一致。
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// Trace.WriteLine($"RPC 服务已启动，监听地址：{endPoint}");
    /// Trace.TraceInformation($"客户端 {clientEndPoint} 已连接");
    /// Trace.TraceWarning($"客户端 {clientEndPoint} 信号量已存在（异常情况）");
    /// Trace.TraceError($"启动 RPC 服务失败：({ex.GetType().Name}) {ex.Message}");
    /// </code>
    /// </example>
    /// <seealso cref="System.Diagnostics.Trace"/>
    public static partial class Trace
    {
        #region 平台检测与初始化
        /// <summary>当前是否运行在 Unity3D 环境中。</summary>
        public static readonly bool IsUnityRuntime;

        private static readonly Action<string> _unityLog;
        private static readonly Action<string> _unityLogWarning;
        private static readonly Action<string> _unityLogError;

        static Trace()
        {
            // 运行时检测 Unity 环境，避免编译时条件宏
            try
            {
                var unityAppType = Type.GetType("UnityEngine.Application, UnityEngine");
                IsUnityRuntime = unityAppType != null;
            }
            catch
            {
                IsUnityRuntime = false;
            }

            if (IsUnityRuntime)
            {
                // 反射获取 UnityEngine.Debug 静态方法，缓存为委托以提升调用性能
                var debugType = Type.GetType("UnityEngine.Debug, UnityEngine");
                if (debugType != null)
                {
                    var logMethod = debugType.GetMethod("Log", new[] { typeof(object) });
                    var logWarningMethod = debugType.GetMethod("LogWarning", new[] { typeof(object) });
                    var logErrorMethod = debugType.GetMethod("LogError", new[] { typeof(object) });

                    _unityLog = msg => logMethod?.Invoke(null, new object[] { msg });
                    _unityLogWarning = msg => logWarningMethod?.Invoke(null, new object[] { msg });
                    _unityLogError = msg => logErrorMethod?.Invoke(null, new object[] { msg });
                }

                // 极端情况回退到 Console
                _unityLog = _unityLog ?? (msg => Console.WriteLine($"[INFO] {msg}"));
                _unityLogWarning = _unityLogWarning ?? (msg => Console.WriteLine($"[WARN] {msg}"));
                _unityLogError = _unityLogError ?? (msg => Console.WriteLine($"[ERROR] {msg}"));
            }
        }
        #endregion

        #region Write / WriteLine
        /// <summary>
        /// 将消息写入跟踪侦听器，后跟行终止符。
        /// <para>使用 <see cref="System.Diagnostics.Trace.WriteLine(string)"/> 转发；Unity 下使用 <c>Debug.Log</c>。</para>
        /// </summary>
        /// <param name="message">要写入的消息。</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void WriteLine(string message)
        {
            if (IsUnityRuntime)
                _unityLog(message);
            else
                System.Diagnostics.Trace.WriteLine(message);
        }

        /// <summary>
        /// 将格式化消息写入跟踪侦听器，后跟行终止符。
        /// <para>使用 <see cref="System.Diagnostics.Trace.WriteLine(string)"/> 转发；Unity 下使用 <c>Debug.Log</c>。</para>
        /// </summary>
        /// <param name="format">复合格式字符串。</param>
        /// <param name="args">包含零个或多个要格式化的对象的数组。</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void WriteLine(string format, params object[] args)
        {
            WriteLine((args != null && args.Length > 0) ? string.Format(format, args) : format);
        }

        /// <summary>
        /// 将消息写入跟踪侦听器。
        /// <para>使用 <see cref="System.Diagnostics.Trace.Write(string)"/> 转发；Unity 下等同于 <c>Debug.Log</c>（自动追加行终止符）。</para>
        /// </summary>
        /// <param name="message">要写入的消息。</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Write(string message)
        {
            if (IsUnityRuntime)
                _unityLog(message);
            else
                System.Diagnostics.Trace.Write(message);
        }

        /// <summary>
        /// 将格式化消息写入跟踪侦听器。
        /// <para>使用 <see cref="System.Diagnostics.Trace.Write(string)"/> 转发；Unity 下等同于 <c>Debug.Log</c>（自动追加行终止符）。</para>
        /// </summary>
        /// <param name="format">复合格式字符串。</param>
        /// <param name="args">包含零个或多个要格式化的对象的数组。</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Write(string format, params object[] args)
        {
            Write((args != null && args.Length > 0) ? string.Format(format, args) : format);
        }
        #endregion

        #region TraceInformation / TraceWarning / TraceError
        /// <summary>
        /// 将信息性消息写入跟踪侦听器。
        /// <para>使用 <see cref="System.Diagnostics.Trace.TraceInformation(string)"/> 转发；Unity 下使用 <c>Debug.Log</c>。</para>
        /// </summary>
        /// <param name="message">要写入的信息性消息。</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void TraceInformation(string message)
        {
            if (IsUnityRuntime)
                _unityLog(message);
            else
                System.Diagnostics.Trace.TraceInformation(message);
        }

        /// <summary>
        /// 将警告消息写入跟踪侦听器。
        /// <para>使用 <see cref="System.Diagnostics.Trace.TraceWarning(string)"/> 转发；Unity 下使用 <c>Debug.LogWarning</c>。</para>
        /// </summary>
        /// <param name="message">要写入的警告消息。</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void TraceWarning(string message)
        {
            if (IsUnityRuntime)
                _unityLogWarning(message);
            else
                System.Diagnostics.Trace.TraceWarning(message);
        }

        /// <summary>
        /// 将错误消息写入跟踪侦听器。
        /// <para>使用 <see cref="System.Diagnostics.Trace.TraceError(string)"/> 转发；Unity 下使用 <c>Debug.LogError</c>。</para>
        /// </summary>
        /// <param name="message">要写入的错误消息。</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void TraceError(string message)
        {
            if (IsUnityRuntime)
                _unityLogError(message);
            else
                System.Diagnostics.Trace.TraceError(message);
        }
        #endregion
    }
}
