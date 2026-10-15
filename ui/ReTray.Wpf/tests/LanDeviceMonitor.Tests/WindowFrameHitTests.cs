using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using LanDeviceMonitor;

internal static class WindowFrameHitTests
{
    [StructLayout(LayoutKind.Sequential)] private struct Bounds {public int Left,Top,Right,Bottom;}
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window,out Bounds bounds);
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr window,int index);
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr window,int message,IntPtr wParam,IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window,IntPtr after,int x,int y,int width,int height,uint flags);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr window,int attr,out Bounds bounds,int size);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateRectRgn(int l,int t,int r,int b);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr value);
    [DllImport("user32.dll")] private static extern int GetWindowRgn(IntPtr window,IntPtr region);
    [DllImport("gdi32.dll")] private static extern int GetRgnBox(IntPtr region,out Bounds bounds);
    public static void Verify(MainWindow window)
    {
        var handle=new WindowInteropHelper(window).Handle;
        var style=GetWindowLong(handle,-16);
        const int required=0x00C00000|0x00080000|0x00020000|0x00010000|0x00040000;
        var chrome=System.Windows.Shell.WindowChrome.GetWindowChrome(window);
        if((style & required)!=required || chrome is null || chrome.CaptionHeight!=32 || chrome.UseAeroCaptionButtons)
            throw new Exception("不是带标题栏、系统菜单、最小化/最大化和可缩放边框的原生窗口");
        GetWindowRect(handle,out var bounds);
        if(window.WindowState==WindowState.Normal)
            foreach(var (x,y,expected) in new[]{(bounds.Left+3,bounds.Top+3,13),(bounds.Right-4,bounds.Top+3,14),((bounds.Left+bounds.Right)/2,bounds.Top+2,12)})
                if(Hit(handle,x,y)!=expected) throw new Exception("紧凑顶栏覆盖了原有缩放命中区域");
        var client=window.WindowRoot.PointToScreen(new Point());
        var captionY=bounds.Top+20*System.Windows.Media.VisualTreeHelper.GetDpi(window).DpiScaleY;
        var hit=Hit(handle,bounds.Left+120,(int)captionY);
        if(hit!=2) throw new Exception($"标题栏未交给 Windows: {hit}");
        if(window.FindName("WindowControlHoverZone") is not null || window.FindName("MaximizeWindowGlyph") is null)
            throw new Exception("原生窗口仍包含自绘窗口控件或最小化覆盖层");
        Console.WriteLine("Historical dev.208 caption PASS: 32 DIP chrome, Windows-style glyphs, system caption hit, no traffic lights.");
    }
    public static void VerifyMaximized(MainWindow window)
    {
        var opacity=window.Opacity;window.Opacity=0;
        try
        {
            foreach(var monitor in System.Windows.Forms.Screen.AllScreens)
            {
                window.WindowState=WindowState.Normal;
                SetWindowPos(new WindowInteropHelper(window).Handle,IntPtr.Zero,monitor.WorkingArea.Left+20,monitor.WorkingArea.Top+20,500,600,0x14);
                window.Dispatcher.Invoke(()=>{},System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                window.WindowState=WindowState.Maximized;
                var frame=new System.Windows.Threading.DispatcherFrame();
                var timer=new System.Windows.Threading.DispatcherTimer {Interval=TimeSpan.FromMilliseconds(350)};
                timer.Tick+=(_,_)=>{timer.Stop();frame.Continue=false;};timer.Start();System.Windows.Threading.Dispatcher.PushFrame(frame);
                window.Dispatcher.Invoke(()=>{},System.Windows.Threading.DispatcherPriority.ApplicationIdle);window.UpdateLayout();
                var handle=new WindowInteropHelper(window).Handle;
                DwmGetWindowAttribute(handle,9,out var visible,Marshal.SizeOf<Bounds>());
                var work=monitor.WorkingArea;
                GetWindowRect(handle,out var raw);
                var clientTop=window.WindowRoot.PointToScreen(new Point());
                var clientBottom=window.WindowRoot.PointToScreen(new Point(window.WindowRoot.ActualWidth,window.WindowRoot.ActualHeight));
                Console.WriteLine($"MAX raw={raw.Left},{raw.Top},{raw.Right},{raw.Bottom}; DWM={visible.Left},{visible.Top},{visible.Right},{visible.Bottom}; WPF={clientTop}/{clientBottom}; work={work}");
                if(clientTop.X<work.Left || clientTop.Y<work.Top || clientBottom.X>work.Right || clientBottom.Y>work.Bottom)
                    throw new Exception("最大化实际窗口/内容范围超出工作区；裁剪区域不能替代实际边界验收");
                var region=CreateRectRgn(0,0,0,0);
                try {if(GetWindowRgn(handle,region)>0){GetRgnBox(region,out var clip);GetWindowRect(handle,out var outer);visible=new Bounds {Left=outer.Left+clip.Left,Top=outer.Top+clip.Top,Right=outer.Left+clip.Right,Bottom=outer.Top+clip.Bottom};}}
                finally {DeleteObject(region);}
                if(visible.Left<work.Left || visible.Top<work.Top || visible.Right>work.Right || visible.Bottom>work.Bottom)
                    throw new Exception($"原生最大化可见范围越界 {visible.Left},{visible.Top},{visible.Right},{visible.Bottom} / {work}");
                Verify(window);
                window.WindowState=WindowState.Normal;
                SendMessage(handle,0x232,IntPtr.Zero,IntPtr.Zero);
                window.Dispatcher.Invoke(()=>{},System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                var source=HwndSource.FromHwnd(handle)!;
                if(source.CompositionTarget.BackgroundColor.A!=0 || !LanDeviceMonitor.Services.MicaWindowService.LastAcrylicApplySucceeded)
                    throw new Exception("还原/拖动结束后透明表面或材质未恢复");
                Console.WriteLine("Native maximize/restore/material PASS: "+monitor.DeviceName);
            }
        }
        finally {window.WindowState=WindowState.Normal;window.Left=-10000;window.Top=-10000;window.Width=1180;window.Height=760;window.Opacity=opacity;}
    }
    private static int Hit(IntPtr window,int x,int y)=>SendMessage(window,0x84,IntPtr.Zero,new IntPtr(unchecked((int)(((uint)y&0xffff)<<16|((uint)x&0xffff))))).ToInt32();
}
