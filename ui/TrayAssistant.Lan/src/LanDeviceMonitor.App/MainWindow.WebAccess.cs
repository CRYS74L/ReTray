using System.Net;
using System.Net.Sockets;
using System.Windows;
using LanDeviceMonitor.Models;
using LanDeviceMonitor.Services.WebAccess;

namespace LanDeviceMonitor;

public partial class MainWindow
{
    private LanWebServer? _lanWebServer;
    private ILanFirewallAccess _lanFirewallAccess = new LanFirewallAccess();
    private bool _applyingWebAccess;

    private async Task InitializeWebAccessAsync()
    {
        if (!_repository.Settings.LanAccessEnabled) return;
        await ApplyWebAccessAsync();
    }
    private async Task StartWebAccessAsync()
    {
        _lanWebServer ??= new LanWebServer(_repository.GetAllDeviceSessionsAsync, _meshMemberFilter);
        await _lanWebServer.StartAsync();
        WebAccessStatus.Text = "正在准备局域网访问；若 Windows 弹出授权，请允许此程序。";
        var port = new Uri(_lanWebServer.Urls[0]).Port;
        if (_lanWebServer.Urls.Any(url => new Uri(url).Host != "127.0.0.1"))
            await _lanFirewallAccess.EnsureAsync(port);
        _lanWebServer.SetPreferences(_repository.Settings, _hasSavedRouterPassword);
        var topology = _monitoringService.CurrentMeshTopology;
        _lanWebServer.SetMesh(topology.Nodes, topology.HasValidTopology);
        _lanWebServer.Publish(new MonitorUpdate(await _repository.GetDevicesAsync(), [], DateTimeOffset.MinValue));
        _lanWebServer.SeedHistory(_trafficHistories.ToDictionary(pair => pair.Key, pair => pair.Value.Snapshot(DateTimeOffset.Now)));
        _lanWebServer.SetConnection(ConnectionStatusText.Text);
        UpdateWebAccessDisplay();
    }
    private void UpdateWebAccessDisplay()
    {
        LanAccessDetails.Visibility = _lanWebServer?.IsRunning == true ? Visibility.Visible : Visibility.Collapsed;
        if (_lanWebServer?.IsRunning != true)
        {
            WebAccessStatus.Text = "";
            LanAddressBox.Text = "";
            return;
        }
        var urls = _lanWebServer.Urls.Where(u => !u.Contains("127.0.0.1")).ToArray();
        string? preferredIp = null;
        if (IPAddress.TryParse(_repository.Settings.RouterAddress, out var router) && router.AddressFamily == AddressFamily.InterNetwork)
        {
            try
            {
                using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                socket.Connect(router, 53); // Route lookup only; no datagram is sent.
                preferredIp = (socket.LocalEndPoint as IPEndPoint)?.Address.ToString();
            }
            catch (SocketException) { }
        }
        var first = urls.FirstOrDefault(u => new Uri(u).Host == preferredIp) ?? urls.FirstOrDefault() ?? _lanWebServer.Urls.First();
        LanAddressBox.Text = first + "/";
        WebAccessStatus.Text = urls.Length > 0 ? "" : "暂未发现局域网连接，目前仅本机可访问。连接网络后重新开启。";

    }
    private async void WebAccessToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_isPopulatingAppSettings || !IsLoaded || _applyingWebAccess) return;
        await ApplyWebAccessAsync();
    }
    private async Task ApplyWebAccessAsync()
    {
        if (_applyingWebAccess) return;
        _applyingWebAccess = true; LanAccessCheck.IsEnabled = false;
        try
        {
            if (LanAccessCheck.IsChecked == true)
            {
                await StartWebAccessAsync();
                await _repository.UpdateWebSettingsAsync(true);
            }
            else
            {
                if (_lanWebServer is not null) await _lanWebServer.StopAsync();
                await _repository.UpdateWebSettingsAsync(false);
                UpdateWebAccessDisplay();
            }
        }
        catch (Exception e)
        {
            if (_lanWebServer is not null) await _lanWebServer.StopAsync();
            LanAccessCheck.IsChecked = false;
            try { await _repository.UpdateWebSettingsAsync(false); } catch { }
            UpdateWebAccessDisplay(); WebAccessStatus.Text = "未能开启网页访问，请重试。" + e.Message;
        }
        finally { _applyingWebAccess = false; LanAccessCheck.IsEnabled = true; }
    }
    private void CopyLanAddress_Click(object sender, RoutedEventArgs e)
    {
        if (_lanWebServer?.IsRunning != true) return;
        try { System.Windows.Clipboard.SetText(LanAddressBox.Text); WebAccessStatus.Text = "链接已复制，手机打开即可查看。"; }
        catch (System.Runtime.InteropServices.COMException) { WebAccessStatus.Text = "暂时无法复制，请直接选择链接复制。"; }
    }
}
