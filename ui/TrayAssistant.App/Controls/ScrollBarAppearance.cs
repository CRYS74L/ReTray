using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;

namespace TrayAssistant_App.Controls;

public static class ScrollBarAppearance
{
    public static readonly DependencyProperty PersistentProperty = DependencyProperty.RegisterAttached("Persistent", typeof(bool), typeof(ScrollBarAppearance), new PropertyMetadata(false, Changed));
    public static bool GetPersistent(DependencyObject element) => (bool)element.GetValue(PersistentProperty);
    public static void SetPersistent(DependencyObject element, bool value) => element.SetValue(PersistentProperty, value);
    private static void Changed(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        if (sender is not ScrollBar bar) return;
        bar.Loaded -= Loaded;
        if (args.NewValue is true)
        {
            bar.Loaded += Loaded;
            if (bar.IsLoaded) Apply(bar);
        }
    }
    private static void Loaded(object sender, RoutedEventArgs args) => Apply((ScrollBar)sender);
    private static void Apply(ScrollBar bar)
    {
        bar.ApplyTemplate();
        SetThumbWidth(bar);
        bar.DispatcherQueue.TryEnqueue(() => SetThumbWidth(bar));
    }
    private static void SetThumbWidth(DependencyObject node)
    {
        if (node is FrameworkElement { Name: "VerticalPanningThumb" } thumb) { thumb.Width = 6; return; }
        if (node is Thumb { Name: "VerticalThumb" } fullThumb) fullThumb.Template = (Microsoft.UI.Xaml.Controls.ControlTemplate)Application.Current.Resources["PersistentScrollThumbTemplate"];
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(node); index++) SetThumbWidth(VisualTreeHelper.GetChild(node, index));
    }
}
