using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Trace = SpaceCG.Diagnostics.Trace;

namespace SpaceCG.Extensions
{
    /// <summary>
    /// File Extensions
    /// </summary>
    public static partial class FileExtensions
    {
        /// <summary>
        /// 保留目录中的文件数量。
        /// <para>按文件创建日期降序排序后，仅保留最新的 <paramref name="count"/> 个文件，其余删除。</para>
        /// <para>注意：该函数比较的是文件创建日期（<see cref="FileSystemInfo.CreationTime"/>）。</para>
        /// </summary>
        /// <param name="dirPath">目标目录路径</param>
        /// <param name="count">保留的文件数量上限</param>
        /// <param name="searchPattern">
        /// 文件搜索模式（仅 TopDirectoryOnly），例如 "*.jpg"、"temp_*.png"；为 null 或空白时匹配所有文件
        /// </param>
        /// <exception cref="ArgumentNullException">dirPath 为 null</exception>
        /// <exception cref="ArgumentException">dirPath 无效</exception>
        public static void ReserveFileCount(string dirPath, int count, string searchPattern = null)
        {
            if (count <= 0 || string.IsNullOrWhiteSpace(dirPath) || !Directory.Exists(dirPath)) return;
            var dir = new DirectoryInfo(dirPath);

            if (string.IsNullOrWhiteSpace(searchPattern)) searchPattern = "*";
            var files = dir.GetFiles(searchPattern, SearchOption.TopDirectoryOnly);

            if (files.Length <= count) return;

            // 按文件创建时间降序排序（最新的在前），使用 SelectionSort 的思想：先取出 CreationTime 避免排序比较中重复访问文件系统属性
            // 注：虽然 FileInfo.CreationTime 有内部缓存，但直接访问属性仍可能触发 Refresh，提前缓存更安全
            var entries = new (FileInfo File, DateTime CreationTime)[files.Length];
            for (int i = 0; i < files.Length; i++)
            {
                entries[i] = (files[i], files[i].CreationTime);
            }

            Array.Sort(entries, (a, b) => b.CreationTime.CompareTo(a.CreationTime));

            // 删除超出保留数量的文件，每个文件独立 try-catch，避免一个文件删除失败（如被占用、权限不足）导致后续文件无法删除
            for (int i = count; i < entries.Length; i++)
            {
                try
                {
                    entries[i].File.Delete();
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    // 调试/维护类操作，删除失败时静默跳过，不中断整体流程
                    Trace.TraceWarning($"[ReserveFileCount] 删除文件失败: {entries[i].File.FullName} — {ex.Message}");
                }
            }
        }
        /// <summary>
        /// 保留目录中最近 N 天内的文件。
        /// <para>以文件的上次修改时间（<see cref="FileSystemInfo.LastWriteTime"/>）为基准，删除超过 <paramref name="days"/> 天的旧文件。</para>
        /// <para>注意：该函数比较的是文件的上次修改日期。</para>
        /// </summary>
        /// <param name="dirPath">目标目录路径</param>
        /// <param name="days">保留的天数，必须 &gt;= 0；为 0 时删除所有文件</param>
        /// <param name="searchPattern">
        /// 文件搜索模式（仅 TopDirectoryOnly），例如 "*.jpg"、"temp_*.png"；为 null 或空白时匹配所有文件
        /// </param>
        /// <exception cref="ArgumentNullException">dirPath 为 null</exception>
        /// <exception cref="ArgumentException">dirPath 无效</exception>
        public static void ReserveFileDays(string dirPath, int days, string searchPattern = null)
        {
            if (days < 0 || string.IsNullOrWhiteSpace(dirPath) || !Directory.Exists(dirPath)) return;

            var dir = new DirectoryInfo(dirPath);

            if (string.IsNullOrWhiteSpace(searchPattern)) searchPattern = "*";
            var files = dir.GetFiles(searchPattern, SearchOption.TopDirectoryOnly);

            if (files.Length == 0) return;

            var threshold = DateTime.Today.AddDays(-days);

            foreach (var file in files)
            {
                if (file.LastWriteTime < threshold)
                {
                    try
                    {
                        file.Delete();
                    }
                    catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                    {
                        Trace.TraceWarning($"[ReserveFileDays] 删除文件失败: {file.FullName} — {ex.Message}");
                    }
                }
            }
        }

