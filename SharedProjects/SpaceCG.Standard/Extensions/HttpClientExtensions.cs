using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace SpaceCG.Extensions
{
    /// <summary>
    /// 提供针对 <see cref="HttpClient"/> 的扩展方法，支持带进度报告的文件上传和下载。
    /// </summary>
    public static partial class HttpClientExtensions
    {
        /// <summary>
        /// 异步下载文件，支持进度报告、取消令牌及安全的临时文件替换机制。
        /// </summary>
        /// <param name="httpClient">The <see cref="HttpClient"/> instance.</param>
        /// <param name="url">要下载的文件的 URL。</param>
        /// <param name="filePath">保存文件的本地完整路径。</param>
        /// <param name="bufferSize">读写缓冲区大小（字节）。默认 128KB。范围：1KB ~ 10MB。</param>
        /// <param name="progress">用于报告下载进度的接口 (0.0 到 1.0)。如果服务器未返回 Content-Length，则不报告进度。</param>
        /// <param name="cancellationToken">用于传播取消操作的通知的取消令牌。</param>
        /// <returns>成功时返回保存的文件完整路径；失败或被取消时返回 <see cref="string.Empty"/>。</returns>
        /// <exception cref="ArgumentNullException">当 <paramref name="httpClient"/>、<paramref name="url"/> 或 <paramref name="filePath"/> 为 null 或空白时抛出。</exception>
        /// <exception cref="ArgumentOutOfRangeException">当 <paramref name="bufferSize"/> 不在有效范围内时抛出。</exception>
        public static async Task<string> DownloadAsync(this HttpClient httpClient, string url, string filePath, int bufferSize = 1024 * 128, IProgress<double> progress = null, CancellationToken cancellationToken = default)
        {
            if (httpClient == null) throw new ArgumentNullException(nameof(httpClient));
            if (string.IsNullOrWhiteSpace(url)) throw new ArgumentNullException(nameof(url));
            if (string.IsNullOrWhiteSpace(filePath)) throw new ArgumentNullException(nameof(filePath));
            if (bufferSize < 1024 || bufferSize > 10 * 1024 * 1024) throw new ArgumentOutOfRangeException(nameof(bufferSize), "Buffer size must be 1KB ~ 10MB");

            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory)) Directory.CreateDirectory(directory);

            var isException = true;
            var tempFileName = $"{filePath}.tmp";
            var internalBufferSize = Math.Min(bufferSize, 8192);

            try
            {
                using (var response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
                {
                    response.EnsureSuccessStatusCode();

                    var totalBytes = response.Content.Headers.ContentLength ?? 0L;
                    using (var contentStream = await response.Content.ReadAsStreamAsync())
                    using (var fileStream = new FileStream(tempFileName, FileMode.Create, FileAccess.Write, FileShare.None, internalBufferSize, useAsync: true))
                    {
                        var bytesRead = 0;
                        var downloadedBytes = 0L;
                        var buffer = new byte[bufferSize];
                        var stopwatch = Stopwatch.StartNew();

                        while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
                        {
                            await fileStream.WriteAsync(buffer, 0, bytesRead, cancellationToken);
                            downloadedBytes += bytesRead;

                            if (progress != null && totalBytes > 0 && stopwatch.ElapsedMilliseconds >= 16)
                            {
                                var percent = (double)downloadedBytes / totalBytes;

                                stopwatch.Restart();
                                progress.Report(percent);
                            }
                        }
                        
                        stopwatch.Start();
                        await fileStream.FlushAsync(cancellationToken);
                    }

                    // 重命名文件
                    if (!File.Exists(filePath))
                    {
                        File.Move(tempFileName, filePath);
                    }
                    else
                    {
                        var backFileName = $"{filePath}.bak";
                        if (File.Exists(backFileName)) File.Delete(backFileName);
                        File.Replace(tempFileName, filePath, backFileName);
                    }

                    isException = false;
                    progress?.Report(1.0);  // 100%
                    return filePath;
                }

            }
            catch (Exception ex) when (ex is OperationCanceledException || ex is ObjectDisposedException)
            {
                Trace.TraceWarning($"文件 {url} 下载取消：{ex.Message}");
            }
            catch (Exception ex)
            {
                Trace.TraceError($"文件 {url} 下载异常：{ex.Message}");
                throw;
            }
            finally
            {
                if (isException)
                {
                    try
                    {
                        if (File.Exists(tempFileName)) File.Delete(tempFileName);
                    }
                    catch (Exception ex)
                    {
                        Trace.TraceError($"删除临时文件文件 {tempFileName} 异常：{ex.Message}");
                    }
                }
            }

            return string.Empty;
        }

        /// <summary>
        /// 异步上传文件，支持进度报告。
        /// </summary>
        /// <param name="httpClient">The <see cref="HttpClient"/> instance.</param>
        /// <param name="url">接收上传的服务器 URL。</param>
        /// <param name="filePath">要上传的本地文件完整路径。</param>
        /// <param name="formFieldName">multipart/form-data 中的表单字段名称。</param>
        /// <param name="bufferSize">读取缓冲区大小（字节）。默认 128KB。范围：1KB ~ 10MB。</param>
        /// <param name="progress">用于报告上传进度的接口 (0.0 到 1.0)。</param>
        /// <param name="cancellationToken">用于传播取消操作的通知的取消令牌。</param>
        /// <returns>成功时返回 <see cref="HttpResponseMessage"/>；失败时返回 <see langword="null"/>。</returns>
        /// <exception cref="ArgumentNullException">当必要参数为 null 或空白时抛出。</exception>
        /// <exception cref="FileNotFoundException">当指定的文件不存在时抛出。</exception>
        /// <exception cref="ArgumentOutOfRangeException">当 <paramref name="bufferSize"/> 不在有效范围内时抛出。</exception>
        public static async Task<HttpResponseMessage> UploadAsync(this HttpClient httpClient, string url, string filePath, string formFieldName, int bufferSize = 1024 * 128, IProgress<double> progress = null, CancellationToken cancellationToken = default)
        {
            if (httpClient == null) throw new ArgumentNullException(nameof(httpClient));
            if (string.IsNullOrWhiteSpace(url)) throw new ArgumentNullException(nameof(url));
            if (string.IsNullOrWhiteSpace(filePath)) throw new ArgumentNullException(nameof(filePath));
            if (string.IsNullOrWhiteSpace(formFieldName)) throw new ArgumentNullException(nameof(formFieldName));

            if (!File.Exists(filePath)) throw new FileNotFoundException("文件不存在", filePath);
            if (bufferSize < 1024 || bufferSize > 10 * 1024 * 1024) throw new ArgumentOutOfRangeException(nameof(bufferSize), "Buffer size must be 1KB ~ 10MB");

            const int InternalBufferSize = 1024 * 8;

            try
            {
                using (var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, InternalBufferSize, useAsync: true))
                {
                    using (var streamContent = new ProgressableStreamContent(fileStream, bufferSize, progress: progress, cancellationToken: cancellationToken))
                    {
                        streamContent.Headers.ContentType = new MediaTypeHeaderValue(GetMimeType(filePath));

                        using (var content = new MultipartFormDataContent())
                        {
                            content.Add(streamContent, formFieldName, Path.GetFileName(filePath));
                            return await httpClient.PostAsync(url, content, cancellationToken);
                        }
                    }
                }
            }
            catch (Exception ex) when (ex is OperationCanceledException || ex is ObjectDisposedException)
            {
                Trace.TraceWarning($"文件 {url} 上传取消：{ex.Message}");
            }
            catch (Exception ex)
            {
                Trace.TraceError($"文件 {filePath} 上传异常：{ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// 根据文件扩展名获取对应的标准 MIME 类型。
        /// </summary>
        /// <param name="file">文件路径或文件名。</param>
        /// <returns>匹配的 MIME 类型字符串；如果未知，则返回 "application/octet-stream"。</returns>
        public static string GetMimeType(string file)
        {
            if (string.IsNullOrWhiteSpace(file)) 
                return "application/octet-stream";

            string ext = Path.GetExtension(file).ToLowerInvariant();

            switch (ext)
            {
                // text
                case ".htm":
                case ".html": return "text/html";
                case ".txt": return "text/plain";
                case ".css": return "text/css";
                case ".csv": return "text/csv";
                case ".md": return "text/markdown";

                // application
                case ".pdf": return "application/pdf";
                case ".js": return "application/javascript";
                case ".xml": return "application/xml";
                case ".xls": return "application/vnd.ms-excel";
                case ".xlsx": return "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
                case ".json": return "application/json";
                case ".wasm": return "application/wasm";
                case ".rar": return "application/vnd.rar";
                case ".zip": return "application/zip";
                case ".gz": return "application/x-gzip";
                case ".7z": return "application/x-7z-compressed";
                case ".tar": return "application/x-tar";

                // 字体
                case ".otf": return "font/otf";
                case ".ttf": return "font/ttf";
                case ".woff": return "font/woff";
                case ".woff2": return "font/woff2";

                // 图像
                case ".gif": return "image/gif";
                case ".svg": return "image/svg+xml";
                case ".png": return "image/png";
                case ".jpg":
                case ".jpeg": return "image/jpeg";
                case ".bmp": return "image/bmp";
                case ".webp": return "image/webp";
                case ".ico": return "image/x-icon";

                // 音频
                case ".mp3": return "audio/mpeg";
                case ".wav": return "audio/wav";
                case ".ogg": return "audio/ogg";
                case ".aac": return "audio/aac";
                case ".weba": return "audio/webm";

                // 视频
                case ".mp4": return "video/mp4";
                case ".webm": return "video/webm";
                case ".mov": return "video/quicktime";
                case ".ogv": return "video/ogg";
                case ".mpg":
                case ".mpeg": return "video/mpeg";
                case ".avi": return "video/x-msvideo";

                // 其他
                default: return "application/octet-stream";
            }
        }

    }

    /// <summary>
    /// 自定义的 <see cref="HttpContent"/> 实现，用于在异步流式传输（上传）时报告进度。
    /// </summary>
    public class ProgressableStreamContent : HttpContent
    {
        private readonly int _bufferSize;
        private readonly Stream _innerStream;

        private readonly IProgress<double> _progress;
        private readonly CancellationToken _cancellationToken;

        /// <summary>
        /// 初始化 <see cref="ProgressableStreamContent"/> 实例。
        /// </summary>
        /// <param name="innerStream">要读取并上传的基础流。</param>
        /// <param name="bufferSize">读取缓冲区大小（字节）。</param>
        /// <param name="progress">进度报告接口。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <exception cref="ArgumentNullException">当 <paramref name="innerStream"/> 为 null 时抛出。</exception>
        public ProgressableStreamContent(Stream innerStream, int bufferSize = 1024 * 128, IProgress<double> progress = null, CancellationToken cancellationToken = default)
        {
            if (innerStream == null) throw new ArgumentNullException(nameof(innerStream));

            _innerStream = innerStream;
            _bufferSize = bufferSize;
            _progress = progress;
            _cancellationToken = cancellationToken;

            // 复制头部信息
            if (innerStream.CanSeek)
            {
                Headers.ContentLength = innerStream.Length;
            }
        }

        /// <inheritdoc /> 
        protected override async Task SerializeToStreamAsync(Stream targetStream, TransportContext context)
        {
            var buffer = new byte[_bufferSize];
            var totalBytes = _innerStream.CanSeek ? _innerStream.Length : 0L;

            var bytesRead = 0;
            var reportedBytes = 0L;
            var stopwatch = Stopwatch.StartNew();

            try
            {
                while ((bytesRead = await _innerStream.ReadAsync(buffer, 0, _bufferSize, _cancellationToken)) > 0)
                {
                    await targetStream.WriteAsync(buffer, 0, bytesRead, _cancellationToken);
                    reportedBytes += bytesRead;

                    if (_progress != null && totalBytes > 0 && stopwatch.ElapsedMilliseconds >= 16)
                    {
                        var percent = (double)reportedBytes / totalBytes;

                        stopwatch.Restart();
                        _progress.Report(percent);
                    }
                }
                await targetStream.FlushAsync(_cancellationToken);

                _progress?.Report(1.0);
            }
            catch (Exception ex) when (ex is OperationCanceledException || ex is ObjectDisposedException)
            {
                Trace.TraceError($"文件上传取消：{ex.Message}");
            }
            catch (Exception ex)
            {
                Trace.TraceError($"文件上传异常：{ex.Message}");
            }
            finally
            {
                stopwatch.Stop();
            }
        }

        /// <inheritdoc /> 
        protected override bool TryComputeLength(out long length)
        {
            if (_innerStream.CanSeek)
            {
                length = _innerStream.Length;
                return true;
            }

            length = 0;
            return false;
        }

        /// <inheritdoc />
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
            }
            base.Dispose(disposing);
        }
    }

}