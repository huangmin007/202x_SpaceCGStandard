using System;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using SpaceCG.Extensions;

namespace SpaceCG.Net
{
    /// <summary>
    /// RPC 消息类型接口
    /// </summary>
    internal interface IRpcMessage
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
        /// 消息协议版本号
        /// </summary>
        Version Version { get; }

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
    public class InvokeMessage : IRpcMessage
    {
        /// <summary>  消息协议版本号，当前版本为 2.0.0。  </summary>
        public static readonly Version ProtocolVersion = new Version(2, 0, 0);

        /// <summary> 
        /// 消息唯一标识，用于 请求-响应 消息的匹配跟踪。
        /// <para>默认值为 0，当值小于 0 时（如 0、-1）表示不进行 Id 匹配跟踪，即忽略请求消息的 Id 属性。</para>
        /// </summary>
        public int Id { get; internal set; } = 0;

        /// <summary> 
        /// 接收或处理消息的已注册对象名称。 
        /// </summary>
        public string ObjectName { get; private set; }
        /// <summary> 
        /// 接收或处理消息的已注册对象的公共方法的名称。 
        /// </summary>
        public string MethodName { get; private set; }
        /// <summary> 
        /// 公共方法 <see cref="MethodName"/> 的参数列表。 
        /// </summary>
        public object[] Parameters { get; private set; }

        /// <summary>
        /// 响应模式，默认为 0 即按服务端的默认规则响应。
        /// <list type="bullet">
        /// <item>-1 表示要求服务端不需要响应当前消息。</item>
        /// <item>0 表示按服务端的默认规则响应即可。默认规则：调用异常响应、方法有返回值响应，没返回值不响应 </item>
        /// <item>1 表示要求服务端必须要响应当前消息。</item>
        /// </list>
        /// </summary>
        public int ResponseMode { get; internal set; } = 0;
        
        /// <summary> 
        /// 消息描述信息，保留字段。 
        /// </summary>
        public string Description { get; set; } = string.Empty;
        /// <summary> 
        /// 消息协议版本信息。 
        /// </summary>
        public Version Version { get; set; } = ProtocolVersion;
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
        /// 初始化 <see cref="InvokeMessage"/> 类的新实例。
        /// <para>推荐通过静态工厂方法 <see cref="Create(string, string, object[], int, int)"/> 创建实例。</para>
        /// </summary>
        internal InvokeMessage() 
        {
        }

        /// <inheritdoc cref="Reset" /> 
        void IRpcMessage.Reset() => Reset();
        
        /// <summary>
        /// 重置参数列表，用于重用消息实例。
        /// </summary>
        internal void Reset()
        {
            TcpClient = null;
            ClientEndPoint = null;

            Id = -1;
            ObjectName = string.Empty;
            MethodName = string.Empty;
            Parameters = null;

            ResponseMode = 0;
            Description = string.Empty;
            Timestamp = DateTimeOffset.UtcNow;            
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return $"[{nameof(InvokeMessage)}](Id:{Id}, ResponseMode:{ResponseMode}, ObjectMethod:'{ObjectName}.{MethodName}({Parameters})', Timestamp:'{Timestamp:O}')";
        }

        /// <inheritdoc cref="Create(string, string, object[], int, int)"/>
        public static InvokeMessage Create(string objectName, string methodName)
        {
            return Create(objectName, methodName, string.Empty, -1, 0);
        }

        /// <inheritdoc cref="Create(string, string, object[], int, int)"/>
        public static InvokeMessage Create(string objectName, string methodName, string parameters)
        {
            return Create(objectName, methodName, parameters, -1, 0);
        }

        /// <inheritdoc cref="Create(string, string, object[], int, int)"/>
        public static InvokeMessage Create(string objectName, string methodName, object[] parameters)
        {
            return Create(objectName, methodName, parameters, -1, 0);
        }

