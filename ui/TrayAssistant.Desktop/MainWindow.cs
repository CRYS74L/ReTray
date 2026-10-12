using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using TrayAssistant_App.Controls;
using TrayAssistant_App.Services;

namespace TrayAssistant_App;

public sealed class MainWindow : MaterialWindow
{
    public MainPage Page { get; } = new();
    public MainWindow()
    {
        Title = "托盘图标助手";
        var shell = new Grid();
        shell.SetResourceReference(Panel.BackgroundProperty, "WindowTintBrush");
        shell.RowDefinitions.Add(new RowDefinition { Height = new GridLength(32) });
        shell.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        shell.Children.Add(DesktopCaption.Create(this, Title, true));
        Grid.SetRow(Page, 1); shell.Children.Add(Page); Content = shell;
        ApplyTheme(ThemeManager.IsDark);
        Icon = new BitmapImage(new Uri(System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico")));
        ThemeManager.Changed += ThemeChanged;
        Closing += async (_, args) =>
        {
            if (App.CanCloseInterface) return;
            args.Cancel = true;
            await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Normal);
            await App.QuitAsync();
        };
        Closed += (_, _) => ThemeManager.Changed -= ThemeChanged;
    }
    private void ThemeChanged() => ApplyTheme(ThemeManager.IsDark);
}

internal static class DesktopCaption
{
    public static Grid Create(Window window, string title, bool main)
    {
        var row = new Grid { Height = 32 };
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var drag = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(14, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        drag.Children.Add(new Image { Width = 16, Height = 16, Source = new BitmapImage(new Uri(System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"))), Margin = new Thickness(0, 0, 10, 0) });
        drag.Children.Add(new TextBlock { Text = title, FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
        row.Children.Add(drag);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        Grid.SetColumn(buttons, 1); row.Children.Add(buttons);
        void Add(string id, string name, string glyph, Action action)
        {
            var button = new Button { Style = (Style)Application.Current.Resources["CaptionButton"], Content = new TextBlock { Text = glyph, FontFamily = new System.Windows.Media.FontFamily("Segoe Fluent Icons"), FontSize = 10 } };
            System.Windows.Automation.AutomationProperties.SetAutomationId(button, id);
            System.Windows.Automation.AutomationProperties.SetName(button, name);
            System.Windows.Shell.WindowChrome.SetIsHitTestVisibleInChrome(button, true);
            button.Click += (_, _) => action(); buttons.Children.Add(button);
        }
        if (main) Add("CaptionMinimize", "最小化", "\uE921", () => ((MaterialWindow)window).RequestMinimizeWithTransitionMaterial());
        Add("CaptionClose", "关闭", "\uE8BB", window.Close);
        // WindowChrome provides the native caption drag and system move loop.
        return row;
    }
}
