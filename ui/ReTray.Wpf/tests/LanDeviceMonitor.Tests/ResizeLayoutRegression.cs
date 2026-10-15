using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using System.Runtime.InteropServices;
using LanDeviceMonitor;
using LanDeviceMonitor.Controls;
using LanDeviceMonitor.Models;
using LanDeviceMonitor.Services;

internal static class ResizeLayoutRegression
{
    internal static int Run()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            App? app = null; MainWindow? window = null;
            try
            {
                app = TestAppFactory.Create(); app.InitializeComponent();
                ThemeService.Apply(ThemeService.LightMode);
                window = new MainWindow(new NotificationService())
                { WindowStartupLocation = WindowStartupLocation.Manual, Left = 100, Top = 60, Width = 1180, Height = 760, Topmost = true };
                typeof(MainWindow).GetField("_isPopulatingAppSettings", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(window, true);
                var now = DateTimeOffset.Now;
                var devices = Enumerable.Range(1, 12).Select(i => new DeviceViewData
                {
                    Profile = new DeviceProfile { MacAddress = $"02:00:00:00:51:{i:X2}", LastRouterName = $"测试设备 {i}", LastKnownOnline = true, LastSeenAt = now, FirstSeenAt = now.AddHours(-4) },
                    Identity = new DeviceIdentity("手机", "\uE8EA", $"测试设备 {i}"),
                    Sessions = Enumerable.Range(0, 30).Select(j => new DeviceSession { MacAddress = $"02:00:00:00:51:{i:X2}", OnlineAt = now.AddHours(-j - 2), OfflineAt = now.AddHours(-j - 1) }).ToList()
                }).ToArray();
                typeof(MainWindow).GetMethod("ReplaceDevices", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(window, [devices, false, false]);
                window.Show();
                typeof(MainWindow).GetMethod("ApplyTheme", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(window, [ThemeService.LightMode, false]);
                window.UpdateLayout(); Pump(300);
                var output = Path.Combine(Environment.CurrentDirectory, "runtime-data", "resize-audit");
                Directory.CreateDirectory(output);
                var times = new List<double>();
                var widths = new[] {1180d,1100,1020,960,920,902,898,860,760,600,460,400,600,760,860,898,902,920,960,1020,1100,1180};
                var repeats = Environment.GetEnvironmentVariable("LAN_RESIZE_PROFILE") == "1" ? 12 : 1;
                var handle = new System.Windows.Interop.WindowInteropHelper(window).Handle;
                SendMessage(handle, 0x231, IntPtr.Zero, IntPtr.Zero);
                var target = System.Windows.Interop.HwndSource.FromHwnd(handle)!.CompositionTarget;
                Console.WriteLine("WPF user-input resize fence=" + typeof(System.Windows.Interop.HwndTarget).GetField("_userInputResize", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target));
                var cycles = new List<double>();
                foreach (var width in Enumerable.Range(0, repeats).SelectMany(_ => widths))
                {
                    var watch = Stopwatch.StartNew(); window.Width = width; window.UpdateLayout(); watch.Stop();
                    times.Add(watch.Elapsed.TotalMilliseconds);
                    Console.WriteLine($"Resize width={width}: synchronous layout={watch.Elapsed.TotalMilliseconds:F2}ms");
                    watch.Start();
                    Pump(20);
                    MicaWindowService.FlushComposition(window); watch.Stop(); cycles.Add(watch.Elapsed.TotalMilliseconds);
                }
                SendMessage(handle, 0x232, IntPtr.Zero, IntPtr.Zero); Pump(60);
                Console.WriteLine($"Resize profile: max={times.Max():F2}ms, total={times.Sum():F2}ms");
                Console.WriteLine($"Resize cycles including callbacks/present and 20ms observation: max={cycles.Max():F2}ms, total={cycles.Sum():F2}ms");
                Capture(window, Path.Combine(output, "wide-light.png"));
                // Several native size messages may arrive before a queued
                // responsive callback runs. The last width must win.
                window.Width = 760; window.UpdateLayout(); window.Width = 1180; window.UpdateLayout(); Pump(80);
                if (!window.DetailSurface.IsVisible || !window.DeviceSidebar.IsVisible) throw new InvalidOperationException("Wide/narrow/wide coalescing kept a stale mode.");
                window.Width = 760; window.UpdateLayout(); Pump(100);
                typeof(MainWindow).GetMethod("TransitionCompact", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(window, [true]);
                var transform = window.DeviceSidebar.RenderTransform;
                window.Width = 1180; window.UpdateLayout(); window.Width = 760; window.UpdateLayout(); Pump(30);
                if (!ReferenceEquals(window.DeviceSidebar.RenderTransform, transform)) throw new InvalidOperationException("A coalesced narrow/wide/narrow resize cancelled navigation.");
                window.Height += 10; window.UpdateLayout();
                if (!ReferenceEquals(window.DeviceSidebar.RenderTransform, transform))
                    throw new InvalidOperationException("A height-only resize cancelled/rebuilt the compact navigation animation.");
                Pump(500);
                if (!window.DetailSurface.IsVisible || window.DeviceSidebar.IsVisible) throw new InvalidOperationException("Compact navigation did not finish.");
                window.Width = 1180; window.UpdateLayout(); Pump(100);
                if (!window.DeviceSidebar.IsVisible || !window.DetailSurface.IsVisible) throw new InvalidOperationException("Wide split layout was not restored.");
                var historyGap = InkGap(window, window.SessionHistoryTitleText, window.SessionHistoryCard);
                var trafficGap = InkGap(window, window.TrafficSummaryTitleText, window.TrafficSummaryCard);
                Console.WriteLine($"Visible title-to-card gaps: history={historyGap:F2}, traffic={trafficGap:F2}");
                if (Math.Abs(historyGap - trafficGap) > 1.1) throw new InvalidOperationException("Traffic title bottom gap does not match the history title.");
                var separator = Descendants<Border>(window).Single(b => b.Height == 1 && Grid.GetRow(b) == 3 && Grid.GetColumnSpan(b) == 2);
                var separatorLight = ((SolidColorBrush)separator.Background).Color;
                typeof(MainWindow).GetMethod("ApplyTheme", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(window, [ThemeService.DarkMode, false]); Pump(200);
                if (((SolidColorBrush)separator.Background).Color == separatorLight) throw new InvalidOperationException("Device separator retained its light-theme brush.");
                window.DetailPanel.ScrollToBottom(); Pump(100); Capture(window, Path.Combine(output, "detail-dark.png"));
                using var json = System.Text.Json.JsonDocument.Parse("""{"graph":{"name":"主路由测试","ip":"10.77.0.1","onlines":7,"leafs":[{"name":"节点测试","ip":"10.77.0.2","link_type":"wired","onlines":5}]}}""");
                window.MeshNodesItemsControl.ItemsSource = XiaomiRouterClient.ParseMeshTopology(json.RootElement);
                window.MeshNodesEmptyText.Visibility = Visibility.Collapsed;
                window.RouterInfoLoadingBorder.Visibility = Visibility.Collapsed;
                window.DetailPanel.Visibility = Visibility.Collapsed; window.RouterPanel.Visibility = Visibility.Visible;
                window.UpdateLayout(); Pump(200);
                var meshCards = Descendants<Border>(window.MeshNodesItemsControl).Where(b => b.Name == "MeshNodeCard").ToArray();
                if (meshCards.Length != 2 || ((SolidColorBrush)meshCards[0].Background).Color != ((SolidColorBrush)meshCards[1].Background).Color)
                    throw new InvalidOperationException("Dark Mesh gateway/child card surfaces differ.");
                window.RouterPanel.ScrollToVerticalOffset(300); Pump(80); Capture(window, Path.Combine(output, "mesh-dark.png"));
                typeof(MainWindow).GetMethod("ApplyTheme", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(window, [ThemeService.LightMode, false]); Pump(100);
                if (((SolidColorBrush)meshCards[0].Background).Color == ((SolidColorBrush)meshCards[1].Background).Color)
                    throw new InvalidOperationException("Existing light Mesh hierarchy was changed.");
                Capture(window, Path.Combine(output, "mesh-light.png"));
                Console.WriteLine("Resize layout PASS: continuous widths, height-only resize preserves navigation, wide restoration.");
            }
            catch(Exception exception) { failure = exception; }
            finally { window?.PrepareForExit(); window?.Close(); app?.Shutdown(); }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        if(!thread.Join(TimeSpan.FromSeconds(40))) { Console.Error.WriteLine("Resize regression timed out."); Environment.Exit(1); }
        if(failure is null) return 0;
        Console.Error.WriteLine(failure); return 1;
    }
    private static void Pump(int milliseconds)
    {
        var frame = new DispatcherFrame(); var timer = new DispatcherTimer(DispatcherPriority.Send) { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_,_) => {timer.Stop();frame.Continue=false;};timer.Start();Dispatcher.PushFrame(frame);
    }
    private static double InkGap(MainWindow window, TextBlock title, FrameworkElement card)
    {
        var ink = FieldLabelAlignment.TextInkBounds(title);
        if (ink.IsEmpty) throw new InvalidOperationException("No rendered title glyphs.");
        return card.TranslatePoint(new Point(), window).Y - title.TranslatePoint(new Point(0, ink.Bottom), window).Y;
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++) { var child=VisualTreeHelper.GetChild(root,i); if(child is T match) yield return match; foreach(var nested in Descendants<T>(child)) yield return nested; }
    }
    private static void Capture(MainWindow window, string output)
    {
        var info = new ProcessStartInfo(Path.GetFullPath(".tools/dotnet/dotnet.exe")) { UseShellExecute=false, CreateNoWindow=true };
        info.ArgumentList.Add(Path.GetFullPath(".tools/native-capture/bin/Debug/net8.0-windows10.0.19041.0/capture.dll"));
        info.ArgumentList.Add(new System.Windows.Interop.WindowInteropHelper(window).Handle.ToInt64().ToString()); info.ArgumentList.Add(output);
        using var process = Process.Start(info)!; while(!process.HasExited) Pump(20);
        if(process.ExitCode!=0) throw new InvalidOperationException("Native fixture capture failed.");
    }
    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr handle, int message, IntPtr word, IntPtr parameter);
}