        /// <inheritdoc cref="Create(string, string, object[], int, int)"/>
        public static InvokeMessage Create(string objectName, string methodName, string parameters, int id, int responseMode)
        {
            return Create(objectName, methodName, StringExtensions.ParseParameters(parameters), id, responseMode);
        }

        /// <summary>
        /// 创建一个 <see cref=" InvokeMessage"/> 对象。
        /// </summary>
        /// <param name="objectName">目标对象名称，需符合 <see cref="RpcServerBase.IdentifierPattern"/> 命名规则。</param>
        /// <param name="methodName">目标方法名称，需符合 <see cref="RpcServerBase.IdentifierPattern"/> 命名规则。</param>
        /// <param name="parameters">目标方法的参数。</param>
        /// <param name="id"> 消息唯一标识，用于 请求-响应 消息的匹配跟踪。默认值为 0 不进行 Id 跟踪匹配。
        /// <para>注意：当值小于 0 时，不进行 Id 匹配跟踪，即忽略请求消息的 Id 属性。</para>
        /// </param>
        /// <param name="responseMode"> 响应模式，默认为 0 即按服务端的默认规则响应。
        ///     <list type="bullet">
        ///         <item>-1 表示要求服务端不需要响应当前消息。</item>
        ///         <item>0 表示按服务端的默认规则响应即可。默认规则：调用异常响应、方法有返回值响应，没返回值不响应 </item>
        ///         <item>1 表示要求服务端必须要响应当前消息。</item>
        ///     </list>
        /// </param>
        /// <returns></returns>
        public static InvokeMessage Create(string objectName, string methodName, object[] parameters, int id, int responseMode)
        {
            if (string.IsNullOrWhiteSpace(objectName) || !RpcServerBase.IdentifierPattern.IsMatch(objectName))
                throw new ArgumentException(nameof(objectName), "对象名称不能为空或命名格式不正确");
            if (string.IsNullOrWhiteSpace(methodName) || !RpcServerBase.IdentifierPattern.IsMatch(methodName))
                throw new ArgumentException(nameof(methodName), "方法名称不能为空或命名格式不正确");

            return new InvokeMessage()
            {
                Id = id,
                ObjectName = objectName,
                MethodName = methodName,
                Parameters = parameters,
                ResponseMode = responseMode,
            };
        }
    }

    /// <summary>
    /// 远程过程调用(Remote Procedure Call) 或 反射程序控制(Reflection Program Control) 的响应消息对象。
    /// <para>封装方法反射调用后的返回结果，包括状态码、描述信息、返回值等信息。</para>
    /// </summary>
    public class ResponseMessage : IRpcMessage
    {
        /// <summary>
        /// 消息唯一标识，对应请求 <see cref="InvokeMessage.Id"/>，用于 请求-响应 的跟踪匹配。
        /// <para>默认值为 0，当值小于 0 时（如 0、-1）表示不进行 Id 跟踪匹配。</para>
        /// </summary>
        public int Id { get; internal set; } = 0;
        /// <summary>
        /// 调用结果状态码：小于 0 表示失败，大于等于 0 表示成功，等于 1 表示成功且有返回值。
        /// </summary>
        public int Code { get; private set; }

        /// <summary>
        /// 被调用方法的完整名称，格式为 "{ObjectName}.{MethodName}"。
        /// </summary>
        public string ObjectMethod { get; private set; }
        
        /// <summary>
        /// 调用方法的返回值类型，方法返回 <c>void</c> 时此值为 <see cref="void"/>。
        /// </summary>
        public Type ReturnType { get; internal set; }
        /// <summary>
        /// 调用方法的返回值，无返回值时为 <c>null</c>。
        /// </summary>
        public object ReturnValue { get; internal set; }

        /// <summary> 
        /// 调用结果的描述信息，如 "Success" 或错误或异常原因等。 
        /// </summary>
        public string Description { get; set; } = string.Empty;
        /// <summary> 
        /// 消息协议版本号，对应请求 <see cref="InvokeMessage.Version"/>。 
        /// </summary>
        public Version Version { get; set; } = InvokeMessage.ProtocolVersion;
        /// <summary>
        /// 调用结果生成时间戳，使用 ISO8601 标准格式。
        /// <para>用于调试分析、延迟统计、日志追踪、超时判断等场景。</para>
        /// </summary>
        public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;

