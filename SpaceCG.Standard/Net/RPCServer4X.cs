using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Reflection;
using System.Text;
using System.Xml.Linq;
using SpaceCG.Extensions;

namespace SpaceCG.Net
{
    /// <summary>
    /// 基于 XML 协议的 RPC 服务端实现。
    /// <para>消息格式为 XML 元素，以 CRLF 作为行分隔符。</para>
    /// </summary>
    public sealed class RPCServer4X : RPCServerBase
    {
        /// <summary> XML 单标签结束标识符字节数组 "/>"（0x2F, 0x3E）。 </summary>
        public static readonly byte[] XMLEndFlags = new byte[] { 0x2F, 0x3E };

        private readonly IEnumerable<PropertyInfo> InvokeResultProperties;

        /// <inheritdoc /> 
        public RPCServer4X(int localPort) : this(IPAddress.Any, localPort)
        {
        }
        /// <inheritdoc /> 
        public RPCServer4X(IPAddress ipAddress, int localPort) : base(ipAddress, localPort)
        {
            InvokeResultProperties = typeof(ResponseMessage).GetProperties(BindingFlags.Instance | BindingFlags.Public);
        }

        /// <summary>
        /// 将 XML 格式的消息帧解析为 <see cref="InvokeMessage"/> 对象。
        /// </summary>
        /// <inheritdoc /> 
        protected override IEnumerable<InvokeMessage> ParseInvokeMessage(ArraySegment<byte> messageLine, IPEndPoint remoteEndPoint)
        {
            var position = 0;
            var messages = new List<InvokeMessage>(8);

            while (position < messageLine.Count)
            {
                // 查找下一个 XML 元素结束标识 "/>"
                var endIndex = messageLine.IndexOf(XMLEndFlags, position, messageLine.Count - position);                
                if (endIndex < 0) break;

                var message = string.Empty;
                var tempPosition = position;
                position = endIndex + XMLEndFlags.Length;   // 移动读指针到当前元素之后，为下一个元素扫描做准备

                try
                {
                    message = Encoding.UTF8.GetString(messageLine.Array, tempPosition, endIndex - tempPosition + XMLEndFlags.Length);
                    Debug.WriteLine($"客户端 {remoteEndPoint} Message:'{message}'");
                }
                catch(Exception ex)
                {
                    Trace.TraceWarning($"客户端 {remoteEndPoint} XML 消息解码异常：{ex.Message}");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(message)) continue;

                try
                {
                    var element = XElement.Parse(message);

                    var objectName = element.Attribute("ObjectName")?.Value;
                    var methodName = element.Attribute("MethodName")?.Value;
                    if (string.IsNullOrWhiteSpace(objectName) || !RPCServerBase.NamedRegex.IsMatch(objectName) ||
                        string.IsNullOrWhiteSpace(methodName) || !RPCServerBase.NamedRegex.IsMatch(methodName)) continue;

                    var invokeMessage = InvokeMessage.Create(objectName, methodName, element.Attribute("Parameters")?.Value);

                    // 解析 Id
                    if (int.TryParse(element.Attribute("Id")?.Value, out var id)) invokeMessage.Id = id;
                    // 解析 ResponseMode
                    if (int.TryParse(element.Attribute("ResponseMode")?.Value, out var responseMode)) invokeMessage.ResponseMode = responseMode;
                    // 解析 Timestamp
                    if (DateTimeOffset.TryParse(element.Attribute("Timestamp")?.Value, out var timestamp)) invokeMessage.Timestamp = timestamp;

                    messages.Add(invokeMessage);
                }
                catch (Exception ex)
                {
                    Trace.TraceWarning($"客户端 {remoteEndPoint} XML 元素('{message}')解析异常：{ex.Message}");
                }
            }

            return messages;
        }

        /// <summary>
        /// 将调用结果序列化为 XML 格式的响应字节数组。
        /// <para>如果函数返回类型为 void 类型，则不响应消息。</para>
        /// </summary>
        /// <inheritdoc /> 
        protected override byte[] ConvertResponseMessage(ResponseMessage invokeResult, IPEndPoint remoteEndPoint)
        {
            if (invokeResult == null) return Array.Empty<byte>();
            //if (invokeResult.Code >= 0 && (invokeResult.ReturnType == null || invokeResult.ReturnType == typeof(void))) return Array.Empty<byte>();

#if false
            var message = new XElement(nameof(ResponseMessage));
            foreach (PropertyInfo property in InvokeResultProperties)
            {
                if (!property.CanRead) continue;

                var value = property.GetValue(invokeResult);
                if (value == null) continue;

                message.Add(new XAttribute(property.Name, value));
            }            
            return Encoding.UTF8.GetBytes($"{message}\r\n");
#else
            var builder = new StringBuilder(512);
            builder.AppendLine($"<{nameof(ResponseMessage)} Id=\"{invokeResult.Id}\" Code=\"{invokeResult.Code}\" ObjectMethod=\"{invokeResult.ObjectMethod}\" ReturnValue=\"{invokeResult.ReturnValue}\" ReturnType=\"{invokeResult.ReturnType}\" Description=\"{invokeResult.Description}\" Version=\"{invokeResult.Version}\" Timestamp=\"{invokeResult.Timestamp}\" />");
            return Encoding.UTF8.GetBytes(builder.ToString());
#endif
        }
    }

    
}
