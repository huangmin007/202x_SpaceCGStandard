using System;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using SpaceCG.Extensions;

namespace SpaceCG.Net
{
    /// <summary>
    /// 基于 XML 消息协议的 RPC 服务端实现（XML-RPC v2.0）。
    /// <para>每行（以 CRLF 为行边界）一条 XML 消息。请求和响应均使用 XElement 解析 / StringBuilder 拼接 XML。</para>
    /// <para>用法示例：<code>new RpcServer4X(port).Start()</code></para>
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
        protected override InvokeMessage DeserializeInvokeMessage(ArraySegment<byte> requestMessage, IPEndPoint clientEndPoint)
        {
            if (requestMessage == null || requestMessage.Count == 0) return null;

            var content = string.Empty;
            try
            {
                content = Encoding.UTF8.GetString(requestMessage.Array, requestMessage.Offset, requestMessage.Count);
                //Debug.WriteLine($"客户端 {remoteEndPoint} Message:'{message}',,,{requestMessage.Offset}, {requestMessage.Count}");
            }
            catch (Exception ex)
            {
                Trace.TraceWarning($"客户端 {clientEndPoint} 请求消息解码为字符串时异常：({ex.GetType().Name}){ex.Message}");
                return null;
            }

            if (string.IsNullOrWhiteSpace(content)) return null;

            InvokeMessage invokeMessage = null;
            try
            {
#if true
                var element = XElement.Parse(content);
                if (element.Name != nameof(InvokeMessage)) return null;

                var objectName = element.Attribute(nameof(InvokeMessage.ObjectName))?.Value;
                var methodName = element.Attribute(nameof(InvokeMessage.MethodName))?.Value;
                if (string.IsNullOrWhiteSpace(objectName) || !RpcServerBase.IdentifierPattern.IsMatch(objectName) ||
                    string.IsNullOrWhiteSpace(methodName) || !RpcServerBase.IdentifierPattern.IsMatch(methodName)) return null;

                int id = int.TryParse(element.Attribute(nameof(InvokeMessage.Id))?.Value, out var _id) ? _id : -1;
                int responseMode = int.TryParse(element.Attribute(nameof(InvokeMessage.ResponseMode))?.Value, out var _responseMode) ? _responseMode : 0;

                invokeMessage = InvokeMessage.Create(objectName, methodName, element.Attribute(nameof(InvokeMessage.Parameters))?.Value, id, responseMode);

                // 解析 Description
                invokeMessage.Description = element.Attribute(nameof(InvokeMessage.Description))?.Value;
                // 解析 Version
                if (Version.TryParse(element.Attribute(nameof(InvokeMessage.Version))?.Value, out var version)) invokeMessage.Version = version;                
                // 解析 Timestamp
                if (DateTimeOffset.TryParse(element.Attribute(nameof(InvokeMessage.Timestamp))?.Value, out var timestamp)) invokeMessage.Timestamp = timestamp;
#else
                invokeMessage = XAttributeParse(content);
                if (invokeMessage == null)
                {
                    Trace.TraceWarning($"客户端 {clientEndPoint} XML 元素属性('{content}')解析失败");
                    return null;
                }
#endif
            }
            catch (Exception ex)
            {
                Trace.TraceWarning($"客户端 {clientEndPoint} 请求消息反序列化时异常：({ex.GetType().Name}){ex.Message}");
            }
            
            return invokeMessage;
        }

        /// <summary>
        /// 将调用结果序列化为 XML 字符格式的响应字节数组。
        /// </summary>
        /// <inheritdoc />
        protected override byte[] SerializeResponseMessage(ResponseMessage responseMessage, IPEndPoint clientEndPoint)
        {
            if (responseMessage == null) return Array.Empty<byte>();

            try
            {
#if false
                var message = new XElement(nameof(ResponseMessage));
                if (responseMessage.Id > 0)
                {
                    message.Add(new XAttribute(nameof(ResponseMessage.Id), responseMessage.Id));
                }

                message.Add(new XAttribute(nameof(ResponseMessage.Code), responseMessage.Code));
                message.Add(new XAttribute(nameof(ResponseMessage.ObjectMethod), responseMessage.ObjectMethod));

                if (responseMessage.ReturnType != null && responseMessage.ReturnType != typeof(void))
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
                if (responseMessage.Id > 0)
                {
                    builder.Append($"{nameof(ResponseMessage.Id)}=\"{responseMessage.Id}\" ");
                }

                builder.Append($"{nameof(ResponseMessage.Code)}=\"{responseMessage.Code}\" ");
                builder.Append($"{nameof(ResponseMessage.ObjectMethod)}=\"{responseMessage.ObjectMethod}\" ");

                if (responseMessage.ReturnType != null && responseMessage.ReturnType != typeof(void))
                {
                    builder.Append($"{nameof(ResponseMessage.ReturnType)}=\"{SecurityElement.Escape(responseMessage.ReturnType.ToString())}\" ");
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
                Trace.TraceWarning($"客户端 {clientEndPoint} 响应消息序列化时异常：({ex.GetType().Name}){ex.Message}");
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

            int id = -1, responseMode = 0;
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

            var invokeMessage = InvokeMessage.Create(objectName, methodName, parameters, id, responseMode);

            if (!string.IsNullOrWhiteSpace(description)) invokeMessage.Description = description;
            if (!string.IsNullOrWhiteSpace(timestamp) && DateTimeOffset.TryParse(timestamp, CultureInfo.InvariantCulture, DateTimeStyles.None, out var _timestamp)) invokeMessage.Timestamp = _timestamp;

            return invokeMessage;
        }
    }

}
