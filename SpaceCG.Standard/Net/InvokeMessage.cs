using System;
using System.ComponentModel;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using SpaceCG.Extensions;

namespace SpaceCG.Net
{
    /// <summary>
    /// 远程过程调用(Remote Procedure Call) 或 反射程序控制(Reflection Program Control) 的请求消息对象。
    /// <para>封装客户端调用请求的所有信息，包括目标对象、方法、参数等。</para>
    /// </summary>
    public class InvokeMessage
    {
        /// <summary>  消息协议版本号，当前版本为 1.5.0。  </summary>
        public static readonly Version VERSION = new Version(1, 5, 0);
        /// <summary> 对象名称或方法名称的命名规则正则表达式，引用 <see cref="RPCServerBase.NamedRegex"/>。 </summary>
        public static readonly Regex NamedRegex = RPCServerBase.NamedRegex;

        /// <summary> 消息唯一标识，用于请求与响应消息的匹配。 </summary>
        public int Id { get; set; }

        /// <summary> 接收或处理消息的目标对象名称。 </summary>
        public string ObjectName { get; private set; }
        /// <summary> 接收或处理消息的目标对象的方法名称。 </summary>
        public string MethodName { get; private set; }
        /// <summary> 方法调用的参数列表。 </summary>
        public object[] Parameters { get; private set; }
        
        /// <summary> 是否将消息分派到异步上下文中执行，默认为 <c>true</c>。 </summary>
        public bool IsAsync { get; set; } = true;
        /// <summary> 消息描述信息。 </summary>
        public string Description { get; set; } = string.Empty;
        /// <summary>
        /// 消息创建/发送时间戳，使用 ISO8601 标准格式。
        /// <para>用于调试分析、延迟统计、日志追踪、超时判断等场景。</para>
        /// </summary>
        public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.Now;

        /// <summary> 发送该消息的客户端 <see cref="TcpClient"/>，服务端内部使用，用于后续响应。 </summary>
        internal TcpClient TcpClient { get; set; }

        /// <summary> 发送该消息的客户端远程端点地址，服务端内部使用。 </summary>
        internal IPEndPoint ClientEndPoint { get; set; }

        /// <summary> 消息协议版本信息。 </summary>
        public Version Version => VERSION;

        /// <summary>
        /// 内部构造函数，实例由静态工厂方法 <see cref="Create(string, string)"/> 等创建。
        /// </summary>
        internal InvokeMessage() { }

        /// <summary> 
        /// 返回消息的摘要字符串表示形式。
        /// </summary>
        public override string ToString()
        {
            return $"[{nameof(InvokeMessage)}](Id:{Id} ObjectMethod:{ObjectName}.{MethodName}({Parameters}) Version:{Version} Timestamp:{Timestamp})";
        }

        /// <summary>
        /// 创建一个不带参数的 <see cref="InvokeMessage"/> 实例。
        /// </summary>
        /// <param name="objectName">目标对象名称，需符合 <see cref="NamedRegex"/> 命名规则。</param>
        /// <param name="methodName">目标方法名称，需符合 <see cref="NamedRegex"/> 命名规则。</param>
        /// <returns>创建的消息实例。</returns>
        public static InvokeMessage Create(string objectName, string methodName) => Create(objectName, methodName, string.Empty);

        /// <summary>
        /// 创建一个带字符串参数列表的 <see cref="InvokeMessage"/> 实例。
        /// <para>参数以字符串格式传入（如 "[1, \"hello\", true]"），内部通过 <see cref="SpaceCG.Extensions"/> 转换为对象数组。</para>
        /// </summary>
        /// <param name="objectName">目标对象名称，需符合 <see cref="NamedRegex"/> 命名规则。</param>
        /// <param name="methodName">目标方法名称，需符合 <see cref="NamedRegex"/> 命名规则。</param>
        /// <param name="parameters">JSON 数组格式的参数列表字符串。</param>
        /// <returns>创建的消息实例。</returns>
        /// <exception cref="ArgumentException">对象名称或方法名称格式不合法时抛出。</exception>
        public static InvokeMessage Create(string objectName, string methodName, string parameters)
        {
            if (string.IsNullOrWhiteSpace(objectName) || !RPCServerBase.NamedRegex.IsMatch(objectName))
                throw new ArgumentException(nameof(objectName), "对象名称不能为空或命名格式不正确");
            if (string.IsNullOrWhiteSpace(methodName) || !RPCServerBase.NamedRegex.IsMatch(methodName))
                throw new ArgumentException(nameof(methodName), "方法名称不能为空或命名格式不正确");

            return new InvokeMessage() { ObjectName = objectName, MethodName = methodName, Parameters = parameters.ToObjectArray() };
        }

