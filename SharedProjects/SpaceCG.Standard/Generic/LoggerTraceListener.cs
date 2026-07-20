using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using System.Diagnostics;
using Trace = SpaceCG.Diagnostics.Trace;

namespace SpaceCG.Generic
{
    /// <summary>
    /// 异步日志跟踪侦听器，将 <see cref="System.Diagnostics.Trace"/> 输出写入文件和控制台。
    /// <para>核心特性：</para>
    /// <list type="bullet">
    /// <item><b>异步写入</b>：所有日志通过无锁队列异步消费，不阻塞调用线程。</item>
    /// <item><b>批量 Flush</b>：通过 <see cref="AutoFlushInterval"/> 控制磁盘刷新频率，减少 I/O 开销。</item>
    /// <item><b>文件轮转</b>：自动按 <see cref="FileSizeLimit"/> 分割日志文件，按 <see cref="FileBackupDays"/> 清理过期备份。</item>
    /// <item><b>控制台彩色输出</b>：在控制台程序中按日志级别显示不同颜色。</item>
    /// <item><b>调用者信息</b>：通过 <see cref="EnableCallerInfo"/> 控制是否捕获调用栈（方法名/行号）。</item>
    /// </list>
    /// <para>线程安全：所有公开方法均为线程安全。内部使用单线程消费队列，无锁竞争。</para>
    /// </summary>
    /// <example>
    /// <code>
    /// // 使用默认配置（自动生成日志文件路径）
    /// var listener = new LoggerTraceListener();
    /// Trace.Listeners.Add(listener);
    /// Trace.TraceInformation("服务已启动");
    ///
    /// // 自定义日志路径
    /// var listener2 = new LoggerTraceListener("D:/logs/myapp.log", "MyLogger");
    /// </code>
    /// </example>
    public class LoggerTraceListener : System.Diagnostics.TraceListener
    {
        #region 公共属性
        /// <summary>
        /// 文件存储的最大字节数，默认 2 MB。
        /// <para>文件大小超过该值时自动备份并创建新文件继续写入。</para>
        /// <para>设置为 &lt;= 0 时不限制文件大小。</para>
        /// </summary>
        public int FileSizeLimit { get; set; } = 1024 * 1024 * 2;

        /// <summary>
        /// 备份文件保留的最大天数，默认 30 天。
        /// <para>超过该天数的备份文件将被自动删除。</para>
        /// <para>设置为 &lt;= 0 时不限制备份时间。</para>
        /// </summary>
        public int FileBackupDays { get; set; } = 30;
        
        /// <summary>
        /// 写入多少条日志后自动调用一次 <see cref="Stream.Flush"/>，默认 4 条。
        /// <para>设置 &lt;= 0 时每条日志写入后立即刷新。增大此值可减少磁盘 I/O 频率，但增加断电丢失风险。</para>
        /// </summary>
        public int AutoFlushInterval { get; set; } = 4;

        /// <summary>
        /// 是否捕获调用者信息（方法名、类型名、行号）。
        /// <para>启用时会使用 <see cref="StackTrace"/> 获取调用栈，有一定性能开销。
        /// 建议生产环境关闭，开发/调试环境开启。</para>
        /// <para>默认值：<c>true</c>（调试模式）或 <c>false</c>（Release 模式）。</para>
        /// </summary>
        public bool EnableCallerInfo { get; set; }
#if DEBUG
            = true;
#else
            = false;
#endif
        #endregion

        #region 私有字段
        private bool _isDisposed = false;
        private bool _isConsoleProgram = false;
        private volatile bool _isFlushRequested = false;

        private Task _writerTask;
        private string _fileName;
        private TextWriter _writer;

        private SynchronizationContext _syncContext;
        private CancellationTokenSource _cancelTokenSource;
        private readonly ConcurrentQueue<TraceMessage> _messageQueue = new ConcurrentQueue<TraceMessage>();
        #endregion

