using System.Windows;
using System.Windows.Media;
using Pen = System.Windows.Media.Pen;
using MediaBrush = System.Windows.Media.Brush;

namespace LanDeviceMonitor.Controls;

public sealed class DeviceTypeIcon : System.Windows.Controls.Control
{
    static DeviceTypeIcon()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(DeviceTypeIcon),
            new FrameworkPropertyMetadata(typeof(DeviceTypeIcon)));
    }

    public static readonly DependencyProperty DeviceTypeProperty =
        DependencyProperty.Register(
            nameof(DeviceType),
            typeof(string),
            typeof(DeviceTypeIcon),
            new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty GlyphProperty =
        DependencyProperty.Register(
            nameof(Glyph),
            typeof(string),
            typeof(DeviceTypeIcon),
            new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty IconBrushProperty =
        DependencyProperty.Register(
            nameof(IconBrush),
            typeof(MediaBrush),
            typeof(DeviceTypeIcon),
            new FrameworkPropertyMetadata(System.Windows.Media.Brushes.DodgerBlue, FrameworkPropertyMetadataOptions.AffectsRender));

    internal Geometry IconGeometry => DeviceIconCatalog.Get(DeviceType);
    public bool CenterInk { get; set; }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        var scale = Math.Min(ActualWidth, ActualHeight) / 24;
        if (scale <= 0) return;
        drawingContext.PushTransform(new TranslateTransform((ActualWidth - 24 * scale) / 2, (ActualHeight - 24 * scale) / 2));
        drawingContext.PushTransform(new ScaleTransform(scale, scale));
        var bounds = IconGeometry.Bounds;
        drawingContext.PushTransform(CenterInk ? new TranslateTransform(12-bounds.Left-bounds.Width/2,12-bounds.Top-bounds.Height/2) : Transform.Identity);
        drawingContext.DrawGeometry(null, new Pen(IconBrush, 1.7)
        { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round }, IconGeometry);
        drawingContext.Pop(); drawingContext.Pop(); drawingContext.Pop();
    }

    public string DeviceType
    {
        get => (string)GetValue(DeviceTypeProperty);
        set => SetValue(DeviceTypeProperty, value);
    }

    public string Glyph
    {
        get => (string)GetValue(GlyphProperty);
        set => SetValue(GlyphProperty, value);
    }

    public MediaBrush IconBrush
    {
        get => (MediaBrush)GetValue(IconBrushProperty);
        set => SetValue(IconBrushProperty, value);
    }
}
