using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LanDeviceMonitor.Controls;

public static class CaptionReferenceTests
{
    public static void Run()
    {
        var app = Application.Current ?? new Application();
        LanDeviceMonitor.Services.ThemeService.Apply("Light");
        var family = (FontFamily)app.Resources["AppTextFont"];
        var face = new Typeface(family, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        Require(face.TryGetGlyphTypeface(out var glyph) && glyph.StyleSimulations == StyleSimulations.None &&
            glyph.FontUri.ToString().EndsWith("/notosanssc-regular.ttf", StringComparison.OrdinalIgnoreCase),
            "Reference tests must use the application's genuine embedded Regular font");

        foreach (double scale in new[] { 1d, 1.25, 1.5, 2d })
        {
            // An Image with Source=null arranges to an empty render size even
            // when Width/Height are specified. Use the actual vector artwork;
            // opacity isolates the R raster without changing the icon layout.
            var icon = new Image
            {
                Source = BrandArtwork.Image, Opacity = 0,
                Width = 16, Height = 16, Margin = new Thickness(0, 0, 10, 0)
            };
            var caption = new TextBlock
            {
                Text = "ReTray", Style = (Style)app.Resources["BodyTextStyle"],
                FontSize = 13, VerticalAlignment = VerticalAlignment.Center,
                UseLayoutRounding = false, SnapsToDevicePixels = false
            };
            var original = caption.RenderTransform;
            var row = new StackPanel
            {
                Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center, UseLayoutRounding = true
            };
            row.Children.Add(icon);
            row.Children.Add(caption);
            var root = new Grid { Width = 144, Height = 40 };
            root.Children.Add(row);
            VisualTreeHelper.SetRootDpi(root, new DpiScale(scale, scale));
            CaptionLetterAlignment.SetReference(caption, icon);
            Layout(root);
            ReportOutlineReference(caption, scale);
            AssertGeometricCenter(caption, icon, root, scale);
            Require(caption.GetType() == typeof(TextBlock) &&
                ReferenceEquals(caption.Style, app.Resources["BodyTextStyle"]) &&
                caption.Margin == new Thickness() && caption.LayoutTransform == Transform.Identity,
                "Caption must retain ordinary body TextBlock rendering with no layout compensation margin");

            var owned = caption.RenderTransform;
            var firstPosition = caption.TranslatePoint(new Point(), root);
            for (int i = 0; i < 32; i++)
            {
                root.InvalidateArrange();
                Layout(root);
            }
            Require(ReferenceEquals(owned, caption.RenderTransform) &&
                owned is TransformGroup { Children.Count: 2 } &&
                Math.Abs(firstPosition.Y - caption.TranslatePoint(new Point(), root).Y) < 1e-7,
                "Repeated layout stacked transforms or drifted the caption");

            // Render R alone at the same placement; no descender, whole-word
            // bounding box, or alpha centroid is used as the reference.
            caption.Text = "R";
            Layout(root);
            AssertGeometricCenter(caption, icon, root, scale);
            AssertRasterCenter(root, icon, scale);

            caption.Text = "ReTray";
            caption.FontSize = 16;
            icon.Height = 20;
            icon.Margin = new Thickness(0, 3, 10, 0);
            Layout(root);
            AssertGeometricCenter(caption, icon, root, scale);
            caption.FontWeight = FontWeights.Medium;
            Layout(root);
            AssertGeometricCenter(caption, icon, root, scale);

            double changedScale = scale == 2 ? 1 : 2;
            VisualTreeHelper.SetRootDpi(root, new DpiScale(changedScale, changedScale));
            root.InvalidateMeasure();
            Layout(root);
            AssertGeometricCenter(caption, icon, root, changedScale);

            caption.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
            caption.FontSize = 14;
            caption.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
            Layout(root);
            AssertGeometricCenter(caption, icon, root, changedScale);
            CaptionLetterAlignment.SetReference(caption, null);
            Require(ReferenceEquals(original, caption.RenderTransform),
                "Removing the reference did not restore the original rendering transform");
            CaptionLetterAlignment.SetReference(caption, icon);
            Layout(root);
            AssertGeometricCenter(caption, icon, root, changedScale);
            CaptionLetterAlignment.SetReference(caption, null);
        }
    }

    private static void Layout(FrameworkElement root)
    {
        root.Measure(new Size(root.Width, root.Height));
        root.Arrange(new Rect(0, 0, root.Width, root.Height));
        root.UpdateLayout();
    }

    private static void AssertGeometricCenter(TextBlock caption, Image icon, Visual root, double scale)
    {
        // Independent reference: the actual typeface's capital R outline is
        // baseline-relative. This deliberately does not reuse helper metrics.
        var face = new Typeface(caption.FontFamily, caption.FontStyle, caption.FontWeight, caption.FontStretch);
        Require(icon.ActualWidth > 0 && icon.ActualHeight > 0,
            "Reference Image must have a source and a nonempty arranged render size");
        Require(face.TryGetGlyphTypeface(out var glyph), "Caption font failed to resolve");
        var bounds = glyph.GetGlyphOutline(glyph.CharacterToGlyphMap['R'], caption.FontSize, caption.FontSize).Bounds;
        Require(!bounds.IsEmpty && double.IsFinite(caption.BaselineOffset),
            "Caption has no real R outline or laid-out first-line baseline");
        var captionCenter = caption.TransformToAncestor(root).Transform(
            new Point(0, caption.BaselineOffset + bounds.Top + bounds.Height / 2));
        var iconCenter = icon.TransformToAncestor(root).Transform(new Point(icon.ActualWidth / 2, icon.ActualHeight / 2));
        Require(Math.Abs(captionCenter.Y - iconCenter.Y) < 1e-6,
            $"Capital R outline center differs from icon center at {scale * 100}%: {captionCenter.Y:R} vs {iconCenter.Y:R}");
        Console.WriteLine($"Caption R {scale * 100}%: outline={captionCenter.Y:F6}, icon={iconCenter.Y:F6}, font={glyph.FontUri}");
    }

    private static void ReportOutlineReference(TextBlock caption, double scale)
    {
        var face = new Typeface(caption.FontFamily, caption.FontStyle, caption.FontWeight, caption.FontStretch);
        Require(face.TryGetGlyphTypeface(out var glyph), "Caption font failed to resolve");
        var direct = glyph.GetGlyphOutline(glyph.CharacterToGlyphMap['R'], caption.FontSize, caption.FontSize).Bounds;
        var formatted = new FormattedText("R", caption.Language.GetSpecificCulture(), caption.FlowDirection,
            face, caption.FontSize, Brushes.Black, null, TextOptions.GetTextFormattingMode(caption),
            VisualTreeHelper.GetDpi(caption).PixelsPerDip);
        var formattedBounds = formatted.BuildGeometry(new Point()).Bounds;
        double directCenter = direct.Top + direct.Height / 2;
        double formattedCenter = formattedBounds.Top + formattedBounds.Height / 2 - formatted.Baseline;
        // Diagnostic only: independently formatted geometry need not have the
        // same hinting/reference bounds as the resolved font's glyph outline.
        Console.WriteLine($"Caption R reference {scale * 100}%: glyph center/baseline={directCenter:R}, " +
            $"FormattedText delta={formattedCenter - directCenter:R}, actual baseline={caption.BaselineOffset:R}");
    }

    private static void AssertRasterCenter(Grid root, Image icon, double scale)
    {
        int width = (int)Math.Ceiling(root.Width * scale);
        int height = (int)Math.Ceiling(root.Height * scale);
        var bitmap = new RenderTargetBitmap(width, height, 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(root);
        var pixels = new byte[width * height * 4];
        bitmap.CopyPixels(pixels, width * 4, 0);
        int top = height, bottom = -1;
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                if (pixels[(y * width + x) * 4 + 3] > 32)
                {
                    top = Math.Min(top, y);
                    bottom = Math.Max(bottom, y);
                }
        Require(bottom >= top && top > 0 && bottom < height - 1, "Capital R raster is empty or clipped");
        double rasterCenter = (top + bottom + 1) / 2d;
        double iconCenter = icon.TranslatePoint(new Point(0, icon.ActualHeight / 2), root).Y * scale;
        Require(Math.Abs(rasterCenter - iconCenter) <= .5 + 1e-6,
            $"Capital R raster bounds differ by more than half a physical pixel at {scale * 100}%: {rasterCenter} vs {iconCenter}");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
