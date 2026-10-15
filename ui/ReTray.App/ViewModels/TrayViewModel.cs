using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
#if TRAY_WPF
using Microsoft.Win32;
using System.Windows.Input;
using WriteableBitmap = System.Windows.Media.Imaging.WriteableBitmap;
#else
using Microsoft.Windows.Storage.Pickers;
using WriteableBitmap = Microsoft.UI.Xaml.Media.Imaging.WriteableBitmap;
#endif
using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using ReTray_App.Models;
using ReTray_App.Services;

namespace ReTray_App.ViewModels;
public partial class TrayViewModel : ObservableObject
{
    public PipeClient Pipe { get; } = new();
    public ObservableCollection<IconRow> Icons { get; } = new();



    private readonly Dictionary<string, IconRow> all = new();
    private readonly Dictionary<string, (long Revision, JsonObject Data)> visibilityAcknowledgments = new();

    private bool polling, connectionError, initialized, initializing;
    private List<string> iconOrder = new();
    private IconRow? selected;
    public IconRow? Selected { get => selected; set { if (SetProperty(ref selected, value)) { OnPropertyChanged(nameof(HasSelection)); if(value != null) ReadEditor(value); } } }
    public bool HasSelection => Selected != null;
    private bool isDragging;
    private long dragGeneration;
    public bool IsDragging { get=>isDragging;set{if(isDragging==value)return;isDragging=value;if(value)dragGeneration++;} }
    public async Task SetRowVisibilityAsync(IconRow row,bool value)
    {
        if(row.IsBusy||value==row.ActualVisible)return;row.IsBusy=true;
        await Execute(async()=>{var reply=await Call("icon.visibility",new(){["key"]=row.Key,["visibility"]=value?0:1});var acknowledgment=(JsonObject)reply["data"]!.DeepClone();visibilityAcknowledgments[row.Key]=(reply["revision"]!.GetValue<long>(),acknowledgment);row.AcknowledgeVisibility(acknowledgment);if(row.Owned){readingEditor=true;try{OwnVisible=value;}finally{readingEditor=false;}}Status=value?"已显示":"已隐藏";});
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
        var result=new JsonObject();foreach(var key in new[]{"selector","visibility","location","order","asset","replacementAsset","replacementAssetIndex","tip","enabled","menuVisible","alwaysShow","inactiveSeconds","unusedDays","hotkey","hotkeyAction","newState","swapButtons","baselineFingerprint","baselineWidth","baselineHeight","assetIndex"})
            if(row.Rule[key]!=null)result[key]=row.Rule[key]!.DeepClone();return result;
    }
    [ObservableProperty] public partial string Status { get; set; } = "";
    [ObservableProperty] public partial string Error { get; set; } = "";
    [ObservableProperty] public partial string ConfigurationStatus { get; set; } = "";
    [ObservableProperty] public partial string ConfigurationBackupPath { get; set; } = "";
    [ObservableProperty] public partial bool IsLoading { get; set; } = true;
    [ObservableProperty] public partial bool IsReady { get; set; }

    [ObservableProperty] public partial string Asset { get; set; } = "";
    [ObservableProperty] public partial bool UseReplacement { get; set; }
    [ObservableProperty] public partial WriteableBitmap? AssetPreview { get; set; }
    [ObservableProperty] public partial string AssetSummary { get; set; } = "";
    partial void OnAssetChanged(string value) => _ = PreviewAssetAsync();
    partial void OnAssetIndexChanged(double value) => _ = PreviewAssetAsync();
    public Task PreviewAssetAsync()=>Execute(async()=>{if(string.IsNullOrEmpty(Asset)){AssetPreview=null;AssetSummary="";return;}var path=Asset;int index=(int)AssetIndex;var result=await Call("icon.preview",new(){["path"]=path,["index"]=index});if(path!=Asset||index!=(int)AssetIndex)return;var image=result["data"]!;AssetPreview=IconRow.Decode(image["pixels"]!.GetValue<string>(),image["width"]!.GetValue<int>(),image["height"]!.GetValue<int>());AssetSummary=$"{image["count"]} 个可选图标 · 当前序号 {index}";});
    [ObservableProperty] public partial string Tip { get; set; } = "";
    [ObservableProperty] public partial int VisibilityIndex { get; set; }
    [ObservableProperty] public partial double Order { get; set; }
    [ObservableProperty] public partial double InactiveSeconds { get; set; }
    [ObservableProperty] public partial double UnusedDays { get; set; }
    [ObservableProperty] public partial bool MenuVisible { get; set; } = true;
    [ObservableProperty] public partial bool AlwaysShow { get; set; }
    [ObservableProperty] public partial bool Startup { get; set; }
    [ObservableProperty] public partial bool OwnVisible { get; set; } = true;
    [ObservableProperty] public partial bool OwnInList { get; set; } = true;
    [ObservableProperty] public partial string OwnTip { get; set; } = "ReTray";
    [ObservableProperty] public partial string OwnAsset { get; set; } = "";

