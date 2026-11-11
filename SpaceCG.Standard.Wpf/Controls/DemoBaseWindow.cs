using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Threading;
using SpaceCG.Extensions;
using SpaceCG.Net;

namespace SpaceCG.Controls
{
    /// <summary>
    /// Demo 基类窗口。
    /// </summary>
    public abstract partial class DemoBaseWindow : Window, INotifyPropertyChanged
    {
        /// <summary>  默认的资源根目录。 </summary>
        public const string RootResources = "Resources";
        protected readonly string DefaultItemTagsFile = $"{RootResources}/ItemTags.txt";

        private RpcServer4X rpcServer;
        /// <summary> 
        /// RPC 服务端，监听的本地端口号。端口小于 1024 则不启用 RPC 服务。
        /// <para>注意：需要在窗体 Loaded 完成之前设置有效。</para>
        /// </summary>
        public int LocalPort { get; set; } = 2000;

        private long lastIdleTimeMilliseconds = 0;
        /// <summary> 进入屏保模式 (<see cref="HomeItem"/>) 时的空闲计时时间，单位：秒。当值小于等于 0 时，会暂停 (<see cref="HomeItem"/>) 计时。 </summary>
#if DEBUG
        public int IdleTime { get; set; } = 30;
#else
        public int IdleTime { get; set; } = 600;
#endif
        private DispatcherTimer dispatcherTimer;

        /// <summary> 上一次操作的时间，单位：毫秒。 </summary>
        private long lastOperateTickCount = int.MinValue;
        /// <summary> 操作间隔的最小时间，单位：毫秒。 </summary>
        private readonly int OperateIntervalTickCount = 500;
        private readonly Stopwatch OperateStopwatch = new Stopwatch();

        /// <summary> ItemTags 关系字典；Key 为上一级资源 Tag, Value 为对应 Key 的子级资源 Tags 集合。 </summary>
        protected IReadOnlyDictionary<int, IEnumerable<int>> ItemTagsDictionary { get; private set; }
        /// <summary> 当前资源文件夹的文件信息。</summary>
        protected IReadOnlyDictionary<int, IEnumerable<FileInfo>> FileResources { get; private set; }
        /// <summary> 资源文件夹中支持的文件扩展名。</summary>
        protected List<string> SupportFileExtensions { get; private set; } = new List<string> { ".mp4", ".mov", ".m4v", ".flv", ".png", ".jpg", ".bmp", ".jpeg" };

        #region Dependency Properties
        private static readonly DependencyPropertyKey ItemTagPropertyKey = DependencyProperty.RegisterReadOnly(nameof(ItemTag), typeof(int), typeof(DemoBaseWindow), new PropertyMetadata(-1));
        /// <summary> 标识 <see cref="ItemTag"/> 依赖属性。  </summary>
        public static readonly DependencyProperty ItemTagProperty = ItemTagPropertyKey.DependencyProperty;
        /// <summary> 当前资源的 Tag 值。 </summary>
        public int ItemTag
        {
            get => (int)GetValue(ItemTagPropertyKey.DependencyProperty);
            private set => SetValue(ItemTagPropertyKey, value);
        }

        private static readonly DependencyPropertyKey HomeItemTagPropertyKey = DependencyProperty.RegisterReadOnly(nameof(HomeItemTag), typeof(int), typeof(DemoBaseWindow), new PropertyMetadata(-1));
        /// <summary> 标识 <see cref="HomeItemTag"/> 依赖属性。  </summary>
        public static readonly DependencyProperty HomeItemTagProperty = HomeItemTagPropertyKey.DependencyProperty;
        /// <summary> 当前资源的 HomeItemTag 值。 </summary>
        public int HomeItemTag
        {
            get => (int)GetValue(HomeItemTagPropertyKey.DependencyProperty);
            private set => SetValue(HomeItemTagPropertyKey, value);
        }

