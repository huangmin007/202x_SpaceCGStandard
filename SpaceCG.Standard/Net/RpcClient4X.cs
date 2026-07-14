using System;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Security;
using System.Text;
using System.Xml.Linq;
using SpaceCG.Extensions;

namespace SpaceCG.Net
{
    /// <summary>
    /// 基于 XML 协议的 RPC 客户端实现（XML-RPC v2.0）。
    /// <para>与服务端 <see cref="RpcServer4X"/> 配套使用，支持 XML 格式的调用消息序列化和响应消息解析。</para>
    /// <para>用法示例：<code>new RpcClient4X("127.0.0.1", 2000).Connect()</code></para>
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
        protected override byte[] SerializeInvokeMessage(InvokeMessage requestMessage)
        {
            if (requestMessage == null) return Array.Empty<byte>();

            try
            {
                var builder = new StringBuilder(128);
                builder.Append($"<{nameof(InvokeMessage)} ");
                if (requestMessage.Id > 0)
                {
                    builder.Append($"{nameof(InvokeMessage.Id)}=\"{requestMessage.Id}\" ");
                }
                builder.Append($"{nameof(InvokeMessage.ObjectName)}=\"{requestMessage.ObjectName}\" ");
                builder.Append($"{nameof(InvokeMessage.MethodName)}=\"{requestMessage.MethodName}\" ");
                if (requestMessage.Parameters != null && requestMessage.Parameters.Length > 0)
                {
                    var parameters = string.Join(",", requestMessage.Parameters.Select(p => StringExtensions.ConvertToString(p)));
                    builder.Append($"{nameof(InvokeMessage.Parameters)}=\"{SecurityElement.Escape(parameters)}\" ");
                }
                builder.Append($"{nameof(InvokeMessage.ResponseMode)}=\"{requestMessage.ResponseMode}\" ");
                if (!string.IsNullOrWhiteSpace(requestMessage.Description))
                {
                    builder.Append($"{nameof(InvokeMessage.Description)}=\"{SecurityElement.Escape(requestMessage.Description)}\" ");
                }
                builder.Append($"{nameof(InvokeMessage.Version)}=\"{requestMessage.Version}\" ");
                builder.Append($"{nameof(InvokeMessage.Timestamp)}=\"{requestMessage.Timestamp:O}\" ");
                builder.AppendLine("/>");

                return Encoding.UTF8.GetBytes(builder.ToString());
            }
            catch (Exception ex)
            {
                Trace.TraceWarning($"客户端 {LocalEndPoint} 请求消息序列化时异常：({ex.GetType().Name}){ex.Message}");
            }

            return Array.Empty<byte>();
        }

        /// <inheritdoc /> 
        protected override ResponseMessage DeserializeResponseMessage(ArraySegment<byte> responseMessage)
        {
            if (responseMessage == null || responseMessage.Count == 0) return null;

            var content = string.Empty;
            try
            {
                content = Encoding.UTF8.GetString(responseMessage.Array, responseMessage.Offset, responseMessage.Count);
            }
            catch(Exception ex)
            {
                Trace.TraceWarning($"客户端 {LocalEndPoint} 响应消息解码为字符串时异常：({ex.GetType().Name}){ex.Message}");
                return null;
            }
            if (string.IsNullOrWhiteSpace(content)) return null;

            try
            {
                XElement element = XElement.Parse(content);
                Trace.WriteLine(element.ToString());

                if (element.Name != nameof(ResponseMessage)) return null;

                var objectMethod = element.Attribute(nameof(ResponseMessage.ObjectMethod))?.Value;

                if (string.IsNullOrWhiteSpace(objectMethod)) return null;
                if (!int.TryParse(element.Attribute(nameof(ResponseMessage.Code))?.Value, out var code)) return null;

                var description = element.Attribute(nameof(ResponseMessage.Description))?.Value;                
                var id = int.TryParse(element.Attribute(nameof(ResponseMessage.Id))?.Value, out var _id) ? _id : 0;
                var version = Version.TryParse(element.Attribute(nameof(ResponseMessage.Version))?.Value, out var _version) ? _version : new Version(0, 0);
                var timestamp = DateTimeOffset.TryParse(element.Attribute(nameof(ResponseMessage.Timestamp))?.Value, out var _timestamp) ? _timestamp : DateTime.UtcNow;

                Type returnType = null;
                object returnValue = null;

                if (code == 1)
                {
                    try
                    {
                        returnType = Type.GetType(element.Attribute(nameof(ResponseMessage.ReturnType))?.Value, false);
                        if (returnType != null) TypeExtensions.TryConvertTo(element.Attribute(nameof(ResponseMessage.ReturnValue))?.Value, returnType, out returnValue);
                    }
                    catch (Exception ex)
                    {
                        Trace.TraceWarning($"客户端 {LocalEndPoint} 响应消息参数反序列化时异常：({ex.GetType().Name}){ex.Message}");
                    }
                }
                
                var message = ResponseMessage.Create(id, code, description, objectMethod, returnType, returnValue);
                message.Version = version;
                message.Timestamp = timestamp;

                return message;
            }
            catch (Exception ex)
            {
                Trace.TraceWarning($"客户端 {LocalEndPoint} 响应消息反序列化时异常：({ex.GetType().Name}){ex.Message}");
            }

            return null;
        }

    }
}
