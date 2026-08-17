using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using SpaceCG.Generic;

namespace SpaceCG.IO
{
    /// <summary>
    /// 表示 Modbus 协议层的异常响应（从站返回的功能码高位置 1 的异常帧）。
    /// </summary>
    /// <remarks>
    /// <para>当从站无法处理主站请求时，会将响应帧的功能码最高位（bit7）置 1，
    /// 并在其后附一个字节的异常码（Exception Code）说明失败原因。</para>
    /// </remarks>
    public sealed class ModbusException : Exception
    {
        /// <summary> 从站地址（8 位）。 </summary>
        public byte SlaveAddress { get; }

        /// <summary> 触发异常的功能码（未置位的原始功能码）。 </summary>
        public byte FunctionCode { get; }

        /// <summary> 异常码（Exception Code），用于指示具体的错误原因。 </summary>
        public byte ExceptionCode { get; }

        /// <summary>
        /// 构造 Modbus 异常实例。
        /// </summary>
        /// <param name="slaveAddress">从站地址。</param>
        /// <param name="functionCode">原始功能码。</param>
        /// <param name="exceptionCode">异常码。</param>
        /// <param name="message">异常描述信息。</param>
        public ModbusException(byte slaveAddress, byte functionCode, byte exceptionCode, string message) : base(message)
        {
            SlaveAddress = slaveAddress;
            FunctionCode = functionCode;
            ExceptionCode = exceptionCode;
        }
    }

    /// <summary>
    /// Modbus RTU 主站客户端，提供基于功能码 1/2/3/4/5/6/15/16/23 的读写操作。
    /// <para>Modbus RTU Data Tables and Formats: https://simplymodbus.ca/learn-rtu.html</para>
    /// </summary>
    /// <remarks>
    /// <para><b>线程安全级别</b>：线程安全。底层 <see cref="RequestResponseSession"/> 采用
    /// 单后台线程 + 严格串行 FIFO 队列，多个调用方并发提交的请求会被顺序执行。</para>
    /// </remarks>
    public class ModbusRtu : IDisposable
	{
        private readonly RequestResponseSession _session;

        /// <summary>
        /// 构造 Modbus RTU 主站客户端。
        /// </summary>
        /// <param name="channelType">底层传输通道类型（串口 / TCP / UDP）。</param>
        /// <param name="arguments">连接参数字符串（由对应传输实现类约定格式）。</param>
        public ModbusRtu(ChannelType channelType, string arguments)
		{
            _session = new RequestResponseSession(channelType, arguments);
        }

        #region BuildRequestFrame & Response Result
        /// <summary>
        /// 构建读操作请求帧（功能码 1/2/3/4 共用）。
        /// </summary>
        /// <param name="slaveAddress">从站地址（8 位）。</param>
        /// <param name="functionCode">功能码。</param>
        /// <param name="startAddress">起始地址（16 位）。</param>
        /// <param name="numberOfPoints">读取数量（16 位）。</param>
        /// <returns>8 字节请求帧（含末尾 2 字节 CRC）。</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static byte[] BuildReadRequestMessage(byte slaveAddress, byte functionCode, ushort startAddress, ushort numberOfPoints)
        {
            var request = new byte[8];
            request[0] = slaveAddress;
            request[1] = functionCode;
            request[2] = (byte)(startAddress >> 8);
            request[3] = (byte)startAddress;
            request[4] = (byte)(numberOfPoints >> 8);
            request[5] = (byte)numberOfPoints;
            AppendCrc16(request);

            return request;
        }
        /// <summary>
        /// 将 CRC16 追加到消息末尾 2 字节（低字节在前，符合 Modbus RTU 约定）。
        /// </summary>
        /// <param name="message">待追加 CRC 的消息帧（最后 2 字节为 CRC 占位）。</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void AppendCrc16(byte[] message)
        {
            var crc16 = CRCCheckHelper.ComputeCRC16_MODBUS(message, 0, message.Length - 2);
            message[message.Length - 2] = (byte)(crc16 & 0xFF);
            message[message.Length - 1] = (byte)((crc16 >> 8) & 0xFF);
        }
        /// <summary>
        /// 从响应帧数据区解包位状态（每字节 8 个线圈，LSB 在前）。
        /// 响应帧：[0]从站地址 [1]功能码 [2]字节数N [3..3+N-1]数据 [..CRC]
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool[] UnpackBits(byte[] response, int numberOfPoints)
        {
            var result = new bool[numberOfPoints];
            for (int i = 0; i < numberOfPoints; i++)
            {
                var value = response[3 + (i >> 3)];      // 所在字节
                var mask = (byte)(1 << (i & 0x07));      // 字节内的位掩码
                result[i] = (value & mask) != 0;
            }
            return result;
        }
        /// <summary>
        /// 从响应帧数据区解包寄存器值（每寄存器 2 字节，大端）。
        /// 响应：[0]从站 [1]功能码 [2]字节数N [3..]寄存器值（大端，每寄存器 2 字节）
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ushort[] UnpackRegisters(byte[] response, int numberOfPoints)
        {
            var result = new ushort[numberOfPoints];
            for (int i = 0; i < numberOfPoints; i++)
            {
                result[i] = (ushort)((response[3 + i * 2] << 8) | response[4 + i * 2]);
            }
            return result;
        }
        #endregion