        private static readonly DependencyPropertyKey HasNextItemPropertyKey = DependencyProperty.RegisterReadOnly(nameof(HasNextItem), typeof(bool), typeof(DemoBaseWindow), new PropertyMetadata(false));
        /// <summary> 标识 <see cref="HasNextItem"/> 依赖属性。  </summary>
        public static readonly DependencyProperty HasNextItemProperty = HasNextItemPropertyKey.DependencyProperty;
        /// <summary> 是否存在下一项资源。 </summary>
        public bool HasNextItem
        {
            get => (bool)GetValue(HasNextItemPropertyKey.DependencyProperty);
            private set => SetValue(HasNextItemPropertyKey, value);
        }
        private static readonly DependencyPropertyKey NextItemTagPropertyKey = DependencyProperty.RegisterReadOnly(nameof(NextItemTag), typeof(int), typeof(DemoBaseWindow), new PropertyMetadata(-1));
        /// <summary> 标识 <see cref="NextItemTag"/> 依赖属性。  </summary>
        public static readonly DependencyProperty NextItemTagProperty = NextItemTagPropertyKey.DependencyProperty;
        /// <summary> 下一资源的 Tag 值。 </summary>
        public int NextItemTag
        {
            get => (int)GetValue(NextItemTagPropertyKey.DependencyProperty);
            private set => SetValue(NextItemTagPropertyKey, value);
        }

        private static readonly DependencyPropertyKey HasPrevItemPropertyKey = DependencyProperty.RegisterReadOnly(nameof(HasPrevItem), typeof(bool), typeof(DemoBaseWindow), new PropertyMetadata(false));
        /// <summary> 标识 <see cref="HasPrevItem"/> 依赖属性。  </summary>
        public static readonly DependencyProperty HasPrevItemProperty = HasPrevItemPropertyKey.DependencyProperty;
        /// <summary> 是否存在上一项资源。 </summary>
        public bool HasPrevItem
        {
            get => (bool)GetValue(HasPrevItemPropertyKey.DependencyProperty);
            private set => SetValue(HasPrevItemPropertyKey, value);
        }
        private static readonly DependencyPropertyKey PrevItemTagPropertyKey = DependencyProperty.RegisterReadOnly(nameof(PrevItemTag), typeof(int), typeof(DemoBaseWindow), new PropertyMetadata(-1));
        /// <summary> 标识 <see cref="PrevItemTag"/> 依赖属性。  </summary>
        public static readonly DependencyProperty PrevItemTagProperty = PrevItemTagPropertyKey.DependencyProperty;
        /// <summary> 上一资源的 Tag 值。 </summary>
        public int PrevItemTag
        {
            get => (int)GetValue(PrevItemTagPropertyKey.DependencyProperty);
            private set => SetValue(PrevItemTagPropertyKey, value);
        }

        private static readonly DependencyPropertyKey HasBackItemPropertyKey = DependencyProperty.RegisterReadOnly(nameof(HasBackItem), typeof(bool), typeof(DemoBaseWindow), new PropertyMetadata(false));
        /// <summary> 标识 <see cref="HasBackItem"/> 依赖属性。  </summary>
        public static readonly DependencyProperty HasBackItemProperty = HasBackItemPropertyKey.DependencyProperty;
        /// <summary> 是否存在后退项的资源。 </summary>
        public bool HasBackItem
        {
            get => (bool)GetValue(HasBackItemPropertyKey.DependencyProperty);
            private set => SetValue(HasBackItemPropertyKey, value);
        }
        private static readonly DependencyPropertyKey BackItemTagPropertyKey = DependencyProperty.RegisterReadOnly(nameof(BackItemTag), typeof(int), typeof(DemoBaseWindow), new PropertyMetadata(-1));
        /// <summary> 标识 <see cref="BackItemTag"/> 依赖属性。  </summary>
        public static readonly DependencyProperty BackItemTagProperty = BackItemTagPropertyKey.DependencyProperty;
        /// <summary> 返回资源的 Tag 值。 </summary>
        public int BackItemTag
        {
            get => (int)GetValue(BackItemTagPropertyKey.DependencyProperty);
            private set => SetValue(BackItemTagPropertyKey, value);
        }
        #endregion

        static DemoBaseWindow()
        {
            LanguageProperty.OverrideMetadata(typeof(DemoBaseWindow), new FrameworkPropertyMetadata(XmlLanguage.GetLanguage("zh-CN"), OnLanguageChanged));
        }
        private static void OnLanguageChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var window = d as DemoBaseWindow;
            if (window != null && e.OldValue != e.NewValue)
            {
                window.OnLanguageChanged(e);
            }
        }

        /// <summary>
        /// 构造函数。
        /// </summary>
        public DemoBaseWindow()
        {
            OperateStopwatch.Restart();
            this.Loaded += DemoBaseWindow_Loaded;
        }


