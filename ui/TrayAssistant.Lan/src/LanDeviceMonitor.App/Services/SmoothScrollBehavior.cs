using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace LanDeviceMonitor.Services;

internal static class SmoothScrollBehavior
{
    private const double DeviceListWheelDistance = 170d;
    private const double DeviceListMaximumPendingDistance = 340d;
    private const double ContentWheelDistance = 180d;
    private const double ContentMaximumPendingDistance = 360d;
    private const double DropDownWheelDistance = 10d;
    private const double DropDownMaximumPendingDistance = 10d;
    private const double AnimationDurationMilliseconds = 400d;
    private const double DropDownAnimationDurationMilliseconds = 150d;
    private static readonly ConditionalWeakTable<ScrollViewer, ScrollController>
        Controllers = new();

    public static bool TryHandle(DependencyObject source, MouseWheelEventArgs e)
    {
        return TryHandle(source, e, isDropDown: false);
    }

    public static bool TryHandleDropDown(
        DependencyObject source,
        MouseWheelEventArgs e)
    {
        return TryHandle(source, e, isDropDown: true);
    }

    private static bool TryHandle(
        DependencyObject source,
        MouseWheelEventArgs e,
        bool isDropDown)
    {
        if (e.Delta == 0)
        {
            return false;
        }

        var isDeviceList = !isDropDown &&
            FindVisualParent<System.Windows.Controls.ListBox>(source) is not null;
        var viewer = FindScrollableViewer(source);
        if (viewer is null)
        {
            return false;
        }

        var controller = Controllers.GetValue(
            viewer,
            target => new ScrollController(target));
        return controller.ScrollBy(
            GetScaledDelta(
                e.Delta,
                isDropDown
                    ? DropDownWheelDistance
                    : isDeviceList
                    ? DeviceListWheelDistance
                    : ContentWheelDistance),
            isDropDown
                ? DropDownMaximumPendingDistance
                : isDeviceList
                ? DeviceListMaximumPendingDistance
                : ContentMaximumPendingDistance,
            isDropDown
                ? DropDownAnimationDurationMilliseconds
                : AnimationDurationMilliseconds);
    }

    internal static double GetDeviceListWheelDelta(int wheelDelta) =>
        GetScaledDelta(wheelDelta, DeviceListWheelDistance);

    internal static double GetContentWheelDelta(int wheelDelta) =>
        GetScaledDelta(wheelDelta, ContentWheelDistance);

    internal static double GetDropDownWheelDelta(int wheelDelta) =>
        GetScaledDelta(wheelDelta, DropDownWheelDistance);

    internal static double GetAnimationDurationMilliseconds() =>
        AnimationDurationMilliseconds;

    internal static double GetDropDownAnimationDurationMilliseconds() =>
        DropDownAnimationDurationMilliseconds;

    internal static double EaseOutQuadratic(double progress)
    {
        var clamped = Math.Clamp(progress, 0d, 1d);
        return 1d - Math.Pow(1d - clamped, 2d);
    }

    internal static double GetBoundedTargetOffset(
        double currentOffset,
        double previousTargetOffset,
        double wheelDelta,
        double scrollableHeight,
        double maximumPendingDistance = DeviceListMaximumPendingDistance)
    {
        var remaining = previousTargetOffset - currentOffset;
        if (Math.Sign(remaining) != Math.Sign(wheelDelta))
        {
            remaining = 0d;
        }

        var boundedDistance = Math.Clamp(
            remaining + wheelDelta,
            -maximumPendingDistance,
            maximumPendingDistance);
        return Math.Clamp(
            currentOffset + boundedDistance,
            0d,
            scrollableHeight);
    }

    private static double GetScaledDelta(int wheelDelta, double distance)
    {
        if (wheelDelta == 0)
        {
            return 0d;
        }

        var magnitude = Math.Clamp(Math.Abs(wheelDelta) / 120d, 0.05d, 1d);
        return -Math.Sign(wheelDelta) * distance * magnitude;
    }

    private static T? FindVisualParent<T>(DependencyObject? current)
        where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T match)
            {
                return match;
            }

            current = current is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(current)
                : LogicalTreeHelper.GetParent(current);
        }

        return null;
    }

    private static ScrollViewer? FindScrollableViewer(
        DependencyObject? current)
    {
        while (current is not null)
        {
            if (current is ScrollViewer viewer &&
                viewer.ScrollableHeight > 0)
            {
                return viewer;
            }

            current = current is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(current)
                : LogicalTreeHelper.GetParent(current);
        }

        return null;
    }

    private sealed class ScrollController
    {
        private readonly ScrollViewer _viewer;
        private readonly Stopwatch _animationStopwatch = new();
        private double _animationStartOffset;
        private double _animationTargetOffset;
        private double _animationDurationMilliseconds =
            AnimationDurationMilliseconds;
        private bool _isAnimating;

        public ScrollController(ScrollViewer viewer)
        {
            _viewer = viewer;
        }

        public bool ScrollBy(
            double wheelDelta,
            double maximumPendingDistance,
            double animationDurationMilliseconds)
        {
            var current = _viewer.VerticalOffset;
            var target = GetBoundedTargetOffset(
                current,
                _isAnimating ? _animationTargetOffset : current,
                wheelDelta,
                _viewer.ScrollableHeight,
                maximumPendingDistance);
            if (Math.Abs(target - current) < 0.1d)
            {
                return false;
            }

            if (!SystemParameters.ClientAreaAnimation)
            {
                StopAnimation();
                _viewer.ScrollToVerticalOffset(target);
                return true;
            }

            _animationStartOffset = current;
            _animationTargetOffset = target;
            _animationDurationMilliseconds = animationDurationMilliseconds;
            _animationStopwatch.Restart();
            if (!_isAnimating)
            {
                _isAnimating = true;
                CompositionTarget.Rendering += CompositionTarget_Rendering;
            }

            return true;
        }

        private void CompositionTarget_Rendering(object? sender, EventArgs e)
        {
            if (!_viewer.IsVisible)
            {
                StopAnimation();
                return;
            }

            var progress =
                _animationStopwatch.Elapsed.TotalMilliseconds /
                _animationDurationMilliseconds;
            var eased = EaseOutQuadratic(progress);
            _viewer.ScrollToVerticalOffset(
                _animationStartOffset +
                ((_animationTargetOffset - _animationStartOffset) * eased));

            if (progress >= 1d)
            {
                _viewer.ScrollToVerticalOffset(_animationTargetOffset);
                StopAnimation();
            }
        }

        private void StopAnimation()
        {
            if (!_isAnimating)
            {
                return;
            }

            _isAnimating = false;
            _animationStopwatch.Stop();
            CompositionTarget.Rendering -= CompositionTarget_Rendering;
        }
    }
}