        #region ResponseFramePredicate
        /// <summary>
        /// 读操作（功能码 1/2/3/4/23）响应的完整性判定器（含可变长度数据区）。
        /// 判断依据：响应帧头部 3 字节已到达，且数据区字节数满足「字节数 N」字段声明，再加上 2 字节 CRC 后即为完整响应。
        /// </summary>
        /// <param name="buffer">当前接收缓冲区（仅含有效数据）。</param>
        /// <returns>完整响应长度；数据不足则返回 -1。</returns>
        private static int ReadDataFramePredicate(ArraySegment<byte> buffer)
        {
            // 至少需要：从站地址(1) + 功能码(1) + 字节数N(1) + CRC(2)
            if (buffer.Count < 5) return -1;

            var data = buffer.Array;
            var offset = buffer.Offset;

            // 异常响应固定 5 字节：[0]从站 [1]功能码|0x80 [2]异常码 [3:4]CRC
            if ((data[offset + 1] & 0x80) != 0) return 5;

            // 正常响应：[0]从站 [1]功能码 [2]字节数N [3..3+N-1]数据 [..+2]CRC
            var byteCount = data[offset + 2];
            var totalLength = 3 + byteCount + 2;

            return buffer.Count >= totalLength ? totalLength : -1;
        }
        /// <summary>
        /// 写操作（功能码 5/6/15/16）响应的完整性判定器。
        /// 这些功能码的响应为固定 8 字节回显（从站地址 + 功能码 + 4 字节数据 + CRC2）。
        /// </summary>
        /// <param name="buffer">当前接收缓冲区（仅含有效数据）。</param>
        /// <returns>完整响应长度（固定 8）；数据不足则返回 -1。</returns>
        private static int WriteResponseFramePredicate(ArraySegment<byte> buffer)
        {
            // 异常响应固定 5 字节：[0]从站 [1]功能码|0x80 [2]异常码 [3:4]CRC
            if (buffer.Count >= 5)
            {
                var data = buffer.Array;
                if ((data[buffer.Offset + 1] & 0x80) != 0) return 5;
            }

            // 正常写响应固定 8 字节
            return buffer.Count >= 8 ? 8 : -1;
        }

        private static int ReadBitsResponseFramePredicate(ArraySegment<byte> buffer, int numberOfPoints)
        {
            if (buffer.Count < 5) return -1;

            var data = buffer.Array;
            var offset = buffer.Offset;

            // 异常响应
            if ((data[offset + 1] & 0x80) != 0) return 5;

            int expectedByteCount = (numberOfPoints + 7) >> 3;
            int byteCount = data[offset + 2];

            if (byteCount != expectedByteCount)
                throw new InvalidDataException($"Modbus响应字节数错误：期望 {expectedByteCount}，实际 {byteCount}");

            int totalLength = 3 + byteCount + 2;

            return buffer.Count >= totalLength ? totalLength : -1;
        }
        private static int ReadRegistersResponseFramePredicate(ArraySegment<byte> buffer, int numberOfPoints)
        {
            if (buffer.Count < 5) return -1;

            var data = buffer.Array;
            var offset = buffer.Offset;

            if ((data[offset + 1] & 0x80) != 0) return 5;

            int expectedByteCount = numberOfPoints * 2;
            int byteCount = data[offset + 2];

            if (byteCount != expectedByteCount)
            {
                throw new InvalidDataException($"Modbus响应字节数错误：期望 {expectedByteCount}，实际 {byteCount}");
            }

            int totalLength = 3 + byteCount + 2;

            return buffer.Count >= totalLength ? totalLength : -1;
        }
        #endregion

