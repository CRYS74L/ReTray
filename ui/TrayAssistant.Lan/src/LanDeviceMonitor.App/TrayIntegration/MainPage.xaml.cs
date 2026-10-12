using Microsoft.Win32;
using System.ComponentModel;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using TrayAssistant_App.Controls;
using LanDeviceMonitor.Controls;
using TrayAssistant_App.Models;
using TrayAssistant_App.Services;
using TrayAssistant_App.ViewModels;
using Localization = TrayAssistant_App.Services.Localization;

namespace TrayAssistant_App;

public partial class MainPage : UserControl
{
    public TrayViewModel Vm { get; } = new();
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly SemaphoreSlim systemSaveGate = new(1, 1);
    private static readonly string[] SystemNames = ["网络", "音量", "电源", "输入法 / 语言", "时钟", "通知", "隐私指示器", "开始", "快速设置"];
    private readonly ToggleButton[] systemToggles = new ToggleButton[SystemNames.Length];
    private readonly bool[] systemAvailable = new bool[SystemNames.Length];
    private string tab = "icons";
    private bool loaded, systemLoading, systemLoaded, systemRefreshPending, editorOpen;
    private IconRow? dragRow;
    private Point dragStart;

    public MainPage()
    {
        InitializeComponent(); DataContext = Vm;
        ThemeChoice.Items.Add("跟随 Windows"); ThemeChoice.Items.Add("浅色"); ThemeChoice.Items.Add("深色"); ThemeChoice.SelectedIndex = 0;
        LanguageChoice.Items.Add("简体中文"); LanguageChoice.Items.Add("English"); LanguageChoice.SelectedIndex = 0;
        CreateSystemRows(); CreateLoadingRows();
        Vm.PropertyChanged += PresentationChanged;
        Vm.ThemeChanged += index => App.Window.ApplyTrayTheme(index);
        Vm.LanguageChanged += () => Localization.Apply(this);
        Vm.WindowStyleChanged += (topmost, _) => App.Window.Topmost = topmost;
        timer.Tick += async (_, _) => await Vm.RefreshAsync();
        Loaded += PageLoaded; Unloaded += (_, _) => timer.Stop();

        UpdatePresentation();
    }
    private async void PageLoaded(object sender, RoutedEventArgs args)
    {
        if (!loaded)
        {
            loaded = true; await Vm.InitializeAsync();
            OpenHotkey.Text = TrayViewModel.DescribeHotkey(Vm.Hotkeys["open"]?.GetValue<int>() ?? 0);
            App.Window.Topmost = Vm.UiTopmost;
        }
        Localization.Apply(this); timer.Start();
    }
    private void PresentationChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(Vm.IsReady) or nameof(Vm.IsLoading) or nameof(Vm.Error)) UpdatePresentation();
        if (args.PropertyName == nameof(Vm.IsReady)) { if (Vm.IsReady) _ = LoadSystemAsync(); else systemLoaded = false; }
    }
    private void UpdatePresentation()
    {
        if (IconList == null) return;
        IconList.Visibility = tab == "icons" && Vm.IsReady ? Visibility.Visible : Visibility.Collapsed;
        LoadingList.Visibility = tab == "icons" && Vm.IsLoading ? Visibility.Visible : Visibility.Collapsed;
        SettingsView.Visibility = tab == "settings" ? Visibility.Visible : Visibility.Collapsed;
        DiagnosticsView.Visibility = tab == "diagnostics" ? Visibility.Visible : Visibility.Collapsed;
        ErrorNotice.Visibility = Vm.Error.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }
    private void Navigate(object sender, SelectionChangedEventArgs args)
    {
        if (Navigation?.SelectedItem is not SegmentItem item) return;
        tab = item.Tag?.ToString() ?? "icons";
        UpdatePresentation(); if (tab == "settings") _ = LoadSystemAsync(true);
    }
    private void CreateLoadingRows()
    {
        for (var index = 0; index < 10; index++)
        {
            var row = new Grid { Height = 34, Opacity = 0.45 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) }); row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(76) });
            var icon = new Border { Width = 20, Height = 20, CornerRadius = new CornerRadius(4), VerticalAlignment = VerticalAlignment.Center };
            icon.SetResourceReference(Border.BackgroundProperty, "ControlHoverBrush"); row.Children.Add(icon);
            var label = new Border { Height = 10, Width = 90 + index % 3 * 22, Margin = new Thickness(8, 0, 0, 0), CornerRadius = new CornerRadius(4), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
            label.SetResourceReference(Border.BackgroundProperty, "ControlHoverBrush"); Grid.SetColumn(label, 1); row.Children.Add(label);
            var toggle = new Border { Width = 38, Height = 23, CornerRadius = new CornerRadius(11.5), VerticalAlignment = VerticalAlignment.Center };
            toggle.SetResourceReference(Border.BackgroundProperty, "ControlHoverBrush"); Grid.SetColumn(toggle, 2); row.Children.Add(toggle); LoadingRows.Children.Add(row);
        }
    }
    private async void IconNativeAction(object sender, TrayIconActionEventArgs args)
    {
        if (sender is TrayIconButton { Tag: IconRow row }) await Vm.NativeActionAsync(row, args.Action, args.Keyboard, args.X, args.Y);
    }
    private async void VisibilityClick(object sender, RoutedEventArgs args)
    {
        var toggle = (ToggleButton)sender;
        if (toggle.Tag is not IconRow row || !ReferenceEquals(toggle.DataContext, row) || Vm.IsDragging) return;
        await Vm.SetRowVisibilityAsync(row, toggle.IsChecked == true);
        if (ReferenceEquals(toggle.Tag, row)) toggle.SetCurrentValue(ToggleButton.IsCheckedProperty, row.Visible);
    }
    private async void IconSettingsClick(object sender, RoutedEventArgs args)
    {
        if (editorOpen || ((Button)sender).Tag is not IconRow row) return;
        editorOpen = true; Vm.Selected = row;
        try { new IconEditorWindow(Vm, row) { Owner = App.Window }.ShowDialog(); }
        finally { await Vm.FlushChangesAsync(); Vm.Selected = null; editorOpen = false; }
    }
    private void DragStarting(object sender, MouseButtonEventArgs args)
    {
        dragRow = null;
        if (args.OriginalSource is not DependencyObject origin || FindAncestor<ButtonBase>(origin) != null) return;
        if (FindAncestor<ListBoxItem>(origin)?.DataContext is IconRow row) { dragRow = row; dragStart = args.GetPosition(IconList); }
    }
    private async void DragMoving(object sender, MouseEventArgs args)
    {
        if (dragRow == null || args.LeftButton != MouseButtonState.Pressed || Vm.IsDragging) return;
        var point = args.GetPosition(IconList);
        if (Math.Abs(point.X - dragStart.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(point.Y - dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        var row = dragRow; dragRow = null; Vm.IsDragging = true;
        try { DragDrop.DoDragDrop(IconList, new DataObject("TrayAssistant.Row", row), DragDropEffects.Move); await Vm.PersistOrderAsync(); }
        finally { Vm.IsDragging = false; await Vm.RefreshAsync(); }
    }
    private void DragOverRow(object sender, DragEventArgs args)
    {
        args.Effects = args.Data.GetDataPresent("TrayAssistant.Row") ? DragDropEffects.Move : DragDropEffects.None; args.Handled = true;
    }
    private void DropRow(object sender, DragEventArgs args)
    {
        if (args.Data.GetData("TrayAssistant.Row") is not IconRow source || args.OriginalSource is not DependencyObject origin) return;
        var target = FindAncestor<ListBoxItem>(origin)?.DataContext as IconRow;
        if (target == null || ReferenceEquals(source, target)) return;
        var from = Vm.Icons.IndexOf(source); var to = Vm.Icons.IndexOf(target);
        if (from >= 0 && to >= 0) Vm.Icons.Move(from, to);
        args.Handled = true;
    }
    private static T? FindAncestor<T>(DependencyObject? node) where T : DependencyObject
    {
        while (node != null)
        {
            if (node is T match) return match;
            node = node is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(node) : (node as FrameworkContentElement)?.Parent;
        }
        return null;
    }
    public async Task<bool> FlushChangesAsync() { await systemSaveGate.WaitAsync(); systemSaveGate.Release(); return await Vm.FlushChangesAsync(); }
    private void CreateSystemRows()
    {
        for (var id = 0; id < SystemNames.Length; id++)
        {
            var row = new Grid { MinHeight = 36, Margin = new Thickness(0, 10, 0, 10) };
            row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(new TextBlock { Text = SystemNames[id], Margin = new Thickness(0, 0, 20, 0), VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap });
            var toggle = new SwitchControl { Style = (Style)Resources["SettingToggle"], Tag = id, IsEnabled = false };
            System.Windows.Automation.AutomationProperties.SetAutomationId(toggle, "System" + id);
            System.Windows.Automation.AutomationProperties.SetName(toggle, SystemNames[id]);
            toggle.Click += SystemVisibilityClick; Grid.SetColumn(toggle, 1); row.Children.Add(toggle); SystemList.Children.Add(row); systemToggles[id] = toggle;
            if (id + 1 < SystemNames.Length)
            {
                var divider = new Border { Height = 1, Margin = new Thickness(0, 5, 0, 5) };
                divider.SetResourceReference(Border.BackgroundProperty, "BorderBrush"); SystemList.Children.Add(divider);
            }
        }
    }
    private async Task LoadSystemAsync(bool refresh = false)
    {
        if (!Vm.IsReady) return;
        if (systemLoading) { systemRefreshPending |= refresh; return; }
        if (systemLoaded && !refresh) return;
        systemLoading = true; foreach (var toggle in systemToggles) toggle.IsEnabled = false;
        await systemSaveGate.WaitAsync();
        try
        {
            var reply = await Vm.Pipe.RequestAsync("system.read"); Check(reply);
            foreach (var item in reply["data"]!.AsArray())
            {
                var id = item!["id"]!.GetValue<int>(); if (id < 0 || id >= SystemNames.Length) continue;
                systemToggles[id].IsChecked = item["visible"]!.GetValue<bool>(); systemAvailable[id] = (item["instances"]?.GetValue<int>() ?? 0) > 0;
            }
            systemLoaded = true;
        }
        catch (Exception exception) { Vm.ReportError(exception); }
        finally
        {
            systemSaveGate.Release(); systemLoading = false;
            for (var id = 0; id < systemToggles.Length; id++) systemToggles[id].IsEnabled = systemAvailable[id];
            if (systemRefreshPending) { systemRefreshPending = false; _ = LoadSystemAsync(true); }
        }
    }
    private async void SystemVisibilityClick(object sender, RoutedEventArgs args)
    {
        if (sender is not ToggleButton { Tag: int id, IsEnabled: true } toggle || systemLoading) return;
        var visible = toggle.IsChecked == true; toggle.IsEnabled = false; await systemSaveGate.WaitAsync();
        try
        {
            var read = await Vm.Pipe.RequestAsync("settings.read"); Check(read);
            var policies = (JsonArray?)read["data"]?["systemControls"]?.DeepClone() ?? new();
            for (var index = policies.Count - 1; index >= 0; index--) if (policies[index]!["id"]!.GetValue<int>() == id) policies.RemoveAt(index);
            policies.Add(new JsonObject { ["id"] = id, ["hidden"] = !visible });
            Check(await Vm.Pipe.RequestAsync("system.apply", new() { ["controls"] = policies }));
        }
        catch (Exception exception) { toggle.IsChecked = !visible; Vm.ReportError(exception); }
        finally { systemSaveGate.Release(); toggle.IsEnabled = systemAvailable[id] && !systemLoading; }
    }
    private void OpenHotkeyKeyDown(object sender, KeyEventArgs args)
    {
        var code = HotkeyCode(args); if (code < 0) return;
        args.Handled = true; Vm.Hotkeys["open"] = code; OpenHotkey.Text = TrayViewModel.DescribeHotkey(code); Vm.QueueMenuPreferences();
    }
    internal static int HotkeyCode(KeyEventArgs args)
    {
        var key = args.Key == Key.System ? args.SystemKey : args.Key;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin) return -1;
        if (key is Key.Back or Key.Delete) return 0;
        var modifiers = (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) ? 2 : 0) | (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt) ? 1 : 0) | (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 4 : 0) | (Keyboard.IsKeyDown(Key.LWin) || Keyboard.IsKeyDown(Key.RWin) ? 8 : 0);
        return modifiers == 0 ? 0 : (modifiers << 16) | KeyInterop.VirtualKeyFromKey(key);
    }
    private async void ExportClick(object sender, RoutedEventArgs args)
    {
        try
        {
            var picker = new SaveFileDialog { FileName = "托盘助手配置", DefaultExt = ".json", Filter = "助手配置|*.json" };
            if (picker.ShowDialog(App.Window) != true) return;
            var reply = await Vm.Pipe.RequestAsync("settings.read"); Check(reply);
            var settings = (JsonObject)reply["data"]!.DeepClone(); var assets = new JsonObject();
            foreach (var rule in settings["rules"]!.AsArray()) await Embed(rule!.AsObject(), assets);
            await Embed(settings["ownIcon"]!.AsObject(), assets); settings["assets"] = assets;
            await File.WriteAllTextAsync(picker.FileName, settings.ToJsonString(new() { WriteIndented = true }));
            Vm.Status = "配置与图标已导出";
        }
        catch (Exception exception) { Vm.ReportError(exception); }
    }
    private static async Task Embed(JsonObject node, JsonObject assets)
    {
        var path = node["asset"]?.GetValue<string>() ?? ""; if (path.Length == 0) return;
        var bytes = await File.ReadAllBytesAsync(path); var name = Convert.ToHexString(SHA256.HashData(bytes)) + Path.GetExtension(path);
        assets[name] = Convert.ToBase64String(bytes); node["asset"] = name;
    }
    private async void ImportClick(object sender, RoutedEventArgs args)
    {
        try
        {
            var picker = new OpenFileDialog { Filter = "助手配置|*.json", CheckFileExists = true };
            if (picker.ShowDialog(App.Window) != true) return;
            if (new FileInfo(picker.FileName).Length > 96 * 1024 * 1024) throw new IOException("配置文件过大");
            var bundle = JsonNode.Parse(await File.ReadAllTextAsync(picker.FileName))!.AsObject();
            if (bundle["schema"]?.GetValue<int>() != 1) throw new IOException("配置版本不兼容");
            if (!DesktopDialog.Confirm("导入配置？", "将替换当前规则与偏好。原配置会自动保留为备份。", "导入")) return;
            var settings = await BackupService.PrepareImportAsync(bundle);
            Check(await Vm.Pipe.RequestAsync("settings.import", new() { ["settings"] = settings }));
            await Vm.InitializeAsync(); systemLoaded = false; await LoadSystemAsync(true);
            OpenHotkey.Text = TrayViewModel.DescribeHotkey(Vm.Hotkeys["open"]?.GetValue<int>() ?? 0);
        }
        catch (Exception exception) { Vm.ReportError(exception); }
    }
    private static void Check(JsonObject reply) { if (reply["success"]?.GetValue<bool>() != true) throw new IOException(reply["detail"]?.GetValue<string>() ?? "操作失败"); }
    private async void ExitClick(object sender, RoutedEventArgs args) => await App.QuitAsync();
}
