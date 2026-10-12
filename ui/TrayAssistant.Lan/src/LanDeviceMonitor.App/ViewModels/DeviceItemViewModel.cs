using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using LanDeviceMonitor.Models;

namespace LanDeviceMonitor.ViewModels;

public sealed class DeviceItemViewModel : INotifyPropertyChanged
{
    private DeviceViewData _data;
    private IReadOnlyList<SessionItemViewModel> _sessions;
    private IReadOnlyList<TrafficSample> _trafficSamples;
    private DateTimeOffset _displayTime;

    public DeviceItemViewModel(
        DeviceViewData data,
        IReadOnlyList<TrafficSample>? trafficSamples = null)
    {
        _data = data;
        _sessions = CreateSessionItems(data.Sessions);
        _trafficSamples = trafficSamples ?? [];
        _displayTime = DateTimeOffset.Now;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public DeviceViewData Data => _data;
    public DeviceProfile Profile => Data.Profile;
    public string MacAddress => Profile.MacAddress;
    public string DisplayName => System.Text.RegularExpressions.Regex.Replace(Data.DisplayName, @"(?<=[\u4e00-\u9fff]) +(?=[A-Za-z0-9])|(?<=[A-Za-z0-9]) +(?=[\u4e00-\u9fff])", "\u2009");
    public string TypeName => Data.TypeName;
    public string IconGlyph => Data.IconGlyph;
    public bool IsOnline => Profile.LastKnownOnline;
    public string StatusText => IsOnline ? "在线" : "离线";
    public DateTimeOffset? OnlineSince => IsOnline ?
        Data.Sessions.Where(session => session.OfflineAt is null && session.OnlineAt != default)
            .Select(session => (DateTimeOffset?)session.OnlineAt).Max() : null;
    public string OnlineElapsedText => FormatRelativeTime(OnlineSince, _displayTime);
    public string OnlineSinceText => OnlineSince is { } since
        ? $"上线于 {since:yyyy 年 MM 月 dd 日 HH:mm:ss}" : "尚无可确认的上线时间";
    public DateTimeOffset? OfflineSince => IsOnline ? null :
        Data.Sessions.Where(session => session.OfflineAt.HasValue)
            .Select(session => session.OfflineAt).Max() ??
        (Profile.LastOnlineSeenAt == default ? null : Profile.LastOnlineSeenAt);
    public string OfflineElapsedText => FormatRelativeTime(OfflineSince, _displayTime);
    public string OfflineSinceText => OfflineSince is { } since
        ? $"离线于 {since:yyyy 年 MM 月 dd 日 HH:mm:ss}" : "尚无可确认的离线时间";

    internal static string FormatRelativeTime(DateTimeOffset? since, DateTimeOffset now)
    {
        if (since is null) return "时间未知";
        var elapsed = now - since.Value;
        if (elapsed.TotalMinutes < 1) return "刚刚";
        if (elapsed.TotalHours < 1) return $"{(int)elapsed.TotalMinutes}\u2009分钟前";
        if (elapsed.TotalDays < 1) return $"{(int)elapsed.TotalHours}\u2009小时前";
        return $"{(int)elapsed.TotalDays}\u2009天前";
    }
    public string IpAddress => string.IsNullOrWhiteSpace(Profile.LastIpAddress) ? "—" : Profile.LastIpAddress;
    public string MacAddressText => MacAddress;
    public string ConnectionText => ConnectionTypeToText(Profile.LastConnectionType);
    public string UploadSpeedText => FormatSpeed(Profile.LastUploadSpeedBytes);
    public string DownloadSpeedText => FormatSpeed(Profile.LastDownloadSpeedBytes);
    public string UploadSpeedValueText => FormatSpeedValue(Profile.LastUploadSpeedBytes);
    public string UploadSpeedUnitText => FormatSpeedUnit(Profile.LastUploadSpeedBytes);
    public string DownloadSpeedValueText => FormatSpeedValue(Profile.LastDownloadSpeedBytes);
    public string DownloadSpeedUnitText => FormatSpeedUnit(Profile.LastDownloadSpeedBytes);
    public string TotalUploadText => FormatBytes(Profile.LastTotalUploadBytes);
    public string TotalDownloadText => FormatBytes(Profile.LastTotalDownloadBytes);
    public string TotalUploadValueText => FormatBytesValue(Profile.LastTotalUploadBytes);
    public string TotalUploadUnitText => FormatBytesUnit(Profile.LastTotalUploadBytes);
    public string TotalDownloadValueText => FormatBytesValue(Profile.LastTotalDownloadBytes);
    public string TotalDownloadUnitText => FormatBytesUnit(Profile.LastTotalDownloadBytes);
    public string TotalUploadDateRangeText => FormatDateRange(
        Profile.FirstSeenAt,
        Profile.LastSeenAt);
    public string TotalDownloadDateRangeText => FormatDateRange(
        Profile.FirstSeenAt,
        Profile.LastSeenAt);
    public string TotalUsageText => FormatBytes(
        Profile.LastTotalUploadBytes + Profile.LastTotalDownloadBytes);
    public string LastSeenText => Profile.LastSeenAt == default
        ? "尚无记录"
        : Profile.LastKnownOnline
            ? "当前在线"
            : $"{Profile.LastSeenAt:MM 月 dd 日 HH:mm} 最后在线";
    public IReadOnlyList<SessionItemViewModel> Sessions => _sessions;
    public IReadOnlyList<TrafficSample> TrafficSamples => _trafficSamples;

    internal void Update(
        DeviceViewData data,
        IReadOnlyList<TrafficSample>? trafficSamples = null)
    {
        if (!MacAddress.Equals(
                data.Profile.MacAddress,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "设备视图模型不能被更新为另一个 MAC 地址。");
        }

        var oldDisplayName = DisplayName;
        var oldTypeName = TypeName;
        var oldIconGlyph = IconGlyph;
        var oldIsOnline = IsOnline;
        var oldStatusText = StatusText;
        var oldOnlineElapsedText = OnlineElapsedText;
        var oldOnlineSinceText = OnlineSinceText;
        var oldOfflineElapsedText = OfflineElapsedText;
        var oldOfflineSinceText = OfflineSinceText;
        var oldIpAddress = IpAddress;
        var oldConnectionText = ConnectionText;
        var oldUploadSpeedText = UploadSpeedText;
        var oldDownloadSpeedText = DownloadSpeedText;
        var oldUploadSpeedValueText = UploadSpeedValueText;
        var oldUploadSpeedUnitText = UploadSpeedUnitText;
        var oldDownloadSpeedValueText = DownloadSpeedValueText;
        var oldDownloadSpeedUnitText = DownloadSpeedUnitText;
        var oldTotalUploadText = TotalUploadText;
        var oldTotalDownloadText = TotalDownloadText;
        var oldTotalUploadValueText = TotalUploadValueText;
        var oldTotalUploadUnitText = TotalUploadUnitText;
        var oldTotalDownloadValueText = TotalDownloadValueText;
        var oldTotalDownloadUnitText = TotalDownloadUnitText;
        var oldTotalUploadDateRangeText = TotalUploadDateRangeText;
        var oldTotalDownloadDateRangeText = TotalDownloadDateRangeText;
        var oldTotalUsageText = TotalUsageText;
        var oldLastSeenText = LastSeenText;
        var sessionsChanged = !AreSessionsEqual(Data.Sessions, data.Sessions);
        var trafficSamplesChanged = trafficSamples is not null &&
            !TrafficSamples.SequenceEqual(trafficSamples);

        _data = data;
        _displayTime = DateTimeOffset.Now;
        if (sessionsChanged)
        {
            _sessions = CreateSessionItems(data.Sessions);
        }

        if (trafficSamplesChanged)
        {
            _trafficSamples = trafficSamples!;
        }

        RaiseIfChanged(oldDisplayName, DisplayName, nameof(DisplayName));
        RaiseIfChanged(oldTypeName, TypeName, nameof(TypeName));
        RaiseIfChanged(oldIconGlyph, IconGlyph, nameof(IconGlyph));
        RaiseIfChanged(oldIsOnline, IsOnline, nameof(IsOnline));
        RaiseIfChanged(oldStatusText, StatusText, nameof(StatusText));
        RaiseIfChanged(oldOnlineElapsedText, OnlineElapsedText, nameof(OnlineElapsedText));
        RaiseIfChanged(oldOnlineSinceText, OnlineSinceText, nameof(OnlineSinceText));
        RaiseIfChanged(oldOfflineElapsedText, OfflineElapsedText, nameof(OfflineElapsedText));
        RaiseIfChanged(oldOfflineSinceText, OfflineSinceText, nameof(OfflineSinceText));
        RaiseIfChanged(oldIpAddress, IpAddress, nameof(IpAddress));
        RaiseIfChanged(oldConnectionText, ConnectionText, nameof(ConnectionText));
        RaiseIfChanged(oldUploadSpeedText, UploadSpeedText, nameof(UploadSpeedText));
        RaiseIfChanged(oldDownloadSpeedText, DownloadSpeedText, nameof(DownloadSpeedText));
        RaiseIfChanged(
            oldUploadSpeedValueText,
            UploadSpeedValueText,
            nameof(UploadSpeedValueText));
        RaiseIfChanged(
            oldUploadSpeedUnitText,
            UploadSpeedUnitText,
            nameof(UploadSpeedUnitText));
        RaiseIfChanged(
            oldDownloadSpeedValueText,
            DownloadSpeedValueText,
            nameof(DownloadSpeedValueText));
        RaiseIfChanged(
            oldDownloadSpeedUnitText,
            DownloadSpeedUnitText,
            nameof(DownloadSpeedUnitText));
        RaiseIfChanged(oldTotalUploadText, TotalUploadText, nameof(TotalUploadText));
        RaiseIfChanged(oldTotalDownloadText, TotalDownloadText, nameof(TotalDownloadText));
        RaiseIfChanged(
            oldTotalUploadValueText,
            TotalUploadValueText,
            nameof(TotalUploadValueText));
        RaiseIfChanged(
            oldTotalUploadUnitText,
            TotalUploadUnitText,
            nameof(TotalUploadUnitText));
        RaiseIfChanged(
            oldTotalDownloadValueText,
            TotalDownloadValueText,
            nameof(TotalDownloadValueText));
        RaiseIfChanged(
            oldTotalDownloadUnitText,
            TotalDownloadUnitText,
            nameof(TotalDownloadUnitText));
        RaiseIfChanged(
            oldTotalUploadDateRangeText,
            TotalUploadDateRangeText,
            nameof(TotalUploadDateRangeText));
        RaiseIfChanged(
            oldTotalDownloadDateRangeText,
            TotalDownloadDateRangeText,
            nameof(TotalDownloadDateRangeText));
        RaiseIfChanged(oldTotalUsageText, TotalUsageText, nameof(TotalUsageText));
        RaiseIfChanged(oldLastSeenText, LastSeenText, nameof(LastSeenText));

        if (sessionsChanged)
        {
            OnPropertyChanged(nameof(Sessions));
        }

        if (trafficSamplesChanged)
        {
            OnPropertyChanged(nameof(TrafficSamples));
        }
    }

    public static string FormatSpeed(long bytesPerSecond)
    {
        return $"{FormatSpeedValue(bytesPerSecond)}\u2009{FormatSpeedUnit(bytesPerSecond)}";
    }

    public static string FormatBytes(long bytes)
    {
        return $"{FormatBytesValue(bytes)}\u2009{FormatBytesUnit(bytes)}";
    }

    public static string FormatSpeedValue(long bytesPerSecond) =>
        FormatRateParts(
            bytesPerSecond,
            "/s",
            minimumExponent: 1,
            maximumDecimalPlaces: 2,
            zeroAsInteger: true).Value;

    public static string FormatSpeedUnit(long bytesPerSecond) =>
        FormatRateParts(
            bytesPerSecond,
            "/s",
            minimumExponent: 1,
            maximumDecimalPlaces: 2,
            zeroAsInteger: true).Unit;

    public static string FormatBytesValue(long bytes) =>
        FormatRateParts(bytes, string.Empty).Value;

    public static string FormatBytesUnit(long bytes) =>
        FormatRateParts(bytes, string.Empty).Unit;

    public static string FormatDateRange(
        DateTimeOffset start,
        DateTimeOffset end)
    {
        if (start == default && end == default)
        {
            return "暂无记录";
        }

        if (start == default)
        {
            start = end;
        }

        if (end == default)
        {
            end = start;
        }

        if (end < start)
        {
            (start, end) = (end, start);
        }

        return start.Date == end.Date
            ? $"{start:MM 月 dd 日}"
            : $"{start:MM 月 dd 日} ~ {end:MM 月 dd 日}";
    }

    private static IReadOnlyList<SessionItemViewModel> CreateSessionItems(
        IReadOnlyList<DeviceSession> sessions) =>
        sessions.Select(session => new SessionItemViewModel(session)).ToList();

    private static bool AreSessionsEqual(
        IReadOnlyList<DeviceSession> previous,
        IReadOnlyList<DeviceSession> current)
    {
        if (ReferenceEquals(previous, current))
        {
            return true;
        }

        if (previous.Count != current.Count)
        {
            return false;
        }

        for (var index = 0; index < previous.Count; index++)
        {
            if (previous[index].OnlineAt != current[index].OnlineAt ||
                previous[index].OfflineAt != current[index].OfflineAt)
            {
                return false;
            }
        }

        return true;
    }

    private void RaiseIfChanged<T>(T previous, T current, string propertyName)
    {
        if (!EqualityComparer<T>.Default.Equals(previous, current))
        {
            OnPropertyChanged(propertyName);
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private static (string Value, string Unit) FormatRateParts(
        long bytes,
        string suffix,
        int minimumExponent = 0,
        int maximumDecimalPlaces = 3,
        bool zeroAsInteger = false)
    {
        bytes = Math.Max(0, bytes);
        var units = new[] { "B", "KB", "MB", "GB", "TB", "PB", "EB", "ZB", "YB" };
        var exponent = bytes < 1
            ? 0
            : Math.Min(
                (int)Math.Floor(Math.Log2(bytes) / 10d),
                units.Length - 1);
        exponent = Math.Max(exponent, minimumExponent);
        var scaled = bytes / Math.Pow(1024d, exponent);
        var value = scaled >= 1000d
            ? scaled.ToString("0", CultureInfo.CurrentCulture)
            : FormatThreeSignificantDigits(
                scaled,
                maximumDecimalPlaces,
                zeroAsInteger);
        return (value, $"{units[Math.Max(0, exponent)]}{suffix}");
    }

    private static string FormatThreeSignificantDigits(
        double value,
        int maximumDecimalPlaces = 3,
        bool zeroAsInteger = false)
    {
        if (value == 0)
        {
            return zeroAsInteger ? "0" : "0.00";
        }

        var digitsBeforeDecimal = (int)Math.Floor(Math.Log10(Math.Abs(value))) + 1;
        var decimalPlaces = Math.Min(
            maximumDecimalPlaces,
            Math.Max(0, 3 - digitsBeforeDecimal));
        var roundedValue = Math.Round(
            value,
            decimalPlaces,
            MidpointRounding.AwayFromZero);
        if (zeroAsInteger && roundedValue == 0d)
        {
            return "0";
        }

        return roundedValue.ToString(
            $"F{decimalPlaces}",
            CultureInfo.CurrentCulture);
    }

    internal static string ConnectionTypeToText(int connectionType) => connectionType switch
    {
        0 => "网线接入",
        1 => "2.4GHz",
        2 => "5GHz",
        3 => "访客 Wi-Fi",
        4 => "Mesh 接入",
        5 => "5GHz-Game",
        _ => "Wi-Fi"
    };
}

public sealed class SessionItemViewModel : INotifyPropertyChanged
{
    private DateTimeOffset _onlineAt;
    private DateTimeOffset? _offlineAt;
    private bool _isVisible;
    private DateTime _calendarDate = DateTime.Today;

    public SessionItemViewModel()
    {
    }

    public SessionItemViewModel(DeviceSession session)
    {
        Update(session);
    }

    public DateTimeOffset OnlineAt => _onlineAt;
    public DateTimeOffset? OfflineAt => _offlineAt;
    public bool IsVisible => _isVisible;
    public System.Windows.Visibility ItemVisibility =>
        IsVisible
            ? System.Windows.Visibility.Visible
            : System.Windows.Visibility.Collapsed;
    public string OnlineDateText => FormatDate(OnlineAt, DateTime.Today);
    public string OnlineTimeText => OnlineAt.ToString("HH:mm");
    public string OfflineDateText =>
        OfflineAt is DateTimeOffset offlineAt
            ? FormatDate(offlineAt, DateTime.Today)
            : "当前在线";
    public string OfflineTimeText =>
        OfflineAt?.ToString("HH:mm") ?? string.Empty;
    internal static string FormatDate(DateTimeOffset value, DateTime today)
    {
        var days = (today.Date - value.ToLocalTime().Date).Days;
        return days switch { 0 => "今天", 1 => "昨天", 2 => "前天", _ => value.ToLocalTime().ToString("MM 月 dd 日") };
    }
    internal static bool ContainsRecentDate(DeviceSession session, DateTime today)
    {
        bool Recent(DateTimeOffset date) { var age=(today.Date-date.ToLocalTime().Date).Days;return age is >=0 and <=2; }
        return Recent(session.OnlineAt) || session.OfflineAt is {} offline && Recent(offline);
    }
    public string DurationText
    {
        get
        {
            var duration = (OfflineAt ?? DateTimeOffset.Now) - OnlineAt;
            if (duration.TotalDays >= 1)
            {
                return $"{(int)duration.TotalDays}\u2009天\u2009{duration.Hours}\u2009小时";
            }

            if (duration.TotalHours >= 1)
            {
                return $"{(int)duration.TotalHours}\u2009小时\u2009{duration.Minutes}\u2009分钟";
            }

            return $"{Math.Max(0, (int)duration.TotalMinutes)}\u2009分钟";
        }
    }

    internal void Update(DeviceSession? session)
    {
        if (_calendarDate != DateTime.Today)
        {
            _calendarDate = DateTime.Today;
            OnPropertyChanged(nameof(OnlineDateText));
            OnPropertyChanged(nameof(OfflineDateText));
        }
        var nextOnlineAt = session?.OnlineAt ?? default;
        var nextOfflineAt = session?.OfflineAt;
        var nextIsVisible = session is not null;
        var onlineChanged = _onlineAt != nextOnlineAt;
        var offlineChanged = _offlineAt != nextOfflineAt;
        var visibilityChanged = _isVisible != nextIsVisible;
        _onlineAt = nextOnlineAt;
        _offlineAt = nextOfflineAt;
        _isVisible = nextIsVisible;
        if (onlineChanged)
        {
            OnPropertyChanged(nameof(OnlineAt));
            OnPropertyChanged(nameof(OnlineDateText));
            OnPropertyChanged(nameof(OnlineTimeText));
            OnPropertyChanged(nameof(DurationText));
        }

        if (offlineChanged)
        {
            OnPropertyChanged(nameof(OfflineAt));
            OnPropertyChanged(nameof(OfflineDateText));
            OnPropertyChanged(nameof(OfflineTimeText));
            OnPropertyChanged(nameof(DurationText));
        }

        if (visibilityChanged)
        {
            OnPropertyChanged(nameof(IsVisible));
            OnPropertyChanged(nameof(ItemVisibility));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed record DeviceTypeOption(string TypeName, string IconGlyph)
{
    public string DisplayText => $"{IconGlyph}  {TypeName}";
}
