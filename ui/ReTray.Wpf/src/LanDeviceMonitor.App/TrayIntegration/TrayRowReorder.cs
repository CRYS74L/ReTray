using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace ReTray_App;

// Like cc-switch's vertical sortable list, keep layout slots stable while the
// lifted row follows the pointer and its neighbours translate out of the way.
internal sealed class TrayRowReorder
{
    private sealed record Slot(ListBoxItem Row, double Top, double Height, TranslateTransform Motion);
    private readonly ListBox list;
    private readonly Action started;
    private readonly Action<int, int> move;
    private readonly Func<bool, Task> finished;
    private readonly DispatcherTimer scrollTimer = new(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(16) };
    private readonly List<Slot> slots = [];
    private object? candidate;
    private Point press;
    private ScrollViewer? scroll;
    private Window? window;
    private double initialScroll;
    private int from, over;
    private bool active, settling, cancelled;
    private Border? surface;
    private ListBoxItem? capture;

    internal TrayRowReorder(ListBox list, Action started, Action<int, int> move, Func<bool, Task> finished)
    {
        this.list = list; this.started = started; this.move = move; this.finished = finished;
        list.PreviewMouseLeftButtonDown += Press;
        list.PreviewMouseMove += Move;
        list.PreviewMouseLeftButtonUp += Release;
        // ListBox handles capture loss during selection; cleanup must still run.
        list.AddHandler(Mouse.LostMouseCaptureEvent, new MouseEventHandler((_, _) =>
        { if (active && !settling && capture?.IsMouseCaptured != true) Cancel(); }), true);
        list.PreviewKeyDown += (_, args) =>
        {
            if (args.Key == Key.Escape && active) { args.Handled = true; Cancel(); }
        };
        list.Loaded += (_, _) =>
        {
            window = Window.GetWindow(list);
            if (window != null) window.Deactivated += Deactivated;
        };
        list.Unloaded += (_, _) =>
        {
            if (window != null) window.Deactivated -= Deactivated;
            window = null; Cancel();
        };
        list.SizeChanged += (_, _) => { if (active) Cancel(); };
        scrollTimer.Tick += (_, _) =>
        {
            if (capture?.IsMouseCaptured != true) Cancel();
            else ScrollAtEdge(Mouse.GetPosition(list));
        };
    }

    private void Deactivated(object? sender, EventArgs args) => Cancel();

    private void Press(object sender, MouseButtonEventArgs args)
    {
        candidate = null;
        if (active || settling || args.OriginalSource is not DependencyObject origin) return;
        // Native icon actions, settings, switches and the scrollbar retain clicks.
        if (Ancestor<ButtonBase>(origin) != null || Ancestor<ScrollBar>(origin) != null) return;
        if (Ancestor<ListBoxItem>(origin) is { } row)
        {
            candidate = list.ItemContainerGenerator.ItemFromContainer(row);
            press = args.GetPosition(list);
        }
    }

    private void Move(object sender, MouseEventArgs args)
    {
        if (settling) return;
        if (args.LeftButton != MouseButtonState.Pressed) { candidate = null; if (active) Cancel(); return; }
        var point = args.GetPosition(list);
        if (!active)
        {
            if (candidate == null || (point - press).Length < 8) return;
            var index = list.Items.IndexOf(candidate);
            candidate = null;
            if (!Begin(index, press)) return;
        }
        Update(point); args.Handled = true;
    }

    private async void Release(object sender, MouseButtonEventArgs args)
    {
        candidate = null;
        if (!active || settling) return;
        args.Handled = true;
        var point = args.GetPosition(list);
        Update(point);
        await CompleteAsync(point.X >= 0 && point.X <= list.ActualWidth && point.Y >= 0 && point.Y <= list.ActualHeight);
    }

