using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using System.IO;
using System.Runtime.InteropServices;

namespace LanDeviceMonitor.Services;

public enum NotificationDeliveryChannel
{
    Pending,
    WindowsToast,
    SubmissionUnknown,
    Unavailable
}

public sealed record NotificationDeliveryResult(
    NotificationDeliveryChannel Channel,
    string StatusText,
    string? FailureDetail = null)
{
    public bool WasSubmittedToWindows =>
        Channel == NotificationDeliveryChannel.WindowsToast;

    public bool IsWindowsNotificationReady => Channel is
        NotificationDeliveryChannel.Pending or NotificationDeliveryChannel.WindowsToast;
}

internal interface IAppNotificationPlatform
{
    bool IsSupported();

    void ConfigureStableIdentity(string appUserModelId);

    void Register(string displayName, Uri iconUri);

    void Show(
        string title,
        string message,
        string argumentName,
        string argumentValue,
        bool singleLineText = false);
}

internal sealed class WindowsAppNotificationPlatform : IAppNotificationPlatform
{
    public bool IsSupported() => AppNotificationManager.IsSupported();

    public void ConfigureStableIdentity(string appUserModelId)
    {
        var result = SetCurrentProcessExplicitAppUserModelID(appUserModelId);
        Marshal.ThrowExceptionForHR(result);
    }

    public void Register(string displayName, Uri iconUri) =>
        AppNotificationManager.Default.Register(displayName, iconUri);

    public void Show(
        string title,
        string message,
        string argumentName,
        string argumentValue,
        bool singleLineText = false)
    {
        var builder = new AppNotificationBuilder().AddArgument(argumentName, argumentValue);
        if (singleLineText)
        {
            builder.AddText(title, new AppNotificationTextProperties().SetMaxLines(1));
            builder.AddText(message, new AppNotificationTextProperties().SetMaxLines(1));
        }
        else builder.AddText(title).AddText(message);
        var notification = builder.BuildNotification();

        AppNotificationManager.Default.Show(notification);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(
        string appUserModelId);
}

public sealed class NotificationService : IDisposable
{
    // This is the application's established unpackaged notification identity.
    // Changing it makes Windows treat the same executable as a new notification sender.
    internal const string StableAppUserModelId = "{95AFFE3D-014F-4F5F-AD8B-FD52B4E62CE3}";

    private const string DisplayName = "局域网设备助手";
    private const string IconRelativePath = "Assets\\SystemIcon.png";

    private readonly object _syncRoot = new();
    private readonly IAppNotificationPlatform _platform;
    private bool _registered;
    private NotificationDeliveryResult _lastResult = new(
        NotificationDeliveryChannel.Pending,
        "Windows 系统通知正在初始化。");

    public NotificationService()
        : this(new WindowsAppNotificationPlatform())
    {
    }

    internal NotificationService(IAppNotificationPlatform platform)
    {
        ArgumentNullException.ThrowIfNull(platform);
        _platform = platform;
    }

    public NotificationDeliveryResult LastResult
    {
        get
        {
            lock (_syncRoot)
            {
                return _lastResult;
            }
        }
    }

    public void Register()
    {
        lock (_syncRoot)
        {
            if (_registered)
            {
                return;
            }

            _lastResult = TryRegister(out var failureDetail)
                ? CreateStatus(NotificationDeliveryChannel.Pending, null)
                : CreateStatus(NotificationDeliveryChannel.Unavailable, failureDetail);
        }
    }

    public NotificationDeliveryResult ShowDeviceState(
        string displayName,
        bool isOnline,
        DateTimeOffset occurredAt)
    {
        var state = isOnline ? "已上线" : "已离线";
        return Show(
            title: $"{displayName}{state}",
            message: $"发生时间：{occurredAt:yyyy年MM月dd日 HH:mm:ss}",
            argumentName: "device",
            argumentValue: displayName);
    }

    public NotificationDeliveryResult ShowNewDeviceOnline(
        string displayName, DateTimeOffset occurredAt) => Show(
        title: $"新设备上线：{displayName}",
        message: $"发生时间：{occurredAt:yyyy年MM月dd日 HH:mm:ss}",
        argumentName: "device",
        argumentValue: displayName,
        singleLineText: true);

