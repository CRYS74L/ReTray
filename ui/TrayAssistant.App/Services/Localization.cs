#if TRAY_WPF
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Media3D;
#else
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
#endif
namespace TrayAssistant_App.Services;
public static partial class Localization
{
    public static bool English { get => CurrentLanguage == "en"; set => CurrentLanguage = value ? "en" : "zh"; }
    private static readonly Dictionary<string,string> map = new()
    {
        ["托盘图标助手"]="Tray Icon Assistant",
        ["图标资源"]="Icon files",
        ["导入结果尚未确认"]="Import result has not been confirmed",
        ["未收到导入确认，配置可能已保存，请检查当前配置。"]="Import confirmation was not received. Settings may already be saved; check the current configuration.",
        ["导入配置无效"]="Invalid configuration to import",
        ["配置校验通过"]="Configuration validated",
        ["导入版本参数无效"]="Invalid import revision",
        ["配置已改变，请重新导入"]="Configuration changed; please import again",
        ["配置版本无法递增"]="The configuration revision cannot be incremented",
        ["导入图标资源无法读取"]="Unable to read imported icon assets",
        ["导入前备份失败，配置未改变"]="Backup failed; current configuration was preserved",
        ["配置保存失败，配置未改变"]="Save failed; current configuration was preserved",
        ["配置已导入"]="Configuration imported",
        ["配置文件"]="Configuration",
        ["导出配置"]="Export",
        ["导入配置"]="Import",
        ["助手配置文件"]="Assistant configuration",
        ["配置已导出"]="Configuration exported",
        ["配置已导入，原配置已备份"]="Configuration imported; previous settings backed up",
        ["导入将替换当前配置，并自动备份原配置。是否继续？"]="Import will replace the current configuration and back up the previous settings. Continue?",
        ["配置 JSON 不能超过 2 MB。"]="Configuration JSON must not exceed 2 MB.",
        ["配置包超过大小限制。"]="The configuration package exceeds the size limit.",
        ["配置包格式或版本不受支持。"]="Unsupported configuration package format or version.",
        ["图标资源或配置文件超过大小限制。"]="The icon or configuration file exceeds the size limit.",
        ["配置包缺少格式标记或 settings.json。"]="The configuration package is missing its manifest or settings.json.",
        ["配置包中的文件数量超过限制。"]="The configuration package has too many files.",
        ["配置中的图标资源总量超过限制。"]="The icons in this configuration exceed the total size limit.",
        ["配置引用的图标资源不存在"]="An icon referenced by the configuration is missing",
        ["不支持此图标资源类型"]="Unsupported icon file type",
        ["图标资源校验失败"]="Icon integrity check failed",
        ["配置包包含非法、重复或多余的文件名称"]="The package contains an invalid, duplicate or unexpected file name",
        ["配置包中的文件超过大小限制"]="A file in the package exceeds the size limit",
        ["配置包缺少引用的图标资源或引用非法"]="A referenced icon is missing or invalid",
        ["配置包包含未被配置引用的文件。"]="The package contains an unreferenced file.",
        ["配置包的图标资源总量不能超过 256 MB。"]="The total icon size must not exceed 256 MB.",
        ["JSON 配置引用了未打包的图标资源，请导入完整 .trayconfig 文件。"]="The JSON configuration references packaged assets. Import the complete .trayconfig file.",
        ["配置 JSON 根节点必须为对象。"]="The configuration JSON root must be an object.",
        ["配置 JSON 包含重复字段"]="The configuration JSON contains a duplicate field",
        ["配置字段必须为数组"]="The configuration field must be an array",
        ["配置图标条目必须为对象。"]="An icon configuration entry must be an object.",
        ["ownIcon 必须为对象。"]="ownIcon must be an object.",
        ["图标资源路径必须为字符串"]="An icon path must be a string",
        ["配置包文件解压长度超过声明或大小限制。"]="An extracted file exceeds its declared size or the size limit.",
        ["配置包文件长度与声明不符。"]="A file size does not match its declaration.",
        ["图标资源目录不能为重解析点。"]="The icon directory must not be a reparse point.",
        ["现有图标资源不能为重解析点"]="An existing icon must not be a reparse point",
        ["现有图标资源校验失败，已保留原文件"]="Existing icon integrity check failed; the original file was preserved",
        ["当前系统没有可单独控制的图标"]="No separately controllable icon is available",
        ["托盘图标"]="Tray icons",
        ["已隐藏"]="Hidden",
        ["图标已退出"]="The icon is no longer registered",
        ["原生图标目录读取失败，请重试"]="Unable to read tray icons. Retry.",
        ["图标状态更新中，请重试"]="The icon state is updating. Retry.",
        ["替代图标"]="Custom icon",
        ["自定义悬停提示内容"]="Custom hover tooltip",
        ["显示"]="Show",
        ["任务栏"]="Taskbar",
        ["折叠区"]="Overflow",
        ["EXE / DLL 图标序号"]="EXE / DLL icon index",
        ["快捷键动作"]="Shortcut action",
        ["不活动后隐藏（秒，0 关闭）"]="Hide when inactive (seconds, 0 disables)",
        ["助手图标"]="Assistant icon",
        ["选择助手图标…"]="Choose assistant icon…",
        ["主题"]="Theme",
        ["浅色"]="Light",
        ["深色"]="Dark",
        ["保持原位置"]="Keep original location",
        ["左键"]="Left click",
        ["左键双击"]="Left double click",
        ["右键双击"]="Right double click",
        ["隐藏开始按钮"]="Hide Start",
        ["任务栏不透明度"]="Taskbar opacity",
        ["助手窗口置顶"]="Keep assistant on top",
        ["导出配置…"]="Export…",
        ["导入配置…"]="Import…",
        ["导出配置"]="Export",
        ["导入配置"]="Import",
        ["未设置"]="Not set",
        ["待确认静态图像"]="Waiting for a static image",
        ["原应用闪烁空帧"]="Original blink off frame",
        ["原应用动态帧"]="Original dynamic frame",
        ["自定义图标"]="Custom icon",
        ["已生效"]="Applied",
        ["偏好已保存并应用"]="Preferences saved and applied",
        ["顺序已应用到实际托盘"]="Ordering applied to the tray",
        ["批量可见性已应用"]="Visibility applied",
        ["已撤销上次规则操作"]="Last rule change undone",
        ["语言"]="Language",
        ["退出软件"]="Exit",
        ["设置"]="Settings",
        ["状态"]="Status",
        ["隐藏"]="Hide",
        ["原图标"]="Original icon",
        ["原始图标"]="Original tray icon",
        ["图标设置"]="Icon settings",
        ["选择图标…"]="Choose icon…",
        ["选择图标"]="Choose icon",
        ["选择替代图标"]="Choose custom icon",
        ["使用原始图标"]="Use original tray icon",
        ["使用替代图标"]="Use custom icon",
        ["留空时使用原始悬停提示内容"]="Leave blank to use the original tooltip",
        ["点击更换替代图标"]="Click to choose another custom icon",
        ["使用原图标"]="Use original icon",
        ["图标提示"]="Tooltip",
        ["位置"]="Location",
        ["更多设置"]="More settings",
        ["开机启动"]="Start at sign-in",
        ["显示助手托盘图标"]="Show assistant tray icon",
        ["保存设置"]="Save settings",
        ["高级设置"]="Advanced",
        ["系统图标"]="System icons",
        ["读取系统图标"]="Read system icons",
        ["自动按名称排序"]="Sort by name automatically",
        ["新图标位置"]="New icon location",
        ["应用排序"]="Apply ordering",
        ["跟随系统"]="Follow system",
        ["交换左右键"]="Swap mouse buttons",
        ["图标快捷键"]="Icon shortcut",
        ["打开助手快捷键"]="Open assistant shortcut",
        ["保存快捷键"]="Save shortcuts",
        ["重试"]="Retry",
        ["重新启动"]="Restart",
        ["应用"]="Apply",
        ["关闭"]="Close",
        ["右键"]="Right click",
        ["网络"]="Network",
        ["音量"]="Volume",
        ["电源"]="Power",
        ["电池"]="Battery",
        ["语言栏"]="Language bar",
        ["时间"]="Time",
        ["输入法 / 语言"]="Input method / language",
        ["输入法"]="Input method",
        ["输入法图标设置"]="Input method icon settings",
        ["没有可编辑的输入法标志"]="No input method logos available",
        ["时钟"]="Clock",
        ["通知"]="Notifications",
        ["隐私指示器"]="Privacy indicator",
        ["开始"]="Start",
        ["快速设置"]="Quick settings",
    };
    public static string Text(string text)
    {
        // Translate known error prefixes while retaining file paths and field names.
        int colon = text.IndexOf('：');
        int separatorLength = 1;
        if(colon<0){colon=text.IndexOf(": ",StringComparison.Ordinal);separatorLength=2;}
        if(colon>0) {
            var prefix=text[..colon];
            var source=canonical.GetValueOrDefault(prefix,prefix);
            if(map.ContainsKey(source)||traditional.ContainsKey(source)||japanese.ContainsKey(source))
                return Text(source)+(CurrentLanguage=="en"?": ":"：")+text[(colon+separatorLength)..];
        }
        var key = map.ContainsKey(text) || traditional.ContainsKey(text) || japanese.ContainsKey(text)
            ? text : canonical.GetValueOrDefault(text, text);
        return CurrentLanguage switch {
            "en" => map.GetValueOrDefault(key,key),
            "zh-Hant" => traditional.GetValueOrDefault(key,key),
            "ja" => japanese.GetValueOrDefault(key,key),
            _ => key
        };
    }
    private static object? Translate(object? value) => value is string text ? Text(text) : value;
    private static bool applying;
    public static void Apply(DependencyObject root)
    {
        if (applying) return;
        applying = true;
        try { ApplyCore(root); }
        finally { applying = false; }
    }
    private static void ApplyCore(DependencyObject root)
    {
#if TRAY_WPF
        var visited = new HashSet<DependencyObject>();
        var visitedResources = new HashSet<ResourceDictionary>();
        if (Application.Current != null) ApplyResources(Application.Current.Resources, visited, visitedResources);
        for (DependencyObject? ancestor = root; ancestor != null; ancestor = ParentOf(ancestor))
        {
            if (ancestor is FrameworkElement element) ApplyResources(element.Resources, visited, visitedResources);
            else if (ancestor is FrameworkContentElement content) ApplyResources(content.Resources, visited, visitedResources);
        }
        ApplyWpfCore(root, visited, visitedResources);
#else
        if(root is ListView or InfoBar)return;
        if(root is TextBlock text)text.Text=Text(text.Text);
        if(root is ContentControl content)content.Content=Translate(content.Content);
        if(root is NavigationViewItem)return;
        if(root is TextBox box){box.Header=Translate(box.Header);box.PlaceholderText=Text(box.PlaceholderText);}
        if(root is NumberBox number)number.Header=Translate(number.Header);
        if(root is Slider slider)slider.Header=Translate(slider.Header);
        if(root is ToggleSwitch toggle){toggle.Header=Translate(toggle.Header);toggle.OnContent=Translate(toggle.OnContent);toggle.OffContent=Translate(toggle.OffContent);}
        if(root is ComboBox combo){combo.Header=Translate(combo.Header);int selected=combo.SelectedIndex;bool changed=false;for(int i=0;i<combo.Items.Count;i++)if(combo.Items[i] is string item){var translated=Text(item);if(translated!=item){combo.Items[i]=translated;changed=true;}}if(changed)combo.SelectedIndex=selected;}
        if(root is Controls.BelowComboBox selector){selector.Header=Translate(selector.Header);for(int i=0;i<selector.Items.Count;i++)if(selector.Items[i] is string item&&Text(item)!=item)selector.Items[i]=Text(item);}
        if(root is Expander expander)expander.Header=Translate(expander.Header);
        for(int n=0;n<VisualTreeHelper.GetChildrenCount(root);n++)ApplyCore(VisualTreeHelper.GetChild(root,n));
#endif
    }
#if TRAY_WPF
    private sealed class TranslationState(string original)
    {
        public string Original { get; private set; } = original;
        public string Applied { get; private set; } = original;
        public string Update(string current)
        {
            if (current != Applied && current != Original) Original = current;
            Applied = Text(Original);
            return Applied;
        }
    }

