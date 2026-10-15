using System.Windows;
using System.Windows.Media;
using Size = System.Windows.Size;

namespace LanDeviceMonitor.Controls;

// Connection metadata always shares one row. Tighten the gaps before
// uniformly adapting its own text; wide layouts keep their normal size.
public sealed class SingleRowMetadataPanel : System.Windows.Controls.Panel
{
    public double Gap { get; set; } = 24;
    public double MinimumGap { get; set; } = 8;

    private readonly List<UIElement> _children = [];
    private readonly List<double> _widths = [];
    private readonly Dictionary<System.Windows.Controls.TextBlock, double> _fontSizes = [];
    private double _height;

    protected override Size MeasureOverride(Size available)
    {
        _children.Clear();
        _children.AddRange(InternalChildren.Cast<UIElement>().Where(child => child.Visibility != Visibility.Collapsed));
        var dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
        foreach (var child in _children)
        {
            child.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            RememberFontSizes(child);
        }
        SetFontScale(1);
        MeasureCells(dpi);

        var width = double.IsInfinity(available.Width)
            ? _widths.Sum() + Math.Max(0, _widths.Count - 1) * Gap
            : available.Width;
        var minimumGaps = Math.Max(0, _widths.Count - 1) * MinimumGap;
        if (width > minimumGaps + _widths.Count * 2 / dpi && _widths.Sum() + minimumGaps > width)
        {
            // Set actual font sizes and remeasure glyphs instead of relying on
            // a render transform's cached text/layout at fractional DPI.
            var scale = Math.Clamp((width - minimumGaps - _widths.Count * 2 / dpi) / _widths.Sum(), .01, 1);
            SetFontScale(scale);
            MeasureCells(dpi);
            // Pixel-rounded glyph metrics can differ slightly from the natural
            // width ratio. Resolve that from the actual newly measured text.
            if (_widths.Sum() + minimumGaps > width)
            {
                scale = Math.Max(.01, scale * (width - minimumGaps - _widths.Count / dpi) / _widths.Sum());
                SetFontScale(scale);
                MeasureCells(dpi);
            }
        }
        return new Size(width, _height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var gap = _widths.Count > 1
            ? Math.Max(0, (finalSize.Width - _widths.Sum()) / (_widths.Count - 1))
            : 0;
        var x = 0d;
        for (var i = 0; i < _children.Count; i++)
        {
            var child = _children[i];
            child.Arrange(new Rect(x, 0, _widths[i], _height));
            x += _widths[i] + gap;
        }
        return finalSize;
    }

    private void MeasureCells(double dpi)
    {
        _widths.Clear();
        _height = 0;
        foreach (var child in _children)
        {
            child.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            _widths.Add((Math.Ceiling(child.DesiredSize.Width * dpi) + 2) / dpi);
            _height = Math.Max(_height, child.DesiredSize.Height);
        }
    }

    private void SetFontScale(double scale)
    {
        foreach (var (text, fontSize) in _fontSizes)
            text.SetCurrentValue(System.Windows.Controls.TextBlock.FontSizeProperty, fontSize * scale);
        // A changed grandchild can still sit below a StackPanel/Border whose
        // previous measure constraint matches. Refresh the complete field tree
        // before reading its new glyph width in this same measure pass.
        foreach (var child in _children) InvalidateFieldMeasure(child);
    }

    private void RememberFontSizes(DependencyObject root)
    {
        if (root is System.Windows.Controls.TextBlock text) _fontSizes.TryAdd(text, text.FontSize);
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            RememberFontSizes(VisualTreeHelper.GetChild(root, i));
    }

    private static void InvalidateFieldMeasure(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            InvalidateFieldMeasure(VisualTreeHelper.GetChild(root, i));
        if (root is UIElement element) element.InvalidateMeasure();
    }
}
