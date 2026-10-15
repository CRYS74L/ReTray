using System.Windows;
using System.Windows.Media;

namespace ReTray_App.Controls;

// Windows composes volume and battery symbols from overlapping font glyphs.
// Recreate those layers in our own window, with a shared origin and full ink bounds.
public sealed class SystemGlyphImage : FrameworkElement
{
    public static readonly DependencyProperty GlyphsProperty = DependencyProperty.Register(nameof(Glyphs), typeof(string), typeof(SystemGlyphImage), new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty FontFamilyProperty = DependencyProperty.Register(nameof(FontFamily), typeof(FontFamily), typeof(SystemGlyphImage), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty ForegroundProperty = System.Windows.Documents.TextElement.ForegroundProperty.AddOwner(typeof(SystemGlyphImage), new FrameworkPropertyMetadata(Brushes.Black, FrameworkPropertyMetadataOptions.AffectsRender));
    public string Glyphs { get => (string)GetValue(GlyphsProperty); set => SetValue(GlyphsProperty, value); }
    public FontFamily FontFamily { get => (FontFamily)GetValue(FontFamilyProperty); set => SetValue(FontFamilyProperty, value); }
    public Brush Foreground { get => (Brush)GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }
    protected override void OnRender(DrawingContext drawing)
    {
        if (Glyphs.Length == 0 || FontFamily is null) return;
        var face = new Typeface(FontFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        if (!face.TryGetGlyphTypeface(out var font)) return;
        var runs = new List<GlyphRun>(); var bounds = Rect.Empty;
        foreach (var character in Glyphs)
        {
            if (!font.CharacterToGlyphMap.TryGetValue(character, out var glyph)) continue;
            var run = new GlyphRun(font, 0, false, 20, (float)VisualTreeHelper.GetDpi(this).PixelsPerDip,
                [glyph], new Point(), [font.AdvanceWidths[glyph] * 20], null, null, null, null, null, null);
            bounds.Union(run.ComputeInkBoundingBox()); runs.Add(run);
        }
        if (bounds.IsEmpty || bounds.Width == 0 || bounds.Height == 0) return;
        var scale = 20 / Math.Max(bounds.Width, bounds.Height);
        drawing.PushTransform(new TranslateTransform((ActualWidth - bounds.Width * scale) / 2 - bounds.X * scale,
            (ActualHeight - bounds.Height * scale) / 2 - bounds.Y * scale));
        drawing.PushTransform(new ScaleTransform(scale, scale));
        foreach (var run in runs) drawing.DrawGlyphRun(Foreground, run);
        drawing.Pop(); drawing.Pop();
    }
}
