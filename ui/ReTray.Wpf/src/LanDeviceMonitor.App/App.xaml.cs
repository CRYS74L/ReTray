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
        // The native agent owns the one-time, lossless migration, including
        // rebasing managed custom-icon paths. Run it before reading the theme.
        using (var migration = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = Path.Combine(AppContext.BaseDirectory, "ReTray.Agent.exe"),
            Arguments = "--migrate-data",
            UseShellExecute = false,
            CreateNoWindow = true
        }))
        {
            if (migration is null || !migration.WaitForExit(15000) || migration.ExitCode != 0)
            {
                System.Windows.MessageBox.Show("ReTray 配置迁移未完成。原配置已保留，请检查数据目录和访问权限。", "ReTray");
                Shutdown(1);
                return;
            }
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
            Environment.Exit(0);
            return;
        }

        _singleInstanceService.StartListening(() =>
            Dispatcher.BeginInvoke(ShowMainWindow));


        DispatcherUnhandledException += App_DispatcherUnhandledException;
        ThemeService.Apply(ReadTrayThemeMode());

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
            var sourceIcon = Path.Combine(AppContext.BaseDirectory, "Assets", "WindowIcon.ico");
            var brandDirectory = Path.Combine(Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData), "ReTray", "Branding");
            Directory.CreateDirectory(brandDirectory);
            var taskbarIcon = Path.Combine(brandDirectory, "WindowIcon.ico");
            if (!File.Exists(taskbarIcon) || !File.ReadAllBytes(taskbarIcon).AsSpan().SequenceEqual(File.ReadAllBytes(sourceIcon)))
                File.Copy(sourceIcon, taskbarIcon, overwrite: true);
            var activation = ReTray_App.App.UiActivationId;
            var relaunch = string.IsNullOrEmpty(activation)
                ? "\"" + Environment.ProcessPath + "\""
                : "\"" + Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe") +
                  "\" \"shell:AppsFolder\\" + activation + "\"";
            TaskbarBranding.Apply(new WindowInteropHelper(_mainWindow).Handle, taskbarIcon, relaunch);
            _windowPlacementService.Restore();
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
        if (!startHidden)
        {
            _mainWindow.Show();
        }

    }

    private static string ReadTrayThemeMode()
    {
        try
        {
            var settingsPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ReTray", "settings.json");
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
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ReTray");
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

    private void ShowMainWindow()
    {
        if (_isShowingMainWindow || _mainWindow is null || _hasExited)
        {
            return;
        }

        _isShowingMainWindow = true;
        try
        {
            if (ShouldShowMainWindow(_hasExited, true, _mainWindow.IsVisible))
            {
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
        ArmForcedExitFallback();
        _windowPlacementService?.SaveNow();
        _mainWindow?.PrepareForExit();
        _notificationService?.Dispose();
        _notificationService = null;

        _singleInstanceService?.Dispose();
        _singleInstanceService = null;
        _windowPlacementService?.Dispose();
        _windowPlacementService = null;
        MicaWindowService.Dispose();

        // 不再先进入可能等待窗口和 COM 组件的 WPF Shutdown 流程。
        Environment.Exit(0);
    }

    internal void CompleteInterfaceExit() => ExitApplication();

    protected override void OnExit(ExitEventArgs e)
    {
        if (!_hasExited)
        {
            _hasExited = true;
            ReTray_App.App.PrepareForProcessExit();
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

    private static void ArmForcedExitFallback()
    {
        var watchdog = new Thread(() =>
        {
            Thread.Sleep(900);
            _ = TerminateProcess(GetCurrentProcess(), 0);
        })
        {
            IsBackground = true,
            Name = "ReTray.ExitWatchdog"
        };
        watchdog.Start();
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr windowHandle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr windowHandle, int command);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TerminateProcess(IntPtr processHandle, uint exitCode);
}
