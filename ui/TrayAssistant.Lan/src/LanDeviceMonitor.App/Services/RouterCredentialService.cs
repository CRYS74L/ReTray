using System.ComponentModel;
using System.Runtime.InteropServices;

namespace LanDeviceMonitor.Services;

public sealed class RouterCredentialService
{
    private const int CredentialTypeGeneric = 1;
    private const int CredentialPersistLocalMachine = 2;

    public void SavePassword(string routerAddress, string password)
    {
        var target = GetTarget(routerAddress);
        var passwordBytes = System.Text.Encoding.Unicode.GetBytes(password);
        var blob = Marshal.AllocCoTaskMem(passwordBytes.Length);

        try
        {
            Marshal.Copy(passwordBytes, 0, blob, passwordBytes.Length);
            var credential = new NativeCredential
            {
                Type = CredentialTypeGeneric,
                TargetName = target,
                CredentialBlobSize = passwordBytes.Length,
                CredentialBlob = blob,
                Persist = CredentialPersistLocalMachine,
                UserName = "admin"
            };

            if (!CredWrite(ref credential, 0))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "无法保存路由器密码到 Windows 凭据管理器。");
            }
        }
        finally
        {
            if (passwordBytes.Length > 0)
            {
                Marshal.Copy(new byte[passwordBytes.Length], 0, blob, passwordBytes.Length);
            }

            Marshal.FreeCoTaskMem(blob);
            Array.Clear(passwordBytes);
        }
    }

    public string? ReadPassword(string routerAddress)
    {
        if (!CredRead(GetTarget(routerAddress), CredentialTypeGeneric, 0, out var credentialPointer))
        {
            var error = Marshal.GetLastWin32Error();
            return error == 1168 ? null : throw new Win32Exception(error, "无法读取 Windows 凭据管理器。");
        }

        try
        {
            var credential = Marshal.PtrToStructure<NativeCredential>(credentialPointer);
            return credential.CredentialBlob == IntPtr.Zero || credential.CredentialBlobSize == 0
                ? string.Empty
                : Marshal.PtrToStringUni(credential.CredentialBlob, credential.CredentialBlobSize / 2);
        }
        finally
        {
            CredFree(credentialPointer);
        }
    }

    public bool HasPassword(string routerAddress)
    {
        if (!CredRead(GetTarget(routerAddress), CredentialTypeGeneric, 0, out var pointer))
        {
            var error = Marshal.GetLastWin32Error();
            return error == 1168 ? false : throw new Win32Exception(error, "无法读取已保存凭据状态。");
        }
        try { return Marshal.PtrToStructure<NativeCredential>(pointer).CredentialBlobSize > 0; }
        finally { CredFree(pointer); }
    }

    private static string GetTarget(string routerAddress) =>
        $"LanDeviceMonitor.Router.{routerAddress.Trim().ToLowerInvariant()}";

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeCredential
    {
        public int Flags;
        public int Type;
        public string TargetName;
        public string? Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public int CredentialBlobSize;
        public IntPtr CredentialBlob;
        public int Persist;
        public int AttributeCount;
        public IntPtr Attributes;
        public string? TargetAlias;
        public string UserName;
    }

    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredWrite(ref NativeCredential userCredential, uint flags);

    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredRead(string target, int type, int reservedFlag, out IntPtr credentialPtr);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern void CredFree(IntPtr buffer);
}
