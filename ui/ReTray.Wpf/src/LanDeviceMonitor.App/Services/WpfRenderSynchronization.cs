using System.Reflection;
using System.Windows.Media;
using System.Windows.Threading;

namespace LanDeviceMonitor.Services;

internal static class WpfRenderSynchronization
{
    // WPF's UI batch and its render thread are separate from the calling
    // thread's DWM queue. DwmFlush alone cannot drain the pending WPF batch.
    // The bundled .NET 8 WPF runtime uses this same fence for synchronous HWND
    // resizing. Keep this narrow runtime dependency isolated here; it does not
    // replace the renderer or change the normal transparent/Acrylic surface.
    private static readonly Type MediaContextType =
        typeof(CompositionTarget).Assembly.GetType("System.Windows.Media.MediaContext", throwOnError: true)!;
    private static readonly MethodInfo FromMethod =
        MediaContextType.GetMethod("From", BindingFlags.Static | BindingFlags.NonPublic,
            binder: null, types: [typeof(Dispatcher)], modifiers: null)
        ?? throw new MissingMethodException(MediaContextType.FullName, "From");
    private static readonly MethodInfo CompleteRenderMethod =
        MediaContextType.GetMethod("CompleteRender", BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null, types: Type.EmptyTypes, modifiers: null)
        ?? throw new MissingMethodException(MediaContextType.FullName, "CompleteRender");
    private static readonly MethodInfo ResizeMethod =
        MediaContextType.GetMethod("Resize", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingMethodException(MediaContextType.FullName, "Resize");

    internal static void CompletePendingRender()
    {
        var dispatcher = Dispatcher.CurrentDispatcher;
        dispatcher.VerifyAccess();
        var context = FromMethod.Invoke(null, [dispatcher]);
        // Resize(null) is WPF's synchronous scene-render entry point. It cancels
        // deferred render operations and renders the current visual tree now;
        // CompleteRender then drains that exact batch, not an older scene.
        ResizeMethod.Invoke(context, [null]);
        CompleteRenderMethod.Invoke(context, null);
    }
}