        /// <summary>
        /// 跟踪消息入队时触发的事件，可用于外部监控或实时日志展示。
        /// <para>注意：事件通过 <see cref="SynchronizationContext.Post"/> 封送回创建线程。</para>
        /// </summary>
        public event EventHandler<TraceMessage> TraceMessageEnqueued;

        #region 静态字典
        /// <summary>
        /// 跟踪事件类型对应的日志级别名称。
        /// </summary>
        public static readonly IReadOnlyDictionary<TraceEventType, string> EventTypeNames = new Dictionary<TraceEventType, string>
        {
            { TraceEventType.Transfer,   "TRANS" },
            { TraceEventType.Resume,     "RESUME" },
            { TraceEventType.Suspend,    "SUSPEND" },
            { TraceEventType.Stop,       "STOP" },
            { TraceEventType.Start,      "START" },
            { TraceEventType.Verbose,    "DEBUG" },
            { TraceEventType.Information,"INFO" },
            { TraceEventType.Warning,    "WARN" },
            { TraceEventType.Error,      "ERROR" },
            { TraceEventType.Critical,   "FATAL" }
        };
        /// <summary>
        /// 跟踪事件类型对应的控制台颜色。
        /// </summary>
        public static readonly IReadOnlyDictionary<TraceEventType, ConsoleColor> EventTypeConsoleColors = new Dictionary<TraceEventType, ConsoleColor>
        {
            { TraceEventType.Transfer,   ConsoleColor.DarkYellow },
            { TraceEventType.Resume,     ConsoleColor.Magenta },
            { TraceEventType.Suspend,    ConsoleColor.Magenta },
            { TraceEventType.Stop,       ConsoleColor.Cyan },
            { TraceEventType.Start,      ConsoleColor.Cyan },
            { TraceEventType.Verbose,    ConsoleColor.Green },
            { TraceEventType.Information,ConsoleColor.Gray },
            { TraceEventType.Warning,    ConsoleColor.Yellow },
            { TraceEventType.Error,      ConsoleColor.Red },
            { TraceEventType.Critical,   ConsoleColor.DarkRed }
        };
        #endregion

        #region 构造函数
        /// <summary>
        /// 创建侦听器，自动以当前模块名和日期生成日志文件路径。
        /// <para>日志文件格式：<c>{模块目录}/logs/{yyyy-MM-dd}.{模块名}.log</c></para>
        /// </summary>
        /// <param name="enabledCallerInfo"> <see cref="EnableCallerInfo"/> 是否启用调用者信息。</param>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public LoggerTraceListener(bool enabledCallerInfo = false) : base(nameof(LoggerTraceListener))
        {
            if (string.IsNullOrWhiteSpace(_fileName))
            {
                var moduleFile = new FileInfo(GetProcessMainModulePath());
                _fileName = Path.Combine(moduleFile.DirectoryName, "logs", $"{DateTime.Now:yyyy-MM-dd}.{moduleFile.Name.Replace(moduleFile.Extension, ".log")}");
            }

            EnableCallerInfo = enabledCallerInfo;

            Initialize();
        }

        /// <summary>
        /// 创建侦听器并指定日志文件路径。
        /// </summary>
        /// <param name="fileName">日志文件的完整路径。</param>
        /// <param name="name">侦听器名称。</param>
        /// <param name="enabledCallerInfo"> <see cref="EnableCallerInfo"/> 是否启用调用者信息。</param>
        /// <exception cref="ArgumentException">fileName 为 null 或空白。</exception>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public LoggerTraceListener(string fileName, string name, bool enabledCallerInfo = false):base(name)
        {
            if (string.IsNullOrWhiteSpace(fileName))
                throw new ArgumentException("文件名不能为空", nameof(fileName));

            _fileName = fileName;
            EnableCallerInfo = enabledCallerInfo;

            Initialize();
        }

