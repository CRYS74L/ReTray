using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using LanDeviceMonitor.Controls;
using LanDeviceMonitor.Services;
using TrayAssistant_App.Models;
using TrayAssistant_App.ViewModels;
using Localization = TrayAssistant_App.Services.Localization;

namespace TrayAssistant_App;

public sealed class IconEditorWindow : LanDeviceMonitor.MainWindow
{
    private bool closing, allowClose;
    public IconEditorWindow(TrayViewModel vm, IconRow row) : base(true)
    {
        Title = row.Name; Height = 640; ShowInTaskbar = false; DataContext = vm;
        var content = new StackPanel();
        var previews = new Grid { Margin = new Thickness(0, 4, 0, 14) };
        previews.ColumnDefinitions.Add(new ColumnDefinition()); previews.ColumnDefinitions.Add(new ColumnDefinition());
        void Preview(int column, string caption, string property)
        {
            var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            var image = new Image { Width = 44, Height = 44, Stretch = System.Windows.Media.Stretch.Uniform, Margin = new Thickness(0, 0, 0, 8) };
            image.SetBinding(Image.SourceProperty, new Binding(property) { Source = vm });
            System.Windows.Media.RenderOptions.SetBitmapScalingMode(image, System.Windows.Media.BitmapScalingMode.HighQuality);
            stack.Children.Add(image); stack.Children.Add(new TextBlock { Text = caption, HorizontalAlignment = HorizontalAlignment.Center, FontSize = 12 });
            Grid.SetColumn(stack, column); previews.Children.Add(stack);
        }
        Preview(0, "原图标", "Selected.Original"); Preview(1, "替代图标", nameof(vm.AssetPreview)); content.Children.Add(previews);
        var buttons = new WrapPanel { Margin = new Thickness(0, 0, 0, 14) };
        buttons.Children.Add(new Button { Style = (Style)FindResource("StudioActionButtonStyle"), Content = "选择图标…", Command = vm.ChooseIconCommand, Margin = new Thickness(0, 0, 8, 0) });
        buttons.Children.Add(new Button { Style = (Style)FindResource("StudioActionButtonStyle"), Content = "使用原图标", Command = vm.UseOriginalAssetCommand }); content.Children.Add(buttons);
        var summary = new TextBlock { FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 14) };
        summary.SetResourceReference(TextBlock.ForegroundProperty, "MutedTextBrush"); summary.SetBinding(TextBlock.TextProperty, nameof(vm.AssetSummary)); content.Children.Add(summary);
        var tip = new TextBox { MaxLength = 127, Margin = new Thickness(0, 8, 0, 14) };
        tip.SetBinding(TextBox.TextProperty, Edit(nameof(vm.Tip))); Identify(tip, "IconTip"); content.Children.Add(Label("图标提示", tip)); content.Children.Add(tip);
        var advanced = new StackPanel { Margin = new Thickness(0, 12, 0, 8) };
        var assetIndex = Number(nameof(vm.AssetIndex), 10000, "IconAssetIndex");
        advanced.Children.Add(Label("EXE / DLL 图标序号", assetIndex)); advanced.Children.Add(assetIndex);
        var swap = new Grid { MinHeight = 36, Margin = new Thickness(0, 10, 0, 14) };
        swap.ColumnDefinitions.Add(new ColumnDefinition()); swap.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        swap.Children.Add(new TextBlock { Text = "交换左右键", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 20, 0) });
        var toggle = new SwitchControl(); toggle.SetBinding(ToggleButton.IsCheckedProperty, Edit(nameof(vm.SwapButtons)));
        Identify(toggle, "SwapButtonsToggle"); Grid.SetColumn(toggle, 1); swap.Children.Add(toggle); advanced.Children.Add(swap);
        var inactive = Number(nameof(vm.InactiveSeconds), 86400, "IconInactiveSeconds");
        advanced.Children.Add(Label("不活动后隐藏（秒，0 关闭）", inactive)); advanced.Children.Add(inactive);
        var hotkey = new TextBox { IsReadOnly = true, Margin = new Thickness(0, 8, 0, 14) };
        hotkey.SetBinding(TextBox.TextProperty, nameof(vm.HotkeyText)); Identify(hotkey, "IconHotkey");
        hotkey.PreviewKeyDown += (_, e) => { var code = MainPage.HotkeyCode(e); if (code < 0) return; e.Handled = true; vm.Hotkey = code; vm.HotkeyText = TrayViewModel.DescribeHotkey(code); };
        advanced.Children.Add(Label("快捷键", hotkey)); advanced.Children.Add(hotkey);
        var action = new ComboBox { Margin = new Thickness(0, 8, 0, 14) };
        foreach (var label in new[] { "左键", "右键", "左键双击", "右键双击" }) action.Items.Add(label);
        action.SetBinding(Selector.SelectedIndexProperty, Edit(nameof(vm.HotkeyAction))); Identify(action, "IconHotkeyActionChoice");
        advanced.Children.Add(Label("快捷键动作", action)); advanced.Children.Add(action);
        var disclosure = new HistoryDisclosure { Child = advanced, TotalCount = 1, VisibleCount = 0 };
        var more = new Button { Style = (Style)FindResource("StudioActionButtonStyle"), Width = double.NaN, MinWidth = 0, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 8) };
        var moreContent = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(14, 0, 8, 0) };
        var moreText = new TextBlock { Text = "更多设置", VerticalAlignment = VerticalAlignment.Center };
        moreText.SetBinding(TextBlock.ForegroundProperty, new Binding(nameof(Button.Foreground)) { Source = more });
        var chevron = new RotatingChevron { Width = 36, Height = 36, IsHitTestVisible = false };
        chevron.SetBinding(RotatingChevron.StrokeProperty, new Binding(nameof(Button.Foreground)) { Source = more });
        moreContent.Children.Add(moreText); moreContent.Children.Add(chevron); more.Content = moreContent;
        more.Click += (_, _) => { disclosure.VisibleCount = disclosure.VisibleCount == 0 ? 1 : 0; chevron.IsReversed = disclosure.VisibleCount > 0; };
        Identify(more, "IconAdvancedSettings"); content.Children.Add(more); content.Children.Add(disclosure);
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12 }; error.SetResourceReference(TextBlock.ForegroundProperty, "DangerTextBrush");
        error.SetBinding(TextBlock.TextProperty, nameof(vm.Error)); content.Children.Add(error);
        var scroll = new ScrollViewer { Style = (Style)FindResource("FadingDetailScrollViewerStyle"), Content = content, Margin = new Thickness(14, 12, 0, 0) };
        DeviceListViewport.SetEnabled(scroll, true); Identify(scroll, "IconEditorScroll"); TrayContent = scroll;
        Loaded += (_, _) => Localization.Apply(this);
        ThemeService.PaletteChanged += RefreshAppearance;
        PreviewKeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Escape) { e.Handled = true; Close(); } };
        Closing += async (_, e) =>
        {
            if (allowClose) { PrepareForExit(); return; }
            e.Cancel = true; if (closing) return; closing = true;
            await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Normal);
            try { if (await vm.FlushChangesAsync()) { allowClose = true; Close(); } }
            finally { closing = false; }
        };
        Closed += (_, _) => ThemeService.PaletteChanged -= RefreshAppearance;
    }
    private void RefreshAppearance() => MicaWindowService.Apply(this, ThemeService.ResolveIsDark(ThemeService.CurrentMode));
    private static TextBlock Label(string text, FrameworkElement input)
    {
        var label = new TextBlock { Text = text, Margin = new Thickness(15, 0, 0, 0) };
        FieldLabelAlignment.SetTarget(label, input); return label;
    }
    private static Binding Edit(string name) => new(name) { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged };
    private static TextBox Number(string name, double maximum, string id)
    {
        var box = new TextBox { Margin = new Thickness(0, 8, 0, 14) };
        var binding = Edit(name); binding.Converter = new BoundedNumber(maximum); box.SetBinding(TextBox.TextProperty, binding);
        box.LostKeyboardFocus += (_, _) => box.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget(); Identify(box, id); return box;
    }
    private static void Identify(DependencyObject value, string id) => System.Windows.Automation.AutomationProperties.SetAutomationId(value, id);
    private sealed class BoundedNumber(double maximum) : IValueConverter
    {
        public object Convert(object value, Type type, object parameter, CultureInfo culture) => ((double)value).ToString("0", culture);
        public object ConvertBack(object value, Type type, object parameter, CultureInfo culture) => double.TryParse(value?.ToString(), NumberStyles.Float, culture, out var number) ? Math.Clamp(Math.Truncate(number), 0, maximum) : Binding.DoNothing;
    }
}

internal static class DesktopDialog
{
    public static bool Confirm(string title, string text, string primary)
    {
        var window = new ConfirmationWindow { Title = title, Owner = App.Window };
        var content = new Grid { Margin = new Thickness(14) };
        content.RowDefinitions.Add(new RowDefinition()); content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        var accept = new Button { Content = primary, Style = (Style)window.FindResource("PrimaryButtonStyle"), Margin = new Thickness(0, 0, 8, 0) }; accept.Click += (_, _) => window.DialogResult = true;
        var cancel = new Button { Content = "取消" }; cancel.Click += (_, _) => window.DialogResult = false;
        buttons.Children.Add(accept); buttons.Children.Add(cancel); Grid.SetRow(buttons, 1); content.Children.Add(buttons); window.TrayContent = content;
        Localization.Apply(window); return window.ShowDialog() == true;
    }
    private sealed class ConfirmationWindow : LanDeviceMonitor.MainWindow
    {
        public ConfirmationWindow() : base(true) { Height = 240; MinHeight = 200; ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false; }
    }
}
