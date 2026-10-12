using System.Text.Json;
using LanDeviceMonitor.Models;
using LanDeviceMonitor.Services;
using LanDeviceMonitor.Services.WebAccess;
using System.Net;
using System.Reflection;
using System.Windows;
using System.Windows.Threading;
using LanDeviceMonitor;

internal static class MeshMembershipRegression
{
    internal static async Task<int> RunAsync()
    {
        CheckIdentityRules();
        CheckDesktopWithoutWeb();
        await CheckAutomaticPollingAsync();
        await CheckReconnectIsolationAsync();
        await using var server = new LanWebServer();
        await server.StartAsync(0, loopbackOnly: true);
        using var http = new HttpClient { BaseAddress = new Uri(server.Urls.Single()) };
        var member = Device("02:00:00:00:72:01", "10.77.0.2", true);
        var ordinaryRouter = Device("02:00:00:00:72:02", "10.77.0.3", true);
        server.Publish(new MonitorUpdate([member, ordinaryRouter], [], DateTimeOffset.Now));
        server.SetMesh([new MeshNodeInfo("节点", member.Profile.LastIpAddress, "AX3000T", "有线", 0, false)]);
        using var result = JsonDocument.Parse(await http.GetStringAsync("/api/snapshot?view=overview"));
        Require(result.RootElement.GetProperty("devices").GetArrayLength() == 1,
            "拓扑首次到达应立即移除真实 Mesh 成员，同时保留非成员路由器");
        member.Profile.LastKnownOnline = false;
        server.Publish(new MonitorUpdate([member, ordinaryRouter], [], DateTimeOffset.Now));
        using var offline = JsonDocument.Parse(await http.GetStringAsync("/api/snapshot?view=overview"));
        Require(offline.RootElement.GetProperty("devices").GetArrayLength() == 1, "已确认 Mesh 成员离线后不能重回网页列表");
        Require((await http.GetAsync("/api/device/" + Uri.EscapeDataString(member.Profile.MacAddress))).IsSuccessStatusCode,
            "隐藏 Mesh 成员不能删除其历史详情缓存");
        server.SetMesh([]);
        using var removed = JsonDocument.Parse(await http.GetStringAsync("/api/snapshot?view=overview"));
        Require(removed.RootElement.GetProperty("devices").GetArrayLength() == 2, "成功拓扑已移除的节点应立即恢复为普通设备");
        var sharedFilter = new MeshMemberFilter();
        member.Profile.LastKnownOnline = true;
        sharedFilter.UpdateTopology([new MeshNodeInfo("节点", member.Profile.LastIpAddress, "AX", "", 0, false)]);
        sharedFilter.Filter([member]);
        member.Profile.LastKnownOnline = false;
        sharedFilter.InvalidateIpEvidence();
        await using var lateServer = new LanWebServer(meshMemberFilter: sharedFilter);
        await lateServer.StartAsync(0, loopbackOnly: true);
        lateServer.SetMesh([], hasValidTopology: false);
        lateServer.Publish(new MonitorUpdate([member, ordinaryRouter], [], DateTimeOffset.Now));
        using var lateHttp = new HttpClient { BaseAddress = new Uri(lateServer.Urls.Single()) };
        using var lateResult = JsonDocument.Parse(await lateHttp.GetStringAsync("/api/snapshot?view=overview"));
        Require(lateResult.RootElement.GetProperty("devices").GetArrayLength() == 1,
            "拓扑失败后才开启 Web 仍须复用桌面已证实的离线 MAC 身份");
        Console.WriteLine("Mesh membership PASS: MAC priority, safe router-only IP fallback, DHCP reuse, online/offline, topology removal/failure, immediate web/native updates, background polling without web, shared requests, A/B generation isolation (late success/failure), Stop/Dispose manual cancellation, retained history.");
        return 0;
    }

