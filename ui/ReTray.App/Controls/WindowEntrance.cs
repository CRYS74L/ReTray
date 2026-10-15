using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using System.Runtime.InteropServices;

namespace ReTray_App.Controls;

public static class WindowEntrance
{
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(nint window, int attribute, ref int value, int size);
    [DllImport("dwmapi.dll")] private static extern int DwmFlush();
    public static void Prepare(Window window)
    {
        var handle = WinRT.Interop.WindowNative.GetWindowHandle(window);
        int enabled = 1;
        DwmSetWindowAttribute(handle, 3, ref enabled, sizeof(int));
        DwmSetWindowAttribute(handle, 13, ref enabled, sizeof(int));
    }
    public static async Task RevealAsync(Window window, FrameworkElement root)
    {
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        bool closed = false;
        int frames = 0;
        void Rendered(object? sender, RenderedEventArgs args) { if (root.IsLoaded && ++frames >= 2) ready.TrySetResult(); }
        void Closed(object sender, WindowEventArgs args) { closed = true; ready.TrySetResult(); }
        window.Closed += Closed;
        CompositionTarget.Rendered += Rendered;
        try { await Task.WhenAny(ready.Task, Task.Delay(500)); }
        finally { CompositionTarget.Rendered -= Rendered; }
        if (!closed) await ElementCompositionPreview.GetElementVisual(root).Compositor.RequestCommitAsync();
        if (!closed) DwmFlush();
        window.Closed -= Closed;
        if (closed) return;
        int disabled = 0;
        DwmSetWindowAttribute(WinRT.Interop.WindowNative.GetWindowHandle(window), 13, ref disabled, sizeof(int));
    }
}
