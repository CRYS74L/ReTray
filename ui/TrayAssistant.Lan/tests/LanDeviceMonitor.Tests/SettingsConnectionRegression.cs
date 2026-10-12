using System.Reflection;
using System.IO;
using LanDeviceMonitor.Services;

internal static class SettingsConnectionRegression
{
    public static async Task<int> RunAsync()
    {
        var client = new XiaomiRouterClient();
        var service = new MonitoringService(client, new DeviceRepository(Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".json")));
        var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = new CancellationTokenSource();
        typeof(MonitoringService).GetField("_cancellation",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(service,cancellation);
        typeof(MonitoringService).GetField("_loopTask",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(service,pending.Task);
        try
        {
            for(var i=0;i<20;i++) service.UpdateRefreshInterval(1);
            if(cancellation.IsCancellationRequested) throw new Exception("相同刷新间隔仍取消轮询；免打扰等设置不应重启连接");
            Console.WriteLine("PASS unchanged refresh interval preserves active poll across repeated settings saves");
        }
        finally { service.Dispose(); pending.TrySetResult(); }
        using var cancelled = new CancellationTokenSource();cancelled.Cancel();
        using var router = new XiaomiRouterClient();router.Configure("http://127.0.0.1:1","synthetic-test");
        try { await router.AuthenticateAsync(cancelled.Token);throw new Exception("取消应传递给调用方"); }
        catch(OperationCanceledException) { Console.WriteLine("PASS intentional authentication cancellation is not reported as router failure"); }
        typeof(XiaomiRouterClient).GetField("_token",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(router,"synthetic-token");
        var method=typeof(XiaomiRouterClient).GetMethod("GetApiDocumentAsync",BindingFlags.Instance|BindingFlags.NonPublic)!;
        try { await (Task)method.Invoke(router,["api/test",cancelled.Token])!;throw new Exception("取消应传递给轮询"); }
        catch(OperationCanceledException) { Console.WriteLine("PASS intentional polling cancellation is not reported as connection error"); }
        return 0;
    }
}
