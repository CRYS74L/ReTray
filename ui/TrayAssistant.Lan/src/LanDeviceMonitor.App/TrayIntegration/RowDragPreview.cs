using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace TrayAssistant_App;

/// <summary>Shows the entire row at its original size and pointer offset during an OLE drag.</summary>
internal sealed class RowDragPreview : Adorner
{
    private readonly BitmapSource image;
    private readonly Size rowSize;
    private readonly Point hotspot;
    private Point position;

    internal RowDragPreview(FrameworkElement owner, FrameworkElement row, Point hotspot) : base(owner)
    {
        IsHitTestVisible = false;
        this.hotspot = hotspot;
        rowSize = row.RenderSize;
        var dpi = VisualTreeHelper.GetDpi(row);
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
            drawing.DrawRectangle(new VisualBrush(row), null, new Rect(rowSize));
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(rowSize.Width * dpi.DpiScaleX),
            (int)Math.Ceiling(rowSize.Height * dpi.DpiScaleY), dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        bitmap.Render(visual); bitmap.Freeze(); image = bitmap;
    }

    internal void FollowPointer()
    {
        GetCursorPos(out var cursor);
        var point = AdornedElement.PointFromScreen(new Point(cursor.X, cursor.Y));
        position = new Point(point.X - hotspot.X, point.Y - hotspot.Y);
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext drawing)
    {
        var bounds = new Rect(position, rowSize);
        drawing.DrawRectangle((Brush)FindResource("StudioSurfaceBrush"), null, bounds);
        drawing.DrawImage(image, bounds);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CursorPoint { public int X; public int Y; }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out CursorPoint point);
}