        /// <summary>
        /// 创建侦听器并写入到指定流。
        /// </summary>
        /// <param name="stream">目标流。</param>
        /// <param name="name">侦听器名称。</param>
        /// <param name="enabledCallerInfo"> <see cref="EnableCallerInfo"/> 是否启用调用者信息。</param>
        /// <exception cref="ArgumentNullException">stream 为 null。</exception>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public LoggerTraceListener(Stream stream, string name, bool enabledCallerInfo = false) :base(name)
        {
            if (stream == null) 
                throw new ArgumentNullException(nameof(stream));

            _writer = new StreamWriter(stream);
            EnableCallerInfo = enabledCallerInfo;

            Initialize();
        }
        /// <inheritdoc cref="LoggerTraceListener(Stream, string, bool)"/>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public LoggerTraceListener(Stream stream, bool enabledCallerInfo = false) : this(stream, nameof(LoggerTraceListener), enabledCallerInfo)
        {
        }

        /// <summary>
        /// 创建侦听器并写入到指定 TextWriter。
        /// </summary>
        /// <param name="writer">目标写入器。</param>
        /// <param name="name">侦听器名称。</param>
        /// <param name="enabledCallerInfo"> <see cref="EnableCallerInfo"/> 是否启用调用者信息。</param>
        /// <exception cref="ArgumentNullException">writer 为 null。</exception>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public LoggerTraceListener(TextWriter writer, string name, bool enabledCallerInfo = false): base(name)
        {
            if (writer == null) 
                throw new ArgumentNullException(nameof(writer));

            _writer = writer;
            EnableCallerInfo = enabledCallerInfo;

            Initialize();
        }
        /// <inheritdoc cref="LoggerTraceListener(TextWriter, string, bool)"/>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public LoggerTraceListener(TextWriter writer, bool enabledCallerInfo = false): this(writer, nameof(LoggerTraceListener), enabledCallerInfo)
        {
        }
        #endregion

        #region 初始化
        /// <summary>
        /// 通用初始化：设置过滤器、启动消费任务、注册进程退出事件。
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private void Initialize()
        {
            base.Filter = new EventTypeFilter(SourceLevels.Information);

            _syncContext = SynchronizationContext.Current;
            _cancelTokenSource = new CancellationTokenSource();
            _isConsoleProgram = string.IsNullOrWhiteSpace(_fileName) && Environment.UserInteractive;

            _writerTask = Task.Run(async () => await WriteMessageQueue(_cancelTokenSource.Token), _cancelTokenSource.Token);

            try
            {
                AppDomain.CurrentDomain.ProcessExit += (sender, e) => Dispose();
            }
            catch (PlatformNotSupportedException) { }
            catch (NotSupportedException) { }
        }
        #endregion

        #region TraceListener 重写
        /// <inheritdoc/>
        /// <remarks>
        /// 注意：与标准 <see cref="TraceListener.Write(string)"/> 不同，本实现中 <c>Write</c> 和 <c>WriteLine</c>
        /// 行为一致（均输出为一行），因为日志采用异步消费模型，无法缓存部分写入的片段。
        /// </remarks>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public override void Write(string message)
        {
            if (Filter != null && !Filter.ShouldTrace(null, null, TraceEventType.Verbose, 0, message, null, null, null)) 
                return;

            EnqueueMessage(TraceEventType.Verbose, message, null);
        }
        /// <inheritdoc/> 
        [MethodImpl(MethodImplOptions.NoInlining)]
        public override void WriteLine(string message)
        {
            if (Filter != null && !Filter.ShouldTrace(null, null, TraceEventType.Verbose, 0, message, null, null, null)) 
                return;

            EnqueueMessage(TraceEventType.Verbose, message, null);
        }
        /// <inheritdoc/> 
        [MethodImpl(MethodImplOptions.NoInlining)]
        public override void TraceEvent(TraceEventCache eventCache, string source, TraceEventType eventType, int id, string message)
        {
            if (Filter != null && !Filter.ShouldTrace(eventCache, source, eventType, id, message, null, null, null)) 
                return;

            EnqueueMessage(eventType, message, source);
        }
        /// <inheritdoc/>
        /// <remarks>
        /// 异步模型下不直接操作 _writer，而是设置标志位，由消费线程在下一次循环中执行 Flush。
        /// 高并发下多次 Flush 调用可能合并为一次。
        /// </remarks>
        public override void Flush()
        {
            _isFlushRequested = true;
        }
        #endregion

