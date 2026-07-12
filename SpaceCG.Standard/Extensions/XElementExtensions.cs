using System.Xml.Linq;
using SpaceCG.Generic;
using System.Linq;
using System.Collections.Generic;
using System;
using System.Security;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices.ComTypes;
using System.ComponentModel;
using System.Globalization;
using System.Xml;

namespace SpaceCG.Extensions
{
    /// <summary>
    /// XElement Extensions
    /// </summary>
    public static partial class XElementExtensions
    {
        /// <summary>
        /// 转义字符字典
        /// <para>或使用 <see cref="SecurityElement.Escape(string)"/> 方法转义后的字符串</para>
        /// </summary>
        public static IReadOnlyDictionary<char, string> EscapeCharacters { get; } = new Dictionary<char, string>()
        {
            { '<', "&lt;" },
            { '>', "&gt;" },
            { '&', "&amp;" },
            { '"', "&quot;" },
            { '\'', "&apos;" },
        };

        /// <summary>
        /// 将其等效的有效 XML 字符转为字符串中的无效 XML 字符
        /// <para>与 <see cref="SecurityElement.Escape(string)"/> 方法配合使用</para>
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public static string Unescape(this string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return value;
            return value.Replace("&lt;", "<").Replace("&gt;", ">").Replace("&amp;", "&").Replace("&quot;", "\"").Replace("&apos;", "'");
        }

