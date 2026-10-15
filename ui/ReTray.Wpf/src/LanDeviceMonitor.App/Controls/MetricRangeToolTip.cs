using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using Point = System.Windows.Point;
using ToolTip = System.Windows.Controls.ToolTip;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;

namespace LanDeviceMonitor.Controls;

/// <summary>Keeps a metric hint attached to its card until its exit fade finishes.</summary>
public sealed class MetricRangeToolTip : ToolTip
{
    private FrameworkElement? _target;
    private IntPtr _handle;
    private Point _anchor;
    private NativeRect _origin;
    private Popup? _popup;
    private PopupAnimation _nativeAnimation;
    private bool _closing;
    private bool _finishClose;
    private bool _scrollExit;
    private int _generation;

    static MetricRangeToolTip() => IsOpenProperty.OverrideMetadata(typeof(MetricRangeToolTip),
        new FrameworkPropertyMetadata(false, null, CoerceOpen));

    protected override void OnOpened(RoutedEventArgs e)
    {
        // Use the public logical parent; no private WPF fields or global settings.
        _popup = LogicalTreeHelper.GetParent(this) as Popup;
        if (_popup is not null) _nativeAnimation = _popup.PopupAnimation;
        _scrollExit = false;
        BeginAnimation(OpacityProperty, null);
        SetCurrentValue(OpacityProperty, 1d);
        base.OnOpened(e);
        if (!IsOpen || PlacementTarget is not FrameworkElement target || !target.IsVisible) return;
        DetachTarget();
        _target = target;
        _handle = (PresentationSource.FromVisual(this) as HwndSource)?.Handle ?? IntPtr.Zero;
        CaptureAnchor();
        target.LayoutUpdated += TargetLayoutUpdated;
        target.MouseEnter += TargetMouseEntered;
        target.ToolTipOpening += TargetToolTipOpening;
    }

    protected override void OnClosed(RoutedEventArgs e)
    {
        ++_generation;
        _closing = false;
        DetachTarget();
        // WPF's service can force-destroy a popup during a rapid reopen while our
        // coerced IsOpen is still true. Do not leave an "open" tooltip without a HWND.
        if (IsOpen)
        {
            _finishClose = true;
            SetCurrentValue(IsOpenProperty, false);
            _finishClose = false;
        }
        BeginAnimation(OpacityProperty, null);
        SetCurrentValue(OpacityProperty, 1d);
        if (_popup is not null) _popup.SetCurrentValue(Popup.PopupAnimationProperty, _nativeAnimation);
        base.OnClosed(e);
    }

    private static object CoerceOpen(DependencyObject value, object requested)
    {
        var tip = (MetricRangeToolTip)value;
        if ((bool)requested)
        {
            if (tip._closing)
            {
                ++tip._generation;
                tip._closing = false;
                tip._scrollExit = false;
                tip.BeginAnimation(OpacityProperty, null);
                tip.SetCurrentValue(OpacityProperty, 1d);
                tip.CaptureAnchor();
            }
            return true;
        }
        // Also suppress native trailing motion for hidden targets and reduced motion.
        if (tip.IsOpen) tip._popup?.SetCurrentValue(Popup.PopupAnimationProperty, PopupAnimation.None);
        if (tip._finishClose || !tip.IsOpen || tip._target is not { IsVisible: true } ||
            !SystemParameters.ClientAreaAnimation || !SystemParameters.ToolTipAnimation) return false;
        if (!tip._closing)
        {
            tip._closing = true;
            tip.FollowAnchor();
            var generation = ++tip._generation;
            var fade = new DoubleAnimation(tip.Opacity, 0d, TimeSpan.FromMilliseconds(120))
            { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }, FillBehavior = FillBehavior.HoldEnd };
            fade.Completed += (_, _) =>
            {
                if (generation != tip._generation) return;
                tip._finishClose = true;
                tip.SetCurrentValue(IsOpenProperty, false);
                tip._finishClose = false;
            };
            // Prevent a second native fade after our anchored fade completes.
            tip.BeginAnimation(OpacityProperty, fade, HandoffBehavior.SnapshotAndReplace);
        }
        return true;
    }

    internal void CloseImmediately()
    {
        ++_generation;
        _finishClose = true;
        _popup?.SetCurrentValue(Popup.PopupAnimationProperty, PopupAnimation.None);
        SetCurrentValue(IsOpenProperty, false);
        CoerceValue(IsOpenProperty);
        _finishClose = false;
    }

    internal void CloseForScroll()
    {
        _scrollExit = true;
        SetCurrentValue(IsOpenProperty, false);
    }

    private void TargetMouseEntered(object sender, MouseEventArgs e)
    {
        if (_closing && !_scrollExit) SetCurrentValue(IsOpenProperty, true);
    }

    private void TargetToolTipOpening(object sender, ToolTipEventArgs e)
    {
        if (_closing && !e.Handled) SetCurrentValue(IsOpenProperty, true);
    }

    private void TargetLayoutUpdated(object? sender, EventArgs e)
    {
        if (_closing) FollowAnchor();
        else CaptureAnchor();
    }

    private void CaptureAnchor()
    {
        if (_target is not { IsVisible: true } || _handle == IntPtr.Zero) return;
        _anchor = _target.PointToScreen(new Point());
        GetWindowRect(_handle, out _origin);
    }

    private void FollowAnchor()
    {
        if (_target is not { IsVisible: true } || _handle == IntPtr.Zero) return;
        var current = _target.PointToScreen(new Point());
        var x = _origin.Left + (int)Math.Round(current.X - _anchor.X);
        var y = _origin.Top + (int)Math.Round(current.Y - _anchor.Y);
        if (GetWindowRect(_handle, out var rect) && (rect.Left != x || rect.Top != y))
            SetWindowPos(_handle, IntPtr.Zero, x, y, 0, 0, 0x0001 | 0x0004 | 0x0010);
    }

    private void DetachTarget()
    {
        if (_target is not null)
        {
            _target.LayoutUpdated -= TargetLayoutUpdated;
            _target.MouseEnter -= TargetMouseEntered;
            _target.ToolTipOpening -= TargetToolTipOpening;
        }
        _target = null;
        _handle = IntPtr.Zero;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr handle, out NativeRect rect);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr handle, IntPtr after, int x, int y, int width, int height, uint flags);
}
