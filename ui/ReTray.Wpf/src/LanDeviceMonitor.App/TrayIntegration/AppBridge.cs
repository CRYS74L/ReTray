using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Interop;
using System.Windows.Threading;

namespace ReTray_App;

// Shared tray business code binds to this facade while the application and
// window continue to use the original LAN project's WPF lifecycle.
public static class App
{
    private static LanDeviceMonitor.App? application;
    private static Process? agent;
    private static int observedAgentId;
    private static bool interfaceShutdownStarted;

    public static bool IsExiting { get; private set; }
    public static bool CanCloseInterface { get; private set; }
    public static string UiActivationId { get; private set; } = "";
    public static LanDeviceMonitor.MainWindow Window { get; private set; } = null!;
    public static UiDispatcher DispatcherQueue { get; } = new();
    public static nint WindowHandle => Window is null
        ? 0
        : new WindowInteropHelper(Window).Handle;

    internal static void Initialize(
        LanDeviceMonitor.App sourceApplication,
        LanDeviceMonitor.MainWindow sourceWindow)
    {
        application = sourceApplication;
        Window = sourceWindow;
        UiActivationId = ReadActivationId();
    }

    public static void RegisterStartedAgent(Process process)
    {
        ArgumentNullException.ThrowIfNull(process);
        if (ReferenceEquals(process, agent)) return;
        // Process.Start has just returned; its main module may not be loaded
        // yet. Validate the requested executable now, and validate the live
        // image when health polling observes it or when it is stopped.
        var requested = process.StartInfo.FileName;
        if (string.IsNullOrWhiteSpace(requested) ||
            !string.Equals(Path.GetFullPath(requested), ExpectedAgentPath, StringComparison.OrdinalIgnoreCase))
            ValidateAgentIdentity(process);
        ReleaseAgent();
        agent = process;
    }

    public static void ObserveAgent(int processId)
    {
        if (IsExiting || observedAgentId == processId)
            return;

        Process? process = null;
        try
        {
            process = agent?.Id == processId ? agent : Process.GetProcessById(processId);
            ValidateAgentIdentity(process);
            if (!ReferenceEquals(process, agent))
            {
                ReleaseAgent();
                agent = process;
            }

            observedAgentId = processId;
            process.Exited += Agent_Exited;
            process.EnableRaisingEvents = true;
        }
        catch (ArgumentException)
        {
            if (!ReferenceEquals(process, agent)) process?.Dispose();
            DispatcherQueue.TryEnqueue(() => _ = QuitAsync());
        }
        catch
        {
            if (!ReferenceEquals(process, agent)) process?.Dispose();
            throw;
        }
    }

    private static void Agent_Exited(object? sender, EventArgs e)
    {
        if (!ReferenceEquals(sender, agent) || IsExiting)
            return;

        DispatcherQueue.TryEnqueue(() =>
        {
            if (!IsExiting && ReferenceEquals(sender, agent))
                _ = QuitAsync();
        });
    }

    public static async Task QuitAsync()
    {
        if (application is null || interfaceShutdownStarted)
            return;

        if (!application.Dispatcher.CheckAccess())
        {
            await application.Dispatcher.InvokeAsync(QuitAsync).Task.Unwrap();
            return;
        }

        if (IsExiting)
            return;

        IsExiting = true;
        var wasEnabled = Window.IsEnabled;
        Window.IsEnabled = false;
        try
        {
            if (!await Window.TrayPage.FlushChangesAsync())
            {
                IsExiting = false;
                Window.IsEnabled = wasEnabled;
                return;
            }

            await StopLocalAgentAsync();
            interfaceShutdownStarted = true;
            CanCloseInterface = true;
            application.CompleteInterfaceExit();
        }
        catch (Exception exception)
        {
            interfaceShutdownStarted = false;
            CanCloseInterface = false;
            IsExiting = false;
            Window.IsEnabled = wasEnabled;
            Window.TrayPage.Vm.ReportError(exception);
        }
    }

