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
            ("selected replacement synchronizes the visible slider",SelectedFileUpdatesMode)})
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
}
