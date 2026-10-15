using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ReTray_App;

// Keep the half-DIP layout slot, but never rasterize less than one device pixel.
// Snapping both edges of an integer-pixel rectangle keeps scrolling coverage stable.
public sealed class PixelAlignedDivider : Border
{
    protected override void OnDpiChanged(DpiScale oldDpi,DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi,newDpi);InvalidateVisual();
    }
    protected override void OnRender(DrawingContext drawing)
    {
        if(Background is null || RenderSize.Width<=0 || RenderSize.Height<=0)return;
        var dpi=VisualTreeHelper.GetDpi(this).DpiScaleY;
        var height=Math.Max(1,Math.Round(RenderSize.Height*dpi))/dpi;
        var top=(RenderSize.Height-height)/2;
        var guidelines=new GuidelineSet();
        guidelines.GuidelinesX.Add(0);guidelines.GuidelinesX.Add(RenderSize.Width);
        guidelines.GuidelinesY.Add(top);guidelines.GuidelinesY.Add(top+height);
        drawing.PushGuidelineSet(guidelines);
        drawing.DrawRectangle(Background,null,new Rect(0,top,RenderSize.Width,height));
        drawing.Pop();
    }
}
