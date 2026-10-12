using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using Size = System.Windows.Size;

namespace LanDeviceMonitor.Controls;

/// <summary>Reveals older rows without rebuilding the virtualized history tree.</summary>
public sealed class HistoryDisclosure : Decorator
{
    public static readonly DependencyProperty VisibleCountProperty = DependencyProperty.Register(nameof(VisibleCount),typeof(int),typeof(HistoryDisclosure),new FrameworkPropertyMetadata(0,FrameworkPropertyMetadataOptions.AffectsMeasure,CountChanged));
    public static readonly DependencyProperty TotalCountProperty = DependencyProperty.Register(nameof(TotalCount),typeof(int),typeof(HistoryDisclosure),new FrameworkPropertyMetadata(0,FrameworkPropertyMetadataOptions.AffectsMeasure));
    private static readonly DependencyProperty DisplayedCountProperty = DependencyProperty.Register("DisplayedCount",typeof(double),typeof(HistoryDisclosure),new FrameworkPropertyMetadata(0d,FrameworkPropertyMetadataOptions.AffectsMeasure));
    public int VisibleCount {get=>(int)GetValue(VisibleCountProperty);set=>SetValue(VisibleCountProperty,value);}
    public int TotalCount {get=>(int)GetValue(TotalCountProperty);set=>SetValue(TotalCountProperty,value);}
    private double _fullHeight;
    private int _generation;
    private bool _resetPending;
    public HistoryDisclosure() { ClipToBounds=true; }
    private static void CountChanged(DependencyObject source,DependencyPropertyChangedEventArgs args)
    {
        var host=(HistoryDisclosure)source;
        var current=(double)host.GetValue(DisplayedCountProperty);
        var target=(double)(int)args.NewValue;
        var generation=++host._generation;
        host.BeginAnimation(DisplayedCountProperty,null);host.SetValue(DisplayedCountProperty,target);
        if(host._resetPending || !host.IsLoaded || !SystemParameters.ClientAreaAnimation)return;
        var animation=new DoubleAnimation(current,target,TimeSpan.FromMilliseconds(target>current?420:320))
        {EasingFunction=target>current ? new QuinticEase {EasingMode=EasingMode.EaseOut} : new CubicEase {EasingMode=EasingMode.EaseInOut}};
        animation.Completed+=(_,_)=>{if(generation==host._generation)host.BeginAnimation(DisplayedCountProperty,null);};
        host.BeginAnimation(DisplayedCountProperty,animation,HandoffBehavior.SnapshotAndReplace);
    }
    public void Reset()
    {
        ++_generation;BeginAnimation(DisplayedCountProperty,null);
        _resetPending=true;InvalidateMeasure();
    }
    protected override Size MeasureOverride(Size available)
    {
        if(Child is null)return new Size();
        Child.Measure(new Size(available.Width,double.PositiveInfinity));
        _fullHeight=Child.DesiredSize.Height;
        if(_resetPending){_resetPending=false;SetValue(DisplayedCountProperty,(double)VisibleCount);}
        var displayed=(double)GetValue(DisplayedCountProperty);
        var panel=FindHistoryPanel(Child);
        var height=panel is not null ? panel.HeightForCount(displayed) : TotalCount>0 ? _fullHeight*Math.Clamp(displayed/TotalCount,0,1) : 0;
        // Clip the final row's trailing gap; the card owns the action spacing.
        return new Size(Child.DesiredSize.Width,Math.Max(0,height-8));
    }
    protected override Size ArrangeOverride(Size finalSize)
    {
        Child?.Arrange(new Rect(0,0,finalSize.Width,_fullHeight));
        return finalSize;
    }
    private static SessionHistoryPanel? FindHistoryPanel(DependencyObject root)
    {
        if(root is SessionHistoryPanel panel)return panel;
        for(var i=0;i<System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);i++)
            if(FindHistoryPanel(System.Windows.Media.VisualTreeHelper.GetChild(root,i)) is {} found)return found;
        return null;
    }
}
