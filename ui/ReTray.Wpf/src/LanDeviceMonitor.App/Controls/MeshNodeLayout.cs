using System.Windows;
using System.Windows.Controls;
using HorizontalAlignment=System.Windows.HorizontalAlignment;
using Orientation=System.Windows.Controls.Orientation;

namespace LanDeviceMonitor.Controls;

// A large count tile sits beside the node on desktop; on narrow cards it
// becomes an inline badge below the metadata so names and addresses keep room.
public sealed class MeshNodeLayout : Grid
{
    public MeshNodeLayout()
    {
        Loaded+=(_,_)=>Reflow();
        SizeChanged+=(_,_)=>Reflow();
    }
    private void Reflow()
    {
        if(ColumnDefinitions.Count!=3 || Children.OfType<Border>().FirstOrDefault(b=>Equals(b.Tag,"MeshCount")) is not {Child:StackPanel count} badge) return;
        var narrow=ActualWidth<300;
        if(RowDefinitions.Count==2) RowDefinitions.Add(new RowDefinition {Height=GridLength.Auto});
        ColumnDefinitions[2].Width=new GridLength(narrow?0:76);
        var content=Children.OfType<FrameworkElement>().First(c=>Equals(c.Tag,"MeshContent"));
        SetColumnSpan(content,narrow?2:1);
        SetRow(badge,narrow?2:0);SetColumn(badge,narrow?1:2);SetColumnSpan(badge,narrow?2:1);
        // The count tile must not force an empty band between the title and IP.
        SetRowSpan(badge,narrow?1:2);
        badge.Width=narrow?double.NaN:64;
        badge.HorizontalAlignment=narrow?HorizontalAlignment.Left:HorizontalAlignment.Right;
        badge.Padding=narrow?new Thickness(10,6,10,6):new Thickness(4,10,4,10);
        badge.Margin=narrow?new Thickness(0,10,0,0):new Thickness(0);
        count.Orientation=narrow?Orientation.Horizontal:Orientation.Vertical;
        var number=(TextBlock)count.Children[0];var label=(TextBlock)count.Children[1];
        number.FontSize=narrow?18:22;label.Margin=narrow?new Thickness(8,0,0,0):new Thickness(0,3,0,0);
        label.VerticalAlignment=VerticalAlignment.Center;
    }
}
