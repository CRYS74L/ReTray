using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using LanDeviceMonitor.Services;

internal static class VersionLinkMotionRegression
{
    private static readonly DependencyPropertyKey HoverKey = (DependencyPropertyKey)typeof(UIElement)
        .GetField("IsMouseOverPropertyKey", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;

    internal static int Run()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            LanDeviceMonitor.App? app = null;
            Window? window = null;
            try
            {
                app = TestAppFactory.Create();
                app.InitializeComponent();
                app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                ThemeService.Apply(ThemeService.LightMode);
                var sourceWindow = new LanDeviceMonitor.MainWindow(new NotificationService());
                var page = sourceWindow.TrayPage;
                var footer = (StackPanel)page.FindName("VersionFooter");
                // Host the real footer without loading the page's native tray
                // refresh or touching the user's running application/settings.
                ((Grid)footer.Parent).Children.Remove(footer);
                window = new Window
                {
                    Content = footer, Width = 430, Height = 140,
                    Left = -10000, Top = -10000, ShowActivated = false, ShowInTaskbar = false
                };
                window.Show(); window.UpdateLayout(); Pump(150);
                var link = (Button)page.FindName("VersionLink");
                var text = (TextBlock)page.FindName("VersionText");
                var icon = (System.Windows.Shapes.Path)page.FindName("RepositoryIcon");
                Require(SystemParameters.ClientAreaAnimation, "Animation sampling requires Windows client-area animations enabled.");
                Require(link.ToolTip is null && !ToolTipService.GetIsEnabled(link), "Version link tooltip reappeared.");
                Require(link.Padding == new Thickness(2, 3, 2, 3), "Version link hit-area padding changed.");
                foreach (var mode in new[] { ThemeService.LightMode, ThemeService.DarkMode })
                {
                    ThemeService.Apply(mode); Pump(40);
                    var accent = (Color)link.FindResource("SwitchOnColor");
                    Require(accent == Color.FromRgb(165, 110, 247), $"Incorrect {mode} accent.");
                    Require((Color)link.FindResource("StudioSelectedTextColor") == accent, "Dropdown and switch accents differ.");
                    var sharedMuted = (SolidColorBrush)link.FindResource("MutedTextBrush");
                    var idle = sharedMuted.Color;
                    Require(ColorOf(link) == idle, "Idle version link did not follow the palette.");
                    Require(!ReferenceEquals(link.Foreground, sharedMuted), "Version animation mutates a shared brush.");
                    Hover(link, true);
                    Require(Distance(ColorOf(link), idle) < 4, "Hover entry jumped to the target color.");
                    Pump(70);
                    var entering = ColorOf(link);
                    Require(Distance(entering, idle) > 3 && Distance(entering, accent) > 3, $"Hover entry has no intermediate colors: idle={idle}, sample={entering}, accent={accent}, hovered={link.IsMouseOver}, animated={((SolidColorBrush)link.Foreground).HasAnimatedProperties}.");
                    Require(sharedMuted.Color == idle, "Hover changed the global muted brush.");
                    Require(ReferenceEquals(link.Foreground, text.Foreground) && ReferenceEquals(link.Foreground, icon.Fill), "Icon and text do not share the animated foreground.");
                    Hover(link, false);
                    Require(Distance(ColorOf(link), entering) < 4, "Rapid hover reversal snapped to an endpoint.");
                    Pump(65);
                    Require(Distance(ColorOf(link), idle) < Distance(entering, idle), "Reversal did not move toward idle.");
                    Pump(250);
                    Require(ColorOf(link) == idle, "Hover exit did not settle on the idle color.");
                    Hover(link, true); Pump(250);
                    Require(ColorOf(link) == accent, "Hover did not settle on the exact chosen HEX.");
                    Hover(link, false); Pump(70);
                    var leaving = ColorOf(link);
                    Require(Distance(leaving, idle) > 3 && Distance(leaving, accent) > 3, "Hover exit has no intermediate colors.");
                    Pump(250);
                    Require(ColorOf(link) == idle, "Hover exit did not complete.");
                    for (var i = 0; i < 6; i++) { Hover(link, i % 2 == 0); Pump(25); }
                    Pump(280);
                    Require(ColorOf(link) == idle, "Rapid crossings left a stale animation.");
                    Hover(link, true); Pump(50);
                    ThemeService.Apply(mode == ThemeService.LightMode ? ThemeService.DarkMode : ThemeService.LightMode);
                    Require(ColorOf(link) == accent, "Palette change left a stale hover animation.");
                    Hover(link, false); Pump(280);
                    Require(ColorOf(link) == ((SolidColorBrush)link.FindResource("MutedTextBrush")).Color, "Exit after theme change used the old palette.");
                    Console.WriteLine($"Version hover {mode} PASS: intermediate entry/exit colors, reversible transitions, exact accent, shared icon/text, isolated resources, rapid crossings and palette changes.");
                }
                window.Close(); window = null; Pump(40);
                var afterUnload = ColorOf(link);
                ThemeService.Apply(ThemeService.LightMode);
                Require(ColorOf(link) == afterUnload, "Unloaded version link retained a palette subscription.");
                Console.WriteLine("Version link motion regression PASS.");
            }
            catch (Exception error) { failure = error; }
            finally { window?.Close(); app?.Shutdown(); }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        if (!thread.Join(TimeSpan.FromSeconds(25))) { Console.Error.WriteLine("Version link animation regression timed out."); return 1; }
        if (failure is null) return 0;
        Console.Error.WriteLine(failure); return 1;
    }

    private static void Hover(Button link, bool hovered)
    {
        var writeFlag = typeof(UIElement).GetMethod("WriteFlag", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var cacheFlag = Enum.Parse(writeFlag.GetParameters()[0].ParameterType, "IsMouseOverCache");
        writeFlag.Invoke(link, new[] { cacheFlag, (object)hovered });
        link.SetValue(HoverKey, hovered);
        link.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, Environment.TickCount)
            { RoutedEvent = hovered ? Mouse.MouseEnterEvent : Mouse.MouseLeaveEvent });
    }
    private static Color ColorOf(Button link) => ((SolidColorBrush)link.Foreground).Color;
    private static int Distance(Color a, Color b) => Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B);
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Pump(int milliseconds)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start(); Dispatcher.PushFrame(frame);
    }
}
