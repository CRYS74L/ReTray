using System.Windows;
using System.Windows.Controls;
using Size = System.Windows.Size;
using Panel = System.Windows.Controls.Panel;

namespace LanDeviceMonitor.Controls;

/// <summary>Keep the complete duration beside its dates at every supported width.</summary>
public sealed class SessionHistoryRow : Panel
{
    private double _markerWidth;
    private double _gap;
    private double _detailsHeight;
    private double _badgeWidth;

    protected override Size MeasureOverride(Size available)
    {
        if (InternalChildren.Count != 3) return new Size();
        var details = InternalChildren[1];
        var badge = InternalChildren[2];
        details.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        badge.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var border = badge as Border;
        // Reserve the original capsule width at every page size.
        var naturalBadgeWidth = badge.DesiredSize.Width + (border is null ? 0 : 34 - border.Padding.Left - border.Padding.Right);
        var required = 24 + details.DesiredSize.Width + 12 + naturalBadgeWidth;
        var width = double.IsInfinity(available.Width) ? required : available.Width;
        var compact = required > width;
        _markerWidth = compact ? 16 : 24;
        _gap = compact ? 6 : 12;
        if (border is not null)
        {
            border.SetCurrentValue(Border.PaddingProperty, new Thickness(17, 0, 17, 0));
            badge.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        }
        _badgeWidth = badge.DesiredSize.Width;
        details.Measure(new Size(Math.Max(0, width - _markerWidth - _gap - _badgeWidth), double.PositiveInfinity));
        _detailsHeight = details.DesiredSize.Height;
        var height = Math.Max(_detailsHeight, badge.DesiredSize.Height);
        InternalChildren[0].Measure(new Size(_markerWidth, height));
        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size size)
    {
        if (InternalChildren.Count != 3) return size;
        InternalChildren[0].Arrange(new Rect(0, 0, _markerWidth, _detailsHeight));
        InternalChildren[1].Arrange(new Rect(_markerWidth, 0,
            Math.Max(0, size.Width - _markerWidth - _gap - _badgeWidth), _detailsHeight));
        InternalChildren[2].Arrange(new Rect(Math.Max(0, size.Width - _badgeWidth), 0, _badgeWidth, size.Height));
        return size;
    }
}

/// <summary>Reflow only date details, keeping duration in its adjacent column.</summary>
public sealed class SessionHistoryDetails : Panel
{
    private bool _compact;
    private readonly double[] _groupHeights = new double[2];
    private readonly bool[] _splitTime = new bool[2];
    private const double GroupGap = 8;

    protected override Size MeasureOverride(Size available)
    {
        if (InternalChildren.Count != 6) return new Size();
        foreach (UIElement child in InternalChildren)
        {
            if (child is HistoryDateTextBlock date) date.SetCurrentValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
            child.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        }
        var naturalWidth = 72 + Math.Max(DateTimeWidth(0), DateTimeWidth(1));
        var width = double.IsInfinity(available.Width) ? naturalWidth : available.Width;
        _compact = naturalWidth > width;
        for (var group = 0; group < 2; group++)
        {
            var label = InternalChildren[group * 3];
            var date = InternalChildren[group * 3 + 1];
            var time = InternalChildren[group * 3 + 2];
            _splitTime[group] = _compact && DateTimeWidth(group) > width;
            if (_compact)
            {
                date.Measure(new Size(Math.Max(0, width - (_splitTime[group] ? 0 : TimeWidth(group))), double.PositiveInfinity));
                _groupHeights[group] = label.DesiredSize.Height + 2 + date.DesiredSize.Height
                    + (_splitTime[group] && time.DesiredSize.Width > 0 ? 2 + time.DesiredSize.Height : 0);
            }
            else _groupHeights[group] = Math.Max(label.DesiredSize.Height, Math.Max(date.DesiredSize.Height, time.DesiredSize.Height));
        }
        return new Size(width, _groupHeights[0] + GroupGap + _groupHeights[1]);
    }

    private double TimeWidth(int group)
    {
        var width = InternalChildren[group * 3 + 2].DesiredSize.Width;
        return width > 0 ? width + 10 : 0;
    }
    private double DateTimeWidth(int group) => InternalChildren[group * 3 + 1].DesiredSize.Width + TimeWidth(group);

    protected override Size ArrangeOverride(Size size)
    {
        if (InternalChildren.Count != 6) return size;
        var top = 0d;
        for (var group = 0; group < 2; group++)
        {
            var label = InternalChildren[group * 3];
            var date = InternalChildren[group * 3 + 1];
            var time = InternalChildren[group * 3 + 2];
            var dateTop = top + (_compact ? label.DesiredSize.Height + 2 : 0);
            var dateLeft = _compact ? 0 : 72;
            var dateWidth = Math.Max(0, size.Width - dateLeft - (_splitTime[group] ? 0 : TimeWidth(group)));
            label.Arrange(new Rect(0, top, _compact ? size.Width : 62, label.DesiredSize.Height));
            date.Arrange(new Rect(dateLeft, dateTop, Math.Min(dateWidth, date.DesiredSize.Width), date.DesiredSize.Height));
            time.Arrange(new Rect(_splitTime[group] ? dateLeft : dateLeft + date.DesiredSize.Width + (time.DesiredSize.Width > 0 ? 10 : 0),
                dateTop + (_splitTime[group] && time.DesiredSize.Width > 0 ? date.DesiredSize.Height + 2 : 0),
                time.DesiredSize.Width, time.DesiredSize.Height));
            top += _groupHeights[group] + GroupGap;
        }
        return size;
    }
}