        #region override methods        
        /// <inheritdoc /> 
        protected override void OnInitialized(EventArgs e)
        {
            base.OnInitialized(e);
            Trace.WriteLine("OnInitialized");
            this.InitializeStandard();

            this.ReadItemTags();
        }
        /// <inheritdoc />
        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            Trace.WriteLine("OnSourceInitialized");
        }
        /// <inheritdoc /> 
        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            ResetIdleTime();
            base.OnPreviewKeyDown(e);
        }
        /// <inheritdoc />
        protected override void OnPreviewMouseDown(MouseButtonEventArgs e)
        {
            ResetIdleTime();
            base.OnPreviewMouseDown(e);
        }
        /// <inheritdoc />
        protected override void OnClosing(CancelEventArgs e)
        {
            base.OnClosing(e);
            rpcServer?.Dispose();
            dispatcherTimer?.Stop();
        }
        /// <summary>
        /// 更新窗体标题
        /// </summary>
        /// <param name="title"></param>
        protected void UpdateWindowTitle(params string[] title)
        {
            this.Title = $"{string.Join("  -  ", title)}  - 【Ver: {Application.ResourceAssembly.GetName().Version}】";
        }
        /// <summary>
        /// 读取 ItemTags.txt 文件，更新 <see cref="ItemTagsDictionary"/> 对象。
        /// </summary>
        protected void ReadItemTags()
        {
            try
            {
                if (!File.Exists(DefaultItemTagsFile)) return;

                var colon = new char[] { ':' };
                var lines = File.ReadAllLines(DefaultItemTagsFile);
                var itemTagsDictionary = new Dictionary<int, IEnumerable<int>>();

                foreach (var line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    if (line.TrimStart().StartsWith("#") || line.TrimStart().StartsWith("//")) continue;

                    var parts = line.Split(colon, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length < 2) continue;

                    if (int.TryParse(parts[0], out var parentTag))
                    {
                        var childTags = WindowExtensions.ParseIntRanges(parts[1]);
                        if (childTags.Any()) itemTagsDictionary.Add(parentTag, childTags.ToList());
                    }
                }

                if (itemTagsDictionary.Any())
                    ItemTagsDictionary = itemTagsDictionary;
            }
            catch (Exception ex)
            {
                ItemTagsDictionary = null;
                Trace.TraceError($"读取 BackTags.txt 文件失败，错误信息：{ex.Message}");
            }
        }
        #endregion


        #region Properites Changed Event Handlers
        /// <summary>
        /// 【可重写】语言属性发生变化时触发的事件。
        /// </summary>
        /// <param name="e"></param>
        protected virtual void OnLanguageChanged(DependencyPropertyChangedEventArgs e)
        {
            if (e.OldValue == e.NewValue) return;
            Trace.TraceInformation($"On Language Changed: {this.Language}  ({e.OldValue} -> {e.NewValue})");

            UpdateFileResources();

            if (FileResources != null && FileResources.ContainsKey(ItemTag))
            {
                OnLoadItemHandler(FileResources[ItemTag], new DependencyPropertyChangedEventArgs(ItemTagProperty, ItemTag, ItemTag));
            }
        }
        /// <summary>
        /// 【可重写】当前资源 Tag 属性发生变化时触发的事件。
        /// </summary>
        /// <param name="e"></param>
        protected virtual void OnItemTagChanged(DependencyPropertyChangedEventArgs e)
        {
            if (e.OldValue == e.NewValue) return;
            if (FileResources == null || !FileResources.Any()) return;

            var oldItemTag = (int)e.OldValue;
            var newItemTag = (int)e.NewValue;
            var minItemTag = FileResources.Keys.Min();

            // 更新 HomeItemTag 属性
            HomeItemTag = minItemTag;

            // 更新 Next/Prev 属性
            // HasNextItem = FileResources.ContainsKey(newItemTag + 1);
            HasNextItem = newItemTag > 0 && FileResources.ContainsKey(newItemTag + 1);    // 排除了 0 号资源文件
            NextItemTag = HasNextItem ? newItemTag + 1 : -1;

            // HasPrevItem = FileResources.ContainsKey(newItemTag - 1);
            HasPrevItem = newItemTag - 1 > 0 && FileResources.ContainsKey(newItemTag - 1); // 排除了 0 号资源文件
            PrevItemTag = HasPrevItem ? newItemTag - 1 : -1;

            // 更新 Back 属性
            HasBackItem = ItemTagsDictionary != null && ItemTagsDictionary.Values.Any(childTags => childTags.Contains(newItemTag));
            BackItemTag = GetParentItemTag();
        }
        /// <summary>
        /// 【必须重写】当处理资源文件加载等相关的业务显示逻辑时调用。
        /// <para>语言切换、ItemTag 发生变化时，会触发此函数。</para>
        /// </summary>
        /// <param name="files"></param>
        /// <param name="e"></param>
        protected abstract void OnLoadItemHandler(IEnumerable<FileInfo> files, DependencyPropertyChangedEventArgs e);

        /// <summary>
        /// 获取上一级资源的 Tag 值。
        /// </summary>
        /// <returns></returns>
        protected int GetParentItemTag()
        {
            if (ItemTagsDictionary == null || !ItemTagsDictionary.Any()) return -1;

            foreach (var itemTags in ItemTagsDictionary)
            {
                if (itemTags.Value.Contains(ItemTag))
                {
                    return itemTags.Key;
                }
            }

            return -1;
        }
        /// <summary>
        /// 更新资源文件信息，重新读取资源文件信息，并更新 <see cref="FileResources"/> 对象。
        /// </summary>
        protected void UpdateFileResources()
        {
            if (!Directory.Exists(RootResources))
            {
                Trace.TraceError($"资源根目录 {RootResources} 不存在！");
                if (MessageBox.Show($"资源根目录 {RootResources} 不存在！\r\n是否需要退出程序？", "Error", MessageBoxButton.OKCancel, MessageBoxImage.Error) == MessageBoxResult.OK)
                {
                    Environment.Exit(0);
                }
                return;
            }

            var directory = $"{RootResources}/{Language}";
            if (!Directory.Exists(directory))
            {
                directory = RootResources;
            }

            Trace.TraceInformation($"从目录 {directory} 中加载资源文件。");
            var fileResources = FileExtensions.GetSeriesFiles(directory, SupportFileExtensions);
            if (fileResources == null || fileResources.Count == 0)
            {
                Trace.TraceError($"资源文件为空，请检查目录 {directory} 下是否存在对应的资源文件。");
                if (MessageBox.Show($"资源文件为空，请检查目录 {directory} 下是否存在对应的资源文件。\r\n是否需要退出程序？", "Error", MessageBoxButton.OKCancel, MessageBoxImage.Error) == MessageBoxResult.OK)
                {
                    Environment.Exit(0);
                }
                return;
            }

            FileResources = fileResources;
        }
        #endregion


        /// <summary>
        /// 窗口加载完成时触发的事件。
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void DemoBaseWindow_Loaded(object sender, RoutedEventArgs e)
        {
            Trace.WriteLine("Demo Base Window Loaded.");

            this.Loaded -= DemoBaseWindow_Loaded;
            OnLanguageChanged(new DependencyPropertyChangedEventArgs(LanguageProperty, null, this.Language));

            if (LocalPort > 1024 && LocalPort < 65535)
            {
                try
                {
                    rpcServer = new RpcServer4X(LocalPort);
                    rpcServer.RegisterObject("Demo", this);
                    rpcServer.ClientInvokeRequest += (_, __) => ResetIdleTime();
                    rpcServer.Start();
                }
                catch (Exception ex)
                {
                    Trace.TraceError($"Failed to start RPC Server: {ex.Message}");
                    MessageBox.Show(this, $"Failed to start RPC Server: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }

            HomeItem();

            dispatcherTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(40), DispatcherPriority.Normal, OnDispatcherTimerCallback, this.Dispatcher);
            dispatcherTimer.Start();
        }


        #region Public 接口函数
        /// <summary>
        /// 【接口函数】加载指定 Tag 的资源文件。
        /// </summary>
        /// <param name="tag"></param>
        public void LoadItem(int tag)
        {
            Trace.WriteLine($"Load Item: {tag}/{Language}");

            if (tag < 0 || tag == ItemTag) return;
            if (FileResources == null || !FileResources.Any() || !FileResources.ContainsKey(tag)) return;

            if (OperateStopwatch.ElapsedMilliseconds - lastOperateTickCount < OperateIntervalTickCount)
            {
                Trace.TraceWarning($"资源操作过于频繁，请稍后再试！");
                return;
            }

            var oldItemTag = ItemTag;
            ItemTag = tag;
            var newItemTag = ItemTag;

            var changedEventArgs = new DependencyPropertyChangedEventArgs(ItemTagProperty, oldItemTag, newItemTag);
            OnItemTagChanged(changedEventArgs);

            // 加载资源文件处理
            OnLoadItemHandler(FileResources[newItemTag], changedEventArgs);

            lastOperateTickCount = OperateStopwatch.ElapsedMilliseconds;
            Trace.TraceInformation($"On ItemTag Changed: {ItemTag}/{Language}  NextItem:{HasNextItem}/{NextItemTag}  PrevItem:{HasPrevItem}/{PrevItemTag}  BackItem:{HasBackItem}/{BackItemTag}");
        }
        /// <summary>
        /// 【接口函数】加载第一个资源文件。
        /// </summary>
        public void HomeItem()
        {
            if (FileResources != null && FileResources.Any())
            {
                LoadItem(FileResources.Keys.Min());
            }
        }
        /// <summary>
        /// 【接口函数】加载下一个资源文件。
        /// </summary>
        public void NextItem()
        {
            if (HasNextItem)
            {
                LoadItem(NextItemTag);
            }
        }
        /// <summary>
        /// 【接口函数】加载上一个资源文件。
        /// </summary>
        public void PrevItem()
        {
            if (HasPrevItem)
            {
                LoadItem(PrevItemTag);
            }
        }
        /// <summary>
        /// 【接口函数】返回上一级。
        /// </summary>
        public void BackItem()
        {
            if (HasBackItem)
            {
                LoadItem(BackItemTag);
            }
        }
        /// <summary>
        /// 【接口函数】【可重写】播放/暂停。
        /// <para>如果资源文件不支持播放/暂停，则可能不会做任何事情。</para>
        /// </summary>
        public virtual void PlayPause() { }
        #endregion


        #region Dispatcher Timer        
        private void OnDispatcherTimerCallback(object sender, EventArgs e)
        {
            var oms = OperateStopwatch.ElapsedMilliseconds;
            OnDispatcherTimerCallback(oms);

            if (IdleTime > 0 && ItemTag != HomeItemTag)
            {
                var currentIdleTime = (oms - lastIdleTimeMilliseconds) / 1000.0;
                //Debug.WriteLine($"OnDispatcher Timer Tick: {currentIdleTime}");

                if (currentIdleTime >= IdleTime)
                {
                    HomeItem();
                }
            }
        }
        /// <summary>
        /// 重置 <see cref="IdleTime"/> 计时器。
        /// </summary>
        protected void ResetIdleTime() => lastIdleTimeMilliseconds = OperateStopwatch.ElapsedMilliseconds;
        /// <summary>
        /// 【可重写】定时器 <see cref="DispatcherTimer"/> 间隔 40 毫秒触发事件。 
        /// <para>注意：UI 线程调用，并不是绝对精准的 40 毫秒钟触发一次。</para>
        /// </summary>
        /// <param name="elapsedMilliseconds"></param>
        protected virtual void OnDispatcherTimerCallback(long elapsedMilliseconds) { }
        #endregion


        #region INotifyPropertyChanged
        /// <inheritdoc /> 
        public event PropertyChangedEventHandler PropertyChanged;
        /// <summary>
        /// 触发 PropertyChanged 事件。
        /// </summary>
        /// <param name="propertyName">属性名称，由 [CallerMemberName] 自动填充</param>
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
        /// <summary>
        /// 设置字段值，若值变化则触发属性变更通知。
        /// </summary>
        /// <typeparam name="T">字段类型</typeparam>
        /// <param name="field">字段的引用</param>
        /// <param name="value">字段的新值</param>
        /// <param name="propertyName">属性名称，由 [CallerMemberName] 自动填充</param>
        protected void SetFieldValue<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return;

            field = value;
            this.OnPropertyChanged(propertyName);
        }
        /// <summary>
        /// 设置字段值，若值变化则触发属性变更通知。
        /// </summary>
        /// <typeparam name="T">字段类型</typeparam>
        /// <param name="field">字段的引用</param>
        /// <param name="value">字段的新值</param>
        /// <param name="propertyName">属性名称，由 [CallerMemberName] 自动填充</param>
        /// <param name="dependentPropertyNames">依赖此属性的其它属性名称列表</param>
        protected void SetFieldValue<T>(ref T field, T value, [CallerMemberName] string propertyName = null, params string[] dependentPropertyNames)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return;

            field = value;
            this.OnPropertyChanged(propertyName);

            foreach (var dependentPropertyName in dependentPropertyNames)
                this.OnPropertyChanged(dependentPropertyName);
        }
        #endregion


    }
}
