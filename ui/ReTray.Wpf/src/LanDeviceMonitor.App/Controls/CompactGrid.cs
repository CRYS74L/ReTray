using System.Windows;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using System.Windows.Controls;

namespace LanDeviceMonitor.Controls;

public static class CompactGrid
{
    public static readonly DependencyProperty StackLastBelowProperty = DependencyProperty.RegisterAttached(
        "StackLastBelow", typeof(bool), typeof(CompactGrid), new PropertyMetadata(false, Attach));
    public static bool GetStackLastBelow(DependencyObject target) => (bool)target.GetValue(StackLastBelowProperty);
    public static void SetStackLastBelow(DependencyObject target, bool value) => target.SetValue(StackLastBelowProperty,value);
    private static void Attach(DependencyObject target, DependencyPropertyChangedEventArgs args)
    {
        if (target is not Grid grid || args.NewValue is not true) return;
        grid.SizeChanged += (_,_) =>
        {
            if (grid.Children.Count == 0) return;
            var compact=grid.ActualWidth < 440;
            var last=(FrameworkElement)grid.Children[^1];
            if(grid.RowDefinitions.Count==0) { grid.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto}); grid.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto}); }
            Grid.SetRow(last,compact ? 1 : 0);
            Grid.SetColumn(last,compact ? 1 : 2);
            Grid.SetColumnSpan(last,compact ? 2 : 1);
            last.HorizontalAlignment=compact ? HorizontalAlignment.Left : HorizontalAlignment.Stretch;
            last.Margin=compact ? new Thickness(0,10,0,0) : new Thickness(12,0,0,0);
        };
    }
}
