/***
 * 
 * 
 * 
 * 
 * 
 **/

namespace SpaceCG.WindowsAPI.Kernel32
{
    using System;
    using System.Runtime.InteropServices;
    using System.Text;


    #region Enumerations
    /// <summary>
    /// <see cref="Kernel32.FormatMessage(FormatMessageFlags, IntPtr, uint, uint, ref string, uint, IntPtr)"/> 函数参数 dwFlags 一个或多个值。
    /// <para>参考：https://docs.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-formatmessage </para>
    /// </summary>
    [Flags]
    public enum FormatMessageFlags
    {
        /// <summary>
        /// 没有输出线宽限制。该函数将消息定义文本中的换行符存储到输出缓冲区中。
        /// </summary>
        FORMAT_MESSAGE_NONE = 0x00000000,
        /// <summary>
        /// 该函数分配一个足以容纳格式化消息的缓冲区，并在 lpBuffer 指定的地址处放置一个指向已分配缓冲区的指针 的 lpBuffer 参数是一个指向 LPTSTR ; 您必须将指针强制转换为 LPTSTR。所述 n 大小 参数指定的最小数目 TCHARS 分配用于输出消息缓冲器。 
        /// <para>当不再需要缓冲区时，调用者应使用 <see cref="Kernel32.LocalFree"/> 函数释放缓冲区。如果格式化消息的长度超过 128K 字节，则 <see cref="Kernel32.FormatMessage"/> 将失败，随后对 <see cref="Kernel32.GetLastError"/> 的调用 将返回 ERROR_MORE_DATA。</para>
        /// </summary>
        FORMAT_MESSAGE_ALLOCATE_BUFFER = 0x00000100,
        /// <summary>
        /// 消息定义中的插入序列将被忽略，并原样传递到输出缓冲区。该标志对于获取消息以供以后格式化很有用。如果设置了此标志，则 Arguments 参数将被忽略。
        /// </summary>
        FORMAT_MESSAGE_IGNORE_INSERTS = 0x00000200,
        /// <summary>
        /// 所述 lpSource 参数是一个指向包含一个消息定义一个空终止字符串。消息定义可以包含插入序列，就像消息表资源中的消息文本可能一样。该标志不能与 <see cref="FORMAT_MESSAGE_FROM_HMODULE"/> 或 <see cref="FORMAT_MESSAGE_FROM_SYSTEM"/> 一起使用 。
        /// </summary>
        FORMAT_MESSAGE_FROM_STRING = 0x00000400,
        /// <summary>
        /// 所述 lpSource 参数是包含该消息表资源（多个），以搜寻模块句柄。如果此 lpSource 句柄为 NULL，则将搜索当前进程的应用程序映像文件。该标志不能与 <see cref="FORMAT_MESSAGE_FROM_STRING"/> 一起使用 。
        /// <para>如果模块没有消息表资源，则该函数将失败，并显示 <see cref="ERROR_RESOURCE_TYPE_NOT_FOUND"/>。</para>
        /// </summary>
        FORMAT_MESSAGE_FROM_HMODULE = 0x00000800,
        /// <summary>
        /// 该功能应在系统消息表资源中搜索所请求的消息。如果使用 <see cref="FORMAT_MESSAGE_FROM_HMODULE"/> 指定了此标志，则如果在 lpSource 指定的模块中找不到消息，该函数将搜索系统消息表。该标志不能与 <see cref="FORMAT_MESSAGE_FROM_STRING"/> 一起使用。
        /// <para>如果指定了此标志，则应用程序可以传递 <see cref="Kernel32.GetLastError"/> 函数的结果 以检索系统定义的错误的消息文本。</para>
        /// </summary>
        FORMAT_MESSAGE_FROM_SYSTEM = 0x00001000,
        /// <summary>
        /// 的参数参数不是 va_list 的 结构，但它是一个指针，它指向表示参数值的数组。
        /// <para>该标志不能与 64 位整数值一起使用。如果使用的是64位整数，则必须使用 va_list 结构。</para>
        /// </summary>
        FORMAT_MESSAGE_ARGUMENT_ARRAY = 0x00002000,
        /// <summary>
        /// 该函数将忽略消息定义文本中的常规换行符。该函数将消息定义文本中的硬编码换行符存储到输出缓冲区中。该函数不会产生新的换行符。
        /// </summary>
        FORMAT_MESSAGE_MAX_WIDTH_MASK = 0x000000FF,
    }
    #endregion


    #region Structures
    #endregion


