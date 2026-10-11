using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using System.Runtime.CompilerServices;

namespace TrayAssistant_App.Controls;

public static class ScrollBarAppearance
{
    private static readonly ConditionalWeakTable<ScrollBar, OverflowVisibility> Attachments = new();
    public static readonly DependencyProperty PersistentProperty = DependencyProperty.RegisterAttached("Persistent", typeof(bool), typeof(ScrollBarAppearance), new PropertyMetadata(false, Changed));
    public static readonly DependencyProperty ScrollableHeightProperty = DependencyProperty.RegisterAttached("ScrollableHeight", typeof(double), typeof(ScrollBarAppearance), new PropertyMetadata(0d, RangeChanged));
    public static readonly DependencyProperty ViewportHeightProperty = DependencyProperty.RegisterAttached("ViewportHeight", typeof(double), typeof(ScrollBarAppearance), new PropertyMetadata(0d, RangeChanged));

    public static bool GetPersistent(DependencyObject element) => (bool)element.GetValue(PersistentProperty);
    public static void SetPersistent(DependencyObject element, bool value) => element.SetValue(PersistentProperty, value);
    public static double GetScrollableHeight(DependencyObject element) => (double)element.GetValue(ScrollableHeightProperty);
    public static void SetScrollableHeight(DependencyObject element, double value) => element.SetValue(ScrollableHeightProperty, value);
    public static double GetViewportHeight(DependencyObject element) => (double)element.GetValue(ViewportHeightProperty);
    public static void SetViewportHeight(DependencyObject element, double value) => element.SetValue(ViewportHeightProperty, value);

    private static void Changed(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        if (sender is not ScrollBar bar) return;
        if (Attachments.TryGetValue(bar, out var previous))
        {
            previous.Detach();
            Attachments.Remove(bar);
        }
        if (args.NewValue is true) Attachments.Add(bar, new OverflowVisibility(bar));
    }

    private static void RangeChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        if (sender is ScrollBar bar && Attachments.TryGetValue(bar, out var attachment)) attachment.QueueUpdate();
    }

    private sealed class OverflowVisibility
    {
        private readonly ScrollBar bar;
        private readonly PointerEventHandler pointerChanged;
        private readonly PointerEventHandler pointerCanceled;
        private long minimumToken;
        private bool watching, queued, updateRequested, pointerInside;
        private int pointerGeneration;

        public OverflowVisibility(ScrollBar bar)
        {
            this.bar = bar;
            pointerChanged = PointerChanged;
            pointerCanceled = (_, _) => UpdatePointerState(false);
            bar.Loaded += Loaded;
            bar.Unloaded += Unloaded;
            if (bar.IsLoaded) Watch();
        }

        private void Loaded(object sender, RoutedEventArgs args) => Watch();
        private void Unloaded(object sender, RoutedEventArgs args) => StopWatching();

        private void Watch()
        {
            if (watching) return;
            watching = true;
            minimumToken = bar.RegisterPropertyChangedCallback(RangeBase.MinimumProperty, (_, _) => QueueUpdate());
            bar.LayoutUpdated += LayoutUpdated;
            bar.SizeChanged += SizeChanged;
            bar.AddHandler(UIElement.PointerEnteredEvent, pointerChanged, true);
            bar.AddHandler(UIElement.PointerExitedEvent, pointerChanged, true);
            bar.AddHandler(UIElement.PointerMovedEvent, pointerChanged, true);
            bar.AddHandler(UIElement.PointerReleasedEvent, pointerChanged, true);
            bar.AddHandler(UIElement.PointerCaptureLostEvent, pointerChanged, true);
            bar.AddHandler(UIElement.PointerCanceledEvent, pointerCanceled, true);
            UpdatePointerState(false);
            QueueUpdate();
        }

        private void PointerChanged(object sender, PointerRoutedEventArgs args)
        {
            var point = args.GetCurrentPoint(bar).Position;
            UpdatePointerState(args.Pointer.PointerDeviceType != Microsoft.UI.Input.PointerDeviceType.Touch &&
                point.X >= 0 && point.Y >= 0 && point.X < bar.ActualWidth && point.Y < bar.ActualHeight);
        }

        private void UpdatePointerState(bool inside)
        {
            pointerInside = inside;
            int generation = ++pointerGeneration;
            // Native Thumb capture can finish after this routed event. Resolve the
            // visual state after native handling, including releases outside the bar.
            bar.DispatcherQueue.TryEnqueue(() =>
            {
                if (!watching || generation != pointerGeneration) return;
                bool hover = pointerInside && bar.IsEnabled && bar.IsHitTestVisible && bar.Visibility == Visibility.Visible;
                VisualStateManager.GoToState(bar, hover ? "PointerOver" : "Normal", true);
            });
        }

        private void SizeChanged(object sender, SizeChangedEventArgs args) => QueueUpdate();

        public void QueueUpdate()
        {
            updateRequested = true;
            if (!watching || queued) return;
            queued = true;
            if (!bar.DispatcherQueue.TryEnqueue(() =>
            {
                queued = false;
                if (!watching || !updateRequested) return;
                // Ensure a layout pass also occurs when the inputs changed outside
                // layout. LayoutUpdated normally consumes the request first.
                bar.InvalidateArrange();
            })) queued = false;
        }

        private void LayoutUpdated(object? sender, object args)
        {
            if (!watching || !updateRequested) return;
            double viewport = GetViewportHeight(bar);
            double maximum = Math.Max(bar.Minimum, GetScrollableHeight(bar));
            bool overflow = maximum > bar.Minimum && viewport > 0;
            if (!overflow)
            {
                pointerInside = false;
                pointerGeneration++;
                VisualStateManager.GoToState(bar, "Normal", false);
            }
            bar.IsHitTestVisible = overflow;
            var visibility = overflow ? Visibility.Visible : Visibility.Collapsed;
            if (bar.Visibility != visibility) bar.Visibility = visibility;
            // A newly shown bar needs its first arrangement before the native
            // thumb calculation can use its actual track length.
            if (overflow && bar.ActualHeight <= 0) return;
            updateRequested = false;
            // The viewer publishes these inputs separately during measure. Pass
            // the final pair to the native ScrollBar after its size is arranged,
            // so it never combines a new viewport with an old track length.
            if (bar.ViewportSize != viewport) bar.ViewportSize = viewport;
            if (bar.Maximum != maximum) bar.Maximum = maximum;
        }

        private void StopWatching()
        {
            if (!watching) return;
            watching = false;
            pointerGeneration++;
            bar.UnregisterPropertyChangedCallback(RangeBase.MinimumProperty, minimumToken);
            bar.LayoutUpdated -= LayoutUpdated;
            bar.SizeChanged -= SizeChanged;
            bar.RemoveHandler(UIElement.PointerEnteredEvent, pointerChanged);
            bar.RemoveHandler(UIElement.PointerExitedEvent, pointerChanged);
            bar.RemoveHandler(UIElement.PointerMovedEvent, pointerChanged);
            bar.RemoveHandler(UIElement.PointerReleasedEvent, pointerChanged);
            bar.RemoveHandler(UIElement.PointerCaptureLostEvent, pointerChanged);
            bar.RemoveHandler(UIElement.PointerCanceledEvent, pointerCanceled);
            pointerInside = false;
            VisualStateManager.GoToState(bar, "Normal", false);
        }

        public void Detach()
        {
            StopWatching();
            bar.Loaded -= Loaded;
            bar.Unloaded -= Unloaded;
        }
    }
}
