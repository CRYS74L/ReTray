using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using MouseEventArgs=System.Windows.Input.MouseEventArgs;
using System.Windows.Media;
using ToolTip=System.Windows.Controls.ToolTip;

namespace LanDeviceMonitor.Controls;

/// <summary>Empty hints must not create a detached popup at the desktop origin.</summary>
public static class ToolTipContentGuard
{
    public static readonly DependencyProperty EnabledProperty=DependencyProperty.RegisterAttached(
        "Enabled",typeof(bool),typeof(ToolTipContentGuard),new PropertyMetadata(false,Attach));
    public static readonly DependencyProperty CloseOnScrollProperty=DependencyProperty.RegisterAttached(
        "CloseOnScroll",typeof(bool),typeof(ToolTipContentGuard),new PropertyMetadata(false,Attach));
    private static readonly DependencyProperty StateProperty=DependencyProperty.RegisterAttached(
        "State",typeof(PlacementLifetime),typeof(ToolTipContentGuard));
    public static bool GetEnabled(DependencyObject target)=>(bool)target.GetValue(EnabledProperty);
    public static void SetEnabled(DependencyObject target,bool value)=>target.SetValue(EnabledProperty,value);
    public static bool GetCloseOnScroll(DependencyObject target)=>(bool)target.GetValue(CloseOnScrollProperty);
    public static void SetCloseOnScroll(DependencyObject target,bool value)=>target.SetValue(CloseOnScrollProperty,value);
    private static void Attach(DependencyObject target,DependencyPropertyChangedEventArgs args)
    {
        if(target is not ToolTip tip)return;
        tip.Opened-=Opened;
        tip.Closed-=Closed;
        ClearLifetime(tip);
        if(GetEnabled(tip)){tip.Opened+=Opened;tip.Closed+=Closed;if(tip.IsOpen)Opened(tip,new RoutedEventArgs());}
    }
    private static void Opened(object sender,RoutedEventArgs args)
    {
        var tip=(ToolTip)sender;
        BindingOperations.GetBindingExpression(tip,ContentControl.ContentProperty)?.UpdateTarget();
        if(tip.Content is null || tip.Content is string text && string.IsNullOrWhiteSpace(text))
        { tip.SetCurrentValue(ToolTip.IsOpenProperty,false);return; }
        ClearLifetime(tip);
        if(GetCloseOnScroll(tip) && tip.PlacementTarget is FrameworkElement target)
        {
            if(!target.IsVisible){tip.SetCurrentValue(ToolTip.IsOpenProperty,false);return;}
            tip.SetValue(StateProperty,new PlacementLifetime(tip,target));
        }
    }

    private static void Closed(object sender,RoutedEventArgs args)=>ClearLifetime((ToolTip)sender);
    private static void ClearLifetime(ToolTip tip)
    {
        (tip.GetValue(StateProperty) as PlacementLifetime)?.Detach();
        tip.ClearValue(StateProperty);
    }

    private sealed class PlacementLifetime
    {
        private readonly ToolTip _tip;
        private readonly FrameworkElement _target;
        private readonly List<ScrollViewer> _viewers=[];
        public PlacementLifetime(ToolTip tip,FrameworkElement target)
        {
            _tip=tip;_target=target;
            for(DependencyObject? current=target;current is not null;current=Parent(current))
                if(current is ScrollViewer viewer){_viewers.Add(viewer);viewer.ScrollChanged+=Scrolled;}
            target.MouseLeave+=MouseLeft;
            target.IsVisibleChanged+=VisibilityChanged;
            target.Unloaded+=Unloaded;
        }
        public void Detach()
        {
            foreach(var viewer in _viewers)viewer.ScrollChanged-=Scrolled;
            _viewers.Clear();
            _target.MouseLeave-=MouseLeft;
            _target.IsVisibleChanged-=VisibilityChanged;
            _target.Unloaded-=Unloaded;
        }
        private void Close()=>_tip.SetCurrentValue(ToolTip.IsOpenProperty,false);
        private void Scrolled(object sender,ScrollChangedEventArgs args)
        {
            if(ReferenceEquals(sender,args.OriginalSource) && (args.VerticalChange!=0 || args.HorizontalChange!=0))
            {if(_tip is MetricRangeToolTip metric)metric.CloseForScroll();else Close();}
        }
        private void MouseLeft(object sender,MouseEventArgs args)=>Close();
        private void CloseImmediately(){if(_tip is MetricRangeToolTip metric)metric.CloseImmediately();else Close();}
        private void VisibilityChanged(object sender,DependencyPropertyChangedEventArgs args){if(args.NewValue is false)CloseImmediately();}
        private void Unloaded(object sender,RoutedEventArgs args)=>CloseImmediately();
        private static DependencyObject? Parent(DependencyObject value)=>value is Visual or System.Windows.Media.Media3D.Visual3D
            ? VisualTreeHelper.GetParent(value) : LogicalTreeHelper.GetParent(value);
    }
}