    #region Delegates
    #endregion


    #region Notifications
    #endregion


    /// <summary>
    /// 
    /// </summary>
    public static partial class Kernel32
    {
        /// <summary>
        /// 格式化消息字符串。该功能需要消息定义作为输入。消息定义可以来自传递给函数的缓冲区。它可以来自已加载模块中的消息表资源。或者调用者可以要求函数在系统的消息表资源中搜索消息定义。
        /// <para>该函数根据消息标识符和语言标识符在消息表资源中找到消息定义。该函数将格式化的消息文本复制到输出缓冲区，如果需要，则处理所有嵌入的插入序列。</para>
        /// <para>参考：https://docs.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-formatmessage </para>
        /// </summary>
        /// <param name="dwFlags">格式设置选项以及如何解释lpSource参数。dwFlags的低位字节指定函数如何处理输出缓冲区中的换行符。低位字节还可以指定格式化输出行的最大宽度。
        ///     <para>如果低位字节是 <see cref="FormatMessageFlags.FORMAT_MESSAGE_MAX_WIDTH_MASK"/> 以外的非零值 ，则它指定输出行中的最大字符数。该函数将忽略消息定义文本中的常规换行符。该函数从不在换行符之间分割由空格分隔的字符串。该函数将消息定义文本中的硬编码换行符存储到输出缓冲区中。硬编码的换行符使用 ％n 转义序列编码。</para>
        /// </param>
        /// <param name="lpSource">消息定义的位置。此参数的类型取决于 dwFlags 参数中的设置。如果在 dwFlags 中均未设置以下这些标志，则将忽略 lpSource
        ///     <para><see cref="FormatMessageFlags.FORMAT_MESSAGE_FROM_HMODULE"/>  0x00000800  包含要搜索的消息表的模块的句柄。</para>
        ///     <para><see cref="FormatMessageFlags.FORMAT_MESSAGE_FROM_STRING"/>  0x00000400  指向由无格式消息文本组成的字符串的指针。将对其进行扫描以查找插入内容并进行相应的格式化。</para>
        /// </param>
        /// <param name="dwMessageId">所请求消息的消息标识符。如果 dwFlags 包含 <see cref="FormatMessageFlags.FORMAT_MESSAGE_FROM_STRING"/>，则忽略此参数 。</param>
        /// <param name="dwLanguageId">请求的消息的语言标识符。如果 dwFlags 包含 <see cref="FormatMessageFlags.FORMAT_MESSAGE_FROM_STRING"/>，则忽略此参数。(0 表示自动选择)</param>
        /// <param name="lpBuffer">[LPTSTR] 指向缓冲区的指针，该缓冲区接收以空终止的字符串，该字符串指定格式化的消息。该缓冲区不能大于 64K 字节。
        ///     <para></para>如果 dwFlags 包含 <see cref="FormatMessageFlags.FORMAT_MESSAGE_ALLOCATE_BUFFER"/>，则该函数使用 <see cref="LocalAlloc"/> 函数分配缓冲区，并将指向缓冲区的指针放在 lpBuffer 中指定的地址处。(否则自已分配空间。)</param>
        /// <param name="nSize">如果未设置 <see cref="FormatMessageFlags.FORMAT_MESSAGE_ALLOCATE_BUFFER"/> 标志，则此参数以 TCHARs 指定输出缓冲区的大小。如果 设置了 <see cref="FormatMessageFlags.FORMAT_MESSAGE_ALLOCATE_BUFFER"/>，则此参数指定分配给输出缓冲区的最小 TCHAR 数 。
        ///     <para>输出缓冲区不能大于64K字节。</para>
        /// </param>
        /// <param name="Arguments">值数组，用作格式化消息中的插入值。格式字符串中的 %1 表示 Arguments数组中的第一个值；%2表示第二个参数；等等。
        ///     <para>每个值的解释取决于与消息定义中的插入关联的格式信息。默认值是将每个值都视为指向以空字符结尾的字符串的指针。</para>
        ///     <para>默认情况下，Arguments 参数的类型为 va_list*，这是一种语言和实现特定的数据类型，用于描述可变数量的参数。从函数返回时，va_list 参数的状态未定义。要再次使用 va_list，请使用 va_end 销毁变量参数列表指针，然后使用 va_start 对其进行初始化 。</para>
        ///     <para>如果您没有类型为 va_list* 的指针 ，则指定 <see cref="FormatMessageFlags.FORMAT_MESSAGE_ARGUMENT_ARRAY"/> 标志并将指针传递给 DWORD_PTR 值数组；这些值被输入到格式化为插入值的消息中。每个插入在数组中必须有一个对应的元素。</para>
        /// </param>
        /// <returns>如果函数成功，则返回值是存储在输出缓冲区中的 TCHAR 数量，不包括终止的空字符。
        ///     <para>如果函数失败，则返回值为零。要获取扩展的错误信息，请调用 <see cref="Marshal.GetLastWin32Error"/> 或 <see cref="Marshal.GetHRForLastWin32Error"/>。</para>
        /// </returns>
        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public extern static uint FormatMessage(FormatMessageFlags dwFlags, IntPtr lpSource, uint dwMessageId, uint dwLanguageId, [MarshalAs(UnmanagedType.LPTStr)]ref string lpBuffer, uint nSize, IntPtr Arguments);

