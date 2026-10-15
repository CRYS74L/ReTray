using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ReTray_App.Controls;

// Resampling a bitmap at its source boundary drops its outer pixel coverage.
// Keep a transparent sampling border outside the original pixels and align
// the original image, rather than the padded bitmap, to the device pixel grid.
public sealed class TrayIconImage : FrameworkElement
{
    private const int Guard = 2;
    private static readonly ConditionalWeakTable<BitmapSource, BitmapSource> padded = new();
    public static readonly DependencyProperty SourceProperty = DependencyProperty.Register(
        nameof(Source), typeof(BitmapSource), typeof(TrayIconImage),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public BitmapSource? Source { get => (BitmapSource?)GetValue(SourceProperty); set => SetValue(SourceProperty, value); }
    public TrayIconImage() => RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);
    private static BitmapSource Pad(BitmapSource source)
    {
        var width = source.PixelWidth; var height = source.PixelHeight;
        var stride = (width + Guard * 2) * 4;
        var bytes = new byte[stride * (height + Guard * 2)];
        source.CopyPixels(new Int32Rect(0, 0, width, height), bytes, stride, Guard * stride + Guard * 4);
        var bitmap = BitmapSource.Create(width + Guard * 2, height + Guard * 2,
            96, 96, PixelFormats.Pbgra32, null, bytes, stride);
        bitmap.Freeze(); return bitmap;
    }
    protected override void OnRender(DrawingContext drawing)
    {
        if (Source is not { } source) return;
        var dpi = VisualTreeHelper.GetDpi(this);
        var size = Math.Round(20 * dpi.DpiScaleX);
        var scale = size / Math.Max(source.PixelWidth, source.PixelHeight);
        var width = source.PixelWidth * scale; var height = source.PixelHeight * scale;
        var x = Math.Round((ActualWidth * dpi.DpiScaleX - width) / 2);
        var y = Math.Round((ActualHeight * dpi.DpiScaleY - height) / 2);
        var bounds = new Rect(
            (x - Guard * scale) / dpi.DpiScaleX, (y - Guard * scale) / dpi.DpiScaleY,
            (width + Guard * 2 * scale) / dpi.DpiScaleX,
            (height + Guard * 2 * scale) / dpi.DpiScaleY);
        var bitmap = padded.GetValue(source, Pad);
        drawing.DrawImage(bitmap, bounds);
    }
}
