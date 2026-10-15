using System.Windows;
using System.Windows.Documents;
using System.Windows.Controls;
using System.Windows.Media;
using System.Globalization;
using LanDeviceMonitor;
using LanDeviceMonitor.Controls;

internal static class DeviceNameSpacingRegression
{
    internal static int Run()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            App? app = null;
            try
            {
                app = TestAppFactory.Create(); app.InitializeComponent();
                foreach (var (name, gaps) in new[] { ("测试设备3", 1), ("华为Mate30", 1), ("中文+English", 2),
                    ("Redmi Note 11T Pro+", 1), ("华为 Mate 30", 1), ("iPhone 13 Pro Max", 0), ("台灯", 0), ("é+灯", 2), ("A×B2", 2), ("设备<abc>", 3), ("设备🙂English", 2) })
                {
                    var display = new DeviceNameTextBlock { NameText = name, Style = (Style)app.FindResource(typeof(TextBlock)) };
                    foreach (var size in new[] { 14d, 26d, 16d })
                    {
                        display.FontSize = size;
                        display.FontWeight = size == 26 ? FontWeights.SemiBold : FontWeights.Normal;
                        var runs = display.Inlines.OfType<Run>().ToArray();
                        if (runs.Count(r => r.Text == " " && Math.Abs(r.FontSize - size / 5) < .001) != gaps)
                            throw new Exception($"Wrong mixed-name gap: {name}, size={size}");
                        if (display.NameText != name || string.Concat(runs.Select(r => r.Text)).Replace(" ", "") != name.Replace(" ", ""))
                            throw new Exception("Stored name or glyphs changed.");
                        if (runs.Any(r => r.Text != " " && r.FontSize != size)) throw new Exception("Device-name glyph size changed.");
                        display.Measure(new Size(600, double.PositiveInfinity));
                        if (display.DesiredSize.Width <= 0) throw new Exception("Mixed device name did not render.");
                        if (name == "测试设备3")
                        {
                            var plain = new TextBlock { Text = name, FontSize = size, FontFamily = display.FontFamily, FontWeight = display.FontWeight };
                            plain.Measure(new Size(600, double.PositiveInfinity));
                            var space = new FormattedText(" ", CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                                new Typeface(display.FontFamily, display.FontStyle, display.FontWeight, display.FontStretch), size, Brushes.Black, 1.25).WidthIncludingTrailingWhitespace;
                            var gap = display.DesiredSize.Width - plain.DesiredSize.Width;
                            if (Math.Abs(gap - space / 5) > .03) throw new Exception($"Rendered gap mismatch: {gap} versus {space / 5}");
                            Console.WriteLine($"Name gap measurement: font={display.FontFamily.Source}, size={size}, ordinarySpace={space:F4}, renderedGap={gap:F4}");
                        }
                    }
                }
            }
            catch (Exception exception) { failure = exception; }
            finally { app?.Shutdown(); }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure is not null) { Console.Error.WriteLine(failure); return 1; }
        Console.WriteLine("Device-name spacing PASS: mixed-script/symbol/number boundaries, exact one-fifth spaces, preserved English spaces and stored names, font-size changes.");
        return 0;
    }
}
