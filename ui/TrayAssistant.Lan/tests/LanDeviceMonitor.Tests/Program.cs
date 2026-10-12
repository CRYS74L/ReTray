using LanDeviceMonitor;
using LanDeviceMonitor.Controls;
using LanDeviceMonitor.Models;
using LanDeviceMonitor.Services;
using LanDeviceMonitor.ViewModels;
using System.Diagnostics;
using System.Reflection;
if(args.Contains("--device-name-spacing-regression")) return DeviceNameSpacingRegression.Run();
if(args.Contains("--mesh-membership-regression")) return await MeshMembershipRegression.RunAsync();
if(args.Contains("--resize-cache-regression")) return ResizeCacheRegression.Run();
if(args.Contains("--resize-layout-regression")) return ResizeLayoutRegression.Run();
if(args.Contains("--minimize-composition-regression")) return MinimizeCompositionRegression.Run();
if(args.Length == 2 && args[0] == "--analyze-minimize-recording") return MinimizeCompositionRegression.Analyze(args[1]);
if(args.Contains("--history-disclosure-regression")) return AppleControlsRegression.Run(historyOnly:true);
if(args.Contains("--history-native-ring-regression")) return AppleControlsRegression.Run(ringOnly:true);
if(args.Contains("--text-editing-regression")) return AppleControlsRegression.Run(editingOnly:true);
if(args.Contains("--status-appearance-regression")) return AppleControlsRegression.Run(appearanceOnly:true);
if(args.Contains("--visual-polish-regression")) return AppleControlsRegression.Run(visualPolishOnly:true);
if(args.Contains("--settings-connection-regression")) return await SettingsConnectionRegression.RunAsync();
if(args.Contains("--page-device-navigation-regression")) return PageDeviceNavigationRegression.Run();
if(args.Contains("--metric-tooltip-scroll-regression")) return MetricToolTipScrollRegression.Run();
if(args.Contains("--history-row-layout-regression")) return HistoryRowLayoutRegression.Run();
if(args.Contains("--detail-responsive-layout-regression")) return DetailResponsiveLayoutRegression.Run();
if(args.Contains("--portrait-caption-back-regression")) return PortraitCaptionBackRegression.Run();
if(args.Contains("--caption-initial-state-regression")) return PortraitCaptionBackRegression.Run(initialOnly: true);
if(args.Contains("--caption-initial-state-dark-regression")) return PortraitCaptionBackRegression.Run(initialOnly: true, initialTheme: ThemeService.DarkMode);
if(args.Contains("--tray-click-regression")) return TrayClickRegression.Run();
if(args.Contains("--startup-portrait-regression")) return StartupPortraitRegression.Run();
if(args.Contains("--desktop-web-reference-capture")) return DesktopWebReferenceCapture.Run();
if(args.Contains("--device-category-regression"))
{
    var profile=new DeviceProfile { MacAddress="02:00:00:00:44:01", CustomType="空气检测仪" };
    var identity=DeviceIdentityService.Identify("","cgllc-airm-cgd1",1);
    var data=new DeviceViewData { Profile=profile,Identity=identity,Sessions=[] };
    if(identity.TypeName!="监测设备" || identity.SuggestedName!="空气检测仪" || data.TypeName!="监测设备" || profile.CustomType!="空气检测仪")
        throw new Exception("监测类别兼容或具体设备名称发生错误");
    if(LanDeviceMonitor.Services.WebAccess.WebDeviceSnapshot.From(data).CustomType!="监测设备") throw new Exception("网页仍显示旧类别");
    Console.WriteLine("Device category PASS: monitoring category, legacy alias preserved without data overwrite, web display normalized.");return 0;
}

if (args.Contains("--apple-controls-regression", StringComparer.OrdinalIgnoreCase))
    return AppleControlsRegression.Run();
if(args.Contains("--sidebar-topology-regression")) return AppleControlsRegression.Run(sidebarPolishOnly:true);
if(args.Contains("--restored-window-regression")) return AppleControlsRegression.Run(restoredWindowOnly:true);
if(args.Contains("--thin-space-regression")) {
    if(DeviceItemViewModel.FormatSpeed(1024)!="1.00\u2009KB/s" || TrafficChart.FormatRate(1024)!="1.00\u2009KB") throw new Exception("Thin space format mismatch");
    Console.WriteLine("Thin space formatting PASS: rate values and chart labels."); return 0;
}
if(args.Contains("--refinement-regression")) return AppleControlsRegression.Run(refinementOnly:true);
if(args.Contains("--responsive-navigation-regression")) return AppleControlsRegression.Run(responsiveOnly:true);
if(args.Contains("--responsive-sizing-regression")) return ResponsiveSizingRegression.Run();
if(args.Contains("--filter-performance-probe")) return AppleControlsRegression.Run(performanceOnly:true);
if (args.Contains("--popup-alignment-regression", StringComparer.OrdinalIgnoreCase))
    return AppleControlsRegression.Run(popupOnly: true);

if (args.Contains("--web-preview", StringComparer.OrdinalIgnoreCase))
{
    var portArgument=args.FirstOrDefault(arg=>arg.StartsWith("--web-preview-port=",StringComparison.Ordinal));
    var previewPort=portArgument is not null && int.TryParse(portArgument.Split('=')[1],out var parsedPreviewPort) ? parsedPreviewPort : 0;
    return await WebAccessTests.PreviewAsync(previewPort);
}

if (args.Contains("--web-access-regression", StringComparer.OrdinalIgnoreCase))
{
    return await WebAccessTests.RunAsync();
}

if (args.Contains("--startup-smoke", StringComparer.OrdinalIgnoreCase))
{
    return RunStartupSmoke();
}


if (args.Contains("--settings-shortcut-regression", StringComparer.OrdinalIgnoreCase))
{
    return RunSettingsShortcutRegression();
}

if (args.Contains("--notification-once-regression", StringComparer.OrdinalIgnoreCase))
{
    return RunNotificationOnceRegression();
}
if (args.Contains("--new-device-notification-regression")) return await NewDeviceNotificationRegression.RunAsync();

if (args.Contains("--traffic-diagnostic", StringComparer.OrdinalIgnoreCase))
{
    return await RunTrafficDiagnosticAsync();
}

if (args.Contains("--realtime-rate-regression", StringComparer.OrdinalIgnoreCase))
{
    return await RunRealtimeRateRegressionAsync();
}

if (args.Contains("--offline-confirmation-regression", StringComparer.OrdinalIgnoreCase))
{
    return await RunOfflineConfirmationRegressionAsync();
}

if (args.Contains("--poll-cadence-regression", StringComparer.OrdinalIgnoreCase))
{
    return RunPollCadenceRegression();
}

if (args.Contains("--chart-regression", StringComparer.OrdinalIgnoreCase))
{
    return RunChartRegression();
}

if (args.Contains("--filter-animation-regression", StringComparer.OrdinalIgnoreCase))
{
    return RunFilterAnimationRegression();
}

if (args.Contains("--synchronized-filter-animation-regression", StringComparer.OrdinalIgnoreCase))
{
    return RunSynchronizedFilterAnimationRegression();
}

if (args.Contains("--rapid-filter-reversal-regression", StringComparer.OrdinalIgnoreCase))
{
    return RunRapidFilterReversalRegression();
}

if (args.Contains("--minimize-during-filter-transition-regression", StringComparer.OrdinalIgnoreCase))
{
    return RunMinimizeDuringFilterTransitionRegression();
}

if (args.Contains("--close-to-tray-transition-regression", StringComparer.OrdinalIgnoreCase))
{
    return RunCloseToTrayTransitionRegression();
}

if (args.Contains("--rate-arrow-regression", StringComparer.OrdinalIgnoreCase))
{
    return RunRateArrowRegression();
}

if (args.Contains("--total-arrow-regression", StringComparer.OrdinalIgnoreCase))
{
    return RunTotalArrowRegression();
}

if (args.Contains("--traffic-accent-regression", StringComparer.OrdinalIgnoreCase))
{
    return RunTrafficAccentRegression();
}

var failures = new List<string>();
var checksRun = 0;

Check(
    TrayService.BuildNotifyIconText() == "局域网设备助手",
    "托盘悬停文字应只显示应用名称");
Check(
    MonitoringService.ShouldPublishConnectionState(null, "已连接") &&
    !MonitoringService.ShouldPublishConnectionState("已连接", "已连接") &&
    MonitoringService.ShouldPublishConnectionState("已连接", "连接异常，正在重试"),
    "监控连接状态未变化时不应每秒重复投递 UI 状态更新");
Check(
    await MonitoringLoopRunsOffUiContextAsync(),
    "轮询应按用户选择的固定节奏触发、不能因请求耗时额外跳秒，并且不能捕获 UI 同步上下文");
Check(
    MonitoringLoop.GetDelayAfterIteration(
        TimeSpan.FromSeconds(1),
        TimeSpan.FromMilliseconds(250)) == TimeSpan.FromMilliseconds(750) &&
    MonitoringLoop.GetDelayAfterIteration(
        TimeSpan.FromSeconds(1),
        TimeSpan.FromMilliseconds(1500)) == TimeSpan.FromMilliseconds(100),
    "轮询请求未超过周期时应补足剩余时间，超过周期时不应再额外等待完整周期");
var internetRefreshBaseline = DateTimeOffset.UtcNow;
Check(
    !MonitoringService.ShouldRefreshInternetInfo(
        internetRefreshBaseline,
        internetRefreshBaseline.AddSeconds(1)) &&
    MonitoringService.ShouldRefreshInternetInfo(
        internetRefreshBaseline,
        internetRefreshBaseline.AddSeconds(5)),
    "上网详情不应阻塞每秒设备流量轮询，只应按较慢节奏后台更新");
Check(
    !LanDeviceMonitor.App.ShouldShowMainWindow(
        hasExited: false,
        hasWindow: true,
        isVisible: true) &&
    LanDeviceMonitor.App.ShouldShowMainWindow(
        hasExited: false,
        hasWindow: true,
        isVisible: false) &&
    !LanDeviceMonitor.App.ShouldShowMainWindow(
        hasExited: true,
        hasWindow: true,
        isVisible: false),
    "已可见主窗口的托盘激活不应再次调用 Show 创建根视觉");

Check(
    DeviceIdentityService.Identify("", "yeelink-light-lamp27_mibt053F", 1).TypeName == "灯",
    "Yeelight 型号应识别为灯");
Check(
    DeviceIdentityService.Identify("", "cgllc-airm-cgd1st_mibtEC5B", 1).TypeName == "监测设备",
    "CGLLC 空气设备应归入监测设备");
Check(
    DeviceIdentityService.Identify("", "MiAiSoundbox-L15A", 1).SuggestedName == "小爱音箱",
    "小米音箱应使用中文建议名称");
Check(
    DeviceIdentityService.Identify("", "Redmi-Note-11T-Pro", 2).TypeName == "手机",
    "Redmi Note 应识别为手机");
Check(DeviceItemViewModel.FormatSpeed(1024) == "1.00\u2009KB/s", "速度单位换算不正确");
Check(
    DeviceItemViewModel.FormatSpeed(0) == "0\u2009KB/s" &&
    DeviceItemViewModel.FormatSpeed(1) == "0\u2009KB/s" &&
    DeviceItemViewModel.FormatSpeed(512) == "0.50\u2009KB/s" &&
    DeviceItemViewModel.FormatSpeed(460) == "0.45\u2009KB/s" &&
    DeviceItemViewModel.FormatSpeedUnit(0) == "KB/s",
    "网速为零时应直接显示 0，且最小单位仍为 KB/s");
Check(DeviceItemViewModel.FormatBytes(1024L * 1024 * 1024) == "1.00\u2009GB", "流量单位换算不正确");
Check(
    DeviceItemViewModel.FormatSpeedValue(6L * 1024 * 1024 + 760 * 1024) == "6.74" &&
    DeviceItemViewModel.FormatSpeedUnit(6L * 1024 * 1024 + 760 * 1024) == "MB/s",
    "流量卡片的数值与单位拆分不正确");
var totalTooltipStart = new DateTimeOffset(
    2026, 8, 30, 23, 59, 58, TimeSpan.FromHours(8));
var totalTooltipEnd = new DateTimeOffset(
    2026, 8, 31, 0, 0, 2, TimeSpan.FromHours(8));
var totalTooltipText = DeviceItemViewModel.FormatDateRange(
    totalTooltipStart,
    totalTooltipEnd);
Check(
    totalTooltipText == "08月30日 ~ 08月31日" &&
    !totalTooltipText.Contains("2026", StringComparison.Ordinal) &&
    !totalTooltipText.Contains(":", StringComparison.Ordinal) &&
    !totalTooltipText.Contains("统计日期", StringComparison.Ordinal),
    "总量卡片悬停日期区间应仅显示月日范围");
Check(new AppSettings().RefreshIntervalSeconds == 1,
    "刷新间隔默认值应为 1 秒");
Check(
    MainWindow.GetTrafficRangeMinutesForSegment(0) == 1 &&
    MainWindow.GetTrafficRangeMinutesForSegment(1) == 5 &&
    MainWindow.GetTrafficRangeMinutesForSegment(2) == 10,
    "流量统计分段应直接选择 1、5、10 分钟");

Check(
    TrafficChart.GetFixedSlotCount(1) == 61 &&
    TrafficChart.GetFixedSlotCount(5) == 301 &&
    TrafficChart.GetFixedSlotCount(10) == 601,
    "流量图表的 1、5、10 分钟时间窗应分别使用固定的一秒采样槽");
Check(
    TrafficChart.GetTimeAxisMaximumLabelCount(1) == 6 &&
    TrafficChart.GetTimeAxisMaximumLabelCount(5) == 6 &&
    TrafficChart.GetTimeAxisMaximumLabelCount(10) == 8,
    "流量图表时间轴标签数量应与 Clash Verge 的区间策略一致");
Check(
    Math.Abs(TrafficChart.GetPlotLeftInset() - 60d) < 0.001,
    "流量图表左侧绘图区留白应回调到 60 DIP");
Check(
    TrafficChart.FormatRate(0) == "0\u2009KB" &&
    TrafficChart.FormatRate(1) == "0\u2009KB" &&
    TrafficChart.FormatRate(512) == "0.50\u2009KB" &&
    TrafficChart.FormatRate(1024) == "1.00\u2009KB" &&
    TrafficChart.FormatRate(1024 * 1024) == "1.00\u2009MB" &&
    TrafficChart.FormatRate(1000) == "0.98\u2009KB" &&
    TrafficChart.FormatRate(460) == "0.45\u2009KB",
    "图表零速率应直接显示 0，且速率标签应使用 KB 起步的单位换算");

var trafficHistoryNow = new DateTimeOffset(2026, 8, 11, 12, 0, 0, TimeSpan.FromHours(8));
var trafficHistory = new TrafficHistoryBuffer();
trafficHistory.Append(new TrafficSample(trafficHistoryNow.AddMinutes(-11), 1, 2));
trafficHistory.Append(new TrafficSample(trafficHistoryNow.AddMinutes(-5), 3, 4));
trafficHistory.Append(new TrafficSample(trafficHistoryNow, 5, 6));
var recentTraffic = trafficHistory.Snapshot(trafficHistoryNow);
Check(
    recentTraffic.Count == 2 &&
    recentTraffic[0].Timestamp == trafficHistoryNow.AddMinutes(-5) &&
    recentTraffic[0].UploadBytesPerSecond == 3 &&
    recentTraffic[^1].DownloadBytesPerSecond == 6,
    "流量图表只能绘制当前时间窗内实际收到的采样，不能由缓冲区补造每秒数据");
var replacedTraffic = new TrafficHistoryBuffer();
replacedTraffic.Append(new TrafficSample(trafficHistoryNow, 1, 2));
replacedTraffic.Append(new TrafficSample(trafficHistoryNow, 7, 8));
var replacedTrafficSnapshot = replacedTraffic.Snapshot(trafficHistoryNow);
Check(
    replacedTrafficSnapshot.Count == 1 &&
    replacedTrafficSnapshot[0].UploadBytesPerSecond == 7 &&
    replacedTrafficSnapshot[0].DownloadBytesPerSecond == 8,
    "同一秒的流量采样应更新当前槽位而不是制造重复点");
var sparseTraffic = new TrafficHistoryBuffer();
sparseTraffic.Append(new TrafficSample(trafficHistoryNow, 9, 11));
sparseTraffic.Append(new TrafficSample(trafficHistoryNow.AddSeconds(2), 13, 17));
var sparseTrafficSnapshot = sparseTraffic.Snapshot(trafficHistoryNow.AddSeconds(2));
Check(
    sparseTrafficSnapshot.Count == 2 &&
    sparseTrafficSnapshot[1].Timestamp == trafficHistoryNow.AddSeconds(2) &&
    sparseTrafficSnapshot[1].UploadBytesPerSecond == 13 &&
    sparseTrafficSnapshot[1].DownloadBytesPerSecond == 17,
    "缺失的一秒采样槽不能沿用最近值伪造数据");
var sameSecondZeroTraffic = new TrafficHistoryBuffer();
sameSecondZeroTraffic.Append(new TrafficSample(trafficHistoryNow, 9, 11));
sameSecondZeroTraffic.Append(new TrafficSample(trafficHistoryNow, 0, 0));
var sameSecondZeroTrafficSnapshot = sameSecondZeroTraffic.Snapshot(trafficHistoryNow);
var isolatedZeroTraffic = new TrafficHistoryBuffer();
isolatedZeroTraffic.Append(new TrafficSample(trafficHistoryNow, 100, 30));
isolatedZeroTraffic.Append(new TrafficSample(trafficHistoryNow.AddSeconds(1), 0, 0));
isolatedZeroTraffic.Append(new TrafficSample(trafficHistoryNow.AddSeconds(2), 140, 50));
var isolatedZeroTrafficSnapshot = isolatedZeroTraffic.Snapshot(
    trafficHistoryNow.AddSeconds(2));
var sustainedZeroTraffic = new TrafficHistoryBuffer();
sustainedZeroTraffic.Append(new TrafficSample(trafficHistoryNow, 100, 30));
sustainedZeroTraffic.Append(new TrafficSample(trafficHistoryNow.AddSeconds(1), 0, 0));
sustainedZeroTraffic.Append(new TrafficSample(trafficHistoryNow.AddSeconds(2), 0, 0));
var sustainedZeroTrafficSnapshot = sustainedZeroTraffic.Snapshot(
    trafficHistoryNow.AddSeconds(2));
Check(
    sameSecondZeroTrafficSnapshot is [{ UploadBytesPerSecond: 0, DownloadBytesPerSecond: 0 }] &&
    isolatedZeroTrafficSnapshot[1].UploadBytesPerSecond == 0 &&
    isolatedZeroTrafficSnapshot[1].DownloadBytesPerSecond == 0 &&
    sustainedZeroTrafficSnapshot[1].UploadBytesPerSecond == 0 &&
    sustainedZeroTrafficSnapshot[1].DownloadBytesPerSecond == 0,
    "同秒晚到的零样本和孤立零样本都必须按原始值保留，不能插值成不存在的流量");
Check(NotificationSchedule.TryParseTime("22:30", out var startMinutes) &&
      startMinutes == 1350,
    "免打扰开始时间解析不正确");
Check(!NotificationSchedule.TryParseTime("25:00", out _),
    "无效的免打扰时间不应被接受");
Check(NotificationSchedule.IsMuted(
        new DateTimeOffset(2026, 7, 24, 23, 0, 0, TimeSpan.FromHours(8)),
        true,
        22 * 60,
        8 * 60),
    "跨零点免打扰时段的夜间部分判断不正确");
Check(NotificationSchedule.IsMuted(
        new DateTimeOffset(2026, 7, 24, 7, 30, 0, TimeSpan.FromHours(8)),
        true,
        22 * 60,
        8 * 60),
    "跨零点免打扰时段的清晨部分判断不正确");
Check(!NotificationSchedule.IsMuted(
        new DateTimeOffset(2026, 7, 24, 12, 0, 0, TimeSpan.FromHours(8)),
        true,
        22 * 60,
        8 * 60),
    "免打扰时段以外不应静音通知");
Check(
    TrayService.CalculateMenuPosition(
        new System.Drawing.Point(1200, 800),
        new System.Drawing.Rectangle(0, 0, 1920, 1040),
        180,
        90,
        1d) == new System.Drawing.Point(1191, 719),
    "托盘菜单可见区域的左下角应与托盘图标右上角对齐");
Check(
    TrayService.CalculateMenuPosition(
        new System.Drawing.Point(4, 1000),
        new System.Drawing.Rectangle(0, 0, 1920, 1040),
        180,
        90,
        1d).X == 0,
    "托盘菜单不应超出工作区左侧");
Check(
    TrayService.CalculateMenuPosition(
        new System.Drawing.Point(100, 20),
        new System.Drawing.Rectangle(0, 0, 1920, 1040),
        180,
        90,
        1d).Y == 11,
    "顶部空间不足时托盘菜单应显示在右键位置下方");
Check(
    Math.Abs(SmoothScrollBehavior.EaseOutQuadratic(0.25d) - 0.4375d) < 0.0001d &&
    Math.Abs(SmoothScrollBehavior.EaseOutQuadratic(0.5d) - 0.75d) < 0.0001d &&
    Math.Abs(SmoothScrollBehavior.EaseOutQuadratic(0.75d) - 0.9375d) < 0.0001d,
    "滚轮平滑滚动应恢复为上一版的二次非线性缓出曲线");
Check(
    Math.Abs(SmoothScrollBehavior.GetDeviceListWheelDelta(-120) - 170d) < 0.0001d,
    "左侧设备栏单次标准滚轮距离应为 170 DIP");
Check(
    Math.Abs(SmoothScrollBehavior.GetDeviceListWheelDelta(-18) - 25.5d) < 0.0001d,
    "左侧设备栏应按高精度滚轮输入幅度滚动，不能把小输入放大成整格");
Check(
    Math.Abs(SmoothScrollBehavior.GetContentWheelDelta(-120) - 180d) < 0.0001d,
    "右侧内容板块应使用 180 DIP 的非线性平滑滚动");
Check(
    Math.Abs(SmoothScrollBehavior.GetDropDownWheelDelta(-120) - 10d) < 0.0001d,
    "下拉栏应使用 10 DIP 的非线性平滑滚动");
Check(
    Math.Abs(SmoothScrollBehavior.GetAnimationDurationMilliseconds() - 400d) < 0.0001d,
    "左右板块平滑滚动动画时长均应为 400 ms");
Check(
    Math.Abs(
        SmoothScrollBehavior.GetDropDownAnimationDurationMilliseconds() -
        150d) < 0.0001d,
    "下拉栏平滑滚动应在 150 ms 内跟手停止");
Check(
    Math.Abs(
        SmoothScrollBehavior.GetBoundedTargetOffset(
            100d,
            108d,
            10d,
            500d,
            10d) -
        110d) < 0.0001d,
    "下拉栏连续滚轮输入最多只应保留一格待滚动距离");
Check(
    Math.Abs(SmoothScrollBehavior.GetBoundedTargetOffset(100d, 400d, 170d, 500d) - 440d) < 0.0001d,
    "左侧连续滚轮输入的待滚动距离应限制为 340 DIP");
Check(
    Math.Abs(SmoothScrollBehavior.GetBoundedTargetOffset(100d, 164d, -100d, 500d) - 0d) < 0.0001d,
    "反向滚轮输入应立即反向，不能延续上一方向的惯性");
Check(
    MainWindow.GetDisplayVersion() == "v0.1.0-dev.226",
    "设置界面应显示当前实际软件版本");
var viewModelData = new DeviceViewData
{
    Profile = new DeviceProfile
    {
        MacAddress = "00:00:00:00:20:01",
        LastKnownOnline = false,
        LastUploadSpeedBytes = 1024,
        LastDownloadSpeedBytes = 2048,
        LastSeenAt = new DateTimeOffset(
            2026, 8, 23, 20, 0, 0, TimeSpan.FromHours(8))
    },
    Identity = new DeviceIdentity("手机", "\uE8EA", "手机"),
    Sessions =
    [
        new DeviceSession
        {
            MacAddress = "00:00:00:00:20:01",
            OnlineAt = new DateTimeOffset(
                2026, 8, 23, 19, 0, 0, TimeSpan.FromHours(8))
        }
    ]
};
var updatedViewModelData = new DeviceViewData
{
    Profile = new DeviceProfile
    {
        MacAddress = viewModelData.Profile.MacAddress,
        LastKnownOnline = true,
        LastUploadSpeedBytes = 4096,
        LastDownloadSpeedBytes = 8192,
        LastSeenAt = new DateTimeOffset(
            2026, 8, 23, 20, 0, 1, TimeSpan.FromHours(8))
    },
    Identity = viewModelData.Identity,
    Sessions =
    [
        new DeviceSession
        {
            MacAddress = viewModelData.Profile.MacAddress,
            OnlineAt = viewModelData.Sessions[0].OnlineAt
        }
    ]
};
var reusableViewModel = new DeviceItemViewModel(viewModelData);
var reusableSessionItems = reusableViewModel.Sessions;
var changedViewModelProperties = new HashSet<string>(StringComparer.Ordinal);
reusableViewModel.PropertyChanged += (_, args) =>
{
    if (args.PropertyName is not null)
    {
        changedViewModelProperties.Add(args.PropertyName);
    }
};
reusableViewModel.Update(updatedViewModelData);
Check(
    ReferenceEquals(reusableSessionItems, reusableViewModel.Sessions) &&
    reusableViewModel.UploadSpeedText == "4.00\u2009KB/s" &&
    changedViewModelProperties.Contains(nameof(DeviceItemViewModel.UploadSpeedText)) &&
    changedViewModelProperties.Contains(nameof(DeviceItemViewModel.LastSeenText)),
    "实时轮询更新应复用设备视图模型和未变化的会话视图，避免每秒重建整棵卡片树");
var pollPersistenceStatePath = Path.Combine(
    Path.GetTempPath(),
    "LanDeviceMonitor.Tests",
    Guid.NewGuid().ToString("N"),
    "state.json");
