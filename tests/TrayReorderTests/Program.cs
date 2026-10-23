using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ReTray_App;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        if (args.Contains("--fixture"))
        {
            app.ShutdownMode = ShutdownMode.OnMainWindowClose;
            var fixture = new Fixture(24);
            fixture.Window.Title = "ReTray drag sorting fixture";
            app.Run(fixture.Window);
            return 0;
        }
        try
        {
            Run(); Console.WriteLine("PASS all tray reorder checks"); return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally { app.Shutdown(); }
    }

    private sealed class Fixture : IDisposable
    {
        internal readonly ObservableCollection<string> Items;
        internal readonly ListBox List;
        internal readonly Window Window;
        internal readonly TrayRowReorder Reorder;
        internal int Saves, Starts;
        internal bool? LastChanged;
        internal Fixture(int count = 8)
        {
            Items = new(Enumerable.Range(1, count).Select(i => $"Tray application {i:00}"));
            List = new ListBox { ItemsSource = Items, BorderThickness = new Thickness(0), Background = Brushes.White };
            ScrollViewer.SetCanContentScroll(List, false);
            var style = new Style(typeof(ListBoxItem));
            style.Setters.Add(new Setter(FrameworkElement.HeightProperty, 34d));
            style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
            List.ItemContainerStyle = style;
            var text = new FrameworkElementFactory(typeof(TextBlock));
            text.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding());
            text.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            text.SetValue(FrameworkElement.MarginProperty, new Thickness(16, 0, 0, 0));
            List.ItemTemplate = new DataTemplate { VisualTree = text };
            var status = new TextBlock { Margin = new Thickness(14), Text = "Drag a row vertically to reorder" };
            var panel = new DockPanel(); DockPanel.SetDock(status, Dock.Bottom); panel.Children.Add(status); panel.Children.Add(List);
            Window = new Window { Title = "ReTray reorder tests", Width = 460, Height = 370, Content = panel };
            Reorder = new TrayRowReorder(List, () => Starts++, Items.Move, changed =>
            {
                LastChanged = changed; if (changed) Saves++;
                status.Text = $"Saved: {Saves} | First: {Items[0]}";
                return Task.CompletedTask;
            });
            Window.Show(); Window.Activate(); Pump(40); Window.UpdateLayout();
        }
        internal ListBoxItem Row(int i) => (ListBoxItem)List.ItemContainerGenerator.ContainerFromIndex(i);
        internal Point Center(int i) => Row(i).TranslatePoint(new Point(120, Row(i).ActualHeight / 2), List);
        public void Dispose() { Reorder.Cancel(); Window.Close(); Pump(30); }
    }

    private static void Run()
    {
        using (var f = new Fixture())
        {
            var original = f.Items.ToArray(); var start = f.Center(1);
            Require(f.Reorder.Begin(1, start), "Could not capture a real WPF list");
            f.Reorder.Update(new Point(start.X + 170, start.Y + 34 * 3));
            Require(f.Items.SequenceEqual(original), "Preview mutated persisted order before release");
            Require(Math.Abs(((TranslateTransform)f.Row(1).RenderTransform).X) < .01, "Horizontal motion was not locked");
            Require(Math.Abs(((TranslateTransform)f.Row(1).RenderTransform).Y - 102) < .01, "Lifted row lost pointer offset");
            Require(Math.Abs(Target(f.Row(3)) + 34) < .01, "Neighbours did not make room");
            // Reverse while the pointer is still down; the initial slot coordinates must remain stable.
            f.Reorder.Update(new Point(start.X, start.Y - 34));
            Require(Math.Abs(Target(f.Row(0)) - 34) < .01, "Reverse drag failed to shift preceding row");
            Require(Math.Abs(Target(f.Row(3))) < .01, "Reverse drag retained a stale gap");
            var dropping = f.Reorder.CompleteAsync(true);
            if (SystemParameters.ClientAreaAnimation)
            {
                Pump(60);
                var position = ((TranslateTransform)f.Row(0).RenderTransform).Y;
                Require(position > 0 && position < 34, "Neighbour skipped its intermediate animation frames");
            }
            Wait(dropping);
            Require(f.Items[0] == original[1] && f.Saves == 1, "Drop did not commit exactly once");
            Require(!f.List.IsMouseCaptured && f.Row(0).RenderTransform.Value.IsIdentity, "Drop leaked mouse capture or transforms");
            Console.WriteLine("PASS vertical tracking, animated neighbours, reversing, single commit and cleanup");

            original = f.Items.ToArray(); start = f.Center(0);
            Require(f.Reorder.Begin(0, start), "Could not begin second drag");
            f.Reorder.Update(new Point(start.X, start.Y + 102));
            f.List.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(f.List), 0, Key.Escape)
                { RoutedEvent = Keyboard.PreviewKeyDownEvent }); Pump(30);
            Require(f.Items.SequenceEqual(original) && f.Saves == 1 && f.LastChanged == false, "Escape changed saved order");
            Require(!f.List.IsMouseCaptured, "Escape kept mouse capture");

            Require(f.Reorder.Begin(0, f.Center(0)), "Could not begin outside-drop check");
            f.Reorder.Update(new Point(120, 170)); Wait(f.Reorder.CompleteAsync(false));
            Require(f.Items.SequenceEqual(original) && f.Saves == 1, "Outside drop persisted a preview");
            Require(f.Reorder.Begin(0, f.Center(0)), "Could not begin no-op check");
            Wait(f.Reorder.CompleteAsync(true)); Require(f.Saves == 1, "Unchanged drop saved unnecessarily");
            Console.WriteLine("PASS Escape, outside drop and unchanged drop preserve order");

            Require(f.Reorder.Begin(0, f.Center(0)), "Could not begin capture-loss check");
            f.Reorder.Update(new Point(120, 170)); Mouse.Capture(null); Pump(30);
            Require(f.Items.SequenceEqual(original) && f.Saves == 1, "Capture loss committed a drag");

            Require(f.Reorder.Begin(0, f.Center(0)), "Could not begin pending-drop check");
            f.Reorder.Update(new Point(120, 170)); var pending = f.Reorder.CompleteAsync(true); f.Reorder.Cancel(); Wait(pending);
            Require(f.Items.SequenceEqual(original) && f.Saves == 1, "Closing during settle committed stale order");
            Console.WriteLine("PASS capture loss and cancellation during settle");
        }
        using (var f = new Fixture(40))
        {
            Require(f.Reorder.Begin(0, f.Center(0)), "Could not begin scroll test");
            for (int i = 0; i < 70; i++) f.Reorder.ScrollAtEdge(new Point(120, f.List.ActualHeight - 2));
            var scroller = Find<ScrollViewer>(f.List)!;
            Require(scroller.VerticalOffset > 500, "Holding at bottom did not auto-scroll");
            f.Reorder.Update(new Point(120, f.List.ActualHeight - 2));
            Wait(f.Reorder.CompleteAsync(true));
            Require(f.Items.IndexOf("Tray application 01") > 15 && f.Saves == 1, "Drag could not reach initially offscreen items");
            Require(!f.List.IsMouseCaptured, "Scroll drop leaked capture");
            Console.WriteLine("PASS edge auto-scroll and drop on an initially offscreen row");
        }
    }

    private static T? Find<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T found) return found;
            if (Find<T>(child) is { } nested) return nested;
        }
        return null;
    }
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static double Target(ListBoxItem row) => (double)((TranslateTransform)row.RenderTransform).GetAnimationBaseValue(TranslateTransform.YProperty);
    private static void Wait(Task task) { while (!task.IsCompleted) Pump(10); task.GetAwaiter().GetResult(); }
    private static void Pump(int milliseconds)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start(); Dispatcher.PushFrame(frame);
    }
}
