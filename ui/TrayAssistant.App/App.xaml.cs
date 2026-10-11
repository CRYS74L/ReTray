using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.Windows.AppLifecycle;
using System.Runtime.InteropServices;
using System.Diagnostics;
using TrayAssistant_App.Services;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace TrayAssistant_App;

/// <summary>
/// Provides application-specific behavior to supplement the default Application class.
/// </summary>
public partial class App : Application
{
    public static bool IsExiting { get; private set; }
    private static Process? agent;
    private static int observedAgentId;
    public static void RegisterStartedAgent(Process process)
    {
        agent?.Dispose(); agent = process;
        observedAgentId = 0;
    }
    public static void ObserveAgent(int processId)
    {
        if (IsExiting || observedAgentId == processId) return;
        if (agent?.Id != processId) { agent?.Dispose(); agent = null; }
        try
        {
            var process = agent?.Id == processId ? agent : Process.GetProcessById(processId);
            if (!string.Equals(Path.GetFileName(process.MainModule?.FileName), "TrayAssistant.Agent.exe", StringComparison.OrdinalIgnoreCase)) { process.Dispose(); return; }
            agent = process;
            observedAgentId = processId;
            process.Exited += (_, _) => DispatcherQueue.TryEnqueue(CloseInterface);
            process.EnableRaisingEvents = true;
        }
        catch (ArgumentException) { CloseInterface(); }
    }
    private static void CloseInterface()
    {
        IsExiting = true;
        agent?.Dispose(); agent = null;
        Current.Exit();
    }
    public static async Task QuitAsync()
    {
        if (IsExiting) return;
        IsExiting = true;
        try
        {
            if (Window.Content is Panel root && root.Children.OfType<Frame>().FirstOrDefault()?.Content is MainPage page && !await page.FlushChangesAsync())
            {
                IsExiting = false;
                return;
            }
            var until = DateTime.UtcNow.AddSeconds(2);
            var window = FindWindow("TrayAssistant.Agent", null);
            while (window == 0 && agent is { HasExited: false } && DateTime.UtcNow < until)
            {
                await Task.Delay(25);
                window = FindWindow("TrayAssistant.Agent", null);
            }
            if (window != 0)
            {
                GetWindowThreadProcessId(window, out var owner);
                using var process = Process.GetProcessById((int)owner);
                var expected = Path.Combine(AppContext.BaseDirectory, "TrayAssistant.Agent.exe");
                if (!string.Equals(process.MainModule?.FileName, expected, StringComparison.OrdinalIgnoreCase)) throw new IOException("后台身份不匹配，未能退出");
                if (!PostMessage(window, 0x0010, 0, 0)) throw new IOException("退出请求未送达");
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await process.WaitForExitAsync(deadline.Token);
            }
            else if (agent is { HasExited: false }) throw new IOException("后台正在启动，请稍后重试退出");
            CloseInterface();
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or System.ComponentModel.Win32Exception)
        {
            IsExiting = false;
            if (Window.Content is Panel panel && panel.Children.OfType<Frame>().FirstOrDefault()?.Content is MainPage page) page.Vm.ReportError(ex);
        }
    }
    [DllImport("user32.dll", EntryPoint="FindWindowW", CharSet=CharSet.Unicode)] private static extern nint FindWindow(string className, string? title);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint processId);
    [DllImport("user32.dll", SetLastError=true)] private static extern bool PostMessage(nint window, uint message, nint wparam, nint lparam);
    [DllImport("user32.dll")] private static extern bool ShowWindow(nint hwnd,int command);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint hwnd);
    /// <summary>
    /// The main application window. Use <c>App.Window</c> from any class that needs
    /// the window reference (for dialogs, pickers, interop, etc.).
    /// </summary>
    public static Window Window { get; private set; } = null!;

    /// <summary>
    /// The UI thread dispatcher. Use <c>App.DispatcherQueue</c> to marshal calls
    /// to the UI thread. Fully qualified to avoid CS0104 ambiguity with
    /// <see cref="Windows.System.DispatcherQueue"/>.
    /// </summary>
    public static Microsoft.UI.Dispatching.DispatcherQueue DispatcherQueue { get; private set; } = null!;

    /// <summary>
    /// The native window handle (HWND). Use for file pickers,
    /// <c>DataTransferManager</c>, and any WinRT interop that requires
    /// <c>InitializeWithWindow</c>.
    /// </summary>
    public static nint WindowHandle =>
        WinRT.Interop.WindowNative.GetWindowHandle(Window);

    /// <summary>
    /// Initializes the singleton application object.
    /// </summary>
    public App()
    {
        InitializeComponent();
        UnhandledException += (_, args) =>
        {
            try { var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TrayIconAssistant"); Directory.CreateDirectory(folder); File.WriteAllText(Path.Combine(folder, "ui-crash.log"), args.Exception.ToString()); }
            catch { }
        };
    }

    /// <summary>
    /// Invoked when the application is launched.
    /// </summary>
    /// <param name="args">Details about the launch request and process.</param>
    protected override async void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        var instance=AppInstance.FindOrRegisterForKey("TrayAssistant.UI."+System.Diagnostics.Process.GetCurrentProcess().SessionId);
        if(!instance.IsCurrent){await instance.RedirectActivationToAsync(AppInstance.GetCurrent().GetActivatedEventArgs());Environment.Exit(0);return;}
        Window = new MainWindow();
        DispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        instance.Activated+=(_,_)=>DispatcherQueue.TryEnqueue(()=>{ShowWindow(WindowHandle,9);Window.Activate();SetForegroundWindow(WindowHandle);});
        Window.Activate();
    }
}