        #region 核心入队逻辑
        /// <summary>
        /// 将日志消息加入队列，可选捕获调用者信息。
        /// </summary>
        /// <param name="eventType">日志级别。</param>
        /// <param name="message">日志内容。</param>
        /// <param name="source">日志源（可为 null）。</param>
        [MethodImpl(MethodImplOptions.NoInlining)]
        protected void EnqueueMessage(TraceEventType eventType, string message, string source)
        {
            if (_isDisposed || string.IsNullOrEmpty(message)) return;

            var typeName = string.Empty;
            var methodName = string.Empty;
            var lineNumber = 0;

            // 仅在开启调用者信息捕获时才执行 StackTrace（有性能开销）
            // 或者跟踪事件类型为 Warning 或以上时，捕获调用者信息（有性能开销）
            if (EnableCallerInfo || eventType <= TraceEventType.Warning)
            {
                try
                {
                    var frame = GetCallerFrame();
                    if (frame != null)
                    {
                        var method = frame.GetMethod();
                        typeName = method?.DeclaringType?.Name ?? string.Empty;
                        //typeName = method?.DeclaringType?.FullName ?? string.Empty;
                        methodName = method?.Name ?? string.Empty;
                        lineNumber = frame.GetFileLineNumber();
                    }
                }
                catch (PlatformNotSupportedException) { }
                catch (NotSupportedException) { }
            }

            var entry = new TraceMessage
            {
                EventType = eventType,
                Message = message,
                Source = source ?? string.Empty,
                TypeName = typeName,
                MethodName = methodName,
                LineNumber = lineNumber,
            };

            _messageQueue.Enqueue(entry);
            if (TraceMessageEnqueued != null)
            {
                if (_syncContext != null)
                    _syncContext.Post(_ => TraceMessageEnqueued.Invoke(this, entry), null);
                else
                    TraceMessageEnqueued.Invoke(this, entry);
            }
        }
        /// <summary>
        /// 获取调用者的栈帧（跳过 LoggerTraceListener、Trace 和 System.Diagnostics 命名空间）。
        /// </summary>
        /// <returns>调用者栈帧，获取失败返回 null。</returns>
        private static StackFrame GetCallerFrame()
        {
            // 捕获全栈，不预设 skipFrames
#if DEBUG
            var stackTrace = new StackTrace(true);
#else
            var stackTrace = new StackTrace(3, false);
#endif
            for (int i = 0; i < stackTrace.FrameCount; i++)
            {
                var frame = stackTrace.GetFrame(i);
                var method = frame?.GetMethod();
                if (method == null) continue;

                var declaringType = method.DeclaringType;
                if (declaringType == null) continue;

                // 跳过本类自身
                if (declaringType == typeof(LoggerTraceListener)) continue;

                // 跳过所有名为 "Trace" 的类型（System.Diagnostics.Trace、SpaceCG.Trace 等封装层）
                if (declaringType.Name == "Trace") continue;

                // 跳过 System.Diagnostics 命名空间（TraceInternal、TraceListenerCollection 等）
                var ns = declaringType.Namespace;
                if (ns != null && ns.StartsWith("System.Diagnostics")) continue;

                // 跳过 System.Threading（Task、ThreadPool 等运行时帧）
                if (ns != null && ns.StartsWith("System.Threading")) continue;

                return frame;
            }
            return null;
        }
#endregion

