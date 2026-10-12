using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.Windows.Storage.Pickers;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using TrayAssistant_App.Controls;
using TrayAssistant_App.Models;
using TrayAssistant_App.Services;
using TrayAssistant_App.ViewModels;
namespace TrayAssistant_App;
public sealed partial class MainPage : Page
{
 public TrayViewModel Vm { get; }=new();
 private readonly DispatcherTimer timer=new(){Interval=TimeSpan.FromSeconds(1)};
 private bool loaded,editorOpen,systemLoading,systemLoaded,systemRefreshPending;
 private readonly SemaphoreSlim systemSaveGate=new(1,1);
 private static readonly string[] SystemNames={"网络","音量","电源","输入法 / 语言","时钟","通知","隐私指示器","开始","快速设置"};
 private readonly bool[] systemAvailable=new bool[SystemNames.Length];
 public async Task<bool> FlushChangesAsync(){await systemSaveGate.WaitAsync();systemSaveGate.Release();return await Vm.FlushChangesAsync();}
 public MainPage(){InitializeComponent();CreateSystemRows();LoadingRows.ItemsSource=Enumerable.Range(0,10);Vm.PropertyChanged+=(_,e)=>{if(e.PropertyName is nameof(Vm.IsReady) or nameof(Vm.IsLoading) or nameof(Vm.Error))UpdatePresentation();if(e.PropertyName==nameof(Vm.IsReady)){if(Vm.IsReady)_=LoadSystemAsync();else systemLoaded=false;}};UpdatePresentation();Vm.LanguageChanged+=()=>Localization.Apply(this);Vm.ThemeChanged+=index=>{RequestedTheme=index switch{1=>ElementTheme.Light,2=>ElementTheme.Dark,_=>ElementTheme.Default};((FrameworkElement)App.Window.Content).RequestedTheme=RequestedTheme;};Vm.WindowStyleChanged+=(topmost,_)=>{if(App.Window.AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter)presenter.IsAlwaysOnTop=topmost;};timer.Tick+=async(_,_)=>await Vm.RefreshAsync();Unloaded+=(_,_)=>timer.Stop();}
 private async void Page_Loaded(object sender,RoutedEventArgs e){if(!loaded){loaded=true;await Vm.InitializeAsync();RefreshHotkeyBoxes();}Localization.Apply(this);timer.Start();}
 private void Nav_Loaded(object sender,RoutedEventArgs args)
 {
  // Top navigation does not use a side pane. Its template otherwise retains a 320 DIP
  // pane column, which arranges the content wider than windows below that width.
  void ClearSidePane(DependencyObject element)
  {
   if(element is SplitView{ Name:"RootSplitView" } split){split.OpenPaneLength=0;split.CompactPaneLength=0;return;}
   for(int index=0;index<VisualTreeHelper.GetChildrenCount(element);index++)ClearSidePane(VisualTreeHelper.GetChild(element,index));
  }
  ClearSidePane(Nav);
 }
 private void Nav_SelectionChanged(NavigationView sender,NavigationViewSelectionChangedEventArgs e){if(IconList==null)return;var tag=(e.SelectedItem as NavigationViewItem)?.Tag?.ToString();UpdatePresentation();SettingsView.Visibility=tag=="settings"?Visibility.Visible:Visibility.Collapsed;if(tag=="settings")_=LoadSystemAsync(true);}
 private void UpdatePresentation(){if(IconList==null||LoadingList==null)return;bool icons=(Nav.SelectedItem as NavigationViewItem)?.Tag?.ToString()=="icons";IconList.Visibility=icons&&Vm.IsReady?Visibility.Visible:Visibility.Collapsed;LoadingList.Visibility=icons&&Vm.IsLoading?Visibility.Visible:Visibility.Collapsed;ErrorNotice.IsOpen=Vm.Error.Length>0;}
 private void IconList_DragItemsStarting(object sender,DragItemsStartingEventArgs e){Vm.IsDragging=true;}
 private async void IconList_DragItemsCompleted(ListViewBase sender,DragItemsCompletedEventArgs e){try{await Vm.PersistOrderAsync();}finally{Vm.IsDragging=false;await Vm.RefreshAsync();}}
 private async void Icon_NativeAction(object sender,TrayIconActionEventArgs e){if(sender is TrayIconButton{Tag:IconRow row})await Vm.NativeActionAsync(row,e.Action,e.Keyboard,e.X,e.Y);}
 private async void Visibility_Toggled(object sender,RoutedEventArgs e)
 {
  var toggle=(ToggleSwitch)sender;
  var wasDragging=Vm.IsDragging;
  // Compiled bindings update IsOn before Tag when a recycled row changes identity.
  // Let that synchronous update finish before resolving the current row.
  await Task.Yield();
  // Model updates already agree with Visible; only a user change needs a command.
  if(toggle.Tag is not IconRow row||!ReferenceEquals(toggle.DataContext,row)||toggle.IsOn==row.Visible)return;
  if(!wasDragging&&!Vm.IsDragging)await Vm.SetRowVisibilityAsync(row,toggle.IsOn);
  if(ReferenceEquals(toggle.Tag,row)&&ReferenceEquals(toggle.DataContext,row))toggle.IsOn=row.Visible;
 }
 private async void IconSettings_Click(object sender,RoutedEventArgs e)
 {
  if(editorOpen||((Button)sender).Tag is not IconRow row)return;
  Vm.Selected=row;editorOpen=true;
  var content=(FrameworkElement)IconEditor.Content;
  var theme=WindowAppearance.ResolveTheme(RequestedTheme);
  // Keep the editor's theme while it has no logical parent during window transfer.
  content.RequestedTheme=theme;
  IconEditor.Content=null;
  try{await MicaDialog.ShowAsync(row.Name,content,theme,beforeClose:Vm.FlushChangesAsync);}
  finally{IconEditor.Content=content;content.RequestedTheme=ElementTheme.Default;Vm.Selected=null;editorOpen=false;}
 }
 private async void Exit_Click(object sender,RoutedEventArgs e)=>await App.QuitAsync();
 [System.Runtime.InteropServices.DllImport("user32.dll")]private static extern short GetKeyState(int key);
 private void HotkeyBox_KeyDown(object sender,Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e){var box=(TextBox)sender;int key=(int)e.Key;if(key is 16 or 17 or 18 or 91 or 92)return;int modifiers=(GetKeyState(17)<0?2:0)|(GetKeyState(18)<0?1:0)|(GetKeyState(16)<0?4:0)|(GetKeyState(91)<0||GetKeyState(92)<0?8:0);int code=key is 8 or 46?0:modifiers==0?0:(modifiers<<16)|key;e.Handled=true;var tag=box.Tag.ToString()!;if(tag=="icon"){Vm.Hotkey=code;Vm.HotkeyText=TrayViewModel.DescribeHotkey(code);}else{Vm.Hotkeys[tag]=code;box.Text=TrayViewModel.DescribeHotkey(code);Vm.QueueMenuPreferences();}}
 private void RefreshHotkeyBoxes(){foreach(var key in new[]{"open"})if(FindName("Hotkey_"+key) is TextBox box)box.Text=TrayViewModel.DescribeHotkey(Vm.Hotkeys[key]?.GetValue<int>()??0);}
 private void CreateSystemRows()
 {
  for(int id=0;id<SystemNames.Length;id++)
  {
   var toggle=new ToggleSwitch{Tag=id,IsEnabled=false};
   Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(toggle,Localization.Text(SystemNames[id]));
   Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(toggle,"System"+id);
   var row=new Grid{MinHeight=34,ColumnSpacing=8};
   row.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});
   row.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
   row.Children.Add(new TextBlock{Text=Localization.Text(SystemNames[id]),VerticalAlignment=VerticalAlignment.Center,TextWrapping=TextWrapping.Wrap});
   Grid.SetColumn(toggle,1);row.Children.Add(toggle);toggle.Toggled+=SystemVisibility_Toggled;SystemList.Children.Add(row);
  }
 }
 private async Task LoadSystemAsync(bool refresh=false)
 {
  if(!Vm.IsReady)return;
  if(systemLoading){systemRefreshPending|=refresh;return;}
  if(systemLoaded&&!refresh)return;systemLoading=true;
  var toggles=SystemList.Children.Cast<Grid>().Select(row=>row.Children.OfType<ToggleSwitch>().Single()).ToArray();
  foreach(var toggle in toggles)toggle.IsEnabled=false;
  await systemSaveGate.WaitAsync();
  try
  {
   var result=await Vm.Pipe.RequestAsync("system.read");
   if(result["success"]?.GetValue<bool>()!=true)throw new IOException(result["detail"]?.GetValue<string>());
   foreach(var item in result["data"]!.AsArray())
   {
    int id=item!["id"]!.GetValue<int>();if(id<0||id>=SystemNames.Length)continue;
    var toggle=((Grid)SystemList.Children[id]).Children.OfType<ToggleSwitch>().Single();
    toggle.IsOn=item["visible"]!.GetValue<bool>();systemAvailable[id]=(item["instances"]?.GetValue<int>()??0)>0;
   }
   systemLoaded=true;
  }
  catch(Exception ex){Vm.ReportError(ex);}
  finally{systemSaveGate.Release();systemLoading=false;for(int i=0;i<toggles.Length;i++)toggles[i].IsEnabled=systemAvailable[i];if(systemRefreshPending){systemRefreshPending=false;_=LoadSystemAsync(true);}}
 }
 private async void SystemVisibility_Toggled(object sender,RoutedEventArgs args)
 {
  if(sender is not ToggleSwitch{Tag:int id,IsEnabled:true} toggle||systemLoading)return;
  var visible=toggle.IsOn;toggle.IsEnabled=false;await systemSaveGate.WaitAsync();
  try
  {
   var read=await Vm.Pipe.RequestAsync("settings.read");
   if(read["success"]?.GetValue<bool>()!=true)throw new IOException(read["detail"]?.GetValue<string>());
   var policies=(JsonArray?)read["data"]?["systemControls"]?.DeepClone()??new();
   for(int n=policies.Count-1;n>=0;n--)if(policies[n]!["id"]!.GetValue<int>()==id)policies.RemoveAt(n);
   policies.Add(new JsonObject{["id"]=id,["hidden"]=!visible});
   var response=await Vm.Pipe.RequestAsync("system.apply",new(){["controls"]=policies});
   if(response["success"]?.GetValue<bool>()!=true)throw new IOException(response["detail"]?.GetValue<string>());
  }
  catch(Exception ex){toggle.IsOn=!visible;Vm.ReportError(ex);}
  finally{systemSaveGate.Release();toggle.IsEnabled=systemAvailable[id]&&!systemLoading;}
 }
 private async void Export_Click(object sender,RoutedEventArgs e){try{var picker=new FileSavePicker(App.Window.AppWindow.Id){SuggestedFileName="托盘助手配置"};picker.FileTypeChoices.Add("助手配置",new List<string>{".json"});var file=await picker.PickSaveFileAsync();if(file==null)return;var r=await Vm.Pipe.RequestAsync("settings.read");var settings=(JsonObject)r["data"]!.DeepClone();var assets=new JsonObject();foreach(var rule in settings["rules"]!.AsArray())await Embed(rule!.AsObject(),assets);await Embed(settings["ownIcon"]!.AsObject(),assets);settings["assets"]=assets;await File.WriteAllTextAsync(file.Path,settings.ToJsonString(new(){WriteIndented=true}));Vm.Status="配置与图标已导出";}catch(Exception ex){Vm.ReportError(ex);}}
 private static async Task Embed(JsonObject node,JsonObject assets){var path=node["asset"]?.GetValue<string>()??"";if(path.Length==0)return;var bytes=await File.ReadAllBytesAsync(path);var name=Convert.ToHexString(SHA256.HashData(bytes))+Path.GetExtension(path);assets[name]=Convert.ToBase64String(bytes);node["asset"]=name;}
 private async void Import_Click(object sender,RoutedEventArgs e){try{var picker=new FileOpenPicker(App.Window.AppWindow.Id);picker.FileTypeFilter.Add(".json");var file=await picker.PickSingleFileAsync();if(file==null)return;if(new FileInfo(file.Path).Length>96*1024*1024)throw new IOException("配置文件过大");var bundle=JsonNode.Parse(await File.ReadAllTextAsync(file.Path))!.AsObject();if(bundle["schema"]?.GetValue<int>()!=1)throw new IOException("配置版本不兼容");var message=new TextBlock{Text=Localization.English?"This replaces current rules and preferences. The previous configuration is backed up automatically.":"将替换当前规则与偏好。原配置会自动保留为备份。",TextWrapping=TextWrapping.Wrap};if(!await MicaDialog.ShowAsync(Localization.English?"Import configuration?":"导入配置？",message,RequestedTheme,Localization.English?"Import":"导入",Localization.English?"Cancel":"取消",height:240))return;var settings=await BackupService.PrepareImportAsync(bundle);var result=await Vm.Pipe.RequestAsync("settings.import",new(){["settings"]=settings});if(result["success"]?.GetValue<bool>()!=true)throw new IOException(result["detail"]?.GetValue<string>());Vm.Status=Localization.English?"Configuration imported":"配置已导入";await Vm.InitializeAsync();systemLoaded=false;await LoadSystemAsync(true);RefreshHotkeyBoxes();}catch(Exception ex){Vm.ReportError(ex);}}
}
