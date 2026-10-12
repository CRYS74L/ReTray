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
public static class Localization
{
    public static bool English { get; set; }
    private static readonly Dictionary<string,string> map = new()
    {
        ["托盘图标助手"]="Tray Icon Assistant",
        ["托盘图标"]="Tray icons",
        ["已隐藏"]="Hidden",
        ["替代图标"]="Custom icon",
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
        ["图标设置"]="Icon settings",
        ["选择图标…"]="Choose icon…",
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
        ["跟随 Windows"]="Follow Windows",
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
        ["输入法 / 语言"]="Input method / language",
        ["时钟"]="Clock",
        ["通知"]="Notifications",
        ["隐私指示器"]="Privacy indicator",
        ["开始"]="Start",
        ["快速设置"]="Quick settings",
    };
    public static string Text(string text) => English ? map.GetValueOrDefault(text,text) : map.FirstOrDefault(pair=>pair.Value==text).Key??text;
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
        if (root is TextBlock) TranslateProperty(root, TextBlock.TextProperty);
        if (root is ContentControl) TranslateProperty(root, ContentControl.ContentProperty);
        if (root is HeaderedContentControl) TranslateProperty(root, HeaderedContentControl.HeaderProperty);
        if (root is HeaderedItemsControl) TranslateProperty(root, HeaderedItemsControl.HeaderProperty);
        if (root is TrayAssistant_App.Controls.BelowComboBox) TranslateProperty(root, TrayAssistant_App.Controls.BelowComboBox.HeaderProperty);
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
