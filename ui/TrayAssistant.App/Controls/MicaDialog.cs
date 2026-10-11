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
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern nint SetWindowLongPtr(nint window, int index, nint value);

    public static async Task<bool> ShowAsync(string title, UIElement content, ElementTheme theme,
        string? primaryText = null, string? closeText = null, Func<Task<bool>>? apply = null,
        int height = 640, Func<Task<bool>>? beforeClose = null)
    {
        var owner = App.Window;
        var ownerHitTest = owner.Content.IsHitTestVisible;
        var ownerHandle = WinRT.Interop.WindowNative.GetWindowHandle(owner);
        var window = new Window { Title = title };
        WindowEntrance.Prepare(window);
        var handle = WinRT.Interop.WindowNative.GetWindowHandle(window);
        SetWindowLongPtr(handle, -8, ownerHandle);
        var presenter = (OverlappedPresenter)window.AppWindow.Presenter;
        presenter.IsMaximizable = false; presenter.IsMinimizable = false;
        var resolved = WindowAppearance.ResolveTheme(theme);
        var root = new Grid { RequestedTheme = resolved, Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent) };
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(32) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.Children.Add(WindowAppearance.CreateTitleBar(window, title));
        var panel = new Grid { Padding = new Thickness(14, 12, content is ScrollViewer ? 2 : 14, 14), RowSpacing = 12 };
        panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        panel.Children.Add(content);
        Grid.SetRow(panel, 1); root.Children.Add(panel);
        window.Content = root;
        using var resizeSynchronization = ResizeSynchronization.Attach(window, root);
        var uiFont = (FontFamily)Application.Current.Resources["AppUiFontFamily"];
        if (content is Control control) control.FontFamily = uiFont;
        if (content is TextBlock text) text.FontFamily = uiFont;
        WindowAppearance.ApplyNativeTheme(window, resolved);
        window.SystemBackdrop = new SoftMicaBackdrop { InitialTheme = resolved };
        window.AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"));
        var completion = new TaskCompletionSource<bool>();
        bool accepted = false, ownerAlive = true, closing = false, allowClose = false, ownerBlocked = false;
        void OwnerActivated(object sender, WindowActivatedEventArgs args)
        {
            if (ownerBlocked && args.WindowActivationState != WindowActivationState.Deactivated) window.Activate();
        }
        void OwnerPreviewKeyDown(object sender, KeyRoutedEventArgs args)
        {
            if (ownerBlocked) { args.Handled = true; window.Activate(); }
        }
        void OwnerGettingFocus(UIElement sender, GettingFocusEventArgs args)
        {
            if (ownerBlocked) args.TryCancel();
        }
        owner.Activated += OwnerActivated;
        owner.Content.PreviewKeyDown += OwnerPreviewKeyDown;
        owner.Content.GettingFocus += OwnerGettingFocus;
        if (primaryText != null)
        {
            panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8 };
            var primary = new Button { Content = primaryText };
            var close = new Button { Content = closeText ?? "关闭" };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(primary, "DialogPrimary");
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(close, "DialogClose");
            primary.Click += async (_, _) => { primary.IsEnabled = false; try { if (apply == null || await apply()) { accepted = true; WindowAppearance.RequestClose(window); } } finally { primary.IsEnabled = true; } };
            close.Click += (_, _) => WindowAppearance.RequestClose(window);
            actions.Children.Add(primary); actions.Children.Add(close); Grid.SetRow(actions, 1); panel.Children.Add(actions);
        }
        root.KeyDown += (_, e) => { if (e.Key == global::Windows.System.VirtualKey.Escape) { e.Handled = true; WindowAppearance.RequestClose(window); } };
        window.AppWindow.Closing += async (_, args) =>
        {
            if (allowClose) return;
            args.Cancel = true;
            if (closing) return;
            closing = true;
            if (beforeClose != null && !await beforeClose()) { closing = false; return; }
            // Keep the themed content and backdrop alive while Windows hides the surface.
            ownerBlocked = false;
            if (ownerAlive) { owner.Content.IsHitTestVisible = ownerHitTest; owner.Activate(); }
            window.AppWindow.Hide();
            await Task.Delay(180);
            allowClose = true; window.Close();
        };
        void OwnerClosed(object sender, WindowEventArgs args) { ownerAlive = false; allowClose = true; window.Close(); }
        owner.Closed += OwnerClosed;
        window.Closed += (_, _) =>
        {
            owner.Closed -= OwnerClosed;
            owner.Activated -= OwnerActivated;
            owner.Content.PreviewKeyDown -= OwnerPreviewKeyDown;
            owner.Content.GettingFocus -= OwnerGettingFocus;
            ownerBlocked = false;
            panel.Children.Remove(content);
            if (ownerAlive) { owner.Content.IsHitTestVisible = ownerHitTest; owner.Activate(); }
            completion.TrySetResult(accepted);
        };
        var scale = GetDpiForWindow(ownerHandle) / 96.0;
        presenter.PreferredMinimumWidth = (int)(300 * scale);
        presenter.PreferredMinimumHeight = (int)(260 * scale);
        var area = DisplayArea.GetFromWindowId(owner.AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        var widthPixels = Math.Min((int)(380 * scale), area.Width);
        var heightPixels = Math.Min((int)(height * scale), area.Height);
        window.AppWindow.Resize(new SizeInt32(widthPixels, heightPixels));
        var x = Math.Clamp(owner.AppWindow.Position.X + (owner.AppWindow.Size.Width - widthPixels) / 2, area.X, area.X + area.Width - widthPixels);
        var y = Math.Clamp(owner.AppWindow.Position.Y + (owner.AppWindow.Size.Height - heightPixels) / 2, area.Y, area.Y + area.Height - heightPixels);
        window.AppWindow.Move(new PointInt32(x, y));
        owner.Content.IsHitTestVisible = false;
        ownerBlocked = true;
        window.Activate();
        await WindowEntrance.RevealAsync(window, root);
        return await completion.Task;
    }
}
