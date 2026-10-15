using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Interop;
using Application = System.Windows.Application;

namespace LanDeviceMonitor.Services;

public sealed class TrayService : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly Icon _icon;
    private readonly Action _showWindow;
    private readonly Action _exitApplication;
    private TrayMenuWindow? _menu;
    private bool _isDisposed;

    public TrayService(Action showWindow, Action exitApplication)
    {
        _showWindow = showWindow;
        _exitApplication = exitApplication;
        _icon = LoadSystemIcon();
        _notifyIcon = new NotifyIcon
        {
            Icon = _icon,
            Text = BuildNotifyIconText(),
            Visible = true
        };

        _notifyIcon.MouseClick += NotifyIcon_MouseClick;
        _notifyIcon.MouseUp += NotifyIcon_MouseUp;
    }

    private void NotifyIcon_MouseClick(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            // NotifyIcon suppresses the second MouseClick in a double click,
            // so the first release opens immediately without activating twice.
            Application.Current.Dispatcher.Invoke(_showWindow);
        }
    }

    private void NotifyIcon_MouseUp(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Right)
        {
            return;
        }

        Application.Current.Dispatcher.Invoke(ShowMenu);
    }

    private void ShowMenu()
    {
        _menu?.Close();
        _menu = new TrayMenuWindow(_showWindow, _exitApplication);
        _menu.Closed += (_, _) => _menu = null;
        _menu.WindowStartupLocation = WindowStartupLocation.Manual;
        _menu.Left = -10000;
        _menu.Top = -10000;
        var cursor = Cursor.Position;
        var workingArea = Screen.FromPoint(cursor).WorkingArea;
        _menu.Show();
        var dpi = GetDpiForWindow(new WindowInteropHelper(_menu).Handle);
        var iconSize = Math.Max(16, GetSystemMetricsForDpi(
            SmCxSmallIcon,
            dpi <= 0 ? GetDpiForSystem() : dpi));
        var trayIconTopRight = new System.Drawing.Point(
            cursor.X + (iconSize / 2),
            cursor.Y - (iconSize / 2));
        PositionMenu(_menu, trayIconTopRight, workingArea);
        _menu.Activate();
    }

    private static void PositionMenu(
        Window menu,
        System.Drawing.Point cursor,
        Rectangle workingArea)
    {
        const int transparentMarginDip = 9;

        var handle = new WindowInteropHelper(menu).Handle;
        var dpi = GetDpiForWindow(handle);
        var scale = dpi <= 0 ? 1d : dpi / 96d;

        int menuWidth;
        int menuHeight;
        if (GetWindowRect(handle, out var rect))
        {
            menuWidth = rect.Right - rect.Left;
            menuHeight = rect.Bottom - rect.Top;
        }
        else
        {
            menuWidth = Math.Max(1, (int)Math.Ceiling(menu.ActualWidth * scale));
            menuHeight = Math.Max(1, (int)Math.Ceiling(menu.ActualHeight * scale));
        }

        var placement = CalculateMenuPosition(
            cursor,
            workingArea,
            menuWidth,
            menuHeight,
            scale,
            transparentMarginDip);

        SetWindowPos(
            handle,
            IntPtr.Zero,
            placement.X,
            placement.Y,
            0,
            0,
            SwpNoSize | SwpNoZOrder | SwpNoActivate);
    }

    internal static System.Drawing.Point CalculateMenuPosition(
        System.Drawing.Point cursor,
        Rectangle workingArea,
        int menuWidth,
        int menuHeight,
        double scale,
        int transparentMarginDip = 9)
    {
        var transparentMargin = (int)Math.Round(transparentMarginDip * scale);
        var left = cursor.X - transparentMargin;
        var top = cursor.Y - menuHeight + transparentMargin;

        if (top < workingArea.Top)
        {
            top = cursor.Y - transparentMargin;
        }

        var maxLeft = Math.Max(workingArea.Left, workingArea.Right - menuWidth);
        var maxTop = Math.Max(workingArea.Top, workingArea.Bottom - menuHeight);
        return new System.Drawing.Point(
            Math.Clamp(left, workingArea.Left, maxLeft),
            Math.Clamp(top, workingArea.Top, maxTop));
    }

    public void ShowSystemNotification(string title, string message)
    {
        if (_isDisposed)
        {
            return;
        }

        if (!System.Windows.Application.Current.Dispatcher.CheckAccess())
        {
            System.Windows.Application.Current.Dispatcher.BeginInvoke(
                () => ShowSystemNotification(title, message));
            return;
        }

        _notifyIcon.ShowBalloonTip(
            5000,
            title,
            message,
            ToolTipIcon.None);
    }

    internal static string BuildNotifyIconText() =>
        "局域网设备助手";

    private static Icon LoadSystemIcon()
    {
        var systemIconPath = Path.Combine(
            AppContext.BaseDirectory,
            "Assets",
            "SystemIcon.ico");
        if (File.Exists(systemIconPath))
        {
            var dpi = GetDpiForSystem();
            var iconSize = GetSystemMetricsForDpi(SmCxSmallIcon, dpi);
            using var systemIcon = new Icon(
                systemIconPath,
                Math.Max(iconSize, 16),
                Math.Max(iconSize, 16));
            return (Icon)systemIcon.Clone();
        }

        var executablePath = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(executablePath))
        {
            using var extracted = Icon.ExtractAssociatedIcon(executablePath);
            if (extracted is not null)
            {
                return (Icon)extracted.Clone();
            }
        }

        return (Icon)SystemIcons.Application.Clone();
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _menu?.Close();
        _notifyIcon.Visible = false;
        _notifyIcon.MouseClick -= NotifyIcon_MouseClick;
        _notifyIcon.MouseUp -= NotifyIcon_MouseUp;
        _notifyIcon.Dispose();
        _icon.Dispose();
    }

    public void PrepareForProcessExit()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _menu?.PrepareForProcessExit();
        _notifyIcon.MouseClick -= NotifyIcon_MouseClick;
        _notifyIcon.MouseUp -= NotifyIcon_MouseUp;
        _notifyIcon.Visible = false;
    }

    [DllImport("user32.dll")]
    private static extern uint GetDpiForSystem();

    [DllImport("user32.dll")]
    private static extern int GetSystemMetricsForDpi(int nIndex, uint dpi);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hWnd, out NativeRect lpRect);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int x,
        int y,
        int cx,
        int cy,
        uint flags);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    private const int SmCxSmallIcon = 49;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
}
