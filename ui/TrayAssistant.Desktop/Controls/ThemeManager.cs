using System;
using System.ComponentModel;
using System.IO;
using System.Security;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

namespace TrayAssistant_App.Controls;

/// <summary>Owns neutral UI colors; native Persistent Acrylic supplies the backdrop.</summary>
public static class ThemeManager
{
    private static int _selection;
    private static bool _watching;

    public static bool IsDark { get; private set; }
    public static int Selection => _selection;
    public static event Action? Changed;

    /// <param name="selection">0 follows Windows, 1 is light, and 2 is dark.</param>
    public static void Apply(int selection)
    {
        var app = Application.Current;
        if (app is not null && !app.Dispatcher.CheckAccess())
        {
            app.Dispatcher.Invoke(() => Apply(selection));
            return;
        }

        _selection = Math.Clamp(selection, 0, 2);
        IsDark = _selection == 2 || (_selection == 0 && ReadWindowsDarkTheme());
        if (app is null) return;

        var colors = IsDark
            ? new[]
            {
                ("WindowTintBrush", "#00000000"),
                ("TextBrush", "#FFF1F1F1"),
                ("SecondaryTextBrush", "#FFB6B6B6"),
                ("DividerBrush", "#22FFFFFF"),
                ("CardBrush", "#08FFFFFF"),
                ("CardBorderBrush", "#18FFFFFF"),
                ("ControlBrush", "#19FFFFFF"),
                ("ControlHoverBrush", "#25FFFFFF"),
                ("ControlPressedBrush", "#31FFFFFF"),
                ("ControlBorderBrush", "#42FFFFFF"),
                ("AccentBrush", "#FF42CDE5"),
                ("AccentForegroundBrush", "#FF183B41"),
                ("ScrollThumbBrush", "#48FFFFFF"),
                ("ScrollHoverThumbBrush", "#78FFFFFF"),
                ("CaptionHoverBrush", "#28FFFFFF"),
                ("CaptionPressedBrush", "#40FFFFFF"),
                ("ToggleOffBrush", "#28FFFFFF"),
                ("ToggleKnobBrush", "#FFD2D2D2"),
                ("PopupSurfaceBrush", "#00000000")
            }
            : new[]
            {
                ("WindowTintBrush", "#00000000"),
                ("TextBrush", "#FF202020"),
                ("SecondaryTextBrush", "#FF646464"),
                ("DividerBrush", "#1F000000"),
                ("CardBrush", "#16FFFFFF"),
                ("CardBorderBrush", "#16000000"),
                ("ControlBrush", "#50FFFFFF"),
                ("ControlHoverBrush", "#10000000"),
                ("ControlPressedBrush", "#18000000"),
                ("ControlBorderBrush", "#35000000"),
                ("AccentBrush", "#FF009BB5"),
                ("AccentForegroundBrush", "#FFFFFFFF"),
                ("ScrollThumbBrush", "#43000000"),
                ("ScrollHoverThumbBrush", "#6B000000"),
                ("CaptionHoverBrush", "#24000000"),
                ("CaptionPressedBrush", "#35000000"),
                ("ToggleOffBrush", "#26000000"),
                ("ToggleKnobBrush", "#FFF9F9F9"),
                ("PopupSurfaceBrush", "#00000000")
            };

        // Resource replacement and the following event share one dispatcher turn.
        // DynamicResource consumers and any open popup receive the selected theme.
        foreach (var (key, color) in colors)
            app.Resources[key] = FrozenBrush(ParseColor(color));

        if (SystemParameters.HighContrast)
            ApplyHighContrast(app.Resources);

        StartWatching(app);
        Changed?.Invoke();
    }

    private static bool ReadWindowsDarkTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
        }
        catch (Exception error) when (error is IOException or SecurityException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static void StartWatching(Application app)
    {
        if (_watching) return;
        _watching = true;
        SystemEvents.UserPreferenceChanged += WindowsPreferenceChanged;
        SystemParameters.StaticPropertyChanged += SystemParameterChanged;
        app.Exit += ApplicationExit;
    }

    private static void WindowsPreferenceChanged(object sender, UserPreferenceChangedEventArgs args)
    {
        if (_selection == 0) QueueApply();
    }

    private static void SystemParameterChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(SystemParameters.HighContrast)) QueueApply();
    }

    private static void QueueApply()
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished) return;
        dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() => Apply(_selection)));
    }

    private static void ApplicationExit(object sender, ExitEventArgs args)
    {
        SystemEvents.UserPreferenceChanged -= WindowsPreferenceChanged;
        SystemParameters.StaticPropertyChanged -= SystemParameterChanged;
        if (sender is Application app) app.Exit -= ApplicationExit;
        _watching = false;
    }

    private static void ApplyHighContrast(ResourceDictionary resources)
    {
        var window = SystemColors.WindowColor;
        var text = SystemColors.WindowTextColor;
        resources["WindowTintBrush"] = FrozenBrush(Colors.Transparent);
        resources["PopupSurfaceBrush"] = FrozenBrush(Colors.Transparent);
        foreach (var key in new[] { "TextBrush", "SecondaryTextBrush", "DividerBrush", "CardBorderBrush", "ControlBorderBrush", "ScrollThumbBrush", "ScrollHoverThumbBrush" })
            resources[key] = FrozenBrush(text);
        foreach (var key in new[] { "CardBrush", "ControlBrush", "ToggleOffBrush" })
            resources[key] = FrozenBrush(window);
        foreach (var key in new[] { "ControlHoverBrush", "ControlPressedBrush", "CaptionHoverBrush", "CaptionPressedBrush", "AccentBrush" })
            resources[key] = FrozenBrush(SystemColors.HighlightColor);
        resources["AccentForegroundBrush"] = FrozenBrush(SystemColors.HighlightTextColor);
        resources["ToggleKnobBrush"] = FrozenBrush(text);
    }

    private static Color ParseColor(string value) => (Color)ColorConverter.ConvertFromString(value)!;

    private static SolidColorBrush FrozenBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

}
