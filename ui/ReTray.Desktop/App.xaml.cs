using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using ReTray_App.Controls;

namespace ReTray_App;

public partial class App : Application
{
    private static Process? agent;
    private static int observedAgentId;
    private static bool interfaceShutdownStarted;
    private Mutex? instanceMutex;
    public static bool IsExiting { get; private set; }
    public static bool CanCloseInterface { get; private set; }
    public static MainWindow Window { get; private set; } = null!;
    public static UiDispatcher DispatcherQueue { get; } = new();
    public static nint WindowHandle => new WindowInteropHelper(Window).Handle;
    public static string UiActivationId { get; private set; } = "";

    protected override void OnStartup(StartupEventArgs args)
    {
        base.OnStartup(args);
        instanceMutex = new Mutex(true, "Local\\ReTray.UI." + Process.GetCurrentProcess().SessionId, out var first);
        if (!first)
        {
            var existing = FindWindow(null, "ReTray");
            if (existing != 0) { ShowWindow(existing, 9); SetForegroundWindow(existing); }
            Shutdown(); return;
        }
        UiActivationId = ReadActivationId();
        DispatcherUnhandledException += (_, eventArgs) =>
        {
            try
            {
                var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ReTray");
                Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder, "ui-crash.log"), eventArgs.Exception.ToString());
            }
            catch { }
            if (Window != null) { Window.Page.Vm.ReportError(eventArgs.Exception); eventArgs.Handled = true; }
        };
        ThemeManager.Apply(ReadThemeIndex());
        Window = new MainWindow();
        MainWindow = Window;
        Window.Show();
    }

    private static int ReadThemeIndex()
    {
        try
        {
            using var settings = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ReTray", "settings.json")));
            return settings.RootElement.GetProperty("theme").GetString() switch { "light" => 1, "dark" => 2, _ => 0 };
        }
        catch { return 0; }
    }

    public static void RegisterStartedAgent(Process process)
    {
        agent?.Dispose(); agent = process; observedAgentId = 0;
    }

    public static void ObserveAgent(int processId)
    {
        if (IsExiting || observedAgentId == processId) return;
        if (agent?.Id != processId) { agent?.Dispose(); agent = null; }
        try
        {
            var process = agent?.Id == processId ? agent : Process.GetProcessById(processId);
            if (!string.Equals(Path.GetFileName(process.MainModule?.FileName), "ReTray.Agent.exe", StringComparison.OrdinalIgnoreCase)) { process.Dispose(); return; }
            agent = process; observedAgentId = processId;
            process.Exited += (_, _) => DispatcherQueue.TryEnqueue(CloseInterface);
            process.EnableRaisingEvents = true;
        }
        catch (ArgumentException) { CloseInterface(); }
    }

    private static void CloseInterface()
    {
        if (interfaceShutdownStarted || Current?.Dispatcher.HasShutdownStarted == true) return;
        interfaceShutdownStarted = true;
        IsExiting = true;
        try
        {
            if (Window != null && Window.IsLoaded && PresentationSource.FromVisual(Window) != null)
                Window.PrepareCloseTransition();
            CanCloseInterface = true;
            agent?.Dispose(); agent = null;
            Current?.Shutdown();
        }
        catch (Exception exception)
        {
            CanCloseInterface = false;
            IsExiting = false;
            interfaceShutdownStarted = false;
            if (Window != null) Window.Page.Vm.ReportError(exception);
        }
    }

    public static async Task QuitAsync()
    {
        if (IsExiting) return;
        IsExiting = true;
        try
        {
            if (!await Window.Page.FlushChangesAsync()) { CanCloseInterface = false; IsExiting = false; return; }
            var until = DateTime.UtcNow.AddSeconds(2);
            var handle = FindWindow("ReTray.Agent", null);
            while (handle == 0 && agent is { HasExited: false } && DateTime.UtcNow < until)
            {
                await Task.Delay(25); handle = FindWindow("ReTray.Agent", null);
            }
            if (handle != 0)
            {
                GetWindowThreadProcessId(handle, out var owner);
                using var process = Process.GetProcessById((int)owner);
                var expected = Path.Combine(AppContext.BaseDirectory, "ReTray.Agent.exe");
                if (!string.Equals(process.MainModule?.FileName, expected, StringComparison.OrdinalIgnoreCase)) throw new IOException("后台身份不匹配，未能退出");
                if (!PostMessage(handle, 0x0010, 0, 0)) throw new IOException("退出请求未送达");
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await process.WaitForExitAsync(deadline.Token);
            }
            else if (agent is { HasExited: false }) throw new IOException("后台正在启动，请稍后重试退出");
            CloseInterface();
        }
        catch (Exception exception) when (exception is IOException or OperationCanceledException or System.ComponentModel.Win32Exception)
        {
            CanCloseInterface = false; IsExiting = false; Window.Page.Vm.ReportError(exception);
        }
    }

    private static string ReadActivationId()
    {
        uint length = 0;
        if (GetCurrentPackageFamilyName(ref length, null) != 122) return "";
        var family = new StringBuilder((int)length);
        return GetCurrentPackageFamilyName(ref length, family) == 0 ? family + "!App" : "";
    }

    protected override void OnExit(ExitEventArgs args)
    {
        agent?.Dispose(); instanceMutex?.Dispose();
        base.OnExit(args);
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern int GetCurrentPackageFamilyName(ref uint length, StringBuilder? family);
    [DllImport("user32.dll", EntryPoint = "FindWindowW", CharSet = CharSet.Unicode)] private static extern nint FindWindow(string? className, string? title);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint handle, out uint process);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool PostMessage(nint handle, uint message, nint wparam, nint lparam);
    [DllImport("user32.dll")] private static extern bool ShowWindow(nint handle, int command);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint handle);
}

public sealed class UiDispatcher
{
    public bool TryEnqueue(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.HasShutdownStarted) return false;
        dispatcher.BeginInvoke(action, DispatcherPriority.Normal);
        return true;
    }
}
