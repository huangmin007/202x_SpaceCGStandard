using System;
using System.Reflection;

namespace SpaceCG.Net
{
    /// <summary>
    /// 远程过程调用(Remote Procedure Call) 或 反射程序控制(Reflection Program Control) 的响应消息对象。
    /// <para>封装方法反射调用后的返回结果，包括状态码、描述信息、返回值等信息。</para>
    /// </summary>
    public class ResponseMessage : IRPCMessage
    {
        /// <summary>
        /// 消息唯一标识，对应请求 <see cref="InvokeMessage.Id"/>，用于 请求-响应 的匹配。
        /// </summary>
        public int Id { get; internal set; }
        /// <summary>
        /// 调用结果状态码：大于等于 0 表示成功，小于 0 表示失败。
        /// </summary>
        public int Code { get; internal set; }

        /// <summary>
        /// 被调用方法的完整名称，格式为 "{ObjectName}.{MethodName}"。
        /// </summary>
        public string ObjectMethod { get; internal set; }
        /// <summary>
        /// 调用结果的描述信息，如 "OK" 或错误或异常原因等。
        /// </summary>
        public string Description { get; internal set; }

        /// <summary>
        /// 调用方法的返回值类型，方法返回 <c>void</c> 时此值为 <see cref="void"/>。
        /// </summary>
        public Type ReturnType { get; internal set; }
        /// <summary>
        /// 调用方法的返回值，无返回值时为 <c>null</c>。
        /// </summary>
        public object ReturnValue { get; internal set; }

        /// <summary>
        /// 调用结果生成时间戳，使用 ISO8601 标准格式。
        /// <para>用于调试分析、延迟统计、日志追踪、超时判断等场景。</para>
        /// </summary>
        public DateTimeOffset Timestamp { get; internal set; }
        /// <summary>
        /// 消息协议版本号，对应请求 <see cref="InvokeMessage.Version"/>。
        /// </summary>
        public Version Version { get; internal set; }

        /// <summary>
        /// 内部构造函数，实例由静态工厂方法创建。
        /// </summary>
        internal ResponseMessage() { }

        /// <inheritdoc cref="Reset" /> 
        void IRPCMessage.Reset() => Reset();
        
        /// <summary>
        /// 重置消息属性为默认值，用于对象池复用时清空上一次调用的遗留数据。
        /// </summary>
        internal void Reset()
        {
            Id = 0;
            Code = 0;
            ObjectMethod = string.Empty;
            Description = string.Empty;

            ReturnType = null;
            ReturnValue = null;

            Timestamp = DateTimeOffset.UtcNow;
        }

        /// <summary>
        /// 根据调用消息创建初始结果对象（状态码待填充）。
        /// </summary>
        /// <param name="invokeMessage">客户端调用消息。</param>
        /// <returns>创建的 <see cref="ResponseMessage"/> 实例。</returns>
        internal static ResponseMessage Create(InvokeMessage invokeMessage)
        {
            if (invokeMessage == null) return null;

            return new ResponseMessage()
            {
                Id = invokeMessage.Id,
                Version = invokeMessage.Version,
                Timestamp = DateTimeOffset.UtcNow,
                ObjectMethod = $"{invokeMessage.ObjectName}.{invokeMessage.MethodName}",
            };
        }

        /// <summary>
        /// 根据调用消息和错误信息创建结果对象。
        /// </summary>
        /// <param name="invokeMessage">客户端调用消息。</param>
        /// <param name="code">错误状态码（小于 0 表示失败）。</param>
        /// <param name="description">错误描述信息。</param>
        /// <returns>创建的 <see cref="ResponseMessage"/> 实例。</returns>
        internal static ResponseMessage Create(InvokeMessage invokeMessage, int code, string description)
        {
            if (invokeMessage == null) return null;

            return new ResponseMessage()
            {
                Code = code,
                Id = invokeMessage.Id,

                Description = description,
                Version = invokeMessage.Version,
                Timestamp = DateTimeOffset.UtcNow,
                ObjectMethod = $"{invokeMessage.ObjectName}.{invokeMessage.MethodName}",
            };
        }

        /// <summary>
        /// 创建响应消息对象，并填充状态码和描述信息。
        /// </summary>
        /// <param name="code"></param>
        /// <param name="description"></param>
        /// <returns></returns>
        internal static ResponseMessage Create(int code, string description)
        {
            return new ResponseMessage()
            {
                Code = code,
                Description = description,
                Timestamp = DateTimeOffset.UtcNow,
            };
        }

        /// <summary>
        /// 返回结果对象的摘要字符串表示形式。
        /// </summary>
        public override string ToString()
        {
            return $"[{nameof(ResponseMessage)}](Id:{Id} Code:{Code} ObjectMethod:{ObjectMethod} Description:{Description} Version:{Version} Timestamp:{Timestamp})";
        }

    }

}
