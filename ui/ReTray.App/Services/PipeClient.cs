using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json.Nodes;

namespace ReTray_App.Services;
public sealed class PipeClient
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly string pipeName;
    private readonly Func<bool> runtimeActive, applicationStopping;
    public PipeClient() : this(DefaultPipeName, DefaultRuntimeActive, () => IsApplicationStopping) { }
    internal PipeClient(string pipeName, Func<bool> runtimeActive, Func<bool> applicationStopping)
    {
        this.pipeName = pipeName;
        this.runtimeActive = runtimeActive;
        this.applicationStopping = applicationStopping;
    }
    private static string DefaultPipeName
    {
        get { using var process = Process.GetCurrentProcess(); return $"ReTray.v1.{process.SessionId}"; }
    }
    private static bool DefaultRuntimeActive()
    {
#if TRAY_WPF
        return LanDeviceMonitor.Services.NativeRuntime.IsActive;
#else
        return !App.IsExiting;
#endif
    }
    // Retained for the shared view model. Reconnect never creates or restarts
    // a backend independently of its owning application process.
    public void AllowStart() { }

    public async Task<JsonObject> RequestAsync(string method, JsonObject? data = null, CancellationToken token = default, TimeSpan? timeout = null)
    {
        var requestTimeout = timeout ?? TimeSpan.FromSeconds(method == "settings.import" ? 60 : 5);
        var elapsed = Stopwatch.StartNew();
        if (requestTimeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(timeout));
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(requestTimeout);
        bool entered = false;
        try
        {
            // Gate, connection and response share one deadline. A queued poll
            // cannot silently extend the operation's requested time budget.
            await gate.WaitAsync(deadline.Token);
            entered = true;
            using var pipe = await ConnectAsync(deadline.Token);
            using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, true) { AutoFlush = true };
            using var reader = new StreamReader(pipe, Encoding.UTF8, false, 4096, true);
            var request = new JsonObject { ["schema"] = 1, ["requestId"] = Guid.NewGuid().ToString(), ["method"] = method, ["data"] = data?.DeepClone() ?? new JsonObject() };
            deadline.Token.ThrowIfCancellationRequested();
            request["timeoutMs"] = (int)Math.Clamp((requestTimeout - elapsed.Elapsed).TotalMilliseconds, 1000, 60000);
            await writer.WriteLineAsync(request.ToJsonString().AsMemory(), deadline.Token);
            var line = await reader.ReadLineAsync(deadline.Token) ?? throw new IOException("ReTray 运行时已断开连接。");
            return JsonNode.Parse(line)?.AsObject() ?? throw new IOException("ReTray 运行时返回无效数据。");
        }
        catch (OperationCanceledException exception) when (!token.IsCancellationRequested && !applicationStopping())
        {
            // Cancellation from our deadline is a request timeout, not user
            // cancellation. Preserve caller cancellation and application exit.
            throw new TimeoutException($"ReTray 请求超时（{method}）。", exception);
        }
        finally { if (entered) gate.Release(); }
    }

    private static bool IsApplicationStopping => App.IsExiting
#if TRAY_WPF
        || LanDeviceMonitor.Services.NativeRuntime.IsStopRequested
#endif
        ;

    private async Task<NamedPipeClientStream> ConnectAsync(CancellationToken token)
    {
        Exception? lastFailure = null;
        do
        {
            token.ThrowIfCancellationRequested();
            if (!runtimeActive())
                throw new IOException("ReTray 运行时已停止。", lastFailure);
            var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            try
            {
                // RequestAsync owns the complete deadline, including an
                // explicitly longer startup budget. Do not impose a second 5s cap.
                await pipe.ConnectAsync(200, token);
                return pipe;
            }
            catch (Exception exception) when (exception is TimeoutException or IOException)
            {
                lastFailure = exception;
                pipe.Dispose();
            }
            catch { pipe.Dispose(); throw; }
            if (applicationStopping()) throw new OperationCanceledException("ReTray 已退出。", token);
            await Task.Delay(25, token);
        } while (true);
    }
}
