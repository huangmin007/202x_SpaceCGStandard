using System;
using System.Net;
using System.Text;

namespace SpaceCG.Net
{
    /// <summary>
    /// 基于 XML 协议的 RPC 客户端实现（XML-RPC v3.0）。
    /// <para>与服务端 <see cref="RpcServer4X"/> 互换使用，支持 XML 格式的调用消息序列化和响应消息解析。</para>
    /// <para>用法示例：<code>new RpcClient4X("127.0.0.1", 2000).ConnectAsync()</code></para>
    /// </summary>
    public sealed class RpcClient4X : RpcClientBase
    {
        public RpcClient4X(IPAddress address, int port) : base(address, port)
        {
        }
        public RpcClient4X(string address, int port) : base(address, port) { }

        protected override ResponseMessage DeserializeResponseMessage(ArraySegment<byte> dataLine)
        {
            throw new NotImplementedException();
        }

        protected override byte[] SerializeInvokeMessage(InvokeMessage invokeMessage)
        {
            throw new NotImplementedException();
        }
    }
}
