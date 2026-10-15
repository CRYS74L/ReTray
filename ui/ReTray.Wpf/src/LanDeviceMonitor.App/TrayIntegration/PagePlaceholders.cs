using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Path = System.Windows.Shapes.Path;
using Localization = ReTray_App.Services.Localization;

namespace ReTray_App;

// Loading silhouettes use the same columns and control footprints as the lists.
// They contain no buttons or toggles, so unavailable settings cannot be edited.
internal static class PagePlaceholders
{
    private static Border Shape(double width,double height,double radius,string tag,double opacity=.1)
    {
        var shape=new Border {Width=width,Height=height,CornerRadius=new CornerRadius(radius),
            HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center,
            Opacity=opacity,Tag=tag};
        shape.SetResourceReference(Border.BackgroundProperty,"TextBrush");return shape;
    }
    private static Border Divider()
    {
        var line=new PixelAlignedDivider {Height=.5,Margin=new Thickness(36,0,0,0),Opacity=.6};
        line.SetResourceReference(Border.BackgroundProperty,"ListDividerBrush");return line;
    }
    private static Grid Toggle()
    {
        var toggle=new Grid {Width=51*.74,Height=36*.74};
        toggle.Children.Add(Shape(51*.74,31*.74,31*.74/2,"PlaceholderToggle",.08));
        return toggle;
    }
    private static Grid Capsule(double width,string tag,double labelWidth,bool arrow=false)
    {
        var capsule=new Grid {Width=width,Height=28};
        capsule.Children.Add(Shape(width,28,14,tag,.055));
        var outline=new Border {CornerRadius=new CornerRadius(14),BorderThickness=new Thickness(1),Opacity=.1};
        outline.SetResourceReference(Border.BorderBrushProperty,"TextBrush");capsule.Children.Add(outline);
        var label=Shape(labelWidth,8,4,"PlaceholderControlLabel");
        label.HorizontalAlignment=arrow?HorizontalAlignment.Left:HorizontalAlignment.Center;
        if(arrow)label.Margin=new Thickness(15,0,31,0);
        capsule.Children.Add(label);
        if(arrow) {
            var chevron=new Path {Data=Geometry.Parse("M0 0 L4 4 L8 0"),Width=8,Height=4,
                StrokeThickness=1.1,StrokeStartLineCap=PenLineCap.Round,StrokeEndLineCap=PenLineCap.Round,
                HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Center,
                Margin=new Thickness(0,0,16,0),Opacity=.16};
            chevron.SetResourceReference(Path.StrokeProperty,"TextBrush");capsule.Children.Add(chevron);
        }
        return capsule;
    }
    private static double TextWidth(string text)
    {
        var label=new TextBlock {Text=Localization.Text(text),FontSize=14,
            FontFamily=(FontFamily)Application.Current.FindResource("AppTextFont")};
        TextOptions.SetTextFormattingMode(label,TextFormattingMode.Ideal);
        label.Measure(new Size(double.PositiveInfinity,double.PositiveInfinity));return label.DesiredSize.Width;
    }
    private static Grid Row(double labelWidth,FrameworkElement control,bool more=false,bool trayDivider=false,double iconSize=20)
    {
        var row=new Grid {Height=34,IsHitTestVisible=false,Focusable=false};
        row.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(28)});
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition {Width=GridLength.Auto});
        row.ColumnDefinitions.Add(new ColumnDefinition {Width=GridLength.Auto});
        row.Children.Add(Shape(iconSize,iconSize,4,"PlaceholderIcon"));
        var labelHost=new Grid {Margin=new Thickness(8,0,8,0),ClipToBounds=true};
        var label=Shape(labelWidth,10,5,"PlaceholderLabel");label.HorizontalAlignment=HorizontalAlignment.Left;
        labelHost.Children.Add(label);Grid.SetColumn(labelHost,1);row.Children.Add(labelHost);
        if(more) {
            var action=Shape(TrayRowMetrics.ActionDiameter,TrayRowMetrics.ActionDiameter,
                TrayRowMetrics.ActionDiameter/2,"PlaceholderMore");action.Margin=new Thickness(0,0,8,0);
            Grid.SetColumn(action,2);row.Children.Add(action);
        }
        control.Margin=new Thickness(0,0,4,0);control.VerticalAlignment=VerticalAlignment.Center;
        Grid.SetColumn(control,3);row.Children.Add(control);
        if(trayDivider) {
            var divider=Divider();divider.Margin=new Thickness(8,0,0,0);divider.VerticalAlignment=VerticalAlignment.Bottom;
            Grid.SetColumn(divider,1);Grid.SetColumnSpan(divider,3);row.Children.Add(divider);
        }
        return row;
    }
    public static Grid TrayRow(int index)=>Row(90+index%3*22,Toggle(),more:true,trayDivider:true);
    public static void FillSystem(StackPanel rows,IEnumerable<(int id,string name)> items)
    {
        rows.Children.Clear();var list=items.ToArray();
        for(int index=0;index<list.Length;index++) {
            rows.Children.Add(Row(TextWidth(list[index].name),Toggle(),more:list[index].id==3));
            if(index+1<list.Length)rows.Children.Add(Divider());
        }
    }
    public static void FillSettings(StackPanel rows)
    {
        rows.Children.Clear();
        var exportWidth=TextWidth("导出配置");var importWidth=TextWidth("导入配置");
        var buttons=new StackPanel {Orientation=Orientation.Horizontal};
        buttons.Children.Add(Capsule(exportWidth+30,"PlaceholderExport",exportWidth));
        var import=Capsule(importWidth+30,"PlaceholderImport",importWidth);import.Margin=new Thickness(8,0,0,0);
        buttons.Children.Add(import);
        FrameworkElement[] controls=[Toggle(),Capsule(132,"PlaceholderSelector",56,true),
            Capsule(132,"PlaceholderSelector",56,true),buttons];
        string[] names=["开机启动","主题","语言","配置文件"];
        for(int index=0;index<names.Length;index++) {
            rows.Children.Add(Row(TextWidth(names[index]),controls[index],iconSize:index==0?16:20));
            if(index+1<names.Length)rows.Children.Add(Divider());
        }
    }
}