        #region WriterStream 管理
        /// <summary>
        /// 确保日志文件存在并创建 StreamWriter。
        /// <para>若文件被占用，自动生成带 GUID 后缀的备用文件名。</para>
        /// </summary>
        private void EnsureWriter()
        {
            if (_writer != null) return;
            if (string.IsNullOrWhiteSpace(_fileName)) return;

            var fullPath = Path.GetFullPath(_fileName);
            var dirPath = Path.GetDirectoryName(fullPath);
            var fileNameOnly = Path.GetFileName(fullPath);

            if (!Directory.Exists(dirPath))
                Directory.CreateDirectory(dirPath);

            var encoding = GetEncodingWithFallback(new UTF8Encoding(false));

            for (int i = 0; i < 2; i++)
            {
                try
                {
                    _writer = new StreamWriter(fullPath, true, encoding, 4096);
                    return;
                }
                catch (IOException ex)
                {
                    // 文件被占用：生成带 GUID 的备用文件名重试一次
                    Trace.WriteLine($"[LoggerTraceListener] 文件访问冲突: {ex.Message}");                    

                    var extensions = Path.GetExtension(fileNameOnly);
                    var nameWithoutExt = Path.GetFileNameWithoutExtension(fileNameOnly);
                    fileNameOnly = $"{nameWithoutExt}.({Guid.NewGuid()}){extensions}";
                    fullPath = Path.Combine(dirPath, fileNameOnly);
                }
                catch (UnauthorizedAccessException ex)
                {
                    Trace.WriteLine($"[LoggerTraceListener] 文件访问被拒绝: {ex.Message}");
                    break;
                }
            }

            // 两次尝试均失败，放弃写入文件
            _fileName = null;
        }
        /// <summary>
        /// 获取带 ReplacementFallback 的编码器，确保不可编码字符用 '?' 替代而非抛异常。
        /// </summary>
        private static Encoding GetEncodingWithFallback(Encoding encoding)
        {
            Encoding fallbackEncoding = (Encoding)encoding.Clone();
            fallbackEncoding.EncoderFallback = EncoderFallback.ReplacementFallback;
            fallbackEncoding.DecoderFallback = DecoderFallback.ReplacementFallback;

            return fallbackEncoding;
        }
        #endregion

        #region 进程/系统信息
        /// <summary>
        /// 写入系统和进程信息到日志头部。
        /// </summary>
        private void WriteProcessInfo()
        {
            if (_writer == null) EnsureWriter();
            if (_writer == null) return;

            // 系统信息：OS 版本在各平台均可用
            try
            {
                var os = Environment.OSVersion;
                var systemInfo = $"[{os} ({os.Platform})]({(Environment.Is64BitOperatingSystem ? "64" : "32")}位OS / CPU逻辑核心: {Environment.ProcessorCount})";
                _writer.WriteLine(systemInfo);
                _writer.Flush();
            }
            catch { }

            try
            {
                var moduleInfo = $"[{Process.GetCurrentProcess().MainModule?.ModuleName}]({(Environment.Is64BitProcess ? "64" : "32")}位进程 / PID: {Process.GetCurrentProcess().Id})";
                _writer.WriteLine(moduleInfo);
                _writer.Flush();

                var processModule = Process.GetCurrentProcess().MainModule;
                var moduleFileName = processModule?.FileName;
                if (!string.IsNullOrWhiteSpace(moduleFileName))
                {
                    var info = new FileInfo(moduleFileName);
                    var versionInfo = System.Diagnostics.FileVersionInfo.GetVersionInfo(moduleFileName);
                    _writer.Write(versionInfo.ToString());

                    if (!string.IsNullOrWhiteSpace(info.Extension))
                    {
                        // 标签左对齐（15字符），值右对齐（20字符），确保列对齐
                        _writer.WriteLine($"{nameof(info.CreationTime),-15}:\t{info.CreationTime,20}");
                        _writer.WriteLine($"{nameof(info.LastWriteTime),-15}:\t{info.LastWriteTime,20}");
                        _writer.WriteLine($"{nameof(info.LastAccessTime),-15}:\t{info.LastAccessTime,20}");
                    }
                    _writer.Flush();
                }
            }
            catch (Exception) { }
        }
        #endregion

