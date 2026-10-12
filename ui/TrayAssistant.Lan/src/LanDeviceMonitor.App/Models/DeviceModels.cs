using System.Text.Json.Serialization;

namespace LanDeviceMonitor.Models;

public sealed record DeviceSnapshot(
    string MacAddress,
    string RouterName,
    string OriginalName,
    bool IsOnline,
    string IpAddress,
    int ConnectionType,
    long OnlineDurationSeconds,
    long UploadSpeedBytes,
    long DownloadSpeedBytes,
    long TotalUploadBytes,
    long TotalDownloadBytes,
    bool HasValidTrafficData = true,
    bool HasValidSpeedData = true,
    bool HasValidTotalsData = true,
    bool HasValidUploadSpeedData = true,
    bool HasValidDownloadSpeedData = true,
    bool HasValidTotalUploadData = true,
    bool HasValidTotalDownloadData = true);

public sealed record TrafficSample(
    DateTimeOffset Timestamp,
    long UploadBytesPerSecond,
    long DownloadBytesPerSecond)
{
    /// <summary>
    /// 表示固定时间窗口中尚未采到数据的前置槽位。它不是路由器报告的
    /// 真实 0，绘图时必须保持为空，避免应用刚启动时出现一大片假归零。
    /// </summary>
    public bool IsPlaceholder { get; init; }

    // 上传和下载可能在不同轮次得到新数据；缺失方向不能用另一轮的旧值
    // 填充，否则图表会把路由器的慢速快照伪装成每秒采样。
    public bool HasUploadSample { get; init; } = true;
    public bool HasDownloadSample { get; init; } = true;

    public bool HasTraffic =>
        !IsPlaceholder &&
        (HasUploadSample && UploadBytesPerSecond != 0 ||
         HasDownloadSample && DownloadBytesPerSecond != 0);
}

/// <summary>
/// 单次路由器流量采样的原始值与合并值，仅用于诊断不同接口的返回语义。
/// </summary>
public sealed record TrafficSampleDiagnostics(
    DateTimeOffset Timestamp,
    string MacAddress,
    long? DeviceListUploadSpeed,
    long? DeviceListDownloadSpeed,
    long? DeviceListTotalUpload,
    long? DeviceListTotalDownload,
    long? StatusUploadSpeed,
    long? StatusDownloadSpeed,
    long? StatusTotalUpload,
    long? StatusTotalDownload,
    long UploadSpeed,
    long DownloadSpeed,
    long TotalUpload,
    long TotalDownload);

public sealed class DeviceProfile
{
    public string MacAddress { get; set; } = string.Empty;
    public string CustomName { get; set; } = string.Empty;
    public string CustomType { get; set; } = string.Empty;
    public string CustomIconGlyph { get; set; } = string.Empty;
    public bool NotifyWhenOnline { get; set; }
    public bool NotifyWhenOffline { get; set; }
    public bool HasObservedState { get; set; }
    public bool LastKnownOnline { get; set; }
    public bool IsRemovedFromList { get; set; }
    public DateTimeOffset FirstSeenAt { get; set; }
    public DateTimeOffset LastSeenAt { get; set; }

    // 最后一次由路由器明确确认在线的时间。设备列表偶发缺行或把 online
    // 临时写成 0 时，不能据此立即结束会话并把流量曲线压到 0。
    public DateTimeOffset LastOnlineSeenAt { get; set; }
    public string LastRouterName { get; set; } = string.Empty;
    public string LastOriginalName { get; set; } = string.Empty;
    public string LastIpAddress { get; set; } = string.Empty;
    public int LastConnectionType { get; set; }
    public long LastUploadSpeedBytes { get; set; }
    public long LastDownloadSpeedBytes { get; set; }
    public long LastTotalUploadBytes { get; set; }
    public long LastTotalDownloadBytes { get; set; }

    // 图表使用本轮真实的瞬时值，或在瞬时字段缺失时使用新累计差分得到的
    // 区间平均值。没有新证据的轮询不追加样本；运行时字段不写入 state.json。
    [JsonIgnore]
    internal long LastUploadGraphSpeedBytes;

    [JsonIgnore]
    internal long LastDownloadGraphSpeedBytes;

    // 仅表示本轮是否有可绘制的有效速率；累计值没有新快照时为 false，
    // 避免把未知秒绘成旧值。这些标志不持久化。
    [JsonIgnore]
    internal bool HasFreshUploadGraphSample;

    [JsonIgnore]
    internal bool HasFreshDownloadGraphSample;

    [JsonIgnore]
    internal DateTimeOffset LastUploadTotalChangedAt;

    [JsonIgnore]
    internal DateTimeOffset LastDownloadTotalChangedAt;

