using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Security;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Microsoft.Win32;
using LanDeviceMonitor.Models;
using LanDeviceMonitor.Services;
using LanDeviceMonitor.ViewModels;
using Button = System.Windows.Controls.Button;
using Brush = System.Windows.Media.Brush;
using MediaColor = System.Windows.Media.Color;

namespace LanDeviceMonitor;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private readonly DeviceRepository _repository = new();
    private readonly RouterCredentialService _credentialService = new();
    private readonly StartupRegistrationService _startupRegistrationService = new();
    private readonly NotificationService _notificationService;
    private readonly MonitoringService _monitoringService;
    private readonly ObservableCollection<DeviceItemViewModel> _devices = [];
    private readonly ObservableCollection<SessionItemViewModel> _detailSessionItems = [];
    private readonly Dictionary<string, DeviceItemViewModel> _deviceViewModelsByMac =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, TrafficHistoryBuffer> _trafficHistories =
        new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<DeviceViewData> _latestDevices = [];
    private DeviceListFilter _deviceListFilter = DeviceListFilter.Online;
    private DeviceItemViewModel? _selectedDevice;
    private CancellationTokenSource? _deviceProfileSaveCancellation;
    private CancellationTokenSource? _appSettingsSaveCancellation;
    private CancellationTokenSource? _newDeviceNotificationSaveCancellation;
    private CancellationTokenSource? _routerSettingsSaveCancellation;
    private PendingDeviceProfile? _pendingDeviceProfile;
    private Task? _initializationTask;
    private readonly Stack<PageKind> _backNavigation = [];
    private readonly Stack<PageKind> _forwardNavigation = [];
    private bool _isExiting;
    private bool _isConnecting;
    private bool _isReplacingDevices;
    private bool _isPopulatingDeviceSettings;
    private bool _isPopulatingAppSettings;
    private bool _hasSavedRouterPassword;
    private bool _showingSettings;
    private bool _showingRouter;
    private bool _isRestartingRouter;
    private int _trafficRangeMinutes = TrafficHistoryBuffer.WindowMinutes;
    private int _pageAnimationGeneration;
    private int _deviceAnimationGeneration;
    private int _deviceFilterTransitionGeneration;
    private bool _isDeviceFilterTransitionActive;
    private bool _deviceFilterAnimationCacheActive;
    private bool _deviceDetailAnimationCacheActive;
    private bool _isCancellingDeviceFilterTransition;
    private Action? _finishDeviceFilterTransition;
    private string? _pendingSelectedDeviceMac;
    private string? _lastNotifiedIpv6Type;
    private string _currentThemeMode = ThemeService.SystemMode;
    private readonly object _monitoringUpdateDispatchGate = new();
    private readonly object _notificationDispatchGate = new();
    private readonly object _statusUpdateDispatchGate = new();
    private MonitorUpdate? _pendingMonitoringUpdate;
    private MonitorUpdate? _pendingNotificationUpdate;
    private Action? _pendingStatusUpdate;
    private int _monitoringUpdateDispatchScheduled;
    private int _notificationDispatchScheduled;
    private int _statusUpdateDispatchScheduled;
    private HwndSource? _windowSource;
    private bool _minimizeTransitionPending;
    private bool _forwardingMinimizeSystemCommand;
    private System.Windows.Threading.DispatcherOperation? _minimizeRenderOperation;
    private MinimizeTransitionRenderPhase _minimizeTransitionRenderPhase;
    private bool _nativeTrayHideRequested;
    private bool _nativeTrayHidden;
    private int _minimizeStableMaterialFramesObserved;
    private int _minimizeSnapshotFramesObserved;
    private bool _minimizeSnapshotCapturedAfterStableFrame;

    private const int WindowMessageSystemCommand = 0x0112;
    private const int SystemCommandMinimize = 0xF020;
    private static readonly int ExitInterfaceMessage = unchecked((int)RegisterWindowMessage("ReTray.ExitInterface.v1"));
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessage(string name);

    private readonly bool trayEditor;
    public ReTray_App.MainPage TrayPage { get; private set; } = null!;
    public int TrayThemeIndex { get; private set; }
    public object? TrayContent { get => TrayContentHost.Content; set => TrayContentHost.Content = value; }
    public MainWindow(NotificationService notificationService) : this(notificationService, false) { }
    protected MainWindow(bool editor) : this(new NotificationService(), editor) { }
    private MainWindow(NotificationService notificationService, bool editor)
    {
        InitializeComponent();
        trayEditor = editor;
        DataContext = this;
        _notificationService = notificationService;
        _monitoringService = new MonitoringService(new XiaomiRouterClient(), _repository);
        System.Windows.Application.Current.Resources.MergedDictionaries.Add(Resources);
        CaptionBackButton.Visibility = Visibility.Collapsed;
        MaximizeWindowButton.Visibility = Visibility.Collapsed;
        if (!editor)
        {
            TrayPage = new ReTray_App.MainPage();
            TrayContentHost.Content = TrayPage;
        }
        AddHandler(Mouse.PreviewMouseWheelEvent, new MouseWheelEventHandler(HandleMouseWheel), true);
        SourceInitialized += MainWindow_SourceInitialized;
        SourceInitialized += (_, _) => MicaWindowService.Apply(this, ThemeService.ResolveIsDark(ThemeService.CurrentMode));
        ContentRendered += (_, _) => _hasRenderedInitialContent = true;
        SystemEvents.UserPreferenceChanged += SystemEvents_UserPreferenceChanged;
    }
    public void ApplyTrayTheme(int index)
    {
        TrayThemeIndex = index;
        _currentThemeMode = index switch { 1 => ThemeService.LightMode, 2 => ThemeService.DarkMode, _ => ThemeService.SystemMode };
        var dark = ThemeService.Apply(_currentThemeMode);
        MicaWindowService.Apply(this, dark);
    }
    internal static string GetDisplayVersion()
    {
        var informationalVersion = typeof(App).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;
        var cleanVersion = informationalVersion?
            .Split('+', 2, StringSplitOptions.TrimEntries)[0];
        return string.IsNullOrWhiteSpace(cleanVersion)
            ? "未知"
            : $"v{cleanVersion}";
    }

    internal static int GetDeviceFilterTransitionDurationMilliseconds() => 480;

    internal int MinimizeStableMaterialFramesObserved =>
        _minimizeStableMaterialFramesObserved;

    internal int MinimizeSnapshotFramesObserved =>
        _minimizeSnapshotFramesObserved;

    internal bool MinimizeSnapshotCapturedAfterStableFrame =>
        _minimizeSnapshotCapturedAfterStableFrame;

    internal static (int ListDurationMilliseconds,
        int DetailExitDurationMilliseconds,
        int DetailEntranceDurationMilliseconds)
        GetSynchronizedDeviceFilterAnimationTiming() =>
        (GetDeviceFilterTransitionDurationMilliseconds(), 150, 450);

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? MinimizeToTrayRequested;

    public DeviceItemViewModel? SelectedDevice
    {
        get => _selectedDevice;
        private set
        {
            if (ReferenceEquals(_selectedDevice, value))
            {
                return;
            }

            _selectedDevice = value;
            OnPropertyChanged();
        }
    }

    public int TrafficRangeMinutes
    {
        get => _trafficRangeMinutes;
        private set
        {
            if (_trafficRangeMinutes == value)
            {
                return;
            }

            _trafficRangeMinutes = value;
            OnPropertyChanged();
        }
    }

    private PageKind CurrentPage =>
        _showingSettings
            ? PageKind.Settings
            : _showingRouter
                ? PageKind.Router
                : PageKind.Devices;

    public Task InitializeAsync() =>
        _initializationTask ??= InitializeCoreAsync();

    private async Task InitializeCoreAsync()
    {
        try
        {
            await _repository.LoadAsync();
            var settings = _repository.Settings;
            ApplyTheme(settings.ThemeMode, animate: false);
            PopulateSettings(settings);
            await InitializeWebAccessAsync();
            ReplaceDevices(await _repository.GetDevicesAsync(), preserveSelection: false);

            var password = _credentialService.ReadPassword(settings.RouterAddress);
            if (string.IsNullOrWhiteSpace(password))
            {
                ShowSettings("首次使用，请输入小米路由器管理员密码。");
                return;
            }

            // Startup refreshes data while preserving the initial device list
            // and any page the user opens while the connection is pending.
            await ConnectAsync(
                settings.RouterAddress,
                password,
                settings.RefreshIntervalSeconds,
                navigateOnSuccess: false);
        }
        catch (Exception exception)
        {
            ShowSettings(exception.Message);
        }
    }

    private async Task<bool> ConnectAsync(
        string address,
        string password,
        int intervalSeconds,
        bool navigateOnSuccess)
    {
        if (_isConnecting)
        {
            return false;
        }

        _isConnecting = true;
        SettingsMessageBorder.Visibility = Visibility.Collapsed;
        try
        {
            await _monitoringService.ConnectAndStartAsync(address, password, intervalSeconds);
            if (navigateOnSuccess)
            {
                ShowDevicesPanel(animate: false, recordNavigation: false);
            }

            return true;
        }
        catch (Exception exception)
        {
            ShowSettings(exception.Message);
            return false;
        }
        finally
        {
            _isConnecting = false;
        }
    }

    private void MonitoringService_Updated(object? sender, MonitorUpdate update)
    {
        if (_lanWebServer?.IsRunning == true)
        {
            _lanWebServer.SetPreferences(_repository.Settings, _hasSavedRouterPassword);
            _lanWebServer.Publish(update);
        }
        QueueNotifications(update);
        QueueMonitoringUpdate(update);
    }

    private void QueueNotifications(MonitorUpdate update)
    {
        if (_isExiting || Dispatcher.HasShutdownStarted)
        {
            return;
        }

        var hasDeviceTransition = update.Transitions.Any(item => item.ShouldNotify);
        if (!hasDeviceTransition && update.InternetInfo is null)
        {
            return;
        }

        lock (_notificationDispatchGate)
        {
            _pendingNotificationUpdate = MergeNotificationUpdates(
                _pendingNotificationUpdate,
                update);
        }

        ScheduleNotificationDispatch();
    }

    private static MonitorUpdate MergeNotificationUpdates(
        MonitorUpdate? pending,
        MonitorUpdate incoming)
    {
        if (pending is null)
        {
            return incoming;
        }

        var transitions = pending.Transitions
            .Concat(incoming.Transitions)
            .Where(item => item.ShouldNotify)
            .GroupBy(item => (item.IsNewDevice ? "new:" : "state:") + item.MacAddress, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Last())
            .ToList();
        return pending with
        {
            Transitions = transitions,
            RefreshedAt = incoming.RefreshedAt,
            InternetInfo = incoming.InternetInfo
        };
    }

    private void ScheduleNotificationDispatch()
    {
        if (Interlocked.Exchange(ref _notificationDispatchScheduled, 1) != 0)
        {
            return;
        }

        try
        {
            _ = Dispatcher.BeginInvoke(
                // 状态通知不应以 Send 抢在所有输入、布局和渲染之前，
                // 否则在外部高负载时会放大 UI 线程饥饿。
                System.Windows.Threading.DispatcherPriority.Normal,
                new Action(DrainNotificationDispatch));
        }
        catch (InvalidOperationException) when (Dispatcher.HasShutdownStarted)
        {
            lock (_notificationDispatchGate)
            {
                _pendingNotificationUpdate = null;
            }

            Volatile.Write(ref _notificationDispatchScheduled, 0);
        }
    }

    public ObservableCollection<SessionItemViewModel> DetailSessionItems =>
        _detailSessionItems;

    private void DrainNotificationDispatch()
    {
        MonitorUpdate? update;
        lock (_notificationDispatchGate)
        {
            update = _pendingNotificationUpdate;
            _pendingNotificationUpdate = null;
        }

        try
        {
            if (update is not null && !_isExiting && !Dispatcher.HasShutdownStarted)
            {
                DeliverNotifications(update);
            }
        }
        finally
        {
            Volatile.Write(ref _notificationDispatchScheduled, 0);
            var hasPendingUpdate = false;
            lock (_notificationDispatchGate)
            {
                hasPendingUpdate = _pendingNotificationUpdate is not null;
            }

            if (hasPendingUpdate && !_isExiting && !Dispatcher.HasShutdownStarted)
            {
                ScheduleNotificationDispatch();
            }
        }
    }

    private void DeliverNotifications(MonitorUpdate update)
    {
        if (_isExiting || Dispatcher.HasShutdownStarted)
        {
            return;
        }

        var settings = _repository.Settings;
        var notificationsMuted = NotificationSchedule.IsMuted(
            update.RefreshedAt,
            settings.DoNotDisturbEnabled,
            settings.DoNotDisturbStartMinutes,
            settings.DoNotDisturbEndMinutes);
        foreach (var transition in update.Transitions.Where(item => item.ShouldNotify))
        {
            if (!notificationsMuted)
            {
                if (transition.IsNewDevice)
                {
                    if (settings.NotifyWhenNewDeviceOnline)
                        _ = _notificationService.ShowNewDeviceOnline(
                            transition.DisplayName, transition.OccurredAt);
                }
                else
                    _ = _notificationService.ShowDeviceState(
                        transition.DisplayName,
                        transition.IsOnline,
                        transition.OccurredAt);
            }
        }

        var ipv6Type = update.InternetInfo?.Ipv6ConnectionType?.Trim();
        if (!settings.NotifyWhenIpv6NotNative)
        {
            _lastNotifiedIpv6Type = null;
        }
        else if (!string.IsNullOrWhiteSpace(ipv6Type) &&
                 ipv6Type.Equals("Native", StringComparison.OrdinalIgnoreCase))
        {
            _lastNotifiedIpv6Type = null;
        }
        else if (!notificationsMuted &&
                 !string.IsNullOrWhiteSpace(ipv6Type) &&
                 !string.Equals(
                     _lastNotifiedIpv6Type,
                     ipv6Type,
                     StringComparison.OrdinalIgnoreCase))
        {
            _ = _notificationService.ShowIpv6NonNative(ipv6Type);
            _lastNotifiedIpv6Type = ipv6Type;
        }
    }

    private void AppendTrafficHistory(
        IReadOnlyList<DeviceViewData> devices,
        DateTimeOffset timestamp)
    {
        foreach (var device in devices)
        {
            if (!device.Profile.HasObservedState)
            {
                continue;
            }

            if (!device.Profile.HasFreshUploadGraphSample &&
                !device.Profile.HasFreshDownloadGraphSample)
            {
                continue;
            }

            var buffer = GetTrafficHistory(device.Profile.MacAddress);
            buffer.Append(new TrafficSample(
                timestamp,
                GetGraphSpeed(
                    device.Profile.LastUploadGraphSpeedBytes,
                    device.Profile.LastUploadSpeedBytes),
                GetGraphSpeed(
                    device.Profile.LastDownloadGraphSpeedBytes,
                    device.Profile.LastDownloadSpeedBytes))
            {
                HasUploadSample = device.Profile.HasFreshUploadGraphSample,
                HasDownloadSample = device.Profile.HasFreshDownloadGraphSample
            });
        }
    }

    internal static long GetGraphSpeed(long graphSpeed, long displaySpeed) =>
        graphSpeed > 0 ? graphSpeed : Math.Max(0, displaySpeed);

    private TrafficHistoryBuffer GetTrafficHistory(string macAddress)
    {
        if (!_trafficHistories.TryGetValue(macAddress, out var history))
        {
            history = new TrafficHistoryBuffer();
            _trafficHistories[macAddress] = history;
        }

        return history;
    }

    private void TrafficRangeSegments_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || TrafficRangeSegments.SelectedIndex < 0) return;
        TrafficRangeMinutes = GetTrafficRangeMinutesForSegment(TrafficRangeSegments.SelectedIndex);
    }

    private void DeviceFilterSegments_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        ChangeDeviceListFilter(DeviceFilterSegments.SelectedIndex == 1 ? DeviceListFilter.Offline : DeviceListFilter.Online);
    }

    internal static int GetTrafficRangeMinutesForSegment(int selectedIndex) =>
        selectedIndex switch
        {
            0 => 1,
            1 => 5,
            _ => 10
        };

    private void UpdateTrafficRangeSelection()
    {
        if (TrafficRangeSegments is not null)
            TrafficRangeSegments.SelectedIndex = TrafficRangeMinutes == 1 ? 0 : TrafficRangeMinutes == 5 ? 1 : 2;
    }

    private void MonitoringService_ConnectionStateChanged(object? sender, string state)
    {
        _lanWebServer?.SetConnection(state);
        DispatchMonitoringUpdate(() =>
        {
            ConnectionStatusText.Text = state;
            ConnectionDot.Fill = state == "已连接"
                ? new SolidColorBrush(MediaColor.FromRgb(77, 178, 128))
                : new SolidColorBrush(MediaColor.FromRgb(234, 139, 20));
            if (state != "已连接") ResetConnectionPulse();
        });
    }

    private void ResetConnectionPulse()
    {
        if (ConnectionDot.RenderTransform is ScaleTransform scale)
        {
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            scale.ScaleX = scale.ScaleY = 1;
        }
        _connectionBreathClock = null;
    }

    private AnimationClock? _connectionBreathClock;

    private void PulseConnectionDot()
    {
        ConnectionStatusText.Text = "已连接";
        ConnectionDot.Fill = new SolidColorBrush(MediaColor.FromRgb(77, 178, 128));
        if (ConnectionDot.RenderTransform is not ScaleTransform scale)
        {
            ConnectionDot.RenderTransformOrigin = new System.Windows.Point(.5, .5);
            ConnectionDot.RenderTransform = scale = new ScaleTransform();
        }
        if (!SystemParameters.ClientAreaAnimation) { ResetConnectionPulse(); return; }
        var velocity = _connectionBreathClock?.CurrentProgress is { } progress &&
            _connectionBreathClock.Timeline is Controls.ConnectionBreathAnimation previous ? previous.Velocity(progress) : 0;
        var pulse = new Controls.ConnectionBreathAnimation
        {
            StartScale = scale.ScaleX,
            StartVelocity = velocity,
            Duration = TimeSpan.FromMilliseconds(Math.Clamp(_repository.Settings.RefreshIntervalSeconds * 1000, 1000, 1800)),
            FillBehavior = FillBehavior.Stop
        };
        var clock = pulse.CreateClock();
        _connectionBreathClock = clock;
        clock.Completed += (_, _) => { if (ReferenceEquals(_connectionBreathClock, clock)) ResetConnectionPulse(); };
        scale.ApplyAnimationClock(ScaleTransform.ScaleXProperty, clock, HandoffBehavior.SnapshotAndReplace);
        scale.ApplyAnimationClock(ScaleTransform.ScaleYProperty, clock, HandoffBehavior.SnapshotAndReplace);
    }

    private void MonitoringService_MonitoringError(object? sender, Exception exception)
    {
        _lanWebServer?.SetConnection("路由器连接异常");
        DispatchMonitoringUpdate(() =>
        {
            // 轮询失败不是路由器报告的真实 0。不要把失败写成零速率，
            // 否则图表会出现用户看到的周期性垂直断崖；下一次成功采样
            // 会由 TrafficHistoryBuffer 用最近有效值填补中间时间槽。
            ConnectionStatusText.Text = $"连接异常：{exception.Message}";
            ConnectionDot.Fill = new SolidColorBrush(MediaColor.FromRgb(220, 68, 55));
            ResetConnectionPulse();
        });
    }

    private void MonitoringService_RouterRestartProgressChanged(object? sender, string state)
    {
        _lanWebServer?.SetConnection(state);
        DispatchMonitoringUpdate(() =>
        {
            RouterRestartStatusText.Text = state;
            ConnectionStatusText.Text = state;
        });
    }

    private void QueueMonitoringUpdate(MonitorUpdate update)
    {
        if (_isExiting || Dispatcher.HasShutdownStarted)
        {
            return;
        }

        Interlocked.Exchange(ref _pendingMonitoringUpdate, update);
        ScheduleMonitoringUpdateDispatch();
    }

    private void ScheduleMonitoringUpdateDispatch()
    {
        if (Interlocked.Exchange(ref _monitoringUpdateDispatchScheduled, 1) != 0)
        {
            return;
        }

        try
        {
            _ = Dispatcher.BeginInvoke(
                System.Windows.Threading.DispatcherPriority.Background,
                new Action(DrainMonitoringUpdate));
        }
        catch (InvalidOperationException) when (Dispatcher.HasShutdownStarted)
        {
            Interlocked.Exchange(ref _pendingMonitoringUpdate, null);
            Volatile.Write(ref _monitoringUpdateDispatchScheduled, 0);
        }
    }

    private void DrainMonitoringUpdate()
    {
        var update = Interlocked.Exchange(ref _pendingMonitoringUpdate, null);
        try
        {
            if (update is not null && !_isExiting && !Dispatcher.HasShutdownStarted)
            {
                // 实时刷新只更新已有视图模型，不触发页面进入动画，也不重填正在编辑的控件。
                AppendTrafficHistory(update.Devices, update.RefreshedAt);
                ReplaceDevices(update.Devices, preserveSelection: true);
                PulseConnectionDot();
                LastRefreshText.Text = $"更新于\u2009{update.RefreshedAt:HH:mm:ss}";
            }
        }
        finally
        {
            Volatile.Write(ref _monitoringUpdateDispatchScheduled, 0);
            if (Volatile.Read(ref _pendingMonitoringUpdate) is not null &&
                !_isExiting &&
                !Dispatcher.HasShutdownStarted)
            {
                ScheduleMonitoringUpdateDispatch();
            }
        }
    }

    private void DispatchMonitoringUpdate(Action update)
    {
        if (_isExiting || Dispatcher.HasShutdownStarted)
        {
            return;
        }

        lock (_statusUpdateDispatchGate)
        {
            _pendingStatusUpdate = update;
        }

        ScheduleStatusUpdateDispatch();
    }

    private void ScheduleStatusUpdateDispatch()
    {
        if (Interlocked.Exchange(ref _statusUpdateDispatchScheduled, 1) != 0)
        {
            return;
        }

        try
        {
            _ = Dispatcher.BeginInvoke(
                System.Windows.Threading.DispatcherPriority.Normal,
                new Action(DrainStatusUpdate));
        }
        catch (InvalidOperationException) when (Dispatcher.HasShutdownStarted)
        {
            lock (_statusUpdateDispatchGate)
            {
                _pendingStatusUpdate = null;
            }

            Volatile.Write(ref _statusUpdateDispatchScheduled, 0);
        }
    }

    private void DrainStatusUpdate()
    {
        Action? update;
        lock (_statusUpdateDispatchGate)
        {
            update = _pendingStatusUpdate;
            _pendingStatusUpdate = null;
        }

        try
        {
            if (update is not null && !_isExiting && !Dispatcher.HasShutdownStarted)
            {
                update();
            }
        }
        finally
        {
            Volatile.Write(ref _statusUpdateDispatchScheduled, 0);
            var hasPendingUpdate = false;
            lock (_statusUpdateDispatchGate)
            {
                hasPendingUpdate = _pendingStatusUpdate is not null;
            }

            if (hasPendingUpdate && !_isExiting && !Dispatcher.HasShutdownStarted)
            {
                ScheduleStatusUpdateDispatch();
            }
        }
    }

    private void ReplaceDevices(
        IReadOnlyList<DeviceViewData> devices,
        bool preserveSelection,
        bool updateEditor = false)
    {
        _unfilteredLatestDevices = devices.ToList();
        _latestDevices = _meshMemberFilter.Filter(_unfilteredLatestDevices);
        EnsureDetailSessionCapacity(_latestDevices);
        OnlineCountText.Text = _latestDevices.Count(device =>
            device.Profile.LastKnownOnline).ToString();
        OfflineCountText.Text = _latestDevices.Count(device =>
            !device.Profile.LastKnownOnline).ToString();
        ReplaceVisibleDevices(preserveSelection, updateEditor);
    }

    private void EnsureDetailSessionCapacity(
        IReadOnlyList<DeviceViewData> devices)
    {
        var maximumSessionCount = devices.Count == 0
            ? 0
            : devices.Max(device => device.Sessions.Count);
        EnsureDetailSessionCapacity(maximumSessionCount);
    }

    private void EnsureDetailSessionCapacity(int sessionCount)
    {
        while (_detailSessionItems.Count < sessionCount)
        {
            _detailSessionItems.Add(new SessionItemViewModel());
        }
    }

    private void UpdateDetailSessionItems(DeviceItemViewModel? device)
    {
        if(_historyDeviceMac!=device?.MacAddress)
        {
            _historyDeviceMac=device?.MacAddress;
            _historyExtraCount=0;
            SessionHistoryDisclosure?.Reset();
        }
        var original = device?.Data.Sessions ?? [];
        var recent=original.Where(session=>SessionItemViewModel.ContainsRecentDate(session,DateTime.Today)).ToArray();
        IReadOnlyList<DeviceSession> sessions=original;
        EnsureDetailSessionCapacity(sessions.Count);
        for (var index = 0; index < _detailSessionItems.Count; index++)
        {
            _detailSessionItems[index].Update(
                index < sessions.Count ? sessions[index] : null);
        }
        RecentSessionCount=recent.Length;HistorySessionCount=sessions.Count;
        OnPropertyChanged(nameof(RecentSessionCount));OnPropertyChanged(nameof(HistorySessionCount));NotifyHistoryVisibility();
    }

    private string? _historyDeviceMac;
    private int _historyExtraCount;
    public int RecentSessionCount {get;private set;}
    public int HistorySessionCount {get;private set;}
    public int VisibleHistoryCount => Math.Min(HistorySessionCount,3+_historyExtraCount);
    public bool HasOlderSessions => HistorySessionCount>VisibleHistoryCount;
    public bool HasExpandedHistory => HistorySessionCount>3 && !HasOlderSessions;
    public string HistoryActionName => HasOlderSessions ? "最多再展示十五条上下线记录" : "收起到最近三条上下线记录";
    public bool HasHistoryControls => HistorySessionCount>3;
    private void NotifyHistoryVisibility()
    {
        OnPropertyChanged(nameof(VisibleHistoryCount));OnPropertyChanged(nameof(HasOlderSessions));
        OnPropertyChanged(nameof(HasExpandedHistory));OnPropertyChanged(nameof(HasHistoryControls));
        OnPropertyChanged(nameof(HistoryActionName));
    }
    private void OlderHistoryToggle_Click(object sender,RoutedEventArgs e)
    {
        if(HasOlderSessions) _historyExtraCount=Math.Max(0,Math.Min(HistorySessionCount,VisibleHistoryCount+15)-3);
        else if(HasExpandedHistory) _historyExtraCount=0;
        NotifyHistoryVisibility();
    }

    private void ReplaceVisibleDevices(
        bool preserveSelection,
        bool updateEditor = false,
        bool keepPendingSelection = false,
        bool updateSelectedDevice = true)
    {
        if (_isDeviceFilterTransitionActive || _deviceDetailAnimationCacheActive)
        {
            // Keep the transition's two visual trees stable. The latest snapshot
            // and counters are still updated by ReplaceDevices and reconciled
            // once the filter animation has completed.
            return;
        }

        if (!preserveSelection && !keepPendingSelection)
        {
            _pendingSelectedDeviceMac = null;
        }

        var visibleDevices = GetFilteredAndSortedDevices();
        var selectedMac = preserveSelection
            ? _pendingSelectedDeviceMac ?? SelectedDevice?.MacAddress
            : null;
        _isReplacingDevices = true;
        try
        {
            _deviceViewModelsByMac.Clear();
            foreach (var existing in _devices)
            {
                _deviceViewModelsByMac[existing.MacAddress] = existing;
            }

            var desiredViewModels = new List<DeviceItemViewModel>(visibleDevices.Count);
            foreach (var data in visibleDevices)
            {
                if (_deviceViewModelsByMac.TryGetValue(
                        data.Profile.MacAddress,
                        out var existing))
                {
                    existing.Update(data);
                    desiredViewModels.Add(existing);
                }
                else
                {
                    desiredViewModels.Add(new DeviceItemViewModel(data));
                }
            }

            for (var index = 0; index < desiredViewModels.Count; index++)
            {
                var desired = desiredViewModels[index];
                if (index < _devices.Count && ReferenceEquals(_devices[index], desired))
                {
                    continue;
                }

                var existingIndex = -1;
                for (var searchIndex = index + 1; searchIndex < _devices.Count; searchIndex++)
                {
                    if (ReferenceEquals(_devices[searchIndex], desired))
                    {
                        existingIndex = searchIndex;
                        break;
                    }
                }

                if (existingIndex >= 0)
                {
                    _devices.Move(existingIndex, index);
                }
                else if (index < _devices.Count)
                {
                    _devices[index] = desired;
                }
                else
                {
                    _devices.Add(desired);
                }
            }

            while (_devices.Count > desiredViewModels.Count)
            {
                _devices.RemoveAt(_devices.Count - 1);
            }

            var selected = selectedMac is null
                ? _devices.FirstOrDefault()
                : _devices.FirstOrDefault(device =>
                    device.MacAddress.Equals(selectedMac, StringComparison.OrdinalIgnoreCase));
            var pendingSelectionIsAnimating =
                selected is not null &&
                _pendingSelectedDeviceMac is not null &&
                selected.MacAddress.Equals(
                    _pendingSelectedDeviceMac,
                    StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(
                    SelectedDevice?.MacAddress,
                    _pendingSelectedDeviceMac,
                    StringComparison.OrdinalIgnoreCase);
            if (selected is null &&
                _pendingSelectedDeviceMac is not null)
            {
                _pendingSelectedDeviceMac = null;
            }

            if (selected is not null && !_deviceDetailAnimationCacheActive)
            {
                selected.Update(
                    selected.Data,
                    GetTrafficHistory(selected.MacAddress).Snapshot(DateTimeOffset.Now));
            }

            DeviceList.SelectedItem = selected;
            if (!pendingSelectionIsAnimating && updateSelectedDevice)
            {
                UpdateSelectedDevice(
                    selected,
                    updateEditor || !preserveSelection || selectedMac is null);
            }
        }
        finally
        {
            _isReplacingDevices = false;
        }
    }

    private IReadOnlyList<DeviceViewData> GetFilteredAndSortedDevices()
    {
        var filtered = _latestDevices.Where(device =>
            device.Profile.LastKnownOnline ==
            (_deviceListFilter == DeviceListFilter.Online));
        return _deviceListFilter == DeviceListFilter.Online
            ? filtered
                .OrderByDescending(GetLatestOnlineAt)
                .ThenBy(device => device.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .ToList()
            : filtered
                .OrderByDescending(GetLatestOfflineAt)
                .ThenBy(device => device.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
    }

    private static DateTimeOffset GetLatestOnlineAt(DeviceViewData device) =>
        device.Sessions
            .OrderByDescending(session => session.OnlineAt)
            .FirstOrDefault()
            ?.OnlineAt ??
        device.Profile.FirstSeenAt;

    private static DateTimeOffset GetLatestOfflineAt(DeviceViewData device) =>
        device.Sessions
            .Where(session => session.OfflineAt.HasValue)
            .OrderByDescending(session => session.OfflineAt)
            .FirstOrDefault()
            ?.OfflineAt ??
        device.Profile.LastSeenAt;

    private void ChangeDeviceListFilter(DeviceListFilter filter)
    {
        if (_minimizeTransitionPending || _deviceListFilter == filter)
        {
            return;
        }

        var previousSelectedMac = SelectedDevice?.MacAddress;
        var direction = filter == DeviceListFilter.Offline ? 1 : -1;
        _deviceListFilter = filter;
        var newViewData = GetFilteredAndSortedDevices();
        var newViewModels = newViewData
            .Select(data => new DeviceItemViewModel(data))
            .ToList();
        var newSelectionMac = newViewData
            .FirstOrDefault()
            ?.Profile.MacAddress;
        var newSelection = newViewModels.FirstOrDefault(device =>
            string.Equals(
                device.MacAddress,
                newSelectionMac,
                StringComparison.OrdinalIgnoreCase));
        var selectionChanged = !string.Equals(
                newSelectionMac,
                previousSelectedMac,
                StringComparison.OrdinalIgnoreCase);

        if (!SystemParameters.ClientAreaAnimation)
        {
            _pendingSelectedDeviceMac = newSelectionMac;
            ReplaceVisibleDevices(
                preserveSelection: false,
                updateEditor: true,
                keepPendingSelection: true);
            if (selectionChanged)
            {
                AnimateDeviceChange(newSelection!);
            }
            else
            {
                _pendingSelectedDeviceMac = null;
                UpdateSelectedDevice(newSelection, updateEditor: true);
            }

            return;
        }

        AnimateDeviceListFilterTransition(
            direction,
            newViewModels,
            newSelection,
            selectionChanged);
        if (selectionChanged)
        {
            _pendingSelectedDeviceMac = newSelectionMac;
            AnimateDeviceChange(newSelection!);
        }
        else
        {
            _pendingSelectedDeviceMac = null;
        }
    }

    internal FrameworkElement GetDeviceListMotion(System.Windows.Controls.ListBox list)
    {
        list.ApplyTemplate();
        if (list.Template.FindName("PART_ScrollViewer", list) is ScrollViewer viewer)
        {
            viewer.ApplyTemplate();
            if (viewer.Template.FindName("DeviceViewportMask", viewer) is FrameworkElement viewport) return viewport;
        }
        return list;
    }

    private System.Windows.Controls.Primitives.ScrollBar? DeviceScrollBar(System.Windows.Controls.ListBox list)
    {
        list.ApplyTemplate();
        if (list.Template.FindName("PART_ScrollViewer",list) is not System.Windows.Controls.ScrollViewer viewer) return null;
        viewer.ApplyTemplate();
        return viewer.Template.FindName("PART_VerticalScrollBar",viewer) as System.Windows.Controls.Primitives.ScrollBar;
    }

    private void AnimateDeviceScrollBars(TimeSpan duration)
    {
        foreach (var list in new[] {DeviceList,DeviceListTransition})
        {
            if (DeviceScrollBar(list) is not { } bar) continue;
            var entering=list==DeviceListTransition;
            var viewer=(System.Windows.Controls.ScrollViewer)list.Template.FindName("PART_ScrollViewer",list);
            bar.Visibility=viewer.ScrollableHeight>0 ? Visibility.Visible : Visibility.Hidden;
            bar.BeginAnimation(OpacityProperty,new DoubleAnimation(entering?0:bar.Opacity,entering?1:0,duration)
                {EasingFunction=new CubicEase {EasingMode=EasingMode.EaseOut}});
        }
    }

    private void CommitEnteringDeviceViewport()
    {
        if (DeviceListTransition.Template.FindName("PART_ScrollViewer", DeviceListTransition) is not ScrollViewer entering ||
            DeviceList.Template.FindName("PART_ScrollViewer", DeviceList) is not ScrollViewer primary) return;
        // WPF remembers the requested offset even when a short list clamps its
        // effective offset to zero. Materialize the new extent, then overwrite
        // that request with the position the user just saw in the entering tree.
        DeviceList.UpdateLayout();
        primary.ScrollToVerticalOffset(entering.VerticalOffset);
        primary.UpdateLayout();
    }

    private void AnimateDeviceListFilterTransition(
        int direction,
        IReadOnlyList<DeviceItemViewModel> newViewModels,
        DeviceItemViewModel? newSelection,
        bool selectionChanged)
    {
        var transitionGeneration = ++_deviceFilterTransitionGeneration;
        var previousFinish = _finishDeviceFilterTransition;
        if (previousFinish is not null)
        {
            // Let the interrupted transition hand its target list to the
            // outgoing visual tree before the new transition resets the
            // transition layer. Its later Completed callback remains stale.
            _isCancellingDeviceFilterTransition = true;
            try
            {
                previousFinish();
            }
            finally
            {
                _isCancellingDeviceFilterTransition = false;
            }
        }

        StopDeviceFilterAnimations();
        GetDeviceListMotion(DeviceList).Opacity = 1;
        GetDeviceListMotion(DeviceList).RenderTransform = Transform.Identity;
        GetDeviceListMotion(DeviceListTransition).Opacity = 1;
        GetDeviceListMotion(DeviceListTransition).RenderTransform = Transform.Identity;
        DeviceListTransition.Visibility = Visibility.Collapsed;
        DeviceListTransition.ItemsSource = null;
        DeviceListTransition.SelectedItem = null;
        _isDeviceFilterTransitionActive = true;
        DeviceList.IsHitTestVisible = false;

        var offset = 340d;
        var exitOffset = -offset * direction;
        var enterOffset = offset * direction;

        var exitTranslate = new TranslateTransform(0, 0);
        GetDeviceListMotion(DeviceList).RenderTransformOrigin = new System.Windows.Point(0.5, 0.5);
        GetDeviceListMotion(DeviceList).RenderTransform = exitTranslate;
        GetDeviceListMotion(DeviceList).Opacity = 1;

        DeviceListTransition.ItemsSource = newViewModels;
        DeviceListTransition.SelectedItem = newSelection;

        DeviceListTransition.Visibility = Visibility.Visible;
        DeviceListTransition.ApplyTemplate();
        var enterTranslate = new TranslateTransform(enterOffset, 0);
        GetDeviceListMotion(DeviceListTransition).RenderTransformOrigin =
            new System.Windows.Point(0.5, 0.5);
        GetDeviceListMotion(DeviceListTransition).RenderTransform = enterTranslate;
        GetDeviceListMotion(DeviceListTransition).Opacity = 0;
        DeviceListTransition.Visibility = Visibility.Visible;
        // Materialize both visual trees before enabling BitmapCache. If the
        // entering ListBox is cached while its first containers are still
        // being generated, WPF can reuse the wrong visual for the first frame
        // and briefly show the target list in both positions.
        DeviceList.UpdateLayout();
        DeviceListTransition.UpdateLayout();
        BeginDeviceFilterAnimationCache();

        var transitionDuration = TimeSpan.FromMilliseconds(
            GetDeviceFilterTransitionDurationMilliseconds());
        AnimateDeviceScrollBars(transitionDuration);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var enterAnimation = new DoubleAnimation(
            enterOffset,
            0,
            transitionDuration)
        {
            EasingFunction = ease
        };
        var finished = false;
        void Finish()
        {
            if (finished)
            {
                return;
            }

            finished = true;
            if (transitionGeneration != _deviceFilterTransitionGeneration)
            {
                // A newer filter transition owns the visual elements now. When
                // this is the synchronous cancellation that starts that newer
                // transition, commit this target as the new outgoing list so
                // the two visual trees remain distinct. A later stale
                // Completed callback only stops its detached clocks.
                if (_isCancellingDeviceFilterTransition)
                {
                    _isReplacingDevices = true;
                    try
                    {
                        _devices.Clear();
                        foreach (var viewModel in newViewModels)
                        {
                            _devices.Add(viewModel);
                        }

                        DeviceList.SelectedItem = _devices.FirstOrDefault(device =>
                            string.Equals(
                                device.MacAddress,
                                newSelection?.MacAddress,
                                StringComparison.OrdinalIgnoreCase));
                        CommitEnteringDeviceViewport();
                    }
                    finally
                    {
                        _isReplacingDevices = false;
                    }
                }

                exitTranslate.BeginAnimation(TranslateTransform.XProperty, null);
                enterTranslate.BeginAnimation(TranslateTransform.XProperty, null);
                return;
            }

            _finishDeviceFilterTransition = null;
            _isReplacingDevices = true;
            try
            {
                _devices.Clear();
                foreach (var viewModel in newViewModels)
                {
                    _devices.Add(viewModel);
                }

                DeviceList.SelectedItem = _devices.FirstOrDefault(device =>
                    string.Equals(
                        device.MacAddress,
                        newSelection?.MacAddress,
                        StringComparison.OrdinalIgnoreCase));
            }
            finally
            {
                _isReplacingDevices = false;
            }

            _isDeviceFilterTransitionActive = false;
            ReplaceVisibleDevices(
                preserveSelection: false,
                updateEditor: false,
                keepPendingSelection: true,
                updateSelectedDevice: false);
            CommitEnteringDeviceViewport();
            StopDeviceFilterAnimations(exitTranslate, enterTranslate);
            GetDeviceListMotion(DeviceList).Opacity = 1;
            GetDeviceListMotion(DeviceList).RenderTransform = Transform.Identity;
            GetDeviceListMotion(DeviceListTransition).Opacity = 1;
            GetDeviceListMotion(DeviceListTransition).RenderTransform = Transform.Identity;
            DeviceListTransition.Visibility = Visibility.Collapsed;
            DeviceListTransition.ItemsSource = null;
            DeviceListTransition.SelectedItem = null;
            DeviceList.IsHitTestVisible = true;
            var reconciledSelection = DeviceList.SelectedItem as DeviceItemViewModel;
            if (selectionChanged)
            {
                _pendingSelectedDeviceMac = reconciledSelection?.MacAddress;
                if (!string.Equals(
                        reconciledSelection?.MacAddress,
                        newSelection?.MacAddress,
                        StringComparison.OrdinalIgnoreCase))
                {
                    if (reconciledSelection is not null)
                    {
                        AnimateDeviceChange(reconciledSelection);
                    }
                    else
                    {
                        _pendingSelectedDeviceMac = null;
                        UpdateSelectedDevice(null, updateEditor: true);
                    }
                }
            }
            else
            {
                _pendingSelectedDeviceMac = null;
                UpdateSelectedDevice(reconciledSelection, updateEditor: true);
            }
            EndDeviceFilterAnimationCache();
        }

        _finishDeviceFilterTransition = Finish;
        enterAnimation.Completed += (_, _) => Finish();
        exitTranslate.BeginAnimation(
            TranslateTransform.XProperty,
            new DoubleAnimation(
                0,
                exitOffset,
                transitionDuration)
            {
                EasingFunction = ease
            });
        enterTranslate.BeginAnimation(
            TranslateTransform.XProperty,
            enterAnimation);
        GetDeviceListMotion(DeviceListTransition).BeginAnimation(
            OpacityProperty,
            new DoubleAnimation(0, 1, transitionDuration)
            {
                EasingFunction = ease
            });
        GetDeviceListMotion(DeviceList).BeginAnimation(
            OpacityProperty,
            new DoubleAnimation(1, 0, transitionDuration)
            {
                EasingFunction = ease
            });
    }

    private void StopDeviceFilterAnimations(
        TranslateTransform? exitTranslate = null,
        TranslateTransform? enterTranslate = null)
    {
        foreach(var list in new[]{DeviceList,DeviceListTransition})
            if(DeviceScrollBar(list) is { } bar) {bar.BeginAnimation(OpacityProperty,null);bar.ClearValue(VisibilityProperty);}

        GetDeviceListMotion(DeviceList).BeginAnimation(OpacityProperty, null);
        GetDeviceListMotion(DeviceListTransition).BeginAnimation(OpacityProperty, null);
        (exitTranslate ?? GetDeviceListMotion(DeviceList).RenderTransform as TranslateTransform)
            ?.BeginAnimation(TranslateTransform.XProperty, null);
        (enterTranslate ?? GetDeviceListMotion(DeviceListTransition).RenderTransform as TranslateTransform)
            ?.BeginAnimation(TranslateTransform.XProperty, null);
    }

    private void BeginDeviceFilterAnimationCache()
    {
        _deviceFilterAnimationCacheActive = true;
        GetDeviceListMotion(DeviceList).CacheMode = CreateAnimationBitmapCache();
        GetDeviceListMotion(DeviceListTransition).CacheMode = CreateAnimationBitmapCache();
    }

    private void EndDeviceFilterAnimationCache()
    {
        _deviceFilterAnimationCacheActive = false;
        RefreshAnimationCacheModes();
    }

    private void BeginDeviceDetailAnimationCache()
    {
        _deviceDetailAnimationCacheActive = true;
        DetailPanel.CacheMode = CreateAnimationBitmapCache();
    }

    private void EndDeviceDetailAnimationCache()
    {
        _deviceDetailAnimationCacheActive = false;
        RefreshAnimationCacheModes();
        if (!_isExiting) ReplaceVisibleDevices(preserveSelection: true, updateEditor: false);
    }

    private void RefreshAnimationCacheModes()
    {
        if (!_deviceFilterAnimationCacheActive)
        {
            GetDeviceListMotion(DeviceList).CacheMode = null;
            GetDeviceListMotion(DeviceListTransition).CacheMode = null;
        }

        if (!_deviceDetailAnimationCacheActive)
        {
            DetailPanel.CacheMode = null;
        }
    }

    private static BitmapCache CreateAnimationBitmapCache() =>
        new() { RenderAtScale = 1d };

    private void AnimateDeviceListFilterChange()
    {
        if (!SystemParameters.ClientAreaAnimation)
        {
            GetDeviceListMotion(DeviceList).Opacity = 1;
            GetDeviceListMotion(DeviceList).RenderTransform = Transform.Identity;
            return;
        }

        var translate = new TranslateTransform(0, 9);
        GetDeviceListMotion(DeviceList).RenderTransform = translate;
        GetDeviceListMotion(DeviceList).Opacity = 0.45;
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        GetDeviceListMotion(DeviceList).BeginAnimation(
            OpacityProperty,
            new DoubleAnimation(0.45, 1, TimeSpan.FromMilliseconds(360))
            {
                EasingFunction = easing
            });
        translate.BeginAnimation(
            TranslateTransform.YProperty,
            new DoubleAnimation(9, 0, TimeSpan.FromMilliseconds(360))
            {
                EasingFunction = easing
            });
    }

    private void DeviceList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isReplacingDevices || _isDeviceFilterTransitionActive)
        {
            return;
        }

        var device = DeviceList.SelectedItem as DeviceItemViewModel;
        if (_compactMode && !_compactDetail)
        {
            // The enclosing list/detail transition owns this navigation's motion.
            UpdateSelectedDevice(device, updateEditor:true);
            return;
        }
        var currentSelectionMac =
            _pendingSelectedDeviceMac ?? SelectedDevice?.MacAddress;
        var selectionChanged = !string.Equals(
            currentSelectionMac,
            device?.MacAddress,
            StringComparison.OrdinalIgnoreCase);
        _deviceProfileSaveCancellation?.Cancel();
        var returningToDevices = CurrentPage != PageKind.Devices;
        if (returningToDevices)
        {
            _pendingSelectedDeviceMac = null;
            UpdateSelectedDevice(device, updateEditor: true);
            ShowDevicesPanel(animate: true);
            return;
        }

        if (selectionChanged && device is not null && !returningToDevices)
        {
            _pendingSelectedDeviceMac = device.MacAddress;
            AnimateDeviceChange(device);
            return;
        }

        _pendingSelectedDeviceMac = null;
        UpdateSelectedDevice(device, updateEditor: true);
    }

    private void DeviceList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_isDeviceFilterTransitionActive ||
            e.OriginalSource is not DependencyObject source ||
            FindVisualParent<ListBoxItem>(source)?.DataContext is not DeviceItemViewModel device ||
            (CurrentPage == PageKind.Devices && !_compactMode))
        {
            return;
        }

        if (_compactMode)
        {
            DeviceList.SelectedItem = device;
            ShowDevicesPanel(animate: false);
            e.Handled = true;
            return;
        }
        // Commit the clicked row while the previous page is still current.
        // SelectionChanged then owns the single page-to-device transition;
        // navigating first would expose the old detail and animate it away.
        DeviceList.SelectedItem = device;
        if (CurrentPage != PageKind.Devices)
            ShowDevicesPanel(animate: true);
    }

    private void UpdateSelectedDevice(DeviceItemViewModel? device, bool updateEditor)
    {
        if (device is not null && device.TrafficSamples.Count == 0)
            device.Update(device.Data, GetTrafficHistory(device.MacAddress).Snapshot(DateTimeOffset.Now));
        var previousMac = SelectedDevice?.MacAddress;
        SelectedDevice = device;
        UpdateDetailSessionItems(device);
        if (!string.Equals(
                previousMac,
                device?.MacAddress,
                StringComparison.OrdinalIgnoreCase))
        {
            DetailPanel.ScrollToTop();
            HideConfirmation(RemoveDeviceConfirmBorder);
        }

        var hasDevice = device is not null;
        DeviceManagementSection.Visibility =
            device is { IsOnline: false }
                ? Visibility.Visible
                : Visibility.Collapsed;
        if (!_showingSettings && !_showingRouter)
        {
            EmptyPanel.Visibility = hasDevice ? Visibility.Collapsed : Visibility.Visible;
            DetailPanel.Visibility = hasDevice ? Visibility.Visible : Visibility.Collapsed;
        }

        if (device is null)
        {
            return;
        }

        if (updateEditor)
        {
            _isPopulatingDeviceSettings = true;
            try
            {
                CustomNameBox.Text = device.Profile.CustomName;
                NotifyStatusChangeCheck.IsChecked =
                    device.Profile.NotifyWhenOnline ||
                    device.Profile.NotifyWhenOffline;
                DeviceTypeCombo.SelectedItem = DeviceTypeCombo.Items
                    .Cast<DeviceTypeOption>()
                    .FirstOrDefault(option => option.TypeName == device.TypeName);
                AutoSaveStatusText.Text = "修改会自动保存";
            }
            finally
            {
                _isPopulatingDeviceSettings = false;
            }
        }

        DetailStatusText.Text = device.StatusText;
        DetailConnectionText.Text = device.ConnectionText == "网线接入" ? "网线" : device.ConnectionText;
        DetailStatusText.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "TextBrush");
    }

    private void RemoveDeviceButton_Click(object sender, RoutedEventArgs e) =>
        ShowConfirmation(RemoveDeviceConfirmBorder);

    private void CancelRemoveDeviceButton_Click(object sender, RoutedEventArgs e) =>
        HideConfirmation(RemoveDeviceConfirmBorder);

    private async void ConfirmRemoveDeviceButton_Click(object sender, RoutedEventArgs e)
    {
        var device = SelectedDevice;
        if (device is null)
        {
            HideConfirmation(RemoveDeviceConfirmBorder);
            return;
        }

        HideConfirmation(RemoveDeviceConfirmBorder);
        try
        {
            await _repository.RemoveDeviceUntilNextOnlineAsync(device.MacAddress);
            ReplaceDevices(
                await _repository.GetDevicesAsync(),
                preserveSelection: false,
                updateEditor: true);
            AnimateDeviceListFilterChange();
        }
        catch (Exception exception)
        {
            SettingsMessageText.Text = $"移除设备失败：{exception.Message}";
            SettingsMessageBorder.Visibility = Visibility.Visible;
        }
    }

    private void DeviceNameBox_TextChanged(object sender, TextChangedEventArgs e) =>
        QueueDeviceProfileSave(TimeSpan.FromMilliseconds(180));

    private void DeviceProfileControl_Changed(object sender, RoutedEventArgs e) =>
        QueueDeviceProfileSave(TimeSpan.FromMilliseconds(30));

    private void QueueDeviceProfileSave(TimeSpan delay)
    {
        if (_isPopulatingDeviceSettings ||
            _isReplacingDevices ||
            SelectedDevice is null)
        {
            return;
        }

        var selectedType = DeviceTypeCombo.SelectedItem as DeviceTypeOption;
        _pendingDeviceProfile = new PendingDeviceProfile(
            SelectedDevice.MacAddress,
            CustomNameBox.Text,
            selectedType?.TypeName ?? string.Empty,
            selectedType?.IconGlyph ?? string.Empty,
            NotifyStatusChangeCheck.IsChecked == true);

        _deviceProfileSaveCancellation?.Cancel();
        _deviceProfileSaveCancellation?.Dispose();
        _deviceProfileSaveCancellation = new CancellationTokenSource();
        AutoSaveStatusText.Text = "正在自动保存…";
        _ = SavePendingDeviceProfileAsync(delay, _deviceProfileSaveCancellation.Token);
    }

    private async Task SavePendingDeviceProfileAsync(
        TimeSpan delay,
        CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(delay, cancellationToken);
            var pending = _pendingDeviceProfile;
            if (pending is null)
            {
                return;
            }

            await _repository.UpdateDeviceProfileAsync(
                pending.MacAddress,
                pending.CustomName,
                pending.CustomType,
                pending.CustomIconGlyph,
                pending.NotifyWhenStatusChanges,
                cancellationToken);

            if (SelectedDevice?.MacAddress.Equals(
                    pending.MacAddress,
                    StringComparison.OrdinalIgnoreCase) == true)
            {
                ReplaceDevices(
                    await _repository.GetDevicesAsync(cancellationToken),
                    preserveSelection: true,
                    updateEditor: false);
                if (ReferenceEquals(_pendingDeviceProfile, pending))
                {
                    AutoSaveStatusText.Text = "已自动保存";
                }
            }

            if (ReferenceEquals(_pendingDeviceProfile, pending))
            {
                _pendingDeviceProfile = null;
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            AutoSaveStatusText.Text = $"保存失败：{exception.Message}";
        }
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        PopulateSettings(_repository.Settings);
        SettingsMessageBorder.Visibility = Visibility.Collapsed;
        ShowSettingsPanel();
    }

    private async void RouterButton_Click(object sender, RoutedEventArgs e)
    {
        ShowRouterPanel();
        await LoadRouterInfoAsync(showLoadingMessage: false);
    }

    private async void RefreshRouterInfoButton_Click(object sender, RoutedEventArgs e) =>
        await LoadRouterInfoAsync(showLoadingMessage: true);

    private async Task LoadRouterInfoAsync(bool showLoadingMessage)
    {
        RouterInfoLoadingText.Text = "正在读取上网信息与 Mesh 节点…";
        RouterInfoLoadingBorder.Visibility =
            GetRouterLoadingVisibility(showLoadingMessage);
        RefreshRouterInfoButton.IsEnabled = false;
        try
        {
            var internetInfoTask = _monitoringService.GetInternetInfoAsync();
            var meshNodesTask = _monitoringService.GetMeshNodesAsync(forceRefresh: showLoadingMessage);
            await Task.WhenAll(internetInfoTask, meshNodesTask);
            var info = await internetInfoTask;
            var meshNodes = await meshNodesTask;
            RouterConnectionBadgeText.Text = info.IsConnected ? "互联网已连接" : "互联网未连接";
            RouterConnectionBadge.Background = (System.Windows.Media.Brush)FindResource(
                info.IsConnected ? "StatusOnlineBrush" : "StatusOfflineBrush");
            RouterConnectionBadgeText.Foreground = (System.Windows.Media.Brush)FindResource(
                info.IsConnected ? "StatusOnlineTextBrush" : "StatusOfflineTextBrush");
            Ipv4TypeText.Text = info.Ipv4ConnectionType;
            Ipv4AddressText.Text = info.Ipv4Address;
            Ipv4MaskText.Text = info.Ipv4SubnetMask;
            Ipv4GatewayText.Text = info.Ipv4Gateway;
            Ipv4DnsText.Text = info.Ipv4Dns;
            Ipv6TypeText.Text = info.Ipv6ConnectionType;
            WanIpv6AddressText.Text = info.WanIpv6Address;
            WanIpv6GatewayText.Text = info.WanIpv6Gateway;
            LanIpv6AddressText.Text = info.LanIpv6Address;
            LanIpv6PrefixText.Text = info.LanIpv6Prefix;
            Ipv6DnsText.Text = info.Ipv6Dns;
            MeshNodesItemsControl.ItemsSource = meshNodes;
            MeshNodesEmptyText.Visibility = meshNodes.Count == 0
                ? Visibility.Visible
                : Visibility.Collapsed;
            RouterInfoLoadingBorder.Visibility = Visibility.Collapsed;
        }
        catch (Exception exception)
        {
            RouterInfoLoadingText.Text = $"读取失败：{exception.Message}";
            RouterInfoLoadingBorder.Visibility = Visibility.Visible;
        }
        finally
        {
            RefreshRouterInfoButton.IsEnabled = true;
        }
    }

    internal static Visibility GetRouterLoadingVisibility(
        bool showLoadingMessage) =>
        showLoadingMessage
            ? Visibility.Visible
            : Visibility.Collapsed;

    private void RestartRouterButton_Click(object sender, RoutedEventArgs e)
    {
        ShowConfirmation(RestartConfirmBorder);
    }

    private void CancelRestartButton_Click(object sender, RoutedEventArgs e)
    {
        HideConfirmation(RestartConfirmBorder);
    }

    private async void ConfirmRestartButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isRestartingRouter)
        {
            return;
        }

        _isRestartingRouter = true;
        HideConfirmation(RestartConfirmBorder);
        RouterRestartProgressBorder.Visibility = Visibility.Visible;
        RouterRestartProgressBar.Visibility = Visibility.Visible;
        RouterRestartProgressBorder.Background =
            (System.Windows.Media.Brush)FindResource("ProgressInfoBrush");
        RouterRestartStatusText.Text = "正在向路由器发送重启指令…";
        RestartRouterButton.IsEnabled = false;
        try
        {
            await _monitoringService.RestartRouterAsync();
            RouterRestartProgressBar.Visibility = Visibility.Collapsed;
            RouterRestartProgressBorder.Background =
                (System.Windows.Media.Brush)FindResource("ProgressSuccessBrush");
            RouterRestartStatusText.Text = "重启完成，路由器与互联网连接已恢复";
            AnimateFeedback(RouterRestartProgressBorder);
            await LoadRouterInfoAsync(showLoadingMessage: true);
        }
        catch (Exception exception)
        {
            RouterRestartProgressBar.Visibility = Visibility.Collapsed;
            RouterRestartProgressBorder.Background =
                (System.Windows.Media.Brush)FindResource("ProgressErrorBrush");
            RouterRestartStatusText.Text = $"重启未完成：{exception.Message}";
        }
        finally
        {
            _isRestartingRouter = false;
            RestartRouterButton.IsEnabled = true;
        }
    }

    private void ShowConfirmation(Border border)
    {
        border.Visibility = Visibility.Visible;
        var scale = new ScaleTransform(0.96, 0.96);
        var translate = new TranslateTransform(0, -6);
        var transform = new TransformGroup();
        transform.Children.Add(scale);
        transform.Children.Add(translate);
        border.RenderTransformOrigin = new System.Windows.Point(0.5, 0);
        border.RenderTransform = transform;
        border.Opacity = 0;
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        border.BeginAnimation(
            OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200))
            {
                EasingFunction = ease
            });
        scale.BeginAnimation(
            ScaleTransform.ScaleXProperty,
            new DoubleAnimation(0.96, 1, TimeSpan.FromMilliseconds(200))
            {
                EasingFunction = ease
            });
        scale.BeginAnimation(
            ScaleTransform.ScaleYProperty,
            new DoubleAnimation(0.96, 1, TimeSpan.FromMilliseconds(200))
            {
                EasingFunction = ease
            });
        translate.BeginAnimation(
            TranslateTransform.YProperty,
            new DoubleAnimation(-6, 0, TimeSpan.FromMilliseconds(200))
            {
                EasingFunction = ease
            });
    }

    private void HideConfirmation(Border border)
    {
        if (border.Visibility != Visibility.Visible)
        {
            return;
        }

        var scale = new ScaleTransform(1, 1);
        var translate = new TranslateTransform(0, 0);
        var transform = new TransformGroup();
        transform.Children.Add(scale);
        transform.Children.Add(translate);
        border.RenderTransformOrigin = new System.Windows.Point(0.5, 0);
        border.RenderTransform = transform;
        var ease = new QuadraticEase { EasingMode = EasingMode.EaseOut };
        var opacity = new DoubleAnimation(
            border.Opacity,
            0,
            TimeSpan.FromMilliseconds(160))
        {
            EasingFunction = ease
        };
        opacity.Completed += (_, _) =>
        {
            border.Visibility = Visibility.Collapsed;
            border.BeginAnimation(OpacityProperty, null);
            border.Opacity = 1;
            border.RenderTransform = Transform.Identity;
        };
        border.BeginAnimation(OpacityProperty, opacity);
        scale.BeginAnimation(
            ScaleTransform.ScaleXProperty,
            new DoubleAnimation(1, 0.97, TimeSpan.FromMilliseconds(160))
            {
                EasingFunction = ease
            });
        scale.BeginAnimation(
            ScaleTransform.ScaleYProperty,
            new DoubleAnimation(1, 0.97, TimeSpan.FromMilliseconds(160))
            {
                EasingFunction = ease
            });
        translate.BeginAnimation(
            TranslateTransform.YProperty,
            new DoubleAnimation(0, -4, TimeSpan.FromMilliseconds(160))
            {
                EasingFunction = ease
            });
    }

    private void Window_PreviewMouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        if (RemoveDeviceConfirmBorder.Visibility != Visibility.Visible &&
            RestartConfirmBorder.Visibility != Visibility.Visible)
        {
            return;
        }

        if (e.OriginalSource is not DependencyObject source ||
            FindVisualParent<Border>(
                source,
                border =>
                    border == RemoveDeviceConfirmBorder ||
                    border == RestartConfirmBorder) is not null)
        {
            return;
        }

        HideConfirmation(RemoveDeviceConfirmBorder);
        HideConfirmation(RestartConfirmBorder);
    }

    private void RouterConnectionSetting_Changed(object sender, RoutedEventArgs e)
    {
        RefreshPasswordIndicator();
        if (_isPopulatingAppSettings)
        {
            return;
        }

        if (ReferenceEquals(sender, RouterAddressBox))
        {
            _hasSavedRouterPassword = _credentialService.HasPassword(RouterAddressBox.Text.Trim());
            RefreshPasswordIndicator();
        }

        _routerSettingsSaveCancellation?.Cancel();
        _routerSettingsSaveCancellation?.Dispose();
        _routerSettingsSaveCancellation = new CancellationTokenSource();
        AppSettingsAutoSaveText.Text = "正在等待输入完成…";
        _ = ApplyRouterConnectionSettingsAsync(_routerSettingsSaveCancellation.Token);
    }

    private async Task ApplyRouterConnectionSettingsAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(700), cancellationToken);
            var address = RouterAddressBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(address))
            {
                AppSettingsAutoSaveText.Text = "请输入路由器地址";
                return;
            }

            var enteredPassword = RouterPasswordBox.Password;
            var password = string.IsNullOrEmpty(enteredPassword)
                ? _credentialService.ReadPassword(address) ?? string.Empty
                : enteredPassword;
            if (string.IsNullOrEmpty(password))
            {
                AppSettingsAutoSaveText.Text = "请输入管理员密码";
                return;
            }

            AppSettingsAutoSaveText.Text = "正在连接并应用…";
            var connected = await ConnectAsync(
                address,
                password,
                GetSelectedRefreshInterval(),
                navigateOnSuccess: false);
            cancellationToken.ThrowIfCancellationRequested();
            if (!connected)
            {
                AppSettingsAutoSaveText.Text = "连接失败，请检查设置";
                return;
            }

            await _repository.UpdateRouterAddressAsync(address, cancellationToken);
            if (!string.IsNullOrEmpty(enteredPassword))
            {
                _credentialService.SavePassword(address, enteredPassword);
                _isPopulatingAppSettings = true;
                try
                {
                    RouterPasswordBox.Clear();
                }
                finally
                {
                    _isPopulatingAppSettings = false;
                }
            }

            _hasSavedRouterPassword = true;
            RefreshPasswordIndicator();
            SettingsMessageBorder.Visibility = Visibility.Collapsed;
            AppSettingsAutoSaveText.Text = "已自动应用";
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            AppSettingsAutoSaveText.Text = $"自动应用失败：{exception.Message}";
        }
    }

    private void CancelSettingsButton_Click(object sender, RoutedEventArgs e) =>
        ShowDevicesPanel(animate: true);

    private void BackFromRouterButton_Click(object sender, RoutedEventArgs e) =>
        ShowDevicesPanel(animate: true);

    private void RefreshPasswordIndicator()
    {
        Controls.InputPlaceholder.SetSuppressed(RouterPasswordBox, _hasSavedRouterPassword);
        if (SavedRouterPasswordMask is not null)
            SavedRouterPasswordMask.Visibility = _hasSavedRouterPassword && RouterPasswordBox.Password.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void PopulateSettings(AppSettings settings)
    {
        _isPopulatingAppSettings = true;
        try
        {
            LanAccessCheck.IsChecked = settings.LanAccessEnabled;
            RouterAddressBox.Text = settings.RouterAddress;
            _hasSavedRouterPassword = _credentialService.HasPassword(settings.RouterAddress);
            RefreshPasswordIndicator();
            try
            {
                var startupEnabled = _startupRegistrationService.IsEnabled();
                if (startupEnabled)
                {
                    // 将旧版本的普通启动项迁移为托盘静默启动命令。
                    _startupRegistrationService.SetEnabled(true);
                }

                StartWithWindowsCheck.IsChecked = startupEnabled;
            }
            catch (SecurityException)
            {
                StartWithWindowsCheck.IsChecked = settings.StartWithWindows;
            }

            RefreshIntervalCombo.SelectedItem = RefreshIntervalCombo.Items
                .Cast<ComboBoxItem>()
                .FirstOrDefault(item =>
                    item.Tag?.ToString() == settings.RefreshIntervalSeconds.ToString())
                ?? RefreshIntervalCombo.Items
                    .Cast<ComboBoxItem>()
                    .First(item => item.Tag?.ToString() == "1");
            ThemeModeCombo.SelectedItem = ThemeModeCombo.Items
                .Cast<ComboBoxItem>()
                .FirstOrDefault(item =>
                    item.Tag?.ToString() == ThemeService.NormalizeMode(settings.ThemeMode))
                ?? ThemeModeCombo.Items.Cast<ComboBoxItem>().First();
            DoNotDisturbCheck.IsChecked = settings.DoNotDisturbEnabled;
            DoNotDisturbStartBox.Text = NotificationSchedule.FormatTime(
                settings.DoNotDisturbStartMinutes);
            DoNotDisturbEndBox.Text = NotificationSchedule.FormatTime(
                settings.DoNotDisturbEndMinutes);
            NotifyNewDeviceOnlineCheck.IsChecked = settings.NotifyWhenNewDeviceOnline;
            NotifyIpv6NonNativeCheck.IsChecked = settings.NotifyWhenIpv6NotNative;

        }
        finally
        {
            _isPopulatingAppSettings = false;
        }
    }

    private async void NewDeviceNotificationSetting_Changed(object sender, RoutedEventArgs e)
    {
        if (_isPopulatingAppSettings || _isExiting) return;
        _newDeviceNotificationSaveCancellation?.Cancel();
        _newDeviceNotificationSaveCancellation?.Dispose();
        _newDeviceNotificationSaveCancellation = new CancellationTokenSource();
        try
        {
            await _repository.UpdateNewDeviceNotificationSettingAsync(
                NotifyNewDeviceOnlineCheck.IsChecked == true,
                _newDeviceNotificationSaveCancellation.Token);
        }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            if (!_isExiting) ShowSettings($"保存新设备通知设置失败：{exception.Message}");
        }
    }

    private void AppBehaviorSetting_Changed(object sender, RoutedEventArgs e)
    {
        if (_isPopulatingAppSettings)
        {
            return;
        }

        if (ReferenceEquals(sender, ThemeModeCombo))
        {
            ApplyTheme(GetSelectedThemeMode(), animate: true);
        }

        QueueBehaviorSettingsSave();
    }

    private void QueueBehaviorSettingsSave()
    {
        _appSettingsSaveCancellation?.Cancel();
        _appSettingsSaveCancellation?.Dispose();
        _appSettingsSaveCancellation = new CancellationTokenSource();
        _ = SaveBehaviorSettingsAsync(_appSettingsSaveCancellation.Token);
    }

    private async Task SaveBehaviorSettingsAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(80), cancellationToken);
            var interval = GetSelectedRefreshInterval();
            var startWithWindows = StartWithWindowsCheck.IsChecked == true;
            var themeMode = GetSelectedThemeMode();
            if (!NotificationSchedule.TryParseTime(
                    DoNotDisturbStartBox.Text,
                    out var doNotDisturbStartMinutes) ||
                !NotificationSchedule.TryParseTime(
                    DoNotDisturbEndBox.Text,
                    out var doNotDisturbEndMinutes))
            {
                SettingsMessageText.Text = "免打扰时间格式不正确，请使用\u200924\u2009小时制，例如\u200922:30。";
                SettingsMessageBorder.Visibility = Visibility.Visible;
                return;
            }

            if (_startupRegistrationService.IsEnabled() != startWithWindows)
                _startupRegistrationService.SetEnabled(startWithWindows);
            await _repository.UpdateBehaviorSettingsAsync(
                interval,
                startWithWindows,
                themeMode,
                DoNotDisturbCheck.IsChecked == true,
                doNotDisturbStartMinutes,
                doNotDisturbEndMinutes,
                NotifyIpv6NonNativeCheck.IsChecked == true,
                cancellationToken);
            _monitoringService.UpdateRefreshInterval(interval);
            SettingsMessageBorder.Visibility = Visibility.Collapsed;
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            ShowSettings($"保存应用设置失败：{exception.Message}");
        }
    }

    private int GetSelectedRefreshInterval()
    {
        var interval = 1;
        if (RefreshIntervalCombo.SelectedItem is ComboBoxItem item)
        {
            int.TryParse(item.Tag?.ToString(), out interval);
        }

        return Math.Clamp(interval, 1, 60);
    }

    private string GetSelectedThemeMode() =>
        ThemeModeCombo.SelectedItem is ComboBoxItem item
            ? ThemeService.NormalizeMode(item.Tag?.ToString())
            : ThemeService.SystemMode;

    private void ApplyTheme(string mode, bool animate)
    {
        _currentThemeMode = ThemeService.NormalizeMode(mode);
        var isDark = ThemeService.Apply(_currentThemeMode);
        MicaWindowService.ApplyTheme(this, isDark);

        if (!animate || !SystemParameters.ClientAreaAnimation)
        {
            WindowRoot.Opacity = 1;
            return;
        }

        WindowRoot.BeginAnimation(
            OpacityProperty,
            new DoubleAnimation(0.72, 1, TimeSpan.FromMilliseconds(420))
            {
                EasingFunction = new ExponentialEase
                {
                    Exponent = 5,
                    EasingMode = EasingMode.EaseOut
                }
            });
    }

    private void SystemEvents_UserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (_currentThemeMode != ThemeService.SystemMode)
        {
            return;
        }

        Dispatcher.BeginInvoke(() => ApplyTheme(ThemeService.SystemMode, animate: true));
    }

    private void ShowSettings(string message)
    {
        SettingsMessageText.Text = message;
        SettingsMessageBorder.Visibility = Visibility.Visible;
        ShowSettingsPanel();
    }

    private void ShowSettingsPanel()
    {
        NavigateToPage(PageKind.Settings, animate: true);
    }

    private void ShowRouterPanel()
    {
        NavigateToPage(PageKind.Router, animate: true);
    }

    private void ShowDevicesPanel(bool animate, bool recordNavigation = true)
    {
        NavigateToPage(PageKind.Devices, animate, recordNavigation);
    }

    private void NavigateToPage(
        PageKind target,
        bool animate,
        bool recordNavigation = true)
    {
        var openingCompactPage = _compactMode && !_compactDetail;
        OpenCompactDetail();
        if(openingCompactPage)
        {
            // The list/detail container owns the full 600ms transition. Do not
            // run a second, longer page transition inside that moving container.
            animate=false;
            _pageAnimationGeneration++;
        }
        var current = CurrentPage;
        var previousPanel = GetPagePanel(current);
        if (current != target && recordNavigation)
        {
            _backNavigation.Push(current);
            _forwardNavigation.Clear();
        }

        _showingSettings = target == PageKind.Settings;
        _showingRouter = target == PageKind.Router;
        UpdateSidebarNavigation();

        FrameworkElement? panel = null;
        switch (target)
        {
            case PageKind.Settings:
                SettingsPanel.Visibility = Visibility.Visible;
                panel = SettingsPanel;
                break;
            case PageKind.Router:
                RouterPanel.Visibility = Visibility.Visible;
                panel = RouterPanel;
                break;
            default:
                UpdateSelectedDevice(
                    DeviceList.SelectedItem as DeviceItemViewModel,
                    updateEditor: false);
                panel = DetailPanel.Visibility == Visibility.Visible
                    ? DetailPanel
                    : EmptyPanel;
                break;
        }

        if (panel is null)
        {
            return;
        }

        if (animate &&
            previousPanel is not null &&
            !ReferenceEquals(previousPanel, panel))
        {
            AnimatePageTransition(previousPanel, panel);
            return;
        }

        ShowOnlyPagePanel(panel);
        ResetPagePanel(panel);
    }

    private FrameworkElement? GetPagePanel(PageKind page) => page switch
    {
        PageKind.Settings => SettingsPanel,
        PageKind.Router => RouterPanel,
        _ => DetailPanel.Visibility == Visibility.Visible
            ? DetailPanel
            : EmptyPanel
    };

    private void ShowOnlyPagePanel(FrameworkElement panel)
    {
        foreach (var candidate in GetPagePanels())
        {
            candidate.Visibility = ReferenceEquals(candidate, panel)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
    }

    private IEnumerable<FrameworkElement> GetPagePanels()
    {
        yield return SettingsPanel;
        yield return RouterPanel;
        yield return DetailPanel;
        yield return EmptyPanel;
    }

    private void MainWindow_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.XButton1)
        {
            NavigateBack();
            e.Handled = true;
        }
        else if (e.ChangedButton == MouseButton.XButton2)
        {
            NavigateForward();
            e.Handled = true;
        }
    }

    private void NavigateBack()
    {
        if (_compactMode && _compactDetail)
        {
            TransitionCompact(false);
            return;
        }
        if (_backNavigation.Count == 0)
        {
            return;
        }

        _forwardNavigation.Push(CurrentPage);
        NavigateToPage(_backNavigation.Pop(), animate: true, recordNavigation: false);
    }

    private void NavigateForward()
    {
        if (_forwardNavigation.Count == 0)
        {
            return;
        }

        _backNavigation.Push(CurrentPage);
        NavigateToPage(_forwardNavigation.Pop(), animate: true, recordNavigation: false);
    }

    private static void HandleMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source)
        {
            return;
        }

        if (FindVisualParent<System.Windows.Controls.ComboBox>(source) is not null)
        {
            e.Handled = true;
            return;
        }

        if (SmoothScrollBehavior.TryHandle(source, e))
        {
            e.Handled = true;
        }
    }

    private static T? FindVisualParent<T>(DependencyObject? current)
        where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T match)
            {
                return match;
            }

            current = current is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(current)
                : LogicalTreeHelper.GetParent(current);
        }

        return null;
    }

    private static T? FindVisualParent<T>(
        DependencyObject? current,
        Func<T, bool> predicate)
        where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T match && predicate(match))
            {
                return match;
            }

            current = current is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(current)
                : LogicalTreeHelper.GetParent(current);
        }

        return null;
    }

    private void AnimatePageTransition(
        FrameworkElement previousPanel,
        FrameworkElement panel)
    {
        if (!SystemParameters.ClientAreaAnimation)
        {
            ShowOnlyPagePanel(panel);
            ResetPagePanel(panel);
            return;
        }

        var generation = ++_pageAnimationGeneration;
        foreach (var candidate in GetPagePanels())
        {
            if (!ReferenceEquals(candidate, previousPanel) &&
                !ReferenceEquals(candidate, panel))
            {
                candidate.Visibility = Visibility.Collapsed;
            }
        }

        previousPanel.Visibility = Visibility.Visible;
        panel.Visibility = Visibility.Visible;
        System.Windows.Controls.Panel.SetZIndex(previousPanel, 1);
        System.Windows.Controls.Panel.SetZIndex(panel, 2);

        var entranceEase = new QuinticEase { EasingMode = EasingMode.EaseOut };
        var exitEase = new CubicEase { EasingMode = EasingMode.EaseInOut };

        var entranceScale = new ScaleTransform(0.975, 0.975);
        var entranceTranslate = new TranslateTransform(0, 18);
        var entranceTransform = new TransformGroup();
        entranceTransform.Children.Add(entranceScale);
        entranceTransform.Children.Add(entranceTranslate);
        panel.RenderTransformOrigin = new System.Windows.Point(0.5, 0.5);
        panel.RenderTransform = entranceTransform;
        panel.Opacity = 0;

        var exitScale = new ScaleTransform(1, 1);
        var exitTranslate = new TranslateTransform(0, 0);
        var exitTransform = new TransformGroup();
        exitTransform.Children.Add(exitScale);
        exitTransform.Children.Add(exitTranslate);
        previousPanel.RenderTransformOrigin = new System.Windows.Point(0.5, 0.5);
        previousPanel.RenderTransform = exitTransform;

        var exitOpacity = new DoubleAnimation(
            previousPanel.Opacity,
            0,
            TimeSpan.FromMilliseconds(260))
        {
            EasingFunction = exitEase
        };
        exitOpacity.Completed += (_, _) =>
        {
            if (generation != _pageAnimationGeneration)
            {
                return;
            }

            previousPanel.Visibility = Visibility.Collapsed;
            ResetPagePanel(previousPanel);
            System.Windows.Controls.Panel.SetZIndex(previousPanel, 0);
        };
        previousPanel.BeginAnimation(OpacityProperty, exitOpacity);
        exitScale.BeginAnimation(
            ScaleTransform.ScaleXProperty,
            new DoubleAnimation(1, 0.985, TimeSpan.FromMilliseconds(260))
            {
                EasingFunction = exitEase
            });
        exitScale.BeginAnimation(
            ScaleTransform.ScaleYProperty,
            new DoubleAnimation(1, 0.985, TimeSpan.FromMilliseconds(260))
            {
                EasingFunction = exitEase
            });
        exitTranslate.BeginAnimation(
            TranslateTransform.YProperty,
            new DoubleAnimation(0, -8, TimeSpan.FromMilliseconds(260))
            {
                EasingFunction = exitEase
            });

        panel.BeginAnimation(
            OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(720))
            {
                BeginTime = TimeSpan.FromMilliseconds(120),
                EasingFunction = entranceEase
            });
        var entranceScaleAnimation = new DoubleAnimation(
            0.975,
            1,
            TimeSpan.FromMilliseconds(780))
        {
            BeginTime = TimeSpan.FromMilliseconds(120),
            EasingFunction = entranceEase
        };
        entranceScaleAnimation.Completed += (_, _) =>
        {
            if (generation != _pageAnimationGeneration ||
                !ReferenceEquals(panel.RenderTransform, entranceTransform))
            {
                return;
            }

            ResetPagePanel(panel);
            System.Windows.Controls.Panel.SetZIndex(panel, 0);
        };
        entranceScale.BeginAnimation(
            ScaleTransform.ScaleXProperty,
            entranceScaleAnimation);
        entranceScale.BeginAnimation(
            ScaleTransform.ScaleYProperty,
            new DoubleAnimation(0.975, 1, TimeSpan.FromMilliseconds(780))
            {
                BeginTime = TimeSpan.FromMilliseconds(120),
                EasingFunction = entranceEase
            });
        entranceTranslate.BeginAnimation(
            TranslateTransform.YProperty,
            new DoubleAnimation(18, 0, TimeSpan.FromMilliseconds(780))
            {
                BeginTime = TimeSpan.FromMilliseconds(120),
                EasingFunction = entranceEase
            });
    }

    private static void ResetPagePanel(FrameworkElement panel)
    {
        panel.BeginAnimation(OpacityProperty, null);
        panel.Opacity = 1;
        panel.RenderTransform = Transform.Identity;
        panel.RenderTransformOrigin = new System.Windows.Point(0.5, 0.5);
    }

    private void AnimateDeviceChange(DeviceItemViewModel? device)
    {
        if (device is null) { ++_deviceAnimationGeneration; ResetPagePanel(DetailPanel); UpdateSelectedDevice(null,true); _pendingSelectedDeviceMac=null; EndDeviceDetailAnimationCache(); return; }
        if (!SystemParameters.ClientAreaAnimation ||
            DetailPanel.Visibility != Visibility.Visible)
        {
            UpdateSelectedDevice(device, updateEditor: true);
            _pendingSelectedDeviceMac = null;
            return;
        }

        var generation = ++_deviceAnimationGeneration;
        var animationTiming = GetSynchronizedDeviceFilterAnimationTiming();
        BeginDeviceDetailAnimationCache();
        var exitEase = new CubicEase { EasingMode = EasingMode.EaseInOut };
        var exitScale = new ScaleTransform(1, 1);
        var exitTranslate = new TranslateTransform(0, 0);
        var exitTransform = new TransformGroup();
        exitTransform.Children.Add(exitScale);
        exitTransform.Children.Add(exitTranslate);
        DetailPanel.RenderTransformOrigin = new System.Windows.Point(0.5, 0.5);
        DetailPanel.RenderTransform = exitTransform;

        var exitOpacity = new DoubleAnimation(
            DetailPanel.Opacity,
            0,
            TimeSpan.FromMilliseconds(animationTiming.DetailExitDurationMilliseconds))
        {
            EasingFunction = exitEase
        };
        exitOpacity.Completed += (_, _) =>
        {
            if (generation != _deviceAnimationGeneration)
            {
                return;
            }

            var currentDevice = _devices.FirstOrDefault(candidate =>
                candidate.MacAddress.Equals(
                    device.MacAddress,
                    StringComparison.OrdinalIgnoreCase)) ?? device;
            UpdateSelectedDevice(currentDevice, updateEditor: true);
            _pendingSelectedDeviceMac = null;
            var entranceEase = new QuinticEase { EasingMode = EasingMode.EaseOut };
            var entranceScale = new ScaleTransform(0.975, 0.975);
            var entranceTranslate = new TranslateTransform(0, 16);
            var entranceTransform = new TransformGroup();
            entranceTransform.Children.Add(entranceScale);
            entranceTransform.Children.Add(entranceTranslate);
            DetailPanel.RenderTransform = entranceTransform;
            DetailPanel.Opacity = 0;

            DetailPanel.BeginAnimation(
                OpacityProperty,
                new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(animationTiming.DetailEntranceDurationMilliseconds * 720d / 780d))
                {
                    EasingFunction = entranceEase
                });
            entranceTranslate.BeginAnimation(
                TranslateTransform.YProperty,
                new DoubleAnimation(
                    16,
                    0,
                    TimeSpan.FromMilliseconds(animationTiming.DetailEntranceDurationMilliseconds))
                {
                    EasingFunction = entranceEase
                });
            var entranceScaleAnimation = new DoubleAnimation(
                0.975,
                1,
                TimeSpan.FromMilliseconds(animationTiming.DetailEntranceDurationMilliseconds))
            {
                EasingFunction = entranceEase
            };
            entranceScaleAnimation.Completed += (_, _) =>
            {
                if (generation == _deviceAnimationGeneration)
                {
                    ResetPagePanel(DetailPanel);
                    EndDeviceDetailAnimationCache();
                }
            };
            entranceScale.BeginAnimation(
                ScaleTransform.ScaleXProperty,
                entranceScaleAnimation);
            entranceScale.BeginAnimation(
                ScaleTransform.ScaleYProperty,
                new DoubleAnimation(
                    0.975,
                    1,
                    TimeSpan.FromMilliseconds(animationTiming.DetailEntranceDurationMilliseconds))
                {
                    EasingFunction = entranceEase
                });
        };
        DetailPanel.BeginAnimation(OpacityProperty, exitOpacity);
        exitScale.BeginAnimation(
            ScaleTransform.ScaleXProperty,
            new DoubleAnimation(
                1,
                0.985,
                TimeSpan.FromMilliseconds(animationTiming.DetailExitDurationMilliseconds))
            {
                EasingFunction = exitEase
            });
        exitScale.BeginAnimation(
            ScaleTransform.ScaleYProperty,
            new DoubleAnimation(
                1,
                0.985,
                TimeSpan.FromMilliseconds(animationTiming.DetailExitDurationMilliseconds))
            {
                EasingFunction = exitEase
            });
        exitTranslate.BeginAnimation(
            TranslateTransform.YProperty,
            new DoubleAnimation(
                0,
                -8,
                TimeSpan.FromMilliseconds(animationTiming.DetailExitDurationMilliseconds))
            {
                EasingFunction = exitEase
            });
    }

    private static void AnimateFeedback(FrameworkElement element)
    {
        if (!SystemParameters.ClientAreaAnimation)
        {
            return;
        }

        element.BeginAnimation(
            OpacityProperty,
            new DoubleAnimation(0.55, 1, TimeSpan.FromMilliseconds(180))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });
    }

    private async void TrayTitleMenu(object sender, MouseButtonEventArgs e)
    {
        if (trayEditor) return;
        e.Handled = true;
        try { await TrayPage.Vm.Pipe.RequestAsync("assistant.menu"); }
        catch (Exception error) { TrayPage.Vm.ReportError(error); }
    }
    private void TrayTitleDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed) { e.Handled = true; DragMove(); }
    }
    private async void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (trayEditor || ReTray_App.App.CanCloseInterface) return;
        e.Cancel = true;
        await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Normal);
        await TrayPage.FlushChangesAsync();
        TrayPage.Suspend();
        RequestHideToTrayPreservingVisual();
    }

    private void MainWindow_SourceInitialized(object? sender, EventArgs e)
    {
        _windowSource = HwndSource.FromHwnd(
            new WindowInteropHelper(this).Handle);
        _windowSource?.AddHook(MainWindowWindowProcedure);
    }

    private IntPtr MainWindowWindowProcedure(
        IntPtr windowHandle,
        int message,
        IntPtr wordParameter,
        IntPtr longParameter,
        ref bool handled)
    {
        if (message == ExitInterfaceMessage)
        {
            handled = true;
            _ = Dispatcher.BeginInvoke(new Action(() => _ = ReTray_App.App.QuitAsync()));
            return IntPtr.Zero;
        }
        if (message == 0x0231)
        {
            // This HWND has a custom client-drawn frame. Keep live sizing on
            // WPF's asynchronous present path instead of waiting for the GPU
            // after every native size message. WM_SIZE still updates layout;
            // the ordinary exit-size refresh restores the final full frame.
            handled = true;
            return IntPtr.Zero;
        }
        if(message==0x0083 && NativeMaximizeBounds.ConstrainClient(windowHandle,longParameter))
        {
            handled=true;
            return IntPtr.Zero;
        }
        if (message == WindowMessageSystemCommand &&
            (wordParameter.ToInt64() & 0xFFF0) == SystemCommandMinimize &&
            !_forwardingMinimizeSystemCommand)
        {
            handled = true;
            RequestMinimizeWithTransitionMaterial();
        }

        if(message is 0x0232 or 0x031E or 0x02E0)
            QueueBackdropRefresh();
        return IntPtr.Zero;
    }

    internal void RequestNativeMinimize() => SystemCommands.MinimizeWindow(this);

    private void MinimizeWindowButton_Click(object sender,RoutedEventArgs e) => RequestMinimizeWithTransitionMaterial();

    internal void RequestMinimizeWithTransitionMaterial()
    {
        if (_minimizeTransitionPending ||
            WindowState == WindowState.Minimized ||
            _nativeTrayHideRequested || _nativeTrayHidden ||
            _isExiting)
        {
            return;
        }

        _minimizeTransitionPending = true;
        QuiesceDeviceAnimationsForWindowTransition();
        WindowTransitionSnapshot.Source = null;
        WindowTransitionSnapshot.Visibility = Visibility.Collapsed;
        _minimizeStableMaterialFramesObserved = 0;
        _minimizeSnapshotFramesObserved = 0;
        _minimizeSnapshotCapturedAfterStableFrame = false;
        _minimizeTransitionRenderPhase =
            MinimizeTransitionRenderPhase.StabilizingLiveMaterial;
        StartMinimizeSnapshotRendering();
        WindowRoot.InvalidateVisual();
    }

    private void StartMinimizeSnapshotRendering()
    {
        StopMinimizeSnapshotRendering();
        _minimizeRenderOperation = Dispatcher.BeginInvoke(new Action(() =>
        {
            _minimizeRenderOperation = null;
            if (_minimizeTransitionRenderPhase == MinimizeTransitionRenderPhase.StabilizingLiveMaterial)
                CaptureMinimizeSnapshotAfterStableFrame();
            else if (_minimizeTransitionRenderPhase == MinimizeTransitionRenderPhase.CommittingSnapshot)
                CommitMinimizeAfterTransitionSnapshot();
        }), System.Windows.Threading.DispatcherPriority.Background);
    }

    private void CaptureMinimizeSnapshotAfterStableFrame()
    {
        if (!_minimizeTransitionPending ||
            _isExiting ||
            _minimizeTransitionRenderPhase !=
                MinimizeTransitionRenderPhase.StabilizingLiveMaterial)
        {
            return;
        }

        // Rendering is raised before WPF sends this frame to its render thread.
        // Background priority alone does not wait for that thread to present.
        // Finish the stable live frame before reading the composed pixels.
        _ = MicaWindowService.FlushComposition(this);
        if (!_minimizeTransitionPending || _isExiting ||
            _nativeTrayHideRequested || _nativeTrayHidden ||
            _minimizeTransitionRenderPhase != MinimizeTransitionRenderPhase.StabilizingLiveMaterial)
            return;
        _minimizeStableMaterialFramesObserved++;
        var transitionSnapshot =
            MicaWindowService.CaptureWindowSnapshot(this);
        _minimizeSnapshotCapturedAfterStableFrame =
            _minimizeStableMaterialFramesObserved > 0 &&
            transitionSnapshot is not null;
        if (transitionSnapshot is not null)
        {
            WindowTransitionSnapshot.Source = transitionSnapshot;
            WindowTransitionSnapshot.Visibility = Visibility.Visible;
        }

        // Prepare the complete opaque surface BEFORE the next render, not in
        // the callback that starts the native animation. Otherwise this change
        // is still queued when DWM captures the transparent client surface.
        MicaWindowService.PrepareForMinimizeTransition(
            this,
            WindowRoot,
            ThemeService.ResolveIsDark(_currentThemeMode),
            transitionSnapshot);
        if (!_minimizeTransitionPending || _isExiting ||
            _nativeTrayHideRequested || _nativeTrayHidden)
            return;

        _minimizeTransitionRenderPhase =
            MinimizeTransitionRenderPhase.CommittingSnapshot;
        WindowTransitionSnapshot.InvalidateVisual();
        WindowRoot.InvalidateVisual();
        CommitMinimizeAfterTransitionSnapshot();
    }

    private void StopMinimizeSnapshotRendering()
    {
        _minimizeRenderOperation?.Abort();
        _minimizeRenderOperation = null;
    }

    private void CommitMinimizeAfterTransitionSnapshot()
    {
        if (!_minimizeTransitionPending ||
            WindowState == WindowState.Minimized ||
            _isExiting ||
            _minimizeTransitionRenderPhase !=
                MinimizeTransitionRenderPhase.CommittingSnapshot)
        {
            return;
        }

        // Wait for WPF's pending batch and the DWM present, in that order.
        // An observed Rendering event is not a GPU completion notification.
        _ = MicaWindowService.FlushComposition(this);
        if (!_minimizeTransitionPending || _isExiting ||
            _nativeTrayHideRequested || _nativeTrayHidden ||
            _minimizeTransitionRenderPhase != MinimizeTransitionRenderPhase.CommittingSnapshot)
            return;
        _minimizeSnapshotFramesObserved++;

        _forwardingMinimizeSystemCommand = true;
        try
        {
            WindowState = WindowState.Minimized;
        }
        finally
        {
            _forwardingMinimizeSystemCommand = false;
        }

        if (WindowState != WindowState.Minimized)
        {
            CompleteMinimizeTransition();
        }
    }

    private bool _backdropRefreshQueued;
    private void QueueBackdropRefresh()
    {
        if(_backdropRefreshQueued || _isExiting) return;
        _backdropRefreshQueued=true;
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ContextIdle,new Action(()=>
        {
            _backdropRefreshQueued=false;
            if(!_isExiting && IsVisible && WindowState!=WindowState.Minimized)
            {
                MicaWindowService.Apply(this,ThemeService.ResolveIsDark(_currentThemeMode));
            }
        }));
    }

    private void RequestHideToTrayPreservingVisual()
    {
        if (_isExiting ||
            _nativeTrayHideRequested ||
            _nativeTrayHidden)
        {
            return;
        }

        _nativeTrayHideRequested = true;
        if (_minimizeTransitionPending) CompleteMinimizeTransition();
        QuiesceDeviceAnimationsForWindowTransition();
        MinimizeToTrayRequested?.Invoke(this, EventArgs.Empty);
        _ = Dispatcher.BeginInvoke(
            CompleteNativeTrayHideRequest,
            System.Windows.Threading.DispatcherPriority.Normal);
    }

    private void CompleteNativeTrayHideRequest()
    {
        // A subscriber enqueues HideToTrayPreservingVisual before this
        // callback. Clear an unhandled request so the close button remains
        // usable even if tray integration is unavailable.
        _nativeTrayHideRequested = false;
    }

    internal void HideToTrayPreservingVisual()
    {
        if (_isExiting ||
            !_nativeTrayHideRequested)
        {
            return;
        }

        var handle = new WindowInteropHelper(this).Handle;
        if (handle != IntPtr.Zero)
        {
            _ = ShowWindow(handle, ShowWindowHide);
            _nativeTrayHidden = !IsWindowVisible(handle);
        }

        _nativeTrayHideRequested = false;
    }

    internal void RestoreFromTray()
    {
        if (_nativeTrayHidden)
        {
            var handle = new WindowInteropHelper(this).Handle;
            if (handle != IntPtr.Zero)
            {
                _ = ShowWindow(handle, ShowWindowRestore);
                _nativeTrayHidden = !IsWindowVisible(handle);
            }

            return;
        }

        ShowInTaskbar = true;
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }
    }

    private void QuiesceDeviceAnimationsForWindowTransition()
    {
        // A filter transition keeps two ListBoxes and the detail panel in
        // BitmapCache-backed animations. If those clocks remain active after
        // the minimize material has been rendered, they can rewrite the WPF
        // redirection surface while DWM is taking its minimize snapshot.
        _finishDeviceFilterTransition?.Invoke();

        if (!_deviceFilterAnimationCacheActive &&
            !_deviceDetailAnimationCacheActive)
        {
            return;
        }

        var pendingSelection = _pendingSelectedDeviceMac is null
            ? DeviceList.SelectedItem as DeviceItemViewModel
            : _devices.FirstOrDefault(device =>
                device.MacAddress.Equals(
                    _pendingSelectedDeviceMac,
                    StringComparison.OrdinalIgnoreCase));

        // Invalidate detail-animation callbacks before committing the target
        // selection. ResetPagePanel removes the active opacity clock and
        // detaches its animated transform in one stable frame.
        _deviceAnimationGeneration++;
        _pendingSelectedDeviceMac = null;
        UpdateSelectedDevice(pendingSelection, updateEditor: true);
        ResetPagePanel(DetailPanel);
        _deviceFilterAnimationCacheActive = false;
        _deviceDetailAnimationCacheActive = false;
        RefreshAnimationCacheModes();
        WindowRoot.UpdateLayout();
    }

    private void CompleteMinimizeTransition()
    {
        StopMinimizeSnapshotRendering();
        _minimizeTransitionRenderPhase = MinimizeTransitionRenderPhase.None;
        WindowTransitionSnapshot.Source = null;
        WindowTransitionSnapshot.Visibility = Visibility.Collapsed;
        MicaWindowService.CompleteMinimizeTransition(WindowRoot);
        _minimizeTransitionPending = false;
    }

    private void MaximizeWindowButton_Click(object sender, RoutedEventArgs e)
    {
        if (WindowState == WindowState.Maximized)
        {
            SystemCommands.RestoreWindow(this);
            return;
        }

        SystemCommands.MaximizeWindow(this);
    }

    private void CloseWindowButton_Click(object sender, RoutedEventArgs e) =>
        SystemCommands.CloseWindow(this);

    private void Window_StateChanged(object? sender, EventArgs e)
    {
        QueueBackdropRefresh();
        if (_minimizeTransitionPending &&
            WindowState != WindowState.Minimized)
        {
            CompleteMinimizeTransition();
        }

        if (MaximizeWindowGlyph is null)
        {
            return;
        }

        var isMaximized = WindowState == WindowState.Maximized;
        MaximizeWindowGlyph.Text = isMaximized ? "\uE923" : "\uE922";
        MaximizeWindowButton.ToolTip = isMaximized ? "还原" : "最大化";
    }

    public void PrepareForExit()
    {
        if (_isExiting)
        {
            return;
        }

        _isExiting = true;
        _responsiveModeOperation?.Abort();
        _responsiveModeOperation = null;
        if (_minimizeTransitionPending) CompleteMinimizeTransition();
        else StopMinimizeSnapshotRendering();
        _nativeTrayHideRequested = false;
        _nativeTrayHidden = false;
        _windowSource?.RemoveHook(MainWindowWindowProcedure);
        _windowSource = null;
        Interlocked.Exchange(ref _pendingMonitoringUpdate, null);
        lock (_notificationDispatchGate)
        {
            _pendingNotificationUpdate = null;
        }

        lock (_statusUpdateDispatchGate)
        {
            _pendingStatusUpdate = null;
        }

        Hide();

        _deviceProfileSaveCancellation?.Cancel();
        _deviceProfileSaveCancellation?.Dispose();
        _deviceProfileSaveCancellation = null;
        _appSettingsSaveCancellation?.Cancel();
        _appSettingsSaveCancellation?.Dispose();
        _appSettingsSaveCancellation = null;
        _newDeviceNotificationSaveCancellation?.Cancel();
        _newDeviceNotificationSaveCancellation?.Dispose();
        _newDeviceNotificationSaveCancellation = null;
        _routerSettingsSaveCancellation?.Cancel();
        _routerSettingsSaveCancellation?.Dispose();
        _routerSettingsSaveCancellation = null;
        SystemEvents.UserPreferenceChanged -= SystemEvents_UserPreferenceChanged;

        _monitoringService.Updated -= MonitoringService_Updated;
        _monitoringService.MeshTopologyUpdated -= MonitoringService_MeshTopologyUpdated;
        _monitoringService.ConnectionStateChanged -= MonitoringService_ConnectionStateChanged;
        _monitoringService.MonitoringError -= MonitoringService_MonitoringError;
        _monitoringService.RouterRestartProgressChanged -=
            MonitoringService_RouterRestartProgressChanged;
        _monitoringService.Dispose();
        if (_lanWebServer is not null) _ = _lanWebServer.StopAsync();
        _pendingDeviceProfile = null;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private sealed record PendingDeviceProfile(
        string MacAddress,
        string CustomName,
        string CustomType,
        string CustomIconGlyph,
        bool NotifyWhenStatusChanges);

    private enum MinimizeTransitionRenderPhase
    {
        None,
        StabilizingLiveMaterial,
        CommittingSnapshot
    }

    private enum PageKind
    {
        Devices,
        Router,
        Settings
    }

    private enum DeviceListFilter
    {
        Online,
        Offline
    }

    private const int ShowWindowHide = 0;
    private const int ShowWindowRestore = 9;

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr windowHandle, int command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr windowHandle);
}
