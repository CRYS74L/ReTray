using System.Runtime.InteropServices;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Input;
using LanDeviceMonitor.Services;

namespace LanDeviceMonitor;

public partial class App : System.Windows.Application
{
    private MainWindow? _mainWindow;
    private NotificationService? _notificationService;
    private SingleInstanceService? _singleInstanceService;
    private WindowPlacementService? _windowPlacementService;
    private bool _hasExited;
    private bool _isShowingMainWindow;
    private bool _placementRestored;
    private bool _showWhenReady;

    internal static bool ShouldShowMainWindow(
        bool hasExited,
        bool hasWindow,
        bool isVisible) =>
        !hasExited && hasWindow && !isVisible;

    private void ComboBoxDropDown_PreviewMouseWheel(
        object sender,
        MouseWheelEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source &&
            SmoothScrollBehavior.TryHandleDropDown(source, e))
        {
            e.Handled = true;
        }
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            if (!ApplicationPaths.IsPortable && PackageActivation.RedirectIfInstalled(e.Args)) { Shutdown(0); return; }
            // A successor must not read/migrate data or claim single-instance
            // ownership until its validated predecessor has completely exited.
            await WaitForRestartParentAsync(e.Args);
            if (!ApplicationPaths.IsPortable) NativeRuntime.MigrateData();
        }
        catch (Exception exception)
        {
            System.Windows.MessageBox.Show(exception.Message, "ReTray");
            Shutdown(1);
            return;
        }
        var startHidden = e.Args.Contains(
            "--background",
            StringComparer.OrdinalIgnoreCase);

        _singleInstanceService = new SingleInstanceService();
        if (!_singleInstanceService.IsPrimary)
        {
            await SingleInstanceService.SignalPrimaryAsync();
            _singleInstanceService.Dispose();
            _singleInstanceService = null;
            Shutdown(0);
            return;
        }

        _singleInstanceService.StartListening(() =>
            Dispatcher.BeginInvoke(ShowMainWindow));


        DispatcherUnhandledException += App_DispatcherUnhandledException;
        try { ApplicationPaths.PrepareData(); }
        catch (Exception error) { System.Windows.MessageBox.Show(error.Message, "ReTray"); Shutdown(1); return; }
        ThemeService.Apply(ReadTrayThemeMode());
        try {
            using var saved = JsonDocument.Parse(File.ReadAllText(Path.Combine(
                ApplicationPaths.DataDirectory, "settings.json")));
            if (saved.RootElement.TryGetProperty("language", out var language) &&
                language.GetString() is "zh" or "en" or "zh-Hant" or "ja")
                ReTray_App.Services.Localization.CurrentLanguage = language.GetString()!;
        } catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { }


        // The original window retains its native frame and material lifecycle.
        // Notification registration, LAN monitoring, and the source NotifyIcon
        // are not part of the tray assistant's application startup.
        _notificationService = new NotificationService();
        _mainWindow = new MainWindow(
            _notificationService);
        MainWindow = _mainWindow;
        ReTray_App.App.Initialize(this, _mainWindow);
        _windowPlacementService = new WindowPlacementService(_mainWindow);
        _mainWindow.SourceInitialized += (_, _) =>
        {
            var sourceIcon = Path.Combine(ApplicationPaths.ResourceDirectory, "Assets", "WindowIcon.ico");
            var brandDirectory = Path.Combine(ApplicationPaths.DataDirectory, "Branding");
            Directory.CreateDirectory(brandDirectory);
            var taskbarIcon = Path.Combine(brandDirectory, "WindowIcon-" + typeof(App).Assembly.GetName().Version!.ToString(3) + ".ico");
            if (!File.Exists(taskbarIcon) || !File.ReadAllBytes(taskbarIcon).AsSpan().SequenceEqual(File.ReadAllBytes(sourceIcon)))
                File.Copy(sourceIcon, taskbarIcon, overwrite: true);
            var relaunch = TaskbarBranding.CreateRelaunchCommand(
                Environment.ProcessPath ?? throw new IOException("无法确定 ReTray 启动路径。"));
            TaskbarBranding.Apply(new WindowInteropHelper(_mainWindow).Handle, taskbarIcon, relaunch);
            // Shell activation can supply a shortcut icon to the new HWND.
            // Explicitly refresh WPF's owned HICONs after the first render too.
            _mainWindow.ContentRendered += (_, _) => _mainWindow.Icon =
                System.Windows.Media.Imaging.BitmapFrame.Create(new Uri(taskbarIcon));
            MicaWindowService.Apply(
                _mainWindow,
                ThemeService.ResolveIsDark(ThemeService.CurrentMode));
        };
        _mainWindow.ContentRendered += MainWindow_ContentRendered;
        _mainWindow.MinimizeToTrayRequested += (_, _) =>
        {
            _windowPlacementService.SaveNow();
            // Window_Closing is still unwinding its native close message.
            // Hide the HWND only after that message returns. Keeping WPF's
            // IsVisible state intact preserves its redirection surface for a
            // complete first frame when the same HWND is restored.
            _mainWindow.Dispatcher.BeginInvoke(
                System.Windows.Threading.DispatcherPriority.Normal,
                new Action(() =>
                {
                    if (!_hasExited && _mainWindow.IsVisible)
                    {
                        _mainWindow.HideToTrayPreservingVisual();
                    }
                }));
        };
        try
        {
            // Ensure the activation/message target also exists for --background.
            // The existing window keeps the same HWND when hidden to the tray.
            var handle = new WindowInteropHelper(_mainWindow).EnsureHandle();
            NativeRuntime.AttachInterface(handle);
            await NativeRuntime.StartAsync();
            _ = Task.Run(ApplicationPaths.PruneOldBundleCaches);
            if (_hasExited) return;
            if (!startHidden || _showWhenReady)
            {
                RestorePlacementOnce();
                _mainWindow.Show();
            }
        }
        catch (Exception exception)
        {
            if (_hasExited) return;
            try { await NativeRuntime.StopAsync(); } catch { }
            System.Windows.MessageBox.Show(exception.Message, "ReTray");
            ExitApplication();
        }

    }

    private static string ReadTrayThemeMode()
    {
        try
        {
            var settingsPath = Path.Combine(
                ApplicationPaths.DataDirectory, "settings.json");
            using var settings = JsonDocument.Parse(File.ReadAllText(settingsPath));
            return settings.RootElement.TryGetProperty("theme", out var theme)
                ? theme.GetString() switch
                {
                    "light" => ThemeService.LightMode,
                    "dark" => ThemeService.DarkMode,
                    _ => ThemeService.SystemMode
                }
                : ThemeService.SystemMode;
        }
        catch (Exception exception) when (exception is IOException or
            UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            return ThemeService.SystemMode;
        }
    }

    private void App_DispatcherUnhandledException(
        object sender,
        System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        try
        {
            var directory = ApplicationPaths.DataDirectory;
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "ui-crash.log"), e.Exception.ToString());
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }

        if (_mainWindow is not null)
        {
            _mainWindow.TrayPage.Vm.ReportError(e.Exception);
            e.Handled = true;
        }
    }

    private void MainWindow_ContentRendered(object? sender, EventArgs e)
    {
        if (_mainWindow is null)
        {
            return;
        }

        _mainWindow.ContentRendered -= MainWindow_ContentRendered;
        _mainWindow.Dispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.ContextIdle,
            new Action(() =>
            {
                if (_mainWindow is { IsVisible: true })
                {
                    MicaWindowService.Apply(
                        _mainWindow,
                        ThemeService.ResolveIsDark(ThemeService.CurrentMode));
                }
            }));
    }

    private void RestorePlacementOnce()
    {
        if (_placementRestored) return;
        _placementRestored = true;
        _windowPlacementService?.Restore();
    }

    internal void ShowMainWindow()
    {
        if (_isShowingMainWindow || _mainWindow is null || _hasExited)
        {
            return;
        }

        if (!NativeRuntime.IsReady)
        {
            _showWhenReady = true;
            return;
        }

        _isShowingMainWindow = true;
        try
        {
            if (ShouldShowMainWindow(_hasExited, true, _mainWindow.IsVisible))
            {
                RestorePlacementOnce();
                _mainWindow.Show();
            }

            _mainWindow.RestoreFromTray();
            _mainWindow.TrayPage.Resume();

            var wasTopmost = _mainWindow.Topmost;
            _mainWindow.Topmost = true;
            _mainWindow.Topmost = wasTopmost;
            _mainWindow.Activate();

            var handle = new WindowInteropHelper(_mainWindow).Handle;
            if (handle != IntPtr.Zero)
            {
                _ = ShowWindow(handle, 9);
                _ = SetForegroundWindow(handle);
            }
        }
        finally
        {
            _isShowingMainWindow = false;
        }
    }

    private void ExitApplication()
    {
        if (_hasExited)
        {
            return;
        }

        _hasExited = true;
        ReTray_App.App.PrepareForProcessExit();
        _windowPlacementService?.SaveNow();
        _mainWindow?.PrepareForExit();
        _notificationService?.Dispose();
        _notificationService = null;

        _singleInstanceService?.Dispose();
        _singleInstanceService = null;
        _windowPlacementService?.Dispose();
        _windowPlacementService = null;
        MicaWindowService.Dispose();

        Shutdown(0);
    }

    internal void CompleteInterfaceExit() => ExitApplication();

    protected override void OnExit(ExitEventArgs e)
    {
        if (!_hasExited)
        {
            _hasExited = true;
            ReTray_App.App.PrepareForProcessExit();
            // Handles direct WPF shutdown paths as well as normal QuitAsync.
            // StopAsync never captures the UI dispatcher while joining.
            try { NativeRuntime.StopAsync().GetAwaiter().GetResult(); } catch { }
            _windowPlacementService?.SaveNow();
            _mainWindow?.PrepareForExit();
            _notificationService?.Dispose();
            _notificationService = null;
            _singleInstanceService?.Dispose();
            _windowPlacementService?.Dispose();
            MicaWindowService.Dispose();
        }

        base.OnExit(e);
    }

    private static async Task WaitForRestartParentAsync(string[] arguments)
    {
        int index = Array.FindIndex(arguments, value =>
            string.Equals(value, "--restart-after", StringComparison.OrdinalIgnoreCase));
        if (index < 0) return;
        if (index + 1 >= arguments.Length ||
            !int.TryParse(arguments[index + 1], out int processId) ||
            processId <= 0 || processId == Environment.ProcessId)
            throw new IOException("ReTray 重启参数无效。");

        System.Diagnostics.Process predecessor;
        try { predecessor = System.Diagnostics.Process.GetProcessById(processId); }
        catch (ArgumentException) { return; } // Already exited before startup reached this point.
        using (predecessor)
        using (var current = System.Diagnostics.Process.GetCurrentProcess())
        {
            string? actual;
            try
            {
                if (predecessor.HasExited) return;
                actual = predecessor.MainModule?.FileName;
                if (predecessor.SessionId != current.SessionId || actual is null ||
                    !string.Equals(Path.GetFullPath(actual), Path.GetFullPath(Environment.ProcessPath!),
                        StringComparison.OrdinalIgnoreCase))
                    throw new IOException("ReTray 重启来源的会话或安装路径不匹配。");
            }
            catch (System.ComponentModel.Win32Exception) when (predecessor.HasExited) { return; }
            catch (InvalidOperationException) when (predecessor.HasExited) { return; }
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            try { await predecessor.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException exception)
            {
                throw new IOException("ReTray 原进程尚未退出，已取消本次重启。", exception);
            }
        }
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr windowHandle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr windowHandle, int command);

}
