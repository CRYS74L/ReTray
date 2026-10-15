using System.IO;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using LanDeviceMonitor.ViewModels;
using LanDeviceMonitor.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Connections;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LanDeviceMonitor.Services.WebAccess;

internal sealed class LanWebServer : IAsyncDisposable
{
    private WebApplication? _app;
    private readonly object _dataGate = new();
    private byte[] _snapshot = "{\"devices\":[],\"refreshedAt\":null,\"connection\":\"等待监控数据\",\"mesh\":[]}"u8.ToArray();
    private readonly Dictionary<string, TrafficHistoryBuffer> _history = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, WebDeviceSnapshot> _devices = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (IReadOnlyList<DeviceSession> Source, DeviceSession[] Sorted)> _sessions = new(StringComparer.OrdinalIgnoreCase);
    private object _preferences = new { refreshIntervalSeconds = 1 };
    private bool _hasRouterPassword;
    private readonly Func<string, Task<IReadOnlyList<DeviceSession>>>? _sessionReader;
    public LanWebServer(Func<string, Task<IReadOnlyList<DeviceSession>>>? sessionReader = null, MeshMemberFilter? meshMemberFilter = null)
    {
        _sessionReader = sessionReader;
        _meshMemberFilter = meshMemberFilter ?? new();
    }
    private IReadOnlyList<MeshNodeInfo> _mesh = [];
    private DateTimeOffset? _meshUpdatedAt;
    private readonly MeshMemberFilter _meshMemberFilter;
    private IReadOnlyList<DeviceViewData> _latestDeviceData = [];
    private string _connection = "等待监控数据";
    public bool IsRunning => _app is not null;
    public string[] Urls { get; private set; } = [];
    public void SeedHistory(IReadOnlyDictionary<string, IReadOnlyList<TrafficSample>> histories)
    {
        lock (_dataGate)
        {
            foreach (var (id, samples) in histories)
            {
                var current = _history.TryGetValue(id, out var buffer) ? buffer.Snapshot(DateTimeOffset.Now) : [];
                var merged = new TrafficHistoryBuffer();
                // A monitor update may arrive while the UI supplies its retained
                // ten-minute buffer. Merge chronologically and prefer newer evidence.
                foreach (var sample in samples.Concat(current).OrderBy(p => p.Timestamp)) merged.Append(sample);
                _history[id] = merged;
            }
        }
    }
    public void SetPreferences(AppSettings settings, bool? hasRouterPassword = null)
    {
        lock (_dataGate)
        {
            // Presence is supplied by the desktop's existing credential-status
            // cache. The web server never reads or accepts a password value.
            if (hasRouterPassword.HasValue) _hasRouterPassword = hasRouterPassword.Value;
            _preferences = new {
                refreshIntervalSeconds = settings.RefreshIntervalSeconds,
                routerAddress = settings.RouterAddress,
                desktopTheme = settings.ThemeMode, startWithWindows = settings.StartWithWindows,
                desktopThemeResolved = ThemeService.ResolveIsDark(settings.ThemeMode) ? "Dark" : "Light",
                closeToTray = settings.CloseToTray, doNotDisturb = settings.DoNotDisturbEnabled,
                quietStart = NotificationSchedule.FormatTime(settings.DoNotDisturbStartMinutes),
                quietEnd = NotificationSchedule.FormatTime(settings.DoNotDisturbEndMinutes),
                ipv6Notification = settings.NotifyWhenIpv6NotNative,
                newDeviceNotification = settings.NotifyWhenNewDeviceOnline,
                lanAccessEnabled = settings.LanAccessEnabled,
                hasRouterPassword = _hasRouterPassword,
                version = MainWindow.GetDisplayVersion()
            };
        }
    }
    public void SetConnection(string state)
    {
        lock (_dataGate)
        {
            _connection = state;
            var data = JsonNode.Parse(_snapshot)!; data["connection"] = state;
            _snapshot = JsonSerializer.SerializeToUtf8Bytes(data);
        }
    }
    public void SetMesh(IReadOnlyList<MeshNodeInfo> mesh, bool hasValidTopology = true)
    {
        lock (_dataGate)
        {
            if (hasValidTopology)
            {
                _mesh = mesh;
                _meshUpdatedAt = DateTimeOffset.Now;
                _meshMemberFilter.UpdateTopology(mesh);
            }
            else _meshMemberFilter.InvalidateIpEvidence();
            var data = JsonNode.Parse(_snapshot)!;
            data["devices"] = JsonSerializer.SerializeToNode(_meshMemberFilter.Filter(_latestDeviceData).Select(d => _devices[d.Profile.MacAddress]), JsonOptions);
            data["mesh"] = JsonSerializer.SerializeToNode(_mesh, JsonOptions);
            data["meshUpdatedAt"] = JsonSerializer.SerializeToNode(_meshUpdatedAt, JsonOptions);
            _snapshot = JsonSerializer.SerializeToUtf8Bytes(data, JsonOptions);
        }
    }
    public void Publish(MonitorUpdate update)
    {
        lock (_dataGate)
        {
            _latestDeviceData = update.Devices;
            // Enabling web access after monitoring is already connected does not
            // trigger MonitoringService's deduplicated connection-state event.
            if (_connection == "等待监控数据" && update.RefreshedAt > DateTimeOffset.MinValue)
                _connection = "已连接";
            var devices = update.Devices.Select(d =>
            {
                var p = d.Profile;
                if (!_history.TryGetValue(p.MacAddress, out var history)) _history[p.MacAddress] = history = new();
                if (update.RefreshedAt > DateTimeOffset.MinValue && (p.HasFreshUploadGraphSample || p.HasFreshDownloadGraphSample))
                    history.Append(new TrafficSample(update.RefreshedAt, MainWindow.GetGraphSpeed(p.LastUploadGraphSpeedBytes, p.LastUploadSpeedBytes), MainWindow.GetGraphSpeed(p.LastDownloadGraphSpeedBytes, p.LastDownloadSpeedBytes)) {
                        HasUploadSample = p.HasFreshUploadGraphSample, HasDownloadSample = p.HasFreshDownloadGraphSample });
                if (!_sessions.TryGetValue(p.MacAddress, out var cached) || !ReferenceEquals(cached.Source, d.Sessions))
                    _sessions[p.MacAddress] = (d.Sessions, d.Sessions.OrderByDescending(s => s.OnlineAt).Select(s => new DeviceSession { MacAddress = s.MacAddress, OnlineAt = s.OnlineAt, OfflineAt = s.OfflineAt }).ToArray());
                var device = WebDeviceSnapshot.From(d);
                _devices[p.MacAddress] = device;
                return device;            }).ToArray();
            var ids = devices.Select(d => d.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var id in _history.Keys.Where(k => !ids.Contains(k)).ToArray()) { _history.Remove(id); _devices.Remove(id); _sessions.Remove(id); }
            var visibleIds = _meshMemberFilter.Filter(update.Devices).Select(d => d.Profile.MacAddress).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var visibleDevices = devices.Where(d => visibleIds.Contains(d.Id)).ToArray();
            _snapshot = JsonSerializer.SerializeToUtf8Bytes(new { devices = visibleDevices, refreshedAt = update.RefreshedAt, connection = _connection, internet = update.InternetInfo, mesh = _mesh, meshUpdatedAt = _meshUpdatedAt, preferences = _preferences }, JsonOptions);
        }
    }
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    public async Task StartAsync(int preferredPort = 18731, bool loopbackOnly = false)
    {
        await StopAsync();
        var addresses = loopbackOnly ? [IPAddress.Loopback] : NetworkPolicy.Addresses();
        for (var attempt = 0; attempt < 30; attempt++)
        {
            var port = preferredPort == 0 ? 0 : 1024 + (preferredPort - 1024 + attempt) % (65536 - 1024);
            if (port == 0 && !loopbackOnly) throw new ArgumentException("动态端口仅用于本机测试。");
            var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { Args = [], ContentRootPath = AppContext.BaseDirectory });
            builder.Logging.ClearProviders();
            builder.WebHost.ConfigureKestrel(k =>
            {
                k.Limits.MaxRequestBodySize = 1024;
                k.Limits.MaxConcurrentConnections = 64;
                foreach (var ip in addresses) k.Listen(ip, port);
            });
            var app = builder.Build();
            app.Use(async (context, next) =>
            {
                context.Response.Headers.CacheControl = "no-store";
                context.Response.Headers.XContentTypeOptions = "nosniff";
                context.Response.Headers["Referrer-Policy"] = "no-referrer";
                context.Response.Headers["Content-Security-Policy"] = "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; connect-src 'self'; frame-ancestors 'none'; base-uri 'none'; form-action 'self'";
                var remote = context.Connection.RemoteIpAddress;
                var host = context.Request.Host.Host;
                if (remote is null || !NetworkPolicy.IsPrivate(remote) ||
                    !(host == "localhost" || addresses.Any(ip => ip.ToString() == host)))
                { context.Response.StatusCode = 403; return; }
                var origin = context.Request.Headers.Origin.ToString();
                if (origin.Length != 0 && origin != "http://" + context.Request.Host)
                { context.Response.StatusCode = 403; return; }
                if (context.Request.Method is not ("GET" or "HEAD"))
                { context.Response.StatusCode = 405; return; }
                await next(context);
            });
            app.MapGet("/api/snapshot", (string? view) =>
            {
                lock (_dataGate)
                {
                    if (view == "overview") return Results.Bytes(_snapshot, "application/json");
                    // A home-screen page can remain alive across desktop upgrades.
                    // Preserve the v194 response for those already-loaded clients;
                    // the new page explicitly requests the lightweight overview.
                    var legacy = JsonNode.Parse(_snapshot)!;
                    foreach (var node in legacy["devices"]!.AsArray())
                    {
                        var id = node!["id"]!.GetValue<string>();
                        node["history"] = JsonSerializer.SerializeToNode(_history[id].Snapshot(DateTimeOffset.Now).TakeLast(300).Select(p => new { at = p.Timestamp, upload = p.HasUploadSample ? (long?)p.UploadBytesPerSecond : null, download = p.HasDownloadSample ? (long?)p.DownloadBytesPerSecond : null }), JsonOptions);
                        node["sessions"] = JsonSerializer.SerializeToNode(_sessions[id].Sorted.Take(100).Select(p => new { onlineAt = p.OnlineAt, offlineAt = p.OfflineAt }), JsonOptions);
                    }
                    return Results.Json(legacy, JsonOptions);
                }
            });
            app.MapGet("/api/device/{id}", GetDeviceAsync);
            var files = new Dictionary<string, string> { ["/"] = "index.html", ["/app.js"] = "app.js", ["/ui.js"] = "ui.js", ["/device-icons.js"] = "device-icons.js", ["/chart.js"] = "chart.js", ["/app.css"] = "app.css", ["/icon.png"] = "icons/network-v197-512.png", ["/apple-touch-icon.png"] = "icons/network-v197-180.png", ["/manifest.webmanifest"] = "manifest.webmanifest" };
            foreach (var size in new[] { 32, 152, 167, 180, 192, 512, 1024 }) files[$"/icons/network-v197-{size}.png"] = $"icons/network-v197-{size}.png";
            files["/icons/app-mark.png"] = "icons/app-mark.png";
            files["/desktop-tokens.css"] = "desktop-tokens.css";
            files["/ios-tokens.css"] = "ios-tokens.css";
            files["/precision.js"] = "precision.js";
            files["/motion.js"] = "motion.js";
            files["/edge-return.js"] = "edge-return.js";
            files["/font-ready.js"] = "font-ready.js";
            foreach (var size in new[] { 16, 32, 48, 64, 192, 512 }) files[$"/icons/browser-v197-{size}.png"] = $"icons/browser-v197-{size}.png";
            foreach (var weight in new[] { "light", "regular", "bold" })
            {
                var resource = $"LanDeviceMonitor.Web.Fonts.app-text-{weight}.woff2";
                app.MapGet($"/fonts/app-text-{weight}.woff2", () => Results.Stream(
                    typeof(LanWebServer).Assembly.GetManifestResourceStream(resource)
                        ?? throw new InvalidOperationException($"Missing embedded font: {resource}"), "font/woff2"));
            }
            app.MapGet("/fonts/app-icons.woff2", () => Results.Stream(
                typeof(LanWebServer).Assembly.GetManifestResourceStream("LanDeviceMonitor.Web.Fonts.app-icons.woff2")
                    ?? throw new InvalidOperationException("Missing embedded icon font: LanDeviceMonitor.Web.Fonts.app-icons.woff2"), "font/woff2"));
            files["/favicon.png"] = "icons/browser-v197-32.png";
            // Previously saved web clips may still request the old icon URLs.
            foreach (var size in new[] { 32, 152, 167, 180, 192, 512, 1024 }) files[$"/icons/network-v195-{size}.png"] = $"icons/network-v197-{size}.png";
            foreach (var file in files)
            {
                var name = file.Value;
                app.MapGet(file.Key, () => Results.File(Path.Combine(AppContext.BaseDirectory, "Web", name), name.EndsWith(".js") ? "text/javascript" : name.EndsWith(".css") ? "text/css" : name.EndsWith(".png") ? "image/png" : name.EndsWith(".webmanifest") ? "application/manifest+json" : "text/html; charset=utf-8"));
            }
            try
            {
                await app.StartAsync();
                Urls = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.ToArray();
                _app = app;
                return;
            }
            catch (IOException e) when (IsAddressInUse(e) && attempt < 29)
            { await app.DisposeAsync(); }
            catch { await app.DisposeAsync(); throw; }
        }
    }
    private async Task<IResult> GetDeviceAsync(string id, int? offset, string? before)
    {
        WebDeviceSnapshot device;
        IReadOnlyList<TrafficSample> history;
        IReadOnlyList<DeviceSession> sessions;
        var now = DateTimeOffset.Now;
        lock (_dataGate)
        {
            if (!_devices.TryGetValue(id, out var found)) return Results.NotFound();
            device = found;
            history = _history[id].Snapshot(now);
            sessions = _sessions[id].Sorted;
        }
        if (_sessionReader is not null) sessions = await _sessionReader(id);
        var pageOffset = Math.Max(0, offset ?? 0);
        var subset = sessions.Skip(pageOffset);
        if (before is not null)
        {
            if (!DateTimeOffset.TryParse(before, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out var cursor)) return Results.BadRequest();
            subset = sessions.Where(s => s.OnlineAt < cursor);
        }
        var page = subset.Take(31).ToArray();
        return Results.Json(new { device = device with { SessionCount = sessions.Count }, serverTime = now,
            history = history.Select(p => new { at = p.Timestamp, upload = p.HasUploadSample ? (long?)p.UploadBytesPerSecond : null, download = p.HasDownloadSample ? (long?)p.DownloadBytesPerSecond : null }),
            sessions = page.Take(30).Select(p => new { onlineAt = p.OnlineAt, offlineAt = p.OfflineAt }),
            totalSessions = sessions.Count, offset = pageOffset, hasMore = page.Length > 30,
            nextBefore = page.Length > 0 ? page[Math.Min(29, page.Length - 1)].OnlineAt.ToString("O") : null
        }, JsonOptions);
    }
    private static bool IsAddressInUse(Exception e) => e is AddressInUseException || e.InnerException is not null && IsAddressInUse(e.InnerException);
    public async Task StopAsync()
    {
        var app = _app; _app = null; Urls = [];
        if (app is not null)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try { await app.StopAsync(timeout.Token); } finally { await app.DisposeAsync(); }
        }
    }
    public async ValueTask DisposeAsync() => await StopAsync();
}