        /// <summary>
        /// 格式化消息字符串。该功能需要消息定义作为输入。消息定义可以来自传递给函数的缓冲区。它可以来自已加载模块中的消息表资源。或者调用者可以要求函数在系统的消息表资源中搜索消息定义。
        /// <para>该函数根据消息标识符和语言标识符在消息表资源中找到消息定义。该函数将格式化的消息文本复制到输出缓冲区，如果需要，则处理所有嵌入的插入序列。</para>
        /// <para>参考：https://docs.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-formatmessage </para>
        /// </summary>
        /// <param name="dwFlags">格式设置选项以及如何解释lpSource参数。dwFlags的低位字节指定函数如何处理输出缓冲区中的换行符。低位字节还可以指定格式化输出行的最大宽度。
        ///     <para>如果低位字节是 <see cref="FormatMessageFlags.FORMAT_MESSAGE_MAX_WIDTH_MASK"/> 以外的非零值 ，则它指定输出行中的最大字符数。该函数将忽略消息定义文本中的常规换行符。该函数从不在换行符之间分割由空格分隔的字符串。该函数将消息定义文本中的硬编码换行符存储到输出缓冲区中。硬编码的换行符使用 ％n 转义序列编码。</para>
        /// </param>
        /// <param name="lpSource">消息定义的位置。此参数的类型取决于 dwFlags 参数中的设置。如果在 dwFlags 中均未设置以下这些标志，则将忽略 lpSource
        ///     <para><see cref="FormatMessageFlags.FORMAT_MESSAGE_FROM_HMODULE"/>  0x00000800  包含要搜索的消息表的模块的句柄。</para>
        ///     <para><see cref="FormatMessageFlags.FORMAT_MESSAGE_FROM_STRING"/>  0x00000400  指向由无格式消息文本组成的字符串的指针。将对其进行扫描以查找插入内容并进行相应的格式化。</para>
        /// </param>
        /// <param name="dwMessageId">所请求消息的消息标识符。如果 dwFlags 包含 <see cref="FormatMessageFlags.FORMAT_MESSAGE_FROM_STRING"/>，则忽略此参数 。</param>
        /// <param name="dwLanguageId">请求的消息的语言标识符。如果 dwFlags 包含 <see cref="FormatMessageFlags.FORMAT_MESSAGE_FROM_STRING"/>，则忽略此参数。(0 表示自动选择)</param>
        /// <param name="lpBuffer">[LPTSTR] 指向缓冲区的指针，该缓冲区接收以空终止的字符串，该字符串指定格式化的消息。该缓冲区不能大于 64K 字节。
        ///     <para></para>如果 dwFlags 包含 <see cref="FormatMessageFlags.FORMAT_MESSAGE_ALLOCATE_BUFFER"/>，则该函数使用 <see cref="LocalAlloc"/> 函数分配缓冲区，并将指向缓冲区的指针放在 lpBuffer 中指定的地址处。(否则自已分配空间。)</param>
        /// <param name="nSize">如果未设置 <see cref="FormatMessageFlags.FORMAT_MESSAGE_ALLOCATE_BUFFER"/> 标志，则此参数以 TCHARs 指定输出缓冲区的大小。如果 设置了 <see cref="FormatMessageFlags.FORMAT_MESSAGE_ALLOCATE_BUFFER"/>，则此参数指定分配给输出缓冲区的最小 TCHAR 数 。
        ///     <para>输出缓冲区不能大于64K字节。</para>
        /// </param>
        /// <param name="Arguments">值数组，用作格式化消息中的插入值。格式字符串中的 %1 表示 Arguments数组中的第一个值；%2表示第二个参数；等等。
        ///     <para>每个值的解释取决于与消息定义中的插入关联的格式信息。默认值是将每个值都视为指向以空字符结尾的字符串的指针。</para>
        ///     <para>默认情况下，Arguments 参数的类型为 va_list*，这是一种语言和实现特定的数据类型，用于描述可变数量的参数。从函数返回时，va_list 参数的状态未定义。要再次使用 va_list，请使用 va_end 销毁变量参数列表指针，然后使用 va_start 对其进行初始化 。</para>
        ///     <para>如果您没有类型为 va_list* 的指针 ，则指定 <see cref="FormatMessageFlags.FORMAT_MESSAGE_ARGUMENT_ARRAY"/> 标志并将指针传递给 DWORD_PTR 值数组；这些值被输入到格式化为插入值的消息中。每个插入在数组中必须有一个对应的元素。</para>
        /// </param>
        /// <returns>如果函数成功，则返回值是存储在输出缓冲区中的 TCHAR 数量，不包括终止的空字符。
        ///     <para>如果函数失败，则返回值为零。要获取扩展的错误信息，请调用 <see cref="Marshal.GetLastWin32Error"/> 或 <see cref="Marshal.GetHRForLastWin32Error"/>。</para>
        /// </returns>
        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public extern static uint FormatMessage(FormatMessageFlags dwFlags, IntPtr lpSource, uint dwMessageId, uint dwLanguageId, StringBuilder lpBuffer, uint nSize, IntPtr Arguments);

