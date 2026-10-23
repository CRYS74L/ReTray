using System.Windows;

namespace ReTray_App;

public static class TrayRowAppearance
{
    public static readonly DependencyProperty IsLiftedProperty = DependencyProperty.RegisterAttached(
        "IsLifted", typeof(bool), typeof(TrayRowAppearance), new PropertyMetadata(false));
    public static bool GetIsLifted(DependencyObject target) => (bool)target.GetValue(IsLiftedProperty);
    public static void SetIsLifted(DependencyObject target, bool value) => target.SetValue(IsLiftedProperty, value);
}