    private static readonly ConditionalWeakTable<DependencyObject, Dictionary<DependencyProperty, TranslationState>> propertyText = new();
    private static readonly ConditionalWeakTable<ComboBox, Dictionary<int, TranslationState>> itemText = new();
    private static readonly ConditionalWeakTable<ResourceDictionary, Dictionary<object, TranslationState>> resourceText = new();

    private static void TranslateProperty(DependencyObject target, DependencyProperty property)
    {
        // Bound text belongs to the view model or user; translating it here would replace its value.
        if (BindingOperations.IsDataBound(target, property) || target.GetValue(property) is not string current) return;
        var originals = propertyText.GetOrCreateValue(target);
        if (!originals.TryGetValue(property, out var state)) originals[property] = state = new TranslationState(current);
        var translated = state.Update(current);
        if (translated != current) target.SetCurrentValue(property, translated);
    }

    private static DependencyObject? ParentOf(DependencyObject root)
    {
        var parent = LogicalTreeHelper.GetParent(root);
        if (parent != null) return parent;
        return root is Visual or Visual3D ? VisualTreeHelper.GetParent(root) : null;
    }

    private static void ApplyResources(ResourceDictionary resources, HashSet<DependencyObject> visited, HashSet<ResourceDictionary> visitedResources)
    {
        if (!visitedResources.Add(resources)) return;
        var originals = resourceText.GetOrCreateValue(resources);
        foreach (var key in resources.Keys.Cast<object>().ToArray())
        {
            if (resources[key] is string current)
            {
                if (!originals.TryGetValue(key, out var state)) originals[key] = state = new TranslationState(current);
                var translated = state.Update(current);
                if (translated != current) resources[key] = translated;
            }
            else if (resources[key] is DependencyObject child) ApplyWpfCore(child, visited, visitedResources);
        }
        foreach (var merged in resources.MergedDictionaries) ApplyResources(merged, visited, visitedResources);
    }