        #region 验证响应数据帧
        /// <summary>
        /// 校验一条完整的 Modbus RTU 响应帧：帧长度、CRC16 校验和、从站地址与功能码，
        /// 并识别异常响应帧（功能码 bit7 置位）。
        /// </summary>
        /// <param name="response">完整的响应帧（含末尾 2 字节 CRC）。</param>
        /// <param name="slaveAddress">期望的从站地址。</param>
        /// <param name="functionCode">期望的功能码。</param>
        /// <exception cref="InvalidOperationException">帧长度不足时抛出。</exception>
        /// <exception cref="InvalidDataException">CRC16 校验失败、从站地址不匹配或功能码不匹配时抛出。</exception>
        /// <exception cref="ModbusException">从站返回异常响应帧（功能码 bit7 置位）时抛出。</exception>
        private static void ValidateResponseFrame(byte[] response, byte slaveAddress, byte functionCode)
        {
            // 1. 帧长度校验：从站地址(1) + 功能码(1) + 异常码(1) + CRC(2) 至少 5 字节。
            if (response == null || response.Length < 5)
                throw new InvalidOperationException($"响应帧长度不足（{response?.Length ?? 0} 字节），至少需要 5 字节");

            // 2. CRC16 校验：对除末尾 2 字节外的全部字节计算 Modbus CRC16， 并与帧末尾 2 字节（低字节在前）比对。
            var expectedCrc = CRCCheckHelper.ComputeCRC16_MODBUS(response, 0, response.Length - 2);
            var actualCrc = (ushort)(response[response.Length - 2] | (response[response.Length - 1] << 8));
            if (expectedCrc != actualCrc)
                throw new InvalidDataException($"CRC16 校验失败：期望 0x{expectedCrc:X4}，实际 0x{actualCrc:X4}");

            // 3. 从站地址校验。
            if (response[0] != slaveAddress)
                throw new InvalidDataException($"响应从站地址不匹配：期望 {slaveAddress}，实际 {response[0]}");

            // 4. 功能码低 7 位校验：允许 bit7 置位以兼容异常响应，因此仅比较低 7 位。
            if ((response[1] & 0x7F) != functionCode)
                throw new InvalidDataException($"响应功能码不匹配：期望 {functionCode}，实际 {response[1] & 0x7F}");

            // 5. 异常帧识别：bit7 置位说明从站返回异常响应，直接抛出 ModbusException。
            if ((response[1] & 0x80) != 0)
                throw new ModbusException(response[0], functionCode, response[2], $"从站 {slaveAddress} 返回异常码 0x{response[2]:X2}");
        }
        #endregion

