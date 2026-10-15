using LanDeviceMonitor.Models;

namespace LanDeviceMonitor.Services;

public sealed class TrafficHistoryBuffer
{
    public const int WindowMinutes = 10;

    public const int SampleIntervalSeconds = 1;

    private const int MaximumSamples = WindowMinutes * 60 + 1;
    private readonly LinkedList<TrafficSample> _samples = [];

    public void Append(TrafficSample sample)
    {
        var normalized = Normalize(sample);
        if (_samples.Last is null)
        {
            _samples.AddLast(normalized);
            Trim(normalized.Timestamp);
            return;
        }

        var last = _samples.Last.Value;
        if (normalized.Timestamp < last.Timestamp)
        {
            return;
        }

        if (normalized.Timestamp == last.Timestamp)
        {
            // 同一秒的后一份结果就是这一秒最后收到的真实采样；不能为了
            // 平滑而把 0 或缺失方向替换成前一份旧值。
            _samples.Last.Value = normalized;
            return;
        }

        _samples.AddLast(normalized);
        Trim(normalized.Timestamp);
    }

    public IReadOnlyList<TrafficSample> Snapshot(DateTimeOffset now)
    {
        Trim(NormalizeTimestamp(now));
        return _samples.ToArray();
    }

    public bool HasSamples => _samples.Count > 0;

    private static TrafficSample Normalize(TrafficSample sample) =>
        new(
            NormalizeTimestamp(sample.Timestamp),
            Math.Max(0, sample.UploadBytesPerSecond),
            Math.Max(0, sample.DownloadBytesPerSecond))
        {
            IsPlaceholder = sample.IsPlaceholder,
            HasUploadSample = sample.HasUploadSample,
            HasDownloadSample = sample.HasDownloadSample
        };

    private static DateTimeOffset NormalizeTimestamp(DateTimeOffset timestamp) =>
        timestamp.AddTicks(-(timestamp.Ticks % TimeSpan.TicksPerSecond));

    private void Trim(DateTimeOffset now)
    {
        var cutoff = now - TimeSpan.FromMinutes(WindowMinutes);
        while (_samples.First is { Value.Timestamp: var timestamp } &&
               timestamp < cutoff)
        {
            _samples.RemoveFirst();
        }

        while (_samples.Count > MaximumSamples)
        {
            _samples.RemoveFirst();
        }
    }
}
