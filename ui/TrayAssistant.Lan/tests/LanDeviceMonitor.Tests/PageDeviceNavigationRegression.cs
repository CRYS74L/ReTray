using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using LanDeviceMonitor;
using LanDeviceMonitor.Models;
using LanDeviceMonitor.Services;

internal static class PageDeviceNavigationRegression
{
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
                { WindowStartupLocation = WindowStartupLocation.Manual, Left = -10000, Top = -10000, Width = 1180, Height = 760, ShowActivated = false, ShowInTaskbar = false };
                typeof(MainWindow).GetField("_isPopulatingAppSettings", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, true);
                var now = DateTimeOffset.Now;
                var devices = Enumerable.Range(0, 3).Select(i => new DeviceViewData
                {
                    Profile = new DeviceProfile { MacAddress = $"02:00:00:00:65:{i:X2}", LastRouterName = $"设备 {(char)('A' + i)}", LastKnownOnline = true, LastSeenAt = now, FirstSeenAt = now },
                    Identity = new DeviceIdentity("手机", "\uE8EA", $"设备 {(char)('A' + i)}"), Sessions = []
                }).ToArray();
                Call(window, "ReplaceDevices", devices, false, false); window.Show(); window.UpdateLayout(); Pump(150);
                foreach (var mode in new[] { ThemeService.LightMode, ThemeService.DarkMode })
                foreach (var page in new[] { "ShowSettingsPanel", "ShowRouterPanel" })
                foreach (var targetIndex in new[] { 0, 1 })
                {
                    Call(window, "ApplyTheme", mode, false);
                    window.DeviceList.SelectedIndex = 0; Pump(1000);
                    Call(window, page); Pump(850);
                    var row = (ListBoxItem)window.DeviceList.ItemContainerGenerator.ContainerFromIndex(targetIndex)!;
                    var target = (LanDeviceMonitor.ViewModels.DeviceItemViewModel)row.DataContext;
                    row.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
                    { RoutedEvent = Mouse.PreviewMouseDownEvent });
                    // Finish the normal ListBox selection part of the same input.
                    window.DeviceList.SelectedItem = target;
                    window.UpdateLayout();
                    for (var frame = 0; frame < 45; frame++)
                    {
                        if (window.DetailPanel.IsVisible && window.DetailPanel.Opacity > .001 && window.SelectedDevice?.MacAddress != target.MacAddress)
                            throw new InvalidOperationException($"Stale device visible during page return: {mode}/{page}, frame={frame}, selected={window.SelectedDevice?.DisplayName}, expected={target.DisplayName}, opacity={window.DetailPanel.Opacity:F3}");
                        if (window.SelectedDevice?.MacAddress == target.MacAddress && window.DetailDeviceNameText.NameText != target.DisplayName)
                            throw new InvalidOperationException("Detail title binding still exposes the previous device.");
                        Pump(20);
                    }
                    if (window.SelectedDevice?.MacAddress != target.MacAddress || !window.DetailPanel.IsVisible || window.DetailPanel.Opacity < .99)
                        throw new InvalidOperationException("Page return did not finish on the requested device.");
                }
                // Interrupt a page entrance with another requested device.
                foreach (var delay in new[] { 20, 850 })
                {
                    window.DeviceList.SelectedIndex = 0; Pump(1000);
                    Call(window, "ShowSettingsPanel"); Pump(1000);
                    var rowB = (ListBoxItem)window.DeviceList.ItemContainerGenerator.ContainerFromIndex(1)!;
                    rowB.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
                    { RoutedEvent = Mouse.PreviewMouseDownEvent });
                    var pageEntrance = window.DetailPanel.RenderTransform;
                    window.DeviceList.SelectedIndex = 1; Pump(delay);
                    window.DeviceList.SelectedIndex = 2; Pump(1100);
                    GC.KeepAlive(pageEntrance);
                    if (window.SelectedDevice?.MacAddress != ((LanDeviceMonitor.ViewModels.DeviceItemViewModel)window.DeviceList.Items[2]).MacAddress)
                        throw new InvalidOperationException($"A second selection at {delay} ms did not finish on the latest device.");
                }
                Console.WriteLine("Page-to-device navigation PASS: settings/router to B, no visible A across 45 animation samples, light/dark, final content correct.");
            }
            catch (Exception exception) { failure = exception; }
            finally { window?.PrepareForExit(); window?.Close(); app?.Shutdown(); }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        if (!thread.Join(TimeSpan.FromSeconds(40))) { Console.Error.WriteLine("Page-device navigation regression timed out."); Environment.Exit(1); }
        if (failure is null) return 0; Console.Error.WriteLine(failure); return 1;
    }

    private static void Call(MainWindow window, string method, params object[] arguments) =>
        typeof(MainWindow).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, arguments);

    private static void Pump(int milliseconds)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; }; timer.Start(); Dispatcher.PushFrame(frame);
    }
}
