using System;

namespace SpaceCG.IO
{
    /// <summary>
    /// 传输通道类型枚举。
    /// </summary>
    public enum ChannelType
    {
        /// <summary>  串口通信（RS-232/RS-485 等）  </summary>
        Serial,

        /// <summary>  TCP 客户端通信  </summary>
        TCP,

        /// <summary>  UDP 客户端通信  </summary>
        UDP,
    }

    /// <summary>
    /// 同步传输通道抽象接口，定义与底层 I/O 通道无关的数据读写契约。
    /// </summary>
    /// <remarks>
    /// <para><b>应用用途</b>：本接口是设备通信体系的「传输底座」。它向上屏蔽串口、TCP、UDP 等不同物理链路的差异，对外只暴露统一的 <see cref="Read"/> / <see cref="Write"/> 数据流抽象。
    /// 上层协议组件（如 <see cref="IRequestResponseClient"/> 请求-响应客户端、数据帧解析器）只需依赖本接口，即可在串口与网络设备之间无缝切换，而无需关心具体的连接与收发细节。</para>
    /// <para><b>设计意图</b>：适用于封装不关心通信或连接状态的应用层业务，将「传输方式的选择」延迟到运行时，通过 <see cref="TransportChannel.Create"/> 工厂按需构造。</para>
    /// <para><b>实现要求</b>：实现类需支持串口（SerialPort）、TCP（TcpClient）、UDP（UdpClient）多种传输方式。</para>
    /// <para><b>线程安全</b>：此接口不保证线程安全，调用方需自行同步对同一实例的读写操作。</para>
    /// </remarks>
    public interface ITransportChannel : IDisposable
    {
        /// <summary>
        /// 获取传输通道类型（串口 / TCP / UDP）
        /// </summary>
        ChannelType Type { get; }

        /// <summary>
        /// 获取传输通道的标识名称。
        /// <para>格式示例："SERIAL_COM3_115200"、"TCP_192.168.1.100_8080"。</para>
        /// </summary>
        string Name { get; }

        /// <summary>
        /// 获取传输通道当前是否处于连接状态。
        /// </summary>
        bool IsConnected { get; }

        /// <summary>
        /// 获取接收缓冲区中可立即读取的字节数。
        /// </summary>
        int Available { get; }

        /// <summary>
        /// 获取或设置读取操作的超时时间（毫秒）。
        /// </summary>
        int ReadTimeout { get; set; }

        /// <summary>
        /// 获取或设置写入操作的超时时间（毫秒）。
        /// </summary>
        int WriteTimeout { get; set; }

        /// <summary>
        /// 打开传输连接。
        /// <para>调用前应确保已配置必要的连接参数（端口号、波特率等）。</para>
        /// </summary>
        void Open();

        /// <summary>
        /// 关闭传输连接并释放底层资源。
        /// <para>关闭后可再次调用 <see cref="Open"/> 重新建立连接。</para>
        /// </summary>
        void Close();

        /// <summary>
        /// 丢弃接收缓冲区中的所有数据。
        /// </summary>
        void ClearReadBuffer();

        /// <summary>
        /// 丢弃发送缓冲区中的所有数据。
        /// </summary>
        void ClearWriteBuffer();

        /// <summary>
        /// 从传输通道同步读取数据。
        /// </summary>
        /// <param name="buffer">接收数据的字节数组。</param>
        /// <param name="offset"><paramref name="buffer"/> 中开始写入数据的偏移量。</param>
        /// <param name="count">最多读取的字节数。</param>
        /// <returns>实际读取的字节数；若连接已关闭则返回 0。</returns>
        int Read(byte[] buffer, int offset, int count);

        /// <summary>
        /// 同步写入数据到传输通道。
        /// </summary>
        /// <param name="buffer">包含要写入数据的字节数组。</param>
        /// <param name="offset"><paramref name="buffer"/> 中开始读取数据的偏移量。</param>
        /// <param name="count">要写入的字节数。</param>
        void Write(byte[] buffer, int offset, int count);
    }

    /// <summary>
    /// 传输通道工厂，提供按类型与参数字符串创建 <see cref="ITransportChannel"/> 实例的静态入口。
    /// </summary>
    /// <remarks>
    /// <para><b>应用用途</b>：作为设备通信体系的统一构造点，将「通道类型 + 连接参数」字符串
    /// 解析为对应的传输通道实例，供上层业务在运行时动态选择串口 / TCP / UDP 通道。</para>
    /// <para><b>参数格式</b>：<paramref name="arguments"/> 中的多个参数用逗号 <c>,</c>、冒号 <c>:</c>
    /// 或分号 <c>;</c> 之一分隔（三种分隔符不可混用），各通道所需参数由对应传输实现类约定。</para>
    /// </remarks>
    public static class TransportChannel
    {
        /// <summary>
        /// 根据通道类型和参数字符串创建传输通道实例。
        /// </summary>
        /// <param name="type">要创建的通道类型（串口 / TCP / UDP）。</param>
        /// <param name="arguments">连接参数字符串，多个参数用逗号、冒号或分号之一分隔。</param>
        /// <returns>对应的 <see cref="ITransportChannel"/> 实例。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="arguments"/> 为 null 或空白时抛出。</exception>
        /// <exception cref="ArgumentException">
        /// <paramref name="arguments"/> 不包含任何支持的分隔符，或 <paramref name="type"/> 为不支持的通道类型时抛出。
        /// </exception>
        public static ITransportChannel Create(ChannelType type, string arguments)
        {
            if (string.IsNullOrWhiteSpace(arguments))
                throw new ArgumentNullException(nameof(arguments));

            string[] args = null;
            if (arguments.IndexOf(',') != -1) args = arguments.Split(',');
            else if (arguments.IndexOf(':') != -1) args = arguments.Split(':');
            else if (arguments.IndexOf(';') != -1) args = arguments.Split(';');
            else throw new ArgumentException("参数格式不正确，多个参数以逗号分隔", nameof(arguments));

            if (type == ChannelType.Serial)
                return new SerialPortTransport(args);
            else if (type == ChannelType.TCP)
                return new TcpClientTransport(args);
            else if (type == ChannelType.UDP)
                return new UdpClientTransport(args);

            throw new ArgumentException("不支持的传输通道类型", nameof(type));
        }
    }
}
