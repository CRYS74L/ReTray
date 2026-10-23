using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Localization = ReTray_App.Services.Localization;
namespace ReTray_App.Controls;

// Own-tray geometry is tuned independently; fonts, palette and motion are shared.
public sealed class TrayCommandMenu
{
    private ContextMenu? menu;
    public void Show(Point? clickPosition = null, FrameworkElement? source = null)
    {
        if (App.IsExiting) return;
        if (menu is { IsOpen: true }) { menu.IsOpen = false; return; }
        if (clickPosition is null) {
            GetCursorPos(out var cursor);
            clickPosition = new Point(cursor.X, cursor.Y);
        }
        var anchor = source is null ? new ScreenAnchor(clickPosition.Value) : null;
        var placementTarget = source ?? anchor!.Target;
        var localAnchor = source is null ? new Point() : source.PointFromScreen(clickPosition.Value);
        var ownerWindow = source is null ? Application.Current.MainWindow : Window.GetWindow(source);
        var ownerHandle = ownerWindow is null ? 0 : new WindowInteropHelper(ownerWindow).Handle;
        bool visibleOwner = ownerHandle != 0 && IsWindowVisible(ownerHandle);
        // Acquire foreground BEFORE WPF enters menu mode. Activating the popup
        // HWND afterwards can reactivate a visible WPF owner and steal capture
        // back from the menu during its first frame.
        if (visibleOwner) SetForegroundWindow(ownerHandle);
        ContextMenu? ownedMenu = null;
        try {
            var openedMenu = new ContextMenu {
                Style = (Style)Application.Current.FindResource("TrayDropdownMenuStyle"),
                Placement = PlacementMode.Custom, PlacementTarget = placementTarget,
                PlacementRectangle = new Rect(localAnchor, new Size()), StaysOpen = false
            };
            menu = ownedMenu = openedMenu;
            openedMenu.ApplyTemplate();
            var surfaceMargin = Surface(openedMenu)?.Margin ?? MenuSurfaceMargin();
            openedMenu.CustomPopupPlacementCallback = (popupSize, _, _) =>
                [TrayMenuPlacement.Candidate(popupSize, Surface(openedMenu)?.Margin ?? surfaceMargin,
                    VisualTreeHelper.GetDpi(openedMenu))];
            openedMenu.Closed += (_, _) => {
                openedMenu.PlacementTarget = null;
                anchor?.Dispose();
                if (ReferenceEquals(menu, openedMenu)) menu = null;
            };
            // A popup can open while the main window is natively hidden in the tray.
            // It owns keyboard focus and outside-click dismissal independently.
            var labels = Labels(Localization.CurrentLanguage);
            Add(labels[0], "TrayMenuOpen", () => { App.OpenInterface(); return Task.CompletedTask; });
            Add(labels[1], "TrayMenuExit", App.QuitAsync);
            System.Windows.Automation.AutomationProperties.SetAutomationId(openedMenu, "ReTrayTrayMenu");
            openedMenu.Opened += (_, _) => openedMenu.Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => {
                if (!ReferenceEquals(menu, openedMenu) || !openedMenu.IsOpen) return;
                // A menu opened inside the interface already has a foreground
                // owner and standard WPF menu focus/capture. Activating its
                // separate popup HWND fights that owner and closes it again.
                if (source is not null || visibleOwner) return;
                if (PresentationSource.FromVisual(openedMenu) is HwndSource popupSource)
                    SetForegroundWindow(popupSource.Handle);
                (openedMenu.Items[0] as MenuItem)?.Focus();
            }));
            openedMenu.IsOpen = true;
        }
        catch {
            if (ownedMenu is not null) ownedMenu.PlacementTarget = null;
            anchor?.Dispose();
            if (ReferenceEquals(menu, ownedMenu)) menu = null;
            throw;
        }
    }
    private static FrameworkElement? Surface(ContextMenu contextMenu)
    {
        if (VisualTreeHelper.GetChildrenCount(contextMenu) == 0) return null;
        var content = VisualTreeHelper.GetChild(contextMenu, 0) as ContentControl;
        content?.ApplyTemplate();
        return content?.Template.FindName("DropdownSurface", content) as FrameworkElement;
    }
    private static Thickness MenuSurfaceMargin()
    {
        var probe = new ContentControl {
            Template = (ControlTemplate)Application.Current.FindResource("TrayMenuSurfaceTemplate")
        };
        probe.ApplyTemplate();
        return ((FrameworkElement)probe.Template.FindName("DropdownSurface", probe)).Margin;
    }
    // The native click is captured once. This invisible source supplies WPF
    // with a real placement target on that screen, independent of the main
    // window's hidden position and of any later movement of the mouse.
    private sealed class ScreenAnchor : IDisposable
    {
        public Border Target { get; } = new() { Background = Brushes.Transparent };
        private readonly HwndSource source;
        public ScreenAnchor(Point physicalPosition)
        {
            var parameters = new HwndSourceParameters("ReTray.MenuAnchor") {
                WindowStyle = unchecked((int)0x80000000), // WS_POPUP, never WS_VISIBLE.
                ExtendedWindowStyle = 0x08000080, // WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW.
                Width = 1, Height = 1
            };
            parameters.SetPosition((int)Math.Round(physicalPosition.X), (int)Math.Round(physicalPosition.Y));
            source = new HwndSource(parameters);
            try {
                source.RootVisual = Target;
                Target.Measure(new Size(1, 1)); Target.Arrange(new Rect(0, 0, 1, 1));
            } catch { source.Dispose(); throw; }
        }
        public void Dispose() => source.Dispose();
    }
    private void Add(string label, string id, Func<Task> action)
    {
        var item = new MenuItem { Header = label,
            Style = (Style)Application.Current.FindResource("TrayCommandItemStyle") };
        // Font advance width includes side bearings. Balance the visible ink.
        var ink = new FormattedText(label, System.Globalization.CultureInfo.CurrentUICulture,
            item.FlowDirection, new Typeface(item.FontFamily, item.FontStyle, item.FontWeight, item.FontStretch),
            item.FontSize, Brushes.Black, VisualTreeHelper.GetDpi(menu!.PlacementTarget).PixelsPerDip);
        item.Padding = new Thickness(11 - ink.OverhangLeading, 0, 11 + ink.OverhangTrailing, 0);
        System.Windows.Automation.AutomationProperties.SetAutomationId(item, id);
        item.Click += async (_, args) => {
            args.Handled = true;
            if (menu is not null) menu.IsOpen = false;
            try { await action(); }
            catch (Exception error) { if (!App.IsExiting) App.Window.TrayPage.Vm.ReportError(error); }
        };
        menu!.Items.Add(item);
    }
    public static string[] Labels(string language) => language switch {
        "en" => ["Open main window", "Exit application"],
        "zh-Hant" => ["開啟主介面", "退出應用"],
        "ja" => ["メイン画面を開く", "アプリを終了"],
        _ => ["打开主界面", "退出应用"]
    };
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint window);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint window);
    [StructLayout(LayoutKind.Sequential)] private struct NativePoint {
        public int X, Y;
        public NativePoint(int x, int y) { X = x; Y = y; }
    }
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out NativePoint point);
}

public static class TrayMenuPlacement
{
    public static CustomPopupPlacement Candidate(Size popupSize, Thickness surfaceMargin) =>
        Candidate(popupSize, surfaceMargin, new DpiScale(1, 1));

    // WPF's placement callback supplies popupSize/Point in device pixels.
    // The shared template Margin remains in DIPs, so convert only that inset.
    public static CustomPopupPlacement Candidate(Size popupSize, Thickness surfaceMargin, DpiScale dpi) => new(
        new Point(-surfaceMargin.Left * dpi.DpiScaleX,
            -popupSize.Height + surfaceMargin.Bottom * dpi.DpiScaleY), PopupPrimaryAxis.None);
}
