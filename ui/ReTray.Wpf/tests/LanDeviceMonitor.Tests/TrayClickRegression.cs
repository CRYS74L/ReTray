using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using LanDeviceMonitor;
using LanDeviceMonitor.Services;
using NotifyIcon = System.Windows.Forms.NotifyIcon;
using FormsMessage = System.Windows.Forms.Message;

internal static class TrayClickRegression
{
    private const int TrayCallbackMessage = 0x0400 + 1024;
    private const int LeftDown = 0x0201, LeftUp = 0x0202, LeftDoubleClick = 0x0203;
    private const int RightDown = 0x0204, RightUp = 0x0205, RightDoubleClick = 0x0206;
    private const int MiddleDown = 0x0207, MiddleUp = 0x0208, MiddleDoubleClick = 0x0209;
    private static readonly BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    internal static int Run()
    {
        Exception? error = null;
        var failures = new List<string>();
        var thread = new Thread(() =>
        {
            App? app = null;
            MainWindow? window = null;
            TrayService? tray = null;
            try
            {
                // Suppress production startup: this fixture must not acquire the
                // installed monitor's mutex or signal its existing instance.
                app = TestAppFactory.Create();
                app.InitializeComponent();
                app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                window = new MainWindow(new NotificationService())
                {
                    WindowStartupLocation = WindowStartupLocation.Manual,
                    Left = -10000, Top = -10000, Width = 900, Height = 560,
                    ShowActivated = false, ShowInTaskbar = false
                };
                typeof(MainWindow).GetField("_isPopulatingAppSettings", PrivateInstance)!.SetValue(window, true);
                typeof(App).GetField("_mainWindow", PrivateInstance)!.SetValue(app, window);
                window.MinimizeToTrayRequested += (_, _) => window.Dispatcher.BeginInvoke(
                    DispatcherPriority.Normal, new Action(window.HideToTrayPreservingVisual));
                window.Show();
                window.UpdateLayout();
                Pump();
                var originalHandle = new WindowInteropHelper(window).Handle;
                var showCalls = 0;
                var exitCalls = 0;
                tray = new TrayService(
                    () =>
                    {
                        showCalls++;
                        typeof(App).GetMethod("ShowMainWindow", PrivateInstance)!.Invoke(app, null);
                    },
                    () => exitCalls++);
                var notifyIcon = GetIcon(tray);
                notifyIcon.Visible = false;

                void HideWindow()
                {
                    window.Close();
                    Pump();
                    if (IsWindowVisible(originalHandle))
                        throw new InvalidOperationException("Tray fixture failed to hide the existing HWND.");
                    showCalls = 0;
                }

                bool RestoredSameWindow() => IsWindowVisible(originalHandle) &&
                    new WindowInteropHelper(window).Handle == originalHandle;

                void Check(bool condition, string name, string observed)
                {
                    Console.WriteLine($"Tray {name}: {(condition ? "PASS" : "FAIL")} ({observed})");
                    if (!condition) failures.Add(name);
                }

                HideWindow();
                Dispatch(notifyIcon, LeftDown, LeftUp);
                Check(showCalls == 1 && RestoredSameWindow(), "left single click restores once",
                    $"showCalls={showCalls}, nativeVisible={IsWindowVisible(originalHandle)}, sameHwnd={new WindowInteropHelper(window).Handle == originalHandle}");

                HideWindow();
                Dispatch(notifyIcon, LeftDown, LeftUp);
                Check(showCalls == 1 && RestoredSameWindow(), "double click opens on first release",
                    $"showCalls={showCalls}, nativeVisible={IsWindowVisible(originalHandle)}");
                Dispatch(notifyIcon, LeftDoubleClick, LeftUp);
                Check(showCalls == 1 && RestoredSameWindow(), "left double click does not activate twice",
                    $"showCalls={showCalls}");
                Dispatch(notifyIcon, LeftDown, LeftUp);
                Check(showCalls == 2 && RestoredSameWindow(), "later single click still activates",
                    $"showCalls={showCalls}");

                HideWindow();
                Dispatch(notifyIcon, MiddleDown, MiddleUp, MiddleDoubleClick, MiddleUp);
                Check(showCalls == 0 && !IsWindowVisible(originalHandle), "middle clicks do not open",
                    $"showCalls={showCalls}, nativeVisible={IsWindowVisible(originalHandle)}");

                HideWindow();
                Dispatch(notifyIcon, RightDown, RightUp);
                var menu = GetMenu(tray);
                Check(showCalls == 0 && !IsWindowVisible(originalHandle) && menu is { IsVisible: true },
                    "right single click opens only the menu", $"showCalls={showCalls}, menuVisible={menu?.IsVisible}");
                menu?.Close();
                Dispatch(notifyIcon, RightDoubleClick, RightUp);
                menu = GetMenu(tray);
                Check(showCalls == 0 && !IsWindowVisible(originalHandle) && menu is { IsVisible: true },
                    "right double click opens only the menu", $"showCalls={showCalls}, menuVisible={menu?.IsVisible}");

                if (menu is null) throw new InvalidOperationException("Right-click menu missing for command verification.");
                ((Button)menu.FindName("OpenButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(showCalls == 1 && RestoredSameWindow() && GetMenu(tray) is null,
                    "menu open restores the existing window", $"showCalls={showCalls}, sameHwnd={new WindowInteropHelper(window).Handle == originalHandle}");

                HideWindow();
                Dispatch(notifyIcon, RightDown, RightUp);
                menu = GetMenu(tray) ?? throw new InvalidOperationException("Exit menu missing.");
                ((Button)menu.FindName("ExitButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(exitCalls == 1 && showCalls == 0, "menu exit keeps its own callback",
                    $"exitCalls={exitCalls}, showCalls={showCalls}");
                menu.Close();

                tray.Dispose();
                Dispatch(notifyIcon, LeftDown, LeftUp, LeftDoubleClick, LeftUp, RightUp);
                Check(showCalls == 0 && GetMenu(tray) is null, "dispose detaches activation and menu callbacks",
                    $"showCalls={showCalls}, menuPresent={GetMenu(tray) is not null}");
                tray = null;

                var exitingTray = new TrayService(() => showCalls++, () => exitCalls++);
                var exitingIcon = GetIcon(exitingTray);
                var ownedIcon = (System.Drawing.Icon)typeof(TrayService).GetField("_icon", PrivateInstance)!.GetValue(exitingTray)!;
                try
                {
                    exitingIcon.Visible = false;
                    exitingTray.PrepareForProcessExit();
                    Dispatch(exitingIcon, LeftDown, LeftUp, LeftDoubleClick, LeftUp, RightUp);
                    Check(showCalls == 0 && GetMenu(exitingTray) is null,
                        "process exit detaches activation and menu callbacks",
                        $"showCalls={showCalls}, menuPresent={GetMenu(exitingTray) is not null}");
                }
                finally
                {
                    // PrepareForProcessExit deliberately leaves native resources
                    // for process termination; the fixture owns their cleanup.
                    exitingIcon.Dispose();
                    ownedIcon.Dispose();
                }
            }
            catch (Exception exception) { error = exception.GetBaseException(); }
            finally
            {
                tray?.Dispose();
                if (window is not null) { window.PrepareForExit(); window.Close(); }
                app?.Shutdown();
            }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        if (!thread.Join(TimeSpan.FromSeconds(45)))
        {
            Console.Error.WriteLine("Tray click regression timed out.");
            return 1;
        }
        if (error is not null) { Console.Error.WriteLine(error); return 1; }
        if (failures.Count > 0)
        {
            Console.Error.WriteLine($"Tray click regression failed: {string.Join("; ", failures)}");
            return 1;
        }
        Console.WriteLine("Tray click regression PASS: real NotifyIcon native messages preserved single/double/right click routing and HWND lifecycle.");
        return 0;
    }

    private static NotifyIcon GetIcon(TrayService tray) =>
        (NotifyIcon)typeof(TrayService).GetField("_notifyIcon", PrivateInstance)!.GetValue(tray)!;

    private static TrayMenuWindow? GetMenu(TrayService tray) =>
        (TrayMenuWindow?)typeof(TrayService).GetField("_menu", PrivateInstance)!.GetValue(tray);

    private static void Dispatch(NotifyIcon notifyIcon, params int[] nativeEvents)
    {
        // Exercise the real runtime's native event sequence, including its
        // suppression of MouseClick after the second release of a double click.
        var wndProc = typeof(NotifyIcon).GetMethod("WndProc", PrivateInstance)!;
        foreach (var nativeEvent in nativeEvents)
        {
            var message = FormsMessage.Create(IntPtr.Zero, TrayCallbackMessage, IntPtr.Zero, (IntPtr)nativeEvent);
            wndProc.Invoke(notifyIcon, [message]);
        }
    }

    private static void Pump()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,
            new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr windowHandle);
}
