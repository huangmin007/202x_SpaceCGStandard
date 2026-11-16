using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Xml.Linq;
using System.IO.Ports;
using System.Text.RegularExpressions;
using System.Threading;
using System.IO;
using System.Threading.Tasks;

namespace SpaceCG.Extensions
{
    /// <summary>
    /// SerialPort Extensions
    /// </summary>
    public static partial class SerialPortExtensions
    {
        /// <summary>
        /// Windows 串口名称的正则表达式
        /// </summary>
        public static readonly Regex PortNameRegexForWindows = new Regex("^COM[0-9]{1,2}$", RegexOptions.IgnoreCase);

        /// <summary>
        /// 串口 Serial_ErrorReceived 事件的错误描述信息
        /// </summary>
        public static readonly IReadOnlyDictionary<SerialError, string> SerialErrorDescriptions = new Dictionary<SerialError, string>(8)
        {
            { SerialError.RXOver,   "发生输入缓冲区溢出，输入缓冲区空间不足，或在文件尾 (EOF) 字符之后接收到字符" },
            { SerialError.Overrun,  "发生字符缓冲区溢出，下一个字符将丢失" },
            { SerialError.RXParity, "硬件检测到奇偶校验错误" },
            { SerialError.Frame,    "硬件检测到一个组帧错误" },
            { SerialError.TXFull,   "应用程序尝试传输一个字符，但是输出缓冲区已满" },
        };


        #region 创建串口实例对象
        /// <summary>
        /// 创建串口实例对象
        /// <para>按基本属性顺序排列：PortName、BaudRate、DataBits、StopBits、Parity，其中 PortName、BaudRate 为必填参数，其余可选不配置</para>
        /// </summary>
        /// <param name="config"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="ArgumentException"></exception>
        public static SerialPort CreateInstance(string config) => CreateInstance(config?.Split(','));
        /// <summary>
        /// 创建串口实例对象
        /// <para>按基本属性顺序排列：PortName、BaudRate、DataBits、StopBits、Parity，其中 PortName、BaudRate 为必填参数，其余可选不配置</para>
        /// </summary>
        /// <param name="config"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="ArgumentException"></exception>
        public static SerialPort CreateInstance(IEnumerable<string> config)
        {
            if (config == null) throw new ArgumentNullException("config");
            if (config.Count() < 2) throw new ArgumentException("配置串口参数错误，至少需要配置两个参数： PortName 和 BaudRate 参数");

            string[] propertyNames = { nameof(SerialPort.PortName), nameof(SerialPort.BaudRate), nameof(SerialPort.DataBits), nameof(SerialPort.StopBits), nameof(SerialPort.Parity) };

            var count = Math.Min(config.Count(), propertyNames.Length);
            var dictionary = new Dictionary<string, string>(count);

            for (int i = 0; i < count; i++)
            {
                dictionary.Add(propertyNames[i], config.ElementAt(i));
            }

            return CreateInstance(dictionary);
        }
        /// <summary>
        /// 创建串口实例对象
        /// </summary>
        /// <param name="config"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="ArgumentException"></exception>
        public static SerialPort CreateInstance(IReadOnlyDictionary<string, string> config)
        {
            if (config == null) throw new ArgumentNullException("config");
            if (config.Count < 2 || !config.ContainsKey(nameof(SerialPort.PortName)) || !config.ContainsKey(nameof(SerialPort.BaudRate)))
                throw new ArgumentException("配置串口参数错误，至少需要配置两个参数： PortName 和 BaudRate 参数");

            SerialPort serialPort = new SerialPort();
            foreach (var attribute in config)
            {
                var propertyName = attribute.Key;
                var propertyValue = attribute.Value;

                if (propertyName == nameof(SerialPort.PortName))
                {
                    propertyValue = GetPortName(propertyValue);
                }

                if (!InstanceExtensions.TrySetPropertyValue(serialPort, propertyName, propertyValue))
                {
                    Trace.TraceWarning($"配置串口属性 {propertyName} 值 {propertyValue} 错误，参数无效");
                }
            }

            return serialPort;
        }
        /// <summary>
        /// 跟据 XElement 节点属性，创建串口实例对象
        /// </summary>
        /// <param name="config"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="ArgumentException"></exception>
        public static SerialPort CreateInstance(XElement config)
        {
            if (config == null) throw new ArgumentNullException("config");
            if (!config.HasAttributes) throw new ArgumentException("配置串口参数错误，至少需要配置两个属性：PortName、BaudRate 属性");

            SerialPort serialPort = new SerialPort();
            foreach (var attribute in config.Attributes())
            {
                string propertyName = attribute.Name.LocalName;
                string propertyValue = attribute.Value;

                if (propertyName == nameof(SerialPort.PortName))
                {
                    propertyValue = GetPortName(propertyValue);
                }

                if (!InstanceExtensions.TrySetPropertyValue(serialPort, propertyName, propertyValue))
                {
                    Trace.TraceWarning($"配置串口属性 {propertyName} 值 {propertyValue} 错误，参数无效");
                }
            }

            return serialPort;
        }
        #endregion
                

