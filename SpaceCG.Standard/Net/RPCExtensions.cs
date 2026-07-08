using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using System.Xml.Linq;
using SpaceCG.Extensions;

namespace SpaceCG.Net
{
    public static partial class RPCExtensions
    {
        
        /// <summary>
        /// 将 InvokeResult 对象转换为 XElement 对象，并返回 XElement 对象的字节数组。
        /// </summary>
        /// <param name="result"></param>
        /// <returns></returns>
        public static byte[] ToXElementBytes(this InvokeResult result)
        {
            if (result == null) return Array.Empty<byte>();

            var type = typeof(InvokeResult);
            var message = new XElement(type.Name);

            foreach (PropertyInfo property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
            {
                if (!property.CanRead) continue;

                var value = property.GetValue(result);
                if (value == null) continue;

                message.Add(new XAttribute(property.Name, value));
            }
            //Trace.WriteLine($"{message}");
            return Encoding.UTF8.GetBytes($"{message}\r\n");
        }

        /// <summary>
        /// 将 XElement 转换为 InvokeMessage 对象。
        /// <para>从 XML 属性中读取 Id、ObjectName、MethodName、Parameters、IsAsync、Timestamp 等信息。</para>
        /// </summary>
        /// <param name="element">包含调用消息属性的 XElement</param>
        /// <returns>成功时返回 InvokeMessage 实例；失败时返回 null</returns>
        public static InvokeMessage ToInvokeMessage(this XElement element)
        {
            if (element == null) return null;

            try
            {
                var idAttr = element.Attribute("Id");
                var objectNameAttr = element.Attribute("ObjectName");
                var methodNameAttr = element.Attribute("MethodName");

                if (idAttr == null || objectNameAttr == null || methodNameAttr == null)
                    return null;

                if (!int.TryParse(idAttr.Value, out var id))
                    return null;

                var objectName = objectNameAttr.Value;
                var methodName = methodNameAttr.Value;

                if (string.IsNullOrWhiteSpace(objectName) || !RPCServer.NamedRegex.IsMatch(objectName))
                    return null;
                if (string.IsNullOrWhiteSpace(methodName) || !RPCServer.NamedRegex.IsMatch(methodName))
                    return null;

                // 解析参数列表
                object[] parameters = null;
                var paramsAttr = element.Attribute("Parameters");
                if (paramsAttr != null && !string.IsNullOrWhiteSpace(paramsAttr.Value))
                {
                    parameters = StringExtensions.ToObjectArray(paramsAttr.Value);
                }

                var message = InvokeMessage.Create(id, objectName, methodName, parameters);

                // 解析 IsAsync
                var isAsyncAttr = element.Attribute("IsAsync");
                if (isAsyncAttr != null && bool.TryParse(isAsyncAttr.Value, out var isAsync))
                {
                    // IsAsync 是只读属性，需要通过反射设置
                    var isAsyncField = typeof(InvokeMessage).GetProperty("IsAsync");
                    isAsyncField?.SetValue(message, isAsync);
                }

                // 解析 Timestamp
                var timestampAttr = element.Attribute("Timestamp");
                if (timestampAttr != null && DateTimeOffset.TryParse(timestampAttr.Value, out var timestamp))
                {
                    message.Timestamp = timestamp;
                }

                return message;
            }
            catch
            {
                return null;
            }
        }

    }
}
