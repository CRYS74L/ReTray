using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Windows.Storage.Pickers;
using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using TrayAssistant_App.Models;
using TrayAssistant_App.Services;

namespace TrayAssistant_App.ViewModels;
public partial class TrayViewModel : ObservableObject
{
    public PipeClient Pipe { get; } = new();
    public ObservableCollection<IconRow> Icons { get; } = new();


    public List<IconRow> BatchSelection { get; } = new();
    private readonly Dictionary<string, IconRow> all = new();
    private readonly Stack<(string key, JsonObject rule)> undo = new();
    private bool polling, connectionError;
    private IconRow? selected;
    public IconRow? Selected { get => selected; set { if (SetProperty(ref selected, value)) { OnPropertyChanged(nameof(HasSelection)); if(value != null) ReadEditor(value); } } }
    public bool HasSelection => Selected != null;
    public bool IsDragging { get; set; }
    public async Task SetRowVisibilityAsync(IconRow row,bool value)
    {
        if(row.IsBusy||value==row.ActualVisible)return;row.IsBusy=true;
        await Execute(async()=>{if(row.Owned){var read=await Call("settings.read");var own=(JsonObject)read["data"]!["ownIcon"]!.DeepClone();own["visible"]=value;await Call("ownIcon.apply",own);OwnVisible=value;}else{var rule=EditableRule(row);rule["visibility"]=value?0:1;rule["enabled"]=true;await Call("rule.apply",new(){["rule"]=rule});}await RefreshAsync();Status=value?"已显示":"已隐藏";});
        row.IsBusy=false;row.Visible=row.ActualVisible;
    }
    [DllImport("user32.dll")] private static extern bool AllowSetForegroundWindow(uint processId);
    public Task NativeActionAsync(IconRow row,string action,bool keyboard,int x,int y)
    {
        AllowSetForegroundWindow(row.Rule["pid"]!.GetValue<uint>());
        return Execute(async()=>{await Call("icon.invoke",new(){["key"]=row.Key,["action"]=action,["nativePointer"]=true,["keyboard"]=keyboard,["x"]=x,["y"]=y});});
    }
    public static JsonObject EditableRule(IconRow row)
    {
        var result=new JsonObject();foreach(var key in new[]{"selector","visibility","location","order","asset","tip","enabled","menuVisible","alwaysShow","inactiveSeconds","unusedDays","hotkey","hotkeyAction","newState","swapButtons","baselineFingerprint","baselineWidth","baselineHeight","assetIndex"})
            if(row.Rule[key]!=null)result[key]=row.Rule[key]!.DeepClone();return result;
    }
    [ObservableProperty] public partial string Status { get; set; } = "";
    [ObservableProperty] public partial string Error { get; set; } = "";
    [ObservableProperty] public partial bool IsLoading { get; set; } = true;
    [ObservableProperty] public partial bool IsReady { get; set; }

    [ObservableProperty] public partial string Asset { get; set; } = "";
    [ObservableProperty] public partial Microsoft.UI.Xaml.Media.Imaging.WriteableBitmap? AssetPreview { get; set; }
    [ObservableProperty] public partial string AssetSummary { get; set; } = "";
    partial void OnAssetChanged(string value) => _ = PreviewAssetAsync();
    partial void OnAssetIndexChanged(double value) => _ = PreviewAssetAsync();
    public Task PreviewAssetAsync()=>Execute(async()=>{if(string.IsNullOrEmpty(Asset)){AssetPreview=null;AssetSummary="";return;}var path=Asset;int index=(int)AssetIndex;var result=await Call("icon.preview",new(){["path"]=path,["index"]=index});if(path!=Asset||index!=(int)AssetIndex)return;var image=result["data"]!;AssetPreview=IconRow.Decode(image["pixels"]!.GetValue<string>(),image["width"]!.GetValue<int>(),image["height"]!.GetValue<int>());AssetSummary=$"{image["count"]} 个可选图标 · 当前序号 {index}";});
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
    [ObservableProperty] public partial bool OwnInList { get; set; } = true;
    [ObservableProperty] public partial string OwnTip { get; set; } = "托盘图标助手";
    [ObservableProperty] public partial string OwnAsset { get; set; } = "";
    [ObservableProperty] public partial bool SafeMode { get; set; }
    [ObservableProperty] public partial int ThemeIndex { get; set; }
    [ObservableProperty] public partial int LanguageIndex { get; set; }
    public event Action? LanguageChanged;
    partial void OnLanguageIndexChanged(int value){Localization.English=value==1;LanguageChanged?.Invoke();}
    partial void OnStatusChanged(string value){var translated=Localization.Text(value);if(translated!=value)Status=translated;}
    [ObservableProperty] public partial double TaskbarOpacity { get; set; } = 255;
    [ObservableProperty] public partial bool HideStart { get; set; }
    [ObservableProperty] public partial bool UiTopmost { get; set; }
    [ObservableProperty] public partial double UiOpacity { get; set; } = 255;
    [ObservableProperty] public partial bool SwapButtons { get; set; }
    [ObservableProperty] public partial string MatchClass { get; set; } = "";
    [ObservableProperty] public partial string MatchTip { get; set; } = "";
    [ObservableProperty] public partial int MatchTipMode { get; set; }
    [ObservableProperty] public partial bool CurrentInstance { get; set; }
    [ObservableProperty] public partial double AssetIndex { get; set; }
    [ObservableProperty] public partial int Hotkey { get; set; }
    [ObservableProperty] public partial string HotkeyText { get; set; } = "未设置";
    [ObservableProperty] public partial int HotkeyAction { get; set; } = 1;
    [ObservableProperty] public partial int NewStateIndex { get; set; }

