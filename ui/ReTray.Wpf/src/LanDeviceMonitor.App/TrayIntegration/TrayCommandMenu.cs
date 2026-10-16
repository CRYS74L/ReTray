using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Threading;
using Localization = ReTray_App.Services.Localization;
namespace ReTray_App.Controls;

// Uses the same surface, item motion and body typography as ComboBox dropdowns.
public sealed class TrayCommandMenu
{
    private ContextMenu? menu;
    public void Show()
    {
        if (App.IsExiting) return;
        if (menu is { IsOpen: true }) { menu.IsOpen = false; return; }
        menu = new ContextMenu {
            Style = (Style)Application.Current.FindResource("TrayDropdownMenuStyle"),
            Placement = PlacementMode.MousePoint, HorizontalOffset = -12, StaysOpen = false
        };
        // A popup can open while the main window is natively hidden in the tray.
        // It owns keyboard focus and outside-click dismissal independently.
        var labels = Labels(Localization.CurrentLanguage);
        Add(labels[0], "TrayMenuOpen", () => { App.OpenInterface(); return Task.CompletedTask; });
        Add(labels[1], "TrayMenuRestart", App.RestartAsync);
        Add(labels[2], "TrayMenuExit", App.QuitAsync);
        System.Windows.Automation.AutomationProperties.SetAutomationId(menu, "ReTrayTrayMenu");
        menu.Opened += (_, _) => menu.Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => {
            if (menu?.IsOpen != true) return;
            if (PresentationSource.FromVisual(menu) is HwndSource source) SetForegroundWindow(source.Handle);
            (menu.Items[0] as MenuItem)?.Focus();
        }));
        menu.IsOpen = true;
    }
    private void Add(string label, string id, Func<Task> action)
    {
        var item = new MenuItem { Header = label };
        System.Windows.Automation.AutomationProperties.SetAutomationId(item, id);
        item.Click += async (_, args) => {
            args.Handled = true;
            if (menu is not null) menu.IsOpen = false;
            try { await action(); }
            catch (Exception error) { if (!App.IsExiting) App.Window.TrayPage.Vm.ReportError(error); }
        };
        menu!.Items.Add(item);
    }
    public static string[] Labels(string language) => language switch {
        "en" => ["Open", "Restart", "Exit"],
        "zh-Hant" => ["開啟", "重新啟動", "退出"],
        "ja" => ["開く", "再起動", "終了"],
        _ => ["打开", "重新启动", "退出"]
    };
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint window);
}
