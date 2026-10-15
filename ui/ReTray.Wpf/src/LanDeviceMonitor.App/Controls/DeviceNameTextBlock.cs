using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace LanDeviceMonitor.Controls;

/// <summary>Display mixed device names with one-fifth-space boundaries, leaving stored names intact.</summary>
public sealed class DeviceNameTextBlock : TextBlock
{
    public static readonly DependencyProperty NameTextProperty = DependencyProperty.Register(
        nameof(NameText), typeof(string), typeof(DeviceNameTextBlock),
        new PropertyMetadata(string.Empty, (owner, _) => ((DeviceNameTextBlock)owner).Refresh()));
    public string NameText { get => (string)GetValue(NameTextProperty); set => SetValue(NameTextProperty, value); }
    static DeviceNameTextBlock() => FontSizeProperty.OverrideMetadata(typeof(DeviceNameTextBlock),
        new FrameworkPropertyMetadata(FontSizeProperty.GetMetadata(typeof(TextBlock)).DefaultValue,
            (owner, _) => ((DeviceNameTextBlock)owner).Refresh()));
    private void Refresh()
    {
        Inlines.Clear();
        foreach (var part in DeviceNameSpacing.Parts(NameText ?? string.Empty))
            Inlines.Add(part.Gap ? new Run(" ") { FontSize = FontSize / 5 } : new Run(part.Text));
        System.Windows.Automation.AutomationProperties.SetName(this, NameText ?? string.Empty);
    }
}

public static class DeviceNameSpacing
{
    public static IReadOnlyList<(string Text, bool Gap)> Parts(string name)
    {
        var elements = new List<string>();
        var iterator = StringInfo.GetTextElementEnumerator(name);
        while (iterator.MoveNext()) elements.Add(iterator.GetTextElement());
        var result = new List<(string Text, bool Gap)>();
        var content = new StringBuilder();
        for (var index = 0; index < elements.Count; index++)
        {
            content.Append(elements[index]);
            var next = index + 1;
            while (next < elements.Count && string.IsNullOrWhiteSpace(elements[next])) next++;
            if (next < elements.Count && Boundary(Kind(elements[index]), Kind(elements[next])))
            {
                result.Add((content.ToString(), false)); result.Add((" ", true)); content.Clear();
                index = next - 1;
            }
        }
        if (content.Length > 0) result.Add((content.ToString(), false));
        return result;
    }
    private static bool Boundary(int a, int b) => (a == 1 && b is 2 or 3 or 4) || (b == 1 && a is 2 or 3 or 4)
        || (a == 2 && b == 4) || (a == 4 && b == 2);
    private static int Kind(string element)
    {
        var rune = element.EnumerateRunes().First(); var value = rune.Value;
        if (value is 0x3007 or >= 0x3400 and <= 0x4dbf or >= 0x4e00 and <= 0x9fff or >= 0xf900 and <= 0xfaff or >= 0x20000 and <= 0x323af) return 1;
        if ((value is >= 65 and <= 90 or >= 97 and <= 122 or >= 0xc0 and <= 0x24f) && Rune.IsLetter(rune)) return 2;
        var category = Rune.GetUnicodeCategory(rune);
        if (category == UnicodeCategory.DecimalDigitNumber) return 3;
        return category is >= UnicodeCategory.ConnectorPunctuation and <= UnicodeCategory.OtherSymbol ? 4 : 0;
    }
}
