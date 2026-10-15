using System.Windows;
using System.Windows.Controls;

namespace LanDeviceMonitor.Controls;

public static class InputPlaceholder
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.RegisterAttached(
        "Text",typeof(string),typeof(InputPlaceholder),new PropertyMetadata(string.Empty,Attach));
    public static readonly DependencyProperty HasContentProperty = DependencyProperty.RegisterAttached(
        "HasContent",typeof(bool),typeof(InputPlaceholder),new PropertyMetadata(false));
    public static readonly DependencyProperty SuppressedProperty = DependencyProperty.RegisterAttached(
        "Suppressed",typeof(bool),typeof(InputPlaceholder),new PropertyMetadata(false));
    public static string GetText(DependencyObject target)=>(string)target.GetValue(TextProperty);
    public static void SetText(DependencyObject target,string value)=>target.SetValue(TextProperty,value);
    public static bool GetHasContent(DependencyObject target)=>(bool)target.GetValue(HasContentProperty);
    public static bool GetSuppressed(DependencyObject target)=>(bool)target.GetValue(SuppressedProperty);
    public static void SetSuppressed(DependencyObject target,bool value)=>target.SetValue(SuppressedProperty,value);
    private static void Attach(DependencyObject target,DependencyPropertyChangedEventArgs args)
    {
        if(target is not PasswordBox box) return;
        box.PasswordChanged-=PasswordChanged;
        box.PasswordChanged+=PasswordChanged;
        using var password=box.SecurePassword;
        box.SetValue(HasContentProperty,password.Length>0);
    }
    private static void PasswordChanged(object sender,RoutedEventArgs args)
    {
        var box=(PasswordBox)sender;
        using var password=box.SecurePassword;
        box.SetValue(HasContentProperty,password.Length>0);
    }
}
