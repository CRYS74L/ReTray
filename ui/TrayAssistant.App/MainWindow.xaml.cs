using Microsoft.UI.Xaml;
using System.Runtime.InteropServices;
using Windows.Graphics;
namespace TrayAssistant_App;
public sealed partial class MainWindow : Window
{
 [DllImport("user32.dll")]private static extern uint GetDpiForWindow(nint window);
 public MainWindow(){InitializeComponent();Title="托盘图标助手";AppTitleBar.Title="托盘图标助手";ExtendsContentIntoTitleBar=true;SetTitleBar(AppTitleBar);AppWindow.SetIcon("Assets/AppIcon.ico");var hwnd=WinRT.Interop.WindowNative.GetWindowHandle(this);var scale=GetDpiForWindow(hwnd)/96.0;AppWindow.Resize(new SizeInt32((int)(1160*scale),(int)(820*scale)));RootFrame.Navigate(typeof(MainPage));}
}
