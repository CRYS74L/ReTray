using System.Windows;
using System.Windows.Threading;
using LanDeviceMonitor.Models;
using LanDeviceMonitor.Services;

namespace LanDeviceMonitor;

public partial class MainWindow
{
    private readonly MeshMemberFilter _meshMemberFilter = new();
    private IReadOnlyList<DeviceViewData> _unfilteredLatestDevices = [];

    private void MonitoringService_MeshTopologyUpdated(object? sender, MeshTopologyUpdate update)
    {
        if (_isExiting || Dispatcher.HasShutdownStarted) return;
        _lanWebServer?.SetMesh(update.Nodes, update.HasValidTopology);
        try
        {
            _ = Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() => ApplyMeshTopology(update)));
        }
        catch (InvalidOperationException) when (Dispatcher.HasShutdownStarted) { }
    }

    private void ApplyMeshTopology(MeshTopologyUpdate update)
    {
        if (_isExiting || Dispatcher.HasShutdownStarted) return;
        if (update.HasValidTopology)
        {
            _meshMemberFilter.UpdateTopology(update.Nodes);
            MeshNodesItemsControl.ItemsSource = update.Nodes;
            MeshNodesEmptyText.Visibility = update.Nodes.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        else _meshMemberFilter.InvalidateIpEvidence();
        ReplaceDevices(_unfilteredLatestDevices, preserveSelection: true);
    }
}
