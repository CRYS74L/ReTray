using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace TrayAssistant_App.Controls;

public sealed class TrayIconActionEventArgs(string action, bool keyboard, int x, int y) : EventArgs
{
    public string Action { get; } = action;
    public bool Keyboard { get; } = keyboard;
    public int X { get; } = x;
    public int Y { get; } = y;
}

public sealed class TrayIconButton : Button
{
    public static readonly DependencyProperty ImageSourceProperty = DependencyProperty.Register(nameof(ImageSource), typeof(ImageSource), typeof(TrayIconButton));
    public ImageSource? ImageSource { get => (ImageSource?)GetValue(ImageSourceProperty); set => SetValue(ImageSourceProperty, value); }
    public event EventHandler<TrayIconActionEventArgs>? NativeAction;
    private object? pressedTag, lastTag;
    private int pointerButton, lastButton, lastX, lastY;
    private long lastClick;
    public TrayIconButton()
    {
        SetResourceReference(StyleProperty, "SidebarIconButtonStyle");
        var image = new Image { Width = 20, Height = 20, Stretch = Stretch.Uniform };
        image.SetBinding(Image.SourceProperty, new System.Windows.Data.Binding(nameof(ImageSource)) { Source = this });
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
        Content = image;
    }
    protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs args)
    {
        pressedTag = Tag; pointerButton = 1;
        base.OnPreviewMouseLeftButtonDown(args);
    }
    protected override void OnClick()
    {
        base.OnClick();
        var pointer = pointerButton == 1;
        pointerButton = 0;
        if (pointer && !ReferenceEquals(pressedTag, Tag)) return;
        Invoke(1, !pointer);
    }
    protected override void OnPreviewMouseRightButtonDown(MouseButtonEventArgs args)
    {
        pressedTag = Tag; pointerButton = 2; Focus(); CaptureMouse(); args.Handled = true;
    }
    protected override void OnPreviewMouseRightButtonUp(MouseButtonEventArgs args)
    {
        if (pointerButton != 2) return;
        var point = args.GetPosition(this); var tag = pressedTag;
        pointerButton = 0; ReleaseMouseCapture(); args.Handled = true;
        if (ReferenceEquals(tag, Tag) && point.X >= 0 && point.Y >= 0 && point.X < ActualWidth && point.Y < ActualHeight) Invoke(2, false);
    }
    protected override void OnLostMouseCapture(MouseEventArgs args)
    {
        base.OnLostMouseCapture(args);
        Dispatcher.BeginInvoke(() => { if (!IsMouseCaptured) pointerButton = 0; });
    }
    protected override void OnPreviewKeyDown(KeyEventArgs args)
    {
        if (args.Key == Key.Apps || (args.Key == Key.F10 && Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)))
        {
            Invoke(2, true); args.Handled = true; return;
        }
        base.OnPreviewKeyDown(args);
    }
    private void Invoke(int button, bool keyboard)
    {
        GetCursorPos(out var point); var now = Environment.TickCount64;
        var twice = !keyboard && ReferenceEquals(lastTag, Tag) && lastButton == button && now - lastClick <= GetDoubleClickTime() && Math.Abs(point.X - lastX) <= GetSystemMetrics(36) && Math.Abs(point.Y - lastY) <= GetSystemMetrics(37);
        if (!keyboard) { lastTag = Tag; lastButton = button; lastClick = twice ? 0 : now; lastX = point.X; lastY = point.Y; }
        NativeAction?.Invoke(this, new(button == 1 ? twice ? "double" : "click" : twice ? "rightDouble" : "menu", keyboard, point.X, point.Y));
    }
    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X, Y; }
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out NativePoint point);
    [DllImport("user32.dll")] private static extern uint GetDoubleClickTime();
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
}
