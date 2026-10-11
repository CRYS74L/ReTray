using Microsoft.UI;
using Microsoft.UI.Xaml;
using System.Runtime.InteropServices;
using Windows.Graphics;
namespace TrayAssistant_App;
public sealed partial class MainWindow : Window
{
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(nint window);
    public MainWindow()
    {
        InitializeComponent();
        Title = "托盘图标助手"; AppTitleBar.Title = Title;
        ExtendsContentIntoTitleBar = true; SetTitleBar(AppTitleBar);
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory,"Assets","AppIcon.ico"));
        var scale = GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this)) / 96.0;
        AppWindow.Resize(new SizeInt32((int)(440*scale),(int)(720*scale)));
        var root = (FrameworkElement)Content;
        root.ActualThemeChanged += (_, _) => UpdateCaptionTheme(root.ActualTheme);
        UpdateCaptionTheme(root.ActualTheme);
        RootFrame.Navigate(typeof(MainPage));
        AppWindow.Closing += async (_, args) => { if (App.IsExiting) return; args.Cancel = true; await App.QuitAsync(); };
    }
    private void UpdateCaptionTheme(ElementTheme theme)
    {
        var dark = theme == ElementTheme.Dark;
        var bar = AppWindow.TitleBar;
        bar.ButtonForegroundColor = dark ? Colors.White : Colors.Black;
        bar.ButtonInactiveForegroundColor = dark ? Colors.Gray : Colors.DimGray;
        bar.ButtonBackgroundColor = Colors.Transparent;
        bar.ButtonInactiveBackgroundColor = Colors.Transparent;
        bar.ButtonHoverForegroundColor = dark ? Colors.White : Colors.Black;
        bar.ButtonHoverBackgroundColor = dark ? Windows.UI.Color.FromArgb(255,64,64,64) : Windows.UI.Color.FromArgb(255,225,225,225);
    }
}
