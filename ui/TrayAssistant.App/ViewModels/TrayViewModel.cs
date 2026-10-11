using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Windows.Storage.Pickers;
using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using TrayAssistant_App.Models;
using TrayAssistant_App.Services;

namespace TrayAssistant_App.ViewModels;
public partial class TrayViewModel : ObservableObject
{
    public PipeClient Pipe { get; } = new();
    public ObservableCollection<IconRow> Icons { get; } = new();
    private readonly Dictionary<string, IconRow> all = new();
    private readonly Stack<(string key, JsonObject rule)> undo = new();
    private bool polling;
    private IconRow? selected;
    public IconRow? Selected { get => selected; set { if (SetProperty(ref selected, value) && value != null) ReadEditor(value); } }
    [ObservableProperty] public partial string Status { get; set; } = "连接后台…";
    [ObservableProperty] public partial string CountLabel { get; set; } = "正在读取实际托盘";
    [ObservableProperty] public partial string Search { get; set; } = "";
    [ObservableProperty] public partial string Asset { get; set; } = "";
    [ObservableProperty] public partial string Tip { get; set; } = "";
    [ObservableProperty] public partial int VisibilityIndex { get; set; }
    [ObservableProperty] public partial int LocationIndex { get; set; }
    [ObservableProperty] public partial double Order { get; set; }
    [ObservableProperty] public partial double InactiveSeconds { get; set; }
    [ObservableProperty] public partial double UnusedDays { get; set; }
    [ObservableProperty] public partial bool MenuVisible { get; set; } = true;
    [ObservableProperty] public partial bool AlwaysShow { get; set; }
    [ObservableProperty] public partial bool Startup { get; set; }
    [ObservableProperty] public partial bool OwnVisible { get; set; } = true;
    [ObservableProperty] public partial string OwnAsset { get; set; } = "";
    [ObservableProperty] public partial bool SafeMode { get; set; }
    [ObservableProperty] public partial int ThemeIndex { get; set; }
    [ObservableProperty] public partial string Diagnostics { get; set; } = "正在读取后台状态";
    [ObservableProperty] public partial string WindowsStatus { get; set; } = "选择窗口，收起到助手；退出助手会恢复窗口。";
    public JsonObject Settings { get; private set; } = new();
    public event Action<int>? ThemeChanged;
    partial void OnSearchChanged(string value) => Filter();
    private void Filter()
    {
        var wanted = all.Values.Where(x => string.IsNullOrWhiteSpace(Search) || (x.Name + x.Tip + x.Executable).Contains(Search, StringComparison.OrdinalIgnoreCase)).ToList();
        for (int i = Icons.Count - 1; i >= 0; i--) if (!wanted.Contains(Icons[i])) Icons.RemoveAt(i);
        foreach (var row in wanted) if (!Icons.Contains(row)) Icons.Add(row);
    }
    public async Task InitializeAsync()
    {
        try
        {
            var result = await Call("settings.read"); Settings = (JsonObject)result["data"]!.DeepClone();
            Startup = Settings["startup"]?.GetValue<bool>() ?? false; OwnVisible = Settings["ownIcon"]!["visible"]!.GetValue<bool>(); OwnAsset = Settings["ownIcon"]!["asset"]!.GetValue<string>(); SafeMode = Settings["safeMode"]?.GetValue<bool>() ?? false;
            ThemeIndex = Settings["theme"]!.GetValue<string>() switch { "light" => 1, "dark" => 2, _ => 0 }; ThemeChanged?.Invoke(ThemeIndex);
            var aumid = Windows.ApplicationModel.Package.Current.Id.FamilyName + "!App";
            await Call("settings.apply", new() { ["uiActivation"] = aumid }); await RefreshAsync();
        }
        catch (Exception ex) { Status = ex.Message; }
    }
    public async Task RefreshAsync()
    {
        if (polling) return; polling = true;
        try
        {
            var result = await Call("catalog.read"); var data = result["data"]!["icons"]!.AsArray(); var active = new HashSet<string>();
            foreach (var item in data) { var j = item!.AsObject(); var key = j["key"]!.GetValue<string>(); active.Add(key); if (all.TryGetValue(key, out var row)) row.Update(j); else all[key] = new IconRow(j); }
            foreach (var key in all.Keys.Where(k => !active.Contains(k)).ToList()) all.Remove(key);
            Filter(); CountLabel = $"{all.Count} 个实际托盘图标 · 持续同步";
            if (Selected != null && !active.Contains(Selected.Key)) Selected = null;
            var health = await Call("status.read"); var h = health["data"]!;
            Diagnostics = $"后台版本  {h["version"]}\n连接状态  {(h["connected"]!.GetValue<bool>() ? "原生桥接已连接" : "等待 Explorer / 安全模式")}\n累计源事件  {h["events"]}\n回调 p95  {h["callbackP95Micros"]} µs\n后台 PID  {h["pid"]}\n配置恢复  {h["recovered"]}";
            if (Status == "连接后台…") Status = "已连接，保留原应用菜单与动态状态。";
        }
        catch (Exception ex) { Status = ex.Message; }
        finally { polling = false; }
    }
    private void ReadEditor(IconRow row)
    {
        var j = row.Rule; Asset = j["asset"]!.GetValue<string>(); Tip = j["tip"]!.GetValue<string>(); VisibilityIndex = j["visibility"]!.GetValue<int>() + 1; LocationIndex = j["location"]!.GetValue<int>() + 1; Order = j["order"]!.GetValue<int>(); InactiveSeconds = j["inactiveSeconds"]!.GetValue<int>(); UnusedDays = j["unusedDays"]!.GetValue<int>(); MenuVisible = j["menuVisible"]!.GetValue<bool>(); AlwaysShow = j["alwaysShow"]!.GetValue<bool>();
    }
    private async Task<JsonObject> Call(string method, JsonObject? data = null)
    {
        var result = await Pipe.RequestAsync(method, data); if (result["success"]?.GetValue<bool>() != true) throw new IOException((result["saved"]?.GetValue<bool>() == true ? "已保存，但未生效：" : "") + (result["detail"]?.GetValue<string>() ?? "操作失败")); return result;
    }
    private async Task Execute(Func<Task> action) { try { await action(); } catch (Exception ex) { Status = ex.Message; } }
    [RelayCommand] private Task SaveRuleAsync() => Execute(async () =>
    {
        if (Selected == null) return; var row = Selected; undo.Push((row.Key, (JsonObject)row.Rule.DeepClone())); var j = (JsonObject)row.Rule.DeepClone();
        if (Asset.Length>0 && (Asset != row.Rule["asset"]!.GetValue<string>() || row.Rule["baselineWidth"]?.GetValue<int>() == 0)) { j["baselineFingerprint"] = row.Rule["sourceFingerprint"]!.DeepClone(); j["baselineWidth"] = row.Rule["originalWidth"]!.DeepClone(); j["baselineHeight"] = row.Rule["originalHeight"]!.DeepClone(); } j["asset"] = Asset; j["tip"] = Tip; j["visibility"] = VisibilityIndex - 1; j["location"] = LocationIndex - 1; j["order"] = (int)Order; j["inactiveSeconds"] = (int)InactiveSeconds; j["unusedDays"] = (int)UnusedDays; j["menuVisible"] = MenuVisible; j["alwaysShow"] = AlwaysShow;
        var result = await Call("rule.apply", new() { ["rule"] = j }); Status = result["detail"]?.GetValue<string>() ?? "规则已保存"; await RefreshAsync();
    });
    [RelayCommand] private Task ResetRuleAsync() => Execute(async () => { if (Selected == null) return; undo.Push((Selected.Key, (JsonObject)Selected.Rule.DeepClone())); await Call("rule.remove", new() { ["rule"] = Selected.Rule.DeepClone() }); Status = "已恢复原应用设置"; await RefreshAsync(); if (Selected != null) ReadEditor(Selected); });
    [RelayCommand] private Task UndoAsync() => Execute(async () => { if (!undo.TryPop(out var previous)) return; await Call("rule.apply", new() { ["rule"] = previous.rule }); Status = "已撤销上次规则操作"; await RefreshAsync(); if (Selected != null) ReadEditor(Selected); });
    [RelayCommand] private Task OriginalMenuAsync() => Execute(async () => { if (Selected != null) await Call("icon.invoke", new() { ["key"] = Selected.Key, ["action"] = "menu" }); });
    [RelayCommand] private Task OpenIconAsync() => Execute(async () => { if (Selected != null) await Call("icon.invoke", new() { ["key"] = Selected.Key, ["action"] = "double" }); });
    private static async Task<string?> ChooseAssetAsync()
    {
        var picker = new FileOpenPicker(App.Window.AppWindow.Id); foreach (var ext in new[] { ".ico", ".png", ".bmp", ".jpg", ".exe", ".dll" }) picker.FileTypeFilter.Add(ext);
        var file = await picker.PickSingleFileAsync(); if (file == null) return null; var info = new FileInfo(file.Path); if (info.Length > 32 * 1024 * 1024) throw new IOException("图标资源不能超过 32 MB。");
        var bytes = await File.ReadAllBytesAsync(file.Path); var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TrayIconAssistant", "Assets"); Directory.CreateDirectory(directory); var target = Path.Combine(directory, Convert.ToHexString(SHA256.HashData(bytes)) + Path.GetExtension(file.Path).ToLowerInvariant()); if (!File.Exists(target)) await File.WriteAllBytesAsync(target, bytes); return target;
    }
    [RelayCommand] private Task ChooseIconAsync() => Execute(async () => { var path = await ChooseAssetAsync(); if (path != null) Asset = path; });
    [RelayCommand] private Task ChooseOwnIconAsync() => Execute(async () => { var path = await ChooseAssetAsync(); if (path != null) OwnAsset = path; });
    [RelayCommand] private Task SavePreferencesAsync() => Execute(async () => { var theme = new[] { "system", "light", "dark" }[ThemeIndex]; await Call("settings.apply", new() { ["startup"] = Startup, ["theme"] = theme, ["safeMode"] = SafeMode }); await Call("ownIcon.apply", new() { ["visible"] = OwnVisible, ["asset"] = OwnAsset, ["tip"] = "托盘图标助手" }); ThemeChanged?.Invoke(ThemeIndex); Status = "偏好已保存并应用"; });
    [RelayCommand] private Task RestoreWindowsAsync() => Execute(async () => { await Call("window.restore"); WindowsStatus = "已恢复所有收起的窗口"; });
    [RelayCommand] private Task StopAgentAsync() => Execute(async () => { await Call("agent.stop"); Status = "后台已停止，托盘与窗口已恢复"; });
}
