using System.Xml.Linq;
using System.Linq;
using System.Collections.Generic;
using System;
using System.Security;
using System.IO;

namespace SpaceCG.Extensions
{
    /// <summary>
    /// XElement Extensions
    /// </summary>
    public static partial class XElementExtensions
    {
        #region 模板引用替换
        /// <summary>
        /// 递归展开 XML 元素树中所有 <c>&lt;RefTemplate Name="..." /&gt;</c> 引用为对应的模板内容。
        /// <para><b>处理顺序（深度优先，从上到下）：</b></para>
        /// <para>
        /// 1. 根元素先用自己的 <c>&lt;ElementName.Templates&gt;</c> 替换整棵子树中的所有 RefTemplate 引用；<br/>
        /// 2. 然后递归处理每个子元素：若子元素有自己的模板，则用子模板处理其子树；
        ///    若没有，则继续向下递归。</para>
        /// <para><b>同名冲突：</b>父级模板先处理，子级同名模板不会被引用。
        /// 因此不同层级不应定义同名模板，应将模板统一定义在最高层级。</para>
        /// <para>引用元素的额外属性（Name 除外）会替换模板中同名的 <c>{AttributeName}</c> 占位符。</para>
        /// <para>自动检测并移除循环引用的模板，避免死循环。</para>
        /// </summary>
        /// <param name="element">待处理的 XML 元素树根节点。</param>
        public static void ApplyTemplates(this XElement element)
        {
            if (element == null || !element.HasElements) return;

            const string Templates = nameof(Templates);

            // 根节点：查找并应用 <ElementName.Templates>
            var templatesElements = element.Elements($"{element.Name}.{Templates}").ToList();
            if (templatesElements.Any())
            {
                templatesElements.Remove();
                ApplyTemplates(element, templatesElements[0]);
            }

            // 递归处理每个子元素（每个子元素有自己的作用域）
            foreach (var child in element.Elements())
            {
                templatesElements = child.Elements($"{child.Name}.{Templates}").ToList();
                if (!templatesElements.Any())
                {
                    ApplyTemplates(child);
                    continue;
                }

                templatesElements.Remove();
                ApplyTemplates(child, templatesElements[0]);
            }
        }
        /// <summary>
        /// 在指定元素范围内，用模板定义替换所有 RefTemplate 引用。
        /// <para>注意：<c>element.Descendants(RefTemplate)</c> 不能固化列表，
        /// 因为替换过程中会动态修改 XML 树（AddAfterSelf + Remove），必须每次实时查询。</para>
        /// </summary>
        /// <param name="element">模板生效的元素范围（作用域根节点）。</param>
        /// <param name="templatesElement">当前作用域的模板定义容器。</param>
        private static void ApplyTemplates(this XElement element, XElement templatesElement)
        {
            if (element == null || templatesElement == null) return;

            const string Name = nameof(Name);
            const string Template = nameof(Template);
            const string RefTemplate = nameof(RefTemplate);

            // 0.检查模板元素是否存在循环引用或无效的模板，如果存在，则删除相关的模板元素
            templatesElement.RemoveInvalidTemplates();
            var templates = templatesElement.Elements(Template);
            if (!templates.Any()) return;

            // 1.构建模板名 → 模板元素的快速查找字典
            var templateDictionary = templates.Where(t => t.Attribute(Name) != null)
                                    .ToDictionary(t => t.Attribute(Name).Value, t => t);
            if (templateDictionary.Count == 0) return;

            // 2.收集所有有效的 RefTemplate 引用（不能固化列表，不能固化列表）
            var refTemplates = from refTemplate in element.Descendants(RefTemplate)
                               let refName = refTemplate.Attribute(Name)?.Value
                               where !string.IsNullOrWhiteSpace(refName)
                               select refTemplate;

            // Analyse And Replace(Apply Templates)
            for (int i = 0; i < refTemplates.Count(); i++)
            {
                var refTemplate = refTemplates.ElementAt(i);
                var refTemplateName = refTemplate.Attribute(Name)?.Value;

                if (!templateDictionary.TryGetValue(refTemplateName, out var matchedTemplate))
                    continue;

                // 拷贝模板文本，并用 RefTemplate 的额外属性替换模板中的占位符
                var templateString = matchedTemplate.ToString();
                foreach (XAttribute attribute in refTemplate.Attributes())
                {
                    if (attribute.Name != Name)
                        templateString = templateString.Replace($"{{{attribute.Name}}}", attribute.Value);
                }

                // 将模板内容插入到 RefTemplate 之后，再删除 RefTemplate 自身
                refTemplate.AddAfterSelf(XElement.Parse(templateString).Elements());
                refTemplate.Remove();

                i--; // 补偿删除后的索引偏移
            }
        }
        /// <summary>
        /// 检测并移除模板集合中的循环引用和无效模板（无 Name 属性或引用不存在的模板）。
        /// <para>使用 DFS 深度优先搜索构建依赖图，检测循环依赖。</para>
        /// </summary>
        /// <param name="templates">包含 Template 子元素的容器。</param>
        private static void RemoveInvalidTemplates(this XElement templates)
        {
            if (templates == null) return;

            const string Name = nameof(Name);
            const string Template = nameof(Template);
            const string RefTemplate = nameof(RefTemplate);

            // 0.移除没有 Name 属性的 Template 节点
            foreach (var template in templates.Elements(Template).ToList())
            {
                var nameAttr = template.Attribute(Name);
                if (nameAttr == null || string.IsNullOrWhiteSpace(nameAttr.Value))
                {
                    template.Remove();
                }
            }

            // 1. 收集所有有效 Template 节点
            var templateDictionary = templates.Elements(Template).ToDictionary(t => t.Attribute(Name).Value, t => t);

            // 2️.构建依赖图（模板名 → 其引用的其他模板名列表）
            var graph = new Dictionary<string, List<string>>();
            foreach (var kvp in templateDictionary)
            {
                var name = kvp.Key;
                var refNames = kvp.Value
                    .Descendants(RefTemplate)
                    .Select(x => (string)x.Attribute(Name))
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .ToList();

                graph[name] = refNames;
            }

            // 3. DFS 检测循环引用
            var visited = new HashSet<string>();
            var inStack = new HashSet<string>();
            var circularTemplates = new HashSet<string>();

            bool Dfs(string node)
            {
                if (inStack.Contains(node))
                {
                    circularTemplates.Add(node);
                    return true;
                }

                if (!graph.ContainsKey(node) || visited.Contains(node))
                    return false;

                visited.Add(node);
                inStack.Add(node);

                foreach (var child in graph[node])
                {
                    if (Dfs(child))
                    {
                        circularTemplates.Add(node);
                    }
                }

                inStack.Remove(node);
                return circularTemplates.Contains(node);
            }

            foreach (var node in graph.Keys)
                Dfs(node);

            // 4. 移除循环引用相关的 Template 节点
            if (circularTemplates.Count > 0)
            {
                Trace.TraceWarning("检测到循环引用模板： " + string.Join(", ", circularTemplates));
                foreach (var name in circularTemplates)
                {
                    if (templateDictionary.TryGetValue(name, out var template))
                    {
                        template.Remove();
                    }
                }
            }
        }
        #endregion

