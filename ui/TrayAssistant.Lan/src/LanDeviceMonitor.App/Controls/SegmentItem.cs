using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Brush = System.Windows.Media.Brush;

namespace LanDeviceMonitor.Controls;

/// <summary>Owns its text brush so hover transitions never animate a shared theme resource.</summary>
public class SegmentItem : ListBoxItem
{
    public static readonly DependencyProperty ActiveBrushProperty = DependencyProperty.Register(
        nameof(ActiveBrush), typeof(Brush), typeof(SegmentItem), new PropertyMetadata(null));
    public static readonly DependencyProperty InactiveBrushProperty = DependencyProperty.Register(
        nameof(InactiveBrush), typeof(Brush), typeof(SegmentItem), new PropertyMetadata(null));

    private readonly SolidColorBrush _textBrush = new();
    private int _transitionGeneration;
    public Brush ActiveBrush { get => (Brush)GetValue(ActiveBrushProperty); set => SetValue(ActiveBrushProperty, value); }
    public Brush InactiveBrush { get => (Brush)GetValue(InactiveBrushProperty); set => SetValue(InactiveBrushProperty, value); }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        Foreground = _textBrush;
        UpdateText(false);
    }

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (_textBrush is null) return;
        if (e.Property == IsEnabledProperty)
            UpdateText(true);
        else if (e.Property == ActiveBrushProperty || e.Property == InactiveBrushProperty)
            UpdateText(false);
    }

    private void UpdateText(bool animate)
    {
        SetEmphasis(IsSelected || (IsMouseOver && IsEnabled), animate);
    }

    protected override void OnSelected(RoutedEventArgs e)
    {
        base.OnSelected(e);
        if (_textBrush is not null) SetEmphasis(true, true);
    }

    protected override void OnUnselected(RoutedEventArgs e)
    {
        base.OnUnselected(e);
        if (_textBrush is not null) SetEmphasis(IsMouseOver && IsEnabled, true);
    }

    protected override void OnMouseEnter(System.Windows.Input.MouseEventArgs e)
    {
        base.OnMouseEnter(e);
        SetEmphasis(IsSelected || IsEnabled, true);
    }

    protected override void OnMouseLeave(System.Windows.Input.MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        SetEmphasis(IsSelected, true);
    }

    private void SetEmphasis(bool emphasized, bool animate)
    {
        var targetBrush = emphasized ? ActiveBrush : InactiveBrush;
        if (targetBrush is not SolidColorBrush solid) return;
        AnimateTextColor(solid.Color, animate);
    }

    private void AnimateTextColor(System.Windows.Media.Color target, bool animate)
    {
        var current = _textBrush.Color;
        var generation = ++_transitionGeneration;
        _textBrush.BeginAnimation(SolidColorBrush.ColorProperty, null);
        if (animate && IsLoaded && SystemParameters.ClientAreaAnimation && current != target)
        {
            // Keep the presentation color as the base until the new clock starts.
            // Setting the base to the destination causes a one-frame jump on reversal.
            _textBrush.Color = current;
            var animation = new ColorAnimation(current, target, TimeSpan.FromMilliseconds(180))
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            animation.Completed += (_, _) =>
            {
                if (generation != _transitionGeneration) return;
                _textBrush.Color = target;
                _textBrush.BeginAnimation(SolidColorBrush.ColorProperty, null);
            };
            _textBrush.BeginAnimation(SolidColorBrush.ColorProperty, animation);
        }
        else _textBrush.Color = target;
    }
}
