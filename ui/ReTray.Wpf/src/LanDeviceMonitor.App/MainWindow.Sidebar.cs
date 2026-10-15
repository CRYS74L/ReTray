using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace LanDeviceMonitor;

public partial class MainWindow
{
    internal const double CollapsedSidebarWidth = 56;
    private double CollapsedSidebarPixelWidth => Math.Ceiling(CollapsedSidebarWidth * System.Windows.Media.VisualTreeHelper.GetDpi(this).DpiScaleX) / System.Windows.Media.VisualTreeHelper.GetDpi(this).DpiScaleX;
    private double _expandedSidebarWidth = 390;
    private bool _sidebarCollapsed;
    private bool _compactMode;
    private bool _compactDetail;
    private bool _sidebarAnimating;
    private bool _hasRenderedInitialContent;
    private int _sidebarAnimationGeneration;
    private System.Windows.Threading.DispatcherOperation? _responsiveModeOperation;
    private static readonly DependencyProperty SidebarAnimationWidthProperty = DependencyProperty.Register(
        "SidebarAnimationWidth", typeof(double), typeof(MainWindow), new PropertyMetadata(390d, (owner, args) =>
        {
            var window = (MainWindow)owner;
            if (window.SidebarColumn is null) return;
            var width = Math.Max(window.CollapsedSidebarPixelWidth, (double)args.NewValue);
            window.SidebarColumn.Width = new GridLength(width);
            window.SidebarDividerColumn.Width = new GridLength(8);
        }));

    private double MaximumSidebarWidth
    {
        get
        {
            var maximum = Math.Max(280, Math.Min(520, WorkspaceGrid.ActualWidth - 606));
            var scale = System.Windows.Media.VisualTreeHelper.GetDpi(WorkspaceGrid).DpiScaleX;
            // A maximum must fit on the pixel grid too. At 125%, for example,
            // 310 DIP rounds to 310.4 DIP, exceeding MaxWidth=310. Grid then
            // repeatedly remeasures the same subtree during native resizing.
            return Math.Max(Math.Ceiling(280 * scale) / scale, Math.Floor(maximum * scale) / scale);
        }
    }

