using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace SpaceCG.Controls
{
    /// <summary>
    /// 图像按钮基类：提供常规态 / 选中态两套图像源。
    /// <para>线程不安全，仅限 UI 线程访问。</para>
    /// </summary>
    /// <remarks>
    /// 模板（Controls/ImageButton.xaml）以 <see cref="ToggleButton"/> 为 TargetType 共享，
    /// 自定义依赖属性在模板中必须使用 Binding + RelativeSource TemplatedParent 绑定，
    /// 不能使用 TemplateBinding（后者按模板 TargetType 编译期校验）。
    /// </remarks>
    public class ImageButtonBase : ToggleButton
    {
        /// <summary> 标识 <see cref="NormalSource"/> 依赖属性。</summary>
        public static readonly DependencyProperty NormalSourceProperty = DependencyProperty.Register(nameof(NormalSource), typeof(ImageSource), typeof(ImageButtonBase), new PropertyMetadata(null));

        /// <summary> 常规状态（未选中）下显示的 <see cref="ImageSource"/> </summary>
        public ImageSource NormalSource
        {
            get => (ImageSource)GetValue(NormalSourceProperty);
            set => SetValue(NormalSourceProperty, value);
        }

        /// <summary> 标识 <see cref="CheckedSource"/> 依赖属性。</summary>
        public static readonly DependencyProperty CheckedSourceProperty = DependencyProperty.Register(nameof(CheckedSource), typeof(ImageSource), typeof(ImageButtonBase), new PropertyMetadata(null));

        /// <summary> 选中（IsChecked = true）状态下显示的 <see cref="ImageSource"/>（未设置时选中态无图） </summary>
        public ImageSource CheckedSource
        {
            get => (ImageSource)GetValue(CheckedSourceProperty);
            set => SetValue(CheckedSourceProperty, value);
        }

        /// <summary>
        /// 鼠标按下时拦截右键，防止右键触发选中状态变更。
        /// </summary>
        /// <param name="e">鼠标按钮事件参数</param>
        internal static void SuppressRightMouseDown(MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Right)
            {
                e.Handled = true;
            }
        }

        /// <inheritdoc/>
        protected override void OnPreviewMouseDown(MouseButtonEventArgs e)
        {
            SuppressRightMouseDown(e);
            base.OnPreviewMouseDown(e);
        }
    }


    /// <summary>
    /// 两态图像切换控件，类似 <see cref="ToggleButton"/> 按钮。
    /// <para>常规态显示 <see cref="ImageButtonBase.NormalSource"/>，选中态显示 <see cref="ImageButtonBase.CheckedSource"/>。</para>
    /// <para>线程不安全，仅限 UI 线程访问。</para>
    /// </summary>
    public sealed class ImageToggleButton : ImageButtonBase
    {
        static ImageToggleButton()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(ImageToggleButton), new FrameworkPropertyMetadata(typeof(ImageToggleButton)));
        }
    }


    /// <summary>
    /// 单选图像控件，类似 <see cref="RadioButton"/> 按钮，保留 <see cref="RadioButton.GroupName"/> 分组互斥能力。
    /// <para>常规态显示 <see cref="NormalSource"/>，选中态显示 <see cref="CheckedSource"/>。</para>
    /// <para>线程不安全，仅限 UI 线程访问。</para>
    /// </summary>
    /// <remarks>
    /// 因继承自 <see cref="RadioButton"/>（而非 <see cref="ImageButtonBase"/>），
    /// 两个依赖属性通过 <see cref="DependencyProperty.AddOwner(Type)"/> 复用基类定义，CLR 包装必须重复声明。
    /// </remarks>
    public sealed class ImageRadioButton : RadioButton
    {
        /// <summary>
        /// 标识 <see cref="NormalSource"/> 依赖属性（通过 AddOwner 复用 <see cref="ImageButtonBase"/> 的定义）。
        /// </summary>
        public static readonly DependencyProperty NormalSourceProperty = ImageButtonBase.NormalSourceProperty.AddOwner(typeof(ImageRadioButton));

        /// <summary> 常规状态（未选中）下显示的 <see cref="ImageSource"/> </summary>
        public ImageSource NormalSource
        {
            get => (ImageSource)GetValue(NormalSourceProperty);
            set => SetValue(NormalSourceProperty, value);
        }

        /// <summary>
        /// 标识 <see cref="CheckedSource"/> 依赖属性（通过 AddOwner 复用 <see cref="ImageButtonBase"/> 的定义）。
        /// </summary>
        public static readonly DependencyProperty CheckedSourceProperty = ImageButtonBase.CheckedSourceProperty.AddOwner(typeof(ImageRadioButton));

        /// <summary> 选中（IsChecked = true）状态下显示的 <see cref="ImageSource"/>（未设置时选中态无图） </summary>
        public ImageSource CheckedSource
        {
            get => (ImageSource)GetValue(CheckedSourceProperty);
            set => SetValue(CheckedSourceProperty, value);
        }

        static ImageRadioButton()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(ImageRadioButton), new FrameworkPropertyMetadata(typeof(ImageRadioButton)));
        }

        /// <summary>
        /// 鼠标按下事件，防止右键触发选中。
        /// </summary>
        protected override void OnPreviewMouseDown(MouseButtonEventArgs e)
        {
            ImageButtonBase.SuppressRightMouseDown(e);
            base.OnPreviewMouseDown(e);
        }
    }

}
