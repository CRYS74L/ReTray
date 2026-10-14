using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.Input;
using LanDeviceMonitor.Controls;
using TrayAssistant_App;
internal static class Program
{
    [STAThread] static int Main()
    {
        int failed=0;
        foreach(var (name,test) in new (string,Action)[]{
            ("input method rows switch independently and retain selection",IndependentRows),
            ("selected replacement synchronizes the visible slider",SelectedFileUpdatesMode),
            ("locale switching preserves source labels and user bindings",LocaleRoundtrip),
            ("every input method balances heading and slider gaps",EqualHeadingSpacing),
            ("language order and saved codes stay independent",LanguageOrder)})
            try{test();Console.WriteLine("PASS "+name);}catch(Exception e){failed++;Console.Error.WriteLine("FAIL "+name+": "+e.Message);}
        return failed==0?0:1;
    }
    private sealed class State : INotifyPropertyChanged
    {
        public ImageSource Original{get;}=new WriteableBitmap(1,1,96,96,PixelFormats.Bgra32,null);
        public ImageSource AssetPreview{get;}=new WriteableBitmap(2,2,96,96,PixelFormats.Bgra32,null);
        private bool mode;
        public bool UseReplacement{get=>mode;set{mode=value;PropertyChanged?.Invoke(this,new(nameof(UseReplacement)));}}
        public event PropertyChangedEventHandler? PropertyChanged;
    }
    private static IconChoiceRow Row(State state)=>new(state,nameof(state.Original),()=>state.AssetPreview,
        ()=>state.UseReplacement,value=>state.UseReplacement=value,new RelayCommand(()=>{}));
    private static SegmentedControl Slider(IconChoiceRow row)=>((StackPanel)row.Content).Children.OfType<SegmentedControl>().Single();
    private static void Require(bool value,string message){if(!value)throw new Exception(message);}
    private static void IndependentRows()
    {
        var a=new State();var b=new State();var first=Row(a);var second=Row(b);var remembered=a.AssetPreview;
        Slider(first).SelectedIndex=1;
        Require(a.UseReplacement && !b.UseReplacement && Slider(second).SelectedIndex==0,"Switching one input method changed another");
        Slider(first).SelectedIndex=0;
        Require(!a.UseReplacement && ReferenceEquals(remembered,a.AssetPreview),"Original mode erased the selected replacement");
        Slider(first).SelectedIndex=1;
        Require(a.UseReplacement && ReferenceEquals(remembered,a.AssetPreview),"Reselecting replacement lost the previous file");
    }
    private static void SelectedFileUpdatesMode()
    {
        var state=new State();var row=Row(state);row.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
        state.UseReplacement=true;
        Require(Slider(row).SelectedIndex==1,"Model selected a custom file but slider still shows original mode");
        row.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
    }
    private static void LocaleRoundtrip()
    {
        var label=new TextBlock{Text="系统图标"};
        var user=new TextBlock();user.SetBinding(TextBlock.TextProperty,new System.Windows.Data.Binding{Source="系统图标"});
        var root=new StackPanel();root.Children.Add(label);root.Children.Add(user);
        foreach(var pair in new[]{("zh-Hant","系統圖示"),("ja","システムアイコン"),("en","System icons"),("zh","系统图标")}) {
            TrayAssistant_App.Services.Localization.CurrentLanguage=pair.Item1;
            TrayAssistant_App.Services.Localization.Apply(root);
            Require(label.Text==pair.Item2,"Locale label did not roundtrip: "+pair.Item1);
            Require(user.Text=="系统图标" && System.Windows.Data.BindingOperations.IsDataBound(user,TextBlock.TextProperty),"Locale replaced user binding");
        }
    }
    private static void EqualHeadingSpacing()
    {
        foreach(int count in new[]{0,1,2,5}) {
            var model=new InputMethodIconEditorModel(new TrayAssistant_App.Services.PipeClient());
            for(int i=0;i<count;i++)model.Rows.Add((InputMethodIconRow)Activator.CreateInstance(typeof(InputMethodIconRow),System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic,null,new object[]{model,new System.Text.Json.Nodes.JsonObject {
                ["original"]=new System.Text.Json.Nodes.JsonObject { ["width"]=1,["height"]=1,["pixels"]="AAAA/w==" },
                ["key"]="test-"+i,["name"]=i%3==0?"微信输入法":i%3==1?"Google Japanese Input":"日本語入力",["policy"]=new System.Text.Json.Nodes.JsonObject {
                    ["asset"]="",["index"]=0,["useReplacement"]=false }
            }},null)!);
            var editor=new InputMethodIconEditor(model,()=>System.Threading.Tasks.Task.CompletedTask);
            var window=new Window{Content=editor,Width=400,SizeToContent=SizeToContent.Height,ShowActivated=false,ShowInTaskbar=false,Opacity=0};window.Show();window.UpdateLayout();
            var root=(Grid)editor.Content;
            var scroll=root.Children.OfType<ScrollViewer>().Single();
            var content=(StackPanel)scroll.Content;
            var fixedHeader=root.Children.OfType<Grid>().SingleOrDefault();
            Require(fixedHeader!=null,"Close header is still inside the clipped scrolling viewport");
            var titles=new[]{fixedHeader!}.Concat(content.Children.OfType<Grid>()).ToArray();
            var choices=content.Children.OfType<IconChoiceRow>().ToArray();
            Require(choices.Length==count,"Editor did not represent every input method");
            Require(titles.Length==Math.Max(1,count),"Input methods did not share the heading component");
            for(int i=0;i<count;i++) {
                var title=titles[i].Children.OfType<TextBlock>().Single();
                title.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
                for(int pass=0;pass<3;pass++){editor.Measure(new Size(400,double.PositiveInfinity));editor.Arrange(new Rect(0,0,400,editor.DesiredSize.Height));editor.UpdateLayout();}
                var image=Find<Image>(choices[i]).First();
                var mode=Slider(choices[i]);
                var ink=new System.Windows.Media.FormattedText(title.Text,System.Globalization.CultureInfo.CurrentCulture,title.FlowDirection,
                    new Typeface(title.FontFamily,title.FontStyle,title.FontWeight,title.FontStretch),title.FontSize,Brushes.Black,VisualTreeHelper.GetDpi(title).PixelsPerDip) {MaxTextWidth=title.ActualWidth,MaxLineCount=1,Trimming=TextTrimming.CharacterEllipsis}.BuildGeometry(new Point()).Bounds;
                double top=image.TranslatePoint(new Point(),root).Y-title.TranslatePoint(new Point(ink.Left,ink.Bottom),root).Y;
                var caption=Find<TextBlock>(choices[i]).First(t=>t.Text=="原始图标");
                var captionInk=new FormattedText(caption.Text,System.Globalization.CultureInfo.CurrentCulture,caption.FlowDirection,new Typeface(caption.FontFamily,caption.FontStyle,caption.FontWeight,caption.FontStretch),caption.FontSize,Brushes.Black,VisualTreeHelper.GetDpi(caption).PixelsPerDip).BuildGeometry(new Point()).Bounds;
                double bottom=mode.TranslatePoint(new Point(),root).Y-caption.TranslatePoint(new Point(captionInk.Left,captionInk.Bottom),root).Y;
                Require(Math.Abs(top-bottom)<.5,"Heading and slider gaps differ in row "+i+": "+top+" vs "+bottom);
            }
            Require(titles.SelectMany(h=>Find<Button>(h)).Count(b=>b.Visibility==Visibility.Visible)==1,"Editor needs exactly one close button");window.Close();
        }
    }
    private static IEnumerable<T> Find<T>(DependencyObject root) where T:DependencyObject
    {
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++) {
            var child=VisualTreeHelper.GetChild(root,i);if(child is T typed)yield return typed;
            foreach(var nested in Find<T>(child))yield return nested;
        }
    }
    private static void LanguageOrder()
    {
        var names=TrayAssistant_App.Services.Localization.LanguageNames;
        Require(names.SequenceEqual(new[]{"简体中文","繁體中文","日本語","English"}),"Language dropdown order differs");
        foreach(var code in new[]{"zh","zh-Hant","ja","en"})
            Require(TrayAssistant_App.Services.Localization.LanguageCodes[TrayAssistant_App.Services.Localization.LanguageIndex(code)]==code,"Saved language code changed meaning");
        Require(TrayAssistant_App.Services.Localization.LanguageIndex(null)==0,"Default language must be simplified Chinese");
    }
}
