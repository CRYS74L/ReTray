using System.Runtime.InteropServices;

namespace LanDeviceMonitor.Services;

// Preserve Windows' invisible maximized resize frame, but keep the custom
// client surface inside the monitor work area, like a native captioned window.
internal static class NativeMaximizeBounds
{
    internal static bool ConstrainClient(IntPtr window,IntPtr parameter)
    {
        if(!IsZoomed(window)) return false;
        var info=new MonitorInfo {Size=Marshal.SizeOf<MonitorInfo>()};
        if(!GetMonitorInfo(MonitorFromWindow(window,2),ref info)) return false;
        // The first RECT is the proposed client rectangle in both forms of
        // WM_NCCALCSIZE. The remaining rectangles belong to Windows.
        Marshal.StructureToPtr(info.Work,parameter,false);
        return true;
    }
    [StructLayout(LayoutKind.Sequential)] private struct Rect {public int Left,Top,Right,Bottom;}
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo {public int Size;public Rect Monitor,Work;public uint Flags;}
    [DllImport("user32.dll")] private static extern bool IsZoomed(IntPtr window);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr window,uint flags);
    [DllImport("user32.dll",CharSet=CharSet.Auto)] private static extern bool GetMonitorInfo(IntPtr monitor,ref MonitorInfo info);
}
