using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Xml.Linq;

namespace LanDeviceMonitor.Controls;

// Keep the approved SVG's geometry and seven paint gradients as native WPF
// drawings. The caption is rasterized once at the monitor's actual DPI.
public static class BrandArtwork
{
    private static readonly Lazy<DrawingImage> Artwork = new(Create);
    public static DrawingImage Image => Artwork.Value;

    private static DrawingImage Create()
    {
        using var stream = Application.GetResourceStream(new Uri(
            "/ReTray.App;component/Assets/BrandLogo.svg", UriKind.Relative))!.Stream;
        var svg = XDocument.Load(stream);
        var elements = svg.Descendants().Where(e => e.Attribute("id") != null)
            .ToDictionary(e => (string)e.Attribute("id")!);
        double Number(XElement e, string name, double fallback = 0) =>
            double.TryParse((string?)e.Attribute(name), NumberStyles.Float,
                CultureInfo.InvariantCulture, out var value) ? value : fallback;
        GradientBrush Gradient(string id)
        {
            var e = elements[id];
            GradientBrush brush;
            if (e.Name.LocalName == "radialGradient")
            {
                var numbers = System.Text.RegularExpressions.Regex.Matches(
                    (string)e.Attribute("gradientTransform")!, @"[-+]?(?:\d*\.)?\d+")
                    .Select(m => double.Parse(m.Value, CultureInfo.InvariantCulture)).ToArray();
                brush = new RadialGradientBrush
                {
                    Center = new Point(0, 0), GradientOrigin = new Point(0, 0),
                    RadiusX = 1, RadiusY = 1, MappingMode = BrushMappingMode.Absolute,
                    Transform = new MatrixTransform(numbers[2], 0, 0, numbers[3], numbers[0], numbers[1])
                };
            }
            else
            {
                brush = new LinearGradientBrush
                {
                    StartPoint = new Point(Number(e, "x1"), Number(e, "y1")),
                    EndPoint = new Point(Number(e, "x2"), Number(e, "y2", 1)),
                    MappingMode = id.StartsWith("hub-")
                        ? BrushMappingMode.RelativeToBoundingBox : BrushMappingMode.Absolute
                };
            }
            brush.ColorInterpolationMode = ColorInterpolationMode.SRgbLinearInterpolation;
            foreach (var stop in e.Elements().Where(s => s.Name.LocalName == "stop"))
            {
                var color = (Color)ColorConverter.ConvertFromString((string)stop.Attribute("stop-color")!);
                color.A = (byte)Math.Round(255 * Number(stop, "stop-opacity", 1));
                brush.GradientStops.Add(new GradientStop(color, Number(stop, "offset")));
            }
            brush.Freeze();
            return brush;
        }
        DrawingBrush Paint(params string[] ids)
        {
            var layers = new DrawingGroup();
            foreach (var id in ids)
                layers.Children.Add(new GeometryDrawing(Gradient(id), null,
                    new RectangleGeometry(new Rect(0, 0, 1024, 1024))));
            var paint = new DrawingBrush(layers)
            {
                Viewbox = new Rect(0, 0, 1024, 1024), Viewport = new Rect(0, 0, 1024, 1024),
                ViewboxUnits = BrushMappingMode.Absolute, ViewportUnits = BrushMappingMode.Absolute,
                Stretch = Stretch.Fill, TileMode = TileMode.None
            };
            paint.Freeze();
            return paint;
        }
        var arrow = Geometry.Parse((string)elements["arrow"].Attribute("d")!);
        var right = arrow.Clone();
        right.Transform = new RotateTransform(180, 512, 512);
        var hub = svg.Descendants().First(e => e.Name.LocalName == "circle" &&
            (string?)e.Attribute("cx") == "512" && (string?)e.Attribute("cy") == "512");
        var drawing = new DrawingGroup();
        using (var dc = drawing.Open())
        {
            const double scale = 1024d / 656;
            dc.PushTransform(new MatrixTransform(scale, 0, 0, scale, -184 * scale, -184 * scale));
            dc.DrawGeometry(Paint("left-base", "left-glow-0"), null, arrow);
            dc.DrawGeometry(Paint("right-base", "right-glow-0", "right-glow-1"), null, right);
            var hubLayers = new DrawingGroup();
            var disk = new EllipseGeometry(new Point(512, 512), Number(hub, "r"), Number(hub, "r"));
            hubLayers.Children.Add(new GeometryDrawing(Gradient("hub-v-left"), null, disk));
            hubLayers.Children.Add(new DrawingGroup
            {
                OpacityMask = new LinearGradientBrush(Colors.Transparent, Colors.White, new Point(0, 0), new Point(1, 0)),
                Children = { new GeometryDrawing(Gradient("hub-v-right"), null, disk) }
            });
            dc.DrawDrawing(hubLayers);
            dc.Pop();
        }
        // A DrawingImage has no SVG viewBox. Preserve that viewport explicitly;
        // WPF's arc tessellation can otherwise expand bounds by a fraction of a pixel.
        var viewport = new DrawingGroup { ClipGeometry = new RectangleGeometry(new Rect(0, 0, 1024, 1024)) };
        viewport.Children.Add(new GeometryDrawing(Brushes.Transparent, null,
            new RectangleGeometry(new Rect(0, 0, 1024, 1024))));
        viewport.Children.Add(drawing);
        var image = new DrawingImage(viewport);
        image.Freeze();
        return image;
    }
}

// Use fractional glyph outlines and grayscale coverage on the translucent
// title surface. Display grid fitting and an independently rounded baseline
// made small letters look stepped and displaced them from the adjacent logo.
public sealed class BrandCaption : FrameworkElement
{
    public static readonly DependencyProperty ForegroundProperty = DependencyProperty.Register(
        nameof(Foreground), typeof(Brush), typeof(BrandCaption),
        new FrameworkPropertyMetadata(Brushes.Black, FrameworkPropertyMetadataOptions.AffectsRender));
    public Brush Foreground { get => (Brush)GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }
    public BrandCaption()
    {
        UseLayoutRounding = true;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal);
        TextOptions.SetTextRenderingMode(this, TextRenderingMode.Grayscale);
    }
    private FormattedText Fit(out Rect ink, out double opticalCenter)
    {
        var typeface = new Typeface(TextElement.GetFontFamily(this), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        var text = new FormattedText("ReTray", CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight, typeface, 13, Foreground, null,
            TextFormattingMode.Ideal, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        ink = text.BuildGeometry(new Point()).Bounds;
        opticalCenter = ink.Top + ink.Height / 2;
        // A descending y gives the word a much lower outline bound than the
        // main letter bodies. Balance the full outline with the capital body,
        // so a mathematically centered box does not leave the word looking high.
        if (typeface.TryGetGlyphTypeface(out var glyph))
            opticalCenter = (opticalCenter + text.Baseline - 13 * glyph.CapsHeight / 2) / 2;
        return text;
    }
    protected override Size MeasureOverride(Size availableSize)
    {
        Fit(out var ink, out _);
        return new Size(Math.Ceiling(ink.Width + 2), 16);
    }
    protected override void OnRender(DrawingContext dc)
    {
        var text = Fit(out var ink, out var opticalCenter);
        var dpi = VisualTreeHelper.GetDpi(this);
        var x = Math.Round(-ink.Left * dpi.DpiScaleX) / dpi.DpiScaleX;
        // Keep the optical text center on the logo center. Do not round the
        // baseline separately: at 125/150% that moves it by almost a pixel.
        var y = ActualHeight / 2 - opticalCenter;
        dc.DrawText(text, new Point(x, y));
    }
}
