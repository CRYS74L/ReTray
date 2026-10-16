using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Color = System.Windows.Media.Color;
using Point = System.Windows.Point;
using System.Windows.Media.Animation;
using Control = System.Windows.Controls.Control;
using Border = System.Windows.Controls.Border;
using ComboBox = System.Windows.Controls.ComboBox;
using ComboBoxItem = System.Windows.Controls.ComboBoxItem;
using ToggleButton = System.Windows.Controls.Primitives.ToggleButton;

namespace LanDeviceMonitor.Controls;

/// <summary>State transitions transcribed from UP Studio's final CSS cascade.</summary>
public static class StudioControlMotion
{
    public static readonly DependencyProperty UseSelectorSurfaceProperty = DependencyProperty.RegisterAttached(
        "UseSelectorSurface", typeof(bool), typeof(StudioControlMotion), new PropertyMetadata(false));
    public static bool GetUseSelectorSurface(DependencyObject target) => (bool)target.GetValue(UseSelectorSurfaceProperty);
    public static void SetUseSelectorSurface(DependencyObject target, bool value) => target.SetValue(UseSelectorSurfaceProperty,value);
    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached(
        "Enabled", typeof(bool), typeof(StudioControlMotion), new PropertyMetadata(false, Attach));
    public static bool GetEnabled(DependencyObject value) => (bool)value.GetValue(EnabledProperty);
    public static void SetEnabled(DependencyObject value, bool enabled) => value.SetValue(EnabledProperty, enabled);

    private static void Attach(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        if (sender is not Control control || args.NewValue is not true) return;
        _ = new MotionState(control);
    }

    private sealed class MotionState
    {
        private readonly Control _control;
        private Border? _surface;
        private Border? _halo;
        private readonly SolidColorBrush _background = new();
        private readonly SolidColorBrush _border = new();
        private readonly SolidColorBrush _foreground = new();
        private readonly ScaleTransform _scale = new();
        private readonly Dictionary<(Animatable, DependencyProperty), int> _motionGenerations = new();
        private ComboBox? _owner;
        private System.Windows.Controls.ItemsControl? _itemOwner;
        private DependencyPropertyDescriptor? _highlight;
        private readonly EventHandler _stateChanged;
        private readonly MouseEventHandler _itemPointerMoved;
        private bool _pressed;
        private bool _keyboardFocus;
        private bool IsSelect => _control is ToggleButton;
        private bool IsItem => _control is ComboBoxItem or System.Windows.Controls.MenuItem;
        private bool IsInput => !IsSelect && !IsItem && !GetUseSelectorSurface(_control);

        internal MotionState(Control control)
        {
            _control = control;
            _stateChanged = (_, _) => Update();
            _itemPointerMoved = (_, _) => {
                if (!_keyboardFocus) return;
                _keyboardFocus = false;
                Update();
            };
            control.SizeChanged += (_, _) => { if (IsItem) UpdateCapsuleRadius(); };
            control.Unloaded += (_, _) =>
            {
                Services.ThemeService.PaletteChanged -= PaletteChanged;
                _highlight?.RemoveValueChanged(control, _stateChanged);
                _itemOwner?.RemoveHandler(Mouse.PreviewMouseMoveEvent, _itemPointerMoved);
                if (_owner is not null) { _owner.IsKeyboardFocusWithinChanged -= OwnerFocusChanged; _owner.SelectionChanged -= OwnerSelectionChanged; }
            };
            control.Loaded += (_, _) => Initialize();
            control.MouseEnter += (_, _) => { _keyboardFocus = false; Update(); };
            control.MouseLeave += (_, _) => { _pressed = false; Update(); };
            control.AddHandler(Mouse.PreviewMouseDownEvent, new MouseButtonEventHandler((_, _) => { _pressed = true; _keyboardFocus = false; Update(); }), true);
            control.AddHandler(Mouse.PreviewMouseUpEvent, new MouseButtonEventHandler((_, _) => { _pressed = false; Update(); }), true);
            control.LostMouseCapture += (_, _) => { _pressed = false; Update(); };
            control.IsKeyboardFocusWithinChanged += (_, _) => Update();
            control.PreviewKeyDown += (_, _) => { _keyboardFocus = true; Update(); };
            control.GotKeyboardFocus += (_, _) => { _keyboardFocus = Mouse.LeftButton != MouseButtonState.Pressed; Update(); };
            control.IsEnabledChanged += (_, _) => Update();
            if (control is ToggleButton toggle)
            {
                toggle.Checked += (_, _) => UpdateArrow();
                toggle.Unchecked += (_, _) => UpdateArrow();
            }
            if (control is ComboBoxItem)
            {
                _highlight = DependencyPropertyDescriptor.FromProperty(ComboBoxItem.IsHighlightedProperty, typeof(ComboBoxItem));
                control.AddHandler(System.Windows.Controls.Primitives.Selector.SelectedEvent, new RoutedEventHandler((_, _) => Update()));
                control.AddHandler(System.Windows.Controls.Primitives.Selector.UnselectedEvent, new RoutedEventHandler((_, _) => Update()));
            }
            else if (control is System.Windows.Controls.MenuItem)
                _highlight = DependencyPropertyDescriptor.FromProperty(System.Windows.Controls.MenuItem.IsHighlightedProperty, typeof(System.Windows.Controls.MenuItem));
        }

