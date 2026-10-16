using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;
using System.Windows.Threading;
using ReTray_App.Services;
using ReTray_App.ViewModels;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        var dispatcher = Dispatcher.CurrentDispatcher;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
        var tests = RunAsync();
        _ = tests.ContinueWith(_ => dispatcher.BeginInvokeShutdown(DispatcherPriority.Background));
        Dispatcher.Run();
        try { tests.GetAwaiter().GetResult(); return 0; }
        catch (Exception exception) { Console.Error.WriteLine(exception); return 1; }
    }

    private static async Task RunAsync()
    {
        foreach (var (name, test) in new (string, Func<Task>)[] {
            ("startup stays loading during a response delayed beyond five seconds", DelayedStartupResponse),
            ("explicit deadline permits connecting after five seconds", DelayedListener),
            ("interactive requests retain their five-second timeout", InteractiveTimeout),
            ("caller cancellation remains cancellation", CallerCancellation),
            ("queued requests include gate wait in their deadline", QueueDeadline),
            ("finite startup failure can be retried without replaying writes", StartupRetry),
            ("not-ready health cannot keep startup placeholders forever", ReadinessDeadline)
        }) {
            await test();
            Console.WriteLine("PASS " + name);
        }
    }

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }
    // Internal seams select a private test pipe and a short test-only startup
    // budget. Tests never load the native DLL or address the user's live pipe.
    private static PipeClient Client(FakeRuntime runtime) => (PipeClient)Activator.CreateInstance(
        typeof(PipeClient), BindingFlags.Instance | BindingFlags.NonPublic, null,
        [runtime.Name, (Func<bool>)(() => true), (Func<bool>)(() => false)], null)!;
    private static TrayViewModel ViewModel(PipeClient pipe, TimeSpan budget) => (TrayViewModel)Activator.CreateInstance(
        typeof(TrayViewModel), BindingFlags.Instance | BindingFlags.NonPublic, null, [pipe, budget], null)!;

    private static async Task DelayedStartupResponse()
    {
        await using var runtime = new FakeRuntime(responseDelay: TimeSpan.FromMilliseconds(6200));
        var vm = ViewModel(Client(runtime), TimeSpan.FromSeconds(30));
        var initialize = vm.InitializeAsync();
        await Task.Delay(5300);
        Require(vm.IsLoading && !vm.IsReady && vm.Error.Length == 0,
            "A legitimate startup delay exposed an error or removed placeholders: " + vm.Error);
        await initialize.WaitAsync(TimeSpan.FromSeconds(10));
        Require(vm.IsReady && !vm.IsLoading && vm.Error.Length == 0, "Delayed startup did not become ready");
        var requests = runtime.Requests;
        Require(requests.Count(r => r["method"]!.GetValue<string>() == "settings.apply") == 1,
            "Startup retried a write");
        int first = requests[0]["timeoutMs"]!.GetValue<int>();
        int last = requests[^1]["timeoutMs"]!.GetValue<int>();
        Require(first <= 30000 && first >= 28000 && last < first - 5000,
            "Startup requests did not consume the same finite budget");
    }

    private static async Task DelayedListener()
    {
        await using var runtime = new FakeRuntime(listenDelay: TimeSpan.FromMilliseconds(6200));
        var result = await Client(runtime).RequestAsync("status.read", timeout: TimeSpan.FromSeconds(9));
        Require(result["success"]!.GetValue<bool>(), "Longer connection budget was ignored");
    }

    private static async Task InteractiveTimeout()
    {
        await using var runtime = new FakeRuntime(responseDelay: TimeSpan.FromSeconds(7));
        var elapsed = Stopwatch.StartNew();
        try { await Client(runtime).RequestAsync("status.read"); throw new Exception("Expected timeout"); }
        catch (TimeoutException exception) {
            Require(exception.Message.Contains("status.read") && elapsed.Elapsed.TotalSeconds is >= 4.5 and < 6.5,
                "Interactive timeout changed or lost request context");
        }
    }

    private static async Task CallerCancellation()
    {
        await using var runtime = new FakeRuntime(responseDelay: TimeSpan.FromSeconds(2));
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));
        try { await Client(runtime).RequestAsync("status.read", token: cancel.Token); throw new Exception("Expected cancellation"); }
        catch (OperationCanceledException) { Require(cancel.IsCancellationRequested, "Caller cancellation was not preserved"); }
    }

    private static async Task QueueDeadline()
    {
        await using var runtime = new FakeRuntime(responseDelay: TimeSpan.FromMilliseconds(800));
        var pipe = Client(runtime);
        var first = pipe.RequestAsync("status.read", timeout: TimeSpan.FromSeconds(3));
        await runtime.FirstRequest.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var elapsed = Stopwatch.StartNew();
        try { await pipe.RequestAsync("settings.read", timeout: TimeSpan.FromMilliseconds(150)); throw new Exception("Expected queue timeout"); }
        catch (TimeoutException) { Require(elapsed.ElapsedMilliseconds < 600, "Gate wait escaped the request deadline"); }
        await first;
        Require(runtime.Requests.Count == 1, "An expired queued request was still sent");
    }

    private static async Task StartupRetry()
    {
        await using var runtime = new FakeRuntime(responseDelay: TimeSpan.FromMilliseconds(800));
        var vm = ViewModel(Client(runtime), TimeSpan.FromMilliseconds(250));
        await vm.InitializeAsync();
        Require(!vm.IsLoading && !vm.IsReady && vm.Error.Contains("启动未在") && !vm.Error.Contains("operation was canceled"),
            "Finite startup failure was hidden or exposed raw cancellation");
        await Task.Delay(850);
        await vm.ReconnectCommand.ExecuteAsync(null);
        Require(vm.IsReady && vm.Error.Length == 0, "Explicit retry did not reset startup budget");
        Require(runtime.Requests.Count(r => r["method"]!.GetValue<string>() == "settings.apply") == 1,
            "A timed-out startup replayed a write");
    }

    private static async Task ReadinessDeadline()
    {
        await using var runtime = new FakeRuntime(ready: false);
        var vm = ViewModel(Client(runtime), TimeSpan.FromMilliseconds(250));
        await vm.InitializeAsync();
        Require(vm.IsLoading && !vm.IsReady && vm.Error.Length == 0, "Not-ready health failed too early");
        await Task.Delay(300);
        await vm.RefreshAsync();
        Require(!vm.IsLoading && vm.Error.Contains("启动未在"), "Not-ready health extended startup indefinitely");
    }

    private sealed class FakeRuntime : IAsyncDisposable
    {
        public string Name { get; } = "ReTray.StartupTest." + Guid.NewGuid().ToString("N");
        public TaskCompletionSource FirstRequest { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly List<JsonObject> requests = [];
        public List<JsonObject> Requests { get { lock (requests) return requests.ToList(); } }
        private readonly CancellationTokenSource stop = new();
        private readonly Task server;
        public FakeRuntime(TimeSpan? responseDelay = null, TimeSpan? listenDelay = null, bool ready = true)
        {
            server = Task.Run(async () => {
                var token = stop.Token;
                if (listenDelay.HasValue) await Task.Delay(listenDelay.Value, token);
                bool first = true;
                while (!token.IsCancellationRequested) {
                    using var pipe = new NamedPipeServerStream(Name, PipeDirection.InOut, 1,
                        PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                    await pipe.WaitForConnectionAsync(token);
                    try {
                        using var reader = new StreamReader(pipe, Encoding.UTF8, false, 4096, true);
                        using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, true) { AutoFlush = true };
                        var line = await reader.ReadLineAsync(token);
                        if (line is null) continue;
                        var request = JsonNode.Parse(line)!.AsObject();
                        lock (requests) requests.Add(request);
                        FirstRequest.TrySetResult();
                        if (first && responseDelay.HasValue) await Task.Delay(responseDelay.Value, token);
                        first = false;
                        string method = request["method"]!.GetValue<string>();
                        JsonNode data = method switch {
                            "settings.read" => JsonNode.Parse("""
                                {"startup":false,"ownIcon":{"visible":true,"asset":"","tip":"ReTray","showInList":true},
                                 "hotkeys":{},"menu":{},"iconOrder":[],"theme":"system","language":"zh"}
                                """)!,
                            "status.read" => new JsonObject { ["pid"] = Environment.ProcessId, ["ready"] = ready, ["connected"] = ready },
                            "catalog.read" => new JsonObject { ["icons"] = new JsonArray() },
                            _ => new JsonObject()
                        };
                        var response = new JsonObject { ["success"] = true, ["data"] = data, ["revision"] = 0,
                            ["requestId"] = request["requestId"]!.DeepClone() };
                        await writer.WriteLineAsync(response.ToJsonString().AsMemory(), token);
                    }
                    catch (IOException) { /* A timed-out client closes its private pipe. */ }
                }
            });
        }
        public async ValueTask DisposeAsync()
        {
            stop.Cancel();
            try { await server; } catch (OperationCanceledException) { }
            stop.Dispose();
        }
    }
}
