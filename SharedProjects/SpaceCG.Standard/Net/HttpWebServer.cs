using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Mime;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SpaceCG.Extensions;
using Trace = SpaceCG.Diagnostics.Trace;

namespace SpaceCG.Net
{
    /// <summary>
    /// 基于 <see cref="HttpServerBase"/> 的静态文件 HTTP Web 服务，用于从本地目录提供静态资源
    /// </summary>
    /// <remarks>
    /// <para><b>线程安全：</b>本类<b>线程不安全</b>——<see cref="HttpServerBase.Start"/> /
    /// <see cref="HttpServerBase.Stop"/> / <see cref="HttpServerBase.Dispose()"/> 不可并发调用。</para>
    /// <para><b>权限要求：</b>若绑定端口 ≤ 1024（特权端口），需要以管理员权限运行。</para>
    /// <para><b>安全特性：</b>内置目录遍历攻击（Directory Traversal）防护，确保仅能访问
    /// <see cref="RootDirectory"/> 及其子目录中的文件。</para>
    /// <para><b>MIME 类型解析：</b>优先查找 <see cref="ContentMimeTypes"/> 自定义映射，
    /// 其次通过 <see cref="HttpClientExtensions.GetMimeType"/> 获取系统注册的 MIME 类型，
    /// 最后回退到 <c>application/octet-stream</c>。</para>
    /// <para><b>默认路由：</b>对以 <c>/</c> 结尾的目录请求，自动追加 <c>index.html</c>。</para>
    /// </remarks>
    public class HttpWebServer : HttpServerBase
    {
        private DirectoryInfo _rootDirectory = new DirectoryInfo(Environment.CurrentDirectory);

        /// <summary>
        /// 获取或设置文件服务的根目录
        /// </summary>
        /// <exception cref="ArgumentException">
        /// 设置的值 <c>null</c> 或目录不存在时抛出
        /// </exception>
        /// <remarks>
        /// <para>设置时自动规范化路径（移除末尾分隔符），以确保路径遍历安全检查的一致性。</para>
        /// </remarks>
        public DirectoryInfo RootDirectory
        {
            get => _rootDirectory;
            set
            {
                if (value == null || !value.Exists)
                    throw new ArgumentException("RootDirectory 不能为空且必须是一个存在的目录", nameof(value));

                // 规范化路径：移除末尾可能存在的分隔符，确保路径比较时的一致性
                string fullPath = value.FullName.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                _rootDirectory = new DirectoryInfo(fullPath);
            }
        }

        /// <summary>
        /// 自定义文件扩展名到 MIME 类型的映射（线程安全的静态字典）
        /// </summary>
        /// <remarks>
        /// <para>键为以小写点号开头的文件扩展名（如 <c>".webp"</c>），值为对应的 MIME 类型（如 <c>"image/webp"</c>）。</para>
        /// <para>此字典的优先级高于系统注册的 MIME 类型，适用于注册系统不支持的或需要覆盖的 MIME 映射。</para>
        /// <para><b>示例：</b><c>ContentMimeTypes[".webp"] = "image/webp";</c></para>
        /// </remarks>
        public static readonly ConcurrentDictionary<string, string> ContentMimeTypes = new ConcurrentDictionary<string, string>();

        /// <summary>
        /// 初始化 <see cref="HttpWebServer"/> 的新实例，使用当前工作目录作为文件根目录
        /// </summary>
        /// <remarks>
        /// <para>默认以 <see cref="Environment.CurrentDirectory"/> 作为静态文件服务的根目录。
        /// 可在构造后通过 <see cref="RootDirectory"/> 属性修改。</para>
        /// </remarks>
        public HttpWebServer()
        {
        }

        /// <summary>
        /// 初始化 <see cref="HttpWebServer"/> 的新实例，并指定自定义文件根目录
        /// </summary>
        /// <param name="rootDirectory">要作为文件服务根目录的本地目录</param>
        /// <exception cref="ArgumentException">
        /// <paramref name="rootDirectory"/> 为 <c>null</c> 或目录不存在时抛出
        /// </exception>
        public HttpWebServer(DirectoryInfo rootDirectory) : this()
        {
            RootDirectory = rootDirectory;
        }

        /// <summary>
        /// 处理 HTTP 请求上下文，将 URL 路径映射为本地文件并响应
        /// </summary>
        /// <param name="context">HTTP 监听上下文</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <remarks>
        /// <para><b>处理流程：</b></para>
        /// <list type="number">
        /// <item>验证请求 URL 非空</item>
        /// <item>规范化路径：目录请求（以 <c>/</c> 结尾）自动追加 <c>index.html</c></item>
        /// <item>将 URL 正斜杠替换为系统目录分隔符</item>
        /// <item>组合绝对路径并进行目录遍历攻击检查</item>
        /// <item>文件存在则流式返回，不存在则返回 404</item>
        /// </list>
        /// <para><b>错误响应：</b>非法路径字符 → 400；目录遍历企图 → 403；文件不存在 → 404；未处理异常 → 500。</para>
        /// </remarks>
        protected override async Task HandleContextSessionAsync(HttpListenerContext context, CancellationToken cancellationToken)
        {
            if (context == null) return;

            HttpListenerRequest request = context.Request;
            HttpListenerResponse response = context.Response;

            if (request.Url == null)
            {
                await ResponseErrorAsync(response, "Invalid Request URL", HttpStatusCode.BadRequest, cancellationToken);
                return;
            }

            string localPath = request.Url.LocalPath;
            Trace.TraceInformation($"RemoteEndPoint:{request.RemoteEndPoint}  RequestUrl:{request.Url}  HttpMethod:{request.HttpMethod}  LocalPath:{localPath}  ThreadId:{Thread.CurrentThread.ManagedThreadId}");

            // 规范化路径：处理目录请求，默认追加 index.html
            if (localPath.EndsWith("/"))
            {
                localPath = $"{localPath}index.html";
            }

            // 将 URL 的正斜杠替换为当前操作系统的目录分隔符，以便 Path.Combine 正确处理
            string relativePath = localPath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);

