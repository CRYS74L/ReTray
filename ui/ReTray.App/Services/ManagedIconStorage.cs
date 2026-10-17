using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Text.Json.Nodes;

namespace ReTray_App.Services;

// Pickers and configuration import must share the same host-owned asset root.
public static class ManagedIconStorage
{
    public static string DirectoryPath =>
#if TRAY_WPF
        LanDeviceMonitor.Services.ApplicationPaths.ManagedAssetsDirectory;
#else
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ReTray", "Assets");
#endif

    public static async Task<string> StoreAsync(string source)
    {
        await using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true);
        var bytes = await ConfigurationArchive.ReadBoundedAsync(input, input.Length, ConfigurationArchive.MaxAssetBytes);
        var directory = DirectoryPath;
        if (Directory.Exists(directory) && (File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("图标资源目录不能为重解析点。");
        Directory.CreateDirectory(directory);
        var hash = Convert.ToHexString(SHA256.HashData(bytes));
        var target = Path.Combine(directory, hash + Path.GetExtension(source).ToLowerInvariant());
        if (!File.Exists(target))
        {
            var temporary = Path.Combine(directory, "." + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                await File.WriteAllBytesAsync(temporary, bytes);
                try { File.Move(temporary, target); }
                catch (IOException) when (File.Exists(target)) { }
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        if ((File.GetAttributes(target) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("现有图标资源校验失败，已保留原文件：" + target);
        await using var existing = new FileStream(target, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true);
        var saved = await ConfigurationArchive.ReadBoundedAsync(existing, existing.Length, ConfigurationArchive.MaxAssetBytes);
        if (!Convert.ToHexString(SHA256.HashData(saved)).Equals(hash, StringComparison.Ordinal))
            throw new IOException("现有图标资源校验失败，已保留原文件：" + target);
        return target;
    }

    public static async Task<JsonObject> ImportAsync(ConfigurationImport package)
    {
        var candidate = await package.InstallAssetsAsync(DirectoryPath);
        // Legacy JSON imports contain external paths instead of archive assets.
        // Materialize these too; importing must never leave a source dependency.
        foreach (var (owner, key) in ConfigurationArchive.AssetFields(candidate))
        {
            var source = ConfigurationArchive.AssetValue(owner, key);
            if (source.Length != 0) owner[key] = await StoreAsync(source);
        }
        return candidate;
    }
}