        #region 字典占位符替换
        /// <summary>
        /// 递归替换 XML 元素树中所有属性的 <c>{Key}</c> 占位符为字典中对应的值。
        /// <para><b>处理顺序（深度优先，从上到下）：</b></para>
        /// <para>
        /// 1. 根元素先用自己的 <c>&lt;RootName.Dictionary&gt;</c> 替换整棵子树中的所有 {Key} 占位符；<br/>
        /// 2. 然后递归处理每个子元素：若子元素有自己的字典，则用子字典处理其子树；
        ///    若没有，则继续向下递归。</para>
        /// <para><b>同名冲突：</b>父级字典先处理，子级同名 Key 不会被匹配。
        /// 因此不同层级不应定义同名 Key，应将字典统一定义在最高层级。</para>
        /// <para>Value 中可引用同字典内的其他 Key（如 <c>{深蓝色}</c>），被引用的 Key 应定义在引用者之前。</para>
        /// </summary>
        /// <param name="element">待处理的 XML 元素树根节点。</param>
        public static void ApplyDictionary(this XElement element)
        {
            if (element == null || !element.HasElements) return;

            const string Dictionary = nameof(Dictionary);

            // 根节点：查找并应用 <RootName.Dictionary>
            var dictionaryElements = element.Elements($"{element.Name}.{Dictionary}").ToList();
            if (dictionaryElements != null && dictionaryElements.Count > 0)
            {
                dictionaryElements.Remove();
                ApplyDictionary(element, dictionaryElements[0]);
            }

            // Child Elements
            foreach (var child in element.Elements())
            {
                dictionaryElements = child.Elements($"{child.Name}.{Dictionary}").ToList();
                if (dictionaryElements == null || dictionaryElements.Count <= 0)
                {
                    ApplyDictionary(child);
                    continue;
                }

                dictionaryElements.Remove();
                ApplyDictionary(child, dictionaryElements[0]);
            }
        }
        /// <summary>
        /// 用字典内容替换指定元素范围内所有属性中的 {Key} 占位符。
        /// </summary>
        private static void ApplyDictionary(this XElement element, XElement dictionaryElement)
        {
            if (element == null || dictionaryElement == null) return;

            const string Key = nameof(Key);
            const string Item = nameof(Item);
            const string Value = nameof(Value);

            // 构建键值字典
            var dictionary = new Dictionary<string, string>();
            foreach (var item in dictionaryElement.Elements(Item))
            {
                var key = item.Attribute(Key);
                var value = item.Attribute(Value);
                if (key == null || value == null || string.IsNullOrWhiteSpace(key.Value)) continue;

                if (dictionary.ContainsKey(key.Value))
                    dictionary[key.Value] = value.Value;
                else
                    dictionary.Add(key.Value, value.Value);
            }

            if (dictionary.Count == 0) return;

            // 遍历所有后代元素的属性，替换占位符
            foreach (var attribute in element.Descendants().Attributes())
            {
                var value = attribute.Value;
                if (string.IsNullOrWhiteSpace(value)) continue;
                if (value.IndexOf('{') < 0) continue;

                foreach (var kv in dictionary)
                {
                    value = value.Replace($"{{{kv.Key}}}", kv.Value);
                }

                attribute.Value = value;
            }
        }
        #endregion

