using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using System.Runtime.InteropServices;

namespace TrayAssistant_App.Controls;

public static class WindowEntrance
{
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(nint window, int attribute, ref int value, int size);
    public static void Prepare(Window window)
    {
        var handle = WinRT.Interop.WindowNative.GetWindowHandle(window);
        int enabled = 1;
        DwmSetWindowAttribute(handle, 3, ref enabled, sizeof(int));
        DwmSetWindowAttribute(handle, 13, ref enabled, sizeof(int));
    }
    public static async Task RevealAsync(Window window, FrameworkElement root)
    {
        var ready = new TaskCompletionSource();
        var visual = ElementCompositionPreview.GetElementVisual(root);
        visual.Opacity = 0;
        bool closed = false;
        int frames = 0;
        void Rendering(object? sender, object args) { if (root.IsLoaded && ++frames >= 2) ready.TrySetResult(); }
        void Closed(object sender, WindowEventArgs args) { closed = true; ready.TrySetResult(); }
        window.Closed += Closed;
        CompositionTarget.Rendering += Rendering;
        try { await Task.WhenAny(ready.Task, Task.Delay(500)); }
        finally { CompositionTarget.Rendering -= Rendering; window.Closed -= Closed; }
        if (closed) return;
        int disabled = 0;
        DwmSetWindowAttribute(WinRT.Interop.WindowNative.GetWindowHandle(window), 13, ref disabled, sizeof(int));
        var fade = visual.Compositor.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(0, 0); fade.InsertKeyFrame(1, 1);
        fade.Duration = TimeSpan.FromMilliseconds(120);
        visual.StartAnimation("Opacity", fade);
    }
}
