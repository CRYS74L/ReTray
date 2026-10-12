using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using LanDeviceMonitor;
using LanDeviceMonitor.Models;
using LanDeviceMonitor.Services;

internal static class ResponsiveSizingRegression
{
    internal static int Run()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            App? app = null;
            MainWindow? window = null;
            try
            {
                app = TestAppFactory.Create(); app.InitializeComponent();
                app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                window = new MainWindow(new NotificationService())
                { WindowStartupLocation = WindowStartupLocation.Manual, Left = -10000, Top = -10000, Width = 1180, Height = 760, ShowActivated = false, ShowInTaskbar = false };
                typeof(MainWindow).GetField("_isPopulatingAppSettings", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, true);
                var now = DateTimeOffset.Now;
                var devices = Enumerable.Range(1, 12).Select(i => new DeviceViewData
                {
                    Profile = new DeviceProfile { MacAddress = $"02:00:00:00:53:{i:X2}", LastRouterName = $"测试设备 {i}", LastKnownOnline = true, LastSeenAt = now, FirstSeenAt = now.AddHours(-4) },
                    Identity = new DeviceIdentity("手机", "\uE8EA", $"测试设备 {i}"),
                    Sessions = []
                }).ToArray();
                typeof(MainWindow).GetMethod("ReplaceDevices", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(window, [devices, false, false]);
                window.Show(); window.UpdateLayout();

                // Continuous native sizing keeps input pending. Deliberately
                // stop each dispatcher turn at Input: render/layout can run,
                // but Background must not be needed for responsive correctness.
                foreach (var mode in new[] { ThemeService.LightMode, ThemeService.DarkMode })
                {
                    typeof(MainWindow).GetMethod("ApplyTheme", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(window, [mode, false]);
                    var widths = new[] { 1180d, 920, 902, 898, 860, 760, 600, 430, 380, 600, 760, 898, 902, 960, 1180, 1340 };
                    foreach (var width in widths)
                    {
                        var clock = Stopwatch.StartNew();
                        window.Width = width; window.UpdateLayout(); PumpRenderWork(); window.UpdateLayout();
                        clock.Stop();
                        var compact = window.WorkspaceGrid.ActualWidth < 900;
                        if (!window.DeviceSidebar.IsVisible || window.DetailSurface.IsVisible == compact)
                            throw new InvalidOperationException($"Responsive mode is stale while input remains pending: width={width}, workspace={window.WorkspaceGrid.ActualWidth}, list={window.DeviceSidebar.IsVisible}, detail={window.DetailSurface.IsVisible}, content={window.SidebarExpandedContent.ActualWidth}");
                        if (compact && Math.Abs(window.SidebarExpandedContent.ActualWidth - (window.WorkspaceGrid.ActualWidth - 54)) > 1)
                            throw new InvalidOperationException($"Compact list kept an earlier width: window={width}, content={window.SidebarExpandedContent.ActualWidth}");
                        if (!compact && window.SidebarColumn.ActualWidth > window.SidebarColumn.MaxWidth + .01)
                            throw new InvalidOperationException("Responsive switch reintroduced the DPI width conflict.");
                        if (!compact && window.DetailSurface.ActualWidth < 570)
                            throw new InvalidOperationException($"Wide detail is squeezed: width={width}, detail={window.DetailSurface.ActualWidth}");
                        if (clock.Elapsed.TotalMilliseconds > 300)
                            throw new InvalidOperationException($"Live responsive step did not converge: {width}, {clock.Elapsed.TotalMilliseconds:F2} ms");
                        if (width is 600 or 1340) Capture(window, $"{mode}-{width}.png");
                    }
                    // Several size events can arrive before the render turn.
                    // The final width, rather than an earlier queued width, wins.
                    window.Width = 898; window.UpdateLayout();
                    window.Width = 1180; window.UpdateLayout();
                    PumpRenderWork(); window.UpdateLayout();
                    if (!window.DetailSurface.IsVisible) throw new InvalidOperationException("A superseded narrow resize hid the wide detail.");
                }
                Console.WriteLine("Responsive sizing PASS: light/dark 380–1340 DIP, both directions, no Background dispatch, full single-column list, timely wide restoration and latest-width coalescing.");
            }
            catch (Exception exception) { failure = exception; }
            finally { window?.PrepareForExit(); window?.Close(); app?.Shutdown(); }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        if (!thread.Join(TimeSpan.FromSeconds(25))) { Console.Error.WriteLine("Responsive sizing regression timed out."); Environment.Exit(1); }
        if (failure is null) return 0;
        Console.Error.WriteLine(failure); return 1;
    }

    private static void PumpRenderWork()
    {
        var dispatcher = Dispatcher.CurrentDispatcher;
        var backgroundRan = false;
        var background = dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => backgroundRan = true));
        var frame = new DispatcherFrame();
        dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
        background.Abort();
        if (backgroundRan) throw new InvalidOperationException("The live-sizing regression accidentally ran Background work.");
    }

    private static void Capture(MainWindow window, string name)
    {
        var output = System.IO.Path.Combine(Environment.CurrentDirectory, "runtime-data", "responsive-layout-dev253");
        System.IO.Directory.CreateDirectory(output);
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth), (int)Math.Ceiling(window.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = System.IO.File.Create(System.IO.Path.Combine(output, name)); encoder.Save(stream);
    }
}
