namespace ReTray_App;
public static class TrayRowMetrics
{
    // Match the visible switch track, including the action button's outline.
    public const double ActionDiameter = 31 * .74;
    // Add one DIP of breathing room at each end of the lifted capsule.
    public static System.Windows.Thickness DragContentInset { get; } =
        new((34 - 28) / 2 + 1, 0, (34 - ActionDiameter) / 2 - 4 + 1, 0);
}
