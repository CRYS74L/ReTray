using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace ReTray_App.Controls;

/// <summary>Uses the LAN monitor's Persistent Acrylic policy on a transparent non-layered WPF popup.</summary>
public static class ComboPopupMaterial
{
    private const int DwmUseImmersiveDarkMode = 20;
    private const int DwmWindowCornerPreference = 33;
    private const int DwmSystemBackdropType = 38;
    private const int NoSystemBackdrop = 1;
    private const int RoundCorners = 2;
    private const int WindowCompositionAttributeAccentPolicy = 19;
    private const int AcrylicBlurBehind = 4;
    private const int DrawAllBorders = 2;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpFrameChanged = 0x0020;
    private const uint RedrawInvalidate = 0x0001;
    private const uint RedrawUpdateNow = 0x0100;
    private const uint RedrawFrame = 0x0400;
    private const uint RedrawAllChildren = 0x0080;

    private static readonly DependencyProperty StateProperty = DependencyProperty.RegisterAttached(
        "State", typeof(PopupState), typeof(ComboPopupMaterial));
    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached(
        "Enabled", typeof(bool), typeof(ComboPopupMaterial), new FrameworkPropertyMetadata(false, EnabledChanged));

    public static bool GetEnabled(DependencyObject element) => (bool)element.GetValue(EnabledProperty);
    public static void SetEnabled(DependencyObject element, bool value) => element.SetValue(EnabledProperty, value);

    private static void EnabledChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        if (sender is not Popup popup) return;
        (popup.GetValue(StateProperty) as PopupState)?.Detach();
        if ((bool)args.NewValue) popup.SetValue(StateProperty, new PopupState(popup));
        else popup.ClearValue(StateProperty);
    }

    private sealed class PopupState
    {
        private readonly Popup _popup;
        private HwndSource? _source;
        private bool _subscribed;
        private bool _initialized;
        private int _generation;

        internal PopupState(Popup popup)
        {
            _popup = popup;
            // Persistent Acrylic is composed behind an ordinary HWND; WPF
            // must not turn the popup into a layered per-pixel-opacity window.
            _popup.AllowsTransparency = false;
            _popup.Opened += Opened;
            _popup.Closed += Closed;
            if (_popup.IsOpen) Opened(_popup, EventArgs.Empty);
        }

        private void Opened(object? sender, EventArgs args)
        {
            _initialized = false;
            var generation = ++_generation;
            PrepareTransparentSurface();
            if (!_subscribed)
            {
                ThemeManager.Changed += ApplyTheme;
                _subscribed = true;
            }
            // Popup.Opened can precede the first WPF render of its HWND. Clear
            // the client surface now and apply the native material once that
            // initialization turn has completed, without layout/event polling.
            _popup.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                if (!_popup.IsOpen || generation != _generation) return;
                _initialized = true;
                ApplyTheme();
            }));
        }

        private void ApplyTheme()
        {
            if (!_popup.IsOpen || !_initialized) return;
            PrepareTransparentSurface();
            if (_source is not { Handle: var hwnd } || hwnd == IntPtr.Zero) return;
            ApplyPersistentAcrylic(hwnd, ThemeManager.IsDark);
        }

        private void PrepareTransparentSurface()
        {
            _source = _popup.Child is Visual child ? PresentationSource.FromVisual(child) as HwndSource : null;
            if (_source?.CompositionTarget is { } target) target.BackgroundColor = Colors.Transparent;
            if (_popup.Child is Border surface) surface.Background = Brushes.Transparent;
        }

        private void Closed(object? sender, EventArgs args)
        {
            if (_subscribed) ThemeManager.Changed -= ApplyTheme;
            _subscribed = false;
            _initialized = false;
            _generation++;
            _source = null;
        }

        internal void Detach()
        {
            Closed(_popup, EventArgs.Empty);
            _popup.Opened -= Opened;
            _popup.Closed -= Closed;
        }
    }

    private static void ApplyPersistentAcrylic(IntPtr hwnd, bool useDarkMode)
    {
        var margins = new Margins(-1);
        _ = DwmExtendFrameIntoClientArea(hwnd, ref margins);
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            var dark = useDarkMode ? 1 : 0;
            var corners = RoundCorners;
            _ = DwmSetWindowAttribute(hwnd, DwmUseImmersiveDarkMode, ref dark, sizeof(int));
            _ = DwmSetWindowAttribute(hwnd, DwmWindowCornerPreference, ref corners, sizeof(int));
        }
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22621))
        {
            var noBackdrop = NoSystemBackdrop;
            _ = DwmSetWindowAttribute(hwnd, DwmSystemBackdropType, ref noBackdrop, sizeof(int));
        }
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763)) return;

        var policy = new AccentPolicy
        {
            AccentState = AcrylicBlurBehind,
            AccentFlags = DrawAllBorders,
            GradientColor = useDarkMode ? unchecked((int)0x30202020) : unchecked((int)0x14F5F5F5)
        };
        var pointer = Marshal.AllocHGlobal(Marshal.SizeOf<AccentPolicy>());
        try
        {
            Marshal.StructureToPtr(policy, pointer, false);
            var data = new WindowCompositionAttributeData
            {
                Attribute = WindowCompositionAttributeAccentPolicy,
                Data = pointer,
                SizeOfData = (UIntPtr)(uint)Marshal.SizeOf<AccentPolicy>()
            };
            _ = SetWindowCompositionAttribute(hwnd, ref data);
        }
        finally
        {
            Marshal.FreeHGlobal(pointer);
        }

        _ = SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0,
            SwpNoSize | SwpNoMove | SwpNoZOrder | SwpNoActivate | SwpFrameChanged);
        _ = RedrawWindow(hwnd, IntPtr.Zero, IntPtr.Zero,
            RedrawInvalidate | RedrawUpdateNow | RedrawFrame | RedrawAllChildren);
        _ = DwmFlush();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AccentPolicy
    {
        public int AccentState;
        public int AccentFlags;
        public int GradientColor;
        public int AnimationId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowCompositionAttributeData
    {
        public int Attribute;
        public IntPtr Data;
        public UIntPtr SizeOfData;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Margins
    {
        public int Left;
        public int Right;
        public int Top;
        public int Bottom;
        public Margins(int value) => Left = Right = Top = Bottom = value;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins margins);

    [DllImport("dwmapi.dll")]
    private static extern int DwmFlush();

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int width, int height, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RedrawWindow(IntPtr hwnd, IntPtr updateRectangle, IntPtr updateRegion, uint flags);
}
