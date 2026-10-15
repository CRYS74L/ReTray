using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using ReTray_App.Controls;
using ReTray_App.Models;
using ReTray_App.Services;
using ReTray_App.ViewModels;
using Localization = ReTray_App.Services.Localization;

namespace ReTray_App;

public sealed class IconEditorWindow : MaterialWindow
{
    private readonly TrayViewModel vm;
    private bool closing, allowClose;
    public IconEditorWindow(TrayViewModel vm, IconRow row)
    {
        this.vm = vm; Title = row.Name; Height = 640; DataContext = vm;
        var shell = new Grid(); shell.SetResourceReference(Panel.BackgroundProperty, "WindowTintBrush"); shell.RowDefinitions.Add(new RowDefinition { Height = new GridLength(32) }); shell.RowDefinitions.Add(new RowDefinition());
        shell.Children.Add(DesktopCaption.Create(this, row.Name, false));
        var content = new StackPanel { Margin = new Thickness(0) };
        var previews = new Grid { Margin = new Thickness(0, 4, 0, 14) };
        previews.ColumnDefinitions.Add(new ColumnDefinition()); previews.ColumnDefinitions.Add(new ColumnDefinition());
        void Preview(int column, string label, object source, string property)
        {
            var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            var image = new Image { Width = 44, Height = 44, Stretch = System.Windows.Media.Stretch.Uniform, Margin = new Thickness(0, 0, 0, 8) };
            image.SetBinding(Image.SourceProperty, new Binding(property) { Source = source });
            System.Windows.Media.RenderOptions.SetBitmapScalingMode(image, System.Windows.Media.BitmapScalingMode.HighQuality);
            stack.Children.Add(image); stack.Children.Add(new TextBlock { Text = label, HorizontalAlignment = HorizontalAlignment.Center, FontSize = 12 });
            Grid.SetColumn(stack, column); previews.Children.Add(stack);
        }
        Preview(0, "原图标", vm, "Selected.Original"); Preview(1, "替代图标", vm, nameof(vm.AssetPreview)); content.Children.Add(previews);
        var buttons = new WrapPanel { Margin = new Thickness(0, 0, 0, 14) };
        buttons.Children.Add(new Button { Content = "选择图标…", Command = vm.ChooseIconCommand, Margin = new Thickness(0, 0, 8, 0) });
        buttons.Children.Add(new Button { Content = "使用原图标", Command = vm.UseOriginalAssetCommand }); content.Children.Add(buttons);
        var summary = new TextBlock { FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 14) };
        summary.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryTextBrush"); summary.SetBinding(TextBlock.TextProperty, nameof(vm.AssetSummary)); content.Children.Add(summary);
        content.Children.Add(Label("图标提示"));
        var tip = new TextBox { MaxLength = 127, Margin = new Thickness(0, 0, 0, 14) };
        tip.SetBinding(TextBox.TextProperty, Edit(nameof(vm.Tip))); Identify(tip, "IconTip"); content.Children.Add(tip);
        var advanced = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
        advanced.Children.Add(Label("EXE / DLL 图标序号")); advanced.Children.Add(Number(nameof(vm.AssetIndex), 10000, "IconAssetIndex"));
        var swap = new Grid { MinHeight = 32, Margin = new Thickness(0, 0, 0, 14) }; swap.ColumnDefinitions.Add(new ColumnDefinition()); swap.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        swap.Children.Add(new TextBlock { Text = "交换左右键", VerticalAlignment = VerticalAlignment.Center });
        var toggle = new ToggleButton { Style = (Style)Application.Current.Resources["TrayToggleSwitch"], VerticalAlignment = VerticalAlignment.Center };
        toggle.SetBinding(ToggleButton.IsCheckedProperty, Edit(nameof(vm.SwapButtons))); Identify(toggle, "SwapButtonsToggle"); Grid.SetColumn(toggle, 1); swap.Children.Add(toggle); advanced.Children.Add(swap);
        advanced.Children.Add(Label("不活动后隐藏（秒，0 关闭）")); advanced.Children.Add(Number(nameof(vm.InactiveSeconds), 86400, "IconInactiveSeconds"));
        advanced.Children.Add(Label("快捷键"));
        var hotkey = new TextBox { IsReadOnly = true, Margin = new Thickness(0, 0, 0, 14) };
        hotkey.SetBinding(TextBox.TextProperty, nameof(vm.HotkeyText)); Identify(hotkey, "IconHotkey");
        hotkey.PreviewKeyDown += (_, args) => { var code = MainPage.HotkeyCode(args); if (code < 0) return; args.Handled = true; vm.Hotkey = code; vm.HotkeyText = TrayViewModel.DescribeHotkey(code); };
        advanced.Children.Add(hotkey);
        var action = new BelowComboBox { Header = "快捷键动作", Margin = new Thickness(0, 0, 0, 14) };
        foreach (var text in new[] { "左键", "右键", "左键双击", "右键双击" }) action.Items.Add(text);
        action.SetBinding(Selector.SelectedIndexProperty, Edit(nameof(vm.HotkeyAction))); Identify(action, "IconHotkeyActionChoice"); advanced.Children.Add(action);
        var expander = new Expander { Header = "更多设置", Content = advanced, HorizontalContentAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 0, 0, 14) };
        Identify(expander, "IconAdvancedSettings"); content.Children.Add(expander);
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = System.Windows.Media.Brushes.IndianRed, FontSize = 12 };
        error.SetBinding(TextBlock.TextProperty, nameof(vm.Error)); content.Children.Add(error);
        var scroll = new ScrollViewer { Style = (Style)Application.Current.Resources["EdgeScrollViewerStyle"], Content = content, Margin = new Thickness(14, 12, 2, 14) };
        Identify(scroll, "IconEditorScroll"); Grid.SetRow(scroll, 1); shell.Children.Add(scroll); Content = shell;
        ApplyTheme(ThemeManager.IsDark); ThemeManager.Changed += ThemeChanged;
        Loaded += (_, _) => Localization.Apply(this);
        PreviewKeyDown += (_, args) => { if (args.Key == System.Windows.Input.Key.Escape) { args.Handled = true; Close(); } };
        Closing += async (_, args) =>
        {
            if (allowClose) return; args.Cancel = true; if (closing) return;
            closing = true;
            // Leave WPF's Closing event before a completed save can call Close.
            await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Normal);
            try { if (await vm.FlushChangesAsync()) { allowClose = true; Close(); } }
            finally { closing = false; }
        };
        Closed += (_, _) => ThemeManager.Changed -= ThemeChanged;
    }
    private void ThemeChanged() => ApplyTheme(ThemeManager.IsDark);
    private static TextBlock Label(string text) => new() { Text = text, Margin = new Thickness(0, 0, 0, 6) };
    private static Binding Edit(string property) => new(property) { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged };
    private static TextBox Number(string property, double maximum, string id)
    {
        var box = new TextBox { Margin = new Thickness(0, 0, 0, 14) };
        var binding = Edit(property); binding.Converter = new BoundedNumber(maximum); box.SetBinding(TextBox.TextProperty, binding);
        box.LostKeyboardFocus += (_, _) => box.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget(); Identify(box, id); return box;
    }
    private static void Identify(DependencyObject control, string id) => System.Windows.Automation.AutomationProperties.SetAutomationId(control, id);
    private sealed class BoundedNumber(double maximum) : IValueConverter
    {
        public object Convert(object value, Type type, object parameter, CultureInfo culture) => ((double)value).ToString("0", culture);
        public object ConvertBack(object value, Type type, object parameter, CultureInfo culture) => double.TryParse(value?.ToString(), NumberStyles.Float, culture, out var number) && double.IsFinite(number) ? Math.Clamp(Math.Truncate(number), 0, maximum) : Binding.DoNothing;
    }
}

