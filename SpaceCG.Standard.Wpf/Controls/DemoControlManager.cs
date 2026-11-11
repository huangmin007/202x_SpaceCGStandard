using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Threading;
using SpaceCG.Extensions;
using SpaceCG.Net;

namespace SpaceCG.Controls
{
    public class ItemTagChangedEventArgs : EventArgs
    {
        public int NewItemTag { get; internal set; }

        public int OldItemTag { get; internal set; }

        public IEnumerable<FileInfo> Resources { get; internal set; }

        public ItemTagChangedEventArgs(int newItemTag, int oldItemTag)
        {
            NewItemTag = newItemTag;
            OldItemTag = oldItemTag;
        }
        public ItemTagChangedEventArgs(int newItemTag, int oldItemTag, IEnumerable<FileInfo> resources)
        {
            Resources = resources;
            NewItemTag = newItemTag;
            OldItemTag = oldItemTag;
        }
    }

    public class DemoControlManager : INotifyPropertyChanged
    {
        /// <summary>  默认的资源根目录。 </summary>
        private readonly static string RootResources = "Resources";
        private readonly static string DefaultItemTagsFile = $"{RootResources}/ItemTags.txt";

        /// <summary> 操作间隔的最小时间，单位：毫秒。 </summary>
        private readonly static int OperateIntervalTickCount = 500;

        /// <summary> 资源文件夹中支持的文件扩展名。</summary>
        public List<string> SupportFileExtensions { get; private set; } = new List<string> { ".mp4", ".mov", ".m4v", ".flv", ".png", ".jpg", ".bmp", ".jpeg" };

        private readonly Window _window;
        private RpcServer4X rpcServer;

        /// <summary> 上一次操作的时间，单位：毫秒。 </summary>
        private long lastOperateTickCount = int.MinValue;

        private DispatcherTimer dispatcherTimer;
        private readonly int TargetIdleTimeSeconds = 0;

        /// <summary> ItemTags 关系字典；Key 为上一级资源 Tag, Value 为对应 Key 的子级资源 Tags 集合。 </summary>
        protected IReadOnlyDictionary<int, IEnumerable<int>> ItemTagsDict { get; private set; }
        /// <summary> 当前资源文件夹的文件信息。</summary>
        public IReadOnlyDictionary<int, IEnumerable<FileInfo>> FileResources { get; private set; }

        public event EventHandler<EventArgs> LanguageChanged;
        public event EventHandler<ItemTagChangedEventArgs> ItemTagChanged;

        #region Properties
        private int _itemTag = -1;
        public int ItemTag
        {
            get => _itemTag;
            private set => SetFieldValue(ref _itemTag, value);
        }

        private int _homeItemTag = -1;
        public int HomeItemTag
        {
            get => _homeItemTag;
            private set => SetFieldValue(ref _homeItemTag, value);
        }

        private bool _hasNextItem = false;
        public bool HasNextItem
        {
            get => _hasNextItem;
            private set => SetFieldValue(ref _hasNextItem, value);
        }
        private int _nextItemTag = -1;
        public int NextItemTag
        {
            get => _nextItemTag;
            private set => SetFieldValue(ref _nextItemTag, value);
        }

        private bool _hasPrevItem = false;
        public bool HasPrevItem
        {
            get => _hasPrevItem;
            private set => SetFieldValue(ref _hasPrevItem, value);
        }
        private int _prevItemTag = -1;
        public int PrevItemTag
        {
            get => _prevItemTag;
            private set => SetFieldValue(ref _prevItemTag, value);
        }

        private bool _hasBackItem = false;
        public bool HasBackItem
        {
            get => _hasBackItem;
            private set => SetFieldValue(ref _hasBackItem, value);
        }
        private int _backItemTag = -1;
        public int BackItemTag
        {
            get => _backItemTag;
            private set => SetFieldValue(ref _backItemTag, value);
        }
        #endregion

        public DemoControlManager(Window window, string defaultLanguage = "zh-CN", int localPort = 2000, int idleTimeSeconds = 300)
        {
            _window = window;
            _window.InitializeStandard(this);
            _window.Closing += Window_Closing;
            _window.PreviewMouseDown += Window_PreviewMouseDown;

            _window.Language = XmlLanguage.GetLanguage(defaultLanguage);
            DependencyPropertyDescriptor.FromProperty(FrameworkElement.LanguageProperty, typeof(FrameworkElement)).AddValueChanged(_window, OnLanguageChanged);

            ReadItemTags();
            UpdateFileResources();

            if (localPort > 1024 && localPort < 65535)
            {
                try
                {
                    rpcServer = new RpcServer4X(localPort);
                    rpcServer.RegisterObject("Demo", this);
                    rpcServer.Start();
                    rpcServer.ClientInvokeRequest += (s, e) => lastOperateTickCount = Environment.TickCount;
                }
                catch (Exception ex)
                {
                    if (MessageBox.Show($"RPC 服务启动失败：{ex.Message}\n是否需要退出程序？", "Error", MessageBoxButton.OKCancel, MessageBoxImage.Error) == MessageBoxResult.OK)
                    {
                        Environment.Exit(0);
                    }
                }
            }

            if (idleTimeSeconds > 0)
            {
                TargetIdleTimeSeconds = idleTimeSeconds;
                dispatcherTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(100), DispatcherPriority.Normal, OnDispatcherTimerCallback, _window.Dispatcher);
                dispatcherTimer.Start();
            }
        }

