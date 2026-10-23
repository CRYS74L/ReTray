namespace ReTray_App;
public static class TrayRowMetrics
{
    // Match the visible switch track, including the action button's outline.
    public const double ActionDiameter = 31 * .74;
    // The track is centered in a 34-DIP row and already has a 4-DIP
    // trailing margin. Add the same inset at both ends only while lifted.
    public static System.Windows.Thickness DragContentInset { get; } =
        new((34 - ActionDiameter) / 2 - 4, 0, (34 - ActionDiameter) / 2 - 4, 0);
}
