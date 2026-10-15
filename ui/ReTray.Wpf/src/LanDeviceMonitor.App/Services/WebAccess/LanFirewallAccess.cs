using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;

namespace LanDeviceMonitor.Services.WebAccess;

internal interface ILanFirewallAccess
{
    Task EnsureAsync(int port);
}

internal sealed class LanFirewallAccess : ILanFirewallAccess
{
    internal const string HelperArgument = "--configure-lan-firewall";
    internal const int Profiles = 7; // Home networks are often marked Public by Windows.
    internal const string RemoteScope = "LocalSubnet";

    internal static string RuleName(string executable) => "LanDeviceMonitor.Web." +
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(executable).ToUpperInvariant())))[..16];

    internal static bool TryParsePort(string[] args, out int port)
    {
        port = 0;
        return args.Length == 2 && args[0] == HelperArgument &&
            int.TryParse(args[1], NumberStyles.None, CultureInfo.InvariantCulture, out port) && port is >= 1024 and <= 65535;
    }

    internal static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static object CreatePolicy() => Activator.CreateInstance(Type.GetTypeFromProgID("HNetCfg.FwPolicy2", true)!)!;
    private static void Release(object? value) { if (value is not null && Marshal.IsComObject(value)) Marshal.FinalReleaseComObject(value); }

    internal static void ValidatePolicy(int state)
    {
        if (state != 0)
            throw new InvalidOperationException("Windows 当前策略阻止本地访问规则生效，请由系统管理员检查网络访问策略。局域网访问未开启。");
    }
    private static void CheckPolicy()
    {
        object? instance = null;
        try { dynamic policy = instance = CreatePolicy(); ValidatePolicy((int)policy.LocalPolicyModifyState); }
        finally { Release(instance); }
    }

    internal static bool HasRule(string executable, int port)
    {
        object? policyObject = null, rulesObject = null, ruleObject = null;
        try
        {
            dynamic policy = policyObject = CreatePolicy();
            dynamic rules = rulesObject = policy.Rules;
            try { ruleObject = rules.Item(RuleName(executable)); }
            catch (Exception ex) when (ex.HResult == unchecked((int)0x80070002)) { return false; }
            dynamic rule = ruleObject;
            return rule.Enabled && rule.Direction == 1 && rule.Action == 1 && rule.Protocol == 6 &&
                rule.Profiles == Profiles && !rule.EdgeTraversal &&
                string.Equals((string)rule.ApplicationName, executable, StringComparison.OrdinalIgnoreCase) &&
                rule.LocalPorts == port.ToString(CultureInfo.InvariantCulture) &&
                string.Equals((string)rule.RemoteAddresses, RemoteScope, StringComparison.OrdinalIgnoreCase);
        }
        finally { Release(ruleObject); Release(rulesObject); Release(policyObject); }
    }

    internal static void Configure(string executable, int port)
    {
        if (port is < 1024 or > 65535 || !File.Exists(executable) || !IsAdministrator())
            throw new UnauthorizedAccessException("需要 Windows 管理员授权。");
        object? policyObject = null, rulesObject = null, ruleObject = null;
        try
        {
            dynamic policy = policyObject = CreatePolicy();
            ValidatePolicy((int)policy.LocalPolicyModifyState);
            dynamic rules = rulesObject = policy.Rules;
            var exists = true;
            try { ruleObject = rules.Item(RuleName(executable)); }
            catch (Exception ex) when (ex.HResult == unchecked((int)0x80070002))
            { exists = false; ruleObject = Activator.CreateInstance(Type.GetTypeFromProgID("HNetCfg.FWRule", true)!)!; }
            dynamic rule = ruleObject;
            rule.Enabled = false;
            rule.Name = RuleName(executable);
            rule.Description = "ReTray：仅允许同一局域网访问本程序的只读网页。";
            rule.ApplicationName = executable;
            rule.Protocol = 6;
            rule.LocalPorts = port.ToString(CultureInfo.InvariantCulture);
            rule.RemotePorts = "*";
            rule.LocalAddresses = "*";
            rule.RemoteAddresses = RemoteScope;
            rule.Profiles = Profiles;
            rule.InterfaceTypes = "All";
            rule.Direction = 1;
            rule.Action = 1;
            rule.EdgeTraversal = false;
            rule.Enabled = true;
            if (!exists) rules.Add(rule);
        }
        finally { Release(ruleObject); Release(rulesObject); Release(policyObject); }
        if (!HasRule(executable, port)) throw new InvalidOperationException("网络访问规则未生效。");
    }

    public async Task EnsureAsync(int port)
    {
        var executable = Environment.ProcessPath ?? throw new InvalidOperationException("无法确认程序位置。");
        try
        {
            // An existing allow rule is not sufficient when Group Policy or the
            // active profile's block-all mode overrides local rules.
            await Task.Run(CheckPolicy);
            if (await Task.Run(() => HasRule(executable, port))) return;
            if (IsAdministrator()) { await Task.Run(() => Configure(executable, port)); return; }
            var start = new ProcessStartInfo(executable)
            {
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden,
                Arguments = HelperArgument + " " + port.ToString(CultureInfo.InvariantCulture)
            };
            using var process = await Task.Run(() => Process.Start(start)) ?? throw new InvalidOperationException("无法启动 Windows 授权。");
            await process.WaitForExitAsync();
            await Task.Run(CheckPolicy);
            if (process.ExitCode != 0 || !await Task.Run(() => HasRule(executable, port)))
                throw new InvalidOperationException("Windows 未能配置局域网访问权限，可能受到组织策略限制。");
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        { throw new InvalidOperationException("未获得 Windows 授权，局域网访问未开启。再次开启开关即可重试。", ex); }
        catch (COMException ex)
        { throw new InvalidOperationException("无法检查 Windows 网络访问权限，请确认 Windows 防火墙服务正常。", ex); }
    }
}
