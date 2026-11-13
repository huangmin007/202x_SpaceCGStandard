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


    #region Enumerations
    /// <summary>
    /// 访问权限，安全对象使用访问掩码格式，其中四个高位指定通用访问权限。每种可保护对象的类型都将这些位映射到一组其标准和特定于对象的访问权限。
    /// </summary>
    [Flags]
    public enum AccessRights : uint
    {
        DEFAULT = 0,
        /// <summary>
        /// [文件访问权限常量] 对于文件对象，有权读取相应的文件数据。对于目录对象，有权读取相应的目录数据。
        /// </summary>
        FILE_READ_DATA = 0x0001,
        /// <summary>
        /// [文件访问权限常量] 对于目录，有权列出目录的内容。
        /// </summary>
        FILE_LIST_DIRECTORY = 0x0001,
        /// <summary>
        /// [文件访问权限常量] 对于文件对象，具有将数据写入文件的权利。对于目录对象，有权在目录（<see cref="FILE_ADD_FILE"/>）中创建文件。
        /// </summary>
        FILE_WRITE_DATA = 0x0002,
        /// <summary>
        /// [文件访问权限常量] 对于目录，有权在目录中创建文件。
        /// </summary>
        FILE_ADD_FILE = 0x0002,
        /// <summary>
        /// [文件访问权限常量] 对于文件对象，有权将数据附加到文件。（对于本地文件，如果在没有 <see cref="FILE_WRITE_DATA"/> 的情况下指定了此标志，则写操作将不会覆盖现有数据。）对于目录对象，有权创建子目录（<see cref="FILE_ADD_SUBDIRECTORY"/>）。
        /// </summary>
        FILE_APPEND_DATA = 0x0004,
        /// <summary>
        /// [文件访问权限常量] 对于目录，有权创建子目录。
        /// </summary>
        FILE_ADD_SUBDIRECTORY = 0x0004,
        /// <summary>
        /// [文件访问权限常量] 对于命名管道，有权创建管道。
        /// </summary>
        FILE_CREATE_PIPE_INSTANCE = 0x0004,
        /// <summary>
        /// [文件访问权限常量] 读取扩展文件属性的权利。
        /// </summary>
        FILE_READ_EA = 0x0008,
        /// <summary>
        /// [文件访问权限常量] 写入扩展文件属性的权利。
        /// </summary>
        FILE_WRITE_EA = 0x0010,
        /// <summary>
        /// [文件访问权限常量] 对于本机代码文件，有权执行该文件。授予脚本的访问权限可能导致脚本可执行，具体取决于脚本解释器。
        /// </summary>
        FILE_EXECUTE = 0x0020,
        /// <summary>
        /// [文件访问权限常量] 对于目录，具有遍历目录的权利。默认情况下，为用户分配 <see cref="BYPASS_TRAVERSE_CHECKING"/> 特权，该特权将忽略 <see cref="FILE_TRAVERSE"/> 访问权限。
        /// </summary>
        FILE_TRAVERSE = 0x0020,
        /// <summary>
        /// [文件访问权限常量] 对于目录，有权删除目录及其包含的所有文件，包括只读文件。
        /// </summary>
        FILE_DELETE_CHILD = 0x0040,
        /// <summary>
        /// [文件访问权限常量] 读取文件属性的权利。
        /// </summary>
        FILE_READ_ATTRIBUTES = 0x0080,
        /// <summary>
        /// [文件访问权限常量] 写入文件属性的权利。
        /// </summary>
        FILE_WRITE_ATTRIBUTES = 0x0100,
        /// <summary>
        /// [文件访问权限常量] 文件的所有可能的访问权限。
        /// </summary>
        FILE_ALL_ACCESS = STANDARD_RIGHTS_REQUIRED | SYNCHRONIZE | 0x1FF,

        /// <summary>
        /// [一般访问权限] 
        /// </summary>
        FILE_GENERIC_READ = STANDARD_RIGHTS_READ | FILE_READ_DATA | FILE_READ_ATTRIBUTES | FILE_READ_EA | SYNCHRONIZE,
        /// <summary>
        /// [一般访问权限]
        /// </summary>
        FILE_GENERIC_WRITE = STANDARD_RIGHTS_WRITE | FILE_WRITE_DATA | FILE_WRITE_ATTRIBUTES | FILE_WRITE_EA | FILE_APPEND_DATA | SYNCHRONIZE,
        /// <summary>
        /// [一般访问权限]
        /// </summary>
        FILE_GENERIC_EXECUTE = STANDARD_RIGHTS_EXECUTE | FILE_READ_ATTRIBUTES | FILE_EXECUTE | SYNCHRONIZE,

        /// <summary>
        /// [标准访问权限] 删除对象的权利。
        /// </summary>
        DELETE = 0x00010000,
        /// <summary>
        /// [标准访问权限] 有权读取对象的安全描述符中的信息，但不包括系统访问控制列表（SACL）中的信息。
        /// </summary>
        READ_CONTROL = 0x00020000,
        /// <summary>
        /// [标准访问权限] 修改对象的安全描述符中的任意访问控制列表（DACL）的权限。
        /// </summary>
        WRITE_DAC = 0x00040000,
        /// <summary>
        /// [标准访问权限] 在对象的安全描述符中更改所有者的权利。
        /// </summary>
        WRITE_OWNER = 0x00080000,
        /// <summary>
        /// [标准访问权限] 使用对象进行同步的权利。这使线程可以等待，直到对象处于信号状态。某些对象类型不支持此访问权限。
        /// </summary>
        SYNCHRONIZE = 0x00100000,
        /// <summary>
        /// [标准访问权限] 合并 <see cref="DELETE"/>，<see cref="READ_CONTROL"/>，<see cref="WRITE_DAC"/> 和 <see cref="WRITE_OWNER"/> 访问。
        /// </summary>
        STANDARD_RIGHTS_REQUIRED = 0x000F0000,
        /// <summary>
        /// [标准访问权限] 当前定义为等于 <see cref="READ_CONTROL"/>。它是读取文件或目录对象的安全描述符中的信息的权利。这不包括 SACL 中的信息。
        /// </summary>
        STANDARD_RIGHTS_READ = READ_CONTROL,
        /// <summary>
        /// [标准访问权限] 当前定义为等于 <see cref="READ_CONTROL"/>。与 <see cref="STANDARD_RIGHTS_READ"/> 相同。
        /// </summary>
        STANDARD_RIGHTS_WRITE = READ_CONTROL,
        /// <summary>
        /// [标准访问权限] 当前定义为等于 <see cref="READ_CONTROL"/>。
        /// </summary>
        STANDARD_RIGHTS_EXECUTE = READ_CONTROL,
        /// <summary>
        /// [标准访问权限] 合并 <see cref="DELETE"/>，<see cref="READ_CONTROL"/>，<see cref="WRITE_DAC"/>，<see cref="WRITE_OWNER"/> 和 <see cref="SYNCHRONIZE"/> 访问。
        /// </summary>
        STANDARD_RIGHTS_ALL = 0x001F0000,

        /// <summary>
        /// 
        /// </summary>
        SPECIFIC_RIGHTS_ALL = 0x0000FFFF,


        /// <summary>
        /// AccessSystemAcl access type
        /// </summary>
        ACCESS_SYSTEM_SECURITY = 0x01000000,
        /// <summary>
        /// MaximumAllowed access type
        /// </summary>
        MAXIMUM_ALLOWED = 0x02000000,



        /// <summary>
        /// [通用访问权限] 所有可能的访问权限
        /// </summary>
        GENERIC_ALL = 0x10000000,
        /// <summary>
        /// [通用访问权限] 执行访问 
        /// </summary>
        GENERIC_EXECUTE = 0x20000000,
        /// <summary>
        /// [通用访问权限] 写访问
        /// </summary>
        GENERIC_WRITE = 0x40000000,
        /// <summary>
        /// [通用访问权限] 读取权限 
        /// </summary>
        GENERIC_READ = 0x80000000,

    }

    /// <summary>
    /// <see cref="Kernel32.CreateFile"/> 参数 dwShareMode
    /// <para>参考： https://docs.microsoft.com/zh-cn/windows/win32/api/fileapi/nf-fileapi-createfilea </para>
    /// </summary>
    [Flags]
    public enum ShareMode:uint
    {
        /// <summary>
        /// 阻止其他进程在请求删除，读取或写入访问时打开文件或设备。
        /// </summary>
        FILE_SHARE_DEFAULT = 0x00000000,
        /// <summary>
        /// 在文件或设备上启用后续打开操作，以请求读取访问权限。否则，其他进程如果请求读取访问权限，则无法打开文件或设备。如果未指定此标志，但是已打开文件或设备以进行读取访问，则该功能将失败。
        /// </summary>
        FILE_SHARE_READ = 0x00000001,
        /// <summary>
        /// 在文件或设备上启用后续打开操作以请求写访问权限。否则，其他进程如果请求写访问权，则无法打开文件或设备。如果未指定此标志，但已打开文件或设备以进行写访问，或者具有具有写访问权限的文件映射，则该功能将失败。
        /// </summary>
        FILE_SHARE_WRITE = 0x00000002,
        /// <summary>
        /// 在文件或设备上启用后续打开操作以请求删除访问。否则，如果其他进程请求删除访问，则它们将无法打开文件或设备。如果未指定此标志，但是已打开文件或设备以进行删除访问，则该功能将失败。
        /// <para>注意:删除访问权限允许删除和重命名操作。</para>
        /// </summary>
        FILE_SHARE_DELETE = 0x00000004,
    }

    /// <summary>
    /// <see cref="Kernel32.CreateFile"/> 函数参数 dwCreationDisposition 的值之一
    /// </summary>
    public enum CreationDisposition:uint
    {
        /// <summary>
        /// 仅在尚不存在时创建一个新文件。如果指定的文件存在，则函数失败，并且上一个错误代码设置为 ERROR_FILE_EXISTS（80）。
        /// <para>如果指定的文件不存在，并且是可写位置的有效路径，则会创建一个新文件。</para>
        /// </summary>
        CREATE_NEW = 1,
        /// <summary>
        /// 始终创建一个新文件。如果指定的文件存在并且可写，则该函数将覆盖该文件，该函数将成功，并且上一个错误代码设置为 ERROR_ALREADY_EXISTS（183）。
        /// <para>如果指定的文件不存在并且是有效路径，则创建一个新文件，该函数成功，并且最后一个错误代码设置为零。</para>
        /// </summary>
        CREATE_ALWAYS = 2,
        /// <summary>
        /// 仅打开文件或设备（如果存在）。如果指定的文件或设备不存在，该功能将失败，并且上一个错误代码将设置为 ERROR_FILE_NOT_FOUND（2）。
        /// </summary>
        OPEN_EXISTING = 3,
        /// <summary>
        /// 始终打开文件。如果指定的文件存在，则函数成功，并且最后一个错误代码设置为 ERROR_ALREADY_EXISTS（183）。
        /// <para>如果指定的文件不存在，并且是可写位置的有效路径，则该函数将创建一个文件，并且上一个错误代码将设置为零。</para>
        /// </summary>
        OPEN_ALWAYS = 4,
        /// <summary>
        /// 打开文件并将其截断，以使其大小为零字节（仅存在时）。如果指定的文件不存在，该函数将失败，并且上一个错误代码将设置为 ERROR_FILE_NOT_FOUND（2）。
        /// <para>调用进程必须使用 dwDesiredAccess 参数的一部分设置 GENERIC_WRITE 位来打开文件。</para>
        /// </summary>
        TRUNCATE_EXISTING = 5,
    }

    /// <summary>
    /// 文件属性标志。此参数可以是一个或多个值，可以使用按位或运算符组合。
    /// <para>参考：https://docs.microsoft.com/zh-cn/windows/win32/api/fileapi/nf-fileapi-setfileattributesa </para>
    /// <para>参考：https://docs.microsoft.com/zh-cn/windows/win32/api/fileapi/nf-fileapi-createfilea </para>
    /// </summary>
    [Flags]
    public enum FileAttributes : uint
    {
        /// <summary>
        /// 该文件是只读的。应用程序可以读取文件，但不能写入或删除文件。
        /// </summary>
        FILE_ATTRIBUTE_READONLY = 0x01,
        /// <summary>
        /// 该文件被隐藏。不要将其包括在普通目录列表中。
        /// </summary>
        FILE_ATTRIBUTE_HIDDEN = 0x02,
        /// <summary>
        /// 该文件是操作系统的一部分或仅由操作系统使用。
        /// </summary>
        FILE_ATTRIBUTE_SYSTEM = 0x04,
        /// <summary>
        /// 该文件应被存档。应用程序使用此属性将文件标记为备份或删除。
        /// </summary>
        FILE_ATTRIBUTE_ARCHIVE = 0x20,
        /// <summary>
        /// 该文件未设置其他属性。仅当单独使用时，此属性才有效。
        /// </summary>
        FILE_ATTRIBUTE_NORMAL = 0x80,
        /// <summary>
        /// 该文件正在用于临时存储。
        /// </summary>
        FILE_ATTRIBUTE_TEMPORARY = 0x0100,
        /// <summary>
        /// 文件的数据不是立即可用的。此属性指示文件数据已物理移动到脱机存储中。分层存储管理软件 Remote Storage 使用此属性。应用程序不应随意更改此属性。
        /// </summary>
        FILE_ATTRIBUTE_OFFLINE = 0x1000,
        /// <summary>
        /// 文件或目录已加密。对于文件，这意味着文件中的所有数据都已加密。对于目录，这意味着加密是新创建的文件和子目录的默认设置。有关更多信息，请参见文件加密。如果还指定了 <see cref="FILE_ATTRIBUTE_SYSTEM"/>，则此标志无效。
        /// <para>Windows的Home，Home Premium，Starter或ARM版本不支持此标志。</para>
        /// </summary>
        FILE_ATTRIBUTE_ENCRYPTED = 0x4000,
    }

    /// <summary>
    /// 文件标志
    /// <para>参考：https://docs.microsoft.com/zh-cn/windows/win32/api/fileapi/nf-fileapi-createfilea </para>
    /// </summary>
    [Flags]
    public enum FileFlags : uint
    {
        /// <summary>
        /// 已请求文件数据，但应继续将其放在远程存储中。不应将其传输回本地存储。该标志供远程存储系统使用。
        /// </summary>
        FILE_FLAG_OPEN_NO_RECALL = 0x00100000,
        /// <summary>
        /// 正常的重定点处理将不会发生；<see cref="Kernel32.CreateFile"/> 将尝试打开重新解析点。打开文件后，无论控制重解析点的筛选器是否可运行，都将返回文件句柄。该标志不能与 <see cref="CreationDisposition.CREATE_ALWAYS"/> 标志一起使用。如果文件不是重新分析点，则忽略此标志。
        /// </summary>
        FILE_FLAG_OPEN_REPARSE_POINT = 0x00200000,
        /// <summary>
        /// 打开文件或设备时具有会话意识。如果未指定此标志，则按会话 0 运行的进程无法打开按会话的设备（例如使用 RemoteFX USB 重定向的设备）。此标志对不在会话0中的调用者无效。仅在以下情况下支持此标志 Windows 的服务器版本。
        /// </summary>
        FILE_FLAG_SESSION_AWARE = 0x00800000,
        /// <summary>
        /// 访问将根据 POSIX 规则进行。对于支持该命名的文件系统，这包括允许多个文件的名称（大小写不同）。使用此选项时请格外小心，因为为 MS-DOS 或 16位 Windows 编写的应用程序可能无法访问使用此标志创建的文件。
        /// </summary>
        FILE_FLAG_POSIX_SEMANTICS = 0x0100000,
        /// <summary>
        /// 正在为备份或还原操作打开或创建文件。当进程具有 SE_BACKUP_NAME 和 SE_RESTORE_NAME 特权时，系统将确保调用进程覆盖文件安全检查。有关更多信息，请参见 更改令牌中的特权。您必须设置此标志以获得目录的句柄。可以将目录句柄而不是文件句柄传递给某些函数。
        /// </summary>
        FILE_FLAG_BACKUP_SEMANTICS = 0x02000000,
        /// <summary>
        /// 关闭所有句柄（包括指定的句柄以及任何其他打开或重复的句柄）后，将立即删除该文件。如果文件已有打开的句柄，则调用将失败，除非使用 <see cref="ShareMode.FILE_SHARE_DELETE"/> 共享模式全部打开了它们 。除非指定了 <see cref="ShareMode.FILE_SHARE_DELETE"/>  共享模式，否则随后对文件的打开请求将失败。
        /// </summary>
        FILE_FLAG_DELETE_ON_CLOSE = 0x04000000,
        /// <summary>
        /// 访问的目的是从头到尾都是顺序的。系统可以以此为提示来优化文件缓存。如果将使用后读（即反向扫描），则不应使用此标志。如果文件系统不支持缓存的 I/O 和 <see cref="FILE_FLAG_NO_BUFFERING"/> ，则此标志无效 。
        /// </summary>
        FILE_FLAG_SEQUENTIAL_SCAN = 0x08000000,
        /// <summary>
        /// 访问意图是随机的。系统可以以此为提示来优化文件缓存。如果文件系统不支持缓存的 I/O 和 <see cref="FILE_FLAG_NO_BUFFERING"/>，则此标志无效 。
        /// </summary>
        FILE_FLAG_RANDOM_ACCESS = 0x10000000,
        /// <summary>
        /// 文件或设备正在打开，没有用于数据读写的系统缓存。该标志不影响硬盘缓存或内存映射文件。对于使用 <see cref="FILE_FLAG_NO_BUFFERING"/> 标志使用 CreateFile 打开的文件成功进行操作有严格的要求 ，有关详细信息，请参阅 文件缓冲。
        /// </summary>
        FILE_FLAG_NO_BUFFERING = 0x20000000,
        /// <summary>
        /// 正在为异步 I/O 打开或创建文件或设备。当在此句柄上完成后续 I/O 操作时，在 OVERLAPPED 结构中指定的事件 将被设置为信号状态。如果指定了此标志，则该文件可用于同时进行读写操作。如果未指定此标志，则即使读取和写入功能的调用指定了 OVERLAPPED 结构， I/O 操作也会被序列化。
        /// </summary>
        FILE_FLAG_OVERLAPPED = 0x40000000,
        /// <summary>
        /// 写操作将不会通过任何中间缓存，而将直接进入磁盘。
        /// </summary>
        FILE_FLAG_WRITE_THROUGH = 0x80000000,
    }

    [Flags]
    public enum SecurityQosFlags:uint
    {
        /// <summary>
        /// 在匿名模拟级别上模拟客户端。
        /// </summary>
        SECURITY_ANONYMOUS,
        /// <summary>
        /// 在“身份”模拟级别上模拟客户端。
        /// </summary>
        SECURITY_IDENTIFICATION,
        /// <summary>
        /// 在委托模拟级别上模拟客户。
        /// </summary>
        SECURITY_DELEGATION,
        /// <summary>
        /// 安全跟踪模式是动态的。如果未指定此标志，则安全跟踪模式为静态。
        /// </summary>
        SECURITY_CONTEXT_TRACKING,
        /// <summary>
        /// 服务器仅可使用客户端安全上下文的已启用方面。如果未指定此标志，则客户端安全上下文的所有方面均可用。这使客户端可以限制服务器在模拟客户端时可以使用的组和特权。
        /// </summary>
        SECURITY_EFFECTIVE_ONLY,
        /// <summary>
        /// 在模拟级别模拟客户。如果未与 <see cref="SECURITY_SQOS_PRESENT"/> 标志一起指定其他标志，则这是默认行为。
        /// </summary>
        SECURITY_IMPERSONATION,
    }

    #endregion


    #region Structures
    /// <summary>
    /// 包含安全描述符的对象，并指定通过指定该结构中检索所述手柄是否是继承。此结构为由各种功能（例如 <see cref="Kernel32.CreateFile"/>，<see cref="Kernel32.CreatePipe"/>，<see cref="Kernel32.CreateProcess"/>，<see cref="Kernel32.RegCreateKeyEx"/> 或 <see cref="Kernel32.RegSaveKeyEx"/>) 创建的对象提供安全设置。
    /// <para>SECURITY_ATTRIBUTES, *PSECURITY_ATTRIBUTES, *LPSECURITY_ATTRIBUTES</para>
    /// <para>参考：https://docs.microsoft.com/zh-cn/previous-versions/windows/desktop/legacy/aa379560(v=vs.85) </para>
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct SECURITY_ATTRIBUTES
    {
        /// <summary>
        /// 此结构的大小（以字节为单位）。将此值设置为 <see cref="SECURITY_ATTRIBUTES.Size"/> 结构的大小。
        /// </summary>
        public uint nLength;
        /// <summary>
        /// 指向 <see cref="SECURITY_DESCRIPTOR"/> 结构的指针，该结构控制对对象的访问。如果此成员的值为 NULL，则为对象分配与调用过程的访问令牌关联的默认安全描述符。
        /// <para>这与通过分配 NULL 自由访问控制列表（DACL）向所有人授予访问权限不同。默认情况下，进程的访问令牌中的默认 DACL 只允许访问该访问令牌所代表的用户。</para>
        /// </summary>
        [MarshalAs(UnmanagedType.AsAny)]
        public IntPtr lpSecurityDescriptor;
        /// <summary>
        /// 一个布尔值，它指定在创建新进程时是否继承返回的句柄。如果此成员为 TRUE，则新进程将继承该句柄。
        /// </summary>
        [MarshalAs(UnmanagedType.Bool)]
        public bool bInheritHandle;
        /// <summary>
        /// Create new SECURITY_ATTRIBUTES
        /// </summary>
        /// <returns></returns>
        public static SECURITY_ATTRIBUTES Create()
        {
            return new SECURITY_ATTRIBUTES() { nLength = (uint)Marshal.SizeOf(typeof(SECURITY_ATTRIBUTES)) };
        }

        /// <summary>
        /// <see cref="SECURITY_ATTRIBUTES"/> 结构体大小，以字节为单位
        /// </summary>
        public static readonly uint Size = (uint)Marshal.SizeOf(typeof(SECURITY_ATTRIBUTES));

        /// <summary>
        /// @ToString()
        /// </summary>
        /// <returns></returns>
        public override string ToString()
        {
            return $"[{nameof(SECURITY_ATTRIBUTES)}] nLength:{nLength}, lpSecurityDescriptor:{lpSecurityDescriptor}, bInheritHandle:{bInheritHandle}";
        }
    }

    /// <summary>
    /// 
    /// </summary>
    public struct ACL
    {
        public byte AclRevision;
        public byte Sbz1;
        public ushort AclSize;
        public ushort AceCount;
        public ushort Sbz2;
    }

    /// <summary>
    /// 包含与对象相关联的安全信息。应用程序使用此结构来设置和查询对象的安全状态。(SECURITY_DESCRIPTOR, *PISECURITY_DESCRIPTOR)
    /// <para>参考：https://docs.microsoft.com/zh-cn/windows/win32/api/winnt/ns-winnt-security_descriptor?redirectedfrom=MSDN </para>
    /// </summary>
    public struct SECURITY_DESCRIPTOR
    {
        public byte Revision;
        public byte Sbz1;
        public ushort Control;
        public IntPtr Owner;
        public IntPtr Group;
        public ACL Sacl;
        public ACL Dacl;
    }
    
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
        #region Create File
        /// <summary>
        /// 创建或打开文件或 I/O 设备。最常用的 I/O 设备如下：文件，文件流，目录，物理磁盘，卷，控制台缓冲区，磁带机，通信资源，邮筒和管道。该函数返回一个句柄，根据文件或设备以及指定的标志和属性，该句柄可用于访问各种类型的 I/O 的文件或设备。
        /// <para>要将此操作作为事务处理操作执行，从而产生可用于事务处理 I/O 的句柄，请使用 <see cref="CreateFileTransacted"/> 函数。</para>
        /// <para><see cref="CreateFile(string, uint, uint, IntPtr, uint, uint, IntPtr)"/> 最初是专门为文件交互而开发的，但此后已进行了扩展和增强，以包括 Windows 开发人员可用的大多数其他类型的 I/O 设备和机制。</para>
        /// <para>使用 <see cref="CreateFile(string, uint, uint, IntPtr, uint, uint, IntPtr)"/> 返回的对象句柄完成应用程序后 ，请使用 <see cref="CloseHandle"/> 函数关闭该句柄。这不仅释放了系统资源，而且还对共享文件或设备以及将数据提交到磁盘等事物产生更大的影响。</para>
        /// <para>参考：https://docs.microsoft.com/zh-cn/windows/win32/api/fileapi/nf-fileapi-createfilea </para>
        /// </summary>
        /// <param name="lpFileName">[LPCSTR] 要创建或打开的文件或设备的名称。您可以在此名称中使用正斜杠（/）或反斜杠（）。</param>
        /// <param name="dwDesiredAccess">所请求的对文件或设备的访问，可以概括为读，写，两者均为零或都不为零
        ///     <para>如果此参数为零，即使将拒绝 <see cref="AccessRights.GENERIC_READ"/> 访问，应用程序也可以查询某些元数据，例如文件，目录或设备属性，而无需访问该文件或设备。</para>
        /// </param>
        /// <param name="dwShareMode">文件或设备的请求共享模式，可以读取，写入，两者，删除，全部或全部不共享。对属性或扩展属性的访问请求不受此标志的影响。
        ///     <para>如果此参数为零，并且 <see cref="CreateFile(string, uint, uint, IntPtr, uint, uint, IntPtr)"/> 成功，则在关闭文件或设备的句柄之前，无法共享文件或设备，也无法再次打开该文件或设备。</para>
        /// </param>
        /// <param name="lpSecurityAttributes">指向 <see cref="SECURITY_ATTRIBUTES"/> 结构的指针，该结构包含两个单独但相关的数据成员：一个可选的安全描述符，以及一个布尔值，该值确定子进程是否可以继承返回的句柄。此参数可以为 NULL。
        ///     <para>如果此参数为 NULL，则 <see cref="CreateFile(string, uint, uint, IntPtr, uint, uint, IntPtr)"/> 返回的句柄 不能被应用程序可能创建的任何子进程继承，并且与返回的句柄关联的文件或设备将获得默认的安全描述符。</para>
        /// </param>
        /// <param name="dwCreationDisposition">对存在或不存在的文件或设备执行的操作。对于文件以外的设备，此参数通常设置为 <see cref="CreationDisposition.OPEN_EXISTING"/>。</param>
        /// <param name="dwFlagsAndAttributes">文件或设备属性和标志，<see cref="FileAttributes.FILE_ATTRIBUTE_NORMAL"/> 是文件的最常见默认值。此参数可以包括可用文件属性（FILE_ATTRIBUTE_*）的任意组合。所有其他文件属性将覆盖 <see cref="FileAttributes.FILE_ATTRIBUTE_NORMAL"/>。
        ///     <para>此参数还可以包含标志的组合（FILE_FLAG_），用于控制文件或设备的缓存行为，访问模式和其他特殊用途的标志。这些与任何 FILE_ATTRIBUTE_ 值组合。</para>
        ///     <para>通过指定 SECURITY_SQOS_PRESENT 标志，此参数还可以包含安全服务质量（SQOS）信息 。在属性和标志表之后的表中提供了其他与 SQOS 相关的标志信息。</para>
        ///     <para>可以是 <see cref="FileAttributes"/> 或 <see cref="FileFlags"/> 或 <see cref="SecurityQosFlags"/> 的值或是组合 </para>
        /// </param>
        /// <param name="hTemplateFile">具有 <see cref="AccessRights.GENERIC_READ"/> 访问权限的模板文件的有效句柄。模板文件为正在创建的文件提供文件属性和扩展属性。
        ///     <para>此参数可以为 NULL。打开现有文件时，<see cref="CreateFile(string, uint, uint, IntPtr, uint, uint, IntPtr)"/> 会忽略此参数。当打开一个新的加密文件时，该文件会从其父目录继承任意访问控制列表。</para>
        /// </param>
        /// <returns>如果函数成功，则返回值是指定文件，设备，命名管道或邮件插槽的打开句柄。
        ///     <para>如果函数失败，则返回值为 INVALID_HANDLE_VALUE。要获取扩展的错误信息，请调用 <see cref="Marshal.GetLastWin32Error"/> 或 <see cref="Marshal.GetHRForLastWin32Error"/>。</para>
        /// </returns>
        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern IntPtr CreateFile(string lpFileName, uint dwDesiredAccess, uint dwShareMode, IntPtr lpSecurityAttributes, uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);
        
        /// <summary>
        /// 创建或打开文件或 I/O 设备。最常用的 I/O 设备如下：文件，文件流，目录，物理磁盘，卷，控制台缓冲区，磁带机，通信资源，邮筒和管道。该函数返回一个句柄，根据文件或设备以及指定的标志和属性，该句柄可用于访问各种类型的 I/O 的文件或设备。
        /// <para>要将此操作作为事务处理操作执行，从而产生可用于事务处理 I/O 的句柄，请使用 <see cref="CreateFileTransacted"/> 函数。</para>
        /// <para><see cref="CreateFile(string, uint, uint, IntPtr, uint, uint, IntPtr)"/> 最初是专门为文件交互而开发的，但此后已进行了扩展和增强，以包括 Windows 开发人员可用的大多数其他类型的 I/O 设备和机制。</para>
        /// <para>使用 <see cref="CreateFile(string, uint, uint, IntPtr, uint, uint, IntPtr)"/> 返回的对象句柄完成应用程序后 ，请使用 <see cref="CloseHandle"/> 函数关闭该句柄。这不仅释放了系统资源，而且还对共享文件或设备以及将数据提交到磁盘等事物产生更大的影响。</para>
        /// <para>参考：https://docs.microsoft.com/zh-cn/windows/win32/api/fileapi/nf-fileapi-createfilea </para>
        /// </summary>
        /// <param name="lpFileName">[LPCSTR] 要创建或打开的文件或设备的名称。您可以在此名称中使用正斜杠（/）或反斜杠（）。</param>
        /// <param name="dwDesiredAccess">所请求的对文件或设备的访问，可以概括为读，写，两者均为零或都不为零
        ///     <para>如果此参数为零，即使将拒绝 <see cref="AccessRights.GENERIC_READ"/> 访问，应用程序也可以查询某些元数据，例如文件，目录或设备属性，而无需访问该文件或设备。</para>
        /// </param>
        /// <param name="dwShareMode">文件或设备的请求共享模式，可以读取，写入，两者，删除，全部或全部不共享。对属性或扩展属性的访问请求不受此标志的影响。
        ///     <para>如果此参数为零，并且 <see cref="CreateFile(string, uint, uint, IntPtr, uint, uint, IntPtr)"/> 成功，则在关闭文件或设备的句柄之前，无法共享文件或设备，也无法再次打开该文件或设备。</para>
        /// </param>
        /// <param name="lpSecurityAttributes">指向 <see cref="SECURITY_ATTRIBUTES"/> 结构的指针，该结构包含两个单独但相关的数据成员：一个可选的安全描述符，以及一个布尔值，该值确定子进程是否可以继承返回的句柄。此参数可以为 NULL。
        ///     <para>如果此参数为 NULL，则 <see cref="CreateFile(string, uint, uint, IntPtr, uint, uint, IntPtr)"/> 返回的句柄 不能被应用程序可能创建的任何子进程继承，并且与返回的句柄关联的文件或设备将获得默认的安全描述符。</para>
        /// </param>
        /// <param name="dwCreationDisposition">对存在或不存在的文件或设备执行的操作。对于文件以外的设备，此参数通常设置为 <see cref="CreationDisposition.OPEN_EXISTING"/>。</param>
        /// <param name="dwFlagsAndAttributes">文件或设备属性和标志，<see cref="FileAttributes.FILE_ATTRIBUTE_NORMAL"/> 是文件的最常见默认值。此参数可以包括可用文件属性（FILE_ATTRIBUTE_*）的任意组合。所有其他文件属性将覆盖 <see cref="FileAttributes.FILE_ATTRIBUTE_NORMAL"/>。
        ///     <para>此参数还可以包含标志的组合（FILE_FLAG_），用于控制文件或设备的缓存行为，访问模式和其他特殊用途的标志。这些与任何 FILE_ATTRIBUTE_ 值组合。</para>
        ///     <para>通过指定 SECURITY_SQOS_PRESENT 标志，此参数还可以包含安全服务质量（SQOS）信息 。在属性和标志表之后的表中提供了其他与 SQOS 相关的标志信息。</para>
        ///     <para>可以是 <see cref="FileAttributes"/> 或 <see cref="FileFlags"/> 或 <see cref="SecurityQosFlags"/> 的值或是组合 </para>
        /// </param>
        /// <param name="hTemplateFile">具有 <see cref="AccessRights.GENERIC_READ"/> 访问权限的模板文件的有效句柄。模板文件为正在创建的文件提供文件属性和扩展属性。
        ///     <para>此参数可以为 NULL。打开现有文件时，<see cref="CreateFile(string, uint, uint, IntPtr, uint, uint, IntPtr)"/> 会忽略此参数。当打开一个新的加密文件时，该文件会从其父目录继承任意访问控制列表。</para>
        /// </param>
        /// <returns>如果函数成功，则返回值是指定文件，设备，命名管道或邮件插槽的打开句柄。
        ///     <para>如果函数失败，则返回值为 INVALID_HANDLE_VALUE。要获取扩展的错误信息，请调用 <see cref="Marshal.GetLastWin32Error"/> 或 <see cref="Marshal.GetHRForLastWin32Error"/>。</para>
        /// </returns>
        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern IntPtr CreateFile(string lpFileName, AccessRights dwDesiredAccess, ShareMode dwShareMode, IntPtr lpSecurityAttributes, CreationDisposition dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);
        
        /// <summary>
        /// 创建或打开文件或 I/O 设备。最常用的 I/O 设备如下：文件，文件流，目录，物理磁盘，卷，控制台缓冲区，磁带机，通信资源，邮筒和管道。该函数返回一个句柄，根据文件或设备以及指定的标志和属性，该句柄可用于访问各种类型的 I/O 的文件或设备。
        /// <para>要将此操作作为事务处理操作执行，从而产生可用于事务处理 I/O 的句柄，请使用 <see cref="CreateFileTransacted"/> 函数。</para>
        /// <para><see cref="CreateFile(string, uint, uint, IntPtr, uint, uint, IntPtr)"/> 最初是专门为文件交互而开发的，但此后已进行了扩展和增强，以包括 Windows 开发人员可用的大多数其他类型的 I/O 设备和机制。</para>
        /// <para>使用 <see cref="CreateFile(string, uint, uint, IntPtr, uint, uint, IntPtr)"/> 返回的对象句柄完成应用程序后 ，请使用 <see cref="CloseHandle"/> 函数关闭该句柄。这不仅释放了系统资源，而且还对共享文件或设备以及将数据提交到磁盘等事物产生更大的影响。</para>
        /// <para>参考：https://docs.microsoft.com/zh-cn/windows/win32/api/fileapi/nf-fileapi-createfilea </para>
        /// </summary>
        /// <param name="lpFileName">[LPCSTR] 要创建或打开的文件或设备的名称。您可以在此名称中使用正斜杠（/）或反斜杠（）。</param>
        /// <param name="dwDesiredAccess">所请求的对文件或设备的访问，可以概括为读，写，两者均为零或都不为零
        ///     <para>如果此参数为零，即使将拒绝 <see cref="AccessRights.GENERIC_READ"/> 访问，应用程序也可以查询某些元数据，例如文件，目录或设备属性，而无需访问该文件或设备。</para>
        /// </param>
        /// <param name="dwShareMode">文件或设备的请求共享模式，可以读取，写入，两者，删除，全部或全部不共享。对属性或扩展属性的访问请求不受此标志的影响。
        ///     <para>如果此参数为零，并且 <see cref="CreateFile(string, uint, uint, IntPtr, uint, uint, IntPtr)"/> 成功，则在关闭文件或设备的句柄之前，无法共享文件或设备，也无法再次打开该文件或设备。</para>
        /// </param>
        /// <param name="lpSecurityAttributes">指向 <see cref="SECURITY_ATTRIBUTES"/> 结构的指针，该结构包含两个单独但相关的数据成员：一个可选的安全描述符，以及一个布尔值，该值确定子进程是否可以继承返回的句柄。此参数可以为 NULL。
        ///     <para>如果此参数为 NULL，则 <see cref="CreateFile(string, uint, uint, IntPtr, uint, uint, IntPtr)"/> 返回的句柄 不能被应用程序可能创建的任何子进程继承，并且与返回的句柄关联的文件或设备将获得默认的安全描述符。</para>
        /// </param>
        /// <param name="dwCreationDisposition">对存在或不存在的文件或设备执行的操作。对于文件以外的设备，此参数通常设置为 <see cref="CreationDisposition.OPEN_EXISTING"/>。</param>
        /// <param name="dwFlagsAndAttributes">文件或设备属性和标志，<see cref="FileAttributes.FILE_ATTRIBUTE_NORMAL"/> 是文件的最常见默认值。此参数可以包括可用文件属性（FILE_ATTRIBUTE_*）的任意组合。所有其他文件属性将覆盖 <see cref="FileAttributes.FILE_ATTRIBUTE_NORMAL"/>。
        ///     <para>此参数还可以包含标志的组合（FILE_FLAG_），用于控制文件或设备的缓存行为，访问模式和其他特殊用途的标志。这些与任何 FILE_ATTRIBUTE_ 值组合。</para>
        ///     <para>通过指定 SECURITY_SQOS_PRESENT 标志，此参数还可以包含安全服务质量（SQOS）信息 。在属性和标志表之后的表中提供了其他与 SQOS 相关的标志信息。</para>
        ///     <para>可以是 <see cref="FileAttributes"/> 或 <see cref="FileFlags"/> 或 <see cref="SecurityQosFlags"/> 的值或是组合 </para>
        /// </param>
        /// <param name="hTemplateFile">具有 <see cref="AccessRights.GENERIC_READ"/> 访问权限的模板文件的有效句柄。模板文件为正在创建的文件提供文件属性和扩展属性。
        ///     <para>此参数可以为 NULL。打开现有文件时，<see cref="CreateFile(string, uint, uint, IntPtr, uint, uint, IntPtr)"/> 会忽略此参数。当打开一个新的加密文件时，该文件会从其父目录继承任意访问控制列表。</para>
        /// </param>
        /// <returns>如果函数成功，则返回值是指定文件，设备，命名管道或邮件插槽的打开句柄。
        ///     <para>如果函数失败，则返回值为 INVALID_HANDLE_VALUE。要获取扩展的错误信息，请调用 <see cref="Marshal.GetLastWin32Error"/> 或 <see cref="Marshal.GetHRForLastWin32Error"/>。</para>
        /// </returns>
        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern IntPtr CreateFile(string lpFileName, AccessRights dwDesiredAccess, ShareMode dwShareMode, ref SECURITY_ATTRIBUTES lpSecurityAttributes, CreationDisposition dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);
        #endregion

       
        #region Read File
        /// <summary>
        /// 从指定的文件或输入/输出（I/O）设备读取数据。如果设备支持，则在文件指针指定的位置进行读取。 此功能设计用于同步和异步操作。有关专门为异步操作设计的类似功能，请参见 <see cref="ReadFileEx"/>
        /// <para>参考：https://docs.microsoft.com/zh-cn/windows/win32/api/fileapi/nf-fileapi-readfile </para>
        /// </summary>
        /// <param name="hFile">设备的句柄（例如，文件，文件流，物理磁盘，卷，控制台缓冲区，磁带驱动器，套接字，通信资源，邮槽或管道）。该参数必须已经具有读取权限创建。</param>
        /// <param name="lpBuffer">指向缓冲区的指针，该缓冲区接收从文件或设备读取的数据。该缓冲区必须在读取操作期间保持有效。在读取操作完成之前，调用者不得使用此缓冲区。</param>
        /// <param name="nNumberOfBytesToRead">要读取的最大字节数。</param>
        /// <param name="lpNumberOfBytesRead">指向变量的指针，该变量接收使用同步 hFile 参数时读取的字节数。在执行任何工作或错误检查之前，ReadFile 将此值设置为零。如果这是异步操作，请对该参数使用 NULL 以避免潜在的错误结果。仅当 lpOverlapped 参数不为 NULL 时，此参数才能为 NULL。</param>
        /// <param name="lpOverlapped">如果 hFILE 参数是使用 <see cref="FileFlags.FILE_FLAG_OVERLAPPED"/> 打开的， 则需要指向 <see cref="OVERLAPPED"/> 结构的指针，否则可以为 NULL。
        ///     <para>如果使用 <see cref="FileFlags.FILE_FLAG_OVERLAPPED"/> 打开 hFile，则 lpOverlapped 参数必须指向有效且唯一的 <see cref="OVERLAPPED"/> 结构，否则该函数可能会错误地报告读取操作已完成。</para>
        ///     <para>对于支持字节偏移量的 hFile，如果使用此参数，则必须指定一个字节偏移量，从该位置开始从文件或设备读取。通过设置 <see cref="OVERLAPPED"/> 结构的 Offset 和 OffsetHigh 成员 来指定此偏移量 。对于不支持字节偏移量的 hFile，将忽略 Offset 和 OffsetHigh。</para>
        /// </param>
        /// <returns>如果函数成功，则返回值为非零（TRUE）。如果函数失败或异步完成，则返回值为零（FALSE）。若要获取扩展的错误信息，请调用 <see cref="Marshal.GetLastWin32Error"/> 或 <see cref="Marshal.GetHRForLastWin32Error"/> 函数。</returns>
        [DllImport("kernel32.dll", SetLastError = true)]
        [return:MarshalAs(UnmanagedType.Bool)]
        public static extern bool ReadFile(IntPtr hFile, byte[] lpBuffer, uint nNumberOfBytesToRead, ref uint lpNumberOfBytesRead, IntPtr lpOverlapped);
        #endregion


        #region Write File