        private void Initialize()
        {
            Services.ThemeService.PaletteChanged -= PaletteChanged;
            Services.ThemeService.PaletteChanged += PaletteChanged;
            _highlight?.RemoveValueChanged(_control, _stateChanged);
            _highlight?.AddValueChanged(_control, _stateChanged);
            _itemOwner?.RemoveHandler(Mouse.PreviewMouseMoveEvent, _itemPointerMoved);
            _itemOwner = IsItem ? System.Windows.Controls.ItemsControl.ItemsControlFromItemContainer(_control) : null;
            _itemOwner?.AddHandler(Mouse.PreviewMouseMoveEvent, _itemPointerMoved, true);
            if (IsSelect)
            {
                DependencyObject? parent = _control;
                while (parent is not null && parent is not ComboBox) parent = VisualTreeHelper.GetParent(parent);
                _owner = parent as ComboBox;
                if (_owner is not null) { _owner.IsKeyboardFocusWithinChanged -= OwnerFocusChanged; _owner.IsKeyboardFocusWithinChanged += OwnerFocusChanged; _owner.SelectionChanged -= OwnerSelectionChanged; _owner.SelectionChanged += OwnerSelectionChanged; }
            }
            _control.ApplyTemplate();
            if (_control is ComboBoxItem item && System.Windows.Controls.ItemsControl.ItemsControlFromItemContainer(item) is ComboBox combo)
            {
                foreach (var property in new[] { Control.FontFamilyProperty, Control.FontSizeProperty, Control.FontWeightProperty })
                    System.Windows.Data.BindingOperations.SetBinding(item, property,
                        new System.Windows.Data.Binding(property.Name) { Source = combo });
                System.Windows.Data.BindingOperations.SetBinding(item, FrameworkElement.HeightProperty,
                    new System.Windows.Data.Binding(nameof(FrameworkElement.ActualHeight)) { Source = combo });
            }
            _surface = _control.Template.FindName("StudioSurface", _control) as Border;
            _halo = _control.Template.FindName("FocusHalo", _control) as Border;
            if (_surface is null) return;
            _surface.Background = _background;
            if (IsItem)
            {
                var hoverColor = Color("StudioHoverColor");
                _background.Color = System.Windows.Media.Color.FromArgb(0, hoverColor.R, hoverColor.G, hoverColor.B);
            }
            _surface.BorderBrush = _border;
            _control.Foreground = _foreground;
            // Scale the trigger/input only; never scale the popup's coordinate space.
            if (_control.Template.FindName("StudioRoot", _control) is FrameworkElement root)
            {
                root.RenderTransformOrigin = new Point(.5, .5);
                root.RenderTransform = _scale;
            }
            BindText(_control);
            if (IsItem) _control.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(UpdateItemInset));
            Update(false);
        }

        private void UpdateItemInset()
        {
            if (_control is not ComboBoxItem item || System.Windows.Controls.ItemsControl.ItemsControlFromItemContainer(item) is not ComboBox combo) return;
            // Icon menus keep their equal optical inset; plain text menus must
            // use the trigger's actual text inset, including DPI rounding.
            if (ContainsIcon(item)) { item.Padding = new Thickness(9.2,0,9.2,0); return; }
            if (combo.Template.FindName("DropDownToggle",combo) is not ToggleButton toggle ||
                toggle.Template.FindName("StudioRoot",toggle) is not FrameworkElement root) return;
            var label = FindLabel(root);
            if (label is null) return;
            var inset = label.TranslatePoint(new Point(),root).X;
            item.Padding = new Thickness(inset,0,inset,0);
        }

