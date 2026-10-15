using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using LanDeviceMonitor.Controls;
using Localization = ReTray_App.Services.Localization;
namespace ReTray_App;

// Shared by tray and input-method editors so their canvases, slider and motion stay identical.
public sealed class IconChoiceRow : UserControl
{
    internal Image OriginalPreview { get; private set; } = null!;
    internal Grid OriginalCanvas { get; private set; } = null!;
    internal TextBlock OriginalCaption { get; private set; } = null!;
    internal SegmentedControl ImageMode { get; private set; } = null!;
    public IconChoiceRow(object source, string originalPath, Func<ImageSource?> preview,
        Func<bool> getMode, Action<bool> setMode, ICommand chooseCommand, string prefix = "", double bottomSpacing = 18)
    {
        var updates = (INotifyPropertyChanged)source;
        var content = new StackPanel();
        var previews = new Grid { Margin = new Thickness(0, 4, 0, 18) };
        previews.ColumnDefinitions.Add(new ColumnDefinition()); previews.ColumnDefinitions.Add(new ColumnDefinition());
        StackPanel Preview(int column, string caption, FrameworkElement visual)
        {
            var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            visual.Margin = new Thickness(0, 0, 0, 8);
            visual.HorizontalAlignment = HorizontalAlignment.Center;
            var label = new TextBlock { Text = caption, HorizontalAlignment = HorizontalAlignment.Center, FontSize = 12 };
            if (column == 0) OriginalCaption = label;
            stack.Children.Add(visual); stack.Children.Add(label);
            Grid.SetColumn(stack, column); previews.Children.Add(stack);
            return stack;
        }
        var originalImage = new Image { Width = 44, Height = 44, Stretch = Stretch.Uniform };
        OriginalPreview = originalImage;
        originalImage.SetBinding(Image.SourceProperty, new Binding(originalPath) { Source = source });
        RenderOptions.SetBitmapScalingMode(originalImage, BitmapScalingMode.HighQuality);
        System.Windows.Automation.AutomationProperties.SetAutomationId(originalImage, prefix + "OriginalIconPreview");
        var originalCanvas = new Grid { Width = 56, Height = 56 };
        OriginalCanvas = originalCanvas;
        originalImage.HorizontalAlignment = HorizontalAlignment.Center; originalImage.VerticalAlignment = VerticalAlignment.Center;
        originalCanvas.Children.Add(originalImage);
        var original = Preview(0, "原始图标", originalCanvas);
        var choose = new Button { Width = 56, Height = 56, MinWidth = 0, MinHeight = 0,
            Command = chooseCommand };
        choose.SetResourceReference(StyleProperty, "ReplacementIconButtonStyle");
        System.Windows.Automation.AutomationProperties.SetAutomationId(choose, prefix + "ChooseReplacementIcon");
        System.Windows.Automation.AutomationProperties.SetName(choose, Localization.Text("选择替代图标"));
        var choiceVisual = new Grid();
        var replacementImage = new Image { Width = 44, Height = 44, Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        System.Windows.Automation.AutomationProperties.SetAutomationId(replacementImage, prefix + "ReplacementIconPreview");
        replacementImage.SetBinding(Image.SourceProperty, new Binding("AssetPreview") { Source = source });
        RenderOptions.SetBitmapScalingMode(replacementImage, BitmapScalingMode.HighQuality);
        choiceVisual.Children.Add(replacementImage);
        var plus = new TextBlock { Text = "\uE710", FontSize = 18,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        plus.SetResourceReference(TextBlock.FontFamilyProperty, "AppIconFont");
        GlyphInkAlignment.SetEnabled(plus, true);
        System.Windows.Automation.AutomationProperties.SetAutomationId(plus, prefix + "ReplacementIconAdd");
        var plusVisual = new Grid { Width = 24, Height = 24, HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center };
        plusVisual.Children.Add(plus); choiceVisual.Children.Add(plusVisual); choose.Content = choiceVisual;
        IconEditorPanel.AddQuarterTurn(choose, plusVisual);
        choose.ToolTip = Localization.Text("点击更换替代图标");
        void UpdateChoice(bool animate)
        {
            var hasImage = preview() != null;
            var hover = choose.IsMouseOver;
            var duration = TimeSpan.FromMilliseconds(animate && SystemParameters.ClientAreaAnimation ? 240 : 0);
            replacementImage.BeginAnimation(OpacityProperty, new DoubleAnimation(hasImage && hover ? .2 : 1, duration)
                { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            plusVisual.BeginAnimation(OpacityProperty, new DoubleAnimation(!hasImage || hover ? 1 : 0, duration)
                { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        }
        choose.MouseEnter += (_, _) => UpdateChoice(true); choose.MouseLeave += (_, _) => UpdateChoice(true);
        var replacement = Preview(1, "替代图标", choose);
        content.Children.Add(previews);
        var mode = new SegmentedControl { Height = 36, MinHeight = 36,
            HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 0, 0, bottomSpacing),
            FontSize = 14, AnimationMilliseconds = 480 };
        ImageMode = mode;
        SegmentItem ModeItem(string caption)
        {
            var item = new SegmentItem();
            var text = new TextBlock { Text = caption };
            text.SetBinding(TextBlock.ForegroundProperty, new Binding(nameof(SegmentItem.Foreground)) { Source = item });
            item.Content = text; return item;
        }
        var originalMode = ModeItem("使用原始图标");
        var replacementMode = ModeItem("使用替代图标");
        System.Windows.Automation.AutomationProperties.SetAutomationId(mode, prefix + "IconImageMode");
        System.Windows.Automation.AutomationProperties.SetAutomationId(originalMode, prefix + "IconModeOriginal");
        System.Windows.Automation.AutomationProperties.SetAutomationId(replacementMode, prefix + "IconModeReplacement");
        mode.Items.Add(originalMode); mode.Items.Add(replacementMode);
        var originalMove = new TranslateTransform(); original.RenderTransform = originalMove;
        var replacementMove = new TranslateTransform(); replacement.RenderTransform = replacementMove;
        int generation = 0;
        void ShowMode(bool animate)
        {
            var currentGeneration = ++generation;
            var custom = mode.SelectedIndex == 1;
            var wasVisible = replacement.Visibility == Visibility.Visible && replacement.Opacity > 0;
            var replacementX = custom && !wasVisible ? 8 : replacementMove.X;
            var previous = original.TranslatePoint(new Point(), previews).X;
            originalMove.BeginAnimation(TranslateTransform.XProperty, null); originalMove.X = 0;
            Grid.SetColumnSpan(original, custom ? 1 : 2);
            if (!animate || !IsLoaded || !SystemParameters.ClientAreaAnimation)
            {
                replacement.BeginAnimation(OpacityProperty, null);
                replacementMove.BeginAnimation(TranslateTransform.XProperty, null); replacementMove.X = 0;
                replacement.Opacity = custom ? 1 : 0;
                replacement.Visibility = custom ? Visibility.Visible : Visibility.Collapsed;
                return;
            }
            replacement.Visibility = Visibility.Visible;
            previews.UpdateLayout();
            var target = original.TranslatePoint(new Point(), previews).X;
            var duration = TimeSpan.FromMilliseconds(animate && IsLoaded && SystemParameters.ClientAreaAnimation ? mode.AnimationMilliseconds : 0);
            originalMove.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(previous - target, 0, duration)
                { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            var fade = new DoubleAnimation(custom ? 1 : 0, duration)
                { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            fade.Completed += (_, _) => { if (currentGeneration == generation && !custom) replacement.Visibility = Visibility.Collapsed; };
            replacement.BeginAnimation(OpacityProperty, fade);
            replacementMove.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(replacementX, custom ? 0 : 8, duration)
                { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        }
        mode.SelectedIndex = getMode() ? 1 : 0;
        ShowMode(false);
        mode.SelectionChanged += (_, args) =>
        {
            if (!ReferenceEquals(args.Source, mode)) return;
            setMode(mode.SelectedIndex == 1);
            ShowMode(true);
        };
        content.Children.Add(mode);
        System.ComponentModel.PropertyChangedEventHandler previewChanged = (_, args) =>
        {
            if (args.PropertyName == "AssetPreview") UpdateChoice(true);
            if (args.PropertyName == "UseReplacement" && mode.SelectedIndex != (getMode() ? 1 : 0))
                mode.SelectedIndex = getMode() ? 1 : 0;
        };
        Loaded += (_, _) => { updates.PropertyChanged += previewChanged; UpdateChoice(false); };
        Unloaded += (_, _) => updates.PropertyChanged -= previewChanged;
        Content = content;
    }
}
