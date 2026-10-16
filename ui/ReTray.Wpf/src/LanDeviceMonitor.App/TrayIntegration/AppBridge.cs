using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Interop;
using System.Windows.Threading;
using LanDeviceMonitor.Services;

namespace ReTray_App;

// Shared business code uses this facade; both UI and native tray runtime now
// belong to the same ReTray process and share one orderly lifetime.
public static class App
{
    private static LanDeviceMonitor.App? application;
    private static bool interfaceShutdownStarted;
    private static bool restartStarted;
    public static bool IsExiting { get; private set; }
    public static bool CanCloseInterface { get; private set; }
    public static string UiActivationId { get; private set; } = "";
    public static LanDeviceMonitor.MainWindow Window { get; private set; } = null!;
    public static UiDispatcher DispatcherQueue { get; } = new();
    public static nint WindowHandle => Window is null ? 0 : new WindowInteropHelper(Window).Handle;

    internal static void Initialize(LanDeviceMonitor.App sourceApplication, LanDeviceMonitor.MainWindow sourceWindow)
    {
        application = sourceApplication;
        Window = sourceWindow;
        UiActivationId = ReadActivationId();
        NativeRuntime.Completed += RuntimeCompleted;
    }

    public static void ObserveAgent(int processId)
    {
        if (processId != Environment.ProcessId)
            throw new IOException("ReTray 运行时进程身份不匹配。");
    }

    public static void OpenInterface()
    {
        if (application is null || IsExiting) return;
        if (application.Dispatcher.CheckAccess()) application.ShowMainWindow();
        else DispatcherQueue.TryEnqueue(application.ShowMainWindow);
    }

    private static void RuntimeCompleted(object? sender, EventArgs args)
    {
        if (interfaceShutdownStarted || NativeRuntime.IsStopRequested) return;
        DispatcherQueue.TryEnqueue(async () =>
        {
            if (interfaceShutdownStarted || NativeRuntime.IsStopRequested) return;
            // There is no live pipe to flush. Immediately retire the interface
            // rather than leaving a stale disconnected settings window behind.
            IsExiting = true;
            CanCloseInterface = true;
            interfaceShutdownStarted = true;
            Window.IsEnabled = false;
            await FinishExitAsync();
        });
    }

    public static async Task QuitAsync()
    {
        if (application is null || interfaceShutdownStarted) return;
        if (!application.Dispatcher.CheckAccess())
        {
            await application.Dispatcher.InvokeAsync(QuitAsync).Task.Unwrap();
            return;
        }
        if (IsExiting) return;
        IsExiting = true;
        bool wasEnabled = Window.IsEnabled;
        Window.IsEnabled = false;
        try
        {
            if (NativeRuntime.IsActive && !await Window.TrayPage.FlushChangesAsync())
            {
                // Completion can race an outstanding save; never restore an
                // interface whose native message loop has already finished.
                if (NativeRuntime.IsActive)
                {
                    IsExiting = false;
                    Window.IsEnabled = wasEnabled;
                    return;
                }
            }
            await FinishExitAsync();
        }
        catch (Exception exception)
        {
            if (!NativeRuntime.IsActive)
            {
                await FinishExitAsync();
                return;
            }
            CanCloseInterface = false;
            interfaceShutdownStarted = false;
            IsExiting = false;
            Window.IsEnabled = wasEnabled;
            Window.TrayPage.Vm.ReportError(exception);
        }
    }

    private static async Task FinishExitAsync()
    {
        try { await NativeRuntime.StopAsync(); }
        catch (Exception) when (!NativeRuntime.IsActive) { }
        interfaceShutdownStarted = true;
        CanCloseInterface = true;
        application?.CompleteInterfaceExit();
    }

    public static async Task RestartAsync()
    {
        if (application is null || IsExiting || restartStarted) return;
        if (!application.Dispatcher.CheckAccess())
        {
            await application.Dispatcher.InvokeAsync(RestartAsync).Task.Unwrap();
            return;
        }
        restartStarted = true;
        bool wasEnabled = Window.IsEnabled;
        Window.IsEnabled = false;
        try
        {
            // Flush before launching the waiting successor. A failed save must
            // leave the current process usable without a stray pending restart.
            if (NativeRuntime.IsActive && !await Window.TrayPage.FlushChangesAsync()) return;
            string executable = Environment.ProcessPath ?? throw new IOException("无法确定 ReTray 启动路径。");
            var info = new ProcessStartInfo(executable) { UseShellExecute = false, WorkingDirectory = AppContext.BaseDirectory };
            info.ArgumentList.Add("--restart-after");
            info.ArgumentList.Add(Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
            using var successor = Process.Start(info) ?? throw new IOException("无法启动 ReTray。");
            // QuitAsync performs the final save and joins the native thread.
            // If that final save fails, the successor expires after 30 seconds
            // while the original process remains usable.
            await QuitAsync();
        }
        catch (Exception exception)
        {
            if (!IsExiting) Window.TrayPage.Vm.ReportError(exception);
        }
        finally
        {
            restartStarted = false;
            if (!IsExiting) Window.IsEnabled = wasEnabled;
        }
    }

    internal static void PrepareForProcessExit()
    {
        IsExiting = true;
        CanCloseInterface = true;
        NativeRuntime.Completed -= RuntimeCompleted;
    }

    private static string ReadActivationId()
    {
        uint length = 0;
        if (GetCurrentApplicationUserModelId(ref length, null) == 122)
        {
            var identity = new StringBuilder((int)length);
            if (GetCurrentApplicationUserModelId(ref length, identity) == 0) return identity.ToString();
        }
        length = 0;
        if (GetCurrentPackageFamilyName(ref length, null) != 122) return "";
        var family = new StringBuilder((int)length);
        return GetCurrentPackageFamilyName(ref length, family) == 0 ? family + "!App" : "";
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetCurrentApplicationUserModelId(ref uint length, StringBuilder? identity);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetCurrentPackageFamilyName(ref uint length, StringBuilder? family);
}

public sealed class UiDispatcher
{
    public bool TryEnqueue(Action action)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished) return false;
        try { dispatcher.BeginInvoke(action, DispatcherPriority.Normal); return true; }
        catch (InvalidOperationException) { return false; }
    }
}
