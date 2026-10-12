using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace TrayAssistant_App.Controls;

internal static class WindowsSystemIcon
{
    public static BitmapSource Start()
    {
        var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad;
        image.UriSource = new Uri(Path.Combine(Environment.SystemDirectory, "@WLOGO_96x96.png")); image.EndInit();
        var converted = new FormatConvertedBitmap(image, System.Windows.Media.PixelFormats.Pbgra32, null, 0);
        converted.Freeze(); return converted;
    }
    public static BitmapSource? InputMethod()
    {
        var icons = new IntPtr[1];
        // Native white mode glyph has no contrast outline around its strokes.
        ExtractIconEx(Path.Combine(Environment.SystemDirectory, "InputMethod", "CHS", "ChsIME.exe"), 10, icons, null, 1);
        if (icons[0] == IntPtr.Zero) return null;
        try {
            var bitmap = Imaging.CreateBitmapSourceFromHIcon(icons[0], Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            // TrayIconImage samples a premultiplied BGRA source.
            var converted = new FormatConvertedBitmap(bitmap, System.Windows.Media.PixelFormats.Pbgra32, null, 0);
            converted.Freeze(); return converted;
        }
        finally { DestroyIcon(icons[0]); }
    }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint ExtractIconEx(string file, int index, IntPtr[] large, IntPtr[]? small, uint count);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);
}
