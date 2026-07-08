using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;

namespace SpaceCG.Generic
{
    /// <summary>
    /// 日志跟踪侦听器
    /// </summary>
    public class LoggerTraceListener : System.Diagnostics.TraceListener
    {
        /// <summary>
        /// 文件存储的最大字节数，默认 2M；如果文件大小超过该值，则会自动备份，并重新创建新的文件继续写入
        /// <para>如果设置小于等于 0，不限制文件大小</para>
        /// </summary>
        public int FileSizeLimit { get; set; } = 1024 * 1024 * 2;
        /// <summary>
        /// 文件备份的最大天数，默认 30 天；如果文件备份超过该值，则会自动删除旧的备份文件
        /// <para>如果设置小于等于 0，不限制备份文件时间</para>
        /// </summary>
        public int FileBackupDays { get; set; } = 30;
        /// <summary>
        /// <see cref="TextWriter.WriteLine()"/> 多少次后自动调用一次 <see cref="TextWriter.Flush()"/>，默认 4 次；目的是为了减少磁盘频繁的 IO，以提高性能
        /// <para>如果设置小于等于 0，则每次写入后都立即刷新缓冲区</para>
        /// </summary>
        public int AutoFlushInterval { get; set; } = 4;
        
        private bool _isDisposed = false;
        private bool _isConsoleProgram = false;
        private volatile bool _isFlushing = false;

        private Task _writerTask;
        private string _fileName;
        private TextWriter _writer;

        private CancellationTokenSource _cancelTokenSource;
        private readonly ConcurrentQueue<TraceMessage> TraceMessageQueue = new ConcurrentQueue<TraceMessage>();
        // 使用缓存队列 ??
        //private readonly ConcurrentQueue<TraceMessage> TraceMessageCache = new ConcurrentQueue<TraceMessage>();

        /// <summary>
        /// 跟踪事件接收入队时触发的事件
        /// </summary>
        public event EventHandler<TraceMessage> TraceEventReceived;

        /// <summary>
        /// 跟踪事件类型名称
        /// </summary>
        public static readonly IReadOnlyDictionary<TraceEventType, string> EventTypeNames = new Dictionary<TraceEventType, string>
        {
            { TraceEventType.Transfer,"Transfer" },
            { TraceEventType.Resume, "Resume" },
            { TraceEventType.Suspend, "Suspend" },
            { TraceEventType.Stop, "Stop" },
            { TraceEventType.Start, "Start" },
            { TraceEventType.Verbose, "Debug" },
            { TraceEventType.Information, "Info" },
            { TraceEventType.Warning, "Warn" },
            { TraceEventType.Error, "Error" },
            { TraceEventType.Critical, "Critical" }
        };
        /// <summary>
        /// 跟踪事件对应的 Console 类型颜色
        /// </summary>
        public static readonly IReadOnlyDictionary<TraceEventType, ConsoleColor> EventTypeConsoleColors = new Dictionary<TraceEventType, ConsoleColor>
        {
            { TraceEventType.Transfer, ConsoleColor.DarkYellow },
            { TraceEventType.Resume, ConsoleColor.Magenta },
            { TraceEventType.Suspend, ConsoleColor.Magenta },
            { TraceEventType.Stop, ConsoleColor.Cyan },
            { TraceEventType.Start, ConsoleColor.Cyan },
            { TraceEventType.Verbose, ConsoleColor.Green },
            { TraceEventType.Information, ConsoleColor.Gray },
            { TraceEventType.Warning, ConsoleColor.Yellow },
            { TraceEventType.Error, ConsoleColor.Red },
            { TraceEventType.Critical, ConsoleColor.DarkRed }
        };