var pollPersistenceStateDirectory = Path.GetDirectoryName(pollPersistenceStatePath)!;
try
{
    var pollPersistenceRepository = new DeviceRepository(pollPersistenceStatePath);
    var pollPersistenceStartedAt = new DateTimeOffset(
        2026, 8, 23, 12, 0, 0, TimeSpan.FromHours(8));
    var pollPersistenceSnapshot = new DeviceSnapshot(
        "00:00:00:00:20:03",
        "轮询落盘测试设备",
        "poll-persistence-test",
        true,
        "10.77.0.242",
        2,
        30,
        1_024,
        2_048,
        10_000,
        20_000);

    var firstPollUpdate = await pollPersistenceRepository.ApplyPollAsync(
        [pollPersistenceSnapshot],
        pollPersistenceStartedAt);
    var stateAfterInitialPoll = await File.ReadAllTextAsync(pollPersistenceStatePath);

    var secondPollUpdate = await pollPersistenceRepository.ApplyPollAsync(
        [pollPersistenceSnapshot],
        pollPersistenceStartedAt.AddSeconds(1));
    var stateBeforeCheckpoint = await File.ReadAllTextAsync(pollPersistenceStatePath);

    await pollPersistenceRepository.ApplyPollAsync(
        [pollPersistenceSnapshot],
        pollPersistenceStartedAt.AddMinutes(5));
    var stateAfterCheckpoint = await File.ReadAllTextAsync(pollPersistenceStatePath);

    await pollPersistenceRepository.ApplyPollAsync(
        [],
        pollPersistenceStartedAt.AddMinutes(5).AddSeconds(1));
    await pollPersistenceRepository.ApplyPollAsync(
        [],
        pollPersistenceStartedAt.AddMinutes(5).AddSeconds(16));
    var reloadedPollPersistenceRepository = new DeviceRepository(pollPersistenceStatePath);
    await reloadedPollPersistenceRepository.LoadAsync();
    var persistedOfflineTransition =
        (await reloadedPollPersistenceRepository.GetDevicesAsync()).Single().Profile.LastKnownOnline == false;

    Check(
        stateAfterInitialPoll == stateBeforeCheckpoint,
        "常规轮询不应每秒重写 state.json，避免长期高频磁盘写入");
    Check(
        ReferenceEquals(
            firstPollUpdate.Devices.Single().Sessions,
            secondPollUpdate.Devices.Single().Sessions),
        "没有上下线变化时应复用历史会话视图，避免每秒复制完整历史列表");
    Check(
        stateAfterCheckpoint != stateBeforeCheckpoint,
        "轮询状态应在五分钟检查点写入 state.json");
    Check(
        persistedOfflineTransition,
        "确认的上下线状态变化应立即写入 state.json，避免丢失设备历史");
}
finally
{
    if (Directory.Exists(pollPersistenceStateDirectory))
    {
        Directory.Delete(pollPersistenceStateDirectory, recursive: true);
    }
}
Check(
    NotificationService.GetDeliveryStatusText(
        NotificationDeliveryChannel.WindowsToast,
        null).Contains("通知中心", StringComparison.Ordinal) &&
    NotificationService.GetDeliveryStatusText(
        NotificationDeliveryChannel.Unavailable,
        "InvalidOperationException").Contains("不可用", StringComparison.Ordinal),
    "通知状态应区分 Windows 通知中心投递和不可用状态");
Check(
    new NotificationDeliveryResult(
        NotificationDeliveryChannel.Pending,
        "已就绪").IsWindowsNotificationReady &&
    !new NotificationDeliveryResult(
        NotificationDeliveryChannel.Unavailable,
        "不可用").IsWindowsNotificationReady,
    "通知区域就绪状态不应被标记为故障");
Check(
    new LanDeviceMonitor.ViewModels.SessionItemViewModel(
        new LanDeviceMonitor.Models.DeviceSession
        {
            OnlineAt = new DateTimeOffset(
                2026, 8, 3, 9, 5, 6, TimeSpan.FromHours(8)),
            OfflineAt = new DateTimeOffset(
                2026, 8, 3, 9, 40, 59, TimeSpan.FromHours(8))
        }) is
        {
            OnlineDateText: "08月03日",
            OnlineTimeText: "09:05",
            OfflineDateText: "08月03日",
            OfflineTimeText: "09:40"
        },
    "上下线历史界面应分别显示日期与时间，隐藏年份和秒，后台仍记录完整日期时间");
Check(
    new LanDeviceMonitor.ViewModels.SessionItemViewModel(
        new LanDeviceMonitor.Models.DeviceSession
        {
            OnlineAt = new DateTimeOffset(
                2026, 8, 3, 9, 5, 6, TimeSpan.FromHours(8))
        }) is
        {
            OfflineDateText: "当前在线",
            OfflineTimeText: ""
        },
    "没有离线时间时应在日期部分显示“当前在线”");
var removalStatePath = Path.Combine(
    Path.GetTempPath(),
    "LanDeviceMonitor.Tests",
    Guid.NewGuid().ToString("N"),
    "state.json");
var removalRepository = new DeviceRepository(removalStatePath);
var removalSnapshot = new LanDeviceMonitor.Models.DeviceSnapshot(
    "00:00:00:00:20:01",
    "移除测试设备",
    "remove-test",
    true,
    "10.77.0.240",
    2,
    30,
    0,
    0,
    0,
    0);
var removalNow = new DateTimeOffset(
    2026, 7, 30, 12, 0, 0, TimeSpan.FromHours(8));
await removalRepository.ApplyPollAsync([removalSnapshot], removalNow);
await removalRepository.RemoveDeviceUntilNextOnlineAsync(
    removalSnapshot.MacAddress);
var hiddenImmediately =
    (await removalRepository.GetDevicesAsync()).Count == 0;
await removalRepository.ApplyPollAsync(
    [removalSnapshot],
    removalNow.AddSeconds(3));
var hiddenWhileStillOnline =
    (await removalRepository.GetDevicesAsync()).Count == 0;
await removalRepository.ApplyPollAsync(
    [],
    removalNow.AddSeconds(6));
await removalRepository.ApplyPollAsync(
    [],
    removalNow.AddSeconds(21));
await removalRepository.ApplyPollAsync(
    [removalSnapshot],
    removalNow.AddSeconds(24));
var restoredAfterNextOnline =
    (await removalRepository.GetDevicesAsync()).Count == 1;
Check(
    hiddenImmediately &&
    hiddenWhileStillOnline &&
    restoredAfterNextOnline,
    "移除的设备应立即隐藏、持续在线时保持隐藏，并仅在离线后再次上线时恢复");
var removalStateDirectory = Path.GetDirectoryName(removalStatePath);
if (!string.IsNullOrWhiteSpace(removalStateDirectory) &&
    Directory.Exists(removalStateDirectory))
{
    Directory.Delete(removalStateDirectory, recursive: true);
}

var trafficFilterStatePath = Path.Combine(
    Path.GetTempPath(),
    "LanDeviceMonitor.Tests",
    Guid.NewGuid().ToString("N"),
    "state.json");
var trafficFilterRepository = new DeviceRepository(trafficFilterStatePath);
var trafficFilterNow = new DateTimeOffset(
    2026, 8, 13, 18, 0, 0, TimeSpan.FromHours(8));
var trafficFilterMac = "00:00:00:00:20:02";
DeviceSnapshot TrafficFilterSnapshot(
    long uploadSpeed,
    long totalUpload,
    bool online = true) => new(
        trafficFilterMac,
        "流量过滤测试设备",
        "traffic-filter-test",
        online,
        "10.77.0.241",
        2,
        30,
        uploadSpeed,
        0,
        totalUpload,
        0);

var realtimeDisplayStatePath = Path.Combine(
    Path.GetTempPath(),
    "LanDeviceMonitor.Tests",
    Guid.NewGuid().ToString("N"),
    "state.json");
var realtimeDisplayRepository = new DeviceRepository(realtimeDisplayStatePath);
var realtimeDisplayFirst = await realtimeDisplayRepository.ApplyPollAsync(
    [TrafficFilterSnapshot(100, 1_000)],
    trafficFilterNow);
var realtimeDisplaySecond = await realtimeDisplayRepository.ApplyPollAsync(
    [TrafficFilterSnapshot(250, 1_500)],
    trafficFilterNow.AddSeconds(1));
var realtimeDisplayThird = await realtimeDisplayRepository.ApplyPollAsync(
    [TrafficFilterSnapshot(350, 2_000)],
    trafficFilterNow.AddSeconds(2));
Check(
    realtimeDisplayFirst.Devices.Single().Profile.LastUploadSpeedBytes == 100 &&
    realtimeDisplaySecond.Devices.Single().Profile.LastUploadSpeedBytes == 250 &&
    realtimeDisplayThird.Devices.Single().Profile.LastUploadSpeedBytes == 350 &&
    realtimeDisplaySecond.Devices.Single().Profile.LastUploadGraphSpeedBytes == 250 &&
    realtimeDisplayThird.Devices.Single().Profile.LastUploadGraphSpeedBytes == 350,
    "设备卡片、顶部汇总和折线图应使用每轮明确的瞬时速率；只有瞬时字段缺失时才退回累计差分平均值");

var firstTrafficUpdate = await trafficFilterRepository.ApplyPollAsync(
    [TrafficFilterSnapshot(6_000_000, 100)],
    trafficFilterNow);
var heldTrafficUpdate = await trafficFilterRepository.ApplyPollAsync(
    [TrafficFilterSnapshot(0, 100)],
    trafficFilterNow.AddSeconds(7));
Check(
    heldTrafficUpdate.Devices.Single().Profile.LastUploadSpeedBytes == 0,
    "原始速率明确为零时，设备卡片不能拿图表旧值冒充当前网速");
var expiredTrafficUpdate = await trafficFilterRepository.ApplyPollAsync(
    [TrafficFilterSnapshot(0, 100)],
    trafficFilterNow.AddSeconds(21));
var totalDerivedStatePath = Path.Combine(
    Path.GetTempPath(),
    "LanDeviceMonitor.Tests",
    Guid.NewGuid().ToString("N"),
    "state.json");
var totalDerivedRepository = new DeviceRepository(totalDerivedStatePath);
var firstDerivedUpdate = await totalDerivedRepository.ApplyPollAsync(
    [TrafficFilterSnapshot(0, 1_000)],
    trafficFilterNow);
var derivedTrafficUpdate = await totalDerivedRepository.ApplyPollAsync(
    [TrafficFilterSnapshot(7_000_000, 4_001_000)],
    trafficFilterNow.AddSeconds(4));
var sustainedTrafficUpdate = await totalDerivedRepository.ApplyPollAsync(
    [TrafficFilterSnapshot(0, 4_001_000)],
    trafficFilterNow.AddSeconds(19));
var idleTrafficUpdate = await totalDerivedRepository.ApplyPollAsync(
    [TrafficFilterSnapshot(0, 4_001_000)],
    trafficFilterNow.AddSeconds(44));
var zeroConfirmationStatePath = Path.Combine(
    Path.GetTempPath(),
    "LanDeviceMonitor.Tests",
    Guid.NewGuid().ToString("N"),
    "state.json");
var zeroConfirmationRepository = new DeviceRepository(zeroConfirmationStatePath);
var zeroConfirmationFirst = await zeroConfirmationRepository.ApplyPollAsync(
    [TrafficFilterSnapshot(6_000_000, 1_000)],
    trafficFilterNow);
var zeroConfirmationAfterGrace = await zeroConfirmationRepository.ApplyPollAsync(
    [TrafficFilterSnapshot(0, 1_000)],
    trafficFilterNow.AddSeconds(22));
var zeroConfirmationBeforeBoundary = await zeroConfirmationRepository.ApplyPollAsync(
    [TrafficFilterSnapshot(0, 1_000)],
    trafficFilterNow.AddSeconds(33));
var zeroConfirmationFinal = await zeroConfirmationRepository.ApplyPollAsync(
    [TrafficFilterSnapshot(0, 1_000)],
    trafficFilterNow.AddSeconds(35));
var directionIndependentStatePath = Path.Combine(
    Path.GetTempPath(),
    "LanDeviceMonitor.Tests",
    Guid.NewGuid().ToString("N"),
    "state.json");
var directionIndependentRepository = new DeviceRepository(directionIndependentStatePath);
DeviceSnapshot DirectionIndependentSnapshot(
    long uploadSpeed,
    long downloadSpeed,
    long totalUpload,
    long totalDownload) => new(
        trafficFilterMac,
        "流量方向独立测试设备",
        "traffic-direction-test",
        true,
        "10.77.0.241",
        2,
        30,
        uploadSpeed,
        downloadSpeed,
        totalUpload,
        totalDownload);
await directionIndependentRepository.ApplyPollAsync(
    [DirectionIndependentSnapshot(6_000_000, 800_000, 1_000, 1_000)],
    trafficFilterNow);
var directionIndependentUpdate = await directionIndependentRepository.ApplyPollAsync(
    [DirectionIndependentSnapshot(0, 850_000, 1_000, 20_000_000)],
    trafficFilterNow.AddSeconds(21));
var delayedSnapshotStatePath = Path.Combine(
    Path.GetTempPath(),
    "LanDeviceMonitor.Tests",
    Guid.NewGuid().ToString("N"),
    "state.json");
var delayedSnapshotRepository = new DeviceRepository(delayedSnapshotStatePath);
await delayedSnapshotRepository.ApplyPollAsync(
    [TrafficFilterSnapshot(6_000_000, 1_000)],
    trafficFilterNow);
var delayedSnapshotUpdate = await delayedSnapshotRepository.ApplyPollAsync(
    [TrafficFilterSnapshot(6_000_000, 31_000_000)],
    trafficFilterNow.AddSeconds(1));
var offlineDebounceStatePath = Path.Combine(
    Path.GetTempPath(),
    "LanDeviceMonitor.Tests",
    Guid.NewGuid().ToString("N"),
    "state.json");
var offlineDebounceRepository = new DeviceRepository(offlineDebounceStatePath);
var offlineDebounceInitial = await offlineDebounceRepository.ApplyPollAsync(
    [TrafficFilterSnapshot(6_000_000, 100)],
    trafficFilterNow);
var transientOfflineUpdate = await offlineDebounceRepository.ApplyPollAsync(
    [TrafficFilterSnapshot(0, 100, online: false)],
    trafficFilterNow.AddSeconds(1));
var restoredAfterTransientOfflineUpdate = await offlineDebounceRepository.ApplyPollAsync(
    [TrafficFilterSnapshot(0, 100)],
    trafficFilterNow.AddSeconds(2));
var transientMissingUpdate = await offlineDebounceRepository.ApplyPollAsync(
    [],
    trafficFilterNow.AddSeconds(3));
var restoredAfterTransientMissingUpdate = await offlineDebounceRepository.ApplyPollAsync(
    [TrafficFilterSnapshot(0, 100)],
    trafficFilterNow.AddSeconds(4));
var confirmedOfflinePendingUpdate = await offlineDebounceRepository.ApplyPollAsync(
    [TrafficFilterSnapshot(0, 100, online: false)],
    trafficFilterNow.AddSeconds(20));
var confirmedOfflineBeforeBoundaryUpdate = await offlineDebounceRepository.ApplyPollAsync(
    [TrafficFilterSnapshot(0, 100, online: false)],
    trafficFilterNow.AddSeconds(22));
var confirmedOfflineFinalUpdate = await offlineDebounceRepository.ApplyPollAsync(
    [TrafficFilterSnapshot(0, 100, online: false)],
    trafficFilterNow.AddSeconds(23));
var restoredSpeedStatePath = Path.Combine(
    Path.GetTempPath(),
    "LanDeviceMonitor.Tests",
    Guid.NewGuid().ToString("N"),
    "state.json");
var restoredSpeedRepository = new DeviceRepository(restoredSpeedStatePath);
await restoredSpeedRepository.ApplyPollAsync(
    [TrafficFilterSnapshot(2_000_000, 500)],
    trafficFilterNow);
var restoredSpeedRepositoryAfterRestart = new DeviceRepository(restoredSpeedStatePath);
await restoredSpeedRepositoryAfterRestart.LoadAsync();
var restoredZeroUpdate = await restoredSpeedRepositoryAfterRestart.ApplyPollAsync(
    [TrafficFilterSnapshot(0, 500)],
    trafficFilterNow.AddSeconds(1));
Check(
    confirmedOfflinePendingUpdate.Devices.Single().Profile.LastKnownOnline &&
    confirmedOfflinePendingUpdate.Transitions.Count == 0 &&
    confirmedOfflineBeforeBoundaryUpdate.Devices.Single().Profile.LastKnownOnline &&
    confirmedOfflineBeforeBoundaryUpdate.Transitions.Count == 0 &&
    !confirmedOfflineFinalUpdate.Devices.Single().Profile.LastKnownOnline &&
    confirmedOfflineFinalUpdate.Transitions is [{ IsOnline: false }],
    "首次离线证据后 2 秒内应继续防抖，持续满 3 秒时必须产生一次离线转换");
Check(
    firstTrafficUpdate.Devices.Single().Profile.LastUploadSpeedBytes == 6_000_000 &&
    heldTrafficUpdate.Devices.Single().Profile.LastUploadSpeedBytes == 0 &&
    expiredTrafficUpdate.Devices.Single().Profile.LastUploadSpeedBytes == 0 &&
    derivedTrafficUpdate.Devices.Single().Profile.LastUploadSpeedBytes == 7_000_000 &&
    derivedTrafficUpdate.Devices.Single().Profile.LastUploadGraphSpeedBytes == 7_000_000 &&
    sustainedTrafficUpdate.Devices.Single().Profile.LastUploadSpeedBytes == 0 &&
    sustainedTrafficUpdate.Devices.Single().Profile.LastUploadGraphSpeedBytes == 0 &&
    idleTrafficUpdate.Devices.Single().Profile.LastUploadSpeedBytes == 0 &&
    zeroConfirmationFirst.Devices.Single().Profile.LastUploadSpeedBytes == 6_000_000 &&
    zeroConfirmationAfterGrace.Devices.Single().Profile.LastUploadSpeedBytes == 0 &&
    zeroConfirmationBeforeBoundary.Devices.Single().Profile.LastUploadSpeedBytes == 0 &&
    zeroConfirmationFinal.Devices.Single().Profile.LastUploadSpeedBytes == 0 &&
    directionIndependentUpdate.Devices.Single().Profile.LastUploadSpeedBytes == 0 &&
    directionIndependentUpdate.Devices.Single().Profile.LastDownloadSpeedBytes > 800_000 &&
    delayedSnapshotUpdate.Devices.Single().Profile.LastUploadSpeedBytes == 6_000_000 &&
    offlineDebounceInitial.Devices.Single().Profile.LastUploadSpeedBytes == 6_000_000 &&
    transientOfflineUpdate.Devices.Single().Profile.LastKnownOnline &&
    transientOfflineUpdate.Devices.Single().Profile.LastUploadSpeedBytes == 6_000_000 &&
    transientOfflineUpdate.Transitions.Count == 0 &&
    restoredAfterTransientOfflineUpdate.Devices.Single().Profile.LastKnownOnline &&
    restoredAfterTransientOfflineUpdate.Devices.Single().Profile.LastUploadSpeedBytes == 0 &&
    restoredAfterTransientOfflineUpdate.Transitions.Count == 0 &&
    transientMissingUpdate.Devices.Single().Profile.LastKnownOnline &&
    transientMissingUpdate.Devices.Single().Profile.LastUploadSpeedBytes == 0 &&
    transientMissingUpdate.Transitions.Count == 0 &&
    restoredAfterTransientMissingUpdate.Devices.Single().Profile.LastKnownOnline &&
    restoredAfterTransientMissingUpdate.Devices.Single().Profile.LastUploadSpeedBytes == 0 &&
    restoredAfterTransientMissingUpdate.Transitions.Count == 0 &&
    confirmedOfflinePendingUpdate.Devices.Single().Profile.LastKnownOnline &&
    confirmedOfflinePendingUpdate.Devices.Single().Profile.LastUploadSpeedBytes == 0 &&
    confirmedOfflineBeforeBoundaryUpdate.Devices.Single().Profile.LastKnownOnline &&
    confirmedOfflineBeforeBoundaryUpdate.Devices.Single().Profile.LastUploadSpeedBytes == 0 &&
    !confirmedOfflineFinalUpdate.Devices.Single().Profile.LastKnownOnline &&
    confirmedOfflineFinalUpdate.Devices.Single().Profile.LastUploadSpeedBytes == 0 &&
    confirmedOfflineFinalUpdate.Transitions is [{ IsOnline: false }] &&
    restoredZeroUpdate.Devices.Single().Profile.LastUploadSpeedBytes == 0,
    "卡片只能显示本轮瞬时值或本轮累计差分平均值；图表只记录新的真实采样，不能用旧值、插值或启动后的占位 0 制造数据");
foreach (var trafficFilterDirectory in new[]
         {
              Path.GetDirectoryName(realtimeDisplayStatePath),
              Path.GetDirectoryName(trafficFilterStatePath),
               Path.GetDirectoryName(totalDerivedStatePath),
              Path.GetDirectoryName(zeroConfirmationStatePath),
              Path.GetDirectoryName(directionIndependentStatePath),
               Path.GetDirectoryName(delayedSnapshotStatePath),
              Path.GetDirectoryName(restoredSpeedStatePath),
              Path.GetDirectoryName(offlineDebounceStatePath)
         }.Where(directory => !string.IsNullOrWhiteSpace(directory)))
{
    if (Directory.Exists(trafficFilterDirectory!))
    {
        Directory.Delete(trafficFilterDirectory!, recursive: true);
    }
}
Check(
    MainWindow.GetRouterLoadingVisibility(showLoadingMessage: false) ==
        System.Windows.Visibility.Collapsed &&
    MainWindow.GetRouterLoadingVisibility(showLoadingMessage: true) ==
        System.Windows.Visibility.Visible,
    "首次进入路由器页面时不显示读取提示，手动刷新时显示读取提示");
Check(
    MicaWindowService.GetPersistentAcrylicGradientColor(
        useDarkMode: false) == unchecked((int)0x14F5F5F5) &&
    MicaWindowService.GetPersistentAcrylicGradientColor(
        useDarkMode: true) == unchecked((int)0x30202020),
    "浅色和深色模式应使用固定的持久 Acrylic 染色值");
var lightMinimizeMaterial =
    MicaWindowService.GetMinimizeTransitionMaterialBrush(useDarkMode: false);
var darkMinimizeMaterial =
    MicaWindowService.GetMinimizeTransitionMaterialBrush(useDarkMode: true);
Check(
    lightMinimizeMaterial.IsFrozen &&
    darkMinimizeMaterial.IsFrozen &&
    lightMinimizeMaterial.TileMode == System.Windows.Media.TileMode.Tile &&
    darkMinimizeMaterial.TileMode == System.Windows.Media.TileMode.Tile &&
    lightMinimizeMaterial.ViewportUnits ==
        System.Windows.Media.BrushMappingMode.Absolute &&
    darkMinimizeMaterial.ViewportUnits ==
        System.Windows.Media.BrushMappingMode.Absolute &&
    !ReferenceEquals(lightMinimizeMaterial, darkMinimizeMaterial),
    "最小化动画应具有独立的浅色和深色非聚焦过渡材质，而不是回退为透明或纯白背景");
Exception? trayMenuOpenException = null;
Exception? repeatedMainWindowActivationException = null;
var trayMenuOpenInvoked = false;
var verticalScrollBarVisible = false;
var verticalScrollBarWidth = 0d;
var verticalScrollThumbVisible = false;
var verticalScrollBarMaximum = 0d;
var verticalScrollBarViewport = 0d;
var verticalScrollThumbHeight = 0d;
var verticalScrollThumbMinimumHeight = 0d;
var scrollThumbDragAnimationConfigured = false;
var scrollThumbHoverAnimationConfigured = false;
var deviceListScrollBarVisible = false;
var settingsScrollBarVisible = false;
var deviceListScrollThumbHeight = 0d;
var settingsScrollThumbHeight = 0d;
var deviceListHoverAnimationConfigured = false;
var trayMenuHoverAnimationConfigured = false;
var trayOpenIconTextAligned = false;
var acrylicAccentApplied = false;
var systemBackdropStableDuringWindowTransitions = false;
var minimizeTransitionMaterialAppliedAndCleared = false;
var minimizeCommandUsesTransitionPipeline = false;
var systemBackdropTransitionDiagnostics = "";
var themePaletteConfigured = false;
var dangerConfirmButtonConfigured = false;
var detailIconThemeConfigured = false;
var topButtonToolTipsReadableInDarkMode = false;
var closeCaptionHoverConfigured = false;
var detailSectionOrderConfigured = false;
var capsuleControlGeometryBalanced = false;
var timelineDurationBadgeBorderConfigured = false;
var comboDropDownUsesPageScrollStyle = false;
var deviceProfileControlsHaveMatchingHeight = false;
var detailTitleSpacingConfigured = false;
var quietTimeEditorsConfigured = false;
var deviceSelectionStableDuringRefresh = false;
var deviceViewModelsReusedDuringRefresh = false;
var deviceFilterAndSortConfigured = false;
var filterHoverAndPressConfigured = false;
var listFilterSlideConfigured = false;
var detailFilterOriginalAnimationConfigured = false;
var filterChangeKeepsOriginalSynchronizedAnimation = false;
var filterAnimationUsesRenderCache = false;
var filterTransitionCacheUsesMaterializedTarget = false;
var sessionHistoryUsesStableItemsSource = false;
var offlineDeviceManagementConfigured = false;
var confirmationBehaviorConfigured = false;
var listFilterSlideDiagnostics = "";
var detailFilterSlideDiagnostics = "";
var trayHoverColorAndSpacingConfigured = false;
var visualQaImagePath = Environment.GetEnvironmentVariable(
    "LAN_DEVICE_MONITOR_QA_IMAGE");
