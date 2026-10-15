using System.Windows;
using System.Windows.Media;
using System.Xml.Linq;
using System.Globalization;

namespace ReTray_App;

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
    private static string? Attribute(XElement element,string name)
    {
        // SVG inline style overrides presentation attributes (Ionicons uses it).
        string? styled=null;
        foreach(var declaration in ((string?)element.Attribute("style")??"").Split(';')) {
            var colon=declaration.IndexOf(':');
            if(colon>0 && declaration[..colon].Trim()==name)styled=declaration[(colon+1)..].Trim();
        }
        return styled??(string?)element.Attribute(name);
    }
    private static double Number(string value) => double.Parse(value.EndsWith("px",StringComparison.Ordinal)?value[..^2]:value,CultureInfo.InvariantCulture);
    private static Geometry GeometryFor(SettingsIconKind kind)
    {
        if(geometries.TryGetValue(kind,out var cached))return cached;
        var name=kind.ToString().ToLowerInvariant();
        var uri=new Uri($"/ReTray.App;component/Assets/SettingsIcons/{name}.svg",UriKind.Relative);
        using var stream=Application.GetResourceStream(uri)!.Stream;
        var svg=XDocument.Load(stream);
        var root=svg.Root!;
        var group=new GeometryGroup { FillRule=FillRule.Nonzero };
        var viewBox=((string)root.Attribute("viewBox")!).Split(' ',StringSplitOptions.RemoveEmptyEntries);
        // Phosphor Light's filled outline has a 12-unit stroke on a 256 grid.
        var nominalOutline=double.Parse(viewBox[2],CultureInfo.InvariantCulture)>100?12d:1d;
        foreach(var path in svg.Descendants().Where(e=>e.Name.LocalName is "path" or "line")) {
            Geometry geometry=path.Name.LocalName=="line"
                ? new LineGeometry(new Point(Number((string)path.Attribute("x1")!),Number((string)path.Attribute("y1")!)),
                    new Point(Number((string)path.Attribute("x2")!),Number((string)path.Attribute("y2")!)))
                : Geometry.Parse((Attribute(path,"fill-rule")=="evenodd"?"F0 ":"F1 ")+(string)path.Attribute("d")!);
            var fill=Attribute(path,"fill")??Attribute(root,"fill")??"black";
            if(fill!="none" && path.Name.LocalName!="line")group.Children.Add(geometry);
            var stroke=Attribute(path,"stroke")??Attribute(root,"stroke")??"none";
            if(stroke!="none") {
                var width=Number(Attribute(path,"stroke-width")??Attribute(root,"stroke-width")??"1");
                nominalOutline=width;
                var cap=Attribute(path,"stroke-linecap")??Attribute(root,"stroke-linecap");
                var join=Attribute(path,"stroke-linejoin")??Attribute(root,"stroke-linejoin");
                var pen=new Pen(Brushes.Black,width) {
                    StartLineCap=cap=="round"?PenLineCap.Round:PenLineCap.Flat,
                    EndLineCap=cap=="round"?PenLineCap.Round:PenLineCap.Flat,
                    LineJoin=join=="round"?PenLineJoin.Round:PenLineJoin.Miter };
                group.Children.Add(geometry.GetWidenedPathGeometry(pen,.005,ToleranceType.Absolute));
            }
        }
        if(kind==SettingsIconKind.Startup) {
            // P3 preserves its SVG viewport margins. Construct the stroke at
            // its final width; boolean erosion can break the arc at its joins.
            var viewportScale=21.5/Math.Max(Number(viewBox[2]),Number(viewBox[3]));
            group.Transform=new MatrixTransform(viewportScale,0,0,viewportScale,
                -Number(viewBox[0])*viewportScale,-Number(viewBox[1])*viewportScale);
            group.Freeze();geometries[kind]=group;return group;
        }
        var bounds=group.Bounds;
        var scale=20/Math.Max(bounds.Width,bounds.Height);
        group.Transform=new MatrixTransform(scale,0,0,scale,-bounds.X*scale,-bounds.Y*scale);
        // Keep the chosen contours and long power stem, while matching the
        // other page icons' outline width after normalizing their visible size.
        Geometry ink=group;
        var correction=nominalOutline*scale-1;
        if(correction>0) {
            var outline=group.GetWidenedPathGeometry(new Pen(Brushes.Black,correction),.005,ToleranceType.Absolute);
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
