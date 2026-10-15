using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace LanDeviceMonitor.Controls;

public static class CapsulePressMotion
{
    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached(
        "Enabled", typeof(bool), typeof(CapsulePressMotion), new PropertyMetadata(false, Attach));
    public static readonly DependencyProperty IsPressedProperty = DependencyProperty.RegisterAttached(
        "IsPressed", typeof(bool), typeof(CapsulePressMotion), new PropertyMetadata(false));
    public static bool GetEnabled(DependencyObject value) => (bool)value.GetValue(EnabledProperty);
    public static void SetEnabled(DependencyObject value, bool enabled) => value.SetValue(EnabledProperty, enabled);
    public static bool GetIsPressed(DependencyObject value) => (bool)value.GetValue(IsPressedProperty);
    private static void Attach(DependencyObject value, DependencyPropertyChangedEventArgs args)
    {
        if (value is ListBoxItem item && args.NewValue is true) _ = new State(item);
    }

    private sealed class State
    {
        private readonly ListBoxItem _item;
        private readonly ScaleTransform _scale = new();
        private Window? _window;
        private int _generation;
        internal State(ListBoxItem item)
        {
            _item = item;
            item.Loaded += (_, _) =>
            {
                item.ApplyTemplate();
                if (item.Template.FindName("ItemMotionRoot", item) is FrameworkElement root)
                { root.RenderTransformOrigin = new System.Windows.Point(.5,.5); root.RenderTransform = _scale; }
                var window = Window.GetWindow(item);
                if (_window is not null) _window.RemoveHandler(Mouse.PreviewMouseUpEvent, new MouseButtonEventHandler(Release));
                _window = window;
                _window?.AddHandler(Mouse.PreviewMouseUpEvent, new MouseButtonEventHandler(Release), true);
            };
            item.Unloaded += (_, _) =>
            {
                _window?.RemoveHandler(Mouse.PreviewMouseUpEvent, new MouseButtonEventHandler(Release));
                _window = null; Set(false);
            };
            item.AddHandler(Mouse.PreviewMouseDownEvent, new MouseButtonEventHandler((_, e) => { if(e.ChangedButton == MouseButton.Left) Set(true); }), true);
            item.MouseLeave += (_, _) => Set(false);
            item.LostMouseCapture += (_, _) => Set(false);
            item.PreviewKeyDown += (_, e) => { if(e.Key is Key.Space or Key.Enter) Set(true); };
            item.PreviewKeyUp += (_, e) => { if(e.Key is Key.Space or Key.Enter) Set(false); };
            item.LostKeyboardFocus += (_, _) => Set(false);
        }
        private void Release(object sender, MouseButtonEventArgs args) => Set(false);
        private void Set(bool pressed)
        {
            if (GetIsPressed(_item) == pressed) return;
            _item.SetValue(IsPressedProperty, pressed);
            var generation=++_generation;
            Animate(ScaleTransform.ScaleXProperty, pressed ? Math.Max(.985, 1-3/Math.Max(1,_item.ActualWidth)) : 1, pressed, generation);
            Animate(ScaleTransform.ScaleYProperty, pressed ? .975 : 1, pressed, generation);
        }
        private void Animate(DependencyProperty property, double target, bool pressed, int generation)
        {
            var current=(double)_scale.GetValue(property);
            _scale.BeginAnimation(property,null); _scale.SetValue(property,target);
            if (!SystemParameters.ClientAreaAnimation || !_item.IsLoaded) return;
            var motion = new DoubleAnimation(current,target,TimeSpan.FromMilliseconds(pressed?100:240))
            { EasingFunction=new CubicEase{EasingMode=EasingMode.EaseOut}, FillBehavior=FillBehavior.Stop };
            motion.Completed += (_,_) => { if(generation==_generation) _scale.BeginAnimation(property,null); };
            _scale.BeginAnimation(property,motion);
        }
    }
}
