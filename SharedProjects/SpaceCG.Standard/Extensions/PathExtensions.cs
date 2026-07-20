using System;
using System.IO;
using System.Runtime.CompilerServices;
using Trace = SpaceCG.Diagnostics.Trace;

namespace SpaceCG.Extensions
{
    /// <summary>
    /// 提供 <see cref="Path"/> 的扩展方法。
    /// <para>旨在为 .NET Framework 4.8 及早期版本提供现代 .NET 中 <see cref="System.IO.Path"/> 缺失的等效功能。</para>
    /// </summary>
    /// <remarks>本类的所有方法均为无状态纯函数，线程安全。</remarks>
    public static partial class PathExtensions
    {
        /// <summary>
        /// 返回一个值，该值指示指定的路径字符串是否以目录分隔符结尾。
        /// </summary>
        /// <param name="path">要检查的路径。</param>
        /// <returns>如果路径字符串以目录分隔符（<see cref="Path.DirectorySeparatorChar"/> 或 <see cref="Path.AltDirectorySeparatorChar"/>）结尾，则为 <c>true</c>；否则为 <c>false</c>。</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool EndsInDirectorySeparator(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;

            char lastChar = path[path.Length - 1];
            return lastChar == Path.DirectorySeparatorChar || lastChar == Path.AltDirectorySeparatorChar;
        }

        /// <summary>
        /// 从指定的路径中移除尾随的目录分隔符。
        /// </summary>
        /// <param name="path">要移除尾随分隔符的路径。</param>
        /// <returns>不包含任何尾随目录分隔符的路径。如果 <paramref name="path"/> 为 <c>null</c> 或空，则返回原值。</returns>
        /// <remarks>
        /// <para>此方法会移除路径末尾的<em>所有</em>连续目录分隔符，而不仅仅是最后一个。</para>
        /// <para>如果路径表示根目录（例如 <c>"C:\"</c> 或 <c>"\\server\share\"</c>），则不会移除其尾随的分隔符，以保持路径的有效性。</para>
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static string TrimEndingDirectorySeparator(string path)
        {
            if (string.IsNullOrEmpty(path)) return path;

            if (!EndsInDirectorySeparator(path)) return path;

            if (IsRootPath(path)) return path;

            return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        /// <summary>
        /// 返回一个值，该值指示指定的路径是否表示根目录。
        /// </summary>
        /// <param name="path">要检查的路径。</param>
        /// <returns>如果 <paramref name="path"/> 表示根目录（例如 <c>"C:\"</c>、<c>"/"</c> 或 <c>"\\server\share\"</c>），则为 <c>true</c>；否则为 <c>false</c>。</returns>
        /// <remarks>
        /// <para>此方法为纯字符串操作，不访问文件系统，因此执行速度极快且不会抛出 I/O 或权限相关的异常。</para>
        /// <para>符合 .NET 谓词方法惯例：对 <c>null</c> 或空字符串输入静默返回 <c>false</c>。</para>
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsRootPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;

            string root = Path.GetPathRoot(path);
            if (string.IsNullOrEmpty(root)) return false;

            // 移除尾部可能存在的多余分隔符后进行不区分大小写的比较
            // 例如："C:\" 和 "C:" 比较，或 "\\server\share\" 和 "\\server\share" 比较
            string trimmedPath = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string trimmedRoot = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            return string.Equals(trimmedPath, trimmedRoot, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 返回从一个路径到另一个路径的相对路径。
        /// </summary>
        /// <param name="relativeTo">结果所相对于的源路径。此路径始终被视为目录。</param>
        /// <param name="path">目标路径。</param>
        /// <returns>相对路径。如果路径不共享相同的根（例如不同的驱动器盘符或不同的 UNC 服务器），则返回 <paramref name="path"/> 的绝对形式。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="relativeTo"/> 或 <paramref name="path"/> 为 <c>null</c>。</exception>
        /// <exception cref="ArgumentException">路径包含无效字符，或无法解析为有效路径。</exception>
        /// <remarks>
        /// <para>此实现基于 <see cref="Uri"/> 机制。因此，如果路径中包含 <c>#</c> 或 <c>?</c> 等字符，它们可能会被误解析为 URI 的片段或查询分隔符。调用方应避免在此类路径上使用此方法，或事先对其进行转义。</para>
        /// </remarks>
        public static string GetRelativePath(string relativeTo, string path)
        {
            if (path == null) throw new ArgumentNullException(nameof(path));
            if (relativeTo == null) throw new ArgumentNullException(nameof(relativeTo));

            string relativeToFull = Path.GetFullPath(relativeTo);
            string pathFull = Path.GetFullPath(path);

            // 显式检查是否共享相同的逻辑根（盘符或 UNC 根）
            string rootRelativeTo = Path.GetPathRoot(relativeToFull);
            string rootPath = Path.GetPathRoot(pathFull);

            if (!string.Equals(rootRelativeTo, rootPath, StringComparison.OrdinalIgnoreCase))
            {
                // 跨盘符或跨 UNC 服务器，Uri 无法生成相对路径，直接返回绝对路径
                return pathFull;
            }

            relativeToFull = TrimEndingDirectorySeparator(relativeToFull) + Path.DirectorySeparatorChar;

            try
            {
                Uri fromUri = new Uri(relativeToFull, UriKind.Absolute);
                Uri toUri = new Uri(pathFull, UriKind.Absolute);

                Uri relativeUri = fromUri.MakeRelativeUri(toUri);
                string result = Uri.UnescapeDataString(relativeUri.ToString());

                // Windows 路径分隔符修正
                if (string.Equals(toUri.Scheme, Uri.UriSchemeFile, StringComparison.OrdinalIgnoreCase))
                {
                    result = result.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
                }

                return result;
            }
            catch (UriFormatException ex)
            {
                throw new ArgumentException("无法解析为有效路径。", ex);
            }
        }
    }
}
