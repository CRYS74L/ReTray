using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace LanDeviceMonitor.Controls;

public sealed class HistoryDateTextBlock : TextBlock
{
    public static readonly DependencyProperty DateTextProperty = DependencyProperty.Register(
        nameof(DateText), typeof(string), typeof(HistoryDateTextBlock),
        new PropertyMetadata(string.Empty, (owner, _) => ((HistoryDateTextBlock)owner).UpdateInlines()));

    public string DateText
    {
        get => (string)GetValue(DateTextProperty);
        set => SetValue(DateTextProperty, value);
    }

    public static readonly DependencyProperty RangeSeparatorSpaceRatioProperty = DependencyProperty.Register(
        nameof(RangeSeparatorSpaceRatio), typeof(double), typeof(HistoryDateTextBlock),
        new PropertyMetadata(1d, (owner, _) => ((HistoryDateTextBlock)owner).UpdateInlines()),
        value => value is double ratio && double.IsFinite(ratio) && ratio > 0 && ratio <= 1);

    public double RangeSeparatorSpaceRatio
    {
        get => (double)GetValue(RangeSeparatorSpaceRatioProperty);
        set => SetValue(RangeSeparatorSpaceRatioProperty, value);
    }

    static HistoryDateTextBlock()
    {
        FontSizeProperty.OverrideMetadata(typeof(HistoryDateTextBlock),
            new FrameworkPropertyMetadata(FontSizeProperty.GetMetadata(typeof(TextBlock)).DefaultValue,
                (owner, _) => ((HistoryDateTextBlock)owner).UpdateInlines()));
    }

    private void UpdateInlines()
    {
        Inlines.Clear();
        var parts = (DateText ?? string.Empty).Split('\u2009');
        for (var index = 0; index < parts.Length; index++)
        {
            if (index > 0)
                // Scale only the blank glyph, whose advance is proportional
                // to font size. Date glyphs retain the normal face and size.
                Inlines.Add(new Run(" ") { FontSize = FontSize / 5 });
            if (RangeSeparatorSpaceRatio == 1)
            {
                Inlines.Add(new Run(parts[index]));
                continue;
            }
            var rangeParts = parts[index].Split(" ~ ", StringSplitOptions.None);
            for (var rangeIndex = 0; rangeIndex < rangeParts.Length; rangeIndex++)
            {
                if (rangeIndex > 0)
                {
                    Inlines.Add(new Run(" ") { FontSize = FontSize * RangeSeparatorSpaceRatio });
                    Inlines.Add(new Run("~"));
                    Inlines.Add(new Run(" ") { FontSize = FontSize * RangeSeparatorSpaceRatio });
                }
                Inlines.Add(new Run(rangeParts[rangeIndex]));
            }
        }
    }
}
