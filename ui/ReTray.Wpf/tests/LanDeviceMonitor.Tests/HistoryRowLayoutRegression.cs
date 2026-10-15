using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using LanDeviceMonitor;
using LanDeviceMonitor.Models;
using LanDeviceMonitor.Services;

internal static class HistoryRowLayoutRegression
{
    internal static int Run()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            App? app = null; MainWindow? window = null;
            try
            {
                app = TestAppFactory.Create(); app.InitializeComponent(); app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                window = new MainWindow(new NotificationService())
                { WindowStartupLocation = WindowStartupLocation.Manual, Left = -10000, Top = -10000, Width = 500, Height = 1000, ShowActivated = false, ShowInTaskbar = false };
                typeof(MainWindow).GetField("_isPopulatingAppSettings", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, true);
                var now = DateTimeOffset.Now;
                var device = new DeviceViewData
                {
                    Profile = new DeviceProfile { MacAddress = "02:00:00:00:68:01", LastRouterName = "历史布局测试", LastKnownOnline = true, FirstSeenAt = now.AddDays(-400), LastSeenAt = now },
                    Identity = new DeviceIdentity("手机", "\uE8EA", "历史布局测试"),
                    Sessions = new[]
                    {
                        new DeviceSession { MacAddress = "02:00:00:00:68:01", OnlineAt = now.AddMinutes(-103) },
                        new DeviceSession { MacAddress = "02:00:00:00:68:01", OnlineAt = now.AddHours(-3), OfflineAt = now.AddHours(-3) },
                        new DeviceSession { MacAddress = "02:00:00:00:68:01", OnlineAt = now.AddYears(-2000).AddDays(-123), OfflineAt = now.AddDays(-3) },
                        new DeviceSession { MacAddress = "02:00:00:00:68:01", OnlineAt = now.AddDays(-500), OfflineAt = now.AddDays(-450) }
                    }.Concat(Enumerable.Range(0, 42).Select(i => new DeviceSession
                    {
                        MacAddress = "02:00:00:00:68:01",
                        OnlineAt = now.AddDays(-600 - i * 130),
                        OfflineAt = now.AddDays(-600 - i * 130).AddHours(i % 2 == 0 ? 2 : 2999)
                    })).ToArray()
                };
                typeof(MainWindow).GetMethod("ReplaceDevices", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [new[] { device }, false, false]);
                window.Show(); Pump();
                var deepChecked = false;
                foreach (var mode in new[] { ThemeService.LightMode, ThemeService.DarkMode })
                foreach (var width in new[] { 500d, 380d, 600d, 1180d, 500d })
                {
                    ThemeService.Apply(mode); window.Width = width; Pump();
                    typeof(MainWindow).GetMethod("ShowDevicesPanel", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [false, false]); Pump(700);
                    window.SessionHistoryTitleText.BringIntoView(); Pump();
                    var badges = Descendants<Border>(window.SessionHistoryItems).Where(b => b.Style == app.FindResource("TimelineDurationBadgeStyle")).ToArray();
                    Require(badges.Length >= 3, "History fixture did not realize three rows.");
                    foreach (var badge in badges.Take(3))
                    {
                        var row = (Panel)VisualTreeHelper.GetParent(badge);
                        VerifyRow(row, badge, $"window={width}");
                        var card = (FrameworkElement)VisualTreeHelper.GetParent(row);
                        Require(badge.TranslatePoint(new Point(0, badge.ActualHeight), card).Y <= card.ActualHeight - 9,
                            "Duration is clipped by an underestimated history row height.");
                    }
                    Require(ToolTipService.GetToolTip(window.OlderHistoryToggle) is null, "History action still has a hover tooltip.");
                    Require(window.OlderHistoryToggle.BorderBrush == app.FindResource("TimelineItemBorderBrush") && window.OlderHistoryToggle.BorderThickness == new Thickness(1), "History action outline differs from its adjacent record card.");
                    for (var i = 1; i < badges.Take(3).Count(); i++)
                    {
                        var previous = (FrameworkElement)VisualTreeHelper.GetParent(VisualTreeHelper.GetParent(badges[i - 1]));
                        var current = (FrameworkElement)VisualTreeHelper.GetParent(VisualTreeHelper.GetParent(badges[i]));
                        var previousBottom = previous.TranslatePoint(new Point(0, previous.ActualHeight), window.SessionHistoryItems).Y;
                        Require(current.TranslatePoint(new Point(), window.SessionHistoryItems).Y >= previousBottom - .5, "Mixed history rows overlap.");
                    }
                    var lastCard = (FrameworkElement)VisualTreeHelper.GetParent(VisualTreeHelper.GetParent(badges[2]));
                    Require(window.SessionHistoryDisclosure.ActualHeight >= lastCard.TranslatePoint(new Point(0, lastCard.ActualHeight), window.SessionHistoryItems).Y - 1,
                        "Collapsed reveal clips its third record.");
                    if (width == 380) VerifyFullDateFixture(app, ((FrameworkElement)VisualTreeHelper.GetParent(badges[0])).ActualWidth);
                    if (width is 380 or 500) Capture(window.SessionHistoryCard, mode, width);
                    if (width == 500 && !deepChecked) { VerifyDeepHistory(window); deepChecked = true; }
                }
                Console.WriteLine("History layout PASS: every duration stays alongside complete dates; light/dark 380/500/600/1180, mixed date lengths, outline and no hover tooltip.");
            }
            catch (Exception exception) { failure = exception; }
            finally { window?.PrepareForExit(); window?.Close(); app?.Shutdown(); }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        if (!thread.Join(TimeSpan.FromSeconds(45))) { Console.Error.WriteLine("History layout regression timed out."); Environment.Exit(1); }
        if (failure is null) return 0; Console.Error.WriteLine(failure); return 1;
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        { var child = VisualTreeHelper.GetChild(root, i); if (child is T match) yield return match; foreach (var item in Descendants<T>(child)) yield return item; }
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void VerifyRow(Panel row, Border badge, string context)
    {
        Require(badge.Padding == new Thickness(17, 0, 17, 0),
            $"Duration capsule lost its original padding: {context}.");
        var details = (FrameworkElement)row.Children[1];
        var badgeBounds = badge.TransformToAncestor(row).TransformBounds(new Rect(badge.RenderSize));
        var detailsBounds = details.TransformToAncestor(row).TransformBounds(new Rect(details.RenderSize));
        Require(badgeBounds.Top < detailsBounds.Bottom && badgeBounds.Bottom > detailsBounds.Top,
            $"Duration moved below its dates: {context}, row={row.ActualWidth}, badge={badgeBounds}, details={detailsBounds}.");
        Require(Math.Abs(badgeBounds.Top + badgeBounds.Height / 2 - detailsBounds.Top - detailsBounds.Height / 2) <= .5,
            $"Duration is not centered beside its dates: {context}.");
        Require(badgeBounds.Left >= detailsBounds.Right - .5 && badgeBounds.Right <= row.ActualWidth + .5,
            $"Duration overlaps dates or extends outside its row: {context}, row={row.ActualWidth}.");
        var textBounds = new List<(TextBlock Text, Rect Bounds)>();
        foreach (var text in Descendants<TextBlock>(row))
        {
            var bounds = text.TransformToAncestor(row).TransformBounds(new Rect(text.RenderSize));
            Require(bounds.Left >= -.5 && bounds.Right <= row.ActualWidth + .5 && bounds.Bottom <= row.ActualHeight + .5,
                $"History text leaves its row: {context}, text={text.Text}, bounds={bounds}, row={row.RenderSize}.");
            Require(text.FontSize >= 12 && text.TextTrimming == TextTrimming.None && text.LayoutTransform.Value.IsIdentity,
                $"History text is too small, truncated or distorted: {context}.");
            // Text uses its measured width and full measured height, including wrapped dates.
            Require(text.DesiredSize.Width <= text.ActualWidth + .5 && text.DesiredSize.Height <= text.ActualHeight + .5,
                $"History text is clipped: {context}, text={text.Text}, desired={text.DesiredSize}, actual={text.RenderSize}.");
            if (text.ActualWidth > 0 && text.ActualHeight > 0) textBounds.Add((text, bounds));
        }
        for (var first = 0; first < textBounds.Count; first++)
        for (var second = first + 1; second < textBounds.Count; second++)
        {
            var intersection = Rect.Intersect(textBounds[first].Bounds, textBounds[second].Bounds);
            Require(intersection.IsEmpty || intersection.Width < .5 || intersection.Height < .5,
                $"History texts overlap: {context}, {textBounds[first].Text.Text} / {textBounds[second].Text.Text}.");
        }
    }
    private static void VerifyFullDateFixture(App app, double minimumRowWidth)
    {
        var details = new LanDeviceMonitor.Controls.SessionHistoryDetails();
        foreach (var dateText in new[] { "9999\u2009年\u200912\u2009月\u200931\u2009日", "0001\u2009年\u200901\u2009月\u200901\u2009日" })
        {
            details.Children.Add(new TextBlock { Text = details.Children.Count == 0 ? "离线时间" : "上线时间" });
            details.Children.Add(new LanDeviceMonitor.Controls.HistoryDateTextBlock { DateText = dateText });
            details.Children.Add(new TextBlock { Text = "23:59" });
        }
        var badge = new Border
        {
            Style = (Style)app.FindResource("TimelineDurationBadgeStyle"),
            Child = new TextBlock { Text = "3652058\u2009天\u200923\u2009小时", FontSize = 14 }
        };
        var row = new LanDeviceMonitor.Controls.SessionHistoryRow();
        row.Children.Add(new Grid { Width = 12 }); row.Children.Add(details); row.Children.Add(badge);
        foreach (var width in new[] { minimumRowWidth, 600, minimumRowWidth })
        {
            row.Measure(new Size(width, double.PositiveInfinity)); row.Arrange(new Rect(0, 0, width, row.DesiredSize.Height));
            VerifyRow(row, badge, $"full dates and maximum legal duration, row={width}");
            if (width == 600)
            {
                var label = (FrameworkElement)details.Children[0]; var date = (FrameworkElement)details.Children[1];
                Require(badge.Padding == new Thickness(17, 0, 17, 0) && details.TranslatePoint(new Point(), row).X == 24
                    && date.TranslatePoint(new Point(), details).X == 72
                    && Math.Abs(date.TranslatePoint(new Point(), details).Y - label.TranslatePoint(new Point(), details).Y) < .5,
                    "Expanding the row did not restore its wide spacing and date layout.");
            }
            foreach (var date in details.Children.OfType<LanDeviceMonitor.Controls.HistoryDateTextBlock>())
            {
                var spaces = date.Inlines.OfType<System.Windows.Documents.Run>().Where(run => run.Text == " ").ToArray();
                Require(spaces.Length == 5 && spaces.All(run => Math.Abs(run.FontSize - date.FontSize / 5) < .001),
                    "Reflow changed the one-fifth date spaces.");
            }
        }
    }
    private static void Capture(FrameworkElement card, string mode, double width)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var bounds = new Rect(0, 0, card.ActualWidth, card.ActualHeight);
            dc.DrawRectangle(Brushes.White, null, bounds); dc.DrawRectangle(new VisualBrush(card), null, bounds);
        }
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(card.ActualWidth * 1.25), (int)Math.Ceiling(card.ActualHeight * 1.25), 120, 120, PixelFormats.Pbgra32);
        bitmap.Render(visual); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        var folder = System.IO.Path.Combine(Environment.CurrentDirectory, "runtime-data", "history-layout-dev274");
        System.IO.Directory.CreateDirectory(folder);
        using var file = System.IO.File.Create(System.IO.Path.Combine(folder, $"history-{width}-{mode}.png")); encoder.Save(file);
    }
    private static void VerifyDeepHistory(MainWindow window)
    {
        while (window.HasOlderSessions) { window.OlderHistoryToggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump(500); }
        var panel = Descendants<LanDeviceMonitor.Controls.SessionHistoryPanel>(window.SessionHistoryItems).Single();
        foreach (var index in new[] { 20, 40 })
        {
            for (var pass = 0; pass < 3; pass++)
            {
                var origin = panel.TranslatePoint(new Point(), window.DetailPanel).Y + window.DetailPanel.VerticalOffset;
                window.DetailPanel.ScrollToVerticalOffset(origin + panel.HeightForCount(index)); Pump();
            }
            Require(window.SessionHistoryItems.ItemContainerGenerator.ContainerFromIndex(index) is not null, "Mixed-height middle record was not realized.");
        }
        foreach (var width in new[] { 380d, 600d, 500d })
        {
            window.Width = width; Pump(700); window.DetailPanel.ScrollToBottom(); Pump();
            Require(window.SessionHistoryItems.ItemContainerGenerator.ContainerFromIndex(window.HistorySessionCount - 1) is not null, "Mixed-height last record was not reachable after resizing.");
            var cards = Descendants<LanDeviceMonitor.Controls.SessionHistoryRow>(window.SessionHistoryItems)
                .Select(row => (FrameworkElement)VisualTreeHelper.GetParent(row)).OrderBy(card => card.TranslatePoint(new Point(), panel).Y).ToArray();
            foreach (var card in cards)
            {
                var row = Descendants<LanDeviceMonitor.Controls.SessionHistoryRow>(card).Single();
                VerifyRow(row, (Border)row.Children[2], $"deep history, window={width}");
            }
            for (var i = 1; i < cards.Length; i++)
                Require(cards[i].TranslatePoint(new Point(), panel).Y >= cards[i - 1].TranslatePoint(new Point(0, cards[i - 1].ActualHeight), panel).Y - .5, "Deep mixed-height rows overlap.");
            Require(window.OlderHistoryToggle.TranslatePoint(new Point(), window.SessionHistoryCard).Y >= window.SessionHistoryDisclosure.TranslatePoint(new Point(0, window.SessionHistoryDisclosure.ActualHeight), window.SessionHistoryCard).Y,
                "History action overlaps the last revealed record.");
        }
        window.OlderHistoryToggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump(500);
        Require(window.VisibleHistoryCount == 3, "Deep history did not collapse back to three records.");
        window.SessionHistoryTitleText.BringIntoView(); Pump();
        Console.WriteLine("Mixed-height history PASS: 46 records, incremental reveal, middle/end virtualization, resize and collapse.");
    }
    private static void Pump(int milliseconds = 0)
    {
        var frame = new DispatcherFrame();
        if (milliseconds == 0) Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
        else
        {
            var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(milliseconds) };
            timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; }; timer.Start();
        }
        Dispatcher.PushFrame(frame);
    }
}