    [ObservableProperty] public partial int MenuActionIndex { get; set; } = 1;
    [ObservableProperty] public partial bool MenuProgramNames { get; set; } = true;
    [ObservableProperty] public partial bool MenuReverse { get; set; }
    [ObservableProperty] public partial bool MenuNewFirst { get; set; } = true;
    [ObservableProperty] public partial double MenuHoverDelay { get; set; }
    [ObservableProperty] public partial double MenuUnusedDays { get; set; }
    [ObservableProperty] public partial bool AutoSort { get; set; }
    [ObservableProperty] public partial int NewLocationIndex { get; set; }
    [ObservableProperty] public partial double ReviewInterval { get; set; }


    public JsonObject Hotkeys { get; private set; } = new();
    [ObservableProperty] public partial string Diagnostics { get; set; } = "正在读取后台状态";

    public JsonObject Settings { get; private set; } = new();
    public event Action<int>? ThemeChanged;
    public event Action<bool,byte>? WindowStyleChanged;


    private void SynchronizeList()
    {
        var wanted = all.Values.ToList();
        for (int i = Icons.Count - 1; i >= 0; i--) if (!wanted.Contains(Icons[i])) Icons.RemoveAt(i);

        foreach (var row in wanted) if (!Icons.Contains(row)) Icons.Add(row);
        for(int i=0;i<wanted.Count;i++)if(Icons[i]!=wanted[i])Icons.Move(Icons.IndexOf(wanted[i]),i);
    }
    public async Task InitializeAsync()
    {
        try
        {
            var result = await Call("settings.read"); Settings = (JsonObject)result["data"]!.DeepClone();
            Startup = Settings["startup"]?.GetValue<bool>() ?? false; OwnVisible = Settings["ownIcon"]!["visible"]!.GetValue<bool>(); OwnAsset = Settings["ownIcon"]!["asset"]!.GetValue<string>(); SafeMode = Settings["safeMode"]?.GetValue<bool>() ?? false;
            OwnTip=Settings["ownIcon"]?["tip"]?.GetValue<string>()??"托盘图标助手";OwnInList=Settings["ownIcon"]?["showInList"]?.GetValue<bool>()??true;
            Hotkeys=(JsonObject?)Settings["hotkeys"]?.DeepClone()??new();
            var menu=Settings["menu"]; MenuProgramNames=menu?["programNames"]?.GetValue<bool>()??true;
            MenuReverse=menu?["reverse"]?.GetValue<bool>()??false;MenuNewFirst=menu?["newFirst"]?.GetValue<bool>()??true;
            MenuHoverDelay=menu?["hoverDelayMs"]?.GetValue<int>()??0;MenuUnusedDays=menu?["filterUnusedDays"]?.GetValue<int>()??0;
            MenuActionIndex=menu?["action"]?.GetValue<string>() switch { "click"=>0,"double"=>2,"rightDouble"=>3,_=>1 };
            AutoSort=Settings["autoSort"]?.GetValue<bool>()??false;NewLocationIndex=(Settings["newIconLocation"]?.GetValue<int>()??-1)+1;
            ReviewInterval=Settings["reviewIntervalSeconds"]?.GetValue<int>()??0;
            ThemeIndex = Settings["theme"]!.GetValue<string>() switch { "light" => 1, "dark" => 2, _ => 0 }; ThemeChanged?.Invoke(ThemeIndex);
            LanguageIndex=Settings["language"]?.GetValue<string>()=="en"?1:0;
            TaskbarOpacity=Settings["taskbar"]?["opacity"]?.GetValue<int>()??255;HideStart=Settings["taskbar"]?["hideStart"]?.GetValue<bool>()??false;UiTopmost=Settings["uiTopmost"]?.GetValue<bool>()??false;UiOpacity=Settings["uiOpacity"]?.GetValue<int>()??255;WindowStyleChanged?.Invoke(UiTopmost,(byte)UiOpacity);
            var aumid = global::Windows.ApplicationModel.Package.Current.Id.FamilyName + "!App";
            await Call("settings.apply", new() { ["uiActivation"] = aumid }); await RefreshAsync();
        }
        catch (Exception ex) { IsReady=false;IsLoading=false;ReportError(ex,true); }
    }
    public async Task RefreshAsync()
    {
        if (polling || IsDragging) return; polling = true;
        try
        {
            var health = await Call("status.read"); var h = health["data"]!; if(!h["ready"]!.GetValue<bool>()||!h["connected"]!.GetValue<bool>()){IsReady=false;IsLoading=true;return;} var result = await Call("catalog.read"); var data = result["data"]!["icons"]!.AsArray(); var active = new HashSet<string>();
            foreach (var item in data) { var j = item!.AsObject(); var key = j["key"]!.GetValue<string>(); active.Add(key); if (all.TryGetValue(key, out var row)) row.Update(j); else all[key] = new IconRow(j); }
            foreach (var key in all.Keys.Where(k => !active.Contains(k)).ToList()) all.Remove(key);
            SynchronizeList(); 
            if (Selected != null && !active.Contains(Selected.Key)) {
                var prior=Selected.Rule;
                var matches=all.Values.Where(r=>r.Executable.Equals(Selected.Executable,StringComparison.OrdinalIgnoreCase)&&r.Rule["selector"]?["uid"]?.ToJsonString()==prior["selector"]?["uid"]?.ToJsonString()&&r.Rule["selector"]?["guid"]?.ToJsonString()==prior["selector"]?["guid"]?.ToJsonString()&&r.Rule["pid"]?.ToJsonString()==prior["pid"]?.ToJsonString()&&r.Rule["created"]?.ToJsonString()==prior["created"]?.ToJsonString()).ToList();
                Selected=matches.Count==1?matches[0]:null;
            }
                        Diagnostics = $"后台版本  {h["version"]}\n连接状态  {(h["connected"]!.GetValue<bool>() ? "原生桥接已连接" : "等待 Explorer / 安全模式")}\n累计源事件  {h["events"]}\n回调 p95  {h["callbackP95Micros"]} µs\n后台 PID  {h["pid"]}\n配置恢复  {h["recovered"]}";
            Diagnostics += $"\n快捷键冲突  {h["hotkeyErrors"]}\n任务栏设置  {h["environment"]?["detail"]}";
            IsReady=true;IsLoading=false;if(connectionError){Error="";connectionError=false;}
        }
        catch (Exception ex) { IsReady=false;IsLoading=false;ReportError(ex,true); }
        finally { polling = false; }
    }
    private void ReadEditor(IconRow row)
    {
        Hotkey=row.Rule["hotkey"]?.GetValue<int>()??0;HotkeyText=DescribeHotkey(Hotkey);HotkeyAction=row.Rule["hotkeyAction"]?.GetValue<int>()??1;
        NewStateIndex=(row.Rule["newState"]?.GetValue<int>()??-1)+1;
        var j = row.Rule; SwapButtons=j["swapButtons"]?.GetValue<bool>()??false;MatchClass=j["selector"]?["windowClass"]?.GetValue<string>()??"";MatchTip=j["selector"]?["tipPattern"]?.GetValue<string>()??"";MatchTipMode=j["selector"]?["tipMode"]?.GetValue<int>()??0;CurrentInstance=(j["selector"]?["ownerPid"]?.GetValue<int>()??0)>0;AssetIndex=j["assetIndex"]?.GetValue<int>()??0; Asset = j["asset"]!.GetValue<string>(); Tip = j["tip"]!.GetValue<string>(); VisibilityIndex = j["visibility"]!.GetValue<int>() + 1; LocationIndex = j["location"]!.GetValue<int>() + 1; Order = j["order"]!.GetValue<int>(); InactiveSeconds = j["inactiveSeconds"]!.GetValue<int>(); UnusedDays = j["unusedDays"]!.GetValue<int>(); MenuVisible = j["menuVisible"]!.GetValue<bool>(); AlwaysShow = j["alwaysShow"]!.GetValue<bool>();
    }
    private async Task<JsonObject> Call(string method, JsonObject? data = null)
    {
        var result = await Pipe.RequestAsync(method, data); if (result["success"]?.GetValue<bool>() != true) throw new IOException((result["saved"]?.GetValue<bool>() == true ? "已保存，但未生效：" : "") + (result["detail"]?.GetValue<string>() ?? "操作失败")); return result;
    }
    public void ReportError(Exception ex,bool connection=false){Status=ex.Message;Error=ex.Message;connectionError=connection;}
    private async Task Execute(Func<Task> action) { try { Error="";connectionError=false;await action(); } catch (Exception ex) { ReportError(ex); } }
    [RelayCommand] private async Task ReconnectAsync(){Pipe.AllowStart();IsReady=false;IsLoading=true;Error="";await InitializeAsync();}
    [RelayCommand] private Task SaveRuleAsync() => Execute(async () =>
    {
        if (Selected == null) return; var row = Selected;
        if(row.Owned){var read=await Call("settings.read");var own=(JsonObject)read["data"]!["ownIcon"]!.DeepClone();own["asset"]=Asset;own["tip"]=Tip.Length>0?Tip:"托盘图标助手";await Call("ownIcon.apply",own);OwnAsset=Asset;OwnTip=own["tip"]!.GetValue<string>();Status="已生效";await RefreshAsync();return;}
        undo.Push((row.Key, EditableRule(row))); var j = EditableRule(row);
        if (Asset.Length>0 && (Asset != row.Rule["asset"]!.GetValue<string>() || row.Rule["baselineWidth"]?.GetValue<int>() == 0)) { j["baselineFingerprint"] = row.Rule["sourceFingerprint"]!.DeepClone(); j["baselineWidth"] = row.Rule["sourceWidth"]!.DeepClone(); j["baselineHeight"] = row.Rule["sourceHeight"]!.DeepClone(); } j["selector"]!["windowClass"]=MatchClass;j["selector"]!["tipPattern"]=MatchTip;j["selector"]!["tipMode"]=MatchTipMode;j["selector"]!["ownerPid"]=CurrentInstance?row.Rule["pid"]!.DeepClone():JsonValue.Create(0);j["selector"]!["ownerCreatedAt"]=CurrentInstance?row.Rule["created"]!.DeepClone():JsonValue.Create(0);j["swapButtons"]=SwapButtons;j["assetIndex"]=(int)AssetIndex;j["asset"] = Asset; j["tip"] = Tip; j["visibility"] = VisibilityIndex - 1; j["location"] = LocationIndex - 1; j["order"] = (int)Order; j["inactiveSeconds"] = (int)InactiveSeconds; j["unusedDays"] = (int)UnusedDays; j["menuVisible"] = MenuVisible; j["alwaysShow"] = AlwaysShow;
        j["enabled"]=true;j["hotkey"]=Hotkey;j["hotkeyAction"]=HotkeyAction;j["newState"]=NewStateIndex-1;
        var result = await Call("rule.apply", new() { ["rule"] = j, ["previousSelector"] = row.Rule["selector"]!.DeepClone() }); Status = result["detail"]?.GetValue<string>() ?? "规则已保存"; await RefreshAsync();
    });
    [RelayCommand] private Task ResetRuleAsync() => Execute(async () => { if (Selected == null) return; if(Selected.Owned){var read=await Call("settings.read");var own=(JsonObject)read["data"]!["ownIcon"]!.DeepClone();own["asset"]="";own["tip"]="托盘图标助手";await Call("ownIcon.apply",own);OwnAsset="";OwnTip="托盘图标助手";Asset="";Tip="托盘图标助手";}else{undo.Push((Selected.Key, EditableRule(Selected)));await Call("rule.remove", new() { ["rule"] = EditableRule(Selected) });} Status = "已恢复默认设置"; await RefreshAsync(); if (Selected != null) ReadEditor(Selected); });
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
    [RelayCommand] private void UseOriginalAsset() { Asset=""; AssetIndex=0; }
    [RelayCommand] private Task ChooseOwnIconAsync() => Execute(async () => { var path = await ChooseAssetAsync(); if (path != null) OwnAsset = path; });
    [RelayCommand] private Task SavePreferencesAsync() => Execute(async () => { var theme = new[] { "system", "light", "dark" }[ThemeIndex]; await Call("settings.apply", new() { ["startup"] = Startup, ["theme"] = theme, ["safeMode"] = SafeMode,["language"]=LanguageIndex==1?"en":"zh" }); await Call("ownIcon.apply", new() { ["visible"] = OwnVisible, ["asset"] = OwnAsset, ["tip"] = OwnTip, ["showInList"]=OwnInList }); ThemeChanged?.Invoke(ThemeIndex); Status = "偏好已保存并应用"; });







