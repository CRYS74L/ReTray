using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using LanDeviceMonitor;
using LanDeviceMonitor.Models;
using LanDeviceMonitor.Services;

internal static class StartupPortraitRegression
{
    private static readonly BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    internal static int Run()
    {
        Exception? error = null;
        var failures = new List<string>();
        var thread = new Thread(() =>
        {
            App? app = null;
            try
            {
                SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
                app = TestAppFactory.Create(); app.InitializeComponent();
                app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                foreach (var theme in new[] { ThemeService.LightMode, ThemeService.DarkMode })
                {
                    ThemeService.Apply(theme);
                    foreach (var restored in new[] { false, true }) VerifyFirstLayout(restored, theme, failures);
                }
                ThemeService.Apply(ThemeService.LightMode);
                VerifyAutomaticConnection(failures);
            }
            catch (Exception exception) { error = exception.GetBaseException(); }
            finally { app?.Shutdown(); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        if (!thread.Join(TimeSpan.FromSeconds(35))) { Console.Error.WriteLine("Startup portrait regression timed out."); return 1; }
        if (error is not null) { Console.Error.WriteLine(error); return 1; }
        if (failures.Count != 0) { Console.Error.WriteLine("Startup portrait failures: " + string.Join("; ", failures)); return 1; }
        Console.WriteLine("Startup portrait PASS: first layout, native restored size, cached data/automatic connection, and intentional detail entry.");
        return 0;
    }

    private static MainWindow CreateWindow(double width = 494, double height = 842)
    {
        var window = new MainWindow(new NotificationService())
        {
            WindowStartupLocation = WindowStartupLocation.Manual, Left = -10000, Top = -10000,
            Width = width, Height = height, ShowActivated = false, ShowInTaskbar = false
        };
        typeof(MainWindow).GetField("_isPopulatingAppSettings", PrivateInstance)!.SetValue(window, true);
        return window;
    }

    private static void Check(bool condition, string name, string observed, List<string> failures)
    {
        Console.WriteLine($"Startup {name}: {(condition ? "PASS" : "FAIL")} ({observed})");
        if (!condition) failures.Add(name);
    }

    private static void VerifyFirstLayout(bool restored, string theme, List<string> failures)
    {
        var window = CreateWindow(restored ? 1180 : 494, restored ? 760 : 842);
        try
        {
            var sizeChanges = 0;
            window.WorkspaceGrid.SizeChanged += (_, e) =>
            {
                if (++sizeChanges > 5) return;
                Console.WriteLine($"Startup SizeChanged #{sizeChanges} {theme}/restored={restored}: {e.PreviousSize} -> {e.NewSize}, loaded={window.IsLoaded}, compact={typeof(MainWindow).GetField("_compactMode", PrivateInstance)!.GetValue(window)}, detailVisibility={window.DetailSurface.Visibility}.");
            };
            if (restored)
                window.SourceInitialized += (_, _) =>
                {
                    var handle = new WindowInteropHelper(window).Handle;
                    var scale = GetDpiForWindow(handle) / 96d;
                    if (!SetWindowPos(handle, IntPtr.Zero, 0, 0, (int)Math.Round(494*scale), (int)Math.Round(842*scale), 0x16))
                        throw new InvalidOperationException("Native restored-size fixture failed.");
                };
            window.Show(); window.UpdateLayout();
            Check(window.WorkspaceGrid.ActualWidth < 900, $"portrait viewport {theme}/restored={restored}",
                $"actualWidth={window.WorkspaceGrid.ActualWidth:F2}", failures);
            // Observe the first completed layout before allowing deferred render
            // work or loading data to disguise the initial two-column flash.
            Check(window.DeviceSidebar.Visibility == Visibility.Visible && window.DetailSurface.Visibility == Visibility.Collapsed &&
                Math.Abs(window.SidebarColumn.ActualWidth-window.WorkspaceGrid.ActualWidth) < 1 &&
                window.SidebarToggleButton.Visibility == Visibility.Collapsed && !window.CaptionBackButton.IsNavigationAvailable,
                $"first portrait layout {theme}/restored={restored}",
                $"list={window.DeviceSidebar.Visibility}, detail={window.DetailSurface.Visibility}, sidebarWidth={window.SidebarColumn.ActualWidth:F2}, viewport={window.WorkspaceGrid.ActualWidth:F2}", failures);
            Pump(150);
            Check(!window.DetailSurface.IsVisible && !window.EmptyPanel.IsVisible,
                $"no squeezed placeholder {theme}/restored={restored}", "settled portrait list", failures);
        }
        finally { window.PrepareForExit(); window.Close(); }
    }

    private static void VerifyAutomaticConnection(List<string> failures)
    {
        var output = System.IO.Path.Combine(Environment.CurrentDirectory, "runtime-data", "startup-regression");
        System.IO.Directory.CreateDirectory(output);
        var identifier = Guid.NewGuid().ToString("N");
        var statePath = System.IO.Path.Combine(output, identifier + ".json");
        var address = $"http://startup-{identifier}.invalid/";
        var credential = new RouterCredentialService();
        if (credential.HasPassword(address)) throw new InvalidOperationException("Synthetic credential target unexpectedly exists.");
        var window = CreateWindow();
        var fixture = new StartupRouterHandler(new Uri(address).Host);
        try
        {
            // The unique fixture credential and HTTP transport never access the
            // user's router password, network, state file or web listener.
            credential.SavePassword(address, "synthetic-fixture");
            var repository = new DeviceRepository(statePath);
            repository.Settings.RouterAddress = address;
            repository.Settings.ThemeMode = ThemeService.LightMode;
            repository.Settings.RefreshIntervalSeconds = 60;
            var cached = new PersistedState { Settings = repository.Settings };
            cached.Devices["02:00:00:00:95:01"] = new DeviceProfile
            {
                MacAddress = "02:00:00:00:95:01", LastRouterName = "Startup fixture",
                LastKnownOnline = true, FirstSeenAt = DateTimeOffset.Now.AddHours(-1), LastSeenAt = DateTimeOffset.Now
            };
            System.IO.File.WriteAllText(statePath, JsonSerializer.Serialize(cached,
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
            ((MonitoringService)typeof(MainWindow).GetField("_monitoringService", PrivateInstance)!.GetValue(window)!).Dispose();
            var service = new MonitoringService(new XiaomiRouterClient(new HttpClient(fixture)), repository);
            typeof(MainWindow).GetField("_repository", PrivateInstance)!.SetValue(window, repository);
            typeof(MainWindow).GetField("_monitoringService", PrivateInstance)!.SetValue(window, service);
            service.Updated += (sender, update) => Call(window, "MonitoringService_Updated", sender!, update);
            service.ConnectionStateChanged += (sender, state) => Call(window, "MonitoringService_ConnectionStateChanged", sender!, state);
            window.Show(); window.UpdateLayout(); Pump(150);
            var initialization = window.InitializeAsync();
            PumpUntil(fixture.LoginStarted.Task);
            Check(window.DeviceSidebar.IsVisible && !window.DetailSurface.IsVisible && window.DeviceList.Items.Count == 1,
                "cached devices wait for authentication on list", "actual InitializeAsync pending with cached device", failures);
            fixture.AllowLogin.TrySetResult();
            PumpUntil(initialization); Pump(700); window.UpdateLayout();
            Check(fixture.DeviceRequests > 0 && window.DeviceList.Items.Count == 1 && window.SelectedDevice is not null,
                "monitoring fixture updates cached device", $"pollRequests={fixture.DeviceRequests}, rows={window.DeviceList.Items.Count}", failures);
            Check(window.DeviceSidebar.IsVisible && !window.DetailSurface.IsVisible && !window.CaptionBackButton.IsNavigationAvailable,
                "automatic connection does not enter detail", $"list={window.DeviceSidebar.IsVisible}, detail={window.DetailSurface.IsVisible}, back={window.CaptionBackButton.IsNavigationAvailable}", failures);
            var key = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window)!, Environment.TickCount, Key.Enter)
                { RoutedEvent = Keyboard.KeyDownEvent };
            window.DeviceList.RaiseEvent(key); Pump(700);
            Check(window.DetailSurface.IsVisible && window.CaptionBackButton.IsNavigationAvailable,
                "intentional Enter still opens detail", "routed list Enter", failures);
            window.CaptionBackButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump(700);
            Check(window.DeviceSidebar.IsVisible && !window.DetailSurface.IsVisible,
                "intentional return still opens list", "routed caption click", failures);
        }
        finally
        {
            fixture.AllowLogin.TrySetResult();
            window.PrepareForExit(); window.Close();
            fixture.Dispose();
            if (!CredDelete("LanDeviceMonitor.Router."+address.ToLowerInvariant(), 1, 0) && Marshal.GetLastWin32Error() != 1168)
                throw new InvalidOperationException("Synthetic credential cleanup failed.");
            if (credential.HasPassword(address)) throw new InvalidOperationException("Synthetic startup credential remains.");
            foreach (var path in new[] { statePath, statePath+".tmp" }) if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
        }
    }

    private sealed class StartupRouterHandler(string host) : HttpMessageHandler
    {
        internal readonly TaskCompletionSource LoginStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource AllowLogin = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int DeviceRequests;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.Host != host) throw new InvalidOperationException("Unexpected non-fixture router address.");
            var path = request.RequestUri.AbsolutePath;
            string body;
            if (path.EndsWith("/web")) body = "key: 'synthetic-key'; var deviceId = '02:00:00:00:95:FF';";
            else if (path.EndsWith("/login"))
            {
                LoginStarted.TrySetResult(); await AllowLogin.Task.WaitAsync(cancellationToken);
                body = """{"code":0,"token":"synthetic-token"}""";
            }
            else if (path.EndsWith("/devicelist"))
            {
                Interlocked.Increment(ref DeviceRequests);
                body = """{"code":0,"list":[{"mac":"02:00:00:00:95:01","name":"Startup fixture","online":1,"type":1,"ip":[{"ip":"192.0.2.10"}],"statistics":{"online":120,"downspeed":0,"upspeed":0}}]}""";
            }
            else if (path.EndsWith("/topo_graph")) body = """{"code":0,"graph":{"ip":"192.0.2.1","mac":"02:00:00:00:95:FF"}}""";
            else if (path.EndsWith("/wan_info")) body = """{"code":0,"info":{}}""";
            else body = """{"code":0,"newEncryptMode":0}""";
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) };
        }
    }

    private static void Call(MainWindow window, string method, params object[] arguments) =>
        typeof(MainWindow).GetMethod(method, PrivateInstance)!.Invoke(window, arguments);
    private static void PumpUntil(Task task)
    {
        var clock = Stopwatch.StartNew();
        while (!task.IsCompleted && clock.Elapsed < TimeSpan.FromSeconds(8)) Pump(20);
        if (!task.IsCompleted) throw new TimeoutException("Synthetic startup did not complete.");
        task.GetAwaiter().GetResult();
    }
    private static void Pump(int milliseconds)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; }; timer.Start(); Dispatcher.PushFrame(frame);
    }
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredDelete(string target, int type, int flags);
}
