using System;

namespace SpaceCG.IO
{
    /// <summary>
    /// 传输通道类型枚举。
    /// </summary>
    public enum TransportType
    {
        /// <summary>  串口通信（RS-232/RS-485 等）  </summary>
        SERIAL,

        /// <summary>  TCP 客户端通信  </summary>
        TCP,

        /// <summary>  UDP 客户端通信  </summary>
        UDP,
    }

    /// <summary>
    /// 同步传输通道抽象接口，定义与底层 I/O 通道无关的数据读写契约。
    /// <para>实现类需支持串口（SerialPort）、TCP（TcpClient）、UDP（UdpClient）多种传输方式。</para>
    /// <para>线程安全：此接口不保证线程安全，调用方需自行同步对同一实例的读写操作。</para>
    /// </summary>
    public interface ITransportChannel : IDisposable
    {
        /// <summary>
        /// 获取传输通道类型（串口 / TCP / UDP）
        /// </summary>
        TransportType Type { get; }

        /// <summary>
        /// 获取传输通道的标识名称。
        /// <para>格式示例："SERIAL_COM3_115200"、"TCP_192.168.1.100_8080"。</para>
        /// </summary>
        string Name { get; }

        /// <summary> 
        /// 获取或设置一个用于存储有关此元素的自定义信息的任意对象值。 
        /// </summary>
        object Tag { get; set; }

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
}
