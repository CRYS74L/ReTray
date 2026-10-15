using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using LanDeviceMonitor;
using LanDeviceMonitor.Models;
using LanDeviceMonitor.Services;
using LanDeviceMonitor.ViewModels;
using LanDeviceMonitor.Controls;

// Opt-in, synthetic desktop reference. Never initializes the real monitor, loads
// saved settings/passwords, registers startup, activates the window, or moves input.
internal static class DesktopWebReferenceCapture
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    internal static int Run()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            App? app = null;
            MainWindow? window = null;
            try
            {
                app = TestAppFactory.Create();
                app.InitializeComponent();
                app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                window = new MainWindow(new NotificationService())
                {
                    WindowStartupLocation = WindowStartupLocation.Manual,
                    Left = -10000, Top = -10000, Width = 1180, Height = 900,
                    ShowActivated = false, ShowInTaskbar = false
                };
                SetField(window, "_isPopulatingAppSettings", true);
                // The relative XAML URI resolves against the test executable,
                // where the app's content asset is not guaranteed to be copied.
                // Point only this synthetic window at the same source bitmap;
                // retain the existing 30 DIP shell / 86 DIP empty-state sizes.
                var iconPath = Path.Combine(Environment.CurrentDirectory, "src", "LanDeviceMonitor.App", "Assets", "AppIcon.png");
                if (!File.Exists(iconPath))
                    throw new FileNotFoundException("The desktop reference needs the app's actual AppIcon.png.", iconPath);
                var icon = new BitmapImage();
                icon.BeginInit();
                icon.CacheOption = BitmapCacheOption.OnLoad;
                icon.UriSource = new Uri(iconPath, UriKind.Absolute);
                icon.EndInit();
                icon.Freeze();
                window.ShellAppIcon.Source = icon;
                var now = DateTimeOffset.Now;
                var devices = MakeDevices(now);
                var history = Enumerable.Range(0, 80).Select(i => new TrafficSample(
                    now.AddSeconds(i - 79), (long)(30000 + Math.Sin(i * .3) * 15000),
                    (long)(400000 + Math.Cos(i * .25) * 220000))).ToArray();
                foreach (var device in devices)
                {
                    var buffer = (TrafficHistoryBuffer)Invoke(window, "GetTrafficHistory", device.Profile.MacAddress)!;
                    foreach (var sample in history) buffer.Append(sample);
                }
                Invoke(window, "ReplaceDevices", devices, false, false);
                // Desktop sorts its own list. Select the preview fixture's first
                // device explicitly, rather than relying on that sort order.
                var selected = window.DeviceList.Items.Cast<DeviceItemViewModel>()
                    .Single(d => d.MacAddress == devices[0].Profile.MacAddress);
                SetField(window, "_isReplacingDevices", true);
                window.DeviceList.SelectedItem = selected;
                SetField(window, "_isReplacingDevices", false);
                Invoke(window, "UpdateSelectedDevice", selected, true);
                FillRouter(window);
                window.Show();
                Pump(100);
                var output = Path.Combine(Environment.CurrentDirectory, "runtime-data",
                    "web-desktop-parity-dev270", "desktop");
                Directory.CreateDirectory(output);
                WriteJson(Path.Combine(output, "fixture.json"), new
                {
                    synthetic = true, selectedDevice = devices[0].Profile.CustomName,
                    selectedMac = devices[0].Profile.MacAddress, generatedAt = now,
                    devices, history,
                    screenshotMethod = "WPF RenderTargetBitmap with VisualBrush over a neutral opaque background",
                    limitation = "Native DWM Acrylic/backdrop and window-manager shadows are not captured; this is a layout and WPF ink reference. No user desktop screenshot was taken."
                });
                WriteAnimationReference(output);
                foreach (var mode in new[] { ThemeService.LightMode, ThemeService.DarkMode })
                {
                    ThemeService.Apply(mode);
                    SetField(window, "_currentThemeMode", mode);
                    FillSettings(window, mode);
                    WritePalette(output, mode);
                    foreach (var size in new[] { (Width: 1180d, Height: 900d), (Width: 1218d, Height: 956d), (Width: 500d, Height: 1000d), (Width: 380d, Height: 900d) })
                    {
                        window.Width = size.Width;
                        window.Height = size.Height;
                        window.UpdateLayout();
                        Pump(100);
                        if (size.Width < 900)
                        {
                            SetField(window, "_compactDetail", false);
                            Invoke(window, "ApplyCompactLayout");
                            Pump(80);
                            Capture(window, output, $"{mode}-{size.Width:0}x{size.Height:0}-list", mode);
                        }
                        // Navigation opens the actual compact detail surface. Wait
                        // for the longest current transition, then measure settled ink.
                        foreach (var page in new[] { "devices", "router", "settings" })
                        {
                            switch (page)
                            {
                                case "devices": Invoke(window, "ShowDevicesPanel", false, false); break;
                                case "router": Invoke(window, "ShowRouterPanel"); break;
                                default: Invoke(window, "ShowSettingsPanel"); break;
                            }
                            Pump(1050);
                            var viewer = page switch
                            {
                                "devices" => window.DetailPanel,
                                "router" => window.RouterPanel,
                                _ => window.SettingsPanel
                            };
                            viewer.ScrollToTop();
                            Pump(80);
                            window.UpdateLayout();
                            if (!viewer.IsVisible || !window.DetailSurface.IsVisible)
                                throw new InvalidOperationException($"Reference page is hidden: {mode}/{size}/{page}.");
                            var name = $"{mode}-{size.Width:0}x{size.Height:0}-{page}";
                            Capture(window, output, name, mode);
                            // A second viewport exposes settings/history/mesh below
                            // the fold without altering the accepted window geometry.
                            if (viewer.ScrollableHeight > 1)
                            {
                                viewer.ScrollToBottom();
                                Pump(80);
                                Capture(window, output, name + "-bottom", mode);
                                viewer.ScrollToTop();
                            }
                        }
                    }
                }
                Console.WriteLine($"DESKTOP_REFERENCE_OUTPUT={output}");
                Console.WriteLine("PASS synthetic desktop references: 客厅电视 / 02:00:00:00:00:00; light/dark, 1180x900, 1218x956, 500x1000, 380x900; device/router/settings, compact list, lower viewports, measured ink and animation constants.");
            }
            catch (Exception exception) { failure = exception; }
            finally { window?.PrepareForExit(); window?.Close(); app?.Shutdown(); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        if (!thread.Join(TimeSpan.FromSeconds(90)))
        {
            Console.Error.WriteLine("Desktop reference capture timed out.");
            return 1;
        }
        if (failure is null) return 0;
        Console.Error.WriteLine(failure);
        return 1;
    }

    private static DeviceViewData[] MakeDevices(DateTimeOffset now)
    {
        var names = new[] { "客厅电视", "我的手机", "工作电脑", "书房音箱", "卧室空调", "门口摄像头" };
        var types = new[] { "电视", "手机", "笔记本电脑", "智能音箱", "空调", "摄像头" };
        return names.Select((name, i) => new DeviceViewData
        {
            Profile = new DeviceProfile
            {
                MacAddress = $"02:00:00:00:00:0{i}", CustomName = name,
                HasObservedState = true, LastKnownOnline = i != 4,
                LastOriginalName = $"Device-{i + 1}", LastRouterName = name,
                FirstSeenAt = now.AddDays(-20), LastSeenAt = now.AddMinutes(i == 4 ? -5 : 0),
                LastIpAddress = $"192.168.31.{101 + i}", LastConnectionType = i % 3,
                LastUploadSpeedBytes = i == 4 ? 0 : 1234 * (i + 1),
                LastDownloadSpeedBytes = i == 4 ? 0 : 234567 * (i + 1),
                LastTotalUploadBytes = 67891234, LastTotalDownloadBytes = 3456789123,
                NotifyWhenOnline = true, NotifyWhenOffline = true,
                HasFreshUploadGraphSample = true, HasFreshDownloadGraphSample = true
            },
            Identity = DeviceIdentityService.AvailableTypes.First(t => t.TypeName == types[i]),
            Sessions = Enumerable.Range(0, i == 1 ? 95 : 3).Select(j => new DeviceSession
            {
                MacAddress = $"02:00:00:00:00:0{i}", OnlineAt = now.AddHours(-2 - j * 4),
                OfflineAt = j == 0 ? (i == 4 ? now.AddMinutes(-5) : null) : now.AddHours(-1 - j * 4)
            }).ToArray()
        }).ToArray();
    }

    private static void FillRouter(MainWindow window)
    {
        // Same documentation/test addresses as the web PreviewAsync fixture.
        var values = new Dictionary<string, string>
        {
            ["RouterConnectionBadgeText"] = "互联网已连接", ["Ipv4TypeText"] = "DHCP",
            ["Ipv4AddressText"] = "203.0.113.5", ["Ipv4MaskText"] = "255.255.255.0",
            ["Ipv4GatewayText"] = "203.0.113.1", ["Ipv4DnsText"] = "192.0.2.53",
            ["Ipv6TypeText"] = "Native", ["WanIpv6AddressText"] = "2001:db8::5",
            ["WanIpv6GatewayText"] = "fe80::1", ["LanIpv6AddressText"] = "2001:db8:1::1",
            ["LanIpv6PrefixText"] = "2001:db8:1::/64", ["Ipv6DnsText"] = "2001:db8::53"
        };
        foreach (var (name, value) in values) ((TextBlock)window.FindName(name)).Text = value;
        window.RouterInfoLoadingBorder.Visibility = Visibility.Collapsed;
        window.MeshNodesEmptyText.Visibility = Visibility.Collapsed;
        window.MeshNodesItemsControl.ItemsSource = new[]
        {
            new MeshNodeInfo("客厅主路由", "10.77.0.1", "AX3000T", "网线接入", 4, true),
            new MeshNodeInfo("书房节点", "10.77.0.2", "AX3000T", "5GHz", 1, false)
        };
    }

    private static void FillSettings(MainWindow window, string mode)
    {
        // Do not call PopulateSettings: it probes stored credentials and can
        // migrate the Windows startup registration even with the guard set.
        SetField(window, "_isPopulatingAppSettings", true);
        var settings = new AppSettings();
        window.RouterAddressBox.Text = settings.RouterAddress;
        window.StartWithWindowsCheck.IsChecked = settings.StartWithWindows;
        window.DoNotDisturbCheck.IsChecked = settings.DoNotDisturbEnabled;
        window.DoNotDisturbStartBox.Text = NotificationSchedule.FormatTime(settings.DoNotDisturbStartMinutes);
        window.DoNotDisturbEndBox.Text = NotificationSchedule.FormatTime(settings.DoNotDisturbEndMinutes);
        window.NotifyNewDeviceOnlineCheck.IsChecked = settings.NotifyWhenNewDeviceOnline;
        window.NotifyIpv6NonNativeCheck.IsChecked = settings.NotifyWhenIpv6NotNative;
        window.LanAccessCheck.IsChecked = settings.LanAccessEnabled;
        window.ThemeModeCombo.SelectedItem = window.ThemeModeCombo.Items.Cast<ComboBoxItem>()
            .Single(item => item.Tag?.ToString() == mode);
        window.RefreshIntervalCombo.SelectedItem = window.RefreshIntervalCombo.Items.Cast<ComboBoxItem>()
            .Single(item => item.Tag?.ToString() == settings.RefreshIntervalSeconds.ToString());
        // Password stays empty. This test never invokes the credential service.
    }

    private static void Capture(MainWindow window, string output, string name, string mode)
    {
        window.UpdateLayout();
        var bounds = new Rect(0, 0, window.ActualWidth, window.ActualHeight);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var neutral = (Color)ColorConverter.ConvertFromString(mode == ThemeService.DarkMode ? "#202020" : "#EEEEEE");
            dc.DrawRectangle(new SolidColorBrush(neutral), null, bounds);
            dc.DrawRectangle(new VisualBrush(window) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left,
                AlignmentY = AlignmentY.Top }, null, bounds);
        }
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(bounds.Width), (int)Math.Ceiling(bounds.Height),
            96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var file = File.Create(Path.Combine(output, name + ".png"))) encoder.Save(file);
        var elements = Descendants(window).OfType<FrameworkElement>()
            .Where(e => e.IsVisible && e.ActualWidth > 0 && e.ActualHeight > 0)
            .Select((element, index) => Describe(element, window, index)).ToArray();
        WriteJson(Path.Combine(output, name + ".json"), new
        {
            mode, name, units = "WPF DIP; PNG is rendered at 96 DPI / 1 pixel per DIP",
            requestedWindow = new { window.Width, window.Height },
            actualWindow = new { window.ActualWidth, window.ActualHeight },
            sourceDpi = VisualTreeHelper.GetDpi(window).DpiScaleX,
            neutralBackground = mode == ThemeService.DarkMode ? "#202020" : "#EEEEEE",
            selectedMac = window.SelectedDevice?.MacAddress,
            pageScroll = new
            {
                device = window.DetailPanel.VerticalOffset, router = window.RouterPanel.VerticalOffset,
                settings = window.SettingsPanel.VerticalOffset
            },
            elements,
            scrollbars = Descendants(window).OfType<ScrollBar>().Where(bar => bar.IsVisible)
                .Select(DescribeScrollbar).ToArray(),
            fieldLabels = Descendants(window).OfType<TextBlock>().Where(label => label.IsVisible
                && FieldLabelAlignment.GetTarget(label) is { IsVisible: true }).Select(DescribeFieldLabel).ToArray()
        });
    }

    private static object Describe(FrameworkElement element, MainWindow window, int index)
    {
        var origin = element.TranslatePoint(new Point(), window);
        var typography = element switch
        {
            TextBlock text => Typography(text.FontFamily, text.FontSize, text.FontWeight, text.FontStyle, text.FontStretch),
            Control control => Typography(control.FontFamily, control.FontSize, control.FontWeight, control.FontStyle, control.FontStretch),
            _ => Typography(TextElement.GetFontFamily(element), TextElement.GetFontSize(element),
                TextElement.GetFontWeight(element), TextElement.GetFontStyle(element), TextElement.GetFontStretch(element))
        };
        return new
        {
            index, name = element.Name, type = element.GetType().Name,
            owner = AncestorPath(element),
            x = origin.X, y = origin.Y, width = element.ActualWidth, height = element.ActualHeight,
            declaredWidth = Finite(element.Width), declaredHeight = Finite(element.Height),
            minWidth = element.MinWidth, minHeight = element.MinHeight,
            minWidthSource = ValueSource(element, FrameworkElement.MinWidthProperty),
            widthSource = ValueSource(element, FrameworkElement.WidthProperty),
            margin = Box(element.Margin), horizontalAlignment = element.HorizontalAlignment.ToString(),
            verticalAlignment = element.VerticalAlignment.ToString(),
            verticalContentAlignment = element is Control contentControl ? contentControl.VerticalContentAlignment.ToString() : null,
            horizontalContentAlignment = element is Control alignmentControl ? alignmentControl.HorizontalContentAlignment.ToString() : null,
            textFormattingMode = TextOptions.GetTextFormattingMode(element).ToString(),
            textRenderingMode = TextOptions.GetTextRenderingMode(element).ToString(),
            textHintingMode = TextOptions.GetTextHintingMode(element).ToString(),
            clearTypeHint = RenderOptions.GetClearTypeHint(element).ToString(),
            element.UseLayoutRounding, element.SnapsToDevicePixels,
            textLineHeight = element is TextBlock lineText ? Finite(lineText.LineHeight) : null,
            textLineStacking = element is TextBlock stackingText ? stackingText.LineStackingStrategy.ToString() : null,
            textBaselineOffset = element is TextBlock baselineText ? Finite(baselineText.BaselineOffset) : null,
            inkBoundsLocal = element is TextBlock or TextBox or PasswordBox ? RectData(FieldLabelAlignment.TextInkBounds(element)) : null,
            renderTransform = element.RenderTransform.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
            opacityMask = element.OpacityMask is null ? null : element.OpacityMask.GetType().Name,
            padding = element is Border border ? Box(border.Padding) : element is Control c ? Box(c.Padding) : null,
            cornerRadius = element is Border b ? new[] { b.CornerRadius.TopLeft, b.CornerRadius.TopRight,
                b.CornerRadius.BottomRight, b.CornerRadius.BottomLeft } : null,
            text = element is TextBlock tb ? tb.Text : element is TextBox box ? box.Text : null,
            foreground = element is TextBlock t ? Brush(t.Foreground) : element is Control ctl ? Brush(ctl.Foreground) : null,
            background = element is Border bd ? Brush(bd.Background) : element is Panel panel ? Brush(panel.Background)
                : element is Control ct ? Brush(ct.Background) : null,
            typography,
            intersectsViewport = origin.X + element.ActualWidth > 0 && origin.Y + element.ActualHeight > 0
                && origin.X < window.ActualWidth && origin.Y < window.ActualHeight
        };
    }

    private static object Typography(FontFamily family, double size, FontWeight weight, FontStyle style, FontStretch stretch)
    {
        var face = new Typeface(family, style, weight, stretch);
        var resolved = face.TryGetGlyphTypeface(out var glyph) ? glyph.FontUri.ToString() : null;
        return new { family = family.Source, size, weight = weight.ToString(), resolvedFont = resolved };
    }

    private static object DescribeScrollbar(ScrollBar bar)
    {
        var viewer = Ancestors(bar).OfType<ScrollViewer>().FirstOrDefault();
        var presenter = viewer is null ? null : Descendants(viewer).OfType<ScrollContentPresenter>()
            .FirstOrDefault(p => p.Name == "PART_ScrollContentPresenter");
        var track = bar.Template.FindName("PART_Track", bar) as Track;
        var thumb = track?.Thumb;
        var defaultInk = thumb is null ? null : Descendants(thumb).OfType<Border>().FirstOrDefault(b => b.Name == "ThumbDefaultVisual");
        var hoverInk = thumb is null ? null : Descendants(thumb).OfType<Border>().FirstOrDefault(b => b.Name == "ThumbHoverVisual");
        var barOrigin = viewer is null ? new Point() : bar.TranslatePoint(new Point(), viewer);
        var presenterOrigin = presenter is null || viewer is null ? new Point() : presenter.TranslatePoint(new Point(), viewer);
        object? Location(FrameworkElement? element)
        {
            if (element is null || viewer is null) return null;
            var point = element.TranslatePoint(new Point(), viewer);
            return new { x = point.X, y = point.Y, width = element.ActualWidth, height = element.ActualHeight,
                margin = Box(element.Margin), minWidth = element.MinWidth, minHeight = element.MinHeight,
                opacity = element.Opacity };
        }
        return new
        {
            viewer = viewer?.Name, orientation = bar.Orientation.ToString(),
            viewerWidth = viewer?.ActualWidth, viewerHeight = viewer?.ActualHeight,
            barDeclaredWidth = Finite(bar.Width), barMinWidth = bar.MinWidth,
            barMinWidthSource = ValueSource(bar, FrameworkElement.MinWidthProperty),
            SystemParameters.VerticalScrollBarWidth,
            contentPresenter = Location(presenter), bar = Location(bar), track = Location(track),
            thumb = Location(thumb), defaultInk = Location(defaultInk), hoverInk = Location(hoverInk),
            contentReserve = viewer is null || presenter is null ? (double?)null
                : viewer.ActualWidth - presenterOrigin.X - presenter.ActualWidth,
            barOffsetFromPresenterRight = presenter is null ? (double?)null
                : barOrigin.X - presenterOrigin.X - presenter.ActualWidth,
            viewport = bar.ViewportSize, maximum = bar.Maximum, value = bar.Value,
            formula = "For vertical scrolling: reserve=viewerWidth-presenterX-presenterWidth; barLeft=presenterRight+barOffset; trackY=barY+round(2*dpi)/dpi; defaultInkRight=barRight-round(2*dpi)/dpi; hoverInkRight=barRight-round(1*dpi)/dpi; visible ink width is rounded 3 or 5 DIP."
        };
    }

    private static object DescribeFieldLabel(TextBlock label)
    {
        var target = FieldLabelAlignment.GetTarget(label)!;
        var targetInk = FieldLabelAlignment.TextInkBounds(target);
        var labelInk = FieldLabelAlignment.TextInkBounds(label);
        var point = labelInk.IsEmpty ? new Point() : label.TranslatePoint(new Point(labelInk.Left, labelInk.Top), target);
        var offset = FieldLabelAlignment.GetHorizontalOffset(label);
        return new
        {
            label = label.Name, target = target.Name, text = label.Text,
            targetInk = RectData(targetInk), labelInk = RectData(labelInk),
            labelInkOriginInTarget = new { x = point.X, y = point.Y },
            horizontalOffset = offset,
            leftError = targetInk.IsEmpty || labelInk.IsEmpty ? (double?)null : point.X - (targetInk.Left + offset),
            formula = "labelWorldX + labelGlyphInkLeft = targetWorldX + targetGlyphInkLeft + horizontalOffset; never align text bounding boxes or padding alone."
        };
    }

    private static object? RectData(Rect bounds) => bounds.IsEmpty ? null
        : new { x = bounds.X, y = bounds.Y, width = bounds.Width, height = bounds.Height };
    private static double? Finite(double value) => double.IsFinite(value) ? value : null;
    private static object ValueSource(DependencyObject owner, DependencyProperty property)
    {
        var source = DependencyPropertyHelper.GetValueSource(owner, property);
        return new { baseValueSource = source.BaseValueSource.ToString(), source.IsExpression, source.IsAnimated, source.IsCoerced };
    }
    private static IEnumerable<DependencyObject> Ancestors(DependencyObject child)
    {
        for (var parent = VisualTreeHelper.GetParent(child); parent is not null; parent = VisualTreeHelper.GetParent(parent)) yield return parent;
    }
    private static string AncestorPath(FrameworkElement element) => string.Join("/", Ancestors(element).OfType<FrameworkElement>()
        .Take(10).Reverse().Select(parent => parent.GetType().Name + (string.IsNullOrEmpty(parent.Name) ? "" : "#" + parent.Name)));

    private static double[] Box(Thickness value) => [value.Left, value.Top, value.Right, value.Bottom];
    private static string? Brush(Brush? value) => value is SolidColorBrush solid ? solid.Color.ToString() : value?.ToString();
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    private static void WritePalette(string output, string mode)
    {
        var resources = Application.Current.Resources;
        var palette = resources.Keys.Cast<object>().OfType<string>()
            .Where(key => resources[key] is SolidColorBrush)
            .ToDictionary(key => key, key => ((SolidColorBrush)resources[key]).Color.ToString());
        WriteJson(Path.Combine(output, mode + "-palette.json"), palette);
    }

    private static void WriteAnimationReference(string output)
    {
        var timing = MainWindow.GetSynchronizedDeviceFilterAnimationTiming();
        // Source excerpts ensure the broader timings/easing remain auditable if
        // they change later. The synchronized timings come from compiled code.
        var appDirectory = Path.Combine(Environment.CurrentDirectory, "src", "LanDeviceMonitor.App");
        var files = new[] { "MainWindow.xaml.cs", "MainWindow.Sidebar.cs" }
            .Select(name => new
            {
                name, lines = File.ReadLines(Path.Combine(appDirectory, name))
                    .Select((line, index) => new { line = index + 1, source = line })
                    .Where(item => item.source.Contains("FromMilliseconds", StringComparison.Ordinal)
                        || item.source.Contains("Ease", StringComparison.Ordinal)
                        || item.source.Contains("TranslateTransform", StringComparison.Ordinal)
                        || item.source.Contains("ScaleTransform", StringComparison.Ordinal))
                    .ToArray()
            }).ToArray();
        WriteJson(Path.Combine(output, "animations.json"), new
        {
            listMilliseconds = timing.ListDurationMilliseconds,
            deviceExitMilliseconds = timing.DetailExitDurationMilliseconds,
            deviceEntranceMilliseconds = timing.DetailEntranceDurationMilliseconds,
            files
        });
    }

    private static void WriteJson(string path, object value) => File.WriteAllText(path, JsonSerializer.Serialize(value, Json));
    private static object? Invoke(MainWindow window, string method, params object?[] arguments) =>
        typeof(MainWindow).GetMethod(method, PrivateInstance)!.Invoke(window, arguments);
    private static void SetField(MainWindow window, string name, object value) =>
        typeof(MainWindow).GetField(name, PrivateInstance)!.SetValue(window, value);
    private static void Pump(int milliseconds)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }
}
