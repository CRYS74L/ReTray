using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

var suite = new ArchiveTests();
await suite.RunAsync();

sealed class ArchiveTests
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "TrayConfigurationTests-" + Guid.NewGuid().ToString("N"));
    private readonly byte[] icon = [1, 2, 3, 4, 5];
    private string AssetName => "assets/" + Convert.ToHexString(SHA256.HashData(icon)).ToLowerInvariant() + ".ico";
    private Type Service => Assembly.GetExecutingAssembly().GetType("TrayAssistant_App.Services.ConfigurationArchive")
        ?? throw new Exception("Portable ConfigurationArchive service has not been implemented.");
    private async Task Export(string target, JsonObject settings) => await (Task)Service.GetMethod("ExportAsync")!.Invoke(null, [target, settings])!;
    private async Task<object> Read(string path)
    {
        var task = (Task)Service.GetMethod("ReadAsync")!.Invoke(null, [path])!;
        await task;
        return task.GetType().GetProperty("Result")!.GetValue(task)!;
    }
    private static JsonObject Settings(object import) => (JsonObject)import.GetType().GetProperty("Settings")!.GetValue(import)!;
    private static async Task<JsonObject> Install(object import, string target) => await (Task<JsonObject>)import.GetType().GetMethod("InstallAssetsAsync")!.Invoke(import, [target])!;
    private string FilePath(string name) => Path.Combine(root, name);
    private static void Check(bool condition, string reason) { if (!condition) throw new Exception(reason); }
    private static async Task Reject(Func<Task> action)
    {
        try { await action(); }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or System.Text.Json.JsonException or ArgumentException) { return; }
        throw new Exception("Expected archive rejection.");
    }
    private JsonObject Fixture(string asset) => new()
    {
        ["schema"] = 1,
        ["rules"] = new JsonArray(new JsonObject { ["asset"] = asset, ["assetIndex"] = 3, ["replacementAsset"] = asset, ["replacementAssetIndex"] = 7 },
            new JsonObject { ["asset"] = "", ["replacementAsset"] = asset, ["replacementAssetIndex"] = 11 }),
        ["ownIcon"] = new JsonObject { ["asset"] = "", ["replacementAsset"] = asset },
        ["inputMethodIcons"] = new JsonArray(new JsonObject { ["asset"] = asset, ["assetIndex"] = 4 })
    };
    private async Task Zip(string path, string? asset = null, byte[]? bytes = null, string? extra = null, string? manifest = null, string? settings = null, bool duplicate = false)
    {
        using var file = System.IO.File.Create(path);
        using var zip = new ZipArchive(file, ZipArchiveMode.Create);
        async Task Entry(string name, byte[] data) { await using var stream = zip.CreateEntry(name).Open(); await stream.WriteAsync(data); }
        await Entry("manifest.json", Encoding.UTF8.GetBytes(manifest ?? "{\"format\":\"TrayIconAssistant.Configuration\",\"version\":1}"));
        await Entry("settings.json", Encoding.UTF8.GetBytes(settings ?? Fixture(AssetName).ToJsonString()));
        if (asset != null) await Entry(asset, bytes ?? icon);
        if (extra != null) await Entry(extra, icon);
        if (duplicate) await Entry("settings.json", "{}"u8.ToArray());
    }
    public async Task RunAsync()
    {
        Directory.CreateDirectory(root);
        var tests = new (string Name, Func<Task> Run)[]
        {
            ("Roundtrip keeps repeated and inactive references and icon indexes", async () =>
            {
                await System.IO.File.WriteAllBytesAsync(FilePath("source.ico"), icon);
                var original = Fixture(FilePath("source.ico")); var originalJson = original.ToJsonString();
                await Export(FilePath("roundtrip.trayconfig"), original);
                Check(original.ToJsonString() == originalJson, "Export mutated input.");
                using (var zip = ZipFile.OpenRead(FilePath("roundtrip.trayconfig")))
                { Check(zip.Entries.Count == 3, "Repeated asset was not deduplicated."); Check(zip.GetEntry(AssetName) != null, "Missing portable asset."); }
                System.IO.File.Delete(FilePath("source.ico"));
                var imported = await Read(FilePath("roundtrip.trayconfig"));
                Check(!Directory.Exists(FilePath("managed")), "Read wrote assets.");
                Check(Settings(imported)["rules"]![1]!["replacementAsset"]!.GetValue<string>() == AssetName, "Inactive selection lost.");
                var installed = await Install(imported, FilePath("managed"));
                var path = installed["rules"]![0]!["asset"]!.GetValue<string>();
                Check(Path.GetFileName(path) == Convert.ToHexString(SHA256.HashData(icon)) + ".ico", "Managed filename differs from ChooseAssetAsync.");
                Check((await System.IO.File.ReadAllBytesAsync(path)).SequenceEqual(icon), "Asset bytes differ.");
                Check(installed["rules"]![0]!["assetIndex"]!.GetValue<int>() == 3 && installed["rules"]![1]!["replacementAssetIndex"]!.GetValue<int>() == 11, "Indexes lost.");
                Check(installed["ownIcon"]!["replacementAsset"]!.GetValue<string>() == path && installed["inputMethodIcons"]![0]!["asset"]!.GetValue<string>() == path, "Other references not rewritten.");
                Check(installed["rules"]![1]!["asset"]!.GetValue<string>() == "", "Inactive replacement became active.");
                await Install(imported, FilePath("managed"));
                Check(Directory.GetFiles(FilePath("managed")).Length == 1, "Reuse made extra files.");
            }),
            ("Missing source preserves existing export atomically", async () =>
            {
                var target = FilePath("preserved.trayconfig"); await System.IO.File.WriteAllTextAsync(target, "previous archive");
                await Reject(() => Export(target, Fixture(FilePath("missing.ico"))));
                Check(await System.IO.File.ReadAllTextAsync(target) == "previous archive", "Previous export changed.");
                Check(!Directory.GetFiles(root).Any(p => p.Contains(".tmp", StringComparison.Ordinal)), "Temporary archive leaked.");
            }),
            ("Locked destination preserves previous export and cleans temporary archive", async () =>
            {
                var target = FilePath("locked.trayconfig"); await System.IO.File.WriteAllTextAsync(target, "old archive");
                await using (var locked = new FileStream(target, FileMode.Open, FileAccess.Read, FileShare.None))
                    await Reject(() => Export(target, new JsonObject()));
                Check(await System.IO.File.ReadAllTextAsync(target) == "old archive", "Failed replacement changed previous file.");
                Check(!Directory.GetFiles(root).Any(p => p.EndsWith(".tmp", StringComparison.Ordinal)), "Failed replacement leaked a temporary file.");
            }),
            ("Reject mismatching asset hash", async () => { await Zip(FilePath("hash.zip"), AssetName, [9]); await Reject(async () => { await Read(FilePath("hash.zip")); }); }),
            ("Reject missing referenced asset", async () => { await Zip(FilePath("missing.zip")); await Reject(async () => { await Read(FilePath("missing.zip")); }); }),
            ("Reject traversal before installation", async () => { await Zip(FilePath("traversal.zip"), AssetName, extra: "../escaped.ico"); await Reject(async () => { await Read(FilePath("traversal.zip")); }); Check(!System.IO.File.Exists(Path.Combine(Path.GetDirectoryName(root)!, "escaped.ico")), "Archive escaped."); }),
            ("Reject unsupported archive version", async () => { await Zip(FilePath("version.zip"), AssetName, manifest: "{\"format\":\"TrayIconAssistant.Configuration\",\"version\":2}"); await Reject(async () => { await Read(FilePath("version.zip")); }); }),
            ("Reject duplicate ZIP names", async () => { await Zip(FilePath("duplicate.zip"), AssetName, duplicate: true); await Reject(async () => { await Read(FilePath("duplicate.zip")); }); }),
            ("Reject unreferenced asset", async () => { await Zip(FilePath("unused.zip"), AssetName, extra: "assets/" + new string('a', 64) + ".png"); await Reject(async () => { await Read(FilePath("unused.zip")); }); }),
            ("Reject settings larger than 2 MiB", async () => { await Zip(FilePath("settings-bound.zip"), settings: "{\"padding\":\"" + new string('x', 2 * 1024 * 1024) + "\"}"); await Reject(async () => { await Read(FilePath("settings-bound.zip")); }); }),
            ("Reject compressed asset larger than 32 MiB", async () =>
            {
                var bytes = new byte[32 * 1024 * 1024 + 1]; var name = "assets/" + Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant() + ".ico";
                await Zip(FilePath("asset-bound.zip"), name, bytes, settings: Fixture(name).ToJsonString());
                await Reject(async () => { await Read(FilePath("asset-bound.zip")); });
            }),
            ("Reject total assets larger than 256 MiB before decompression", async () =>
            {
                var path = FilePath("total-bound.zip"); var bytes = new byte[32 * 1024 * 1024];
                using (var file = System.IO.File.Create(path)) using (var zip = new ZipArchive(file, ZipArchiveMode.Create))
                {
                    var rules = new JsonArray();
                    for (int i = 0; i < 9; i++)
                    {
                        bytes[0] = (byte)i; var name = "assets/" + Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant() + ".ico";
                        rules.Add(new JsonObject { ["asset"] = name });
                        await using var stream = zip.CreateEntry(name).Open(); await stream.WriteAsync(bytes);
                    }
                    await using (var stream = zip.CreateEntry("manifest.json").Open()) await stream.WriteAsync("{\"format\":\"TrayIconAssistant.Configuration\",\"version\":1}"u8.ToArray());
                    await using (var stream = zip.CreateEntry("settings.json").Open()) await stream.WriteAsync(Encoding.UTF8.GetBytes(new JsonObject { ["rules"] = rules }.ToJsonString()));
                }
                await Reject(async () => { await Read(path); });
            }),
            ("Reject physical archive larger than bound", async () =>
            {
                var path = FilePath("physical-bound.zip");
                using (var file = System.IO.File.Create(path))
                {
                    file.SetLength(263L * 1024 * 1024); file.Position = file.Length;
                    using var zip = new ZipArchive(file, ZipArchiveMode.Create);
                    await using (var stream = zip.CreateEntry("manifest.json").Open()) await stream.WriteAsync("{\"format\":\"TrayIconAssistant.Configuration\",\"version\":1}"u8.ToArray());
                    await using (var stream = zip.CreateEntry("settings.json").Open()) await stream.WriteAsync("{}"u8.ToArray());
                }
                await Reject(async () => { await Read(path); });
            }),
            ("Reject more than 2000 entries", async () =>
            {
                using (var file = System.IO.File.Create(FilePath("count.zip"))) using (var zip = new ZipArchive(file, ZipArchiveMode.Create))
                {
                    var rows = new JsonArray(); var extensions = new[] { ".ico", ".png", ".bmp", ".jpg", ".exe", ".dll" };
                    for (int i = 0; i < 1999; i++)
                    {
                        var bytes = BitConverter.GetBytes(i); var name = "assets/" + Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant() + extensions[i % 6];
                        rows.Add(new JsonObject { ["asset"] = name });
                        await using var stream = zip.CreateEntry(name).Open(); await stream.WriteAsync(bytes);
                    }
                    await using (var stream = zip.CreateEntry("manifest.json").Open()) await stream.WriteAsync("{\"format\":\"TrayIconAssistant.Configuration\",\"version\":1}"u8.ToArray());
                    await using (var stream = zip.CreateEntry("settings.json").Open()) await stream.WriteAsync(Encoding.UTF8.GetBytes(new JsonObject { ["rules"] = rows }.ToJsonString()));
                }
                await Reject(async () => { await Read(FilePath("count.zip")); });
            }),
            ("Reject duplicate JSON properties", async () => { await Zip(FilePath("duplicate-json.zip"), settings: "{\"rules\":[],\"rules\":[]}"); await Reject(async () => { await Read(FilePath("duplicate-json.zip")); }); }),
            ("Reject malformed asset field", async () => { await Zip(FilePath("field.zip"), settings: "{\"rules\":[{\"asset\":123}]}"); await Reject(async () => { await Read(FilePath("field.zip")); }); }),
            ("Reject external path inside packaged settings", async () => { await Zip(FilePath("external.zip"), settings: Fixture("C:\\private\\secret.ico").ToJsonString()); await Reject(async () => { await Read(FilePath("external.zip")); }); }),
            ("Reject unsupported asset extension", async () => { await System.IO.File.WriteAllBytesAsync(FilePath("private.txt"), icon); await Reject(() => Export(FilePath("unsupported.zip"), Fixture(FilePath("private.txt")))); }),
            ("Reject corrupt existing managed asset without overwrite", async () =>
            {
                await Zip(FilePath("existing.zip"), AssetName); var imported = await Read(FilePath("existing.zip"));
                var destination = FilePath("corrupt-managed"); Directory.CreateDirectory(destination);
                var target = Path.Combine(destination, Convert.ToHexString(SHA256.HashData(icon)) + ".ico"); await System.IO.File.WriteAllTextAsync(target, "keep corrupt");
                await Reject(async () => { await Install(imported, destination); });
                Check(await System.IO.File.ReadAllTextAsync(target) == "keep corrupt", "Corrupt existing file overwritten.");
                Check(Directory.GetFiles(destination).Length == 1, "Failed install leaked files.");
            }),
            ("Check all existing assets before writing any missing asset", async () =>
            {
                byte[] newBytes = [8, 8]; var newName = "assets/" + Convert.ToHexString(SHA256.HashData(newBytes)).ToLowerInvariant() + ".png";
                var packaged = Fixture(newName); packaged["ownIcon"]!["replacementAsset"] = AssetName;
                await Zip(FilePath("preflight.zip"), newName, newBytes, extra: AssetName, settings: packaged.ToJsonString());
                var import = await Read(FilePath("preflight.zip")); var destination = FilePath("preflight-assets"); Directory.CreateDirectory(destination);
                var corrupt = Path.Combine(destination, Convert.ToHexString(SHA256.HashData(icon)) + ".ico"); await System.IO.File.WriteAllTextAsync(corrupt, "corrupt");
                await Reject(async () => { await Install(import, destination); });
                Check(Directory.GetFiles(destination).Length == 1, "Install wrote a missing asset before discovering another corrupt destination.");
            }),
            ("Legacy JSON preserves local paths without copying", async () =>
            {
                var path = FilePath("legacy.json"); await System.IO.File.WriteAllTextAsync(path, Fixture("C:\\legacy\\icon.ico").ToJsonString());
                var import = await Read(path); var settings = await Install(import, FilePath("legacy-assets"));
                Check(settings["rules"]![0]!["asset"]!.GetValue<string>() == "C:\\legacy\\icon.ico", "Legacy path changed."); Check(!Directory.Exists(FilePath("legacy-assets")), "Legacy import copied assets.");
            }),
            ("Legacy JSON rejects unresolved packaged references", async () => { await System.IO.File.WriteAllTextAsync(FilePath("legacy-portable.json"), Fixture(AssetName).ToJsonString()); await Reject(async () => { await Read(FilePath("legacy-portable.json")); }); })
        };
        int failed = 0;
        try
        {
            foreach (var test in tests)
            {
                try { await test.Run(); Console.WriteLine("PASS " + test.Name); }
                catch (Exception ex) { failed++; Console.WriteLine("FAIL " + test.Name + ": " + ex.GetBaseException().Message); }
            }
            Console.WriteLine($"{tests.Length - failed}/{tests.Length} configuration archive tests passed.");
            Environment.ExitCode = failed == 0 ? 0 : 1;
        }
        finally { Directory.Delete(root, true); }
    }
}
