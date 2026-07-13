using System;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Text;
using SpaceCG.Extensions;

namespace SpaceCG.Net
{
    /// <summary>
    /// 基于 XML 协议的 RPC 客户端实现（XML-RPC v2.0）。
    /// <para>与服务端 <see cref="RpcServer4X"/> 互换使用，支持 XML 格式的调用消息序列化和响应消息解析。</para>
    /// <para>用法示例：<code>new RpcClient4X("127.0.0.1", 2000).ConnectAsync()</code></para>
    /// </summary>
    public sealed class RpcClient4X : RpcClientBase
    {
        /// <summary>
        /// 构造函数
        /// </summary>
        public RpcClient4X() : base() { }

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="address"></param>
        /// <param name="port"></param>
        public RpcClient4X(string address, int port) : base(address, port) { }

        /// <inheritdoc /> 
        protected override byte[] SerializeInvokeMessage(InvokeMessage invokeMessage)
        {
            if (invokeMessage == null) return Array.Empty<byte>();

            try
            {
                var builder = new StringBuilder(128);
                builder.Append($"<{nameof(InvokeMessage)} ");
                if (invokeMessage.Id >= 0)
                {
                    builder.Append($"{nameof(InvokeMessage.Id)}=\"{invokeMessage.Id}\" ");
                }
                builder.Append($"{nameof(InvokeMessage.ObjectName)}=\"{invokeMessage.ObjectName}\" ");
                builder.Append($"{nameof(InvokeMessage.MethodName)}=\"{invokeMessage.MethodName}\" ");
                if (invokeMessage.Parameters != null && invokeMessage.Parameters.Length > 0)
                {
                    var parameters = string.Join(",", invokeMessage.Parameters.Select(p => StringExtensions.ConvertToString(p)));
                    builder.Append($"{nameof(InvokeMessage.Parameters)}=\"{parameters}\" ");
                }
                builder.Append($"{nameof(InvokeMessage.ResponseMode)}=\"{invokeMessage.ResponseMode}\" ");
                if (!string.IsNullOrWhiteSpace(invokeMessage.Description))
                {
                    builder.Append($"{nameof(InvokeMessage.Description)}=\"{invokeMessage.Description}\" ");
                }
                builder.Append($"{nameof(InvokeMessage.Version)}=\"{invokeMessage.Version}\" ");
                builder.Append($"{nameof(InvokeMessage.Timestamp)}=\"{invokeMessage.Timestamp:O}\" ");
                builder.AppendLine("/>");

                return Encoding.UTF8.GetBytes(builder.ToString());
            }
            catch (Exception ex)
            {
                Trace.TraceWarning($"客户端 {LocalEndPoint} 序列化调用消息失败：({ex.GetType().Name}){ex.Message}");
            }

            return Array.Empty<byte>();
        }

        /// <inheritdoc /> 
        protected override ResponseMessage DeserializeResponseMessage(ArraySegment<byte> dataLine)
        {


            throw new NotImplementedException();
        }

    }
}
