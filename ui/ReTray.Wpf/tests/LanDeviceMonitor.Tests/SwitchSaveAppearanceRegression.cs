using System.IO;
using System.IO.Pipes;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using LanDeviceMonitor.Controls;
using LanDeviceMonitor.Services;
using ReTray_App.Models;
using ReTray_App.Services;
using ReTray_App.ViewModels;

internal static class SwitchSaveAppearanceRegression
{
    internal static int Run()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            LanDeviceMonitor.App? app = null;
            Window? host = null;
            try
            {
                app = TestAppFactory.Create(); app.InitializeComponent(); app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                var source = new LanDeviceMonitor.MainWindow(new NotificationService());
                var row = new IconRow(new JsonObject
                {
                    ["key"] = "switch-save-fixture", ["placement"] = 2, ["canPersist"] = true,
                    ["selector"] = new JsonObject { ["executable"] = "sample.exe", ["hasGuid"] = false },
                    ["pixels"] = "", ["originalPixels"] = ""
                });
                var presenter = new ContentPresenter
                {
                    Content = row, ContentTemplate = (DataTemplate)source.TrayPage.Resources["IconRowTemplate"]
                };
                var systemSwitch = new SwitchControl { Style = (Style)source.TrayPage.Resources["TrayRowToggle"] };
                var panel = new StackPanel(); panel.Children.Add(presenter); panel.Children.Add(systemSwitch);
                host = new Window { Content = panel, Width = 430, Height = 160, Left = -10000, Top = -10000, ShowInTaskbar = false, ShowActivated = false };
                host.Show(); host.UpdateLayout(); Pump(80);
                var toggle = Descendants<SwitchControl>(presenter).Single();
                var pipeName = "ReTray.SwitchSaveRegression." + Guid.NewGuid().ToString("N");
                var vm = new TrayViewModel(new PipeClient(pipeName, () => true, () => false), TimeSpan.FromSeconds(5));
                foreach (var mode in new[] { ThemeService.LightMode, ThemeService.DarkMode })
                {
                    ThemeService.Apply(mode);
                    foreach (var (value, success) in new[] { (true, true), (false, true), (true, false) })
                    {
                        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                        using var pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                        var server = ReplyAsync(pipe, success, release.Task, deadline.Token);
                        var previous = row.ActualVisible;
                        var knob = (Border)toggle.Template.FindName("PART_Knob", toggle);
                        var activeTrack = (Border)toggle.Template.FindName("PART_ActiveTrack", toggle);
                        var movement = (TranslateTransform)knob.RenderTransform;
                        var initialColor = activeTrack.Opacity;
                        toggle.SetCurrentValue(ToggleButton.IsCheckedProperty, value);
                        Require(Math.Abs(activeTrack.Opacity - initialColor) < .001, "Theme color jumped before the thumb animation.");
                        var saving = vm.SetRowVisibilityAsync(row, value);
                        Require(row.IsBusy && !toggle.IsEnabled, "Saving did not block duplicate activation.");
                        var duplicate = vm.SetRowVisibilityAsync(row, !value);
                        Require(duplicate.IsCompletedSuccessfully, "Duplicate click entered the request queue.");
                        var peer = (IToggleProvider)UIElementAutomationPeer.CreatePeerForElement(toggle)!.GetPattern(PatternInterface.Toggle)!;
                        try { peer.Toggle(); throw new InvalidOperationException("Busy switch accepted UIA activation."); }
                        catch (System.Windows.Automation.ElementNotEnabledException) { }
                        var samples = 0; var minimumOpacity = 1d;
                        var positions = new HashSet<double>();
                        while (!saving.IsCompleted)
                        {
                            Pump(16); samples++;
                            if (samples == 12) release.SetResult();
                            minimumOpacity = Math.Min(minimumOpacity, toggle.Opacity);
                            Require(knob.Opacity == 1, "Saving dimmed the knob.");
                            Require(Math.Abs(movement.X / 20 - activeTrack.Opacity) < .001, $"Color fade and thumb movement are out of sync: x={movement.X}, opacity={activeTrack.Opacity}, transformAnimated={movement.HasAnimatedProperties}, opacityAnimated={activeTrack.HasAnimatedProperties}.");
                            positions.Add(Math.Round(movement.X, 3));
                        }
                        saving.GetAwaiter().GetResult(); server.GetAwaiter().GetResult();
                        toggle.SetCurrentValue(ToggleButton.IsCheckedProperty, row.Visible);
                        Pump(220);
                        Require(samples >= 12, "The pending response was not sampled across frames.");
                        Require(positions.Count >= 5, "Switch motion skipped its intermediate frames.");
                        Require(minimumOpacity == 1 && toggle.Opacity == 1, $"Saving caused a faded frame: minimum opacity={minimumOpacity}.");
                        Require(!row.IsBusy && toggle.IsEnabled, "Save completion left the switch blocked.");
                        Require(toggle.IsChecked == (success ? value : previous), "Acknowledgment/rollback state is wrong.");
                        Require(activeTrack.Opacity == (toggle.IsChecked == true ? 1 : 0), "Theme fade did not finish with the switch.");
                        Require(!movement.HasAnimatedProperties && !activeTrack.HasAnimatedProperties, "Completed switch retained animation clocks.");
                        Require(knob.CacheMode is BitmapCache cache && cache.RenderAtScale == VisualTreeHelper.GetDpi(toggle).DpiScaleX, "Thumb/shadow cache does not match display DPI.");
                        Require(success ? vm.Error.Length == 0 : vm.Error.Contains("fixture rejection"), "Failure feedback was lost.");
                        Console.WriteLine($"{mode} {(value ? "on" : "off")} {(success ? "save" : "rollback")} PASS: {samples} samples, {positions.Count} positions, synchronous thumb/color, opacity={minimumOpacity}, duplicate activation blocked.");
                    }
                    foreach (var value in new[] { true, false })
                    {
                        systemSwitch.IsChecked = value; systemSwitch.IsBusy = true; Pump(80);
                        Require(!systemSwitch.IsEnabled && systemSwitch.Opacity == 1, "System switch faded or remained interactive during saving.");
                        systemSwitch.IsBusy = false; Pump(40);
                        Require(systemSwitch.IsEnabled && systemSwitch.Opacity == 1, "System switch failed to recover after saving.");
                    }
                    systemSwitch.IsEnabled = false; Pump(40);
                    Require(systemSwitch.Opacity == .45, "Unavailable switches lost their disabled appearance.");
                    systemSwitch.IsEnabled = true;
                    systemSwitch.IsChecked = false; Pump(250);
                    var systemKnob = (Border)systemSwitch.Template.FindName("PART_Knob", systemSwitch);
                    var systemTrack = (Border)systemSwitch.Template.FindName("PART_ActiveTrack", systemSwitch);
                    var systemMovement = (TranslateTransform)systemKnob.RenderTransform;
                    systemSwitch.IsChecked = true; Pump(75);
                    var reversalPosition = systemMovement.X; var reversalColor = systemTrack.Opacity;
                    Require(reversalColor is > 0 and < 1, "Color fade has no intermediate state.");
                    systemSwitch.IsChecked = false;
                    Require(Math.Abs(systemMovement.X - reversalPosition) < .001 && Math.Abs(systemTrack.Opacity - reversalColor) < .001, "Rapid reversal jumped to an endpoint.");
                    Pump(80);
                    Require(systemMovement.X < reversalPosition && systemTrack.Opacity < reversalColor, "Rapid reversal did not fade and move back together.");
                    Pump(180);
                    Require(systemMovement.X == 0 && systemTrack.Opacity == 0, "Reversed switch did not finish at off.");
                }
                Console.WriteLine("Switch save appearance regression PASS: real tray-row template and view-model pipe requests, both directions, failure rollback, busy input guard, light/dark and unavailable states.");
            }
            catch (Exception error) { failure = error; }
            finally { host?.Close(); app?.Shutdown(); }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        if (!thread.Join(TimeSpan.FromSeconds(25))) { Console.Error.WriteLine("Switch save appearance regression timed out."); return 1; }
        if (failure is null) return 0;
        Console.Error.WriteLine(failure); return 1;
    }

    private static async Task ReplyAsync(NamedPipeServerStream pipe, bool success, Task release, CancellationToken token)
    {
        await pipe.WaitForConnectionAsync(token).ConfigureAwait(false);
        using var reader = new StreamReader(pipe, leaveOpen: true);
        using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
        var request = JsonNode.Parse((await reader.ReadLineAsync(token).ConfigureAwait(false))!)!;
        Require(request["method"]!.GetValue<string>() == "icon.visibility", "Unexpected fixture request.");
        var visible = request["data"]!["visibility"]!.GetValue<int>() == 0;
        await release.WaitAsync(token).ConfigureAwait(false);
        var response = new JsonObject
        {
            ["success"] = success, ["revision"] = 1, ["detail"] = "fixture rejection",
            ["data"] = new JsonObject { ["visible"] = visible, ["visibility"] = visible ? 0 : 1, ["placement"] = visible ? 0 : 2 }
        };
        await writer.WriteLineAsync(response.ToJsonString().AsMemory(), token).ConfigureAwait(false);
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T found) yield return found;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Pump(int milliseconds)
    {
        var frame = new DispatcherFrame(); var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; }; timer.Start(); Dispatcher.PushFrame(frame);
    }
}
