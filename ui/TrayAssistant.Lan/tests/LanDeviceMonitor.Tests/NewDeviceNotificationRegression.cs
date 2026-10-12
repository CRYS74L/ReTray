using System.Text.Json;
using System.Text.Json.Nodes;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using LanDeviceMonitor;
using LanDeviceMonitor.Models;
using LanDeviceMonitor.Services;

internal static class NewDeviceNotificationRegression
{
    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }

    internal static bool IsNew(DeviceTransition transition) => transition.IsNewDevice;

    private static void SetEnabled(DeviceRepository repository, bool enabled) =>
        repository.Settings.NotifyWhenNewDeviceOnline = enabled;

    internal static async Task<int> RunAsync()
    {
        try
        {
            var directory = System.IO.Path.Combine(Environment.CurrentDirectory, ".tools", "test-data", $"new-device-{Guid.NewGuid():N}");
            System.IO.Directory.CreateDirectory(directory);
            var path = System.IO.Path.Combine(directory, "state.json");
            var state = new PersistedState();
            state.Sessions.Add(new DeviceSession { MacAddress = "AA:00:00:00:59:02", OnlineAt = DateTimeOffset.Now.AddDays(-2), OfflineAt = DateTimeOffset.Now.AddDays(-1) });
            var json = JsonNode.Parse(JsonSerializer.Serialize(state, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }))!;
            json["settings"]!["notifyWhenNewDeviceOnline"] = true;
            await System.IO.File.WriteAllTextAsync(path, json.ToJsonString());
            var repository = new DeviceRepository(path); await repository.LoadAsync();
            var now = new DateTimeOffset(2026, 10, 5, 12, 0, 0, TimeSpan.FromHours(8));
            DeviceSnapshot Snapshot(string mac, bool online) => new(mac, "新设备测试", "", online, "10.77.0.59", 1, 0, 0, 0, 0, 0);
            var first = await repository.ApplyPollAsync([Snapshot("AA:00:00:00:59:01", true)], now);
            Require(first.Transitions.Count == 1 && first.Transitions[0].ShouldNotify && first.Transitions[0].IsOnline,
                "Previously unrecorded online device did not produce exactly one eligible notification.");
            Require(IsNew(first.Transitions[0]), "First-device notification was not marked separately from a reconnect.");
            var named = await repository.ApplyPollAsync(
                [Snapshot("AA:00:00:00:59:05", true) with { RouterName = "书房路由器", OriginalName = "miwifi" }], now);
            Require(named.Transitions.Single(IsNew).DisplayName == "书房路由器", "New-device notification used a generic type name instead of the recorded device name.");
            var second = await repository.ApplyPollAsync([Snapshot("aa:00:00:00:59:01", true)], now.AddSeconds(1));
            Require(!second.Transitions.Any(IsNew), "Repeated/case-varied MAC produced another new-device notification.");
            await repository.ApplyPollAsync([Snapshot("AA:00:00:00:59:01", false)], now.AddSeconds(2));
            await repository.ApplyPollAsync([Snapshot("AA:00:00:00:59:01", false)], now.AddSeconds(6));
            await repository.RemoveDeviceUntilNextOnlineAsync("AA:00:00:00:59:01");
            var returning = await repository.ApplyPollAsync([Snapshot("AA:00:00:00:59:01", true)], now.AddSeconds(7));
            Require(!returning.Transitions.Any(IsNew), "Removed historical device was mistaken for a new one.");
            var historyOnly = await repository.ApplyPollAsync([Snapshot("AA:00:00:00:59:02", true)], now.AddSeconds(8));
            Require(!historyOnly.Transitions.Any(IsNew), "A MAC present in session history was mistaken for a new device.");
            await repository.ApplyPollAsync([Snapshot("AA:00:00:00:59:03", false)], now.AddSeconds(9));
            var formerlyOffline = await repository.ApplyPollAsync([Snapshot("AA:00:00:00:59:03", true)], now.AddSeconds(10));
            Require(!formerlyOffline.Transitions.Any(IsNew), "A previously recorded offline device was mistaken for a new one.");
            SetEnabled(repository, false);
            var disabled = await repository.ApplyPollAsync([Snapshot("AA:00:00:00:59:04", true)], now.AddSeconds(11));
            Require(!disabled.Transitions.Any(IsNew), "Disabled setting emitted a new-device notification.");
            SetEnabled(repository, true);
            var afterEnabled = await repository.ApplyPollAsync([Snapshot("AA:00:00:00:59:04", true)], now.AddSeconds(12));
            Require(!afterEnabled.Transitions.Any(IsNew), "Enabling the setting retroactively notified an already recorded device.");
            await repository.UpdateNewDeviceNotificationSettingAsync(true);
            var reloaded = new DeviceRepository(path); await reloaded.LoadAsync();
            Require(reloaded.Settings.NotifyWhenNewDeviceOnline, "Enabled preference did not survive saving and reload.");
            var afterRestart = await reloaded.ApplyPollAsync([Snapshot("AA:00:00:00:59:01", true)], now.AddSeconds(13));
            Require(!afterRestart.Transitions.Any(IsNew), "Restart lost the historical identity and repeated a new-device notification.");
            await reloaded.UpdateNewDeviceNotificationSettingAsync(false);
            var disabledReload = new DeviceRepository(path); await disabledReload.LoadAsync();
            Require(!disabledReload.Settings.NotifyWhenNewDeviceOnline, "Disabled preference did not survive saving and reload.");
            Require(!new AppSettings().NotifyWhenNewDeviceOnline, "Existing installations should not enable a new notification without choosing it.");
            var oldPath = System.IO.Path.Combine(directory, "old-state.json");
            json["settings"]!.AsObject().Remove("notifyWhenNewDeviceOnline");
            await System.IO.File.WriteAllTextAsync(oldPath, json.ToJsonString());
            var oldRepository = new DeviceRepository(oldPath); await oldRepository.LoadAsync();
            Require(!oldRepository.Settings.NotifyWhenNewDeviceOnline, "Old state migration enabled the new setting.");
            var beforeLoad = new DeviceRepository(oldPath);
            var earlyToggle = beforeLoad.UpdateNewDeviceNotificationSettingAsync(true);
            await beforeLoad.LoadAsync(); await earlyToggle;
            Require(beforeLoad.Settings.NotifyWhenNewDeviceOnline, "An initial state load overwrote the user's latest toggle.");
            using var superseded = new CancellationTokenSource();
            var olderEnable = beforeLoad.UpdateNewDeviceNotificationSettingAsync(true, superseded.Token);
            var latestDisable = beforeLoad.UpdateNewDeviceNotificationSettingAsync(false);
            superseded.Cancel(); await latestDisable;
            try { await olderEnable; throw new InvalidOperationException("Superseded preference save was not cancelled."); }
            catch (OperationCanceledException) { }
            var latestReload = new DeviceRepository(oldPath); await latestReload.LoadAsync();
            Require(!latestReload.Settings.NotifyWhenNewDeviceOnline, "A superseded enable overwrote the latest disabled preference.");
            var corruptPath = System.IO.Path.Combine(directory, "corrupt-state.json");
            await System.IO.File.WriteAllTextAsync(corruptPath, "{invalid-json");
            var recovering = new DeviceRepository(corruptPath);
            var recoveryToggle = recovering.UpdateNewDeviceNotificationSettingAsync(true);
            await recovering.LoadAsync(); await recoveryToggle;
            Require(recovering.Settings.NotifyWhenNewDeviceOnline, "JSON recovery discarded the user's latest toggle.");
            Console.WriteLine("New-device detection PASS: first-online only, case-insensitive MAC, hidden/history/offline records excluded, disabled and later enable do not retroactively notify, restart preserves identity.");
            VerifyDispatchAndSettings(first.Transitions[0], now, directory);
            return 0;
        }
        catch (Exception exception) { Console.Error.WriteLine(exception); return 1; }
    }

    private static void VerifyDispatchAndSettings(DeviceTransition first, DateTimeOffset now, string directory)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            App? app = null; MainWindow? window = null;
            var platform = new TestAppNotificationPlatform();
            using var service = new NotificationService(platform);
            try
            {
                app = TestAppFactory.Create(); app.InitializeComponent();
                app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                var repository = new DeviceRepository(System.IO.Path.Combine(directory, "dispatch-state.json"));
                repository.Settings.NotifyWhenNewDeviceOnline = true;
                service.Register();
                window = new MainWindow(service)
                { WindowStartupLocation = WindowStartupLocation.Manual, Left = -10000, Top = -10000, Width = 1180, Height = 900, ShowActivated = false, ShowInTaskbar = false };
                typeof(MainWindow).GetField("_repository", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(window, repository);
                typeof(MainWindow).GetField("_isPopulatingAppSettings", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(window, true);
                var update = new MonitorUpdate([], [first], now);
                Invoke(window, "QueueNotifications", update);
                Invoke(window, "QueueNotifications", update);
                Pump(30);
                Require(platform.Delivered.Count == 1 && platform.Delivered[0].Title.StartsWith("新设备上线：", StringComparison.Ordinal) &&
                    platform.Delivered[0].Title.Contains(first.DisplayName) && !platform.Delivered[0].Message.Contains(first.MacAddress) &&
                    !platform.Delivered[0].Message.Contains("MAC", StringComparison.OrdinalIgnoreCase),
                    "New-device queue did not submit one correctly identified Windows notification.");
                Require(platform.SingleLineText[0] && !platform.Delivered[0].Message.Contains('\n'),
                    "New-device notification was not limited to one title and one time line.");
                platform.Delivered.Clear();
                repository.Settings.DoNotDisturbEnabled = true;
                var muted = update with { RefreshedAt = now.Date.AddHours(23) };
                Invoke(window, "DeliverNotifications", muted);
                Require(platform.Delivered.Count == 0, "Do-not-disturb did not suppress a new-device notification.");
                repository.Settings.DoNotDisturbEnabled = false;
                repository.Settings.NotifyWhenNewDeviceOnline = false;
                Invoke(window, "DeliverNotifications", update);
                Require(platform.Delivered.Count == 0, "Disabling the switch did not suppress an already queued new-device notification.");
                repository.Settings.NotifyWhenNewDeviceOnline = true;
                var reconnect = new DeviceTransition(first.MacAddress.ToLowerInvariant(), first.DisplayName, false, now.AddSeconds(1), true);
                var merged = (MonitorUpdate)typeof(MainWindow).GetMethod("MergeNotificationUpdates", BindingFlags.NonPublic | BindingFlags.Static)!
                    .Invoke(null, [update, new MonitorUpdate([], [reconnect], now.AddSeconds(1))])!;
                Require(merged.Transitions.Count == 2 && merged.Transitions.Any(IsNew), "An ordinary state event erased a pending first-device notification.");
                Invoke(window, "DeliverNotifications", merged);
                Require(platform.Delivered.Count == 2 && platform.Delivered.Count(item => item.Title.StartsWith("新设备上线：", StringComparison.Ordinal)) == 1,
                    "New and ordinary state notices were duplicated or suppressed.");
                window.Show(); Pump(100);
                window.NotifyNewDeviceOnlineCheck.IsChecked = true;
                // Real Checked/Unchecked must take effect before the delayed
                // settings save, even while a separate DND field is invalid.
                window.DoNotDisturbStartBox.Text = "invalid";
                typeof(MainWindow).GetField("_isPopulatingAppSettings", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(window, false);
                platform.Delivered.Clear();
                window.NotifyNewDeviceOnlineCheck.IsChecked = false;
                Invoke(window, "DeliverNotifications", update);
                Require(platform.Delivered.Count == 0 && !repository.Settings.NotifyWhenNewDeviceOnline,
                    "Real Unchecked did not immediately suppress the queued notice while DND input was invalid.");
                Pump(140);
                var offAfterReload = new DeviceRepository(System.IO.Path.Combine(directory, "dispatch-state.json"));
                Task.Run(() => offAfterReload.LoadAsync()).GetAwaiter().GetResult();
                Require(!offAfterReload.Settings.NotifyWhenNewDeviceOnline, "Invalid DND input prevented saving the new toggle.");
                window.NotifyNewDeviceOnlineCheck.IsChecked = true;
                var immediatelyOnline = Task.Run(() => repository.ApplyPollAsync(
                    [new DeviceSnapshot("AA:00:00:00:59:06", "即时开关测试", "", true, "10.77.0.60", 1, 0, 0, 0, 0, 0)], now)).GetAwaiter().GetResult();
                Require(immediatelyOnline.Transitions.Any(IsNew), "Real Checked did not enable discovery before the persistence delay.");
                typeof(MainWindow).GetField("_isPopulatingAppSettings", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(window, true);
                foreach (var mode in new[] { ThemeService.LightMode, ThemeService.DarkMode })
                {
                    Invoke(window, "ApplyTheme", mode, false);
                    foreach (var width in new[] { 1180d, 430d })
                    {
                        window.Width = width; window.UpdateLayout(); Pump(100);
                        Invoke(window, "ShowSettingsPanel"); Pump(800);
                        var dnd = window.DoNotDisturbCheck.TranslatePoint(new Point(), window.SettingsPanel).Y;
                        var added = window.NotifyNewDeviceOnlineCheck.TranslatePoint(new Point(), window.SettingsPanel).Y;
                        var ipv6 = window.NotifyIpv6NonNativeCheck.TranslatePoint(new Point(), window.SettingsPanel).Y;
                        Require(window.NotifyNewDeviceOnlineCheck.IsVisible && dnd < added && added < ipv6,
                            "The new toggle was not visible between DND and IPv6 notification controls.");
                        window.SettingsPanel.ScrollToVerticalOffset(220); Pump(100);
                        Capture(window, mode, width);
                    }
                }
                Console.WriteLine("New-device dispatch/settings PASS: one channel submission, DND and late disable honored, mixed events preserved, setting round-trip and light/dark 1180/430 placement.");
            }
            catch (Exception exception) { failure = exception; }
            finally { window?.PrepareForExit(); window?.Close(); app?.Shutdown(); }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        if (!thread.Join(TimeSpan.FromSeconds(25))) { Console.Error.WriteLine("New-device UI checks timed out."); Environment.Exit(1); }
        if (failure is not null) throw failure;
    }

    private static void Invoke(MainWindow window, string method, params object[] args) =>
        typeof(MainWindow).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(window, args);

    private static void Pump(int milliseconds)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start(); Dispatcher.PushFrame(frame);
    }

    private static void Capture(MainWindow window, string mode, double width)
    {
        var bounds = new Rect(0, 0, window.ActualWidth, window.ActualHeight);
        var drawing = new DrawingVisual();
        using (var dc = drawing.RenderOpen())
        {
            dc.DrawRectangle(mode == ThemeService.DarkMode ? new SolidColorBrush(Color.FromRgb(30, 30, 30)) : System.Windows.Media.Brushes.White, null, bounds);
            dc.DrawRectangle(new VisualBrush(window) { ViewboxUnits = BrushMappingMode.Absolute, Viewbox = bounds }, null, bounds);
        }
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(bounds.Width), (int)Math.Ceiling(bounds.Height), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(drawing);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        var folder = System.IO.Path.Combine(Environment.CurrentDirectory, "runtime-data", "new-device-dev259");
        System.IO.Directory.CreateDirectory(folder);
        using var output = System.IO.File.Create(System.IO.Path.Combine(folder, $"settings-{mode}-{width}.png")); encoder.Save(output);
    }
}