    protected override void OnDpiChanged(System.Windows.DpiScale oldDpi, System.Windows.DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        // Wait until the new scale has propagated through the visual tree.
        _ = Dispatcher.BeginInvoke(new Action(() =>
        {
            if (!_isExiting && SidebarColumn is not null && !_compactMode)
                ApplySidebarState(false);
        }), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void WorkspaceGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (SidebarColumn is null || SidebarExpandedContent is null || WorkspaceGrid.ActualWidth <= 0) return;
        if (ConnectionStateInfoBlock is not null)
            ConnectionStateInfoBlock.Visibility = e.NewSize.Height >= e.NewSize.Width
                ? Visibility.Collapsed : Visibility.Visible;
        UpdateCompactBackButtonPlacement();
        if (!e.WidthChanged) return;
        var compact = WorkspaceGrid.ActualWidth < 900;
        if (compact != _compactMode)
        {
            // Native placement can restore the final size after Loaded but
            // before the first frame. Resolve that startup layout immediately
            // so a portrait window never presents the initial two-column grid.
            if (!_hasRenderedInitialContent)
            {
                ApplyResponsiveMode();
                return;
            }
            // Finish this layout pass and coalesce to the latest width, but
            // switch before pending input. Background work is starved during
            // live sizing: it leaves narrow windows split into two columns and
            // widened windows showing the old compact list with a blank tail.
            if (_responsiveModeOperation is null)
                _responsiveModeOperation = Dispatcher.BeginInvoke(new Action(() =>
                {
                    _responsiveModeOperation = null;
                    if (!_isExiting) ApplyResponsiveMode();
                }), System.Windows.Threading.DispatcherPriority.Render);
            return;
        }
        UpdateResponsiveSidebarWidth(e);
    }

    private void ApplyResponsiveMode()
    {
        var compact = WorkspaceGrid.ActualWidth < 900;
        if (compact != _compactMode)
        {
            ++_compactAnimationGeneration;
            foreach(var surface in new FrameworkElement[]{DeviceSidebar,DetailSurface})
            { surface.BeginAnimation(OpacityProperty,null);surface.Opacity=1;surface.RenderTransform=System.Windows.Media.Transform.Identity;surface.IsHitTestVisible=true; }
            _compactMode = compact;
            _compactDetail = false;
            ++_sidebarAnimationGeneration;
            BeginAnimation(SidebarAnimationWidthProperty, null);
            _sidebarAnimating = false;
            if (!compact) ApplySidebarState(false);
            ApplyCompactLayout();
        }
    }

    private void UpdateResponsiveSidebarWidth(SizeChangedEventArgs e)
    {
        if (_compactMode)
        {
            SidebarExpandedContent.Width = Math.Max(0, WorkspaceGrid.ActualWidth - 54);
            return;
        }
        if (_sidebarAnimating)
        {
            var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(this);
            // Rounding can move the layout edge by one physical pixel while
            // focus visuals change. It is not a user window resize.
            if (Math.Abs(e.NewSize.Width-e.PreviousSize.Width)*dpi.DpiScaleX>1.1 ||
                Math.Abs(e.NewSize.Height-e.PreviousSize.Height)*dpi.DpiScaleY>1.1) ApplySidebarState(false);
            return;
        }
        if (_sidebarCollapsed) return;
        SidebarColumn.MaxWidth = MaximumSidebarWidth;
        SidebarColumn.Width = new GridLength(Math.Clamp(_expandedSidebarWidth,280,MaximumSidebarWidth));
    }

    private void DeviceSidebar_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!_compactMode && !_sidebarAnimating && !_sidebarCollapsed && SidebarExpandedContent is not null)
            SidebarExpandedContent.Width = Math.Max(0, DeviceSidebar.ActualWidth - 24);
    }

    private void SidebarSplitter_DragCompleted(object sender, DragCompletedEventArgs e) =>
        _expandedSidebarWidth = SidebarColumn.ActualWidth;

    private void SidebarToggleButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_sidebarCollapsed && !_sidebarAnimating) _expandedSidebarWidth = SidebarColumn.ActualWidth;
        _sidebarCollapsed = !_sidebarCollapsed;
        ApplySidebarState(SystemParameters.ClientAreaAnimation && IsLoaded);
    }

    private void ApplySidebarState(bool animate)
    {
        if (_compactMode) { ApplyCompactLayout(); return; }
        var currentWidth = _sidebarAnimating ? (double)GetValue(SidebarAnimationWidthProperty) : SidebarColumn.ActualWidth;
        var currentOpacity = SidebarExpandedContent.Opacity;
        var collapsedOpacity = CollapsedSidebarButton.Opacity;
        var targetWidth = _sidebarCollapsed ? CollapsedSidebarPixelWidth : Math.Clamp(_expandedSidebarWidth, 280, MaximumSidebarWidth);
        var generation = ++_sidebarAnimationGeneration;
        BeginAnimation(SidebarAnimationWidthProperty, null);
        SidebarExpandedContent.BeginAnimation(OpacityProperty, null);
        CollapsedSidebarButton.BeginAnimation(OpacityProperty, null);
        _sidebarAnimating = animate && Math.Abs(currentWidth - targetWidth) > 0.1;
        SidebarColumn.MinWidth = CollapsedSidebarPixelWidth;
        SidebarColumn.MaxWidth = MaximumSidebarWidth;
        DeviceSidebar.Visibility = Visibility.Visible;
        SidebarExpandedContent.Visibility = Visibility.Visible;
        CollapsedSidebarButton.Visibility = Visibility.Visible;
        CollapsedSidebarContent.Visibility = Visibility.Visible;
        CollapsedSidebarButton.IsHitTestVisible = _sidebarCollapsed;
        CancelCollapsedHint();
        SidebarSplitter.Visibility = Visibility.Visible;
        SidebarSplitter.IsEnabled = !_sidebarAnimating && !_sidebarCollapsed;
        SidebarExpandedContent.IsHitTestVisible = !_sidebarCollapsed;
        SidebarExpandedContent.Width = Math.Max(0, Math.Clamp(_expandedSidebarWidth, 280, MaximumSidebarWidth) - 36);
        SidebarToggleButton.ToolTip = "收起侧栏";
        System.Windows.Automation.AutomationProperties.SetName(SidebarToggleButton, "收起侧栏");

        void Finish()
        {
            if (generation != _sidebarAnimationGeneration) return;
            BeginAnimation(SidebarAnimationWidthProperty, null);
            SetValue(SidebarAnimationWidthProperty, targetWidth);
            SidebarColumn.Width = new GridLength(targetWidth);
            SidebarDividerColumn.Width = new GridLength(8);
            SidebarExpandedContent.BeginAnimation(OpacityProperty, null);
            SidebarExpandedContent.Opacity = _sidebarCollapsed ? 0 : 1;
            CollapsedSidebarButton.BeginAnimation(OpacityProperty, null);
            CollapsedSidebarButton.Opacity = _sidebarCollapsed ? 1 : 0;
            _sidebarAnimating = false;
            SidebarExpandedContent.Visibility = _sidebarCollapsed ? Visibility.Collapsed : Visibility.Visible;
            CollapsedSidebarButton.Visibility = _sidebarCollapsed ? Visibility.Visible : Visibility.Collapsed;
            CollapsedSidebarContent.Visibility = _sidebarCollapsed ? Visibility.Visible : Visibility.Collapsed;
            SidebarSplitter.Visibility = _sidebarCollapsed ? Visibility.Hidden : Visibility.Visible;
            SidebarColumn.MinWidth = _sidebarCollapsed ? CollapsedSidebarPixelWidth : 280;
            SidebarColumn.MaxWidth = _sidebarCollapsed ? CollapsedSidebarPixelWidth : MaximumSidebarWidth;
            if (_sidebarCollapsed && SidebarExpandedContent.IsKeyboardFocusWithin) CollapsedSidebarButton.Focus();
            if (!_sidebarCollapsed && CollapsedSidebarButton.IsKeyboardFocusWithin) SidebarToggleButton.Focus();
            SidebarSplitter.IsEnabled = !_sidebarCollapsed;
            if (!_sidebarCollapsed) SidebarExpandedContent.Width = Math.Max(0, targetWidth - 36);
        }

        if (!_sidebarAnimating) { Finish(); return; }
        SetValue(SidebarAnimationWidthProperty, currentWidth);
        SidebarExpandedContent.Opacity = currentOpacity;
        CollapsedSidebarButton.Opacity = collapsedOpacity;
        var widthAnimation = new System.Windows.Media.Animation.DoubleAnimation(currentWidth, targetWidth, TimeSpan.FromMilliseconds(260))
        { EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut } };
        widthAnimation.Completed += (_, _) => Finish();
        BeginAnimation(SidebarAnimationWidthProperty, widthAnimation);
        SidebarExpandedContent.BeginAnimation(OpacityProperty,
            new System.Windows.Media.Animation.DoubleAnimation(currentOpacity, _sidebarCollapsed ? 0 : 1, TimeSpan.FromMilliseconds(160))
            { BeginTime = TimeSpan.FromMilliseconds(_sidebarCollapsed ? 0 : 60) });
        CollapsedSidebarButton.BeginAnimation(OpacityProperty,
            new System.Windows.Media.Animation.DoubleAnimation(collapsedOpacity, _sidebarCollapsed ? 1 : 0, TimeSpan.FromMilliseconds(120))
            { BeginTime = TimeSpan.FromMilliseconds(_sidebarCollapsed ? 100 : 0) });
    }

    private void CollapsedSidebarButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_sidebarCollapsed) return;
        _sidebarCollapsed = false;
        ApplySidebarState(SystemParameters.ClientAreaAnimation && IsLoaded);
    }

    private void DeviceSidebar_MouseLeftButtonUp(object sender,System.Windows.Input.MouseButtonEventArgs e)
    {
        if(!_sidebarCollapsed || _compactMode || e.OriginalSource is not DependencyObject source ||
            FindVisualParent<System.Windows.Controls.Primitives.ButtonBase>(source) is not null) return;
        CollapsedSidebarButton_Click(sender,e);
        e.Handled=true;
    }

    private void CancelCollapsedHint()
    {
        CollapsedSidebarGlyph.Opacity = 1;
        CollapsedSidebarKeyboardRing.Visibility = Visibility.Collapsed;
    }

    private void CollapsedSidebar_GotKeyboardFocus(object sender, System.Windows.Input.KeyboardFocusChangedEventArgs e)
    {
        CollapsedSidebarKeyboardRing.Visibility = System.Windows.Input.InputManager.Current.MostRecentInputDevice is System.Windows.Input.KeyboardDevice
            ? Visibility.Visible : Visibility.Collapsed;
    }

    private void CollapsedSidebar_LostKeyboardFocus(object sender, System.Windows.Input.KeyboardFocusChangedEventArgs e) =>
        CollapsedSidebarKeyboardRing.Visibility = Visibility.Collapsed;

    private void DeviceFilterSegments_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        // Match the inset marked in the reference: 12 DIP for a 40 DIP filter.
        if (ShellIdentityRow is null || ShellAppIcon is null) return;
        ShellIdentityRow.Margin = new Thickness(e.NewSize.Height * 0.3, 0, 14, 0);
        ShellIdentityRow.ColumnDefinitions[0].Width = new GridLength(32);
        ShellAppIcon.Width = ShellAppIcon.Height = 30;
    }


    private void UpdateSidebarNavigation()
    {
        foreach (var button in new[] { SidebarRailRouterButton, SidebarRailSettingsButton, CollapsedRouterButton, CollapsedSettingsButton })
            button.ClearValue(System.Windows.Controls.Control.BackgroundProperty);
        var selected = _showingSettings ? SidebarRailSettingsButton : _showingRouter ? SidebarRailRouterButton : null;
        selected?.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty, "QuietControlBrush");
        var collapsedSelected = _showingSettings ? CollapsedSettingsButton : _showingRouter ? CollapsedRouterButton : null;
        collapsedSelected?.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty, "QuietControlBrush");
    }

    private void OpenCompactDetail()
    {
        if (!_compactMode || _compactDetail) return;
        TransitionCompact(true);
    }

    private void DeviceList_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (_compactMode && e.Key == System.Windows.Input.Key.Enter && DeviceList.SelectedItem is not null)
        {
            ShowDevicesPanel(animate:false);
            e.Handled=true;
        }
    }

    private void DoNotDisturbTimeRow_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (DoNotDisturbEndBox is null) return;
        var stacked = e.NewSize.Width < 430;
        if (DoNotDisturbTimeRow.RowDefinitions.Count == 0)
        {
            DoNotDisturbTimeRow.RowDefinitions.Add(new RowDefinition { Height=GridLength.Auto });
            DoNotDisturbTimeRow.RowDefinitions.Add(new RowDefinition { Height=GridLength.Auto });
        }
        Grid.SetColumnSpan(DoNotDisturbTimeLabel,stacked ? 4 : 1);
        DoNotDisturbTimeLabel.Margin = stacked ? new Thickness(0,0,0,10) : new Thickness(0);
        foreach(var element in new FrameworkElement[] { DoNotDisturbStartBox,DoNotDisturbTimeSeparator,DoNotDisturbEndBox })
            Grid.SetRow(element,stacked ? 1 : 0);
    }

    private void CompactBackButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_compactMode || !_compactDetail) return;
        TransitionCompact(false);
    }

    private void UpdateCompactBackButtonPlacement()
    {
        if (CompactBackButton is null || CaptionBackButton is null || WorkspaceGrid is null) return;
        var available = _compactMode && _compactDetail;
        var portrait = WorkspaceGrid.ActualHeight >= WorkspaceGrid.ActualWidth;
        CaptionBackButton.IsNavigationAvailable = available && portrait;
        CompactBackButton.Visibility = available && !portrait ? Visibility.Visible : Visibility.Collapsed;
    }

    private int _compactAnimationGeneration;
    private void TransitionCompact(bool detail)
    {
        var outgoing=_compactDetail ? (FrameworkElement)DetailSurface : DeviceSidebar;
        var incoming=detail ? (FrameworkElement)DetailSurface : DeviceSidebar;
        var generation=++_compactAnimationGeneration;
        _compactDetail=detail;
        UpdateCompactBackButtonPlacement();
        foreach(var surface in new FrameworkElement[]{DeviceSidebar,DetailSurface})
        { surface.BeginAnimation(OpacityProperty,null);surface.Opacity=1;surface.RenderTransform=System.Windows.Media.Transform.Identity;surface.IsHitTestVisible=true; }
        if(!_compactMode || !SystemParameters.ClientAreaAnimation || !IsLoaded) { ApplyCompactLayout();return; }
        outgoing.IsHitTestVisible=false;
        var timing=GetSynchronizedDeviceFilterAnimationTiming();
        var leave=new System.Windows.Media.TranslateTransform();
        var exitScale=new System.Windows.Media.ScaleTransform(1,1);
        var exitTransform=new System.Windows.Media.TransformGroup();exitTransform.Children.Add(exitScale);exitTransform.Children.Add(leave);
        outgoing.RenderTransformOrigin=new System.Windows.Point(.5,.5);outgoing.RenderTransform=exitTransform;
        var exitEase=new System.Windows.Media.Animation.CubicEase {EasingMode=System.Windows.Media.Animation.EasingMode.EaseInOut};
        var fade=new System.Windows.Media.Animation.DoubleAnimation(1,0,TimeSpan.FromMilliseconds(timing.DetailExitDurationMilliseconds)){EasingFunction=exitEase};
        fade.Completed+=(_,_)=>
        {
            if(generation!=_compactAnimationGeneration || !_compactMode) return;
            outgoing.BeginAnimation(OpacityProperty,null);outgoing.Opacity=1;outgoing.RenderTransform=System.Windows.Media.Transform.Identity;outgoing.IsHitTestVisible=true;
            ApplyCompactLayout();
            var move=new System.Windows.Media.TranslateTransform(0,16);
            var scale=new System.Windows.Media.ScaleTransform(.975,.975);
            var transform=new System.Windows.Media.TransformGroup();transform.Children.Add(scale);transform.Children.Add(move);
            incoming.RenderTransformOrigin=new System.Windows.Point(.5,.5);incoming.RenderTransform=transform;
            var ease=new System.Windows.Media.Animation.QuinticEase {EasingMode=System.Windows.Media.Animation.EasingMode.EaseOut};
            var enter=new System.Windows.Media.Animation.DoubleAnimation(0,1,TimeSpan.FromMilliseconds(timing.DetailEntranceDurationMilliseconds*720d/780d)){EasingFunction=ease};
            incoming.BeginAnimation(OpacityProperty,enter);
            move.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty,new System.Windows.Media.Animation.DoubleAnimation(16,0,TimeSpan.FromMilliseconds(timing.DetailEntranceDurationMilliseconds)){EasingFunction=ease});
            var finish=new System.Windows.Media.Animation.DoubleAnimation(.975,1,TimeSpan.FromMilliseconds(timing.DetailEntranceDurationMilliseconds)){EasingFunction=ease};
            finish.Completed+=(_,_)=>{if(generation==_compactAnimationGeneration){incoming.BeginAnimation(OpacityProperty,null);incoming.Opacity=1;incoming.RenderTransform=System.Windows.Media.Transform.Identity;}};
            scale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty,finish);
            scale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty,new System.Windows.Media.Animation.DoubleAnimation(.975,1,TimeSpan.FromMilliseconds(timing.DetailEntranceDurationMilliseconds)){EasingFunction=ease});
        };
        outgoing.BeginAnimation(OpacityProperty,fade);
        leave.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty,new System.Windows.Media.Animation.DoubleAnimation(0,-8,TimeSpan.FromMilliseconds(timing.DetailExitDurationMilliseconds)){EasingFunction=exitEase});
        exitScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty,new System.Windows.Media.Animation.DoubleAnimation(1,.985,TimeSpan.FromMilliseconds(timing.DetailExitDurationMilliseconds)){EasingFunction=exitEase});
        exitScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty,new System.Windows.Media.Animation.DoubleAnimation(1,.985,TimeSpan.FromMilliseconds(timing.DetailExitDurationMilliseconds)){EasingFunction=exitEase});
    }

    private void ApplyCompactLayout()
    {
        if (CompactBackButton is null) return;
        UpdateCompactBackButtonPlacement();
        SidebarToggleButton.Visibility = _compactMode ? Visibility.Collapsed : Visibility.Visible;
        DetailSurface.Visibility = !_compactMode || _compactDetail ? Visibility.Visible : Visibility.Collapsed;
        DeviceSidebar.Visibility = !_compactMode || !_compactDetail ? Visibility.Visible : Visibility.Collapsed;
        DetailSurface.Margin = new Thickness(_compactMode ? 8 : 0,0,8,8);
        DeviceSidebar.Margin = new Thickness(8,0,_compactMode ? 8 : 0,8);
        foreach (var viewer in new[] { DetailPanel, SettingsPanel, RouterPanel })
            if (viewer.Content is FrameworkElement content) content.Margin = new Thickness(_compactMode ? 16 : 28,24,_compactMode ? 16 : 28,36);
        if (!_compactMode) { WorkspaceGrid.ColumnDefinitions[2].Width = new GridLength(1,GridUnitType.Star); return; }
        SidebarExpandedContent.BeginAnimation(OpacityProperty,null);
        SidebarExpandedContent.Opacity = 1;
        SidebarExpandedContent.Visibility = Visibility.Visible;
        SidebarExpandedContent.IsHitTestVisible = true;
        CollapsedSidebarContent.Visibility = Visibility.Collapsed;
        SidebarSplitter.Visibility = Visibility.Collapsed;
        SidebarDividerColumn.Width = new GridLength(0);
        SidebarColumn.MinWidth = 0;
        SidebarColumn.MaxWidth = double.PositiveInfinity;
        SidebarColumn.Width = _compactDetail ? new GridLength(0) : new GridLength(1,GridUnitType.Star);
        WorkspaceGrid.ColumnDefinitions[2].Width = _compactDetail ? new GridLength(1,GridUnitType.Star) : new GridLength(0);
        SidebarExpandedContent.Width = Math.Max(0,WorkspaceGrid.ActualWidth-54);
    }

}
