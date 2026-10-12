using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Input;
using System.Windows.Threading;
using LanDeviceMonitor;
using LanDeviceMonitor.Models;
using LanDeviceMonitor.Services;

internal static class MetricToolTipScrollRegression
{
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr handle, IntPtr after, int x, int y, int width, int height, uint flags);

    internal static int Run()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            App? app = null; MainWindow? window = null;
            try
            {
                app = TestAppFactory.Create(); app.InitializeComponent(); app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                window = new MainWindow(new NotificationService())
                { WindowStartupLocation = WindowStartupLocation.Manual, Left = -10000, Top = -10000, Width = 1180, Height = 630, ShowActivated = false, ShowInTaskbar = false };
                typeof(MainWindow).GetField("_isPopulatingAppSettings", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, true);
                var now = DateTimeOffset.Now;
                var device = new DeviceViewData
                {
                    Profile = new DeviceProfile { MacAddress = "02:00:00:00:66:01", LastRouterName = "提示测试设备", LastKnownOnline = true, FirstSeenAt = now.AddDays(-80), LastSeenAt = now },
                    Identity = new DeviceIdentity("手机", "\uE8EA", "提示测试设备"),
                    Sessions = Enumerable.Range(0, 12).Select(i => new DeviceSession { MacAddress = "02:00:00:00:66:01", OnlineAt = now.AddHours(-2 * i - 2), OfflineAt = now.AddHours(-2 * i - 1) }).ToArray()
                };
                typeof(MainWindow).GetMethod("ReplaceDevices", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [new[] { device }, false, false]);
                window.Show(); window.UpdateLayout(); Pump(100);
                Require(window.DetailPanel.ScrollableHeight > 30, "Fixture has no scrollable detail content.");
                foreach (var mode in new[] { ThemeService.LightMode, ThemeService.DarkMode })
                {
                ThemeService.Apply(mode);
                foreach (var card in new[] { window.DownloadTotalCard, window.UploadTotalCard })
                {
                    var tip = (ToolTip)ToolTipService.GetToolTip(card);
                    tip.PlacementTarget = card;
                    tip.Opened += (_, _) =>
                    {
                        if (System.Windows.Interop.HwndSource.FromVisual(tip) is System.Windows.Interop.HwndSource source)
                            SetWindowPos(source.Handle, IntPtr.Zero, -20000, -20000, 0, 0, 0x0001 | 0x0004 | 0x0010);
                    };
                    for (var round = 0; round < 3; round++)
                    {
                        window.DetailPanel.ScrollToTop(); window.UpdateLayout(); Pump(20);
                        tip.IsOpen = true; Pump(220); Require(tip.IsOpen, "Valid tooltip did not open.");
                        var popup = (Popup)LogicalTreeHelper.GetParent(tip);
                        var nativeAnimation = popup.PopupAnimation;
                        var gap = tip.PointToScreen(new Point()) - card.PointToScreen(new Point());
                        Require(tip.FontSize == 12 && tip.Padding == new Thickness(2, 4, 2, 4), "Accepted tooltip type sizing changed.");
                        var surface = (Border)VisualTreeHelper.GetChild(tip, 0);
                        Require(Math.Abs(surface.CornerRadius.TopLeft - surface.ActualHeight / 2) < .01, "Accepted capsule radius changed.");
                        // No mouse movement: the real scroll offset alone must
                        // dismiss a popup whose target is moving underneath it.
                        window.DetailPanel.ScrollToVerticalOffset(100); window.UpdateLayout(); Pump(20);
                        RequireFade(tip, $"{card.Name} scroll");
                        if (tip.IsOpen) RequireAnchor(tip, card, gap, "Scroll fade");
                        card.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, Environment.TickCount) { RoutedEvent = UIElement.MouseEnterEvent });
                        Pump(180); Require(!tip.IsOpen, $"{card.Name} tooltip remained open after stationary-pointer scrolling.");
                        Require(popup.PopupAnimation == nativeAnimation, "Original tooltip entry animation was not restored.");
                    }
                    window.DetailPanel.ScrollToTop(); window.UpdateLayout(); Pump(20);
                    tip.IsOpen = true; Pump(220);
                    card.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, Environment.TickCount) { RoutedEvent = UIElement.MouseLeaveEvent });
                    Pump(30); RequireFade(tip, "Mouse leave"); Pump(180);
                    Require(!tip.IsOpen, "Mouse leave did not close the range hint.");
                    tip.IsOpen = true; Pump(220);
                    tip.IsOpen = false; Pump(30); RequireFade(tip, "Native service close request");
                    tip.IsOpen = true; Pump(160); Require(tip.IsOpen && tip.Opacity >= .99, "Reopening lost to a previous close completion.");
                    VerifyNativeServiceReopen(tip, card);
                    var wheelGap = tip.PointToScreen(new Point()) - card.PointToScreen(new Point());
                    Require(SmoothScrollBehavior.TryHandle(card, new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, -120)), "Smooth scroll was not started.");
                    for (var sample = 0; sample < 4; sample++)
                    {
                        Pump(20);
                        if (tip.IsOpen)
                            RequireAnchor(tip, card, wheelGap, "Smooth wheel fade");
                    }
                    Pump(160); Require(!tip.IsOpen, "Animated wheel scrolling did not close the hint."); Pump(250);
                    window.DetailPanel.ScrollToTop(); window.UpdateLayout(); Pump(20);
                    tip.IsOpen = true; Pump(20); card.Visibility = Visibility.Collapsed; Pump(20);
                    Require(!tip.IsOpen, "Hidden target left an orphaned hint.");
                    tip.IsOpen = true; Pump(20); Require(!tip.IsOpen, "An already hidden target reopened an orphaned hint.");
                    card.Visibility = Visibility.Visible; window.UpdateLayout();
                }
                }
                Console.WriteLine("Metric tooltip motion PASS: light/dark download/upload, leave/service fade, scroll/wheel anchor within 1.5px, native service rapid/already-open/same-owner reopen, scroll re-hit, hidden target, original entry animation and capsule/font sizing.");
            }
            catch (Exception exception) { failure = exception; }
            finally { window?.PrepareForExit(); window?.Close(); app?.Shutdown(); }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        if (!thread.Join(TimeSpan.FromSeconds(35))) { Console.Error.WriteLine("Tooltip scroll regression timed out."); Environment.Exit(1); }
        if (failure is null) return 0; Console.Error.WriteLine(failure); return 1;
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void RequireFade(ToolTip tip, string action)
    {
        if (!SystemParameters.ClientAreaAnimation || !SystemParameters.ToolTipAnimation) return;
        Require(tip.IsOpen && tip.Opacity is > 0 and < 1, $"{action} hard-cut the hint instead of fading it.");
    }
    private static void RequireAnchor(ToolTip tip, FrameworkElement card, Vector gap, string action)
    {
        var delta = tip.PointToScreen(new Point()) - card.PointToScreen(new Point()) - gap;
        Require(Math.Abs(delta.X) <= 1.5 && Math.Abs(delta.Y) <= 1.5, $"{action} detached from its card: {delta} pixels.");
    }
    private static void VerifyNativeServiceReopen(ToolTip tip, FrameworkElement card)
    {
        // Run WPF's real service code without moving the user's mouse. This includes
        // its force-close cleanup and its already-open shortcut, unlike IsOpen=true.
        var type = typeof(ToolTip).Assembly.GetType("System.Windows.Controls.PopupControlService")!;
        var current = type.GetProperty("Current", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        var show = type.GetMethod("ShowToolTip", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var dismiss = type.GetMethod("DismissCurrentToolTip", BindingFlags.Instance | BindingFlags.NonPublic)!;
        show.Invoke(current, [card, false]); Pump(220);
        dismiss.Invoke(current, null); Pump(30);
        show.Invoke(current, [card, false]); Pump(200);
        Require(tip.IsOpen && tip.Opacity >= .99 && PresentationSource.FromVisual(tip) is not null,
            "WPF service rapid reopen left a closing or missing popup.");
        card.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, Environment.TickCount) { RoutedEvent = UIElement.MouseLeaveEvent });
        Pump(30);
        card.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, Environment.TickCount) { RoutedEvent = UIElement.MouseEnterEvent });
        var begin = type.GetMethod("BeginShowToolTip", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var triggerType = begin.GetParameters()[1].ParameterType;
        begin.Invoke(current, [card, Enum.Parse(triggerType, "Mouse")]); Pump(200);
        Require(tip.IsOpen && tip.Opacity >= .99, "Same-owner native service shortcut lost to the old leave fade.");
        card.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, Environment.TickCount) { RoutedEvent = UIElement.MouseLeaveEvent });
        Pump(30);
        type.GetField("_currentToolTip", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(current, null);
        show.Invoke(current, [card, false]); Pump(200);
        Require(tip.IsOpen && tip.Opacity >= .99 && PresentationSource.FromVisual(tip) is not null,
            "WPF already-open service shortcut lost to the old fade completion.");
        dismiss.Invoke(current, null); Pump(220);
        tip.IsOpen = true; Pump(220);
    }
    private static void Pump(int milliseconds)
    {
        var frame = new DispatcherFrame(); var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; }; timer.Start(); Dispatcher.PushFrame(frame);
    }
}
