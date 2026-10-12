using System.Globalization;

namespace LanDeviceMonitor.Services;

public static class NotificationSchedule
{
    public static bool IsMuted(
        DateTimeOffset now,
        bool enabled,
        int startMinutes,
        int endMinutes)
    {
        if (!enabled)
        {
            return false;
        }

        startMinutes = Math.Clamp(startMinutes, 0, 1439);
        endMinutes = Math.Clamp(endMinutes, 0, 1439);
        var currentMinutes = (now.Hour * 60) + now.Minute;
        if (startMinutes == endMinutes)
        {
            return true;
        }

        return startMinutes < endMinutes
            ? currentMinutes >= startMinutes && currentMinutes < endMinutes
            : currentMinutes >= startMinutes || currentMinutes < endMinutes;
    }

    public static bool TryParseTime(string? value, out int minutes)
    {
        var formats = new[] { "H:mm", "HH:mm" };
        if (DateTime.TryParseExact(
                value?.Trim(),
                formats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var parsed))
        {
            minutes = (parsed.Hour * 60) + parsed.Minute;
            return true;
        }

        minutes = 0;
        return false;
    }

    public static string FormatTime(int minutes)
    {
        minutes = Math.Clamp(minutes, 0, 1439);
        return $"{minutes / 60:00}:{minutes % 60:00}";
    }
}
