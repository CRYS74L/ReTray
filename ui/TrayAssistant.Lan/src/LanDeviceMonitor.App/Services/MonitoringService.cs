using System.Net.Http;
using LanDeviceMonitor.Models;

namespace LanDeviceMonitor.Services;

public sealed class MonitoringService : IDisposable
{
    private readonly XiaomiRouterClient _routerClient;
    private readonly DeviceRepository _repository;
    private readonly CancellationTokenSource _disposeCancellation = new();
    private readonly SemaphoreSlim _loopRestartGate = new(1, 1);
    private static readonly TimeSpan InternetInfoRefreshInterval =
        TimeSpan.FromSeconds(5);
    private CancellationTokenSource? _cancellation;
    private Task? _loopTask;
    private Task? _internetInfoLoopTask;
    private Task? _meshLoopTask;
    private readonly SemaphoreSlim _meshRequestGate = new(1, 1);
    private readonly object _meshContextGate = new();
    private CancellationTokenSource _meshRequestsCancellation = new();
    private long _meshGeneration;
    private IReadOnlyList<MeshNodeInfo> _lastMeshNodes = [];
    private DateTimeOffset _lastMeshRequestedAt;
    private bool _hasValidMeshTopology;
    private static readonly TimeSpan MeshRefreshInterval = TimeSpan.FromMinutes(1);
    private RouterInternetInfo? _lastInternetInfo;
    private int _refreshIntervalSeconds = 1;
    private string? _lastConnectionState;
    private bool _isDisposed;

    public MonitoringService(XiaomiRouterClient routerClient, DeviceRepository repository)
    {
        _routerClient = routerClient;
        _repository = repository;
    }

    public event EventHandler<MonitorUpdate>? Updated;
    public event EventHandler<string>? ConnectionStateChanged;
    public event EventHandler<Exception>? MonitoringError;
    public event EventHandler<string>? RouterRestartProgressChanged;
    public event EventHandler<MeshTopologyUpdate>? MeshTopologyUpdated;

    internal static bool ShouldPublishConnectionState(
        string? previousState,
        string currentState) =>
        !string.Equals(previousState, currentState, StringComparison.Ordinal);

    internal static bool ShouldRefreshInternetInfo(
        DateTimeOffset lastRequestedAt,
        DateTimeOffset now) =>
        lastRequestedAt == default ||
        now < lastRequestedAt ||
        now - lastRequestedAt >= InternetInfoRefreshInterval;

    public async Task ConnectAndStartAsync(
        string routerAddress,
        string password,
        int refreshIntervalSeconds,
        CancellationToken cancellationToken = default)
    {
        await StopAsync().ConfigureAwait(false);
        await _meshRequestGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            lock (_meshContextGate)
            {
                ObjectDisposedException.ThrowIf(_isDisposed, this);
                // Stop drained every manual/background request. Configure and capture
                // of the replacement request context are serialized by this gate.
                _routerClient.Configure(routerAddress, password);
                RenewMeshRequestContextLocked();
                _lastMeshNodes = [];
                _lastMeshRequestedAt = default;
                _hasValidMeshTopology = false;
                MeshTopologyUpdated?.Invoke(this, new MeshTopologyUpdate([], true));
            }
        }
        finally { _meshRequestGate.Release(); }
        _lastInternetInfo = null;
        _lastConnectionState = null;
        PublishConnectionState("正在连接路由器…");
        await _routerClient.AuthenticateAsync(cancellationToken).ConfigureAwait(false);
        PublishConnectionState("已连接");

        var firstUpdate = await PollOnceAsync(cancellationToken).ConfigureAwait(false);
        Updated?.Invoke(this, firstUpdate);

