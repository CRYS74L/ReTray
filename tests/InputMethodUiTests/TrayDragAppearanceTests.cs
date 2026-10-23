using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml.Linq;
using ReTray_App;

internal static class TrayDragAppearanceTests
{
    public static void Run()
    {
        var app = Application.Current ?? new Application();
        LanDeviceMonitor.Services.ThemeService.Apply("Light");
        var x = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");
        var source = XDocument.Load(System.IO.Path.Combine(AppContext.BaseDirectory, "WindowTemplates.xaml"));
        var dictionary = new XElement(source.Root!.Name.Namespace + "ResourceDictionary",
            source.Root.Attributes().Where(a => a.IsNamespaceDeclaration).Select(a => new XAttribute(a.Name,
                a.Value.StartsWith("clr-namespace:") && !a.Value.Contains(";assembly=") ? a.Value + ";assembly=ReTray" : a.Value)),
            source.Descendants().Where(e => e.Name.LocalName == "Style" &&
                new[] { "DeviceListScrollViewerStyle", "DeviceListBoxStyle", "FooterCircleButtonStyle" }.Contains((string?)e.Attribute(x + "Key"))));
        dictionary.Elements().Single(e => (string?)e.Attribute(x + "Key") == "DeviceListBoxStyle")
            .Elements().Where(e => (string?)e.Attribute("Property") is "ItemContainerStyle" or "ItemTemplate").ToList().ForEach(e => e.Remove());
        foreach (var element in dictionary.Descendants())
        {
            if (element.Name.NamespaceName.StartsWith("clr-namespace:") && !element.Name.NamespaceName.Contains(";assembly="))
                element.Name = XNamespace.Get(element.Name.NamespaceName + ";assembly=ReTray") + element.Name.LocalName;
            foreach (var attribute in element.Attributes().Where(a => a.Name.NamespaceName.StartsWith("clr-namespace:") && !a.Name.NamespaceName.Contains(";assembly=")).ToArray())
            {
                element.SetAttributeValue(XNamespace.Get(attribute.Name.NamespaceName + ";assembly=ReTray") + attribute.Name.LocalName, attribute.Value);
                attribute.Remove();
            }
        }
        app.Resources.MergedDictionaries.Add((ResourceDictionary)System.Windows.Markup.XamlReader.Parse(dictionary.ToString()));
        foreach (var theme in new[] { "Light", "Dark" })
        foreach (var scale in new[] { 1d, 1.25, 1.5, 2d })
        foreach (var width in new[] { 280d, 364d, 500d })
        {
            LanDeviceMonitor.Services.ThemeService.Apply(theme);
            var page = new MainPage();
            page.Vm.IsLoading = false;
            var list = (ListBox)page.FindName("IconList");
            // Exercise the shipped row, divider, container and scroll templates
            // using synthetic names without loading the native tray runtime.
            list.ItemsSource = Enumerable.Range(1, 30).Select(i => new { Name = $"Application {i:00}", Visible = true, CanToggle = true }).ToArray();
            list.Visibility = Visibility.Visible;
            ((FrameworkElement)page.FindName("LoadingList")).Visibility = Visibility.Collapsed;
            VisualTreeHelper.SetRootDpi(page, new DpiScale(scale, scale));
            void Layout() { page.Measure(new Size(width, 400)); page.Arrange(new Rect(0, 0, width, 400)); page.UpdateLayout(); }
            Layout();
            var row = (ListBoxItem)list.ItemContainerGenerator.ContainerFromIndex(3);
            var line = Find<PixelAlignedDivider>(row).Single();
            var surface = (Border)row.Template.FindName("DragSurface", row);
            Require(line.Visibility == Visibility.Visible && surface.Visibility == Visibility.Collapsed, "Resting row appearance changed");
            TrayRowAppearance.SetIsLifted(row, true); Layout();
            Require(line.Visibility == Visibility.Hidden, "The lifted row still carries its separator");
            Require(surface.Visibility == Visibility.Visible && Math.Abs(surface.CornerRadius.TopLeft * 2 - surface.ActualHeight) < .01,
                "The lifted row is not a capsule");
            var bar = Find<ScrollBar>(list).Single(b => b.Orientation == Orientation.Vertical);
            var bounds = new Rect(surface.TranslatePoint(new Point(), page), surface.RenderSize);
            var barBounds = new Rect(bar.TranslatePoint(new Point(), page), bar.RenderSize);
            var toggle = Find<LanDeviceMonitor.Controls.SwitchControl>(row).Single();
            var track = (Border)toggle.Template.FindName("Track", toggle);
            var trackBounds = track.TransformToAncestor(page).TransformBounds(new Rect(track.RenderSize));
            var rightGap = bounds.Right - trackBounds.Right;
            var bottomGap = bounds.Bottom - trackBounds.Bottom;
            var topGap = trackBounds.Top - bounds.Top;
            Require(Math.Abs(rightGap - bottomGap) < .01 && Math.Abs(topGap - bottomGap) < .01,
                $"Switch spacing is asymmetric: right={rightGap}, bottom={bottomGap}, top={topGap}");
            var content = (ContentPresenter)row.Template.FindName("RowContent", row);
            Require(content.Margin.Left == content.Margin.Right && content.Margin.Left > 0,
                "The content inset was not applied symmetrically");
            Require(bounds.Left >= 0 && bounds.Right <= page.ActualWidth && bounds.Right < barBounds.Left,
                "The capsule overlaps the scrollbar or leaves the interface");
            Require(barBounds.Left - bounds.Right >= 1, "The capsule touches the scrollbar");
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(surface.ActualWidth * scale), (int)Math.Ceiling(surface.ActualHeight * scale),
                96 * scale, 96 * scale, PixelFormats.Pbgra32);
            bitmap.Render(surface);
            var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4]; bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
            Require(pixels[3] == 0 && pixels[(bitmap.PixelWidth / 2) * 4 + 3] > 0,
                "Rendered capsule corners are square or its top is missing");
            if (scale == 1.25 && width == 364)
            {
                var preview = new RenderTargetBitmap(455, 500, 120, 120, PixelFormats.Pbgra32);
                var background = new DrawingVisual();
                using (var draw = background.RenderOpen()) draw.DrawRectangle(theme == "Light" ? Brushes.WhiteSmoke : new SolidColorBrush(Color.FromRgb(36, 36, 36)), null, new Rect(0, 0, width, 400));
                preview.Render(background); preview.Render(page);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(preview));
                using var file = System.IO.File.Create(System.IO.Path.Combine(AppContext.BaseDirectory, $"tray-drag-{theme.ToLowerInvariant()}.png")); encoder.Save(file);
            }
            TrayRowAppearance.SetIsLifted(row, false); Layout();
            Require(line.Visibility == Visibility.Visible && surface.Visibility == Visibility.Collapsed, "Drop did not restore the separator and surface");
            Console.WriteLine($"PASS drag capsule {theme}, {scale * 100}% DPI, width {width}: radius={surface.CornerRadius.TopLeft}, scrollbar gap={barBounds.Left - bounds.Right:F2}, switch gap={rightGap:F2}/{bottomGap:F2}");
        }
    }

    private static IEnumerable<T> Find<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T found) yield return found;
            foreach (var nested in Find<T>(child)) yield return nested;
        }
    }
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
}
