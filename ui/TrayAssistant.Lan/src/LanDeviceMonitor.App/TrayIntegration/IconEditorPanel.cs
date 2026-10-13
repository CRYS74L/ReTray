using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Globalization;
using System.Windows.Media;
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
        dismiss.Content = glyph;
        dismiss.Click += async (_, _) => await close();
        System.Windows.Automation.AutomationProperties.SetAutomationId(dismiss, "IconEditorClose");
        System.Windows.Automation.AutomationProperties.SetName(dismiss, Localization.Text("关闭"));
        Grid.SetColumn(dismiss, 1); header.Children.Add(dismiss); root.Children.Add(header);
        title.Loaded += (_, _) => AlignHeaderInk(title, dismiss);
        title.SizeChanged += (_, _) => AlignHeaderInk(title, dismiss);
        dismiss.SizeChanged += (_, _) => AlignHeaderInk(title, dismiss);
        var content = new StackPanel { Margin = new Thickness(4) };
        var previews = new Grid { Margin = new Thickness(0, 4, 0, 18) };
        Grid.SetIsSharedSizeScope(previews, true);
        previews.ColumnDefinitions.Add(new ColumnDefinition()); previews.ColumnDefinitions.Add(new ColumnDefinition());
        void Preview(int column, string caption, string property, string buttonText, System.Windows.Input.ICommand command)
        {
            var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            var image = new Image { Width = 44, Height = 44, Stretch = System.Windows.Media.Stretch.Uniform,
                Margin = new Thickness(0, 0, 0, 8) };
            image.SetBinding(Image.SourceProperty, new Binding(property) { Source = vm });
            System.Windows.Media.RenderOptions.SetBitmapScalingMode(image, System.Windows.Media.BitmapScalingMode.HighQuality);
            stack.Children.Add(image); stack.Children.Add(new TextBlock { Text = caption,
                HorizontalAlignment = HorizontalAlignment.Center, FontSize = 12 });
            var actionHost = new Grid { Margin = new Thickness(0, 14, 0, 0) };
            actionHost.ColumnDefinitions.Add(new ColumnDefinition
                { Width = GridLength.Auto, SharedSizeGroup = "IconEditorAction" });
            var action = new Button { Content = buttonText, Command = command,
                Height = 36, MinHeight = 36, MinWidth = 0, Padding = new Thickness(10, 0, 10, 0),
                HorizontalAlignment = HorizontalAlignment.Stretch };
            action.SetResourceReference(StyleProperty, "StudioActionButtonStyle");
            System.Windows.Automation.AutomationProperties.SetAutomationId(action,
                column == 0 ? "UseOriginalIcon" : "ChooseReplacementIcon");
            actionHost.Children.Add(action); stack.Children.Add(actionHost);
            Grid.SetColumn(stack, column); previews.Children.Add(stack);
        }
        Preview(0, "原图标", "Selected.Original", "使用原图标", vm.UseOriginalAssetCommand);
        Preview(1, "替代图标", nameof(vm.AssetPreview), "选择替代图标", vm.ChooseIconCommand);
        content.Children.Add(previews);
        var tip = new TextBox { MaxLength = 127, Height = 36, MinHeight = 36, Margin = new Thickness(0, 8, 0, 0) };
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