        #region 获取串口信息列表
        /// <summary>
        /// 获取当前计算机的唯一的串行端口号
        /// </summary>
        /// <param name="searchPattern">查找匹配字符，不区分大小写。</param>
        /// <returns>返回唯一串行端口号名称（例如："COM3"，"COM14"），如果没有找到则返回空字符串；如果找到多个，则返回其中的第一个。</returns>
        public static string GetPortName(string searchPattern)
        {
            if (PortNameRegexForWindows.IsMatch(searchPattern)) return searchPattern;

            var portNames = GetPortNames(searchPattern);
            if (portNames == null || portNames.Count() == 0) return string.Empty;

            var portName = portNames.First();

            int left = portName.LastIndexOf('(');
            int right = portName.LastIndexOf(')');
            if (left == -1 || right == -1) return string.Empty;

            return portName.Substring(left + 1, right - left - 1);
        }
        /// <summary>
        /// 获取当前计算机的  完整名称的串行端口 的集合
        /// <para>与 <see cref="System.IO.Ports.SerialPort.GetPortNames"/> 函数不同，<see cref="System.IO.Ports.SerialPort.GetPortNames"/> 只输出类似"COM3,COM4,COMn"；该函数输出串口对象的名称或是驱动名，类似："USB Serial Port (COM3)" ... </para>
        /// <para>这只是 WMI 示例应用函数，用于查询 串口名称 信息。更多应用参考 WMI。</para>
        /// </summary>
        /// <returns></returns>
        public static IEnumerable<string> GetPortNames()
        {
            string query = "SELECT Name FROM Win32_PnPEntity WHERE PNPClass='Ports' AND (Name LIKE '%(COM_)' OR Name LIKE '%(COM__)')";

            try
            {
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher(query))
                {
                    var portNames = from ManagementObject obj in searcher.Get()
                                    from PropertyData pd in obj.Properties
                                    where !string.IsNullOrWhiteSpace(pd.Name) && pd.Value != null
                                    select pd.Value.ToString();

                    return portNames;
                }
            }
            catch (Exception ex)
            {
                Trace.TraceError($"GetPortNames Error: {ex.Message}");
            }

            return Array.Empty<string>();
        }
        /// <summary>
        /// 获取当前计算机的 完整名称的串行端口 的集合
        /// <para>通配符：百分号(%)，表示匹配零个或多个任意字符串；下划线(_)，表示匹配一个任意字符串</para>
        /// <para>例如："Serial Port (COM_)"，"USB Serial Port (COM%)"，"%SERIAL-A%"</para>
        /// </summary>
        /// <param name="searchPattern">查找匹配字符，不区分大小写。</param>
        /// <returns></returns>
        public static IEnumerable<string> GetPortNames(string searchPattern)
        {
            string query = $"SELECT Name FROM Win32_PnPEntity WHERE PNPClass='Ports' AND (Name LIKE '%{searchPattern}%')";

            try
            {
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher(query))
                {
                    var portNames = from ManagementObject obj in searcher.Get()
                                    from PropertyData pd in obj.Properties
                                    where !string.IsNullOrWhiteSpace(pd.Name) && pd.Value != null
                                    select pd.Value.ToString();

                    return portNames;
                }
            }
            catch (Exception ex)
            {
                Trace.TraceError($"GetPortNames Error: {ex.Message}");
            }

