using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace ReTray_App.Services;

/// <summary>A bounded, portable settings package. Only known icon fields are packaged.</summary>
public static class ConfigurationArchive
{
    internal const int MaxAssetBytes = 32 * 1024 * 1024;
    private const int MaxAssetsBytes = 256 * 1024 * 1024;
    private const int MaxSettingsBytes = 2 * 1024 * 1024;
    private const int MaxArchiveBytes = MaxAssetsBytes + MaxSettingsBytes + 4 * 1024 * 1024;
    private const int MaxEntries = 2000;
    private const string Format = "ReTray.Configuration";
    private static readonly HashSet<string> Extensions = new(StringComparer.Ordinal)
        { ".ico", ".png", ".bmp", ".jpg", ".exe", ".dll" };

    public static async Task ExportAsync(string target, JsonObject settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var portable = (JsonObject)settings.DeepClone();
        var assets = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        var sourceNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        long total = 0;
        foreach (var (owner, key) in AssetFields(portable))
        {
            var source = AssetValue(owner, key);
            if (source.Length == 0) continue;
            var sourcePath = Path.GetFullPath(source);
            if (!sourceNames.TryGetValue(sourcePath, out var name))
            {
                var extension = Path.GetExtension(sourcePath).ToLowerInvariant();
                if (!Extensions.Contains(extension)) throw new InvalidDataException("不支持此图标资源类型：" + extension);
                try
                {
                    await using var input = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true);
                    var bytes = await ReadBoundedAsync(input, input.Length, MaxAssetBytes);
                    name = "assets/" + Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant() + extension;
                    if (!assets.ContainsKey(name))
                    {
                        total += bytes.Length;
                        if (total > MaxAssetsBytes || assets.Count >= MaxEntries - 2) throw new InvalidDataException("配置中的图标资源总量超过限制。");
                        assets.Add(name, bytes);
                    }
                }
                catch (FileNotFoundException ex) { throw new IOException("配置引用的图标资源不存在：" + sourcePath, ex); }
                catch (DirectoryNotFoundException ex) { throw new IOException("配置引用的图标资源不存在：" + sourcePath, ex); }
                sourceNames.Add(sourcePath, name);
            }
            owner[key] = name;
        }
        var settingsBytes = JsonSerializer.SerializeToUtf8Bytes(portable);
        if (settingsBytes.Length > MaxSettingsBytes) throw new InvalidDataException("配置 JSON 不能超过 2 MB。");
        var manifestBytes = JsonSerializer.SerializeToUtf8Bytes(new { format = Format, version = 1 });
        var finalPath = Path.GetFullPath(target);
        var temporary = finalPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true))
            {
                using (var archive = new ZipArchive(output, ZipArchiveMode.Create, true))
                {
                    await WriteEntryAsync(archive, "manifest.json", manifestBytes);
                    await WriteEntryAsync(archive, "settings.json", settingsBytes);
                    foreach (var asset in assets) await WriteEntryAsync(archive, asset.Key, asset.Value);
                }
                await output.FlushAsync();
                output.Flush(true);
            }
            File.Move(temporary, finalPath, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public static async Task<ConfigurationImport> ReadAsync(string path)
    {
        await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true);
        if (string.Equals(Path.GetExtension(path), ".json", StringComparison.OrdinalIgnoreCase))
        {
            var settings = ParseObject(await ReadBoundedAsync(input, input.Length, MaxSettingsBytes));
            foreach (var (owner, key) in AssetFields(settings))
            {
                var value = AssetValue(owner, key);
                if (value.StartsWith("assets/", StringComparison.OrdinalIgnoreCase) || value.StartsWith("assets\\", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("JSON 配置引用了未打包的图标资源，请导入完整 .trayconfig 文件。");
            }
            return new ConfigurationImport(settings, new Dictionary<string, byte[]>(StringComparer.Ordinal));
        }
        if (input.Length > MaxArchiveBytes) throw new InvalidDataException("配置包超过大小限制。");
        using var archive = new ZipArchive(input, ZipArchiveMode.Read, true);
        if (archive.Entries.Count > MaxEntries) throw new InvalidDataException("配置包中的文件数量超过限制。");
        var entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.Ordinal);
        long assetBytes = 0;
        foreach (var entry in archive.Entries)
        {
            var name = entry.FullName;
            var limit = name == "manifest.json" ? 4096 : name == "settings.json" ? MaxSettingsBytes : MaxAssetBytes;
            if ((name != "manifest.json" && name != "settings.json" && !ValidAssetName(name)) || !entries.TryAdd(name, entry))
                throw new InvalidDataException("配置包包含非法、重复或多余的文件名称：" + name);
            if (entry.Length < 0 || entry.Length > limit || entry.CompressedLength < 0 || entry.CompressedLength > MaxArchiveBytes)
                throw new InvalidDataException("配置包中的文件超过大小限制：" + name);
            if (name.StartsWith("assets/", StringComparison.Ordinal)) assetBytes += entry.Length;
            if (assetBytes > MaxAssetsBytes) throw new InvalidDataException("配置包的图标资源总量不能超过 256 MB。");
        }
        if (!entries.TryGetValue("manifest.json", out var manifestEntry) || !entries.TryGetValue("settings.json", out var settingsEntry))
            throw new InvalidDataException("配置包缺少格式标记或 settings.json。");
        var manifest = ParseObject(await ReadEntryAsync(manifestEntry, 4096));
        if (manifest["format"] is not JsonValue format || !format.TryGetValue<string>(out var formatText) ||
            (formatText != Format && formatText != "TrayIconAssistant.Configuration") ||
            manifest["version"] is not JsonValue version || !version.TryGetValue<int>(out var versionNumber) || versionNumber != 1)
            throw new InvalidDataException("配置包格式或版本不受支持。");
        var packaged = ParseObject(await ReadEntryAsync(settingsEntry, MaxSettingsBytes));
        var references = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (owner, key) in AssetFields(packaged))
        {
            var name = AssetValue(owner, key);
            if (name.Length == 0) continue;
            if (!ValidAssetName(name) || !entries.ContainsKey(name)) throw new InvalidDataException("配置包缺少引用的图标资源或引用非法：" + name);
            references.Add(name);
        }
        if (entries.Count != references.Count + 2) throw new InvalidDataException("配置包包含未被配置引用的文件。");
        var assets = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var name in references)
        {
            var bytes = await ReadEntryAsync(entries[name], MaxAssetBytes);
            if (!HashMatches(name, bytes)) throw new InvalidDataException("图标资源校验失败：" + name);
            assets.Add(name, bytes);
        }
        return new ConfigurationImport(packaged, assets);
    }

    internal static IEnumerable<(JsonObject Owner, string Key)> AssetFields(JsonObject settings)
    {
        foreach (var group in new[] { "rules", "inputMethodIcons" })
        {
            if (settings[group] == null) continue;
            if (settings[group] is not JsonArray rows) throw new InvalidDataException("配置字段必须为数组：" + group);
            foreach (var row in rows)
            {
                if (row is not JsonObject owner) throw new InvalidDataException("配置图标条目必须为对象。");
                if (owner.ContainsKey("asset")) yield return (owner, "asset");
                if (group == "rules" && owner.ContainsKey("replacementAsset")) yield return (owner, "replacementAsset");
            }
        }
        if (settings["ownIcon"] != null)
        {
            if (settings["ownIcon"] is not JsonObject own) throw new InvalidDataException("ownIcon 必须为对象。");
            if (own.ContainsKey("asset")) yield return (own, "asset");
            if (own.ContainsKey("replacementAsset")) yield return (own, "replacementAsset");
        }
    }

    internal static string AssetValue(JsonObject owner, string key)
    {
        if (owner[key] is not JsonValue value || !value.TryGetValue<string>(out var text) || text == null)
            throw new InvalidDataException("图标资源路径必须为字符串：" + key);
        return text;
    }

    private static bool ValidAssetName(string name)
    {
        if (!name.StartsWith("assets/", StringComparison.Ordinal) || name.Length < 75) return false;
        var hash = name.AsSpan(7, 64);
        foreach (var character in hash) if (!(character is >= '0' and <= '9' or >= 'a' and <= 'f')) return false;
        return Extensions.Contains(name[71..]);
    }

    internal static bool HashMatches(string name, byte[] bytes) =>
        string.Equals(name.Substring(7, 64), Convert.ToHexString(SHA256.HashData(bytes)), StringComparison.OrdinalIgnoreCase);

    private static JsonObject ParseObject(byte[] bytes)
    {
        using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 64 });
        ValidateProperties(document.RootElement);
        return JsonNode.Parse(bytes, documentOptions: new JsonDocumentOptions { MaxDepth = 64 }) as JsonObject
            ?? throw new InvalidDataException("配置 JSON 根节点必须为对象。");
    }

    private static void ValidateProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new InvalidDataException("配置 JSON 包含重复字段：" + property.Name);
                ValidateProperties(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array) foreach (var child in element.EnumerateArray()) ValidateProperties(child);
    }

    private static async Task<byte[]> ReadEntryAsync(ZipArchiveEntry entry, int limit)
    {
        await using var stream = entry.Open();
        return await ReadBoundedAsync(stream, entry.Length, limit);
    }

    internal static async Task<byte[]> ReadBoundedAsync(Stream input, long length, int limit)
    {
        if (length < 0 || length > limit) throw new InvalidDataException("图标资源或配置文件超过大小限制。");
        using var output = new MemoryStream((int)length);
        var buffer = new byte[65536];
        int count;
        while ((count = await input.ReadAsync(buffer)) != 0)
        {
            if (output.Length + count > length || output.Length + count > limit) throw new InvalidDataException("配置包文件解压长度超过声明或大小限制。");
            await output.WriteAsync(buffer.AsMemory(0, count));
        }
        if (output.Length != length) throw new InvalidDataException("配置包文件长度与声明不符。");
        return output.ToArray();
    }

    private static async Task WriteEntryAsync(ZipArchive archive, string name, byte[] bytes)
    {
        await using var stream = archive.CreateEntry(name, CompressionLevel.Optimal).Open();
        await stream.WriteAsync(bytes);
    }
}

