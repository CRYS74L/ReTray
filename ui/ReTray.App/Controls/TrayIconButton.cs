using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System.Runtime.InteropServices;
using Microsoft.UI.Input;
using Windows.System;
namespace ReTray_App.Controls;
public sealed class TrayIconActionEventArgs(string action,bool keyboard,int x,int y) : EventArgs
{
    public string Action { get; }=action;
    public bool Keyboard { get; }=keyboard;
    public int X { get; }=x;
    public int Y { get; }=y;
}
public sealed partial class TrayIconButton : Control
{
    public static readonly DependencyProperty ImageSourceProperty=DependencyProperty.Register(nameof(ImageSource),typeof(ImageSource),typeof(TrayIconButton),new PropertyMetadata(null));
    public ImageSource? ImageSource { get=>(ImageSource?)GetValue(ImageSourceProperty);set=>SetValue(ImageSourceProperty,value); }
    public event EventHandler<TrayIconActionEventArgs>? NativeAction;
    private uint pointer;
    private int pressed,lastButton,lastX,lastY;
    private object? pressedTag,lastTag;
    private long lastClick;
    private bool nonMouse;
    [StructLayout(LayoutKind.Sequential)]private struct Point{public int X,Y;}
    [DllImport("user32.dll")]private static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")]private static extern uint GetDoubleClickTime();
    [DllImport("user32.dll")]private static extern int GetSystemMetrics(int index);
    public TrayIconButton(){DefaultStyleKey=typeof(TrayIconButton);IsTabStop=true;UseSystemFocusVisuals=true;}
    protected override void OnPointerEntered(PointerRoutedEventArgs e){base.OnPointerEntered(e);VisualStateManager.GoToState(this,"PointerOver",true);}
    protected override void OnPointerExited(PointerRoutedEventArgs e){base.OnPointerExited(e);VisualStateManager.GoToState(this,"Normal",true);}
    protected override void OnPointerPressed(PointerRoutedEventArgs e)
    {
        base.OnPointerPressed(e);var point=e.GetCurrentPoint(this);pressed=point.Properties.IsRightButtonPressed?2:point.Properties.IsLeftButtonPressed||e.Pointer.PointerDeviceType!=PointerDeviceType.Mouse?1:0;
        if(pressed==0)return;pointer=e.Pointer.PointerId;pressedTag=Tag;nonMouse=e.Pointer.PointerDeviceType!=PointerDeviceType.Mouse;
        Focus(FocusState.Pointer);CapturePointer(e.Pointer);VisualStateManager.GoToState(this,"Pressed",true);e.Handled=true;
    }
    protected override void OnPointerReleased(PointerRoutedEventArgs e)
    {
        base.OnPointerReleased(e);if(pointer!=e.Pointer.PointerId||pressed==0)return;
        var local=e.GetCurrentPoint(this).Position;int button=pressed;var tag=pressedTag;pressed=0;ReleasePointerCapture(e.Pointer);VisualStateManager.GoToState(this,"Normal",true);e.Handled=true;
        if(!ReferenceEquals(Tag,tag)||local.X<0||local.Y<0||local.X>ActualWidth||local.Y>ActualHeight)return;
        GetCursorPos(out var screen);long now=Environment.TickCount64;
        bool twice=ReferenceEquals(lastTag,Tag)&&lastButton==button&&now-lastClick<=GetDoubleClickTime()&&Math.Abs(screen.X-lastX)<=GetSystemMetrics(36)&&Math.Abs(screen.Y-lastY)<=GetSystemMetrics(37);
        lastClick=twice?0:now;lastButton=button;lastTag=Tag;lastX=screen.X;lastY=screen.Y;
        NativeAction?.Invoke(this,new(button==2?(twice?"rightDouble":"menu"):(twice?"double":"click"),nonMouse,screen.X,screen.Y));
    }
    protected override void OnPointerCaptureLost(PointerRoutedEventArgs e){base.OnPointerCaptureLost(e);pressed=0;VisualStateManager.GoToState(this,"Normal",true);}
    protected override void OnKeyDown(KeyRoutedEventArgs e)
    {
        base.OnKeyDown(e);if(e.Key is VirtualKey.Enter or VirtualKey.Space){InvokeKeyboard("click");e.Handled=true;}
        else if(e.Key==VirtualKey.Application){InvokeKeyboard("menu");e.Handled=true;}
    }
    internal void InvokeKeyboard(string action){GetCursorPos(out var point);NativeAction?.Invoke(this,new(action,true,point.X,point.Y));}
    protected override AutomationPeer OnCreateAutomationPeer()=>new IconPeer(this);
    private sealed partial class IconPeer(TrayIconButton owner) : FrameworkElementAutomationPeer(owner),IInvokeProvider
    {
        protected override string GetClassNameCore()=>nameof(TrayIconButton);
        protected override AutomationControlType GetAutomationControlTypeCore()=>AutomationControlType.Button;
        protected override object GetPatternCore(PatternInterface pattern)=>pattern==PatternInterface.Invoke?this:base.GetPatternCore(pattern);
        public void Invoke()=>owner.DispatcherQueue.TryEnqueue(()=>owner.InvokeKeyboard("click"));
    }
}

