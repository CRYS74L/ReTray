using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml.Media.Imaging;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text.Json.Nodes;

namespace TrayAssistant_App.Models;
public sealed class IconRow : ObservableObject
{
    public string Key { get; }
    public string StableKey => Rule["stableKey"]?.GetValue<string>() ?? Key;
    public JsonObject Rule { get; private set; } = new();
    private string name = "", tip = "", state = "", identity = "", executable = "";
    private WriteableBitmap? image, original;
    private string pixels = "", originalPixels = "";
    private bool visible,isBusy;
    public bool Visible { get=>visible;set=>SetProperty(ref visible,value); }
    public bool ActualVisible=>Rule["acknowledgedVisible"]?.GetValue<bool>()??Rule["placement"]?.GetValue<int>()!=2;
    public bool Owned=>Rule["owned"]?.GetValue<bool>()==true;
    public bool IsBusy { get=>isBusy;set{if(SetProperty(ref isBusy,value))OnPropertyChanged(nameof(CanToggle));} }
    public bool CanToggle=>!IsBusy;
    public string Name { get => name; private set => SetProperty(ref name, value); }
    public string Tip { get => tip; private set => SetProperty(ref tip, value); }
    public string State { get => state; private set => SetProperty(ref state, value); }
    public string Identity { get => identity; private set => SetProperty(ref identity, value); }
    public string Executable { get => executable; private set => SetProperty(ref executable, value); }
    public WriteableBitmap? Image { get => image; private set => SetProperty(ref image, value); }
    public WriteableBitmap? Original { get => original; private set => SetProperty(ref original, value); }
    public IconRow(JsonObject json, JsonObject? acknowledgment = null) { Key = json["key"]!.GetValue<string>(); Update(json, acknowledgment); }
    private void ApplyVisibilityAcknowledgment(JsonObject acknowledgment)
    {
        Rule["acknowledgedVisible"] = acknowledgment["visible"]!.DeepClone();
        Rule["visibility"] = acknowledgment["visibility"]!.DeepClone();
        if (acknowledgment["placement"] is JsonNode placement) Rule["placement"] = placement.DeepClone();
    }
    public void AcknowledgeVisibility(JsonObject acknowledgment) { ApplyVisibilityAcknowledgment(acknowledgment); Visible = ActualVisible; }
    public void Update(JsonObject json, JsonObject? acknowledgment = null)
    {
        Rule = (JsonObject)json.DeepClone();
        if (acknowledgment != null) ApplyVisibilityAcknowledgment(acknowledgment);
        if(!IsBusy)Visible=ActualVisible;
        Executable = json["selector"]!["executable"]!.GetValue<string>();
        Name = Path.GetFileNameWithoutExtension(Executable);
        Name=Name.ToLowerInvariant() switch{"weixin"=>"微信","qq"=>"QQ","trayassistant.agent"=>"托盘图标助手",_=>Name};
        Tip = json["tipCurrent"]?.GetValue<string>() ?? "";
        Identity = (json["selector"]!["hasGuid"]!.GetValue<bool>() ? "GUID" : $"UID {json["selector"]!["uid"]}") + $" · PID {json["pid"]}";
        State = json["decision"]?.GetValue<int>()==4?"待确认静态图像":json["decision"]?.GetValue<int>()==2?"原应用闪烁空帧":json["dynamic"]?.GetValue<bool>() == true ? "原应用动态帧" : json["placement"]!.GetValue<int>() == 2 ? "已隐藏" : json["custom"]?.GetValue<bool>() == true ? "自定义图标" : json["placement"]!.GetValue<int>() == 0 ? "任务栏" : "折叠区";
        State=Services.Localization.Text(State);
        var value = json["pixels"]!.GetValue<string>();
        if (pixels != value) { pixels = value; Image = Decode(value, json["width"]!.GetValue<int>(), json["height"]!.GetValue<int>()); }
        value = json["originalPixels"]!.GetValue<string>();
        if (originalPixels != value) { originalPixels = value; Original = Decode(value, json["originalWidth"]!.GetValue<int>(), json["originalHeight"]!.GetValue<int>()); }
    }
    public static WriteableBitmap? Decode(string data, int width, int height)
    {
        if (width <= 0 || height <= 0 || width > 64 || height > 64) return null;
        var bytes = Convert.FromBase64String(data); if (bytes.Length != width * height * 4) return null;
        // The native snapshot is straight BGRA; the XAML buffer uses premultiplied BGRA.
        for (var i = 0; i < bytes.Length; i += 4) { var a = bytes[i + 3]; for (var c = 0; c < 3; c++) bytes[i + c] = (byte)((bytes[i + c] * a + 127) / 255); }
        var bitmap = new WriteableBitmap(width, height); using var stream = bitmap.PixelBuffer.AsStream(); stream.Write(bytes); bitmap.Invalidate(); return bitmap;
    }
}