        /// <summary>
        /// 初始化 <see cref="ResponseMessage"/> 类的新实例。
        /// <para>推荐通过静态工厂方法 <see cref="Create(InvokeMessage, int, string, Type, object)"/> 等重载创建实例。</para>
        /// </summary>
        internal ResponseMessage()
        {
        }

        /// <inheritdoc cref="Reset" /> 
        void IRpcMessage.Reset() => Reset();

        /// <summary>
        /// 重置消息属性为默认值，用于对象池复用时清空上一次调用的遗留数据。
        /// </summary>
        internal void Reset()
        {
            Id = -1;
            Code = 0;
            ObjectMethod = string.Empty;
            Description = string.Empty;

            ReturnType = null;
            ReturnValue = null;

            Timestamp = DateTimeOffset.UtcNow;
        }

        /// <inheritdoc cref="Create(InvokeMessage, int, string, Type, object)" />
        public static ResponseMessage Create(InvokeMessage invokeMessage, int code, string description)
        {
            return Create(invokeMessage, code, description, null, null);
        }

        /// <summary>
        /// 跟据调用消息，创建一个响应消息。
        /// </summary>
        /// <param name="invokeMessage">调用消息</param>
        /// <param name="code">响应状态码</param>
        /// <param name="description">响应描述信息</param>
        /// <param name="returnType">调用结果的返回类型</param>
        /// <param name="returnValue">调用结果的返回值</param>
        /// <returns></returns>
        public static ResponseMessage Create(InvokeMessage invokeMessage, int code, string description, Type returnType, object returnValue)
        {
            if (invokeMessage == null)
                throw new ArgumentNullException(nameof(invokeMessage));

            var responseMessage = new ResponseMessage()
            {
                Id = invokeMessage.Id,
                Version = invokeMessage.Version,
                ObjectMethod = $"{invokeMessage.ObjectName}.{invokeMessage.MethodName}",
            };

            responseMessage.Code = code;
            responseMessage.ReturnType = returnType;
            responseMessage.ReturnValue = returnValue;
            responseMessage.Description = description;

            return responseMessage;
        }

        /// <summary>
        /// 根据原始参数创建响应消息，用于客户端反序列化场景（如 <c>RpcClient4X.DeserializeResponseMessage</c>）。
        /// </summary>
        /// <param name="id">消息唯一标识，对应请求 <see cref="InvokeMessage.Id"/>，用于 请求-响应 的跟踪匹配；
        /// 注意：当值小于 0 时，不进行 Id 跟踪匹配，即忽略请求消息的 Id 属性。</param>
        /// <param name="code">响应状态码</param>
        /// <param name="description">响应描述信息</param>
        /// <param name="objectMethod">被调用方法的完整名称，格式为 "{ObjectName}.{MethodName}"。</param>
        /// <param name="returnType">调用结果的返回类型</param>
        /// <param name="returnValue">调用结果的返回值</param>
        /// <returns></returns>
        public static ResponseMessage Create(int id, int code, string description, string objectMethod, Type returnType, object returnValue)
        {
            var responseMessage = new ResponseMessage();
            responseMessage.Id = id;
            responseMessage.Code = code;
            responseMessage.Description = description;

            responseMessage.ReturnType = returnType;
            responseMessage.ReturnValue = returnValue;
            responseMessage.ObjectMethod = objectMethod;

            return responseMessage;
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return $"[{nameof(ResponseMessage)}](Id:{Id}, Code:{Code}, ObjectMethod:'{ObjectMethod}', Description:'{Description}', Timestamp:'{Timestamp:O}')";
        }
    }

