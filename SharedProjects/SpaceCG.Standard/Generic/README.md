

- ### LoggerTraceListener 跟踪并记录 Trace/Debug 信息
```C#
// 简单示例：使用默认配置（自动以模块名和日期生成日志文件路径）
Trace.Listeners.Add(new LoggerTraceListener());

// 写入到控制台（通过 TextWriter 构造）
Trace.Listeners.Add(new LoggerTraceListener(Console.Out));

// 写入到自定义文件路径，指定侦听器名称
Trace.Listeners.Add(new LoggerTraceListener($"logs/{DateTime.Now:yyyy-MM-dd}.log", "Trace Listener Name"));

// 完整配置示例
var ltl = new LoggerTraceListener($"logs/{DateTime.Now:yyyy-MM-dd}.log", "Trace Listener Name");
ltl.EnableCallerInfo = true;           // 启用调用者信息捕获（方法名/行号），默认 DEBUG=true、Release=false
ltl.FileSizeLimit = 1024 * 1024 * 1;  // 设置文件存储的最大字节数 1M 大小，超过自动备份并创建新文件
ltl.FileBackupDays = 30;              // 备份文件最多保留天数，超出则自动删除
ltl.AutoFlushInterval = 4;            // 写入多少条日志后 Flush 一次，减少磁盘频繁 I/O
ltl.Filter = new EventTypeFilter(SourceLevels.Warning); // 不同监听器可设置不同的事件类型级别

ltl.TraceMessageEnqueued += TraceListener_TraceMessageEnqueued;
private void TraceListener_TraceMessageEnqueued(object sender, TraceMessage traceMessage)
{
    // 实时获取格式化的日志消息（事件通过 SynchronizationContext.Post 封送回创建线程）
    traceMessage.ToFormatString();
    // 输出格式: [12:01:15.234] [ INFO] [ 1] [MyClass.MyMethod](42) - 日志内容
}

Trace.Listeners.Add(ltl);
```