            try
            {
                // 组合并获取绝对路径
                string fullPath = Path.GetFullPath(Path.Combine(_rootDirectory.FullName, relativePath));
                // 防止目录遍历攻击 (Directory Traversal)，确保解析后的绝对路径严格位于 RootDirectory 之下
                if (!fullPath.StartsWith(_rootDirectory.FullName, StringComparison.OrdinalIgnoreCase))
                {
                    Trace.TraceWarning($"[HttpWebServer] Directory Traversal Attempt Blocked: {request.Url} -> {fullPath}");
                    await ResponseErrorAsync(response, "403 Forbidden: Access to this path is denied.", HttpStatusCode.Forbidden, cancellationToken);
                    return;
                }

                // 检查文件是否存在且为文件（而非目录）
                if (File.Exists(fullPath))
                {                    
                    await ResponseFileAsync(response, fullPath, cancellationToken);
                }
                else
                {
                    await ResponseErrorAsync(response, $"404 Not Found: The requested file '{localPath}' does not exist.", HttpStatusCode.NotFound, cancellationToken);
                }
            }
            catch (ArgumentException) // Path.GetFullPath 可能因非法字符抛出
            {
                await ResponseErrorAsync(response, "400 Bad Request: Invalid path characters.", HttpStatusCode.BadRequest, cancellationToken);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.TraceError($"[HttpWebServer] Context Handler Exception: ({ex.GetType().Name}){ex}");
                await ResponseErrorAsync(response, "500 Internal Server Error", HttpStatusCode.InternalServerError, cancellationToken);
            }
            finally
            {
                response.Close();
            }
        }

        /// <summary>
        /// 以流式方式响应文件内容，适用于大文件传输场景
        /// </summary>
        /// <param name="response">HTTP 响应对象</param>
        /// <param name="filePath">要发送的文件绝对路径</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <remarks>
        /// <para><b>性能特性：</b></para>
        /// <list type="bullet">
        /// <item>使用 <see cref="FileShare.Read"/> 共享模式，允许多个客户端并发读取同一文件。</item>
        /// <item>通过 <c>CopyToAsync</c> 以 80KB 缓冲区流式复制，内存占用固定，不会因文件大小增长。</item>
        /// <item>设置 <see cref="HttpListenerResponse.ContentLength64"/> 以支持客户端显示下载进度。</item>
        /// </list>
        /// <para><b>注意：</b>此方法不设置 <c>Content-Encoding</c>，文本文件的字符编码由 <c>Content-Type</c> 决定。</para>
        /// </remarks>
        private static async Task ResponseFileAsync(HttpListenerResponse response, string filePath, CancellationToken cancellationToken)
        {
            // 获取文件大小以设置 Content-Length (有助于客户端显示下载进度)
            long fileLength = new FileInfo(filePath).Length;

            response.StatusCode = (int)HttpStatusCode.OK;
            response.ContentType = GetMimeType(filePath);
            response.ContentLength64 = fileLength;
            response.ContentEncoding = Encoding.UTF8;

            // 使用 FileShare.Read 允许多个客户端同时读取同一个文件
            using (var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true))
            {
                // 流式复制到响应输出流，内存占用极小 (默认 8KB 缓冲区)
                await fileStream.CopyToAsync(response.OutputStream, 81920, cancellationToken);
            }
        }

        /// <summary>
        /// 向客户端返回纯文本格式的错误信息
        /// </summary>
        /// <param name="response">HTTP 响应对象</param>
        /// <param name="message">要返回给客户端的错误消息文本</param>
        /// <param name="statusCode">HTTP 状态码（如 400、403、404、500）</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <remarks>
        /// <para>响应内容类型为 <c>text/plain</c>，编码为 UTF-8。</para>
        /// </remarks>
        protected static async Task ResponseErrorAsync(HttpListenerResponse response, string message, HttpStatusCode statusCode, CancellationToken cancellationToken)
        {
            response.StatusCode = (int)statusCode;
            response.ContentType = MediaTypeNames.Text.Plain;
            response.ContentEncoding = Encoding.UTF8;

            byte[] messageBytes = Encoding.UTF8.GetBytes(message);
            response.ContentLength64 = messageBytes.Length;

            await response.OutputStream.WriteAsync(messageBytes, 0, messageBytes.Length, cancellationToken);
        }

        /// <summary>
        /// 根据文件路径解析对应的 MIME 类型
        /// </summary>
        /// <param name="filePath">文件路径（用于提取扩展名）</param>
        /// <returns>对应的 MIME 类型字符串</returns>
        /// <remarks>
        /// <para><b>查找优先级（从高到低）：</b></para>
        /// <list type="number">
        /// <item><see cref="ContentMimeTypes"/> 自定义映射字典</item>
        /// <item>通过 <see cref="HttpClientExtensions.GetMimeType"/> 获取系统注册的 MIME 类型</item>
        /// <item>回退值 <c>"application/octet-stream"</c>（当扩展名为空或未找到时）</item>
        /// </list>
        /// </remarks>
        public static string GetMimeType(string filePath)
        {
            string extension = Path.GetExtension(filePath).ToLowerInvariant();

            if (string.IsNullOrEmpty(extension))
            {
                return "application/octet-stream";
            }

            // 优先查找自定义字典
            if (ContentMimeTypes.TryGetValue(extension, out string customMime))
            {
                return customMime;
            }

            return HttpClientExtensions.GetMimeType(filePath);
        }
    }
}