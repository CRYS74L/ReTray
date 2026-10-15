using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WpfPanel = System.Windows.Controls.Panel;

namespace LanDeviceMonitor.Services;

public static class MicaWindowService
{
    private const int DwmUseImmersiveDarkMode = 20;
    private const int DwmWindowCornerPreference = 33;
    private const int DwmSystemBackdropType = 38;
    private const int DwmRedirectionBitmapAlpha = 39;
    private const int NoSystemBackdrop = 1;
    private const int RoundWindowCorners = 2;
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

    internal static bool LastAcrylicApplySucceeded { get; private set; }
    internal static bool LastSystemBackdropApplySucceeded { get; private set; }
    internal static bool LastFrameRefreshSucceeded { get; private set; }
    internal static bool LastRedirectionBitmapAlphaApplySucceeded { get; private set; }
    internal static bool LastRedirectionBitmapAlphaEnabled { get; private set; }
    internal static string? LastAcrylicError { get; private set; }
    internal static IntPtr ActiveBackdropWindowHandle { get; private set; }
    private static readonly DrawingBrush LightMinimizeTransitionMaterial =
        CreateMinimizeTransitionMaterialBrush(useDarkMode: false);
    private static readonly DrawingBrush DarkMinimizeTransitionMaterial =
        CreateMinimizeTransitionMaterialBrush(useDarkMode: true);

