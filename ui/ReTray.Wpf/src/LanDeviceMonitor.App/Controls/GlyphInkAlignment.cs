using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Brushes = System.Windows.Media.Brushes;
using Point = System.Windows.Point;

namespace LanDeviceMonitor.Controls;

// Symbol fonts include asymmetric line-box bearings. Center the visible ink in
// the existing text box without changing the button's layout or hit area.
public static class GlyphInkAlignment
{
    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached(
        "Enabled", typeof(bool), typeof(GlyphInkAlignment), new PropertyMetadata(false, Changed));
    public static bool GetEnabled(DependencyObject element) => (bool)element.GetValue(EnabledProperty);
    public static void SetEnabled(DependencyObject element, bool value) => element.SetValue(EnabledProperty, value);

    private static void Changed(DependencyObject element, DependencyPropertyChangedEventArgs args)
    {
        if (element is not TextBlock text) return;
        text.Loaded -= Loaded;
        text.SizeChanged -= Resized;
        if (args.NewValue is true)
        {
            text.Loaded += Loaded;
            text.SizeChanged += Resized;
            if (text.IsLoaded) QueueAlignment(text);
        }
        else text.ClearValue(UIElement.RenderTransformProperty);
    }

    private static void Loaded(object sender, RoutedEventArgs args) => QueueAlignment((TextBlock)sender);
    private static void Resized(object sender, SizeChangedEventArgs args) => QueueAlignment((TextBlock)sender);
    private static void QueueAlignment(TextBlock text) => text.Dispatcher.BeginInvoke(
        System.Windows.Threading.DispatcherPriority.Loaded, new Action(() => { if (GetEnabled(text)) Align(text); }));

    private static void Align(TextBlock text)
    {
        if (text.ActualWidth <= 0 || text.ActualHeight <= 0 || string.IsNullOrEmpty(text.Text)) return;
        var formatted = new FormattedText(text.Text, CultureInfo.CurrentCulture, text.FlowDirection,
            new Typeface(text.FontFamily, text.FontStyle, text.FontWeight, text.FontStretch),
            text.FontSize, Brushes.Black, VisualTreeHelper.GetDpi(text).PixelsPerDip);
        var ink = formatted.BuildGeometry(new Point()).Bounds;
        if (ink.IsEmpty) return;
        text.RenderTransform = Transform.Identity;
        text.UpdateLayout();
        DependencyObject? parent = VisualTreeHelper.GetParent(text);
        while (parent is not null && parent is not System.Windows.Controls.Primitives.ButtonBase)
            parent = VisualTreeHelper.GetParent(parent);
        var center = new Point(text.ActualWidth / 2, text.ActualHeight / 2);
        if (parent is FrameworkElement button)
        {
            var origin = text.TranslatePoint(new Point(), button);
            center = new Point(button.ActualWidth / 2 - origin.X, button.ActualHeight / 2 - origin.Y);
        }
        text.RenderTransform = new TranslateTransform(center.X - ink.Left - ink.Width / 2,
            center.Y - ink.Top - ink.Height / 2);
    }
}
