using System;
using NModbus;
using Trace = SpaceCG.Diagnostics.Trace;

namespace SpaceCG.NModbus
{
    /// <summary>
    /// NModbus 日志适配器：将 NModbus 的 <see cref="IModbusLogger"/> 输出桥接到 <see cref="Trace"/>，
    /// 由 ModbusFactory 注入（new ModbusFactory(null, true, new ModbusLogger())）。
    /// 线程安全：无状态实现，可并发调用（Trace 自身为线程安全）。
    /// </summary>
    public class ModbusLogger : IModbusLogger
    {
        /// <summary>
        /// 按日志级别输出到 <see cref="Trace"/>：Debug/Trace → WriteLine，Information/Warning/Error/Critical → 对应级别的 TraceXxx。
        /// </summary>
        /// <remarks>
        /// 复杂度 O(1)。注意 NModbus 在收发帧时会高频调用本方法（Debug/Trace 级别，通常每帧一次），
        /// 属于热路径，此处不做字符串拼接等额外分配，仅直接转发。
        /// </remarks>
        /// <param name="level">日志级别。</param>
        /// <param name="message">日志内容，由 NModbus 格式化后传入。</param>
        public void Log(LoggingLevel level, string message)
        {
            if (level == LoggingLevel.Information) Trace.TraceInformation(message);
            //else if (level == LoggingLevel.Debug || level == LoggingLevel.Trace) Trace.WriteLine(message);
            else if (level == LoggingLevel.Warning) Trace.TraceWarning(message);
            else if (level == LoggingLevel.Error || level == LoggingLevel.Critical) Trace.TraceError(message);
        }

        /// <summary>
        /// 判断指定级别是否需要记录，供 NModbus 在格式化日志前调用，避免无谓的字符串分配。
        /// </summary>
        /// <remarks>Debug 构建下全量输出（含逐帧 Trace）；Release 构建下仅输出 Information 及以上级别。</remarks>
        /// <param name="level">待判定的日志级别。</param>
        /// <returns>true 表示需要记录。</returns>
        public bool ShouldLog(LoggingLevel level)
        {
#if DEBUG
            return true;
#else
            return level >= LoggingLevel.Information;
#endif
        }
    }
}