    [RelayCommand] private Task SaveMenuAsync()=>Execute(async()=>{await Call("settings.apply",new(){["menu"]=new JsonObject{["programNames"]=MenuProgramNames,["reverse"]=MenuReverse,["newFirst"]=MenuNewFirst,["hoverDelayMs"]=(int)MenuHoverDelay,["filterUnusedDays"]=(int)MenuUnusedDays,["action"]=new[]{"click","menu","double","rightDouble"}[MenuActionIndex]},["hotkeys"]=Hotkeys.DeepClone(),["autoSort"]=AutoSort,["newIconLocation"]=NewLocationIndex-1,["reviewIntervalSeconds"]=(int)ReviewInterval});Status="菜单、快捷键和排序设置已应用";});
    public static string DescribeHotkey(int code){if(code==0)return "未设置";var modifiers=code>>16;return ((modifiers&2)!=0?"Ctrl + ":"")+((modifiers&1)!=0?"Alt + ":"")+((modifiers&4)!=0?"Shift + ":"")+((modifiers&8)!=0?"Win + ":"")+(global::Windows.System.VirtualKey)(code&65535);}
    public Task ApplyOrderAsync(bool reverse=false,bool alphabetic=false)=>Execute(async()=>{var ordered=Icons.ToList();if(reverse)ordered.Reverse();if(alphabetic)ordered=ordered.OrderBy(r=>r.Name,StringComparer.CurrentCultureIgnoreCase).ToList();var list=new JsonArray();var positions=new int[2];foreach(var row in ordered){var placement=row.Rule["placement"]!.GetValue<int>();if(placement>1)continue;var rule=EditableRule(row);rule["location"]=placement;rule["order"]=positions[placement]++;list.Add(rule);}await Call("rules.apply",new(){["rules"]=list});Status="顺序已应用到实际托盘";await RefreshAsync();});
    public Task BatchVisibilityAsync(int visibility)=>Execute(async()=>{if(BatchSelection.Count==0){Status="请先选择图标；按 Ctrl / Shift 可选择多个。";return;}var list=new JsonArray();foreach(var row in BatchSelection){var rule=EditableRule(row);rule["visibility"]=visibility;list.Add(rule);}await Call("rules.apply",new(){["rules"]=list});Status="批量可见性已应用";await RefreshAsync();});
    public async Task MoveSelectedAsync(int direction){if(Selected==null)return;int current=Icons.IndexOf(Selected),next=current+direction;if(next<0||next>=Icons.Count||Icons[next].Rule["placement"]!.GetValue<int>()!=Selected.Rule["placement"]!.GetValue<int>())return;Icons.Move(current,next);await ApplyOrderAsync();}
    [RelayCommand] private Task StopAgentAsync() => Execute(async () => { await Call("agent.stop"); Status = "后台已停止，托盘已恢复"; });
    [RelayCommand] private Task SaveAdvancedAsync()=>Execute(async()=>{await Call("settings.apply",new(){["uiTopmost"]=UiTopmost,["uiOpacity"]=(int)UiOpacity});await Call("taskbar.apply",new(){["opacity"]=(int)TaskbarOpacity,["hideStart"]=HideStart});WindowStyleChanged?.Invoke(UiTopmost,(byte)UiOpacity);Status="窗口与任务栏设置已应用";});



}







