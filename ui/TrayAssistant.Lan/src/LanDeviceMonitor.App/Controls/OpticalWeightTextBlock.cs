using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Pen = System.Windows.Media.Pen;
using System.Windows.Documents;

namespace LanDeviceMonitor.Controls;

// A very small outline adds intermediate ink weight when the embedded static
// family has no face between Regular and Bold. Text metrics remain unchanged.
public sealed class OpticalWeightTextBlock : TextBlock
{
    private WeightAdorner? _weight;
    private AdornerLayer? _layer;
    public OpticalWeightTextBlock()
    {
        Loaded += (_, _) =>
        {
            _layer = AdornerLayer.GetAdornerLayer(this);
            if (_layer is not null && _weight is null) { _weight = new WeightAdorner(this) { IsHitTestVisible = false }; _layer.Add(_weight); }
            Services.ThemeService.PaletteChanged -= PaletteChanged;
            Services.ThemeService.PaletteChanged += PaletteChanged;
        };
        Unloaded += (_, _) => { Services.ThemeService.PaletteChanged -= PaletteChanged; if (_weight is not null) _layer?.Remove(_weight); _weight = null; _layer = null; };
        SizeChanged += (_, _) => _weight?.InvalidateVisual();
    }
    private void PaletteChanged() => _weight?.InvalidateVisual();

    private sealed class WeightAdorner(OpticalWeightTextBlock owner) : Adorner(owner)
    {
        protected override void OnRender(DrawingContext drawingContext)
        {
            IsHitTestVisible = false;
            if (string.IsNullOrEmpty(owner.Text) || owner.ActualWidth <= 0 || owner.ActualHeight <= 0) return;
            var text = new FormattedText(owner.Text, CultureInfo.CurrentCulture, owner.FlowDirection,
                new Typeface(owner.FontFamily, owner.FontStyle, owner.FontWeight, owner.FontStretch), owner.FontSize, owner.Foreground,
                null, TextOptions.GetTextFormattingMode(owner), VisualTreeHelper.GetDpi(owner).PixelsPerDip)
            { MaxTextWidth = owner.ActualWidth, MaxTextHeight = owner.ActualHeight, Trimming = owner.TextTrimming, MaxLineCount = 1 };
            drawingContext.DrawGeometry(null, new Pen(owner.Foreground, .12) { LineJoin = PenLineJoin.Round },
                text.BuildGeometry(new System.Windows.Point()));
        }
    }
}
