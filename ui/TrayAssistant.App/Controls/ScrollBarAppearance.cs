using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls.Primitives;
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
        private long minimumToken;
        private bool watching, queued, updateRequested;

        public OverflowVisibility(ScrollBar bar)
        {
            this.bar = bar;
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
            QueueUpdate();
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
            bar.UnregisterPropertyChangedCallback(RangeBase.MinimumProperty, minimumToken);
            bar.LayoutUpdated -= LayoutUpdated;
            bar.SizeChanged -= SizeChanged;
        }

        public void Detach()
        {
            StopWatching();
            bar.Loaded -= Loaded;
            bar.Unloaded -= Unloaded;
        }
    }
}
