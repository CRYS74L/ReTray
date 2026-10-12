using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace LanDeviceMonitor.Controls;

public class SwitchControl : System.Windows.Controls.CheckBox
{
    private readonly TranslateTransform _translation = new();
    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        if (GetTemplateChild("PART_Knob") is FrameworkElement knob)
            knob.RenderTransform = _translation;
        UpdateKnob(false);
    }

    protected override void OnChecked(RoutedEventArgs e)
    {
        base.OnChecked(e);
        UpdateKnob(true);
    }

    protected override void OnUnchecked(RoutedEventArgs e)
    {
        base.OnUnchecked(e);
        UpdateKnob(true);
    }

    private void UpdateKnob(bool animate)
    {
        var current = _translation.X;
        var target = IsChecked == true ? 20d : 0d;
        _translation.BeginAnimation(TranslateTransform.XProperty, null);
        _translation.X = target;
        if (animate && IsLoaded && SystemParameters.ClientAreaAnimation)
            _translation.BeginAnimation(TranslateTransform.XProperty,
                new DoubleAnimation(current, target, TimeSpan.FromMilliseconds(180))
                { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
    }
}