        private void OnDispatcherTimerCallback(object sender, EventArgs e)
        {
            if (ItemTag != HomeItemTag)
            {
                var idleTimeSeconds = (Environment.TickCount - lastOperateTickCount) / 1000.0;
                //Debug.WriteLine($"OnDispatcher Timer Tick: {idleTimeSeconds}");

                if (idleTimeSeconds >= TargetIdleTimeSeconds)
                {
                    HomeItem();
                }
            }
        }
        private void Window_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            lastOperateTickCount = Environment.TickCount;
        }
        private void OnLanguageChanged(object sender, EventArgs e)
        {
            Trace.TraceInformation($"On Language Changed:{_window.Language}");

            UpdateFileResources();
            LanguageChanged?.Invoke(this, EventArgs.Empty);
        }
        private void Window_Closing(object sender, CancelEventArgs e)
        {
            rpcServer?.Dispose();
            dispatcherTimer?.Stop();
        }

        #region
        /// <summary> 更新资源文件信息，重新读取资源文件信息，并更新 <see cref="FileResources"/> 对象。 </summary>
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

            var directory = $"{RootResources}/{_window.Language}";
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
        /// <summary> 当前资源 Tag 属性发生变化时触发的事件。 </summary>
        /// <param name="e"></param>
        protected virtual void OnItemTagChanged(int newValue, int oldValue)
        {
            if (oldValue == newValue) return;
            if (FileResources == null || !FileResources.Any()) return;

            var oldItemTag = oldValue;
            var newItemTag = newValue;
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
            HasBackItem = ItemTagsDict != null && ItemTagsDict.Values.Any(childTags => childTags.Contains(newItemTag));
            BackItemTag = GetParentItemTag();
        }
        /// <summary> 获取上一级资源的 Tag 值。 </summary>
        /// <returns></returns>
        protected int GetParentItemTag()
        {
            if (ItemTagsDict == null || !ItemTagsDict.Any()) return -1;

            foreach (var itemTags in ItemTagsDict)
            {
                if (itemTags.Value.Contains(ItemTag))
                {
                    return itemTags.Key;
                }
            }

            return -1;
        }
        /// <summary> 读取 Resources/ItemTags.txt 文件，更新 <see cref="ItemTagsDict"/> 对象。 </summary>
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
                    ItemTagsDict = itemTagsDictionary;
            }
            catch (Exception ex)
            {
                ItemTagsDict = null;
                Trace.TraceError($"读取 BackTags.txt 文件失败，错误信息：{ex.Message}");
            }
        }
        #endregion


        #region Public 接口函数
        /// <summary>
        /// 【接口函数】加载指定 Tag 的资源文件。
        /// </summary>
        /// <param name="tag"></param>
        public void LoadItem(int tag)
        {
            Trace.WriteLine($"Load Item: {tag}/{_window.Language}");

            if (tag < 0 || tag == ItemTag) return;
            if (FileResources == null || !FileResources.Any() || !FileResources.ContainsKey(tag)) return;
            
            if (Environment.TickCount - lastOperateTickCount < OperateIntervalTickCount)
            {
                Trace.TraceWarning($"资源操作过于频繁，请稍后再试！");
                return;
            }

            var oldItemTag = ItemTag;
            ItemTag = tag;
            var newItemTag = ItemTag;

            OnItemTagChanged(newItemTag, oldItemTag);
            ItemTagChanged?.Invoke(this, new ItemTagChangedEventArgs(oldItemTag, newItemTag, FileResources[newItemTag]));

            lastOperateTickCount = Environment.TickCount;
            Trace.TraceInformation($"On ItemTag Changed: {ItemTag}/{_window.Language}  NextItem:{HasNextItem}/{NextItemTag}  PrevItem:{HasPrevItem}/{PrevItemTag}  BackItem:{HasBackItem}/{BackItemTag}");
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

        /// <see cref="WindowExtensions.FullScreen(Window)"/>
        public void FullScreen() => _window.FullScreen();

        /// <see cref="WindowExtensions.ChangeLanguage(Window)"/>
        public void ChangeLanguage() => _window.ChangeLanguage();

        /// <see cref="WindowExtensions.ChangeLanguage(Window, string)"/>
        public void ChangeLanguage(string language) => _window.ChangeLanguage(language);
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
