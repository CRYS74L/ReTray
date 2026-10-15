using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Point=System.Windows.Point;
using Rect=System.Windows.Rect;
using TextBox=System.Windows.Controls.TextBox;
namespace LanDeviceMonitor.Controls;

public static class FieldLabelAlignment
{
    public static readonly DependencyProperty TargetProperty=DependencyProperty.RegisterAttached("Target",typeof(FrameworkElement),typeof(FieldLabelAlignment),new PropertyMetadata(null,Changed));
    private static readonly DependencyProperty StateProperty=DependencyProperty.RegisterAttached("State",typeof(State),typeof(FieldLabelAlignment));
    public static readonly DependencyProperty HorizontalOffsetProperty=DependencyProperty.RegisterAttached("HorizontalOffset",typeof(double),typeof(FieldLabelAlignment),new PropertyMetadata(0d,(value,_) => (value.GetValue(StateProperty) as State)?.Refresh()));
    public static double GetHorizontalOffset(DependencyObject value)=>(double)value.GetValue(HorizontalOffsetProperty);
    public static void SetHorizontalOffset(DependencyObject value,double offset)=>value.SetValue(HorizontalOffsetProperty,offset);
    public static FrameworkElement? GetTarget(DependencyObject value)=>(FrameworkElement?)value.GetValue(TargetProperty);
    public static void SetTarget(DependencyObject value,FrameworkElement target)=>value.SetValue(TargetProperty,target);
    private static void Changed(DependencyObject value,DependencyPropertyChangedEventArgs args)
    {
        if(value is not TextBlock label)return;(label.GetValue(StateProperty) as State)?.Detach();
        label.SetValue(StateProperty,args.NewValue is FrameworkElement target ? new State(label,target) : null);
    }
    private sealed class State
    {
        private readonly TextBlock _label;private readonly FrameworkElement _target;private readonly TranslateTransform _offset=new();private bool _pending;
        public State(TextBlock label,FrameworkElement target)
        {
            _label=label;_target=target;var group=new TransformGroup();group.Children.Add(label.RenderTransform.Clone());group.Children.Add(_offset);label.RenderTransform=group;
            label.LayoutUpdated+=Queue;label.Unloaded+=Unloaded;label.Loaded+=Loaded;Queue(null,EventArgs.Empty);
        }
        public void Detach(){_label.LayoutUpdated-=Queue;_label.Unloaded-=Unloaded;_label.Loaded-=Loaded;}
        public void Refresh()=>Queue(null,EventArgs.Empty);
        private void Unloaded(object sender,RoutedEventArgs e)=>_label.LayoutUpdated-=Queue;
        private void Loaded(object sender,RoutedEventArgs e){_label.LayoutUpdated-=Queue;_label.LayoutUpdated+=Queue;Queue(null,EventArgs.Empty);}
        private void Queue(object? sender,EventArgs e)
        {
            if(_pending || !_label.IsVisible || !_target.IsVisible)return;
            _pending=true;_label.Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle,new Action(()=>{
                _pending=false;if(!_label.IsVisible || !_target.IsVisible || _target is TextBox {HorizontalOffset:>.1})return;
                var ink=TextInkBounds(_target);var caption=TextInkBounds(_label);if(ink.IsEmpty || caption.IsEmpty)return;
                var delta=ink.Left+GetHorizontalOffset(_label)-_label.TranslatePoint(new Point(caption.Left,caption.Top),_target).X;if(Math.Abs(delta)>.025)_offset.X+=delta;
            }));
        }
    }
    internal static Rect TextInkBounds(FrameworkElement root)
    {
        var result=Rect.Empty;
        void Visit(DependencyObject value)
        {
            if(value is UIElement {IsVisible:false})return;
            if(value is Visual visual && VisualTreeHelper.GetDrawing(visual) is {} drawing){var bounds=GlyphBounds(drawing);if(!bounds.IsEmpty)result.Union(visual.TransformToAncestor(root).TransformBounds(bounds));}
            for(var i=0;i<VisualTreeHelper.GetChildrenCount(value);i++)Visit(VisualTreeHelper.GetChild(value,i));
        }
        Visit(root);return result;
    }
    private static Rect GlyphBounds(Drawing drawing)
    {
        if(drawing is GlyphRunDrawing glyph)return glyph.Bounds;if(drawing is not DrawingGroup group)return Rect.Empty;
        var result=Rect.Empty;foreach(var child in group.Children)result.Union(GlyphBounds(child));
        if(!result.IsEmpty && group.ClipGeometry is {} clip)result.Intersect(clip.Bounds);
        return result.IsEmpty || group.Transform is null ? result : group.Transform.TransformBounds(result);
    }
}
