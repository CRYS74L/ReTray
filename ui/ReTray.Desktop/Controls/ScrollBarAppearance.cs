using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;

namespace ReTray_App.Controls;

/// <summary>Resolves hover from geometry even while a child Thumb owns mouse capture.</summary>
public static class ScrollBarAppearance
{
    private static readonly DependencyProperty StateProperty = DependencyProperty.RegisterAttached(
        "State", typeof(PointerState), typeof(ScrollBarAppearance));
    private static readonly DependencyPropertyKey IsPointerOverPropertyKey = DependencyProperty.RegisterAttachedReadOnly(
        "IsPointerOver", typeof(bool), typeof(ScrollBarAppearance), new FrameworkPropertyMetadata(false));

    public static readonly DependencyProperty IsPointerOverProperty = IsPointerOverPropertyKey.DependencyProperty;
    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached(
        "Enabled", typeof(bool), typeof(ScrollBarAppearance), new FrameworkPropertyMetadata(false, EnabledChanged));

    public static bool GetEnabled(DependencyObject element) => (bool)element.GetValue(EnabledProperty);
    public static void SetEnabled(DependencyObject element, bool value) => element.SetValue(EnabledProperty, value);
    public static bool GetIsPointerOver(DependencyObject element) => (bool)element.GetValue(IsPointerOverProperty);

    private static void EnabledChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        if (sender is not ScrollBar bar) return;
        (bar.GetValue(StateProperty) as PointerState)?.Detach();
        if ((bool)args.NewValue) bar.SetValue(StateProperty, new PointerState(bar));
        else bar.ClearValue(StateProperty);
    }

    private sealed class PointerState
    {
        private readonly ScrollBar _bar;
        private readonly MouseEventHandler _mouseChanged;
        private readonly MouseButtonEventHandler _mouseReleased;
        private readonly DependencyPropertyDescriptor _minimum;
        private readonly DependencyPropertyDescriptor _maximum;
        private bool _watching;
        private int _generation;

        internal PointerState(ScrollBar bar)
        {
            _bar = bar;
            _mouseChanged = MouseChanged;
            _mouseReleased = MouseReleased;
            _minimum = DependencyPropertyDescriptor.FromProperty(RangeBase.MinimumProperty, typeof(ScrollBar))!;
            _maximum = DependencyPropertyDescriptor.FromProperty(RangeBase.MaximumProperty, typeof(ScrollBar))!;
            _bar.Loaded += Loaded;
            _bar.Unloaded += Unloaded;
            if (_bar.IsLoaded) Watch();
        }

        private void Loaded(object sender, RoutedEventArgs args) => Watch();
        private void Unloaded(object sender, RoutedEventArgs args) => Stop();

        private void Watch()
        {
            if (_watching) return;
            _watching = true;
            _bar.AddHandler(Mouse.MouseEnterEvent, _mouseChanged, true);
            _bar.AddHandler(Mouse.MouseLeaveEvent, _mouseChanged, true);
            _bar.AddHandler(Mouse.MouseMoveEvent, _mouseChanged, true);
            _bar.AddHandler(Mouse.PreviewMouseUpEvent, _mouseReleased, true);
            _bar.AddHandler(Mouse.LostMouseCaptureEvent, _mouseChanged, true);
            _bar.IsVisibleChanged += VisibleChanged;
            _bar.IsEnabledChanged += EnabledChanged;
            _bar.SizeChanged += SizeChanged;
            _minimum.AddValueChanged(_bar, RangeChanged);
            _maximum.AddValueChanged(_bar, RangeChanged);
            Update();
        }

        private void MouseChanged(object sender, MouseEventArgs args) => UpdateAndResolveCapture();
        private void MouseReleased(object sender, MouseButtonEventArgs args) => UpdateAndResolveCapture();
        private void VisibleChanged(object sender, DependencyPropertyChangedEventArgs args) => Update();
        private void EnabledChanged(object sender, DependencyPropertyChangedEventArgs args) => Update();
        private void SizeChanged(object sender, SizeChangedEventArgs args) => Update();
        private void RangeChanged(object? sender, EventArgs args) => Update();

        private void UpdateAndResolveCapture()
        {
            Update();
            var generation = ++_generation;
            // PreviewMouseUp precedes the native Thumb's capture release. Recheck
            // afterwards so an outside release cannot retain the expanded thumb.
            _bar.Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
            {
                if (_watching && generation == _generation) Update();
            }));
        }

        private void Update()
        {
            var hasRange = _bar.Maximum > _bar.Minimum;
            _bar.SetCurrentValue(UIElement.IsHitTestVisibleProperty, hasRange);
            var point = Mouse.GetPosition(_bar);
            var inside = _watching && hasRange && _bar.IsVisible && _bar.IsEnabled && _bar.IsMouseOver &&
                         point.X >= 0 && point.Y >= 0 && point.X < _bar.ActualWidth && point.Y < _bar.ActualHeight;
            _bar.SetValue(IsPointerOverPropertyKey, inside);
        }

        private void Stop()
        {
            if (!_watching) return;
            _watching = false;
            _generation++;
            _bar.RemoveHandler(Mouse.MouseEnterEvent, _mouseChanged);
            _bar.RemoveHandler(Mouse.MouseLeaveEvent, _mouseChanged);
            _bar.RemoveHandler(Mouse.MouseMoveEvent, _mouseChanged);
            _bar.RemoveHandler(Mouse.PreviewMouseUpEvent, _mouseReleased);
            _bar.RemoveHandler(Mouse.LostMouseCaptureEvent, _mouseChanged);
            _bar.IsVisibleChanged -= VisibleChanged;
            _bar.IsEnabledChanged -= EnabledChanged;
            _bar.SizeChanged -= SizeChanged;
            _minimum.RemoveValueChanged(_bar, RangeChanged);
            _maximum.RemoveValueChanged(_bar, RangeChanged);
            _bar.SetValue(IsPointerOverPropertyKey, false);
        }

        internal void Detach()
        {
            Stop();
            _bar.Loaded -= Loaded;
            _bar.Unloaded -= Unloaded;
        }
    }
}
