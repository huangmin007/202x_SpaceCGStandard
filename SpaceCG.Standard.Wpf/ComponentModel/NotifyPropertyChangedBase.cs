using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SpaceCG.ComponentModel
{
    /// <summary>
    /// 实现 INotifyPropertyChanged 接口的基类，用于 MVVM 数据绑定
    /// </summary>
    public class NotifyPropertyChangedBase : INotifyPropertyChanged
    {
        /// <inheritdoc /> 
        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>
        /// 触发 PropertyChanged 事件
        /// </summary>
        /// <param name="propertyName">属性名称，由 [CallerMemberName] 自动填充</param>
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        /// <summary>
        /// 设置字段值，若值变化则触发属性变更通知
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
        /// 设置字段值，若值变化则触发属性变更通知
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

    }
}
