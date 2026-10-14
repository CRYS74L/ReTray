#if TRAY_WPF
using System.Windows.Threading;
#else
using Microsoft.UI.Xaml;
#endif
using System.ComponentModel;
using System.Text.Json.Nodes;
using TrayAssistant_App.Models;
using Localization = TrayAssistant_App.Services.Localization;

namespace TrayAssistant_App.ViewModels;

public partial class TrayViewModel
{
    private readonly DispatcherTimer autosaveTimer = new() { Interval = TimeSpan.FromMilliseconds(180) };
    private readonly Dictionary<string, Func<Task>> pendingEdits = new();
    private readonly SemaphoreSlim saveGate = new(1, 1);
    private bool readingEditor;

    public TrayViewModel() => autosaveTimer.Tick += async (_, _) => await FlushChangesAsync();

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (!initialized || readingEditor || App.IsExiting) return;
        switch (e.PropertyName)
        {
            case nameof(Startup):
            case nameof(ThemeIndex):
            case nameof(LanguageIndex):
                if (ThemeIndex is < 0 or > 2 || LanguageIndex<0 || LanguageIndex>=Localization.LanguageCodes.Length) return;
                var preferences = new JsonObject();
                if (e.PropertyName == nameof(Startup)) preferences["startup"] = Startup;
                else if (e.PropertyName == nameof(ThemeIndex)) preferences["theme"] = new[] { "system", "light", "dark" }[ThemeIndex];
                else preferences["language"] = Localization.LanguageCodes[LanguageIndex];
                QueueEdit("preference:" + e.PropertyName, async () => await Call("settings.apply", preferences));
                break;
            case nameof(OwnVisible):
            case nameof(OwnAsset):
            case nameof(OwnTip):
            case nameof(OwnInList):
                var own = new JsonObject { ["visible"] = OwnVisible, ["asset"] = OwnAsset, ["tip"] = OwnTip, ["showInList"] = OwnInList };
                QueueEdit("ownIcon", async () =>
                {
                    var read = await Call("settings.read");
                    var merged = (JsonObject)read["data"]!["ownIcon"]!.DeepClone();
                    foreach (var entry in own) merged[entry.Key] = entry.Value?.DeepClone();
                    if (!string.IsNullOrEmpty(own["asset"]!.GetValue<string>()))
                        merged["replacementAsset"] = own["asset"]!.DeepClone();
                    await Call("ownIcon.apply", merged); await RefreshAsync();
                });
                break;
            case nameof(UiTopmost):
            case nameof(UiOpacity):
                var topmost = UiTopmost; var opacity = (byte)UiOpacity;
                var window = new JsonObject { ["uiTopmost"] = topmost, ["uiOpacity"] = (int)opacity };
                WindowStyleChanged?.Invoke(topmost, opacity);
                QueueEdit("window", async () => await Call("settings.apply", window));
                break;
            case nameof(HideStart):
            case nameof(TaskbarOpacity):
                if (!double.IsFinite(TaskbarOpacity)) return;
                var taskbar = new JsonObject { ["hideStart"] = HideStart, ["opacity"] = (int)TaskbarOpacity };
                QueueEdit("taskbar", async () => await Call("taskbar.apply", taskbar));
                break;
            case nameof(AutoSort):
                QueueMenuPreferences();
                break;
            case nameof(Asset):
            case nameof(UseReplacement):
            case nameof(AssetIndex):
            case nameof(Tip):
            case nameof(SwapButtons):
            case nameof(InactiveSeconds):
            case nameof(Hotkey):
            case nameof(HotkeyAction):
                QueueEditor();
                break;
        }
    }

    private void QueueEdit(string key, Func<Task> apply)
    {
        pendingEdits[key] = apply;
        autosaveTimer.Stop(); autosaveTimer.Start();
    }

    public async Task<bool> FlushChangesAsync()
    {
        autosaveTimer.Stop();
        await saveGate.WaitAsync();
        try
        {
            while (pendingEdits.Count > 0)
            {
                var batch = pendingEdits.ToArray(); pendingEdits.Clear();
                for (var i = 0; i < batch.Length; i++)
                {
                    try { await batch[i].Value(); }
                    catch (Exception ex)
                    {
                        for (var remaining = i; remaining < batch.Length; remaining++)
                            pendingEdits.TryAdd(batch[remaining].Key, batch[remaining].Value);
                        ReportError(ex); return false;
                    }
                }
            }
            Error = "";
            return true;
        }
        finally { saveGate.Release(); }
    }

    public void QueueMenuPreferences()
    {
        if (!initialized || readingEditor) return;
        var data = new JsonObject { ["autoSort"] = AutoSort, ["hotkeys"] = Hotkeys.DeepClone() };
        QueueEdit("menu", async () => { await Call("settings.apply", data); await RefreshAsync(); });
    }

    private void QueueEditor()
    {
        if (Selected is not IconRow row || !double.IsFinite(AssetIndex) || !double.IsFinite(InactiveSeconds) || HotkeyAction is < 0 or > 3) return;
        if (row.Owned)
        {
            var remembered = Asset; var asset = UseReplacement ? Asset : ""; var tip = Tip.Length > 0 ? Tip : "托盘图标助手";
            QueueEdit("ownIcon", async () =>
            {
                var read = await Call("settings.read");
                var own = (JsonObject)read["data"]!["ownIcon"]!.DeepClone();
                own["asset"] = asset; own["replacementAsset"] = remembered; own["tip"] = tip;
                await Call("ownIcon.apply", own);
                readingEditor = true;
                try { OwnAsset = asset; OwnTip = tip; }
                finally { readingEditor = false; }
                await RefreshAsync();
            });
            return;
        }
        var rule = BuildEditorRule(row);
        var previous = row.Rule["selector"]!.DeepClone();
        QueueEdit("rule:" + row.StableKey, async () =>
        {
            await Call("rule.apply", new() { ["key"] = row.Key, ["rule"] = rule.DeepClone(), ["previousSelector"] = previous.DeepClone() });
            await RefreshAsync();
        });
    }

    private JsonObject BuildEditorRule(IconRow row)
    {
        var rule = EditableRule(row);
        if (UseReplacement && Asset.Length > 0 && (Asset != row.Rule["asset"]!.GetValue<string>() || row.Rule["baselineWidth"]?.GetValue<int>() == 0))
        {
            rule["baselineFingerprint"] = row.Rule["sourceFingerprint"]!.DeepClone();
            rule["baselineWidth"] = row.Rule["sourceWidth"]!.DeepClone();
            rule["baselineHeight"] = row.Rule["sourceHeight"]!.DeepClone();
        }
        rule["selector"]!["windowClass"] = MatchClass;
        rule["selector"]!["tipPattern"] = MatchTip;
        rule["selector"]!["tipMode"] = MatchTipMode;
        rule["selector"]!["ownerPid"] = CurrentInstance ? row.Rule["pid"]!.DeepClone() : JsonValue.Create(0);
        rule["selector"]!["ownerCreatedAt"] = CurrentInstance ? row.Rule["created"]!.DeepClone() : JsonValue.Create(0);
        rule["asset"] = UseReplacement ? Asset : ""; rule["assetIndex"] = UseReplacement ? (int)AssetIndex : 0;
        rule["replacementAsset"] = Asset; rule["replacementAssetIndex"] = (int)AssetIndex; rule["tip"] = Tip;
        rule["swapButtons"] = SwapButtons; rule["visibility"] = VisibilityIndex - 1;
        rule["order"] = (int)Order;
        rule["inactiveSeconds"] = (int)InactiveSeconds; rule["unusedDays"] = (int)UnusedDays;
        rule["menuVisible"] = MenuVisible; rule["alwaysShow"] = AlwaysShow;
        rule["enabled"] = true; rule["hotkey"] = Hotkey; rule["hotkeyAction"] = HotkeyAction;
        rule["newState"] = NewStateIndex - 1;
        return rule;
    }
}
