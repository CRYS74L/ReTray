using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace ReTray_App.Controls;

/// <summary>A native WPF selector whose popup stays below the visible selection box.</summary>
public sealed class BelowComboBox : ComboBox
{
    public static readonly DependencyProperty HeaderProperty = DependencyProperty.RegisterAttached(
        nameof(Header), typeof(object), typeof(BelowComboBox), new FrameworkPropertyMetadata(null));

    public object? Header
    {
        get => GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    public static object? GetHeader(DependencyObject element) => element.GetValue(HeaderProperty);
    public static void SetHeader(DependencyObject element, object? value) => element.SetValue(HeaderProperty, value);

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        if (GetTemplateChild("PART_Popup") is not Popup popup) return;
        popup.Placement = PlacementMode.Bottom;
        // ComboBox owns subtree capture and dismisses on outside clicks or
        // capture loss. Giving Popup a second capture owner breaks that flow.
        popup.StaysOpen = true;
        popup.PopupAnimation = PopupAnimation.None;
    }
}