    private static void CheckIdentityRules()
    {
        var filter = new MeshMemberFilter();
        var root = Device("02:00:00:00:72:11", "10.77.0.1", true);
        root.Profile.CustomType = "手机"; // User preference must not defeat an exact MAC identity.
        var leaf = Device("02:00:00:00:72:12", "10.77.0.2", false);
        var otherRouter = Device("02:00:00:00:72:13", "10.77.0.9", true);
        var phone = Device("02:00:00:00:72:14", "10.77.0.3", true, "手机");
        var staleOffline = Device("02:00:00:00:72:15", "10.77.0.9", false);
        using var topology = JsonDocument.Parse("""{"graph":{"mac":"02-00-00-00-72-11","ip":"10.77.0.9","leafs":[{"macaddr":"02:00:00:00:72:12","ip":"10.77.0.2"},{"ip":"10.77.0.3"}]}}""");
        var nodes = XiaomiRouterClient.ParseMeshTopology(topology.RootElement);
        Require(nodes.Count == 3 && nodes[0].IsGateway && nodes[1].MacAddress == leaf.Profile.MacAddress, "完整拓扑应提取主/子节点 MAC");
        filter.UpdateTopology(nodes);
        var remaining = filter.Filter([root, leaf, otherRouter, phone, staleOffline]);
        Require(remaining.Count == 3 && remaining.Contains(otherRouter) && remaining.Contains(phone) && remaining.Contains(staleOffline),
            "MAC 应优先；主路由、离线子路由隐藏，其他路由器/同名终端/旧 IP 离线记录保留");
        var fallback = Device("02:00:00:00:72:16", "10.77.0.4", true);
        var node = new MeshNodeInfo("同名", "10.77.0.4", "AX", "有线", 0, false);
        filter.UpdateTopology([node]);
        Require(filter.Filter([fallback, otherRouter]).Count == 1, "无 MAC 节点可用当前唯一在线路由器 IP 建立身份");
        fallback.Profile.LastKnownOnline = false;
        var successor = Device("02:00:00:00:72:17", "10.77.0.4", true);
        Require(filter.Filter([fallback, successor]).Single() == successor, "DHCP IP 接替者不能继承已绑定 Mesh MAC 的成员资格");
        filter.InvalidateIpEvidence();
        Require(filter.Filter([fallback, successor]).Single() == successor, "拓扑失败应保留已证实的离线 MAC，不再使用 IP");
        filter.UpdateTopology([]);
        Require(filter.Filter([fallback, successor]).Count == 2, "成功新拓扑移除节点应释放原成员身份");
        filter.UpdateTopology([node]);
        var duplicate = Device("02:00:00:00:72:18", "10.77.0.4", true);
        Require(filter.Filter([successor, duplicate]).Count == 2, "冲突地址不能建立成员资格");
        foreach (var invalid in new[] { "", "—", "0.0.0.0", "127.0.0.1", "255.255.255.255", "224.0.0.1", "1234" })
        {
            filter.UpdateTopology([node with { IpAddress = invalid }]);
            successor.Profile.LastIpAddress = invalid;
            Require(filter.Filter([successor]).Count == 1, "非法/特殊 IP 不应隐藏设备: " + invalid);
        }
        successor.Profile.LastIpAddress = "10.77.0.4";
        filter.UpdateTopology([node]); filter.InvalidateIpEvidence();
        Require(filter.Filter([successor]).Count == 1, "失败拓扑不能建立新的 IP 身份");
    }

