using System.Windows;
using System.IO;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Interop;
using System.Windows.Threading;
using System.Xml.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using ReTray_App.Controls;
using System.Windows.Input;
using System.Text.Json.Nodes;
internal static class Program
{
 [STAThread] static int Main(string[] args) {
  try {
   var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};
   var xml=XDocument.Load(Path.Combine(AppContext.BaseDirectory,"ApplicationResources.xaml"));
   var resources=xml.Root!.Elements().Single(e=>e.Name.LocalName=="Application.Resources").Elements().Single();
   string old="clr-namespace:LanDeviceMonitor.Controls", current=old+";assembly=ReTray";
   foreach(var e in resources.DescendantsAndSelf()) {
    if(e.Name.NamespaceName==old)e.Name=XName.Get(e.Name.LocalName,current);
    foreach(var a in e.Attributes().ToArray()) {
     if(a.IsNamespaceDeclaration&&a.Value==old)a.Value=current;
     if(a.Name.LocalName=="PreviewMouseWheel")a.Remove();
     if(a.Name.LocalName=="Source"&&a.Value=="Controls/AppleControls.xaml")a.Value="/ReTray;component/Controls/AppleControls.xaml";
    }
   }
   resources.SetAttributeValue(XNamespace.Xmlns+"controls",current);
   resources.SetAttributeValue(XNamespace.Xmlns+"x","http://schemas.microsoft.com/winfx/2006/xaml");
   app.Resources=(ResourceDictionary)XamlReader.Parse(resources.ToString());
   LanDeviceMonitor.Services.ThemeService.Apply("Dark");
   using var fixtureOwner=new TestWindow();var fixture=fixtureOwner.Window;fixture.Show();Pump(80);
   var hook=typeof(LanDeviceMonitor.MainWindow).GetMethod("MainWindowWindowProcedure",BindingFlags.NonPublic|BindingFlags.Instance)!;
   foreach(int message in new[]{0xA4,0xA5,0xA6}) {
    object[] parameters=[new WindowInteropHelper(fixture).Handle,message,(nint)2,(nint)0,false];
    hook.Invoke(fixture,parameters);
    if(!(bool)parameters[4])throw new InvalidOperationException($"Caption right-button message 0x{message:X} reaches Windows system-menu handling");
   }
   var caption=fixture.PointToScreen(new Point(fixture.ActualWidth/2,16));
   long packed=((long)(ushort)(short)caption.Y<<16)|(ushort)(short)caption.X;
   object[] context=[new WindowInteropHelper(fixture).Handle,0x7B,(nint)0,(nint)packed,false];
   hook.Invoke(fixture,context);
   if(!(bool)context[4])throw new InvalidOperationException("Caption WM_CONTEXTMENU reaches default handling");
   var anchor=new Point(1300,900);var owner=new TrayCommandMenu();owner.Show(anchor);Pump(150);
   var menu=(ContextMenu)typeof(TrayCommandMenu).GetField("menu",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(owner)!;
   var surface=(FrameworkElement)typeof(TrayCommandMenu).GetMethod("Surface",BindingFlags.NonPublic|BindingFlags.Static)!.Invoke(null,[menu])!;
   var source=(HwndSource)PresentationSource.FromVisual(menu)!;
   var text=Descendants<TextBlock>(menu).OrderByDescending(t=>new FormattedText(t.Text,
       System.Globalization.CultureInfo.CurrentUICulture,t.FlowDirection,
       new Typeface(t.FontFamily,t.FontStyle,t.FontWeight,t.FontStretch),t.FontSize,Brushes.Black,
       VisualTreeHelper.GetDpi(t).PixelsPerDip).WidthIncludingTrailingWhitespace).First();
   if(!menu.Items.Cast<MenuItem>().Any(i=>Equals(i.Header,"打开")))
       throw new InvalidOperationException("Open command was not restored");
   var ink=new FormattedText(text.Text,System.Globalization.CultureInfo.CurrentUICulture,text.FlowDirection,
       new Typeface(text.FontFamily,text.FontStyle,text.FontWeight,text.FontStretch),text.FontSize,Brushes.Black,
       VisualTreeHelper.GetDpi(text).PixelsPerDip);
   var origin=text.TranslatePoint(new Point(0,0),surface);
   double left=origin.X+ink.OverhangLeading,right=surface.ActualWidth-origin.X-ink.Width-ink.OverhangTrailing;
   Console.WriteLine($"Longest menu text={text.Text}, left={left:F3}, right={right:F3}");
   // Compare visible ink, including the per-label bearing correction, against
   // the approved equal 2 DIP panel padding plus the border and text insets.
   if(Math.Abs(left-13.5)>.05 || Math.Abs(right-13.5)>.05)
       throw new InvalidOperationException("Tray menu does not preserve the approved equal ink insets");
   if(surface is not Border plate || plate.Padding!=new Thickness(2) ||
       plate.BorderThickness!=new Thickness(1.5) || plate.CornerRadius!=new CornerRadius(15.75))
       throw new InvalidOperationException("Tray plate differs from the approved dimensions");
   if(Math.Abs(surface.ActualHeight-97)>.05 || Math.Abs(surface.ActualWidth-79.039)>.1)
       throw new InvalidOperationException("Tray plate is not the approved auto-sized 79.039 x 97 DIP");
   foreach(var item in menu.Items.Cast<MenuItem>()) {
       if(item.FontSize!=13 || item.Height!=30 || item.Margin!=new Thickness(0))
           throw new InvalidOperationException("Tray item typography or spacing differs from the approved tuning");
       var label=Descendants<TextBlock>(item).Single();
       var labelOrigin=label.TranslatePoint(new Point(),item);
       if(Math.Abs(labelOrigin.Y+label.ActualHeight/2-15)>.05)
           throw new InvalidOperationException("Tray label is no longer vertically centered");
   }
   var target=menu.PlacementTarget;
   Console.WriteLine($"menuDpi={VisualTreeHelper.GetDpi(menu).DpiScaleX}, targetDpi={VisualTreeHelper.GetDpi(target).DpiScaleX}, menuHWNDDpi={GetDpiForWindow(source.Handle)}, targetHWNDDpi={GetDpiForWindow(((HwndSource)PresentationSource.FromVisual(target)!).Handle)}");
   var callback=menu.CustomPopupPlacementCallback;
   menu.CustomPopupPlacementCallback=(popup,targetSize,offset)=> {Console.WriteLine($"CALL popup={popup} target={targetSize} offset={offset} margin={surface.Margin}");return callback!(popup,targetSize,offset);};
   menu.HorizontalOffset=.01;Pump(100);
   var corner=surface.PointToScreen(new Point(0,surface.ActualHeight));
   Console.WriteLine($"surface={surface.ActualWidth}x{surface.ActualHeight}, lowerLeft={corner}, click={anchor}, delta={corner-anchor}");
   Pump(1000);Console.WriteLine($"residence={menu.IsOpen}, capture={System.Windows.Input.Mouse.Captured}");
   bool fullyClosed=false;menu.Closed+=(_,_)=>fullyClosed=true;
   menu.IsOpen=false;
   for(int i=0;i<20&&!fullyClosed;i++)Pump(20);
   if(args.Contains("--appearance-only")) {
       Console.WriteLine("Appearance, typography, ink insets and click-anchor checks passed; lifecycle scenarios were not selected");
       return Math.Abs(corner.X-anchor.X)<=1&&Math.Abs(corner.Y-anchor.Y)<=1?0:1;
   }
   fixture.Activate();SetForegroundWindow(new WindowInteropHelper(fixture).Handle);fixture.Focus();Pump(50);
   if(GetForegroundWindow()!=new WindowInteropHelper(fixture).Handle) {
       // A test launched by a background terminal may lack foreground rights.
       // Temporarily share the foreground input queue for fixture activation;
       // never change the product's activation behavior to accommodate a test.
       uint foregroundThread=GetWindowThreadProcessId(GetForegroundWindow(),out uint foregroundPid);
       uint testThread=GetCurrentThreadId();
       Console.WriteLine($"Fixture activation denied: foregroundPid={foregroundPid}, testPid={Environment.ProcessId}");
       bool attached=foregroundThread!=testThread&&AttachThreadInput(testThread,foregroundThread,true);
       try {fixture.Activate();SetForegroundWindow(new WindowInteropHelper(fixture).Handle);fixture.Focus();}
       finally {if(attached)AttachThreadInput(testThread,foregroundThread,false);}
       Pump(50);
   }
   Console.WriteLine($"FOREGROUND_REQUIRED {new WindowInteropHelper(fixture).Handle}");Console.Out.Flush();
   for(int i=0;i<100&&GetForegroundWindow()!=new WindowInteropHelper(fixture).Handle;i++)Pump(50);
   Console.WriteLine($"local source foreground={GetForegroundWindow()}, expected={new WindowInteropHelper(fixture).Handle}");
   if(GetForegroundWindow()!=new WindowInteropHelper(fixture).Handle)throw new InvalidOperationException("Test fixture could not acquire foreground for the local-menu interaction");
   var localMethod=typeof(LanDeviceMonitor.MainWindow).GetMethod("TryShowOwnTrayMenu",BindingFlags.NonPublic|BindingFlags.Instance)!;
   var own=new ReTray_App.Models.IconRow(new JsonObject {
    ["key"]="own",["owned"]=true,["placement"]=0,["pixels"]="",["originalPixels"]="",
    ["selector"]=new JsonObject{["executable"]="ReTray.exe",["hasGuid"]=false,["uid"]=1},["pid"]=Environment.ProcessId
   });
   var action=new TrayIconActionEventArgs("menu",false,1300,900);
   if(!(bool)localMethod.Invoke(fixture,[own,action,fixture])!)throw new InvalidOperationException("Own row was routed back to Explorer");
   var localOwner=(TrayCommandMenu)typeof(LanDeviceMonitor.MainWindow).GetField("_trayCommandMenu",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(fixture)!;
   var localMenu=(ContextMenu)typeof(TrayCommandMenu).GetField("menu",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(localOwner)!;
   Pump(1600);
   if(!localMenu.IsOpen)throw new InvalidOperationException("Own row menu dismissed before interaction");
   Console.WriteLine("Own row menu remains open after 1600ms");
   bool escapedClosed=false;localMenu.Closed+=(_,_)=>escapedClosed=true;
   localMenu.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(localMenu),Environment.TickCount,Key.Escape){RoutedEvent=Keyboard.KeyDownEvent});
   Pump(50);if(localMenu.IsOpen)throw new InvalidOperationException("Escape did not dismiss the menu");
   for(int i=0;i<20&&!escapedClosed;i++)Pump(20);
   localMethod.Invoke(fixture,[own,action,fixture]);Pump(100);
   localMenu=(ContextMenu)typeof(TrayCommandMenu).GetField("menu",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(localOwner)!;
   localMenu.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,Environment.TickCount,MouseButton.Left){RoutedEvent=Mouse.PreviewMouseDownOutsideCapturedElementEvent});
   Pump(50);if(localMenu.IsOpen)throw new InvalidOperationException("Outside click did not dismiss the menu");
   Console.WriteLine("Caption right clicks suppressed; own menu Escape/outside dismissal passed");
   // Match the product's native hide lifecycle: WPF's visual stays visible
   // while only its HWND is hidden. Window.Hide tears that visual down.
   Pump(250);ShowWindow(new WindowInteropHelper(fixture).Handle,0);Pump(50);
   var hiddenOwner=new TrayCommandMenu();
   // Native tray requests are marshalled onto the WPF dispatcher rather than
   // opened synchronously inside the previous menu's close/focus transition.
   fixture.Dispatcher.BeginInvoke(new Action(()=>hiddenOwner.Show(anchor)));
   Pump(1000);
   var hiddenMenu=(ContextMenu)typeof(TrayCommandMenu).GetField("menu",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(hiddenOwner)!;
   if(hiddenMenu is null || !hiddenMenu.IsOpen)throw new InvalidOperationException("System tray menu failed to stay open with a hidden main window");
   hiddenMenu.IsOpen=false;Pump(250);
   Console.WriteLine("Hidden-owner tray menu remained open for 1000ms");
   return Math.Abs(corner.X-anchor.X)<=1&&Math.Abs(corner.Y-anchor.Y)<=1?0:1;
  } catch(Exception e){Console.Error.WriteLine(e);return 2;}
 }
 static void Pump(int ms){var frame=new DispatcherFrame();var timer=new DispatcherTimer(DispatcherPriority.Background){Interval=TimeSpan.FromMilliseconds(ms)};timer.Tick+=(_,_)=>{timer.Stop();frame.Continue=false;};timer.Start();Dispatcher.PushFrame(frame);}
 static IEnumerable<T> Descendants<T>(DependencyObject root) where T:DependencyObject {
  for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++) {var child=VisualTreeHelper.GetChild(root,i);if(child is T match)yield return match;foreach(var next in Descendants<T>(child))yield return next;}
 }
 [DllImport("user32.dll")] static extern uint GetDpiForWindow(nint h);
 [DllImport("user32.dll")] static extern nint GetForegroundWindow();
 [DllImport("user32.dll")] static extern bool SetForegroundWindow(nint h);
 [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(nint h,out uint pid);
 [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
 [DllImport("user32.dll")] static extern bool AttachThreadInput(uint source,uint target,bool attach);
 [DllImport("user32.dll")] static extern bool ShowWindow(nint window,int command);
 sealed class TestWindow:IDisposable {
  public LanDeviceMonitor.MainWindow Window{get;}
  public TestWindow(){Window=(LanDeviceMonitor.MainWindow)Activator.CreateInstance(typeof(LanDeviceMonitor.MainWindow),BindingFlags.NonPublic|BindingFlags.Instance,null,[new LanDeviceMonitor.Services.NotificationService(),true],null)!;Window.ShowActivated=true;Window.ShowInTaskbar=false;Window.Opacity=.01;}
  public void Dispose(){Window.PrepareForExit();Window.Close();}
 }
}
