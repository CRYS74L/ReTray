using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

// Opt-in visual comparison against the same machine before embedding. Outputs stay in .tools.
internal static class FontVisualProof
{
    private static readonly Dictionary<string, int> Captures = new();
    private sealed record Sample(string Text, string Family, string Face, double Size,
        string Weight, double Width, double Height, string Pixels);

    public static void Capture(FrameworkElement root, string scenario)
    {
        var mode = Environment.GetEnvironmentVariable("LAN_FONT_PROOF");
        if (mode is not ("baseline" or "compare")) return;
        var sequence = Captures.GetValueOrDefault(scenario) + 1;
        Captures[scenario] = sequence;
        scenario += $"-{sequence}";
        var samples = new Dictionary<string, Sample>();
        var folder = Path.Combine(Environment.CurrentDirectory, ".tools", "font-proof", mode, scenario);
        Directory.CreateDirectory(folder);
        var index = 0;
        foreach (var text in Texts(root))
        {
            var key = $"{index++:D4}-{text.Name}";
            if (!text.IsVisible || text.ActualWidth <= 0 || text.ActualHeight <= 0 ||
                string.IsNullOrEmpty(text.Text) || IsIntentionalChange(text)) continue;
            var face = new Typeface(text.FontFamily, text.FontStyle, text.FontWeight, text.FontStretch);
            if (!face.TryGetGlyphTypeface(out var glyph))
                throw new Exception($"字体没有解析到真实字形文件: {scenario}/{key} {text.FontFamily}");
            foreach (var scale in new[] { 1d, 1.25, 1.5, 2 })
            {
                var visual = new DrawingVisual();
                using (var dc = visual.RenderOpen())
                {
                    dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, text.ActualWidth + 4, text.ActualHeight + 4));
                    dc.DrawRectangle(new VisualBrush(text) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left,
                        AlignmentY = AlignmentY.Top }, null, new Rect(2, 2, text.ActualWidth, text.ActualHeight));
                }
                var bitmap = new RenderTargetBitmap((int)Math.Ceiling((text.ActualWidth + 4) * scale),
                    (int)Math.Ceiling((text.ActualHeight + 4) * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
                bitmap.Render(visual);
                var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
                bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
                var sampleKey = $"{key}-{scale}";
                samples[sampleKey] = new(text.Text, text.FontFamily.Source, glyph.FontUri.ToString(), text.FontSize,
                    text.FontWeight.ToString(), text.ActualWidth, text.ActualHeight, Convert.ToHexString(SHA256.HashData(pixels)));
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var file = File.Create(Path.Combine(folder, sampleKey + ".png")); encoder.Save(file);
            }
        }
        File.WriteAllText(Path.Combine(folder, "manifest.json"), JsonSerializer.Serialize(samples, new JsonSerializerOptions { WriteIndented = true }));
        if (mode == "baseline")
        {
            Console.WriteLine($"FONT BASELINE {scenario}: {samples.Count} samples; {string.Join(", ", samples.Values.Select(s => s.Family).Distinct())}");
            return;
        }
        var before = JsonSerializer.Deserialize<Dictionary<string, Sample>>(File.ReadAllText(
            Path.Combine(Environment.CurrentDirectory, ".tools", "font-proof", "baseline", scenario, "manifest.json")))!;
        // These two symbol glyphs were separately requested to move to the button's ink center.
        foreach (var key in before.Where(p => p.Value.Text is "\uE8CE" or "\uE713").Select(p => p.Key).ToArray()) before.Remove(key);
        foreach (var key in samples.Where(p => p.Value.Text is "\uE8CE" or "\uE713").Select(p => p.Key).ToArray()) samples.Remove(key);
        var errors = new List<string>();
        foreach (var (key, old) in before)
        {
            if (!samples.TryGetValue(key, out var current)) { errors.Add($"missing {key}"); continue; }
            if (old.Text != current.Text || old.Size != current.Size || old.Weight != current.Weight ||
                old.Width != current.Width || old.Height != current.Height || old.Pixels != current.Pixels)
                errors.Add($"{key}: {old.Text} | {old.Face} -> {current.Face}; pixelsEqual={old.Pixels == current.Pixels}; size={old.Width}x{old.Height}->{current.Width}x{current.Height}");
            if (!current.Face.StartsWith("pack:", StringComparison.OrdinalIgnoreCase))
                errors.Add($"字体仍依赖系统: {key} {current.Face}");
        }
        if (samples.Count != before.Count) errors.Add($"sample count {before.Count}->{samples.Count}");
        if (errors.Count != 0) throw new Exception($"FONT COMPARISON FAILED {scenario}:\n" + string.Join("\n", errors.Take(15)));
        Console.WriteLine($"FONT PIXEL PASS {scenario}: {samples.Count} unchanged samples at 100/125/150/200%, all embedded.");
    }

    private static bool IsIntentionalChange(TextBlock text)
    {
        if (text.Name == "ShellAppName") return true;
        for (DependencyObject? parent = text; parent is not null; parent = VisualTreeHelper.GetParent(parent))
            if (parent is TextBox or PasswordBox or ComboBox or ComboBoxItem) return true;
        return false;
    }

    private static IEnumerable<TextBlock> Texts(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is TextBlock text) yield return text;
            foreach (var descendant in Texts(child)) yield return descendant;
        }
    }
}
