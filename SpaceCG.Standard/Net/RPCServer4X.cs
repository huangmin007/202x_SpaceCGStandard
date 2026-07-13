using System;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;
using SpaceCG.Extensions;

namespace SpaceCG.Net
{
    /// <summary>
    /// 基于 XML 协议的 RPC 服务端实现（XML-RPC v2.0）。
    /// <para>用法示例：<code>new RPCServer4X(port).Start()</code></para>
    /// </summary>
    public sealed class RpcServer4X : RpcServerBase
    {        
        /// <summary> XML 元素属性正则表达式。 </summary>
        private static readonly Regex AttributeRegex = new Regex(@"(\w+)\s*=\s*""((?:[^""\\]|\\.)*)""", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        /// <inheritdoc /> 
        public RpcServer4X(int localPort = 2000) : this(IPAddress.Any, localPort)
        {
        }
        /// <inheritdoc /> 
        public RpcServer4X(IPAddress ipAddress, int localPort) : base(ipAddress, localPort)
        {
        }

        /// <summary>
        /// 将 XML 格式的字节数据解析为 <see cref="InvokeMessage"/> 对象。
        /// </summary>
        /// <inheritdoc /> 
        protected override InvokeMessage DeserializeInvokeMessage(ArraySegment<byte> dataLine, IPEndPoint remoteEndPoint)
        {
            string message = string.Empty;
            try
            {
                message = Encoding.UTF8.GetString(dataLine.Array, dataLine.Offset, dataLine.Count);
                //Debug.WriteLine($"客户端 {remoteEndPoint} Message:'{message}',,,{dataLine.Offset}, {dataLine.Count}");
            }
            catch (Exception ex)
            {
                Trace.TraceWarning($"客户端 {remoteEndPoint} 消息解码为字符串异常：{ex.Message}");
                return null;
            }

            if (string.IsNullOrWhiteSpace(message)) return null;

            InvokeMessage invokeMessage = null;
            try
            {
#if false
                var element = XElement.Parse(message);
                if (element.Name != nameof(InvokeMessage)) return null;

                var objectName = element.Attribute(nameof(InvokeMessage.ObjectName))?.Value;
                var methodName = element.Attribute(nameof(InvokeMessage.MethodName))?.Value;
                if (string.IsNullOrWhiteSpace(objectName) || !RpcServerBase.IdentifierPattern.IsMatch(objectName) ||
                    string.IsNullOrWhiteSpace(methodName) || !RpcServerBase.IdentifierPattern.IsMatch(methodName)) return null;

                invokeMessage = InvokeMessage.Create(objectName, methodName, element.Attribute(nameof(InvokeMessage.Parameters))?.Value);

                // 解析 Id
                if (int.TryParse(element.Attribute(nameof(InvokeMessage.Id))?.Value, out var id)) invokeMessage.Id = id;
                // 解析 ResponseMode
                if (int.TryParse(element.Attribute(nameof(InvokeMessage.ResponseMode))?.Value, out var responseMode)) invokeMessage.ResponseMode = responseMode;
                // 解析 Timestamp
                if (DateTimeOffset.TryParse(element.Attribute(nameof(InvokeMessage.Timestamp))?.Value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var timestamp)) invokeMessage.Timestamp = timestamp;
#else
                invokeMessage = XAttributeParse(message);
                if (invokeMessage == null)
                {
                    Trace.TraceWarning($"客户端 {remoteEndPoint} XML 元素属性('{message}')解析失败");
                    return null;
                }
#endif
            }
            catch (Exception ex)
            {
                Trace.TraceWarning($"客户端 {remoteEndPoint} XML 元素('{message}')解析异常：{ex.Message}");
            }

            return invokeMessage;
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
                if (responseMessage.Id >= 0)
                {
                    message.Add(new XAttribute(nameof(ResponseMessage.Id), responseMessage.Id));
                }

                message.Add(new XAttribute(nameof(ResponseMessage.Code), responseMessage.Code));
                message.Add(new XAttribute(nameof(ResponseMessage.ObjectMethod), responseMessage.ObjectMethod));

                if (responseMessage.ReturnType != typeof(void))
                {
                    message.Add(new XAttribute(nameof(ResponseMessage.ReturnType), responseMessage.ReturnType));
                    message.Add(new XAttribute(nameof(ResponseMessage.ReturnValue), StringExtensions.ConvertToString(responseMessage.ReturnValue)));
                }

                if (!string.IsNullOrWhiteSpace(responseMessage.Description))
                {
                    message.Add(new XAttribute(nameof(ResponseMessage.Description), responseMessage.Description));
                }

                message.Add(new XAttribute(nameof(ResponseMessage.Version), responseMessage.Version));
                message.Add(new XAttribute(nameof(ResponseMessage.Timestamp), responseMessage.Timestamp.ToString("O")));

                return Encoding.UTF8.GetBytes($"{message}\r\n");
#else
                var builder = new StringBuilder(256 + responseMessage.Description?.Length ?? 0);

                builder.Append($"<{nameof(ResponseMessage)} ");
                if (responseMessage.Id >= 0)
                {
                    builder.Append($"{nameof(ResponseMessage.Id)}=\"{responseMessage.Id}\" ");
                }

                builder.Append($"{nameof(ResponseMessage.Code)}=\"{responseMessage.Code}\" ");
                builder.Append($"{nameof(ResponseMessage.ObjectMethod)} =\"{responseMessage.ObjectMethod}\" ");

                if (responseMessage.ReturnType != typeof(void))
                {
                    builder.Append($"{nameof(ResponseMessage.ReturnType)}=\"{responseMessage.ReturnType}\" ");
                    builder.Append($"{nameof(ResponseMessage.ReturnValue)}=\"{SecurityElement.Escape(StringExtensions.ConvertToString(responseMessage.ReturnValue))}\" ");
                }

                if (!string.IsNullOrWhiteSpace(responseMessage.Description))
                {
                    builder.Append($"{nameof(ResponseMessage.Description)}=\"{SecurityElement.Escape(responseMessage.Description)}\" ");
                }

                builder.Append($"{nameof(ResponseMessage.Version)}=\"{responseMessage.Version}\" ");
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

            if (string.IsNullOrWhiteSpace(objectName) || !RpcServerBase.IdentifierPattern.IsMatch(objectName) ||
                string.IsNullOrWhiteSpace(methodName) || !RpcServerBase.IdentifierPattern.IsMatch(methodName)) return null;

            var invokeMessage = InvokeMessage.Create(objectName, methodName, parameters);
            invokeMessage.Id = id;
            invokeMessage.ResponseMode = responseMode;

            if (!string.IsNullOrWhiteSpace(description)) invokeMessage.Description = description;
            if (!string.IsNullOrWhiteSpace(timestamp) && DateTimeOffset.TryParse(timestamp, CultureInfo.InvariantCulture, DateTimeStyles.None, out var _timestamp)) invokeMessage.Timestamp = _timestamp;

            return invokeMessage;
        }
    }

}
