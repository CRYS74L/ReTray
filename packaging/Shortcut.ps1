# Shared by the installer and development deployment. Target the package's
# native activation launcher, so Explorer's "Open file location" opens ReTray.
function Write-ReTrayDesktopShortcut {
    param(
        [Parameter(Mandatory)][string]$InstallLocation,
        [Parameter(Mandatory)][string]$PackageFamilyName,
        [string]$ShortcutPath=(Join-Path ([Environment]::GetFolderPath('Desktop')) 'ReTray.lnk'),
        [string]$BrandDirectory=(Join-Path $env:LOCALAPPDATA 'ReTray/Branding')
    )
    $ErrorActionPreference='Stop'
    if($PackageFamilyName -notmatch '^CRYS74L\.ReTray_[A-Za-z0-9]+$') { throw 'Unexpected ReTray package family' }
    $launcher=Join-Path $InstallLocation 'ReTray.Startup.exe'
    $icon=Join-Path $InstallLocation 'Assets/DesktopIcon.ico'
    foreach($required in @($launcher,$icon)) {
        if(!(Test-Path -LiteralPath $required -PathType Leaf)) { throw "ReTray shortcut payload is missing $required" }
    }
    $shell=New-Object -ComObject WScript.Shell
    if(Test-Path -LiteralPath $ShortcutPath) {
        $existing=$shell.CreateShortcut($ShortcutPath)
        if($existing.Arguments -notmatch 'CRYS74L\.ReTray' -and
           [IO.Path]::GetFileName($existing.TargetPath) -notin @('ReTray.exe','ReTray.App.exe','ReTray.Startup.exe')) {
            throw 'An unrelated ReTray desktop shortcut already exists; it was preserved.'
        }
    }
    New-Item -ItemType Directory -Path $BrandDirectory -Force | Out-Null
    $desktopIcon=Join-Path $BrandDirectory 'DesktopIcon.ico'
    Copy-Item -LiteralPath $icon -Destination $desktopIcon -Force
    $shortcut=$shell.CreateShortcut($ShortcutPath)
    $shortcut.TargetPath=$launcher
    $shortcut.Arguments='"'+$PackageFamilyName+'!App" --show'
    $shortcut.WorkingDirectory=$InstallLocation
    $shortcut.IconLocation=$desktopIcon+',0'
    $shortcut.Description='ReTray — Redefine Your Tray'
    $shortcut.Save()
    # Explicitly identify the link with the same group as the settings window;
    # never let shell heuristics brand the executable of an unrelated host.
    if(-not ('ReTray.Packaging.ShortcutIdentity' -as [type])) {
        Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
namespace ReTray.Packaging {
    public static class ShortcutIdentity {
        [StructLayout(LayoutKind.Sequential)] private struct Key { public Guid Format; public uint Id; }
        [StructLayout(LayoutKind.Explicit, Size=24)] private struct Value {
            [FieldOffset(0)] public ushort Type;
            [FieldOffset(8)] public IntPtr Text;
        }
        [ComImport, Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface Store {
            [PreserveSig] int GetCount(out uint count);
            [PreserveSig] int GetAt(uint index, out Key key);
            [PreserveSig] int GetValue(ref Key key, out Value value);
            [PreserveSig] int SetValue(ref Key key, ref Value value);
            [PreserveSig] int Commit();
        }
        [DllImport("shell32.dll", CharSet=CharSet.Unicode)]
        private static extern int SHGetPropertyStoreFromParsingName(string path, IntPtr bind, uint flags, ref Guid iid, out Store store);
        [DllImport("ole32.dll")] private static extern int PropVariantClear(ref Value value);
        [DllImport("shell32.dll", CharSet=CharSet.Unicode)]
        private static extern void SHChangeNotify(uint eventId, uint flags, string item, IntPtr other);
        private static Key AppIdKey() { return new Key { Format=new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), Id=5 }; }
        public static void Write(string path, string id) {
            var iid=typeof(Store).GUID; Store store;
            Marshal.ThrowExceptionForHR(SHGetPropertyStoreFromParsingName(path, IntPtr.Zero, 2, ref iid, out store));
            try {
                var key=AppIdKey(); var value=new Value { Type=31, Text=Marshal.StringToCoTaskMemUni(id) };
                try { Marshal.ThrowExceptionForHR(store.SetValue(ref key, ref value)); Marshal.ThrowExceptionForHR(store.Commit()); }
                finally { PropVariantClear(ref value); }
            } finally { Marshal.ReleaseComObject(store); }
            // Refresh only this completed shortcut, including an old link whose
            // target was Explorer. Leave global icon caches and windows alone.
            SHChangeNotify(0x00002000, 0x0005 | 0x1000, path, IntPtr.Zero);
        }
        public static string Read(string path) {
            var iid=typeof(Store).GUID; Store store;
            Marshal.ThrowExceptionForHR(SHGetPropertyStoreFromParsingName(path, IntPtr.Zero, 0, ref iid, out store));
            try {
                var key=AppIdKey(); Value value; Marshal.ThrowExceptionForHR(store.GetValue(ref key, out value));
                try { return value.Type==31 ? Marshal.PtrToStringUni(value.Text) : null; }
                finally { PropVariantClear(ref value); }
            } finally { Marshal.ReleaseComObject(store); }
        }
    }
}
'@
    }
    [ReTray.Packaging.ShortcutIdentity]::Write($ShortcutPath,'CRYS74L.ReTray.Window')
}
