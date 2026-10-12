using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace LanDeviceMonitor.Controls;

public static class DeviceListViewport
{
    private sealed record FadeState(double Height, double Top, double Bottom, System.Windows.Media.Brush Brush);
    private static readonly DependencyProperty FadeStateProperty = DependencyProperty.RegisterAttached(
        "FadeState", typeof(FadeState), typeof(DeviceListViewport));
    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached(
        "Enabled", typeof(bool), typeof(DeviceListViewport), new PropertyMetadata(false, Attach));
    public static bool GetEnabled(DependencyObject target) => (bool)target.GetValue(EnabledProperty);
    public static void SetEnabled(DependencyObject target, bool value) => target.SetValue(EnabledProperty, value);
    private static void Attach(DependencyObject target, DependencyPropertyChangedEventArgs args)
    {
        if (target is not ScrollViewer viewer || args.NewValue is not true) return;
        viewer.Loaded += (_, _) => Update(viewer);
        viewer.ScrollChanged += (_, _) => Update(viewer);
        viewer.SizeChanged += (_, _) => Update(viewer);
    }
    private static void Update(ScrollViewer viewer)
    {
        if (viewer.Template.FindName("DeviceViewportMask",viewer) is not null && viewer.Template.FindName("PART_VerticalScrollBar",viewer) is System.Windows.Controls.Primitives.ScrollBar bar)
        {
            bar.Margin=new Thickness(0,0,0,5);
            bar.ApplyTemplate();
            if (bar.Template.FindName("PART_Track",bar) is FrameworkElement track) track.Margin=new Thickness(0);
        }
        if ((viewer.Template.FindName("DeviceViewportMask",viewer) ?? viewer.Template.FindName("DetailViewportMask",viewer)) is not FrameworkElement viewport) return;
        var height=Math.Max(1,viewport.ActualHeight);
        // Keep alpha zero at a clipped edge. Grow the fade's length as content
        // moves beyond that edge; changing endpoint alpha leaves a visible cut.
        var top=Math.Min(6,Math.Max(0,viewer.VerticalOffset));
        var bottom=Math.Min(12,Math.Max(0,viewer.ScrollableHeight-viewer.VerticalOffset));
        if (viewport.GetValue(FadeStateProperty) is FadeState previous &&
            previous.Height == height && previous.Top == top && previous.Bottom == bottom &&
            ReferenceEquals(viewport.OpacityMask, previous.Brush)) return;
        var brush=new LinearGradientBrush {StartPoint=new System.Windows.Point(0,0),EndPoint=new System.Windows.Point(0,1)};
        for(var step=0;step<=8;step++)
        {
            var position=step/8d;
            var smooth=position*position*(3-2*position);
            var opacity=1-(1-smooth)*(1-smooth);
            brush.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromArgb((byte)Math.Round(opacity*255),0,0,0),position*top/height));
        }
        foreach(var (position,opacity) in new[]{(0d,1d),(.25,.84375),(.5,.5),(.75,.15625),(1d,0d)})
            brush.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromArgb((byte)Math.Round(opacity*255),0,0,0),1-bottom/height+position*bottom/height));
        brush.Freeze();viewport.OpacityMask=brush;
        viewport.SetValue(FadeStateProperty, new FadeState(height, top, bottom, brush));
    }
}