    private static void CheckDesktopWithoutWeb()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            App? app = null; MainWindow? window = null;
            try
            {
                app = TestAppFactory.Create(); app.InitializeComponent(); app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                window = new MainWindow(new NotificationService());
                var root = Device("02:00:00:00:72:21", "10.77.0.1", true);
                var leaf = Device("02:00:00:00:72:22", "10.77.0.2", false);
                var ordinary = Device("02:00:00:00:72:23", "10.77.0.3", true);
                Call(window, "ReplaceDevices", new DeviceViewData[] { root, leaf, ordinary }, false, false);
                Require(window.OnlineCountText.Text == "2" && window.OfflineCountText.Text == "1", "首次拓扑前应显示原设备");
                var nodes = new[] {
                    new MeshNodeInfo("主路由", root.Profile.LastIpAddress, "AX", "", 0, true) { MacAddress = root.Profile.MacAddress },
                    new MeshNodeInfo("子路由", leaf.Profile.LastIpAddress, "AX", "", 0, false) { MacAddress = leaf.Profile.MacAddress }
                };
                var captured = window;
                Task.Run(() => Call(captured, "MonitoringService_MeshTopologyUpdated", null!, new MeshTopologyUpdate(nodes, true))).GetAwaiter().GetResult();
                Pump();
                Require(window.OnlineCountText.Text == "1" && window.OfflineCountText.Text == "0" && window.DeviceList.Items.Count == 1,
                    "后台拓扑首次到达应在 UI Dispatcher 上更新 WPF 计数/VM，未开启 Web 或打开路由页也有效");
                Require(typeof(MainWindow).GetField("_lanWebServer", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window) is null,
                    "桌面过滤测试不得依赖 Web 开启");
                Call(window, "ApplyMeshTopology", new MeshTopologyUpdate([], false));
                Require(window.DeviceList.Items.Count == 1, "拓扑请求失败应保留已证实成员隐藏状态");
                Call(window, "ApplyMeshTopology", new MeshTopologyUpdate([], true));
                Require(window.OnlineCountText.Text == "2" && window.OfflineCountText.Text == "1" && window.DeviceList.Items.Count == 2,
                    "成功拓扑重新刷新应恢复退出成员且保留离线原始数据");
            }
            catch (Exception ex) { failure = ex; }
            finally { window?.PrepareForExit(); window?.Close(); app?.Shutdown(); }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        Require(thread.Join(TimeSpan.FromSeconds(20)), "Desktop Mesh regression timed out");
        if (failure is not null) throw failure;
    }

    private static async Task CheckAutomaticPollingAsync()
    {
        var statePath = Path.Combine(Path.GetTempPath(), "LanMeshRegression-" + Guid.NewGuid().ToString("N") + ".json");
        var handler = new FakeRouterHandler();
        using var service = new MonitoringService(new XiaomiRouterClient(new HttpClient(handler)), new DeviceRepository(statePath));
        var arrived = new TaskCompletionSource<MeshTopologyUpdate>(TaskCreationOptions.RunContinuationsAsynchronously);
        service.MeshTopologyUpdated += (_, update) => { if (update.Nodes.Count > 0) arrived.TrySetResult(update); };
        try
        {
            await service.ConnectAndStartAsync("10.77.0.1", "synthetic-test-password", 1);
            var update = await arrived.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Require(update.HasValidTopology && handler.MeshRequests == 1, "连接监控后应自动读取拓扑，独立于 Web 与路由页面");
            Require((await service.GetMeshNodesAsync()).Count == 1 && handler.MeshRequests == 1, "普通页面读取应共用自动轮询缓存");
            await Task.WhenAll(service.GetMeshNodesAsync(forceRefresh: true), service.GetMeshNodesAsync(forceRefresh: true));
            Require(handler.MeshRequests == 2, "同时强制刷新必须共享一个实际拓扑请求");
            await service.StopAsync();
            var stopped = handler.MeshRequests;
            await Task.Delay(120);
            Require(handler.MeshRequests == stopped, "停止监控应取消统一拓扑轮询");
        }
        finally
        {
            await service.StopAsync();
            if (File.Exists(statePath)) File.Delete(statePath);
        }
    }

    private static async Task CheckReconnectIsolationAsync()
    {
        foreach (var lateFailure in new[] { false, true })
        {
            var statePath = Path.Combine(Path.GetTempPath(), "LanMeshReconnect-" + Guid.NewGuid().ToString("N") + ".json");
            var handler = new FakeRouterHandler();
            using var service = new MonitoringService(new XiaomiRouterClient(new HttpClient(handler)), new DeviceRepository(statePath));
            var updates = new List<MeshTopologyUpdate>();
            service.MeshTopologyUpdated += (_, update) => { lock (updates) updates.Add(update); };
            try
            {
                await service.ConnectAndStartAsync("10.77.0.1", "synthetic-A", 60);
                await service.GetMeshNodesAsync();
                handler.DelayNextMesh = true;
                handler.DelayedMeshFails = lateFailure;
                var oldManual = service.GetMeshNodesAsync(forceRefresh: true);
                await handler.DelayedMeshStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
                var before = updates.Count;
                var reconnect = service.ConnectAndStartAsync("10.77.1.1", "synthetic-B", 60);
                await Task.Delay(40);
                Require(handler.DelayedMeshCancellation.IsCancellationRequested,
                    "重连 B 必须取消无 caller token 的手动 A 拓扑请求");
                Require(!reconnect.IsCompleted, "Configure B 前必须排空 A 请求，禁止可变 RouterClient 跨连接并发使用");
                handler.ReleaseDelayedMesh.TrySetResult();
                try { await oldManual; throw new InvalidOperationException("旧 A 请求取消后不能交付成功拓扑"); }
                catch (OperationCanceledException) { }
                await reconnect.WaitAsync(TimeSpan.FromSeconds(5));
                var b = await service.GetMeshNodesAsync();
                Require(handler.BMeshRequests == 1 && b.Single().IpAddress == "10.77.1.1",
                    "B 必须进行自己的真实请求，不能因 A 请求完成时间复用旧 A 缓存");
                lock (updates)
                {
                    Require(updates.Skip(before).All(update => update.HasValidTopology &&
                        update.Nodes.All(node => node.IpAddress == "10.77.1.1")),
                        "失效 generation 的 A 晚成功/晚失败不能更新缓存或发布 Mesh 成功/失败事件");
                }
                await service.StopAsync();
                try { await service.GetMeshNodesAsync(forceRefresh: true); throw new InvalidOperationException("Stop 后的新 Mesh 请求必须取消"); }
                catch (OperationCanceledException) { }
                await service.ConnectAndStartAsync("10.77.1.1", "synthetic-B", 60);
                await service.GetMeshNodesAsync();
                handler.DelayedMeshStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
                handler.ReleaseDelayedMesh = new(TaskCreationOptions.RunContinuationsAsynchronously);
                handler.DelayNextMesh = true;
                var disposingManual = service.GetMeshNodesAsync(forceRefresh: true);
                await handler.DelayedMeshStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
                var beforeDispose = updates.Count;
                service.Dispose();
                Require(handler.DelayedMeshCancellation.IsCancellationRequested, "Dispose 必须取消无 caller token 的手动拓扑请求");
                handler.ReleaseDelayedMesh.TrySetResult();
                try { await disposingManual; throw new InvalidOperationException("Dispose 后的晚拓扑不能成功交付"); }
                catch (OperationCanceledException) { }
                Require(updates.Count == beforeDispose, "Dispose 后的手动晚成功/晚失败均不能发布 Mesh 事件");
            }
            finally
            {
                handler.ReleaseDelayedMesh.TrySetResult();
                await service.StopAsync();
                if (File.Exists(statePath)) File.Delete(statePath);
            }
        }
    }

    private sealed class FakeRouterHandler : HttpMessageHandler
    {
        internal int MeshRequests;
        internal int BMeshRequests;
        internal bool DelayNextMesh;
        internal bool DelayedMeshFails;
        internal CancellationToken DelayedMeshCancellation;
        internal TaskCompletionSource DelayedMeshStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource ReleaseDelayedMesh = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            string body;
            if (path.EndsWith("/web")) body = "key: 'synthetic-key'; var deviceId = '02:00:00:00:72:01';";
            else if (path.EndsWith("/login")) body = """{"code":0,"token":"synthetic-token"}""";
            else if (path.EndsWith("/topo_graph"))
            {
                Interlocked.Increment(ref MeshRequests);
                if (request.RequestUri.Host == "10.77.1.1") Interlocked.Increment(ref BMeshRequests);
                if (DelayNextMesh)
                {
                    DelayNextMesh = false;
                    DelayedMeshCancellation = cancellationToken;
                    DelayedMeshStarted.TrySetResult();
                    // Simulate a transport that delivers late success/failure despite cancellation.
                    await ReleaseDelayedMesh.Task;
                    if (DelayedMeshFails) throw new HttpRequestException("Synthetic delayed A failure");
                }
                else await Task.Delay(80, cancellationToken);
                body = "{\"code\":0,\"graph\":{\"ip\":\"" + request.RequestUri.Host + "\",\"mac\":\"02:00:00:00:72:01\"}}";
            }
            else if (path.EndsWith("/devicelist")) body = """{"code":0,"list":[]}""";
            else if (path.EndsWith("/wan_info")) body = """{"code":0,"info":{}}""";
            else body = """{"code":0,"newEncryptMode":0}""";
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) };
        }
    }

    private static void Call(MainWindow window, string name, params object[] args) =>
        typeof(MainWindow).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, args);
    private static void Pump()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    private static DeviceViewData Device(string mac, string ip, bool online, string type = "路由器") => new()
    {
        Profile = new DeviceProfile { MacAddress = mac, LastIpAddress = ip, LastKnownOnline = online, LastRouterName = "相同展示名" },
        Identity = new DeviceIdentity(type, "\uE968", "相同展示名"), Sessions = []
    };
    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
}
