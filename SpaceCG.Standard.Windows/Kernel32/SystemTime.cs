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
    #endregion


    #region Structures
    /// <summary>
    /// 系统时间类
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public sealed class SystemTime
    {
        public ushort Year;
        public ushort Month;
        public ushort DayOfWeek;
        public ushort Day;
        public ushort Hour;
        public ushort Minute;
        public ushort Second;
        public ushort Milliseconds;

        /// <summary>
        /// 隐式转换类型 publi static implicit operator Construct()
        /// </summary>
        /// <param name="year"></param>
        public static implicit operator SystemTime(ushort year)
        {
            return new SystemTime() { Year = year };
        }

        /// <summary>
        /// 隐式转换类型
        /// </summary>
        /// <param name="st"></param>
        public static implicit operator SystemTime(SYSTEMTIME st)
        {
            return new SystemTime()
            {
                Year = st.Year,
                Month = st.Month,
                DayOfWeek = st.DayOfWeek,
                Day = st.Day,
                Hour = st.Hour,
                Minute = st.Minute,
                Second = st.Second,
                Milliseconds = st.Milliseconds,
            };
        }


        public override string ToString()
        {
            return $"SystemTime {Year} {Month} {Day} {DayOfWeek} {Hour} {Minute} {Second} {Milliseconds}";
        }
    }

    /// <summary>
    /// 系统时间结构
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct SYSTEMTIME
    {
        public ushort Year;
        public ushort Month;
        public ushort DayOfWeek;
        public ushort Day;
        public ushort Hour;
        public ushort Minute;
        public ushort Second;
        public ushort Milliseconds;

        /// <summary>
        /// 隐式转换类型
        /// </summary>
        /// <param name="st"></param>
        public static implicit operator SYSTEMTIME(SystemTime st)
        {
            return new SYSTEMTIME()
            {
                Year = st.Year,
                Month = st.Month,
                DayOfWeek = st.DayOfWeek,
                Day = st.Day,
                Hour = st.Hour,
                Minute = st.Minute,
                Second = st.Second,
                Milliseconds = st.Milliseconds,
            };
        }

        public override string ToString()
        {
            return $"SYSTEMTIME {Year} {Month} {Day} {DayOfWeek} {Hour} {Minute} {Second} {Milliseconds}";
        }
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
        /// <summary>
        /// 获取系统时间
        /// <para>这只是一个示例，传参类型为 Class 引用</para>
        /// <para> <see cref="SystemTime"/> 与 <see cref="SYSTEMTIME"/> 隐式转换 </para>
        /// </summary>
        /// <param name="systemTime"></param>
        [DllImport("kernel32.dll")]
        public static extern void GetSystemTime(SystemTime systemTime);

        /// <summary>
        /// 获取系统时间
        /// <para>这只是一个示例，传参类型为 Struct 按值传，引用传需加 ref </para>
        /// <para> <see cref="SystemTime"/> 与 <see cref="SYSTEMTIME"/> 隐式转换 </para>
        /// </summary>
        /// <param name="systemTime"></param>
        [DllImport("kernel32.dll")]
        public static extern void GetSystemTime(ref SYSTEMTIME systemTime);
    }


    /// <summary>
    /// WindowsAPI Kernel32库，扩展常用/通用，功能/函数，扩展示例，以及使用方式ad
    /// </summary>
    public static partial class Kernel32Extensions
    {
    }

}
