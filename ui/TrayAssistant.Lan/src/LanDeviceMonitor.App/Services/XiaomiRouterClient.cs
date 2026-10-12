using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using LanDeviceMonitor.Models;

namespace LanDeviceMonitor.Services;

public sealed partial class XiaomiRouterClient : IDisposable
{
    private static readonly TimeSpan StatusEnhancementTimeout =
        TimeSpan.FromMilliseconds(300);
    private readonly HttpClient _httpClient;
    private Uri _routerBaseUri = new("http://192.168.31.1/");
    private string? _token;
    private string? _password;

    public XiaomiRouterClient()
    {
        _httpClient = new HttpClient(new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            UseCookies = false,
            // 路由器位于局域网，必须绕过系统/环境代理，避免本地代理软件造成循环或超时。
            UseProxy = false
        })
        {
            Timeout = TimeSpan.FromSeconds(8)
        };

        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
            "LanDeviceMonitor/0.1 WindowsDesktop");
    }

    internal XiaomiRouterClient(HttpClient httpClient) => _httpClient = httpClient;

    public void Configure(string routerAddress, string password)
    {
        if (string.IsNullOrWhiteSpace(routerAddress))
        {
            throw new ArgumentException("路由器地址不能为空。", nameof(routerAddress));
        }

        var normalized = routerAddress.Trim();
        if (!normalized.Contains("://", StringComparison.Ordinal))
        {
            normalized = $"http://{normalized}";
        }

        if (!Uri.TryCreate(normalized, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException("路由器地址格式不正确。", nameof(routerAddress));
        }

        _routerBaseUri = new Uri($"{uri.Scheme}://{uri.Authority}/");
        _password = password;
        _token = null;
    }

    public async Task AuthenticateAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_password))
        {
            throw new RouterApiException("请先输入路由器管理员密码。");
        }

        string loginPage;
        try
        {
            loginPage = await _httpClient.GetStringAsync(
                new Uri(_routerBaseUri, "cgi-bin/luci/web"),
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            throw new RouterApiException("无法连接路由器，请检查地址和网络连接。", innerException: exception);
        }

        var key = LoginKeyRegex().Match(loginPage).Groups[1].Value;
        var deviceId = DeviceIdRegex().Match(loginPage).Groups[1].Value;
        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(deviceId))
        {
            throw new RouterApiException("路由器登录页格式不受支持，未找到认证参数。");
        }

        var encryptMode = await GetEncryptModeAsync(cancellationToken);
        var nonce = string.Create(
            CultureInfo.InvariantCulture,
            $"0_{deviceId}_{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}_{RandomNumberGenerator.GetInt32(0, 10000)}");
        var passwordHash = encryptMode == 1
            ? HashSha256(nonce + HashSha256(_password + key))
            : HashSha1(nonce + HashSha1(_password + key));

        using var content = new FormUrlEncodedContent(
        [
            new KeyValuePair<string, string>("username", "admin"),
            new KeyValuePair<string, string>("password", passwordHash),
            new KeyValuePair<string, string>("logtype", "2"),
            new KeyValuePair<string, string>("nonce", nonce)
        ]);

        using var response = await _httpClient.PostAsync(
            new Uri(_routerBaseUri, "cgi-bin/luci/api/xqsystem/login"),
            content,
            cancellationToken);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        using var document = JsonDocument.Parse(json);
        var code = GetInt(document.RootElement, "code");
        if (code != 0)
        {
            throw new RouterApiException(
                code is 401 or 403 ? "路由器管理员密码不正确。" : $"路由器登录失败（代码 {code}）。",
                code);
        }

        _token = GetString(document.RootElement, "token");
        if (string.IsNullOrWhiteSpace(_token))
        {
            throw new RouterApiException("路由器登录成功，但未返回访问令牌。");
        }
    }

    public async Task<IReadOnlyList<DeviceSnapshot>> GetDeviceSnapshotsAsync(
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_token))
        {
            await AuthenticateAsync(cancellationToken);
        }

        try
        {
            return await GetDeviceSnapshotsCoreAsync(cancellationToken);
        }
        catch (RouterApiException exception) when (exception.Code is 401 or 403)
        {
            _token = null;
            await AuthenticateAsync(cancellationToken);
            return await GetDeviceSnapshotsCoreAsync(cancellationToken);
        }
    }

    internal event EventHandler<TrafficSampleDiagnostics>? TrafficSampleObserved;

    public async Task<RouterInternetInfo> GetInternetInfoAsync(
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_token))
        {
            await AuthenticateAsync(cancellationToken);
        }

        try
        {
            return await GetInternetInfoCoreAsync(cancellationToken);
        }
        catch (RouterApiException exception) when (exception.Code is 401 or 403)
        {
            _token = null;
            await AuthenticateAsync(cancellationToken);
            return await GetInternetInfoCoreAsync(cancellationToken);
        }
    }

    public async Task<IReadOnlyList<MeshNodeInfo>> GetMeshNodesAsync(
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_token))
        {
            await AuthenticateAsync(cancellationToken);
        }

        try
        {
            return await GetMeshNodesCoreAsync(cancellationToken);
        }
        catch (RouterApiException exception) when (exception.Code is 401 or 403)
        {
            _token = null;
            await AuthenticateAsync(cancellationToken);
            return await GetMeshNodesCoreAsync(cancellationToken);
        }
    }

    public async Task RestartRouterAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_token))
        {
            await AuthenticateAsync(cancellationToken);
        }

        try
        {
            await RestartRouterCoreAsync(cancellationToken);
        }
        catch (RouterApiException exception) when (exception.Code is 401 or 403)
        {
            _token = null;
            await AuthenticateAsync(cancellationToken);
            await RestartRouterCoreAsync(cancellationToken);
        }

        _token = null;
    }

    private async Task<RouterInternetInfo> GetInternetInfoCoreAsync(
        CancellationToken cancellationToken)
    {
        using var document = await GetApiDocumentAsync(
            "api/xqnetwork/wan_info",
            cancellationToken);
        var root = document.RootElement;
        if (!root.TryGetProperty("info", out var info) ||
            info.ValueKind != JsonValueKind.Object)
        {
            throw new RouterApiException("路由器没有返回上网信息。");
        }

        var ipv4Address = string.Empty;
        var ipv4Mask = string.Empty;
        if (info.TryGetProperty("ipv4", out var ipv4) &&
            ipv4.ValueKind == JsonValueKind.Array &&
            ipv4.GetArrayLength() > 0)
        {
            ipv4Address = GetString(ipv4[0], "ip");
            ipv4Mask = GetString(ipv4[0], "mask");
        }

        info.TryGetProperty("ipv6_info", out var ipv6);
        var hasIpv6 = ipv6.ValueKind == JsonValueKind.Object;
        return new RouterInternetInfo(
            GetInt(info, "status") == 1 || GetInt(info, "link") == 1,
            FormatConnectionType(GetString(info, "details", "wanType")),
            EmptyAsDash(ipv4Address),
            EmptyAsDash(ipv4Mask),
            EmptyAsDash(GetString(info, "gateWay")),
            EmptyAsDash(GetString(info, "dnsAddrs")),
            hasIpv6
                ? FormatConnectionType(GetString(ipv6, "wanType"))
                : "未启用",
            hasIpv6 ? JoinArrayValues(ipv6, "ip6addr") : "—",
            hasIpv6 ? EmptyAsDash(GetString(ipv6, "ip6gw")) : "—",
            hasIpv6 ? JoinArrayValues(ipv6, "lan_ip6addr") : "—",
            hasIpv6 ? JoinArrayValues(ipv6, "lan_ip6prefix") : "—",
            hasIpv6 ? JoinArrayValues(ipv6, "dns") : "—");
    }

    private async Task<IReadOnlyList<MeshNodeInfo>> GetMeshNodesCoreAsync(
        CancellationToken cancellationToken)
    {
        using var document = await GetApiDocumentAsync(
            "api/misystem/topo_graph",
            cancellationToken);
        var root = document.RootElement;
        if (!root.TryGetProperty("graph", out var graph) || graph.ValueKind != JsonValueKind.Object)
            throw new RouterApiException("未获取到有效的 Mesh 拓扑。");
        var nodes = ParseMeshTopology(root);
        if (!nodes.Any(node => MeshMemberFilter.NormalizeMac(node.MacAddress) is not null || MeshMemberFilter.NormalizeIp(node.IpAddress) is not null))
            throw new RouterApiException("Mesh 拓扑缺少有效的节点身份。");
        return nodes;
    }

    internal static IReadOnlyList<MeshNodeInfo> ParseMeshTopology(JsonElement root)
    {
        if (!root.TryGetProperty("graph", out var graph) ||
            graph.ValueKind != JsonValueKind.Object)
        {
            return [];
        }

        var nodes = new List<MeshNodeInfo>();
        void Visit(JsonElement node,string id,string parent)
        {
            nodes.Add(ParseMeshNode(node,isGateway:parent.Length==0) with {NodeId=id,ParentNodeId=parent});
            if(node.TryGetProperty("leafs",out var children) && children.ValueKind==JsonValueKind.Array)
            {
                var index=0;
                foreach(var child in children.EnumerateArray())
                { if(child.ValueKind==JsonValueKind.Object) Visit(child,id+"/"+index,id);index++; }
            }
        }
        Visit(graph,"root",string.Empty);
        return nodes;
    }

    private static MeshNodeInfo ParseMeshNode(JsonElement node, bool isGateway)
    {
        var locale = GetString(node, "locale");
        var name = GetString(node, "name");
        var linkType = GetString(node, "link_type").ToLowerInvariant();
        return new MeshNodeInfo(
            string.IsNullOrWhiteSpace(locale)
                ? EmptyAsDash(name)
                : locale,
            EmptyAsDash(GetString(node, "ip")),
            EmptyAsDash(GetString(node, "hardware")),
            isGateway
                ? "主路由"
                : linkType switch
                {
                    "wired" => "有线回程",
                    "wireless" or "wifi" => "无线回程",
                    _ => "Mesh 节点"
                },
            Math.Max(0, GetInt(node, "onlines")),
            isGateway)
        {
            MacAddress = new[] { "mac", "macaddr", "mac_address" }
                .Select(key => GetString(node, key))
                .FirstOrDefault(value => MeshMemberFilter.NormalizeMac(value) is not null) ?? string.Empty
        };
    }

    private async Task RestartRouterCoreAsync(CancellationToken cancellationToken)
    {
        using var content = new FormUrlEncodedContent(
        [
            new KeyValuePair<string, string>("client", "web")
        ]);
        using var document = await PostApiDocumentAsync(
            "api/xqsystem/reboot",
            content,
            cancellationToken);
    }

    private async Task<IReadOnlyList<DeviceSnapshot>> GetDeviceSnapshotsCoreAsync(
        CancellationToken cancellationToken)
    {
        var sampleTimestamp = DateTimeOffset.Now;
        var deviceTask = GetApiDocumentAsync("api/misystem/devicelist?mlo=1", cancellationToken);
        using var statusCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        statusCancellation.CancelAfter(StatusEnhancementTimeout);
        var statusTask = GetApiDocumentAsync(
            "api/misystem/status",
            statusCancellation.Token);

        using var deviceDocument = await deviceTask;
        var statusByMac = new Dictionary<string, RouterStatusEntry>(
            StringComparer.OrdinalIgnoreCase);
        try
        {
            using var statusDocument = await statusTask;
            statusByMac = ParseStatus(statusDocument.RootElement);
        }
        catch (Exception exception) when (
            !cancellationToken.IsCancellationRequested &&
            exception is RouterApiException or HttpRequestException or TaskCanceledException)
        {
            // status 是增强数据；设备列表自身仍可提供设备、在线状态和部分速度。
            // status 不能因为路由器偶发的慢响应把每秒设备流量刷新拖成
            // 3～4 秒一批；超时后本轮直接使用 devicelist 的字段。
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // status 的增强请求有独立的短超时，不应阻塞实时设备采样。
        }

        var snapshots = new List<DeviceSnapshot>();

        if (!deviceDocument.RootElement.TryGetProperty("list", out var deviceList) ||
            deviceList.ValueKind != JsonValueKind.Array)
        {
            throw new RouterApiException("路由器设备接口返回了不完整的数据。");
        }

        foreach (var item in deviceList.EnumerateArray())
        {
            var mac = NormalizeMac(GetString(item, "mac"));
            if (string.IsNullOrWhiteSpace(mac))
            {
                continue;
            }

            var routerName = GetString(item, "name");
            var originalName = GetString(item, "oname");
            var deviceListOnline = GetInt(item, "online") == 1;
            var connectionType = GetInt(item, "type");
            var ipAddress = string.Empty;
            var onlineSeconds = GetNullableLong(item, "statistics", "online");
            var uploadSpeed = GetNullableLong(item, "statistics", "upspeed");
            var downloadSpeed = GetNullableLong(item, "statistics", "downspeed");
            var totalUpload = GetNullableLong(item, "statistics", "upload");
            var totalDownload = GetNullableLong(item, "statistics", "download");

            if (item.TryGetProperty("ip", out var ipList) &&
                ipList.ValueKind == JsonValueKind.Array &&
                ipList.GetArrayLength() > 0)
            {
                var firstIp = ipList[0];
                ipAddress = GetString(firstIp, "ip");
                onlineSeconds = MaxValid(onlineSeconds, GetNullableLong(firstIp, "online"));
                uploadSpeed = MaxValid(uploadSpeed, GetNullableLong(firstIp, "upspeed"));
                downloadSpeed = MaxValid(downloadSpeed, GetNullableLong(firstIp, "downspeed"));
                totalUpload = MaxValid(totalUpload, GetNullableLong(firstIp, "upload"));
                totalDownload = MaxValid(totalDownload, GetNullableLong(firstIp, "download"));
            }

            statusByMac.TryGetValue(mac, out var status);
            // 两个接口可能在同一轮返回不同步的快照。只要字段本身存在，0
            // 就是合法值；合并时取本轮有效值中的最大值，避免一个接口偶发
            // 的占位 0 制造曲线断崖，同时保留两个接口都为 0 的真实零流量。
            var mergedOnlineSeconds = MaxValid(onlineSeconds, status?.OnlineSeconds) ?? 0;
            var mergedUploadSpeed = MaxValid(uploadSpeed, status?.UploadSpeed) ?? 0;
            var mergedDownloadSpeed = MaxValid(downloadSpeed, status?.DownloadSpeed) ?? 0;
            var mergedTotalUpload = MaxValid(totalUpload, status?.TotalUpload) ?? 0;
            var mergedTotalDownload = MaxValid(totalDownload, status?.TotalDownload) ?? 0;
            var hasUploadSpeed = uploadSpeed.HasValue ||
                                 status?.UploadSpeed.HasValue == true;
            var hasDownloadSpeed = downloadSpeed.HasValue ||
                                   status?.DownloadSpeed.HasValue == true;
            var hasTotalUpload = totalUpload.HasValue ||
                                 status?.TotalUpload.HasValue == true;
            var hasTotalDownload = totalDownload.HasValue ||
                                   status?.TotalDownload.HasValue == true;
            var hasTrafficFields = uploadSpeed.HasValue ||
                                   downloadSpeed.HasValue ||
                                   totalUpload.HasValue ||
                                   totalDownload.HasValue ||
                                   status?.UploadSpeed.HasValue == true ||
                                   status?.DownloadSpeed.HasValue == true ||
                                   status?.TotalUpload.HasValue == true ||
                                   status?.TotalDownload.HasValue == true;
            var hasSpeedFields = uploadSpeed.HasValue ||
                                 downloadSpeed.HasValue ||
                                 status?.UploadSpeed.HasValue == true ||
                                 status?.DownloadSpeed.HasValue == true;
            var hasTotalsFields = totalUpload.HasValue ||
                                  totalDownload.HasValue ||
                                  status?.TotalUpload.HasValue == true ||
                                  status?.TotalDownload.HasValue == true;
            var hasValidTrafficData = hasTrafficFields;
            // 设备列表和 status 是同一轮的两个独立快照。只要任一接口仍
            // 确认在线，即认为本轮可用于刷新在线状态；真正离线还会由仓库
            // 做连续 15 秒确认，不能让 devicelist 的单帧 online=0 清零。
            var online = deviceListOnline || (status?.OnlineSeconds ?? 0) > 0;
            TrafficSampleObserved?.Invoke(
                this,
                new TrafficSampleDiagnostics(
                    sampleTimestamp,
                    mac,
                    uploadSpeed,
                    downloadSpeed,
                    totalUpload,
                    totalDownload,
                    status?.UploadSpeed,
                    status?.DownloadSpeed,
                    status?.TotalUpload,
                    status?.TotalDownload,
                    mergedUploadSpeed,
                    mergedDownloadSpeed,
                    mergedTotalUpload,
                    mergedTotalDownload));

            snapshots.Add(new DeviceSnapshot(
                mac,
                routerName,
                originalName,
                online,
                ipAddress,
                connectionType,
                mergedOnlineSeconds,
                mergedUploadSpeed,
                mergedDownloadSpeed,
                mergedTotalUpload,
                mergedTotalDownload,
                hasValidTrafficData,
                hasSpeedFields,
                hasTotalsFields,
                hasUploadSpeed,
                hasDownloadSpeed,
                hasTotalUpload,
                hasTotalDownload));
        }

        return snapshots;
    }

    private async Task<int> GetEncryptModeAsync(CancellationToken cancellationToken)
    {
        try
        {
            var json = await _httpClient.GetStringAsync(
                new Uri(_routerBaseUri, "cgi-bin/luci/api/xqsystem/init_info"),
                cancellationToken);
            using var document = JsonDocument.Parse(json);
            return GetInt(document.RootElement, "newEncryptMode");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) when (
            exception is HttpRequestException or JsonException or TaskCanceledException)
        {
            throw new RouterApiException("无法读取路由器认证模式。", innerException: exception);
        }
    }

    private async Task<JsonDocument> GetApiDocumentAsync(
        string relativePath,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_token))
        {
            throw new RouterApiException("路由器会话尚未建立。", 401);
        }

        var uri = new Uri(
            _routerBaseUri,
            $"cgi-bin/luci/;stok={Uri.EscapeDataString(_token)}/{relativePath}");

        string json;
        try
        {
            json = await _httpClient.GetStringAsync(uri, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            throw new RouterApiException("读取路由器设备数据失败。", innerException: exception);
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException exception)
        {
            throw new RouterApiException("路由器返回了无法识别的数据。", innerException: exception);
        }

        var code = GetInt(document.RootElement, "code");
        if (code != 0)
        {
            document.Dispose();
            throw new RouterApiException($"路由器接口返回错误（代码 {code}）。", code);
        }

        return document;
    }

    private async Task<JsonDocument> PostApiDocumentAsync(
        string relativePath,
        HttpContent content,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_token))
        {
            throw new RouterApiException("路由器会话尚未建立。", 401);
        }

        var uri = new Uri(
            _routerBaseUri,
            $"cgi-bin/luci/;stok={Uri.EscapeDataString(_token)}/{relativePath}");

        string json;
        try
        {
            using var response = await _httpClient.PostAsync(uri, content, cancellationToken);
            json = await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            throw new RouterApiException("无法向路由器发送操作请求。", innerException: exception);
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException exception)
        {
            throw new RouterApiException("路由器返回了无法识别的操作结果。", innerException: exception);
        }

        var code = GetInt(document.RootElement, "code");
        if (code != 0)
        {
            document.Dispose();
            throw new RouterApiException($"路由器操作失败（代码 {code}）。", code);
        }

        return document;
    }

    private static Dictionary<string, RouterStatusEntry> ParseStatus(JsonElement root)
    {
        var result = new Dictionary<string, RouterStatusEntry>(StringComparer.OrdinalIgnoreCase);
        if (!root.TryGetProperty("dev", out var devices) || devices.ValueKind != JsonValueKind.Array)
        {
            return result;
        }

        foreach (var device in devices.EnumerateArray())
        {
            var mac = NormalizeMac(GetString(device, "mac"));
            if (string.IsNullOrWhiteSpace(mac))
            {
                continue;
            }

            result[mac] = new RouterStatusEntry(
                GetNullableLong(device, "online"),
                GetNullableLong(device, "upspeed"),
                GetNullableLong(device, "downspeed"),
                GetNullableLong(device, "upload"),
                GetNullableLong(device, "download"));
        }

        return result;
    }

    private static string GetString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return string.Empty;
        }

        return property.ValueKind switch
        {
            JsonValueKind.String => property.GetString() ?? string.Empty,
            JsonValueKind.Number => property.GetRawText(),
            _ => string.Empty
        };
    }

    private static string GetString(
        JsonElement element,
        string objectName,
        string propertyName) =>
        element.TryGetProperty(objectName, out var nested) &&
        nested.ValueKind == JsonValueKind.Object
            ? GetString(nested, propertyName)
            : string.Empty;

    private static int GetInt(JsonElement element, string propertyName) =>
        int.TryParse(GetString(element, propertyName), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : 0;

    private static long GetLong(JsonElement element, string propertyName) =>
        long.TryParse(GetString(element, propertyName), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : 0;

    private static long? GetNullableLong(
        JsonElement element,
        string objectName,
        string propertyName) =>
        element.TryGetProperty(objectName, out var nested) &&
        nested.ValueKind == JsonValueKind.Object
            ? GetNullableLong(nested, propertyName)
            : null;

    private static long? GetNullableLong(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return long.TryParse(
            GetString(element, propertyName),
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var value)
            ? value
            : null;
    }

    private static long? MaxValid(long? first, long? second)
    {
        if (!first.HasValue)
        {
            return second.HasValue ? Math.Max(0, second.Value) : null;
        }

        if (!second.HasValue)
        {
            return Math.Max(0, first.Value);
        }

        return Math.Max(0, Math.Max(first.Value, second.Value));
    }

    private static long GetLong(JsonElement element, string objectName, string propertyName) =>
        element.TryGetProperty(objectName, out var nested) && nested.ValueKind == JsonValueKind.Object
            ? GetLong(nested, propertyName)
            : 0;

    private static string JoinArrayValues(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return "—";
        }

        var values = EnumerateStringValues(property)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToArray();
        return values.Length == 0 ? "—" : string.Join(Environment.NewLine, values);
    }

    private static IEnumerable<string> EnumerateStringValues(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                foreach (var value in EnumerateStringValues(item))
                {
                    yield return value;
                }
            }

            yield break;
        }

        if (element.ValueKind == JsonValueKind.String)
        {
            yield return element.GetString() ?? string.Empty;
        }
        else if (element.ValueKind == JsonValueKind.Number)
        {
            yield return element.GetRawText();
        }
    }

    private static string FormatConnectionType(string type) => type.ToLowerInvariant() switch
    {
        "dhcp" => "DHCP",
        "pppoe" => "PPPoE",
        "static" => "静态 IP",
        "native" => "Native",
        "nat6" => "NAT6",
        "" => "—",
        _ => type
    };

    private static string EmptyAsDash(string value) =>
        string.IsNullOrWhiteSpace(value) ? "—" : value;

    private static string NormalizeMac(string mac) => mac.Trim().ToUpperInvariant();

    private static string HashSha1(string value) => Convert.ToHexString(
        SHA1.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static string HashSha256(string value) => Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    public void Dispose()
    {
        _password = null;
        _token = null;
        _httpClient.Dispose();
    }

    private sealed record RouterStatusEntry(
        long? OnlineSeconds,
        long? UploadSpeed,
        long? DownloadSpeed,
        long? TotalUpload,
        long? TotalDownload);

    [GeneratedRegex(@"key:\s*'([^']+)'", RegexOptions.CultureInvariant)]
    private static partial Regex LoginKeyRegex();

    [GeneratedRegex(@"var\s+deviceId\s*=\s*'([^']+)'", RegexOptions.CultureInvariant)]
    private static partial Regex DeviceIdRegex();
}