        #region 消费线程-写消息
        /// <summary>
        /// 消费线程主循环：从队列取出消息、写入文件/控制台、定期检查和维护。
        /// </summary>
        private async Task WriteMessageQueue(CancellationToken cancellationToken)
        {
            EnsureWriter();
            if (_writer == null) return;

            var startTime = DateTime.Now;
            _writer.WriteLine($"\r\n[Header] {startTime:yyyy-MM-dd HH:mm:ss.fff}");

            WriteProcessInfo();
            CheckFileSizeLimit();
            CleanupOldBackupFiles();

            var lastCheckSize = DateTime.Now;
            var lastCheckDays = DateTime.Now;

            int flushCounter = 0;
            var lastWriteTime = DateTime.Now;            
            var isConsole = _isConsoleProgram; // 缓存到局部变量，消费线程生命周期内不变

            TimeSpan DayCheckTime = TimeSpan.FromHours(1);
            TimeSpan SizeCheckTime = TimeSpan.FromMinutes(1);
            TimeSpan IdleFlushInterval = TimeSpan.FromSeconds(2);

            while (!cancellationToken.IsCancellationRequested)
            {
                var now = DateTime.Now;

                try
                {
                    // 定期维护：每小时检查过期备份
                    if (now - lastCheckDays > DayCheckTime)
                    {
                        CleanupOldBackupFiles();
                        lastCheckDays = now;
                    }
                    // 定期维护：每分钟检查文件大小限制
                    if (now - lastCheckSize > SizeCheckTime)
                    {
                        CheckFileSizeLimit();
                        lastCheckSize = now;
                    }

                    // 文件轮转后 writer 被置 null，需要重新创建
                    if (_writer == null)
                    {
                        EnsureWriter();
                        if (_writer == null)
                        {
                            await Task.Delay(100, cancellationToken).ConfigureAwait(false);
                            continue;
                        }
                    }

                    // 处理外部 Flush 请求
                    if (_isFlushRequested)
                    {
                        _writer.Flush();
                        flushCounter = 0;
                        _isFlushRequested = false;
                    }

                    // 队列为空：空闲一段时间后执行一次 Flush，避免数据长时间留在缓冲区
                    if (_messageQueue.IsEmpty)
                    {
                        if (DateTime.Now - lastWriteTime > IdleFlushInterval)
                        {
                            _writer.Flush();
                            flushCounter = 0;
                            lastWriteTime = DateTime.MaxValue; // 防止重复触发，直到下次写入时重置
                        }

                        await Task.Delay(30, cancellationToken).ConfigureAwait(false);
                        continue;
                    }

                    // 取出并写入消息
                    if (_messageQueue.TryDequeue(out var entry))
                    {
                        lastWriteTime = DateTime.Now;

                        if (isConsole)
                        {
                            // 控制台模式：彩色输出，每条立即 Flush（控制台无缓冲）
                            try
                            {
                                if (!Trace.IsUnityRuntime) Console.ForegroundColor = EventTypeConsoleColors[entry.EventType];
                                _writer.WriteLine(entry.ToFormatString());
                                if (!Trace.IsUnityRuntime) Console.ResetColor();
                            }
                            catch (PlatformNotSupportedException)
                            {
                                // 非 Windows 平台可能不支持 Console 颜色
                                _writer.WriteLine(entry.ToFormatString());
                            }
                            _writer.Flush();
                        }
                        else
                        {
                            // 文件模式：批量 Flush
                            flushCounter++;
                            _writer.WriteLine(entry.ToFormatString());

                            if (AutoFlushInterval <= 0 || flushCounter >= AutoFlushInterval)
                            {
                                _writer.Flush();
                                flushCounter = 0;
                            }
                        }
                    }
                }
                catch (Exception ex) when (ex is OperationCanceledException)
                {
                    break;
                }
            }

            // 优雅关闭：排空队列
            while (_messageQueue.TryDequeue(out var entry))
            {
                if (_writer == null) EnsureWriter();
                if (_writer != null)
                {
                    _writer.WriteLine(entry.ToFormatString());
                    _writer.Flush();
                }
            }

            var uptime = DateTime.Now - startTime;
            _writer?.WriteLine($"[Footer] {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} (ExitCode={Environment.ExitCode} Uptime={uptime:dd\\.hh\\:mm\\:ss})\r\n");
            _writer?.Flush();
        }
        #endregion

