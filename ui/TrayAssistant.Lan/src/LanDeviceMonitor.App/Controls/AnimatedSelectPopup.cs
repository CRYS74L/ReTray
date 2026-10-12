using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace LanDeviceMonitor.Controls;

/// <summary>Preserves the native ComboBox popup while its 180 ms exit completes.</summary>
public sealed class AnimatedSelectPopup : Popup
{
    private bool _closing;
    private bool _finishClose;
    private int _generation;

    static AnimatedSelectPopup() => IsOpenProperty.OverrideMetadata(typeof(AnimatedSelectPopup),
        new FrameworkPropertyMetadata(false, null, CoerceOpen));

    public AnimatedSelectPopup()
    {
        CustomPopupPlacementCallback = (popup, target, offset) =>
        [new CustomPopupPlacement(new System.Windows.Point(-12, target.Height), PopupPrimaryAxis.Horizontal),
         new CustomPopupPlacement(new System.Windows.Point(-12, -popup.Height), PopupPrimaryAxis.Horizontal)];
        Opened += (_, _) =>
        {
            if (Child is System.Windows.Controls.Border surface && PlacementTarget is System.Windows.Controls.ComboBox owner)
            {
                var dpi = VisualTreeHelper.GetDpi(surface).DpiScaleX;
                var border = surface.UseLayoutRounding
                    ? (Math.Round(surface.BorderThickness.Left*dpi)+Math.Round(surface.BorderThickness.Right*dpi))/dpi
                    : surface.BorderThickness.Left+surface.BorderThickness.Right;
                surface.MinWidth = owner.ActualWidth + surface.Padding.Left + surface.Padding.Right + border;
            }
            if (Child is FrameworkElement panel)
            {
                panel.Opacity = 0;
                panel.RenderTransform = new TranslateTransform(0, 2);
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(() =>
                {
                    panel.UpdateLayout();
                    Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ContextIdle, new Action(AlignSelectedLabel));
                }));
                Animate(true);
            }
        };
    }

    private void AlignSelectedLabel()
    {
        if (!IsOpen || _closing || Child is not FrameworkElement panel || PlacementTarget is not System.Windows.Controls.ComboBox owner) return;
        panel.UpdateLayout();
        var trigger = FindLabel(owner);
        var selected = owner.ItemContainerGenerator.ContainerFromIndex(owner.SelectedIndex);
        var label = selected is null ? null : FindLabel(selected);
        if (trigger is null || label is null) return;
        var target = trigger.PointToScreen(new System.Windows.Point());
        // An anchor outside the work area cannot have a visible aligned popup.
        var screenPoint = new System.Drawing.Point((int)Math.Round(target.X), (int)Math.Round(target.Y));
        if (!System.Windows.Forms.Screen.FromPoint(screenPoint).WorkingArea.Contains(screenPoint)) return;
        var delta = label.PointToScreen(new System.Windows.Point()).X - target.X;
        if (Math.Abs(delta) > .25) HorizontalOffset -= delta / VisualTreeHelper.GetDpi(owner).DpiScaleX;
    }

    private static System.Windows.Controls.TextBlock? FindLabel(DependencyObject root)
    {
        if (root is System.Windows.Controls.TextBlock text && !string.IsNullOrWhiteSpace(text.Text)) return text;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            if (FindLabel(VisualTreeHelper.GetChild(root, i)) is { } found) return found;
        return null;
    }

    private static object CoerceOpen(DependencyObject value, object requested)
    {
        var popup = (AnimatedSelectPopup)value;
        if ((bool)requested)
        {
            if (popup._closing) { popup._closing = false; popup.Animate(true); }
            return true;
        }
        if (popup._finishClose || !popup.IsOpen || !SystemParameters.ClientAreaAnimation) return false;
        if (!popup._closing) { popup._closing = true; popup.Animate(false); }
        return true;
    }

    private void Animate(bool open)
    {
        if (Child is not FrameworkElement panel) return;
        var generation = ++_generation;
        panel.IsHitTestVisible = open;
        if (panel.RenderTransform is not TranslateTransform transform)
            panel.RenderTransform = transform = new TranslateTransform();
        var duration = SystemParameters.ClientAreaAnimation ? TimeSpan.FromMilliseconds(180) : TimeSpan.Zero;
        var opacity = Motion(open ? 1 : 0, duration);
        opacity.Completed += (_, _) =>
        {
            if (generation != _generation) return;
            panel.BeginAnimation(UIElement.OpacityProperty, null);
            panel.Opacity = open ? 1 : 0;
            transform.BeginAnimation(TranslateTransform.YProperty, null);
            transform.Y = open ? 0 : 2;
            if (open) return;
            _finishClose = true;
            CoerceValue(IsOpenProperty);
            _finishClose = false;
            _closing = false;
        };
        panel.BeginAnimation(UIElement.OpacityProperty, opacity, HandoffBehavior.SnapshotAndReplace);
        transform.BeginAnimation(TranslateTransform.YProperty, Motion(open ? 0 : 2, duration), HandoffBehavior.SnapshotAndReplace);
    }

    private static DoubleAnimationUsingKeyFrames Motion(double target, TimeSpan duration)
    {
        var animation = new DoubleAnimationUsingKeyFrames();
        animation.KeyFrames.Add(new SplineDoubleKeyFrame(target, KeyTime.FromTimeSpan(duration), new KeySpline(.23, 1, .32, 1)));
        return animation;
    }
}