        _refreshIntervalSeconds = Math.Clamp(refreshIntervalSeconds, 1, 60);
        StartLoop();
    }

    public async Task StopAsync()
    {
        CancelMeshRequests();
        var cancellation = _cancellation;
        var loopTask = _loopTask;
        var internetInfoLoopTask = _internetInfoLoopTask;
        var meshLoopTask = _meshLoopTask;
        _cancellation = null;
        _loopTask = null;
        _internetInfoLoopTask = null;
        _meshLoopTask = null;

        cancellation?.Cancel();
        var tasks = new[] { loopTask, internetInfoLoopTask, meshLoopTask }
            .Where(task => task is not null)
            .Select(task => task!)
            .ToArray();
        if (tasks.Length > 0)
        {
            try
            {
                await Task.WhenAll(tasks);
            }
            catch (OperationCanceledException)
            {
            }
        }

        cancellation?.Dispose();
        await DrainMeshRequestsAsync().ConfigureAwait(false);
    }

    public Task<RouterInternetInfo> GetInternetInfoAsync(
        CancellationToken cancellationToken = default) =>
        _routerClient.GetInternetInfoAsync(cancellationToken);

    public async Task<IReadOnlyList<MeshNodeInfo>> GetMeshNodesAsync(
        CancellationToken cancellationToken = default, bool forceRefresh = false)
    {
        long generation;
        CancellationTokenSource linked;
        lock (_meshContextGate)
        {
            generation = _meshGeneration;
            linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _disposeCancellation.Token, _meshRequestsCancellation.Token);
        }
        using var requestCancellation = linked;
        cancellationToken = linked.Token;
        var requestedAt = DateTimeOffset.UtcNow;
        await _meshRequestGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            lock (_meshContextGate)
            {
                EnsureCurrentMeshContextLocked(generation, cancellationToken);
                // A page refresh and the background loop share one request/cache
                // only within the same connection generation.
                if (_hasValidMeshTopology && (_lastMeshRequestedAt >= requestedAt ||
                    !forceRefresh && requestedAt - _lastMeshRequestedAt < MeshRefreshInterval)) return _lastMeshNodes;
            }
            try
            {
                var nodes = await _routerClient.GetMeshNodesAsync(cancellationToken).ConfigureAwait(false);
                lock (_meshContextGate)
                {
                    EnsureCurrentMeshContextLocked(generation, cancellationToken);
                    _lastMeshNodes = nodes;
                    _lastMeshRequestedAt = DateTimeOffset.UtcNow;
                    _hasValidMeshTopology = true;
                    MeshTopologyUpdated?.Invoke(this, new MeshTopologyUpdate(nodes, true));
                }
                return nodes;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch
            {
                lock (_meshContextGate)
                {
                    // An obsolete transport may throw a non-cancellation exception
                    // after cancellation. It cannot invalidate the new connection.
                    EnsureCurrentMeshContextLocked(generation, cancellationToken);
                    _hasValidMeshTopology = false;
                    MeshTopologyUpdated?.Invoke(this, new MeshTopologyUpdate(_lastMeshNodes, false));
                }
                throw;
            }
        }
        finally { _meshRequestGate.Release(); }
    }

    internal MeshTopologyUpdate CurrentMeshTopology
    { get { lock (_meshContextGate) return new(_lastMeshNodes, _hasValidMeshTopology); } }

    private void EnsureCurrentMeshContextLocked(long generation, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (generation != _meshGeneration || _isDisposed) throw new OperationCanceledException(cancellationToken);
    }

    private void CancelMeshRequests()
    {
        lock (_meshContextGate)
        {
            ++_meshGeneration;
            _meshRequestsCancellation.Cancel();
        }
    }

    private void RenewMeshRequestContextLocked()
    {
        _meshRequestsCancellation.Dispose();
        _meshRequestsCancellation = new();
        ++_meshGeneration;
    }

    private async Task DrainMeshRequestsAsync()
    {
        await _meshRequestGate.WaitAsync().ConfigureAwait(false);
        _meshRequestGate.Release();
    }

    public void UpdateRefreshInterval(int refreshIntervalSeconds)
    {
        var next = Math.Clamp(refreshIntervalSeconds, 1, 60);
        if (next == _refreshIntervalSeconds) return;
        _refreshIntervalSeconds = next;
        if (_loopTask is not null && !_loopTask.IsCompleted)
        {
            _ = RestartLoopAsync();
        }
    }

    public async Task RestartRouterAsync(CancellationToken cancellationToken = default)
    {
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _disposeCancellation.Token);
        cancellationToken = linkedCancellation.Token;
        await StopAsync();
        RouterRestartProgressChanged?.Invoke(this, "正在向路由器发送重启指令…");

        try
        {
            await _routerClient.RestartRouterAsync(cancellationToken);
            RouterRestartProgressChanged?.Invoke(this, "路由器正在重启，请保持应用开启…");

            // 给路由器留出真正进入重启阶段的时间，避免旧会话在关机前被误判为恢复。
            await Task.Delay(TimeSpan.FromSeconds(8), cancellationToken);
            var startedAt = DateTimeOffset.Now;
            Exception? lastError = null;
            while (DateTimeOffset.Now - startedAt < TimeSpan.FromMinutes(2))
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    await _routerClient.AuthenticateAsync(cancellationToken);
                    var internetInfo = await _routerClient.GetInternetInfoAsync(cancellationToken);
                    if (!internetInfo.IsConnected)
                    {
                        throw new RouterApiException("路由器已启动，正在等待外网连接。");
                    }

                    var update = await PollOnceAsync(cancellationToken);
                    Updated?.Invoke(this, update);
                    PublishConnectionState("已连接");
                    RouterRestartProgressChanged?.Invoke(this, "路由器已重启并恢复连接");
                    StartLoop();
                    return;
                }
                catch (Exception exception) when (
                    exception is RouterApiException or HttpRequestException or TaskCanceledException)
                {
                    lastError = exception;
                }

                var elapsed = (int)(DateTimeOffset.Now - startedAt).TotalSeconds;
                RouterRestartProgressChanged?.Invoke(
                    this,
                    $"路由器正在重启，已等待 {elapsed} 秒…");
                await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);
            }

            throw new RouterApiException(
                "路由器重启等待超时，请稍后检查网络连接。",
                innerException: lastError);
        }
        catch
        {
            if (!_isDisposed)
            {
                StartLoop();
            }

            throw;
        }
    }

    private void StartLoop()
    {
        if (_isDisposed)
        {
            return;
        }

        lock (_meshContextGate)
        {
            // Refresh-interval changes and router restart use Stop/Start without
            // Configure; reopen the cancelled context after Stop has drained it.
            if (_meshRequestsCancellation.IsCancellationRequested) RenewMeshRequestContextLocked();
        }
        _cancellation?.Cancel();
        _cancellation?.Dispose();
        _cancellation = new CancellationTokenSource();
        _loopTask = RunLoopAsync(_refreshIntervalSeconds, _cancellation.Token);
        _internetInfoLoopTask = RunInternetInfoLoopAsync(_cancellation.Token);
        _meshLoopTask = RunMeshLoopAsync(_cancellation.Token);
    }

    private async Task RunMeshLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try { await GetMeshNodesAsync(cancellationToken, forceRefresh: true).ConfigureAwait(false); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { break; }
            catch { /* Unsupported/temporarily missing topology cannot fail device monitoring. */ }
            await Task.Delay(MeshRefreshInterval, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task RunLoopAsync(int refreshIntervalSeconds, CancellationToken cancellationToken)
    {
        var loop = new MonitoringLoop(
            TimeSpan.FromSeconds(Math.Max(1, refreshIntervalSeconds)),
            PollAndPublishAsync);
        await loop.RunAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task PollAndPublishAsync(CancellationToken cancellationToken)
    {
        try
        {
            var update = await PollOnceAsync(cancellationToken).ConfigureAwait(false);
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            Updated?.Invoke(this, update);
            PublishConnectionState("已连接");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            PublishConnectionState("连接异常，正在重试");
            // 失败不是“真实零流量”。UI 会保留最近有效采样，待下一次
            // 成功轮询后继续推进固定 1 秒时间槽。
            MonitoringError?.Invoke(this, exception);
        }
    }

    private async Task<MonitorUpdate> PollOnceAsync(CancellationToken cancellationToken)
    {
        var snapshotsTask = _routerClient.GetDeviceSnapshotsAsync(cancellationToken);
        var snapshots = await snapshotsTask.ConfigureAwait(false);

        var update = await _repository.ApplyPollAsync(
            snapshots,
            DateTimeOffset.Now,
            cancellationToken).ConfigureAwait(false);

        return update with { InternetInfo = _lastInternetInfo };
    }

    private async Task RunInternetInfoLoopAsync(CancellationToken cancellationToken)
    {
        var lastRequestedAt = default(DateTimeOffset);
        while (true)
        {
            if (ShouldRefreshInternetInfo(lastRequestedAt, DateTimeOffset.UtcNow))
            {
                lastRequestedAt = DateTimeOffset.UtcNow;
                try
                {
                    var internetInfo = await TryGetInternetInfoAsync(cancellationToken)
                        .ConfigureAwait(false);
                    if (internetInfo is not null)
                    {
                        _lastInternetInfo = internetInfo;
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception exception)
                {
                    MonitoringError?.Invoke(this, exception);
                }
            }

            await Task.Delay(InternetInfoRefreshInterval, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private void PublishConnectionState(string state)
    {
        if (!ShouldPublishConnectionState(_lastConnectionState, state))
        {
            return;
        }

        _lastConnectionState = state;
        ConnectionStateChanged?.Invoke(this, state);
    }

    private async Task<RouterInternetInfo?> TryGetInternetInfoAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            return await _routerClient.GetInternetInfoAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (
            !cancellationToken.IsCancellationRequested &&
            exception is RouterApiException or HttpRequestException or TaskCanceledException)
        {
            return null;
        }
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _disposeCancellation.Cancel();
        CancelMeshRequests();
        var cancellation = _cancellation;
        var loopTask = _loopTask;
        var internetInfoLoopTask = _internetInfoLoopTask;
        var meshLoopTask = _meshLoopTask;
        _cancellation = null;
        _loopTask = null;
        _internetInfoLoopTask = null;
        _meshLoopTask = null;
        cancellation?.Cancel();

        var tasks = new[] { loopTask, internetInfoLoopTask, meshLoopTask, DrainMeshRequestsAsync() }
            .Where(task => task is not null)
            .Select(task => task!)
            .ToArray();
        if (tasks.Length == 0 || tasks.All(task => task.IsCompleted))
        {
            cancellation?.Dispose();
            _routerClient.Dispose();
            return;
        }

        // Dispose 可能由窗口关闭事件同步调用。不要在 UI 线程阻塞等待
        // 正在取消的 HTTP 请求；等轮询任务退出后再释放 HttpClient/CTS。
        _ = FinishDisposeAsync(Task.WhenAll(tasks), cancellation);
    }

    private async Task RestartLoopAsync()
    {
        await _loopRestartGate.WaitAsync();
        try
        {
            if (_isDisposed)
            {
                return;
            }

            await StopAsync();
            if (!_isDisposed)
            {
                StartLoop();
            }
        }
        finally
        {
            _loopRestartGate.Release();
        }
    }

    private async Task FinishDisposeAsync(
        Task loopTask,
        CancellationTokenSource? cancellation)
    {
        try
        {
            await loopTask;
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
            // 应用正在退出；轮询异常不应阻止资源释放。
        }
        finally
        {
            cancellation?.Dispose();
            _routerClient.Dispose();
        }
    }
}
