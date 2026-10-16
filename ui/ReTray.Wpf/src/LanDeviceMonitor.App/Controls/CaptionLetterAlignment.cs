using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Point = System.Windows.Point;

namespace LanDeviceMonitor.Controls;

/// <summary>
/// Vertically aligns the outline bounds of the caption's leading capital R to
/// an adjacent element. TextBlock still performs all text layout and rendering.
/// </summary>
public static class CaptionLetterAlignment
{
    public static readonly DependencyProperty ReferenceProperty = DependencyProperty.RegisterAttached(
        "Reference", typeof(FrameworkElement), typeof(CaptionLetterAlignment),
        new PropertyMetadata(null, ReferenceChanged));

    private static readonly DependencyProperty StateProperty = DependencyProperty.RegisterAttached(
        "State", typeof(State), typeof(CaptionLetterAlignment));

    public static FrameworkElement? GetReference(DependencyObject element) =>
        (FrameworkElement?)element.GetValue(ReferenceProperty);

    public static void SetReference(DependencyObject element, FrameworkElement? value) =>
        element.SetValue(ReferenceProperty, value);

    private static void ReferenceChanged(DependencyObject element, DependencyPropertyChangedEventArgs args)
    {
        if (element is not TextBlock caption) return;
        (caption.GetValue(StateProperty) as State)?.Detach();
        caption.SetValue(StateProperty,
            args.NewValue is FrameworkElement reference ? new State(caption, reference) : null);
    }

    private sealed class State
    {
        private readonly TextBlock _caption;
        private readonly FrameworkElement _reference;
        private readonly Transform _original;
        private readonly TransformGroup _transform;
        private readonly TranslateTransform _translation = new();
        private FontFamily? _family;
        private FontStyle _style;
        private FontWeight _weight;
        private FontStretch _stretch;
        private double _size;
        private double _pixelsPerDip;
        private double _rCenterFromBaseline;
        private bool _metricsKnown;
        private bool _hasMetrics;
        private bool _observing;

        public State(TextBlock caption, FrameworkElement reference)
        {
            _caption = caption;
            _reference = reference;
            _original = caption.RenderTransform;
            _transform = new TransformGroup();
            _transform.Children.Add(_original);
            _transform.Children.Add(_translation);
            caption.SetCurrentValue(UIElement.RenderTransformProperty, _transform);
            caption.Loaded += Loaded;
            caption.Unloaded += Unloaded;
            ObserveLayout();
        }

        public void Detach()
        {
            StopObservingLayout();
            _caption.Loaded -= Loaded;
            _caption.Unloaded -= Unloaded;
            if (ReferenceEquals(_caption.RenderTransform, _transform))
                _caption.SetCurrentValue(UIElement.RenderTransformProperty, _original);
        }

        private void Loaded(object sender, RoutedEventArgs args)
        {
            ObserveLayout();
            Align();
        }

        private void Unloaded(object sender, RoutedEventArgs args) => StopObservingLayout();

        private void ObserveLayout()
        {
            if (_observing) return;
            _caption.LayoutUpdated += LayoutUpdated;
            _observing = true;
        }

        private void StopObservingLayout()
        {
            if (!_observing) return;
            _caption.LayoutUpdated -= LayoutUpdated;
            _observing = false;
        }

        private void LayoutUpdated(object? sender, EventArgs args) => Align();

        private void Align()
        {
            if (!_caption.IsMeasureValid || !_caption.IsArrangeValid ||
                !_reference.IsArrangeValid || _caption.ActualHeight <= 0 ||
                _reference.ActualHeight <= 0 || !_caption.Text.StartsWith("R", StringComparison.Ordinal) ||
                VisualTreeHelper.GetParent(_caption) is not UIElement parent ||
                _caption.FindCommonVisualAncestor(_reference) is null ||
                !ReferenceEquals(_caption.RenderTransform, _transform)) return;

            if (!RefreshMetrics()) return;

            // The real font's R outline is already baseline-relative. Add the
            // REAL TextBlock first-line baseline after normal body-text layout.
            // FormattedText.BuildGeometry has a separately formatted/hinted
            // outline and must not substitute for this glyph reference.
            double baseline = _caption.BaselineOffset;
            if (!double.IsFinite(baseline)) return;
            var actual = _caption.TranslatePoint(new Point(0, baseline + _rCenterFromBaseline), parent);
            var expected = _reference.TranslatePoint(
                new Point(_reference.ActualWidth / 2, _reference.ActualHeight / 2), parent);

            // The owned translation follows the original transform, so its Y
            // is in the caption parent's coordinates. Read the current error
            // instead of accumulating a font-dependent or DPI-dependent fudge.
            double next = _translation.Y + expected.Y - actual.Y;
            if (double.IsFinite(next) && Math.Abs(next - _translation.Y) > 1e-7)
                _translation.Y = next;
        }

        private bool RefreshMetrics()
        {
            double pixelsPerDip = VisualTreeHelper.GetDpi(_caption).PixelsPerDip;
            if (_metricsKnown && Equals(_family, _caption.FontFamily) &&
                _style == _caption.FontStyle && _weight == _caption.FontWeight &&
                _stretch == _caption.FontStretch && _size == _caption.FontSize &&
                _pixelsPerDip == pixelsPerDip)
                return _hasMetrics;

            _metricsKnown = true;
            _hasMetrics = false;
            _family = _caption.FontFamily;
            _style = _caption.FontStyle;
            _weight = _caption.FontWeight;
            _stretch = _caption.FontStretch;
            _size = _caption.FontSize;
            _pixelsPerDip = pixelsPerDip;
            var face = new Typeface(_family, _style, _weight, _stretch);
            if (!face.TryGetGlyphTypeface(out var glyph) ||
                !glyph.CharacterToGlyphMap.TryGetValue('R', out ushort index)) return false;
            var bounds = glyph.GetGlyphOutline(index, _size, _size).Bounds;
            _hasMetrics = !bounds.IsEmpty;
            if (_hasMetrics) _rCenterFromBaseline = bounds.Top + bounds.Height / 2;
            return _hasMetrics;
        }
    }
}
