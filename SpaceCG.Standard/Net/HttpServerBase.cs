using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using SpaceCG.Extensions;
using Trace = SpaceCG.Diagnostics.Trace;

namespace SpaceCG.Net
{   
    /// <summary>
    /// 基于 <see cref="HttpListener"/> 的简单 HTTP 服务基类，负责接收并分发请求。
    /// </summary>
    /// <remarks>
    /// <para><b>线程安全：</b>本类<b>线程不安全</b>——<see cref="Start"/>/<see cref="Stop"/>/<see cref="Dispose(bool)"/> 不可并发调用；
    /// 监听主循环与并发请求处理内部对 <see cref="HttpListener"/> 的调用由 .NET 保证线程安全。</para>
    /// <para><b>子类约定：</b>必须实现 <see cref="HandleContextSessionAsync"/>，并在其中写入响应并调用 <c>Response.Close()</c>，
    /// 否则客户端可能无限等待；所有未处理异常都会被基类捕获并转为 500 响应，不会导致进程崩溃。</para>
    /// </remarks>
    public abstract class HttpServerBase
    {
        private Task _listenTask;
        private HttpListener _listener;
        private CancellationTokenSource _cts;

#if false
        /// <summary>
        /// 获取底层的 <see cref="HttpListener"/> 实例
        /// </summary>
        /// <remarks>
        /// <para><b>生命周期：</b>仅在 <see cref="Start"/> 调用之后、<see cref="Dispose(bool)"/> 调用之前有效。
        /// 服务停止或释放后，此属性可能为 <c>null</c>。</para>
        /// <para><b>注意：</b>不要在外部直接修改此侦听器的配置（如 Prefixes），应通过 <see cref="Start"/> 方法统一管理。</para>
        /// </remarks>
        protected HttpListener Listener
        {
            get => _listener;
            private set => _listener = value;
        }
#endif

        /// <summary>
        /// 获取当前服务监听的本机端口号
        /// </summary>
        /// <remarks>
        /// <para>此值在 <see cref="Start"/> 成功后设置，<see cref="Stop"/> 或 <see cref="Dispose()"/> 后保持不变，
        /// 可用于记录日志或外部查询。</para>
        /// </remarks>
        public ushort LocalPort { get; private set; }

        /// <summary>
        /// 获取一个值，指示服务是否正在运行（监听中）
        /// </summary>
        /// <remarks>
        /// <para>仅当以下三个条件同时满足时返回 <c>true</c>：</para>
        /// <list type="bullet">
        /// <item>取消令牌源已创建（<c>_cts != null</c>）</item>
        /// <item>侦听器已初始化（<c>_listener != null</c>）</item>
        /// <item>侦听器正在监听（<c>_listener.IsListening == true</c>）</item>
        /// </list>
        /// </remarks>
        public bool IsRunning => _cts != null && _listener != null && _listener.IsListening;

        /// <summary>
        /// 初始化 <see cref="HttpServerBase"/> 的新实例
        /// </summary>
        /// <remarks>
        /// <para>构造函数会检查当前操作系统是否支持 <see cref="HttpListener"/>。
        /// 若不支持（如部分 Windows Server Core 版本），将记录警告但不抛出异常，
        /// 后续调用 <see cref="Start"/> 将静默返回。</para>
        /// </remarks>
        public HttpServerBase()
        {
            if (!HttpListener.IsSupported)
            {
                Trace.TraceWarning($"{this.GetType().Name} 当前操作系统不支持 HttpListener");
                return;
            }

            _listener = new HttpListener();
        }