        /// <summary>
        /// 构造函数
        /// <para>如果未指定文件名，则会自动获取当前模块的文件名作为日志文件名</para>
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public LoggerTraceListener() : base(nameof(LoggerTraceListener))
        {
            if (string.IsNullOrWhiteSpace(_fileName))
            {
                var moduleFile = new FileInfo(GetModuleFileName());
                _fileName = Path.Combine(moduleFile.DirectoryName, "logs", $"{DateTime.Now:yyyy-MM-dd}.{moduleFile.Name.Replace(moduleFile.Extension, ".log")}");
            }
            
            Initialize();
        }

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="fileName">日志文件名</param>
        /// <param name="name">侦听器名称</param>
        /// <exception cref="ArgumentException"></exception>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public LoggerTraceListener(string fileName, string name):base(name)
        {
            if (string.IsNullOrWhiteSpace(fileName)) throw new ArgumentException(nameof(fileName));
            _fileName = fileName;

            Initialize();
        }

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="stream"></param>
        /// <param name="name">侦听器名称</param>
        /// <exception cref="ArgumentNullException"></exception>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public LoggerTraceListener(Stream stream, string name):base(name)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));

            _writer = new StreamWriter(stream);

            Initialize();
        }
        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="stream"></param>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public LoggerTraceListener(Stream stream): this(stream, nameof(LoggerTraceListener))
        {
        }
        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="writer"></param>
        /// <param name="name">侦听器名称</param>
        /// <exception cref="ArgumentNullException"></exception>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public LoggerTraceListener(TextWriter writer, string name): base(name)
        {
            if (writer == null) 
                throw new ArgumentNullException(nameof(writer));
            _writer = writer;

            Initialize();
        }
        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="writer"></param>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public LoggerTraceListener(TextWriter writer): this(writer, nameof(LoggerTraceListener))
        {
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void Initialize()
        {
            base.Filter = new EventTypeFilter(SourceLevels.Information);

            _cancelTokenSource = new CancellationTokenSource();
            _isConsoleProgram = string.IsNullOrWhiteSpace(_fileName) && Environment.UserInteractive;
            _writerTask = Task.Factory.StartNew(ProcessTraceQueue, TaskCreationOptions.LongRunning);

            AppDomain.CurrentDomain.ProcessExit += (sender, e) => Dispose();
        }

        /// <inheritdoc/> 
        [MethodImpl(MethodImplOptions.NoInlining)]
        public override void Write(string message)
        {
            if (Filter != null && !Filter.ShouldTrace(null, null, TraceEventType.Verbose, 0, message, null, null, null)) return;

            EnqueueTraceEvent(TraceEventType.Verbose, message, null);
        }
        /// <inheritdoc/> 
        [MethodImpl(MethodImplOptions.NoInlining)]
        public override void WriteLine(string message)
        {
            if (Filter != null && !Filter.ShouldTrace(null, null, TraceEventType.Verbose, 0, message, null, null, null)) return;

            EnqueueTraceEvent(TraceEventType.Verbose, message, null);
        }
        /// <inheritdoc/> 
        [MethodImpl(MethodImplOptions.NoInlining)]
        public override void TraceEvent(TraceEventCache eventCache, string source, TraceEventType eventType, int id, string message)
        {
            if (Filter != null && !Filter.ShouldTrace(eventCache, source, eventType, id, message, null, null, null)) return;

            EnqueueTraceEvent(eventType, message, source);
        }
        /// <inheritdoc/> 
        public override void Flush()
        {
            // 这时是异步的，考虑到线程安全，不在这里直接调用 _writer.Flush()
            if (_isFlushing) return;
            _isFlushing = true;
        }

        private void WriteProcessInfo()
        {
            if (_writer == null) EnsureWriter();
            if (_writer == null) return;

            OperatingSystem os = Environment.OSVersion;
            string systemInfo = $"[{os} ({os.Platform})]({(Environment.Is64BitOperatingSystem ? "64" : "32")} 位操作系统 / 逻辑处理器: {Environment.ProcessorCount})";
            string moduleInfo = $"[{Process.GetCurrentProcess().MainModule?.ModuleName}]({(Environment.Is64BitProcess ? "64" : "32")} 位进程 / 进程 ID: {Process.GetCurrentProcess().Id})";
            _writer.WriteLine(systemInfo);
            _writer.WriteLine(moduleInfo);
            _writer.Flush();

            ProcessModule processModule = Process.GetCurrentProcess().MainModule;
            var moduleFileName = processModule?.FileName;
            if (!string.IsNullOrWhiteSpace(moduleFileName))
            {
                FileInfo info = new FileInfo(moduleFileName);
                FileVersionInfo versionInfo = FileVersionInfo.GetVersionInfo(moduleFileName);
                _writer.Write(versionInfo.ToString());

                if (!string.IsNullOrWhiteSpace(info.Extension))
                {
                    _writer.WriteLine($"{nameof(info.CreationTime)}:\t{info.CreationTime,20}");
                    _writer.WriteLine($"{nameof(info.LastWriteTime)}:\t{info.LastWriteTime,20}");
                    _writer.WriteLine($"{nameof(info.LastAccessTime)}:\t{info.LastAccessTime,20}");
                }
                _writer.Flush();
            }
        }

        private void ProcessTraceQueue()
        {
            if (_writer == null) EnsureWriter();
            if (_writer == null) return;
            _writer.WriteLine($"\r\n[Header] {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");

            WriteProcessInfo();
            CheckFileSizeLimit();
            CheckFileBackupDays();

            DateTime lastCheckSizeTime = DateTime.Now;
            DateTime lastCheckDaysTime = DateTime.Now;

            int flushCount = 0;
            DateTime lastWriteTime = DateTime.Now;
            TimeSpan WriteInterval = TimeSpan.FromSeconds(2);

            while (!_cancelTokenSource.IsCancellationRequested)
            {
                var now = DateTime.Now;
                if (now - lastCheckDaysTime > TimeSpan.FromHours(1)) CheckFileBackupDays();
                if (now - lastCheckSizeTime > TimeSpan.FromMinutes(1)) CheckFileSizeLimit();

                if (_writer == null) EnsureWriter();
                if (_writer == null)
                {
                    Thread.Sleep(100);
                    continue;
                }

                if (_isFlushing)
                {
                    _writer.Flush();

                    flushCount = 0;
                    _isFlushing = false;
                }

                if (TraceMessageQueue.IsEmpty)
                {                   
                    if (DateTime.Now - lastWriteTime > WriteInterval)
                    {
                        flushCount = 0;
                        _writer.Flush();
                        lastWriteTime = DateTime.MaxValue;
                    }

                    Thread.Sleep(30);
                    continue;
                }

                if (TraceMessageQueue.TryDequeue(out TraceMessage entry))
                {                    
                    if (_isConsoleProgram)
                    {                        
                        Console.ForegroundColor = EventTypeConsoleColors[entry.EventType];
                        _writer.WriteLine($"{entry.ToFromatString()}");
                        Console.ResetColor();                        
                    }
                    else
                    {
                        flushCount++;
                        lastWriteTime = DateTime.Now;
                        _writer.WriteLine($"{entry.ToFromatString()}");

                        if (AutoFlushInterval <= 0 || flushCount >= AutoFlushInterval)
                        {
                            flushCount = 0;
                            _writer.Flush();                            
                        }
                    }
                }
            }

            while (!TraceMessageQueue.IsEmpty)
            {
                if (_writer == null) EnsureWriter();
                if (TraceMessageQueue.TryDequeue(out TraceMessage entry))
                {
                    _writer.WriteLine(entry.ToFromatString());
                    _writer.Flush();
                }
            }

            _writer.WriteLine($"[Footer] {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}\r\n");
            _writer.Flush();
        }


        /// <inheritdoc/> 
        protected override void Dispose(bool disposing)
        {
            if (_isDisposed) return;
            _isDisposed = true;

            _cancelTokenSource.Cancel();
            _writerTask.Wait();            
            
            _writer?.Close();
            _writer?.Dispose();
            _writer = null;

            _writerTask?.Dispose();
            _writerTask = null;

            _cancelTokenSource.Dispose();
            _cancelTokenSource = null;
        }

        /// <summary>
        /// 将跟踪事件写入队列
        /// </summary>
        /// <param name="eventType"></param>
        /// <param name="message"></param>
        /// <param name="source"></param>
        [MethodImpl(MethodImplOptions.NoInlining)]
        protected void EnqueueTraceEvent(TraceEventType eventType, string message, string source)
        {
            if (_isDisposed || string.IsNullOrEmpty(message)) return;

#if true
            var stackTrace = new StackTrace(3, true);
            var frame = FindCallerFrame(stackTrace);
#else
            var frame = new StackFrame(3, true);
#endif

            var entry = new TraceMessage
            {
                EventType = eventType,
                Message = message,
                Source = source,
                LineNumber = frame?.GetFileLineNumber() ?? 0,
                MethodName = frame?.GetMethod()?.Name ?? string.Empty,
                TypeName = frame?.GetMethod()?.DeclaringType?.FullName ?? string.Empty,
            };

            TraceMessageQueue.Enqueue(entry);
            TraceEventReceived?.Invoke(this, entry);
        }

        /// <summary>
        /// 查找调用者的栈帧
        /// </summary>
        /// <param name="stackTrace"></param>
        /// <returns></returns>
        private StackFrame FindCallerFrame(StackTrace stackTrace)
        {
            for (int i = 0; i < stackTrace.FrameCount; i++)
            {
                var frame = stackTrace.GetFrame(i);
                var method = frame?.GetMethod();
                if (method == null) continue;                

                var declaringType = method.DeclaringType;
                if (declaringType == null) continue;

                if (declaringType.Namespace != null && (declaringType.Namespace.StartsWith("System.Diagnostics") || declaringType == typeof(LoggerTraceListener))) continue;
                
                return frame;
            }
            return null;
        }

        /// <summary>
        /// 获取指定编码的编码器，并设置"?"替换回退
        /// </summary>
        /// <param name="encoding"></param>
        /// <returns></returns>
        private static Encoding GetEncodingWithFallback(Encoding encoding)
        {
            Encoding fallbackEncoding = (Encoding)encoding.Clone();
            fallbackEncoding.EncoderFallback = EncoderFallback.ReplacementFallback;
            fallbackEncoding.DecoderFallback = DecoderFallback.ReplacementFallback;

            return fallbackEncoding;
        }

        /// <summary>
        /// 确保日志文件存在，以及日志文件大小限制
        /// </summary>
        private void EnsureWriter()
        {
            if (_writer == null)
            {
                if (string.IsNullOrWhiteSpace(_fileName)) return;
                
                bool success = false;

                string fullPath = Path.GetFullPath(_fileName);
                string dirPath = Path.GetDirectoryName(fullPath);
                string fileNameOnly = Path.GetFileName(fullPath);

                if (!Directory.Exists(dirPath)) Directory.CreateDirectory(dirPath);

                Encoding noBOMwithFallback = GetEncodingWithFallback(new System.Text.UTF8Encoding(false));

                for (int i = 0; i < 2; i++)
                {
                    try
                    {
                        _writer = new StreamWriter(fullPath, true, noBOMwithFallback, 4096);
                        success = true;
                        break;
                    }
                    catch (IOException ex)
                    {
                        Console.WriteLine(ex);
                        var extensions = Path.GetExtension(fileNameOnly);
                        fileNameOnly = fileNameOnly.Replace(extensions, $".({Guid.NewGuid()}){extensions}");
                        fullPath = Path.Combine(dirPath, fileNameOnly);
                        continue;
                    }
                    catch (UnauthorizedAccessException)
                    {
                        break;
                    }
                    catch (Exception)
                    {
                        break;
                    }
                }

                if (!success)
                {
                    _fileName = null;
                }
            }
        }

        /// <summary>
        /// 检查文件大小的限制
        /// </summary>
        protected void CheckFileSizeLimit()
        {
            if (FileSizeLimit <= 0) return;
            if (string.IsNullOrWhiteSpace(_fileName) || _writer == null) return;
            if (_writer is StreamWriter streamWriter && streamWriter.BaseStream is FileStream fileStream && fileStream.Length >= FileSizeLimit)
            {
                try
                {
                    _writer?.Flush();
                    _writer?.Close();
                    _writer?.Dispose();

                    string fullPath = Path.GetFullPath(_fileName);
                    FileInfo fileInfo = new FileInfo(fullPath);

                    var fnPrefix = fileInfo.Name.Substring(0, fileInfo.Name.Length - fileInfo.Extension.Length);
                    var fnCount = Directory.GetFiles(fileInfo.DirectoryName, $"{fnPrefix}*{fileInfo.Extension}", SearchOption.TopDirectoryOnly).Length;
                    var destFileName = Path.Combine(fileInfo.DirectoryName, $"{fnPrefix}({fnCount}){fileInfo.Extension}");

//#if NETSTANDARD2_0
                    File.Move(fullPath, destFileName);
//#else
                    //File.Move(fullPath, destFileName, true);
//#endif
                }
                catch (Exception)
                {
                }
                finally
                {
                    _writer = null;
                    EnsureWriter();
                }
            }
        }
        /// <summary>
        /// 检查文件备份的时间限制
        /// </summary>
        protected void CheckFileBackupDays()
        {
            if (FileBackupDays <= 0) return;
            if (string.IsNullOrWhiteSpace(_fileName)) return;

            var fileExtension = Path.GetExtension(_fileName);
            var fileDirectory = Path.GetDirectoryName(_fileName);

            var backupFiles = from file in Directory.GetFiles(fileDirectory, $"*{fileExtension}", SearchOption.TopDirectoryOnly)
                              let fileInfo = new FileInfo(file)
                              where fileInfo.LastWriteTime < DateTime.Now.AddDays(-FileBackupDays)
                              select fileInfo;

            foreach (var fileInfo in backupFiles)
            {
                try
                {
                    File.Delete(fileInfo.FullName);
                }
                catch (Exception)
                {
                }
            }
        }

        /// <summary>
        /// 获取当前程序模块的文件名
        /// </summary>
        /// <returns>返回当前程序模块的完整路径文件名</returns>
        protected string GetModuleFileName()
        {
            ProcessModule processModule = Process.GetCurrentProcess().MainModule;
            if (!string.IsNullOrWhiteSpace(processModule?.FileName)) 
                return processModule.FileName;

            // Linux
            string moduleName = processModule?.ModuleName;
            if (!string.IsNullOrWhiteSpace(moduleName) && moduleName.StartsWith("donet", StringComparison.OrdinalIgnoreCase))
            {
                var args = Environment.GetCommandLineArgs();
                if (args.Length > 0) return args[0];
            }
            
            return $"{Environment.CurrentDirectory}{Path.DirectorySeparatorChar}{DateTime.Now:yyyy-MM-dd}.Unknown.{Guid.NewGuid()}.log";
        }

    }


