using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Size = System.Windows.Size;

namespace LanDeviceMonitor.Controls;

// History rows share the page's outer ScrollViewer. Keep the full
// document extent while generating only rows near that viewport.
public sealed class SessionHistoryPanel : VirtualizingPanel
{
    public static readonly DependencyProperty ActiveCountProperty = DependencyProperty.Register(
        nameof(ActiveCount),typeof(int),typeof(SessionHistoryPanel),
        new FrameworkPropertyMetadata(int.MaxValue,FrameworkPropertyMetadataOptions.AffectsMeasure));
    public int ActiveCount { get => (int)GetValue(ActiveCountProperty); set => SetValue(ActiveCountProperty,value); }
    private ScrollViewer? _scroll;
    private double[] _heights=[];
    private double[] _offsets=[0];
    private double _measuredWidth=double.NaN;
    private bool _arranged;
    public SessionHistoryPanel()
    {
        Loaded += (_,_) =>
        {
            DependencyObject? parent=VisualTreeHelper.GetParent(this);
            while(parent is not null && parent is not ScrollViewer) parent=VisualTreeHelper.GetParent(parent);
            if(_scroll is not null) _scroll.ScrollChanged-=Scrolled;
            _scroll=parent as ScrollViewer;
            if(_scroll is not null) _scroll.ScrollChanged+=Scrolled;
            InvalidateMeasure();
        };
        Unloaded += (_,_) => { if(_scroll is not null) _scroll.ScrollChanged-=Scrolled; _scroll=null; };
    }
    private void Scrolled(object sender,ScrollChangedEventArgs args)
    {
        if(args.VerticalChange!=0 || args.ViewportHeightChange!=0 || args.ViewportWidthChange!=0) InvalidateMeasure();
    }
    protected override void OnItemsChanged(object sender,ItemsChangedEventArgs args)
    { base.OnItemsChanged(sender,args); InvalidateMeasure(); }

    protected override Size MeasureOverride(Size available)
    {
        // WPF connects a virtualizing panel to its generator lazily through
        // InternalChildren. Cached startup data can make the first measure nonempty.
        _ = InternalChildren;
        var owner=ItemsControl.GetItemsOwner(this);
        var count=Math.Min(owner?.Items.Count??0,Math.Max(0,ActiveCount));
        if(count==0) { ClearChildren(); _heights=[];_offsets=[0];return new Size(double.IsInfinity(available.Width)?0:available.Width,0); }
        var first=Realize(0);
        first.Measure(new Size(available.Width,double.PositiveInfinity));
        var probe=Math.Max(1,first.DesiredSize.Height);
        if(_heights.Length!=count || _measuredWidth!=available.Width)
        { _heights=Enumerable.Repeat(probe,count).ToArray();_measuredWidth=available.Width; }
        _heights[0]=probe;RebuildOffsets();
        var viewport=_scroll?.ViewportHeight is >0 ? _scroll.ViewportHeight : 700;
        var top=_scroll is not null && _arranged ? TranslatePoint(new System.Windows.Point(),_scroll).Y : 0;
        var start=0;var end=0;
        for(var pass=0;pass<2;pass++)
        {
            start=Math.Max(0,IndexAt(-top)-2);
            end=Math.Min(count-1,IndexAt(viewport-top)+2);
            for(var i=start;i<=end;i++)
            {
                var child=Realize(i);child.Measure(new Size(available.Width,double.PositiveInfinity));
                _heights[i]=Math.Max(1,child.DesiredSize.Height);
            }
            RebuildOffsets();
        }
        // Index zero remains as the row-height probe; offscreen it costs one row.
        for(var i=InternalChildren.Count-1;i>=0;i--)
        {
            var index=owner!.ItemContainerGenerator.IndexFromContainer(InternalChildren[i]);
            if(index!=0 && (index<start || index>end))
            { ItemContainerGenerator.Remove(new GeneratorPosition(i,0),1); RemoveInternalChildRange(i,1); }
        }
        return new Size(double.IsInfinity(available.Width)?first.DesiredSize.Width:available.Width,_offsets[^1]);
    }
    private void RebuildOffsets()
    {
        if(_offsets.Length!=_heights.Length+1)_offsets=new double[_heights.Length+1];
        _offsets[0]=0;
        for(var i=0;i<_heights.Length;i++)_offsets[i+1]=_offsets[i]+_heights[i];
    }
    private int IndexAt(double position)
    {
        var index=Array.BinarySearch(_offsets,position);
        if(index<0)index=~index-1;
        return Math.Clamp(index,0,_heights.Length-1);
    }
    internal double HeightForCount(double count)
    {
        var clamped=Math.Clamp(count,0,_heights.Length);
        var whole=(int)clamped;
        return _offsets[whole]+(whole<_heights.Length ? (clamped-whole)*_heights[whole] : 0);
    }
    private UIElement Realize(int index)
    {
        var position=ItemContainerGenerator.GeneratorPositionFromIndex(index);
        using(ItemContainerGenerator.StartAt(position,GeneratorDirection.Forward,true))
        {
            var child=(UIElement)ItemContainerGenerator.GenerateNext(out var created);
            if(created)
            {
                var insertion=position.Offset==0?position.Index:position.Index+1;
                InsertInternalChild(insertion,child); ItemContainerGenerator.PrepareItemContainer(child);
            }
            return child;
        }
    }
    protected override Size ArrangeOverride(Size finalSize)
    {
        var owner=ItemsControl.GetItemsOwner(this);
        foreach(UIElement child in InternalChildren)
        {
            var index=owner.ItemContainerGenerator.IndexFromContainer(child);
            if(index>=0 && index<_heights.Length)child.Arrange(new Rect(0,_offsets[index],finalSize.Width,_heights[index]));
        }
        if(!_arranged)
        {
            _arranged=true;
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded,new Action(InvalidateMeasure));
        }
        return finalSize;
    }
    private void ClearChildren()
    {
        if(InternalChildren.Count==0) return;
        ItemContainerGenerator.RemoveAll(); RemoveInternalChildRange(0,InternalChildren.Count);
    }
}
