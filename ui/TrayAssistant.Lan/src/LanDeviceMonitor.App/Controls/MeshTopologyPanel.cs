using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using LanDeviceMonitor.Models;
using Size=System.Windows.Size;
using Point=System.Windows.Point;

namespace LanDeviceMonitor.Controls;

// A parent/child outline avoids crossing links and remains readable at phone widths.
public sealed class MeshTopologyPanel : System.Windows.Controls.Panel
{
    public static readonly DependencyProperty LinkBrushProperty=DependencyProperty.Register(
        nameof(LinkBrush),typeof(System.Windows.Media.Brush),typeof(MeshTopologyPanel),
        new FrameworkPropertyMetadata(System.Windows.Media.Brushes.Gray,FrameworkPropertyMetadataOptions.AffectsRender));
    public System.Windows.Media.Brush LinkBrush {get=>(System.Windows.Media.Brush)GetValue(LinkBrushProperty);set=>SetValue(LinkBrushProperty,value);}
    public CornerRadius LinkRadius {get;set;} = new(24);
    private readonly Dictionary<string,Rect> _positions=[];
    private readonly Dictionary<string,MeshNodeInfo> _nodes=[];
    private static MeshNodeInfo? Node(UIElement element) => (element as ContentPresenter)?.Content as MeshNodeInfo;
    private double Indent(MeshNodeInfo? node,double width)
    {
        var depth=0;var visited=new HashSet<string>();
        while(node is not null && !string.IsNullOrEmpty(node.ParentNodeId) && visited.Add(node.ParentNodeId) && _nodes.TryGetValue(node.ParentNodeId,out node)) depth++;
        return Math.Min(depth*44,Math.Max(0,width*.3));
    }
    protected override Size MeasureOverride(Size available)
    {
        _nodes.Clear();
        foreach(UIElement child in InternalChildren) if(Node(child) is {NodeId.Length:>0} n) _nodes.TryAdd(n.NodeId,n);
        var width=double.IsInfinity(available.Width)?500:available.Width;
        var height=0d;
        foreach(UIElement child in InternalChildren)
        {
            child.Measure(new Size(Math.Max(0,width-Indent(Node(child),width)),double.PositiveInfinity));
            height+=child.DesiredSize.Height+16;
        }
        return new Size(width,Math.Max(0,height-16));
    }
    protected override Size ArrangeOverride(Size size)
    {
        _positions.Clear();var y=0d;
        foreach(UIElement child in InternalChildren)
        {
            var node=Node(child);var x=Indent(node,size.Width);
            var rect=new Rect(x,y,Math.Max(0,size.Width-x),child.DesiredSize.Height);
            child.Arrange(rect);
            if(node is not null && node.NodeId.Length>0) _positions[node.NodeId]=rect;
            y+=rect.Height+16;
        }
        InvalidateVisual();return size;
    }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        var geometry=new StreamGeometry();
        using(var path=geometry.Open())
            foreach(var node in _nodes.Values)
            {
                if(!_positions.TryGetValue(node.NodeId,out var child) || !_positions.TryGetValue(node.ParentNodeId,out var parent)) continue;
                var x=parent.Left+12;var y=child.Top+child.Height/2;
                path.BeginFigure(new Point(x,parent.Bottom-2),false,false);
                var radius=Math.Min(LinkRadius.TopLeft,Math.Max(0,child.Left-x));
                path.LineTo(new Point(x,y-radius),true,false);
                path.ArcTo(new Point(x+radius,y),new Size(radius,radius),0,false,SweepDirection.Counterclockwise,true,false);
                path.LineTo(new Point(child.Left,y),true,false);
            }
        geometry.Freeze();
        dc.PushOpacity(.45);dc.DrawGeometry(null,new System.Windows.Media.Pen(LinkBrush,1.2),geometry);dc.Pop();
    }
}