        /// <summary>
        /// 格式化消息字符串。该功能需要消息定义作为输入。消息定义可以来自传递给函数的缓冲区。它可以来自已加载模块中的消息表资源。或者调用者可以要求函数在系统的消息表资源中搜索消息定义。
        /// <para>该函数根据消息标识符和语言标识符在消息表资源中找到消息定义。该函数将格式化的消息文本复制到输出缓冲区，如果需要，则处理所有嵌入的插入序列。</para>
        /// <para>参考：https://docs.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-formatmessage </para>
        /// </summary>
        /// <param name="dwFlags">格式设置选项以及如何解释lpSource参数。dwFlags的低位字节指定函数如何处理输出缓冲区中的换行符。低位字节还可以指定格式化输出行的最大宽度。
        ///     <para>如果低位字节是 <see cref="FormatMessageFlags.FORMAT_MESSAGE_MAX_WIDTH_MASK"/> 以外的非零值 ，则它指定输出行中的最大字符数。该函数将忽略消息定义文本中的常规换行符。该函数从不在换行符之间分割由空格分隔的字符串。该函数将消息定义文本中的硬编码换行符存储到输出缓冲区中。硬编码的换行符使用 ％n 转义序列编码。</para>
        /// </param>
        /// <param name="lpSource">消息定义的位置。此参数的类型取决于 dwFlags 参数中的设置。如果在 dwFlags 中均未设置以下这些标志，则将忽略 lpSource
        ///     <para><see cref="FormatMessageFlags.FORMAT_MESSAGE_FROM_HMODULE"/>  0x00000800  包含要搜索的消息表的模块的句柄。</para>
        ///     <para><see cref="FormatMessageFlags.FORMAT_MESSAGE_FROM_STRING"/>  0x00000400  指向由无格式消息文本组成的字符串的指针。将对其进行扫描以查找插入内容并进行相应的格式化。</para>
        /// </param>
        /// <param name="dwMessageId">所请求消息的消息标识符。如果 dwFlags 包含 <see cref="FormatMessageFlags.FORMAT_MESSAGE_FROM_STRING"/>，则忽略此参数 。</param>
        /// <param name="dwLanguageId">请求的消息的语言标识符。如果 dwFlags 包含 <see cref="FormatMessageFlags.FORMAT_MESSAGE_FROM_STRING"/>，则忽略此参数。(0 表示自动选择)</param>
        /// <param name="lpBuffer">[LPTSTR] 指向缓冲区的指针，该缓冲区接收以空终止的字符串，该字符串指定格式化的消息。该缓冲区不能大于 64K 字节。
        ///     <para></para>如果 dwFlags 包含 <see cref="FormatMessageFlags.FORMAT_MESSAGE_ALLOCATE_BUFFER"/>，则该函数使用 <see cref="LocalAlloc"/> 函数分配缓冲区，并将指向缓冲区的指针放在 lpBuffer 中指定的地址处。(否则自已分配空间。)</param>
        /// <param name="nSize">如果未设置 <see cref="FormatMessageFlags.FORMAT_MESSAGE_ALLOCATE_BUFFER"/> 标志，则此参数以 TCHARs 指定输出缓冲区的大小。如果 设置了 <see cref="FormatMessageFlags.FORMAT_MESSAGE_ALLOCATE_BUFFER"/>，则此参数指定分配给输出缓冲区的最小 TCHAR 数 。
        ///     <para>输出缓冲区不能大于64K字节。</para>
        /// </param>
        /// <param name="arguments">值数组，用作格式化消息中的插入值。格式字符串中的 %1 表示 Arguments数组中的第一个值；%2表示第二个参数；等等。
        ///     <para>每个值的解释取决于与消息定义中的插入关联的格式信息。默认值是将每个值都视为指向以空字符结尾的字符串的指针。</para>
        ///     <para>默认情况下，Arguments 参数的类型为 va_list*，这是一种语言和实现特定的数据类型，用于描述可变数量的参数。从函数返回时，va_list 参数的状态未定义。要再次使用 va_list，请使用 va_end 销毁变量参数列表指针，然后使用 va_start 对其进行初始化 。</para>
        ///     <para>如果您没有类型为 va_list* 的指针 ，则指定 <see cref="FormatMessageFlags.FORMAT_MESSAGE_ARGUMENT_ARRAY"/> 标志并将指针传递给 DWORD_PTR 值数组；这些值被输入到格式化为插入值的消息中。每个插入在数组中必须有一个对应的元素。</para>
        /// </param>
        /// <returns>如果函数成功，则返回值是存储在输出缓冲区中的 TCHAR 数量，不包括终止的空字符。
        ///     <para>如果函数失败，则返回值为零。要获取扩展的错误信息，请调用 <see cref="Marshal.GetLastWin32Error"/> 或 <see cref="Marshal.GetHRForLastWin32Error"/>。</para>
        /// </returns>
        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern uint FormatMessage(int dwFlags, ref long lpSource, uint dwMessageId, uint dwLanguageId, StringBuilder lpBuffer, uint nSize, IntPtr arguments);
    }


