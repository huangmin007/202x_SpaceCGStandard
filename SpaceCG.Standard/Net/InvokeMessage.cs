using System;
using System.ComponentModel;
using System.Net;
using System.Net.Sockets;

namespace SpaceCG.Net
{
    /// <summary>
    /// 远程过程调用(Remote Procedure Call) 或 反射程序控制(Reflection Program Control) 消息对象。
    /// </summary>
    public class InvokeMessage
    {
        /// <summary>
        /// 消息版本号，当前版本为 1.3。
        /// </summary>
        public static readonly Version Version = new Version(1, 3);

        /// <summary>
        /// 消息唯一标识，用于请求与响应消息的匹配。
        /// </summary>
        public int Id { get; private set; }

        /// <summary>
        /// 接收消息或处理消息的对象名称。
        /// </summary>
        public string ObjectName { get; private set; }

        /// <summary>
        /// 接收消息或处理消息对象的方法名称。
        /// </summary>
        public string MethodName { get; private set; }

        /// <summary>
        /// 接收消息或处理消息的方法参数列表。
        /// </summary>
        public object[] Parameters { get; private set; }

        /// <summary>
        /// 将异步消息分派到指定的同步上下文中执行，默认为 true。
        /// </summary>
        public bool IsAsync { get; set; } = true;

        /// <summary>
        /// 消息创建时间戳或是发送时间戳，使用 ISO8601 标准；用于调试分析，延迟统计，日志追踪，超时判断等。
        /// </summary>
        public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.Now;

        /// <summary>
        /// 用于标识该消息是由哪个客户端发送的，方便在服务端处理时响应不同的客户端。
        /// </summary>
        internal TcpClient TcpClient { get; set; }

        internal InvokeMessage() { }

        /// <inheritdoc /> 
        public override string ToString()
        {
            return $"[{nameof(InvokeMessage)}]({Id} {ObjectName} {MethodName} {Parameters} {Version})";
        }

        /// <summary>
        /// 创建一个 InvokeMessage 实例。
        /// </summary>
        /// <param name="id"></param>
        /// <param name="objectName"></param>
        /// <param name="methodName"></param>
        /// <returns></returns>
        public static InvokeMessage Create(int id, string objectName, string methodName) => Create(id, objectName, methodName, null);

        /// <summary>
        /// 创建一个 InvokeMessage 实例。
        /// </summary>
        /// <param name="id"></param>
        /// <param name="objectName"></param>
        /// <param name="methodName"></param>
        /// <param name="parameters"></param>
        /// <returns></returns>
        public static InvokeMessage Create(int id, string objectName, string methodName, object[] parameters)
        {
            if (string.IsNullOrWhiteSpace(objectName) || !RPCServer.NamedRegex.IsMatch(objectName))
                throw new ArgumentException(nameof(objectName), "对象名称不能为空或命名格式不正确");
            if (string.IsNullOrWhiteSpace(methodName) || !RPCServer.NamedRegex.IsMatch(methodName))
                throw new ArgumentException(nameof(methodName), "方法名称不能为空或命名格式不正确");

            return new InvokeMessage() { Id = id, ObjectName = objectName, MethodName = methodName, Parameters = parameters };
        }
    }

    /// <summary>
    /// 远程客户端发送的调用消息事件参数
    /// </summary>
    public class InvokeMessageEventArgs : CancelEventArgs
    {
        /// <summary>
        /// 远程调用端点
        /// </summary>
        public IPEndPoint RemoteEndPoint { get; internal set; }

        /// <summary>
        /// 调用消息
        /// </summary>
        public InvokeMessage InvokeMessage { get; internal set; }

        /// <summary>
        /// 初始化 <see cref="InvokeMessageEventArgs"/> 类的新实例
        /// </summary>
        /// <param name="invokeMessage"></param>
        /// <param name="remoteEndPoint"></param>
        public InvokeMessageEventArgs(InvokeMessage invokeMessage, IPEndPoint remoteEndPoint) : base(false)
        {
            InvokeMessage = invokeMessage;
            RemoteEndPoint = remoteEndPoint;
        }

        /// <summary>
        /// 初始化 <see cref="InvokeMessageEventArgs"/> 类的新实例
        /// </summary>
        /// <param name="cancel"></param>
        /// <param name="invokeMessage"></param>
        /// <param name="remoteEndPoint"></param>
        public InvokeMessageEventArgs(bool cancel, InvokeMessage invokeMessage, IPEndPoint remoteEndPoint) : base(cancel)
        {
            InvokeMessage = invokeMessage;
            RemoteEndPoint = remoteEndPoint;
        }

        /// <inheritdoc/> 
        override public string ToString()
        {
            return $"{nameof(InvokeMessageEventArgs)} Cancel:{Cancel}, RemoteEndPoint:{RemoteEndPoint}, InvokeMessage:{InvokeMessage}";
        }
    }
}
