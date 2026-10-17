using System.IO;
using System.Text.Json;
using LanDeviceMonitor.Models;

namespace LanDeviceMonitor.Services;

public sealed class DeviceRepository
{
    // Xiaomi 的 devicelist 偶尔会在一次成功返回中遗漏已在线设备，或将
    // online 暂时写成 0。必须连续观察到反向证据一个确认窗口，才能把它
    // 当作真正离线；否则会产生用户截图中的单点垂直归零和误通知。
    private static readonly TimeSpan RouterOfflineConfirmationPeriod =
        TimeSpan.FromSeconds(3);

    // 瞬时速率和最后可见时间每秒都会变化，但这些运行时字段不需要每轮
    // 重写完整 state.json。持久化只在结构性变化或检查点写入，避免磁盘
    // 写入、JSON 序列化和临时文件替换在高负载时持续争抢系统资源。
    private static readonly TimeSpan PollStateCheckpointInterval =
        TimeSpan.FromMinutes(5);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _statePath;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private PersistedState _state = new();
    private int _newDeviceNotificationOverride = -1;
    private DateTimeOffset _lastPollStateSavedAt;
    private readonly Dictionary<string, IReadOnlyList<DeviceSession>> _sessionViewCache =
        new(StringComparer.OrdinalIgnoreCase);
    private int _sessionsVersion;
    private int _sessionViewCacheVersion = -1;
    private int _webSessionCacheVersion = -1;
    private readonly Dictionary<string, IReadOnlyList<DeviceSession>> _webSessionCache = new(StringComparer.OrdinalIgnoreCase);

    public DeviceRepository()
    {
        var dataDirectory = ApplicationPaths.DataDirectory;
        _statePath = Path.Combine(dataDirectory, "state.json");
    }

    internal DeviceRepository(string statePath)
    {
        _statePath = statePath;
    }

