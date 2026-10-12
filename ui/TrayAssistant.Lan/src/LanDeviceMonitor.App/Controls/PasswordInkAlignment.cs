using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace LanDeviceMonitor.Controls;

public static class PasswordInkAlignment
{
    public static readonly DependencyProperty EnabledProperty=DependencyProperty.RegisterAttached(
        "Enabled",typeof(bool),typeof(PasswordInkAlignment),new PropertyMetadata(false,Attach));
    public static bool GetEnabled(DependencyObject value)=>(bool)value.GetValue(EnabledProperty);
    public static void SetEnabled(DependencyObject value,bool enabled)=>value.SetValue(EnabledProperty,enabled);
    private static void Attach(DependencyObject value,DependencyPropertyChangedEventArgs args)
    {
        if(value is not FrameworkElement element || args.NewValue is not true) return;
        void Queue() => element.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded,new Action(()=>Align(element)));
        element.Loaded+=(_,_)=>Queue(); element.SizeChanged+=(_,_)=>Queue();
    }
    private static void Align(FrameworkElement element)
    {
        FrameworkElement? target; System.Windows.Media.FontFamily family; double size; FontWeight weight; string glyph;
        if(element is PasswordBox box)
        {
            box.ApplyTemplate(); target=box.Template.FindName("PART_ContentHost",box) as FrameworkElement;
            family=box.FontFamily;size=box.FontSize;weight=box.FontWeight;glyph=box.PasswordChar.ToString();
        }
        else if(element is TextBlock text)
        { target=text;family=text.FontFamily;size=text.FontSize;weight=text.FontWeight;glyph=text.Text; }
        else return;
        if(target is null || target.ActualHeight<=0) return;
        var formatted=new FormattedText(glyph,CultureInfo.CurrentCulture,System.Windows.FlowDirection.LeftToRight,
            new Typeface(family,FontStyles.Normal,weight,FontStretches.Normal),size,System.Windows.Media.Brushes.Black,VisualTreeHelper.GetDpi(element).PixelsPerDip);
        var ink=formatted.BuildGeometry(new System.Windows.Point()).Bounds;
        if(!ink.IsEmpty) target.RenderTransform=new TranslateTransform(0,target.ActualHeight/2-ink.Top-ink.Height/2);
    }
}
