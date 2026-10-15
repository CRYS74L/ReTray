using Microsoft.UI.Xaml;
using System.Runtime.InteropServices;
using ReTray_App.Controls;
using Windows.Graphics;
namespace ReTray_App;
public sealed partial class MainWindow : Window
{
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(nint window);
    private readonly ResizeSynchronization resizeSynchronization;
    public MainWindow()
    {
        InitializeComponent();
        Title = "ReTray";
        WindowRoot.RequestedTheme = WindowAppearance.PreferredTheme();
        TitleBarHost.Children.Add(WindowAppearance.CreateTitleBar(this, Title, main: true));
        WindowAppearance.ApplyNativeTheme(this, WindowRoot.RequestedTheme == ElementTheme.Default ? WindowRoot.ActualTheme : WindowRoot.RequestedTheme);
        SystemBackdrop = new SoftMicaBackdrop { InitialTheme = WindowRoot.RequestedTheme };
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory,"Assets","WindowIcon.ico"));
        var scale = GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this)) / 96.0;
        var presenter = (Microsoft.UI.Windowing.OverlappedPresenter)AppWindow.Presenter;
        presenter.PreferredMinimumWidth = (int)(300 * scale);
        presenter.PreferredMinimumHeight = (int)(260 * scale);
        AppWindow.Resize(new SizeInt32((int)(440*scale),(int)(720*scale)));
        var root = (FrameworkElement)Content;
        root.ActualThemeChanged += (_, _) => WindowAppearance.ApplyNativeTheme(this, root.ActualTheme);
        RootFrame.Navigate(typeof(MainPage));
        resizeSynchronization = ResizeSynchronization.Attach(this, root);
        AppWindow.Closing += async (_, args) => { if (App.IsExiting) return; args.Cancel = true; await App.QuitAsync(); };
    }
}
