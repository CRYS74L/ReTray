using System.Net;
using System.Net.Sockets;
using LanDeviceMonitor.Models;

namespace LanDeviceMonitor.Services;

// Membership is topology evidence, never a device type or display-name heuristic.
// Keep the repository untouched; both desktop and web filter only their views.
internal sealed class MeshMemberFilter
{
    private readonly object _gate = new();
    private HashSet<string> _memberMacs = [];
    private HashSet<string> _fallbackIps = [];
    private readonly Dictionary<string, string> _resolvedMacsByIp = [];

    internal void UpdateTopology(IReadOnlyList<MeshNodeInfo> nodes)
    {
        lock (_gate)
        {
            _memberMacs = nodes.Select(n => NormalizeMac(n.MacAddress)).Where(m => m is not null).Select(m => m!).ToHashSet();
            _fallbackIps = nodes.Where(n => NormalizeMac(n.MacAddress) is null)
                .Select(n => NormalizeIp(n.IpAddress)).Where(ip => ip is not null).Select(ip => ip!).ToHashSet();
            foreach (var ip in _resolvedMacsByIp.Keys.Where(ip => !_fallbackIps.Contains(ip)).ToArray())
                _resolvedMacsByIp.Remove(ip);
            _memberMacs.UnionWith(_resolvedMacsByIp.Values);
        }
    }

    internal void InvalidateIpEvidence()
    { lock (_gate) _fallbackIps.Clear(); }

    internal IReadOnlyList<DeviceViewData> Filter(IReadOnlyList<DeviceViewData> devices)
    {
        lock (_gate)
        {
            // An old offline profile can carry an IP now assigned to someone else.
            // Resolve only a unique currently-online profile, then bind to its MAC.
            var onlineByIp = devices.Where(d => d.Profile.LastKnownOnline)
                .Select(d => (Device: d, Ip: NormalizeIp(d.Profile.LastIpAddress)))
                .Where(d => d.Ip is not null).GroupBy(d => d.Ip!).ToDictionary(g => g.Key, g => g.ToArray());
            foreach (var ip in _fallbackIps)
            {
                if (_resolvedMacsByIp.ContainsKey(ip) || !onlineByIp.TryGetValue(ip, out var matches) || matches.Length != 1 ||
                    matches[0].Device.Identity.TypeName != "路由器") continue;
                if (NormalizeMac(matches[0].Device.Profile.MacAddress) is not { } mac) continue;
                _resolvedMacsByIp[ip] = mac;
                _memberMacs.Add(mac);
            }
            return devices.Where(d => NormalizeMac(d.Profile.MacAddress) is not { } mac || !_memberMacs.Contains(mac)).ToArray();
        }
    }

    internal static string? NormalizeMac(string value)
    {
        var compact = value.Trim().Replace(":", "").Replace("-", "");
        if (compact.Length != 12 || !compact.All(Uri.IsHexDigit)) return null;
        var first = Convert.ToByte(compact[..2], 16);
        if ((first & 1) != 0 || compact.All(c => c == '0')) return null;
        return compact.ToUpperInvariant();
    }

    internal static string? NormalizeIp(string value)
    {
        if (!IPAddress.TryParse(value.Trim(), out var ip) || IPAddress.IsLoopback(ip) ||
            ip.Equals(IPAddress.Any) || ip.Equals(IPAddress.IPv6Any) || ip.Equals(IPAddress.Broadcast)) return null;
        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            // Require dotted IPv4; TryParse also accepts ambiguous integer forms.
            if (value.Trim().Split('.').Length != 4 || ip.GetAddressBytes()[0] is 0 or >= 224) return null;
        }
        else if (ip.AddressFamily != AddressFamily.InterNetworkV6 || ip.IsIPv6Multicast) return null;
        return ip.ToString();
    }
}
