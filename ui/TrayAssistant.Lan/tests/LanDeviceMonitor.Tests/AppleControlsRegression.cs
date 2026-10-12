using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using LanDeviceMonitor;
using LanDeviceMonitor.Controls;
using LanDeviceMonitor.Services;

internal static class AppleControlsRegression
{
    private static void Require(bool condition,string message)
    {
        if(!condition) throw new InvalidOperationException(message);
    }
    public static int Run(bool popupOnly = false, bool performanceOnly = false, bool responsiveOnly = false, bool refinementOnly = false, bool restoredWindowOnly = false, bool sidebarPolishOnly = false, bool visualPolishOnly = false, bool appearanceOnly = false, bool historyOnly = false, bool editingOnly = false, bool ringOnly = false)
    {
        Directory.CreateDirectory(Path.Combine(Environment.CurrentDirectory,".tools","ui-dev249"));
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            App? app = null;
            MainWindow? window = null;
            try
            {
                // App queues its real startup (tray, credentials and single-instance handoff).
                // Cancel that queued work before pumping a dispatcher for visual tests.
                var startupOperations = new List<DispatcherOperation>();
                DispatcherHookEventHandler posted = (_, e) => startupOperations.Add(e.Operation);
                Dispatcher.CurrentDispatcher.Hooks.OperationPosted += posted;
                app = new App();
                Dispatcher.CurrentDispatcher.Hooks.OperationPosted -= posted;
                foreach (var operation in startupOperations) operation.Abort();
                app.InitializeComponent();
                app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                ThemeService.Apply(ThemeService.LightMode);
                // Startup may receive cached history before the first visual measure.
                var populatedHistory = new ItemsControl
                {
                    ItemsSource = new[] { "first", "second", "third" },
                    ItemsPanel = new ItemsPanelTemplate(new FrameworkElementFactory(typeof(SessionHistoryPanel)))
                };
                var startupHost = new Window { Content=populatedHistory, Width=400, Height=300,
                    Left=-10000, Top=-10000, ShowActivated=false, ShowInTaskbar=false };
                try { startupHost.Show(); startupHost.UpdateLayout(); }
                finally { startupHost.Close(); }
                Require(populatedHistory.ItemContainerGenerator.ContainerFromIndex(2) is not null,
                    "首次布局前已经存在的历史数据未呈现");
                Console.WriteLine("Prepopulated history startup PASS.");
                window = new MainWindow(new NotificationService())
                { Left = -10000, Top = -10000, ShowActivated = false, ShowInTaskbar = false };
                // Never initialize monitoring or change the user's persisted settings in a UI test.
                typeof(MainWindow).GetField("_isPopulatingAppSettings", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, true);
                var fixtureTime = new DateTimeOffset(2026, 10, 3, 12, 0, 0, TimeSpan.FromHours(8));
                var devices = Enumerable.Range(0, 12).Select(index => index < 10).Select((online, index) => new LanDeviceMonitor.Models.DeviceViewData
                {
                    Profile = new LanDeviceMonitor.Models.DeviceProfile
                    {
                        MacAddress = $"00:00:00:00:30:{index + 1:X2}", LastRouterName = index == 0 ? "测试笔记本" : $"测试设备 {index + 1}",
                        LastKnownOnline = online, LastSeenAt = fixtureTime, FirstSeenAt = fixtureTime,
                        LastIpAddress = $"192.168.31.{100 + index}",
                        LastDownloadSpeedBytes = index == 9 ? 999 * 1024 * 1024 : index * 1024
                    },
                    Identity = new LanDeviceMonitor.Models.DeviceIdentity("笔记本电脑", "\uE770", index == 0 ? "测试笔记本" : $"测试设备 {index + 1}"),
                    Sessions = (performanceOnly ? Enumerable.Range(1,100).Select(i=>i*62) : new[] { 62, 1550 }).Select(minutes => new LanDeviceMonitor.Models.DeviceSession
                    {
                        MacAddress = $"00:00:00:00:30:{index + 1:X2}",
                        OnlineAt = fixtureTime.AddMinutes(-minutes - 10),
                        OfflineAt = fixtureTime.AddMinutes(-10)
                    }).Concat(appearanceOnly && online ? new[] { new LanDeviceMonitor.Models.DeviceSession { MacAddress = $"00:00:00:00:30:{index + 1:X2}", OnlineAt = fixtureTime.AddHours(-3) } } : []).ToList()
                }).ToArray();
                typeof(MainWindow).GetMethod("ReplaceDevices", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [devices, false, false]);
var shellIcon = new BitmapImage(); shellIcon.BeginInit(); shellIcon.CacheOption=BitmapCacheOption.OnLoad;
                shellIcon.UriSource=new Uri(Path.Combine(Environment.CurrentDirectory,"src","LanDeviceMonitor.App","Assets","AppIcon.png")); shellIcon.EndInit(); shellIcon.Freeze();
                window.ShellAppIcon.Source=shellIcon;
                if (popupOnly) window.Opacity = 0;
                window.Show(); window.UpdateLayout();
                Pump(600);
                if(ringOnly) { VerifyNativeRing(window); return; }
                if(editingOnly) { VerifyTextEditing(window); return; }
                if (historyOnly) { VerifyHistoryDisclosure(window); return; }
                if (appearanceOnly) { VerifyStatusAppearance(window); return; }
                if (visualPolishOnly) { VerifyVisualPolish(window); return; }
                if (sidebarPolishOnly) { VerifySidebarPolish(window); return; }
                if (restoredWindowOnly) { VerifyRestoredWindow(window); return; }
                if (performanceOnly) { ProfileDeviceTransitions(window); return; }
                if (responsiveOnly) { VerifyResponsiveNavigation(window); return; }
                if (refinementOnly) { VerifyRefinements(window); return; }
                var profileGuard=typeof(MainWindow).GetField("_isPopulatingDeviceSettings",BindingFlags.Instance|BindingFlags.NonPublic)!;
                var oldGuard=profileGuard.GetValue(window);profileGuard.SetValue(window,true);
                var placeholder=(TextBlock)window.CustomNameBox.Template.FindName("Placeholder",window.CustomNameBox);
                Require(placeholder.Visibility==Visibility.Visible && window.CustomNameBox.Text=="", "空名称应显示提示，提示不可写入真实文本");
                window.CustomNameBox.Text="测试名称"; Pump(20);
                Require(placeholder.Visibility==Visibility.Collapsed,"有文字时占位提示未隐藏");
                window.CustomNameBox.Clear();profileGuard.SetValue(window,oldGuard);
                window.SidebarToggleButton.Focus();Pump(20);
                Require(((Border)window.SidebarToggleButton.Template.FindName("IconHover",window.SidebarToggleButton)).Opacity==0,"键盘焦点被误当作鼠标悬停亮层");
                Require(window.DetailDeviceIconBackground.CornerRadius.TopLeft==window.DetailDeviceIconBackground.ActualHeight/2,"详情设备图标底色不是圆形");
                if (popupOnly) { VerifyPopupAlignment(window); return; }
                KeepTestPopupsOffDesktop(window);
                foreach (var option in DeviceIdentityService.AvailableTypes)
                {
                    var icon = new DeviceTypeIcon { DeviceType=option.TypeName };
                    var bounds=icon.IconGeometry.GetRenderBounds(new Pen(Brushes.Black,1.7));
                    Require(bounds.Left>=0 && bounds.Top>=0 && bounds.Right<=24 && bounds.Bottom<=24,
                        $"设备图标超出24px画布: {option.TypeName} {bounds}");
                }
                Require(DeviceIdentityService.AvailableTypes.Any(t=>t.TypeName=="摄像头") &&
                    DeviceIdentityService.Identify("我的摄像头","",1).TypeName=="摄像头", "摄像头类型或识别缺失");
                ExportDeviceIconAtlas(window);
                window.DetailPanel.ScrollToBottom(); Pump(120);
                Require(window.DetailPanel.VerticalOffset > 0, "滚动重置回归缺少可滚动内容");
                window.DeviceList.SelectedIndex = 1; Pump(1150);
                Require(window.DetailPanel.VerticalOffset < .1, "切换设备后详情未回到顶部");
                window.DeviceFilterSegments.SelectedIndex = 1; Pump(60);
                Require(window.SelectedDevice?.IsOnline == true, "详情应先完成退出段再替换内容");
                if (SystemParameters.ClientAreaAnimation)
                {
                    var listProgress = 1 - Math.Abs(((TranslateTransform)window.GetDeviceListMotion(window.DeviceListTransition).RenderTransform).X) / 340;
                    var filterIndicator = (Border)window.DeviceFilterSegments.Template.FindName("PART_Indicator",window.DeviceFilterSegments);
                    var filterProgress = ((TranslateTransform)filterIndicator.RenderTransform).X / filterIndicator.ActualWidth;
                    Console.WriteLine($"SYNC at 60ms: list={listProgress:F3}, selector={filterProgress:F3}, detail={window.DetailPanel.Opacity:F3}");
                    Require(Math.Abs(listProgress-filterProgress)<.12 && window.DetailPanel.Opacity > 0 && window.DetailPanel.Opacity < 1, "列表与筛选不同步，或详情未同时开始退出段");
                }
                Pump(1100); window.DeviceFilterSegments.SelectedIndex = 0; Pump(1100);
                foreach (var button in new[] { window.SidebarRailRouterButton, window.SidebarRailSettingsButton })
                {
                    if (button.Content is not TextBlock glyphText) continue;
                    var formatted = new FormattedText(glyphText.Text, System.Globalization.CultureInfo.CurrentCulture,
                        FlowDirection.LeftToRight, new Typeface(glyphText.FontFamily, glyphText.FontStyle, glyphText.FontWeight, glyphText.FontStretch),
                        glyphText.FontSize, Brushes.Black, VisualTreeHelper.GetDpi(glyphText).PixelsPerDip);
                    var ink = glyphText.TransformToAncestor(button).TransformBounds(formatted.BuildGeometry(new Point()).Bounds);
                    Console.WriteLine($"ICON CENTER {button.Name}: dx={ink.Left + ink.Width / 2 - button.ActualWidth / 2:F3}, dy={ink.Top + ink.Height / 2 - button.ActualHeight / 2:F3}");
                    Require(Math.Abs(ink.Left + ink.Width / 2 - button.ActualWidth / 2) < .1 &&
                        Math.Abs(ink.Top + ink.Height / 2 - button.ActualHeight / 2) < .1, $"按钮图标未按字形居中: {button.Name}");
                }
                Require(window.CustomNameBox.FontFamily.Source == window.ShellAppName.FontFamily.Source &&
                    window.CustomNameBox.FontWeight == FontWeights.Normal && window.DeviceTypeCombo.FontWeight == FontWeights.Normal,
                    $"表单文字应与普通界面文字采用同一字体及正常字重: input={window.CustomNameBox.FontFamily}, title={window.ShellAppName.FontFamily}, weights={window.CustomNameBox.FontWeight}/{window.DeviceTypeCombo.FontWeight}");
                foreach (var familyKey in new[] { "AppTextFont", "AppIconFont" })
                {
                    var family = (FontFamily)window.FindResource(familyKey);
                    var face = new Typeface(family, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
                    var expectedScheme = Environment.GetEnvironmentVariable("LAN_FONT_PROOF") == "baseline" ? "file" : "pack";
                    Require(face.TryGetGlyphTypeface(out var actualGlyph) && actualGlyph.FontUri.Scheme == expectedScheme,
                        $"字体没有从内嵌资源加载: {familyKey}, {family.Source}");
                    Console.WriteLine($"Embedded font: {familyKey} = {actualGlyph!.FontUri}");
                }
                window.DeviceTypeCombo.IsDropDownOpen = true; Pump(650);
                var iconPopup = (System.Windows.Controls.Primitives.Popup)window.DeviceTypeCombo.Template.FindName("PART_Popup", window.DeviceTypeCombo);
                var typeTrigger = (System.Windows.Controls.Primitives.ToggleButton)window.DeviceTypeCombo.Template.FindName("DropDownToggle",window.DeviceTypeCombo);
                var typeTriggerRoot = (FrameworkElement)typeTrigger.Template.FindName("StudioRoot",typeTrigger);
                Require(!typeTriggerRoot.RenderTransform.HasAnimatedProperties, "静止下拉框仍有缩放动画时钟");
                var deviceIcons = Descendants<DeviceTypeIcon>(iconPopup.Child).ToArray();
                Require(deviceIcons.Length > 0, "设备类型菜单缺少图标");
                foreach (var icon in deviceIcons)
                {
                    Require(!icon.IconGeometry.Bounds.IsEmpty && icon.IconGeometry.Bounds.Width > 0, $"设备矢量图标缺失: {icon.DeviceType}");
                }
                CaptureElement((FrameworkElement)iconPopup.Child,"device-types-Light.png");
                window.DeviceTypeCombo.IsDropDownOpen = false; Pump(250);
                var originalTypeIndex = window.DeviceTypeCombo.SelectedIndex;
                typeof(MainWindow).GetField("_isPopulatingDeviceSettings", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, true);
                foreach (var mode in new[] { ThemeService.LightMode, ThemeService.DarkMode })
                {
                    ThemeService.Apply(mode);
                    for (var index = 0; index < window.DeviceTypeCombo.Items.Count; index++)
                    {
                        window.DeviceTypeCombo.SelectedIndex = index; Pump(30);
                        foreach (var icon in Descendants<DeviceTypeIcon>(window.DeviceTypeCombo))
                        {
                            Require(!icon.IconGeometry.Bounds.IsEmpty && icon.IconGeometry.Bounds.Width > 0, $"设备矢量图标缺失: {icon.DeviceType}");
                        }
                    }
                    window.DeviceTypeCombo.IsDropDownOpen = true; Pump(650);
                    CaptureElement((FrameworkElement)iconPopup.Child,$"device-types-{mode}.png");
                    window.DeviceTypeCombo.IsDropDownOpen = false; Pump(250);
                }
                window.DeviceTypeCombo.SelectedIndex = originalTypeIndex;
                typeof(MainWindow).GetField("_isPopulatingDeviceSettings", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, false);
                ThemeService.Apply(ThemeService.LightMode);
                foreach (var pair in new[] { (0, 1), (2, 10), (1, 5), (0, 1), (2, 10) })
                {
                    window.TrafficRangeSegments.SelectedIndex = pair.Item1;
                    Require(window.TrafficRangeMinutes == pair.Item2, "统计时段未同步");
                }
                var segments = window.DeviceFilterSegments;
                segments.SelectedIndex = 1; segments.SelectedIndex = 0; segments.SelectedIndex = 1;
                Pump(300);
                var indicator = (Border)segments.Template.FindName("PART_Indicator", segments);
                VerifySegmentText(segments);
                VerifySegmentText(window.TrafficRangeSegments);
                var hoverItem = (ListBoxItem)segments.Items[0];
                var idleColor = ((SolidColorBrush)hoverItem.Foreground).Color;
                var activeColor = ((SolidColorBrush)((SegmentItem)hoverItem).ActiveBrush).Color;
                void Hover(bool enter) => hoverItem.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, Environment.TickCount)
                { RoutedEvent = enter ? Mouse.MouseEnterEvent : Mouse.MouseLeaveEvent });
                Hover(true);
                Require(((SolidColorBrush)hoverItem.Foreground).HasAnimatedProperties || !SystemParameters.ClientAreaAnimation,
                    "悬停事件必须启动动画");
                Pump(55);
                var halfwayColor = ((SolidColorBrush)hoverItem.Foreground).Color;
                Require(halfwayColor != idleColor, "未选中项缺少悬停反馈");
                if (SystemParameters.ClientAreaAnimation)
                    Require(halfwayColor != activeColor, "悬停进入必须存在中间帧而非瞬间切换");
                Hover(false);
                Require(((SolidColorBrush)hoverItem.Foreground).Color == halfwayColor || !SystemParameters.ClientAreaAnimation,
                    "悬停反向必须从当前颜色继续");
                Pump(240);
                Require(((SolidColorBrush)hoverItem.Foreground).Color == idleColor, "悬停结束没有恢复灰色");
                Hover(true); Pump(240);
                Require(((SolidColorBrush)hoverItem.Foreground).Color == activeColor, "悬停进入没有到达主文字色");
                Hover(false); Pump(240);
                segments.Focus(); Pump(50);
                Require(segments.Template.FindName("FocusRing", segments) is null, "分段仍含蓝色焦点框");
                var scale = VisualTreeHelper.GetDpi(segments).DpiScaleY;
                var expectedPadding = 2 * Math.Round(1.5 * scale) / scale;
                Require(Math.Abs(segments.ActualHeight - indicator.ActualHeight - expectedPadding) < 0.1, $"分段上下留白应各为 1.5: control={segments.ActualHeight}, indicator={indicator.ActualHeight}");
                Require(Math.Abs(((TranslateTransform)indicator.RenderTransform).X - indicator.ActualWidth) < 1, "滑块没有到达离线分段");
                segments.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window)!, 0, Key.Home) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
                Require(segments.SelectedIndex == 0, "Home 键未返回首段");
                Pump(1200);
                VerifySegmentText(segments);
                window.Background = new SolidColorBrush(Color.FromRgb(242,243,247));
                Require(window.ShellAppName.Text == "局域网设备助手" && window.ShellAppName.IsVisible && window.ShellAppIcon.IsVisible,
                    "标题区缺少软件图标或名称");
                Require(window.DeviceSidebar.CornerRadius == window.DeviceNetworkInfoCard.CornerRadius &&
                    window.DetailSurface.CornerRadius == window.DeviceNetworkInfoCard.CornerRadius &&
                    window.DeviceSidebar.BorderThickness == new Thickness(1) && window.DetailSurface.BorderThickness == new Thickness(1), "左右内容应使用独立的统一圆角卡片");
                Require(System.Windows.Automation.AutomationProperties.GetName(window.SidebarRailSettingsButton) == "设置" && System.Windows.Automation.AutomationProperties.GetName(window.SidebarRailRouterButton) == "路由器", "底部图标缺少可访问名称");
                WindowFrameHitTests.Verify(window);
                Require(window.DetailPanel.FocusVisualStyle is null, "详情滚动区域仍有默认虚线焦点框");
                Require(ReferenceEquals(window.DetailStatusText.Foreground, window.FindResource("TextBrush")), "连接状态应使用普通文字颜色");
                foreach (var dot in Descendants<System.Windows.Shapes.Ellipse>(window.DetailPanel).Where(x=> x.Fill == window.FindResource("TimelineOfflineDotBrush") || x.Fill == window.FindResource("TimelineOnlineDotBrush")))
                    Require(dot.Width == 9 && dot.Height == 9, "历史记录的小点尺寸被误改");