        /// <summary>
        /// 释放由 <see cref="HttpServerBase"/> 占用的所有资源
        /// </summary>
        /// <remarks>
        /// <para>调用流程：取消令牌 → 停止监听并关闭侦听器 → 等待后台任务退出（最多 3 秒）→ 释放托管资源。</para>
        /// </remarks>
        public virtual void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// 释放由 <see cref="HttpServerBase"/> 占用的非托管资源（可选释放托管资源）
        /// </summary>
        /// <param name="disposing">
        /// <c>true</c> 表示同时释放托管资源和非托管资源；
        /// <c>false</c> 表示仅释放非托管资源（由终结器调用）
        /// </param>
        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                try
                {
                    // 1. 触发取消信号
                    _cts?.Cancel();

                    // 2. 停止监听并关闭 (Close 内部会调用 Stop 和 Abort，释放底层资源)
                    if (_listener != null)
                    {
                        _listener.Stop();
                        _listener.Close();
                    }

                    // 3. 等待监听任务优雅退出 (最多等待 3 秒，防止死锁)
                    if (_listenTask != null)
                    {
                        _listenTask.Wait(TimeSpan.FromSeconds(3));
                    }
                }
                catch (Exception ex)
                {
                    Trace.TraceError($"{this.GetType().Name} Dispose Failed: ({ex.GetType().Name}){ex.Message}");
                }
                finally
                {
                    _cts?.Dispose();
                    _cts = null;
                    _listener = null;
                    _listenTask = null;
                }
            }
        }

        /// <summary>
        /// 启动 HTTP 服务并开始监听指定端口
        /// </summary>
        /// <param name="localPort">要监听的本地端口号，有效范围 1-65535，默认值为 8080。
        /// 端口 ≤ 1024 为特权端口，需要管理员权限才能绑定。</param>
        /// <remarks>
        /// <para><b>绑定策略：</b>自动绑定 <c>localhost</c>、<c>127.0.0.1</c> 以及本机所有已启用的 IPv4 单播地址。</para>
        /// <para><b>重入安全：</b>若服务已在运行，调用此方法将静默返回（幂等操作）。</para>
        /// <para><b>操作系统兼容性：</b>若当前操作系统不支持 <see cref="HttpListener"/>，将记录警告后返回。</para>
        /// <para>成功启动后，会在后台启动异步监听循环 <see cref="LoopListenerContextAsync"/>。</para>
        /// </remarks>
        public virtual void Start(ushort localPort = 8080)
        {
            if (!HttpListener.IsSupported)
            {
                Trace.TraceWarning($"{this.GetType().Name} 当前操作系统不支持 HttpListener");
                return;
            }

            if (localPort == 0 || localPort > 65535)
            {
                throw new ArgumentOutOfRangeException(nameof(localPort), "端口号必须在 1-65535 之间");
            }

            if (IsRunning) return;

            var instanceType = this.GetType();            
            if (localPort <= 1024)
            {
                Trace.TraceWarning($"{instanceType} 使用特权端口 ({localPort})，请确保程序以管理员权限运行。");
            }

            try
            {
                _listener.Prefixes.Clear();

                _listener.Prefixes.Add($"http://localhost:{localPort}/");
                _listener.Prefixes.Add($"http://127.0.0.1:{localPort}/");

                var ips = NetworkExtensions.GetLocalIPv4Addresses();
                foreach (var ip in ips)
                {
                    _listener.Prefixes.Add($"http://{ip}:{localPort}/");
                    Trace.TraceInformation($"{instanceType} 绑定 IP 地址：{ip}:{localPort}");
                }

                _listener.Start();
                LocalPort = localPort;
                Trace.TraceInformation($"{instanceType} 启动成功，监听端口：{localPort}");
            }
            catch (Exception ex)
            {
                Trace.TraceError($"{instanceType} 启动失败：({ex.GetType().Name}){ex.Message}");
                return;
            }            

            _cts = new CancellationTokenSource();
            var cancellationToken = _cts.Token;

            _listenTask = Task.Run(() => LoopListenerContextAsync(cancellationToken), cancellationToken);
        }

        /// <summary>
        /// 停止 HTTP 服务并等待后台监听任务退出
        /// </summary>
        /// <remarks>
        /// <para><b>关闭序列：</b>触发取消令牌 → 调用 <see cref="HttpListener.Stop"/> 中断 <c>GetContextAsync</c>
        /// → 等待后台任务退出（最多 500ms）→ 释放取消令牌源。</para>
        /// <para><b>重入安全：</b>若服务未在运行，调用此方法将静默返回（幂等操作）。</para>
        /// <para><b>注意：</b>此方法不会释放 <see cref="Listener"/> 实例，调用后仍可重新 <see cref="Start"/>。
        /// 如需彻底释放资源，请调用 <see cref="Dispose()"/>。</para>
        /// </remarks>
        public virtual void Stop()
        {
            if (!HttpListener.IsSupported)
            {
                Trace.TraceWarning($"{this.GetType().Name} 当前操作系统不支持 HttpListener");
                return;
            }

            if (!IsRunning) return;

            try
            {
                _cts?.Cancel();
                _listener.Stop(); // Stop 会使得 GetContextAsync 抛出 995 错误，从而优雅退出循环

                Trace.TraceInformation($"{this.GetType().Name} Stopped.");
            }
            catch (Exception ex)
            {
                Trace.TraceError($"{this.GetType().Name} Stop Failed: ({ex.GetType().Name}){ex.Message}");
            }
            finally
            {
                _cts?.Dispose();
                _cts = null;
            }

            try
            {
                _listenTask?.Wait(500);
            }
            finally
            {
                _listenTask?.Dispose();
                _listenTask = null;
            }
        }

        /// <summary>
        /// HTTP 监听主循环（后台任务）
        /// </summary>
        /// <param name="cancellationToken">用于取消监听循环的令牌</param>
        /// <remarks>
        /// <para><b>循环逻辑：</b>在取消令牌未触发且侦听器处于监听状态时，循环调用
        /// <see cref="HttpListener.GetContextAsync"/> 等待客户端连接。</para>
        /// <para><b>异常处理策略：</b></para>
        /// <list type="bullet">
        /// <item><b>错误码 995（ERROR_OPERATION_ABORTED）：</b>由 <see cref="HttpListener.Stop"/> 触发，视为正常退出信号。</item>
        /// <item><b>ObjectDisposedException：</b>侦听器已被释放，视为正常退出信号。</item>
        /// <item><b>其他异常：</b>记录警告日志后继续监听，防止偶发网络波动导致服务中断。</item>
        /// </list>
        /// <para>每个接收到的请求上下文通过 <c>_ = SafeHandleContextSessionAsync(...)</c> 以"即发即弃"方式
        /// 异步处理，避免阻塞监听循环。</para>
        /// </remarks>
        private async Task LoopListenerContextAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                HttpListenerContext context;
                try
                {
                    context = await _listener.GetContextAsync().ConfigureAwait(false);
                }
                catch (HttpListenerException ex) when (ex.ErrorCode == 995)
                {
                    // 995 = ERROR_OPERATION_ABORTED。这是调用 Stop/Close 时的预期行为，优雅退出。
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    // 其他网络异常，记录日志并继续监听 (防止偶发网络抖动导致服务停止)
                    Trace.TraceWarning($"{this.GetType().Name} GetContextAsync Failed: ({ex.GetType().Name}){ex.Message}");
                    continue;
                }

                _ = SafeHandleContextSessionAsync(context, cancellationToken);
                //_ = Task.Run(() => SafeHandleContextSessionAsync(context, cancellationToken), cancellationToken);
            }
        }

        /// <summary>
        /// 安全地处理 HTTP 上下文，提供异常隔离层
        /// </summary>
        /// <param name="context">当前请求的 HTTP 监听上下文</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <remarks>
        /// <para>此方法作为子类 <see cref="HandleContextSessionAsync"/> 的包装器，提供以下保障：</para>
        /// <list type="bullet">
        /// <item>捕获 <see cref="OperationCanceledException"/>：服务关闭期间的取消操作静默忽略。</item>
        /// <item>捕获所有未处理异常：防止子类异常导致进程崩溃或请求静默挂起。</item>
        /// <item>兜底 500 响应：若响应输出流仍可写入，向客户端返回 HTTP 500 错误，避免客户端无限等待。</item>
        /// </list>
        /// </remarks>
        private async Task SafeHandleContextSessionAsync(HttpListenerContext context, CancellationToken cancellationToken)
        {
            try
            {
                // 调用子类实现的具体处理逻辑
                await HandleContextSessionAsync(context, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // 服务正在关闭，忽略取消异常
            }
            catch (Exception ex)
            {
                // 捕获子类未处理的异常，防止进程崩溃或请求静默挂起
                Trace.TraceError($"{this.GetType().Name} ContextHandlerAsync Failed: ({ex.GetType().Name}){ex}");

                // 尽可能向客户端返回 500 错误，避免客户端无限期等待
                if (context.Response.OutputStream.CanWrite)
                {
                    try
                    {
                        context.Response.StatusCode = 500;
                        context.Response.StatusDescription = "Internal Server Error";
                    }
                    catch
                    {
                        // 忽略关闭响应时的异常 (客户端可能已断开连接)
                    }
                    finally
                    {
                        context.Response.Close();
                    }
                }
            }
        }

        /// <summary>
        /// HTTP 请求上下文处理器（由子类实现具体的请求路由与响应逻辑）
        /// </summary>
        /// <param name="context">包含请求和响应对象的 HTTP 监听上下文</param>
        /// <param name="cancellationToken">取消令牌，用于检测服务关闭信号并中断长时间运行的操作</param>
        /// <remarks>
        /// <para><b>子类实现约定：</b></para>
        /// <list type="bullet">
        /// <item>必须在此方法内完成对 <paramref name="context"/> 的请求解析和响应写入。</item>
        /// <item>必须调用 <c>context.Response.Close()</c> 或在 <c>finally</c> 块中确保响应被关闭，
        /// 否则客户端将无限等待。</item>
        /// <item>建议在耗时操作（如文件读取、数据库查询）中检查 <paramref name="cancellationToken"/>，
        /// 以支持服务优雅关闭。</item>
        /// <item>此方法抛出的未处理异常会被 <see cref="SafeHandleContextSessionAsync"/> 捕获，
        /// 并自动向客户端返回 HTTP 500 错误。</item>
        /// </list>
        /// </remarks>
        protected abstract Task HandleContextSessionAsync(HttpListenerContext context, CancellationToken cancellationToken);

        /// <summary>
        /// 返回当前实例的类型名称字符串表示
        /// </summary>
        /// <returns>当前类型名称（如 <c>"HttpWebServer"</c>）</returns>
        public override string ToString() => this.GetType().Name;
    }
}