    public static void Apply(Window window, bool useDarkMode = false)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        PrepareTransparentWpfSurface(window, handle);
        ApplyWindowFrameAttributes(handle);
        ApplyTheme(window, useDarkMode);
        LastFrameRefreshSucceeded =
            window.IsVisible && RefreshWindowFrame(handle);
    }

    public static void ApplyTheme(Window window, bool useDarkMode)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        PrepareTransparentWpfSurface(window, handle);
        ApplyDwmThemeAttribute(handle, useDarkMode);
        ApplyPersistentAcrylicBackdrop(handle, useDarkMode);
    }

    public static void Dispose()
    {
        ActiveBackdropWindowHandle = IntPtr.Zero;
    }

    internal static DrawingBrush GetMinimizeTransitionMaterialBrush(
        bool useDarkMode) =>
        useDarkMode
            ? DarkMinimizeTransitionMaterial
            : LightMinimizeTransitionMaterial;

    internal static BitmapSource? CaptureWindowSnapshot(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero ||
            !GetWindowRect(handle, out var bounds) ||
            bounds.Right <= bounds.Left ||
            bounds.Bottom <= bounds.Top)
        {
            return null;
        }

        try
        {
            var width = bounds.Right - bounds.Left;
            var height = bounds.Bottom - bounds.Top;
            using var bitmap = new System.Drawing.Bitmap(
                width,
                height,
                System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
            {
                graphics.CopyFromScreen(
                    bounds.Left,
                    bounds.Top,
                    0,
                    0,
                    new System.Drawing.Size(width, height),
                    System.Drawing.CopyPixelOperation.SourceCopy);
            }

            var bitmapHandle = bitmap.GetHbitmap();
            try
            {
                var source = Imaging.CreateBitmapSourceFromHBitmap(
                    bitmapHandle,
                    IntPtr.Zero,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());
                source.Freeze();
                return source;
            }
            finally
            {
                _ = DeleteObject(bitmapHandle);
            }
        }
        catch (Exception exception) when (
            exception is System.ComponentModel.Win32Exception or
            ExternalException or
            ArgumentException)
        {
            return null;
        }
    }

    internal static void PrepareForMinimizeTransition(
        Window window,
        WpfPanel materialHost,
        bool useDarkMode,
        ImageSource? transitionSnapshot = null)
    {
        materialHost.Background = transitionSnapshot is null
            ? GetMinimizeTransitionMaterialBrush(useDarkMode)
            : CreateTransitionSnapshotBrush(transitionSnapshot);
        materialHost.UpdateLayout();

        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero ||
            !OperatingSystem.IsWindowsVersionAtLeast(10, 0, 26100))
        {
            LastRedirectionBitmapAlphaApplySucceeded = false;
            LastRedirectionBitmapAlphaEnabled = false;
            return;
        }

        // The WPF client surface is transparent because Acrylic is composed
        // behind it. Preserve the rendered fallback as an opaque DWM animation
        // bitmap instead of reapplying that transparency to the snapshot.
        var useRedirectionBitmapAlpha = 0;
        LastRedirectionBitmapAlphaApplySucceeded =
            DwmSetWindowAttribute(
                handle,
                DwmRedirectionBitmapAlpha,
                ref useRedirectionBitmapAlpha,
                Marshal.SizeOf<int>()) == 0;
        LastRedirectionBitmapAlphaEnabled = false;
    }

    internal static void CompleteMinimizeTransition(WpfPanel materialHost)
    {
        materialHost.Background = System.Windows.Media.Brushes.Transparent;
    }

    internal static bool FlushComposition(Window window)
    {
        window.Dispatcher.VerifyAccess();
        // A wholly off-screen HWND has no visible animation to protect.
        // Do not skip the fence for partly visible or occluded on-screen HWNDs.
        var handle = new WindowInteropHelper(window).Handle;
        if (handle != IntPtr.Zero && window.IsVisible &&
            MonitorFromWindow(handle, 0) != IntPtr.Zero)
        {
            WpfRenderSynchronization.CompletePendingRender();
        }
        return DwmFlush() == 0;
    }


    private static ImageBrush CreateTransitionSnapshotBrush(
        ImageSource transitionSnapshot)
    {
        var brush = new ImageBrush(transitionSnapshot)
        {
            Stretch = Stretch.Fill,
            AlignmentX = AlignmentX.Center,
            AlignmentY = AlignmentY.Center
        };
        if (brush.CanFreeze)
        {
            brush.Freeze();
        }

        return brush;
    }

    private static DrawingBrush CreateMinimizeTransitionMaterialBrush(
        bool useDarkMode)
    {
        var drawing = new DrawingGroup();
        drawing.Children.Add(
            new GeometryDrawing(
                new SolidColorBrush(
                    useDarkMode
                        ? System.Windows.Media.Color.FromArgb(0xCC, 0x20, 0x20, 0x22)
                        : System.Windows.Media.Color.FromArgb(0xCC, 0xEF, 0xF0, 0xF2)),
                null,
                new RectangleGeometry(new Rect(0, 0, 64, 64))));

        var grainBrush = new SolidColorBrush(
            useDarkMode
                ? System.Windows.Media.Color.FromArgb(0x12, 0xFF, 0xFF, 0xFF)
                : System.Windows.Media.Color.FromArgb(0x16, 0x55, 0x55, 0x55));
        drawing.Children.Add(
            new GeometryDrawing(
                grainBrush,
                null,
                new EllipseGeometry(new System.Windows.Point(11, 17), 0.7, 0.7)));
        drawing.Children.Add(
            new GeometryDrawing(
                grainBrush,
                null,
                new EllipseGeometry(new System.Windows.Point(43, 9), 0.55, 0.55)));
        drawing.Children.Add(
            new GeometryDrawing(
                grainBrush,
                null,
                new EllipseGeometry(new System.Windows.Point(29, 48), 0.65, 0.65)));
        drawing.Children.Add(
            new GeometryDrawing(
                grainBrush,
                null,
                new EllipseGeometry(new System.Windows.Point(57, 38), 0.5, 0.5)));

        var brush = new DrawingBrush(drawing)
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, 64, 64),
            ViewportUnits = BrushMappingMode.Absolute,
            Stretch = Stretch.None
        };
        brush.Freeze();
        return brush;
    }

    private static void PrepareTransparentWpfSurface(
        Window window,
        IntPtr handle)
    {
        window.Background =
            System.Windows.Media.Brushes.Transparent;
        if (HwndSource.FromHwnd(handle) is { CompositionTarget: { } target })
        {
            target.BackgroundColor = Colors.Transparent;
        }
    }

    private static void ApplyWindowFrameAttributes(IntPtr handle)
    {
        var margins = new Margins
        {
            Left = -1,
            Right = -1,
            Top = -1,
            Bottom = -1
        };
        _ = DwmExtendFrameIntoClientArea(handle, ref margins);

        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            return;
        }

        var cornerPreference = RoundWindowCorners;
        _ = DwmSetWindowAttribute(
            handle,
            DwmWindowCornerPreference,
            ref cornerPreference,
            Marshal.SizeOf<int>());
    }

    private static void ApplyDwmThemeAttribute(
        IntPtr handle,
        bool useDarkMode)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            return;
        }

        var darkMode = useDarkMode ? 1 : 0;
        _ = DwmSetWindowAttribute(
            handle,
            DwmUseImmersiveDarkMode,
            ref darkMode,
            Marshal.SizeOf<int>());
    }

    internal static int GetPersistentAcrylicGradientColor(
        bool useDarkMode) =>
        useDarkMode
            ? unchecked((int)0x30202020)
            : unchecked((int)0x14F5F5F5);

    private static void ApplyPersistentAcrylicBackdrop(
        IntPtr handle,
        bool useDarkMode)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763))
        {
            LastAcrylicApplySucceeded = false;
            LastSystemBackdropApplySucceeded = false;
            LastAcrylicError =
                "当前 Windows 版本不支持持久 Acrylic 背景。";
            return;
        }

        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22621))
        {
            var noSystemBackdrop = NoSystemBackdrop;
            _ = DwmSetWindowAttribute(
                handle,
                DwmSystemBackdropType,
                ref noSystemBackdrop,
                Marshal.SizeOf<int>());
        }

        var policy = new AccentPolicy
        {
            AccentState = AcrylicBlurBehind,
            AccentFlags = DrawAllBorders,
            GradientColor =
                GetPersistentAcrylicGradientColor(useDarkMode)
        };
        var policyPointer = Marshal.AllocHGlobal(
            Marshal.SizeOf<AccentPolicy>());
        try
        {
            Marshal.StructureToPtr(
                policy,
                policyPointer,
                false);
            var data = new WindowCompositionAttributeData
            {
                Attribute =
                    WindowCompositionAttributeAccentPolicy,
                Data = policyPointer,
                SizeOfData = Marshal.SizeOf<AccentPolicy>()
            };
            LastAcrylicApplySucceeded =
                SetWindowCompositionAttribute(handle, ref data);
        }
        finally
        {
            Marshal.FreeHGlobal(policyPointer);
        }

        LastSystemBackdropApplySucceeded =
            LastAcrylicApplySucceeded;
        LastAcrylicError = LastAcrylicApplySucceeded
            ? null
            : $"SetWindowCompositionAttribute 返回失败，" +
              $"Win32Error={Marshal.GetLastWin32Error()}。";
        ActiveBackdropWindowHandle =
            LastAcrylicApplySucceeded ? handle : IntPtr.Zero;
    }

    private static bool RefreshWindowFrame(IntPtr windowHandle)
    {
        var frameChanged = SetWindowPos(
            windowHandle,
            IntPtr.Zero,
            0,
            0,
            0,
            0,
            SwpNoSize |
            SwpNoMove |
            SwpNoZOrder |
            SwpNoActivate |
            SwpFrameChanged);
        _ = RedrawWindow(
            windowHandle,
            IntPtr.Zero,
            IntPtr.Zero,
            RedrawInvalidate |
            RedrawUpdateNow |
            RedrawFrame |
            RedrawAllChildren);
        return frameChanged && DwmFlush() == 0;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        IntPtr windowHandle,
        int attribute,
        ref int value,
        int valueSize);

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(
        IntPtr windowHandle,
        ref Margins margins);

    [DllImport("dwmapi.dll")]
    private static extern int DwmFlush();

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        IntPtr windowHandle,
        IntPtr windowInsertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RedrawWindow(
        IntPtr windowHandle,
        IntPtr updateRectangle,
        IntPtr updateRegion,
        uint flags);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr windowHandle, uint flags);


    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(
        IntPtr windowHandle,
        out WindowRectangle rectangle);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr graphicsObject);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowCompositionAttribute(
        IntPtr windowHandle,
        ref WindowCompositionAttributeData data);

    [StructLayout(LayoutKind.Sequential)]
    private struct Margins
    {
        public int Left;
        public int Right;
        public int Top;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowRectangle
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
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
        public int SizeOfData;
    }
}