        #region Read Registers（功能码 1/2/3/4）
        /// <summary>
        /// 读取从站的离散输出（DO）线圈状态（功能码 0x01）。
        /// </summary>
        /// <param name="slaveAddress">从站地址（8 位）。</param>
        /// <param name="startAddress">起始线圈地址（16 位）。</param>
        /// <param name="numberOfPoints">要读取的线圈数量（16 位），取值范围 [1, 2000]。</param>
        /// <param name="cancellationToken">取消操作的通知。</param>
        /// <returns>长度等于 <paramref name="numberOfPoints"/> 的 <see cref="bool"/> 数组。</returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="numberOfPoints"/> 不在 [1, 2000] 范围内时抛出。</exception>
        /// <exception cref="ModbusException">从站返回异常响应时抛出。</exception>
        /// <exception cref="TimeoutException">等待响应超时时抛出。</exception>
        public async Task<bool[]> ReadCoilsAsync(byte slaveAddress, ushort startAddress, ushort numberOfPoints, CancellationToken cancellationToken = default)
		{
            if (slaveAddress < 1 || slaveAddress > 247)
                throw new ArgumentOutOfRangeException(nameof(slaveAddress), "Modbus RTU 从站地址必须为 1-247。");
            if (numberOfPoints < 1 || numberOfPoints > 2000)
                throw new ArgumentOutOfRangeException(nameof(numberOfPoints), "读取线圈数量必须在 1-2000 之间");

            var request = BuildReadRequestMessage(slaveAddress, 0x01, startAddress, numberOfPoints);
			var response = await _session.TransceiveAsync(request, 0, request.Length, ReadDataFramePredicate, cancellationToken).ConfigureAwait(false);
            ValidateResponseFrame(response, slaveAddress, 0x01);

			return UnpackBits(response, numberOfPoints);
		}
        /// <summary>
        /// 读取从站的离散输入（DI）状态（功能码 0x02）。
        /// </summary>
        /// <param name="slaveAddress">从站地址（8 位）。</param>
        /// <param name="startAddress">起始输入地址（16 位）。</param>
        /// <param name="numberOfPoints">要读取的输入数量（16 位），取值范围 [1, 2000]。</param>
        /// <param name="cancellationToken">取消操作的通知。</param>
        /// <returns>长度等于 <paramref name="numberOfPoints"/> 的 <see cref="bool"/> 数组。</returns>
        public async Task<bool[]> ReadInputsAsync(byte slaveAddress, ushort startAddress, ushort numberOfPoints, CancellationToken cancellationToken = default)
        {
            if (slaveAddress < 1 || slaveAddress > 247)
                throw new ArgumentOutOfRangeException(nameof(slaveAddress), "Modbus RTU 从站地址必须为 1-247。");
            if (numberOfPoints < 1 || numberOfPoints > 2000)
                throw new ArgumentOutOfRangeException(nameof(numberOfPoints), "读取输入数量必须在 1-2000 之间");

            var request = BuildReadRequestMessage(slaveAddress, 0x02, startAddress, numberOfPoints);
            var response = await _session.TransceiveAsync(request, 0, request.Length, ReadDataFramePredicate, cancellationToken).ConfigureAwait(false);
            ValidateResponseFrame(response, slaveAddress, 0x02);

            return UnpackBits(response, numberOfPoints);
        }
        /// <summary>
        /// 读取从站的保持寄存器（AO）值（功能码 0x03）。
        /// </summary>
        /// <param name="slaveAddress">从站地址（8 位）。</param>
        /// <param name="startAddress">起始寄存器地址（16 位）。</param>
        /// <param name="numberOfPoints">要读取的寄存器数量（16 位），取值范围 [1, 125]。</param>
        /// <param name="cancellationToken">取消操作的通知。</param>
        /// <returns>长度等于 <paramref name="numberOfPoints"/> 的 <see cref="ushort"/> 数组。</returns>
        public async Task<ushort[]> ReadHoldingRegistersAsync(byte slaveAddress, ushort startAddress, ushort numberOfPoints, CancellationToken cancellationToken = default)
        {
            return await ReadRegistersCoreAsync(slaveAddress, 0x03, startAddress, numberOfPoints, cancellationToken).ConfigureAwait(false);
        }
        /// <summary>
        /// 读取从站的输入寄存器（AI）值（功能码 0x04）。
        /// </summary>
        public async Task<ushort[]> ReadInputRegistersAsync(byte slaveAddress, ushort startAddress, ushort numberOfPoints, CancellationToken cancellationToken = default)
        {
            return await ReadRegistersCoreAsync(slaveAddress, 0x04, startAddress, numberOfPoints, cancellationToken).ConfigureAwait(false);
        }
        /// <summary>
        /// 读取寄存器的核心实现（功能码 3/4 共用）。
        /// </summary>
        private async Task<ushort[]> ReadRegistersCoreAsync(byte slaveAddress, byte functionCode, ushort startAddress, ushort numberOfPoints, CancellationToken cancellationToken)
        {
            if (slaveAddress < 1 || slaveAddress > 247)
                throw new ArgumentOutOfRangeException(nameof(slaveAddress), "Modbus RTU 从站地址必须为 1-247。");
            if (numberOfPoints < 1 || numberOfPoints > 125)
                throw new ArgumentOutOfRangeException(nameof(numberOfPoints), "读取寄存器数量必须在 1-125 之间");

            var request = BuildReadRequestMessage(slaveAddress, functionCode, startAddress, numberOfPoints);
            var response = await _session.TransceiveAsync(request, 0, request.Length, ReadDataFramePredicate, cancellationToken).ConfigureAwait(false);
            ValidateResponseFrame(response, slaveAddress, functionCode);

            return UnpackRegisters(response, numberOfPoints);
        }
        #endregion