    internal bool Begin(int index, Point point)
    {
        if (active || settling || index < 0 || index >= list.Items.Count || list.Items.Count < 2) return false;
        list.UpdateLayout();
        scroll = Descendant<ScrollViewer>(list);
        initialScroll = scroll?.VerticalOffset ?? 0;
        for (int i = 0; i < list.Items.Count; i++)
        {
            if (list.ItemContainerGenerator.ContainerFromIndex(i) is not ListBoxItem row)
            { slots.Clear(); return false; }
            slots.Add(new(row, row.TranslatePoint(new Point(), list).Y, row.ActualHeight, new TranslateTransform()));
        }
        // Capture the lifted item so ListBox's selection gesture cannot release
        // our capture while processing its own mouse-move/selection state.
        capture = slots[index].Row;
        if (!capture.CaptureMouse()) { capture = null; slots.Clear(); return false; }
        from = over = index; press = point; active = true; cancelled = false;
        list.Focus();
        foreach (var slot in slots) slot.Row.RenderTransform = slot.Motion;
        var lifted = slots[from].Row;
        Panel.SetZIndex(lifted, 10);
        lifted.Effect = new DropShadowEffect { BlurRadius = 12, ShadowDepth = 2, Opacity = .18 };
        surface = lifted.Template.FindName("DragSurface", lifted) as Border;
        if (surface != null) surface.Visibility = Visibility.Visible;
        list.SetCurrentValue(FrameworkElement.CursorProperty, Cursors.SizeNS);
        started(); scrollTimer.Start();
        return true;
    }

    internal void Update(Point point)
    {
        if (!active || settling) return;
        var delta = point.Y - press.Y + (scroll?.VerticalOffset ?? 0) - initialScroll;
        delta = Math.Clamp(delta, slots[0].Top - slots[from].Top, slots[^1].Top - slots[from].Top);
        slots[from].Motion.Y = delta;
        var center = slots[from].Top + delta + slots[from].Height / 2;
        var target = Enumerable.Range(0, slots.Count).MinBy(i => Math.Abs(slots[i].Top + slots[i].Height / 2 - center));
        if (over == target) return;
        over = target;
        for (int i = 0; i < slots.Count; i++)
        {
            if (i == from) continue;
            var offset = i > from && i <= over ? -slots[from].Height : i < from && i >= over ? slots[from].Height : 0;
            Animate(slots[i].Motion, offset);
        }
    }

    internal void ScrollAtEdge(Point point)
    {
        if (!active || settling || scroll == null || point.X < 0 || point.X > list.ActualWidth) return;
        const double edge = 32;
        double speed = point.Y < edge ? -Math.Clamp((edge - point.Y) / edge, 0, 1) * 10 :
            point.Y > list.ActualHeight - edge ? Math.Clamp((point.Y - list.ActualHeight + edge) / edge, 0, 1) * 10 : 0;
        if (speed == 0) return;
        scroll.ScrollToVerticalOffset(scroll.VerticalOffset + speed);
        list.UpdateLayout(); Update(point);
    }

    internal void Cancel()
    {
        candidate = null;
        cancelled = true;
        if (active && !settling) _ = CompleteAsync(false);
    }

    internal async Task CompleteAsync(bool commit)
    {
        if (!active || settling) return;
        settling = true; scrollTimer.Stop();
        bool changed = commit && from != over;
        try
        {
            if (commit)
            {
                Animate(slots[from].Motion, slots[over].Top - slots[from].Top);
                if (SystemParameters.ClientAreaAnimation) await Task.Delay(200);
                changed &= !cancelled;
                if (changed) move(from, over);
            }
        }
        finally
        {
            active = false;
            foreach (var slot in slots) slot.Row.ClearValue(UIElement.RenderTransformProperty);
            var lifted = slots[from].Row;
            lifted.ClearValue(Panel.ZIndexProperty); lifted.ClearValue(UIElement.EffectProperty);
            if (surface != null) surface.Visibility = Visibility.Collapsed;
            slots.Clear(); surface = null;
            list.ClearValue(FrameworkElement.CursorProperty);
            if (capture?.IsMouseCaptured == true) capture.ReleaseMouseCapture();
            capture = null;
            list.UpdateLayout();
            try { await finished(changed); }
            finally { settling = false; }
        }
    }

    private static void Animate(TranslateTransform transform, double to)
    {
        var current = transform.Y;
        transform.BeginAnimation(TranslateTransform.YProperty, null);
        transform.Y = to;
        if (!SystemParameters.ClientAreaAnimation) return;
        transform.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(current, to, TimeSpan.FromMilliseconds(200))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }, FillBehavior = FillBehavior.Stop
        });
    }

    private static T? Ancestor<T>(DependencyObject? node) where T : DependencyObject
    {
        while (node != null)
        {
            if (node is T value) return value;
            node = node is Visual ? VisualTreeHelper.GetParent(node) : (node as FrameworkContentElement)?.Parent;
        }
        return null;
    }

    private static T? Descendant<T>(DependencyObject node) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
        {
            var child = VisualTreeHelper.GetChild(node, i);
            if (child is T value) return value;
            if (Descendant<T>(child) is { } nested) return nested;
        }
        return null;
    }
}