internal static class DesktopDialog
{
    public static bool Confirm(string title, string text, string primary)
    {
        var window = new MaterialWindow { Title = title, Height = 240, MinHeight = 200, Owner = App.Window, ResizeMode = ResizeMode.NoResize };
        var shell = new Grid(); shell.SetResourceReference(Panel.BackgroundProperty, "WindowTintBrush"); shell.RowDefinitions.Add(new RowDefinition { Height = new GridLength(32) }); shell.RowDefinitions.Add(new RowDefinition()); shell.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        shell.Children.Add(DesktopCaption.Create(window, title, false));
        var label = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(14, 12, 14, 14) }; Grid.SetRow(label, 1); shell.Children.Add(label);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(14) };
        var accept = new Button { Content = primary, Margin = new Thickness(0, 0, 8, 0) }; accept.Click += (_, _) => { window.DialogResult = true; };
        var cancel = new Button { Content = "取消" }; cancel.Click += (_, _) => { window.DialogResult = false; };
        System.Windows.Automation.AutomationProperties.SetAutomationId(accept, "DialogPrimary"); System.Windows.Automation.AutomationProperties.SetAutomationId(cancel, "DialogClose");
        buttons.Children.Add(accept); buttons.Children.Add(cancel); Grid.SetRow(buttons, 2); shell.Children.Add(buttons); window.Content = shell;
        window.ApplyTheme(ThemeManager.IsDark); Localization.Apply(window); return window.ShowDialog() == true;
    }
}
