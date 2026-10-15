using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Markup;
using System.Collections.ObjectModel;

namespace ReTray_App.Controls;

[ContentProperty(Name = nameof(Items))]
public sealed partial class BelowComboBox : Control
{
    public static readonly DependencyProperty HeaderProperty = DependencyProperty.Register(nameof(Header), typeof(object), typeof(BelowComboBox), new PropertyMetadata(null, Changed));
    public static readonly DependencyProperty SelectedIndexProperty = DependencyProperty.Register(nameof(SelectedIndex), typeof(int), typeof(BelowComboBox), new PropertyMetadata(-1, Changed));
    public object? Header { get => GetValue(HeaderProperty); set => SetValue(HeaderProperty, value); }
    public int SelectedIndex { get => (int)GetValue(SelectedIndexProperty); set => SetValue(SelectedIndexProperty, value); }
    public ObservableCollection<object> Items { get; } = new();
    private DropDownButton? button;
    private ContentPresenter? header;
    private readonly MenuFlyout menu = new() { Placement = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.BottomEdgeAlignedLeft };
    private bool expanded;
    private string SelectedText => SelectedIndex >= 0 && SelectedIndex < Items.Count ? Items[SelectedIndex]?.ToString() ?? "" : "";

    public BelowComboBox()
    {
        DefaultStyleKey = typeof(BelowComboBox);
        IsTabStop = false;
        Items.CollectionChanged += (_, _) => UpdateSelection();
        menu.Opening += (_, _) => PopulateMenu();
        menu.Opened += (_, _) => SetExpanded(true);
        menu.Closed += (_, _) => SetExpanded(false);
    }
    private static void Changed(DependencyObject sender, DependencyPropertyChangedEventArgs args) => ((BelowComboBox)sender).UpdateSelection();
    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        button = GetTemplateChild("SelectionButton") as DropDownButton;
        header = GetTemplateChild("HeaderPresenter") as ContentPresenter;
        if (button != null) button.Flyout = menu;
        UpdateSelection();
    }
    private void UpdateSelection()
    {
        if (header != null) header.Visibility = Header == null ? Visibility.Collapsed : Visibility.Visible;
        if (button == null) return;
        var prior = button.Content?.ToString() ?? "";
        button.Content = SelectedText;
        AutomationProperties.SetName(button, Header?.ToString() ?? AutomationProperties.GetName(this));
        if (FrameworkElementAutomationPeer.FromElement(this) is SelectorPeer peer && prior != SelectedText)
            peer.RaisePropertyChangedEvent(ValuePatternIdentifiers.ValueProperty, prior, SelectedText);
    }
    private void PopulateMenu()
    {
        menu.Items.Clear();
        for (var index = 0; index < Items.Count; index++)
        {
            var captured = index;
            var item = new RadioMenuFlyoutItem { Text = Items[index]?.ToString() ?? "", IsChecked = SelectedIndex == index, FontFamily = (Microsoft.UI.Xaml.Media.FontFamily)Application.Current.Resources["AppUiFontFamily"], FontSize = FontSize };
            item.Click += (_, _) => { SelectedIndex = captured; menu.Hide(); };
            menu.Items.Add(item);
        }
        var style = new Style(typeof(MenuFlyoutPresenter)) { BasedOn = (Style)Application.Current.Resources["MaterialSelectionFlyoutStyle"] };
        style.Setters.Add(new Setter(FrameworkElement.MinWidthProperty, button?.ActualWidth ?? ActualWidth));
        style.Setters.Add(new Setter(FrameworkElement.RequestedThemeProperty, ActualTheme));
        style.Setters.Add(new Setter(MenuFlyoutPresenter.SystemBackdropProperty, new SoftMicaBackdrop { InitialTheme = ActualTheme }));
        menu.MenuFlyoutPresenterStyle = style;
    }
    private void SetExpanded(bool value)
    {
        expanded = value;
        if (FrameworkElementAutomationPeer.FromElement(this) is SelectorPeer peer)
            peer.RaisePropertyChangedEvent(ExpandCollapsePatternIdentifiers.ExpandCollapseStateProperty,
                value ? ExpandCollapseState.Collapsed : ExpandCollapseState.Expanded,
                value ? ExpandCollapseState.Expanded : ExpandCollapseState.Collapsed);
    }
    private void OpenMenu()
    {
        if (button != null) menu.ShowAt(button);
    }
    protected override void OnKeyDown(KeyRoutedEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == global::Windows.System.VirtualKey.F4)
        {
            if (expanded) menu.Hide(); else OpenMenu();
            e.Handled = true;
        }
    }
    protected override AutomationPeer OnCreateAutomationPeer() => new SelectorPeer(this);
    private sealed partial class SelectorPeer(BelowComboBox owner) : FrameworkElementAutomationPeer(owner), IExpandCollapseProvider, IValueProvider
    {
        protected override string GetClassNameCore() => nameof(BelowComboBox);
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.ComboBox;
        protected override string GetNameCore() => base.GetNameCore() is { Length: > 0 } name ? name : owner.Header?.ToString() ?? "";
        protected override void SetFocusCore() => owner.button?.Focus(FocusState.Keyboard);
        protected override object GetPatternCore(PatternInterface pattern) => pattern is PatternInterface.ExpandCollapse or PatternInterface.Value ? this : base.GetPatternCore(pattern);
        public ExpandCollapseState ExpandCollapseState => owner.expanded ? ExpandCollapseState.Expanded : ExpandCollapseState.Collapsed;
        public void Expand() => owner.DispatcherQueue.TryEnqueue(owner.OpenMenu);
        public void Collapse() => owner.DispatcherQueue.TryEnqueue(owner.menu.Hide);
        public string Value => owner.SelectedText;
        public bool IsReadOnly => true;
        public void SetValue(string value) => throw new InvalidOperationException("Use the selection menu.");
    }
}
