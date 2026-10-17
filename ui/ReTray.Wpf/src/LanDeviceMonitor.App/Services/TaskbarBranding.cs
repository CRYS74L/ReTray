using System;
using System.IO;
using System.Runtime.InteropServices;

namespace LanDeviceMonitor.Services;

// Windows uses the package's plate icon unless the window supplies an explicit
// taskbar identity and relaunch artwork. Keep the package/EXE identity intact.
public static class TaskbarBranding
{
    public const string WindowAppId = "CRYS74L.ReTray.Window";
    private static readonly Guid AppModel = new("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3");

    public static string CreateRelaunchCommand(string directory, string executable, string activationId)
    {
        if (string.IsNullOrEmpty(activationId)) return "\"" + executable + "\"";
        var launcher = Path.Combine(directory, "ReTray.Startup.exe");
        if (!File.Exists(launcher)) throw new FileNotFoundException("ReTray package launcher is missing.", launcher);
        return "\"" + launcher + "\" \"" + activationId + "\" --show";
    }

    public static void Apply(nint window, string iconPath, string relaunchCommand)
    {
        GetWindowThreadProcessId(window, out var processId);
        if (processId != Environment.ProcessId)
            throw new ArgumentException("Taskbar branding requires a window owned by this process.", nameof(window));
        var iid = typeof(IPropertyStore).GUID;
        Marshal.ThrowExceptionForHR(SHGetPropertyStoreForWindow(window, ref iid, out var store));
        try
        {
            Set(store, 2, relaunchCommand);
            Set(store, 3, iconPath + ",0");
            Set(store, 4, "ReTray");
            // Set the ID last: this notifies the taskbar to refresh the group.
            Set(store, 5, WindowAppId);
            Marshal.ThrowExceptionForHR(store.Commit());
        }
        finally { Marshal.ReleaseComObject(store); }
    }

    public static string? Read(nint window, uint propertyId)
    {
        var iid = typeof(IPropertyStore).GUID;
        Marshal.ThrowExceptionForHR(SHGetPropertyStoreForWindow(window, ref iid, out var store));
        try
        {
            var key = new PropertyKey { Format = AppModel, Id = propertyId };
            Marshal.ThrowExceptionForHR(store.GetValue(ref key, out var value));
            try { return value.Type == 31 ? Marshal.PtrToStringUni(value.Text) : null; }
            finally { PropVariantClear(ref value); }
        }
        finally { Marshal.ReleaseComObject(store); }
    }

    private static void Set(IPropertyStore store, uint id, string text)
    {
        var key = new PropertyKey { Format = AppModel, Id = id };
        var value = new PropVariant { Type = 31, Text = Marshal.StringToCoTaskMemUni(text) };
        try { Marshal.ThrowExceptionForHR(store.SetValue(ref key, ref value)); }
        finally { PropVariantClear(ref value); }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PropertyKey { public Guid Format; public uint Id; }
    [StructLayout(LayoutKind.Explicit, Size = 24)]
    private struct PropVariant
    {
        [FieldOffset(0)] public ushort Type;
        [FieldOffset(8)] public nint Text;
    }
    [ComImport, Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetAt(uint index, out PropertyKey key);
        [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
        [PreserveSig] int SetValue(ref PropertyKey key, ref PropVariant value);
        [PreserveSig] int Commit();
    }
    [DllImport("shell32.dll")]
    private static extern int SHGetPropertyStoreForWindow(nint window, ref Guid iid,
        [MarshalAs(UnmanagedType.Interface)] out IPropertyStore store);
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint window, out int processId);
    [DllImport("ole32.dll")]
    private static extern int PropVariantClear(ref PropVariant value);
}
