using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
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
        ["恢复默认"]="Reset",
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
        ["恢复托盘快捷键"]="Restore tray shortcut",
        ["保存快捷键"]="Save shortcuts",
        ["停止后台"]="Stop background",
        ["恢复所有托盘图标"]="Restore all tray icons",
        ["重试"]="Retry",
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
    public static void Apply(DependencyObject root)
    {
        if(root is ListView or InfoBar)return;
        if(root is TextBlock text)text.Text=Text(text.Text);
        if(root is ContentControl content)content.Content=Translate(content.Content);
        if(root is TextBox box){box.Header=Translate(box.Header);box.PlaceholderText=Text(box.PlaceholderText);}
        if(root is NumberBox number)number.Header=Translate(number.Header);
        if(root is Slider slider)slider.Header=Translate(slider.Header);
        if(root is ToggleSwitch toggle){toggle.Header=Translate(toggle.Header);toggle.OnContent=Translate(toggle.OnContent);toggle.OffContent=Translate(toggle.OffContent);}
        if(root is ComboBox combo){combo.Header=Translate(combo.Header);int selected=combo.SelectedIndex;for(int i=0;i<combo.Items.Count;i++)if(combo.Items[i] is string item)combo.Items[i]=Text(item);combo.SelectedIndex=selected;}
        if(root is Expander expander)expander.Header=Translate(expander.Header);
        for(int n=0;n<VisualTreeHelper.GetChildrenCount(root);n++)Apply(VisualTreeHelper.GetChild(root,n));
    }
}



