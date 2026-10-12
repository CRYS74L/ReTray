using System.Reflection;
using System.Windows.Media;
using System.Windows.Threading;

namespace TrayAssistant_App.Controls;

internal static class WpfRenderSynchronization
{
    // Same narrow WPF scene/present fence used by the LAN tool. Used only for
    // system window transitions; ordinary resizing stays with HwndTarget.
    private static readonly Type MediaContextType = typeof(CompositionTarget).Assembly
        .GetType("System.Windows.Media.MediaContext", throwOnError: true)!;
    private static readonly MethodInfo FromMethod = MediaContextType.GetMethod("From",
        BindingFlags.Static | BindingFlags.NonPublic, null, [typeof(Dispatcher)], null)
        ?? throw new MissingMethodException(MediaContextType.FullName, "From");
    private static readonly Type CompositionTargetType = typeof(CompositionTarget).Assembly
        .GetType("System.Windows.Media.ICompositionTarget", throwOnError: true)!;
    private static readonly MethodInfo ResizeMethod = MediaContextType.GetMethod("Resize",
        BindingFlags.Instance | BindingFlags.NonPublic, null, [CompositionTargetType], null)
        ?? throw new MissingMethodException(MediaContextType.FullName, "Resize");
    private static readonly MethodInfo CompleteRenderMethod = MediaContextType.GetMethod("CompleteRender",
        BindingFlags.Instance | BindingFlags.NonPublic, null, Type.EmptyTypes, null)
        ?? throw new MissingMethodException(MediaContextType.FullName, "CompleteRender");

    internal static void CompletePendingRender()
    {
        var dispatcher = Dispatcher.CurrentDispatcher;
        dispatcher.VerifyAccess();
        var context = FromMethod.Invoke(null, [dispatcher]);
        ResizeMethod.Invoke(context, [null]);
        CompleteRenderMethod.Invoke(context, null);
    }
}
