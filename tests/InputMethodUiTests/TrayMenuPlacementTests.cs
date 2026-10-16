using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using ReTray_App.Controls;

internal static class TrayMenuPlacementTests
{
    public static void Run()
    {
        var margins = new Thickness(12, 6, 12, 22);
        foreach (double scale in new[] { 1d, 1.25, 1.5, 2d }) {
            foreach (var popup in new[] { new Size(170, 132), new Size(230, 180) }) {
                var candidate = TrayMenuPlacement.Candidate(popup, margins);
                Require(candidate.PrimaryAxis == PopupPrimaryAxis.None, "WPF must retain both boundary-nudge axes");
                // Use the production callback helper, then map its DIPs to the
                // target display's pixels. Include a taskbar point below rcWork.
                foreach (var anchor in new[] { new Point(700, 800), new Point(-1300, 1055) }) {
                    var origin = new Point(anchor.X + candidate.Point.X * scale,
                                           anchor.Y + candidate.Point.Y * scale);
                    var visibleLeft = origin.X + margins.Left * scale;
                    var visibleBottom = origin.Y + (popup.Height - margins.Bottom) * scale;
                    Require(Math.Abs(visibleLeft - anchor.X) < .001 && Math.Abs(visibleBottom - anchor.Y) < .001,
                        "The visible plate lost its captured anchor when scaling or resizing");
                }
            }
        }
        var taskbar = TrayMenuPlacement.Candidate(new Size(170, 132), margins);
        Require(1055 + taskbar.Point.Y + 132 <= 1080,
            "Taskbar popup should fit the monitor including its shadow");
        Require(1055 + taskbar.Point.Y + 132 > 1032,
            "The callback silently pulled a taskbar click back inside the work area");
        HiddenAnchorIsPositionedAndReleased();
    }
    private static void HiddenAnchorIsPositionedAndReleased()
    {
        var type = typeof(TrayCommandMenu).GetNestedType("ScreenAnchor", BindingFlags.NonPublic)!;
        var position = new Point(-1333, 420);
        var anchor = (IDisposable)Activator.CreateInstance(type, [position])!;
        HwndSource? source = null;
        try {
            source = (HwndSource)type.GetField("source", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(anchor)!;
            Require(!IsWindowVisible(source.Handle), "A placement helper became a visible desktop window");
            var target = (Border)source.RootVisual;
            var actual = target.PointToScreen(new Point());
            Require(Math.Abs(actual.X - position.X) <= 1 && Math.Abs(actual.Y - position.Y) <= 1,
                "A hidden placement target lost its negative captured screen position");
        }
        finally { anchor.Dispose(); }
        Require(source is { IsDisposed: true }, "Closing a menu must release its native placement anchor");
    }
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint window);
}
