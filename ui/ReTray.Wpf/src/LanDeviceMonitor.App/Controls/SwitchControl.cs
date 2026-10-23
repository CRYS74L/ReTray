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
    private FrameworkElement? _activeTrack;
    private ClockGroup? _motion;
    private int _motionGeneration;
    private const double MotionMilliseconds = 200;

    public SwitchControl()
    {
        Unloaded += (_, _) => UpdateVisualState(false);
    }
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
        ClearMotion();
        base.OnApplyTemplate();
        if (GetTemplateChild("PART_Knob") is FrameworkElement knob)
        {
            knob.RenderTransform = _translation;
            // Only the static circle/shadow is cached, at the display's native
            // pixel density. Its transform runs outside the cached surface.
            knob.CacheMode = new BitmapCache { RenderAtScale = VisualTreeHelper.GetDpi(this).DpiScaleX };
        }
        _activeTrack = GetTemplateChild("PART_ActiveTrack") as FrameworkElement;
        UpdateVisualState(false);
    }

    protected override void OnChecked(RoutedEventArgs e)
    {
        UpdateVisualState(true);
        base.OnChecked(e);
    }

    protected override void OnUnchecked(RoutedEventArgs e)
    {
        UpdateVisualState(true);
        base.OnUnchecked(e);
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        if (GetTemplateChild("PART_Knob") is FrameworkElement knob)
            knob.CacheMode = new BitmapCache { RenderAtScale = newDpi.DpiScaleX };
    }

    private void UpdateVisualState(bool animate)
    {
        if (_activeTrack is null) return;
        var currentX = _translation.X;
        var currentOpacity = _activeTrack.Opacity;
        var targetOpacity = IsChecked == true ? 1d : 0d;
        var targetX = targetOpacity * 20;
        var generation = ++_motionGeneration;
        ClearMotion();
        _translation.X = currentX;
        _activeTrack.Opacity = currentOpacity;
        if (!animate || !IsLoaded || !SystemParameters.ClientAreaAnimation ||
            (Math.Abs(currentX - targetX) < .001 && Math.Abs(currentOpacity - targetOpacity) < .001))
        {
            _translation.X = targetX; _activeTrack.Opacity = targetOpacity;
            return;
        }

        var duration = TimeSpan.FromMilliseconds(MotionMilliseconds);
        var ease = new SineEase { EasingMode = EasingMode.EaseInOut };
        var translation = new DoubleAnimation(currentX, targetX, duration) { EasingFunction = ease };
        var opacity = new DoubleAnimation(currentOpacity, targetOpacity, duration) { EasingFunction = ease };
        var timeline = new ParallelTimeline();
        timeline.Children.Add(translation); timeline.Children.Add(opacity);
        var motion = (ClockGroup)timeline.CreateClock();
        motion.Completed += (_, _) =>
        {
            if (generation != _motionGeneration) return;
            _translation.X = targetX; _activeTrack.Opacity = targetOpacity;
            ClearMotion();
        };
        _motion = motion;
        _translation.ApplyAnimationClock(TranslateTransform.XProperty, (AnimationClock)motion.Children[0], HandoffBehavior.SnapshotAndReplace);
        _activeTrack.ApplyAnimationClock(OpacityProperty, (AnimationClock)motion.Children[1], HandoffBehavior.SnapshotAndReplace);
        motion.Controller!.Begin();
    }

    private void ClearMotion()
    {
        _motion?.Controller?.Remove(); _motion = null;
        _translation.ApplyAnimationClock(TranslateTransform.XProperty, null);
        _activeTrack?.ApplyAnimationClock(OpacityProperty, null);
    }
}
