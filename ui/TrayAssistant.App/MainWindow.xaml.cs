using Microsoft.UI.Xaml;
using System.Runtime.InteropServices;
using TrayAssistant_App.Controls;
using Windows.Graphics;
namespace TrayAssistant_App;
public sealed partial class MainWindow : Window
{
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(nint window);
    public MainWindow()
    {
        InitializeComponent();
        Title = "托盘图标助手";
        WindowRoot.RequestedTheme = WindowAppearance.PreferredTheme();
        TitleBarHost.Children.Add(WindowAppearance.CreateTitleBar(this, Title, main: true));
        WindowAppearance.ApplyNativeTheme(this, WindowRoot.RequestedTheme == ElementTheme.Default ? WindowRoot.ActualTheme : WindowRoot.RequestedTheme);
        SystemBackdrop = new SoftMicaBackdrop { InitialTheme = WindowRoot.RequestedTheme };
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory,"Assets","AppIcon.ico"));
        var scale = GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this)) / 96.0;
        AppWindow.Resize(new SizeInt32((int)(440*scale),(int)(720*scale)));
        var root = (FrameworkElement)Content;
        root.ActualThemeChanged += (_, _) => WindowAppearance.ApplyNativeTheme(this, root.ActualTheme);
        RootFrame.Navigate(typeof(MainPage));
        AppWindow.Closing += async (_, args) => { if (App.IsExiting) return; args.Cancel = true; await App.QuitAsync(); };
    }
}
