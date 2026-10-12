using LanDeviceMonitor.Models;

namespace LanDeviceMonitor.Services;

public static class DeviceIdentityService
{
    public static readonly IReadOnlyList<DeviceIdentity> AvailableTypes =
    [
        new("手机", "\uE8EA", "手机"),
        new("平板电脑", "\uE70A", "平板电脑"),
        new("笔记本电脑", "\uE770", "笔记本电脑"),
        new("台式电脑", "\uE7F4", "台式电脑"),
        new("路由器", "\uE8CE", "路由器"),
        new("智能音箱", "\uE7F5", "智能音箱"),
        new("灯", "\uEA80", "灯"),
        new("插座", "\uE7E8", "插座"),
        new("空调", "\uE9CA", "空调"),
        new("监测设备", "\uE9D9", "监测设备"),
        new("窗帘", "\uE7F8", "窗帘"),
        new("传感器", "\uE957", "传感器"),
        new("电视", "\uE7F4", "电视"),
        new("游戏设备", "\uE7FC", "游戏设备"),
        new("摄像头", "\uE722", "摄像头"),
        new("其他设备", "\uE772", "其他设备")
    ];

    public static DeviceIdentity Identify(string routerName, string originalName, int connectionType)
    {
        var source = $"{routerName} {originalName}".Trim();
        var normalized = source.ToLowerInvariant();

        if (ContainsAny(normalized, "camera", "webcam", "摄像头", "摄像机", "chuangmi.camera", "ipcam"))
        {
            return Identity("摄像头", "\uE722", CleanProductName(source));
        }

        if (ContainsAny(normalized, "iphone", "redmi-note", "xiaomi-", "huawei", "mate", "phone"))
        {
            return Identity("手机", "\uE8EA", CleanProductName(source));
        }

        if (ContainsAny(normalized, "ipad", "tablet", "pad "))
        {
            return Identity("平板电脑", "\uE70A", CleanProductName(source));
        }

        if (ContainsAny(normalized, "laptop", "notebook", "macbook", "thinkpad", "天选", "rog"))
        {
            return Identity("笔记本电脑", "\uE770", CleanProductName(source));
        }

        if (ContainsAny(normalized, "desktop", "windows-pc", "imac"))
        {
            return Identity("台式电脑", "\uE7F4", CleanProductName(source));
        }

        if (ContainsAny(normalized, "miwifi", "router", "路由器"))
        {
            return Identity("路由器", "\uE8CE", "小米路由器");
        }

        if (ContainsAny(normalized, "soundbox", "speaker", "小爱音箱", "xiaoa"))
        {
            return Identity("智能音箱", "\uE7F5", "小爱音箱");
        }

        if (ContainsAny(normalized, "yeelink-light", "lamp", "bulb", "light"))
        {
            return Identity("灯", "\uEA80", "智能灯");
        }

        if (ContainsAny(normalized, "plug", "socket", "acpartner"))
        {
            return Identity(
                "插座",
                "\uE7E8",
                normalized.Contains("acpartner", StringComparison.Ordinal) ? "空调伴侣" : "智能插座");
        }

        if (ContainsAny(normalized, "aircondition", "air-condition", "midea", "gree"))
        {
            return Identity("空调", "\uE9CA", "空调");
        }

        if (ContainsAny(normalized, "airm", "air-monitor", "cgllc"))
        {
            return Identity("监测设备", "\uE9D9", "空气检测仪");
        }

        if (ContainsAny(normalized, "curtain", "motor", "wfxx"))
        {
            return Identity("窗帘", "\uE7F8", "智能窗帘");
        }

        if (ContainsAny(normalized, "sensor", "lumi-sensor"))
        {
            return Identity("传感器", "\uE957", "传感器");
        }

        if (ContainsAny(normalized, "tv", "television", "mitv"))
        {
            return Identity("电视", "\uE7F4", "电视");
        }

        if (ContainsAny(normalized, "xbox", "playstation", "switch", "steamdeck"))
        {
            return Identity("游戏设备", "\uE7FC", CleanProductName(source));
        }

        if (connectionType == 0 && ContainsAny(normalized, "pc", "computer"))
        {
            return Identity("台式电脑", "\uE7F4", CleanProductName(source));
        }

        return Identity(
            "其他设备",
            "\uE772",
            string.IsNullOrWhiteSpace(routerName) ? CleanProductName(originalName) : routerName);
    }

    private static DeviceIdentity Identity(string type, string glyph, string name) =>
        new(type, glyph, string.IsNullOrWhiteSpace(name) ? type : name);

    private static bool ContainsAny(string value, params string[] candidates) =>
        candidates.Any(candidate => value.Contains(candidate, StringComparison.OrdinalIgnoreCase));

    private static string CleanProductName(string value)
    {
        var first = value.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? value;
        return first.Replace('-', ' ').Replace('_', ' ').Trim();
    }
}
