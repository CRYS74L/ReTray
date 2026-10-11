using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Windows.Foundation;

namespace TrayAssistant_App.Controls;

/// <summary>Synchronizes WinUI's implicit window presenters with the actual native client.</summary>
public sealed class ResizeSynchronization : IDisposable
{
    private const uint WmSize=5,WmWindowPosChanged=0x47,WmDpiChanged=0x2E0,WmExitSizeMove=0x232,WmNcDestroy=0x82;
    private const nuint SubclassId=0x5452535A;
    private readonly Window window;
    private readonly FrameworkElement root;
    private readonly nint handle;
    private readonly SubclassProcedure procedure;
    private readonly double originalWidth,originalHeight;
    private readonly HorizontalAlignment originalHorizontalAlignment;
    private readonly VerticalAlignment originalVerticalAlignment;
    private XamlRoot? xamlRoot;
    private bool disposed,synchronizing;
    private int hostWidthInset,hostHeightInset,lastWidth=-1,lastHeight=-1;
    private uint lastDpi;

    private ResizeSynchronization(Window window,FrameworkElement root)
    {
        this.window=window;this.root=root;handle=WinRT.Interop.WindowNative.GetWindowHandle(window);
        originalWidth=root.Width;originalHeight=root.Height;
        originalHorizontalAlignment=root.HorizontalAlignment;originalVerticalAlignment=root.VerticalAlignment;
        procedure=WindowProcedure;
        if(!SetWindowSubclass(handle,procedure,SubclassId,0))throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        window.SizeChanged+=WindowSizeChanged;window.Closed+=WindowClosed;root.Loaded+=RootLoaded;
        // The window content fills its host. A fixed content size inside an older
        // ClientAreaPresenter gets centered, even if the inner Grid was arranged correctly.
        root.Width=double.NaN;root.Height=double.NaN;
        root.HorizontalAlignment=HorizontalAlignment.Stretch;root.VerticalAlignment=VerticalAlignment.Stretch;
        if(root.IsLoaded)ConnectRoot();
    }
    public static ResizeSynchronization Attach(Window window,FrameworkElement root)=>new(window,root);
    private bool Alive=>!disposed&&IsWindow(handle)&&GetWindowThreadProcessId(handle,out var pid)!=0&&pid==(uint)Environment.ProcessId;
    private void RootLoaded(object sender,RoutedEventArgs args)=>ConnectRoot();
    private void WindowClosed(object sender,WindowEventArgs args)=>Dispose();
    private void WindowSizeChanged(object sender,WindowSizeChangedEventArgs args)=>Synchronize(true);
    private void XamlRootChanged(XamlRoot sender,XamlRootChangedEventArgs args)=>Synchronize(true);
    private void ConnectRoot()
    {
        if(!Alive)return;
        if(!ReferenceEquals(xamlRoot,root.XamlRoot))
        {
            if(xamlRoot!=null)xamlRoot.Changed-=XamlRootChanged;
            xamlRoot=root.XamlRoot;if(xamlRoot!=null)xamlRoot.Changed+=XamlRootChanged;
        }
        // DesktopChildSiteBridge has a one-pixel top inset in the current WinUI host.
        // Measure it from the real HWNDs; do not add a hard-coded DIP compensation.
        var host=FindWindowEx(handle,0,"Microsoft.UI.Content.DesktopChildSiteBridge",null);
        if(host!=0&&GetClientRect(handle,out var client)&&GetClientRect(host,out var hostClient))
        {
            hostWidthInset=Math.Max(0,client.Width-hostClient.Width);
            hostHeightInset=Math.Max(0,client.Height-hostClient.Height);
        }
        Synchronize(true);
    }
    private nint WindowProcedure(nint hwnd,uint message,nuint wparam,nint lparam,nuint id,nuint data)
    {
        // Native geometry proceeds normally. Synchronize after WinUI processes WM_SIZE,
        // rather than predicting geometry or resizing a visible child ahead of its parent.
        var result=DefSubclassProc(hwnd,message,wparam,lparam);
        try
        {
            if(message==WmNcDestroy)Dispose();
            else if((message==WmSize&&wparam!=1)||message==WmDpiChanged||message==WmExitSizeMove)Synchronize(true);
            else if(message==WmWindowPosChanged)Synchronize(false);
        }
        catch(Exception ex){Debug.WriteLine(ex);}
        return result;
    }
    private void Synchronize(bool force)
    {
        if(!Alive||synchronizing||!root.IsLoaded||IsIconic(handle)||!GetClientRect(handle,out var client))return;
        var width=client.Width-hostWidthInset;var height=client.Height-hostHeightInset;
        var dpi=GetDpiForWindow(handle);if(width<=0||height<=0||dpi==0)return;
        var scale=dpi/96.0;var size=new Size(width/scale,height/scale);
        var ancestors=new List<FrameworkElement>();
        DependencyObject? node=root;
        while(node!=null)
        {
            if(node is FrameworkElement element)ancestors.Add(element);
            node=VisualTreeHelper.GetParent(node);
        }
        var top=ancestors[^1];
        // XamlRoot.Content may itself be the implicit ScrollViewer rather than Window.Content.
        if(root.XamlRoot?.Content is FrameworkElement hostRoot&&!ancestors.Contains(hostRoot))
        {
            top=hostRoot;ancestors.Add(hostRoot);
            while(VisualTreeHelper.GetParent(top) is FrameworkElement parent){top=parent;ancestors.Add(parent);}
        }
        if(!force&&lastWidth==width&&lastHeight==height&&lastDpi==dpi&&
            Math.Abs(top.ActualWidth-size.Width)<0.01&&Math.Abs(top.ActualHeight-size.Height)<0.01)return;
        synchronizing=true;
        try
        {
            // Invalidate the actual presenter/viewport chain, then lay it out top-down.
            // ScrollContentPresenter computes its own Clip from this Arrange; never fake
            // a larger Clip or only arrange the application Grid beneath that viewport.
            foreach(var element in ancestors){element.InvalidateMeasure();element.InvalidateArrange();}
            top.Measure(size);top.Arrange(new Rect(0,0,size.Width,size.Height));top.UpdateLayout();
            lastWidth=width;lastHeight=height;lastDpi=dpi;
        }
        finally{synchronizing=false;}
    }
    public void Dispose()
    {
        if(disposed)return;disposed=true;RemoveWindowSubclass(handle,procedure,SubclassId);
        window.SizeChanged-=WindowSizeChanged;window.Closed-=WindowClosed;root.Loaded-=RootLoaded;
        if(xamlRoot!=null)xamlRoot.Changed-=XamlRootChanged;
        root.Width=originalWidth;root.Height=originalHeight;
        root.HorizontalAlignment=originalHorizontalAlignment;root.VerticalAlignment=originalVerticalAlignment;
    }
    [StructLayout(LayoutKind.Sequential)]private struct NativeRect
    {public int Left,Top,Right,Bottom;public readonly int Width=>Right-Left;public readonly int Height=>Bottom-Top;}
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]private delegate nint SubclassProcedure(nint hwnd,uint message,nuint wparam,nint lparam,nuint id,nuint data);
    [DllImport("comctl32.dll",SetLastError=true)]private static extern bool SetWindowSubclass(nint hwnd,SubclassProcedure procedure,nuint id,nuint data);
    [DllImport("comctl32.dll")]private static extern bool RemoveWindowSubclass(nint hwnd,SubclassProcedure procedure,nuint id);
    [DllImport("comctl32.dll")]private static extern nint DefSubclassProc(nint hwnd,uint message,nuint wparam,nint lparam);
    [DllImport("user32.dll")]private static extern bool GetClientRect(nint hwnd,out NativeRect rect);
    [DllImport("user32.dll")]private static extern uint GetDpiForWindow(nint hwnd);
    [DllImport("user32.dll")]private static extern bool IsIconic(nint hwnd);
    [DllImport("user32.dll")]private static extern bool IsWindow(nint hwnd);
    [DllImport("user32.dll")]private static extern uint GetWindowThreadProcessId(nint hwnd,out uint pid);
    [DllImport("user32.dll",EntryPoint="FindWindowExW",CharSet=CharSet.Unicode)]private static extern nint FindWindowEx(nint parent,nint childAfter,string className,string? name);
}