    /// <summary>
    /// WindowsAPI Kernel32库，扩展常用/通用，功能/函数，扩展示例，以及使用方式ad
    /// </summary>
    public static partial class Kernel32Extensions
    {
        /// <summary>
        /// 获取系统错误信息的描述
        /// <para>see <see cref="Marshal.GetLastWin32Error"/></para>
        /// <para>封装 <see cref="Kernel32.FormatMessage(FormatMessageFlags, IntPtr, uint, uint, ref string, uint, IntPtr)"/></para>
        /// </summary>
        /// <param name="errorCode">系统错误码</param>
        /// <returns></returns>
        public static string GetSystemErrorMessage(uint errorCode)
        {

#if false
            string message = null;
            FormatMessageFlags flags = FormatMessageFlags.FORMAT_MESSAGE_ALLOCATE_BUFFER | FormatMessageFlags.FORMAT_MESSAGE_IGNORE_INSERTS | FormatMessageFlags.FORMAT_MESSAGE_FROM_SYSTEM;
            Kernel32.FormatMessage(flags, IntPtr.Zero, errorCode, 0, ref message, 255, IntPtr.Zero);
#else
            StringBuilder message = new StringBuilder(255);
            FormatMessageFlags flags = FormatMessageFlags.FORMAT_MESSAGE_IGNORE_INSERTS | FormatMessageFlags.FORMAT_MESSAGE_FROM_SYSTEM;
            Kernel32.FormatMessage(flags, IntPtr.Zero, errorCode, 0, message, 255, IntPtr.Zero);
#endif

            return message.ToString().Trim();
        }

        /// <summary>
        /// 获取系统错误信息的描述
        /// <para>see <see cref="Marshal.GetLastWin32Error"/></para>
        /// </summary>
        /// <param name="sysFunctionName">系统函数名称，Window API 函数名称</param>
        /// <returns></returns>
        public static string GetSystemErrorMessage(string sysFunctionName)
        {
            StringBuilder message = new StringBuilder(255);
            int errorCode = Marshal.GetLastWin32Error();
            uint len = Kernel32.FormatMessage(FormatMessageFlags.FORMAT_MESSAGE_FROM_SYSTEM, IntPtr.Zero, (uint)errorCode, 0, message, 255, IntPtr.Zero);

            return $"函数 {sysFunctionName} 调用结果为: {message.ToString().Trim()}";
        }
    }

}
