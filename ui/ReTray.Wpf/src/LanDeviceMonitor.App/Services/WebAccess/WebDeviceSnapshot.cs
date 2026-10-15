using LanDeviceMonitor.Models;
using LanDeviceMonitor.ViewModels;

namespace LanDeviceMonitor.Services.WebAccess;

internal sealed record WebDeviceSnapshot(string Id, string Name, string Type, string IconType,
    bool Online, string Ip, string Mac, string Connection, long Upload, long Download,
    long TotalUpload, long TotalDownload, DateTimeOffset FirstSeen, DateTimeOffset LastSeen,
    bool NotifyOnline, bool NotifyOffline, string OriginalName, string RouterName,
    string CustomName, string CustomType, string RecordRange, DateTimeOffset? OnlineSince,
    int SessionCount, DateTimeOffset? OfflineSince, DateTimeOffset? StatusChangedAt,
    DateTimeOffset ListSortAt, string DisplayName)
{
    public static WebDeviceSnapshot From(DeviceViewData d)
    {
        var p = d.Profile;
        // Elapsed labels and sidebar order deliberately have different desktop
        // fallbacks. Unknown state-change times must stay unknown rather than
        // borrowing the profile's last refresh timestamp.
        var onlineSince = p.LastKnownOnline ? d.Sessions
            .Where(s => s.OfflineAt is null && s.OnlineAt != default)
            .Select(s => (DateTimeOffset?)s.OnlineAt).Max() : null;
        var latestOfflineAt = d.Sessions.Where(s => s.OfflineAt.HasValue)
            .Select(s => s.OfflineAt).Max();
        var offlineSince = p.LastKnownOnline ? null : latestOfflineAt ??
            (p.LastOnlineSeenAt == default ? (DateTimeOffset?)null : p.LastOnlineSeenAt);
        var listSortAt = p.LastKnownOnline
            ? d.Sessions.Select(s => (DateTimeOffset?)s.OnlineAt).Max() ?? p.FirstSeenAt
            : latestOfflineAt ?? p.LastSeenAt;
        return new(p.MacAddress, d.DisplayName, d.TypeName,
            (DeviceIdentityService.AvailableTypes.FirstOrDefault(t => t.IconGlyph == d.IconGlyph && t.TypeName == d.TypeName)
                ?? DeviceIdentityService.AvailableTypes.FirstOrDefault(t => t.IconGlyph == d.IconGlyph))?.TypeName ?? d.TypeName,
            p.LastKnownOnline, p.LastIpAddress, p.MacAddress, DeviceItemViewModel.ConnectionTypeToText(p.LastConnectionType),
            p.LastUploadSpeedBytes, p.LastDownloadSpeedBytes, p.LastTotalUploadBytes, p.LastTotalDownloadBytes,
            p.FirstSeenAt, p.LastSeenAt, p.NotifyWhenOnline, p.NotifyWhenOffline,
            p.LastOriginalName, p.LastRouterName, p.CustomName, DeviceIdentity.NormalizeTypeName(p.CustomType),
            DeviceItemViewModel.FormatDateRange(p.FirstSeenAt, p.LastSeenAt),
            onlineSince, d.Sessions.Count, offlineSince,
            p.LastKnownOnline ? onlineSince : offlineSince, listSortAt,
            System.Text.RegularExpressions.Regex.Replace(d.DisplayName,
                @"(?<=[\u4e00-\u9fff]) +(?=[A-Za-z0-9])|(?<=[A-Za-z0-9]) +(?=[\u4e00-\u9fff])", "\u2009"));
    }
}
