using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;
using LanDeviceMonitor.Controls;

public static class HoverTests
{
    public static void Run()
    {
        // The child harness owns a plain Application; never start the product lifecycle.
        var app = Application.Current ?? throw new InvalidOperationException("Missing isolated hover application");
        var failures = new List<string>();
        foreach (string theme in new[] { "Dark", "Light" })
        foreach (bool menu in new[] { true, false })
        {
            LanDeviceMonitor.Services.ThemeService.Apply(theme);
            Control item = menu ? new MenuItem() : new ComboBoxItem();
            item.Width = 160; item.Height = 32;
            item.Template = (ControlTemplate)XamlReader.Parse("""
                <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="Control">
                    <Grid x:Name="StudioRoot"><Border x:Name="StudioSurface"/></Grid>
                </ControlTemplate>
                """);
            var owner = new ItemsControl { Width = 180, Height = 40 };
            owner.Items.Add(item);
            var stateType = typeof(StudioControlMotion).GetNestedType("MotionState", BindingFlags.NonPublic)!;
            var state = Activator.CreateInstance(stateType, BindingFlags.Instance | BindingFlags.NonPublic,
                null, [item], null)!;
            owner.Measure(new Size(180, 40)); owner.Arrange(new Rect(0, 0, 180, 40)); owner.UpdateLayout();
            item.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
            var surface = (Border)item.Template.FindName("StudioSurface", item);
            var brush = (SolidColorBrush)surface.Background;
            var backdrop = (Color)app.Resources["StudioSurfaceColor"];
            var hover = (Color)app.Resources["StudioHoverColor"];
            string label = theme + "/" + item.GetType().Name;

            // Reproduce leaving a settled hover. Sample real WPF animation
            // values; composited brightness must stay inside the two endpoint
            // colors, including when opacity reaches zero.
            brush.BeginAnimation(SolidColorBrush.ColorProperty, null);
            brush.Color = hover;
            item.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseLeaveEvent });
            int samples = 0; double peak = 0;
            var clock = Stopwatch.StartNew();
            while (clock.ElapsedMilliseconds < 650)
            {
                Pump(8);
                Color actual = brush.Color;
                double red = actual.R * actual.A / 255d + backdrop.R * (1 - actual.A / 255d);
                peak = Math.Max(peak, red); samples++;
            }
            var target = (Color)brush.GetAnimationBaseValue(SolidColorBrush.ColorProperty);
            if (target.A != 0 || target.R != hover.R || target.G != hover.G || target.B != hover.B)
                failures.Add(label + " hover exit must retain hover RGB at alpha zero: " + target);
            if (peak > Math.Max(backdrop.R, hover.R) + 1)
                failures.Add(label + $" exit brightens beyond hover: peak {peak:F2}, hover {hover.R}, backdrop {backdrop.R}");
            Console.WriteLine($"Hover {label}: samples={samples}, peak={peak:F2}, endpoints={backdrop.R}/{hover.R}, target={target}");

            // Highlight is a WPF read-only state. Change its real dependency
            // property to verify that both item types observe later transitions,
            // rather than relying on MouseLeave event ordering.
            var key = (DependencyPropertyKey)item.GetType().GetField("IsHighlightedPropertyKey",
                BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
            item.SetValue(key, true);
            var highlightTarget = (Color)brush.GetAnimationBaseValue(SolidColorBrush.ColorProperty);
            if (highlightTarget != hover) failures.Add(label + " ignores IsHighlighted changes");
            item.SetValue(key, false);

            // Mouse movement over another item must retire a keyboard-focus
            // capsule retained by this container, without moving actual focus.
            var keyboard = stateType.GetField("_keyboardFocus", BindingFlags.NonPublic | BindingFlags.Instance)!;
            keyboard.SetValue(state, true);
            owner.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.PreviewMouseMoveEvent });
            if ((bool)keyboard.GetValue(state)!) failures.Add(label + " retains keyboard emphasis after pointer takes over");
            item.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
        }
        LanDeviceMonitor.Services.ThemeService.Apply("Light");
        if (failures.Count > 0) throw new InvalidOperationException(string.Join(Environment.NewLine, failures));
    }

    private static void Pump(int milliseconds)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start(); Dispatcher.PushFrame(frame);
    }
}