        private static bool ContainsIcon(DependencyObject root)
        {
            if(root is DeviceTypeIcon)return true;
            for(var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)if(ContainsIcon(VisualTreeHelper.GetChild(root,i)))return true;
            return false;
        }

        private static System.Windows.Controls.TextBlock? FindLabel(DependencyObject root)
        {
            if(root is System.Windows.Controls.TextBlock text && !string.IsNullOrWhiteSpace(text.Text))return text;
            for(var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)if(FindLabel(VisualTreeHelper.GetChild(root,i)) is {} found)return found;
            return null;
        }

        private void OwnerSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) =>
            _control.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(() => { if (_control.IsLoaded) BindText(_control); }));

        private void BindText(DependencyObject root)
        {
            for (var i=0; i<VisualTreeHelper.GetChildrenCount(root); i++)
            {
                var child=VisualTreeHelper.GetChild(root,i);
                // Icon templates own their symbol font, size and color. Only synchronize labels.
                if (child is DeviceTypeIcon) continue;
                if (child is System.Windows.Controls.TextBlock text)
                {
                    if (text.Name == "Placeholder") continue;
                    System.Windows.Data.BindingOperations.SetBinding(text, System.Windows.Controls.TextBlock.ForegroundProperty, new System.Windows.Data.Binding("Foreground") { Source=_control });
                    System.Windows.Data.BindingOperations.SetBinding(text, System.Windows.Controls.TextBlock.FontFamilyProperty, new System.Windows.Data.Binding("FontFamily") { Source=_control });
                    System.Windows.Data.BindingOperations.SetBinding(text, System.Windows.Controls.TextBlock.FontSizeProperty, new System.Windows.Data.Binding("FontSize") { Source=_control });
                    System.Windows.Data.BindingOperations.SetBinding(text, System.Windows.Controls.TextBlock.FontWeightProperty, new System.Windows.Data.Binding("FontWeight") { Source=_control });
                }
                BindText(child);
            }
        }

        private void PaletteChanged() => Update(false);
        private void OwnerFocusChanged(object sender, DependencyPropertyChangedEventArgs e) { _keyboardFocus = Mouse.LeftButton != MouseButtonState.Pressed; Update(); }

        private Color Color(string key) => (Color)_control.FindResource(key);

        private void UpdateCapsuleRadius()
        {
            // Popup items can be recycled/reloaded without a new Border size
            // notification. Use the container's final size for every state.
            if(IsItem && _surface is not null)
                _surface.CornerRadius=new CornerRadius(Math.Max(0,_control.ActualHeight/2));
        }

        private void Update(bool animate = true)
        {
            if (_surface is null) return;
            UpdateCapsuleRadius();
            var enabled = _control.IsEnabled;
            var hover = enabled && (_control.IsMouseOver || _control is ComboBoxItem { IsHighlighted: true } or System.Windows.Controls.MenuItem { IsHighlighted: true });
            var focus = enabled && (_owner?.IsKeyboardFocusWithin ?? _control.IsKeyboardFocusWithin);
            var selected = _control is ComboBoxItem { IsSelected: true };
            var pressed = enabled && _pressed;
            var fast = pressed && !IsInput;
            var curve = IsInput ? new KeySpline(.16, 1, .3, 1) : new KeySpline(.18, .75, .25, 1);
            var duration = fast ? 100 : IsInput ? 240 : 560;
            var background = Color(hover && !IsSelect ? "StudioHoverColor" : IsInput ? "StudioInputSurfaceColor" : "StudioSurfaceColor");
            // Colors.Transparent is transparent WHITE. Interpolating a dark
            // hover to it raises RGB while alpha falls, producing a bright
            // flash. Preserve hover RGB and animate only its coverage away.
            if (IsItem && !hover)
            {
                var hoverColor = Color("StudioHoverColor");
                background = System.Windows.Media.Color.FromArgb(0, hoverColor.R, hoverColor.G, hoverColor.B);
            }
            if (IsInput && focus) background = Color("StudioInputSurfaceColor");
            if (IsItem && focus && _keyboardFocus && !hover) background = Color("StudioKeyboardColor");
            var border = Color(IsInput && focus ? "AccentColor" : IsInput && hover ? "StudioHoverBorderColor" : "InputBorderColor");
            AnimateColor(_background, background, duration, curve, animate);
            AnimateColor(_border, border, duration, curve, animate);
            AnimateColor(_foreground, Color(selected ? "StudioSelectedTextColor" : "StudioTextColor"), fast ? 100 : 420, curve, animate);
            _control.Opacity = enabled ? 1 : .45;
            if (_halo is not null) Animate(_halo, UIElement.OpacityProperty, focus ? .17 : 0, 280, curve, animate);
            var scaleX = pressed ? IsInput || IsItem ? .995 : Math.Max(.975, 1 - 3 / Math.Max(1, _control.ActualWidth)) : 1;
            var scaleY = pressed ? IsInput ? .995 : IsItem ? .985 : .975 : 1;
            Animate(_scale, ScaleTransform.ScaleXProperty, scaleX, fast ? 100 : IsInput ? 280 : 360, curve, animate);
            Animate(_scale, ScaleTransform.ScaleYProperty, scaleY, fast ? 100 : IsInput ? 280 : 360, curve, animate);
        }

        private void UpdateArrow()
        {
            if (_control.Template.FindName("StudioArrow", _control) is FrameworkElement arrow && arrow.RenderTransform is RotateTransform rotation)
            {
                if (rotation.IsFrozen) arrow.RenderTransform = rotation = rotation.Clone();
                Animate(rotation, RotateTransform.AngleProperty, ((ToggleButton)_control).IsChecked == true ? 180 : 0, 180, new KeySpline(.25, .1, .25, 1), true);
            }
        }

        private static void AnimateColor(SolidColorBrush brush, Color value, int milliseconds, KeySpline curve, bool animate)
        {
            var current = brush.Color;
            brush.BeginAnimation(SolidColorBrush.ColorProperty, null);
            brush.Color = value;
            var duration = Duration(milliseconds, animate);
            if (duration == TimeSpan.Zero) return;
            var animation = new ColorAnimationUsingKeyFrames { FillBehavior = FillBehavior.Stop };
            animation.KeyFrames.Add(new DiscreteColorKeyFrame(current, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            animation.KeyFrames.Add(new SplineColorKeyFrame(value, KeyTime.FromTimeSpan(duration), curve));
            brush.BeginAnimation(SolidColorBrush.ColorProperty, animation, HandoffBehavior.SnapshotAndReplace);
        }

        private void Animate(Animatable target, DependencyProperty property, double value, int milliseconds, KeySpline curve, bool animate)
        {
            var current = (double)target.GetValue(property);
            var key = (target, property);
            var generation = _motionGenerations.GetValueOrDefault(key) + 1;
            _motionGenerations[key] = generation;
            target.BeginAnimation(property, null);
            target.SetValue(property, value);
            var duration = Duration(milliseconds, animate);
            if (duration == TimeSpan.Zero || Math.Abs(current - value) < .000001) return;
            var animation = new DoubleAnimationUsingKeyFrames { FillBehavior = FillBehavior.Stop };
            animation.KeyFrames.Add(new DiscreteDoubleKeyFrame(current, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            animation.KeyFrames.Add(new SplineDoubleKeyFrame(value, KeyTime.FromTimeSpan(duration), curve));
            animation.Completed += (_, _) =>
            {
                // Keep interrupted motions continuous, but release the finished clock so
                // resting text can use the stationary rasterization path again.
                if (_motionGenerations[key] == generation) target.BeginAnimation(property, null);
            };
            target.BeginAnimation(property, animation, HandoffBehavior.SnapshotAndReplace);
        }

        private static void Animate(UIElement target, DependencyProperty property, double value, int milliseconds, KeySpline curve, bool animate)
        {
            var animation = new DoubleAnimationUsingKeyFrames();
            animation.KeyFrames.Add(new SplineDoubleKeyFrame(value, KeyTime.FromTimeSpan(Duration(milliseconds, animate)), curve));
            target.BeginAnimation(property, animation, HandoffBehavior.SnapshotAndReplace);
        }

        private static TimeSpan Duration(int milliseconds, bool animate) =>
            animate && SystemParameters.ClientAreaAnimation ? TimeSpan.FromMilliseconds(milliseconds) : TimeSpan.Zero;
    }
}