#if 没必要的
        public static extern void WriteFile();
#endif
        #endregion


        #region Close And Delete File
        /// <summary>
        /// 关闭打开的对象句柄。
        /// <para>如果应用程序在调试器下运行，则该函数将收到无效的句柄值或伪句柄值，否则将引发异常。如果您两次关闭一个句柄，或者在 <see cref="FindFirstFile"/> 函数返回的句柄上 调用 CloseHandle 而不是调用 FindClose 函数，则会发生这种情况。</para>
        /// <para>参考：https://docs.microsoft.com/en-us/windows/win32/api/handleapi/nf-handleapi-closehandle </para>
        /// </summary>
        /// <param name="hObject">打开对象的有效句柄。</param>
        /// <returns>如果函数成功，则返回值为非零。如果函数失败，则返回值为零。要获取扩展的错误信息，请调用 <see cref="Marshal.GetLastWin32Error"/> 或 <see cref="Marshal.GetHRForLastWin32Error"/>。</returns>
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool CloseHandle(IntPtr hObject);

        /// <summary>
        /// 删除现有文件。
        /// <para>若要将此操作作为事务处理操作执行，请使用 <see cref="DeleteFileTransacted"/> 函数。</para>
        /// <para>参考：https://docs.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-deletefile </para>
        /// </summary>
        /// <param name="lpFileName">要删除的文件名。在此函数的 ANSI 版本中，名称限制为 <see cref="SpaceCG.WindowsAPI.WinDef.MAX_PATH"/> 个字符。若要将此限制扩展为 32,767 个宽字符，请调用该函数的 Unicode 版本并在名称前加上 "\?" 到路径。</param>
        /// <returns></returns>
        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern bool DeleteFile(string lpFileName);
        #endregion
    }


    /// <summary>
    /// WindowsAPI Kernel32库，扩展常用/通用，功能/函数，扩展示例，以及使用方式ad
    /// </summary>
    public static partial class Kernel32Extensions
    {
    }

}
