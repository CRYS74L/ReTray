using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace TrayAssistant_App.Controls;

public static class WindowAppearance
{
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint window, int attribute, ref int value, int size);
    [DllImport("user32.dll")] private static extern bool PostMessage(nint window, uint message, nint wparam, nint lparam);
    public static void RequestClose(Window window) => PostMessage(WinRT.Interop.WindowNative.GetWindowHandle(window), 0x0010, 0, 0);
    public static ElementTheme PreferredTheme()
    {
        try
        {
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TrayIconAssistant", "settings.json");
            using var settings = JsonDocument.Parse(File.ReadAllText(path));
            return settings.RootElement.GetProperty("theme").GetString() switch
            {
                "dark" => ElementTheme.Dark,
                "light" => ElementTheme.Light,
                _ => ElementTheme.Default
            };
        }
        catch { return ElementTheme.Default; }
    }
    public static ElementTheme ResolveTheme(ElementTheme theme) => theme != ElementTheme.Default
        ? theme : ((FrameworkElement)App.Window.Content).ActualTheme;
    public static void ApplyNativeTheme(Window window, ElementTheme theme)
    {
        int dark = theme == ElementTheme.Dark ? 1 : 0;
        DwmSetWindowAttribute(WinRT.Interop.WindowNative.GetWindowHandle(window), 20, ref dark, sizeof(int));
    }
    public static Grid CreateTitleBar(Window window, string title, bool main = false)
    {
        var presenter = (OverlappedPresenter)window.AppWindow.Presenter;
        presenter.SetBorderAndTitleBar(true, false);
        var bar = new Grid { Height = 32 };
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var drag = new Grid { Padding = new Thickness(14, 0, 8, 0), ColumnSpacing = 10 };
        drag.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        drag.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        drag.Children.Add(new Image { Width = 16, Height = 16, Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri("ms-appx:///Assets/AppIcon.ico")), VerticalAlignment = VerticalAlignment.Center });
        var label = new TextBlock { Text = title, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(label, 1); drag.Children.Add(label); bar.Children.Add(drag);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        Grid.SetColumn(buttons, 1); bar.Children.Add(buttons);
        Button Caption(string id, string name, string glyph, Action clicked)
        {
            var button = new Button { Style = (Style)Application.Current.Resources["CaptionButtonStyle"], Content = new FontIcon { Glyph = glyph, FontSize = 10 } };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(button, id);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, name);
            ToolTipService.SetToolTip(button, name);
            button.Click += (_, _) => clicked(); buttons.Children.Add(button); return button;
        }
        if (main)
        {
            Caption("CaptionMinimize", "最小化", "\uE921", () => presenter.Minimize());
            var maximize = Caption("CaptionMaximize", "最大化", "\uE922", () =>
            {
                if (presenter.State == OverlappedPresenterState.Maximized) presenter.Restore();
                else presenter.Maximize();
            });
            window.AppWindow.Changed += (_, _) => ((FontIcon)maximize.Content).Glyph = presenter.State == OverlappedPresenterState.Maximized ? "\uE923" : "\uE922";
        }
        Caption("CaptionClose", "关闭", "\uE8BB", () => RequestClose(window));
        bar.Loaded += (_, _) => window.SetTitleBar(drag);
        window.ExtendsContentIntoTitleBar = true;
        return bar;
    }
}