var darkVisualQaImagePath = Environment.GetEnvironmentVariable(
    "LAN_DEVICE_MONITOR_DARK_QA_IMAGE");
var trayVisualQaImagePath = Environment.GetEnvironmentVariable(
    "LAN_DEVICE_MONITOR_TRAY_QA_IMAGE");
var trayMenuThread = new Thread(() =>
{
    try
    {
        var application = TestAppFactory.Create();
        application.InitializeComponent();
        ThemeService.Apply(ThemeService.LightMode);
        var lightScrollDefault =
            (System.Windows.Media.Color)application.Resources["ScrollThumbColor"];
        var lightScrollHover =
            (System.Windows.Media.Color)application.Resources["ScrollThumbHoverColor"];
        var lightCombo =
            (System.Windows.Media.Color)application.Resources["ComboBoxColor"];
        var lightControl =
            (System.Windows.Media.Color)application.Resources["ControlColor"];
        var lightPopup =
            (System.Windows.Media.Color)application.Resources["ComboBoxPopupColor"];
        var lightPanel =
            (System.Windows.Media.Color)application.Resources["MicaPanelColor"];
        var lightCard =
            (System.Windows.Media.Color)application.Resources["MicaCardColor"];
        var lightAccentSurface =
            (System.Windows.Media.Color)application.Resources["AccentLightColor"];
        var lightTimelineItem =
            (System.Windows.Media.Color)application.Resources["TimelineItemColor"];
        var lightTimelineDuration =
            (System.Windows.Media.Color)application.Resources["TimelineDurationColor"];
        var lightCaptionHover =
            (System.Windows.Media.Color)application.Resources["CaptionHoverColor"];
        var lightDeviceListItem =
            (System.Windows.Media.Color)application.Resources["DeviceListItemColor"];
        var lightDeviceListItemSelected =
            (System.Windows.Media.Color)application.Resources["DeviceListItemSelectedColor"];
        var lightDeviceListItemHover =
            (System.Windows.Media.Color)application.Resources["DeviceListItemHoverColor"];
        var lightDeviceListItemSelectedHover =
            (System.Windows.Media.Color)application.Resources["DeviceListItemSelectedHoverColor"];
        var lightOnlineCard =
            (System.Windows.Media.Color)application.Resources["OnlineCardColor"];
        var lightOfflineCard =
            (System.Windows.Media.Color)application.Resources["OfflineCardColor"];
        var lightOnlineCardBorder =
            (System.Windows.Media.Color)application.Resources["OnlineCardBorderColor"];
        var lightOfflineCardBorder =
            (System.Windows.Media.Color)application.Resources["OfflineCardBorderColor"];
        var lightOnlineDot =
            (System.Windows.Media.Color)application.Resources["TimelineOnlineDotColor"];
        var lightOfflineDot =
            (System.Windows.Media.Color)application.Resources["TimelineOfflineDotColor"];
        var dangerConfirmButton = new System.Windows.Controls.Button
        {
            Style = (System.Windows.Style)application.FindResource(
                "DangerConfirmButtonStyle")
        };
        dangerConfirmButton.ApplyTemplate();
        dangerConfirmButtonConfigured =
            dangerConfirmButton.Foreground is
                System.Windows.Media.SolidColorBrush dangerForeground &&
            dangerForeground.Color ==
                System.Windows.Media.Colors.White &&
            dangerConfirmButton.Background is
                System.Windows.Media.SolidColorBrush dangerBackground &&
            dangerBackground.Color ==
                (System.Windows.Media.Color)application.Resources[
                    "DangerConfirmColor"];
        ThemeService.Apply(ThemeService.DarkMode);
        var darkScrollDefault =
            (System.Windows.Media.Color)application.Resources["ScrollThumbColor"];
        var darkScrollHover =
            (System.Windows.Media.Color)application.Resources["ScrollThumbHoverColor"];
        var darkCombo =
            (System.Windows.Media.Color)application.Resources["ComboBoxColor"];
        var darkControl =
            (System.Windows.Media.Color)application.Resources["ControlColor"];
        var darkPopup =
            (System.Windows.Media.Color)application.Resources["ComboBoxPopupColor"];
        var darkPanel =
            (System.Windows.Media.Color)application.Resources["MicaPanelColor"];
        var darkCard =
            (System.Windows.Media.Color)application.Resources["MicaCardColor"];
        var darkAccentSurface =
            (System.Windows.Media.Color)application.Resources["AccentLightColor"];
        var darkTimelineItem =
            (System.Windows.Media.Color)application.Resources["TimelineItemColor"];
        var darkTimelineDuration =
            (System.Windows.Media.Color)application.Resources["TimelineDurationColor"];
        var darkCaptionHover =
            (System.Windows.Media.Color)application.Resources["CaptionHoverColor"];
        var darkOnlineCard =
            (System.Windows.Media.Color)application.Resources["OnlineCardColor"];
        var darkOfflineCard =
            (System.Windows.Media.Color)application.Resources["OfflineCardColor"];
        var darkOnlineCardBorder =
            (System.Windows.Media.Color)application.Resources["OnlineCardBorderColor"];
        var darkOfflineCardBorder =
            (System.Windows.Media.Color)application.Resources["OfflineCardBorderColor"];
        var darkOnlineDot =
            (System.Windows.Media.Color)application.Resources["TimelineOnlineDotColor"];
        var darkOfflineDot =
            (System.Windows.Media.Color)application.Resources["TimelineOfflineDotColor"];
        var darkToolTip =
            (System.Windows.Media.Color)application.Resources["ToolTipColor"];
        var darkText =
            (System.Windows.Media.Color)application.Resources["TextColor"];
        themePaletteConfigured =
            lightScrollDefault == System.Windows.Media.Color.FromArgb(0x88, 0x66, 0x66, 0x66) &&
            lightScrollHover == System.Windows.Media.Color.FromArgb(0x8F, 0x66, 0x66, 0x66) &&
            darkScrollDefault == System.Windows.Media.Color.FromArgb(0x96, 0xC2, 0xC2, 0xC2) &&
            darkScrollHover == System.Windows.Media.Color.FromArgb(0x9E, 0xC2, 0xC2, 0xC2) &&
            lightScrollHover.A == (byte)Math.Round(lightScrollDefault.A * 1.05d) &&
            darkScrollHover.A == (byte)Math.Round(darkScrollDefault.A * 1.05d) &&
            lightCombo == lightControl &&
            lightPopup.A == 0xE6 &&
            darkCombo == darkControl &&
            darkPopup.A == 0xAD &&
            lightPanel == System.Windows.Media.Color.FromArgb(0x0C, 0xFF, 0xFF, 0xFF) &&
            lightCard == System.Windows.Media.Color.FromArgb(0x70, 0xFF, 0xFF, 0xFF) &&
            lightAccentSurface == System.Windows.Media.Color.FromArgb(0xDE, 0xFF, 0xFF, 0xFF) &&
            darkPanel == System.Windows.Media.Color.FromArgb(0x09, 0x20, 0x20, 0x20) &&
            darkCard == System.Windows.Media.Color.FromArgb(0x54, 0x50, 0x50, 0x50) &&
            darkAccentSurface == System.Windows.Media.Color.FromArgb(0x74, 0x61, 0x61, 0x61) &&
            lightTimelineItem == System.Windows.Media.Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF) &&
            lightTimelineDuration == System.Windows.Media.Color.FromArgb(0x80, 0xFA, 0xFA, 0xFA) &&
            lightCaptionHover == System.Windows.Media.Color.FromArgb(0x2B, 0x00, 0x00, 0x00) &&
            lightDeviceListItem == System.Windows.Media.Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF) &&
            lightDeviceListItemSelected == System.Windows.Media.Color.FromArgb(0xC0, 0xFF, 0xFF, 0xFF) &&
            lightDeviceListItemHover == System.Windows.Media.Color.FromArgb(0xBF, 0xFF, 0xFF, 0xFF) &&
            lightDeviceListItemSelectedHover == System.Windows.Media.Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF) &&
            Math.Abs(
                1d -
                ((1d - (lightDeviceListItem.A / 255d)) *
                 (1d - (lightDeviceListItemHover.A / 255d))) -
                0.90d) < 0.001d &&
            lightOnlineCard == System.Windows.Media.Color.FromArgb(0x84, 0xD9, 0xF8, 0xE6) &&
            lightOfflineCard == System.Windows.Media.Color.FromArgb(0x84, 0xFF, 0xEA, 0xEA) &&
            lightOnlineCardBorder == System.Windows.Media.Color.FromArgb(0x70, 0x4C, 0xC8, 0x8A) &&
            lightOfflineCardBorder == System.Windows.Media.Color.FromArgb(0x40, 0xE3, 0x6C, 0x6C) &&
            darkOnlineCard == System.Windows.Media.Color.FromArgb(0x63, 0x19, 0x58, 0x3D) &&
            darkOfflineCard == System.Windows.Media.Color.FromArgb(0x63, 0x5B, 0x1B, 0x2E) &&
            darkOnlineCardBorder == System.Windows.Media.Color.FromArgb(0x90, 0x67, 0xDD, 0xA6) &&
            darkOfflineCardBorder == System.Windows.Media.Color.FromArgb(0x8C, 0xE3, 0x6E, 0x82) &&
            lightOnlineDot == System.Windows.Media.Color.FromRgb(0x68, 0xD9, 0xA3) &&
            lightOfflineDot == System.Windows.Media.Color.FromRgb(0xFF, 0x85, 0x85) &&
            darkOnlineDot == System.Windows.Media.Color.FromRgb(0x6C, 0xEF, 0xAE) &&
            darkOfflineDot == System.Windows.Media.Color.FromRgb(0xFF, 0x9B, 0x9B) &&
            darkTimelineItem == System.Windows.Media.Color.FromArgb(0x30, 0x58, 0x58, 0x58) &&
            darkTimelineDuration == System.Windows.Media.Color.FromArgb(0xA0, 0x8E, 0x8E, 0x8E) &&
            darkCaptionHover == System.Windows.Media.Color.FromArgb(0x52, 0x00, 0x00, 0x00) &&
            lightTimelineDuration.R == lightTimelineDuration.G &&
            lightTimelineDuration.G == lightTimelineDuration.B &&
            darkTimelineDuration.R == darkTimelineDuration.G &&
            darkTimelineDuration.G == darkTimelineDuration.B;
        var toolTipStyle =
            application.FindResource(typeof(System.Windows.Controls.ToolTip))
                as System.Windows.Style;
        topButtonToolTipsReadableInDarkMode =
            toolTipStyle is not null &&
            darkToolTip == System.Windows.Media.Color.FromArgb(0xB8, 0x3E, 0x3E, 0x3E) &&
            darkToolTip != darkText;
        var comboBoxStyle =
            (System.Windows.Style)application.FindResource(typeof(System.Windows.Controls.ComboBox));
        var textBoxStyle =
            (System.Windows.Style)application.FindResource(typeof(System.Windows.Controls.TextBox));
        var passwordBoxStyle =
            (System.Windows.Style)application.FindResource(typeof(System.Windows.Controls.PasswordBox));
        var cardStyle =
            (System.Windows.Style)application.FindResource("CardStyle");
        var timelineDurationBadgeStyle =
            (System.Windows.Style)application.FindResource(
                "TimelineDurationBadgeStyle");
        timelineDurationBadgeBorderConfigured =
            (System.Windows.Thickness)GetStyleSetterValue(
                timelineDurationBadgeStyle,
                System.Windows.Controls.Border.BorderThicknessProperty) ==
                new System.Windows.Thickness(1);
        capsuleControlGeometryBalanced =
            (System.Windows.Thickness)GetStyleSetterValue(
                comboBoxStyle,
                System.Windows.Controls.Control.PaddingProperty) ==
                new System.Windows.Thickness(16, 0, 16, 0) &&
            (double)GetStyleSetterValue(
                comboBoxStyle,
                System.Windows.FrameworkElement.MinHeightProperty) == 40d &&
            (System.Windows.Thickness)GetStyleSetterValue(
                textBoxStyle,
                System.Windows.Controls.Control.PaddingProperty) ==
                new System.Windows.Thickness(16, 0, 16, 0) &&
            (double)GetStyleSetterValue(
                textBoxStyle,
                System.Windows.FrameworkElement.MinHeightProperty) == 40d &&
            (System.Windows.Thickness)GetStyleSetterValue(
                passwordBoxStyle,
                System.Windows.Controls.Control.PaddingProperty) ==
                new System.Windows.Thickness(16, 0, 16, 0) &&
            (double)GetStyleSetterValue(
                passwordBoxStyle,
                System.Windows.FrameworkElement.MinHeightProperty) == 40d &&
            (System.Windows.CornerRadius)GetStyleSetterValue(
                cardStyle,
                System.Windows.Controls.Border.CornerRadiusProperty) ==
                new System.Windows.CornerRadius(24) &&
            (System.Windows.Thickness)GetStyleSetterValue(
                cardStyle,
                System.Windows.Controls.Border.PaddingProperty) ==
                new System.Windows.Thickness(18);
        ThemeService.Apply(ThemeService.LightMode);
        var overflowViewer = new System.Windows.Controls.ScrollViewer
        {
            Style = (System.Windows.Style)application.FindResource(
                "WindowsSettingsScrollViewerStyle"),
            Width = 220,
            Height = 120,
            VerticalScrollBarVisibility =
                System.Windows.Controls.ScrollBarVisibility.Auto,
            Content = new System.Windows.Controls.Border
            {
                Width = 180,
                Height = 600
            }
        };
        var scrollbarTestWindow = new System.Windows.Window
        {
            Width = 240,
            Height = 140,
            Left = -10000,
            Top = -10000,
            ShowActivated = false,
            ShowInTaskbar = false,
            WindowStyle = System.Windows.WindowStyle.None,
            Content = overflowViewer
        };
        scrollbarTestWindow.Show();
        scrollbarTestWindow.UpdateLayout();
        var verticalScrollBar =
            FindVisualDescendant<System.Windows.Controls.Primitives.ScrollBar>(
                overflowViewer,
                scrollBar =>
                    scrollBar.Orientation ==
                    System.Windows.Controls.Orientation.Vertical);
        var verticalThumb =
            FindVisualDescendant<System.Windows.Controls.Primitives.Thumb>(
                verticalScrollBar);
        verticalScrollBarVisible =
            verticalScrollBar?.Visibility == System.Windows.Visibility.Visible;
        verticalScrollBarWidth = verticalScrollBar?.ActualWidth ?? 0d;
        verticalScrollBarMaximum = verticalScrollBar?.Maximum ?? 0d;
        verticalScrollBarViewport = verticalScrollBar?.ViewportSize ?? 0d;
        verticalScrollThumbHeight = verticalThumb?.ActualHeight ?? 0d;
        verticalScrollThumbMinimumHeight = verticalThumb?.MinHeight ?? 0d;
        verticalScrollThumbVisible =
            verticalThumb?.Visibility == System.Windows.Visibility.Visible &&
            verticalThumb.MinHeight >= 28d;
        var draggingTrigger = verticalThumb?.Template.Triggers
            .OfType<System.Windows.Trigger>()
            .FirstOrDefault(trigger =>
                trigger.Property ==
                    System.Windows.Controls.Primitives.Thumb.IsDraggingProperty &&
                Equals(trigger.Value, true));
        var dragEnterAnimation = draggingTrigger?.EnterActions
            .OfType<System.Windows.Media.Animation.BeginStoryboard>()
            .SelectMany(action => action.Storyboard.Children)
            .OfType<System.Windows.Media.Animation.DoubleAnimation>()
            .FirstOrDefault();
        var dragExitAnimation = draggingTrigger?.ExitActions
            .OfType<System.Windows.Media.Animation.BeginStoryboard>()
            .SelectMany(action => action.Storyboard.Children)
            .OfType<System.Windows.Media.Animation.DoubleAnimation>()
            .FirstOrDefault();
        scrollThumbDragAnimationConfigured =
            dragEnterAnimation?.Duration.HasTimeSpan == true &&
            dragEnterAnimation.Duration.TimeSpan ==
                TimeSpan.FromMilliseconds(300) &&
            dragEnterAnimation.EasingFunction is
                System.Windows.Media.Animation.QuadraticEase
                {
                    EasingMode:
                        System.Windows.Media.Animation.EasingMode.EaseIn
                } &&
            dragExitAnimation?.Duration.HasTimeSpan == true &&
            dragExitAnimation.Duration.TimeSpan ==
                TimeSpan.FromMilliseconds(300) &&
            dragExitAnimation.EasingFunction is
                System.Windows.Media.Animation.QuadraticEase
                {
                    EasingMode:
                        System.Windows.Media.Animation.EasingMode.EaseOut
                };
        var hoverTrigger = verticalThumb?.Template.Triggers
            .OfType<System.Windows.Trigger>()
            .FirstOrDefault(trigger =>
                trigger.Property == System.Windows.UIElement.IsMouseOverProperty &&
                Equals(trigger.Value, true));
        var hoverEnterAnimation = hoverTrigger?.EnterActions
            .OfType<System.Windows.Media.Animation.BeginStoryboard>()
            .SelectMany(action => action.Storyboard.Children)
            .OfType<System.Windows.Media.Animation.DoubleAnimation>()
            .FirstOrDefault();
        var hoverExitAnimation = hoverTrigger?.ExitActions
            .OfType<System.Windows.Media.Animation.BeginStoryboard>()
            .SelectMany(action => action.Storyboard.Children)
            .OfType<System.Windows.Media.Animation.DoubleAnimation>()
            .FirstOrDefault();
        scrollThumbHoverAnimationConfigured =
            hoverEnterAnimation?.Duration.HasTimeSpan == true &&
            hoverEnterAnimation.Duration.TimeSpan ==
                TimeSpan.FromMilliseconds(300) &&
            hoverEnterAnimation.EasingFunction is
                System.Windows.Media.Animation.QuadraticEase &&
            hoverExitAnimation?.Duration.HasTimeSpan == true &&
            hoverExitAnimation.Duration.TimeSpan ==
                TimeSpan.FromMilliseconds(300) &&
            hoverExitAnimation.EasingFunction is
                System.Windows.Media.Animation.QuadraticEase;
        scrollbarTestWindow.Close();

        var smokeWindow = new MainWindow(new NotificationService())
        {
            Left = -10000,
            Top = -10000,
            Width = 900,
            Height = 560,
            ShowActivated = false,
            ShowInTaskbar = false
        };
        var testDevices = Enumerable.Range(1, 20)
            .Select(index => new DeviceItemViewModel(new LanDeviceMonitor.Models.DeviceViewData
            {
                Profile = new LanDeviceMonitor.Models.DeviceProfile
                {
                    MacAddress = $"00:00:00:00:00:{index:00}",
                    CustomName = index == 1
                        ? "iPhone 13 Pro Max"
                        : string.Empty,
                    LastKnownOnline = true,
                    LastRouterName = $"测试设备 {index}",
                    LastConnectionType = 2
                },
                Identity = new LanDeviceMonitor.Models.DeviceIdentity(
                    index == 1
                        ? "窗帘"
                        : index == 2
                            ? "空调"
                            : "手机",
                    index <= 2 ? string.Empty : "\uE8EA",
                    $"测试设备 {index}"),
                Sessions = index == 1
                    ?
                    [
                        new LanDeviceMonitor.Models.DeviceSession
                        {
                            MacAddress = "00:00:00:00:00:01",
                            OnlineAt = new DateTimeOffset(
                                2026, 7, 24, 18, 27, 7, TimeSpan.FromHours(8)),
                            OfflineAt = new DateTimeOffset(
                                2026, 7, 24, 20, 35, 7, TimeSpan.FromHours(8))
                        }
                    ]
                    : []
            }))
            .ToList();
        smokeWindow.DeviceList.ItemsSource = testDevices;
        smokeWindow.EmptyPanel.Visibility = System.Windows.Visibility.Collapsed;
        smokeWindow.SettingsPanel.Visibility = System.Windows.Visibility.Visible;
        smokeWindow.Show();
        smokeWindow.UpdateLayout();
        var appMainWindowField = typeof(LanDeviceMonitor.App).GetField(
            "_mainWindow",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        appMainWindowField.SetValue(application, smokeWindow);
        var showMainWindowMethod = typeof(LanDeviceMonitor.App).GetMethod(
            "ShowMainWindow",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        try
        {
            showMainWindowMethod.Invoke(application, null);
        }
        catch (TargetInvocationException exception)
        {
            repeatedMainWindowActivationException =
                exception.InnerException ?? exception;
        }
        var closeCaptionStyle =
            (System.Windows.Style)smokeWindow.FindResource(
                "CloseCaptionButtonStyle");
        var closeHoverTrigger =
            closeCaptionStyle.Triggers
                .OfType<System.Windows.Trigger>()
                .FirstOrDefault(trigger =>
                    trigger.Property ==
                        System.Windows.UIElement.IsMouseOverProperty &&
                    Equals(trigger.Value, true));
        var closeHoverBrush =
            closeHoverTrigger?.Setters
                .OfType<System.Windows.Setter>()
                .FirstOrDefault(setter =>
                    setter.Property ==
                        System.Windows.Controls.Control.BorderBrushProperty)
                ?.Value as System.Windows.Media.SolidColorBrush;
        var closeProbe = new System.Windows.Controls.Button
        {
            Style = closeCaptionStyle
        };
        closeProbe.ApplyTemplate();
        var closeTemplate = closeProbe.Template;
        var closeGlyphLight =
            closeTemplate?.FindName(
                "CloseGlyphLight",
                closeProbe) as System.Windows.Controls.TextBlock;
        var closeHoverTemplateTrigger =
            closeTemplate?.Triggers
                .OfType<System.Windows.Trigger>()
                .FirstOrDefault(trigger =>
                    trigger.Property ==
                        System.Windows.UIElement.IsMouseOverProperty &&
                    Equals(trigger.Value, true));
        var closeEnterStoryboard =
            closeHoverTemplateTrigger?.EnterActions
                .OfType<
                    System.Windows.Media.Animation.BeginStoryboard>()
                .FirstOrDefault()
                ?.Storyboard;
        var closeExitStoryboard =
            closeHoverTemplateTrigger?.ExitActions
                .OfType<
                    System.Windows.Media.Animation.BeginStoryboard>()
                .FirstOrDefault()
                ?.Storyboard;
        var closeEnterAnimations =
            closeEnterStoryboard?.Children
                .OfType<
                    System.Windows.Media.Animation.DoubleAnimation>()
                .ToList() ?? [];
        var closeExitAnimations =
            closeExitStoryboard?.Children
                .OfType<
                    System.Windows.Media.Animation.DoubleAnimation>()
                .ToList() ?? [];
        static System.Windows.Media.Animation.DoubleAnimation?
            CloseAnimation(
                IReadOnlyList<
                    System.Windows.Media.Animation.DoubleAnimation>
                    animations,
                string targetName) =>
            animations.FirstOrDefault(animation =>
                System.Windows.Media.Animation.Storyboard
                    .GetTargetName(animation) == targetName);
        var closeSurfaceEnter = CloseAnimation(
            closeEnterAnimations,
            "CaptionHoverSurface");
        var closeGlyphEnter = CloseAnimation(
            closeEnterAnimations,
            "CloseGlyphLight");
        var closeSurfaceExit = CloseAnimation(
            closeExitAnimations,
            "CaptionHoverSurface");
        var closeGlyphExit = CloseAnimation(
            closeExitAnimations,
            "CloseGlyphLight");
        closeCaptionHoverConfigured =
            closeHoverBrush?.Color ==
                System.Windows.Media.Color.FromRgb(0xFF, 0x52, 0x5C) &&
            closeGlyphLight is not null &&
            closeGlyphLight.Foreground is
                System.Windows.Media.SolidColorBrush glyphBrush &&
            glyphBrush.Color == System.Windows.Media.Colors.White &&
            closeSurfaceEnter?.Duration.HasTimeSpan == true &&
            closeSurfaceEnter.Duration.TimeSpan ==
                TimeSpan.FromMilliseconds(220) &&
            closeSurfaceEnter.EasingFunction is
                System.Windows.Media.Animation.CubicEase &&
            closeGlyphEnter?.Duration.HasTimeSpan == true &&
            closeGlyphEnter.Duration.TimeSpan ==
                TimeSpan.FromMilliseconds(220) &&
            closeGlyphEnter.EasingFunction is
                System.Windows.Media.Animation.CubicEase &&
            closeSurfaceExit?.Duration.HasTimeSpan == true &&
            closeSurfaceExit.Duration.TimeSpan ==
                TimeSpan.FromMilliseconds(180) &&
            closeSurfaceExit.EasingFunction is
                System.Windows.Media.Animation.QuadraticEase &&
            closeGlyphExit?.Duration.HasTimeSpan == true &&
            closeGlyphExit.Duration.TimeSpan ==
                TimeSpan.FromMilliseconds(180) &&
            closeGlyphExit.EasingFunction is
                System.Windows.Media.Animation.QuadraticEase;
        smokeWindow.ThemeModeCombo.IsDropDownOpen = true;
        smokeWindow.UpdateLayout();
        var comboPopup =
            smokeWindow.ThemeModeCombo.Template.FindName(
                "PART_Popup",
                smokeWindow.ThemeModeCombo)
            as System.Windows.Controls.Primitives.Popup;
        var comboDropDownScrollViewer =
            FindVisualDescendant<System.Windows.Controls.ScrollViewer>(
                comboPopup?.Child);
        comboDropDownUsesPageScrollStyle =
            comboDropDownScrollViewer?.Style ==
            (System.Windows.Style)application.FindResource(
                "WindowsSettingsScrollViewerStyle") &&
            comboDropDownScrollViewer.CanContentScroll == false;
        smokeWindow.ThemeModeCombo.IsDropDownOpen = false;
        application.Dispatcher.Invoke(
            () => { },
            System.Windows.Threading.DispatcherPriority.ContextIdle);
        application.Dispatcher.Invoke(
            () => { },
            System.Windows.Threading.DispatcherPriority.ContextIdle);
        MicaWindowService.Apply(smokeWindow, useDarkMode: false);
        acrylicAccentApplied =
            MicaWindowService.LastAcrylicApplySucceeded &&
            MicaWindowService.LastSystemBackdropApplySucceeded &&
            MicaWindowService.LastFrameRefreshSucceeded;
        var transitionHandles = new List<string>
        {
            $"{new System.Windows.Interop.WindowInteropHelper(smokeWindow).Handle}/" +
            $"{MicaWindowService.ActiveBackdropWindowHandle}"
        };
        MicaWindowService.PrepareForMinimizeTransition(
            smokeWindow,
            smokeWindow.WindowRoot,
            useDarkMode: false);
        var minimizeMaterialWasApplied =
            ReferenceEquals(
                smokeWindow.WindowRoot.Background,
                lightMinimizeMaterial) &&
            (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 26100) ||
                MicaWindowService.LastRedirectionBitmapAlphaApplySucceeded);
        smokeWindow.WindowState = System.Windows.WindowState.Minimized;
        application.Dispatcher.Invoke(
            () => { },
            System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        transitionHandles.Add(
            $"{new System.Windows.Interop.WindowInteropHelper(smokeWindow).Handle}/" +
            $"{MicaWindowService.ActiveBackdropWindowHandle}");
        smokeWindow.WindowState = System.Windows.WindowState.Normal;
        application.Dispatcher.Invoke(
            () => { },
            System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        transitionHandles.Add(
            $"{new System.Windows.Interop.WindowInteropHelper(smokeWindow).Handle}/" +
                $"{MicaWindowService.ActiveBackdropWindowHandle}");
        MicaWindowService.CompleteMinimizeTransition(
            smokeWindow.WindowRoot);
        minimizeTransitionMaterialAppliedAndCleared =
            minimizeMaterialWasApplied &&
            ReferenceEquals(
                smokeWindow.WindowRoot.Background,
                System.Windows.Media.Brushes.Transparent);
        smokeWindow.RequestMinimizeWithTransitionMaterial();
        var customMinimizeReachedTargetState = WaitForWindowState(
            smokeWindow,
            System.Windows.WindowState.Minimized,
            TimeSpan.FromSeconds(3));
        var customMinimizeUsedSnapshotSurface =
            customMinimizeReachedTargetState &&
            smokeWindow.WindowRoot.Background is
                System.Windows.Media.ImageBrush
                {
                    ImageSource: not null
                };
        smokeWindow.WindowState = System.Windows.WindowState.Normal;
        minimizeCommandUsesTransitionPipeline =
            customMinimizeUsedSnapshotSurface &&
            ReferenceEquals(
                smokeWindow.WindowRoot.Background,
                System.Windows.Media.Brushes.Transparent);
        smokeWindow.WindowState = System.Windows.WindowState.Maximized;
        application.Dispatcher.Invoke(
            () => { },
            System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        transitionHandles.Add(
            $"{new System.Windows.Interop.WindowInteropHelper(smokeWindow).Handle}/" +
            $"{MicaWindowService.ActiveBackdropWindowHandle}");
        smokeWindow.WindowState = System.Windows.WindowState.Normal;
        application.Dispatcher.Invoke(
            () => { },
            System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        transitionHandles.Add(
            $"{new System.Windows.Interop.WindowInteropHelper(smokeWindow).Handle}/" +
            $"{MicaWindowService.ActiveBackdropWindowHandle}");
        systemBackdropStableDuringWindowTransitions =
            MicaWindowService.ActiveBackdropWindowHandle ==
                new System.Windows.Interop.WindowInteropHelper(
                    smokeWindow).Handle &&
            MicaWindowService.LastAcrylicApplySucceeded;
        systemBackdropTransitionDiagnostics =
            $"最后写入 {MicaWindowService.LastAcrylicApplySucceeded}，" +
            $"窗口句柄 {string.Join(" -> ", transitionHandles)}，" +
            $"错误 {MicaWindowService.LastAcrylicError ?? "无"}";
        quietTimeEditorsConfigured =
            Math.Abs(smokeWindow.DoNotDisturbStartBox.ActualWidth - 86d) <= 0.1d &&
            Math.Abs(smokeWindow.DoNotDisturbEndBox.ActualWidth - 86d) <= 0.1d &&
            Math.Abs(smokeWindow.DoNotDisturbStartBox.ActualHeight - 40d) <= 0.1d &&
            Math.Abs(smokeWindow.DoNotDisturbEndBox.ActualHeight - 40d) <= 0.1d &&
            smokeWindow.DoNotDisturbStartBox.TextAlignment ==
                System.Windows.TextAlignment.Center &&
            smokeWindow.DoNotDisturbEndBox.TextAlignment ==
                System.Windows.TextAlignment.Center &&
            smokeWindow.DoNotDisturbStartBox.VerticalContentAlignment ==
                System.Windows.VerticalAlignment.Center &&
            smokeWindow.DoNotDisturbEndBox.VerticalContentAlignment ==
                System.Windows.VerticalAlignment.Center;
        var deviceListScrollBar =
            FindVisualDescendant<System.Windows.Controls.Primitives.ScrollBar>(
                smokeWindow.DeviceList,
                scrollBar =>
                    scrollBar.Orientation ==
                        System.Windows.Controls.Orientation.Vertical &&
                    scrollBar.Visibility ==
                        System.Windows.Visibility.Visible);
        var settingsScrollBar =
            FindVisualDescendant<System.Windows.Controls.Primitives.ScrollBar>(
                smokeWindow.SettingsPanel,
                scrollBar =>
                    scrollBar.Orientation ==
                        System.Windows.Controls.Orientation.Vertical &&
                    scrollBar.Visibility ==
                        System.Windows.Visibility.Visible);
        deviceListScrollBarVisible = deviceListScrollBar is not null;
        settingsScrollBarVisible = settingsScrollBar is not null;
        deviceListScrollThumbHeight =
            FindVisualDescendant<System.Windows.Controls.Primitives.Thumb>(
                deviceListScrollBar)?.ActualHeight ?? 0d;
        settingsScrollThumbHeight =
            FindVisualDescendant<System.Windows.Controls.Primitives.Thumb>(
                settingsScrollBar)?.ActualHeight ?? 0d;
        var firstDeviceItem =
            smokeWindow.DeviceList.ItemContainerGenerator.ContainerFromIndex(0)
                as System.Windows.Controls.ListBoxItem;
        firstDeviceItem?.ApplyTemplate();
        var deviceHoverMultiTrigger = firstDeviceItem?.Template.Triggers
            .OfType<System.Windows.MultiTrigger>()
            .FirstOrDefault(trigger =>
                trigger.Conditions.Any(condition =>
                    condition.Property ==
                        System.Windows.UIElement.IsMouseOverProperty &&
                    Equals(condition.Value, true)) &&
                trigger.Conditions.Any(condition =>
                    condition.Property ==
                        System.Windows.Controls.ListBoxItem
                            .IsSelectedProperty &&
                    Equals(condition.Value, false)));
        var deviceHoverEnterAnimation =
            deviceHoverMultiTrigger?.EnterActions
            .OfType<System.Windows.Media.Animation.BeginStoryboard>()
            .SelectMany(action => action.Storyboard.Children)
            .OfType<System.Windows.Media.Animation.DoubleAnimation>()
            .FirstOrDefault(animation =>
                System.Windows.Media.Animation.Storyboard
                    .GetTargetName(animation) == "HoverBorder");
        var deviceHoverExitAnimation =
            deviceHoverMultiTrigger?.ExitActions
            .OfType<System.Windows.Media.Animation.BeginStoryboard>()
            .SelectMany(action => action.Storyboard.Children)
            .OfType<System.Windows.Media.Animation.DoubleAnimation>()
            .FirstOrDefault(animation =>
                System.Windows.Media.Animation.Storyboard
                    .GetTargetName(animation) == "HoverBorder");
        var deviceHoverBorder =
            firstDeviceItem?.Template.FindName(
                "HoverBorder",
                firstDeviceItem) as System.Windows.Controls.Border;
        var deviceSelectedBorder =
            firstDeviceItem?.Template.FindName(
                "SelectedBorder",
                firstDeviceItem) as System.Windows.Controls.Border;
        var deviceSelectedTrigger =
            firstDeviceItem?.Template.Triggers
                .OfType<System.Windows.Trigger>()
                .FirstOrDefault(trigger =>
                    trigger.Property ==
                        System.Windows.Controls.ListBoxItem
                            .IsSelectedProperty &&
                    Equals(trigger.Value, true));
        var selectedBorderSetter =
            deviceSelectedTrigger?.Setters
                .OfType<System.Windows.Setter>()
                .FirstOrDefault(setter =>
                    setter.TargetName == "SelectedBorder" &&
                    setter.Property ==
                        System.Windows.Controls.Border.OpacityProperty);
        deviceListHoverAnimationConfigured =
            deviceHoverBorder?.Background is
                System.Windows.Media.SolidColorBrush hoverBrush &&
            hoverBrush.Color ==
                (System.Windows.Media.Color)application.Resources[
                    "DeviceListItemSelectedColor"] &&
            deviceSelectedBorder?.Background is
                System.Windows.Media.SolidColorBrush selectedBrush &&
            selectedBrush.Color ==
                (System.Windows.Media.Color)application.Resources[
                    "DeviceListItemSelectedColor"] &&
            Equals(selectedBorderSetter?.Value, 1d) &&
            deviceHoverEnterAnimation?.Duration.HasTimeSpan == true &&
            deviceHoverEnterAnimation.Duration.TimeSpan ==
                TimeSpan.FromMilliseconds(240) &&
            deviceHoverEnterAnimation.EasingFunction is
                System.Windows.Media.Animation.CubicEase &&
            deviceHoverExitAnimation?.Duration.HasTimeSpan == true &&
            deviceHoverExitAnimation.Duration.TimeSpan ==
                TimeSpan.FromMilliseconds(200) &&
            deviceHoverExitAnimation.EasingFunction is
                System.Windows.Media.Animation.QuadraticEase;
        if (!string.IsNullOrWhiteSpace(visualQaImagePath))
        {
            var populatingSettingsField = typeof(MainWindow).GetField(
                "_isPopulatingAppSettings",
                BindingFlags.Instance | BindingFlags.NonPublic)!;
            populatingSettingsField.SetValue(smokeWindow, true);
            smokeWindow.DoNotDisturbStartBox.Text = "23:55";
            smokeWindow.DoNotDisturbEndBox.Text = "06:00";
            populatingSettingsField.SetValue(smokeWindow, false);
            smokeWindow.SettingsPanel.ScrollToVerticalOffset(
                smokeWindow.SettingsPanel.ScrollableHeight);
            smokeWindow.UpdateLayout();
            RenderWindowToPng(smokeWindow, visualQaImagePath);
            smokeWindow.SettingsPanel.ScrollToTop();
        }
        if (!string.IsNullOrWhiteSpace(darkVisualQaImagePath))
        {
            ThemeService.Apply(ThemeService.DarkMode);
            typeof(MainWindow).GetMethod(
                    "UpdateSelectedDevice",
                    BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(smokeWindow, [testDevices[0], true]);
            smokeWindow.SettingsPanel.Visibility = System.Windows.Visibility.Collapsed;
            smokeWindow.EmptyPanel.Visibility = System.Windows.Visibility.Collapsed;
            smokeWindow.DetailPanel.Visibility = System.Windows.Visibility.Visible;
            smokeWindow.Width = 1100;
            smokeWindow.Height = 900;
            smokeWindow.UpdateLayout();
            var detailStack =
                smokeWindow.DetailPanel.Content as
                    System.Windows.Controls.StackPanel;
            var detailTexts =
                detailStack?.Children
                    .OfType<System.Windows.Controls.TextBlock>()
                    .Select((child, index) =>
                        new { Text = child.Text, Index = index })
                    .ToList() ?? [];
            var settingsSectionIndex =
                detailTexts.FirstOrDefault(item =>
                    item.Text == "设备设置")?.Index ?? -1;
            var historySectionIndex =
                detailTexts.FirstOrDefault(item =>
                    item.Text == "上下线历史")?.Index ?? -1;
            detailSectionOrderConfigured =
                settingsSectionIndex >= 0 &&
                historySectionIndex >= 0 &&
                historySectionIndex < settingsSectionIndex;
            deviceProfileControlsHaveMatchingHeight =
                Math.Abs(
                    smokeWindow.CustomNameBox.ActualHeight -
                    smokeWindow.DeviceTypeCombo.ActualHeight) <= 0.1d &&
                Math.Abs(smokeWindow.CustomNameBox.ActualHeight - 48d) <= 0.1d &&
                smokeWindow.CustomNameBox.TextWrapping ==
                    System.Windows.TextWrapping.NoWrap &&
                smokeWindow.CustomNameBox.Padding ==
                    new System.Windows.Thickness(12, 4, 12, 4) &&
                smokeWindow.CustomNameBox.Text == "iPhone 13 Pro Max";
            detailTitleSpacingConfigured =
                smokeWindow.DetailIconColumn.Width.IsAbsolute &&
                Math.Abs(smokeWindow.DetailIconColumn.Width.Value - 74d) <= 0.1d &&
                smokeWindow.DetailIconColumn.Parent is System.Windows.Controls.Grid detailHeader &&
                detailHeader.Children
                    .OfType<System.Windows.Controls.StackPanel>()
                    .Any(panel =>
                        System.Windows.Controls.Grid.GetColumn(panel) == 1 &&
                        panel.Margin.Left == 16d);
            detailIconThemeConfigured =
                smokeWindow.DetailDeviceIconBackground.Background is
                    System.Windows.Media.SolidColorBrush detailIconBrush &&
                detailIconBrush.Color ==
                    (System.Windows.Media.Color)application.Resources["DeviceIconColor"];
            smokeWindow.DetailPanel.ScrollToVerticalOffset(
                smokeWindow.DetailPanel.ScrollableHeight);
            smokeWindow.UpdateLayout();
            RenderWindowToPng(smokeWindow, darkVisualQaImagePath);
            ThemeService.Apply(ThemeService.LightMode);
        }
        smokeWindow.PrepareForExit();
        smokeWindow.Close();

        var selectionWindow = new MainWindow(new NotificationService())
        {
            Left = -10000,
            Top = -10000,
            Width = 900,
            Height = 560,
            ShowActivated = false,
            ShowInTaskbar = false
        };
        var replaceDevicesMethod = typeof(MainWindow).GetMethod(
            "ReplaceDevices",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        replaceDevicesMethod.Invoke(
            selectionWindow,
            [CreateSelectionRaceDevices(), false, false]);
        selectionWindow.Show();
        selectionWindow.UpdateLayout();
        sessionHistoryUsesStableItemsSource =
            ReferenceEquals(
                selectionWindow.SessionHistoryItems.ItemsSource,
                selectionWindow.DetailSessionItems);
        selectionWindow.DeviceList.SelectedIndex = 1;
        var firstDeviceBeforeRefresh = selectionWindow.DeviceList.Items[0];
        replaceDevicesMethod.Invoke(
            selectionWindow,
            [CreateSelectionRaceDevices(), true, false]);
        deviceSelectionStableDuringRefresh =
            selectionWindow.DeviceList.SelectedItem is
                DeviceItemViewModel selectedAfterRefresh &&
            selectedAfterRefresh.MacAddress == "00:00:00:00:10:02";
        deviceViewModelsReusedDuringRefresh =
            ReferenceEquals(firstDeviceBeforeRefresh, selectionWindow.DeviceList.Items[0]);
        replaceDevicesMethod.Invoke(
            selectionWindow,
            [CreateFilterDevices(), false, false]);
        selectionWindow.DeviceFilterSegments.ApplyTemplate();
        filterHoverAndPressConfigured = selectionWindow.DeviceFilterSegments.Template.FindName(
            "PART_Indicator", selectionWindow.DeviceFilterSegments) is System.Windows.Controls.Border;
        selectionWindow.DeviceFilterSegments.SelectedIndex = 1;
        var selectedDeviceBeforeOfflineListSlide = selectionWindow.SelectedDevice;
        filterAnimationUsesRenderCache =
            !System.Windows.SystemParameters.ClientAreaAnimation ||
            selectionWindow.GetDeviceListMotion(selectionWindow.DeviceList).CacheMode is System.Windows.Media.BitmapCache &&
            selectionWindow.GetDeviceListMotion(selectionWindow.DeviceListTransition).CacheMode is System.Windows.Media.BitmapCache &&
            selectionWindow.DetailPanel.CacheMode is System.Windows.Media.BitmapCache;
        var transitionFirstContainer =
            selectionWindow.DeviceListTransition.ItemContainerGenerator
                .ContainerFromIndex(0) as System.Windows.Controls.ListBoxItem;
        var outgoingFirstContainer =
            selectionWindow.DeviceList.ItemContainerGenerator
                .ContainerFromIndex(0) as System.Windows.Controls.ListBoxItem;
        filterTransitionCacheUsesMaterializedTarget =
            !System.Windows.SystemParameters.ClientAreaAnimation ||
            selectionWindow.GetDeviceListMotion(selectionWindow.DeviceListTransition).CacheMode is
                System.Windows.Media.BitmapCache &&
            transitionFirstContainer?.DataContext is DeviceItemViewModel incoming &&
            incoming.MacAddress == "00:00:00:00:30:04" &&
            outgoingFirstContainer?.DataContext is DeviceItemViewModel outgoing &&
            outgoing.MacAddress == "00:00:00:00:30:02";
        PumpWpf(
            selectionWindow.Dispatcher,
            TimeSpan.FromMilliseconds(320));
        filterChangeKeepsOriginalSynchronizedAnimation =
            !System.Windows.SystemParameters.ClientAreaAnimation ||
            !ReferenceEquals(
                selectionWindow.SelectedDevice,
                selectedDeviceBeforeOfflineListSlide) &&
            selectionWindow.DeviceListTransition.Visibility ==
                System.Windows.Visibility.Visible;
        var offlineTransitionSlide =
            selectionWindow.GetDeviceListMotion(selectionWindow.DeviceListTransition).RenderTransform as
                System.Windows.Media.TranslateTransform;
        var offlineTransitionVisible =
            selectionWindow.DeviceListTransition.Visibility ==
                System.Windows.Visibility.Visible;
        var offlineListSlide =
            selectionWindow.GetDeviceListMotion(selectionWindow.DeviceList).RenderTransform as
                System.Windows.Media.TranslateTransform;
        var offlineDetailTransform =
            selectionWindow.DetailPanel.RenderTransform as
                System.Windows.Media.TransformGroup;
        var offlineDetailScale =
            offlineDetailTransform?.Children
                .OfType<System.Windows.Media.ScaleTransform>()
                .FirstOrDefault();
        var offlineDetailTranslate =
            offlineDetailTransform?.Children
                .OfType<System.Windows.Media.TranslateTransform>()
                .FirstOrDefault();
        var offlineDetailOpacity =
            selectionWindow.DetailPanel.Opacity;
        var offlineSlideOk =
            (offlineTransitionSlide?.X ?? -1d) > 0d &&
            (offlineTransitionSlide?.X ?? -1d) < 340d &&
            (offlineListSlide?.X ?? 1d) < 0d &&
            (offlineListSlide?.X ?? 1d) > -340d &&
            offlineTransitionVisible;
        var offlineSlideDiag =
            $"离线滑入 X={offlineTransitionSlide?.X}，" +
            $"旧列表 X={offlineListSlide?.X}，可见={offlineTransitionVisible}";
        var offlineDetailOk =
            (offlineDetailScale?.ScaleX ?? 0d) >= 0.975d &&
            (offlineDetailScale?.ScaleX ?? 0d) <= 1d &&
            Math.Abs(offlineDetailTranslate?.X ?? 999d) <= 0.01d &&
            (offlineDetailTranslate?.Y ?? -1d) >= 0d &&
            (offlineDetailTranslate?.Y ?? -1d) <= 16d &&
            offlineDetailOpacity >= 0d &&
            offlineDetailOpacity <= 1d &&
            ((offlineDetailScale?.ScaleX ?? 1d) < 0.999d ||
             (offlineDetailTranslate?.Y ?? 0d) > 0.01d ||
             offlineDetailOpacity < 0.999d);
        var offlineDetailDiag =
            $"离线缩放={offlineDetailScale?.ScaleX}，" +
            $"离线位移={offlineDetailTranslate?.X},{offlineDetailTranslate?.Y}，" +
            $"透明度={offlineDetailOpacity}";
        PumpWpf(
            selectionWindow.Dispatcher,
            TimeSpan.FromMilliseconds(1300));
        var offlineOrder = selectionWindow.DeviceList.Items
            .Cast<DeviceItemViewModel>()
            .Select(device => device.MacAddress)
            .ToList();
        var deviceManagementParent =
            selectionWindow.DeviceManagementSection.Parent as
                System.Windows.Controls.StackPanel;
        offlineDeviceManagementConfigured =
            selectionWindow.DeviceManagementSection.Visibility ==
                System.Windows.Visibility.Visible &&
            deviceManagementParent is not null &&
            deviceManagementParent.Children.IndexOf(
                selectionWindow.DeviceManagementSection) ==
                deviceManagementParent.Children.Count - 1 &&
            selectionWindow.RemoveDeviceButton.Height ==
                selectionWindow.RestartRouterButton.Height &&
            selectionWindow.RemoveDeviceButton.Padding ==
                selectionWindow.RestartRouterButton.Padding &&
            Equals(
                selectionWindow.RemoveDeviceButton.Background,
                selectionWindow.RestartRouterButton.Background);
        selectionWindow.DeviceFilterSegments.SelectedIndex = 0;
        var onlineTransitionSlide =
            selectionWindow.GetDeviceListMotion(selectionWindow.DeviceListTransition).RenderTransform as
                System.Windows.Media.TranslateTransform;
        var onlineListSlide =
            selectionWindow.GetDeviceListMotion(selectionWindow.DeviceList).RenderTransform as
                System.Windows.Media.TranslateTransform;
        var onlineDetailTransform =
            selectionWindow.DetailPanel.RenderTransform as
                System.Windows.Media.TransformGroup;
        var onlineDetailScale =
            onlineDetailTransform?.Children
                .OfType<System.Windows.Media.ScaleTransform>()
                .FirstOrDefault();
        var onlineDetailTranslate =
            onlineDetailTransform?.Children
                .OfType<System.Windows.Media.TranslateTransform>()
                .FirstOrDefault();
        var onlineDetailOpacity =
            selectionWindow.DetailPanel.Opacity;
        var onlineSlideOk =
            Math.Abs(
                (onlineTransitionSlide?.X ?? 0d) + 340d) <= 0.5d &&
            Math.Abs(onlineListSlide?.X ?? 999d) <= 0.5d;
        var onlineSlideDiag =
            $"在线滑入 X={onlineTransitionSlide?.X}，" +
            $"旧列表 X={onlineListSlide?.X}";
        var onlineDetailOk =
            Math.Abs(
                (onlineDetailScale?.ScaleX ?? 0d) - 1d) <= 0.01d &&
            Math.Abs(onlineDetailTranslate?.X ?? 999d) <= 0.01d &&
            Math.Abs(onlineDetailTranslate?.Y ?? 999d) <= 0.01d &&
            Math.Abs(onlineDetailOpacity - 1d) <= 0.01d;
        var onlineDetailDiag =
            $"在线缩放={onlineDetailScale?.ScaleX}，" +
            $"在线位移={onlineDetailTranslate?.X},{onlineDetailTranslate?.Y}，" +
            $"透明度={onlineDetailOpacity}";
        listFilterSlideConfigured =
            !System.Windows.SystemParameters.ClientAreaAnimation ||
            (offlineSlideOk && onlineSlideOk);
        listFilterSlideDiagnostics =
            $"{offlineSlideDiag}，{onlineSlideDiag}";
        detailFilterOriginalAnimationConfigured =
            !System.Windows.SystemParameters.ClientAreaAnimation ||
            (offlineDetailOk && onlineDetailOk);
        detailFilterSlideDiagnostics =
            $"{offlineDetailDiag}，{onlineDetailDiag}";
        PumpWpf(
            selectionWindow.Dispatcher,
            TimeSpan.FromMilliseconds(1300));
        var onlineOrder = selectionWindow.DeviceList.Items
            .Cast<DeviceItemViewModel>()
            .Select(device => device.MacAddress)
            .ToList();
        deviceFilterAndSortConfigured =
            offlineOrder.SequenceEqual(
                ["00:00:00:00:30:04", "00:00:00:00:30:03"]) &&
            onlineOrder.SequenceEqual(
                ["00:00:00:00:30:02", "00:00:00:00:30:01"]);
        offlineDeviceManagementConfigured =
            offlineDeviceManagementConfigured &&
            selectionWindow.DeviceManagementSection.Visibility ==
                System.Windows.Visibility.Collapsed;
        selectionWindow.RemoveDeviceButton.RaiseEvent(
            new System.Windows.RoutedEventArgs(
                System.Windows.Controls.Button.ClickEvent));
        var removeConfirmVisible =
            selectionWindow.RemoveDeviceConfirmBorder.Visibility ==
                System.Windows.Visibility.Visible &&
            Math.Abs(
                selectionWindow.RemoveDeviceConfirmBorder.Opacity) <=
                0.01d;
        var removeConfirmTransform =
            selectionWindow.RemoveDeviceConfirmBorder.RenderTransform as
                System.Windows.Media.TransformGroup;
        var removeConfirmScale =
            removeConfirmTransform?.Children
                .OfType<System.Windows.Media.ScaleTransform>()
                .FirstOrDefault();
        var removeConfirmTranslate =
            removeConfirmTransform?.Children
                .OfType<System.Windows.Media.TranslateTransform>()
                .FirstOrDefault();
        var removeConfirmStyled =
            Equals(
                selectionWindow.RemoveDeviceConfirmBorder.Background,
                selectionWindow.FindResource("ConfirmSurfaceBrush")) &&
            Equals(
                selectionWindow.RemoveDeviceConfirmBorder.BorderBrush,
                selectionWindow.FindResource("BorderBrush"));
        var removeConfirmAnimationStarted =
            Math.Abs(
                (removeConfirmScale?.ScaleX ?? 0d) - 0.96d) <= 0.01d &&
            Math.Abs(
                (removeConfirmTranslate?.Y ?? 0d) + 6d) <= 0.01d;
        replaceDevicesMethod.Invoke(
            selectionWindow,
            [CreateFilterDevices(), true, false]);
        var confirmationSurvivesRefresh =
            selectionWindow.RemoveDeviceConfirmBorder.Visibility ==
                System.Windows.Visibility.Visible;
        selectionWindow.DeviceList.SelectedIndex = 1;
        PumpWpf(
            selectionWindow.Dispatcher,
            TimeSpan.FromMilliseconds(700));
        var confirmationDismissedOnDeviceSwitch =
            selectionWindow.RemoveDeviceConfirmBorder.Visibility ==
                System.Windows.Visibility.Collapsed;
        selectionWindow.RemoveDeviceButton.RaiseEvent(
            new System.Windows.RoutedEventArgs(
                System.Windows.Controls.Button.ClickEvent));
        var outsideClickArgs =
            new System.Windows.Input.MouseButtonEventArgs(
                System.Windows.Input.Mouse.PrimaryDevice,
                0,
                System.Windows.Input.MouseButton.Left)
            {
                RoutedEvent =
                    System.Windows.UIElement
                        .PreviewMouseLeftButtonDownEvent,
                Source = selectionWindow.OnlineCountText
            };
        selectionWindow.RaiseEvent(outsideClickArgs);
        PumpWpf(
            selectionWindow.Dispatcher,
            TimeSpan.FromMilliseconds(300));
        confirmationBehaviorConfigured =
            removeConfirmVisible &&
            removeConfirmAnimationStarted &&
            removeConfirmStyled &&
            confirmationSurvivesRefresh &&
            confirmationDismissedOnDeviceSwitch &&
            selectionWindow.RemoveDeviceConfirmBorder.Visibility ==
                System.Windows.Visibility.Collapsed;
        selectionWindow.PrepareForExit();
        selectionWindow.Close();

        var menu = new TrayMenuWindow(
            () => trayMenuOpenInvoked = true,
            () => { })
        {
            Left = -10000,
            Top = -10000
        };
        menu.Show();
        menu.UpdateLayout();
        Check(
            menu.OpenIconViewbox.Width == 16d &&
            menu.ExitIconViewbox.Width == 16d,
            "托盘菜单两个矢量图标应使用一致的 16 DIP 画布");
        var openIconCenter = menu.OpenIconViewbox.TranslatePoint(
            new System.Windows.Point(
                menu.OpenIconViewbox.ActualWidth / 2d,
                menu.OpenIconViewbox.ActualHeight / 2d),
            menu);
        var openTextCenter = menu.OpenText.TranslatePoint(
            new System.Windows.Point(
                menu.OpenText.ActualWidth / 2d,
                menu.OpenText.ActualHeight / 2d),
            menu);
        trayOpenIconTextAligned =
            Math.Abs(openIconCenter.Y - openTextCenter.Y) <= 0.5d;
        var trayHoverTrigger = menu.OpenButton.Template.Triggers
            .OfType<System.Windows.Trigger>()
            .FirstOrDefault(trigger =>
                trigger.Property == System.Windows.UIElement.IsMouseOverProperty &&
                Equals(trigger.Value, true));
        var trayHoverAnimation = trayHoverTrigger?.EnterActions
            .OfType<System.Windows.Media.Animation.BeginStoryboard>()
            .SelectMany(action => action.Storyboard.Children)
            .OfType<System.Windows.Media.Animation.DoubleAnimation>()
            .FirstOrDefault();
        trayMenuHoverAnimationConfigured =
            trayHoverAnimation?.Duration.HasTimeSpan == true &&
            trayHoverAnimation.Duration.TimeSpan ==
                TimeSpan.FromMilliseconds(180) &&
            trayHoverAnimation.EasingFunction is
                System.Windows.Media.Animation.CubicEase;
        menu.OpenButton.ApplyTemplate();
        var trayHoverSurface = menu.OpenButton.Template.FindName(
            "HoverSurface",
            menu.OpenButton) as System.Windows.Controls.Border;
        trayHoverColorAndSpacingConfigured =
            trayHoverSurface?.Background is
                System.Windows.Media.SolidColorBrush trayHoverBrush &&
            trayHoverBrush.Color ==
                System.Windows.Media.Color.FromArgb(0x30, 0x60, 0x60, 0x60) &&
            Math.Abs(menu.OpenText.Margin.Left - 8d) <= 0.1d;
        if (!string.IsNullOrWhiteSpace(trayVisualQaImagePath))
        {
            RenderWindowToPng(menu, trayVisualQaImagePath);
        }
        var openMethod = typeof(TrayMenuWindow).GetMethod(
            "OpenButton_Click",
            BindingFlags.Instance | BindingFlags.NonPublic);
        openMethod!.Invoke(
            menu,
            [menu, new System.Windows.RoutedEventArgs()]);
        application.Shutdown();
    }
    catch (TargetInvocationException exception)
    {
        trayMenuOpenException = exception.InnerException ?? exception;
    }
    catch (Exception exception)
    {
        trayMenuOpenException = exception;
    }
});
trayMenuThread.SetApartmentState(ApartmentState.STA);
trayMenuThread.Start();
trayMenuThread.Join(TimeSpan.FromSeconds(20));
Check(
    !trayMenuThread.IsAlive &&
    trayMenuOpenException is null &&
    trayMenuOpenInvoked,
    $"主窗口启动与托盘菜单打开不应触发异常：{trayMenuOpenException}");
Check(
    repeatedMainWindowActivationException is null,
    $"重复激活已可见主窗口不应重新创建窗口根视觉：{repeatedMainWindowActivationException}");
Check(
    verticalScrollBarVisible &&
    verticalScrollBarWidth >= 10d &&
    verticalScrollBarMaximum > 0d &&
    verticalScrollBarViewport > 0d &&
    verticalScrollThumbVisible,
    $"内容溢出时应显示 Windows 设置风格细滚动条：可见={verticalScrollBarVisible}，宽度={verticalScrollBarWidth:0.##}，最大值={verticalScrollBarMaximum:0.##}，视口={verticalScrollBarViewport:0.##}，滑块高度={verticalScrollThumbHeight:0.##}，最小高度={verticalScrollThumbMinimumHeight:0.##}");
Check(
    scrollThumbDragAnimationConfigured,
    "滚动条按住与松开应分别使用 300 ms 的二次缓入和缓出动画");
Check(
    scrollThumbHoverAnimationConfigured,
    "滚动条悬停与非悬停应分别使用 300 ms 的非线性动画");
Check(
    deviceListScrollBarVisible &&
    settingsScrollBarVisible &&
    deviceListScrollThumbHeight >= 28d &&
    settingsScrollThumbHeight >= 28d,
    $"主窗口左右板块内容溢出时都应显示具有实际高度的细滚动条：左侧可见={deviceListScrollBarVisible}，左侧滑块={deviceListScrollThumbHeight:0.##}，右侧可见={settingsScrollBarVisible}，右侧滑块={settingsScrollThumbHeight:0.##}");
Check(
    deviceListHoverAnimationConfigured,
    "左侧设备条块的悬停与非悬停状态应使用非线性动画");
Check(
    trayMenuHoverAnimationConfigured,
    "托盘菜单的两个选项应分别使用独立的非线性悬停动画");
Check(
    trayHoverColorAndSpacingConfigured,
    "托盘菜单选项悬停层应加深至 18.8% 不透明度，图标与文字间距应增加 8 DIP");
Check(
    trayOpenIconTextAligned,
    "托盘菜单“打开主界面”的图标与文字应在同一垂直中心线上");
Check(
    themePaletteConfigured,
    "浅深色材质、下拉栏、时间轴与滚动条颜色资源应使用指定的不透明度和主题值");
Check(
    topButtonToolTipsReadableInDarkMode,
    "深色模式下路由器与设置按钮的提示框应使用可读的深色背景与文字");
Check(
    closeCaptionHoverConfigured,
    "关闭按钮悬停态应使用低饱和红色、浅色叉形图标，并以同步动画过渡");
Check(
    detailSectionOrderConfigured,
    "设备详情页中设备设置区域应位于上下线历史区域之后");
Check(
    capsuleControlGeometryBalanced,
    "输入框、密码框、选择框、下拉框与卡片的大小比例应与 v0.1.0-dev.48 一致");
Check(
    timelineDurationBadgeBorderConfigured,
    "在线时长徽标应使用 1 DIP 的主题自适应细描边");
Check(
    comboDropDownUsesPageScrollStyle,
    "所有下拉栏应复用普通页面的细滚动条与平滑滚动样式，并以 DIP 而不是条目数滚动");
Check(
    quietTimeEditorsConfigured,
    "免打扰开始与结束时间输入框应同为 86 x 40 DIP，且时间文字水平、垂直居中");
Check(
    deviceProfileControlsHaveMatchingHeight,
    "设备自定义名称输入框应保持完整单行显示，并与设备类型选择框同为 48 DIP 高度");
Check(
    detailTitleSpacingConfigured,
    "设备详情标题文字应在原位置基础上向右移动 8 DIP");
Check(
    detailIconThemeConfigured,
    "深色模式右侧详情设备图标应与左侧设备列表共用适配主题的背景资源");
Check(
    acrylicAccentApplied,
    $"Windows 应实际接受不会随失焦自动切换为回退色的持久 Acrylic 材质写入：{MicaWindowService.LastAcrylicError}");
Check(
    systemBackdropStableDuringWindowTransitions,
    $"最小化、还原和最大化期间应保持同一个持久 Acrylic 背景，且不创建覆盖 WPF 内容的独立合成目标（{systemBackdropTransitionDiagnostics}）");
Check(
    minimizeTransitionMaterialAppliedAndCleared,
    "最小化前应应用可进入 DWM 动画快照的非聚焦过渡材质，还原后必须立即清除且不影响常规态");
Check(
    minimizeCommandUsesTransitionPipeline,
    "主窗口最小化命令应在下一渲染帧进入动画，还原后也应在下一渲染帧恢复常态材质，不得额外停留固定延时");
Check(
    dangerConfirmButtonConfigured,
    "确认移除与确认重启按钮应使用低饱和红色底色和白色文字");
Check(
    deviceSelectionStableDuringRefresh,
    "用户选择新设备后，实时数据刷新不应把左侧选择抢回旧设备");
Check(
    deviceViewModelsReusedDuringRefresh,
    "实时刷新应复用已有设备视图模型，避免每秒重建 WPF 列表项");
Check(
    deviceFilterAndSortConfigured,
    "在线与离线统计卡应分别筛选设备，并按最近上线或离线时间倒序排列");
Check(
    filterHoverAndPressConfigured,
    "在线与离线统计卡悬停时应轻微放大上浮，并按非线性动画过渡");
Check(
    listFilterSlideConfigured,
    $"在线与离线切换时设备列表应按按钮左右方向水平滑动（{listFilterSlideDiagnostics}）");
Check(
    detailFilterOriginalAnimationConfigured,
    $"在线与离线切换导致设备详情变化时，右侧详情面板应使用原有切换动画（{detailFilterSlideDiagnostics}）");
Check(
    filterChangeKeepsOriginalSynchronizedAnimation,
    "在线与离线切换时，左侧列表和右侧详情应沿用原有同步动画时序");
Check(
    filterAnimationUsesRenderCache,
    "在线与离线切换动画期间应使用渲染缓存降低重绘开销，不改变动画时序");
Check(
    filterTransitionCacheUsesMaterializedTarget,
    "列表切换启用渲染缓存前应先完成进入列表的首个视觉条目生成，避免首帧把旧列表显示成目标列表");
Check(
    sessionHistoryUsesStableItemsSource,
    "详情上下线历史应复用稳定的数据源，切换设备时不重建整个历史控件树");
Check(
    offlineDeviceManagementConfigured,
    "设备管理栏应位于上下线历史下方、只对离线设备显示，并复用重启路由器按钮样式");
Check(
    confirmationBehaviorConfigured,
    "确认框应使用更亮的圆角矩形样式、非线性弹出动画，并支持点击外部自动收回");
Check(
    WindowPlacementService.HasVisibleArea(
        new SavedWindowPlacement(120, 80, 1300, 840, false),
        [new System.Drawing.Rectangle(0, 0, 1920, 1040)]),
    "位于工作区内的窗口布局应当可以恢复");
Check(
    !WindowPlacementService.HasVisibleArea(
        new SavedWindowPlacement(-2200, 80, -1020, 840, false),
        [new System.Drawing.Rectangle(0, 0, 1920, 1040)]),
    "已离开所有屏幕的窗口布局不应恢复");

if (args.Contains("--router", StringComparer.OrdinalIgnoreCase))
{
    var credentialService = new RouterCredentialService();
    var password = credentialService.ReadPassword("10.77.0.1");
    Check(!string.IsNullOrWhiteSpace(password), "Windows 凭据管理器中没有路由器密码");
    if (!string.IsNullOrWhiteSpace(password))
    {
        using var client = new XiaomiRouterClient();
        client.Configure("10.77.0.1", password);
        var snapshots = await client.GetDeviceSnapshotsAsync();
        Check(snapshots.Count > 0, "路由器没有返回设备快照");
        Check(
            snapshots.All(snapshot =>
                snapshot.UploadSpeedBytes >= 0 &&
                snapshot.DownloadSpeedBytes >= 0 &&
                snapshot.TotalUploadBytes >= 0 &&
                snapshot.TotalDownloadBytes >= 0),
            "设备流量字段包含无效值");
        var internetInfo = await client.GetInternetInfoAsync();
        Check(
            !string.IsNullOrWhiteSpace(internetInfo.Ipv4ConnectionType) &&
            !string.IsNullOrWhiteSpace(internetInfo.Ipv4Address),
            "路由器没有返回有效的 IPv4 上网信息");
        Check(
            !string.IsNullOrWhiteSpace(internetInfo.Ipv6ConnectionType),
            "路由器没有返回 IPv6 连接模式");
        var meshNodes = await client.GetMeshNodesAsync();
        Check(meshNodes.Count > 0, "路由器没有返回 Mesh 节点");
        Check(
            meshNodes.Any(node => node.IsGateway) &&
            meshNodes.All(node => node.ConnectedDeviceCount >= 0),
            "Mesh 节点或设备数量无效");
        Console.WriteLine(
            $"路由器联调：已读取 {snapshots.Count} 台在线设备和 {meshNodes.Count} 个 Mesh 节点。");
    }
}

if (failures.Count > 0)
{
    Console.Error.WriteLine($"自检失败：{failures.Count} 项");
    foreach (var failure in failures)
    {
        Console.Error.WriteLine($"- {failure}");
    }

    return 1;
}

Console.WriteLine($"自检通过：设备识别、单位换算、通知时段、受控滚动、细滚动条、版本显示、主窗口启动、托盘菜单与窗口布局共 {checksRun} 项；路由器模式另验证上网与 Mesh 信息。");
return 0;

void Check(bool condition, string message)
{
    checksRun++;
    if (!condition)
    {
        failures.Add(message);
    }
}

string? FindWorkspaceFile(params string[] relativeParts)
{
    for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
         directory is not null;
         directory = directory.Parent)
    {
        var candidate = directory.FullName;
        foreach (var relativePart in relativeParts)
        {
            candidate = Path.Combine(candidate, relativePart);
        }

        if (File.Exists(candidate))
        {
            return candidate;
        }
    }

    return null;
}

int RunNotificationOnceRegression()
{
    var occurredAt = new DateTimeOffset(2026, 8, 28, 20, 41, 13, TimeSpan.FromHours(8));
    var failures = new List<string>();
    var platform = new TestAppNotificationPlatform();
    using var service = new NotificationService(platform);

    service.Register();
    service.Register();

    var testResult = service.ShowTestNotification();
    var offlineResult = service.ShowDeviceState(
        "离线测试设备",
        isOnline: false,
        occurredAt);
    var onlineResult = service.ShowDeviceState(
        "上线测试设备",
        isOnline: true,
        occurredAt.AddMinutes(1));

    if (platform.ConfigureIdentityCalls != 1 ||
        platform.RegisterCalls != 1 ||
        NotificationService.StableAppUserModelId != "{95AFFE3D-014F-4F5F-AD8B-FD52B4E62CE3}" ||
        platform.StableAppUserModelId != NotificationService.StableAppUserModelId ||
        platform.DisplayName != "局域网设备助手" ||
        !platform.Calls.SequenceEqual(
            ["ConfigureIdentity", "IsSupported", "Register", "Show", "Show", "Show"]) ||
        platform.Delivered.Count != 3 ||
        testResult.Channel != NotificationDeliveryChannel.WindowsToast ||
        offlineResult.Channel != NotificationDeliveryChannel.WindowsToast ||
        onlineResult.Channel != NotificationDeliveryChannel.WindowsToast ||
        !testResult.WasSubmittedToWindows ||
        !offlineResult.WasSubmittedToWindows ||
        !onlineResult.WasSubmittedToWindows)
    {
        failures.Add(
            $"稳定 AUMID 应只配置一次、App SDK 应只注册一次，设置测试、离线和上线应各调用一次 Show；身份配置={platform.ConfigureIdentityCalls}，注册={platform.RegisterCalls}，Show={platform.Delivered.Count}，状态={testResult.Channel}/{offlineResult.Channel}/{onlineResult.Channel}");
    }
    else if (platform.Delivered[0].Title != "Windows 系统通知测试" ||
             platform.Delivered[1].Title != "离线测试设备已离线" ||
             platform.Delivered[2].Title != "上线测试设备已上线" ||
             !platform.Delivered[1].Message.Contains("2026年08月28日 20:41:13", StringComparison.Ordinal) ||
             !platform.Delivered[2].Message.Contains("2026年08月28日 20:42:13", StringComparison.Ordinal))
    {
        failures.Add("单通道投递必须保留设置测试、离线、上线的正确标题和发生时间");
    }

    var failingPlatform = new TestAppNotificationPlatform
    {
        ShowFailure = new InvalidOperationException("模拟 App SDK Show 结果不确定")
    };
    using (var failingService = new NotificationService(failingPlatform))
    {
        failingService.Register();
        var failedResult = failingService.ShowTestNotification();
        if (failingPlatform.Delivered.Count != 1 ||
            failingPlatform.RegisterCalls != 1 ||
            failedResult.Channel != NotificationDeliveryChannel.SubmissionUnknown)
        {
            failures.Add("App SDK Show 抛出异常后必须停止，不得重试、重新注册或切换到第二条通道");
        }
    }

    var registrationFailurePlatform = new TestAppNotificationPlatform
    {
        RegisterFailure = new InvalidOperationException("模拟 App SDK 注册失败")
    };
    using (var registrationFailureService = new NotificationService(registrationFailurePlatform))
    {
        var failedResult = registrationFailureService.ShowTestNotification();
        if (!registrationFailurePlatform.Calls.SequenceEqual(
                ["ConfigureIdentity", "IsSupported", "Register"]) ||
            registrationFailurePlatform.Delivered.Count != 0 ||
            failedResult.Channel != NotificationDeliveryChannel.Unavailable)
        {
            failures.Add("稳定身份配置、支持性检查或 App SDK 注册失败时必须停止，不得调用 Show");
        }
    }

    if (failures.Count > 0)
    {
        Console.Error.WriteLine($"通知至多一次回归失败：{failures.Count} 项");
        foreach (var failure in failures)
        {
            Console.Error.WriteLine($"- {failure}");
        }

        return 1;
    }

    Console.WriteLine("通知单通道回归通过：稳定 AUMID 只配置一次、App SDK 只注册一次，设置测试、设备离线和设备上线分别只调用一次 Show，Show 异常后不重试或切换通道。");
    return 0;
}

int RunRateArrowRegression()
{
    var mainWindowXamlPath = FindWorkspaceFile(
        "src",
        "LanDeviceMonitor.App",
        "MainWindow.xaml");
    if (mainWindowXamlPath is null)
    {
        Console.Error.WriteLine("速率箭头回归失败：找不到主窗口 XAML。");
        return 1;
    }

    var mainWindowDocument =
        System.Xml.Linq.XDocument.Load(mainWindowXamlPath);
    System.Xml.Linq.XNamespace presentationNamespace =
        "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    System.Xml.Linq.XNamespace xNamespace =
        "http://schemas.microsoft.com/winfx/2006/xaml";
    var arrowGeometry = mainWindowDocument
        .Descendants(presentationNamespace + "Geometry")
        .SingleOrDefault(element =>
            element.Attribute(xNamespace + "Key")?.Value ==
            "TrafficRateArrowGeometry");
    var downloadRateArrow = mainWindowDocument
        .Descendants(presentationNamespace + "Path")
        .SingleOrDefault(element =>
            element.Attribute(xNamespace + "Name")?.Value ==
            "DownloadRateArrow");
    var uploadRateArrow = mainWindowDocument
        .Descendants(presentationNamespace + "Path")
        .SingleOrDefault(element =>
            element.Attribute(xNamespace + "Name")?.Value ==
            "UploadRateArrow");
    var arrowData = arrowGeometry?.Value.Trim();
    var passed =
        arrowData == "M12 19.5V4.5M4.7 11.8L12 4.5L19.3 11.8" &&
        LinearArrowPathMatches(
            downloadRateArrow,
            "{DynamicResource AccentBrush}") &&
        LinearArrowPathMatches(
            uploadRateArrow,
            "{DynamicResource TrafficUploadBrush}") &&
        LinearArrowVisualBoundsMatch(arrowData) &&
        downloadRateArrow?.Attribute("RenderTransformOrigin")?.Value ==
            "0.5,0.5" &&
        downloadRateArrow
            .Element(presentationNamespace + "Path.RenderTransform")
            ?.Element(presentationNamespace + "RotateTransform")
            ?.Attribute("Angle")?.Value == "180" &&
        uploadRateArrow
            ?.Element(presentationNamespace + "Path.RenderTransform") is null;
    Console.WriteLine(
        passed
            ? "速率箭头回归通过：两枚箭头已放大且维持固定线宽。"
            : "速率箭头回归失败：左侧箭头大小或线宽不符合约束。");
    return passed ? 0 : 1;

    bool LinearArrowPathMatches(System.Xml.Linq.XElement? path, string brush) =>
        path is not null &&
        path.Attribute("Data")?.Value ==
            "{StaticResource TrafficRateArrowGeometry}" &&
        path.Attribute("Stroke")?.Value == brush &&
        path.Attribute("StrokeThickness")?.Value == "1.6" &&
        path.Attribute("Fill")?.Value == "Transparent" &&
        path.Attribute("Width")?.Value == "24" &&
        path.Attribute("Height")?.Value == "24" &&
        path.Attribute("Stretch")?.Value == "None" &&
        path.Attribute("StrokeStartLineCap")?.Value == "Round" &&
        path.Attribute("StrokeEndLineCap")?.Value == "Round" &&
        path.Attribute("StrokeLineJoin")?.Value == "Round" &&
        path.Parent?.Name != presentationNamespace + "Viewbox";

    bool LinearArrowVisualBoundsMatch(string data)
    {
        var geometry = System.Windows.Media.Geometry.Parse(data);
        var bounds = geometry.Bounds;
        return Math.Abs(bounds.X + bounds.Width / 2d - 12d) < 0.001d &&
               Math.Abs(bounds.Y + bounds.Height / 2d - 12d) < 0.001d &&
               Math.Abs(bounds.Width + 1.6d - 16.2d) < 0.001d &&
               Math.Abs(bounds.Height + 1.6d - 16.6d) < 0.001d;
    }
}

int RunTotalArrowRegression()
{
    var mainWindowXamlPath = FindWorkspaceFile(
        "src",
        "LanDeviceMonitor.App",
        "MainWindow.xaml");
    if (mainWindowXamlPath is null)
    {
        Console.Error.WriteLine("总量图标回归失败：找不到主窗口 XAML。");
        return 1;
    }

    var mainWindowDocument =
        System.Xml.Linq.XDocument.Load(mainWindowXamlPath);
    System.Xml.Linq.XNamespace presentationNamespace =
        "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    System.Xml.Linq.XNamespace xNamespace =
        "http://schemas.microsoft.com/winfx/2006/xaml";
    var downloadGeometry = FindResource(
        "TrafficTotalDownloadIconGeometry",
        "GeometryGroup");
    var uploadGeometry = FindResource(
        "TrafficTotalUploadIconGeometry",
        "GeometryGroup");
    var downloadIcon = FindPath("DownloadTotalIcon");
    var uploadIcon = FindPath("UploadTotalIcon");
    var passed =
        UnifiedGeometryMatches(
            downloadGeometry,
            "M14 8.6V19.4M8.8 14.2L14 19.4L19.2 14.2") &&
        UnifiedGeometryMatches(
            uploadGeometry,
            "M14 19.4V8.6M8.8 13.8L14 8.6L19.2 13.8") &&
        UnifiedIconPathMatches(
            downloadIcon,
            "{StaticResource TrafficTotalDownloadIconGeometry}",
            "{DynamicResource AccentBrush}") &&
        UnifiedIconPathMatches(
            uploadIcon,
            "{StaticResource TrafficTotalUploadIconGeometry}",
            "{DynamicResource TrafficUploadBrush}");
    Console.WriteLine(
        passed
            ? "总量图标回归通过：右侧总量图标已缩小且维持固定线宽。"
            : "总量图标回归失败：右侧图标大小、线宽或中心不符合约束。");
    return passed ? 0 : 1;

    System.Xml.Linq.XElement? FindPath(string name) =>
        FindElement(name, "Path");

    System.Xml.Linq.XElement? FindElement(string name, string localName) =>
        mainWindowDocument
        .Descendants()
        .SingleOrDefault(element =>
            element.Name.LocalName == localName &&
            element.Attribute(xNamespace + "Name")?.Value == name);

    System.Xml.Linq.XElement? FindResource(string resourceKey, string localName) =>
        mainWindowDocument
        .Descendants()
        .SingleOrDefault(element =>
            element.Name.LocalName == localName &&
            element.Attribute(xNamespace + "Key")?.Value == resourceKey);

    bool UnifiedIconPathMatches(
        System.Xml.Linq.XElement? path,
        string geometryResource,
        string brush) =>
        path is not null &&
        path.Attribute("Data")?.Value == geometryResource &&
        path.Attribute("Stroke")?.Value == brush &&
        path.Attribute("StrokeThickness")?.Value == "1.6" &&
        path.Attribute("Fill")?.Value == "Transparent" &&
        path.Attribute("Width")?.Value == "28" &&
        path.Attribute("Height")?.Value == "28" &&
        path.Attribute("Stretch")?.Value == "None" &&
        path.Attribute("StrokeStartLineCap")?.Value == "Round" &&
        path.Attribute("StrokeEndLineCap")?.Value == "Round" &&
        path.Attribute("StrokeLineJoin")?.Value == "Round" &&
        path.Element(presentationNamespace + "Path.RenderTransform") is null;

    bool UnifiedGeometryMatches(
        System.Xml.Linq.XElement? group,
        string arrowData)
    {
        try
        {
            if (group is null)
            {
                return false;
            }

            var children = group.Elements().ToArray();
            if (children.Length != 2 ||
                !EllipseMatches(children[0], "10.2") ||
                children[1].Name != presentationNamespace + "PathGeometry" ||
                children[1].Attribute("Figures")?.Value != arrowData ||
                children[1]
                    .Element(presentationNamespace + "PathGeometry.Transform") is not null)
            {
                return false;
            }

            var arrowGeometry = System.Windows.Media.Geometry.Parse(arrowData);
            var arrowBounds = arrowGeometry.Bounds;
            var visualArrowWidth = arrowBounds.Width + 1.6d;
            var visualArrowHeight = arrowBounds.Height + 1.6d;
            var visualRingDiameter = 2d * (10.2d + 0.8d);
            return Math.Abs(
                       arrowBounds.X + arrowBounds.Width / 2d - 14d) <
                   0.001d &&
                   Math.Abs(
                       arrowBounds.Y + arrowBounds.Height / 2d - 14d) <
                   0.001d &&
                   Math.Abs(visualArrowWidth - 12d) < 0.001d &&
                   Math.Abs(visualArrowHeight - 12.4d) < 0.001d &&
                   Math.Abs(visualRingDiameter - 22d) < 0.001d;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    bool EllipseMatches(System.Xml.Linq.XElement element, string radius) =>
        element.Name == presentationNamespace + "EllipseGeometry" &&
        element.Attribute("Center")?.Value == "14,14" &&
        element.Attribute("RadiusX")?.Value == radius &&
        element.Attribute("RadiusY")?.Value == radius;

}

int RunTrafficAccentRegression()
{
    var passed = false;
    var diagnostics = string.Empty;
    Exception? failure = null;
    var thread = new Thread(() =>
    {
        LanDeviceMonitor.App? application = null;
        MainWindow? window = null;
        try
        {
            application = TestAppFactory.Create();
            application.InitializeComponent();
            application.ShutdownMode =
                System.Windows.ShutdownMode.OnExplicitShutdown;
            ThemeService.Apply(ThemeService.LightMode);
            window = new MainWindow(new NotificationService())
            {
                Left = -10000,
                Top = -10000,
                Width = 900,
                Height = 560,
                ShowActivated = false,
                ShowInTaskbar = false
            };
            window.DetailPanel.Visibility = System.Windows.Visibility.Visible;
            window.Show();
            window.UpdateLayout();

            var lightMatches = MatchesTitleDeviceIconAccent(
                application,
                window);
            ThemeService.Apply(ThemeService.DarkMode);
            window.UpdateLayout();
            var darkMatches = MatchesTitleDeviceIconAccent(
                application,
                window);
            passed = lightMatches && darkMatches;
            diagnostics = $"浅色一致={lightMatches}，深色一致={darkMatches}";
        }
        catch (Exception exception)
        {
            failure = exception;
        }
        finally
        {
            try
            {
                window?.PrepareForExit();
                window?.Close();
                application?.Shutdown();
            }
            catch (Exception exception)
            {
                failure ??= exception;
            }
        }
    });
    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    thread.Join(TimeSpan.FromSeconds(20));

    if (thread.IsAlive)
    {
        Console.Error.WriteLine("流量统计主题色回归超时。 ");
        return 1;
    }

    if (failure is not null)
    {
        Console.Error.WriteLine($"流量统计主题色回归异常：{failure}");
        return 1;
    }

    Console.WriteLine(
        passed
            ? $"流量统计主题色回归通过：{diagnostics}。"
            : $"流量统计主题色回归失败：{diagnostics}。 ");
    return passed ? 0 : 1;

    bool MatchesTitleDeviceIconAccent(
        LanDeviceMonitor.App currentApplication,
        MainWindow currentWindow)
    {
        var accentBrush = currentApplication.Resources["AccentBrush"] as
            System.Windows.Media.SolidColorBrush;
        var titleDeviceIcon = FindVisualDescendant<DeviceTypeIcon>(
            currentWindow,
            icon => Math.Abs(icon.Width - 31d) < 0.001d &&
                    Math.Abs(icon.Height - 31d) < 0.001d);
        var trafficChart = FindVisualDescendant<TrafficChart>(currentWindow);
        var downloadRateArrow = currentWindow.FindName("DownloadRateArrow") as
            System.Windows.Shapes.Path;
        var downloadTotalIcon = currentWindow.FindName("DownloadTotalIcon") as
            System.Windows.Shapes.Path;
        return accentBrush is not null &&
               titleDeviceIcon is not null &&
               SameColor(titleDeviceIcon.IconBrush, accentBrush) &&
               trafficChart is not null &&
               SameColor(trafficChart.DownloadBrush, accentBrush) &&
               downloadRateArrow is not null &&
               SameColor(downloadRateArrow.Stroke, accentBrush) &&
               downloadTotalIcon is not null &&
               SameColor(downloadTotalIcon.Stroke, accentBrush);
    }

    static bool SameColor(
        System.Windows.Media.Brush? brush,
        System.Windows.Media.SolidColorBrush expected) =>
        brush is System.Windows.Media.SolidColorBrush solidBrush &&
        solidBrush.Color == expected.Color;
}

async Task<bool> MonitoringLoopRunsOffUiContextAsync()
{
    var loopType = typeof(MonitoringService).Assembly.GetType(
        "LanDeviceMonitor.Services.MonitoringLoop");
    if (loopType is null)
    {
        return false;
    }

    var interval = TimeSpan.FromMilliseconds(200);
    var firstPollStarted = new TaskCompletionSource<bool>(
        TaskCreationOptions.RunContinuationsAsynchronously);
    var secondPollStarted = new TaskCompletionSource<bool>(
        TaskCreationOptions.RunContinuationsAsynchronously);
    using var cancellation = new CancellationTokenSource();
    var synchronizationContexts = new List<SynchronizationContext?>();
    var firstPollStartedAt = Stopwatch.GetTimestamp();
    var secondPollStartedAt = Stopwatch.GetTimestamp();
    var pollCount = 0;

    var poll = new Func<CancellationToken, Task>(async token =>
    {
        lock (synchronizationContexts)
        {
            synchronizationContexts.Add(SynchronizationContext.Current);
        }

        if (Interlocked.Increment(ref pollCount) == 1)
        {
            firstPollStartedAt = Stopwatch.GetTimestamp();
            firstPollStarted.TrySetResult(true);
            await Task.Delay(TimeSpan.FromMilliseconds(30), token).ConfigureAwait(false);
            return;
        }

        secondPollStartedAt = Stopwatch.GetTimestamp();
        secondPollStarted.TrySetResult(true);
        cancellation.Cancel();
    });

    var loop = Activator.CreateInstance(loopType, interval, poll);
    var runMethod = loopType.GetMethod("RunAsync");
    if (loop is null || runMethod is null)
    {
        return false;
    }

    var previousContext = SynchronizationContext.Current;
    SynchronizationContext.SetSynchronizationContext(new SynchronizationContext());
    try
    {
        var runTask = (Task?)runMethod.Invoke(loop, [cancellation.Token]);
        if (runTask is null)
        {
            return false;
        }

        await firstPollStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await secondPollStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        try
        {
            await runTask;
        }
        catch (OperationCanceledException)
        {
        }

        var intervalBetweenPollStarts =
            (secondPollStartedAt - firstPollStartedAt) / (double)Stopwatch.Frequency;
        lock (synchronizationContexts)
        {
            return synchronizationContexts.Count >= 2 &&
                   synchronizationContexts.All(context => context is null) &&
                   intervalBetweenPollStarts >= interval.TotalSeconds * 0.75d &&
                   intervalBetweenPollStarts <= interval.TotalSeconds * 1.10d;
        }
    }
    finally
    {
        SynchronizationContext.SetSynchronizationContext(previousContext);
        cancellation.Cancel();
    }
}

T? FindVisualDescendant<T>(
    System.Windows.DependencyObject? root,
    Func<T, bool>? predicate = null)
    where T : System.Windows.DependencyObject
{
    if (root is null)
    {
        return null;
    }

    for (var index = 0;
         index < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
         index++)
    {
        var child =
            System.Windows.Media.VisualTreeHelper.GetChild(root, index);
        if (child is T match && (predicate is null || predicate(match)))
        {
            return match;
        }

        var descendant = FindVisualDescendant(child, predicate);
        if (descendant is not null)
        {
            return descendant;
        }
    }

    return null;
}

IReadOnlyList<LanDeviceMonitor.Models.DeviceViewData>
    CreateSelectionRaceDevices() =>
[
    new LanDeviceMonitor.Models.DeviceViewData
    {
        Profile = new LanDeviceMonitor.Models.DeviceProfile
        {
            MacAddress = "00:00:00:00:10:01",
            LastRouterName = "旧设备",
            LastKnownOnline = true,
            LastConnectionType = 2
        },
        Identity = new LanDeviceMonitor.Models.DeviceIdentity(
            "手机",
            "\uE8EA",
            "旧设备"),
        Sessions = []
    },
    new LanDeviceMonitor.Models.DeviceViewData
    {
        Profile = new LanDeviceMonitor.Models.DeviceProfile
        {
            MacAddress = "00:00:00:00:10:02",
            LastRouterName = "新设备",
            LastKnownOnline = true,
            LastConnectionType = 2
        },
        Identity = new LanDeviceMonitor.Models.DeviceIdentity(
            "手机",
            "\uE8EA",
            "新设备"),
        Sessions = []
    }
];

IReadOnlyList<LanDeviceMonitor.Models.DeviceViewData>
    CreateFilterDevices() =>
[
    CreateFilterDevice(
        "00:00:00:00:30:01",
        true,
        new DateTimeOffset(2026, 7, 30, 9, 0, 0, TimeSpan.FromHours(8)),
        null),
    CreateFilterDevice(
        "00:00:00:00:30:02",
        true,
        new DateTimeOffset(2026, 7, 30, 11, 0, 0, TimeSpan.FromHours(8)),
        null),
    CreateFilterDevice(
        "00:00:00:00:30:03",
        false,
        new DateTimeOffset(2026, 7, 29, 9, 0, 0, TimeSpan.FromHours(8)),
        new DateTimeOffset(2026, 7, 29, 10, 0, 0, TimeSpan.FromHours(8))),
    CreateFilterDevice(
        "00:00:00:00:30:04",
        false,
        new DateTimeOffset(2026, 7, 30, 9, 0, 0, TimeSpan.FromHours(8)),
        new DateTimeOffset(2026, 7, 30, 10, 0, 0, TimeSpan.FromHours(8)))
];

LanDeviceMonitor.Models.DeviceViewData CreateFilterDevice(
    string macAddress,
    bool isOnline,
    DateTimeOffset onlineAt,
    DateTimeOffset? offlineAt) =>
    new()
    {
        Profile = new LanDeviceMonitor.Models.DeviceProfile
        {
            MacAddress = macAddress,
            LastRouterName = macAddress,
            LastKnownOnline = isOnline,
            FirstSeenAt = onlineAt,
            LastSeenAt = offlineAt ?? onlineAt,
            LastConnectionType = 2
        },
        Identity = new LanDeviceMonitor.Models.DeviceIdentity(
            "手机",
            "\uE8EA",
            macAddress),
        Sessions =
        [
            new LanDeviceMonitor.Models.DeviceSession
            {
                MacAddress = macAddress,
                OnlineAt = onlineAt,
                OfflineAt = offlineAt
            }
        ]
    };

object GetStyleSetterValue(
    System.Windows.Style style,
    System.Windows.DependencyProperty property)
{
    return style.Setters
        .OfType<System.Windows.Setter>()
        .Single(setter => setter.Property == property)
        .Value;
}

void RenderWindowToPng(
    System.Windows.Window window,
    string outputPath)
{
    window.UpdateLayout();
    var width = Math.Max(1, (int)Math.Ceiling(window.ActualWidth));
    var height = Math.Max(1, (int)Math.Ceiling(window.ActualHeight));
    var target = new System.Windows.Media.Imaging.RenderTargetBitmap(
        width,
        height,
        96,
        96,
        System.Windows.Media.PixelFormats.Pbgra32);
    target.Render(window);
    var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
    encoder.Frames.Add(
        System.Windows.Media.Imaging.BitmapFrame.Create(target));
    var directory = Path.GetDirectoryName(outputPath);
    if (!string.IsNullOrWhiteSpace(directory))
    {
        Directory.CreateDirectory(directory);
    }

    using var stream = File.Create(outputPath);
    encoder.Save(stream);
}

void PumpWpf(
    System.Windows.Threading.Dispatcher dispatcher,
    TimeSpan duration)
{
    var frame = new System.Windows.Threading.DispatcherFrame();
    var timer = new System.Windows.Threading.DispatcherTimer(
        System.Windows.Threading.DispatcherPriority.Background,
        dispatcher)
    {
        Interval = duration
    };
    timer.Tick += (_, _) =>
    {
        timer.Stop();
        frame.Continue = false;
    };
    timer.Start();
    System.Windows.Threading.Dispatcher.PushFrame(frame);
}

int RunSettingsShortcutRegression()
{
    var failures = new List<string>();
    CheckRegression(
        DeviceItemViewModel.FormatSpeed(0) == "0\u2009KB/s" &&
        DeviceItemViewModel.FormatSpeed(1) == "0\u2009KB/s" &&
        DeviceItemViewModel.FormatSpeed(512) == "0.50\u2009KB/s" &&
        DeviceItemViewModel.FormatSpeed(460) == "0.45\u2009KB/s" &&
        DeviceItemViewModel.FormatSpeedUnit(0) == "KB/s",
        "网速为零时应直接显示 0，且最小单位仍为 KB/s");
    CheckRegression(
        TrafficChart.FormatRate(0) == "0\u2009KB" &&
        TrafficChart.FormatRate(1) == "0\u2009KB" &&
        TrafficChart.FormatRate(512) == "0.50\u2009KB" &&
        TrafficChart.FormatRate(1000) == "0.98\u2009KB" &&
        TrafficChart.FormatRate(460) == "0.45\u2009KB",
        "图表零速率应直接显示 0，且速率标签应使用 KB 起步的单位换算");
    CheckRegression(
        DeviceItemViewModel.FormatDateRange(
                new DateTimeOffset(2026, 8, 30, 23, 59, 58, TimeSpan.FromHours(8)),
                new DateTimeOffset(2026, 8, 31, 0, 0, 2, TimeSpan.FromHours(8))) ==
            "08月30日 ~ 08月31日",
        "总量卡片悬停日期区间应仅显示月日范围");
    CheckRegression(
        MainWindow.GetDisplayVersion() == "v0.1.0-dev.226",
        "设置界面应显示 dev.226 发布版本");
    var legacy = System.Text.Json.JsonSerializer.Deserialize<AppSettings>("{\"EmergencyHotKeyEnabled\":true,\"EmergencyHotKeyVirtualKey\":112,\"RefreshIntervalSeconds\":5}")!;
    CheckRegression(legacy.RefreshIntervalSeconds == 5 && typeof(AppSettings).GetProperty("EmergencyHotKeyEnabled") is null && typeof(AppSettings).Assembly.GetType("LanDeviceMonitor.Services.EmergencyHotKeyService") is null,
        "旧设置必须可读取，且不再包含或注册结束进程快捷键");

    CheckRegression(
        string.Equals(
            TrayService.BuildNotifyIconText(),
            "局域网设备助手",
            StringComparison.Ordinal),
        "托盘悬停文字应始终只显示应用名称");

    var trafficChartSourcePath = FindWorkspaceFile(
        "src",
        "LanDeviceMonitor.App",
        "Controls",
        "TrafficChart.cs");
    var trafficChartSource = trafficChartSourcePath is null
        ? string.Empty
        : File.ReadAllText(trafficChartSourcePath);
    CheckRegression(
        trafficChartSourcePath is not null &&
        !trafficChartSource.Contains("\"下载\"", StringComparison.Ordinal) &&
        !trafficChartSource.Contains("\"上传\"", StringComparison.Ordinal),
        "折线图右上角不应再绘制下载和上传图例文字");

    Exception? uiFailure = null;
    var uiChecksPassed = false;
    var thread = new Thread(() =>
    {
        LanDeviceMonitor.App? application = null;
        MainWindow? window = null;
        try
        {
            application = TestAppFactory.Create();
            application.InitializeComponent();
            application.ShutdownMode =
                System.Windows.ShutdownMode.OnExplicitShutdown;
            window = new MainWindow(new NotificationService());

            var summaryPanel = window.FindName("DetailNetworkSummaryPanel") as
                System.Windows.Controls.Grid;
            var metadataRow = window.FindName("DetailNetworkMetadataRow") as
                System.Windows.Controls.Grid;
            var metadataIpCell = metadataRow?.Children.Count > 0
                ? metadataRow.Children[0] as System.Windows.Controls.Grid
                : null;
            var ipLabelText = window.FindName("DetailIpLabelText") as
                System.Windows.Controls.TextBlock;
            var connectionText = window.FindName("DetailConnectionText") as
                System.Windows.Controls.TextBlock;
            var ipText = window.FindName("DetailIpAddressText") as
                System.Windows.Controls.TextBlock;
            var macLabelText = window.FindName("DetailMacLabelText") as
                System.Windows.Controls.TextBlock;
            var macText = window.FindName("DetailMacAddressText") as
                System.Windows.Controls.TextBlock;
            var downloadRateContent = window.FindName("DownloadRateMetricContent") as
                System.Windows.Controls.StackPanel;
            var uploadRateContent = window.FindName("UploadRateMetricContent") as
                System.Windows.Controls.StackPanel;
            var downloadTotalContent = window.FindName("DownloadTotalMetricContent") as
                System.Windows.Controls.StackPanel;
            var uploadTotalContent = window.FindName("UploadTotalMetricContent") as
                System.Windows.Controls.StackPanel;
            var downloadSpeedUnitText = window.FindName("DownloadSpeedUnitText") as
                System.Windows.Controls.TextBlock;
            var uploadSpeedUnitText = window.FindName("UploadSpeedUnitText") as
                System.Windows.Controls.TextBlock;
            var downloadTotalUnitText = window.FindName("DownloadTotalUnitText") as
                System.Windows.Controls.TextBlock;
            var uploadTotalUnitText = window.FindName("UploadTotalUnitText") as
                System.Windows.Controls.TextBlock;
            var downloadTotalCard = window.FindName("DownloadTotalCard") as
                System.Windows.Controls.Border;
            var uploadTotalCard = window.FindName("UploadTotalCard") as
                System.Windows.Controls.Border;
            var downloadTotalToolTip = downloadTotalCard?.ToolTip as
                System.Windows.Controls.ToolTip;
            var uploadTotalToolTip = uploadTotalCard?.ToolTip as
                System.Windows.Controls.ToolTip;
            var downloadTotalTitleText = window.FindName("DownloadTotalTitleText") as
                System.Windows.Controls.TextBlock;
            var uploadTotalTitleText = window.FindName("UploadTotalTitleText") as
                System.Windows.Controls.TextBlock;
            var shortcutEnabled = window.FindName("EmergencyHotKeyEnabledCheck") as
                System.Windows.Controls.CheckBox;
            var shortcutText = window.FindName("EmergencyHotKeyShortcutText") as
                System.Windows.Controls.TextBlock;
            var shortcutButton = window.FindName("EmergencyHotKeyChangeButton") as
                System.Windows.Controls.Button;
            var shortcutStatus = window.FindName("EmergencyHotKeyStatusText") as
                System.Windows.Controls.TextBlock;
            var trafficSummaryHeader = window.FindName("TrafficSummaryHeader") as
                System.Windows.Controls.Grid;
            var trafficSummaryTitle = window.FindName("TrafficSummaryTitleText") as
                System.Windows.Controls.TextBlock;
            var sessionHistoryTitle = window.FindName("SessionHistoryTitleText") as
                System.Windows.Controls.TextBlock;
            var trafficSummaryIcon = window.FindName("TrafficSummaryIcon") as
                System.Windows.FrameworkElement;
            var trafficSummaryCard = window.FindName("TrafficSummaryCard") as
                System.Windows.Controls.Border;
            var routerIpInfoGrid = window.FindName("RouterIpInfoGrid") as
                System.Windows.Controls.Grid;
            var routerIpInfoCard = window.FindName("RouterIpInfoCard") as
                System.Windows.Controls.Border;
            var ipv4InfoCard = window.FindName("Ipv4InfoCard") as
                System.Windows.Controls.Border;
            var ipv6InfoCard = window.FindName("Ipv6InfoCard") as
                System.Windows.Controls.Border;

            uiChecksPassed =
                summaryPanel is not null &&
                System.Windows.Controls.Grid.GetColumn(summaryPanel) == 1 &&
                summaryPanel.RowDefinitions.Count == 1 &&
                metadataRow is not null && metadataRow.Children.OfType<System.Windows.Controls.StackPanel>().Count() == 4 &&
                connectionText is not null && ipText is not null && macText is not null &&
                ipLabelText?.Text == "IP 地址" && macLabelText?.Text == "MAC 地址" &&
                window.FindName("DeviceNetworkInfoCard") is System.Windows.Controls.Border &&
                !IsLogicalDescendant(metadataRow, summaryPanel) &&
                downloadRateContent?.HorizontalAlignment ==
                    System.Windows.HorizontalAlignment.Center &&
                downloadRateContent.VerticalAlignment ==
                    System.Windows.VerticalAlignment.Center &&
                uploadRateContent?.HorizontalAlignment ==
                    System.Windows.HorizontalAlignment.Center &&
                uploadRateContent.VerticalAlignment ==
                    System.Windows.VerticalAlignment.Center &&
                downloadTotalContent?.HorizontalAlignment ==
                    System.Windows.HorizontalAlignment.Center &&
                downloadTotalContent.VerticalAlignment ==
                    System.Windows.VerticalAlignment.Center &&
                uploadTotalContent?.HorizontalAlignment ==
                    System.Windows.HorizontalAlignment.Center &&
                uploadTotalContent.VerticalAlignment ==
                    System.Windows.VerticalAlignment.Center &&
                IsTextCenteredByTransform(downloadRateContent, -11.5) &&
                IsTextCenteredByTransform(uploadRateContent, -11.5) &&
                IsTextCenteredByTransform(downloadTotalContent, -13.5) &&
                IsTextCenteredByTransform(uploadTotalContent, -13.5) &&
                UsesFixedTitleReference(downloadRateContent) &&
                UsesFixedTitleReference(uploadRateContent) &&
                UsesFixedTitleReference(downloadTotalContent) &&
                UsesFixedTitleReference(uploadTotalContent) &&
                downloadTotalContent?.IsHitTestVisible == false &&
                uploadTotalContent?.IsHitTestVisible == false &&
                UnitTextHasRequestedSpacing(downloadSpeedUnitText) &&
                UnitTextHasRequestedSpacing(uploadSpeedUnitText) &&
                UnitTextHasRequestedSpacing(downloadTotalUnitText) &&
                UnitTextHasRequestedSpacing(uploadTotalUnitText) &&
                IsMetricCardToolTip(downloadTotalToolTip,
                    downloadTotalTitleText,
                    "TotalDownloadDateRangeText") &&
                IsMetricCardToolTip(uploadTotalToolTip,
                    uploadTotalTitleText,
                    "TotalUploadDateRangeText") &&
                window.FindName("TestWindowsNotificationButton") is null &&
                window.FindName("NotificationStatusText") is null &&
                shortcutEnabled is null && shortcutText is null && shortcutButton is null && shortcutStatus is null &&
                  trafficSummaryHeader is not null &&
                  trafficSummaryTitle is not null &&
                  trafficSummaryTitle.Margin.Left == 0 &&
                  trafficSummaryTitle.FontSize == 18 &&
                  trafficSummaryTitle.FontWeight == System.Windows.FontWeights.Normal &&
                  sessionHistoryTitle is not null &&
                  trafficSummaryTitle.FontSize == sessionHistoryTitle.FontSize &&
                  trafficSummaryTitle.FontWeight == sessionHistoryTitle.FontWeight &&
                  trafficSummaryTitle.FontFamily?.Equals(sessionHistoryTitle.FontFamily) == true &&
                  ReferenceEquals(
                      System.Windows.LogicalTreeHelper.GetParent(trafficSummaryTitle),
                      trafficSummaryHeader) &&
                  trafficSummaryIcon is null &&
                  trafficSummaryCard is not null &&
                  !IsLogicalDescendant(trafficSummaryHeader, trafficSummaryCard) &&
                  window.TrafficRangeSegments.Items.Count == 3 &&
                  window.TrafficRangeSegments.SelectedIndex == 2 &&
                  RouterInfoCardsUseAdaptiveHorizontalLayout(
                      window.WindowRoot,
                      routerIpInfoCard,
                      routerIpInfoGrid,
                      ipv4InfoCard,
                      ipv6InfoCard,
                      (System.Windows.Style)window.FindResource("CardStyle"),
                       (System.Windows.Media.Brush)window.FindResource("ListItemBrush"),
                       (System.Windows.Media.Brush)window.FindResource("BorderBrush"));
        }
        catch (Exception exception)
        {
            uiFailure = exception;
        }
        finally
        {
            try
            {
                window?.PrepareForExit();
                window?.Close();
                application?.Shutdown();
            }
            catch (Exception exception)
            {
                uiFailure ??= exception;
            }
        }
    });
    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    thread.Join(TimeSpan.FromSeconds(20));
    CheckRegression(
        !thread.IsAlive && uiFailure is null && uiChecksPassed,
        uiFailure is null
            ? "详情头部应删除当前在线文字，网络信息外层卡片内固定同排；快捷键勾选框应位于设置项最左侧；流量图标题和区间按钮应提供清晰的无障碍视觉提示；路由器 IPv4/IPv6 信息应在外层卡片内自适应横向排列且标签、数值分别右左对齐"
            : $"设置与详情界面回归出现异常：{uiFailure.Message}");

    if (failures.Count == 0)
    {
        Console.WriteLine("设置、快捷键与详情头部回归通过。");
        return 0;
    }

    foreach (var failure in failures)
    {
        Console.Error.WriteLine($"设置、快捷键与详情头部回归失败：{failure}");
    }

    return 1;

    void CheckRegression(bool condition, string message)
    {
        if (!condition)
        {
            failures.Add(message);
        }
    }

    static bool IsLogicalDescendant(
        System.Windows.DependencyObject? element,
        System.Windows.DependencyObject? ancestor)
    {
        var current = element is null
            ? null
            : System.Windows.LogicalTreeHelper.GetParent(element);
        while (current is not null)
        {
            if (ReferenceEquals(current, ancestor))
            {
                return true;
            }

            current = System.Windows.LogicalTreeHelper.GetParent(current);
        }

        return false;
    }

    static bool IsTextCenteredByTransform(
        System.Windows.Controls.StackPanel? content,
        double expectedOffset)
    {
        return content?.RenderTransform is System.Windows.Media.TranslateTransform transform &&
            Math.Abs(transform.X - expectedOffset) < 0.001;
    }

    static bool UnitTextHasRequestedSpacing(
        System.Windows.Controls.TextBlock? unitText)
    {
        return unitText?.FontSize == 12 &&
            Math.Abs(unitText.Margin.Left - 5) < 0.001 &&
            unitText.TextWrapping == System.Windows.TextWrapping.NoWrap;
    }

    static bool IsMetricCardToolTip(
        System.Windows.Controls.ToolTip? toolTip,
        System.Windows.Controls.TextBlock? titleText,
        string propertyName)
    {
        var widthBinding = toolTip is null
            ? null
            : System.Windows.Data.BindingOperations.GetBinding(
                    toolTip,
                    System.Windows.FrameworkElement.WidthProperty);
        var contentBinding = toolTip is null
            ? null
            : System.Windows.Data.BindingOperations.GetBinding(
                    toolTip,
                    System.Windows.Controls.ContentControl.ContentProperty);
        return toolTip is not null &&
            titleText is not null &&
            toolTip.Style is not null &&
            toolTip.Placement == System.Windows.Controls.Primitives.PlacementMode.Bottom &&
            toolTip.HorizontalContentAlignment ==
                System.Windows.HorizontalAlignment.Center &&
            toolTip.Foreground?.Equals(titleText.Foreground) == true &&
            toolTip.FontSize == titleText.FontSize + 1 &&
            toolTip.FontWeight == titleText.FontWeight &&
            toolTip.FontFamily?.Equals(titleText.FontFamily) == true &&
            widthBinding?.Path?.Path == "PlacementTarget.ActualWidth" &&
            contentBinding?.Path?.Path ==
                $"PlacementTarget.DataContext.SelectedDevice.{propertyName}" &&
            toolTip.ContentTemplate is not null;
    }

    static bool RouterInfoCardsUseAdaptiveHorizontalLayout(
        System.Windows.Controls.Grid? windowRoot,
        System.Windows.Controls.Border? outerCard,
        System.Windows.Controls.Grid? infoGrid,
        System.Windows.Controls.Border? ipv4Card,
        System.Windows.Controls.Border? ipv6Card,
        System.Windows.Style expectedOuterCardStyle,
        System.Windows.Media.Brush expectedInnerCardBackground,
        System.Windows.Media.Brush expectedInnerCardBorderBrush)
    {
        return windowRoot is not null &&
            windowRoot.UseLayoutRounding &&
            windowRoot.SnapsToDevicePixels &&
            outerCard is not null &&
            outerCard.Style == expectedOuterCardStyle &&
            outerCard.Child == infoGrid &&
            outerCard.CornerRadius == new System.Windows.CornerRadius(24) &&
            outerCard.Padding == new System.Windows.Thickness(18) &&
            Math.Abs(outerCard.Margin.Top - 12d) < 0.001d &&
            infoGrid is not null &&
            infoGrid.UseLayoutRounding &&
            infoGrid.SnapsToDevicePixels &&
            infoGrid.ColumnDefinitions.Count == 2 &&
            infoGrid.ColumnDefinitions.All(column =>
                column.Width.GridUnitType == System.Windows.GridUnitType.Auto) &&
            infoGrid.RowDefinitions.Count == 0 &&
            ipv4Card is not null &&
            ipv6Card is not null &&
            System.Windows.Controls.Grid.GetRow(ipv4Card) == 0 &&
            System.Windows.Controls.Grid.GetRow(ipv6Card) == 0 &&
            System.Windows.Controls.Grid.GetColumn(ipv4Card) == 0 &&
            System.Windows.Controls.Grid.GetColumn(ipv6Card) == 1 &&
            System.Windows.Controls.Grid.GetColumnSpan(ipv4Card) == 1 &&
            System.Windows.Controls.Grid.GetColumnSpan(ipv6Card) == 1 &&
            ipv4Card.HorizontalAlignment ==
                System.Windows.HorizontalAlignment.Left &&
            ipv6Card.HorizontalAlignment ==
                System.Windows.HorizontalAlignment.Left &&
            ipv4Card.Style is null &&
            ipv6Card.Style is null &&
            ipv4Card.Background?.Equals(expectedInnerCardBackground) == true &&
            ipv6Card.Background?.Equals(expectedInnerCardBackground) == true &&
            ipv4Card.BorderBrush?.Equals(expectedInnerCardBorderBrush) == true &&
            ipv6Card.BorderBrush?.Equals(expectedInnerCardBorderBrush) == true &&
            ipv4Card.BorderThickness == new System.Windows.Thickness(1) &&
            ipv6Card.BorderThickness == new System.Windows.Thickness(1) &&
            ipv4Card.CornerRadius == new System.Windows.CornerRadius(20) &&
            ipv6Card.CornerRadius == new System.Windows.CornerRadius(20) &&
            ipv4Card.Padding == new System.Windows.Thickness(13) &&
            ipv6Card.Padding == new System.Windows.Thickness(13) &&
            Math.Abs(ipv4Card.Margin.Right - 12d) < 0.001d &&
            Math.Abs(ipv4Card.Margin.Bottom) < 0.001d &&
            ipv6Card.Margin == new System.Windows.Thickness(0) &&
            System.Windows.Data.BindingOperations.GetBinding(
                    ipv6Card,
                    System.Windows.FrameworkElement.MinWidthProperty)
                is { ElementName: "Ipv4InfoCard", Path.Path: "ActualWidth" } &&
            RouterInfoCardUsesScreenshotAlignment(
                ipv4Card,
                "IPv4 信息",
                ["连接类型：", "IP 地址：", "子网掩码：", "默认网关：", "DNS："]) &&
            RouterInfoCardUsesScreenshotAlignment(
                ipv6Card,
                "IPv6 信息",
                [
                    "连接类型：",
                    "WAN IPv6 地址：",
                    "WAN IPv6 网关：",
                    "LAN IPv6 地址：",
                    "LAN IPv6 前缀：",
                    "DNS："
                ]);
    }

    static bool RouterInfoCardUsesScreenshotAlignment(
        System.Windows.Controls.Border card,
        string heading,
        IReadOnlyList<string> labels)
    {
        if (card.Child is not System.Windows.Controls.StackPanel stackPanel ||
            stackPanel.Children.OfType<System.Windows.Controls.TextBlock>()
                .FirstOrDefault()?.Text != heading)
        {
            return false;
        }

        var detailsGrids = stackPanel.Children
            .OfType<System.Windows.Controls.Grid>()
            .ToList();
        if (detailsGrids.Count != 1 ||
            detailsGrids[0] is not System.Windows.Controls.Grid detailsGrid ||
            detailsGrid.ColumnDefinitions.Count != 2 ||
            detailsGrid.ColumnDefinitions[0].Width.GridUnitType !=
                System.Windows.GridUnitType.Auto ||
            detailsGrid.ColumnDefinitions[1].Width.GridUnitType !=
                System.Windows.GridUnitType.Star ||
            detailsGrid.RowDefinitions.Count != labels.Count ||
            detailsGrid.Children.Count != labels.Count * 2)
        {
            return false;
        }

        for (var index = 0; index < labels.Count; index++)
        {
            var rowChildren = detailsGrid.Children
                .OfType<System.Windows.Controls.TextBlock>()
                .Where(child =>
                    System.Windows.Controls.Grid.GetRow(child) == index)
                .ToList();
            var label = rowChildren.FirstOrDefault(child =>
                System.Windows.Controls.Grid.GetColumn(child) == 0);
            var value = rowChildren.FirstOrDefault(child =>
                System.Windows.Controls.Grid.GetColumn(child) == 1);
            if (rowChildren.Count != 2 ||
                label is null ||
                value is null ||
                label.Text != labels[index] ||
                label.HorizontalAlignment != System.Windows.HorizontalAlignment.Stretch ||
                label.TextAlignment != System.Windows.TextAlignment.Right ||
                Math.Abs(label.Margin.Right - 18d) > 0.001d ||
                value.HorizontalAlignment != System.Windows.HorizontalAlignment.Left ||
                value.TextAlignment != System.Windows.TextAlignment.Left ||
                value.TextWrapping != System.Windows.TextWrapping.NoWrap ||
                value.TextTrimming != System.Windows.TextTrimming.CharacterEllipsis)
            {
                return false;
            }
        }

        return true;
    }

    static bool UsesFixedTitleReference(
        System.Windows.Controls.StackPanel? content)
    {
        if (content?.Children.Count != 2 ||
            content.Children[1] is not System.Windows.Controls.Grid textLayout ||
            textLayout.RowDefinitions.Count != 2 ||
            textLayout.Children.Count != 2 ||
            textLayout.Children[0] is not System.Windows.Controls.TextBlock titleText ||
            textLayout.Children[1] is not System.Windows.Controls.Canvas valueCanvas)
        {
            return false;
        }

        return titleText.HorizontalAlignment == System.Windows.HorizontalAlignment.Center &&
            valueCanvas.HorizontalAlignment == System.Windows.HorizontalAlignment.Stretch &&
            !valueCanvas.ClipToBounds;
    }
}

int RunStartupSmoke()
{
    Exception? startupException = null;
    var windowShown = false;
    var firstFrameMaterialReady = false;
    var thread = new Thread(() =>
    {
        LanDeviceMonitor.App? application = null;
        try
        {
            application = new LanDeviceMonitor.App();
            application.DispatcherUnhandledException += (_, eventArgs) =>
            {
                startupException = eventArgs.Exception;
                eventArgs.Handled = true;
                application.Shutdown(2);
            };
            application.InitializeComponent();
            var activationTimer = new System.Windows.Threading.DispatcherTimer(
                TimeSpan.FromSeconds(1.5),
                System.Windows.Threading.DispatcherPriority.ApplicationIdle,
                async (sender, _) =>
                {
                    ((System.Windows.Threading.DispatcherTimer)sender!).Stop();
                    await SingleInstanceService.SignalPrimaryAsync();
                },
                application.Dispatcher);
            var timer = new System.Windows.Threading.DispatcherTimer(
                TimeSpan.FromSeconds(4),
                System.Windows.Threading.DispatcherPriority.ApplicationIdle,
                (_, _) =>
                {
                    windowShown = application.Windows
                        .OfType<MainWindow>()
                        .Any(window => window.IsVisible);
                    firstFrameMaterialReady =
                        MicaWindowService.LastAcrylicApplySucceeded &&
                        MicaWindowService.LastSystemBackdropApplySucceeded &&
                        MicaWindowService.LastFrameRefreshSucceeded;
                    application.Shutdown();
                },
                application.Dispatcher);
            activationTimer.Start();
            timer.Start();
            application.Run();
        }
        catch (Exception exception)
        {
            startupException = exception;
            application?.Shutdown(2);
        }
    });
    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    thread.Join(TimeSpan.FromSeconds(55));

    if (thread.IsAlive)
    {
        Console.Error.WriteLine("启动冒烟测试超时，应用未能在预期时间内退出。");
        return 1;
    }

    if (startupException is not null)
    {
        Console.Error.WriteLine($"启动冒烟测试异常：{startupException}");
        return 1;
    }

    if (!windowShown)
    {
        Console.Error.WriteLine("启动冒烟测试未检测到可见主窗口。");
        return 1;
    }

    if (!firstFrameMaterialReady)
    {
        Console.Error.WriteLine(
            "启动冒烟测试未检测到首次显示时的 Acrylic 与窗口框架刷新。");
        return 1;
    }

    Console.WriteLine(
        "启动冒烟测试通过：主窗口首次显示即完成 Acrylic 与窗口框架刷新。");
    return 0;
}

async Task<int> RunTrafficDiagnosticAsync()
{
    var credentialService = new RouterCredentialService();
    var password = credentialService.ReadPassword("10.77.0.1");
    if (string.IsNullOrWhiteSpace(password))
    {
        Console.Error.WriteLine("流量诊断无法开始：Windows 凭据管理器中没有路由器密码。");
        return 1;
    }

    using var client = new XiaomiRouterClient();
    client.Configure("10.77.0.1", password);
    var counters = new Dictionary<string, long[]>(StringComparer.OrdinalIgnoreCase);
    var profiles = new Dictionary<string, DeviceProfile>(StringComparer.OrdinalIgnoreCase);
    var diagnosticStatePath = Path.Combine(
        Path.GetTempPath(),
        "LanDeviceMonitor.Tests",
        $"traffic-diagnostic-{Guid.NewGuid():N}",
        "state.json");
    var diagnosticRepository = new DeviceRepository(diagnosticStatePath);
client.TrafficSampleObserved += (_, sample) =>
{
        if (!counters.TryGetValue(sample.MacAddress, out var values))
        {
            values = new long[8];
            counters[sample.MacAddress] = values;
        }

        values[0]++;
        if (sample.DeviceListUploadSpeed > 0 && sample.StatusUploadSpeed == 0)
        {
            values[1]++;
        }

        if (sample.DeviceListDownloadSpeed > 0 && sample.StatusDownloadSpeed == 0)
        {
            values[2]++;
        }

        if (sample.UploadSpeed == 0)
        {
            values[3]++;
        }

        if (sample.DownloadSpeed == 0)
        {
            values[4]++;
        }

        values[5] = Math.Max(values[5], sample.UploadSpeed);
        values[6] = Math.Max(values[6], sample.DownloadSpeed);

        if (sample.MacAddress.Equals(
                "02:00:00:00:00:01",
                StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine(
                $"{sample.Timestamp:HH:mm:ss} 测试设备3原始值：设备列表 ↑{FormatOptionalRate(sample.DeviceListUploadSpeed)} ↓{FormatOptionalRate(sample.DeviceListDownloadSpeed)}，"
                + $"累计 ↑{FormatOptionalBytes(sample.DeviceListTotalUpload)} ↓{FormatOptionalBytes(sample.DeviceListTotalDownload)}，"
                + $"status ↑{sample.StatusUploadSpeed?.ToString("N0") ?? "缺失"} ↓{sample.StatusDownloadSpeed?.ToString("N0") ?? "缺失"}，"
                + $"累计 ↑{FormatOptionalBytes(sample.StatusTotalUpload)} ↓{FormatOptionalBytes(sample.StatusTotalDownload)}，"
                + $"合并 ↑{sample.UploadSpeed:N0} ↓{sample.DownloadSpeed:N0}，"
                + $"累计 ↑{sample.TotalUpload:N0} ↓{sample.TotalDownload:N0}");
        }
    };

    await client.AuthenticateAsync();
    Console.WriteLine("开始流量原始采样诊断（20 秒）…");
    var startedAt = DateTimeOffset.UtcNow;
    var sampleRounds = 0;
    while (DateTimeOffset.UtcNow - startedAt < TimeSpan.FromSeconds(20))
    {
        var snapshots = await client.GetDeviceSnapshotsAsync();
        var update = await diagnosticRepository.ApplyPollAsync(snapshots, DateTimeOffset.Now);
        foreach (var device in update.Devices)
        {
            profiles[device.Profile.MacAddress] = device.Profile;
        }
        sampleRounds++;
        profiles.TryGetValue("02:00:00:00:00:01", out var targetProfile);
        Console.WriteLine(
            $"第 {sampleRounds,2} 次：{snapshots.Count} 台设备，"
            + $"总上传 {snapshots.Sum(item => item.UploadSpeedBytes):N0} B/s，"
            + $"总下载 {snapshots.Sum(item => item.DownloadSpeedBytes):N0} B/s，"
            + $"测试设备3最终 ↑{targetProfile?.LastUploadSpeedBytes:N0} ↓{targetProfile?.LastDownloadSpeedBytes:N0}");
        await Task.Delay(TimeSpan.FromSeconds(1));
    }

    Console.WriteLine("--- 原始接口合并统计（总采样 / 主接口正值但 status=0 / 合并后上传为 0 / 合并后下载为 0）---");
    foreach (var entry in counters.OrderByDescending(item => item.Value[5]))
    {
        var values = entry.Value;
        Console.WriteLine(
            $"{entry.Key}: {values[0]} / {values[1]} / {values[3]} / {values[4]}，"
            + $"峰值 {values[5]:N0} B/s ↑、{values[6]:N0} B/s ↓");
    }

    var diagnosticDirectory = Path.GetDirectoryName(diagnosticStatePath);
    if (!string.IsNullOrWhiteSpace(diagnosticDirectory) &&
        Directory.Exists(diagnosticDirectory))
    {
        Directory.Delete(diagnosticDirectory, recursive: true);
    }

    return 0;
}

async Task<int> RunRealtimeRateRegressionAsync()
{
    var directory = Path.Combine(
        Path.GetTempPath(),
        "LanDeviceMonitor.Tests",
        $"realtime-rate-{Guid.NewGuid():N}");
    var repository = new DeviceRepository(Path.Combine(directory, "state.json"));
    var macAddress = "00:00:00:00:20:03";
    var now = new DateTimeOffset(2026, 8, 24, 21, 0, 0, TimeSpan.FromHours(8));

    DeviceSnapshot Snapshot(long instantaneousSpeed, long totalBytes) => new(
        macAddress,
        "实时速率回归设备",
        "realtime-rate-regression",
        true,
        "10.77.0.243",
        2,
        30,
        instantaneousSpeed,
        0,
        totalBytes,
        0);

    try
    {
        var first = await repository.ApplyPollAsync([Snapshot(100, 1_000)], now);
        var second = await repository.ApplyPollAsync(
            [Snapshot(250, 1_500)],
            now.AddSeconds(1));
        var third = await repository.ApplyPollAsync(
            [Snapshot(350, 2_000)],
            now.AddSeconds(2));
        var zeroWithoutNewTotal = await repository.ApplyPollAsync(
            [Snapshot(0, 2_000)],
            now.AddSeconds(3));
        var heldRepository = new DeviceRepository(
            Path.Combine(directory, "held-state.json"));
        await heldRepository.ApplyPollAsync([Snapshot(0, 1_000)], now);
        var heldSecond = await heldRepository.ApplyPollAsync(
            [Snapshot(0, 1_500)],
            now.AddSeconds(5));
        var heldThird = await heldRepository.ApplyPollAsync(
            [Snapshot(0, 1_500)],
            now.AddSeconds(6));
        var cumulativeOnlyRepository = new DeviceRepository(
            Path.Combine(directory, "cumulative-only-state.json"));
        DeviceSnapshot CumulativeOnlySnapshot(long totalBytes) =>
            Snapshot(0, totalBytes) with
            {
                HasValidTrafficData = true,
                HasValidSpeedData = false,
                HasValidTotalsData = true,
                HasValidUploadSpeedData = false,
                HasValidDownloadSpeedData = false,
                HasValidTotalUploadData = true,
                HasValidTotalDownloadData = false
            };
        await cumulativeOnlyRepository.ApplyPollAsync(
            [CumulativeOnlySnapshot(1_000)],
            now);
        var cumulativeOnlySecond = await cumulativeOnlyRepository.ApplyPollAsync(
            [CumulativeOnlySnapshot(1_500)],
            now.AddSeconds(5));
        var cumulativeOnlyThird = await cumulativeOnlyRepository.ApplyPollAsync(
            [CumulativeOnlySnapshot(1_500)],
            now.AddSeconds(6));
        var firstProfile = first.Devices.Single().Profile;
        var secondProfile = second.Devices.Single().Profile;
        var thirdProfile = third.Devices.Single().Profile;
        var zeroProfile = zeroWithoutNewTotal.Devices.Single().Profile;
        var heldSecondProfile = heldSecond.Devices.Single().Profile;
        var heldThirdProfile = heldThird.Devices.Single().Profile;
        var cumulativeOnlySecondProfile = cumulativeOnlySecond.Devices.Single().Profile;
        var cumulativeOnlyThirdProfile = cumulativeOnlyThird.Devices.Single().Profile;
        var sparseHistory = new TrafficHistoryBuffer();
        sparseHistory.Append(new TrafficSample(now, 9, 11));
        sparseHistory.Append(new TrafficSample(now.AddSeconds(2), 13, 17));
        var sparseSnapshot = sparseHistory.Snapshot(now.AddSeconds(2));
        var passed =
            firstProfile.LastUploadSpeedBytes == 100 &&
            secondProfile.LastUploadSpeedBytes == 250 &&
            thirdProfile.LastUploadSpeedBytes == 350 &&
            secondProfile.LastUploadGraphSpeedBytes == 250 &&
            thirdProfile.LastUploadGraphSpeedBytes == 350 &&
            zeroProfile.LastUploadSpeedBytes == 0 &&
            zeroProfile.HasFreshUploadGraphSample &&
            heldSecondProfile.LastUploadSpeedBytes == 0 &&
            heldThirdProfile.LastUploadSpeedBytes == 0 &&
            heldThirdProfile.LastUploadGraphSpeedBytes == 0 &&
            heldThirdProfile.HasFreshUploadGraphSample &&
            cumulativeOnlySecondProfile.LastUploadSpeedBytes == 100 &&
            cumulativeOnlySecondProfile.LastUploadGraphSpeedBytes == 100 &&
            cumulativeOnlySecondProfile.HasFreshUploadGraphSample &&
            cumulativeOnlyThirdProfile.LastUploadSpeedBytes == 0 &&
            cumulativeOnlyThirdProfile.LastUploadGraphSpeedBytes == 0 &&
            !cumulativeOnlyThirdProfile.HasFreshUploadGraphSample &&
            sparseSnapshot.Count == 2 &&
            sparseSnapshot[1].Timestamp == now.AddSeconds(2) &&
            sparseSnapshot[1].UploadBytesPerSecond == 13 &&
            sparseSnapshot[1].DownloadBytesPerSecond == 17;
        Console.WriteLine(
            passed
                ? "真实速率回归通过：累计变化可换算区间平均值，后续轮询保持有效值且不插值未知秒。"
                : $"实时速率回归失败：卡片={firstProfile.LastUploadSpeedBytes}/{secondProfile.LastUploadSpeedBytes}/{thirdProfile.LastUploadSpeedBytes}，"
                  + $"图表={secondProfile.LastUploadGraphSpeedBytes}/{thirdProfile.LastUploadGraphSpeedBytes}，"
                  + $"零值={zeroProfile.LastUploadSpeedBytes}，保持={heldSecondProfile.LastUploadSpeedBytes}/{heldThirdProfile.LastUploadSpeedBytes}，"
                  + $"累计仅变化={cumulativeOnlySecondProfile.LastUploadSpeedBytes}/{cumulativeOnlyThirdProfile.LastUploadSpeedBytes}，"
                  + $"稀疏点数={sparseSnapshot.Count}");
        return passed ? 0 : 1;
    }
    finally
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}

async Task<int> RunOfflineConfirmationRegressionAsync()
{
    var directory = Path.Combine(
        Path.GetTempPath(),
        "LanDeviceMonitor.Tests",
        $"offline-confirmation-{Guid.NewGuid():N}");
    var repository = new DeviceRepository(Path.Combine(directory, "state.json"));
    var now = new DateTimeOffset(2026, 8, 28, 23, 10, 0, TimeSpan.FromHours(8));

    DeviceSnapshot Snapshot(bool isOnline) => new(
        "00:00:00:00:20:04",
        "离线确认回归设备",
        "offline-confirmation-regression",
        isOnline,
        "10.77.0.244",
        2,
        30,
        0,
        0,
        1_000,
        2_000);

    try
    {
        await repository.ApplyPollAsync([Snapshot(true)], now);
        var firstOfflineEvidence = await repository.ApplyPollAsync(
            [Snapshot(false)],
            now.AddSeconds(1));
        var beforeBoundary = await repository.ApplyPollAsync(
            [Snapshot(false)],
            now.AddSeconds(3));
        var atBoundary = await repository.ApplyPollAsync(
            [Snapshot(false)],
            now.AddSeconds(4));
        var onlineAgain = await repository.ApplyPollAsync(
            [Snapshot(true)],
            now.AddSeconds(5));

        var passed =
            firstOfflineEvidence.Devices.Single().Profile.LastKnownOnline &&
            firstOfflineEvidence.Transitions.Count == 0 &&
            beforeBoundary.Devices.Single().Profile.LastKnownOnline &&
            beforeBoundary.Transitions.Count == 0 &&
            !atBoundary.Devices.Single().Profile.LastKnownOnline &&
            atBoundary.Transitions is [{ IsOnline: false }] &&
            onlineAgain.Devices.Single().Profile.LastKnownOnline &&
            onlineAgain.Transitions is [{ IsOnline: true }];
        if (!passed)
        {
            Console.Error.WriteLine(
                "离线确认回归失败：2 秒内应继续防抖，持续满 3 秒应离线，再次上线应立即恢复。");
            return 1;
        }

        Console.WriteLine(
            "离线确认回归通过：2 秒内继续防抖，持续满 3 秒产生一次离线转换，再次上线立即恢复。");
        return 0;
    }
    finally
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}

int RunPollCadenceRegression()
{
    var passed =
        MonitoringLoop.GetDelayAfterIteration(
            TimeSpan.FromSeconds(1),
            TimeSpan.FromMilliseconds(250)) == TimeSpan.FromMilliseconds(750) &&
        MonitoringLoop.GetDelayAfterIteration(
            TimeSpan.FromSeconds(1),
            TimeSpan.FromMilliseconds(1500)) == TimeSpan.FromMilliseconds(100);
    Console.WriteLine(
        passed
            ? "轮询周期回归通过：慢请求不会再额外叠加一个完整刷新周期。"
            : $"轮询周期回归失败：慢请求延迟={MonitoringLoop.GetDelayAfterIteration(TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(1500))}");
    return passed ? 0 : 1;
}

int RunChartRegression()
{
    var now = new DateTimeOffset(2026, 8, 24, 22, 0, 0, TimeSpan.FromHours(8));
    var samples = new[]
    {
        new TrafficSample(now, 0, 0) { IsPlaceholder = true },
        new TrafficSample(now.AddSeconds(1), 10, 20),
        new TrafficSample(now.AddSeconds(2), 0, 0) { IsPlaceholder = true },
        new TrafficSample(now.AddSeconds(3), 30, 40)
    };
    var method = typeof(TrafficChart).GetMethod(
        "GetRenderableSampleSegments",
        BindingFlags.NonPublic | BindingFlags.Static);
    var segments = method?.Invoke(null, [samples, true]) as IReadOnlyList<IReadOnlyList<int>>;
    var passed = segments is not null &&
        segments.Count == 1 &&
        segments[0].SequenceEqual([1, 3]);
    Console.WriteLine(
        passed
            ? "图表连续线回归通过：真实样本之间保持连续，未知槽位不生成伪造点。"
            : "图表连续线回归失败：真实样本之间被错误断开或生成了伪造点。 ");
    return passed ? 0 : 1;
}

int RunFilterAnimationRegression()
{
    var method = typeof(MainWindow).GetMethod(
        "GetDeviceFilterTransitionDurationMilliseconds",
        BindingFlags.NonPublic | BindingFlags.Static);
    var duration = method?.Invoke(null, null) as int?;
    var passed = duration == 480;
    Console.WriteLine(
        passed
            ? $"列表切换动画回归通过：统一时长={duration}ms，并由切换期间的重建抑制保证流畅。"
            : $"列表切换动画回归失败：时长={duration?.ToString() ?? "缺失"}ms，不能改变原动画时长。 ");
    return passed ? 0 : 1;
}

int RunSynchronizedFilterAnimationRegression()
{
    var method = typeof(MainWindow).GetMethod(
        "GetSynchronizedDeviceFilterAnimationTiming",
        BindingFlags.NonPublic | BindingFlags.Static);
    var value = method?.Invoke(null, null);
    var passed = value is ValueTuple<int, int, int> timing &&
        timing.Item1 == 480 &&
        timing.Item2 == 150 &&
        timing.Item3 == 450;
    Console.WriteLine(
        passed
            ? "同步动画回归通过：列表与详情同时触发；详情恢复两段式退出150ms、进入450ms，总计600ms。"
            : "同步动画回归失败：原有列表与详情动画时序未保留。 ");
    return passed ? 0 : 1;
}

int RunRapidFilterReversalRegression()
{
    var passed = false;
    var diagnostics = string.Empty;
    Exception? failure = null;
    var thread = new Thread(() =>
    {
        LanDeviceMonitor.App? application = null;
        MainWindow? window = null;
        try
        {
            application = TestAppFactory.Create();
            application.InitializeComponent();
            ThemeService.Apply(ThemeService.LightMode);
            window = new MainWindow(new NotificationService())
            {
                Left = -10000,
                Top = -10000,
                Width = 900,
                Height = 560,
                ShowActivated = false,
                ShowInTaskbar = false
            };
            var replaceDevicesMethod = typeof(MainWindow).GetMethod(
                "ReplaceDevices",
                BindingFlags.Instance | BindingFlags.NonPublic)!;
            replaceDevicesMethod.Invoke(
                window,
                [CreateFilterDevices(), false, false]);
            window.Show();
            window.UpdateLayout();

            if (!System.Windows.SystemParameters.ClientAreaAnimation)
            {
                passed = true;
                diagnostics = "系统动画已关闭，跳过过渡中断检查";
            }
            else
            {
                var clickArgs = new System.Windows.RoutedEventArgs(
                    System.Windows.Controls.Button.ClickEvent);
                window.DeviceFilterSegments.SelectedIndex = 1;
                window.DeviceFilterSegments.SelectedIndex = 1;
                window.DeviceFilterSegments.SelectedIndex = 0;

                var firstOutgoing = window.DeviceList.Items
                    .OfType<DeviceItemViewModel>()
                    .Select(device => device.MacAddress)
                    .ToArray();
                var firstIncoming = window.DeviceListTransition.Items
                    .OfType<DeviceItemViewModel>()
                    .Select(device => device.MacAddress)
                    .ToArray();
                var onlineMacs = new[]
                {
                    "00:00:00:00:30:02",
                    "00:00:00:00:30:01"
                };
                var offlineMacs = new[]
                {
                    "00:00:00:00:30:04",
                    "00:00:00:00:30:03"
                };
                var firstTransitionPassed =
                    firstOutgoing.SequenceEqual(offlineMacs) &&
                    firstIncoming.SequenceEqual(onlineMacs) &&
                    window.DeviceListTransition.Visibility ==
                        System.Windows.Visibility.Visible;

                window.DeviceFilterSegments.SelectedIndex = 0;
                window.DeviceFilterSegments.SelectedIndex = 0;
                window.DeviceFilterSegments.SelectedIndex = 1;
                var secondOutgoing = window.DeviceList.Items
                    .OfType<DeviceItemViewModel>()
                    .Select(device => device.MacAddress)
                    .ToArray();
                var secondIncoming = window.DeviceListTransition.Items
                    .OfType<DeviceItemViewModel>()
                    .Select(device => device.MacAddress)
                    .ToArray();
                var secondTransitionPassed =
                    secondOutgoing.SequenceEqual(onlineMacs) &&
                    secondIncoming.SequenceEqual(offlineMacs) &&
                    window.DeviceListTransition.Visibility ==
                        System.Windows.Visibility.Visible;
                passed = firstTransitionPassed && secondTransitionPassed;
                diagnostics =
                    $"第一次旧层=[{string.Join(", ", firstOutgoing)}]，" +
                    $"新层=[{string.Join(", ", firstIncoming)}]；" +
                    $"第二次旧层=[{string.Join(", ", secondOutgoing)}]，" +
                    $"新层=[{string.Join(", ", secondIncoming)}]，" +
                    $"新层可见={window.DeviceListTransition.Visibility}";
            }
        }
        catch (Exception exception)
        {
            failure = exception;
        }
        finally
        {
            try
            {
                window?.PrepareForExit();
                window?.Close();
                application?.Shutdown();
            }
            catch (Exception exception)
            {
                failure ??= exception;
            }
        }
    });
    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    thread.Join(TimeSpan.FromSeconds(20));

    if (thread.IsAlive)
    {
        Console.Error.WriteLine("快速反向列表切换回归超时。 ");
        return 1;
    }

    if (failure is not null)
    {
        Console.Error.WriteLine($"快速反向列表切换回归异常：{failure}");
        return 1;
    }

    Console.WriteLine(
        passed
            ? $"快速反向列表切换回归通过：{diagnostics}。"
            : $"快速反向列表切换回归失败：{diagnostics}。 ");
    return passed ? 0 : 1;
}

int RunMinimizeDuringFilterTransitionRegression()
{
    var passed = false;
    var diagnostics = string.Empty;
    Exception? failure = null;
    var thread = new Thread(() =>
    {
        LanDeviceMonitor.App? application = null;
        MainWindow? window = null;
        try
        {
            application = TestAppFactory.Create();
            application.InitializeComponent();
            ThemeService.Apply(ThemeService.LightMode);
            window = new MainWindow(new NotificationService())
            {
                WindowStartupLocation = System.Windows.WindowStartupLocation.Manual,
                Left = -10000,
                Top = -10000,
                Width = 900,
                Height = 560,
                ShowActivated = false,
                ShowInTaskbar = false
            };
            var replaceDevicesMethod = typeof(MainWindow).GetMethod(
                "ReplaceDevices",
                BindingFlags.Instance | BindingFlags.NonPublic)!;
            replaceDevicesMethod.Invoke(
                window,
                [CreateFilterDevices(), false, false]);
            window.Show();
            window.UpdateLayout();

            if (!System.Windows.SystemParameters.ClientAreaAnimation)
            {
                passed = true;
                diagnostics = "系统动画已关闭，跳过活动动画缓存检查";
            }
            else
            {
                var clickArgs = new System.Windows.RoutedEventArgs(
                    System.Windows.Controls.Button.ClickEvent);
                window.DeviceFilterSegments.SelectedIndex = 1;
                window.DeviceFilterSegments.SelectedIndex = 0;
                window.DeviceFilterSegments.SelectedIndex = 1;

                var cacheWasActive =
                    window.GetDeviceListMotion(window.DeviceList).CacheMode is not null &&
                    window.GetDeviceListMotion(window.DeviceListTransition).CacheMode is not null;
                window.RequestMinimizeWithTransitionMaterial();
                var transitionSnapshot =
                    window.FindName("WindowTransitionSnapshot") as
                        System.Windows.Controls.Image;
                var minimizeReachedTargetState = WaitForWindowState(
                    window,
                    System.Windows.WindowState.Minimized,
                    TimeSpan.FromSeconds(3));
                var pixelSnapshotWasPrepared =
                    transitionSnapshot?.Visibility ==
                        System.Windows.Visibility.Visible &&
                    transitionSnapshot.Source is not null;

                var transitionField = typeof(MainWindow).GetField(
                    "_isDeviceFilterTransitionActive",
                    BindingFlags.Instance | BindingFlags.NonPublic)!;
                var transitionActive = (bool)transitionField.GetValue(window)!;
                var stableFrameProperty = typeof(MainWindow).GetProperty(
                    "MinimizeStableMaterialFramesObserved",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                var stableFrameCount =
                    stableFrameProperty?.GetValue(window) as int?;
                var snapshotFrameProperty = typeof(MainWindow).GetProperty(
                    "MinimizeSnapshotFramesObserved",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                var snapshotFrameCount =
                    snapshotFrameProperty?.GetValue(window) as int?;
                var capturedAfterStableFrameProperty =
                    typeof(MainWindow).GetProperty(
                        "MinimizeSnapshotCapturedAfterStableFrame",
                        BindingFlags.Instance | BindingFlags.NonPublic);
                var snapshotCapturedAfterStableFrame =
                    capturedAfterStableFrameProperty?.GetValue(window) is true;
                var twoPhaseCompositionCompleted =
                    stableFrameCount is >= 1 &&
                    snapshotFrameCount is >= 1 &&
                    snapshotCapturedAfterStableFrame;
                var redirectionAlphaEnabledProperty =
                    typeof(MicaWindowService).GetProperty(
                        "LastRedirectionBitmapAlphaEnabled",
                        BindingFlags.Static | BindingFlags.NonPublic);
                var redirectionBitmapIsOpaque =
                    redirectionAlphaEnabledProperty?.GetValue(null) is false;
                var snapshotBacksFallbackSurface =
                    window.WindowRoot.Background is
                        System.Windows.Media.ImageBrush imageBrush &&
                    ReferenceEquals(
                        imageBrush.ImageSource,
                        transitionSnapshot?.Source);
                var minimizedWithStableSnapshot =
                    cacheWasActive &&
                    minimizeReachedTargetState &&
                    snapshotBacksFallbackSurface &&
                    pixelSnapshotWasPrepared &&
                    twoPhaseCompositionCompleted &&
                    redirectionBitmapIsOpaque &&
                    !transitionActive &&
                    window.GetDeviceListMotion(window.DeviceList).CacheMode is null &&
                    window.GetDeviceListMotion(window.DeviceListTransition).CacheMode is null &&
                    window.DetailPanel.CacheMode is null &&
                    window.DeviceListTransition.Visibility ==
                        System.Windows.Visibility.Collapsed &&
                    window.DeviceListTransition.ItemsSource is null;
                window.WindowState = System.Windows.WindowState.Normal;
                var snapshotClearedOnRestore =
                    transitionSnapshot?.Visibility ==
                        System.Windows.Visibility.Collapsed &&
                    transitionSnapshot.Source is null &&
                    ReferenceEquals(
                        window.WindowRoot.Background,
                        System.Windows.Media.Brushes.Transparent);
                passed =
                    minimizedWithStableSnapshot &&
                    snapshotClearedOnRestore;
                diagnostics =
                    $"切换前缓存活跃={cacheWasActive}，" +
                    $"像素快照已进入底层重定向表面={snapshotBacksFallbackSurface}，" +
                    $"常态像素快照已准备={pixelSnapshotWasPrepared}，" +
                    $"稳定常态帧数={stableFrameCount?.ToString() ?? "缺失"}，" +
                    $"快照提交帧数={snapshotFrameCount?.ToString() ?? "缺失"}，" +
                    $"稳定后才截取={snapshotCapturedAfterStableFrame}，" +
                    $"DWM 重定向位图不透明={redirectionBitmapIsOpaque}，" +
                    $"最小化后过渡活跃={transitionActive}，" +
                    $"列表缓存={window.GetDeviceListMotion(window.DeviceList).CacheMode?.GetType().Name ?? "无"}/" +
                    $"{window.GetDeviceListMotion(window.DeviceListTransition).CacheMode?.GetType().Name ?? "无"}，" +
                    $"详情缓存={window.DetailPanel.CacheMode?.GetType().Name ?? "无"}，" +
                    $"过渡层={window.DeviceListTransition.Visibility}，" +
                    $"还原后快照已清除={snapshotClearedOnRestore}";
            }
        }
        catch (Exception exception)
        {
            failure = exception;
        }
        finally
        {
            try
            {
                window?.PrepareForExit();
                window?.Close();
                application?.Shutdown();
            }
            catch (Exception exception)
            {
                failure ??= exception;
            }
        }
    });
    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    thread.Join(TimeSpan.FromSeconds(20));

    if (thread.IsAlive)
    {
        Console.Error.WriteLine("列表切换期间最小化回归超时。 ");
        return 1;
    }

    if (failure is not null)
    {
        Console.Error.WriteLine($"列表切换期间最小化回归异常：{failure}");
        return 1;
    }

    Console.WriteLine(
        passed
            ? $"列表切换期间最小化回归通过：{diagnostics}。"
            : $"列表切换期间最小化回归失败：{diagnostics}。 ");
    return passed ? 0 : 1;
}

int RunCloseToTrayTransitionRegression()
{
    var passed = false;
    var diagnostics = string.Empty;
    Exception? failure = null;
    var thread = new Thread(() =>
    {
        LanDeviceMonitor.App? application = null;
        MainWindow? window = null;
        try
        {
            var startupOperations = new List<System.Windows.Threading.DispatcherOperation>();
            System.Windows.Threading.DispatcherHookEventHandler posted = (_, e) => startupOperations.Add(e.Operation);
            System.Windows.Threading.Dispatcher.CurrentDispatcher.Hooks.OperationPosted += posted;
            application = TestAppFactory.Create();
            System.Windows.Threading.Dispatcher.CurrentDispatcher.Hooks.OperationPosted -= posted;
            foreach (var operation in startupOperations) operation.Abort();
            application.InitializeComponent();
            application.ShutdownMode =
                System.Windows.ShutdownMode.OnExplicitShutdown;
            ThemeService.Apply(ThemeService.LightMode);
            window = new MainWindow(new NotificationService())
            {
                Left = -10000,
                Top = -10000,
                Width = 900,
                Height = 560,
                ShowActivated = false,
                ShowInTaskbar = false
            };
            window.Show();
            window.UpdateLayout();
            var originalSurface = window.WindowRoot.Background;

            var closeToTrayRequested = false;
            var normalSurfaceRetainedWhenTrayWasRequested = false;
            var enteredMinimizedState = false;
            var nativeHideMethod = typeof(MainWindow).GetMethod(
                "HideToTrayPreservingVisual",
                BindingFlags.Instance | BindingFlags.NonPublic);
            var nativeTrayLifecycleAvailable = nativeHideMethod is not null;
            window.StateChanged += (_, _) =>
            {
                if (window.WindowState ==
                    System.Windows.WindowState.Minimized)
                {
                    enteredMinimizedState = true;
                }
            };
            window.MinimizeToTrayRequested += (_, _) =>
            {
                closeToTrayRequested = true;
                var transitionSnapshot =
                    window.FindName("WindowTransitionSnapshot") as
                        System.Windows.Controls.Image;
                normalSurfaceRetainedWhenTrayWasRequested =
                    transitionSnapshot?.Visibility ==
                        System.Windows.Visibility.Collapsed &&
                    transitionSnapshot.Source is null &&
                    ReferenceEquals(
                        window.WindowRoot.Background,
                        originalSurface);
                _ = window.Dispatcher.BeginInvoke(
                    new Action(() => nativeHideMethod?.Invoke(window, null)),
                    System.Windows.Threading.DispatcherPriority.Normal);
            };

            var closingArguments = new System.ComponentModel.CancelEventArgs();
            var closingHandler = typeof(MainWindow).GetMethod(
                "Window_Closing",
                BindingFlags.Instance | BindingFlags.NonPublic)!;
            closingHandler.Invoke(window, [null, closingArguments]);
            var closeRequestReachedAfterTransition = WaitForDispatcherCondition(
                window.Dispatcher,
                () => closeToTrayRequested,
                TimeSpan.FromSeconds(3));
            application.Dispatcher.Invoke(
                () => { },
                System.Windows.Threading.DispatcherPriority.ContextIdle);
            var transitionSnapshot =
                window.FindName("WindowTransitionSnapshot") as
                    System.Windows.Controls.Image;
            var windowHandle =
                new System.Windows.Interop.WindowInteropHelper(window).Handle;
            var nativeHiddenStateReached = WaitForDispatcherCondition(
                window.Dispatcher,
                () => window.IsVisible &&
                    !NativeWindowVisibilityProbe.IsWindowVisible(windowHandle),
                TimeSpan.FromSeconds(3));
            var normalSurfaceRetainedWhileNativelyHidden =
                nativeHiddenStateReached &&
                window.IsVisible &&
                window.WindowState ==
                    System.Windows.WindowState.Normal &&
                transitionSnapshot?.Visibility ==
                    System.Windows.Visibility.Collapsed &&
                transitionSnapshot.Source is null &&
                ReferenceEquals(
                    window.WindowRoot.Background,
                    originalSurface);

            window.RestoreFromTray();
            var nativeWindowRestored = WaitForDispatcherCondition(
                window.Dispatcher,
                () => NativeWindowVisibilityProbe.IsWindowVisible(
                    windowHandle),
                TimeSpan.FromSeconds(3));
            var fullUiRestored =
                window.IsVisible &&
                window.WindowRoot.IsVisible &&
                window.DeviceList.IsVisible;
            passed =
                closingArguments.Cancel &&
                closeRequestReachedAfterTransition &&
                nativeTrayLifecycleAvailable &&
                !enteredMinimizedState &&
                normalSurfaceRetainedWhenTrayWasRequested &&
                normalSurfaceRetainedWhileNativelyHidden &&
                nativeWindowRestored &&
                fullUiRestored &&
                window.WindowState == System.Windows.WindowState.Normal;
            diagnostics =
                $"关闭已取消={closingArguments.Cancel}，" +
                $"已请求托盘化={closeToTrayRequested}，" +
                $"原生托盘生命周期可用={nativeTrayLifecycleAvailable}，" +
                $"未进入最小化状态={!enteredMinimizedState}，" +
                $"托盘化请求时保持常态表面={normalSurfaceRetainedWhenTrayWasRequested}，" +
                $"原生隐藏期间保持常态表面={normalSurfaceRetainedWhileNativelyHidden}，" +
                $"原生窗口已恢复={nativeWindowRestored}，" +
                $"完整 UI 已恢复={fullUiRestored}，" +
                $"窗口状态={window.WindowState}";
        }
        catch (Exception exception)
        {
            failure = exception;
        }
        finally
        {
            try
            {
                window?.PrepareForExit();
                window?.Close();
                application?.Shutdown();
            }
            catch (Exception exception)
            {
                failure ??= exception;
            }
        }
    });
    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    thread.Join(TimeSpan.FromSeconds(20));

    if (thread.IsAlive)
    {
        Console.Error.WriteLine("关闭到托盘材质回归超时。 ");
        return 1;
    }

    if (failure is not null)
    {
        Console.Error.WriteLine(
            $"关闭到托盘材质回归异常：{failure}");
        return 1;
    }

    Console.WriteLine(
        passed
            ? $"关闭到托盘材质回归通过：{diagnostics}。"
            : $"关闭到托盘材质回归失败：{diagnostics}。 ");
    return passed ? 0 : 1;
}

bool WaitForWindowState(
    System.Windows.Window window,
    System.Windows.WindowState targetState,
    TimeSpan timeout)
{
    if (window.WindowState == targetState)
    {
        return true;
    }

    var frame = new System.Windows.Threading.DispatcherFrame();
    var timer = new System.Windows.Threading.DispatcherTimer(
        System.Windows.Threading.DispatcherPriority.Background,
        window.Dispatcher)
    {
        Interval = timeout
    };
    EventHandler? stateChanged = null;
    stateChanged = (_, _) =>
    {
        if (window.WindowState == targetState)
        {
            frame.Continue = false;
        }
    };
    timer.Tick += (_, _) => frame.Continue = false;
    window.StateChanged += stateChanged;
    timer.Start();
    try
    {
        System.Windows.Threading.Dispatcher.PushFrame(frame);
    }
    finally
    {
        timer.Stop();
        window.StateChanged -= stateChanged;
    }

    return window.WindowState == targetState;
}

bool WaitForDispatcherCondition(
    System.Windows.Threading.Dispatcher dispatcher,
    Func<bool> condition,
    TimeSpan timeout)
{
    if (condition())
    {
        return true;
    }

    var frame = new System.Windows.Threading.DispatcherFrame();
    var timeoutTimer = new System.Windows.Threading.DispatcherTimer(
        System.Windows.Threading.DispatcherPriority.Background,
        dispatcher)
    {
        Interval = timeout
    };
    var conditionTimer = new System.Windows.Threading.DispatcherTimer(
        System.Windows.Threading.DispatcherPriority.Background,
        dispatcher)
    {
        Interval = TimeSpan.FromMilliseconds(10)
    };
    conditionTimer.Tick += (_, _) =>
    {
        if (condition())
        {
            frame.Continue = false;
        }
    };
    timeoutTimer.Tick += (_, _) => frame.Continue = false;
    conditionTimer.Start();
    timeoutTimer.Start();
    try
    {
        System.Windows.Threading.Dispatcher.PushFrame(frame);
    }
    finally
    {
        conditionTimer.Stop();
        timeoutTimer.Stop();
    }

    return condition();
}

string FormatOptionalRate(long? value) =>
    value?.ToString("N0") ?? "缺失";

string FormatOptionalBytes(long? value) =>
    value?.ToString("N0") ?? "缺失";

internal static class NativeWindowVisibilityProbe
{
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(
        System.Runtime.InteropServices.UnmanagedType.Bool)]
    internal static extern bool IsWindowVisible(IntPtr windowHandle);
}

internal sealed class TestAppNotificationPlatform : IAppNotificationPlatform
{
    internal int ConfigureIdentityCalls { get; private set; }
    internal int RegisterCalls { get; private set; }
    internal string? StableAppUserModelId { get; private set; }
    internal string? DisplayName { get; private set; }
    internal Uri? IconUri { get; private set; }
    internal Exception? ConfigureIdentityFailure { get; init; }
    internal Exception? RegisterFailure { get; init; }
    internal Exception? ShowFailure { get; init; }
    internal List<string> Calls { get; } = [];
    internal List<(string Title, string Message, string ArgumentName, string ArgumentValue)> Delivered { get; } = [];
    internal List<bool> SingleLineText { get; } = [];

    public bool IsSupported()
    {
        Calls.Add("IsSupported");
        return true;
    }

    public void ConfigureStableIdentity(string appUserModelId)
    {
        Calls.Add("ConfigureIdentity");
        ConfigureIdentityCalls++;
        StableAppUserModelId = appUserModelId;
        if (ConfigureIdentityFailure is not null)
        {
            throw ConfigureIdentityFailure;
        }
    }

    public void Register(string displayName, Uri iconUri)
    {
        Calls.Add("Register");
        RegisterCalls++;
        DisplayName = displayName;
        IconUri = iconUri;
        if (RegisterFailure is not null)
        {
            throw RegisterFailure;
        }
    }

    public void Show(
        string title,
        string message,
        string argumentName,
        string argumentValue,
        bool singleLineText = false)
    {
        Calls.Add("Show");
        Delivered.Add((title, message, argumentName, argumentValue));
        SingleLineText.Add(singleLineText);
        if (ShowFailure is not null)
        {
            throw ShowFailure;
        }
    }
}
