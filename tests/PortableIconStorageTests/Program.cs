using System.Text.Json.Nodes;
using LanDeviceMonitor.Services;
using ReTray_App.Services;

var root = Path.Combine(Path.GetTempPath(), "ReTray-asset-test-" + Guid.NewGuid().ToString("N"));
var data = ApplicationPaths.DataDirectory;
if (Directory.Exists(data)) throw new Exception("Refusing to overwrite preexisting test data");
Directory.CreateDirectory(root);
void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
try
{
    Check(ApplicationPaths.IsPortable && ManagedIconStorage.DirectoryPath == Path.Combine(Path.GetDirectoryName(Environment.ProcessPath!)!, "Data", "Assets"), "Picker storage does not use the portable profile");
    var source = Path.Combine(root, "selected.PNG");
    byte[] bytes = [1, 3, 5, 7, 9];
    await File.WriteAllBytesAsync(source, bytes);
    var stored = await ManagedIconStorage.StoreAsync(source);
    Check(stored.StartsWith(ManagedIconStorage.DirectoryPath + Path.DirectorySeparatorChar), "Selected icon escaped Data/Assets");
    Check(await ManagedIconStorage.StoreAsync(source) == stored, "Identical images were not reused");
    Check(Directory.EnumerateFiles(ManagedIconStorage.DirectoryPath).Count() == 1, "Repeated selection created extra copies");
    await File.WriteAllBytesAsync(source, [2, 4, 6]);
    Check(File.ReadAllBytes(stored).SequenceEqual(bytes), "Changing the source changed the managed copy");
    Console.WriteLine("PASS newly selected icons are independent, deduplicated copies inside portable Data/Assets");

    var settings = new JsonObject { ["schema"] = 1, ["rules"] = new JsonArray(new JsonObject { ["asset"] = stored, ["replacementAsset"] = stored }), ["ownIcon"] = new JsonObject { ["asset"] = stored }, ["inputMethodIcons"] = new JsonArray(new JsonObject { ["asset"] = stored }) };
    var archive = Path.Combine(root, "backup.trayconfig");
    await ConfigurationArchive.ExportAsync(archive, settings);
    File.Delete(source); File.Delete(stored);
    var package = await ConfigurationArchive.ReadAsync(archive);
    var installed = await ManagedIconStorage.ImportAsync(package);
    var imported = installed["rules"]![0]!["asset"]!.GetValue<string>();
    Check(imported.StartsWith(ManagedIconStorage.DirectoryPath + Path.DirectorySeparatorChar) && File.ReadAllBytes(imported).SequenceEqual(bytes), "Imported icons did not reconstruct into Data/Assets");
    foreach (var path in new[] { installed["ownIcon"]!["asset"]!.GetValue<string>(), installed["inputMethodIcons"]![0]!["asset"]!.GetValue<string>() }) Check(path == imported, "An icon category used a different managed root");
    var moved = Path.Combine(root, "MovedData"); Directory.CreateDirectory(Path.Combine(moved, "Assets"));
    File.Copy(imported, Path.Combine(moved, "Assets", Path.GetFileName(imported)));
    await File.WriteAllTextAsync(Path.Combine(moved, "settings.json"), installed.ToJsonString());
    ApplicationPaths.RebaseManagedAssets(moved);
    var relocated = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(moved, "settings.json")))!;
    Check(relocated["rules"]![0]!["asset"]!.GetValue<string>() == Path.Combine(moved, "Assets", Path.GetFileName(imported)), "New/imported images did not follow a moved portable directory");
    Console.WriteLine("PASS import reconstructs every image category without source files and remains relocatable");

    var legacySource = Path.Combine(root, "legacy-source.ico"); await File.WriteAllBytesAsync(legacySource, [8, 6, 4, 2]);
    var legacyJson = Path.Combine(root, "legacy.json");
    var legacySettings = new JsonObject { ["schema"] = 1, ["rules"] = new JsonArray(new JsonObject { ["asset"] = legacySource }), ["ownIcon"] = new JsonObject { ["asset"] = "" } };
    await File.WriteAllTextAsync(legacyJson, legacySettings.ToJsonString());
    var legacyPackage = await ConfigurationArchive.ReadAsync(legacyJson);
    var legacyInstalled = await ManagedIconStorage.ImportAsync(legacyPackage);
    var legacyTarget = legacyInstalled["rules"]![0]!["asset"]!.GetValue<string>();
    File.Delete(legacySource);
    Check(File.Exists(legacyTarget) && legacyTarget.StartsWith(ManagedIconStorage.DirectoryPath + Path.DirectorySeparatorChar), "Legacy JSON import retained an external source path");
    Console.WriteLine("PASS legacy JSON import copies external icon sources into portable storage");

    var oldAssets = Path.Combine(root, "old-managed", "Assets"); Directory.CreateDirectory(oldAssets);
    byte[] oldBytes = [12, 34, 56]; var oldHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(oldBytes));
    var oldFile = Path.Combine(oldAssets, oldHash + ".ico"); await File.WriteAllBytesAsync(oldFile, oldBytes);
    var oldProfile = new JsonObject { ["rules"] = new JsonArray(new JsonObject { ["asset"] = oldFile }) };
    await File.WriteAllTextAsync(Path.Combine(data, "settings.json"), oldProfile.ToJsonString());
    ApplicationPaths.RebaseManagedAssets(data, oldAssets);
    var repaired = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(data, "settings.json")))!;
    var repairedPath = repaired["rules"]![0]!["asset"]!.GetValue<string>();
    Check(repairedPath == Path.Combine(data, "Assets", Path.GetFileName(oldFile)) && File.Exists(oldFile) && File.ReadAllBytes(repairedPath).SequenceEqual(oldBytes), "Previous misplaced managed icons were not copied safely");
    Console.WriteLine("PASS previous managed-image paths are repaired while original files remain intact");

    await File.WriteAllBytesAsync(imported, [99]);
    var rejected = false;
    var candidate = Path.Combine(root, "same.png"); await File.WriteAllBytesAsync(candidate, bytes);
    try { await ManagedIconStorage.StoreAsync(candidate); } catch (IOException) { rejected = true; }
    Check(rejected && File.ReadAllBytes(imported).SequenceEqual(new byte[] { 99 }), "A corrupt existing asset was silently overwritten or reused");
    Console.WriteLine("PASS corrupt existing assets are rejected and preserved");
}
finally { Directory.Delete(root, true); if (Directory.Exists(data)) Directory.Delete(data, true); }