        #region Write Registers （功能码 5/6/0F/10）(广播 slaveAddress == 0 05/06/0F/10 无返回响应)
        /// <summary>
        /// 写入值到从站单个离散输出线圈（功能码 0x05）。
        /// </summary>
        /// <param name="slaveAddress">从站地址（8 位）。</param>
        /// <param name="coilAddress">线圈地址（16 位）。</param>
        /// <param name="value">写入值（true=ON，false=OFF）。</param>
        /// <param name="cancellationToken">取消操作的通知。</param>
        public async Task WriteSingleCoilAsync(byte slaveAddress, ushort coilAddress, bool value, CancellationToken cancellationToken = default)
        {
            //if (slaveAddress < 1 || slaveAddress > 247)
            //    throw new ArgumentOutOfRangeException(nameof(slaveAddress), "Modbus RTU 从站地址必须为 1-247。");

            // 功能码 5 请求：从站(1) + 0x05(1) + 线圈地址(2) + 写入值(2: 0xFF00=ON, 0x0000=OFF) + CRC(2)
            var request = new byte[8];
            request[0] = slaveAddress;
            request[1] = 0x05;
            request[2] = (byte)(coilAddress >> 8);
            request[3] = (byte)coilAddress;
            // 功能码 5 的写入值：ON = 0xFF00，OFF = 0x0000（高字节在前）
            request[4] = value ? (byte)0xFF : (byte)0x00;
            request[5] = 0x00;

            AppendCrc16(request);

            var response = await _session.TransceiveAsync(request, 0, request.Length, WriteResponseFramePredicate, cancellationToken).ConfigureAwait(false);
            ValidateResponseFrame(response, slaveAddress, 0x05);
        }
        /// <summary>
        /// 写入值到从站单个保持寄存器（功能码 0x06）。
        /// </summary>
        /// <param name="slaveAddress">从站地址（8 位）。</param>
        /// <param name="registerAddress">寄存器地址（16 位）。</param>
        /// <param name="value">写入值（16 位）。</param>
        /// <param name="cancellationToken">取消操作的通知。</param>
        public async Task WriteSingleRegisterAsync(byte slaveAddress, ushort registerAddress, ushort value, CancellationToken cancellationToken = default)
        {
            // 功能码 6 请求：从站(1) + 0x06(1) + 寄存器地址(2) + 写入值(2) + CRC(2)
            var request = new byte[8];
            request[0] = slaveAddress;
            request[1] = 0x06;
            request[2] = (byte)(registerAddress >> 8);
            request[3] = (byte)registerAddress;
            request[4] = (byte)(value >> 8);
            request[5] = (byte)value;

            AppendCrc16(request);

            var response = await _session.TransceiveAsync(request, 0, request.Length, WriteResponseFramePredicate, cancellationToken).ConfigureAwait(false);
            ValidateResponseFrame(response, slaveAddress, 0x06);
        }
        /// <summary>
        /// 写入多个线圈（功能码 0x0F）。
        /// </summary>
        /// <param name="slaveAddress">从站地址（8 位）。</param>
        /// <param name="startAddress">起始线圈地址（16 位）。</param>
        /// <param name="values">写入值（布尔数组）。</param>
        /// <param name="cancellationToken">取消操作的通知。</param>
        public async Task WriteMultipleCoilsAsync(byte slaveAddress, ushort startAddress, bool[] values, CancellationToken cancellationToken = default)
        {
            if (values == null || values.Length == 0)
                throw new ArgumentNullException(nameof(values), "线圈值数组不能为空");
            if (values.Length > 1968)
                throw new ArgumentOutOfRangeException(nameof(values), "写入线圈数量不能超过 1968");

            // 将 bool[] 打包为位数组（每字节 8 个线圈，LSB 在前）
            var byteCount = (values.Length + 7) / 8;
            var packed = new byte[byteCount];
            for (int i = 0; i < values.Length; i++)
            {
                if (values[i])
                    packed[i >> 3] |= (byte)(1 << (i & 0x07));
            }

            // 功能码 15 请求：从站(1) + 0x0F(1) + 起始地址(2) + 数量(2) + 字节数(1) + 数据(N) + CRC(2)
            var request = new byte[9 + byteCount];
            request[0] = slaveAddress;
            request[1] = 0x0F;
            request[2] = (byte)(startAddress >> 8);
            request[3] = (byte)startAddress;
            request[4] = (byte)(values.Length >> 8);
            request[5] = (byte)values.Length;
            request[6] = (byte)byteCount;

            Array.Copy(packed, 0, request, 7, byteCount);
            AppendCrc16(request);

            var response = await _session.TransceiveAsync(request, 0, request.Length, WriteResponseFramePredicate, cancellationToken).ConfigureAwait(false);

            ValidateResponseFrame(response, slaveAddress, 0x0F);
        }
        /// <summary>
        /// 写入多个保持寄存器（功能码 0x10）。
        /// </summary>
        /// <param name="slaveAddress">从站地址（8 位）。</param>
        /// <param name="startAddress">起始寄存器地址（16 位）。</param>
        /// <param name="values">写入值（16 位整型数组）。</param>
        /// <param name="cancellationToken">取消操作的通知。</param>
        public async Task WriteMultipleRegistersAsync(byte slaveAddress, ushort startAddress, ushort[] values, CancellationToken cancellationToken = default)
        {
            if (values == null || values.Length == 0)
                throw new ArgumentNullException(nameof(values), "寄存器值数组不能为空");
            if (values.Length > 123)
                throw new ArgumentOutOfRangeException(nameof(values), "写入寄存器数量不能超过 123");

            var byteCount = values.Length * 2;
            // 功能码 16 请求：从站(1) + 0x10(1) + 起始地址(2) + 数量(2) + 字节数(1) + 数据(2N) + CRC(2)
            var request = new byte[9 + byteCount];
            request[0] = slaveAddress;
            request[1] = 0x10;
            request[2] = (byte)(startAddress >> 8);
            request[3] = (byte)startAddress;
            request[4] = (byte)(values.Length >> 8);
            request[5] = (byte)values.Length;
            request[6] = (byte)byteCount;
            for (int i = 0; i < values.Length; i++)
            {
                request[7 + i * 2] = (byte)(values[i] >> 8);
                request[8 + i * 2] = (byte)values[i];
            }

            AppendCrc16(request);

            var response = await _session.TransceiveAsync(request, 0, request.Length, WriteResponseFramePredicate, cancellationToken).ConfigureAwait(false);
            ValidateResponseFrame(response, slaveAddress, 0x10);
        }
        #endregion

