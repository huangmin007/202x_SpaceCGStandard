namespace SpaceCG.WindowsAPI.Kernel32
{
    using System;
    using System.Runtime.InteropServices;


    #region Enumerations
    #endregion


    #region Structures
    #endregion


    #region Delegates
    #endregion


    #region Notifications
    #endregion


    /// <summary>
    /// Kernel32.dll 常用/实用 函数
    /// <para>Marshal.GetLastWin32Error(), new WindowInteropHelper(Window).Handle</para>
    /// <para>参考：C:\Program Files (x86)\Windows Kits\10\Include\10.0.18362.0 </para>
    /// </summary>
    public static partial class Kernel32
    {
        #region Functions   
        /// <summary>
        /// 将指定的模块加载到调用进程的地址空间中。指定的模块可能会导致其他模块被加载。
        /// <para>有关其他加载选项，请使用 LoadLibraryEx 函数。</para>
        /// <para>若要启用或禁用 DLL 加载期间加载程序显示的错误消息，请使用 SetErrorMode 函数。</para>
        /// <para>参考：https://docs.microsoft.com/zh-cn/windows/win32/api/libloaderapi/nf-libloaderapi-loadlibrarya </para>
        /// </summary>
        /// <param name="lpLibFileName">[LPCSTR] 模块的名称。这可以是库模块（.dll文件）或可执行模块（.exe文件）。指定的名称是模块的文件名，并且与模块模块定义（.def）文件中的 LIBRARY 关键字指定的库模块本身中存储的名称无关。
        ///     <para>如果字符串指定完整路径，则该函数仅在该路径中搜索模块。如果字符串指定相对路径或不带路径的模块名称，则该函数使用标准搜索策略查找模块；有关更多信息，请参见备注。</para>
        ///     <para>如果该功能找不到模块，则该功能将失败。指定路径时，请确保使用反斜杠（\），而不使用正斜杠（/）。有关路径的更多信息，请参见 命名文件或目录。</para>
        ///     <para>如果字符串指定没有路径的模块名称，并且省略了文件扩展名，则该函数会将默认库扩展名.dll附加到模块名。为防止函数将.dll附加到模块名称，请在模块名称字符串中包含尾随字符（。）。</para>
        /// </param>
        /// <returns>如果函数成功，则返回值是模块的句柄。如果函数失败，则返回值为 NULL。要获取扩展的错误信息，请调用 GetLastError。</returns>
        [DllImport("kernel32.dll", EntryPoint = "LoadLibrary", SetLastError = true)]
        public static extern IntPtr LoadLibrary(string lpLibFileName);

        /// <summary>
        /// 释放已加载的动态链接库（DLL）模块，并在必要时减少其引用计数。当引用计数达到零时，将从调用进程的地址空间中卸载模块，并且该句柄不再有效。
        /// <para>参考：https://docs.microsoft.com/zh-cn/windows/win32/api/libloaderapi/nf-libloaderapi-freelibrary </para>
        /// </summary>
        /// <param name="hLibModule">加载的库模块的句柄。在 调用 LoadLibrary，LoadLibraryEx，GetModuleHandle 或 GetModuleHandleEx 函数返回该句柄。</param>
        /// <returns>如果函数成功，则返回值为非零。如果函数失败，则返回值为零。若要获取扩展的错误信息，请调用 GetLastError 函数。</returns>
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool FreeLibrary(IntPtr hLibModule);
        
        /// <summary>
        /// 从指定的动态链接库（DLL）检索导出的函数或变量的地址。
        /// <para>参考：https://docs.microsoft.com/zh-cn/windows/win32/api/libloaderapi/nf-libloaderapi-getprocaddress </para>
        /// </summary>
        /// <param name="hModule">包含函数或变量的 DLL 模块的句柄。在调用LoadLibrary，LoadLibraryEx，LoadPackagedLibrary，或 GetModuleHandle 函数返回该句柄。</param>
        /// <param name="lpProcName">[LPCSTR] 函数或变量名称，或函数的序数值。如果此参数是序数值，则必须使用低位字；高阶字必须为零。</param>
        /// <returns>如果函数成功，则返回值是导出的函数或变量的地址。如果函数失败，则返回值为NULL。要获取扩展的错误信息，请调用 GetLastError。</returns>
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);

        /// <summary>
        /// 检索调用线程的线程标识符。
        /// <para>参考：https://docs.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-getcurrentthreadid </para>
        /// </summary>
        /// <returns>返回值是调用线程的线程标识符。</returns>
        [DllImport("kernel32.dll")]
        public static extern int GetCurrentThreadId();

        /// <summary>
        /// 检索指定模块的模块句柄。该模块必须已由调用进程加载。
        /// <para>返回的句柄不是全局的或可继承的。它不能被其他进程复制或使用。</para>
        /// <para>如果 lpModuleName 不包含路径，并且有多个加载的模块具有相同的基本名称和扩展名，则您无法预测将返回哪个模块句柄。要变通解决此问题，您可以指定路径，使用并行程序集或使用 GetModuleHandleEx 来指定内存位置而不是 DLL 名称。</para>
        /// <para>参考：https://docs.microsoft.com/en-us/windows/win32/api/libloaderapi/nf-libloaderapi-getmodulehandlea </para>
        /// </summary>
        /// <param name="lpModuleName">[LPCSTR] 加载的模块的名称（.dll或.exe文件）。如果省略文件扩展名，则会附加默认库扩展名.dll。</param>
        /// <returns>如果函数成功，则返回值是指定模块的句柄。如果函数失败，则返回值为NULL。要获取扩展的错误信息，请调用 GetLastError。</returns>
        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern IntPtr GetModuleHandle(string lpModuleName);
        #endregion

        #region Memory Functons
        /// <summary>
        /// 将内存从一个位置复制到另一个位置。
        /// <para><see cref="RtlCopyMemory"/> 比 <see cref="RtlMoveMemory"/> 运行得更快。但是，<see cref="RtlCopyMemory"/> 要求由 Source 和 Length 定义的源内存块不能与由 Destination 和 Length 定义的目标内存块重叠。相反，<see cref="RtlMoveMemory"/> 正确处理源和目标内存块重叠的情况。</para>
        /// <para>新驱动程序应该使用 <see cref="RtlCopyMemory"/> 例程而不是 <see cref="RtlCopyBytes"/>。</para>
        /// <para>参考：https://docs.microsoft.com/en-us/windows-hardware/drivers/ddi/wdm/nf-wdm-rtlcopymemory?redirectedfrom=MSDN </para>
        /// </summary>
        /// <param name="destination">指向复制块目标起始地址的指针</param>
        /// <param name="source">指向要复制的内存块起始地址的指针</param>
        /// <param name="length">要复制的内存块的大小，以字节为单位。</param>
        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern void RtlCopyMemory(IntPtr destination, IntPtr source, uint length);

        /// <summary>
        /// 拷贝源存储块的内容到目的地存储器块，并支持重叠的源和目的地的存储器块。
        /// <para>由 Source 和 Length 定义的源内存块可以与由 Destination 和 Length 定义的目标内存块重叠。</para>
        /// <para>该 <see cref="RtlCopyMemory"/> 程序运行速度比 <see cref="RtlMoveMemory"/>，但 <see cref="RtlCopyMemory"/> 要求源和目的地的内存块不重叠。</para>
        /// <para>参考：https://docs.microsoft.com/en-us/windows-hardware/drivers/ddi/wdm/nf-wdm-rtlmovememory </para>
        /// </summary>
        /// <param name="destination">指向要将字节复制到的目标内存块的指针</param>
        /// <param name="source">指向要从中复制字节的源内存块的指针。</param>
        /// <param name="length">要从源复制到目标的字节数。</param>
        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern void RtlMoveMemory(IntPtr destination, IntPtr source, uint length);

        /// <summary>
        /// 常规的填充用零存储器，给定指针块和长度，以字节为单位的块，将被填充。
        /// <para>要将内存缓冲区清零以擦除安全敏感数据，请改用 <see cref="RtlSecureZeroMemory"/>。</para>
        /// <para>参考：https://docs.microsoft.com/zh-cn/windows-hardware/drivers/ddi/wdm/nf-wdm-rtlzeromemory </para>
        /// </summary>
        /// <param name="destination">指向要填充零的内存块的指针</param>
        /// <param name="length">用零填充的字节数。</param>
        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern void RtlZeroMemory(IntPtr destination, uint length);

        /// <summary>
        /// 常规填充的内存在保证是安全的方式零块。
        /// <para><see cref="RtlSecureZeroMemory"/> 的效果与 <see cref="RtlZeroMemory"/> 的效果相同，除了它保证将内存位置归零，即使随后没有写入。（编译器可以优化掉对 <see cref="RtlZeroMemory"/> 的调用，如果它确定调用者不再访问该内存范围。）</para>
        /// <para>使用 <see cref="RtlSecureZeroMemory"/> 来保证敏感信息已被清零。例如，假设一个函数使用一个局部数组变量来存储密码信息。一旦函数退出，密码信息可以保留在相同的内存位置，除非被 <see cref="RtlSecureZeroMemory"/> 清零。</para>
        /// <para><see cref="RtlSecureZeroMemory"/> 比 <see cref="RtlZeroMemory"/> 慢；因此，如果安全性不是问题，请改用 <see cref="RtlZeroMemory"/>。</para>
        /// </summary>
        /// <param name="destination">指向要填充零的内存缓冲区的指针。</param>
        /// <param name="length">指定要填充零的字节数</param>
        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern void RtlSecureZeroMemory(IntPtr destination, uint length);

        /// <summary>
        /// 填充的存储器具有指定填充值的块
        /// <para>参考：https://docs.microsoft.com/en-us/windows-hardware/drivers/ddi/wdm/nf-wdm-rtlfillmemory </para>
        /// </summary>
        /// <param name="destination">指向要填充的内存块的指针。</param>
        /// <param name="length">要填充的内存块中的字节数</param>
        /// <param name="fill">填充目标内存块的值。该值被复制到由 Destination 和 Length 定义的内存块中的每个字节。</param>
        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern void RtlFillMemory(IntPtr destination, uint length, int fill);

        /// <summary>
        /// 比较两个存储器块，以确定指定的字节数是否相同。
        /// <para><see cref="RtlEqualMemory"/> 以每个块的字节零开始比较。如果 Source1 和 Source2 相等，则 <see cref="RtlEqualMemory"/> 返回 TRUE；否则，它返回 FALSE。</para>
        /// <para>参考：https://docs.microsoft.com/en-us/windows-hardware/drivers/ddi/wdm/nf-wdm-rtlequalmemory </para>
        /// </summary>
        /// <param name="destination">指向调用者分配的要比较的内存块的指针。</param>
        /// <param name="source">指向调用者分配的内存块的指针，该内存块与Source1指向的内存块进行比较。</param>
        /// <param name="length">指定要比较的字节数。</param>
        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern bool RtlEqualMemory(IntPtr destination, IntPtr source, uint length);

        /// <summary>
        /// 比较两个存储器块和返回的字节相匹配的数量。
        /// <para><see cref="RtlCompareMemory"/> 返回匹配的两个块中的字节数。如果所有字节都与指定的 Length 值匹配，则返回 Length 值。</para>
        /// <para>该例程首先将第一个块中的第一个字节与第二个块中的第一个字节进行比较，并在字节匹配时继续比较两个块中的连续字节。当遇到第一对不相等的字节时，或当匹配字节数等于 Length 参数值时，例程停止比较字节，以先发生者为准。</para>
        /// <para>参考：https://docs.microsoft.com/en-us/windows-hardware/drivers/ddi/wdm/nf-wdm-rtlcomparememory </para>
        /// </summary>
        /// <param name="source1">指向第一个内存块的指针</param>
        /// <param name="source2">指向第二个内存块的指针</param>
        /// <param name="length">要比较的字节数</param>
        /// <returns></returns>
        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern uint RtlCompareMemory(IntPtr source1, IntPtr source2, uint length);

        #endregion
    }

    /// <summary>
    /// WindowsAPI Kernel32 库，扩展常用/通用，功能/函数，扩展示例，以及使用方式
    /// </summary>
    public static partial class Kernel32Extensions
    {
        #region CopyMemoryEx

        /// <summary>   
        /// CopyMemoryEx   
        /// </summary>   
        /// <param name="dest">目标缓存区</param>   
        /// <param name="source">DataGraphPackage</param>   
        /// <returns></returns>   
        public unsafe static void CopyMemoryEx(byte[] dest, ref DataGraphHeader source)
        {
            CopyMemoryEx(dest, 0, ref source);
        }

        /// <summary>   
        /// CopyMemoryEx   
        /// </summary>   
        /// <param name="dest">目标缓存区</param>   
        /// <param name="DestStart">目标缓存区中的开始位置</param>   
        /// <param name="source">DataGraphPackage</param>   
        /// <returns></returns>   
        public unsafe static void CopyMemoryEx(byte[] dest, int DestStart, ref DataGraphHeader source)
        {
            IntPtr dp;
            IntPtr sp;
            fixed (byte* ds = &dest[DestStart])
            {
                fixed (DataGraphHeader* sr = &source)
                {
                    dp = (IntPtr)ds;
                    sp = (IntPtr)sr;
                    Kernel32.RtlCopyMemory(dp, sp, (uint)sizeof(DataGraphHeader));
                }
            }
        }


        /// <summary>   
        /// CopyMemoryEx   
        /// </summary>   
        /// <param name="dest">DataGraphPackage</param>   
        /// <param name="source">源数据缓存</param>   
        /// <returns></returns>   
        public unsafe static void CopyMemoryEx(ref DataGraphHeader dest, byte[] source)
        {
            CopyMemoryEx(ref dest, source, 0);
        }
        /// <summary>   
        /// CopyMemoryEx   
        /// </summary>   
        /// <param name="dest">DataGraphPackage</param>   
        /// <param name="source">源数据缓存</param>   
        /// <returns></returns>   
        public unsafe static void CopyMemoryEx(ref DataGraphHeader dest, byte[] source, int SourceStart)
        {
            IntPtr dp;
            IntPtr sp;
            fixed (DataGraphHeader* ds = &dest)
            {
                fixed (byte* sr = &source[SourceStart])
                {
                    dp = (IntPtr)ds;
                    sp = (IntPtr)sr;
                    Kernel32.RtlCopyMemory(dp, sp, (uint)sizeof(DataGraphHeader));
                }
            }
        }
        /// <summary>   
        /// CopyMemoryEx   
        /// </summary>   
        /// <param name="dest">目标缓存</param>   
        /// <param name="source">源数据</param>   
        /// <param name="size">要从源数据中复制的长度</param>   
        /// <returns></returns>   
        public unsafe static void CopyMemoryEx(byte[] dest, byte[] source, uint size)
        {
            CopyMemoryEx(dest, 0, source, 0, size);
        }
        /// <summary>   
        /// CopyMemoryEx   
        /// </summary>   
        /// <param name="dest">目标缓存</param>   
        /// <param name="source">源数据</param>   
        /// <param name="SourceStart">源数据缓存中开始位置</param>   
        /// <param name="size">要从源数据中复制的长度</param>   
        /// <returns></returns>   
        public unsafe static void CopyMemoryEx(byte[] dest, byte[] source, int SourceStart, uint size)
        {
            CopyMemoryEx(dest, 0, source, SourceStart, size);
        }
        /// <summary>   
        /// CopyMemoryEx   
        /// </summary>   
        /// <param name="dest">目标缓存</param>   
        /// <param name="DestStart">目标缓存中开始复制的位置</param>   
        /// <param name="source">源数据</param>   
        /// <param name="SourceStart">源数据缓存中开始位置</param>   
        /// <param name="size">要从源数据中复制的长度</param>   
        /// <returns></returns>   
        public unsafe static void CopyMemoryEx(byte[] dest, int DestStart, byte[] source, int SourceStart, uint size)
        {
            IntPtr dp;
            IntPtr sp;
            fixed (byte* ds = &dest[DestStart])
            {
                fixed (byte* sr = &source[SourceStart])
                {
                    dp = (IntPtr)ds;
                    sp = (IntPtr)sr;
                    Kernel32.RtlCopyMemory(dp, sp, size);
                }
            }
        }
        #endregion

    [StructLayout(LayoutKind.Sequential)]
    public struct ProtocolInfo
    {
        public byte Type;
        public byte Version;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DataGraphHeader
    {
        public byte Signature;              //包头：    1字节   
        public ProtocolInfo Protocol;       //协议：    2字节   
        public byte Type;                   //包类型：  1字节   
        public uint SerialID;               //包序号    4字节   
        public int Size;                    //包尺寸    4字节   
    }
}
}
