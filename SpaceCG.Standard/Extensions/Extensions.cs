using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SpaceCG.Extensions
{
    /// <summary>
    /// 扩展方法
    /// </summary>
    public static partial class Extensions
    {
        /// <summary>
        /// 将字节序列转换为格式化的十六进制字符串，用于测试输出。
        /// <para>输出格式：每行最多8个字节，格式受 <paramref name="format"/> 控制。</para>
        /// </summary>
        /// <param name="bytes">字节序列</param>
        /// <param name="count">最大输出字节数，默认值 64</param>
        /// <param name="format">每字节的格式化字符串，使用 {0} 占位，默认 "0x{0:X2}"</param>
        /// <returns>格式化的十六进制字符串。空输入返回 <see cref="string.Empty"/>。</returns>
        public static string ToHexString(this IEnumerable<byte> bytes, int count = 64, string format = "0x{0:X2}")
        {
            if (bytes == null || !bytes.Any() || count <= 0) return string.Empty;

            IList<byte> list;
            if (bytes is byte[] array)
            {
                list = array;
            }
            else if (bytes is IList<byte> ilist)
            {
                list = ilist;
            }
            else
            {
                list = bytes.ToArray();
            }

            var length = Math.Min(count, list.Count);
            var builder = new StringBuilder(count * 8);

            for (int i = 0; i < length; i += 8)
            {
                var lineEnd = Math.Min(i + 8, length);
                for (int j = i; j < lineEnd; j++)
                {
                    if (j > i) builder.Append(' ');
                    builder.AppendFormat(format, list[j]);
                }

                builder.AppendLine();
            }

            return builder.ToString().TrimEnd();
        }

        /// <summary>
        /// 在字节序列中查找指定模式首次出现的位置。
        /// <para>采用头尾双过滤策略：先同时比对首尾字节，只有两者都命中才进入内循环逐字节比对。
        /// 随机数据下误入内循环概率约 1/65536，适合实时数据流分析场景。</para>
        /// <para>时间复杂度：平均 O(n)；空间复杂度 O(1)。</para>
        /// </summary>
        /// <param name="source">源字节序列</param>
        /// <param name="pattern">待查找的字节模式</param>
        /// <param name="index">搜索起始索引，默认为 0</param>
        /// <param name="count">要搜索的元素数量。-1 表示搜索到 source 末尾</param>
        /// <returns>找到时返回首次出现的起始索引；未找到或参数无效返回 -1。</returns>
        public static int IndexOf(this IReadOnlyList<byte> source, IReadOnlyList<byte> pattern, int index = 0, int count = -1)
        {
            if (source == null || pattern == null) return -1;

            var sCount = source.Count;
            var pCount = pattern.Count;

            // 边界检查：空模式、模式过长、index 越界
            if (pCount == 0 || pCount > sCount || index < 0 || index >= sCount) return -1;

            // 计算有效搜索上界：若 count=-1 则搜索到 source 末尾，否则取 index+count
            var searchEnd = count < 0 ? sCount : index + count;
            if (searchEnd > sCount) searchEnd = sCount;       // 截断到 source 长度
            if (searchEnd <= index) return -1;                // 无有效搜索范围

            var limit = searchEnd - pCount;     
            if (index > limit) return -1;

            var head = pattern[0];              // 首字节（缓存成员访问）

            // 单字节模式
            if (pCount == 1)
            {
                for (int i = index; i <= limit; i++)
                {
                    if (source[i] == head) return i;
                }
                return -1;
            }

            var pLast = pCount - 1;             // 尾部索引
            var tail = pattern[pLast];          // 尾字节（缓存成员访问）

            // 双字节模式，直接比对两个字节
            if (pCount == 2)
            {
                for (int i = index; i <= limit; i++)
                {
                    if (source[i] == head && source[i + 1] == tail) return i;
                }
                return -1;
            }

            // 头尾双过滤 + 中间逐字节比对
            // 策略：先同时检查首尾两个字节
            // 随机数据下同时命中的概率 ≈ 1/65536，第一步几乎全部排除
            // 若首尾都命中，再从头到中间逐字节确认（尾部已验过，跳过）
            for (int i = index; i <= limit; i++)
            {
                // 头尾同时比对（两次比较，短路求值：头不命中直接跳过尾比较）
                if (source[i] != head || source[i + pLast] != tail) continue;

                // 逐字节比对中间部分，发现不匹配立即 break
                int j = 1;
                for (j = 1; j < pLast; j++)
                {
                    if (source[i + j] != pattern[j]) break;
                }

                if (j == pLast) return i;
            }

            return -1;
        }
    }
}
