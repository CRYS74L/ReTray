using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System.Runtime.InteropServices;
using Windows.Graphics;

namespace TrayAssistant_App.Controls;

public static class MicaDialog
{
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(nint window);
    [DllImport("user32.dll")] private static extern bool EnableWindow(nint window, bool enabled);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern nint SetWindowLongPtr(nint window, int index, nint value);

    // A separate native surface keeps the material independent of the page behind the dialog.
    public static async Task<bool> ShowAsync(string title, UIElement content, ElementTheme theme,
        string primaryText, string closeText, Func<Task<bool>>? apply = null, int height = 640)
    {
        var owner = App.Window;
        var ownerHandle = WinRT.Interop.WindowNative.GetWindowHandle(owner);
        var window = new Window { Title = title, SystemBackdrop = new SoftMicaBackdrop() };
        var handle = WinRT.Interop.WindowNative.GetWindowHandle(window);
        SetWindowLongPtr(handle, -8, ownerHandle);
        window.AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"));
        var presenter = (OverlappedPresenter)window.AppWindow.Presenter;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        var panel = new Grid { RequestedTheme = theme, Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent), Padding = new Thickness(20), RowSpacing = 16 };
        panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.Children.Add(content);
        var actions = new Grid { ColumnSpacing = 10 };
        actions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        actions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var primary = new Button { Content = primaryText, HorizontalAlignment = HorizontalAlignment.Stretch };
        var close = new Button { Content = closeText, HorizontalAlignment = HorizontalAlignment.Stretch };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(primary, "DialogApply");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(close, "DialogClose");
        actions.Children.Add(primary);
        Grid.SetColumn(close, 1); actions.Children.Add(close);
        Grid.SetRow(actions, 1); panel.Children.Add(actions);
        window.Content = panel;
        var completion = new TaskCompletionSource<bool>();
        var accepted = false;
        var ownerAlive = true;
        close.Click += (_, _) => window.Close();
        primary.Click += async (_, _) =>
        {
            primary.IsEnabled = false;
            try { if (apply == null || await apply()) { accepted = true; window.Close(); } }
            finally { primary.IsEnabled = true; }
        };
        panel.KeyDown += (_, e) => { if (e.Key == global::Windows.System.VirtualKey.Escape) { e.Handled = true; window.Close(); } };
        void OwnerClosed(object sender, WindowEventArgs args) { ownerAlive = false; window.Close(); }
        owner.Closed += OwnerClosed;
        window.Closed += (_, _) =>
        {
            owner.Closed -= OwnerClosed;
            panel.Children.Remove(content);
            if (ownerAlive) { EnableWindow(ownerHandle, true); owner.Activate(); }
            completion.TrySetResult(accepted);
        };
        var scale = GetDpiForWindow(ownerHandle) / 96.0;
        var area = DisplayArea.GetFromWindowId(owner.AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        var widthPixels = Math.Min((int)(380 * scale), area.Width);
        var heightPixels = Math.Min((int)(height * scale), area.Height);
        window.AppWindow.Resize(new SizeInt32(widthPixels, heightPixels));
        var x = Math.Clamp(owner.AppWindow.Position.X + (owner.AppWindow.Size.Width - widthPixels) / 2, area.X, area.X + area.Width - widthPixels);
        var y = Math.Clamp(owner.AppWindow.Position.Y + (owner.AppWindow.Size.Height - heightPixels) / 2, area.Y, area.Y + area.Height - heightPixels);
        window.AppWindow.Move(new PointInt32(x, y));
        EnableWindow(ownerHandle, false);
        window.Activate();
        return await completion.Task;
    }
}