        /// <summary>
        /// 跟据当前元素的模板 (Element.Templates) 替换当前子元素中的引用的模板元素 (RefTemplate) 对象，支持多层模板嵌套。
        /// <para>注意：不要出现模板循环引用，否则会移除异常模板或无效的模型。</para>
        /// </summary>
        /// <param name="element"></param>
        public static void ReplaceTemplateElements(this XElement element)
        {
            if (element == null || !element.HasElements) return;

            const string Templates = nameof(Templates);

            // Root Element
            var templatesElements = element.Elements($"{element.Name}.{Templates}").ToList();
            if (templatesElements != null && templatesElements.Count > 0)
            {
                templatesElements.Remove();
                ReplaceTemplateElements(element, templatesElements[0]);
            }

            // Child Elements
            foreach (var child in element.Elements())
            {
                templatesElements = child.Elements($"{child.Name}.{Templates}").ToList();
                if (templatesElements == null || templatesElements.Count <= 0)
                {
                    ReplaceTemplateElements(child);
                    continue;
                }

                templatesElements.Remove();
                ReplaceTemplateElements(child, templatesElements[0]);
            }
        }
        /// <summary>
        /// 替换模板元素
        /// </summary>
        /// <param name="element"></param>
        /// <param name="templatesElement"></param>
        private static void ReplaceTemplateElements(this XElement element, XElement templatesElement)
        {
            if (element == null || templatesElement == null) return;

            const string Name = nameof(Name);
            const string Template = nameof(Template);
            const string RefTemplate = nameof(RefTemplate);

            // 检查模板元素是否存在循环引用或无效的模板，如果存在，则删除相关的模板元素
            templatesElement.RemoveInvalidTemplates();
            var templates = templatesElement?.Elements(Template);
            if (templates == null || templates.Count() <= 0) return;

            // 获取有效的引用模板元素集合
            var refTemplates = from refTemplate in element.Descendants(RefTemplate)
                               where !string.IsNullOrWhiteSpace(refTemplate.Attribute(Name)?.Value)
                               select refTemplate;
            if (refTemplates == null || refTemplates.Count() <= 0) return;

            // Analyse And Replace
            for (int i = 0; i < refTemplates.Count(); i++)
            {
                XElement refTemplate = refTemplates.ElementAt(i);
                string refTemplateName = refTemplate.Attribute(Name)?.Value;

                // 在模板集合中查找指定名称的模板
                var temps = from template in templates
                            where refTemplateName == template.Attribute(Name)?.Value
                            select template;
                if (temps == null || temps.Count() <= 0) continue;

                // 拷贝模板并更新属性值
                string templateString = temps.First().ToString();
                foreach (XAttribute attribute in refTemplate.Attributes())
                {
                    if (attribute.Name != Name)
                        templateString = templateString.Replace($"{{{attribute.Name}}}", attribute.Value);
                }

                refTemplate.AddAfterSelf(XElement.Parse(templateString).Elements());
                refTemplate.Remove();

                i--;
            }
        }
        /// <summary>
        /// 检查模板元素是否存在循环引用 或 无效的模板，如果存在，则删除相关的模板元素
        /// </summary>
        /// <param name="templates"></param>
        private static void RemoveInvalidTemplates(this XElement templates)
        {
            if (templates == null) return;

            const string Name = nameof(Name);
            const string Template = nameof(Template);
            const string RefTemplate = nameof(RefTemplate);

            // 0.移除没有 Name 属性的 Template 节点
            foreach (var template in templates.Elements(Template))
            {
                var nameAttr = template.Attribute(Name);
                if (nameAttr == null || string.IsNullOrWhiteSpace(nameAttr.Value))
                {
                    template.Remove();
                    continue;
                }
            }

            // 1.收集所有 Template 节点
            var templateDictionary = templates.Elements(Template).ToDictionary(t => t.Attribute(Name).Value, t => t);

            // 2️.构建依赖图
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

            // 3.检测循环引用
            var visited = new HashSet<string>();
            var stack = new HashSet<string>();
            var circularTemplates = new HashSet<string>();

            bool Dfs(string node)
            {
                if (stack.Contains(node))
                {
                    circularTemplates.Add(node);
                    return true;
                }

                if (!graph.ContainsKey(node) || visited.Contains(node))
                    return false;

                visited.Add(node);
                stack.Add(node);

                foreach (var child in graph[node])
                {
                    if (Dfs(child))
                    {
                        circularTemplates.Add(node);
                    }
                }

                stack.Remove(node);
                return circularTemplates.Contains(node);
            }

            foreach (var node in graph.Keys)
                Dfs(node);

            // 4.删除循环引用相关的 Template 节点
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

        /// <summary>
        /// 跟据当前元素的字典 (Element.Dictionary) 替换当前子元素中的属性值 {Key} 对应的值
        /// </summary>
        /// <param name="element"></param>
        public static void ReplaceDictionaryValues(this XElement element)
        {
            if (element == null || !element.HasElements) return;

            const string Dictionary = nameof(Dictionary);

            // Root Element
            var dictionaryElements = element.Elements($"{element.Name}.{Dictionary}").ToList();
            if (dictionaryElements != null && dictionaryElements.Count > 0)
            {
                dictionaryElements.Remove();
                ReplaceDictionaryValues(element, dictionaryElements[0]);
            }

            // Child Elements
            foreach (var child in element.Elements())
            {
                dictionaryElements = child.Elements($"{child.Name}.{Dictionary}").ToList();
                if (dictionaryElements == null || dictionaryElements.Count <= 0)
                {
                    ReplaceDictionaryValues(child);
                    continue;
                }

                dictionaryElements.Remove();
                ReplaceDictionaryValues(child, dictionaryElements[0]);
            }
        }
        /// <summary>
        /// 替换属性值 {Key} 对应的值
        /// </summary>
        /// <param name="element"></param>
        /// <param name="dictionaryElement"></param>
        private static void ReplaceDictionaryValues(this XElement element, XElement dictionaryElement)
        {
            if (element == null || dictionaryElement == null) return;

            const string Key = nameof(Key);
            const string Item = nameof(Item);
            const string Value = nameof(Value);

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

            foreach (var attribute in element.Descendants().Attributes())
            {
                foreach (var kv in dictionary)
                {
                    attribute.Value = attribute.Value.Replace($"{{{kv.Key}}}", kv.Value);
                }
            }
        }

        /// <summary>
        /// 获取元素的属性或是子元素的值并尝试将其转换为指定类型。
        /// <para>优先获取元素的属性值，如果属性不存在，则获取元素的第一个(按文档顺序)子元素的值。如果为空字符串，则获取元素本身的值。</para>
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="element"></param>
        /// <param name="name">属性名称，或是元素的第一个(按文档顺序)子元素名称；如果为空字符串，则获取元素本身的值。</param>
        /// <param name="value">输出值</param>
        /// <returns></returns>
        public static bool TryGetValue<T>(this XElement element, string name, out T value) // where T : notnull  // C# 7.3 不支持？？
        {
            value = default(T);
            if (element == null) return false;

            var rawValue = element.Attribute(name) != null ? element.Attribute(name).Value : element.Element(name)?.Value ?? element.Value;

            try
            {
                var result = rawValue.TryConvertTo(typeof(T), out var targetValue);
                value = (T)targetValue;
                return result;
            }
            catch (Exception ex)
            {
                Trace.TraceWarning($"获取元素值异常：{ex.Message}");
            }

            return false;
        }

        /// <summary>
        /// 尝试添加或更新元素的属性值
        /// <para>如果属性存在，则更新其值；如果属性不存在，则添加新的属性。</para>
        /// </summary>
        /// <param name="element"></param>
        /// <param name="name"></param>
        /// <param name="value"></param>
        /// <returns></returns>
        public static bool TrySetValue(this XElement element, string name, object value)
        {
            if (element == null) return false;
            if (string.IsNullOrWhiteSpace(name)) return false;

            var attribute = element.Attribute(name);
            if (attribute != null)
            {
                attribute.Value = value.ToString();
                return true;
            }
            else
            {
                element.Add(new XAttribute(name, value));
                return true;
            }
        }

        /// <summary>
        /// 加载 XML 文件
        /// </summary>
        /// <param name="configFile"></param>
        /// <returns></returns>
        public static XElement Load(string configFile)
        {
            if (string.IsNullOrWhiteSpace(configFile))
                throw new ArgumentException("文件路径不能为空！", nameof(configFile));

            if (!File.Exists(configFile))
                throw new FileNotFoundException("文件不存在！", configFile);

            XElement config = XElement.Load(configFile);
            config.ReplaceTemplateElements();
            config.ReplaceDictionaryValues();

            return config;
        }
    }
}
