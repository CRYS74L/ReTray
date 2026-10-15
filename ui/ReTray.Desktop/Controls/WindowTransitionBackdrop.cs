using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ReTray_App.Controls;

/// <summary>Preserves the composed LAN-style material in DWM's system animation bitmap.</summary>
internal sealed class WindowTransitionBackdrop : IDisposable
{
    private readonly Window window;
    private Panel? host;
    private Brush? normalBackground;
    private Image? overlay;
    public bool IsPrepared { get; private set; }
    private static readonly DrawingBrush LightFallback = CreateFallback(false), DarkFallback = CreateFallback(true);

    public WindowTransitionBackdrop(Window window) => this.window = window;

    public void Prepare(bool dark, Panel? root = null)
    {
        window.Dispatcher.VerifyAccess();
        if (IsPrepared || !window.IsVisible || window.WindowState == WindowState.Minimized) return;
        host = root ?? window.Content as Panel
            ?? throw new InvalidOperationException("Window transition material requires the complete root Panel.");
        normalBackground = host.Background;
        // First finish the live WPF batch and DWM present, then capture its actual
        // composed pixels. Rendering notifications alone do not complete this work.
        Flush(window);
        var snapshot = Capture(window);
        var material = snapshot == null ? (Brush)(dark ? DarkFallback : LightFallback)
            : new ImageBrush(snapshot) { Stretch = Stretch.Fill, AlignmentX = AlignmentX.Center, AlignmentY = AlignmentY.Center };
        if (material.CanFreeze) material.Freeze();
        host.SetCurrentValue(Panel.BackgroundProperty, material);
        if (snapshot != null)
        {
            overlay = new Image { Source = snapshot, Stretch = Stretch.Fill,
                IsHitTestVisible = false, Focusable = false, HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch };
            if (host is Grid grid)
            {
                Grid.SetRowSpan(overlay, Math.Max(1, grid.RowDefinitions.Count));
                Grid.SetColumnSpan(overlay, Math.Max(1, grid.ColumnDefinitions.Count));
            }
            Panel.SetZIndex(overlay, int.MaxValue);
            host.Children.Add(overlay);
        }
        IsPrepared = true;
        host.UpdateLayout();
        var handle = new WindowInteropHelper(window).Handle;
        if (handle != 0 && OperatingSystem.IsWindowsVersionAtLeast(10, 0, 26100))
        {
            var alpha = 0;
            DwmSetWindowAttribute(handle, 39, ref alpha, sizeof(int));
        }
        host.InvalidateVisual(); overlay?.InvalidateVisual();
        // Commit the complete snapshot before starting minimize/close/hide.
        Flush(window);
    }

    public void Complete()
    {
        window.Dispatcher.VerifyAccess();
        if (host != null)
        {
            if (overlay != null) { overlay.Source = null; host.Children.Remove(overlay); }
            host.SetCurrentValue(Panel.BackgroundProperty, normalBackground ?? Brushes.Transparent);
            host.InvalidateVisual();
        }
        overlay = null; host = null; normalBackground = null; IsPrepared = false;
    }

    public static void Flush(Window window)
    {
        window.Dispatcher.VerifyAccess();
        var handle = new WindowInteropHelper(window).Handle;
        if (handle != 0 && window.IsVisible && MonitorFromWindow(handle, 0) != 0)
            WpfRenderSynchronization.CompletePendingRender();
        DwmFlush();
    }

