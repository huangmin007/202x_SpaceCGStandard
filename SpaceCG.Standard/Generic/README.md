
- ### LoggerTraceListener 跟踪并记录 Trace/Debug 信息
```C#
//简单示例
Trace.Listeners.Add(new LoggerTraceListener());
Trace.Listeners.Add(new LoggerTraceListener(Console.Out));

Trace.Listeners.Add(new LoggerTraceListener($"logs/{DateTime.Now:yyyy-MM-dd}.log", "Trace Listener Name"));

var ltl = new LoggerTraceListener($"logs/{DateTime.Now:yyyy-MM-dd}.log", "Trace Listener Name");
ltl.FileSizeLimit = 1024 * 1024 * 1;	//设置文件存储的最大字节数 1M 大小，如果文件大小超过该值，则会自动备份，并重新创建新的文件继续写入
ltl.FileBackupDays = 30;	//设置相同的后缀文件最多保存天数，超出该天数则删除；是以文件最后写入时间开始计算，超过 30 天则删掉
ltl.AutoFlushInterval = 4;	//Write 多少次后，Flush一次，减少磁盘频繁的 IO
ltl.Filter = new EventTypeFilter(SourceLevels.Warning); //不同的监听器设置不用的事件类型级别
ltl.TraceEventReceived += TraceListener_TraceEventReceived;
private void TraceListener_TraceEventReceived(object sender, TraceMessage traceMessage)
{
	// ....
}
Trace.Listeners.Add(ltl);
```