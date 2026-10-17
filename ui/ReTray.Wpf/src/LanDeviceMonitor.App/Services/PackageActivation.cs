using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace LanDeviceMonitor.Services;

// One executable serves package, shortcut, taskbar and extracted-ZIP launches.
// Before touching settings, retain the installed package's storage identity.
public static class PackageActivation
{
    private const string Family = "CRYS74L.ReTray_fh3g15a7mygna";

    public static bool RedirectIfInstalled(string[] arguments)
    {
        uint length = 0;
        if (GetCurrentPackageFullName(ref length, null) == 122) return false;
        var directory = Path.GetDirectoryName(Environment.ProcessPath!)!;
        if (!MatchesInstalledDirectory(directory)) return false;
        var manager = (IApplicationActivationManager)Activator.CreateInstance(
            Type.GetTypeFromCLSID(new Guid("45BA127D-10A8-46EA-8AB7-56EA9078943C"))!)!;
        try
        {
            // Startup and taskbar commands use these two public modes. Restarts
            // already inherit package identity and never enter this branch.
            var mode = arguments.Contains("--background", StringComparer.OrdinalIgnoreCase) ? "--background" : "--show";
            Marshal.ThrowExceptionForHR(manager.ActivateApplication(Family + "!App", mode, 0, out _));
            return true;
        }
        finally { Marshal.ReleaseComObject(manager); }
    }

    public static bool MatchesInstalledDirectory(string directory)
    {
        uint count = 0, length = 0;
        if (GetPackagesByPackageFamily(Family, ref count, 0, ref length, 0) != 122) return false;
        var names = Marshal.AllocHGlobal(checked((int)count * IntPtr.Size));
        var buffer = Marshal.AllocHGlobal(checked((int)length * 2));
        try
        {
            Marshal.ThrowExceptionForHR(HResult(GetPackagesByPackageFamily(Family, ref count, names, ref length, buffer)));
            for (int i = 0; i < count; i++)
            {
                var fullName = Marshal.PtrToStringUni(Marshal.ReadIntPtr(names, i * IntPtr.Size))!;
                uint pathLength = 0;
                if (GetPackagePathByFullName(fullName, ref pathLength, null) != 122) continue;
                var path = new StringBuilder((int)pathLength);
                if (GetPackagePathByFullName(fullName, ref pathLength, path) == 0 &&
                    SameDirectory(directory, path.ToString())) return true;
            }
            return false;
        }
        finally { Marshal.FreeHGlobal(names); Marshal.FreeHGlobal(buffer); }
    }

    public static bool SameDirectory(string first, string second) => string.Equals(
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(first)),
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(second)), StringComparison.OrdinalIgnoreCase);
    private static int HResult(int error) => error == 0 ? 0 : unchecked((int)(0x80070000u | (uint)error));

    [ComImport, Guid("2E941141-7F97-4756-BA1D-9DECDE894A3D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IApplicationActivationManager
    {
        [PreserveSig] int ActivateApplication([MarshalAs(UnmanagedType.LPWStr)] string appId,
            [MarshalAs(UnmanagedType.LPWStr)] string arguments, uint options, out uint processId);
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetCurrentPackageFullName(ref uint length, StringBuilder? fullName);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetPackagesByPackageFamily(string family, ref uint count, nint names, ref uint length, nint buffer);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetPackagePathByFullName(string fullName, ref uint length, StringBuilder? path);
}
