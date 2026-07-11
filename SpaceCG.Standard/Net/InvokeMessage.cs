using System;
using System.ComponentModel;
using System.Net;
using System.Net.Sockets;
using SpaceCG.Extensions;

namespace SpaceCG.Net
{
    /// <summary>
    /// RPC 消息类型接口
    /// </summary>
    internal interface IRPCMessage
    {
        /// <summary> 
        /// 消息唯一标识，用于 请求-响应 消息的匹配。 
        /// </summary>
        int Id { get; }

        /// <summary> 
        /// 消息描述信息说明，或是异常信息说明。 
        /// </summary>
        string Description { get; }

        /// <summary>
        /// 消息时间戳，使用 ISO8601 标准格式。
        /// <para>用于调试分析、延迟统计、日志追踪、超时判断等场景。</para>
        /// </summary>
        DateTimeOffset Timestamp { get; }

        /// <summary>
        /// 重置参数重置，用于重用消息实例。
        /// </summary>
        void Reset();
    }


    /// <summary>
    /// 远程过程调用(Remote Procedure Call) 或 反射程序控制(Reflection Program Control) 的请求消息对象。
    /// <para>封装客户端调用请求的所有信息，包括已注册的目标对象、方法、参数等。</para>
    /// </summary>
    public class InvokeMessage : IRPCMessage
    {
        /// <summary>  消息协议版本号，当前版本为 2.0.0。  </summary>
        public static readonly Version DefaultProtocolVersion = new Version(2, 0, 0);

        /// <summary> 
        /// 消息唯一标识，用于 请求-响应 消息的匹配。 
        /// </summary>
        public int Id { get; set; }

        /// <summary> 
        /// 接收或处理消息的已注册对象名称。 
        /// </summary>
        public string ObjectName { get; internal set; }
        /// <summary> 
        /// 接收或处理消息的已注册对象的公共方法的名称。 
        /// </summary>
        public string MethodName { get; internal set; }
        /// <summary> 
        /// 公共方法 <see cref="MethodName"/> 的参数列表。 
        /// </summary>
        public object[] Parameters { get; internal set; }

        /// <summary>
        /// 响应模式，默认为 0 即按服务端设计的默认规则响应。
        /// <list type="bullet">
        /// <item>-1 表示要求服务端不需要要响应当前消息</item>
        /// <item>0 表示按服务端的默认规则响应即可</item>
        /// <item>1 表示要求服务端必须要响应当前消息</item>
        /// </list>
        /// </summary>
        public int ResponseMode { get; set; } = 0;
        
        /// <summary> 
        /// 消息描述信息。 
        /// </summary>
        public string Description { get; set; } = string.Empty;
        /// <summary>
        /// 消息时间戳，使用 ISO8601 标准格式。
        /// <para>用于调试分析、延迟统计、日志追踪、超时判断等场景。</para>
        /// </summary>
        public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;

        /// <summary> 发送该消息的客户端 <see cref="TcpClient"/>，服务端内部使用，用于后续响应写入。 </summary>
        internal TcpClient TcpClient { get; set; }

        /// <summary> 发送该消息的客户端远程 IP 端点地址，服务端内部使用，用于日志追踪和连接标识。 </summary>
        internal IPEndPoint ClientEndPoint { get; set; }

        /// <summary> 
        /// 消息协议版本信息。 
        /// </summary>
        public Version Version => DefaultProtocolVersion;

        /// <summary>
        /// 内部构造函数，实例由静态工厂方法 <see cref="Create(string, string)"/> 等创建。
        /// </summary>
        internal InvokeMessage() { }

        /// <inheritdoc cref="Reset" /> 
        void IRPCMessage.Reset() => Reset();
        
        /// <summary>
        /// 重置参数列表，用于重用消息实例。
        /// </summary>
        internal void Reset()
        {
            TcpClient = null;
            ClientEndPoint = null;

            Id = 0;
            ObjectName = string.Empty;
            MethodName = string.Empty;
            Parameters = null;

            ResponseMode = 0;
            Description = string.Empty;
            Timestamp = DateTimeOffset.UtcNow;            
        }

