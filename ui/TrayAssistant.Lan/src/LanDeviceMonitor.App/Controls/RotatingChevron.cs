using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Point=System.Windows.Point;
using Brush=System.Windows.Media.Brush;
using Brushes=System.Windows.Media.Brushes;
using Pen=System.Windows.Media.Pen;
namespace LanDeviceMonitor.Controls;

public sealed class RotatingChevron : FrameworkElement
{
    public static readonly DependencyProperty StrokeProperty=DependencyProperty.Register(nameof(Stroke),typeof(Brush),typeof(RotatingChevron),new FrameworkPropertyMetadata(Brushes.Black,FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty IsReversedProperty=DependencyProperty.Register(nameof(IsReversed),typeof(bool),typeof(RotatingChevron),new PropertyMetadata(false,Changed));
    private static readonly DependencyProperty AngleProperty=DependencyProperty.Register(nameof(Angle),typeof(double),typeof(RotatingChevron),new FrameworkPropertyMetadata(0d,FrameworkPropertyMetadataOptions.AffectsRender));
    public Brush Stroke {get=>(Brush)GetValue(StrokeProperty);set=>SetValue(StrokeProperty,value);}
    public bool IsReversed {get=>(bool)GetValue(IsReversedProperty);set=>SetValue(IsReversedProperty,value);}
    public double Angle=>(double)GetValue(AngleProperty);
    private int _generation;
    public RotatingChevron()
    {
        Loaded+=(_,_)=>InvalidateVisual();
        IsVisibleChanged+=(_,_)=>InvalidateVisual();
    }
    private static void Changed(DependencyObject value,DependencyPropertyChangedEventArgs args)
    {
        var glyph=(RotatingChevron)value;var current=glyph.Angle;var target=(bool)args.NewValue ? 180d : 0d;var generation=++glyph._generation;
        glyph.BeginAnimation(AngleProperty,null);
        if(!glyph.IsLoaded || !SystemParameters.ClientAreaAnimation){glyph.SetValue(AngleProperty,target);return;}
        glyph.SetValue(AngleProperty,current);
        var animation=new DoubleAnimationUsingKeyFrames {FillBehavior=FillBehavior.Stop};
        animation.KeyFrames.Add(new DiscreteDoubleKeyFrame(current,KeyTime.FromTimeSpan(TimeSpan.Zero)));
        animation.KeyFrames.Add(new SplineDoubleKeyFrame(target,KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(180)),new KeySpline(.25,.1,.25,1)));
        animation.Completed+=(_,_)=>{if(generation==glyph._generation){glyph.SetValue(AngleProperty,target);glyph.BeginAnimation(AngleProperty,null);}};
        glyph.BeginAnimation(AngleProperty,animation,HandoffBehavior.SnapshotAndReplace);
    }
    protected override void OnRender(DrawingContext dc)
    {
        // The visual fills the button, so its center never depends on an ancestor's layout.
        var center=new Point(RenderSize.Width/2,RenderSize.Height/2);
        // A small optical compensation balances the round join against the two caps.
        var geometry=new StreamGeometry();using(var path=geometry.Open()){path.BeginFigure(new Point(-6,-2.8),false,false);path.LineTo(new Point(0,3.2),true,false);path.LineTo(new Point(6,-2.8),true,false);}geometry.Freeze();
        var pen=new Pen(Stroke,1.6){StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round,LineJoin=PenLineJoin.Round};
        dc.PushTransform(new TranslateTransform(center.X,center.Y));dc.PushTransform(new RotateTransform(Angle));dc.DrawGeometry(null,pen,geometry);dc.Pop();dc.Pop();
    }
}