    public AppSettings Settings => _state.Settings;

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(_statePath))
            {
                return;
            }

            await using (var stream = File.OpenRead(_statePath))
            {
                _state = await JsonSerializer.DeserializeAsync<PersistedState>(
                    stream,
                    _jsonOptions,
                    cancellationToken) ?? new PersistedState();
            }

            ApplyNewDeviceNotificationOverride();

            _state.Devices = new Dictionary<string, DeviceProfile>(
                _state.Devices,
                StringComparer.OrdinalIgnoreCase);
            _sessionsVersion++;
            _state.Settings.ThemeMode = ThemeService.NormalizeMode(_state.Settings.ThemeMode);

            var stateChanged = false;
            foreach (var profile in _state.Devices.Values)
            {
                if (profile.CustomType == "其他设备" &&
                    profile.CustomIconGlyph == "\uE7F4")
                {
                    profile.CustomIconGlyph = "\uE772";
                    stateChanged = true;
                }

                if (profile.CustomType == "灯" &&
                    profile.CustomIconGlyph == "\uE781")
                {
                    profile.CustomIconGlyph = "\uEA80";
                    stateChanged = true;
                }

                if (_state.SchemaVersion < 3)
                {
                    var notifyWhenStatusChanges =
                        profile.NotifyWhenOnline ||
                        profile.NotifyWhenOffline;
                    profile.NotifyWhenOnline = notifyWhenStatusChanges;
                    profile.NotifyWhenOffline = notifyWhenStatusChanges;
                    stateChanged = true;
                }
            }

            if (!_state.Settings.CloseToTray)
            {
                _state.Settings.CloseToTray = true;
                stateChanged = true;
            }

            var startMinutes = Math.Clamp(
                _state.Settings.DoNotDisturbStartMinutes,
                0,
                1439);
            var endMinutes = Math.Clamp(
                _state.Settings.DoNotDisturbEndMinutes,
                0,
                1439);
            if (startMinutes != _state.Settings.DoNotDisturbStartMinutes ||
                endMinutes != _state.Settings.DoNotDisturbEndMinutes ||
                _state.SchemaVersion < 2)
            {
                _state.Settings.DoNotDisturbStartMinutes = startMinutes;
                _state.Settings.DoNotDisturbEndMinutes = endMinutes;
                _state.SchemaVersion = 2;
                stateChanged = true;
            }

            if (_state.SchemaVersion < 3)
            {
                _state.SchemaVersion = 3;
                stateChanged = true;
            }

            if (_state.SchemaVersion < 4)
            {
                _state.SchemaVersion = 4;
                stateChanged = true;
            }

            if (_state.SchemaVersion < 5)
            {
                _state.Settings.RefreshIntervalSeconds = 1;
                _state.SchemaVersion = 5;
                stateChanged = true;
            }
            else if (_state.Settings.RefreshIntervalSeconds is < 1 or > 60)
            {
                _state.Settings.RefreshIntervalSeconds = 1;
                stateChanged = true;
            }

            if (_state.SchemaVersion < 6)
            {
                foreach (var profile in _state.Devices.Values)
                {
                    // 旧 state.json 没有明确在线时间；保留已有的最后可见
                    // 时间作为历史基线，真正的离线确认仍从本进程的连续证据
                    // 开始，避免升级后第一轮错误快照立刻触发离线。
                    if (profile.LastKnownOnline && profile.LastOnlineSeenAt == default)
                    {
                        profile.LastOnlineSeenAt = profile.LastSeenAt;
                    }
                }

                _state.SchemaVersion = 6;
                stateChanged = true;
            }

            if (_state.SchemaVersion < 7) { _state.SchemaVersion = 7; stateChanged = true; }

            if (stateChanged)
            {
                await SaveCoreAsync(cancellationToken);
            }
        }
        catch (JsonException)
        {
            var backupPath = $"{_statePath}.corrupt-{DateTime.Now:yyyyMMdd-HHmmss}";
            File.Copy(_statePath, backupPath, overwrite: false);
            _state = new PersistedState();
            ApplyNewDeviceNotificationOverride();
        }
        finally
        {
            _gate.Release();
        }
    }

    private void ApplyNewDeviceNotificationOverride()
    {
        var notificationOverride = Volatile.Read(ref _newDeviceNotificationOverride);
        if (notificationOverride >= 0)
            _state.Settings.NotifyWhenNewDeviceOnline = notificationOverride == 1;
    }

    public async Task<MonitorUpdate> ApplyPollAsync(
        IReadOnlyList<DeviceSnapshot> snapshots,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var transitions = new List<DeviceTransition>();
            var requiresImmediatePollSave = false;
            foreach (var profile in _state.Devices.Values)
            {
                profile.HasFreshUploadGraphSample = false;
                profile.HasFreshDownloadGraphSample = false;
            }

            var reportedOnlineMacs = snapshots
                .Where(snapshot => snapshot.IsOnline)
                .Select(snapshot => snapshot.MacAddress)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var reportedMacs = snapshots
                .Select(snapshot => snapshot.MacAddress)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var snapshot in snapshots)
            {
                var isNewProfile = false;
                if (!_state.Devices.TryGetValue(snapshot.MacAddress, out var profile))
                {
                    isNewProfile = true;
                    profile = new DeviceProfile
                    {
                        MacAddress = snapshot.MacAddress,
                        FirstSeenAt = now,
                        LastSeenAt = now,
                        LastRouterName = snapshot.RouterName,
                        LastOriginalName = snapshot.OriginalName
                    };
                    _state.Devices[snapshot.MacAddress] = profile;
                    requiresImmediatePollSave = true;
                }

                var wasOnline = profile.HasObservedState && profile.LastKnownOnline;
                var previousUploadTotal = profile.LastTotalUploadBytes;
                var previousDownloadTotal = profile.LastTotalDownloadBytes;
                var previousUploadTotalChangedAt = profile.LastUploadTotalChangedAt;
                var previousDownloadTotalChangedAt = profile.LastDownloadTotalChangedAt;

                var identity = DeviceIdentityService.Identify(
                    snapshot.RouterName,
                    snapshot.OriginalName,
                    snapshot.ConnectionType);
                var displayName = GetDisplayName(profile, identity);

                // Check all retained records before adding this poll's first
                // session. Hidden profiles and session-only legacy records
                // are already known, irrespective of the current UI filter.
                if (isNewProfile && snapshot.IsOnline && _state.Settings.NotifyWhenNewDeviceOnline &&
                    !_state.Sessions.Any(session => session.MacAddress.Equals(snapshot.MacAddress, StringComparison.OrdinalIgnoreCase)))
                    transitions.Add(new DeviceTransition(snapshot.MacAddress, displayName, true, now, true)
                    { IsNewDevice = true });

                bool effectiveOnline;
                if (!profile.HasObservedState)
                {
                    profile.HasObservedState = true;
                    profile.LastKnownOnline = snapshot.IsOnline;
                    profile.OfflineEvidenceStartedAt = default;
                    effectiveOnline = snapshot.IsOnline;
                    if (snapshot.IsOnline)
                    {
                        var estimatedStart = now - TimeSpan.FromSeconds(
                            Math.Max(0, snapshot.OnlineDurationSeconds));
                        AddOpenSessionIfMissing(snapshot.MacAddress, estimatedStart);
                        profile.FirstSeenAt = estimatedStart;
                        profile.LastOnlineSeenAt = now;
                    }
                }
                else
                {
                    effectiveOnline = ResolveEffectiveOnlineState(
                        profile,
                        snapshot.IsOnline,
                        now,
                        displayName,
                        transitions);
                }

                if (!wasOnline && snapshot.IsOnline)
                {
                    // 新一轮上线时不把离线期间的累计增量误算为当前速度。
                    previousUploadTotalChangedAt = default;
                    previousDownloadTotalChangedAt = default;
                }

                profile.LastSeenAt = now;
                profile.LastRouterName = snapshot.RouterName;
                profile.LastOriginalName = snapshot.OriginalName;
                profile.LastIpAddress = snapshot.IpAddress;
                profile.LastConnectionType = snapshot.ConnectionType;

                // raw online=false 的这一帧没有资格改写流量数据，即便我们
                // 仍在状态确认窗口内把设备逻辑上保持在线。这样短暂的路由器
                // 误报既不会清零速度，也不会污染累计字节差分的时间基准。
                if (!effectiveOnline || !snapshot.IsOnline)
                {
                    continue;
                }

                if (snapshot.HasValidTotalUploadData)
                {
                    profile.LastTotalUploadBytes = ApplyOnlineTotalSample(
                        profile.LastTotalUploadBytes,
                        snapshot.TotalUploadBytes,
                        effectiveOnline);
                }

                if (snapshot.HasValidTotalDownloadData)
                {
                    profile.LastTotalDownloadBytes = ApplyOnlineTotalSample(
                        profile.LastTotalDownloadBytes,
                        snapshot.TotalDownloadBytes,
                        effectiveOnline);
                }

                var derivedUploadSpeed = CalculateDerivedSpeed(
                    previousUploadTotal,
                    profile.LastTotalUploadBytes,
                    previousUploadTotalChangedAt,
                    now,
                    effectiveOnline && snapshot.HasValidTotalUploadData,
                    out var lastUploadTotalChangedAt);
                var derivedDownloadSpeed = CalculateDerivedSpeed(
                    previousDownloadTotal,
                    profile.LastTotalDownloadBytes,
                    previousDownloadTotalChangedAt,
                    now,
                    effectiveOnline && snapshot.HasValidTotalDownloadData,
                    out var lastDownloadTotalChangedAt);
                profile.LastUploadTotalChangedAt = lastUploadTotalChangedAt;
                profile.LastDownloadTotalChangedAt = lastDownloadTotalChangedAt;

                var uploadSample = ApplyOnlineSpeedSample(
                    snapshot.HasValidUploadSpeedData ? snapshot.UploadSpeedBytes : 0,
                    snapshot.HasValidUploadSpeedData,
                    snapshot.HasValidTotalUploadData,
                    effectiveOnline,
                    derivedUploadSpeed);
                profile.LastUploadSpeedBytes = uploadSample.DisplaySpeed;
                profile.LastUploadGraphSpeedBytes = uploadSample.GraphSpeed;
                profile.HasFreshUploadGraphSample = uploadSample.HasFreshGraphSample;

                var downloadSample = ApplyOnlineSpeedSample(
                    snapshot.HasValidDownloadSpeedData ? snapshot.DownloadSpeedBytes : 0,
                    snapshot.HasValidDownloadSpeedData,
                    snapshot.HasValidTotalDownloadData,
                    effectiveOnline,
                    derivedDownloadSpeed);
                profile.LastDownloadSpeedBytes = downloadSample.DisplaySpeed;
                profile.LastDownloadGraphSpeedBytes = downloadSample.GraphSpeed;
                profile.HasFreshDownloadGraphSample = downloadSample.HasFreshGraphSample;
            }

            foreach (var profile in _state.Devices.Values)
            {
                if (!profile.HasObservedState ||
                    !profile.LastKnownOnline ||
                    reportedOnlineMacs.Contains(profile.MacAddress) ||
                    reportedMacs.Contains(profile.MacAddress))
                {
                    continue;
                }

                var identity = DeviceIdentityService.Identify(
                    profile.LastRouterName,
                    profile.LastOriginalName,
                    profile.LastConnectionType);
                ResolveEffectiveOnlineState(
                    profile,
                    reportedOnline: false,
                    now,
                    GetDisplayName(profile, identity),
                    transitions);
            }

            if (_state.Sessions.Count > 20000)
            {
                _state.Sessions = _state.Sessions
                    .OrderByDescending(session => session.OnlineAt)
                    .Take(20000)
                    .OrderBy(session => session.OnlineAt)
                    .ToList();
                _sessionsVersion++;
                requiresImmediatePollSave = true;
            }

            if (requiresImmediatePollSave ||
                transitions.Count > 0 ||
                IsPollStateCheckpointDue(now))
            {
                await SaveCoreAsync(cancellationToken);
                _lastPollStateSavedAt = now;
            }

            return new MonitorUpdate(BuildViewData(), transitions, now);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<DeviceViewData>> GetDevicesAsync(
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return BuildViewData();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task UpdateDeviceProfileAsync(
        string macAddress,
        string customName,
        string customType,
        string customIconGlyph,
        bool notifyWhenStatusChanges,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!_state.Devices.TryGetValue(macAddress, out var profile))
            {
                throw new InvalidOperationException("找不到要修改的设备。");
            }

            profile.CustomName = customName.Trim();
            profile.CustomType = customType.Trim();
            profile.CustomIconGlyph = customIconGlyph;
            profile.NotifyWhenOnline = notifyWhenStatusChanges;
            profile.NotifyWhenOffline = notifyWhenStatusChanges;
            await SaveCoreAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task RemoveDeviceUntilNextOnlineAsync(
        string macAddress,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!_state.Devices.TryGetValue(macAddress, out var profile))
            {
                throw new InvalidOperationException("找不到要移除的设备。");
            }

            profile.IsRemovedFromList = true;
            await SaveCoreAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task UpdateRouterAddressAsync(
        string routerAddress,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            _state.Settings.RouterAddress = routerAddress.Trim();
            await SaveCoreAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task UpdateBehaviorSettingsAsync(
        int refreshIntervalSeconds,
        bool startWithWindows,
        string themeMode,
        bool doNotDisturbEnabled,
        int doNotDisturbStartMinutes,
        int doNotDisturbEndMinutes,
        bool notifyWhenIpv6NotNative,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            _state.Settings.RefreshIntervalSeconds = Math.Clamp(refreshIntervalSeconds, 1, 60);
            _state.Settings.CloseToTray = true;
            _state.Settings.StartWithWindows = startWithWindows;
            _state.Settings.ThemeMode = ThemeService.NormalizeMode(themeMode);
            _state.Settings.DoNotDisturbEnabled = doNotDisturbEnabled;
            _state.Settings.DoNotDisturbStartMinutes =
                Math.Clamp(doNotDisturbStartMinutes, 0, 1439);
            _state.Settings.DoNotDisturbEndMinutes =
                Math.Clamp(doNotDisturbEndMinutes, 0, 1439);
            _state.Settings.NotifyWhenIpv6NotNative = notifyWhenIpv6NotNative;
            await SaveCoreAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task UpdateNewDeviceNotificationSettingAsync(
        bool enabled, CancellationToken cancellationToken = default)
    {
        // Effective immediately, while file writes remain debounced. Save the
        // current preference rather than replaying a stale toggle after a wait.
        _state.Settings.NotifyWhenNewDeviceOnline = enabled;
        Volatile.Write(ref _newDeviceNotificationOverride, enabled ? 1 : 0);
        await Task.Delay(TimeSpan.FromMilliseconds(80), cancellationToken);
        await _gate.WaitAsync(cancellationToken);
        try { await SaveCoreAsync(cancellationToken); }
        finally { _gate.Release(); }
    }

    public async Task UpdateWebSettingsAsync(bool enabled)
    {
        await _gate.WaitAsync();
        try { _state.Settings.LanAccessEnabled = enabled; await SaveCoreAsync(CancellationToken.None); }
        finally { _gate.Release(); }
    }
    public async Task<IReadOnlyList<DeviceSession>> GetAllDeviceSessionsAsync(string macAddress)
    {
        await _gate.WaitAsync();
        try
        {
            // The desktop's lightweight view intentionally contains only 100 recent
            // sessions. A paginated web history needs its own complete read path.
            if (_webSessionCacheVersion != _sessionsVersion)
            {
                _webSessionCache.Clear();
                _webSessionCacheVersion = _sessionsVersion;
            }
            if (_webSessionCache.TryGetValue(macAddress, out var cached)) return cached;
            var sessions = _state.Sessions.Where(s => s.MacAddress.Equals(macAddress, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(s => s.OnlineAt)
                .Select(s => new DeviceSession { MacAddress = s.MacAddress, OnlineAt = s.OnlineAt, OfflineAt = s.OfflineAt }).ToArray();
            _webSessionCache[macAddress] = sessions;
            return sessions;
        }
        finally { _gate.Release(); }
    }

    private List<DeviceViewData> BuildViewData()
    {
        var sessionsByMac = GetSessionViewCache();

        var devices = new List<DeviceViewData>();
        foreach (var profile in _state.Devices.Values)
        {
            if (profile.IsRemovedFromList)
            {
                continue;
            }

            var identity = DeviceIdentityService.Identify(
                profile.LastRouterName,
                profile.LastOriginalName,
                profile.LastConnectionType);
            var sessions = sessionsByMac.TryGetValue(
                    profile.MacAddress,
                    out var deviceSessions)
                ? deviceSessions
                : Array.Empty<DeviceSession>();

            devices.Add(new DeviceViewData
            {
                Profile = CloneProfile(profile),
                Identity = identity,
                Sessions = sessions
            });
        }

        return devices
            .OrderByDescending(device => device.Profile.LastKnownOnline)
            .ThenBy(device => device.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private IReadOnlyDictionary<string, IReadOnlyList<DeviceSession>> GetSessionViewCache()
    {
        if (_sessionViewCacheVersion == _sessionsVersion)
        {
            return _sessionViewCache;
        }

        _sessionViewCache.Clear();
        var sessionsByMac = new Dictionary<string, List<DeviceSession>>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var session in _state.Sessions)
        {
            if (!sessionsByMac.TryGetValue(session.MacAddress, out var sessions))
            {
                sessions = [];
                sessionsByMac[session.MacAddress] = sessions;
            }

            sessions.Add(session);
        }

        foreach (var pair in sessionsByMac)
        {
            _sessionViewCache[pair.Key] = pair.Value
                .OrderByDescending(session => session.OnlineAt)
                .Take(100)
                .Select(CloneSession)
                .ToArray();
        }

        _sessionViewCacheVersion = _sessionsVersion;
        return _sessionViewCache;
    }

    private void AddOpenSessionIfMissing(string macAddress, DateTimeOffset onlineAt)
    {
        if (_state.Sessions.Any(session =>
                session.MacAddress.Equals(macAddress, StringComparison.OrdinalIgnoreCase) &&
                session.OfflineAt is null))
        {
            return;
        }

        _state.Sessions.Add(new DeviceSession
        {
            MacAddress = macAddress,
            OnlineAt = onlineAt
        });
        _sessionsVersion++;
    }

    private void CloseOpenSession(string macAddress, DateTimeOffset offlineAt)
    {
        var session = _state.Sessions
            .Where(item =>
                item.MacAddress.Equals(macAddress, StringComparison.OrdinalIgnoreCase) &&
                item.OfflineAt is null)
            .OrderByDescending(item => item.OnlineAt)
            .FirstOrDefault();
        if (session is not null)
        {
            session.OfflineAt = offlineAt;
            _sessionsVersion++;
        }
    }

    /// <summary>
    /// 将单次路由器原始 online 值转换为真正用于 UI、通知和流量统计的状态。
    /// Xiaomi 固件可在一个成功轮询中临时漏掉设备或报告 online=0；这种单帧
    /// 错误不能立刻关闭会话或写入 (0, 0) 的流量样本。
    /// </summary>
    private bool ResolveEffectiveOnlineState(
        DeviceProfile profile,
        bool reportedOnline,
        DateTimeOffset now,
        string displayName,
        ICollection<DeviceTransition> transitions)
    {
        if (reportedOnline)
        {
            profile.LastOnlineSeenAt = now;
            profile.OfflineEvidenceStartedAt = default;
            if (profile.LastKnownOnline)
            {
                return true;
            }

            profile.LastKnownOnline = true;
            profile.IsRemovedFromList = false;
            AddOpenSessionIfMissing(profile.MacAddress, now);
            transitions.Add(new DeviceTransition(
                profile.MacAddress,
                displayName,
                true,
                now,
                profile.NotifyWhenOnline));
            return true;
        }

        if (!profile.LastKnownOnline)
        {
            return false;
        }

        if (profile.OfflineEvidenceStartedAt == default)
        {
            profile.OfflineEvidenceStartedAt = now;
            return true;
        }

        if (now - profile.OfflineEvidenceStartedAt < RouterOfflineConfirmationPeriod)
        {
            return true;
        }

        var firstOfflineEvidenceAt = profile.OfflineEvidenceStartedAt;
        profile.LastKnownOnline = false;
        profile.LastUploadSpeedBytes = 0;
        profile.LastDownloadSpeedBytes = 0;
        profile.LastUploadGraphSpeedBytes = 0;
        profile.LastDownloadGraphSpeedBytes = 0;
        profile.LastUploadTotalChangedAt = default;
        profile.LastDownloadTotalChangedAt = default;
        profile.OfflineEvidenceStartedAt = default;
        // 在线历史记录的是首次持续反向证据出现的时间；通知仍在当前确认
        // 时刻发出，避免把 3 秒防抖窗口错误计入“在线时长”。
        CloseOpenSession(profile.MacAddress, firstOfflineEvidenceAt);
        transitions.Add(new DeviceTransition(
            profile.MacAddress,
            displayName,
            false,
            now,
            profile.NotifyWhenOffline));
        return false;
    }

    private async Task SaveCoreAsync(CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_statePath)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = $"{_statePath}.tmp";

        await using (var stream = new FileStream(
                         temporaryPath,
                         FileMode.Create,
                         FileAccess.Write,
                         FileShare.None,
                         4096,
                         FileOptions.Asynchronous))
        {
            await JsonSerializer.SerializeAsync(stream, _state, _jsonOptions, cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }

        File.Move(temporaryPath, _statePath, overwrite: true);
    }

    private bool IsPollStateCheckpointDue(DateTimeOffset now) =>
        _lastPollStateSavedAt == default ||
        now < _lastPollStateSavedAt ||
        now - _lastPollStateSavedAt >= PollStateCheckpointInterval;

    private static string GetDisplayName(DeviceProfile profile, DeviceIdentity identity) =>
        !string.IsNullOrWhiteSpace(profile.CustomName)
            ? profile.CustomName
            : !string.IsNullOrWhiteSpace(profile.LastRouterName) &&
              !LooksLikeMacAddress(profile.LastRouterName) &&
              !profile.LastRouterName.Equals(profile.LastOriginalName, StringComparison.OrdinalIgnoreCase)
                ? profile.LastRouterName
                : !string.IsNullOrWhiteSpace(identity.SuggestedName)
                    ? identity.SuggestedName
                    : profile.LastOriginalName;

    private static bool LooksLikeMacAddress(string value)
    {
        var compact = value.Replace(":", string.Empty).Replace("-", string.Empty);
        return compact.Length == 12 && compact.All(Uri.IsHexDigit);
    }

    private readonly record struct SpeedSample(
        long DisplaySpeed,
        long GraphSpeed,
        bool HasFreshGraphSample);

    private static SpeedSample ApplyOnlineSpeedSample(
        long instantaneousSpeed,
        bool hasInstantaneousSpeed,
        bool hasCumulativeTotal,
        bool isOnline,
        long derivedSpeed)
    {
        instantaneousSpeed = Math.Max(0, instantaneousSpeed);
        derivedSpeed = Math.Max(0, derivedSpeed);
        if (!isOnline)
        {
            return new SpeedSample(0, 0, false);
        }

        var graphSpeed = ApplyGraphSpeedSample(
            instantaneousSpeed,
            hasInstantaneousSpeed,
            hasCumulativeTotal,
            derivedSpeed);

        // 明确存在的瞬时字段（包括 0）优先作为本轮真实值；只有瞬时字段
        // 缺失且累计计数刚刚产生正向差分时，才使用该观察区间的平均值。
        // 没有这两类新证据时不沿用旧值，也不把未知秒伪装成新样本。
        var hasValidGraphSample = hasInstantaneousSpeed || derivedSpeed > 0;
        var displaySpeed = hasInstantaneousSpeed ? instantaneousSpeed : graphSpeed;
        return new SpeedSample(
            displaySpeed,
            graphSpeed,
            hasValidGraphSample);
    }

    private static long ApplyGraphSpeedSample(
        long instantaneousSpeed,
        bool hasInstantaneousSpeed,
        bool hasCumulativeTotal,
        long derivedSpeed)
    {
        if (hasInstantaneousSpeed)
        {
            // 路由器明确返回的 0 也是有效的真实样本，不能用图表旧值覆盖。
            return instantaneousSpeed;
        }

        if (!hasCumulativeTotal || derivedSpeed <= 0)
        {
            return 0;
        }

        return derivedSpeed;
    }

    private static long ApplyOnlineTotalSample(
        long previous,
        long current,
        bool isOnline)
    {
        current = Math.Max(0, current);
        if (!isOnline)
        {
            return previous > 0 ? previous : current;
        }

        if (previous > 0 && current < previous)
        {
            return previous;
        }

        return current;
    }

    private static long CalculateDerivedSpeed(
        long previousTotal,
        long currentTotal,
        DateTimeOffset previousTotalChangedAt,
        DateTimeOffset now,
        bool isValid,
        out DateTimeOffset lastTotalChangedAt)
    {
        lastTotalChangedAt = previousTotalChangedAt;
        if (!isValid)
        {
            return 0;
        }

        if (lastTotalChangedAt == default)
        {
            // 第一次见到累计计数只建立基线，不能把应用启动前的全部流量
            // 误算进当前一秒的网速。
            lastTotalChangedAt = now;
            return 0;
        }

        if (previousTotal < 0 || currentTotal <= previousTotal)
        {
            return 0;
        }

        var elapsedSeconds = (now - lastTotalChangedAt).TotalSeconds;
        lastTotalChangedAt = now;
        if (elapsedSeconds <= 0)
        {
            return 0;
        }

        var speed = (currentTotal - previousTotal) / elapsedSeconds;
        return speed >= long.MaxValue ? long.MaxValue : (long)Math.Round(speed);
    }

    private static DeviceSession CloneSession(DeviceSession session) => new()
    {
        MacAddress = session.MacAddress,
        OnlineAt = session.OnlineAt,
        OfflineAt = session.OfflineAt
    };

    private static DeviceProfile CloneProfile(DeviceProfile profile) => new()
    {
        MacAddress = profile.MacAddress,
        CustomName = profile.CustomName,
        CustomType = profile.CustomType,
        CustomIconGlyph = profile.CustomIconGlyph,
        NotifyWhenOnline = profile.NotifyWhenOnline,
        NotifyWhenOffline = profile.NotifyWhenOffline,
        HasObservedState = profile.HasObservedState,
        LastKnownOnline = profile.LastKnownOnline,
        IsRemovedFromList = profile.IsRemovedFromList,
        FirstSeenAt = profile.FirstSeenAt,
        LastSeenAt = profile.LastSeenAt,
        LastOnlineSeenAt = profile.LastOnlineSeenAt,
        LastRouterName = profile.LastRouterName,
        LastOriginalName = profile.LastOriginalName,
        LastIpAddress = profile.LastIpAddress,
        LastConnectionType = profile.LastConnectionType,
        LastUploadSpeedBytes = profile.LastUploadSpeedBytes,
        LastDownloadSpeedBytes = profile.LastDownloadSpeedBytes,
        LastTotalUploadBytes = profile.LastTotalUploadBytes,
        LastTotalDownloadBytes = profile.LastTotalDownloadBytes,
        LastUploadGraphSpeedBytes = profile.LastUploadGraphSpeedBytes,
        LastDownloadGraphSpeedBytes = profile.LastDownloadGraphSpeedBytes,
        HasFreshUploadGraphSample = profile.HasFreshUploadGraphSample,
        HasFreshDownloadGraphSample = profile.HasFreshDownloadGraphSample
    };
}