    public NotificationDeliveryResult ShowIpv6NonNative(string connectionType)
    {
        var displayType = string.IsNullOrWhiteSpace(connectionType)
            ? "未知"
            : connectionType.Trim();
        return Show(
            title: "当前 IPv6 连接类型不是 Native",
            message: $"当前连接类型：{displayType}",
            argumentName: "category",
            argumentValue: "ipv6");
    }

    public NotificationDeliveryResult ShowTestNotification() => Show(
        title: "Windows 系统通知测试",
        message: "局域网设备助手已将这条通知提交到 Windows。",
        argumentName: "category",
        argumentValue: "test");

    public void Dispose()
    {
        lock (_syncRoot)
        {
            // 只释放进程内状态。稳定 AUMID 的持久注册必须保留，避免下次启动
            // 重新生成通知身份；进程退出时系统会释放进程内 COM 注册。
            _registered = false;
        }
    }

    internal static string GetDeliveryStatusText(
        NotificationDeliveryChannel channel,
        string? failureDetail)
    {
        return channel switch
        {
            NotificationDeliveryChannel.Pending =>
                "Windows 系统通知已就绪；可使用下方按钮验证投递。",
            NotificationDeliveryChannel.WindowsToast =>
                "Windows 系统通知已提交到通知中心。",
            NotificationDeliveryChannel.SubmissionUnknown =>
                $"Windows 系统通知提交结果不确定：{failureDetail ?? "未知错误"}。为避免重复通知，未自动重试。",
            _ =>
                $"Windows 系统通知不可用：{failureDetail ?? "未知错误"}。可使用下方按钮重新检测。"
        };
    }

    private NotificationDeliveryResult Show(
        string title,
        string message,
        string argumentName,
        string argumentValue,
        bool singleLineText = false)
    {
        lock (_syncRoot)
        {
            if (!_registered && !TryRegister(out var registerFailure))
            {
                return Publish(
                    NotificationDeliveryChannel.Unavailable,
                    registerFailure);
            }

            try
            {
                _platform.Show(
                    title,
                    message,
                    argumentName,
                    argumentValue,
                    singleLineText);
                return Publish(NotificationDeliveryChannel.WindowsToast, null);
            }
            catch (Exception exception)
            {
                // Show 具有提交副作用；异常不能证明 Windows 未接收，因此绝不
                // 重试、重新注册或改走第二条通道。
                return Publish(
                    NotificationDeliveryChannel.SubmissionUnknown,
                    DescribeException(exception));
            }
        }
    }

    private bool TryRegister(out string? failureDetail)
    {
        failureDetail = null;
        if (_registered)
        {
            return true;
        }

        try
        {
            _platform.ConfigureStableIdentity(StableAppUserModelId);
            if (!_platform.IsSupported())
            {
                failureDetail = "当前 Windows 版本不支持 App SDK 系统通知";
                return false;
            }

            _platform.Register(DisplayName, GetIconUri());
            _registered = true;
            return true;
        }
        catch (Exception exception)
        {
            _registered = false;
            failureDetail = DescribeException(exception);
            return false;
        }
    }

    private NotificationDeliveryResult Publish(
        NotificationDeliveryChannel channel,
        string? failureDetail)
    {
        _lastResult = CreateStatus(channel, failureDetail);
        return _lastResult;
    }

    private static NotificationDeliveryResult CreateStatus(
        NotificationDeliveryChannel channel,
        string? failureDetail) => new(
        channel,
        GetDeliveryStatusText(channel, failureDetail),
        failureDetail);

    private static Uri GetIconUri()
    {
        var iconPath = Path.Combine(AppContext.BaseDirectory, IconRelativePath);
        if (!File.Exists(iconPath))
        {
            throw new FileNotFoundException("Windows 系统通知图标不存在", iconPath);
        }

        return new Uri(iconPath);
    }

    private static string DescribeException(Exception exception)
    {
        var rootException = exception.GetBaseException();
        var hresult = rootException.HResult == 0
            ? string.Empty
            : $"，HRESULT 0x{rootException.HResult:X8}";
        var message = string.IsNullOrWhiteSpace(rootException.Message)
            ? "没有提供错误说明"
            : rootException.Message.Trim();
        return $"{rootException.GetType().Name}{hresult}：{message}";
    }
}
