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
    /// XML 协议相关的扩展方法。
    /// </summary>
    public static partial class RPCServer4XExtensions
    {
        /// <summary>
        /// 将 <see cref="XElement"/> 转换为 <see cref="InvokeMessage"/> 对象。
        /// <para>从 XML 属性中读取 Id、ObjectName、MethodName、Parameters、IsAsync、Timestamp 等信息进行反序列化。</para>
        /// </summary>
        /// <param name="element">包含调用消息属性的 <see cref="XElement"/> 对象。</param>
        /// <returns>成功时返回 <see cref="InvokeMessage"/> 实例；失败时返回 <c>null</c>。</returns>
        public static InvokeMessage ToInvokeMessage(this XElement element)
        {
            if (element == null) return null;

            try
            {
                var objectName = element.Attribute("ObjectName")?.Value;
                var methodName = element.Attribute("MethodName")?.Value;
                if (string.IsNullOrWhiteSpace(objectName) || !RPCServerBase.NamedRegex.IsMatch(objectName) ||
                    string.IsNullOrWhiteSpace(methodName) || !RPCServerBase.NamedRegex.IsMatch(methodName)) return null;

                var invokeMessage = InvokeMessage.Create(objectName, methodName, element.Attribute("Parameters")?.Value);

                if (int.TryParse(element.Attribute("Id")?.Value, out var id))
                {
                    invokeMessage.Id = id;
                }
                // 解析 IsAsync
                if (bool.TryParse(element.Attribute("IsAsync")?.Value, out var isAsync))
                {
                    invokeMessage.IsAsync = isAsync;
                }
                // 解析 Timestamp
                if (DateTimeOffset.TryParse(element.Attribute("Timestamp")?.Value, out var timestamp))
                {
                    invokeMessage.Timestamp = timestamp;
                }

                return invokeMessage;
            }
            catch
            {
                return null;
            }
        }

    }
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
            InvokeResultProperties = typeof(InvokeResult).GetProperties(BindingFlags.Instance | BindingFlags.Public);
        }

        /// <summary>
        /// 将 XML 格式的消息帧解析为 <see cref="InvokeMessage"/> 对象。
        /// </summary>
        /// <inheritdoc /> 
        protected override IEnumerable<InvokeMessage> ParseInvokeMessage(ArraySegment<byte> messageLine, IPEndPoint remoteEndPoint)
        {
            //messageFrame.IndexOf(XMLEndFlags, 0, messageFrame.Count);

            var message = string.Empty;

            try
            {
                message = Encoding.UTF8.GetString(messageLine.Array, messageLine.Offset, messageLine.Count);
                Debug.WriteLine($">>>{message}");
            }
            catch (Exception ex)
            {
                Trace.TraceWarning($"客户端 {remoteEndPoint} 数据帧异常：{ex.Message}");
                return Array.Empty<InvokeMessage>();
            }

            try
            {
                XElement element = XElement.Parse(message);
                return new[] { element.ToInvokeMessage() };
            }
            catch (Exception ex)
            {
                Trace.TraceWarning($"解析客户端 {remoteEndPoint} 数据帧 \"{message}\" 异常：{ex.Message}");
            }

            return Array.Empty<InvokeMessage>();
        }

        /// <summary>
        /// 将调用结果序列化为 XML 格式的响应字节数组。
        /// <para>如果函数返回类型为 void 类型，则不响应消息。</para>
        /// </summary>
        /// <inheritdoc /> 
        protected override byte[] ResponseInvokeMessage(InvokeResult invokeResult, IPEndPoint remoteEndPoint)
        {
            if (invokeResult == null) return Array.Empty<byte>();
            if (invokeResult.Code > 0 && (invokeResult.ReturnType == null || invokeResult.ReturnType == typeof(void))) return Array.Empty<byte>();

            var message = new XElement(nameof(InvokeResult));

            foreach (PropertyInfo property in InvokeResultProperties)
            {
                if (!property.CanRead) continue;

                var value = property.GetValue(invokeResult);
                if (value == null) continue;

                message.Add(new XAttribute(property.Name, value));
            }
            
            return Encoding.UTF8.GetBytes($"{message}\r\n");
        }
    }

    
}
