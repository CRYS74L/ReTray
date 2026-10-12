using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using LanDeviceMonitor.Controls;
using TrayAssistant_App.Models;
using TrayAssistant_App.ViewModels;
using Localization = TrayAssistant_App.Services.Localization;

namespace TrayAssistant_App;

public sealed class IconEditorPanel : UserControl
{
    public IconEditorPanel(TrayViewModel vm, IconRow row, Func<Task> close)
    {
        DataContext = vm;
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition());
        var header = new Grid { Margin = new Thickness(0, 0, 0, 14) };
        header.ColumnDefinitions.Add(new ColumnDefinition());
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(new TextBlock { Text = row.Name, FontWeight = FontWeights.Medium,
            VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
        var dismiss = new Button { Width = 32, Height = 32, MinWidth = 0, MinHeight = 0,
            Margin = new Thickness(12, 0, 0, 0), ToolTip = "关闭" };
        dismiss.SetResourceReference(StyleProperty, "FloatingCloseButtonStyle");
        var glyph = new TextBlock { Text = "\uE8BB", FontSize = 14 };
        glyph.SetResourceReference(TextBlock.FontFamilyProperty, "AppIconFont");
        glyph.SetBinding(TextBlock.ForegroundProperty, new Binding(nameof(Button.Foreground)) { Source = dismiss });
        GlyphInkAlignment.SetEnabled(glyph, true);
        dismiss.Content = glyph;
        dismiss.Click += async (_, _) => await close();
        System.Windows.Automation.AutomationProperties.SetAutomationId(dismiss, "IconEditorClose");
        Grid.SetColumn(dismiss, 1); header.Children.Add(dismiss); root.Children.Add(header);
        var content = new StackPanel { Margin = new Thickness(4) };
        var previews = new Grid { Margin = new Thickness(0, 4, 0, 18) };
        previews.ColumnDefinitions.Add(new ColumnDefinition()); previews.ColumnDefinitions.Add(new ColumnDefinition());
        void Preview(int column, string caption, string property, string buttonText, System.Windows.Input.ICommand command)
        {
            var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            var image = new Image { Width = 44, Height = 44, Stretch = System.Windows.Media.Stretch.Uniform,
                Margin = new Thickness(0, 0, 0, 8) };
            image.SetBinding(Image.SourceProperty, new Binding(property) { Source = vm });
            System.Windows.Media.RenderOptions.SetBitmapScalingMode(image, System.Windows.Media.BitmapScalingMode.HighQuality);
            stack.Children.Add(image); stack.Children.Add(new TextBlock { Text = caption,
                HorizontalAlignment = HorizontalAlignment.Center, FontSize = 12 });
            var action = new Button { Content = buttonText, Command = command,
                HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 14, 0, 0) };
            action.SetResourceReference(StyleProperty, "StudioActionButtonStyle");
            System.Windows.Automation.AutomationProperties.SetAutomationId(action,
                column == 0 ? "UseOriginalIcon" : "ChooseReplacementIcon");
            stack.Children.Add(action);
            Grid.SetColumn(stack, column); previews.Children.Add(stack);
        }
        Preview(0, "原图标", "Selected.Original", "使用原图标", vm.UseOriginalAssetCommand);
        Preview(1, "替代图标", nameof(vm.AssetPreview), "选择图标", vm.ChooseIconCommand);
        content.Children.Add(previews);
        var tip = new TextBox { MaxLength = 127, Margin = new Thickness(0, 8, 0, 0) };
        tip.SetBinding(TextBox.TextProperty, new Binding(nameof(vm.Tip))
            { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
        System.Windows.Automation.AutomationProperties.SetAutomationId(tip, "IconTip");
        var label = new TextBlock { Text = "图标提示", Margin = new Thickness(15, 0, 0, 0) };
        FieldLabelAlignment.SetTarget(label, tip); content.Children.Add(label); content.Children.Add(tip);
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new Thickness(0, 10, 0, 0) };
        error.SetResourceReference(TextBlock.ForegroundProperty, "DangerTextBrush");
        error.SetBinding(TextBlock.TextProperty, nameof(vm.Error)); content.Children.Add(error);
        var scroll = new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        scroll.SetResourceReference(StyleProperty, "FadingDetailScrollViewerStyle");
        DeviceListViewport.SetEnabled(scroll, true); Grid.SetRow(scroll, 1); root.Children.Add(scroll);
        Content = root;
        System.Windows.Automation.AutomationProperties.SetAutomationId(this, "IconEditorPanel");
        Loaded += (_, _) => { Localization.Apply(this); tip.Focus(); };
    }
}

internal sealed class ImportConfirmationPanel : UserControl
{
    public ImportConfirmationPanel(Action<bool> complete)
    {
        var content = new StackPanel();
        content.Children.Add(new TextBlock { Text = "导入配置？", FontWeight = FontWeights.Medium, Margin = new Thickness(0, 0, 0, 14) });
        content.Children.Add(new TextBlock { Text = "将替换当前规则与偏好。原配置会自动保留为备份。", TextWrapping = TextWrapping.Wrap });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 18, 0, 0) };
        var accept = new Button { Content = "导入", Margin = new Thickness(0, 0, 8, 0) };
        accept.SetResourceReference(StyleProperty, "PrimaryButtonStyle");
        var cancel = new Button { Content = "取消" };
        accept.Click += (_, _) => complete(true); cancel.Click += (_, _) => complete(false);
        buttons.Children.Add(accept); buttons.Children.Add(cancel); content.Children.Add(buttons); Content = content;
        Loaded += (_, _) => Localization.Apply(this);
    }
}
