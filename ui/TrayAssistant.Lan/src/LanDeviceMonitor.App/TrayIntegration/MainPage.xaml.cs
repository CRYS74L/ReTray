using System.ComponentModel;
using System.IO;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
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
    private static readonly string[] SystemNames = ["网络", "音量", "电源", "输入法", "时钟", "通知", "隐私指示器", "开始", "快速设置", "语言"];
    private readonly ToggleButton?[] systemToggles = new ToggleButton?[SystemNames.Length];
    private readonly SystemGlyphImage?[] systemGlyphs = new SystemGlyphImage?[SystemNames.Length];
    private readonly bool[] systemAvailable = new bool[SystemNames.Length];
    private readonly bool[] systemSaving = new bool[SystemNames.Length];
    private string tab = "icons";
    private Action? finishPageTransition;
    private bool loaded, systemLoading, systemLoaded, systemRefreshPending, editorOpen;
    private bool closingFloating;
    private InputMethodIconEditorModel? inputMethodEditor;
    private readonly BlurEffect floatingBlur = new() { Radius = 0, RenderingBias = RenderingBias.Performance };
    private IconRow? dragRow;
    private Point dragStart;
    private Point dragHotspot;

    public MainPage()
    {
        InitializeComponent(); DataContext = Vm;
        VersionText.Text = $"v{typeof(MainPage).Assembly.GetName().Version!.ToString(3)}";
        ThemeChoice.Items.Add("跟随系统"); ThemeChoice.Items.Add("浅色"); ThemeChoice.Items.Add("深色"); ThemeChoice.SelectedIndex = 0;
        foreach(var name in Localization.LanguageNames)LanguageChoice.Items.Add(name); LanguageChoice.SelectedIndex = 0;
        CreateSystemRows(); CreateLoadingRows();
        Vm.PropertyChanged += PresentationChanged;
        Vm.ThemeChanged += index => App.Window.ApplyTrayTheme(index);
        Vm.LanguageChanged += () => {
            var root=(FrameworkElement?)Window.GetWindow(this) ?? this;
            root.Language=System.Windows.Markup.XmlLanguage.GetLanguage(Localization.CurrentLanguage switch {"zh-Hant"=>"zh-TW","ja"=>"ja-JP","en"=>"en-US",_=>"zh-CN"});
            Localization.Apply(root);
        };
        timer.Tick += async (_, _) =>
        {
            await Vm.RefreshAsync();
            if (tab == "system" && Vm.IsReady && !systemLoading) await LoadSystemAsync(true);
        };
        Loaded += PageLoaded; Unloaded += (_, _) => { timer.Stop(); finishPageTransition?.Invoke(); };
        SizeChanged += (_, _) => FloatingCard.MaxHeight = Math.Max(0, ActualHeight - 32);
        PreviewKeyDown += async (_, args) =>
        {
            if (args.Key != Key.Escape || FloatingLayer.Visibility != Visibility.Visible) return;
            args.Handled = true; await CloseFloatingAsync();
        };

        UpdatePresentation();
    }
    private async void PageLoaded(object sender, RoutedEventArgs args)
    {
        if (!loaded)
        {
            loaded = true; await Vm.InitializeAsync();
            App.Window.Topmost = false;
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
        finishPageTransition?.Invoke();
        IconList.Visibility = tab == "icons" && Vm.IsReady ? Visibility.Visible : Visibility.Collapsed;
        LoadingList.Visibility = tab == "icons" && Vm.IsLoading ? Visibility.Visible : Visibility.Collapsed;
        SettingsView.Visibility = tab == "settings" ? Visibility.Visible : Visibility.Collapsed;
        SystemView.Visibility = tab == "system" ? Visibility.Visible : Visibility.Collapsed;
        ErrorNotice.Visibility = Vm.Error.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }
    private void Navigate(object sender, SelectionChangedEventArgs args)
    {
        if (Navigation?.SelectedItem is not SegmentItem item) return;
        var next = item.Tag?.ToString() ?? "icons";
        if (next == tab) return;
        finishPageTransition?.Invoke();
        var previous = tab;
        var outgoing = PanelFor(previous);
        tab = next;
        var incoming = PanelFor(next);
        if (PagesHost == null || !IsLoaded || !SystemParameters.ClientAreaAnimation || outgoing == incoming)
            UpdatePresentation();
        else
            SlidePage(outgoing, incoming, PageIndex(next) > PageIndex(previous) ? 1 : -1);
        if (tab == "system") _ = LoadSystemAsync(true);
    }
    private FrameworkElement PanelFor(string page) => page switch
    {
        "system" => SystemView,
        "settings" => SettingsView,
        _ => Vm.IsReady ? IconList : LoadingList
    };
    private static int PageIndex(string page) => page switch { "system" => 1, "settings" => 2, _ => 0 };
    private static ScrollViewer? ViewerFor(FrameworkElement panel)
    {
        if (panel is ScrollViewer viewer) return viewer;
        if (panel is not ListBox list) return null;
        list.ApplyTemplate();
        return list.Template.FindName("PART_ScrollViewer", list) as ScrollViewer;
    }
    private static FrameworkElement MotionFor(FrameworkElement panel)
    {
        if (ViewerFor(panel) is not { } viewer) return panel;
        viewer.ApplyTemplate();
        return (viewer.Template.FindName("DeviceViewportMask", viewer) ??
                viewer.Template.FindName("DetailViewportMask", viewer)) as FrameworkElement ?? panel;
    }
    private static System.Windows.Controls.Primitives.ScrollBar? BarFor(FrameworkElement panel)
    {
        if (ViewerFor(panel) is not { } viewer) return null;
        viewer.ApplyTemplate();
        return viewer.Template.FindName("PART_VerticalScrollBar", viewer) as System.Windows.Controls.Primitives.ScrollBar;
    }
    private void SlidePage(FrameworkElement outgoing, FrameworkElement incoming, int direction)
    {
        outgoing.Visibility = incoming.Visibility = Visibility.Visible;
        outgoing.UpdateLayout(); incoming.UpdateLayout();
        var leaving = MotionFor(outgoing); var entering = MotionFor(incoming);
        var exitTranslate = new TranslateTransform();
        var enterTranslate = new TranslateTransform(340d * direction, 0);
        leaving.RenderTransformOrigin = entering.RenderTransformOrigin = new Point(.5, .5);
        leaving.RenderTransform = exitTranslate; entering.RenderTransform = enterTranslate;
        leaving.Opacity = 1; entering.Opacity = 0;
        leaving.CacheMode = new BitmapCache { RenderAtScale = 1d };
        entering.CacheMode = new BitmapCache { RenderAtScale = 1d };
        outgoing.IsHitTestVisible = incoming.IsHitTestVisible = false;
        var duration = TimeSpan.FromMilliseconds(LanDeviceMonitor.MainWindow.GetDeviceFilterTransitionDurationMilliseconds());
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var oldBar = BarFor(outgoing); var newBar = BarFor(incoming);
        var finished = false;
        void Finish()
        {
            if (finished) return;
            finished = true; finishPageTransition = null;
            exitTranslate.BeginAnimation(TranslateTransform.XProperty, null);
            enterTranslate.BeginAnimation(TranslateTransform.XProperty, null);
            leaving.BeginAnimation(OpacityProperty, null); entering.BeginAnimation(OpacityProperty, null);
            leaving.RenderTransform = entering.RenderTransform = Transform.Identity;
            leaving.Opacity = entering.Opacity = 1;
            leaving.CacheMode = entering.CacheMode = null;
            oldBar?.BeginAnimation(OpacityProperty, null); newBar?.BeginAnimation(OpacityProperty, null);
            if (oldBar != null) oldBar.Opacity = 1;
            if (newBar != null) newBar.Opacity = 1;
            outgoing.IsHitTestVisible = incoming.IsHitTestVisible = true;
            UpdatePresentation();
        }
        finishPageTransition = Finish;
        var enter = new DoubleAnimation(340d * direction, 0, duration) { EasingFunction = ease };
        enter.Completed += (_, _) => Finish();
        exitTranslate.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, -340d * direction, duration) { EasingFunction = ease });
        enterTranslate.BeginAnimation(TranslateTransform.XProperty, enter);
        entering.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, duration) { EasingFunction = ease });
        leaving.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0, duration) { EasingFunction = ease });
        oldBar?.BeginAnimation(OpacityProperty, new DoubleAnimation(oldBar.Opacity, 0, duration) { EasingFunction = ease });
        newBar?.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, duration) { EasingFunction = ease });
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
    private void IconSettingsClick(object sender, RoutedEventArgs args)
    {
        if (editorOpen || ((Button)sender).Tag is not IconRow row) return;
        editorOpen = true; Vm.Selected = row;
        ShowFloating(new IconEditorPanel(Vm, row, CloseFloatingAsync));
    }
    private async void InputMethodSettingsClick(object sender, RoutedEventArgs args)
    {
        if(editorOpen || !Vm.IsReady)return;
        editorOpen=true;
        var model=new InputMethodIconEditorModel(Vm.Pipe);
        try {
            await model.LoadAsync(); inputMethodEditor=model;
            ShowFloating(new InputMethodIconEditor(model,CloseFloatingAsync));
        } catch(Exception ex) {editorOpen=false;model.Stop();Vm.ReportError(ex);}
    }
    private void ShowFloating(UIElement content)
    {
        finishPageTransition?.Invoke();
        FloatingCard.MaxHeight = Math.Max(0, ActualHeight - 32);
        FloatingContent.Content = content; FloatingLayer.Visibility = Visibility.Visible;
        PageContent.IsHitTestVisible = false;
        PageContent.CacheMode = new BitmapCache
        {
            RenderAtScale = VisualTreeHelper.GetDpi(PageContent).DpiScaleX
        };
        PageContent.Effect = floatingBlur;
        FloatingLayer.Opacity = 0;
        FloatingScale.ScaleX = FloatingScale.ScaleY = .975;
        FloatingTranslate.Y = 18;
        FloatingCard.RenderTransform = new TransformGroup
        {
            Children = new TransformCollection { FloatingScale, FloatingTranslate }
        };
        AnimateFloating(true);
    }
    private void AnimateFloating(bool opening, Action? completed = null)
    {
        var duration = TimeSpan.FromMilliseconds(opening ? 420 : 260);
        IEasingFunction ease = opening
            ? new QuinticEase { EasingMode = EasingMode.EaseOut }
            : new CubicEase { EasingMode = EasingMode.EaseInOut };
        if (!opening && FloatingCard.RenderTransform is not TransformGroup)
        {
            FloatingCard.RenderTransform = new TransformGroup
            {
                Children = new TransformCollection { FloatingScale, FloatingTranslate }
            };
        }
        DoubleAnimation Motion(double from, double to) => new(from, to, duration)
        {
            EasingFunction = ease, FillBehavior = FillBehavior.Stop
        };
        void AnimateValue(Animatable target, DependencyProperty property, double to)
        {
            var from = (double)target.GetValue(property);
            target.BeginAnimation(property, null); target.SetValue(property, to);
            if (SystemParameters.ClientAreaAnimation)
                target.BeginAnimation(property, Motion(from, to));
        }
        void AnimateElement(UIElement target, double to, EventHandler? finish = null)
        {
            var from = target.Opacity;
            target.BeginAnimation(OpacityProperty, null); target.Opacity = to;
            if (!SystemParameters.ClientAreaAnimation) return;
            var animation = Motion(from, to);
            if (finish != null) animation.Completed += finish;
            target.BeginAnimation(OpacityProperty, animation);
        }
        void Finish()
        {
            ClearFloatingMotion();
            if (opening) FloatingCard.RenderTransform = Transform.Identity;
            completed?.Invoke();
        }
        AnimateValue(FloatingScale, ScaleTransform.ScaleXProperty, opening ? 1 : .985);
        AnimateValue(FloatingScale, ScaleTransform.ScaleYProperty, opening ? 1 : .985);
        AnimateValue(FloatingTranslate, TranslateTransform.YProperty, opening ? 0 : 8);
        AnimateValue(floatingBlur, BlurEffect.RadiusProperty, opening ? 12 : 0);
        AnimateElement(PageContent, opening ? .4 : 1);
        AnimateElement(FloatingLayer, opening ? 1 : 0, (_, _) => Finish());
        if (!SystemParameters.ClientAreaAnimation) Finish();
    }
    private void ClearFloatingMotion()
    {
        FloatingLayer.BeginAnimation(OpacityProperty, null);
        PageContent.BeginAnimation(OpacityProperty, null);
        floatingBlur.BeginAnimation(BlurEffect.RadiusProperty, null);
        FloatingScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        FloatingScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        FloatingTranslate.BeginAnimation(TranslateTransform.YProperty, null);
    }
    private Task HideFloatingAsync()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        AnimateFloating(false, () => { ClearFloating(); completion.TrySetResult(); });
        return completion.Task;
    }
    private void ClearFloating()
    {
        ClearFloatingMotion();
        FloatingLayer.Visibility = Visibility.Collapsed; FloatingContent.Content = null;
        FloatingLayer.Opacity = 1;
        FloatingScale.ScaleX = FloatingScale.ScaleY = 1; FloatingTranslate.Y = 0;
        floatingBlur.Radius = 0;
        PageContent.IsHitTestVisible = true;
        PageContent.Effect = null; PageContent.CacheMode = null; PageContent.Opacity = 1;
    }
    private async Task CloseFloatingAsync()
    {
        if (closingFloating || !editorOpen) return;
        closingFloating = true;
        try
        {
            if (!await FlushChangesAsync()) return;
            await HideFloatingAsync(); editorOpen = false; Vm.Selected = null; inputMethodEditor=null;
        }
        finally { closingFloating = false; }
    }
    private async void FloatingBackgroundDown(object sender, MouseButtonEventArgs args)
    {
        if (ReferenceEquals(args.OriginalSource, sender)) await CloseFloatingAsync();
    }
    public void Suspend() { timer.Stop(); finishPageTransition?.Invoke(); }
    public void Resume() { timer.Start(); _ = Vm.RefreshAsync(); }
    private void DragStarting(object sender, MouseButtonEventArgs args)
    {
        dragRow = null;
        if (args.OriginalSource is not DependencyObject origin || FindAncestor<ButtonBase>(origin) != null) return;
        if (FindAncestor<ListBoxItem>(origin) is { DataContext: IconRow row } container)
        {
            dragRow = row; dragStart = args.GetPosition(IconList); dragHotspot = args.GetPosition(container);
        }
    }
    private async void DragMoving(object sender, MouseEventArgs args)
    {
        if (dragRow == null || args.LeftButton != MouseButtonState.Pressed || Vm.IsDragging) return;
        var point = args.GetPosition(IconList);
        if (Math.Abs(point.X - dragStart.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(point.Y - dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        var row = dragRow; dragRow = null; Vm.IsDragging = true;
        var container = (ListBoxItem)IconList.ItemContainerGenerator.ContainerFromItem(row);
        var layer = AdornerLayer.GetAdornerLayer(PageRoot)!;
        var preview = new RowDragPreview(PageRoot, container, dragHotspot);
        void FollowPointer(object? sender, GiveFeedbackEventArgs feedback) => preview.FollowPointer();
        try
        {
            layer.Add(preview); preview.FollowPointer();
            container.Opacity = .3;
            IconList.GiveFeedback += FollowPointer;
            try { DragDrop.DoDragDrop(IconList, new DataObject("TrayAssistant.Row", row), DragDropEffects.Move); }
            finally
            {
                IconList.GiveFeedback -= FollowPointer;
                container.Opacity = 1;
                layer.Remove(preview);
            }
            await Vm.PersistOrderAsync();
        }
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
    public async Task<bool> FlushChangesAsync() { await systemSaveGate.WaitAsync(); systemSaveGate.Release(); if(inputMethodEditor!=null && !await inputMethodEditor.FlushAsync())return false; return await Vm.FlushChangesAsync(); }
    private void CreateSystemRows()
    {
        foreach (var id in new[] { 0, 1, 2, 3, 9, 4, 6 })
        {
            if (id == 5 || id == 8) continue;
            var row = new Grid { Height = 34 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var glyph = new SystemGlyphImage { Width = 28, Height = 28, VerticalAlignment = VerticalAlignment.Center };
            glyph.SetResourceReference(SystemGlyphImage.FontFamilyProperty, "AppIconFont");
            glyph.SetResourceReference(SystemGlyphImage.ForegroundProperty, "TextBrush");
            // Windows' own symbol font supplies static symbols for controls whose
            // taskbar content is only created on demand or is a Composition animation.
            glyph.Glyphs = id switch { 3 => "\uE765", 9 => "\uE765", 4 => "\uE917", 6 => "\uE720", _ => "" };
            systemGlyphs[id] = glyph;
            if ((id == 9 ? WindowsSystemIcon.InputMethod() : id == 7 ? WindowsSystemIcon.Start() : null) is { } systemBitmap)
            {
                var mask = new Border { Width = 20, Height = 20, VerticalAlignment = VerticalAlignment.Center,
                    OpacityMask = new ImageBrush(systemBitmap) { Stretch = Stretch.Uniform } };
                mask.SetResourceReference(Border.BackgroundProperty, "TextBrush"); row.Children.Add(mask);
            }
            else row.Children.Add(glyph);
            var label = new TextBlock { Text = SystemNames[id], Margin = new Thickness(8, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(label, 1); row.Children.Add(label);
            if(id==3) {
                var more=new Button { Margin=new Thickness(0,0,8,0),
                    VerticalAlignment=VerticalAlignment.Center,Style=(Style)Resources["TrayMoreButtonStyle"] };
                var dots=new TextBlock { Text="\uE712",FontSize=12 };dots.SetResourceReference(TextBlock.FontFamilyProperty,"AppIconFont");
                dots.SetBinding(TextBlock.ForegroundProperty,new System.Windows.Data.Binding(nameof(Button.Foreground)){Source=more});
                GlyphInkAlignment.SetEnabled(dots,true);more.Content=dots;more.Click+=InputMethodSettingsClick;
                System.Windows.Automation.AutomationProperties.SetAutomationId(more,"InputMethodSettings");
                System.Windows.Automation.AutomationProperties.SetName(more,"输入法图标设置");
                Grid.SetColumn(more,2);row.Children.Add(more);
            }
            var toggle = new SwitchControl { Style = (Style)Resources["TrayRowToggle"], Tag = id, IsEnabled = false,
                Margin = new Thickness(0, 0, 4, 0) };
            System.Windows.Automation.AutomationProperties.SetAutomationId(toggle, "System" + id);
            System.Windows.Automation.AutomationProperties.SetName(toggle, SystemNames[id]);
            toggle.Click += SystemVisibilityClick; Grid.SetColumn(toggle, 3); row.Children.Add(toggle); SystemList.Children.Add(row); systemToggles[id] = toggle;
            if (id != 6)
            {
                var divider = new Border { Height = .5, Margin = new Thickness(36, 0, 0, 0) };
                divider.SetResourceReference(Border.BackgroundProperty, "BorderBrush"); SystemList.Children.Add(divider);
            }
        }
    }
    private async Task LoadSystemAsync(bool refresh = false)
    {
        if (!Vm.IsReady) return;
        if (systemLoading) { systemRefreshPending |= refresh; return; }
        if (systemLoaded && !refresh) return;
        systemLoading = true;
        if (!systemLoaded) foreach (var toggle in systemToggles.OfType<ToggleButton>()) toggle.IsEnabled = false;
        await systemSaveGate.WaitAsync();
        try
        {
            var reply = await Vm.Pipe.RequestAsync("system.read"); Check(reply);
            Array.Clear(systemAvailable);
            foreach (var item in reply["data"]!.AsArray())
            {
                var id = item!["id"]!.GetValue<int>(); if (id < 0 || id >= SystemNames.Length || systemToggles[id] is not { } toggle) continue;
                if (id != 4 && item["glyph"]?.GetValue<string>() is { Length: > 0 } glyph && systemGlyphs[id] is { } glyphView)
                {
                    glyphView.Glyphs = glyph;
                    glyphView.FontFamily = new FontFamily(item["fontFamily"]!.GetValue<string>());
                }
                var exists = (item["instances"]?.GetValue<int>() ?? 0) > 0;
                var savedHidden = item["savedHidden"]?.GetValue<bool>() ?? false;
                if (!systemSaving[id]) toggle.IsChecked = id == 6 ? !savedHidden : exists ? item["visible"]!.GetValue<bool>() : !savedHidden;
                systemAvailable[id] = id == 6 || exists || savedHidden;
            }
            for (var id = 0; id < systemToggles.Length; id++)
                if (!systemAvailable[id] && !systemSaving[id] && systemToggles[id] is { } toggle) toggle.IsChecked = false;
            systemLoaded = true;
        }
        catch (Exception exception) { Vm.ReportError(exception); }
        finally
        {
            systemSaveGate.Release(); systemLoading = false;
            for (var id = 0; id < systemToggles.Length; id++)
            {
                if (systemToggles[id] is not { } toggle) continue;
                toggle.IsEnabled = systemAvailable[id] && !systemSaving[id];
                toggle.ToolTip = systemAvailable[id] ? null : Localization.Text("当前系统没有可单独控制的图标");
            }
            if (systemRefreshPending) { systemRefreshPending = false; _ = LoadSystemAsync(true); }
        }
    }
    private async void SystemVisibilityClick(object sender, RoutedEventArgs args)
    {
        if (sender is not ToggleButton { Tag: int id, IsEnabled: true } toggle) return;
        var visible = toggle.IsChecked == true;
        systemSaving[id] = true; toggle.IsEnabled = false; await systemSaveGate.WaitAsync();
        try
        {
            Check(await Vm.Pipe.RequestAsync("system.set", new() { ["id"] = id, ["visible"] = visible }));
            Vm.Error = "";
        }
        catch (Exception exception) { toggle.IsChecked = !visible; Vm.ReportError(exception); }
        finally { systemSaveGate.Release(); systemSaving[id] = false; toggle.IsEnabled = systemAvailable[id]; }
        await LoadSystemAsync(true);
    }
    private static void Check(JsonObject reply) { if (reply["success"]?.GetValue<bool>() != true) throw new IOException(reply["detail"]?.GetValue<string>() ?? "操作失败"); }
    private async void ExitClick(object sender, RoutedEventArgs args) => await App.QuitAsync();
}