    /// <summary>
    /// 跟踪消息对象
    /// </summary>
    public class TraceMessage
    {
        /// <summary>
        /// 跟踪事件的时间戳
        /// </summary>
        public DateTime Timestamp { get; internal set; } = DateTime.Now;
        /// <summary>
        /// 跟踪事件的类型
        /// </summary>
        public TraceEventType EventType { get; internal set; } = TraceEventType.Verbose;

        /// <summary>
        /// 跟踪事件的消息
        /// </summary>
        public string Message { get; internal set; } = string.Empty;
        /// <summary>
        /// 跟踪事件的源
        /// </summary>
        public string Source { get; internal set; } = string.Empty;

        /// <summary>
        /// 跟踪事件的类型名
        /// </summary>
        public string TypeName { get; internal set; } = string.Empty;
        /// <summary>
        /// 跟踪事件的方法名
        /// </summary>
        public string MethodName { get; internal set; } = string.Empty;

        /// <summary>
        /// 跟踪事件的具体行号
        /// </summary>
        public int LineNumber { get; internal set; } = 0;
        /// <summary>
        /// 获取当前线程的ID
        /// </summary>
        public int ThreadId { get; internal set; } = Environment.CurrentManagedThreadId;

        /// <summary>
        /// 构造函数
        /// </summary>
        public TraceMessage()
        {
        }

        /// <summary>
        /// 重置消息
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
        public string ToFromatString()
        {
            return $"[{Timestamp:HH:mm:ss.fff}] [{LoggerTraceListener.EventTypeNames[EventType].PadLeft(5)}] [{ThreadId.ToString().PadLeft(2)}] [({Source}).{TypeName}.{MethodName}]({LineNumber}) - {Message}";
        }
    }

}
