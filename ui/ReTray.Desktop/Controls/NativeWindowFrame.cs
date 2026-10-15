using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ReTray_App.Controls;

internal sealed class NativeWindowFrame : IDisposable
{
    internal const int DwmCompositionChanged = 0x031E;
    private const int GwlStyle = -16, WmStyleChanging = 0x007C, WmSysCommand = 0x0112;
    private const uint WsMaximizeBox = 0x00010000, WsSystemMenu = 0x00080000, ScMaximize = 0xF030;
    private const uint SystemCaptionButtons = WsSystemMenu | WsMaximizeBox;
    private const int DwmImmersiveDarkMode = 20, DwmCornerPreference = 33, DwmSystemBackdrop = 38, DwmCloak = 13;
    private readonly uint threadId;
    private bool ownsCloak, disposed;
    public nint Handle { get; }

    public NativeWindowFrame(nint handle)
    {
        Handle = handle;
        threadId = GetCurrentThreadId();
        if (!IsOwnedWindow) throw new InvalidOperationException("The material frame requires a window on its own UI thread.");
    }

    private bool IsOwnedWindow => !disposed && IsWindow(Handle) && GetCurrentThreadId() == threadId &&
        GetWindowThreadProcessId(Handle, out var process) == threadId && process == (uint)Environment.ProcessId;

    public void PrepareFirstShow()
    {
        if (!IsOwnedWindow) return;
        ownsCloak = SetDwmAttribute(DwmCloak, 1);
        try { RemoveSystemCaptionButtons(); }
        catch
        {
            if (ownsCloak) SetDwmAttribute(DwmCloak, 0);
            ownsCloak = false;
            throw;
        }
    }

    public bool ApplyTheme(bool dark,bool refreshFrame=false)
    {
        if (!IsOwnedWindow) return false;
        SetDwmAttribute(DwmImmersiveDarkMode, dark ? 1 : 0);
        SetDwmAttribute(DwmCornerPreference, 2); // DWMWCP_ROUND
        // Same persistent Acrylic policy as LanDeviceMonitor.MicaWindowService.
        // The native system backdrop must be disabled before installing AccentPolicy.
        if(OperatingSystem.IsWindowsVersionAtLeast(10,0,22621))SetDwmAttribute(DwmSystemBackdrop,1);
        var margins = new NativeMargins { Left = -1, Right = -1, Top = -1, Bottom = -1 };
        var result = DwmExtendFrameIntoClientArea(Handle, ref margins);
        if (result < 0) Debug.WriteLine($"DWM glass preparation failed: 0x{result:X8}");
        if(!OperatingSystem.IsWindowsVersionAtLeast(10,0,17763))return false;
        var policy=new AccentPolicy
        {
            AccentState=4, // AcrylicBlurBehind
            AccentFlags=2, // DrawAllBorders
            GradientColor=dark?unchecked((int)0x30202020):unchecked((int)0x14F5F5F5)
        };
        var pointer=Marshal.AllocHGlobal(Marshal.SizeOf<AccentPolicy>());
        bool applied;
        try
        {
            Marshal.StructureToPtr(policy,pointer,false);
            var data=new WindowCompositionAttributeData{Attribute=19,Data=pointer,SizeOfData=Marshal.SizeOf<AccentPolicy>()};
            applied=SetWindowCompositionAttribute(Handle,ref data);
        }
        finally{Marshal.FreeHGlobal(pointer);}
        if(!applied)Debug.WriteLine($"Persistent Acrylic failed: {Marshal.GetLastWin32Error()}");
        if(applied&&refreshFrame)RefreshWindowFrame();
        return applied&&result>=0;
    }

    public bool Reveal()
    {
        if (!IsOwnedWindow) return false;
        if (!ownsCloak) return true;
        // This fence is only for the first show. Interactive resizing uses WPF's
        // own render-thread synchronization and never calls this helper.
        DwmFlush();
        if (!SetDwmAttribute(DwmCloak, 0)) return false;
        ownsCloak = false;
        return true;
    }

