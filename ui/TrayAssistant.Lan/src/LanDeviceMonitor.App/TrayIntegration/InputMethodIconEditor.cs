using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using LanDeviceMonitor.Controls;
using TrayAssistant_App.Models;
using TrayAssistant_App.Services;
using TrayAssistant_App.ViewModels;
using Localization = TrayAssistant_App.Services.Localization;

namespace TrayAssistant_App;

public sealed class InputMethodIconRow : ObservableObject
{
    private readonly InputMethodIconEditorModel owner;
    public string Key { get; }
    public string Name { get; }
    public ImageSource? Original { get; }
    private ImageSource? assetPreview;
    public ImageSource? AssetPreview { get => assetPreview; private set => SetProperty(ref assetPreview, value); }
    private bool useReplacement;
    public bool UseReplacement
    {
        get => useReplacement;
        set { if (SetProperty(ref useReplacement, value)) owner.Queue(this); }
    }
    public string Asset { get; private set; }
    public int AssetIndex { get; private set; }
    public IAsyncRelayCommand ChooseIconCommand { get; }
    internal InputMethodIconRow(InputMethodIconEditorModel owner, JsonObject data)
    {
        this.owner = owner;
        Key = data["key"]!.GetValue<string>(); Name = data["name"]!.GetValue<string>();
        Original = Decode(data["original"]); AssetPreview = Decode(data["replacement"]);
        var policy = data["policy"]!;
        Asset = policy["asset"]!.GetValue<string>(); AssetIndex = policy["index"]!.GetValue<int>();
        useReplacement = policy["useReplacement"]!.GetValue<bool>();
        ChooseIconCommand = new AsyncRelayCommand(() => owner.ChooseAsync(this));
    }
    internal void SetAsset(string asset, ImageSource preview)
    {
        Asset = asset; AssetIndex = 0; AssetPreview = preview;
        if (!UseReplacement) UseReplacement = true; else owner.Queue(this);
    }
    internal JsonObject Policy() => new()
    {
        ["key"] = Key, ["asset"] = Asset, ["index"] = AssetIndex, ["useReplacement"] = UseReplacement
    };
    internal static ImageSource? Decode(JsonNode? image) => image == null ? null : IconRow.Decode(
        image["pixels"]!.GetValue<string>(), image["width"]!.GetValue<int>(), image["height"]!.GetValue<int>());
}

public sealed class InputMethodIconEditorModel : ObservableObject
{
    private readonly PipeClient pipe;
    private readonly DispatcherTimer debounce = new() { Interval = TimeSpan.FromMilliseconds(180) };
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly Dictionary<string, JsonObject> pending = new();
    public ObservableCollection<InputMethodIconRow> Rows { get; } = new();
    private string error = "";
    public string Error { get => error; private set => SetProperty(ref error, value); }
    public InputMethodIconEditorModel(PipeClient pipe)
    {
        this.pipe = pipe;
        debounce.Tick += async (_, _) => await FlushAsync();
    }
    private static void Check(JsonObject reply)
    {
        if (reply["success"]?.GetValue<bool>() != true) throw new IOException(reply["detail"]?.GetValue<string>() ?? "操作失败");
    }
    public async Task LoadAsync()
    {
        var reply = await pipe.RequestAsync("inputMethods.read"); Check(reply);
        foreach (var row in reply["data"]!.AsArray()) Rows.Add(new InputMethodIconRow(this, row!.AsObject()));
    }
    internal void Queue(InputMethodIconRow row)
    {
        pending[row.Key] = row.Policy(); debounce.Stop(); debounce.Start();
    }
    internal async Task ChooseAsync(InputMethodIconRow row)
    {
        try
        {
            var path = await TrayViewModel.ChooseAssetAsync(); if (path == null) return;
            var result = await pipe.RequestAsync("icon.preview", new() { ["path"] = path, ["index"] = 0 }); Check(result);
            row.SetAsset(path, InputMethodIconRow.Decode(result["data"])!); Error = "";
        }
        catch (Exception ex) { Error = Localization.Text(ex.Message); }
    }
    public async Task<bool> FlushAsync()
    {
        // Closing the editor must wait for a selected file's preview before flushing its policy.
        foreach (var row in Rows)
            if (row.ChooseIconCommand.ExecutionTask is { IsCompleted: false } choosing) await choosing;
        debounce.Stop(); await gate.WaitAsync();
        try
        {
            while (pending.Count > 0)
            {
                var batch = pending.ToArray(); pending.Clear();
                for (int i = 0; i < batch.Length; i++)
                {
                    try { Check(await pipe.RequestAsync("inputMethods.set", batch[i].Value)); }
                    catch (Exception ex)
                    {
                        for (int j = i; j < batch.Length; j++) pending.TryAdd(batch[j].Key, batch[j].Value);
                        Error = Localization.Text(ex.Message); return false;
                    }
                }
            }
            Error = ""; return true;
        }
        finally { gate.Release(); }
    }
    public void Stop() => debounce.Stop();
}