        /// <summary>
        /// 在指定目录中获取文件名匹配正则表达式的文件，按创建时间降序排序。
        /// <para>仅搜索顶层目录，不包括子目录。</para>
        /// </summary>
        /// <param name="directory">要搜索的目录路径</param>
        /// <param name="pattern">文件名匹配的正则表达式</param>
        /// <returns>匹配的文件信息集合；目录无效时返回空集合</returns>
        /// <exception cref="ArgumentNullException">pattern 为 null</exception>
        public static IEnumerable<FileInfo> GetPatternFiles(string directory, Regex pattern)
        {
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
                return Array.Empty<FileInfo>();
            if (pattern == null) throw new ArgumentNullException(nameof(pattern));

            return from fileName in Directory.GetFiles(directory, "*.*", SearchOption.TopDirectoryOnly)
                   let fileInfo = new FileInfo(fileName)
                   where pattern.IsMatch(fileInfo.Name)
                   orderby fileInfo.CreationTime descending
                   select fileInfo;
        }
        /// <summary>
        /// 在指定目录中获取匹配指定扩展名的文件，按创建时间降序排序。
        /// <para>仅搜索顶层目录，不包括子目录。扩展名比较忽略大小写。</para>
        /// </summary>
        /// <param name="directory">要搜索的目录路径</param>
        /// <param name="extensions">文件扩展名集合，例如 ".mp4"、".jpg"、".png"（含点号）</param>
        /// <returns>匹配的文件信息集合；目录无效或扩展名为空时返回空集合</returns>
        public static IEnumerable<FileInfo> GetPatternFiles(string directory, IEnumerable<string> extensions)
        {
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory) || extensions == null || !extensions.Any())
                return Array.Empty<FileInfo>();

            // 将 IEnumerable<string> 转为 HashSet 以 O(1) 查找，避免 LINQ Contains 对每个文件执行 O(n) 线性扫描
            var extensionSet = new HashSet<string>(extensions, StringComparer.OrdinalIgnoreCase);

            var files = from fileName in Directory.GetFiles(directory, "*.*", SearchOption.TopDirectoryOnly)
                        let fileInfo = new FileInfo(fileName)
                        where extensionSet.Contains(fileInfo.Extension)
                        orderby fileInfo.CreationTime descending
                        select fileInfo;

            return files;
        }

        /// <summary> 空字典  </summary>
        internal static readonly IReadOnlyDictionary<int, IEnumerable<FileInfo>> EmptyDictionary = new ReadOnlyDictionary<int, IEnumerable<FileInfo>>(new Dictionary<int, IEnumerable<FileInfo>>());
        /// <summary>
        /// 在 <paramref name="directory"/> 目录中获取匹配 <paramref name="extensions"/> 文件类型的系列文件，并按文件名中第一组数字分组。
        /// <para>使用文件名中第一组数字作为 Key ，按文件创建时间排序的文件集合作为 Value。</para>
        /// </summary>
        /// <param name="directory"></param>
        /// <param name="extensions">文件类型，例如：.mp4、.jpg、.png、.lnk</param>
        /// <returns>Key:文件名中第一组数字，Value:按文件创建时间排序的文件集合</returns>
        public static IReadOnlyDictionary<int, IEnumerable<FileInfo>> GetSeriesFiles(string directory, IEnumerable<string> extensions)
        {
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory) || extensions == null || !extensions.Any()) return EmptyDictionary;

            var regex = new Regex(@"\d{1,9}");
            var extensionSet = new HashSet<string>(extensions, StringComparer.OrdinalIgnoreCase);

            var files = (from fileName in Directory.GetFiles(directory, "*.*", SearchOption.TopDirectoryOnly)
                         let fileInfo = new FileInfo(fileName)
                         where extensionSet.Contains(fileInfo.Extension)
                         let match = regex.Match(Path.GetFileNameWithoutExtension(fileInfo.Name))
                         where match.Success && int.TryParse(match.Value, out _)
                         let index = int.Parse(match.Value)
                         group fileInfo by index into g
                         select g)
                        .ToDictionary(g => g.Key, g => g.OrderByDescending(f => f.CreationTime).AsEnumerable());

            return files;
        }
    }
}