        #region 文件维护
        /// <summary>
        /// 检查当前日志文件是否超过大小限制，超过则备份并创建新文件。
        /// </summary>
        private void CheckFileSizeLimit()
        {
            if (FileSizeLimit <= 0) return;
            if (string.IsNullOrWhiteSpace(_fileName) || _writer == null) return;
            if (_writer is StreamWriter streamWriter && streamWriter.BaseStream is FileStream fileStream && fileStream.Length >= FileSizeLimit)
            {
                try
                {
                    _writer.Flush();
                    _writer.Close();
                    _writer.Dispose();

                    var fullPath = Path.GetFullPath(_fileName);
                    var fileInfo = new FileInfo(fullPath);

                    var nameWithoutExt = Path.GetFileNameWithoutExtension(fileInfo.Name);
                    var ext = fileInfo.Extension;
                    var dirPath = fileInfo.DirectoryName;

                    var backupCount = Directory.GetFiles(dirPath, $"{nameWithoutExt}*{ext}", SearchOption.TopDirectoryOnly).Length;
                    var destFileName = Path.Combine(dirPath, $"{nameWithoutExt}({backupCount}){ext}");

                    File.Move(fullPath, destFileName);
                }
                catch (IOException ex)
                {
                    Trace.WriteLine($"[LoggerTraceListener] 日志文件轮转失败: {ex.Message}");
                }
                catch (UnauthorizedAccessException ex)
                {
                    Trace.WriteLine($"[LoggerTraceListener] 日志文件轮转权限不足: {ex.Message}");
                }
                finally
                {
                    _writer = null;
                    EnsureWriter();
                }
            }
        }
        /// <summary>
        /// 检查并删除超过保留天数的备份日志文件。
        /// </summary>
        private void CleanupOldBackupFiles()
        {
            if (FileBackupDays <= 0) return;
            if (string.IsNullOrWhiteSpace(_fileName)) return;

            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(_fileName);
            }
            catch { return; }

            var dirPath = Path.GetDirectoryName(fullPath);
            if (string.IsNullOrWhiteSpace(dirPath) || !Directory.Exists(dirPath)) return;

            var extension = Path.GetExtension(fullPath);
            var threshold = DateTime.Now.AddDays(-FileBackupDays);

            FileInfo[] backupFiles;
            try
            {
                backupFiles = Directory.GetFiles(dirPath, $"*{extension}", SearchOption.TopDirectoryOnly)
                    .Select(f => new FileInfo(f))
                    .Where(f => f.LastWriteTime < threshold)
                    .ToArray();
            }
            catch { return; }

            foreach (var fileInfo in backupFiles)
            {
                try
                {
                    File.Delete(fileInfo.FullName);
                }
                catch (IOException)
                {
                    // 文件被占用，跳过
                }
                catch (UnauthorizedAccessException)
                {
                    // 权限不足，跳过
                }
            }
        }
        #endregion

        /// <summary>
        /// 获取当前程序模块的文件名
        /// </summary>
        /// <returns>返回当前程序模块的完整路径文件名</returns>
        protected static string GetProcessMainModulePath()
        {
            try
            {
                var processModule = Process.GetCurrentProcess().MainModule;
                if (!string.IsNullOrWhiteSpace(processModule?.FileName)) return processModule.FileName;

                // Linux
                var moduleName = processModule?.ModuleName;
                if (!string.IsNullOrWhiteSpace(moduleName) && moduleName.StartsWith("dotnet", StringComparison.OrdinalIgnoreCase))
                {
                    var args = Environment.GetCommandLineArgs();
                    if (args.Length > 0) return args[0];
                }
            }
            catch (PlatformNotSupportedException) { }
            catch (NotSupportedException) { }
            catch (InvalidOperationException) { }

            return Path.Combine(Environment.CurrentDirectory, $"{DateTime.Now:yyyy-MM-dd}.Unknown.{Guid.NewGuid()}.log");
        }