var iconLeft = window.ShellAppIcon.TranslatePoint(new Point(),window.SidebarExpandedContent).X;
                var tangentX = window.DeviceFilterSegments.TranslatePoint(new Point(),window.SidebarExpandedContent).X + window.DeviceFilterSegments.ActualHeight * 0.3;
                Require(Math.Abs(iconLeft-tangentX)<0.1,$"软件图标左缘未对齐筛选胶囊水平切点 icon={iconLeft} tangent={tangentX} margin={window.ShellIdentityRow.Margin} height={window.DeviceFilterSegments.ActualHeight}");
                Require(window.ShellAppName.FontSize == 18 && window.ShellAppName.FontWeight == FontWeights.Medium && Math.Abs(window.ShellAppIcon.ActualHeight - 30) <= 1, $"标题字号或图标尺寸错误: {window.ShellAppName.FontSize}, {window.ShellAppName.FontWeight}, {window.ShellAppIcon.ActualHeight}");
                Require(window.FindName("CopyDeviceIpButton") is null && window.FindName("CopyDeviceMacButton") is null, "连接信息不应保留复制按钮");
                window.SidebarColumn.Width = new GridLength(310); window.UpdateLayout();
                var selectedBeforeCollapse = window.SelectedDevice;
                var collapseStartWidth=window.SidebarColumn.ActualWidth;
                var collapseClock=System.Diagnostics.Stopwatch.StartNew();
                window.SidebarToggleButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                if(SystemParameters.ClientAreaAnimation)
                    PumpUntil(()=>window.SidebarColumn.ActualWidth> MainWindow.CollapsedSidebarWidth && window.SidebarColumn.ActualWidth<collapseStartWidth-.5);
                var intermediateWidth = window.SidebarColumn.ActualWidth;
                if (SystemParameters.ClientAreaAnimation)
                    Require(intermediateWidth > MainWindow.CollapsedSidebarWidth && intermediateWidth < collapseStartWidth, $"侧栏收起必须存在动画中间帧 width={intermediateWidth}, elapsed={collapseClock.ElapsedMilliseconds}");
                Capture(window, "sidebar-mid-transition.png", ThemeService.LightMode);
                window.CollapsedSidebarButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                window.UpdateLayout();
                Require(Math.Abs(window.SidebarColumn.ActualWidth - intermediateWidth) < 2 || !SystemParameters.ClientAreaAnimation,
                    "侧栏反向动画发生跳变");
                Pump(350);
                Require(Math.Abs(window.SidebarColumn.ActualWidth - collapseStartWidth) < 1, "反向展开没有恢复侧栏宽度");
                window.SidebarToggleButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent)); Pump(350);
                Require(window.SidebarColumn.ActualWidth == MainWindow.CollapsedSidebarWidth && window.SidebarExpandedContent.Visibility == Visibility.Collapsed &&
                    window.CollapsedSidebarButton.IsVisible && !window.SidebarRailSettingsButton.IsVisible, "收起后应仅保留统一窄栏");
                Require(window.SidebarDividerLine.IsVisible && window.SidebarDividerColumn.ActualWidth + 0.001 >= 1 / VisualTreeHelper.GetDpi(window).DpiScaleX, $"收起侧栏缺少分界线 width={window.SidebarDividerColumn.ActualWidth} line={window.SidebarDividerLine.ActualWidth} visible={window.SidebarDividerLine.IsVisible}");
                Capture(window, "sidebar-collapsed.png", ThemeService.LightMode);
                window.TrafficRangeSegments.Focus();
                Pump(220);
                window.CollapsedSidebarButton.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, Environment.TickCount) { RoutedEvent = Mouse.MouseEnterEvent });
                Require(window.CollapsedSidebarGlyph.Opacity == 1 && window.FindName("CollapsedAppIcon") is null, "收起后应直接显示侧栏按钮，不切换软件图标");
                Capture(window, "sidebar-collapsed-hover.png", ThemeService.LightMode);
                window.CollapsedSidebarButton.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice,Environment.TickCount){RoutedEvent=Mouse.MouseLeaveEvent});
                Pump(200); Require(window.CollapsedSidebarGlyph.Opacity==1,"移出后不应隐藏展开图标");
                var blankHit = window.CollapsedSidebarButton.InputHitTest(new Point(28, window.CollapsedSidebarButton.ActualHeight * 0.7));
                Require(window.CollapsedRouterButton.IsVisible && window.CollapsedSettingsButton.IsVisible, "收起后必须保留两个独立圆形导航按钮");
                var expandPeer = UIElementAutomationPeer.CreatePeerForElement(window.CollapsedSidebarButton)!;
                ((IInvokeProvider)expandPeer.GetPattern(PatternInterface.Invoke)!).Invoke(); Pump(350);
                Require(Math.Abs(window.SidebarColumn.ActualWidth - collapseStartWidth) < 1 && ReferenceEquals(selectedBeforeCollapse, window.SelectedDevice), "点击窄栏应展开并保留选中设备");
                window.SidebarRailSettingsButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                typeof(MainWindow).GetField("_isPopulatingAppSettings", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, true); Pump(550);
                Require(window.SettingsPanel.Visibility == Visibility.Visible, "合并后的设置入口失效");
                var selectedRow=(ListBoxItem)window.DeviceList.ItemContainerGenerator.ContainerFromItem(window.DeviceList.SelectedItem);
                var selectedRowText=Descendants<TextBlock>(selectedRow).First();
                selectedRowText.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,Environment.TickCount,MouseButton.Left) { RoutedEvent=Mouse.PreviewMouseDownEvent }); Pump(550);
                window.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,Environment.TickCount,MouseButton.Left) { RoutedEvent=Mouse.PreviewMouseUpEvent }); Pump(280);
                Require(window.DetailPanel.Visibility == Visibility.Visible, "合并导航无法返回设备详情");
                window.SidebarColumn.Width = new GridLength(390); window.UpdateLayout();
                Capture(window, "devices-Light.png", ThemeService.LightMode);
                Require(window.DetailConnectionText.Text == window.SelectedDevice!.ConnectionText.Replace("网线接入", "网线") &&
                    window.DetailIpAddressText.Text == window.SelectedDevice.IpAddress &&
                    window.DetailMacAddressText.Text == window.SelectedDevice.MacAddress,
                    "独立网络信息栏必须对应当前设备");
                var deviceItem = (ListBoxItem)window.DeviceList.ItemContainerGenerator.ContainerFromIndex(0);
                var rowSurface = Descendants<Border>(deviceItem).First(border => border.Name == "ItemBorder");
                Require(Math.Abs(rowSurface.ActualWidth - window.DeviceFilterSegments.ActualWidth) < 1, "设备胶囊与筛选滑块不等宽");
                var listScroll = Descendants<ScrollViewer>(window.DeviceList).First();
                var bar = (System.Windows.Controls.Primitives.ScrollBar)listScroll.Template.FindName("PART_VerticalScrollBar", listScroll);
                Require(bar.IsVisible && bar.ActualWidth > 0 && bar.TranslatePoint(new Point(), window.DeviceList).X >= rowSurface.ActualWidth - 1, "滚动条没有移至胶囊右侧");
                Console.WriteLine($"SCROLL bar={bar.ActualWidth}x{bar.ActualHeight} max={bar.Maximum} viewport={bar.ViewportSize} position={bar.TranslatePoint(new Point(), window)} thumb={Descendants<System.Windows.Controls.Primitives.Thumb>(bar).First().ActualHeight}");
                listScroll.ScrollToEnd(); Pump(70);
                Require(listScroll.VerticalOffset > 0, "设备列表无法滚动");
                listScroll.ScrollToHome(); Pump(70);
                Capture(window, "device-scrollbar-verified.png", ThemeService.LightMode);
                window.ConnectionStatusText.Text = "已连接";
                window.LastRefreshText.Text = "更新于 12:34:56"; Pump(30);
                Require(window.ConnectionStatusText.Visibility == Visibility.Collapsed && window.LastRefreshText.IsVisible && window.LastRefreshText.FontSize == 14 && window.ConnectionDot.Width == 9.6, "已连接应仅显示较大的更新时间与圆点");
                window.ConnectionStatusText.Text = "连接异常"; Pump(30);
                Require(window.ConnectionStatusText.IsVisible && window.LastRefreshText.Visibility == Visibility.Collapsed, "异常状态不可被更新时间覆盖");
                var thumb = Descendants<System.Windows.Controls.Primitives.Thumb>(bar).First();
                var thumbVisual = Descendants<Border>(thumb).First(x => x.Name == "ThumbDefaultVisual");
                var thumbX = thumbVisual.TranslatePoint(new Point(thumbVisual.ActualWidth / 2, 0), window.DeviceSidebar).X;
                var capsuleRight = rowSurface.TranslatePoint(new Point(rowSurface.ActualWidth, 0), window.DeviceSidebar).X;
                Require(thumbX > capsuleRight && thumbX < window.DeviceSidebar.ActualWidth, "滚动条必须位于胶囊右缘与侧栏边缘之间");
                var hoverKey = (DependencyPropertyKey)typeof(UIElement).GetField("IsMouseOverPropertyKey",BindingFlags.Static|BindingFlags.NonPublic)!.GetValue(null)!;
                var pressedKey = (DependencyPropertyKey)typeof(System.Windows.Controls.Primitives.ButtonBase).GetField("IsPressedPropertyKey", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
                foreach (var mode in new[] { ThemeService.LightMode, ThemeService.DarkMode })
                {
                    ThemeService.Apply(mode);
                    window.Background = new SolidColorBrush(mode == ThemeService.DarkMode ? Color.FromRgb(30,32,38) : Color.FromRgb(242,243,247));
                    foreach (var footer in new[] { window.SidebarRailRouterButton, window.SidebarRailSettingsButton })
                    {
                        var hover = (Border)footer.Template.FindName("CircleHover", footer);
                        var pressed = (Border)footer.Template.FindName("CirclePressed", footer);
                        footer.SetValue(hoverKey, true); Pump(150);
                        Require(hover.Opacity == 1, "底部按钮缺少悬停态");
                        Require(hover.Margin.Left>=footer.BorderThickness.Left,"悬停亮层覆盖了按钮描边");
                        var actualInk=footer.Content is DeviceTypeIcon vector ? vector.IconBrush : ((TextBlock)footer.Content).Foreground;
                        Require(((SolidColorBrush)actualInk).Color==(Color)window.FindResource("FooterIconHoverColor"),"底部按钮图标没有随悬停提亮");
                        CaptureElement(footer,$"footer-{footer.Name}-{mode}-hover.png");
                        footer.SetValue(pressedKey, true); Pump(30);
                        Require(pressed.Opacity == 1, "底部按钮缺少点按态");
                        CaptureElement(footer,$"footer-{footer.Name}-{mode}-pressed.png");
                        footer.SetValue(pressedKey, false); footer.SetValue(hoverKey, false); Pump(150);
                        Require(hover.Opacity == 0 && pressed.Opacity == 0, "底部按钮状态残留");
                    }
                    Capture(window, $"devices-{mode}.png", mode);
                }
                ThemeService.Apply(ThemeService.LightMode);
                Capture(window, "footer-and-controls.png", ThemeService.LightMode);

                var rowIcon = Descendants<Border>(deviceItem).First(border => border.Name == "DeviceRowIconBackground");
                var iconPosition = rowIcon.TranslatePoint(new Point(0,0), rowSurface);
                var bottomSpace = rowSurface.ActualHeight - iconPosition.Y - rowIcon.ActualHeight;
                const double pixelTolerance = 0.001;
                Require(Math.Abs(iconPosition.X - iconPosition.Y) <= pixelTolerance && Math.Abs(iconPosition.Y - bottomSpace) <= pixelTolerance,
                    $"图标四周间距不均: left={iconPosition.X}, top={iconPosition.Y}, bottom={bottomSpace}");
                var rateText = Descendants<TextBlock>(deviceItem).First(text => text.Name == "DeviceListRate");
                var rowDevice=(LanDeviceMonitor.ViewModels.DeviceItemViewModel)deviceItem.DataContext;
                Require(rateText.Text==rowDevice.OnlineElapsedText && Equals(rateText.ToolTip,rowDevice.OnlineSinceText),
                    "在线条目应显示上线距今，并提供准确上线时间");
                Require(deviceItem.ActualHeight >= 52 && deviceItem.ActualHeight <= 54, "单行设备胶囊没有压缩高度");
                Require(!Descendants<TextBlock>(deviceItem).Any(text => text.Text is "在线" or "离线" or "网线接入"),
                    "左侧设备条目仍含状态或连接信息");
                window.Width = 980; window.Height = 650; Pump(250);
                Require(window.SidebarColumn.ActualWidth <= window.WorkspaceGrid.ActualWidth - 605, "窄窗口没有限制侧栏宽度");
                Require(window.DetailNetworkMetadataRow.Children.OfType<StackPanel>().Count() == 4, "连接信息未横向排列");
                Capture(window, "devices-Light-980.png", ThemeService.LightMode);
                Require(Math.Abs(indicator.CornerRadius.TopLeft - indicator.ActualHeight / 2) < 0.1, "胶囊圆角不是高度的一半");
                typeof(MainWindow).GetMethod("ShowSettingsPanel", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null);
                Pump(500);
                window.RouterAddressBox.Text = "10.77.0.1";
                typeof(MainWindow).GetField("_hasSavedRouterPassword",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(window,true);
                window.RouterPasswordBox.Password="";
                typeof(MainWindow).GetMethod("RefreshPasswordIndicator",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,null);
                Require(window.SavedRouterPasswordMask.Visibility==Visibility.Visible,"已有凭据没有掩码提示");
                window.RouterPasswordBox.Password="synthetic";
                Require(window.SavedRouterPasswordMask.Visibility==Visibility.Collapsed,"输入新密码时仍叠加已保存占位");
                window.RouterPasswordBox.Password="";
                window.DoNotDisturbStartBox.Text = "22:30";
                window.DoNotDisturbEndBox.Text = "08:00";
                window.RefreshIntervalCombo.SelectedIndex = 0;
                window.ThemeModeCombo.SelectedIndex = 0;

                var labelText = Descendants<TextBlock>(window.ThemeModeCombo).First(t => t.Text == "跟随系统");
                Console.WriteLine($"TEXT START input={window.RouterAddressBox.GetRectFromCharacterIndex(0).Left:F3}, dropdown={labelText.TranslatePoint(new Point(), window.ThemeModeCombo).X:F3}");
                Require(Math.Abs(window.RouterAddressBox.GetRectFromCharacterIndex(0).Left - labelText.TranslatePoint(new Point(), window.ThemeModeCombo).X) < .1,
                    "下拉文字起点与输入框不一致");
                window.ThemeModeCombo.IsDropDownOpen = true; Pump(90);
                var selectPopup = (System.Windows.Controls.Primitives.Popup)window.ThemeModeCombo.Template.FindName("PART_Popup", window.ThemeModeCombo);
                Require(selectPopup.IsOpen && selectPopup.Child.Opacity > 0, "下拉菜单未打开");
                Pump(600);
                var trigger = (System.Windows.Controls.Primitives.ToggleButton)window.ThemeModeCombo.Template.FindName("DropDownToggle",window.ThemeModeCombo);
                trigger.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice,Environment.TickCount) { RoutedEvent = Mouse.MouseEnterEvent });
                trigger.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,Environment.TickCount,MouseButton.Left) { RoutedEvent = Mouse.PreviewMouseDownEvent }); Pump(150);
                var selectFace=(Border)trigger.Template.FindName("StudioSurface",trigger);
                var selectRoot=(FrameworkElement)trigger.Template.FindName("StudioRoot",trigger);
                Require(((SolidColorBrush)selectFace.Background).Color == (Color)window.FindResource("StudioSurfaceColor"), "点按下拉框不应改变胶囊底色");
                Require(((ScaleTransform)selectRoot.RenderTransform).ScaleY < .99, "下拉框点按缩放丢失");
                trigger.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,Environment.TickCount,MouseButton.Left) { RoutedEvent = Mouse.PreviewMouseUpEvent }); Pump(650);
                Require(Math.Abs(((ScaleTransform)selectRoot.RenderTransform).ScaleY-1)<.001, "下拉框点按未恢复");
                Require(!selectPopup.Child.HasAnimatedProperties && !selectPopup.Child.RenderTransform.HasAnimatedProperties, "下拉弹层展开结束后仍残留动画时钟");
                Require(!selectRoot.RenderTransform.HasAnimatedProperties, "下拉按压结束后仍残留缩放动画时钟");
                Require(((Border)selectPopup.Child).CornerRadius == window.DeviceNetworkInfoCard.CornerRadius, "弹层圆角未统一");
                Require(window.RouterAddressBox.FontWeight == FontWeights.Normal && window.CustomNameBox.FontWeight == FontWeights.Normal, "输入文字应与普通文字使用相同字重");
                var arrow = (FrameworkElement)trigger.Template.FindName("StudioArrow",trigger);
                Require(Math.Abs(((RotateTransform)arrow.RenderTransform).Angle-180)<.01, "下拉箭头未旋转180度");
                Require(((Border)selectPopup.Child).ActualWidth >= window.ThemeModeCombo.ActualWidth, $"弹层不能窄于触发胶囊: {((Border)selectPopup.Child).ActualWidth}");
                var choice = (ComboBoxItem)window.ThemeModeCombo.Items[0];
                Require(choice.FontSize==window.ThemeModeCombo.FontSize && Math.Abs(choice.ActualHeight-window.ThemeModeCombo.ActualHeight)<.1,"弹出胶囊字体或高度与触发胶囊不一致");
                Require(choice.ActualHeight >= 43.5 && ((SolidColorBrush)choice.Foreground).Color == (Color)window.FindResource("StudioSelectedTextColor"), "菜单选中样式与参考不符");
                foreach(var text in Descendants<TextBlock>(choice))
                    Require(((SolidColorBrush)text.Foreground).Color == (Color)window.FindResource("StudioSelectedTextColor"), $"菜单实际文字未跟随选中色: {text.Text} {text.Foreground}");
                Console.WriteLine($"Popup shape: height={choice.ActualHeight}; surface={((Border)choice.Template.FindName("StudioSurface",choice)).ActualHeight}; radius={((Border)choice.Template.FindName("StudioSurface",choice)).CornerRadius}");
                CaptureElement((FrameworkElement)selectPopup.Child,"dropdown-Light.png");
                window.ThemeModeCombo.IsDropDownOpen = false;
                Require(selectPopup.IsOpen && !selectPopup.Child.IsHitTestVisible, "关闭动画应保留视觉但立即释放点击");
                window.ThemeModeCombo.IsDropDownOpen = true; Pump(220);
                Require(selectPopup.IsOpen && selectPopup.Child.IsHitTestVisible && selectPopup.Child.Opacity > .99, "反向打开下拉菜单失败");
                window.ThemeModeCombo.IsDropDownOpen = false; Pump(220);
                Require(!selectPopup.IsOpen, "关闭后透明弹层未释放");
                window.Left = -10000; window.Top = -10000;
                window.RouterAddressBox.Focus(); Pump(320);
                var focusHalo = (Border)window.RouterAddressBox.Template.FindName("FocusHalo", window.RouterAddressBox);
                Require(Math.Abs(focusHalo.Opacity-.17)<.01, "输入框聚焦光环不正确");
                var inputFace = (Border)window.RouterAddressBox.Template.FindName("StudioSurface",window.RouterAddressBox);
                Require(((SolidColorBrush)inputFace.BorderBrush).Color == (Color)window.FindResource("AccentColor") &&
                    ReferenceEquals(focusHalo.BorderBrush, window.FindResource("AccentBrush")), "输入框聚焦描边与高光必须使用主题蓝色");
                CaptureElement(window.RouterAddressBox,"input-focused-Light.png");
                window.RouterAddressBox.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,Environment.TickCount,MouseButton.Left){RoutedEvent=Mouse.PreviewMouseDownEvent}); Pump(320);
                var inputRoot=(FrameworkElement)window.RouterAddressBox.Template.FindName("StudioRoot",window.RouterAddressBox);
                Require(Math.Abs(((ScaleTransform)inputRoot.RenderTransform).ScaleX-.995)<.001,"输入框缺少按压缩放");
                window.RouterAddressBox.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,Environment.TickCount,MouseButton.Left){RoutedEvent=Mouse.PreviewMouseUpEvent}); Pump(320);
                Require(Math.Abs(((ScaleTransform)inputRoot.RenderTransform).ScaleX-1)<.001,"输入框按压状态残留");
                Require(!inputRoot.RenderTransform.HasAnimatedProperties, "输入框松开后仍有缩放动画时钟，文字没有回到静止渲染");

                var switchNames = new[] { "StartWithWindowsCheck", "DoNotDisturbCheck", "NotifyIpv6NonNativeCheck", "LanAccessCheck", "NotifyStatusChangeCheck" };
                foreach (var name in switchNames)
                {
                    var control = window.FindName(name) as SwitchControl;
                    Require(control is not null, name + " 未转换为开关");
                    Require(!string.IsNullOrWhiteSpace(System.Windows.Automation.AutomationProperties.GetName(control!)), name + " 缺少辅助名称");
                }
                var toggle = window.DoNotDisturbCheck;
                var peer = UIElementAutomationPeer.CreatePeerForElement(toggle)!;
                var provider = (IToggleProvider)peer.GetPattern(PatternInterface.Toggle)!;
                toggle.IsChecked = false;
                provider.Toggle(); Pump(250);
                Require(toggle.IsChecked == true && window.DoNotDisturbStartBox.IsEnabled, "开关未启用时间编辑");
                Require(window.DoNotDisturbTimeLabel.Opacity==1 && window.DoNotDisturbTimeSeparator.Opacity==1 &&
                    ((SolidColorBrush)window.DoNotDisturbTimeLabel.Foreground).Color==(Color)window.FindResource("TextColor"),"免打扰启用后整行文字未恢复正常色");
                var knob = (Border)toggle.Template.FindName("PART_Knob", toggle);
                Require(Math.Abs(((TranslateTransform)knob.RenderTransform).X - 20) < 0.1, "开关滑块位置不正确");
                provider.Toggle(); Pump(250);
                Require(toggle.IsChecked == false && !window.DoNotDisturbStartBox.IsEnabled, "关闭后时间编辑未禁用");
                Require(window.DoNotDisturbTimeLabel.Opacity==.45 && window.DoNotDisturbTimeSeparator.Opacity==.45,"禁用时标签和至字状态不一致");
                toggle.Focus(); Pump(50);
                Require(toggle.Template.FindName("FocusRing", toggle) is null, "开关仍含蓝色焦点框");
                window.DoNotDisturbCheck.BringIntoView(); Pump(150);
                Capture(window, "switch-off-focused.png", ThemeService.LightMode);
                Require(window.FindName("LanQrImage") is null, "二维码仍在设置页中");
                window.StartWithWindowsCheck.IsChecked = true;
                window.NotifyIpv6NonNativeCheck.IsChecked = true;
                window.LanAccessCheck.IsChecked = true;
                window.LanAccessDetails.Visibility = Visibility.Visible;
                window.LanAddressBox.Text = "http://10.77.0.100:17863/";
                window.WebAccessStatus.Text = "已开启 · 同一局域网内打开下方链接即可查看。";
                window.UpdateLayout();
                Require(Math.Abs(window.RouterAddressBox.ActualHeight - 44) < 1, "地址输入框高度不应被重复内边距撑高");
                Require(Math.Abs(window.LanAddressBox.ActualHeight - window.CopyLanAddressButton.ActualHeight) < 1, "访问地址与复制按钮高度必须一致");
                foreach (var mode in new[] { ThemeService.LightMode, ThemeService.DarkMode })
                foreach (var size in new[] { (1180d,760d), (980d,650d) })
                {
                    ThemeService.Apply(mode);
                    window.Background = new SolidColorBrush(mode == ThemeService.DarkMode ? Color.FromRgb(30,32,38) : Color.FromRgb(242,243,247));
                    window.Width = size.Item1; window.Height = size.Item2;
                    window.SettingsPanel.ScrollToTop(); Pump(650);
                    var copySurface=(Border)window.CopyLanAddressButton.Template.FindName("StudioSurface",window.CopyLanAddressButton);
                    Require(((SolidColorBrush)copySurface.Background).Color==(Color)window.FindResource("StudioSurfaceColor"),"复制链接按钮底色与表单不统一");
                    Require(((SolidColorBrush)window.CopyLanAddressButton.Foreground).Color==(Color)window.FindResource("StudioTextColor"),"复制链接文字颜色不可读");
                    foreach (var combo in new[]{window.ThemeModeCombo,window.RefreshIntervalCombo})
                    {
                        var toggleSurface=(System.Windows.Controls.Primitives.ToggleButton)combo.Template.FindName("DropDownToggle",combo);
                        var face=(Border)toggleSurface.Template.FindName("StudioSurface",toggleSurface);
                        var rendered=((SolidColorBrush)face.Background).Color;
                        Require(rendered == (Color)window.FindResource("StudioSurfaceColor") || rendered == (Color)window.FindResource("StudioHoverColor") || rendered == (Color)window.FindResource("StudioKeyboardColor"), $"下拉背景未跟随主题: {mode} {rendered}");
                    }
                    VerifySegmentText(segments);
                    var startupKnob = (Border)window.StartWithWindowsCheck.Template.FindName("PART_Knob", window.StartWithWindowsCheck);
                    var startupTrack = (Border)window.StartWithWindowsCheck.Template.FindName("Track", window.StartWithWindowsCheck);
                    Require(((SolidColorBrush)startupTrack.Background).Color == (Color)window.FindResource("SwitchOnColor"), "开启的开关必须跟随当前主题蓝色");
                    window.StartWithWindowsCheck.IsChecked = false; Pump(220);
                    Require(((SolidColorBrush)startupTrack.Background).Color.A is > 0 and < 255, "关闭的开关轨道应透出背景材质");
                    CaptureElement(window.StartWithWindowsCheck, $"switch-off-{mode}.png");
                    Capture(window, $"settings-{mode}-{size.Item1}-switch-off.png", mode);
                    window.StartWithWindowsCheck.IsChecked = true; Pump(220);
                    Require(((SolidColorBrush)startupTrack.Background).Color == (Color)window.FindResource("SwitchOnColor"), "重新开启开关后没有恢复主题色");
                    Require(Math.Abs(((TranslateTransform)startupKnob.RenderTransform).X - 20) < 0.1,
                        $"启用开关位置错误: {((TranslateTransform)startupKnob.RenderTransform).X}");
                    Capture(window, $"settings-{mode}-{size.Item1}-top.png", mode);
                    window.DoNotDisturbStartBox.BringIntoView(); Pump(150);
                    Capture(window, $"settings-{mode}-{size.Item1}-middle.png", mode);
                    window.SettingsPanel.ScrollToBottom(); Pump(250);
                    Capture(window, $"settings-{mode}-{size.Item1}-bottom.png", mode);
                    Require(window.SettingsPanel.ExtentWidth <= window.SettingsPanel.ViewportWidth + 1, "设置页发生横向溢出");
                    Require(window.LanAddressBox.ActualWidth > 180, "局域网链接区域被挤压");
                }
                Console.WriteLine("Apple controls regression PASS: direct ranges, rapid segments, keyboard, five switches, four connection fields, doubled proximity area, reversible dropdown motion, UIA toggle, dependent fields, no QR, light/dark 1180/980 layouts.");
            }
            catch (Exception e) { failure = e; }
            finally { window?.PrepareForExit(); window?.Close(); app?.Shutdown(); }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure is null) return 0;
        Console.Error.WriteLine(failure); return 1;
    }

    private static void PumpUntil(Func<bool> condition)
    {
        var frame=new DispatcherFrame();
        var timer=new DispatcherTimer(DispatcherPriority.Send) { Interval=TimeSpan.FromSeconds(2) };
        EventHandler render=(_,_)=>{if(condition()) frame.Continue=false;};
        timer.Tick+=(_,_)=>frame.Continue=false;
        CompositionTarget.Rendering+=render;timer.Start();
        try { Dispatcher.PushFrame(frame); }
        finally { timer.Stop();CompositionTarget.Rendering-=render; }
    }

    private static void RenderViewport(RenderTargetBitmap bitmap,FrameworkElement viewport)
    {
        var visual=new DrawingVisual();
        using(var dc=visual.RenderOpen())
            dc.DrawRectangle(new VisualBrush(viewport) {ViewboxUnits=BrushMappingMode.Absolute,Viewbox=new Rect(0,0,viewport.ActualWidth,viewport.ActualHeight),Stretch=Stretch.Fill},null,new Rect(0,0,viewport.ActualWidth,viewport.ActualHeight));
        bitmap.Render(visual);
    }

    private static void VerifyTextEditing(MainWindow window)
    {
        foreach(var source in PresentationSource.CurrentSources.OfType<System.Windows.Interop.HwndSource>())
        {
            var root=source.RootVisual;
            Console.WriteLine($"SOURCE {source.Handle}: {root?.GetType().Name}");
            if(root is null)continue;
            foreach(var tip in Descendants<ToolTip>(root))Console.WriteLine($"TIP open={tip.IsOpen},content={tip.Content},target={tip.PlacementTarget},size={tip.ActualWidth}/{tip.ActualHeight}");
        }
        typeof(MainWindow).GetField("_isPopulatingDeviceSettings",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(window,true);
        window.DetailPanel.ScrollToBottom();Pump(120);
        var box=window.CustomNameBox;box.Text="Redmi AX3000 路由器";box.Focus();Pump(120);
        foreach(var name in new[]{"华为 Mate 30","Redmi Note 11T Pro+"})
        {
            box.Text=name;box.CaretIndex=0;Pump(120);
            var captionInk=FieldLabelAlignment.TextInkBounds(window.CustomNameLabel);var valueInk=FieldLabelAlignment.TextInkBounds(box);
            var captionX=window.CustomNameLabel.TranslatePoint(new Point(captionInk.Left,captionInk.Top),box).X;
            Console.WriteLine($"CUSTOM INK {name}: label={captionX:F2},text={valueInk.Left:F2}");
            Require(!captionInk.IsEmpty && !valueInk.IsEmpty && Math.Abs(captionX-valueInk.Left)<.1,"中文与英文名称标签应对齐真实笔画");
        }
        box.Text="Redmi AX3000 路由器";Pump(100);
        var host=(ScrollViewer)box.Template.FindName("PART_ContentHost",box);
        Console.WriteLine($"INPUT HOST style={host.Style},content={host.Content?.GetType().Name},size={host.ActualWidth}/{host.ActualHeight}");
        // An isolated WPF input device supplies coordinates and button state;
        // the test never moves or clicks the user's physical mouse.
        var assembly=System.Reflection.Emit.AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("EditingInput"),System.Reflection.Emit.AssemblyBuilderAccess.Run);
        assembly.SetCustomAttribute(new System.Reflection.Emit.CustomAttributeBuilder(typeof(System.Runtime.CompilerServices.IgnoresAccessChecksToAttribute).GetConstructor([typeof(string)])!,[typeof(MouseDevice).Assembly.GetName().Name!]));
        var builder=assembly.DefineDynamicModule("Input").DefineType("EditingMouse",TypeAttributes.Public,typeof(MouseDevice));
        var pressed=builder.DefineField("Pressed",typeof(bool),FieldAttributes.Public);
        var ctor=builder.DefineConstructor(MethodAttributes.Public,CallingConventions.Standard,[typeof(InputManager)]);
        var il=ctor.GetILGenerator();il.Emit(System.Reflection.Emit.OpCodes.Ldarg_0);il.Emit(System.Reflection.Emit.OpCodes.Ldarg_1);il.Emit(System.Reflection.Emit.OpCodes.Call,typeof(MouseDevice).GetConstructors(BindingFlags.Instance|BindingFlags.NonPublic).Single());il.Emit(System.Reflection.Emit.OpCodes.Ret);
        var baseState=typeof(MouseDevice).GetMethod("GetButtonStateFromSystem",BindingFlags.Instance|BindingFlags.NonPublic)!;
        var state=builder.DefineMethod(baseState.Name,MethodAttributes.Assembly|MethodAttributes.Virtual,typeof(MouseButtonState),[typeof(MouseButton)]);
        il=state.GetILGenerator();il.Emit(System.Reflection.Emit.OpCodes.Ldarg_0);il.Emit(System.Reflection.Emit.OpCodes.Ldfld,pressed);il.Emit(System.Reflection.Emit.OpCodes.Ret);builder.DefineMethodOverride(state,baseState);
        var mouseType=builder.CreateType()!;var mouse=(MouseDevice)Activator.CreateInstance(mouseType,[InputManager.Current])!;
        for(var index=2;index<box.Text.Length;index+=4)
        {
            box.Select(0,0);window.SidebarToggleButton.Focus();Pump(20);var caret=box.GetRectFromCharacterIndex(index);var click=new Point(caret.X+.1,index==2 ? 5 : caret.Y+caret.Height/2);var point=box.TranslatePoint(click,window);
            var flags=BindingFlags.Instance|BindingFlags.NonPublic;var dpi=VisualTreeHelper.GetDpi(window);
            var sourceField=typeof(MouseDevice).GetField("_inputSource",flags)!;
            var source=PresentationSource.FromVisual(window);
            sourceField.SetValue(mouse,sourceField.FieldType.IsInstanceOfType(source) ? source : Activator.CreateInstance(sourceField.FieldType,flags|BindingFlags.Public,null,[source],null));
            typeof(MouseDevice).GetField("_lastPosition",flags)!.SetValue(mouse,new Point(point.X*dpi.DpiScaleX,point.Y*dpi.DpiScaleY));
            mouseType.GetField("Pressed")!.SetValue(mouse,true);
            var down=new MouseButtonEventArgs(mouse,Environment.TickCount,MouseButton.Left){RoutedEvent=Mouse.MouseDownEvent};
            typeof(MouseButtonEventArgs).GetField("_count",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(down,1);
            var hit=(UIElement)box.InputHitTest(click);
            hit.RaiseEvent(new MouseButtonEventArgs(mouse,Environment.TickCount,MouseButton.Left){RoutedEvent=Mouse.PreviewMouseDownEvent});
            Console.WriteLine($"EVENT POS {down.GetPosition(box)}");hit.RaiseEvent(down);
            mouseType.GetField("Pressed")!.SetValue(mouse,false);
            hit.RaiseEvent(new MouseButtonEventArgs(mouse,Environment.TickCount,MouseButton.Left){RoutedEvent=Mouse.MouseUpEvent});Pump(320);
            Console.WriteLine($"CLICK expected={index},caret={box.CaretIndex},selection={box.SelectionStart}/{box.SelectionLength},hit={box.InputHitTest(new Point(caret.X+.1,caret.Y+caret.Height/2))?.GetType().Name}");
            Require(Math.Abs(box.CaretIndex-index)<=1 && box.SelectionLength==0,"单击输入框应定位光标，不能选中从开头到点击处的文字");
        }
        void PositionAt(int index)
        {
            var caret=box.GetRectFromCharacterIndex(index);var point=box.TranslatePoint(new Point(caret.X+.1,caret.Y+caret.Height/2),window);var dpi=VisualTreeHelper.GetDpi(window);
            var sourceField=typeof(MouseDevice).GetField("_inputSource",BindingFlags.Instance|BindingFlags.NonPublic)!;var source=PresentationSource.FromVisual(window);
            sourceField.SetValue(mouse,Activator.CreateInstance(sourceField.FieldType,BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public,null,[source],null));
            typeof(MouseDevice).GetField("_lastPosition",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(mouse,new Point(point.X*dpi.DpiScaleX,point.Y*dpi.DpiScaleY));
        }
        PositionAt(2);mouseType.GetField("Pressed")!.SetValue(mouse,true);
        var dragDown=new MouseButtonEventArgs(mouse,Environment.TickCount,MouseButton.Left){RoutedEvent=Mouse.MouseDownEvent};
        var dragHit=host.Content as UIElement ?? throw new InvalidOperationException("缺少文字宿主");
        typeof(MouseButtonEventArgs).GetField("_count",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(dragDown,1);dragHit.RaiseEvent(dragDown);
        typeof(MouseDevice).GetField("_mouseCapture",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(mouse,box);
        Console.WriteLine($"CAPTURE {box.CaptureMouse()},position={dragDown.GetPosition(box)}");
        Console.WriteLine($"DRAG down caret={box.CaretIndex},capture={Mouse.Captured?.GetType().Name},fake={mouse.Captured?.GetType().Name}");
        PositionAt(6);dragHit.RaiseEvent(new MouseEventArgs(mouse,Environment.TickCount){RoutedEvent=Mouse.MouseMoveEvent});
        mouseType.GetField("Pressed")!.SetValue(mouse,false);dragHit.RaiseEvent(new MouseButtonEventArgs(mouse,Environment.TickCount,MouseButton.Left){RoutedEvent=Mouse.MouseUpEvent});Pump(30);
        Console.WriteLine($"DRAG selection={box.SelectionStart}/{box.SelectionLength}");
        box.ReleaseMouseCapture();
        typeof(MouseDevice).GetField("_mouseCapture",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(mouse,null);
        Require(Mouse.Captured!=box,"输入回归结束必须释放鼠标捕获");
        Require(box.SelectionStart==2 && box.SelectionLength==4,"拖选应从按下处开始，而非从文字开头开始");
        box.Select(3,4);var edited=box.Text;
        typeof(MainWindow).GetMethod("UpdateSelectedDevice",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,[window.SelectedDevice,false]);Pump(50);
        Require(box.Text==edited && box.SelectionStart==3 && box.SelectionLength==4,"刷新不能重置正在编辑的文本和选择范围");
        box.RaiseEvent(new TextCompositionEventArgs(Keyboard.PrimaryDevice,new TextComposition(InputManager.Current,box,"X")){RoutedEvent=TextCompositionManager.TextInputEvent});
        Require(box.Text==edited.Remove(3,4).Insert(3,"X") && box.CaretIndex==4,"文字输入应在当前光标或选择范围中发生");
        var emptyTip=new ToolTip();emptyTip.IsOpen=true;Pump(30);
        Require(!emptyTip.IsOpen,"空提示框不能残留在桌面左上角");
        var validTip=new ToolTip {Content="有效提示",PlacementTarget=box,Placement=System.Windows.Controls.Primitives.PlacementMode.Bottom};
        validTip.IsOpen=true;Pump(30);Require(validTip.IsOpen,"有效提示仍应正常打开");validTip.IsOpen=false;
        var metric=(Border)window.DownloadTotalMetricContent.Parent;var boundTip=(ToolTip)metric.ToolTip;
        boundTip.PlacementTarget=metric;boundTip.IsOpen=true;Pump(30);
        Require(boundTip.IsOpen && boundTip.Content is string content && content.Length>0,"累计流量的绑定提示仍应显示正确日期范围");boundTip.IsOpen=false;
        Console.WriteLine("Text editing PASS: full input hit area, character caret positions, refresh selection retention, empty/valid tooltip lifecycle.");
    }

    private static void VerifyNativeRing(MainWindow window)
    {
        window.Left=80;window.Top=40;window.Width=1180;window.Height=940;
        ThemeService.Apply(ThemeService.LightMode);MicaWindowService.Apply(window,false);
        var selected=window.SelectedDevice!;
        var device=new LanDeviceMonitor.ViewModels.DeviceItemViewModel(new LanDeviceMonitor.Models.DeviceViewData {
            Profile=selected.Profile,Identity=selected.Data.Identity,
            Sessions=Enumerable.Range(0,5).Select(i=>new LanDeviceMonitor.Models.DeviceSession {OnlineAt=DateTimeOffset.Now.AddHours(-i*4-1),OfflineAt=i==0 ? null : DateTimeOffset.Now.AddHours(-i*4)}).ToArray()});
        typeof(MainWindow).GetMethod("UpdateSelectedDevice",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,[device,true]);Pump(1000);
        var button=window.OlderHistoryToggle;var surface=(Border)button.Template.FindName("CircleSurface",button);
        var original=surface.BorderBrush;
        window.DetailPanel.ScrollToVerticalOffset(240);Pump(200);
        var host=(Grid)surface.Parent;var index=host.Children.IndexOf(surface);
        var legacy=new RoundedCardBorder {Background=surface.Background,BorderBrush=Brushes.Black,BorderThickness=surface.BorderThickness,CornerRadius=surface.CornerRadius};
        host.Children.RemoveAt(index);host.Children.Insert(index,legacy);Pump(200);CaptureNative(window,"ring-legacy-native.png");
        host.Children.Remove(legacy);host.Children.Insert(index,surface);Pump(200);
        foreach(var offset in new[]{200d,200.4,200.8,220d,240d,260d})
        {
            window.DetailPanel.ScrollToVerticalOffset(offset);Pump(200);
            CaptureNative(window,$"ring-native-{offset}.png");
            surface.SetCurrentValue(Border.BorderBrushProperty,Brushes.Black);Pump(150);
            CaptureNative(window,$"ring-black-{offset}.png");
            using(var shot=new System.Drawing.Bitmap(Path.Combine(Environment.CurrentDirectory,".tools","ui-dev249",$"ring-black-{offset}.png")))
            {
                GetWindowRect(new System.Windows.Interop.WindowInteropHelper(window).Handle,out var rect);
                var center=button.PointToScreen(new Point(button.ActualWidth/2,button.ActualHeight/2));var scale=VisualTreeHelper.GetDpi(button).DpiScaleX;
                var cx=center.X-rect.Left;var cy=center.Y-rect.Top;var radius=button.ActualWidth*scale/2-1.5;
                var worst=0;
                for(var degree=0;degree<360;degree+=2)
                {
                    var angle=degree*Math.PI/180;var darkest=255;
                    for(var dr=-1d;dr<=1;dr+=.25)
                    {
                        var pixel=shot.GetPixel((int)Math.Floor(cx+(radius+dr)*Math.Cos(angle)),(int)Math.Floor(cy+(radius+dr)*Math.Sin(angle)));
                        darkest=Math.Min(darkest,(pixel.R+pixel.G+pixel.B)/3);
                    }
                    worst=Math.Max(worst,darkest);Require(darkest<175,$"原生圆环在 offset={offset},angle={degree} 出现断口");
                }
                Console.WriteLine($"NATIVE RING contour offset={offset},worst luminance={worst}");
            }
            surface.SetCurrentValue(Border.BorderBrushProperty,original);Pump(80);
            Console.WriteLine($"RING native offset={offset},screen={button.PointToScreen(new Point())},size={button.ActualWidth}/{button.ActualHeight}");
        }
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump(500);window.DetailPanel.ScrollToVerticalOffset(420);Pump(200);CaptureNative(window,"ring-up-native.png");
    }

    private static void VerifyHistoryDisclosure(MainWindow window)
    {
        var today=DateTimeOffset.Now.Date;
        DateTimeOffset Day(int offset)=>new(today.AddDays(offset).AddHours(9),TimeZoneInfo.Local.GetUtcOffset(today));
        var sessions=new[]{
            new LanDeviceMonitor.Models.DeviceSession {OnlineAt=Day(0),OfflineAt=null},
            new LanDeviceMonitor.Models.DeviceSession {OnlineAt=Day(-1),OfflineAt=Day(-1).AddHours(1)},
            new LanDeviceMonitor.Models.DeviceSession {OnlineAt=Day(-10),OfflineAt=Day(-2)},
            new LanDeviceMonitor.Models.DeviceSession {OnlineAt=Day(-4),OfflineAt=Day(-3)},
            new LanDeviceMonitor.Models.DeviceSession {OnlineAt=Day(-30),OfflineAt=null}};
        Require(new LanDeviceMonitor.ViewModels.SessionItemViewModel(sessions[0]).OnlineDateText=="今天" &&
            new LanDeviceMonitor.ViewModels.SessionItemViewModel(sessions[1]).OnlineDateText=="昨天" &&
            new LanDeviceMonitor.ViewModels.SessionItemViewModel(sessions[2]).OfflineDateText=="前天","历史日期应显示今天、昨天、前天");
        var selected=window.SelectedDevice!;
        var data=new LanDeviceMonitor.Models.DeviceViewData {Profile=selected.Profile,Identity=selected.Data.Identity,Sessions=sessions};
        var device=new LanDeviceMonitor.ViewModels.DeviceItemViewModel(data);
        typeof(MainWindow).GetMethod("UpdateSelectedDevice",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,[device,true]);Pump(250);
        Require((int)typeof(MainWindow).GetProperty("RecentSessionCount")!.GetValue(window)! == 3,"任一端落在最近三天的会话应外显，其余应折叠");
        var reveal=(FrameworkElement)window.FindName("SessionHistoryDisclosure");
        var toggle=(System.Windows.Controls.Button)window.FindName("OlderHistoryToggle");
        var collapse=toggle;
        Require(toggle.Content is RotatingChevron && toggle.Width==36 && toggle.Height==36,"历史按钮应为沿用底部样式的符号圆按钮");
        Require(window.FindName("CollapseHistoryButton") is null,"展开与收起应共用一个按钮");
        var glyph=(RotatingChevron)toggle.Content;
        Require(toggle.TranslatePoint(new Point(),(FrameworkElement)reveal.Parent).Y>=reveal.ActualHeight,"折叠按钮应放在记录下方");
        var collapsed=reveal.ActualHeight;
        void VerifyChevronCenter()
        {
            window.DetailPanel.ScrollToBottom();Pump(100);
            var dpi=VisualTreeHelper.GetDpi(glyph);var bitmap=new RenderTargetBitmap((int)Math.Ceiling(glyph.ActualWidth*dpi.DpiScaleX),(int)Math.Ceiling(glyph.ActualHeight*dpi.DpiScaleY),dpi.PixelsPerInchX,dpi.PixelsPerInchY,PixelFormats.Pbgra32);
            bitmap.Render(glyph);var pixels=new byte[bitmap.PixelWidth*bitmap.PixelHeight*4];bitmap.CopyPixels(pixels,bitmap.PixelWidth*4,0);
            double total=0,x=0,y=0;for(var row=0;row<bitmap.PixelHeight;row++)for(var col=0;col<bitmap.PixelWidth;col++){var alpha=pixels[(row*bitmap.PixelWidth+col)*4+3];total+=alpha;x+=(col+.5)*alpha;y+=(row+.5)*alpha;}
            var origin=glyph.TranslatePoint(new Point(),toggle);var expectedX=(toggle.ActualWidth/2-origin.X)*dpi.DpiScaleX;var expectedY=(toggle.ActualHeight/2-origin.Y)*dpi.DpiScaleY;
            Console.WriteLine($"CHEVRON angle={glyph.Angle:F1},ink={x/total:F2}/{y/total:F2},center={expectedX:F2}/{expectedY:F2}");
            Require(total>0 && Math.Abs(x/total-expectedX)<.35 && Math.Abs(y/total-expectedY)<.35,"箭头可见笔画应在圆按钮中光学居中");
        }
        VerifyChevronCenter();
        foreach(var width in new[]{1181d,1182d,1191d,430d,1180d}){window.Width=width;Pump(120);VerifyChevronCenter();}
        var ring=(Border)toggle.Template.FindName("CircleSurface",toggle);
        Require(ring is CircleButtonBorder,"圆形按钮应使用连续轮廓绘制");
        var originalBackground=ring.Background;var originalOutline=ring.BorderBrush;
        try
        {
            ring.SetCurrentValue(Border.BackgroundProperty,Brushes.Transparent);ring.SetCurrentValue(Border.BorderBrushProperty,Brushes.Black);Pump(40);
            var dpi=VisualTreeHelper.GetDpi(ring);var count=(int)Math.Round(ring.ActualWidth*dpi.DpiScaleX);var bitmap=new RenderTargetBitmap(count,count,dpi.PixelsPerInchX,dpi.PixelsPerInchY,PixelFormats.Pbgra32);bitmap.Render(ring);
            var pixels=new byte[count*count*4];bitmap.CopyPixels(pixels,count*4,0);
            for(var degree=0;degree<360;degree+=2)
            {
                var radians=degree*Math.PI/180;var px=(int)Math.Floor(count/2d+(count/2d-.7)*Math.Cos(radians));var py=(int)Math.Floor(count/2d+(count/2d-.7)*Math.Sin(radians));
                var alpha=0;for(var y=Math.Max(0,py-1);y<=Math.Min(count-1,py+1);y++)for(var x=Math.Max(0,px-1);x<=Math.Min(count-1,px+1);x++)alpha=Math.Max(alpha,pixels[(y*count+x)*4+3]);
                Require(alpha>30,$"圆环轮廓不能在 {degree} 度出现断口");
            }
        }
        finally{ring.SetCurrentValue(Border.BackgroundProperty,originalBackground);ring.SetCurrentValue(Border.BorderBrushProperty,originalOutline);}
        var historyCard=window.SessionHistoryCard;
        var lastContainer=(FrameworkElement)window.SessionHistoryItems.ItemContainerGenerator.ContainerFromIndex(2);
        var lastRow=Descendants<Border>(lastContainer).First(b=>b.CornerRadius.TopLeft==18);
        var gapAbove=toggle.TranslatePoint(new Point(),historyCard).Y-lastRow.TranslatePoint(new Point(0,lastRow.ActualHeight),historyCard).Y;
        var gapBelow=historyCard.ActualHeight-toggle.TranslatePoint(new Point(0,toggle.ActualHeight),historyCard).Y;
        Console.WriteLine($"HISTORY gaps above={gapAbove:F2},below={gapBelow:F2}");
        Require(Math.Abs(gapAbove-8)<=.800001 && Math.Abs(gapBelow-8)<=.800001,"按钮上下间距应与记录之间的八 DIP 间距一致");
        var angles=new List<double>();EventHandler sampleRotation=(_,_)=>angles.Add(glyph.Angle);CompositionTarget.Rendering+=sampleRotation;
        toggle.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));Pump(90);
        var mid=reveal.ActualHeight;
        Require(!SystemParameters.ClientAreaAnimation || mid>collapsed,"展开应出现连续中间高度");
        Pump(450);var expanded=reveal.ActualHeight;
        CompositionTarget.Rendering-=sampleRotation;
        Require(!SystemParameters.ClientAreaAnimation || angles.Any(a=>a>1 && a<179),"箭头旋转应包含中间帧");
        Require(Math.Abs(glyph.Angle-180)<.01 && !glyph.HasAnimatedProperties,"旋转结束应到达向上状态并释放时钟");VerifyChevronCenter();
        Require(expanded>collapsed+80,"展开应显示更早的全部会话");
        typeof(MainWindow).GetMethod("UpdateSelectedDevice",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,[device,false]);Pump(50);
        Require(Math.Abs(reveal.ActualHeight-expanded)<1,"同设备刷新不能收起正在查看的旧记录");
        collapse.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));Pump(70);
        var shrinking=reveal.ActualHeight;
        var reversingAngle=glyph.Angle;
        toggle.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
        Require(Math.Abs(glyph.Angle-reversingAngle)<.01,"反向旋转应继承当前角度，不能跳回起点");
        Require(Math.Abs(reveal.ActualHeight-shrinking)<1,"反向展开不能重置当前高度");Pump(500);
        Require(Math.Abs(reveal.ActualHeight-expanded)<1,"反向展开应回到完整高度");
        collapse.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));Pump(450);
        Require(Math.Abs(reveal.ActualHeight-collapsed)<1,"收起应恢复最近三天的高度");
        foreach(var title in new[]{window.SessionHistoryTitleText,window.TrafficSummaryTitleText})
        {
            var actual=title.TranslatePoint(new Point(),window.DetailSurface).X;
            var expected=window.ConnectionStateInfoBlock.TranslatePoint(new Point(),window.DetailSurface).X;
            Console.WriteLine($"TITLE {title.Name}: actual={actual:F2},expected={expected:F2}");
            Require(Math.Abs(actual-expected)<=.1,"小标题与卡片内部内容左边界对齐");
        }
        var otherProfile=new LanDeviceMonitor.Models.DeviceProfile {MacAddress="00:00:00:00:90:01",LastKnownOnline=true};
        var other=new LanDeviceMonitor.ViewModels.DeviceItemViewModel(new LanDeviceMonitor.Models.DeviceViewData {Profile=otherProfile,Identity=selected.Data.Identity,Sessions=sessions});
        toggle.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));Pump(450);
        typeof(MainWindow).GetMethod("UpdateSelectedDevice",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,[other,true]);Pump(150);
        Require(Math.Abs(reveal.ActualHeight-collapsed)<1,"切换设备后旧历史默认收起");
        var update=typeof(MainWindow).GetMethod("UpdateSelectedDevice",BindingFlags.Instance|BindingFlags.NonPublic)!;
        var old=sessions.Select(s=>new LanDeviceMonitor.Models.DeviceSession {OnlineAt=Day(-20),OfflineAt=Day(-19)}).ToArray();
        for(var count=0;count<=5;count++)
        {
            var sample=new LanDeviceMonitor.ViewModels.DeviceItemViewModel(new LanDeviceMonitor.Models.DeviceViewData {Profile=new LanDeviceMonitor.Models.DeviceProfile {MacAddress=$"00:00:00:00:91:{count:X2}",LastKnownOnline=true},Identity=selected.Data.Identity,Sessions=old.Take(count).ToArray()});
            update.Invoke(window,[sample,true]);Pump(100);
            Require((int)typeof(MainWindow).GetProperty("VisibleHistoryCount")!.GetValue(window)! == Math.Min(3,count),$"旧历史 {count} 条也应至少外显三条或全部");
            Require((toggle.Visibility==Visibility.Visible)==(count>3),$"{count} 条历史的折叠按钮可见性");
            var full=((Decorator)reveal).Child.DesiredSize.Height;
            var expected=count==0 ? 0 : Math.Max(0,full*Math.Min(3,count)/count-8);
            Require(Math.Abs(reveal.ActualHeight-expected)<1,$"{count} 条历史的实际外显高度");
        }
        var many=new LanDeviceMonitor.ViewModels.DeviceItemViewModel(new LanDeviceMonitor.Models.DeviceViewData {Profile=otherProfile,Identity=selected.Data.Identity,Sessions=Enumerable.Range(0,47).Select(i=>new LanDeviceMonitor.Models.DeviceSession {OnlineAt=Day(-i-5),OfflineAt=Day(-i-5).AddHours(1)}).ToArray()});
        update.Invoke(window,[many,true]);Pump(100);
        foreach(var expected in new[]{18,33,47})
        {
            toggle.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));Pump(500);
            Require(window.VisibleHistoryCount==expected,$"每次最多新增十五段会话，应显示 {expected} 条");
            Require(window.HasExpandedHistory==(expected==47),"只有全部记录展开后才能收起");
            Require((glyph.Angle>179)==(expected==47),"部分展开保持向下符号，全部展开才切换向上符号");
            var full=((Decorator)reveal).Child.DesiredSize.Height;
            Require(Math.Abs(reveal.ActualHeight-Math.Max(0,full*expected/47-8))<1,"分页的实际可见高度应与显示条数一致");
            update.Invoke(window,[many,false]);Pump(30);Require(window.VisibleHistoryCount==expected,"轮询刷新必须保留分页进度");
        }
        Require(toggle.Visibility==Visibility.Visible && window.HasExpandedHistory,"全部展开后同一按钮应切换为收起");
        collapse.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));Pump(450);
        Require(window.VisibleHistoryCount==3,"收起应恢复最近三条");
        Require(Math.Abs(glyph.Angle)<.01,"收起后应恢复展开符号");
        update.Invoke(window,[other,true]);Pump(150);
        window.DetailPanel.ScrollToBottom();Pump(100);
        var history=(ItemsControl)window.FindName("SessionHistoryItems");
        var relativeDates=Descendants<TextBlock>(history).Where(t=>t.Text=="前天").ToArray();
        Require(relativeDates.Length>0,"混合日期宽度用例应已实现并显示在视觉树中");
        foreach(var date in relativeDates)
        {
            Require(date.Parent is StackPanel,"每个端点的日期与时间应独立排版");
            var line=(StackPanel)date.Parent;var time=line.Children.OfType<TextBlock>().Last();
            Require(Math.Abs(time.TranslatePoint(new Point(),line).X-date.ActualWidth-10)<=1,"相对日期与时间应保持固定十像素间距");
        }
        window.DetailPanel.ScrollToVerticalOffset(300);Pump(100);Capture(window,"history-collapsed.png",ThemeService.LightMode);
        toggle.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));Pump(500);Capture(window,"history-expanded.png",ThemeService.LightMode);
        Console.WriteLine($"History disclosure PASS: dates, endpoint classification, collapsed={collapsed:F1},middle={mid:F1},expanded={expanded:F1}, reversible animation and title alignment.");
    }

    private static void VerifyStatusAppearance(MainWindow window)
    {
        KeepTestPopupsOffDesktop(window);
        var failures=new List<string>();
        void Check(bool passed,string name) {Console.WriteLine($"{(passed ? "PASS" : "FAIL")}: {name}");if(!passed)failures.Add(name);}
        var row=(ListBoxItem)window.DeviceList.ItemContainerGenerator.ContainerFromIndex(0);
        var elapsedLabel=Descendants<TextBlock>(row).First(t=>t.Name=="DeviceListRate");
        Check(elapsedLabel.Text.Contains("前") || elapsedLabel.Text=="刚刚","在线列表右侧显示上线距今，而非速率");
        window.DetailPanel.ScrollToBottom();Pump(180);
        var notification=Descendants<TextBlock>(window.DetailPanel).Single(t=>t.Text=="当设备上下线时，推送 Windows 通知");
        var notificationRow=(FrameworkElement)notification.Parent;
        var card=Descendants<RoundedCardBorder>(window.DetailPanel).First(b=>Descendants<TextBlock>(b).Contains(notification));
        var rowTop=notificationRow.TranslatePoint(new Point(),card).Y;
        var label=window.CustomNameLabel;var box=window.CustomNameBox;
        var customStart=FieldLabelAlignment.TextInkBounds(box).Left;
        Require(Math.Abs(label.TranslatePoint(new Point(FieldLabelAlignment.TextInkBounds(label).Left,0),box).X-customStart)<=.1,"自定义名称标签应对齐实际文字笔画起点");
        var icon=Descendants<Border>(window.DeviceTypeCombo).First(b=>b.Width==26);
        var iconStart=icon.TranslatePoint(new Point(),window.DeviceTypeCombo);
        Require(Math.Abs(window.DeviceTypeLabel.TranslatePoint(new Point(),window.DeviceTypeCombo).X-iconStart.X)<=.800001,"类型标签应对齐图标起点");
        Console.WriteLine($"TYPE inset left={iconStart.X:F2},top={iconStart.Y:F2}");
        Require(Math.Abs(iconStart.X-iconStart.Y)<=.800001,"类型触发框图标左侧与上下留白应一致");
        var gapBelow=card.ActualHeight-rowTop-notificationRow.ActualHeight;
        var separator=Descendants<Border>(card).Last(b=>b.Height==1);
        var separatorBottom=separator.TranslatePoint(new Point(0,separator.ActualHeight),card).Y;
        Check(rowTop-separatorBottom<=20 && gapBelow<=20,$"通知行留白接近设置页: 上={rowTop-separatorBottom:F1},下={gapBelow:F1}");
        var hostCanvas=new Canvas {Background=Brushes.Transparent,UseLayoutRounding=false,SnapsToDevicePixels=false};
        var outline=new RoundedCardBorder {Width=160,Height=100,Background=Brushes.Transparent,BorderBrush=Brushes.Black,BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(20),UseLayoutRounding=false,SnapsToDevicePixels=false};
        hostCanvas.Children.Add(outline);
        var host=new Window {Width=230,Height=170,Left=-15000,Top=-15000,ShowActivated=false,ShowInTaskbar=false,Content=hostCanvas};
        try
        {
            host.Show();host.UpdateLayout();var dpi=VisualTreeHelper.GetDpi(host);
            for(var phase=0;phase<4;phase++)
            {
                Canvas.SetLeft(outline,20+phase*.25/dpi.DpiScaleX);Canvas.SetTop(outline,20);host.UpdateLayout();
                var bitmap=new RenderTargetBitmap((int)Math.Ceiling(hostCanvas.ActualWidth*dpi.DpiScaleX),(int)Math.Ceiling(hostCanvas.ActualHeight*dpi.DpiScaleY),dpi.PixelsPerInchX,dpi.PixelsPerInchY,PixelFormats.Pbgra32);
                bitmap.Render(hostCanvas);var pixels=new byte[bitmap.PixelWidth*bitmap.PixelHeight*4];bitmap.CopyPixels(pixels,bitmap.PixelWidth*4,0);
                var location=outline.TranslatePoint(new Point(),hostCanvas);
                var x=(int)Math.Floor(location.X*dpi.DpiScaleX);var y=(int)Math.Floor(location.Y*dpi.DpiScaleY);
                var middleY=y+(int)(outline.ActualHeight*dpi.DpiScaleY/2);var middleX=x+(int)(outline.ActualWidth*dpi.DpiScaleX/2);
                var side=Enumerable.Range(x-2,6).Select(px=>(int)pixels[(middleY*bitmap.PixelWidth+px)*4+3]).ToArray();
                var top=Enumerable.Range(y-2,6).Select(py=>(int)pixels[(py*bitmap.PixelWidth+middleX)*4+3]).ToArray();
                Check(side.Count(a=>a>15)==top.Count(a=>a>15) && side.Max()>235,$"侧边与顶边都是完整单像素描边 phase={phase}: side=[{string.Join(',',side)}], top=[{string.Join(',',top)}]");
            }
            outline.Background=new SolidColorBrush(Color.FromArgb(112,255,255,255));
            outline.BorderBrush=new SolidColorBrush(Color.FromArgb(154,212,212,212));
            Canvas.SetLeft(outline,20);Canvas.SetTop(outline,20);host.UpdateLayout();
            var translucent=new RenderTargetBitmap((int)Math.Ceiling(hostCanvas.ActualWidth*dpi.DpiScaleX),(int)Math.Ceiling(hostCanvas.ActualHeight*dpi.DpiScaleY),dpi.PixelsPerInchX,dpi.PixelsPerInchY,PixelFormats.Pbgra32);
            translucent.Render(hostCanvas);var rgba=new byte[translucent.PixelWidth*translucent.PixelHeight*4];translucent.CopyPixels(rgba,translucent.PixelWidth*4,0);
            var borderAlpha=rgba[((int)(70*dpi.DpiScaleY)*translucent.PixelWidth+(int)(20*dpi.DpiScaleX))*4+3];
            Check(Math.Abs(borderAlpha-154)<=1,$"半透明描边不应叠加背景而增亮: alpha={borderAlpha},expected=154");
        }
        finally {host.Close();}
        foreach(var mode in new[]{ThemeService.LightMode,ThemeService.DarkMode})
        {
            ThemeService.Apply(mode);
            window.Background=new SolidColorBrush(mode==ThemeService.DarkMode ? Color.FromRgb(18,18,20) : Color.FromRgb(242,243,247));
            Pump(100);
            var resting=((SolidColorBrush)window.FindResource("ScrollThumbBrush")).Color;
            var hover=((SolidColorBrush)window.FindResource("ScrollThumbHoverBrush")).Color;
            Check(resting.A<=85 && hover.A>resting.A,$"{mode}滚动条常态更淡且交互态可辨认: {resting}/{hover}");
            if(mode==ThemeService.DarkMode)
            {
                foreach(var key in new[]{"ShellPanelBrush","MicaCardBrush","SurfaceBrush","StudioSurfaceBrush","TimelineItemBrush","TimelineDurationBrush","SegmentTrackBrush","InputBorderBrush"})
                {
                    var neutral=((SolidColorBrush)window.FindResource(key)).Color;
                    Check(neutral.R==neutral.G && neutral.G==neutral.B,$"深色 {key} 不带蓝色底调: {neutral}");
                }
                var duration=((SolidColorBrush)window.FindResource("TimelineDurationBrush")).Color;
                var segment=((SolidColorBrush)window.FindResource("SegmentTrackBrush")).Color;
                Check(Math.Max(duration.R,Math.Max(duration.G,duration.B))<=75 && Math.Max(segment.R,Math.Max(segment.G,segment.B))<=36,"深色填充不再使用大片中灰");
            }
            window.SidebarRailSettingsButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));Pump(350);
            foreach(var pair in new[]{(window.RouterAddressLabel,window.RouterAddressBox),(window.LanAccessLabel,window.LanAddressBox)})
            {
                var oldVisibility=window.LanAccessDetails.Visibility;window.LanAccessDetails.Visibility=Visibility.Visible;window.UpdateLayout();
                Pump(80);var start=FieldLabelAlignment.TextInkBounds(pair.Item2).Left;
                Console.WriteLine($"FORM {pair.Item1.Name}: label={pair.Item1.TranslatePoint(new Point(FieldLabelAlignment.TextInkBounds(pair.Item1).Left,0),pair.Item2).X:F2},input={start:F2}");
                Require(Math.Abs(pair.Item1.TranslatePoint(new Point(FieldLabelAlignment.TextInkBounds(pair.Item1).Left,0),pair.Item2).X-start)<=.1,"设置标签应对齐实际文字笔画起点");
                window.LanAccessDetails.Visibility=oldVisibility;
            }
            var oldMask=window.SavedRouterPasswordMask.Visibility;window.SavedRouterPasswordMask.Visibility=Visibility.Visible;window.UpdateLayout();Pump(80);
            Require(Math.Abs(window.RouterPasswordLabel.TranslatePoint(new Point(FieldLabelAlignment.TextInkBounds(window.RouterPasswordLabel).Left,0),window.RouterPasswordField).X-FieldLabelAlignment.TextInkBounds(window.RouterPasswordField).Left+2)<=.1,"密码标签应位于实际密码笔画起点左侧2 DIP");
            window.SavedRouterPasswordMask.Visibility=oldMask;
            Capture(window,$"appearance-settings-{mode}.png",mode);
            Console.WriteLine($"SETTINGS {mode}: current={ThemeService.CurrentMode}, loaded={window.RouterAddressBox.IsLoaded}, background={((Border)window.RouterAddressBox.Template.FindName("StudioSurface",window.RouterAddressBox)).Background}, foreground={window.RouterAddressBox.Foreground}");
            typeof(MainWindow).GetMethod("ShowDevicesPanel",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,[false,false]);Pump(150);
            window.DetailPanel.ScrollToTop();Capture(window,$"appearance-detail-{mode}.png",mode);
        }
        ThemeService.Apply(ThemeService.LightMode);
        var pulse=typeof(MainWindow).GetMethod("PulseConnectionDot",BindingFlags.Instance|BindingFlags.NonPublic)!;
        pulse.Invoke(window,null);var scale=(ScaleTransform)window.ConnectionDot.RenderTransform;
        var samples=new List<double>();
        var clockField=typeof(MainWindow).GetField("_connectionBreathClock",BindingFlags.Instance|BindingFlags.NonPublic)!;
        var lateSamples=new List<double>();
        for(var i=0;i<20;i++)
        {
            Pump(50);samples.Add(scale.ScaleX);
            if(clockField.GetValue(window) is System.Windows.Media.Animation.AnimationClock clock && clock.CurrentProgress is >=.78 and <=.93)lateSamples.Add(scale.ScaleX);
        }
        if(SystemParameters.ClientAreaAnimation)
        {
            Check(samples.Max()<1.16 && samples.Max()>1.08,"呼吸峰值克制且可见");
            Check(lateSamples.Count>0 && lateSamples.All(value=>value>1.001),"呼气覆盖整个刷新周期，末段不提前停住");
            Check(samples.Select(v=>Math.Round(v,3)).Distinct().Count()>10,"呼吸包含连续中间帧");
        }
        var green=((SolidColorBrush)window.ConnectionDot.Fill).Color;
        Check(green.G<190 && green.G>150,"连接绿降低亮度并保留状态辨识");
        Require(window.ConnectionHalo.Opacity==.3 && Math.Abs(window.ConnectionDot.Width/window.ConnectionHalo.Width-.6)<.001,"状态灯必须匹配 SVG 的外圈透明度和内外圈比例");
        Require(window.ConnectionIndicator.Width==16 && window.ConnectionHalo.Width==16 && window.ConnectionDot.Width==9.6,"状态灯两层必须整体缩小 20%");
        Require(ReferenceEquals(window.ConnectionHalo.RenderTransform,window.ConnectionDot.RenderTransform),"SVG 双层圆点必须同步呼吸");
        var now=DateTimeOffset.Now;
        var onlineData=new LanDeviceMonitor.Models.DeviceViewData
        {
            Profile=new LanDeviceMonitor.Models.DeviceProfile {MacAddress="00:00:00:00:80:01",LastKnownOnline=true,LastSeenAt=now,LastOnlineSeenAt=now},
            Identity=new LanDeviceMonitor.Models.DeviceIdentity("手机","","在线时长测试"),
            Sessions=[new LanDeviceMonitor.Models.DeviceSession {OnlineAt=now.AddHours(-3),OfflineAt=now.AddHours(-2)},new LanDeviceMonitor.Models.DeviceSession {OnlineAt=now.AddMinutes(-10)}]
        };
        var online=new LanDeviceMonitor.ViewModels.DeviceItemViewModel(onlineData);
        Check(online.OnlineElapsedText=="10 分钟前","上线时间取当前未结束会话，不能取轮询时间或旧会话");
        var published=new List<string?>();online.PropertyChanged+=(_,e)=>published.Add(e.PropertyName);
        online.Update(new LanDeviceMonitor.Models.DeviceViewData {Profile=onlineData.Profile,Identity=onlineData.Identity,Sessions=[new LanDeviceMonitor.Models.DeviceSession {OnlineAt=now.AddHours(-2)}]});
        Check(online.OnlineElapsedText=="2 小时前" && published.Contains("OnlineElapsedText"),"上线时间变化刷新列表绑定");
        var curve=new ConnectionBreathAnimation {Duration=TimeSpan.FromSeconds(1)};
        Check(Math.Abs(curve.Sample(0)-1)<1e-10 && Math.Abs(curve.Sample(1)-1)<1e-10 &&
            Math.Abs(curve.Velocity(0))<1e-10 && Math.Abs(curve.Velocity(.42))<1e-10 && Math.Abs(curve.Velocity(1))<1e-10,"呼吸起点、峰值和终点速度都为零");
        if(SystemParameters.ClientAreaAnimation)
        {
            pulse.Invoke(window,null);Pump(240);
            var oldClock=(System.Windows.Media.Animation.AnimationClock)clockField.GetValue(window)!;
            var oldCurve=(ConnectionBreathAnimation)oldClock.Timeline;
            var beforeScale=scale.ScaleX;var beforeVelocity=oldCurve.Velocity(oldClock.CurrentProgress!.Value);
            pulse.Invoke(window,null);
            var newClock=(System.Windows.Media.Animation.AnimationClock)clockField.GetValue(window)!;
            var newCurve=(ConnectionBreathAnimation)newClock.Timeline;
            Check(Math.Abs(newCurve.Sample(0)-beforeScale)<1e-6 && Math.Abs(newCurve.Velocity(0)-beforeVelocity)<1e-6,"中途刷新继承当前尺寸和速度，无重置跳变");
            Pump(1100);
        }
        var framesRoot=Path.Combine(Environment.CurrentDirectory,".tools","ui-dev249","breath-frames");Directory.CreateDirectory(framesRoot);
        var frameTimes=new List<double>();
        window.LastRefreshText.Text=$"更新于 {DateTimeOffset.Now:HH:mm:ss}";
        pulse.Invoke(window,null);var stopwatch=System.Diagnostics.Stopwatch.StartNew();
        for(var frame=0;frame<36;frame++)
        {
            Pump(25);frameTimes.Add(stopwatch.Elapsed.TotalSeconds);
            CaptureElement((FrameworkElement)window.ConnectionIndicator.Parent,$"breath-frames/frame-{frame:D3}.png");
        }
        File.WriteAllText(Path.Combine(framesRoot,"times.json"),System.Text.Json.JsonSerializer.Serialize(frameTimes));
        window.Background=new SolidColorBrush(Color.FromRgb(242,243,247));
        window.Width=380;Pump(180);window.DeviceList.SelectedIndex=1;
        typeof(MainWindow).GetMethod("OpenCompactDetail",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,null);
        Pump(650);window.DetailPanel.ScrollToBottom();Pump(150);
        Require(window.DetailPanel.IsVisible,"窄窗口通知检查必须使用可见详情");
        var narrowNotification=Descendants<TextBlock>(window.DetailPanel).Single(t=>t.Text=="当设备上下线时，推送 Windows 通知");
        var naturalLabel=new TextBlock {Text=narrowNotification.Text,FontFamily=narrowNotification.FontFamily,FontSize=narrowNotification.FontSize,FontWeight=narrowNotification.FontWeight};naturalLabel.Measure(new Size(double.PositiveInfinity,double.PositiveInfinity));
        Check(narrowNotification.ActualWidth+.1>=naturalLabel.DesiredSize.Width,$"380 宽度通知文字完整显示: actual={narrowNotification.ActualWidth:F2},natural={naturalLabel.DesiredSize.Width:F2}");
        Capture(window,"notification-narrow.png",ThemeService.LightMode);
        Console.WriteLine($"BREATH samples={string.Join(',',samples.Select(v=>v.ToString("F4")))}");
        if(failures.Count>0)throw new InvalidOperationException(string.Join("; ",failures));
    }

    private static void VerifyVisualPolish(MainWindow window)
    {
        KeepTestPopupsOffDesktop(window);
        var failures = new List<string>();
        void Check(bool pass,string message) { Console.WriteLine($"{(pass ? "PASS" : "FAIL")}: {message}"); if(!pass) failures.Add(message); }
        window.Width=1180;window.Height=760;Pump(150);
        Capture(window,"polish-detail.png",ThemeService.LightMode);
        foreach(var text in new[]{window.DetailIpAddressText,window.DetailMacAddressText})
        {
            var natural=new TextBlock {Text=text.Text,FontFamily=text.FontFamily,FontSize=text.FontSize,FontWeight=text.FontWeight,Padding=text.Padding};
            natural.Measure(new Size(double.PositiveInfinity,double.PositiveInfinity));
            Console.WriteLine($"Address {text.Text}: actual={text.ActualWidth:F3}, desired={natural.DesiredSize.Width:F3}, dpi={VisualTreeHelper.GetDpi(text).DpiScaleX}");
            Check(text.ActualWidth>=natural.DesiredSize.Width+1,"地址须保留完整字符及像素取整余量");
        }
        window.SidebarRailSettingsButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));Pump(400);
        Capture(window,"polish-settings-initial.png",ThemeService.LightMode);
        window.DeviceFilterSegments.SelectedIndex=1;Pump(550);
        window.DeviceFilterSegments.SelectedIndex=0;Pump(60);
        var incoming=Descendants<ScrollViewer>(window.DeviceListTransition).First();
        var outgoing=Descendants<ScrollViewer>(window.DeviceList).First();
        FrameworkElement Mask(ScrollViewer v)=>(FrameworkElement)v.Template.FindName("DeviceViewportMask",v);
        Console.WriteLine($"Incoming scrollable={incoming.ScrollableHeight} height={Mask(incoming).ActualHeight}, gradient={string.Join(';',((LinearGradientBrush)Mask(incoming).OpacityMask).GradientStops.Select(s=>$"{s.Offset:F4}:{s.Color.A}"))}");
        var incomingTail=((LinearGradientBrush)Mask(incoming).OpacityMask).GradientStops[^2].Offset;
        var incomingOffset=incoming.VerticalOffset;
        Pump(350);Capture(window,"polish-filter-end.png",ThemeService.LightMode);
        Pump(150);
        var settledTail=((LinearGradientBrush)Mask(outgoing).OpacityMask).GradientStops[^2].Offset;
        Check(Math.Abs(incomingTail-settledTail)<.0001,"进入列表与交接后的渐变范围一致");
        Check(Math.Abs(incomingOffset-outgoing.VerticalOffset)<.1,"交接后应保持进入列表的滚动位置");
        Console.WriteLine($"Handoff offset incoming={incomingOffset}, primary={outgoing.VerticalOffset}");
        // The clip must move with its content during a horizontal transition.
        var mover=window.GetDeviceListMotion(window.DeviceList);
        Check(ReferenceEquals(mover,Mask(outgoing)),"横向切换应移动包含渐变的完整视口");
        var elapsed=typeof(LanDeviceMonitor.ViewModels.DeviceItemViewModel).GetProperty("OfflineElapsedText");
        Check(elapsed is not null,"离线列表应显示多久之前离线");
        outgoing.ScrollToVerticalOffset(40);Pump(100);
        window.DeviceFilterSegments.SelectedIndex=1;Pump(550);
        window.DeviceFilterSegments.SelectedIndex=0;Pump(60);
        var expected=incoming.VerticalOffset;
        Pump(550);
        Check(Math.Abs(outgoing.VerticalOffset-expected)<.1,$"之前滚动过的列表交接后不能跳位: {expected}/{outgoing.VerticalOffset}");
        for(var iteration=0;iteration<4;iteration++)
        {
            window.DeviceFilterSegments.SelectedIndex=1;Pump(80);
            window.DeviceFilterSegments.SelectedIndex=0;Pump(650);
            Check(Math.Abs(outgoing.VerticalOffset)<.1,"快速反向切换后保持进入列表位置");
        }
        var now=DateTimeOffset.Now;
        Check(LanDeviceMonitor.ViewModels.DeviceItemViewModel.FormatRelativeTime(null,now)=="时间未知" &&
            LanDeviceMonitor.ViewModels.DeviceItemViewModel.FormatRelativeTime(now.AddSeconds(-25),now)=="刚刚" &&
            LanDeviceMonitor.ViewModels.DeviceItemViewModel.FormatRelativeTime(now.AddMinutes(-59),now)=="59 分钟前" &&
            LanDeviceMonitor.ViewModels.DeviceItemViewModel.FormatRelativeTime(now.AddHours(-23),now)=="23 小时前" &&
            LanDeviceMonitor.ViewModels.DeviceItemViewModel.FormatRelativeTime(now.AddDays(-3),now)=="3 天前","离线时长的分钟、小时、天及未知边界");
        var data=new LanDeviceMonitor.Models.DeviceViewData
        {
            Profile=new LanDeviceMonitor.Models.DeviceProfile {MacAddress="00:00:00:00:70:01",LastKnownOnline=false,LastSeenAt=now,LastOnlineSeenAt=now.AddDays(-2)},
            Identity=new LanDeviceMonitor.Models.DeviceIdentity("平板电脑","", "测试离线设备"),
            Sessions=[new LanDeviceMonitor.Models.DeviceSession {OfflineAt=now.AddMinutes(-10)},new LanDeviceMonitor.Models.DeviceSession {OfflineAt=now.AddDays(-1)}]
        };
        var offline=new LanDeviceMonitor.ViewModels.DeviceItemViewModel(data);
        Check(offline.OfflineElapsedText=="10 分钟前","离线时长取最近会话的离线时间，不能取刷新时间或旧会话");
        var names=new List<string?>();offline.PropertyChanged+=(_,e)=>names.Add(e.PropertyName);
        var previous=offline.Data;
        offline.Update(new LanDeviceMonitor.Models.DeviceViewData {Profile=previous.Profile,Identity=previous.Identity,Sessions=[new LanDeviceMonitor.Models.DeviceSession {OfflineAt=now.AddHours(-2)}]});
        Check(offline.OfflineElapsedText=="2 小时前" && names.Contains("OfflineElapsedText"),"离线时间变化通知绑定刷新");

        typeof(MainWindow).GetMethod("ShowDevicesPanel",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,[false,false]);Pump(250);
        window.DetailPanel.ScrollToBottom();Pump(180);
        var notification=Descendants<TextBlock>(window.DetailPanel).Single(t=>t.Text=="当设备上下线时，推送 Windows 通知");
        Check(notification.ActualHeight<25,"通知文案保持单行且无副标题");
        Capture(window,"polish-device-settings-Light.png",ThemeService.LightMode);
        window.DeviceTypeCombo.IsDropDownOpen=true;Pump(350);
        var selected=(ComboBoxItem)window.DeviceTypeCombo.ItemContainerGenerator.ContainerFromIndex(window.DeviceTypeCombo.SelectedIndex);
        var icon=Descendants<Border>(selected).First(b=>b.Width==26 && b.Height==26);
        var point=icon.TranslatePoint(new Point(),selected);
        Check(Math.Abs(point.X-point.Y)<1,"菜单图标左侧与上下留白一致");
        Console.WriteLine($"Menu inset left={point.X}, top={point.Y}, height={selected.ActualHeight}");
        var popup=(System.Windows.Controls.Primitives.Popup)window.DeviceTypeCombo.Template.FindName("PART_Popup",window.DeviceTypeCombo);
        CaptureElement((FrameworkElement)popup.Child,"polish-dropdown-Light.png");
        window.DeviceTypeCombo.IsDropDownOpen=false;Pump(250);
        window.DeviceFilterSegments.SelectedIndex=1;Pump(650);
        Capture(window,"polish-offline-Light.png",ThemeService.LightMode);
        window.DeviceFilterSegments.SelectedIndex=0;Pump(650);

        var monitorUpdate=new LanDeviceMonitor.Models.MonitorUpdate([window.SelectedDevice!.Data],[],now);
        var queue=typeof(MainWindow).GetMethod("QueueMonitoringUpdate",BindingFlags.Instance|BindingFlags.NonPublic)!;
        queue.Invoke(window,[monitorUpdate]);Pump(150);
        var scale=(ScaleTransform)window.ConnectionDot.RenderTransform;
        Check(!SystemParameters.ClientAreaAnimation || scale.ScaleX>1,"成功刷新触发缓动呼吸");
        Pump(850);Check(Math.Abs(scale.ScaleX-1)<.001,"呼吸结束后恢复原尺寸");
        queue.Invoke(window,[monitorUpdate]);Pump(150);
        Check(!SystemParameters.ClientAreaAnimation || scale.ScaleX>1,"相同连接状态的下一次刷新仍触发呼吸");
        typeof(MainWindow).GetMethod("MonitoringService_MonitoringError",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,[null,new Exception("测试连接失败")]);Pump(100);
        Check(!scale.HasAnimatedProperties && Math.Abs(scale.ScaleX-1)<.001,"连接失败立即停止呼吸");
        foreach(var mode in new[]{ThemeService.LightMode,ThemeService.DarkMode})
        {
            ThemeService.Apply(mode);window.SidebarRailSettingsButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));Pump(350);
            Capture(window,$"polish-settings-{mode}.png",mode);
        }
        if(failures.Count>0) throw new InvalidOperationException(string.Join("; ",failures));
    }

    private static void VerifySidebarPolish(MainWindow window)
    {
        KeepTestPopupsOffDesktop(window);
        window.Width=2000;window.Height=760;Pump(200);
        var detailContent=(FrameworkElement)window.DetailPanel.Content;
        Require(detailContent.ActualWidth<=720 && detailContent.ActualWidth>700,"详情内容没有按设置页面限制最大宽度");
        Require(Math.Abs(window.DetailDeviceIconBackground.TranslatePoint(new Point(),detailContent).X)<.1,"标题设备图标仍有额外左缩进");
        Require(((SolidColorBrush)window.CollapsedRouterButton.Foreground).Color==((SolidColorBrush)window.FindResource("MutedTextBrush")).Color &&
            ((SolidColorBrush)window.CollapsedSettingsButton.Foreground).Color==((SolidColorBrush)window.FindResource("MutedTextBrush")).Color,"收起侧栏的底部图标颜色与侧栏按钮不一致");
        window.DetailPanel.ScrollToVerticalOffset(55);Pump(100);
        var detailMask=(FrameworkElement)window.DetailPanel.Template.FindName("DetailViewportMask",window.DetailPanel);
        var detailBitmap=new RenderTargetBitmap((int)Math.Ceiling(detailMask.ActualWidth),(int)Math.Ceiling(detailMask.ActualHeight),96,96,PixelFormats.Pbgra32);RenderViewport(detailBitmap,detailMask);
        var detailPixels=new byte[detailBitmap.PixelWidth*detailBitmap.PixelHeight*4];detailBitmap.CopyPixels(detailPixels,detailBitmap.PixelWidth*4,0);
        Require(Enumerable.Range(0,detailBitmap.PixelWidth).Max(x=>(int)detailPixels[x*4+3])<40,"详情顶部渐隐仍然是硬边");
        Capture(window,"detail-wide-fade.png",ThemeService.LightMode);window.DetailPanel.ScrollToTop();
        window.Width=1180;window.Height=760;Pump(200);
        var listViewer=Descendants<ScrollViewer>(window.DeviceList).First();
        var listBar=(System.Windows.Controls.Primitives.ScrollBar)listViewer.Template.FindName("PART_VerticalScrollBar",listViewer);
        Require(listBar.IsVisible,"测试在线列表未产生滚动条");
        var stationaryX=listBar.TranslatePoint(new Point(),window).X;
        window.DeviceFilterSegments.SelectedIndex=1;Pump(90);
        Console.WriteLine($"Scrollbar outgoing at 90ms opacity={listBar.Opacity:F3}");
        Require(listBar.IsVisible && listBar.Opacity>0 && listBar.Opacity<.9 && Math.Abs(listBar.TranslatePoint(new Point(),window).X-stationaryX)<.1,"滚动条没有同步渐隐或跟随了横移动画");
        Pump(500);Require(!listBar.IsVisible,"无滚动列表仍残留滚动条");
        window.DeviceFilterSegments.SelectedIndex=0;Pump(90);
        var enteringViewer=Descendants<ScrollViewer>(window.DeviceListTransition).First();
        var enteringBar=(System.Windows.Controls.Primitives.ScrollBar)enteringViewer.Template.FindName("PART_VerticalScrollBar",enteringViewer);
        Require(enteringBar.IsVisible && enteringBar.Opacity>0 && enteringBar.Opacity<1,"滚动条出现没有同步渐入");
        Pump(550);Require(listBar.IsVisible && listBar.Opacity==1,"列表切换后滚动条未恢复交互状态");
        var first=window.ConnectionStateInfoBlock.TranslatePoint(new Point(),window.DetailNetworkMetadataRow);
        var last=window.MacInfoBlock.TranslatePoint(new Point(window.MacInfoBlock.ActualWidth,0),window.DetailNetworkMetadataRow);
        Require(Math.Abs(first.X)<1 && Math.Abs(last.X-window.DetailNetworkMetadataRow.ActualWidth)<1,"连接信息没有贴齐内容区两端");
        Require(window.WindowRoot.RowDefinitions[0].ActualHeight==window.MaximizeWindowButton.ActualHeight,"顶栏比窗口按钮多出空白高度");
        Require(window.DetailSurface.TranslatePoint(new Point(),window.WindowRoot).Y==window.MaximizeWindowButton.ActualHeight,"顶栏下方仍有额外留白");
        window.SidebarToggleButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));Pump(350);
        window.DeviceSidebar.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,Environment.TickCount,MouseButton.Left){RoutedEvent=UIElement.MouseLeftButtonUpEvent});Pump(350);
        Require(window.SidebarExpandedContent.IsVisible,"点击收起侧栏空白区没有展开");
        var scroller=Descendants<ScrollViewer>(window.DeviceList).First();
        scroller.ScrollToVerticalOffset(26);Pump(300);
        var viewport=(FrameworkElement)scroller.Template.FindName("DeviceViewportMask",scroller);
        Console.WriteLine($"Fade diagnostic items={window.DeviceList.Items.Count}, opacity={window.GetDeviceListMotion(window.DeviceList).Opacity}, cache={window.GetDeviceListMotion(window.DeviceList).CacheMode}, filter={window.DeviceFilterSegments.SelectedIndex}, offset={scroller.VerticalOffset}, content={viewport.ActualHeight}");
        Capture(window,"fade-diagnostic.png",ThemeService.LightMode);
        var bitmap=new RenderTargetBitmap((int)Math.Ceiling(viewport.ActualWidth),(int)Math.Ceiling(viewport.ActualHeight),96,96,PixelFormats.Pbgra32);RenderViewport(bitmap,viewport);
        var pixels=new byte[bitmap.PixelWidth*bitmap.PixelHeight*4];bitmap.CopyPixels(pixels,bitmap.PixelWidth*4,0);
        int MaxAlpha(int y)=>Enumerable.Range(8,bitmap.PixelWidth-16).Max(x=>(int)pixels[(y*bitmap.PixelWidth+x)*4+3]);
        Console.WriteLine($"FADE alpha edge={MaxAlpha(0)}/{MaxAlpha(bitmap.PixelHeight-1)}, middle={MaxAlpha(40)}");
        Require(MaxAlpha(0)<40 && MaxAlpha(bitmap.PixelHeight-1)<40 && MaxAlpha(40)>100,"渐隐边界仍残留不透明硬切");
        CaptureElement(viewport,"fade-viewport.png");Capture(window,"sidebar-fade.png",ThemeService.LightMode);
        using var json=System.Text.Json.JsonDocument.Parse("""
        {"graph":{"name":"客厅主路由","ip":"10.77.0.1","hardware":"AX3000","onlines":8,"leafs":[
          {"name":"书房节点","ip":"10.77.0.2","hardware":"AX3000","link_type":"wired","onlines":3,"leafs":[
            {"name":"卧室节点","ip":"10.77.0.3","hardware":"AX3000","link_type":"wireless","onlines":2}]},
          {"name":"楼上节点","ip":"10.77.0.4","hardware":"AX3000","link_type":"wireless","onlines":1}]}}
        """);
        var nodes=XiaomiRouterClient.ParseMeshTopology(json.RootElement);
        Require(nodes.Count==4 && nodes[2].ParentNodeId==nodes[1].NodeId && nodes[3].ParentNodeId==nodes[0].NodeId,"Mesh 层级被压平或上联关系错误");
        window.MeshNodesItemsControl.ItemsSource=nodes;window.MeshNodesEmptyText.Visibility=Visibility.Collapsed;
        window.Ipv4AddressText.Text="10.77.0.1";window.Ipv4TypeText.Text="DHCP";window.Ipv6TypeText.Text="Native";
        window.RouterConnectionBadgeText.Text="互联网已连接";window.RouterInfoLoadingBorder.Visibility=Visibility.Collapsed;
        Require(window.Ipv4AddressText.FontSize==window.Ipv4TypeText.FontSize,"IP 地址仍被单独放大");
        foreach(var mode in new[]{ThemeService.LightMode,ThemeService.DarkMode})
        {
            ThemeService.Apply(mode);window.Background=Brushes.Transparent;
            foreach(var width in new[]{1180d,430d,380d})
            {
                window.Width=width;Pump(250);
                typeof(MainWindow).GetMethod("ShowRouterPanel",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,null);Pump(1000);
                window.RouterPanel.ScrollToTop();Pump(100);
                Capture(window,$"router-fields-{mode}-{width}.png",mode);
                var panel=Descendants<MeshTopologyPanel>(window.MeshNodesItemsControl).Single();
                var containers=Descendants<ContentPresenter>(panel).Where(c=>c.Content is LanDeviceMonitor.Models.MeshNodeInfo).ToArray();
                Require(containers.Length==4,"Mesh 节点未完整显示");
                foreach(var layout in Descendants<MeshNodeLayout>(panel))
                {
                    var badge=layout.Children.OfType<Border>().Single(b=>Equals(b.Tag,"MeshCount"));
                    var metadata=Descendants<StackPanel>(layout).Single(c=>Equals(c.Tag,"MeshMetadata"));
                    var title=Descendants<WrapPanel>(layout).Single(c=>Equals(c.Tag,"MeshTitle"));
                    if(layout.ActualWidth>=300)
                    {
                        Require(Grid.GetRowSpan(badge)==2,"宽 Mesh 卡片数量块应跨两行");
                        var gap=metadata.TranslatePoint(new Point(),layout).Y-title.TranslatePoint(new Point(0,title.ActualHeight),layout).Y;
                        Console.WriteLine($"Mesh layout width={layout.ActualWidth:F1},rows={string.Join(',',layout.RowDefinitions.Select(r=>r.ActualHeight.ToString("F1")))},title={title.ActualHeight:F1},meta={metadata.ActualHeight:F1},badge={badge.ActualHeight:F1},gap={gap:F1}");
                        Require(gap<=9,$"Mesh 标题到地址不应被数量块撑高: gap={gap:F1}");
                    }
                    else Require(Grid.GetRowSpan(badge)==1 && Grid.GetRow(badge)==2,"窄 Mesh 卡片的数量徽章应回到独立第三行");
                }
                var restart=(Grid)window.FindName("RestartActionLayout");
                var icon=restart.Children.OfType<Border>().First();var description=restart.Children.OfType<StackPanel>().First();
                Require(description.TranslatePoint(new Point(),restart).X-icon.TranslatePoint(new Point(icon.ActualWidth,0),restart).X>=16,"重启图标与文字应有足够间距");
                var x=containers.Select(c=>c.TranslatePoint(new Point(),panel).X).ToArray();
                Require(x[0]<x[1] && x[1]<x[2] && Math.Abs(x[1]-x[3])<.1,"Mesh 缩进未体现真实父子关系");
                CaptureElement(window.MeshNodesItemsControl,$"mesh-{mode}-{width}.png");
                window.RouterPanel.ScrollToBottom();Pump(100);Capture(window,$"router-bottom-{mode}-{width}.png",mode);
                window.RestartRouterButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));Pump(400);
                Require(window.RestartConfirmBorder.Visibility==Visibility.Visible && window.RestartConfirmText.Text=="重启期间设备会暂时断网，应用会持续显示恢复进度","重启说明应出现在确认区域");
                Require(Descendants<TextBlock>(window.RouterPanel).Count(t=>t.Text==window.RestartConfirmText.Text)==1,"初始操作行不能重复出现重启说明");
                window.RouterPanel.ScrollToBottom();Pump(100);Capture(window,$"router-confirm-{mode}-{width}.png",mode);
                Descendants<Button>(window.RestartConfirmBorder).First(b=>Equals(b.Content,"取消")).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));Pump(250);
            }
        }
        ThemeService.Apply(ThemeService.LightMode);window.Width=430;Pump(100);
        window.CompactBackButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));Pump(700);
        typeof(MainWindow).GetMethod("OpenCompactDetail",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,null);
        Pump(350);Console.WriteLine($"COMPACT at 350ms opacity={window.DetailSurface.Opacity:F3}");
        Require(window.DetailSurface.Opacity>.9,"窄屏进入曲线仍慢于详情的 QuinticEase");Pump(350);
        Require(window.DetailSurface.RenderTransform.Value.IsIdentity,"窄屏动画没有按时结束");
        window.CompactBackButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));Pump(700);
        typeof(MainWindow).GetMethod("ShowSettingsPanel",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,null);
        Require(!window.SettingsPanel.HasAnimatedProperties && window.SettingsPanel.RenderTransform.Value.IsIdentity,"窄屏叠加了第二套页面切换动画");
        Pump(700);Require(window.DetailSurface.RenderTransform.Value.IsIdentity,"窄屏设置入口超出统一动画时长");
        Console.WriteLine("Sidebar/topology PASS: justified metadata, compact title, blank rail click, transparent fade edges, 3-level mesh, IPv4 parity, light/dark and narrow layout.");
    }

    private static void VerifyRestoredWindow(MainWindow window)
    {
        MicaWindowService.Apply(window,false);
        WindowFrameHitTests.Verify(window);
        WindowFrameHitTests.VerifyMaximized(window);Pump(150);
        window.RequestNativeMinimize();Pump(250);
        Require(window.WindowState==WindowState.Minimized,"系统最小化未执行");

        window.WindowState=WindowState.Normal;Pump(300);
        Require(MicaWindowService.LastAcrylicApplySucceeded && window.WindowTransitionSnapshot.Visibility==Visibility.Collapsed,"最小化恢复后材质或覆盖层未恢复");
        var closedToTray=false;
        window.MinimizeToTrayRequested+=(_,_)=>closedToTray=true;
        window.Close();Pump(100);
        Require(closedToTray && window.IsVisible,"原生关闭没有保留关闭到托盘请求");
        CaptureNative(window,"native-caption-restored.png");
        Capture(window,"native-client-restored.png",ThemeService.LightMode);
        Console.WriteLine("Restored dev.208 window PASS: historical caption, system commands, original Acrylic, min/restore and close-to-tray.");
    }

    private static void VerifyRefinements(MainWindow window)
    {
        void Call(string method) => typeof(MainWindow).GetMethod(method,BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,null);
        KeepTestPopupsOffDesktop(window);
        WindowFrameHitTests.VerifyMaximized(window);Pump(100);
        window.Width=1180;Pump(200);
        var width=window.SidebarColumn.ActualWidth;
        window.Width=430;Pump(200);
        Call("OpenCompactDetail");Pump(70);
        Require(window.DeviceSidebar.Opacity>0 && window.DeviceSidebar.Opacity<1,"窄屏进入仍然硬切");
        Pump(180);
        Require(window.DetailSurface.IsVisible && window.DetailSurface.Opacity>0 && window.DetailSurface.Opacity<1,"窄屏没有详情进入段");
        Capture(window,"compact-enter-mid.png",ThemeService.LightMode);Pump(500);
        window.CompactBackButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));Pump(70);
        Require(window.DetailSurface.Opacity>0 && window.DetailSurface.Opacity<1,"窄屏返回仍然硬切");Pump(650);
        window.Width=1180;Pump(200);
        Require(Math.Abs(window.SidebarColumn.ActualWidth-width)<1,"横竖屏往返改变了侧栏宽度");
        window.RouterConnectionBadgeText.Text="互联网已连接";
        window.RouterInfoLoadingBorder.Visibility=Visibility.Collapsed;
        window.Ipv4AddressText.Text="10.77.0.1";window.Ipv4TypeText.Text="DHCP";window.Ipv6TypeText.Text="Native";
        window.WanIpv6AddressText.Text="2001:db8::1";
        foreach(var w in new[]{1180d,1100d,980d,760d,430d,380d})
        {
            window.Width=w;Pump(200);Call("ShowRouterPanel");Pump(700);window.RouterPanel.ScrollToTop();Pump(100);
            var header=window.RouterPageHeader;
            var action=header.Children[1] as FrameworkElement;
            var point=action!.TranslatePoint(new Point(),header);
            Require(point.X>=-.1 && point.X+action.ActualWidth<=header.ActualWidth+.1,"路由器状态或刷新越界");
            Require(window.RouterConnectionBadge.ActualWidth>=window.RouterConnectionBadgeText.ActualWidth+20,"连接状态胶囊挤压文字");
            if(header.ActualWidth>=470) Require(point.Y<20,"足够宽时标题栏提前换行");
            CaptureElement(window.RouterPageHeader,$"router-header-{w}.png");
            window.RouterPanel.ScrollToBottom();Pump(80);Capture(window,$"router-restart-{w}.png",ThemeService.LightMode);window.RouterPanel.ScrollToTop();
        }
        window.Width=1180;Call("ShowSettingsPanel");Pump(700);
        window.RouterPasswordBox.Password="sample-only";window.SettingsPanel.ScrollToTop();Pump(120);
        CaptureElement(window.RouterPasswordBox,"password-entered.png");
        var passwordHost=(FrameworkElement)window.RouterPasswordBox.Template.FindName("PART_ContentHost",window.RouterPasswordBox);
        Require(passwordHost.RenderTransform is TranslateTransform,"密码未进行字形轴对齐");
        window.RouterPasswordBox.Clear();
        window.SidebarToggleButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));Pump(350);
        Require(window.CollapsedRouterButton.Template.FindName("IconHover",window.CollapsedRouterButton) is Border,"收起导航不是轻量图标样式");
        Capture(window,"collapsed-lightweight.png",ThemeService.LightMode);
        ExportDeviceIconAtlas(window);
        Console.WriteLine("Refinement PASS: native maximize, compact two-stage frames, width roundtrip, 6 router widths, restart layout, password glyph alignment, lightweight rail.");
    }

    private static void VerifyResponsiveNavigation(MainWindow window)
    {
        void Navigate(string method) => typeof(MainWindow).GetMethod(method,BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,null);
        KeepTestPopupsOffDesktop(window);
        window.Width=1180;window.Height=760;Pump(200);
        window.DoNotDisturbCheck.IsChecked=false;Pump(30);
        Require(window.DoNotDisturbTimeRow.Visibility==Visibility.Collapsed,"关闭免打扰后时段没有隐藏");
        window.DoNotDisturbCheck.IsChecked=true;Pump(30);
        Require(window.DoNotDisturbTimeRow.Visibility==Visibility.Visible,"开启免打扰后时段未显示");
        foreach(ComboBoxItem interval in window.RefreshIntervalCombo.Items)
            Require(interval.Content.ToString()==interval.Tag+"\u2009秒","刷新间隔缺少单位或数值标签");
        window.RefreshIntervalCombo.SelectedIndex=0; window.ThemeModeCombo.SelectedIndex=0;
        window.DeviceFilterSegments.SelectedIndex=1;Pump(90);
        Require(window.DeviceList.RenderTransform.Value.IsIdentity && window.DeviceListTransition.RenderTransform.Value.IsIdentity,"滚动条仍与整张列表平移");
        Require(window.GetDeviceListMotion(window.DeviceListTransition).RenderTransform is TranslateTransform,"列表内容没有执行切换动画");
        Pump(700);
        var row=(ListBoxItem)window.DeviceList.ItemContainerGenerator.ContainerFromIndex(0);
        var icon=Descendants<DeviceTypeIcon>(row).First();
        Require(((SolidColorBrush)icon.IconBrush).Color==((SolidColorBrush)window.FindResource("MutedTextBrush")).Color,"离线设备图标不是灰色");
        window.DeviceFilterSegments.SelectedIndex=0;Pump(800);
        var scroller=Descendants<ScrollViewer>(window.DeviceList).First();
        scroller.ScrollToTop();Pump(40);
        var bar=Descendants<System.Windows.Controls.Primitives.ScrollBar>(scroller).First(b=>b.Orientation==Orientation.Vertical);
        Require(bar.Margin.Top==0 && bar.Margin.Bottom==5,"滚动条仍有额外上下空隙");
        scroller.ScrollToVerticalOffset(30);Pump(50);
        var viewport=(FrameworkElement)scroller.Template.FindName("DeviceViewportMask",scroller);
        Require(viewport.OpacityMask is LinearGradientBrush,"列表边界缺少渐隐");
        window.SidebarToggleButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));Pump(350);
        Require(window.CollapsedRouterButton.IsVisible && window.CollapsedSettingsButton.IsVisible,"收起侧栏缺少圆按钮");
        Require(!Descendants<System.Windows.Shapes.Ellipse>(window.CollapsedSidebarContent).Any(),"收起侧栏仍有状态圆点");
        Capture(window,"collapsed-234.png",ThemeService.LightMode);
        window.CollapsedSidebarButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));Pump(350);
        window.Ipv4AddressText.Text="10.77.0.100";window.Ipv4TypeText.Text="DHCP";
        window.WanIpv6AddressText.Text="2001:db8::1";
        window.Ipv6TypeText.Text="Native";window.RouterInfoLoadingBorder.Visibility=Visibility.Collapsed;
        foreach(var width in new[]{1180d,1132d,760d,430d,380d})
        {
            window.Width=width;Pump(300);
            if(width<900)
            {
                window.CompactBackButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));Pump(700);
                Require(window.DeviceSidebar.IsVisible && !window.DetailSurface.IsVisible,"窄窗口没有只显示设备列表");
                Require(window.DeviceSidebar.ActualWidth>width-40,"窄窗口设备列表没有填满宽度");
                Capture(window,$"compact-list-{width}.png",ThemeService.LightMode);
                var tappedRow=(ListBoxItem)window.DeviceList.ItemContainerGenerator.ContainerFromIndex(2);
                tappedRow.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,Environment.TickCount,MouseButton.Left)
                    { RoutedEvent=Mouse.PreviewMouseDownEvent });
                tappedRow.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,Environment.TickCount,MouseButton.Left) { RoutedEvent=Mouse.PreviewMouseUpEvent });
                Pump(700);
                var portrait=window.WorkspaceGrid.ActualHeight>=window.WorkspaceGrid.ActualWidth;
                Require(!window.DeviceSidebar.IsVisible && window.DetailSurface.IsVisible &&
                    (portrait ? window.CaptionBackButton.IsVisible && !window.CompactBackButton.IsVisible : window.CompactBackButton.IsVisible && !window.CaptionBackButton.IsVisible),"窄窗口详情或对应位置的返回按钮缺失");
                Require(ReferenceEquals(window.SelectedDevice,window.DeviceList.Items[2]),"点击胶囊后显示了错误的设备");
            }
            window.DetailPanel.ScrollToTop();Pump(100);Capture(window,$"compact-detail-{width}.png",ThemeService.LightMode);
            window.DetailPanel.ScrollToBottom();Pump(150);Capture(window,$"compact-device-settings-{width}.png",ThemeService.LightMode);
            Navigate("ShowSettingsPanel");Pump(700);window.SettingsPanel.ScrollToTop();Pump(100);Capture(window,$"compact-settings-{width}.png",ThemeService.LightMode);
            window.SettingsPanel.ScrollToBottom();Pump(100);Capture(window,$"compact-settings-bottom-{width}.png",ThemeService.LightMode);
            Navigate("ShowRouterPanel");Pump(700);Capture(window,$"router-{width}.png",ThemeService.LightMode);
            typeof(MainWindow).GetMethod("ShowDevicesPanel",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,[false,false]);Pump(100);
        }
        window.Width=1180;Pump(250);
        Require(window.DeviceSidebar.IsVisible && window.DetailSurface.IsVisible && !window.CompactBackButton.IsVisible,"放宽后未恢复双栏");
        Console.WriteLine("Responsive navigation PASS: 1180/760/430/380, list/detail/back, stable scrollbar, gray offline icons, DND visibility, refresh units, collapsed buttons, wide restoration.");
    }

    private static void ProfileDeviceTransitions(MainWindow window)
    {
        // The default three-row disclosure cannot exercise middle/end virtualization.
        while(window.HasOlderSessions) { window.OlderHistoryToggle.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));Pump(500); }
        Console.WriteLine($"HISTORY visuals={Descendants<TextBlock>(window.SessionHistoryItems).Count()}, models={window.DetailSessionItems.Count}, extent={window.DetailPanel.ScrollableHeight:F1}");
        Require(window.DetailSessionItems.Count==100 && Descendants<TextBlock>(window.SessionHistoryItems).Count()<140,"不可见历史记录仍全部创建了控件");
        for(var run=0;run<4;run++)
        {
            var clock=System.Diagnostics.Stopwatch.StartNew();var frames=new List<double>();
            EventHandler tick=(_,_)=>frames.Add(clock.Elapsed.TotalMilliseconds);
            CompositionTarget.Rendering+=tick;
            window.DeviceFilterSegments.SelectedIndex=(run+1)%2;Pump(800);
            CompositionTarget.Rendering-=tick;
            var gaps=frames.Zip(frames.Skip(1),(a,b)=>(Start:a,Gap:b-a)).Where(p=>p.Start<650).ToArray();
            Console.WriteLine($"PERF run={run} frames={frames.Count} maxGap={gaps.Max(p=>p.Gap):F1} gapsOver25={string.Join(';',gaps.Where(p=>p.Gap>25).Select(p=>$"{p.Start:F0}:{p.Gap:F1}"))}");
        }
        // Switching the filter can select another device and reset its disclosure.
        while(window.HasOlderSessions) { window.OlderHistoryToggle.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));Pump(500); }
        var historyPanel=Descendants<SessionHistoryPanel>(window.SessionHistoryItems).Single();
        var historyOrigin=historyPanel.TranslatePoint(new Point(),window.DetailPanel).Y+window.DetailPanel.VerticalOffset;
        window.DetailPanel.ScrollToVerticalOffset(historyOrigin+historyPanel.HeightForCount(50));Pump(250);
        Require(window.SessionHistoryItems.ItemContainerGenerator.ContainerFromIndex(50) is not null,"历史中部记录没有按需呈现");
        window.DetailPanel.ScrollToBottom();Pump(250);
        Require(window.SessionHistoryItems.ItemContainerGenerator.ContainerFromIndex(99) is not null,"最后一条历史记录不可访问");
        var fullHeight=window.SessionHistoryItems.ActualHeight;
        var selected=window.SelectedDevice!;
        var fewer=new LanDeviceMonitor.ViewModels.DeviceItemViewModel(new LanDeviceMonitor.Models.DeviceViewData
        { Profile=selected.Data.Profile, Identity=selected.Data.Identity, Sessions=selected.Data.Sessions.Take(5).ToArray() });
        typeof(MainWindow).GetMethod("UpdateSelectedDevice",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,[fewer,true]);Pump(250);
        Require(window.SessionHistoryItems.ActualHeight<fullHeight/10 && window.SessionHistoryItems.ActualHeight>0,"切换到少量历史后仍保留空白长尾");
        Console.WriteLine("History viewport regression PASS: full 100 records retained, middle/end reachable, short history extent correct.");
    }

    private static void KeepTestPopupsOffDesktop(MainWindow window)
    {
        foreach(var combo in new[]{window.DeviceTypeCombo,window.ThemeModeCombo,window.RefreshIntervalCombo})
        {
            combo.ApplyTemplate();
            if(combo.Template.FindName("PART_Popup",combo) is not System.Windows.Controls.Primitives.Popup popup) continue;
            popup.Opened += (_,_) =>
            {
                if(System.Windows.Interop.HwndSource.FromVisual(popup.Child) is System.Windows.Interop.HwndSource source)
                {
                    // Keep opacity, DPI, layout, animations and drawing intact. Only
                    // move the test popup HWND; real placement has its separate check.
                    SetWindowPos(source.Handle,IntPtr.Zero,-20000,-20000,0,0,0x0001|0x0004|0x0010);
                }
            };
        }
    }

    private static void VerifyPopupAlignment(MainWindow window)
    {
        typeof(MainWindow).GetMethod("ShowSettingsPanel",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,null);
        window.Left=80;window.Top=80;window.ThemeModeCombo.SelectedIndex=0;Pump(600);
        var combo=window.ThemeModeCombo;
        var triggerLabel=Descendants<TextBlock>(combo).First(t=>t.Text=="跟随系统");
        var popup=(System.Windows.Controls.Primitives.Popup)combo.Template.FindName("PART_Popup",combo);
        // Only screen coordinates are under test here. Opacity does not change
        // layout/placement; the normal animation is tested separately offscreen.
        popup.Opened += (_,_) => { popup.Child.BeginAnimation(UIElement.OpacityProperty,null); popup.Child.Opacity=0; };
        combo.IsDropDownOpen=true;Pump(650);
        var border=(Border)popup.Child;
        var item=(ComboBoxItem)combo.Items[0];
        var text=Descendants<TextBlock>(item).First(t=>t.Text=="跟随系统");
        Console.WriteLine($"POPUP diagnostic target={combo.PointToScreen(new Point()).X}, surface={border.PointToScreen(new Point()).X}, trigger={triggerLabel.PointToScreen(new Point()).X}, text={text.PointToScreen(new Point()).X}, item={item.PointToScreen(new Point()).X}, itemPadding={item.Padding}, surfacePadding={border.Padding}, width={border.ActualWidth}");
        CaptureElement((FrameworkElement)popup.Child,"popup-content-width.png");
        Require(Math.Abs(text.PointToScreen(new Point()).X-triggerLabel.PointToScreen(new Point()).X)<=1,"弹层文字屏幕对齐失败");
        Require(item.ActualWidth >= combo.ActualWidth-1,"菜单高亮胶囊不应窄于触发胶囊");
        Require(Math.Abs(item.PointToScreen(new Point()).X-combo.PointToScreen(new Point()).X)<=1 &&
            Math.Abs(item.PointToScreen(new Point(item.ActualWidth,0)).X-combo.PointToScreen(new Point(combo.ActualWidth,0)).X)<=1,"菜单高亮胶囊两侧必须与触发框对齐");
        Require(Math.Abs(text.TranslatePoint(new Point(),item).X-triggerLabel.TranslatePoint(new Point(),combo).X)<1,"高亮胶囊内部文字留白与触发框不一致");
        var face=(Border)item.Template.FindName("StudioSurface",item);
        Require(Math.Abs(face.CornerRadius.TopLeft-item.ActualHeight/2)<.1,"下拉选中项丢失胶囊圆角");
        Require(((SolidColorBrush)item.Foreground).Color==(Color)window.FindResource("AccentColor"),"下拉选中文本没有使用主题色");
        foreach(var mode in new[]{ThemeService.DarkMode,ThemeService.LightMode})
        {
            combo.IsDropDownOpen=false;Pump(250);ThemeService.Apply(mode);
            combo.IsDropDownOpen=true;Pump(600);
            Require(Math.Abs(face.CornerRadius.TopLeft-item.ActualHeight/2)<.1,"主题切换或重新打开后胶囊圆角丢失");
        }
        combo.IsDropDownOpen=false;Pump(250);
        Console.WriteLine("Popup alignment regression PASS: label alignment, accent selection, capsule shape after reopen/theme changes.");
    }

    private static void ExportDeviceIconAtlas(MainWindow window)
    {
        var panel=new System.Windows.Controls.WrapPanel { Width=400, Background=Brushes.White };
        foreach(var type in DeviceIdentityService.AvailableTypes)
        {
            var stack=new StackPanel { Width=100, Height=86 };
            stack.Children.Add(new DeviceTypeIcon { DeviceType=type.TypeName,Width=32,Height=32,Margin=new Thickness(0,12,0,8),IconBrush=(Brush)window.FindResource("AccentBrush") });
            stack.Children.Add(new TextBlock { Text=type.TypeName,FontFamily=(FontFamily)window.FindResource("AppTextFont"),FontSize=12,HorizontalAlignment=HorizontalAlignment.Center });
            panel.Children.Add(stack);
        }
        panel.Measure(new Size(400,1000));panel.Arrange(new Rect(0,0,400,panel.DesiredSize.Height));panel.UpdateLayout();
        CaptureElement(panel,"device-icon-atlas.png");
    }

    private static void VerifySegmentText(SegmentedControl segments)
    {
        segments.UpdateLayout();
        foreach (ListBoxItem item in segments.Items)
        {
            var expected = ((SolidColorBrush)item.Foreground).Color;
            var themeTarget = (SolidColorBrush)(item.IsSelected ? ((SegmentItem)item).ActiveBrush : ((SegmentItem)item).InactiveBrush);
            Require(expected == themeTarget.Color, $"分段文字没有跟随主题: actual={expected}, target={themeTarget.Color}");
            foreach (var text in Descendants<TextBlock>(item))
            {
                Require(text.Foreground is SolidColorBrush brush && brush.Color == expected,
                    $"分段文字颜色被全局样式覆盖: {text.Text}");
                Require(text.FontWeight == FontWeights.Normal, "分段标签或计数不应额外加粗");
            }
        }
        var selected = (ListBoxItem)segments.Items[segments.SelectedIndex];
        var other = (ListBoxItem)segments.Items[(segments.SelectedIndex + 1) % segments.Items.Count];
        Require(((SolidColorBrush)selected.Foreground).Color != ((SolidColorBrush)other.Foreground).Color,
            $"选中和未选中文字必须有不同颜色: selected={selected.IsSelected}/{selected.IsMouseOver}/{((SolidColorBrush)selected.Foreground).Color}, other={other.IsSelected}/{other.IsMouseOver}/{((SolidColorBrush)other.Foreground).Color}, target={((SolidColorBrush)((SegmentItem)other).InactiveBrush).Color}, own={((SolidColorBrush)typeof(SegmentItem).GetField("_textBrush", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(other)!).Color}, ref={ReferenceEquals(other.Foreground, typeof(SegmentItem).GetField("_textBrush", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(other))}");
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }


    private static void Pump(int milliseconds)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start(); Dispatcher.PushFrame(frame);
    }

    private static void CaptureElement(FrameworkElement element, string name)
    {
        element.UpdateLayout();
        var exportedControls=Descendants<System.Windows.Controls.Control>(element).Select(c=>(Control:c,Brush:c.Foreground)).ToArray();
        var exportedText=Descendants<TextBlock>(element).Select(c=>(Text:c,Brush:c.Foreground)).ToArray();
        foreach(var c in exportedControls) c.Control.SetCurrentValue(Control.ForegroundProperty,c.Brush.CloneCurrentValue());
        foreach(var c in exportedText) c.Text.SetCurrentValue(TextBlock.ForegroundProperty,c.Brush.CloneCurrentValue());
        var visual=new DrawingVisual();
        using(var dc=visual.RenderOpen()) { dc.DrawRectangle(new SolidColorBrush(ThemeService.ResolveIsDark(ThemeService.CurrentMode) ? Color.FromRgb(30,32,38) : Colors.White),null,new Rect(0,0,element.ActualWidth+12,element.ActualHeight+12)); dc.DrawRectangle(new VisualBrush(element),null,new Rect(6,6,element.ActualWidth,element.ActualHeight)); }
        var bitmap=new RenderTargetBitmap((int)Math.Ceiling((element.ActualWidth+12)*2),(int)Math.Ceiling((element.ActualHeight+12)*2),192,192,PixelFormats.Pbgra32); bitmap.Render(visual);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file=File.Create(Path.Combine(Environment.CurrentDirectory,".tools","ui-dev249",name));encoder.Save(file);
        foreach(var c in exportedControls) c.Control.SetCurrentValue(Control.ForegroundProperty,c.Brush);
        foreach(var c in exportedText) c.Text.SetCurrentValue(TextBlock.ForegroundProperty,c.Brush);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool PrintWindow(IntPtr handle, IntPtr dc, int flags);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr handle, IntPtr after, int x, int y, int width, int height, uint flags);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr handle, out NativeRect rect);
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct NativeRect { public int Left,Top,Right,Bottom; }
    private static void CaptureNative(Window window, string name)
    {
        var handle=new System.Windows.Interop.WindowInteropHelper(window).Handle;
        GetWindowRect(handle,out var rect);
        using var bitmap=new System.Drawing.Bitmap(rect.Right-rect.Left,rect.Bottom-rect.Top);
        using var graphics=System.Drawing.Graphics.FromImage(bitmap);
        var dc=graphics.GetHdc();
        try { Require(PrintWindow(handle,dc,2),"原生窗口截图失败"); } finally { graphics.ReleaseHdc(dc); }
        bitmap.Save(Path.Combine(Environment.CurrentDirectory,".tools","ui-dev249",name),System.Drawing.Imaging.ImageFormat.Png);
    }

    private static void Capture(MainWindow window, string name, string mode)
    {
        window.UpdateLayout();
        if (name.StartsWith("devices-") || name.StartsWith("settings-")) FontVisualProof.Capture(window, name);
        if (name == "settings-Dark-980-top.png") CaptureNative(window,"settings-Dark-native.png");
        // Offscreen WPF exports can paint an animation's cached initial transform.
        // Snapshot its current value for the export, then restore the live clock.
        var transforms = Descendants<UIElement>(window)
            .Where(element => element.RenderTransform.HasAnimatedProperties)
            .Select(element => (Element: element, Transform: element.RenderTransform)).ToArray();
        foreach (var pair in transforms) pair.Element.RenderTransform = pair.Transform.CloneCurrentValue();
        var textBrushes = Descendants<SegmentItem>(window)
            .Select(item => (Item: item, Brush: item.Foreground)).ToArray();
        foreach (var pair in textBrushes) pair.Item.Foreground = pair.Brush.CloneCurrentValue();
        var width = (int)window.ActualWidth; var height = (int)window.ActualHeight;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(mode == ThemeService.DarkMode ? Color.FromRgb(30,32,38) : Color.FromRgb(242,243,247)), null, new Rect(0,0,width,height));
            dc.DrawRectangle(new VisualBrush(window) { ViewboxUnits=BrushMappingMode.Absolute, Viewbox=new Rect(0,0,width,height) }, null, new Rect(0,0,width,height));
        }
        var dpi = VisualTreeHelper.GetDpi(window);
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(width*dpi.DpiScaleX),(int)Math.Ceiling(height*dpi.DpiScaleY),dpi.PixelsPerInchX,dpi.PixelsPerInchY,PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        var directory = Path.Combine(Environment.CurrentDirectory, ".tools", "ui-dev249");
        Directory.CreateDirectory(directory);
        using var file = File.Create(Path.Combine(directory,name)); encoder.Save(file);
        foreach (var pair in transforms) pair.Element.RenderTransform = pair.Transform;
        foreach (var pair in textBrushes) pair.Item.Foreground = pair.Brush;
    }
}