        #region 读写操作（功能码 23）
        /// <summary>
        /// 读写多个保持寄存器（功能码 0x17）。
        /// </summary>
        /// <param name="slaveAddress">从站地址（8 位）。</param>
        /// <param name="startReadAddress">读起始地址（16 位）。</param>
        /// <param name="numberOfPointsToRead">读取数量（16 位），取值范围 [1, 125]。</param>
        /// <param name="startWriteAddress">写入起始地址（16 位）。</param>
        /// <param name="writeData">写入值（16 位整型数组）。</param>
        /// <param name="cancellationToken">取消操作的通知。</param>
        /// <returns>读回的寄存器值数组。</returns>
        public async Task<ushort[]> ReadWriteMultipleRegistersAsync(byte slaveAddress, ushort startReadAddress, ushort numberOfPointsToRead, ushort startWriteAddress, ushort[] writeData, CancellationToken cancellationToken = default)
        {
            if (numberOfPointsToRead < 1 || numberOfPointsToRead > 125)
                throw new ArgumentOutOfRangeException(nameof(numberOfPointsToRead), "读取寄存器数量必须在 1-125 之间");
            if (writeData == null || writeData.Length == 0)
                throw new ArgumentNullException(nameof(writeData), "写入寄存器数组不能为空");
            if (writeData.Length > 121)
                throw new ArgumentOutOfRangeException(nameof(writeData), "写入寄存器数量不能超过 121");

            var writeByteCount = writeData.Length * 2;

            // 功能码 23 请求：从站(1) + 0x17(1) + 读起始地址(2) + 读数量(2) + 写起始地址(2) + 写数量(2) + 字节数(1) + 写数据(2N) + CRC(2)
            var request = new byte[13 + writeByteCount];
            request[0] = slaveAddress;
            request[1] = 0x17;
            request[2] = (byte)(startReadAddress >> 8);
            request[3] = (byte)startReadAddress;
            request[4] = (byte)(numberOfPointsToRead >> 8);
            request[5] = (byte)numberOfPointsToRead;
            request[6] = (byte)(startWriteAddress >> 8);
            request[7] = (byte)startWriteAddress;
            request[8] = (byte)(writeData.Length >> 8);
            request[9] = (byte)writeData.Length;
            request[10] = (byte)writeByteCount;
            for (int i = 0; i < writeData.Length; i++)
            {
                request[11 + i * 2] = (byte)(writeData[i] >> 8);
                request[12 + i * 2] = (byte)writeData[i];
            }

            AppendCrc16(request);

            var response = await _session.TransceiveAsync(request, 0, request.Length, ReadDataFramePredicate, cancellationToken).ConfigureAwait(false);
            ValidateResponseFrame(response, slaveAddress, 0x17);

            return UnpackRegisters(response, numberOfPointsToRead);
        }
        #endregion

        /// <inheritdoc /> 
        public void Dispose() => _session.Dispose();
        
    }
}
