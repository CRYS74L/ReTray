using System.ComponentModel;
using System.Runtime.InteropServices;
using System.IO;

namespace LanDeviceMonitor.Services;

/// <summary>Owns the native tray message loop inside the interface process.</summary>
public static class NativeRuntime
{
    private const string Library = "ReTray.Runtime.dll";
    private static readonly object Gate = new();
    private static Thread? thread;
    private static TaskCompletionSource<uint>? completion;
    private static bool stopRequested;

    // P/Invoke keeps the module loaded for the process lifetime. Never free it
    // while its message thread, callbacks or Explorer hooks can still execute.
    public static event EventHandler? Completed;
    public static bool IsActive => completion is { Task.IsCompleted: false };
    public static bool IsReady => IsActive && ReTrayRuntimeState() == 2;

    public static void MigrateData()
    {
        uint error = ReTrayMigrateData(null);
        if (error != 0) throw new Win32Exception((int)error, "ReTray 配置迁移未完成，原配置已保留。");
    }

    public static async Task StartAsync()
    {
        Task<uint> finished;
        lock (Gate)
        {
            if (stopRequested) throw new OperationCanceledException("ReTray 已退出。");
            if (completion is not null)
                throw new InvalidOperationException("本进程的 ReTray 运行时已经启动过。");
            completion = new TaskCompletionSource<uint>(TaskCreationOptions.RunContinuationsAsynchronously);
            finished = completion.Task;
            thread = new Thread(Run) { IsBackground = true, Name = "ReTray.NativeMessageLoop" };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
        }

        var deadline = System.Diagnostics.Stopwatch.StartNew();
        while (!finished.IsCompleted)
        {
            if (ReTrayRuntimeState() == 2) return;
            if (deadline.Elapsed >= TimeSpan.FromSeconds(15))
            {
                await StopAsync().ConfigureAwait(false);
                throw new TimeoutException("ReTray 运行时未能及时完成启动。");
            }
            await Task.WhenAny(finished, Task.Delay(25)).ConfigureAwait(false);
        }
        uint result = await finished.ConfigureAwait(false);
        throw new IOException($"ReTray 运行时在就绪前退出（{result}）。");
    }

    private static void Run()
    {
        try { completion!.TrySetResult(ReTrayRunBackend(ApplicationPaths.IsPortable ? ApplicationPaths.DataDirectory : null)); }
        catch (Exception exception) { completion!.TrySetException(exception); }
        finally { Completed?.Invoke(null, EventArgs.Empty); }
    }

    public static void AttachInterface(nint window)
    {
        if (window == 0 || !ReTraySetInterfaceWindow(window))
            throw new IOException("ReTray 运行时无法关联本进程的界面窗口。");
    }

    public static async Task StopAsync()
    {
        Task<uint>? finished;
        Thread? runningThread;
        lock (Gate)
        {
            finished = completion?.Task;
            runningThread = thread;
            stopRequested = true;
        }
        if (finished is null || runningThread is null) return;

        // A stop requested during startup must survive the interval before
        // the native thread publishes its message queue and ready state.
        while (!finished.IsCompleted)
        {
            _ = ReTrayRequestStop();
            await Task.WhenAny(finished, Task.Delay(25)).ConfigureAwait(false);
        }
        await Task.Run(() => runningThread.Join()).ConfigureAwait(false);
        await finished.ConfigureAwait(false);
    }

    public static bool IsStopRequested
    {
        get { lock (Gate) return stopRequested; }
    }

    [DllImport(Library, ExactSpelling = true, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Winapi)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory)]
    private static extern uint ReTrayMigrateData(string? localAppData);

    [DllImport(Library, ExactSpelling = true, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Winapi)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory)]
    private static extern uint ReTrayRunBackend(string? dataDirectory);

    [DllImport(Library, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReTrayRequestStop();

    [DllImport(Library, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory)]
    private static extern uint ReTrayRuntimeState();

    [DllImport(Library, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReTraySetInterfaceWindow(nint window);
}
