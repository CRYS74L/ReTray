using System.Windows;
using Size = System.Windows.Size;

namespace LanDeviceMonitor.Controls;

public sealed class AdaptiveHeader : System.Windows.Controls.Panel
{
    private bool _stacked;
    public double Gap { get; set; } = 16;
    protected override Size MeasureOverride(Size available)
    {
        if (InternalChildren.Count != 2) return new Size();
        var title=InternalChildren[0]; var actions=InternalChildren[1];
        title.Measure(new Size(double.PositiveInfinity,double.PositiveInfinity));
        actions.Measure(new Size(double.PositiveInfinity,double.PositiveInfinity));
        _stacked=title.DesiredSize.Width+actions.DesiredSize.Width+Gap > available.Width;
        if (_stacked) title.Measure(new Size(available.Width,double.PositiveInfinity));
        return new Size(available.Width,_stacked ? title.DesiredSize.Height+Gap+actions.DesiredSize.Height : Math.Max(title.DesiredSize.Height,actions.DesiredSize.Height));
    }
    protected override Size ArrangeOverride(Size size)
    {
        var title=InternalChildren[0]; var actions=InternalChildren[1];
        title.Arrange(new Rect(0,0,_stacked ? size.Width : Math.Max(0,size.Width-actions.DesiredSize.Width-Gap),title.DesiredSize.Height));
        actions.Arrange(new Rect(_stacked ? 0 : size.Width-actions.DesiredSize.Width,
            _stacked ? title.DesiredSize.Height+Gap : (size.Height-actions.DesiredSize.Height)/2,
            actions.DesiredSize.Width,actions.DesiredSize.Height));
        return size;
    }
}