    private static async Task StopLocalAgentAsync()
    {
        var until = DateTime.UtcNow.AddSeconds(2);
        while (true)
        {
            var window = FindLocalAgentWindow(out var process);
            if (window != 0)
            {
                using (process)
                {
                    if (!PostMessage(window, 0x0010, 0, 0))
                        throw new IOException("后台退出请求未送达。", new Win32Exception(Marshal.GetLastWin32Error()));
                    using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    try
                    {
                        await process!.WaitForExitAsync(deadline.Token);
                    }
                    catch (OperationCanceledException exception)
                    {
                        throw new IOException("后台未在等待时间内完成退出，请稍后重试。", exception);
                    }
                }
                return;
            }

            if (agent is null || agent.HasExited)
                return;

            ValidateAgentIdentity(agent);
            if (DateTime.UtcNow >= until)
                throw new IOException("后台正在启动，未能完成正常退出，请稍后重试。");
            await Task.Delay(25);
        }
    }

    private static nint FindLocalAgentWindow(out Process? process)
    {
        process = null;
        nint previous = 0;
        while ((previous = FindWindowEx(0, previous, "ReTray.Agent", null)) != 0)
        {
            GetWindowThreadProcessId(previous, out var processId);
            Process? candidate = null;
            try
            {
                candidate = Process.GetProcessById((int)processId);
                ValidateAgentIdentity(candidate);
                if (agent is not null && !agent.HasExited && candidate.Id != agent.Id)
                {
                    candidate.Dispose();
                    continue;
                }

                process = candidate;
                return previous;
            }
            catch (Exception exception) when (exception is ArgumentException or
                InvalidOperationException or Win32Exception or IOException)
            {
                candidate?.Dispose();
            }
        }
        return 0;
    }

    private static void ValidateAgentIdentity(Process process)
    {
        using var currentProcess = Process.GetCurrentProcess();
        var actual = process.MainModule?.FileName;
        if (process.SessionId != currentProcess.SessionId ||
            actual is null ||
            !string.Equals(Path.GetFullPath(actual), ExpectedAgentPath, StringComparison.OrdinalIgnoreCase))
            throw new IOException("后台身份或安装目录不匹配。");
    }

    private static string ExpectedAgentPath =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "ReTray.Agent.exe"));

    internal static void PrepareForProcessExit()
    {
        IsExiting = true;
        CanCloseInterface = true;
        ReleaseAgent();
    }

    private static void ReleaseAgent()
    {
        observedAgentId = 0;
        if (agent is null) return;
        agent.Exited -= Agent_Exited;
        agent.Dispose();
        agent = null;
    }

    private static string ReadActivationId()
    {
        uint length = 0;
        if (GetCurrentApplicationUserModelId(ref length, null) == 122)
        {
            var identity = new StringBuilder((int)length);
            if (GetCurrentApplicationUserModelId(ref length, identity) == 0)
                return identity.ToString();
        }

        length = 0;
        if (GetCurrentPackageFamilyName(ref length, null) != 122) return "";
        var family = new StringBuilder((int)length);
        return GetCurrentPackageFamilyName(ref length, family) == 0
            ? family + "!App"
            : "";
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetCurrentApplicationUserModelId(ref uint length, StringBuilder? identity);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetCurrentPackageFamilyName(ref uint length, StringBuilder? family);
    [DllImport("user32.dll", EntryPoint = "FindWindowExW", CharSet = CharSet.Unicode)]
    private static extern nint FindWindowEx(nint parent, nint after, string? className, string? title);
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint handle, out uint processId);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(nint handle, uint message, nint wordParameter, nint longParameter);
}

public sealed class UiDispatcher
{
    public bool TryEnqueue(Action action)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
            return false;
        try
        {
            dispatcher.BeginInvoke(action, DispatcherPriority.Normal);
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }
}
