using System.Windows.Threading;
using LanDeviceMonitor;

internal static class TestAppFactory
{
    // WPF queues OnStartup from Application's constructor. Visual tests must
    // not start the real monitor, claim its mutex, or signal the installed app.
    internal static App Create()
    {
        var pending = new List<DispatcherOperation>();
        DispatcherHookEventHandler posted = (_, e) => pending.Add(e.Operation);
        var dispatcher = Dispatcher.CurrentDispatcher;
        dispatcher.Hooks.OperationPosted += posted;
        App app;
        try { app = new App(); }
        finally { dispatcher.Hooks.OperationPosted -= posted; }
        foreach (var operation in pending) operation.Abort();
        return app;
    }
}
