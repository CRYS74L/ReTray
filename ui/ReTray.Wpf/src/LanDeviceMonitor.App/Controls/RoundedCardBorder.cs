using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace LanDeviceMonitor.Controls;

/// <summary>Draws translucent cards with one continuous, pixel-aligned outline.</summary>
public sealed class RoundedCardBorder : Border
{
    protected override void OnRender(DrawingContext drawingContext)
    {
        if (BorderThickness.Left != BorderThickness.Right ||
            BorderThickness.Left != BorderThickness.Top ||
            BorderThickness.Left != BorderThickness.Bottom ||
            CornerRadius.TopLeft != CornerRadius.TopRight ||
            CornerRadius.TopLeft != CornerRadius.BottomLeft ||
            CornerRadius.TopLeft != CornerRadius.BottomRight)
        {
            base.OnRender(drawingContext);
            return;
        }
        var size = RenderSize;
        if (size.Width <= 0 || size.Height <= 0) return;
        var dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
        var stroke = Math.Round(BorderThickness.Left * dpi) / dpi;
        var radius = Math.Min(CornerRadius.TopLeft, Math.Min(size.Width, size.Height) / 2);
        // Centered MaxWidth containers can position a card at a fractional
        // device pixel. Snap both edges of the stroke, not just its centerline;
        // otherwise a side becomes two half-opacity pixels while the top is one.
        var guidelines = new GuidelineSet();
        foreach (var x in new[] { 0, stroke, size.Width - stroke, size.Width }) guidelines.GuidelinesX.Add(x);
        foreach (var y in new[] { 0, stroke, size.Height - stroke, size.Height }) guidelines.GuidelinesY.Add(y);
        drawingContext.PushGuidelineSet(guidelines);
        if (stroke <= 0)
        {
            drawingContext.DrawRoundedRectangle(Background, null, new Rect(size), radius, radius);
        }
        else
        {
            // Keep fill and outline disjoint. Painting a translucent outline on
            // top of the fill increases its alpha and creates a bright edge.
            var inner = new Rect(stroke, stroke, Math.Max(0, size.Width - 2 * stroke), Math.Max(0, size.Height - 2 * stroke));
            var innerRadius = Math.Max(0, radius - stroke);
            drawingContext.DrawRoundedRectangle(Background, null, inner, innerRadius, innerRadius);
            if (BorderBrush is not null)
            {
                var ring = new GeometryGroup { FillRule = FillRule.EvenOdd };
                ring.Children.Add(new RectangleGeometry(new Rect(size), radius, radius));
                ring.Children.Add(new RectangleGeometry(inner, innerRadius, innerRadius));
                drawingContext.DrawGeometry(BorderBrush, null, ring);
            }
        }
        drawingContext.Pop();
    }
}
