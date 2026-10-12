namespace LanDeviceMonitor.Services;

public sealed class SingleInstanceService : IDisposable
{
    private const string InstanceEventName = @"Local\TrayIconAssistant.UI.6A40A512-75FB-4B96-8DA9-9F09BE6B8254";
    private const string ActivationEventName = @"Local\TrayIconAssistant.UI.Activation.6A40A512";
    private readonly EventWaitHandle _instanceEvent;
    private readonly EventWaitHandle _activationEvent;
    private readonly CancellationTokenSource _cancellation = new();
    private Task? _listenerTask;

    public SingleInstanceService()
    {
        _instanceEvent = new EventWaitHandle(
            initialState: false,
            EventResetMode.ManualReset,
            InstanceEventName,
            out var createdNew);
        _activationEvent = new EventWaitHandle(
            initialState: false,
            EventResetMode.AutoReset,
            ActivationEventName);
        IsPrimary = createdNew;
    }

    public bool IsPrimary { get; }

    public void StartListening(Action activationRequested)
    {
        if (!IsPrimary || _listenerTask is not null)
        {
            return;
        }

        _listenerTask = Task.Run(() =>
            Listen(activationRequested, _cancellation.Token));
    }

    public static Task SignalPrimaryAsync()
    {
        try
        {
            using var activationEvent =
                EventWaitHandle.OpenExisting(ActivationEventName);
            activationEvent.Set();
        }
        catch (WaitHandleCannotBeOpenedException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        return Task.CompletedTask;
    }

    private void Listen(
        Action activationRequested,
        CancellationToken cancellationToken)
    {
        var handles = new WaitHandle[]
        {
            _activationEvent,
            cancellationToken.WaitHandle
        };

        while (!cancellationToken.IsCancellationRequested)
        {
            var signaled = WaitHandle.WaitAny(handles);
            if (signaled == 1 || cancellationToken.IsCancellationRequested)
            {
                break;
            }

            activationRequested();
        }
    }

    public void Dispose()
    {
        _cancellation.Cancel();
        _activationEvent.Set();
        _cancellation.Dispose();
        _activationEvent.Dispose();
        _instanceEvent.Dispose();
    }
}