            return Array.Empty<string>();
        }        
        /// <summary>
        /// 获取当前计算机的 完整名称的串行端口 的字典集合
        /// <para>示例：key:"USB-SERIAL CH340", value:"COM3"</para>
        /// </summary>
        /// <returns></returns>
        public static IReadOnlyDictionary<string, string> GetPortNamesWithDict()
        {
            var dict = new Dictionary<string, string>();
            IEnumerable<string> portNames = GetPortNames();
            if (portNames == null || portNames.Count() == 0) return dict;

            foreach (var name in portNames)
            {
                int left = name.LastIndexOf('(');
                int right = name.LastIndexOf(')');
                if (left == -1 || right == -1) continue;

                var key = name.Substring(0, left).Trim();
                var value = name.Substring(left + 1, right - left - 1);
                dict.Add(key, value);
            }

            return dict;
        }
        #endregion


        #region Transceive
        /// <summary>
        /// 向串口发送数据，并阻塞等待接收指定长度的响应数据。此方法是同步（阻塞）调用，所有异常均向上层抛出。
        /// </summary>
        /// <param name="serialPort">已打开的串口实例。</param>
        /// <param name="data">待发送的数据缓冲区。</param>
        /// <param name="offset"><paramref name="data"/> 中开始发送的字节偏移量。</param>
        /// <param name="length">从 <paramref name="data"/> 中发送的字节数。</param>
        /// <param name="responseLength">期望接收的响应数据字节数。</param>
        /// <returns>接收到的完整响应数据，长度等于 <paramref name="responseLength"/>。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="serialPort"/> 为 <c>null</c>。</exception>
        /// <exception cref="InvalidOperationException">串口未打开。</exception>
        /// <exception cref="TimeoutException">在 <see cref="SerialPort.ReadTimeout"/> 时间内未收到完整响应。</exception>
        /// <exception cref="IOException">串口写入或读取时发生 I/O 错误。</exception>
        /// <remarks> 调用前会先清空接收缓冲区（<see cref="SerialPort.DiscardInBuffer"/>），以确保收到的响应数据对应当前发送的命令。超时时间由 <see cref="SerialPort.ReadTimeout"/> 决定，默认为 300ms。
        /// </remarks>
        public static byte[] Transceive(this SerialPort serialPort, byte[] data, int offset, int length, int responseLength)
        {
            if (serialPort == null) throw new ArgumentNullException(nameof(serialPort));
            if (!serialPort.IsOpen) throw new InvalidOperationException("串口未打开");
            if (data == null || data.Length == 0) throw new ArgumentNullException(nameof(data));

            serialPort.DiscardInBuffer();
            serialPort.BaseStream.Write(data, offset, length);
            serialPort.BaseStream.Flush();

            if (responseLength <= 0) return Array.Empty<byte>();

            var received = 0;
            var buffer = new byte[responseLength];

            var spinWait = new SpinWait();
            var beginTime = Environment.TickCount;
            var readTimeout = serialPort.ReadTimeout > 0 ? serialPort.ReadTimeout : 300;

            while (true)
            {
                if (Environment.TickCount - beginTime >= readTimeout)
                {
                    throw new TimeoutException($"串口通道 ({serialPort.PortName}) 读取超时");
                }

                int available = serialPort.BytesToRead;
                if (available <= 0)
                {
                    spinWait.SpinOnce();
                    continue;
                }

                spinWait.Reset();

                var readCount = Math.Min(available, responseLength - received);
                var bytesToRead = serialPort.BaseStream.Read(buffer, received, readCount);

                received += bytesToRead;
                if (received == responseLength) return buffer;
            }
        }
        /// <summary>
        /// 向串口发送 <paramref name="data"/> 的全部内容，并阻塞等待接收指定长度的响应数据。
        /// </summary>
        /// <param name="serialPort">已打开的串口实例。</param>
        /// <param name="data">待发送的数据缓冲区。</param>
        /// <param name="responseLength">期望接收的响应数据字节数。</param>
        /// <returns>接收到的完整响应数据，长度等于 <paramref name="responseLength"/>。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="data"/> 为 <c>null</c>。</exception>
        /// <inheritdoc cref="Transceive(SerialPort, byte[], int, int, int)"/>
        public static byte[] Transceive(this SerialPort serialPort, byte[] data, int responseLength) => serialPort.Transceive(data, 0, data.Length, responseLength);
        #endregion


        #region TransceiveAsync
        /// <summary>
        /// 异步向串口发送数据，并等待接收指定长度的响应数据。<br />
        /// 注意：在 .NET Framework 4.8 下，串口底层不支持真正的异步 I/O，此方法通过 <see cref="SerialPort.BytesToRead"/> 预检 + 异步轮询实现，避免 ReadAsync 在无数据时永久阻塞。
        /// </summary>
        /// <param name="serialPort">已打开的串口实例。</param>
        /// <param name="data">待发送的数据缓冲区。</param>
        /// <param name="offset"><paramref name="data"/> 中开始发送的字节偏移量。</param>
        /// <param name="sendLength">从 <paramref name="data"/> 中发送的字节数。</param>
        /// <param name="responseLength">期望接收的响应数据字节数。</param>
        /// <param name="cancellationToken">用于取消操作的取消令牌。</param>
        /// <returns>接收到的完整响应数据。</returns>
        /// <exception cref="OperationCanceledException">操作被取消。</exception>
        /// <inheritdoc cref="Transceive(SerialPort, byte[], int, int, int)"/>
        public static async Task<byte[]> TransceiveAsync(this SerialPort serialPort, byte[] data, int offset, int sendLength, int responseLength, CancellationToken cancellationToken)
        {
            if (serialPort == null) throw new ArgumentNullException(nameof(serialPort));
            if (!serialPort.IsOpen) throw new InvalidOperationException("串口未打开");
            if (data == null || data.Length == 0) throw new ArgumentNullException(nameof(data));

            // 清空接收缓冲区，发送命令
            serialPort.DiscardInBuffer();
            await serialPort.BaseStream.WriteAsync(data, offset, sendLength, cancellationToken).ConfigureAwait(false);
            await serialPort.BaseStream.FlushAsync(cancellationToken).ConfigureAwait(false);

            if (responseLength <= 0) return Array.Empty<byte>();

            var received = 0;
            var buffer = new byte[responseLength];

            var spinWait = new SpinWait();
            var beginTime = Environment.TickCount;
            var readTimeout = serialPort.ReadTimeout > 0 ? serialPort.ReadTimeout : 300;

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (Environment.TickCount - beginTime >= readTimeout)
                {
                    throw new TimeoutException($"串口通道 ({serialPort.PortName}) 读取超时：已接收 {received}/{responseLength} 字节");
                }

                int available = serialPort.BytesToRead;
                if (available <= 0)
                {
                    spinWait.SpinOnce();
                    // 无数据可读，异步等待 1ms 后重试（避免同步 Sleep 阻塞线程）
                    //await Task.Delay(1, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                spinWait.Reset();
                var readCount = Math.Min(available, responseLength - received);

                // 确认有数据后才调用 ReadAsync，避免永久阻塞
                var bytesToRead = await serialPort.BaseStream.ReadAsync(buffer, received, readCount, cancellationToken).ConfigureAwait(false);

                if (bytesToRead == 0)
                    throw new IOException($"串口通道 ({serialPort.PortName}) 连接已关闭");

                received += bytesToRead;
                if (received == responseLength) return buffer;
            }
        }
        /// <summary>
        /// 异步向串口发送 <paramref name="data"/> 的全部内容，并等待接收指定长度的响应数据。
        /// </summary>
        /// <inheritdoc cref="TransceiveAsync(SerialPort, byte[], int, int, int, CancellationToken)"/>
        public static Task<byte[]> TransceiveAsync(this SerialPort serialPort, byte[] data, int responseLength, CancellationToken cancellationToken)
            => serialPort.TransceiveAsync(data, 0, data.Length, responseLength, cancellationToken);
        #endregion


