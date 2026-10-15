using System.Diagnostics;

namespace LanDeviceMonitor.Services;

internal sealed class MonitoringLoop
{
    private readonly TimeSpan _interval;
    private readonly Func<CancellationToken, Task> _iterationAsync;

    public MonitoringLoop(
        TimeSpan interval,
        Func<CancellationToken, Task> iterationAsync)
    {
        _interval = interval;
        _iterationAsync = iterationAsync;
    }

    internal static TimeSpan GetDelayAfterIteration(
        TimeSpan interval,
        TimeSpan iterationDuration)
    {
        interval = interval > TimeSpan.Zero
            ? interval
            : TimeSpan.FromSeconds(1);
        iterationDuration = iterationDuration > TimeSpan.Zero
            ? iterationDuration
            : TimeSpan.Zero;

        var remaining = interval - iterationDuration;
        // 请求未超过用户选择的周期时，只等待剩余时间，让“轮询开始”
        // 按周期推进；请求超过周期时不再额外等待一个完整周期，但保留
        // 100ms 最小退避，避免慢请求完成后形成无间隔重试压力。循环本身
        // 仍然等待当前请求完成，不会并发重叠请求。
        return remaining > TimeSpan.Zero
            ? remaining
            : TimeSpan.FromMilliseconds(100);
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var interval = _interval > TimeSpan.Zero
            ? _interval
            : TimeSpan.FromSeconds(1);

        // 保留首次连接后的一个完整刷新间隔，避免连接成功回调和首轮
        // 后台轮询在同一时刻叠加。
        await Task.Delay(interval, cancellationToken).ConfigureAwait(false);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var startedAt = Stopwatch.GetTimestamp();
            await _iterationAsync(cancellationToken).ConfigureAwait(false);

            var duration = TimeSpan.FromSeconds(
                Math.Max(0d, Stopwatch.GetTimestamp() - startedAt) /
                Stopwatch.Frequency);
            await Task.Delay(
                    GetDelayAfterIteration(interval, duration),
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }
}