    // 单次路由器快照不足以确认设备已离线。该值只在当前进程内作为连续
    // 反向证据的起点；应用重启后会重新要求一个完整确认窗口。
    [JsonIgnore]
    internal DateTimeOffset OfflineEvidenceStartedAt;
}

public sealed class DeviceSession
{
    public string MacAddress { get; set; } = string.Empty;
    public DateTimeOffset OnlineAt { get; set; }
    public DateTimeOffset? OfflineAt { get; set; }
}

public sealed class AppSettings
{
    public bool LanAccessEnabled { get; set; }
    public string RouterAddress { get; set; } = "192.168.31.1";
    public int RefreshIntervalSeconds { get; set; } = 1;
    public bool CloseToTray { get; set; } = true;
    public bool StartWithWindows { get; set; }
    public string ThemeMode { get; set; } = "System";
    public bool DoNotDisturbEnabled { get; set; }
    public int DoNotDisturbStartMinutes { get; set; } = 22 * 60;
    public int DoNotDisturbEndMinutes { get; set; } = 8 * 60;
    public bool NotifyWhenNewDeviceOnline { get; set; }
    public bool NotifyWhenIpv6NotNative { get; set; }
}

public sealed class PersistedState
{
    public int SchemaVersion { get; set; } = 7;
    public AppSettings Settings { get; set; } = new();
    public Dictionary<string, DeviceProfile> Devices { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
    public List<DeviceSession> Sessions { get; set; } = [];
}

public sealed record DeviceTransition(
    string MacAddress,
    string DisplayName,
    bool IsOnline,
    DateTimeOffset OccurredAt,
    bool ShouldNotify)
{
    public bool IsNewDevice { get; init; }
}

public sealed record MonitorUpdate(
    IReadOnlyList<DeviceViewData> Devices,
    IReadOnlyList<DeviceTransition> Transitions,
    DateTimeOffset RefreshedAt,
    RouterInternetInfo? InternetInfo = null);

public sealed record RouterInternetInfo(
    bool IsConnected,
    string Ipv4ConnectionType,
    string Ipv4Address,
    string Ipv4SubnetMask,
    string Ipv4Gateway,
    string Ipv4Dns,
    string Ipv6ConnectionType,
    string WanIpv6Address,
    string WanIpv6Gateway,
    string LanIpv6Address,
    string LanIpv6Prefix,
    string Ipv6Dns);

public sealed record MeshNodeInfo(
    string DisplayName,
    string IpAddress,
    string Hardware,
    string ConnectionText,
    int ConnectedDeviceCount,
    bool IsGateway)
{
    public string NodeId { get; init; } = string.Empty;
    public string ParentNodeId { get; init; } = string.Empty;
    public string MacAddress { get; init; } = string.Empty;
}

public sealed record MeshTopologyUpdate(IReadOnlyList<MeshNodeInfo> Nodes, bool HasValidTopology);

public sealed record DeviceIdentity(string TypeName, string IconGlyph, string SuggestedName)
{
    public static string NormalizeTypeName(string value) => value == "空气检测仪" ? "监测设备" : value;
}

public sealed class RouterApiException : Exception
{
    public RouterApiException(string message, int? code = null, Exception? innerException = null)
        : base(message, innerException)
    {
        Code = code;
    }

    public int? Code { get; }
}

public sealed class DeviceViewData
{
    public required DeviceProfile Profile { get; init; }
    public required DeviceIdentity Identity { get; init; }
    public required IReadOnlyList<DeviceSession> Sessions { get; init; }

    [JsonIgnore]
    public string DisplayName =>
        !string.IsNullOrWhiteSpace(Profile.CustomName)
            ? Profile.CustomName
            : IsMeaningfulRouterName(Profile.LastRouterName, Profile.LastOriginalName)
                ? Profile.LastRouterName
                : !string.IsNullOrWhiteSpace(Identity.SuggestedName)
                    ? Identity.SuggestedName
                    : Profile.LastOriginalName;

    [JsonIgnore]
    public string TypeName =>
        DeviceIdentity.NormalizeTypeName(!string.IsNullOrWhiteSpace(Profile.CustomType) ? Profile.CustomType : Identity.TypeName);

    [JsonIgnore]
    public string IconGlyph =>
        !string.IsNullOrWhiteSpace(Profile.CustomIconGlyph)
            ? Profile.CustomIconGlyph
            : Identity.IconGlyph;

    private static bool IsMeaningfulRouterName(string routerName, string originalName)
    {
        if (string.IsNullOrWhiteSpace(routerName))
        {
            return false;
        }

        var compact = routerName.Replace(":", string.Empty).Replace("-", string.Empty);
        var looksLikeMac = compact.Length == 12 && compact.All(Uri.IsHexDigit);
        return !looksLikeMac &&
               !routerName.Equals(originalName, StringComparison.OrdinalIgnoreCase);
    }
}