#if false
        /// <summary>
        /// 串口设备热插拔自动重新连接
        /// <para>使用 ManagementEventWatcher WQL 事件监听模式</para>
        /// </summary>
        /// <param name="serialPort"></param>
        public static void AutoReconnection(this SerialPort serialPort)
        {
            if (serialPort == null) throw new ArgumentException("参数不能为空");
            Trace.TraceInformation("ManagementEventWatcher WQL Event Listen SerialPort Name:{0}", serialPort.PortName);

            TimeSpan withinInterval = TimeSpan.FromSeconds(1);
            string wql_condition = $"TargetInstance isa 'Win32_PnPEntity' AND TargetInstance.Name LIKE '%({serialPort.PortName.ToUpper()})'";

            ManagementScope scope = new ManagementScope(@"\\.\Root\CIMV2")
            {
                Options = new ConnectionOptions() { EnablePrivileges = true },
            };

            ManagementEventWatcher CreationEvent = new ManagementEventWatcher(scope, new WqlEventQuery("__InstanceCreationEvent", withinInterval, wql_condition));
            CreationEvent.EventArrived += (s, e) =>
            {
                if (!serialPort.IsOpen) serialPort.Open();
                Trace.TraceInformation("Instance Creation Event SerialPort Name:{0}", serialPort.PortName);
            };
            ManagementEventWatcher DeletionEvent = new ManagementEventWatcher(scope, new WqlEventQuery("__InstanceDeletionEvent", withinInterval, wql_condition));
            DeletionEvent.EventArrived += (s, e) =>
            {
                if (serialPort.IsOpen) serialPort.Close();
                Trace.TraceError("Instance Deletion Event SerialPort Name:{0}", serialPort.PortName);
            };

            CreationEvent.Start();
            DeletionEvent.Start();
        }
#endif


    }
}
