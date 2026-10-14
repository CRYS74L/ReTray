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
            ("every input method uses equal heading to icon spacing",EqualHeadingSpacing)})
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
                ["key"]="test-"+i,["name"]="输入法 "+i,["policy"]=new System.Text.Json.Nodes.JsonObject {
                    ["asset"]="",["index"]=0,["useReplacement"]=false }
            }},null)!);
            var editor=new InputMethodIconEditor(model,()=>System.Threading.Tasks.Task.CompletedTask);
            editor.Measure(new Size(400,double.PositiveInfinity));editor.Arrange(new Rect(0,0,400,editor.DesiredSize.Height));
            var content=(StackPanel)((ScrollViewer)((Grid)editor.Content).Children[0]).Content;
            var titles=content.Children.OfType<Grid>().ToArray();
            var choices=content.Children.OfType<IconChoiceRow>().ToArray();
            Require(choices.Length==count,"Editor did not represent every input method");
            Require(titles.Length==Math.Max(1,count),"Input methods did not share the heading component");
            double? gap=null;
            for(int i=0;i<count;i++) {
                var title=titles[i].Children.OfType<TextBlock>().Single();
                title.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
                double current=choices[i].TranslatePoint(new Point(),content).Y-title.TranslatePoint(new Point(0,title.ActualHeight),content).Y;
                if(gap.HasValue)Require(Math.Abs(gap.Value-current)<.01,"Heading to icon spacing depends on row position");
                gap=current;
            }
            Require(content.Children.OfType<Grid>().SelectMany(h=>h.Children.OfType<Button>()).Count(b=>b.Visibility==Visibility.Visible)==1,"Editor needs exactly one close button");
        }
    }
}