    public bool MaintainCustomCaption(int message, nint wparam, nint lparam)
    {
        if (!IsOwnedWindow) return false;
        if (message == WmSysCommand && ((nuint)wparam & 0xFFF0) == ScMaximize) return true;
        if (message == WmStyleChanging && unchecked((int)wparam) == GwlStyle && lparam != 0)
        {
            var style = Marshal.PtrToStructure<NativeStyle>(lparam);
            // Keep WS_CAPTION/WS_THICKFRAME for native material and resizing, but
            // do not let DWM draw the standard caption buttons beneath our own.
            style.NewStyle &= ~SystemCaptionButtons;
            Marshal.StructureToPtr(style, lparam, false);
        }
        return false;
    }

    private void RefreshWindowFrame()
    {
        if(!IsOwnedWindow)return;
        const uint frameFlags=0x0001|0x0002|0x0004|0x0010|0x0020;
        SetWindowPos(Handle,0,0,0,0,0,frameFlags);
        // RDW_INVALIDATE | RDW_UPDATENOW | RDW_FRAME | RDW_ALLCHILDREN
        RedrawWindow(Handle,0,0,0x0001|0x0100|0x0400|0x0080);
        DwmFlush();
    }

    private void RemoveSystemCaptionButtons()
    {
        Marshal.SetLastPInvokeError(0);
        var style = GetWindowLong(Handle, GwlStyle);
        var error = Marshal.GetLastPInvokeError();
        if (style == 0 && error != 0) throw new Win32Exception(error);
        var desired = unchecked((uint)style) & ~SystemCaptionButtons;
        if (desired == unchecked((uint)style)) return;
        Marshal.SetLastPInvokeError(0);
        var previous = IntPtr.Size == 8
            ? SetWindowLongPtr64(Handle, GwlStyle, unchecked((nint)(int)desired))
            : (nint)SetWindowLong32(Handle, GwlStyle, unchecked((int)desired));
        error = Marshal.GetLastPInvokeError();
        if (previous == 0 && error != 0) throw new Win32Exception(error);
        const uint refreshFrame = 0x0001 | 0x0002 | 0x0004 | 0x0010 | 0x0020;
        if (!SetWindowPos(Handle, 0, 0, 0, 0, 0, refreshFrame)) throw new Win32Exception(Marshal.GetLastPInvokeError());
    }

    private bool SetDwmAttribute(int attribute, int value)
    {
        var result = DwmSetWindowAttribute(Handle, attribute, ref value, sizeof(int));
        if (result < 0) Debug.WriteLine($"DWM attribute {attribute} failed: 0x{result:X8}");
        return result >= 0;
    }

    public void Dispose()
    {
        if (disposed) return;
        if (ownsCloak && IsOwnedWindow) SetDwmAttribute(DwmCloak, 0);
        ownsCloak = false;
        disposed = true;
    }

    [StructLayout(LayoutKind.Sequential)] private struct NativeMargins { public int Left, Right, Top, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct NativeStyle { public uint OldStyle, NewStyle; }
    [StructLayout(LayoutKind.Sequential)] private struct AccentPolicy { public int AccentState,AccentFlags,GradientColor,AnimationId; }
    [StructLayout(LayoutKind.Sequential)] private struct WindowCompositionAttributeData { public int Attribute;public nint Data;public int SizeOfData; }
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] private static extern bool IsWindow(nint handle);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint handle, out uint process);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
    private static extern int GetWindowLong(nint handle, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern nint SetWindowLongPtr64(nint handle, int index, nint value);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    private static extern int SetWindowLong32(nint handle, int index, int value);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(nint handle, nint insertAfter, int x, int y, int width, int height, uint flags);
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(nint handle, int attribute, ref int value, int size);
    [DllImport("user32.dll",SetLastError=true)]
    [return:MarshalAs(UnmanagedType.Bool)]private static extern bool SetWindowCompositionAttribute(nint handle,ref WindowCompositionAttributeData data);
    [DllImport("user32.dll")]
    [return:MarshalAs(UnmanagedType.Bool)]private static extern bool RedrawWindow(nint handle,nint rectangle,nint region,uint flags);
    [DllImport("dwmapi.dll")] private static extern int DwmExtendFrameIntoClientArea(nint handle, ref NativeMargins margins);
    [DllImport("dwmapi.dll")] private static extern int DwmFlush();
}
