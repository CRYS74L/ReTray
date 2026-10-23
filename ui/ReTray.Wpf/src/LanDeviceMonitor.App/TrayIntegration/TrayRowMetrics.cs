namespace ReTray_App;
public static class TrayRowMetrics
{
    // Match the visible switch track, including the action button's outline.
    public const double ActionDiameter = 31 * .74;
    // Match the 28-DIP icon's left clearance to its top clearance.
    // Keep the switch track's existing right clearance unchanged.
    public static System.Windows.Thickness DragContentInset { get; } =
        new((34 - 28) / 2, 0, (34 - ActionDiameter) / 2 - 4, 0);
}
