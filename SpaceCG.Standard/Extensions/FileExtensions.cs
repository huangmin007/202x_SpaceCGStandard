using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace SpaceCG.Extensions
{
    /// <summary>
    /// File Extensions
    /// </summary>
    public static partial class FileExtensions
    {
        /// <summary>
        /// 保留目录中的文件数量
        /// <para>跟据文件创建日期排序，保留 count 个最新文件，超出 count 数量的文件删除</para>
        /// <para>注意：该函数是比较文件的创建日期</para>
        /// </summary>
        /// <param name="dirPath"></param>
        /// <param name="count"></param>
        /// <param name="searchPattern">只在目录中(不包括子目录)，查找匹配的文件；例如："*.jpg" 或 "temp_*.png"</param>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="ArgumentException"></exception>
        public static void ReserveFileCount(string dirPath, int count, string searchPattern = null)
        {
            if (count <= 0 || string.IsNullOrWhiteSpace(dirPath) || !Directory.Exists(dirPath)) return;

            DirectoryInfo dir = new DirectoryInfo(dirPath);

            if (string.IsNullOrWhiteSpace(searchPattern)) searchPattern = "*";
            FileInfo[] files = dir.GetFiles(searchPattern, SearchOption.TopDirectoryOnly);

            if (files.Length <= count) return;

            //按文件的创建时间，降序排序(最新创建的排在最前面)
            Array.Sort(files, (f1, f2) =>
            {
                return f2.CreationTime.CompareTo(f1.CreationTime);
            });

            for (int i = count; i < files.Length; i++)
            {
                files[i].Delete();
            }
        }
        /// <summary>
        /// 保留目录中的文件天数
        /// <para>跟据文件上次修时间起计算，保留 days 天的文件，超出 days 天的文件删除</para>
        /// <para>注意：该函数是比较文件的上次修改日期</para>
        /// </summary>
        /// <param name="dirPath"></param>
        /// <param name="days"></param>
        /// <param name="searchPattern">文件匹配类型, 只在目录中(不包括子目录)，查找匹配的文件；例如："*.jpg" 或 "temp_*.png"</param>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="ArgumentException"></exception>
        public static void ReserveFileDays(string dirPath, int days, string searchPattern = null)
        {
            if (days < 0 || string.IsNullOrWhiteSpace(dirPath) || !Directory.Exists(dirPath)) return;

            DirectoryInfo dir = new DirectoryInfo(dirPath);

            if (string.IsNullOrWhiteSpace(searchPattern)) searchPattern = "*";
            FileInfo[] files = dir.GetFiles(searchPattern, SearchOption.TopDirectoryOnly);

            if (files.Length == 0) return;

            IEnumerable<FileInfo> removes =
                  from file in files
                  where file.LastWriteTime < DateTime.Today.AddDays(-days)
                  select file;

            foreach (var file in removes)
            {
                file.Delete();
            }
        }

        /// <summary>
        /// 在 <paramref name="directory"/> 目录中获取匹配 <paramref name="pattern"/> 正则表达式的文件，并按文件的创建时间排序。
        /// <para>注意不包括子目录</para>
        /// </summary>
        /// <param name="directory">要匹配的目录</param>
        /// <param name="pattern">文件名的匹配规则（正则表达式）</param>
        /// <returns></returns>
        public static IEnumerable<FileInfo> GetPatternFiles(string directory, Regex pattern)
        {
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
                return Array.Empty<FileInfo>();
            if (pattern == null) throw new ArgumentNullException(nameof(pattern));

            return from fileName in Directory.GetFiles(directory, "*.*", SearchOption.TopDirectoryOnly)
                   let fileInfo = new FileInfo(fileName)
                   where pattern != null ? pattern.IsMatch(fileInfo.Name) : true
                   orderby fileInfo.CreationTime descending
                   select fileInfo;
        }
        /// <summary>
        /// 在 <paramref name="directory"/> 目录中获取匹配 <paramref name="extensions"/> 文件类型的文件，并按文件的创建时间排序
        /// </summary>
        /// <param name="directory"></param>
        /// <param name="extensions">文件类型，例如：.mp4、.jpg、.png、.lnk</param>
        public static IEnumerable<FileInfo> GetPatternFiles(string directory, IEnumerable<string> extensions)
        {
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory) || extensions == null || !extensions.Any())
                return Array.Empty<FileInfo>();

            var files = from fileName in Directory.GetFiles(directory, "*.*", SearchOption.TopDirectoryOnly)
                        let fileInfo = new FileInfo(fileName)
                        where extensions.Contains(fileInfo.Extension, StringComparer.OrdinalIgnoreCase)
                        orderby fileInfo.CreationTime descending
                        select fileInfo;

            return files;
        }

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
            var files = (from fileName in Directory.GetFiles(directory, "*.*", SearchOption.TopDirectoryOnly)
                         let fileInfo = new FileInfo(fileName)
                         where extensions.Contains(fileInfo.Extension, StringComparer.OrdinalIgnoreCase)
                         let match = regex.Match(Path.GetFileNameWithoutExtension(fileInfo.Name))
                         where match.Success && int.TryParse(match.Value, out int number)
                         let index = int.Parse(match.Value)
                         group fileInfo by index into g
                         select g)
                        .ToDictionary(g => g.Key, g => g.OrderByDescending(f => f.CreationTime).AsEnumerable());

            return files;
        }
    }
}
