using System.Drawing.Imaging;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using LanDeviceMonitor;
using LanDeviceMonitor.Services;
using Brushes = System.Windows.Media.Brushes;
using PixelFormat = System.Drawing.Imaging.PixelFormat;

// This is an on-screen integration test: an opaque snapshot must prevent the
// sharp checkerboard behind the window from leaking into the native animation.
internal static class MinimizeCompositionRegression
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<int, Process> OwnedProcesses = new();
    internal static int Analyze(string video)
    {
        var (sampled, leaked) = AnalyzeVideo(video, Path.GetDirectoryName(Path.GetFullPath(video))!);
        Console.WriteLine($"Material samples={sampled}, sharp-background leaks={leaked}");
        return sampled >= 30 && leaked == 0 ? 0 : 1;
    }
    internal static int Run()
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            LanDeviceMonitor.App? app = null;
            Window? backdrop = null;
            MainWindow? window = null;
            Process? recorder = null;
            try
            {
                if (!SystemParameters.ClientAreaAnimation)
                    throw new InvalidOperationException("Native animations are disabled; pixel verification cannot run.");
                app = TestAppFactory.Create();
                app.InitializeComponent();
                app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                var drawing = new DrawingGroup();
                drawing.Children.Add(new GeometryDrawing(Brushes.White, null, new RectangleGeometry(new Rect(0, 0, 32, 32))));
                drawing.Children.Add(new GeometryDrawing(Brushes.Black, null, new RectangleGeometry(new Rect(0, 0, 16, 16))));
                drawing.Children.Add(new GeometryDrawing(Brushes.Black, null, new RectangleGeometry(new Rect(16, 16, 16, 16))));
                backdrop = new Window
                {
                    Left = 0, Top = 0, Width = 2048, Height = 1120,
                    Topmost = true, ShowActivated = false, ShowInTaskbar = false,
                    WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize,
                    Background = new DrawingBrush(drawing)
                    {
                        TileMode = TileMode.Tile, Viewport = new Rect(0, 0, 32, 32),
                        ViewportUnits = BrushMappingMode.Absolute
                    }
                };
                var marker = new System.Windows.Controls.Border
                {
                    Width = 24, Height = 24, Margin = new Thickness(40, 0, 0, 0),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Top,
                    Background = new SolidColorBrush(Color.FromRgb(20, 224, 240))
                };
                backdrop.Content = marker;
                backdrop.Show();
                if (Math.Abs(VisualTreeHelper.GetDpi(backdrop).DpiScaleX - 1.25) > 0.01 ||
                    System.Windows.Forms.Screen.PrimaryScreen is not { Bounds.Width: >= 2000, Bounds.Height: >= 1200 })
                    throw new InvalidOperationException("Pixel fixture requires a primary screen >=2000x1200 at 125% scaling.");
                window = new MainWindow(new NotificationService())
                {
                    WindowStartupLocation = WindowStartupLocation.Manual,
                    Left = 330, Top = 50, Width = 1180, Height = 760,
                    Topmost = true, ShowInTaskbar = true
                };
                window.Show();
                window.Activate();
                typeof(MainWindow).GetMethod("ApplyTheme", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(window, [ThemeService.LightMode, false]);
                Pump(700);
                var output = Path.Combine(Path.GetTempPath(), "LanDeviceMonitor-minimize-composition");
                Directory.CreateDirectory(output);
                var video = Path.Combine(output, "native-animation.mp4");
                recorder = StartFfmpeg(["-y", "-hide_banner", "-loglevel", "error", "-f", "gdigrab", "-framerate", "60", "-video_size", "2000x1200", "-i", "desktop", "-t", "60", "-c:v", "libx264", "-preset", "ultrafast", "-crf", "16", video]);
                Pump(500);
                int failureCount = 0;
                int cycleIndex = 0;
                foreach (var mode in new[] { ThemeService.LightMode, ThemeService.DarkMode })
                {
                    typeof(MainWindow).GetMethod("ApplyTheme", BindingFlags.Instance | BindingFlags.NonPublic)!
                        .Invoke(window, [mode, false]);
                    Pump(600);
                    var baseline = CaptureMaterialMeans(window);
                    for (int cycle = 0; cycle < 8; cycle++)
                    {
                        // Do not synchronously read pixels on the UI thread or
                        // between render callbacks: that can accidentally mask
                        // the race by giving WPF time to present its bitmap.
                        marker.Background = new SolidColorBrush(Color.FromRgb((byte)(20 + 12 * cycleIndex), 32, 240));
                        Pump(50);
                        if (cycle % 2 == 0) window.RequestNativeMinimize();
                        else window.RequestMinimizeWithTransitionMaterial();
                        Pump(500);
                        if (window.WindowState != WindowState.Minimized) failureCount++;
                        Console.WriteLine($"{mode} cycle={cycle}: minimized={window.WindowState == WindowState.Minimized}");
                        marker.Background = new SolidColorBrush(Color.FromRgb((byte)(20 + 12 * cycleIndex), 160, 240));
                        Pump(50);
                        window.WindowState = WindowState.Normal;
                        window.Activate();
                        Pump(500);
                        var restored = CaptureMaterialMeans(window);
                        double difference = baseline.Zip(restored, (a, b) => Math.Abs(a - b)).Max();
                        if (difference > 3 || window.WindowTransitionSnapshot.Source is not null ||
                            window.WindowTransitionSnapshot.Visibility != Visibility.Collapsed ||
                            !ReferenceEquals(window.WindowRoot.Background, Brushes.Transparent))
                            throw new InvalidOperationException($"Normal Acrylic was not restored: {mode}/{cycle}, mean pixel difference={difference:F2}");
                        Console.WriteLine($"Normal material {mode}/{cycle}: maximum mean pixel difference={difference:F2}");
                        marker.Background = new SolidColorBrush(Color.FromRgb((byte)(20 + 12 * cycleIndex), 224, 240));
                        cycleIndex++;
                    }
                }
                recorder.StandardInput.WriteLine("q");
                recorder.StandardInput.Flush();
                if (!recorder.WaitForExit(10000) || recorder.ExitCode != 0)
                    throw new InvalidOperationException("Native animation recording failed.");
                window.MinimizeToTrayRequested += (_, _) =>
                    window.Dispatcher.BeginInvoke(new Action(window.HideToTrayPreservingVisual), DispatcherPriority.Normal);
                window.RequestMinimizeWithTransitionMaterial();
                window.Close();
                Pump(150);
                var handle = new System.Windows.Interop.WindowInteropHelper(window).Handle;
                if (window.WindowState == WindowState.Minimized || IsWindowVisible(handle) ||
                    window.WindowTransitionSnapshot.Source is not null)
                    throw new InvalidOperationException("Close-to-tray failed to cancel the queued minimize.");
                window.RequestMinimizeWithTransitionMaterial();
                Pump(100);
                if (window.WindowState == WindowState.Minimized)
                    throw new InvalidOperationException("A hidden tray HWND was minimized by a stale request.");
                window.RestoreFromTray();
                window.Activate();
                Pump(300);
                window.RequestMinimizeWithTransitionMaterial();
                window.PrepareForExit();
                Pump(100);
                if (window.WindowState == WindowState.Minimized || window.WindowTransitionSnapshot.Source is not null)
                    throw new InvalidOperationException("Exit failed to cancel the queued minimize.");
                Console.WriteLine("Queued minimize cancellation PASS: tray hide, hidden HWND, restore and exit.");
                var (sampled, leaked) = AnalyzeVideo(video, output, requireCoverage: true);
                Console.WriteLine($"Actual native material frames={sampled}, sharp-background leaks={leaked}");
                if (failureCount > 0 || sampled < 30 || leaked > 0)
                    throw new InvalidOperationException($"Native animation pixel regression failed. State failures={failureCount}, leaks={leaked}, samples={sampled}. Evidence: {output}");
                Console.WriteLine("Minimize composition PASS: 16 light/dark native/custom cycles, no sharp checkerboard leaked.");
            }
            catch (Exception exception) { error = exception; }
            finally
            {
                try
                {
                    window?.PrepareForExit();
                    window?.Close();
                    backdrop?.Close();
                    app?.Shutdown();
                }
                finally { StopOwnedProcess(recorder); }
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        if (!thread.Join(TimeSpan.FromSeconds(90)))
        {
            Console.Error.WriteLine("Minimize composition regression timed out.");
            foreach (var process in OwnedProcesses.Values.ToArray())
                try { StopOwnedProcess(process); } catch (Exception exception) { Console.Error.WriteLine(exception.Message); }
            // This mode runs in a dedicated test process. Exiting releases its
            // native HWNDs even if the STA dispatcher itself is stuck.
            Environment.Exit(1);
        }
        if (error is null) return 0;
        Console.Error.WriteLine(error);
        return 1;
    }

    private static Process StartFfmpeg(string[] arguments, bool output = false)
    {
        var info = new ProcessStartInfo("ffmpeg") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = output };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        var process = Process.Start(info)!;
        OwnedProcesses[process.Id] = process;
        return process;
    }

    private static void StopOwnedProcess(Process? process)
    {
        if (process is null) return;
        try
        {
            if (!process.HasExited)
            {
                try { process.StandardInput.WriteLine("q"); process.StandardInput.Flush(); } catch (IOException) { }
                if (!process.WaitForExit(3000)) { process.Kill(entireProcessTree: true); process.WaitForExit(); }
            }
        }
        finally { OwnedProcesses.TryRemove(process.Id, out _); process.Dispose(); }
    }

    private static double[] CaptureMaterialMeans(MainWindow window)
    {
        var handle = new System.Windows.Interop.WindowInteropHelper(window).Handle;
        if (!GetWindowRect(handle, out var bounds)) throw new InvalidOperationException("Cannot read normal window bounds.");
        using var bitmap = new System.Drawing.Bitmap(bounds.Right - bounds.Left, bounds.Bottom - bounds.Top);
        using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
            graphics.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bitmap.Size);
        var result = new List<double>();
        foreach (var (x, y) in new[] { (0.45, 0.015), (0.13, 0.45), (0.7, 0.28) })
        {
            double red = 0, green = 0, blue = 0;
            for (int dy = 0; dy < 12; dy++)
                for (int dx = 0; dx < 30; dx++)
                {
                    var pixel = bitmap.GetPixel((int)(bitmap.Width * x) + dx, (int)(bitmap.Height * y) + dy);
                    red += pixel.R; green += pixel.G; blue += pixel.B;
                }
            result.AddRange([red / 360, green / 360, blue / 360]);
        }
        return result.ToArray();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRectangle { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr handle, out NativeRectangle bounds);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr handle);

    private static (int Sampled, int Leaked) AnalyzeVideo(string video, string output, bool requireCoverage = false)
    {
        using var decoder = StartFfmpeg(["-hide_banner", "-loglevel", "error", "-i", video, "-vf", "scale=1000:600", "-pix_fmt", "bgra", "-f", "rawvideo", "pipe:1"], output: true);
        var buffer = new byte[1000 * 600 * 4];
        int sampled = 0, leaked = 0, frameNumber = 0;
        bool fixtureReady = false;
        Measurement? baseline = null;
        var coverage = new int[16, 2];
        try
        {
        while (true)
        {
            int read = 0;
            while (read < buffer.Length)
            {
                int count = decoder.StandardOutput.BaseStream.Read(buffer, read, buffer.Length - read);
                if (count == 0) break;
                read += count;
            }
            if (read == 0) break;
            if (read != buffer.Length) throw new InvalidDataException("Incomplete decoded frame.");
            frameNumber++;
            using var bitmap = new System.Drawing.Bitmap(1000, 600, PixelFormat.Format32bppArgb);
            var data = bitmap.LockBits(new System.Drawing.Rectangle(0, 0, 1000, 600), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            Marshal.Copy(buffer, 0, data.Scan0, buffer.Length);
            bitmap.UnlockBits(data);
            var measurement = MeasureMaterialContrast(bitmap);
            if (measurement is not { } current) continue;
            int value = current.Contrast;
            // Initial Show() has its own native entrance animation, before the
            // fixture applies Acrylic. Start at the first stable material frame.
            if (!fixtureReady)
            {
                if (value > 20) continue;
                fixtureReady = true;
            }
            baseline ??= current;
            if (requireCoverage)
            {
                var marker = bitmap.GetPixel(33, 5);
                int cycle = (marker.R - 20 + 6) / 12;
                int phase = marker.G is > 15 and < 55 ? 0 : marker.G is > 140 and < 180 ? 1 : -1;
                if (marker.B < 220 || cycle is < 0 or > 15 || phase < 0) continue;
                bool moving = current.Width < baseline.Value.Width * 0.9 ||
                    Math.Abs(current.Center - baseline.Value.Center) > 10 || Math.Abs(current.Top - baseline.Value.Top) > 10;
                if (!moving) continue;
                coverage[cycle, phase]++;
            }
            sampled++;
            if (value > 90)
            {
                if (leaked < 5) Console.WriteLine($"Leak frame={frameNumber}, contrast={value}");
                if (leaked < 5) bitmap.Save(Path.Combine(output, $"leak-{leaked}.png"));
                leaked++;
            }
        }
        decoder.WaitForExit();
        if (decoder.ExitCode != 0) throw new InvalidOperationException("Video pixel decoding failed.");
        if (requireCoverage)
            for (int cycle = 0; cycle < 16; cycle++)
            {
                Console.WriteLine($"Native moving-frame coverage cycle={cycle}: minimize={coverage[cycle, 0]}, restore={coverage[cycle, 1]}");
                if (coverage[cycle, 0] < 1 || coverage[cycle, 1] < 1)
                    throw new InvalidOperationException($"Cycle {cycle} has insufficient actual native-animation pixel coverage.");
            }
        return (sampled, leaked);
        }
        finally { StopOwnedProcess(decoder); }
    }

    private readonly record struct Measurement(int Contrast, int Width, int Center, int Top);

    private static Measurement? MeasureMaterialContrast(System.Drawing.Bitmap bitmap)
    {
        var data = bitmap.LockBits(new System.Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var bytes = new byte[data.Stride * data.Height];
            Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
            int Gray(int x, int y)
            {
                int p = y * data.Stride + x * 4;
                return (bytes[p] + bytes[p + 1] + bytes[p + 2]) / 3;
            }
            // ffmpeg can start before the synthetic backdrop is shown. Never
            // classify unrelated desktop/app pixels as test material.
            if (Gray(2, 2) > 30 || Gray(12, 2) < 220 || Gray(2, 12) < 220 || Gray(12, 12) > 30)
                return null;
            // The empty-detail monitor glyph is the only large blue cluster in
            // this fixture. Track it as DWM moves/scales the window; inspecting
            // a fixed screen coordinate would accidentally sample the desktop.
            var clusters = new Dictionary<(int X, int Y), List<(int X, int Y)>>();
            for (int y = 80; y < 580; y++)
                for (int x = 175; x < 970; x++)
                {
                    int p = y * data.Stride + x * 4;
                    if (bytes[p] > bytes[p + 2] + 45 && bytes[p + 2] is > 35 and < 180)
                    {
                        var key = (x / 40, y / 40);
                        if (!clusters.TryGetValue(key, out var points)) clusters[key] = points = [];
                        points.Add((x, y));
                    }
                }
            var glyph = clusters.Values.OrderByDescending(points => points.Count).FirstOrDefault();
            if (glyph is null || glyph.Count < 8) return null;
            int left = glyph.Min(p => p.X), right = glyph.Max(p => p.X), top = glyph.Min(p => p.Y);
            int width = right - left;
            if (width < 5) return null; // Final tiny/fading frames have no usable material patch.
            int center = (left + right) / 2, min = 255, max = 0;
            for (int y = top - width * 3; y < top - width * 2; y += 2)
                for (int x = center - width; x <= center + width; x += 2)
                {
                    if (x < 0 || y < 0 || x >= bitmap.Width || y >= bitmap.Height) continue;
                    int p = y * data.Stride + x * 4;
                    int luminance = (bytes[p] + bytes[p + 1] + bytes[p + 2]) / 3;
                    min = Math.Min(min, luminance);
                    max = Math.Max(max, luminance);
                }
            return new Measurement(max - min, width, center, top);
        }
        finally { bitmap.UnlockBits(data); }
    }

    private static void Pump(int milliseconds)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }
}