public sealed class InputMethodIconEditor : UserControl
{
    public InputMethodIconEditor(InputMethodIconEditorModel model, Func<Task> close)
    {
        Focusable = true; System.Windows.Input.KeyboardNavigation.SetIsTabStop(this, false);
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition());
        var content = new StackPanel { Margin = new Thickness(0,4,0,0) };
        for (int i = 0; i < model.Rows.Count; i++)
        {
            var row = model.Rows[i];
            // Reserve the same heading height and align title ink in every row.
            // Only the first row exposes the dialog's close action.
            var header=IconEditorPanel.CreateHeader(row.Name,close,i==0?"InputMethodEditorClose":"",showClose:i==0);
            foreach(var text in header.Children.OfType<TextBlock>())text.FontWeight=FontWeights.Normal;
            if(i==0)root.Children.Add(header);else content.Children.Add(header);
            var choice = new IconChoiceRow(row, nameof(row.Original), () => row.AssetPreview,
                () => row.UseReplacement, value => row.UseReplacement = value, row.ChooseIconCommand, $"InputMethod{i}.",0)
                { Margin=new Thickness(4,0,4,i+1==model.Rows.Count?4:0) };
            System.Windows.Automation.AutomationProperties.SetAutomationId(choice, "InputMethodRow." + row.Key);
            content.Children.Add(choice);
            IconEditorPanel.BalanceHeaderSpacing(header,choice,i==0?content.Margin.Top:0);
            if (i + 1 < model.Rows.Count)
            {
                var divider = new Border { Height = .5, Margin = new Thickness(4,16,4,16) };
                divider.SetResourceReference(Border.BackgroundProperty, "BorderBrush"); content.Children.Add(divider);
            }
        }
        if (model.Rows.Count == 0)root.Children.Add(IconEditorPanel.CreateHeader("没有可编辑的输入法标志",close,"InputMethodEditorClose"));
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12,Margin=new Thickness(4,10,4,4) };
        error.SetResourceReference(TextBlock.ForegroundProperty, "DangerTextBrush");
        var errorStyle=new Style(typeof(TextBlock),TryFindResource(typeof(TextBlock)) as Style);
        var emptyError=new DataTrigger {Binding=new Binding(nameof(model.Error)){Source=model},Value=""};
        emptyError.Setters.Add(new Setter(VisibilityProperty,Visibility.Collapsed));errorStyle.Triggers.Add(emptyError);error.Style=errorStyle;
        error.SetBinding(TextBlock.TextProperty, new Binding(nameof(model.Error)) { Source = model }); content.Children.Add(error);
        var scroll = new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        scroll.SetResourceReference(StyleProperty, "IconEditorScrollViewerStyle"); DeviceListViewport.SetEnabled(scroll, true);
        Grid.SetRow(scroll,1); root.Children.Add(scroll); Content = root;
        System.Windows.Automation.AutomationProperties.SetAutomationId(this, "InputMethodIconEditor");
        Loaded += (_, _) => { Localization.Apply(this); Focus(); }; Unloaded += (_, _) => model.Stop();
    }
}
