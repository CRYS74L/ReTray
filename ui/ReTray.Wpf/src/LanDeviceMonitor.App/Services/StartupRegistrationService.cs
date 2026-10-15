using System.IO;
using Microsoft.Win32;

namespace LanDeviceMonitor.Services;

public sealed class StartupRegistrationService
{
    internal const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    internal const string ValueName = "\u5C40\u57DF\u7F51\u8BBE\u5907\u52A9\u624B";
    internal const string BackgroundArgument = "--background";

    public bool IsEnabled()
    {
        using var runKey = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        return runKey?.GetValue(ValueName) is string value &&
               !string.IsNullOrWhiteSpace(value);
    }

    public void SetEnabled(bool enabled)
    {
        using var runKey = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true)
            ?? throw new InvalidOperationException("无法打开当前用户的开机启动注册表项。");

        if (!enabled)
        {
            // 只删除本应用拥有的精确值名，不枚举、不覆盖、不改动任何其他启动项。
            runKey.DeleteValue(ValueName, throwOnMissingValue: false);
            return;
        }

        var executablePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
        {
            throw new InvalidOperationException("无法确定应用程序路径，未修改开机启动设置。");
        }

        runKey.SetValue(
            ValueName,
            BuildCommand(executablePath),
            RegistryValueKind.String);
    }

    internal static string BuildCommand(string executablePath) =>
        $"\"{Path.GetFullPath(executablePath)}\" {BackgroundArgument}";
}