    private static BitmapSource? Capture(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == 0 || !GetWindowRect(handle, out var bounds)) return null;
        var width = bounds.Right - bounds.Left; var height = bounds.Bottom - bounds.Top;
        if (width <= 0 || height <= 0) return null;
        nint screen = 0, memory = 0, bitmap = 0, original = 0;
        try
        {
            // GDI capture is the same CopyFromScreen/SRCCOPY operation as the LAN
            // implementation, without adding a System.Drawing.Common dependency.
            screen = GetDC(0); memory = CreateCompatibleDC(screen);
            var info = new BitmapInfo { Header = new BitmapInfoHeader { Size = 40, Width = width,
                Height = -height, Planes = 1, BitCount = 32 } };
            bitmap = CreateDIBSection(screen, ref info, 0, out var pixels, 0, 0);
            if (screen == 0 || memory == 0 || bitmap == 0 || pixels == 0) throw new Win32Exception();
            original = SelectObject(memory, bitmap);
            if (!BitBlt(memory, 0, 0, width, height, screen, bounds.Left, bounds.Top, 0x00CC0020)) throw new Win32Exception();
            var stride = checked(width * 4); var bytes = new byte[checked(stride * height)];
            Marshal.Copy(pixels, bytes, 0, bytes.Length);
            var dpi = GetDpiForWindow(handle);
            var result = BitmapSource.Create(width, height, dpi == 0 ? 96 : dpi, dpi == 0 ? 96 : dpi,
                PixelFormats.Bgr32, null, bytes, stride);
            result.Freeze(); return result;
        }
        catch (Exception exception) when (exception is Win32Exception or ExternalException or ArgumentException)
        { return null; }
        finally
        {
            if (original != 0 && memory != 0) SelectObject(memory, original);
            if (bitmap != 0) DeleteObject(bitmap);
            if (memory != 0) DeleteDC(memory);
            if (screen != 0) ReleaseDC(0, screen);
        }
    }

    private static DrawingBrush CreateFallback(bool dark)
    {
        var drawing = new DrawingGroup();
        drawing.Children.Add(new GeometryDrawing(new SolidColorBrush(dark
            ? Color.FromArgb(0xCC, 0x20, 0x20, 0x22) : Color.FromArgb(0xCC, 0xEF, 0xF0, 0xF2)),
            null, new RectangleGeometry(new Rect(0, 0, 64, 64))));
        var grain = new SolidColorBrush(dark ? Color.FromArgb(0x12, 255, 255, 255) : Color.FromArgb(0x16, 0x55, 0x55, 0x55));
        foreach (var point in new[] { (11.0, 17.0, .7), (43.0, 9.0, .55), (29.0, 48.0, .65), (57.0, 38.0, .5) })
            drawing.Children.Add(new GeometryDrawing(grain, null, new EllipseGeometry(new Point(point.Item1, point.Item2), point.Item3, point.Item3)));
        var brush = new DrawingBrush(drawing) { TileMode = TileMode.Tile, Viewport = new Rect(0, 0, 64, 64),
            ViewportUnits = BrushMappingMode.Absolute, Stretch = Stretch.None };
        brush.Freeze(); return brush;
    }

    public void Dispose() => Complete();
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct BitmapInfoHeader
    { public uint Size; public int Width, Height; public ushort Planes, BitCount; public uint Compression, SizeImage; public int XPels, YPels; public uint ColorsUsed, ColorsImportant; }
    [StructLayout(LayoutKind.Sequential)] private struct BitmapInfo { public BitmapInfoHeader Header; public uint Colors; }
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint handle, out NativeRect bounds);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(nint handle);
    [DllImport("user32.dll")] private static extern nint MonitorFromWindow(nint handle, uint flags);
    [DllImport("user32.dll")] private static extern nint GetDC(nint handle);
    [DllImport("user32.dll")] private static extern int ReleaseDC(nint handle, nint dc);
    [DllImport("gdi32.dll")] private static extern nint CreateCompatibleDC(nint dc);
    [DllImport("gdi32.dll")] private static extern nint CreateDIBSection(nint dc, ref BitmapInfo info, uint usage, out nint pixels, nint section, uint offset);
    [DllImport("gdi32.dll")] private static extern nint SelectObject(nint dc, nint value);
    [DllImport("gdi32.dll")] private static extern bool BitBlt(nint destination, int x, int y, int width, int height, nint source, int sourceX, int sourceY, uint operation);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(nint value);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(nint dc);
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(nint handle, int attribute, ref int value, int size);
    [DllImport("dwmapi.dll")] private static extern int DwmFlush();
}
