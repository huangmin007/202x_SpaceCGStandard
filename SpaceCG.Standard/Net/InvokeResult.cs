using System;
using System.Reflection;

namespace SpaceCG.Net
{
    public class InvokeResult
    {
        public static readonly Version VERSION = new Version(1, 3);
        
        /// <summary>
        /// 消息唯一标识，用于请求与响应消息的匹配。
        /// </summary>
        public int Id { get; internal set; }
        /// <summary>
        /// 调用结果状态码，大于等于 0 表示成功，小于 0 表示失败。
        /// </summary>
        public int Code { get; internal set; }

        /// <summary>
        /// 调用结果的对象方法名称，格式为 {ObjectName}.{MethodName}
        /// </summary>
        public string ObjectMethod { get; internal set; }
        /// <summary>
        /// 调用结果状态信息，描述调用结果。
        /// </summary>
        public string Description { get; internal set; }

        /// <summary>
        /// 调用结果返回值类型
        /// </summary>
        public Type ReturnType { get; internal set; }
        /// <summary>
        /// 调用结果返回值
        /// </summary>
        public object ReturnValue { get; internal set; }
        /// <summary>
        /// 调用结果时间戳，使用 ISO8601 标准；用于调试分析，延迟统计，日志追踪，超时判断等。
        /// </summary>
        public DateTimeOffset Timestamp { get; internal set; }

        /// <summary>
        /// 消息版本号，当前版本为 1.3。
        /// </summary>
        public Version Version => VERSION;

        internal InvokeResult() { }

        internal static InvokeResult Create(InvokeMessage invokeMessage)
        {
            if (invokeMessage == null) return null;

            return new InvokeResult()
            {
                Id = invokeMessage.Id,
                Timestamp = DateTimeOffset.Now,
                ObjectMethod = $"{invokeMessage.ObjectName}.{invokeMessage.MethodName}",
            };
        }

        internal static InvokeResult Create(InvokeMessage invokeMessage, int code, string description)
        {
            if (invokeMessage == null) return null;

            return new InvokeResult()
            {
                Id = invokeMessage.Id,
                Code = code,
                Description = description,
                Timestamp = DateTimeOffset.Now,
                ObjectMethod = $"{invokeMessage.ObjectName}.{invokeMessage.MethodName}",
            };
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return $"[{nameof(InvokeResult)}]({Id} {ObjectMethod} {Description} {Version})";
        }

    }

}
