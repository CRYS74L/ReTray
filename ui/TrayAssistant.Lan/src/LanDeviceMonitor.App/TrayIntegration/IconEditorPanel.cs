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
        var header = CreateHeader(row.Name, close);
        root.Children.Add(header);
        var content = new StackPanel { Margin = new Thickness(4) };
        var choice = new IconChoiceRow(vm, "Selected.Original", () => vm.AssetPreview,
            () => vm.UseReplacement, value => vm.UseReplacement = value, vm.ChooseIconCommand, bottomSpacing: 0);
        content.Children.Add(choice);
        BalanceHeaderSpacing(header, choice, content.Margin.Top);
        content.Children.Add(CreateDivider(new Thickness(0,16,0,16)));
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

    internal static Grid CreateHeader(string caption, Func<Task> close, string automationId = "IconEditorClose", bool showClose = true)
    {
        var header = new Grid { Margin = new Thickness(4, 4, 4, 0) };
        header.ColumnDefinitions.Add(new ColumnDefinition());
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var title = new TextBlock { Text = caption, FontWeight = FontWeights.Medium,
            VerticalAlignment = VerticalAlignment.Top, TextTrimming = TextTrimming.CharacterEllipsis };
        header.Children.Add(title);
        var dismiss = new Button { Width = 32, Height = 32, MinWidth = 0, MinHeight = 0,
            VerticalAlignment = VerticalAlignment.Top,
            Visibility = showClose ? Visibility.Visible : Visibility.Hidden };
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
        System.Windows.Automation.AutomationProperties.SetAutomationId(dismiss, automationId);
        System.Windows.Automation.AutomationProperties.SetName(dismiss, Localization.Text("关闭"));
        // The close target must not enlarge the title's layout height. Its circle
        // can render into the card padding without entering the clipped viewport.
        var closeHost = new Canvas { Width=32,Height=0,Margin=new Thickness(12,0,0,0),VerticalAlignment=VerticalAlignment.Top };
        closeHost.Children.Add(dismiss); Grid.SetColumn(closeHost,1); header.Children.Add(closeHost);
        title.Loaded += (_, _) => AlignHeaderInk(title, dismiss);
        title.SizeChanged += (_, _) => AlignHeaderInk(title, dismiss);
        dismiss.SizeChanged += (_, _) => AlignHeaderInk(title, dismiss);
        return header;
    }

    internal static void AddQuarterTurn(Button owner, FrameworkElement visual)
    {
        var rotation = new RotateTransform(); visual.RenderTransform = rotation; visual.RenderTransformOrigin = new Point(.5, .5);
        void Rotate(double target, int milliseconds) => rotation.BeginAnimation(RotateTransform.AngleProperty,
            new DoubleAnimation(target, TimeSpan.FromMilliseconds(SystemParameters.ClientAreaAnimation ? milliseconds : 0))
            { EasingFunction = new QuinticEase { EasingMode = EasingMode.EaseOut } });
        owner.MouseEnter += (_, _) => Rotate(90, 420);
        owner.MouseLeave += (_, _) => Rotate(0, 280);
    }

    internal static void BalanceHeaderSpacing(Grid header, IconChoiceRow choice, double contentTopInset)
    {
        var title = header.Children.OfType<TextBlock>().Single();
        void Balance()
        {
            var mode = choice.ImageMode;
            var caption = choice.OriginalCaption;
            if (header.ActualHeight <= 0 || caption.ActualHeight <= 0 || mode.ActualHeight <= 0 || title.ActualWidth <= 0) return;
            var ink = TitleInk(title);
            var captionInk = TitleInk(caption);
            if (ink.IsEmpty || captionInk.IsEmpty) return;
            // Match the two blank gaps marked by the user: title to icon, and
            // the bottom of the icon caption to the top of the mode slider.
            var gap = mode.TranslatePoint(new Point(), choice).Y - caption.TranslatePoint(new Point(captionInk.Left, captionInk.Bottom), choice).Y;
            var titleBottom = title.TranslatePoint(new Point(ink.Left, ink.Bottom), header).Y;
            var iconTop = choice.OriginalCanvas.TranslatePoint(new Point(), choice).Y + 6;
            var bottom = titleBottom + gap - header.ActualHeight - iconTop - contentTopInset;
            if (Math.Abs(header.Margin.Bottom - bottom) > .01)
                header.Margin = new Thickness(header.Margin.Left, header.Margin.Top, header.Margin.Right, bottom);
        }
        header.LayoutUpdated += (_, _) => Balance();
        title.Loaded += (_, _) => Balance();
        title.SizeChanged += (_, _) => Balance();
        choice.SizeChanged += (_, _) => Balance();
    }

    internal static Border CreateDivider(Thickness margin)
    {
        var divider = new Border { Height=1,Margin=margin,Opacity=.14 };
        divider.SetResourceReference(Border.BackgroundProperty,"TextBrush");
        System.Windows.Automation.AutomationProperties.SetAutomationId(divider,"IconEditorDivider");
        return divider;
    }

    private static Rect TitleInk(TextBlock title)
    {
        var formatted = new FormattedText(title.Text, CultureInfo.CurrentCulture, title.FlowDirection,
            new Typeface(title.FontFamily, title.FontStyle, title.FontWeight, title.FontStretch),
            title.FontSize, Brushes.Black, VisualTreeHelper.GetDpi(title).PixelsPerDip)
        {
            MaxTextWidth = title.ActualWidth, MaxLineCount = 1, Trimming = TextTrimming.CharacterEllipsis
        };
        return formatted.BuildGeometry(new Point()).Bounds;
    }

    private static void AlignHeaderInk(TextBlock title, Button dismiss)
    {
        if (title.ActualWidth <= 0 || title.ActualHeight <= 0 || dismiss.ActualHeight <= 0 ||
            string.IsNullOrEmpty(title.Text)) return;
        var ink = TitleInk(title);
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
