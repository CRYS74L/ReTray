using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using LanDeviceMonitor;
using LanDeviceMonitor.Controls;
using LanDeviceMonitor.Services;

internal static class ResizeCacheRegression
{
    internal static int Run()
    {
        Exception? failure=null;
        var thread=new Thread(()=>
        {
            App? app=null; MainWindow? resources=null; Window? host=null;
            try
            {
                app=TestAppFactory.Create();app.InitializeComponent();
                resources=new MainWindow(new NotificationService());
                resources.WindowStartupLocation=WindowStartupLocation.Manual;
                resources.Left=100;resources.Top=60;resources.Show();resources.UpdateLayout();
                foreach(var width in new[]{916d,922,940,946,970,994})
                {
                    var clock=System.Diagnostics.Stopwatch.StartNew();
                    resources.Width=width;resources.UpdateLayout();
                    clock.Stop();
                    var actual=resources.SidebarColumn.ActualWidth;
                    var maximum=resources.SidebarColumn.MaxWidth;
                    if(actual>maximum+.01) throw new InvalidOperationException($"Rounded sidebar exceeds its own maximum: window={width}, actual={actual}, maximum={maximum}");
                    if(clock.Elapsed.TotalMilliseconds>300) throw new InvalidOperationException($"Breakpoint layout failed to converge promptly: width={width}, ms={clock.Elapsed.TotalMilliseconds:F2}");
                }
                var viewer=new ScrollViewer {Style=(Style)resources.FindResource("FadingDetailScrollViewerStyle"),Content=new Border {Height=1600,Background=Brushes.Gray}};
                DeviceListViewport.SetEnabled(viewer,true);
                host=new Window {Content=viewer,Width=600,Height=500,Left=100,Top=100,ShowActivated=false};host.Show();host.UpdateLayout();
                var viewport=(FrameworkElement)viewer.Template.FindName("DetailViewportMask",viewer);
                var original=viewport.OpacityMask;
                if(original is null) throw new InvalidOperationException("Fade mask not initialized.");
                host.Width=650;host.UpdateLayout();
                if(!ReferenceEquals(original,viewport.OpacityMask)) throw new InvalidOperationException("Width-only layout rebuilt an identical fade mask/render surface.");
                viewer.ScrollToVerticalOffset(20);host.UpdateLayout();
                if(ReferenceEquals(original,viewport.OpacityMask)) throw new InvalidOperationException("Scrolling did not update the fade.");
                var scrolled=viewport.OpacityMask;host.Height=450;host.UpdateLayout();
                if(ReferenceEquals(scrolled,viewport.OpacityMask)) throw new InvalidOperationException("Viewport height did not update normalized fade coordinates.");
                Console.WriteLine("Resize cache PASS: identical width-only fade retained; scrolling and viewport-height changes update it.");
            }
            catch(Exception exception){failure=exception;}
            finally{resources?.PrepareForExit();resources?.Close();host?.Close();app?.Shutdown();}
        });
        thread.SetApartmentState(ApartmentState.STA);thread.Start();
        if(!thread.Join(TimeSpan.FromSeconds(15))){Console.Error.WriteLine("Resize cache test timed out");Environment.Exit(1);}
        if(failure is null)return 0;Console.Error.WriteLine(failure);return 1;
    }
}