/// <summary>Validated package data; ReadAsync has no installation side effects.</summary>
public sealed class ConfigurationImport
{
    private readonly JsonObject settings;
    private readonly IReadOnlyDictionary<string, byte[]> assets;
    public JsonObject Settings => (JsonObject)settings.DeepClone();

    internal ConfigurationImport(JsonObject settings, IReadOnlyDictionary<string, byte[]> assets)
    { this.settings = settings; this.assets = assets; }

    public async Task<JsonObject> InstallAssetsAsync(string managedAssetsDir)
    {
        var installed = (JsonObject)settings.DeepClone();
        if (assets.Count == 0) return installed;
        var directory = Path.GetFullPath(managedAssetsDir);
        if (Directory.Exists(directory) && (File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("图标资源目录不能为重解析点。");
        var paths = assets.Keys.ToDictionary(name => name, name => Path.Combine(directory,
            name.Substring(7, 64).ToUpperInvariant() + Path.GetExtension(name)), StringComparer.Ordinal);
        // Check every existing destination before creating any new files.
        foreach (var asset in assets)
            if (File.Exists(paths[asset.Key])) await VerifyExistingAsync(paths[asset.Key], asset.Key);
        Directory.CreateDirectory(directory);
        foreach (var asset in assets)
        {
            var target = paths[asset.Key];
            if (File.Exists(target)) { await VerifyExistingAsync(target, asset.Key); continue; }
            var temporary = Path.Combine(directory, "." + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true))
                { await output.WriteAsync(asset.Value); await output.FlushAsync(); output.Flush(true); }
                try { File.Move(temporary, target); }
                catch (IOException) when (File.Exists(target)) { await VerifyExistingAsync(target, asset.Key); }
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        foreach (var (owner, key) in ConfigurationArchive.AssetFields(installed))
        {
            var name = ConfigurationArchive.AssetValue(owner, key);
            if (name.Length != 0) owner[key] = paths[name];
        }
        return installed;
    }

    private static async Task VerifyExistingAsync(string path, string name)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new IOException("现有图标资源不能为重解析点：" + path);
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true);
        var bytes = await ConfigurationArchive.ReadBoundedAsync(stream, stream.Length, ConfigurationArchive.MaxAssetBytes);
        if (!ConfigurationArchive.HashMatches(name, bytes)) throw new IOException("现有图标资源校验失败，已保留原文件：" + path);
    }
}