    /// <summary>
    /// 客户端调用消息事件参数，用于 <see cref="RpcServerBase.ClientInvokeRequest"/> 事件。
    /// <para>继承 <see cref="CancelEventArgs"/>，可通过设置 <see cref="CancelEventArgs.Cancel"/> 为 <c>true</c> 来拦截取消执行调用。</para>
    /// </summary>
    public class InvokeMessageEventArgs : CancelEventArgs
    {
        /// <summary>
        /// 发送消息的远程客户端端点地址。
        /// </summary>
        public IPEndPoint RemoteEndPoint { get; internal set; }
        /// <summary>
        /// 客户端调用的消息实例。可读取消息属性，通过设置 <see cref="CancelEventArgs.Cancel"/> 为 <c>true</c> 拦截取消本次调用。
        /// </summary>
        public InvokeMessage InvokeMessage { get; internal set; }

        /// <summary>
        /// 初始化 <see cref="InvokeMessageEventArgs"/> 类的新实例，默认不取消调用。
        /// </summary>
        /// <param name="invokeMessage">客户端调用的消息实例。</param>
        /// <param name="clientEndPoint">发送消息的远程客户端端点地址。</param>
        public InvokeMessageEventArgs(InvokeMessage invokeMessage, IPEndPoint clientEndPoint) : base(false)
        {
            InvokeMessage = invokeMessage;
            RemoteEndPoint = clientEndPoint;
        }

        /// <inheritdoc />
        override public string ToString()
        {
            return $"{nameof(InvokeMessageEventArgs)} RemoteEndPoint:{RemoteEndPoint}, Cancel:{Cancel}, InvokeMessage:{InvokeMessage}";
        }
    }

    /// <summary>
    /// 消息池对象
    /// </summary>
    /// <typeparam name="T">对象类型</typeparam>
    internal class RpcMessagePool<T> where T : IRpcMessage, new()
    {
        /// <summary>
        /// 对象池的最大容量（防止内存无限增长）。
        /// </summary>
        public readonly int MaxCount = 64;

        /// <summary>
        /// 队列池
        /// </summary>
        protected readonly ConcurrentQueue<T> Pool = new ConcurrentQueue<T>();

        /// <summary>
        /// 当前池中对象数（近似值，用于容量控制）。
        /// </summary>
        private int _count;

        /// <summary>
        /// 
        /// </summary>
        public int Count => _count;

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="initialCount"></param>
        public RpcMessagePool(int initialCount) : this(initialCount, 64)
        {
        }

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="initialCount"></param>
        /// <param name="maxCount"></param>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        public RpcMessagePool(int initialCount, int maxCount)
        {
            if (initialCount <= 0 || initialCount > maxCount)
                throw new ArgumentOutOfRangeException(nameof(initialCount));
            if (maxCount <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxCount));

            MaxCount = maxCount;
            for (int i = 0; i < initialCount; i++)
            {
                Pool.Enqueue(new T());
            }
        }

        /// <summary>
        /// 从池中租用一个 <see cref="T"/> 实例。
        /// <para>池为空时创建新实例</para>
        /// </summary>
        /// <returns></returns>
        public T Rent()
        {
            if (Pool.TryDequeue(out var message))
            {
                Interlocked.Decrement(ref _count);
                message.Reset();
                return message;
            }

            return new T();
        }

        /// <summary>
        /// 将 <see cref="T"/> 实例归还到池中。
        /// <para>池容量达到上限时，实例将被丢弃（由 GC 回收）。</para>
        /// </summary>
        /// <param name="message"></param>
        public void Return(T message)
        {
            if (message == null) return;

            // 容量控制：超过上限则丢弃
            if (Interlocked.Increment(ref _count) > MaxCount)
            {
                Interlocked.Decrement(ref _count);
                return;
            }

            message.Reset();
            Pool.Enqueue(message);
        }

        /// <summary>
        /// 清空对象池，释放所有缓存的实例。
        /// <para>通常在服务停止时调用。</para>
        /// </summary>
        public void Clear()
        {
            while (Pool.TryDequeue(out _))
            {
                Interlocked.Decrement(ref _count);
            }
        }
    }

}