        /// <inheritdoc/> 
        protected override void Dispose(bool disposing)
        {
            if (_isDisposed) return;

            _isDisposed = true;

            try
            {
                // 通知消费线程停止
                _cancelTokenSource?.Cancel();

                // 等待消费线程完成（带超时，防止 I/O 阻塞导致死锁）
                if (_writerTask != null)
                {
                    _writerTask.Wait(300);
                }
            }
            finally
            {
                _writer?.Close();
                _writer?.Dispose();
                _writer = null;

                _writerTask?.Dispose();
                _writerTask = null;

                _cancelTokenSource?.Dispose();
                _cancelTokenSource = null;
            }

            base.Dispose(disposing);
        }
    }

    /// <summary>
    /// 跟踪消息实体，包含日志级别、内容、来源、调用位置等完整信息。
    /// </summary>
    public class TraceMessage
    {
        /// <summary>
        /// 日志事件的时间戳。
        /// </summary>
        public DateTime Timestamp { get; internal set; } = DateTime.Now;

        /// <summary>
        /// 日志事件类型（级别）。
        /// </summary>
        public TraceEventType EventType { get; internal set; } = TraceEventType.Verbose;

        /// <summary>
        /// 日志消息内容。
        /// </summary>
        public string Message { get; internal set; } = string.Empty;

        /// <summary>
        /// 日志来源（TraceSource 名称）。
        /// </summary>
        public string Source { get; internal set; } = string.Empty;

        /// <summary>
        /// 调用者的类型全名。
        /// </summary>
        public string TypeName { get; internal set; } = string.Empty;

        /// <summary>
        /// 调用者的方法名。
        /// </summary>
        public string MethodName { get; internal set; } = string.Empty;

        /// <summary>
        /// 调用者的源代码行号（需要 PDB 文件支持）。
        /// </summary>
        public int LineNumber { get; internal set; }

        /// <summary>
        /// 创建日志消息时的线程 ID。
        /// </summary>
        public int ThreadId { get; internal set; } = Environment.CurrentManagedThreadId;

        /// <summary>
        /// 构造函数
        /// </summary>
        public TraceMessage()
        {
        }

        /// <summary>
        /// 重置消息。预留用于重用对象，使用对象池
        /// </summary>
        /// <param name="eventType"></param>
        /// <param name="message"></param>
        /// <param name="source"></param>
        /// <param name="typeName"></param>
        /// <param name="methodName"></param>
        /// <param name="lineNumber"></param>
        internal void ResetMessage(TraceEventType eventType, string message, string source, string typeName, string methodName, int lineNumber)
        {
            Timestamp = DateTime.Now;
            ThreadId = Environment.CurrentManagedThreadId;

            EventType = eventType;
            Message = message;
            Source = source;
            TypeName = typeName;
            MethodName = methodName;
            LineNumber = lineNumber;
        }

        /// <summary>
        /// 转换为格式化字符串
        /// </summary>
        /// <returns></returns>
        public string ToFormatString()
        {
            var threadId = ThreadId.ToString().PadLeft(2);
            var lineNumber = LineNumber > 0 ? $"({LineNumber})" : string.Empty;
            var eventType = LoggerTraceListener.EventTypeNames[EventType].PadLeft(5);

            return $"[{Timestamp:HH:mm:ss.fff}] [{eventType}] [{threadId}] [{TypeName}.{MethodName}]{lineNumber} - {Message}";
            //return $"[{Timestamp:HH:mm:ss.fff}] [{eventType}] [{threadId}] [({Source}).{TypeName}.{MethodName}]({LineNumber}) - {Message}";
        }
    }

}
