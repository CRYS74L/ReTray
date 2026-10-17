using System.IO;
using System.Text.Json.Nodes;
using System.Security.Cryptography;
using System.Diagnostics.CodeAnalysis;

namespace LanDeviceMonitor.Services;

public static class ApplicationPaths
{
#if RETRAY_PORTABLE
    public static bool IsPortable => true;
#else
    public static bool IsPortable => false;
#endif
    public static string ExecutableDirectory => Path.GetDirectoryName(Environment.ProcessPath!)!;
    public static string DataDirectory => IsPortable ? Path.Combine(ExecutableDirectory, "Data") :
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ReTray");
    public static string ManagedAssetsDirectory => Path.Combine(DataDirectory, "Assets");

    // Windows App SDK single-file publishing requires IncludeAllContentForSelfExtract;
    // the extracted assembly directory holds native libraries and physical content.
    [UnconditionalSuppressMessage("SingleFile", "IL3000", Justification = "Portable publishing requires IncludeAllContentForSelfExtract and validates the extracted assembly path.")]
    public static string ResourceDirectory => Path.GetDirectoryName(typeof(ApplicationPaths).Assembly.Location)
        ?? throw new IOException("无法确定 ReTray 运行资源的位置。");

    public static void PrepareData()
    {
        if (!IsPortable) return;
        var target = DataDirectory;
        // Fresh portable copies have an independent profile. Upgrades retain
        // this Data directory; installed-version migration uses configuration
        // import so a stale global/virtualized profile cannot win silently.
        Directory.CreateDirectory(target);
        RebaseManagedAssets(target);
        var branding = Path.Combine(target, "Branding");
        Directory.CreateDirectory(branding);
        foreach (var name in new[] { "DesktopIcon.ico", "WindowIcon.ico" })
        {
            var bytes = File.ReadAllBytes(Path.Combine(ResourceDirectory, "Assets", name));
            var destination = Path.Combine(branding, name);
            if (!File.Exists(destination) || !File.ReadAllBytes(destination).AsSpan().SequenceEqual(bytes))
                File.WriteAllBytes(destination, bytes);
        }
    }

    // Only content-addressed managed image references are relocated. Application
    // executable selectors and arbitrary external image paths are left intact.
    public static void RebaseManagedAssets(string directory, string? legacyAssetsDirectory = null)
    {
        var legacy = Path.GetFullPath(legacyAssetsDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ReTray", "Assets"));
        foreach (var name in new[] { "settings.json", "settings.json.bak" })
        {
            var path = Path.Combine(directory, name);
            if (!File.Exists(path)) continue;
            JsonNode? value;
            try { value = JsonNode.Parse(File.ReadAllText(path)); }
            catch (System.Text.Json.JsonException) { continue; } // Native loader recovers from backup.
            bool changed = false;
            void Visit(JsonNode? node)
            {
                if (node is JsonObject obj)
                    foreach (var (key, child) in obj.ToArray())
                    {
                        if (key is "asset" or "replacementAsset" && child is JsonValue scalar && scalar.TryGetValue<string>(out var original) && original.Length != 0)
                        {
                            var file = Path.GetFileName(original);
                            var hash = Path.GetFileNameWithoutExtension(file);
                            var destination = Path.Combine(directory, "Assets", file);
                            if (hash.Length == 64 && hash.All(Uri.IsHexDigit) && !File.Exists(destination) &&
                                Path.IsPathFullyQualified(original) && File.Exists(original) &&
                                string.Equals(Path.GetDirectoryName(Path.GetFullPath(original)), legacy, StringComparison.OrdinalIgnoreCase))
                            {
                                using var input = new FileStream(original, FileMode.Open, FileAccess.Read, FileShare.Read);
                                if (input.Length <= 32 * 1024 * 1024)
                                {
                                    var bytes = new byte[(int)input.Length];
                                    input.ReadExactly(bytes);
                                    if (Convert.ToHexString(SHA256.HashData(bytes)).Equals(hash, StringComparison.OrdinalIgnoreCase))
                                    {
                                        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                                        var temporary = destination + ".migration-" + Guid.NewGuid().ToString("N");
                                        try { File.WriteAllBytes(temporary, bytes); File.Move(temporary, destination); }
                                        finally { if (File.Exists(temporary)) File.Delete(temporary); }
                                    }
                                }
                            }
                            if (hash.Length == 64 && hash.All(Uri.IsHexDigit) && File.Exists(destination) &&
                                Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(destination))).Equals(hash, StringComparison.OrdinalIgnoreCase) &&
                                !string.Equals(original, destination, StringComparison.OrdinalIgnoreCase))
                            { obj[key] = destination; changed = true; }
                        }
                        else Visit(child);
                    }
                else if (node is JsonArray array) foreach (var child in array) Visit(child);
            }
            Visit(value);
            if (value is JsonObject root && root["uiActivation"] is JsonValue activation &&
                activation.TryGetValue<string>(out var identity) && identity.Length > 0)
            { root["uiActivation"] = ""; changed = true; }
            if (changed)
            {
                var temporary = path + ".portable-" + Environment.ProcessId;
                File.WriteAllText(temporary, value!.ToJsonString(new() { WriteIndented = true }));
                File.Move(temporary, path, overwrite: true);
            }
        }
    }

    // Only caches bearing our marker are eligible. Keep the active bundle and
    // the newest other marked bundle; leave unrelated .NET applications alone.
    public static void PruneOldBundleCaches()
    {
        if (!IsPortable) return;
        try
        {
            var current = Path.GetFullPath(ResourceDirectory);
            var parent = Directory.GetParent(current);
            if (parent?.Parent?.Name != ".net" ||
                !parent.Name.Equals(Path.GetFileNameWithoutExtension(Environment.ProcessPath), StringComparison.OrdinalIgnoreCase)) return;
            const string marker = ".retray-bundle-cache-v1";
            File.WriteAllText(Path.Combine(current, marker), typeof(ApplicationPaths).Assembly.GetName().Version!.ToString());
            var old = parent.EnumerateDirectories().Where(d => !d.FullName.Equals(current, StringComparison.OrdinalIgnoreCase) &&
                (d.Attributes & FileAttributes.ReparsePoint) == 0 && File.Exists(Path.Combine(d.FullName, marker)))
                .OrderByDescending(d => File.GetLastWriteTimeUtc(Path.Combine(d.FullName, marker))).Skip(1);
            foreach (var directory in old)
                try
                {
                    if (directory.EnumerateFileSystemInfos("*", SearchOption.AllDirectories)
                        .Any(item => (item.Attributes & FileAttributes.ReparsePoint) != 0)) continue;
                    bool busy = false;
                    foreach (var process in System.Diagnostics.Process.GetProcessesByName(Path.GetFileNameWithoutExtension(Environment.ProcessPath)))
                        using (process)
                        {
                            if (process.Id == Environment.ProcessId) continue;
                            try { busy |= process.Modules.Cast<System.Diagnostics.ProcessModule>().Any(module =>
                                module.FileName.StartsWith(directory.FullName + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)); }
                            catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException) { busy = true; }
                        }
                    if (busy) continue;
                    // Rename first: an interrupted deletion must not leave a
                    // partial cache at the bundle ID that the runtime reuses.
                    var retired = directory.FullName + ".retired-" + Guid.NewGuid().ToString("N");
                    Directory.Move(directory.FullName, retired);
                    Directory.Delete(retired, recursive: true);
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }
}
