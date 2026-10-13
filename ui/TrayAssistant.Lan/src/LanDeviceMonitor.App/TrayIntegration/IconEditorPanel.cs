using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Globalization;
using System.Windows.Media;
using System.Windows.Media.Animation;
using LanDeviceMonitor.Controls;
using TrayAssistant_App.Models;
using TrayAssistant_App.ViewModels;
using Localization = TrayAssistant_App.Services.Localization;

namespace TrayAssistant_App;

public sealed class IconEditorPanel : UserControl
{
    public IconEditorPanel(TrayViewModel vm, IconRow row, Func<Task> close)
    {
        DataContext = vm;
        Focusable = true;
        System.Windows.Input.KeyboardNavigation.SetIsTabStop(this, false);
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition());
        var header = new Grid { Margin = new Thickness(4, 4, 4, 14) };
        header.ColumnDefinitions.Add(new ColumnDefinition());
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var title = new TextBlock { Text = row.Name, FontWeight = FontWeights.Medium,
            VerticalAlignment = VerticalAlignment.Top, TextTrimming = TextTrimming.CharacterEllipsis };
        header.Children.Add(title);
        var dismiss = new Button { Width = 32, Height = 32, MinWidth = 0, MinHeight = 0,
            VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(12, 0, 0, 0) };
        dismiss.SetResourceReference(StyleProperty, "FloatingCloseButtonStyle");
        var glyph = new TextBlock { Text = "\uE8BB", FontSize = 14 };
        glyph.SetResourceReference(TextBlock.FontFamilyProperty, "AppIconFont");
        glyph.SetBinding(TextBlock.ForegroundProperty, new Binding(nameof(Button.Foreground)) { Source = dismiss });
        GlyphInkAlignment.SetEnabled(glyph, true);
        var closeVisual = new Grid { Width = 24, Height = 24 };
        glyph.HorizontalAlignment = HorizontalAlignment.Center; glyph.VerticalAlignment = VerticalAlignment.Center;
        closeVisual.Children.Add(glyph); dismiss.Content = closeVisual;
        AddQuarterTurn(dismiss, closeVisual);
        dismiss.Click += async (_, _) => await close();
        System.Windows.Automation.AutomationProperties.SetAutomationId(dismiss, "IconEditorClose");
        System.Windows.Automation.AutomationProperties.SetName(dismiss, Localization.Text("关闭"));
        Grid.SetColumn(dismiss, 1); header.Children.Add(dismiss); root.Children.Add(header);
        title.Loaded += (_, _) => AlignHeaderInk(title, dismiss);
        title.SizeChanged += (_, _) => AlignHeaderInk(title, dismiss);
        dismiss.SizeChanged += (_, _) => AlignHeaderInk(title, dismiss);
        var content = new StackPanel { Margin = new Thickness(4) };
        var previews = new Grid { Margin = new Thickness(0, 4, 0, 18) };
        previews.ColumnDefinitions.Add(new ColumnDefinition()); previews.ColumnDefinitions.Add(new ColumnDefinition());
        StackPanel Preview(int column, string caption, FrameworkElement visual)
        {
            var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            visual.Margin = new Thickness(0, 0, 0, 8);
            visual.HorizontalAlignment = HorizontalAlignment.Center;
            stack.Children.Add(visual); stack.Children.Add(new TextBlock { Text = caption,
                HorizontalAlignment = HorizontalAlignment.Center, FontSize = 12 });
            Grid.SetColumn(stack, column); previews.Children.Add(stack);
            return stack;
        }
        var originalImage = new Image { Width = 44, Height = 44, Stretch = Stretch.Uniform };
        originalImage.SetBinding(Image.SourceProperty, new Binding("Selected.Original") { Source = vm });
        RenderOptions.SetBitmapScalingMode(originalImage, BitmapScalingMode.HighQuality);
        System.Windows.Automation.AutomationProperties.SetAutomationId(originalImage, "OriginalIconPreview");
        var originalCanvas = new Grid { Width = 56, Height = 56 };
        originalImage.HorizontalAlignment = HorizontalAlignment.Center; originalImage.VerticalAlignment = VerticalAlignment.Center;
        originalCanvas.Children.Add(originalImage);
        var original = Preview(0, "原始图标", originalCanvas);
        var choose = new Button { Width = 56, Height = 56, MinWidth = 0, MinHeight = 0,
            Command = vm.ChooseIconCommand };
        choose.SetResourceReference(StyleProperty, "ReplacementIconButtonStyle");
        System.Windows.Automation.AutomationProperties.SetAutomationId(choose, "ChooseReplacementIcon");
        System.Windows.Automation.AutomationProperties.SetName(choose, Localization.Text("选择替代图标"));
        var choiceVisual = new Grid();
        var replacementImage = new Image { Width = 44, Height = 44, Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        System.Windows.Automation.AutomationProperties.SetAutomationId(replacementImage, "ReplacementIconPreview");
        replacementImage.SetBinding(Image.SourceProperty, new Binding(nameof(vm.AssetPreview)) { Source = vm });
        RenderOptions.SetBitmapScalingMode(replacementImage, BitmapScalingMode.HighQuality);
        choiceVisual.Children.Add(replacementImage);
        var plus = new TextBlock { Text = "\uE710", FontSize = 18,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        plus.SetResourceReference(TextBlock.FontFamilyProperty, "AppIconFont");
        GlyphInkAlignment.SetEnabled(plus, true);
        System.Windows.Automation.AutomationProperties.SetAutomationId(plus, "ReplacementIconAdd");
        var plusVisual = new Grid { Width = 24, Height = 24, HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center };
        plusVisual.Children.Add(plus); choiceVisual.Children.Add(plusVisual); choose.Content = choiceVisual;
        AddQuarterTurn(choose, plusVisual);
        choose.ToolTip = Localization.Text("点击更换替代图标");
        void UpdateChoice(bool animate)
        {
            var hasImage = vm.AssetPreview != null;
            var hover = choose.IsMouseOver;
            var duration = TimeSpan.FromMilliseconds(animate && SystemParameters.ClientAreaAnimation ? 240 : 0);
            replacementImage.BeginAnimation(OpacityProperty, new DoubleAnimation(hasImage && hover ? .2 : 1, duration)
                { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            plusVisual.BeginAnimation(OpacityProperty, new DoubleAnimation(!hasImage || hover ? 1 : 0, duration)
                { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        }
        choose.MouseEnter += (_, _) => UpdateChoice(true); choose.MouseLeave += (_, _) => UpdateChoice(true);
        System.ComponentModel.PropertyChangedEventHandler previewChanged = (_, args) =>
        { if (args.PropertyName == nameof(vm.AssetPreview)) UpdateChoice(true); };
        Loaded += (_, _) => { vm.PropertyChanged += previewChanged; UpdateChoice(false); };
        Unloaded += (_, _) => vm.PropertyChanged -= previewChanged;
        var replacement = Preview(1, "替代图标", choose);
        content.Children.Add(previews);
        var mode = new SegmentedControl { Height = 36, MinHeight = 36,
            HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 0, 0, 18),
            FontSize = 14, AnimationMilliseconds = 480 };
        SegmentItem ModeItem(string caption)
        {
            var item = new SegmentItem();
            var text = new TextBlock { Text = caption };
            text.SetBinding(TextBlock.ForegroundProperty, new Binding(nameof(SegmentItem.Foreground)) { Source = item });
            item.Content = text; return item;
        }
        var originalMode = ModeItem("使用原始图标");
        var replacementMode = ModeItem("使用替代图标");
        System.Windows.Automation.AutomationProperties.SetAutomationId(mode, "IconImageMode");
        System.Windows.Automation.AutomationProperties.SetAutomationId(originalMode, "IconModeOriginal");
        System.Windows.Automation.AutomationProperties.SetAutomationId(replacementMode, "IconModeReplacement");
        mode.Items.Add(originalMode); mode.Items.Add(replacementMode);
        var originalMove = new TranslateTransform(); original.RenderTransform = originalMove;
        var replacementMove = new TranslateTransform(); replacement.RenderTransform = replacementMove;
        int generation = 0;
        void ShowMode(bool animate)
        {
            var currentGeneration = ++generation;
            var custom = mode.SelectedIndex == 1;
            var wasVisible = replacement.Visibility == Visibility.Visible && replacement.Opacity > 0;
            var replacementX = custom && !wasVisible ? 8 : replacementMove.X;
            var previous = original.TranslatePoint(new Point(), previews).X;
            originalMove.BeginAnimation(TranslateTransform.XProperty, null); originalMove.X = 0;
            Grid.SetColumnSpan(original, custom ? 1 : 2);
            if (!animate || !IsLoaded || !SystemParameters.ClientAreaAnimation)
            {
                replacement.BeginAnimation(OpacityProperty, null);
                replacementMove.BeginAnimation(TranslateTransform.XProperty, null); replacementMove.X = 0;
                replacement.Opacity = custom ? 1 : 0;
                replacement.Visibility = custom ? Visibility.Visible : Visibility.Collapsed;
                return;
            }
            replacement.Visibility = Visibility.Visible;
            previews.UpdateLayout();
            var target = original.TranslatePoint(new Point(), previews).X;
            var duration = TimeSpan.FromMilliseconds(animate && IsLoaded && SystemParameters.ClientAreaAnimation ? mode.AnimationMilliseconds : 0);
            originalMove.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(previous - target, 0, duration)
                { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            var fade = new DoubleAnimation(custom ? 1 : 0, duration)
                { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            fade.Completed += (_, _) => { if (currentGeneration == generation && !custom) replacement.Visibility = Visibility.Collapsed; };
            replacement.BeginAnimation(OpacityProperty, fade);
            replacementMove.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(replacementX, custom ? 0 : 8, duration)
                { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        }
        mode.SelectedIndex = vm.UseReplacement ? 1 : 0;
        ShowMode(false);
        mode.SelectionChanged += (_, args) =>
        {
            if (!ReferenceEquals(args.Source, mode)) return;
            vm.UseReplacement = mode.SelectedIndex == 1;
            ShowMode(true);
        };
        content.Children.Add(mode);
        var tip = new TextBox { MaxLength = 127, Height = 36, MinHeight = 36, Margin = new Thickness(0, 8, 0, 0) };
        InputPlaceholder.SetText(tip, Localization.Text("留空时使用原始悬停提示内容"));
        tip.Loaded += (_, _) => { tip.ApplyTemplate(); if (tip.Template.FindName("Placeholder", tip) is TextBlock hint) hint.Opacity = .4; };
        tip.SetBinding(TextBox.TextProperty, new Binding(nameof(vm.Tip))
            { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
        System.Windows.Automation.AutomationProperties.SetAutomationId(tip, "IconTip");
        var label = new TextBlock { Text = "自定义悬停提示内容", Margin = new Thickness(15, 0, 0, 0) };
        FieldLabelAlignment.SetTarget(label, tip); content.Children.Add(label); content.Children.Add(tip);
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new Thickness(0, 10, 0, 0) };
        error.SetResourceReference(TextBlock.ForegroundProperty, "DangerTextBrush");
        var errorStyle = new Style(typeof(TextBlock), TryFindResource(typeof(TextBlock)) as Style);
        var emptyError = new DataTrigger { Binding = new Binding(nameof(vm.Error)), Value = "" };
        emptyError.Setters.Add(new Setter(VisibilityProperty, Visibility.Collapsed));
        errorStyle.Triggers.Add(emptyError); error.Style = errorStyle;
        error.SetBinding(TextBlock.TextProperty, nameof(vm.Error)); content.Children.Add(error);
        var scroll = new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        scroll.SetResourceReference(StyleProperty, "IconEditorScrollViewerStyle");
        System.Windows.Automation.AutomationProperties.SetAutomationId(scroll, "IconEditorScroll");
        DeviceListViewport.SetEnabled(scroll, true); Grid.SetRow(scroll, 1); root.Children.Add(scroll);
        Content = root;
        System.Windows.Automation.AutomationProperties.SetAutomationId(this, "IconEditorPanel");
        Loaded += (_, _) => { Localization.Apply(this); Focus(); };
    }

    private static void AddQuarterTurn(Button owner, FrameworkElement visual)
    {
        var rotation = new RotateTransform(); visual.RenderTransform = rotation; visual.RenderTransformOrigin = new Point(.5, .5);
        void Rotate(double target, int milliseconds) => rotation.BeginAnimation(RotateTransform.AngleProperty,
            new DoubleAnimation(target, TimeSpan.FromMilliseconds(SystemParameters.ClientAreaAnimation ? milliseconds : 0))
            { EasingFunction = new QuinticEase { EasingMode = EasingMode.EaseOut } });
        owner.MouseEnter += (_, _) => Rotate(90, 420);
        owner.MouseLeave += (_, _) => Rotate(0, 280);
    }

    private static void AlignHeaderInk(TextBlock title, Button dismiss)
    {
        if (title.ActualWidth <= 0 || title.ActualHeight <= 0 || dismiss.ActualHeight <= 0 ||
            string.IsNullOrEmpty(title.Text)) return;
        var formatted = new FormattedText(title.Text, CultureInfo.CurrentCulture, title.FlowDirection,
            new Typeface(title.FontFamily, title.FontStyle, title.FontWeight, title.FontStretch),
            title.FontSize, Brushes.Black, VisualTreeHelper.GetDpi(title).PixelsPerDip)
        {
            MaxTextWidth = title.ActualWidth, MaxLineCount = 1, Trimming = TextTrimming.CharacterEllipsis
        };
        var ink = formatted.BuildGeometry(new Point()).Bounds;
        // Match the visible glyph edges to the content inset, excluding font line bearings.
        if (ink.IsEmpty) return;
        title.RenderTransform = new TranslateTransform(-ink.Left, -ink.Top);
        // Use layout slots; TranslatePoint can still include the previous render transform
        // while an alignment change is waiting for WPF's next arrange pass.
        var caption = System.Windows.Controls.Primitives.LayoutInformation.GetLayoutSlot(title);
        var button = System.Windows.Controls.Primitives.LayoutInformation.GetLayoutSlot(dismiss);
        var offset = caption.Y + title.Margin.Top + ink.Height / 2 - button.Y - dismiss.Margin.Top - dismiss.ActualHeight / 2;
        // The square close button shares the top/right inset. Move it diagonally so
        // its centered cross stays level with the title and equally inset on both edges.
        dismiss.RenderTransform = new TranslateTransform(-offset, offset);
    }
}
