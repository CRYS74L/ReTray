using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Pen=System.Windows.Media.Pen;
using Point=System.Windows.Point;

namespace LanDeviceMonitor.Controls;

/// <summary>A circle outline is stroked directly, without subtracting two touching curved fills.</summary>
public sealed class CircleButtonBorder : Border
{
    protected override void OnRender(DrawingContext dc)
    {
        var dpi=VisualTreeHelper.GetDpi(this).DpiScaleX;
        var stroke=Math.Round(BorderThickness.Left*dpi)/dpi;
        var center=new Point(RenderSize.Width/2,RenderSize.Height/2);
        // Keep antialiased coverage inside the visual's native clipping bounds.
        var radius=Math.Max(0,Math.Min(RenderSize.Width,RenderSize.Height)/2-1/dpi);
        dc.DrawEllipse(Background,null,center,Math.Max(0,radius-stroke),Math.Max(0,radius-stroke));
        if(BorderBrush is not null && stroke>0)dc.DrawEllipse(null,new Pen(BorderBrush,stroke),center,Math.Max(0,radius-stroke/2),Math.Max(0,radius-stroke/2));
    }
}