    private static void ApplyWpfCore(DependencyObject root, HashSet<DependencyObject> visited, HashSet<ResourceDictionary> visitedResources)
    {
        if (!visited.Add(root)) return;
        TranslateProperty(root, System.Windows.Automation.AutomationProperties.NameProperty);
        if (root is TextBlock) TranslateProperty(root, TextBlock.TextProperty);
        if (root is ContentControl) TranslateProperty(root, ContentControl.ContentProperty);
        if (root is HeaderedContentControl) TranslateProperty(root, HeaderedContentControl.HeaderProperty);
        if (root is HeaderedItemsControl) TranslateProperty(root, HeaderedItemsControl.HeaderProperty);

        if (root is Window) TranslateProperty(root, Window.TitleProperty);
        if (root is FrameworkElement element)
        {
            TranslateProperty(element, FrameworkElement.ToolTipProperty);
            ApplyResources(element.Resources, visited, visitedResources);
            if (element.ToolTip is DependencyObject tooltip) ApplyWpfCore(tooltip, visited, visitedResources);
            if (element.ContextMenu != null) ApplyWpfCore(element.ContextMenu, visited, visitedResources);
        }
        else if (root is FrameworkContentElement content)
        {
            TranslateProperty(content, FrameworkContentElement.ToolTipProperty);
            ApplyResources(content.Resources, visited, visitedResources);
            if (content.ToolTip is DependencyObject tooltip) ApplyWpfCore(tooltip, visited, visitedResources);
            if (content.ContextMenu != null) ApplyWpfCore(content.ContextMenu, visited, visitedResources);
        }
        if (root is ComboBox combo && combo.ItemsSource == null)
        {
            var selected = combo.SelectedIndex;
            var originals = itemText.GetOrCreateValue(combo);
            var changed = false;
            for (var i = 0; i < combo.Items.Count; i++)
            {
                if (combo.Items[i] is not string current) continue;
                if (!originals.TryGetValue(i, out var state)) originals[i] = state = new TranslationState(current);
                var translated = state.Update(current);
                if (translated != current) { combo.Items[i] = translated; changed = true; }
            }
            if (changed) combo.SetCurrentValue(ComboBox.SelectedIndexProperty, selected);
        }
        if (root is ItemsControl items)
            foreach (var item in items.Items) if (item is DependencyObject child) ApplyWpfCore(child, visited, visitedResources);
        if (root is Popup popup && popup.Child != null) ApplyWpfCore(popup.Child, visited, visitedResources);
        foreach (var logicalChild in LogicalTreeHelper.GetChildren(root))
            if (logicalChild is DependencyObject dependency) ApplyWpfCore(dependency, visited, visitedResources);
        if (root is Visual or Visual3D)
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) ApplyWpfCore(VisualTreeHelper.GetChild(root, i), visited, visitedResources);
    }
#endif
}