        /// <summary> 
        /// 返回消息的摘要字符串表示形式。
        /// </summary>
        public override string ToString()
        {
            return $"[{nameof(InvokeMessage)}](Id:{Id}, ObjectMethod:{ObjectName}.{MethodName}({Parameters}), Version:{Version}, Timestamp:{Timestamp})";
        }

        /// <summary>
        /// 创建一个不带参数的 <see cref="InvokeMessage"/> 实例。
        /// </summary>
        /// <inheritdoc cref="Create(string,string,object[])"/>
        public static InvokeMessage Create(string objectName, string methodName) => Create(objectName, methodName, string.Empty);

        /// <summary>
        /// 创建一个带字符串参数列表的 <see cref="InvokeMessage"/> 实例。
        /// <para>参数以字符串格式传入（如 "1,\"hello\",true", "[0x01,0x02],true"），内部通过 <see cref="StringExtensions.ToObjectArray(string)"/> 转换为对象数组。</para>
        /// </summary>
        /// <inheritdoc cref="Create(string,string,object[])"/>
        public static InvokeMessage Create(string objectName, string methodName, string parameters) => Create(objectName, methodName, StringExtensions.ToObjectArray(parameters));
        
        /// <summary>
        /// 创建一个带对象参数列表的 <see cref="InvokeMessage"/> 实例。
        /// </summary>
        /// <param name="objectName">目标对象名称，需符合 <see cref="RPCServerBase.IdentifierPattern"/> 命名规则。</param>
        /// <param name="methodName">目标方法名称，需符合 <see cref="RPCServerBase.IdentifierPattern"/> 命名规则。</param>
        /// <param name="parameters">可变参数列表。</param>
        /// <returns>创建的消息实例。</returns>
        public static InvokeMessage Create(string objectName, string methodName, params object[] parameters)
        {
            if (string.IsNullOrWhiteSpace(objectName) || !RPCServerBase.IdentifierPattern.IsMatch(objectName))
                throw new ArgumentException(nameof(objectName), "对象名称不能为空或命名格式不正确");
            if (string.IsNullOrWhiteSpace(methodName) || !RPCServerBase.IdentifierPattern.IsMatch(methodName))
                throw new ArgumentException(nameof(methodName), "方法名称不能为空或命名格式不正确");

            return new InvokeMessage() { ObjectName = objectName, MethodName = methodName, Parameters = parameters };
        }

    }

    /// <summary>
    /// 客户端调用消息事件参数，用于 <see cref="RPCServerBase.ClientMessageInvoking"/> 事件。
    /// <para>继承 <see cref="CancelEventArgs"/>，可通过设置 <see cref="CancelEventArgs.Cancel"/> 为 <c>true</c> 来拦截取消执行调用。</para>
    /// </summary>
    public class InvokeMessageEventArgs : CancelEventArgs
    {
        /// <summary>
        /// 发送消息的远程客户端端点地址。
        /// </summary>
        public IPEndPoint RemoteEndPoint { get; internal set; }
        /// <summary>
        /// 客户端调用的消息实例。可通过此属性拦截、修改消息属性、内容等，如修改参数列表、设置响应模式等。
        /// </summary>
        public InvokeMessage InvokeMessage { get; internal set; }

        /// <summary>
        /// 初始化 <see cref="InvokeMessageEventArgs"/> 类的新实例，默认不取消调用。
        /// </summary>
        /// <param name="invokeMessage">客户端调用的消息实例。</param>
        /// <param name="remoteEndPoint">发送消息的远程客户端端点地址。</param>
        public InvokeMessageEventArgs(InvokeMessage invokeMessage, IPEndPoint remoteEndPoint) : base(false)
        {
            InvokeMessage = invokeMessage;
            RemoteEndPoint = remoteEndPoint;
        }

        /// <inheritdoc />
        override public string ToString()
        {
            return $"{nameof(InvokeMessageEventArgs)} RemoteEndPoint:{RemoteEndPoint}, Cancel:{Cancel}, InvokeMessage:{InvokeMessage}";
        }
    }
}