    [ObservableProperty] public partial int ThemeIndex { get; set; }
    [ObservableProperty] public partial int LanguageIndex { get; set; }
    public event Action? LanguageChanged;
    partial void OnLanguageIndexChanged(int value){if(value<0 || value>=Localization.LanguageCodes.Length)return;Localization.CurrentLanguage=Localization.LanguageCodes[value];Status=Localization.Text(Status);Error=Localization.Text(Error);ConfigurationStatus=Localization.Text(ConfigurationStatus);LanguageChanged?.Invoke();}
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
    [ObservableProperty] public partial double ReviewInterval { get; set; }


    public JsonObject Hotkeys { get; private set; } = new();

    public JsonObject Settings { get; private set; } = new();
    public event Action<int>? ThemeChanged;
    partial void OnThemeIndexChanged(int value){if(value is <0 or >2)return;App.DispatcherQueue.TryEnqueue(()=>ThemeChanged?.Invoke(value));}
    public event Action<bool,byte>? WindowStyleChanged;


    private void SynchronizeList()
    {
        var ranks=iconOrder.Select((key,index)=>(key,index)).ToDictionary(x=>x.key,x=>x.index); var wanted = AutoSort?all.Values.OrderBy(row=>Path.GetFileName(row.Executable),StringComparer.OrdinalIgnoreCase).ToList():all.Values.OrderBy(row=>ranks.GetValueOrDefault(row.StableKey,int.MaxValue)).ToList();
        for (int i = Icons.Count - 1; i >= 0; i--) if (!wanted.Contains(Icons[i])) Icons.RemoveAt(i);

        foreach (var row in wanted) if (!Icons.Contains(row)) Icons.Add(row);
        for(int i=0;i<wanted.Count;i++)if(Icons[i]!=wanted[i])Icons.Move(Icons.IndexOf(wanted[i]),i);
    }
    public async Task InitializeAsync(JsonObject? importedSettings = null)
    {
        if (initializing) return;
        initializing = true;
        try
        {
            initialized=false;var result = importedSettings==null?await Call("settings.read"):new JsonObject{["data"]=importedSettings.DeepClone()}; Settings = (JsonObject)result["data"]!.DeepClone();iconOrder=Settings["iconOrder"]?.AsArray().Select(x=>x!.GetValue<string>()).ToList()??new();
            Startup = Settings["startup"]?.GetValue<bool>() ?? false; OwnVisible = Settings["ownIcon"]!["visible"]!.GetValue<bool>(); OwnAsset = Settings["ownIcon"]!["asset"]!.GetValue<string>();
            OwnTip=Settings["ownIcon"]?["tip"]?.GetValue<string>()??"ReTray";OwnInList=Settings["ownIcon"]?["showInList"]?.GetValue<bool>()??true;
            Hotkeys=(JsonObject?)Settings["hotkeys"]?.DeepClone()??new();Hotkeys.Remove("restoreIcons");
            var menu=Settings["menu"]; MenuProgramNames=menu?["programNames"]?.GetValue<bool>()??true;
            MenuReverse=menu?["reverse"]?.GetValue<bool>()??false;MenuNewFirst=menu?["newFirst"]?.GetValue<bool>()??true;
            MenuHoverDelay=menu?["hoverDelayMs"]?.GetValue<int>()??0;MenuUnusedDays=menu?["filterUnusedDays"]?.GetValue<int>()??0;
            MenuActionIndex=menu?["action"]?.GetValue<string>() switch { "click"=>0,"double"=>2,"rightDouble"=>3,_=>1 };
            AutoSort=Settings["autoSort"]?.GetValue<bool>()??false;
            ReviewInterval=Settings["reviewIntervalSeconds"]?.GetValue<int>()??0;
            ThemeIndex = Settings["theme"]!.GetValue<string>() switch { "light" => 1, "dark" => 2, _ => 0 }; ThemeChanged?.Invoke(ThemeIndex);
            LanguageIndex=Localization.LanguageIndex(Settings["language"]?.GetValue<string>());
            Localization.CurrentLanguage=Localization.LanguageCodes[LanguageIndex];LanguageChanged?.Invoke();
            TaskbarOpacity=Settings["taskbar"]?["opacity"]?.GetValue<int>()??255;HideStart=Settings["taskbar"]?["hideStart"]?.GetValue<bool>()??false;UiTopmost=Settings["uiTopmost"]?.GetValue<bool>()??false;UiOpacity=Settings["uiOpacity"]?.GetValue<int>()??255;WindowStyleChanged?.Invoke(UiTopmost,(byte)UiOpacity);
#if TRAY_WPF
            var aumid = App.UiActivationId;
#else
            var aumid = global::Windows.ApplicationModel.Package.Current.Id.FamilyName + "!App";
#endif
            await Call("settings.apply", new() { ["uiActivation"] = aumid }, importedSettings==null?null:TimeSpan.FromSeconds(60));
            await Call("ui.attach", new() { ["hwnd"] = (long)App.WindowHandle }, importedSettings==null?null:TimeSpan.FromSeconds(60));
            await RefreshAsync();initialized=true;
        }
        catch (Exception ex) { IsReady=false;IsLoading=false;ReportError(ex,true); }
        finally { initializing = false; }
    }
    public async Task RefreshAsync()
    {
        if (polling || IsDragging) return; polling = true;var observedDrag=dragGeneration;
        try
        {
            var health = await Call("status.read"); var h = health["data"]!; App.ObserveAgent(h["pid"]!.GetValue<int>()); if(!h["ready"]!.GetValue<bool>()||!h["connected"]!.GetValue<bool>()){IsReady=false;IsLoading=true;return;}
            if (!initialized && !initializing) { await InitializeAsync(); if (!initialized) return; }
            var result = await Call("catalog.read");
            if(IsDragging||observedDrag!=dragGeneration)return;
            var data = result["data"]!["icons"]!.AsArray(); var active = new HashSet<string>();var snapshotRevision=result["revision"]!.GetValue<long>();
            foreach (var item in data) { var j = item!.AsObject(); var key = j["key"]!.GetValue<string>(); active.Add(key);JsonObject? retained=null;if(visibilityAcknowledgments.TryGetValue(key,out var acknowledgment)){if(snapshotRevision<acknowledgment.Revision)retained=acknowledgment.Data;else visibilityAcknowledgments.Remove(key);}if(all.TryGetValue(key,out var row))row.Update(j,retained);else all[key]=new IconRow(j,retained); }
            foreach (var key in all.Keys.Where(k => !active.Contains(k)).ToList()) { all.Remove(key);visibilityAcknowledgments.Remove(key); }
            SynchronizeList();
            if (Selected != null && !active.Contains(Selected.Key)) {
                var prior=Selected.Rule;
                var matches=all.Values.Where(r=>r.Executable.Equals(Selected.Executable,StringComparison.OrdinalIgnoreCase)&&r.Rule["selector"]?["uid"]?.ToJsonString()==prior["selector"]?["uid"]?.ToJsonString()&&r.Rule["selector"]?["guid"]?.ToJsonString()==prior["selector"]?["guid"]?.ToJsonString()&&r.Rule["pid"]?.ToJsonString()==prior["pid"]?.ToJsonString()&&r.Rule["created"]?.ToJsonString()==prior["created"]?.ToJsonString()).ToList();
                Selected=matches.Count==1?matches[0]:null;
            }
            IsReady=true;IsLoading=false;if(connectionError){Error="";connectionError=false;}
        }
        catch (Exception ex) { IsReady=false;IsLoading=false;ReportError(ex,true); }
        finally { polling = false; }
    }
    private void ReadEditor(IconRow row)
    {
        readingEditor = true;
        try {
        Hotkey=row.Rule["hotkey"]?.GetValue<int>()??0;HotkeyText=DescribeHotkey(Hotkey);HotkeyAction=row.Rule["hotkeyAction"]?.GetValue<int>()??1;
        NewStateIndex=(row.Rule["newState"]?.GetValue<int>()??-1)+1;
        var j = row.Rule; SwapButtons=j["swapButtons"]?.GetValue<bool>()??false;MatchClass=j["selector"]?["windowClass"]?.GetValue<string>()??"";MatchTip=j["selector"]?["tipPattern"]?.GetValue<string>()??"";MatchTipMode=j["selector"]?["tipMode"]?.GetValue<int>()??0;CurrentInstance=(j["selector"]?["ownerPid"]?.GetValue<int>()??0)>0;AssetIndex=j["assetIndex"]?.GetValue<int>()??0; Asset = j["asset"]!.GetValue<string>(); Tip = j["tip"]!.GetValue<string>(); VisibilityIndex = j["visibility"]!.GetValue<int>() + 1; Order = j["order"]!.GetValue<int>(); InactiveSeconds = j["inactiveSeconds"]!.GetValue<int>(); UnusedDays = j["unusedDays"]!.GetValue<int>(); MenuVisible = j["menuVisible"]!.GetValue<bool>(); AlwaysShow = j["alwaysShow"]!.GetValue<bool>();
        UseReplacement = !string.IsNullOrEmpty(j["asset"]!.GetValue<string>());
        AssetIndex = j["replacementAssetIndex"]?.GetValue<int>() ?? AssetIndex;
        Asset = j["replacementAsset"]?.GetValue<string>() ?? Asset;
        }
        finally { readingEditor = false; }
    }
    private async Task<JsonObject> Call(string method, JsonObject? data = null, TimeSpan? timeout = null)
    {
        var result = await Pipe.RequestAsync(method, data, timeout: timeout); if (result["success"]?.GetValue<bool>() != true) throw new IOException((result["saved"]?.GetValue<bool>() == true ? "已保存，但未生效：" : "") + (result["detail"]?.GetValue<string>() ?? "操作失败")); return result;
    }
    public void ReportError(Exception ex,bool connection=false){Status=Localization.Text(ex.Message);Error=Localization.Text(ex.Message);connectionError=connection;}
    private async Task Execute(Func<Task> action) { try { Error="";connectionError=false;await action(); } catch (Exception ex) { ReportError(ex); } }
    [RelayCommand] private async Task ReconnectAsync(){Pipe.AllowStart();if(!await FlushChangesAsync())return;IsReady=false;IsLoading=true;Error="";await InitializeAsync();}
    public static async Task<string?> ChooseAssetAsync()
    {
#if TRAY_WPF
        var picker = new OpenFileDialog
        {
            Title = Localization.Text("选择图标…"),
            Filter = Localization.Text("图标资源")+"|*.ico;*.png;*.bmp;*.jpg;*.exe;*.dll",
            CheckFileExists = true,
            Multiselect = false
        };
        var owner = System.Windows.Application.Current?.MainWindow;
        if ((owner == null ? picker.ShowDialog() : picker.ShowDialog(owner)) != true) return null;
        var path = picker.FileName;
#else
        var picker = new FileOpenPicker(App.Window.AppWindow.Id); foreach (var ext in new[] { ".ico", ".png", ".bmp", ".jpg", ".exe", ".dll" }) picker.FileTypeFilter.Add(ext);
        var file = await picker.PickSingleFileAsync(); if (file == null) return null;
        var path = file.Path;
#endif
        var info = new FileInfo(path); if (info.Length > 32 * 1024 * 1024) throw new IOException("图标资源不能超过 32 MB。");
        var bytes = await File.ReadAllBytesAsync(path); var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ReTray", "Assets"); Directory.CreateDirectory(directory); var target = Path.Combine(directory, Convert.ToHexString(SHA256.HashData(bytes)) + Path.GetExtension(path).ToLowerInvariant()); if (!File.Exists(target)) await File.WriteAllBytesAsync(target, bytes); return target;
    }
    [RelayCommand] private Task ChooseIconAsync() => Execute(async () => { var path = await ChooseAssetAsync(); if (path != null) { UseReplacement = true; AssetIndex = 0; Asset = path; } });
    [RelayCommand] private void UseOriginalAsset() { UseReplacement=false; }
    [RelayCommand] private Task ChooseOwnIconAsync() => Execute(async () => { var path = await ChooseAssetAsync(); if (path != null) OwnAsset = path; });







    public async Task PersistOrderAsync(){var ordered=Icons.Select(row=>row.StableKey).ToList();if(ordered.SequenceEqual(iconOrder))return;try{var keys=new JsonArray();foreach(var key in ordered)keys.Add(key);await Call("icons.reorder",new(){["keys"]=keys});iconOrder=ordered;AutoSort=false;}catch(Exception ex){ReportError(ex);SynchronizeList();}}
    public static string DescribeHotkey(int code)
    {
        if(code==0)return "未设置";
        var modifiers=code>>16;
#if TRAY_WPF
        var key = KeyInterop.KeyFromVirtualKey(code & 65535).ToString();
#else
        var key = ((global::Windows.System.VirtualKey)(code & 65535)).ToString();
#endif
        return ((modifiers&2)!=0?"Ctrl + ":"")+((modifiers&1)!=0?"Alt + ":"")+((modifiers&4)!=0?"Shift + ":"")+((modifiers&8)!=0?"Win + ":"")+key;
    }



}