        /// <summary>
        /// 创建一个带对象参数列表的 <see cref="InvokeMessage"/> 实例。
        /// </summary>
        /// <param name="objectName">目标对象名称，需符合 <see cref="NamedRegex"/> 命名规则。</param>
        /// <param name="methodName">目标方法名称，需符合 <see cref="NamedRegex"/> 命名规则。</param>
        /// <param name="parameters">可变参数列表。</param>
        /// <returns>创建的消息实例。</returns>
        public static InvokeMessage Create(string objectName, string methodName, params object[] parameters)
        {
            if (string.IsNullOrWhiteSpace(objectName) || !RPCServerBase.NamedRegex.IsMatch(objectName))
                throw new ArgumentException(nameof(objectName), "对象名称不能为空或命名格式不正确");
            if (string.IsNullOrWhiteSpace(methodName) || !RPCServerBase.NamedRegex.IsMatch(methodName))
                throw new ArgumentException(nameof(methodName), "方法名称不能为空或命名格式不正确");

            return new InvokeMessage() { ObjectName = objectName, MethodName = methodName, Parameters = parameters };
        }
    }

    /// <summary>
    /// 客户端调用消息事件参数，用于 <see cref="RPCServerBase.ClientMessageInvoking"/> 事件。
    /// <para>继承 <see cref="CancelEventArgs"/>，可通过设置 <see cref="CancelEventArgs.Cancel"/> 为 <c>true</c> 来拦截取消调用。</para>
    /// </summary>
    public class InvokeMessageEventArgs : CancelEventArgs
    {
        /// <summary>
        /// 发送消息的客户端远程端点地址。
        /// </summary>
        public IPEndPoint RemoteEndPoint { get; internal set; }
        /// <summary>
        /// 客户端调用消息实例。
        /// </summary>
        public InvokeMessage InvokeMessage { get; internal set; }

        /// <summary>
        /// 初始化 <see cref="InvokeMessageEventArgs"/> 类的新实例，默认不取消调用。
        /// </summary>
        /// <param name="invokeMessage">客户端调用消息实例。</param>
        /// <param name="remoteEndPoint">发送消息的客户端远程端点地址。</param>
        public InvokeMessageEventArgs(InvokeMessage invokeMessage, IPEndPoint remoteEndPoint) : base(false)
        {
            InvokeMessage = invokeMessage;
            RemoteEndPoint = remoteEndPoint;
        }

        /// <summary>
        /// 初始化 <see cref="InvokeMessageEventArgs"/> 类的新实例，并指定初始取消状态。
        /// </summary>
        /// <param name="cancel">是否预设取消状态。</param>
        /// <param name="invokeMessage">客户端调用消息实例。</param>
        /// <param name="remoteEndPoint">发送消息的客户端远程端点地址。</param>
        public InvokeMessageEventArgs(bool cancel, InvokeMessage invokeMessage, IPEndPoint remoteEndPoint) : base(cancel)
        {
            InvokeMessage = invokeMessage;
            RemoteEndPoint = remoteEndPoint;
        }

        /// <summary> 返回事件参数的摘要字符串表示形式。 </summary>
        override public string ToString()
        {
            return $"{nameof(InvokeMessageEventArgs)} Cancel:{Cancel}, RemoteEndPoint:{RemoteEndPoint}, InvokeMessage:{InvokeMessage}";
        }
    }
}
