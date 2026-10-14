using System.Windows;
using System.Windows.Media;
using System.Xml.Linq;

namespace TrayAssistant_App;

public enum SettingsIconKind { Startup, Theme, Language, Configuration }

public sealed class SettingsIcon : FrameworkElement
{
    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(
        nameof(Kind), typeof(SettingsIconKind), typeof(SettingsIcon),
        new FrameworkPropertyMetadata(SettingsIconKind.Startup, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty ForegroundProperty = System.Windows.Documents.TextElement.ForegroundProperty.AddOwner(
        typeof(SettingsIcon), new FrameworkPropertyMetadata(Brushes.Black, FrameworkPropertyMetadataOptions.AffectsRender));
    public SettingsIconKind Kind { get => (SettingsIconKind)GetValue(KindProperty); set => SetValue(KindProperty,value); }
    public Brush Foreground { get => (Brush)GetValue(ForegroundProperty); set => SetValue(ForegroundProperty,value); }

    private static readonly Dictionary<SettingsIconKind, Geometry> geometries = new();
    private static Geometry GeometryFor(SettingsIconKind kind)
    {
        if(geometries.TryGetValue(kind,out var cached))return cached;
        var name=kind.ToString().ToLowerInvariant();
        var uri=new Uri($"/TrayAssistant.App;component/Assets/SettingsIcons/{name}.svg",UriKind.Relative);
        using var stream=Application.GetResourceStream(uri)!.Stream;
        var svg=XDocument.Load(stream);
        var group=new GeometryGroup { FillRule=FillRule.Nonzero };
        foreach(var path in svg.Descendants().Where(e=>e.Name.LocalName=="path"))
            group.Children.Add(Geometry.Parse(((string?)path.Attribute("fill-rule")=="evenodd"?"F0 ":"F1 ")+(string)path.Attribute("d")!));
        var bounds=group.Bounds;
        var scale=20/Math.Max(bounds.Width,bounds.Height);
        group.Transform=new MatrixTransform(scale,0,0,scale,-bounds.X*scale,-bounds.Y*scale);
        // The 20px Fluent paths have a nominal one-unit outline. Normalizing
        // their ink bounds enlarged that outline by a different amount per icon.
        // Keep the normalized size but bring every outline back to one DIP.
        Geometry ink=group;
        if(scale>1) {
            var outline=group.GetWidenedPathGeometry(new Pen(Brushes.Black,scale-1),.005,ToleranceType.Absolute);
            ink=Geometry.Combine(group,outline,GeometryCombineMode.Exclude,null,.005,ToleranceType.Absolute);
        }
        ink.Freeze();geometries[kind]=ink;return ink;
    }
    protected override void OnRender(DrawingContext drawing)
    {
        var geometry=GeometryFor(Kind);
        var bounds=geometry.Bounds;
        if(bounds.IsEmpty || bounds.Width<=0 || bounds.Height<=0)return;
        drawing.PushTransform(new TranslateTransform((ActualWidth-bounds.Width)/2-bounds.X,
            (ActualHeight-bounds.Height)/2-bounds.Y));
        drawing.DrawGeometry(Foreground,null,geometry);
        drawing.Pop();
    }
}
