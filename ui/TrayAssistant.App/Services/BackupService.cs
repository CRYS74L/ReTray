using System.Security.Cryptography;
using System.Text.Json.Nodes;
namespace TrayAssistant_App.Services;
public static class BackupService
{
    public static async Task<string> StoreAssetAsync(byte[] bytes,string extension)
    {
        if(bytes.Length>32*1024*1024)throw new IOException("图标资源过大");
        var dir=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"TrayIconAssistant","Assets");Directory.CreateDirectory(dir);
        var hash=Convert.ToHexString(SHA256.HashData(bytes));var path=Path.Combine(dir,hash+extension.ToLowerInvariant());
        if(File.Exists(path)){if(!Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(path))).Equals(hash,StringComparison.OrdinalIgnoreCase))throw new IOException("已有图标资源已被改动，未覆盖。" );return path;}
        var temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";await File.WriteAllBytesAsync(temporary,bytes);File.Move(temporary,path);return path;
    }
    public static async Task<JsonObject> PrepareImportAsync(JsonObject bundle)
    {
        var settings=(JsonObject)bundle.DeepClone();var assets=settings["assets"]?.AsObject()??new();var decoded=new Dictionary<string,byte[]>();long total=0;
        foreach(var item in assets){var ext=Path.GetExtension(item.Key).ToLowerInvariant();if(Path.GetFileName(item.Key)!=item.Key||!new[]{".ico",".png",".bmp",".jpg",".jpeg",".exe",".dll"}.Contains(ext))throw new IOException("图标资源路径或格式无效");var bytes=Convert.FromBase64String(item.Value!.GetValue<string>());total+=bytes.Length;if(bytes.Length>32*1024*1024||total>64*1024*1024)throw new IOException("图标资源过大");var expected=Convert.ToHexString(SHA256.HashData(bytes))+ext;if(!item.Key.Equals(expected,StringComparison.OrdinalIgnoreCase))throw new IOException("图标资源内容与名称不一致");decoded.Add(item.Key,bytes);}
        var nodes=settings["rules"]!.AsArray().Select(r=>r!.AsObject()).Append(settings["ownIcon"]!.AsObject()).ToList();
        foreach(var node in nodes){var asset=node["asset"]?.GetValue<string>()??"";if(asset.Length>0&&!decoded.ContainsKey(asset))throw new IOException("配置缺少图标资源");}
        foreach(var node in nodes){var asset=node["asset"]?.GetValue<string>()??"";if(asset.Length>0)node["asset"]=await StoreAssetAsync(decoded[asset],Path.GetExtension(asset));}
        settings.Remove("assets");return settings;
    }
}
