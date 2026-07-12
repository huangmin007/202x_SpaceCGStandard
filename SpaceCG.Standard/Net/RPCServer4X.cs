using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using SpaceCG.Extensions;

namespace SpaceCG.Net
{
    /// <summary>
    /// 基于 XML 协议的 RPC 服务端实现（XML-RPC v1.5）。
    /// <para>消息格式为 XML 自闭合元素（以 "/>" 结束），多消息以 CRLF 作为行分隔符。</para>
    /// <para>用法示例：<code>new RPCServer4X(port).Start()</code></para>
    /// </summary>
    public sealed class RPCServer4X : RPCServerBase
    {
        /// <summary> XML 自闭合元素结束标识字节数组 "/>"（0x2F, 0x3E）。 </summary>
        private static readonly byte[] XMLEndMarker = new byte[] { 0x2F, 0x3E };
        /// <summary> XML 元素属性正则表达式。 </summary>
        private static readonly Regex AttributeRegex = new Regex(@"(\w+)\s*=\s*""((?:[^""\\]|\\.)*)""", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        /// <summary> 响应消息属性集合。 </summary>
        private static readonly IEnumerable<PropertyInfo> ResponseMessageProperties = typeof(ResponseMessage).GetProperties(BindingFlags.Instance | BindingFlags.Public);

        /// <inheritdoc /> 
        public RPCServer4X(int localPort = 2000) : this(IPAddress.Any, localPort)
        {
        }
        /// <inheritdoc /> 
        public RPCServer4X(IPAddress ipAddress, int localPort) : base(ipAddress, localPort)
        {
        }

        /// <summary>
        /// 将 XML 格式的消息行解析为 <see cref="InvokeMessage"/> 对象。
        /// </summary>
        /// <inheritdoc /> 
        protected override IEnumerable<InvokeMessage> ParseInvokeMessage(ArraySegment<byte> messageLine, IPEndPoint remoteEndPoint)
        {
            var position = 0;
            var messages = new List<InvokeMessage>(8);

            while (position < messageLine.Count)
            {
                // 查找下一个 XML 元素结束标识 "/>"
                var endIndex = messageLine.IndexOf(XMLEndMarker, position, messageLine.Count - position);                
                if (endIndex < 0) break;

                var message = string.Empty;
                var tempPosition = position;
                position = endIndex + XMLEndMarker.Length;   // 移动读指针到当前元素之后，为下一个元素扫描做准备

                try
                {
                    message = Encoding.UTF8.GetString(messageLine.Array, tempPosition, endIndex - tempPosition + XMLEndMarker.Length);
                    Debug.WriteLine($"客户端 {remoteEndPoint} Message:'{message}'");
                }
                catch(Exception ex)
                {
                    Trace.TraceWarning($"客户端 {remoteEndPoint} 消息解码为字符串异常：{ex.Message}");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(message)) continue;
                
                try
                {
#if true
                    var element = XElement.Parse(message);
                    var objectName = element.Attribute(nameof(InvokeMessage.ObjectName))?.Value;
                    var methodName = element.Attribute(nameof(InvokeMessage.MethodName))?.Value;
                    if (string.IsNullOrWhiteSpace(objectName) || !RPCServerBase.IdentifierPattern.IsMatch(objectName) ||
                        string.IsNullOrWhiteSpace(methodName) || !RPCServerBase.IdentifierPattern.IsMatch(methodName)) continue;

                    var invokeMessage = InvokeMessage.Create(objectName, methodName, element.Attribute(nameof(InvokeMessage.Parameters))?.Value);

                    // 解析 Id
                    if (int.TryParse(element.Attribute(nameof(InvokeMessage.Id))?.Value, out var id)) invokeMessage.Id = id;
                    // 解析 ResponseMode
                    if (int.TryParse(element.Attribute(nameof(InvokeMessage.ResponseMode))?.Value, out var responseMode)) invokeMessage.ResponseMode = responseMode;
                    // 解析 Timestamp
                    if (DateTimeOffset.TryParse(element.Attribute(nameof(InvokeMessage.Timestamp))?.Value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var timestamp)) invokeMessage.Timestamp = timestamp;
#else
                    var invokeMessage = XAttributeParse(message);
                    if (invokeMessage == null)
                    {
                        Trace.TraceWarning($"客户端 {remoteEndPoint} XML 元素属性('{message}')解析失败");
                        continue;
                    }
#endif              
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
        /// 将调用结果序列化为 XML 字符格式的响应字节数组。
        /// <para>当调用成功（Code == 0）且返回类型为 void 时不发响应；错误和带返回值的结果才响应。</para>
        /// <para>当前使用 StringBuilder 直拼 XML（性能优化方案），旧版 XElement 反射方案通过 <c>#if false</c> 保留在源码中。</para>
        /// </summary>
        /// <inheritdoc />
        protected override byte[] SerializeResponseMessage(ResponseMessage responseMessage, IPEndPoint remoteEndPoint)
        {
            if (responseMessage == null) return Array.Empty<byte>();

            try
            {
#if false
                var message = new XElement(nameof(ResponseMessage));
                foreach (PropertyInfo property in ResponseMessageProperties)
                {
                    if (!property.CanRead) continue;

                    var value = property.GetValue(responseMessage);
                    if (value == null) continue;

                    message.Add(new XAttribute(property.Name, value));
                }

                if (responseMessage.ReturnType != null)
                {
                    if (responseMessage.ReturnType == typeof(void))
                    {
                        message.Attribute(nameof(ResponseMessage.ReturnType)).Remove();
                        message.Attribute(nameof(ResponseMessage.ReturnValue)).Remove();
                    }
                    else
                    {
                        message.Attribute(nameof(ResponseMessage.ReturnType)).Value = responseMessage.ReturnType.Name;
                    }
                }

                return Encoding.UTF8.GetBytes($"{message}\r\n");
#else
                var builder = new StringBuilder(256 + responseMessage.Description?.Length ?? 0);

                builder.Append($"<{nameof(ResponseMessage)} ");
                builder.Append($"{nameof(ResponseMessage.Id)}=\"{responseMessage.Id}\" ");
                builder.Append($"{nameof(ResponseMessage.Code)}=\"{responseMessage.Code}\" ");
                builder.Append($"{nameof(ResponseMessage.ObjectMethod)} =\"{responseMessage.ObjectMethod}\" ");
                if (responseMessage.ReturnType != typeof(void))
                {
                    builder.Append($"{nameof(ResponseMessage.ReturnType)}=\"{responseMessage.ReturnType.Name}\" ");
                    builder.Append($"{nameof(ResponseMessage.ReturnValue)} =\"{SecurityElement.Escape(StringExtensions.ConvertToString(responseMessage.ReturnValue))}\" ");
                }
                if (!string.IsNullOrWhiteSpace(responseMessage.Description))
                {
                    builder.Append($"{nameof(ResponseMessage.Description)}=\"{SecurityElement.Escape(responseMessage.Description)}\" ");
                }
                builder.Append($"{nameof(ResponseMessage.Version)} =\"{responseMessage.Version}\" ");
                builder.Append($"{nameof(ResponseMessage.Timestamp)}=\"{responseMessage.Timestamp:O}\" ");
                builder.AppendLine("/>");

                return Encoding.UTF8.GetBytes(builder.ToString());
#endif
            }
            catch (Exception ex)
            {
                Trace.TraceWarning($"客户端 {remoteEndPoint} 响应消息序列化异常：{ex.Message}");
                return Array.Empty<byte>();
            } 
        }

        /// <summary>
        /// 将 XML 格式的消息行解析为 <see cref="InvokeMessage"/> 对象。
        /// </summary>
        /// <param name="xmlContent"></param>
        /// <returns></returns>
        private static InvokeMessage XAttributeParse(string xmlContent)
        {
            if (string.IsNullOrWhiteSpace(xmlContent)) return null;

            int id = 0, responseMode = 0;
            string objectName = null, methodName = null, parameters = null, description = null, timestamp = null;

            foreach (Match match in AttributeRegex.Matches(xmlContent))
            {
                var name = match.Groups[1].Value;
                var value = Regex.Unescape(match.Groups[2].Value);  // 还原转义: \"→"  \\→\  \n→换行  \t→制表

                switch (name)
                {
                    case nameof(InvokeMessage.Timestamp): timestamp = value; break;
                    case nameof(InvokeMessage.ObjectName): objectName = value; break;
                    case nameof(InvokeMessage.MethodName): methodName = value; break;
                    case nameof(InvokeMessage.Parameters): parameters = value; break;
                    case nameof(InvokeMessage.Description): description = value; break;
                    
                    case nameof(InvokeMessage.Id):  int.TryParse(value, out id); break;
                    case nameof(InvokeMessage.ResponseMode): int.TryParse(value, out responseMode); break;
                }
            }

            if (string.IsNullOrWhiteSpace(objectName) || !RPCServerBase.IdentifierPattern.IsMatch(objectName) ||
                string.IsNullOrWhiteSpace(methodName) || !RPCServerBase.IdentifierPattern.IsMatch(methodName)) return null;

            var invokeMessage = InvokeMessage.Create(objectName, methodName, parameters);
            invokeMessage.Id = id;
            invokeMessage.ResponseMode = responseMode;

            if (!string.IsNullOrWhiteSpace(description)) invokeMessage.Description = description;
            if (!string.IsNullOrWhiteSpace(timestamp) && DateTimeOffset.TryParse(timestamp, CultureInfo.InvariantCulture, DateTimeStyles.None, out var _timestamp)) invokeMessage.Timestamp = _timestamp;

            return invokeMessage;
        }
    }

}