        /// <summary>
        /// 从 XML 元素中读取属性值或子元素值并转换为指定类型。
        /// <para>查找优先级：属性 → 第一个匹配名称的子元素 → 元素自身的 Value。</para>
        /// </summary>
        /// <typeparam name="T">目标值类型。</typeparam>
        /// <param name="element">XML 元素。</param>
        /// <param name="name">属性名称或子元素名称；为 null/空白时直接读取元素自身的 Value。</param>
        /// <param name="value">转换成功时输出强类型值；否则为 <c>default(T)</c>。</param>
        /// <returns>读取并转换成功返回 <c>true</c>；元素为 null 或转换失败返回 <c>false</c>。</returns>
        public static bool TryGetValue<T>(this XElement element, string name, out T value) // where T : notnull  // C# 7.3 不支持？？
        {
            value = default(T);
            if (element == null) return false;

            // 按优先级获取原始值：属性 > 子元素 > 元素自身 Value
            string rawValue;
            if (string.IsNullOrWhiteSpace(name))
            {
                rawValue = element.Value;
            }
            else
            {
                var attribute = element.Attribute(name);
                rawValue = attribute != null ? attribute.Value : element.Element(name)?.Value ?? element.Value;
            }
            if (rawValue == null) return false;

            if (!rawValue.TryConvertTo(typeof(T), out var targetValue)) return false;

            try
            {
                value = (T)targetValue;
                return true;
            }
            catch (Exception ex)
            {
                Trace.TraceWarning($"获取元素值类型转换异常：{ex.Message}");
            }

            return false;
        }

        /// <summary>
        /// 设置 XML 元素的属性值（存在则更新，不存在则添加）。
        /// </summary>
        /// <param name="element">XML 元素。</param>
        /// <param name="name">属性名称。</param>
        /// <param name="value">属性值，null 转为空字符串。</param>
        /// <returns>设置成功返回 <c>true</c>；元素为 null 或名称为空返回 <c>false</c>。</returns>
        public static bool SetAttributeValue(this XElement element, string name, object value)
        {
            if (element == null) return false;
            if (string.IsNullOrWhiteSpace(name)) return false;

            var attribute = element.Attribute(name);
            if (attribute != null)
            {
                attribute.Value = value?.ToString() ?? string.Empty;
            }
            else
            {
                element.Add(new XAttribute(name, value ?? string.Empty));
            }

            return true;
        }

        /// <summary>
        /// 加载 XML 配置文件并自动执行模板替换和字典占位符替换。
        /// <para>等效于 <c>XElement.Load(path) + ApplyTemplates() + ApplyDictionaryValues()</c>。</para>
        /// </summary>
        /// <param name="configFile">XML 配置文件路径。</param>
        /// <returns>预处理后的 XML 元素树。</returns>
        /// <exception cref="ArgumentException">文件路径为 null 或空白。</exception>
        /// <exception cref="FileNotFoundException">文件不存在。</exception>
        public static XElement LoadConfig(string configFile)
        {
            if (string.IsNullOrWhiteSpace(configFile))
                throw new ArgumentException("文件路径不能为空！", nameof(configFile));

            if (!File.Exists(configFile))
                throw new FileNotFoundException("文件不存在！", configFile);

            XElement config = XElement.Load(configFile);
            config.ApplyTemplates();
            config.ApplyDictionary();

            return config;
        }
    }
}
