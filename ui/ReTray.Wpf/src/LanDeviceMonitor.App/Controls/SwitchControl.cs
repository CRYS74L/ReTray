using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace LanDeviceMonitor.Controls;

public class SwitchControl : System.Windows.Controls.CheckBox
{
    public static readonly DependencyProperty IsBusyProperty = DependencyProperty.Register(
        nameof(IsBusy), typeof(bool), typeof(SwitchControl),
        new PropertyMetadata(false, (sender, _) => sender.CoerceValue(IsEnabledProperty)));

    // Saving blocks another activation while preserving the switch's appearance.
    // An unavailable switch still uses the normal disabled presentation.
    public bool IsBusy
    {
        get => (bool)GetValue(IsBusyProperty);
        set => SetValue(IsBusyProperty, value);
    }

    protected override bool IsEnabledCore => base.IsEnabledCore && !IsBusy;

    private readonly TranslateTransform _translation = new();
    protected override AutomationPeer OnCreateAutomationPeer() => new SwitchAutomationPeer(this);

    private sealed class SwitchAutomationPeer(SwitchControl owner) : CheckBoxAutomationPeer(owner), IToggleProvider
    {
        public override object GetPattern(PatternInterface pattern) =>
            pattern == PatternInterface.Toggle ? this : base.GetPattern(pattern);

        ToggleState IToggleProvider.ToggleState => owner.IsChecked switch
        {
            true => ToggleState.On,
            false => ToggleState.Off,
            null => ToggleState.Indeterminate
        };

        void IToggleProvider.Toggle()
        {
            if (!IsEnabled()) throw new ElementNotEnabledException();
            owner.OnClick();
        }
    }

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
