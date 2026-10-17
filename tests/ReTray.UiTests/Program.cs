using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using LanDeviceMonitor.Controls;
using System.Text.Json.Nodes;
using ReTray_App.Models;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        int failed = 0;
        foreach (var (name, test) in new (string, Action)[]
        {
            ("automation commits one-way tray switch click", OneWaySwitch),
            ("automation preserves two-way setting binding", TwoWaySwitch),
            ("disabled automation switch cannot act", DisabledSwitch),
            ("owner identity recovery reenables bound tray toggle", IdentityRecovery),
            ("system theme follows Windows mode independently of app mode", WindowsTheme),
            ("window taskbar branding overrides package artwork with valid relaunch metadata", TaskbarIdentity),
            ("single executable relaunch, package directory identity and menu labels", PackageRelaunch),
            ("taskbar branding rejects windows owned by another process", RejectForeignWindow),
            ("own row uses product identity rather than the agent executable name", OwnProductName)
        })
        {
            try { test(); Console.WriteLine($"PASS {name}"); }
            catch (Exception error) { failed++; Console.Error.WriteLine($"FAIL {name}: {error.Message}"); }
        }
        return failed == 0 ? 0 : 1;
    }

    private static void WindowsTheme()
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
            @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", false);
        bool expected = key?.GetValue("SystemUsesLightTheme") is int value && value == 0;
        Require(LanDeviceMonitor.Services.ThemeService.ResolveIsDark("System") == expected,
            "System theme must follow SystemUsesLightTheme, even when AppsUseLightTheme differs");
        Require(LanDeviceMonitor.Services.ThemeService.ResolveIsDark("Dark"), "Explicit dark was lost");
        Require(!LanDeviceMonitor.Services.ThemeService.ResolveIsDark("Light"), "Explicit light was lost");
    }
    private static void OwnProductName()
    {
        var json = JsonNode.Parse("""
        {"key":"own","owned":true,"selector":{"executable":"C:\\ReTray\\ReTray.Agent.exe","hasGuid":false,"uid":1},
         "nativeUid":1,"pid":123,"placement":1,"displayPixels":"","displayWidth":0,"displayHeight":0,
         "originalPixels":"","originalWidth":0,"originalHeight":0}
        """)!.AsObject();
        var row = new IconRow(json);
        Require(row.Name == "ReTray", "Own row exposed the agent process name");
        json["owned"] = false;
        row.Update(json);
        Require(row.Name == "ReTray.Agent", "An unrelated process was renamed by filename alone");
    }
    private static void TaskbarIdentity()
    {
        var window = new Window { ShowInTaskbar = false };
        var handle = new System.Windows.Interop.WindowInteropHelper(window).EnsureHandle();
        try
        {
            LanDeviceMonitor.Services.TaskbarBranding.Apply(handle, @"C:\ReTray\WindowIcon.ico", "retray-test-relaunch");
            Require(LanDeviceMonitor.Services.TaskbarBranding.Read(handle, 5) == "CRYS74L.ReTray.Window", "Window identity missing");
            Require(LanDeviceMonitor.Services.TaskbarBranding.Read(handle, 3) == @"C:\ReTray\WindowIcon.ico,0", "Colored relaunch icon missing");
            Require(LanDeviceMonitor.Services.TaskbarBranding.Read(handle, 2) == "retray-test-relaunch", "Relaunch command lost");
            Require(LanDeviceMonitor.Services.TaskbarBranding.Read(handle, 4) == "ReTray", "Display name missing");
        }
        finally { window.Close(); }
    }

    private static void PackageRelaunch()
    {
        var command = LanDeviceMonitor.Services.TaskbarBranding.CreateRelaunchCommand(@"C:\Apps\ReTray\ReTray.exe");
        Require(command == "\"C:\\Apps\\ReTray\\ReTray.exe\" --show", "Relaunch did not use the actual root executable");
        Require(LanDeviceMonitor.Services.PackageActivation.SameDirectory(@"C:\Apps\ReTray\", @"c:\apps\retray"), "Directory identity lost case/trailing separator tolerance");
        Require(!LanDeviceMonitor.Services.PackageActivation.SameDirectory(@"C:\Apps\ReTray", @"C:\Apps\ReTray-copy"), "A separate ZIP copy was mistaken for the installed package");
        Require(!LanDeviceMonitor.Services.PackageActivation.MatchesInstalledDirectory(System.IO.Path.GetTempPath()), "An unrelated folder was redirected to the installed package");
    }
    private static void RejectForeignWindow()
    {
        // The desktop HWND belongs to Explorer; rejection must happen before
        // opening a property store or modifying the shell's taskbar identity.
        try {
            LanDeviceMonitor.Services.TaskbarBranding.Apply(GetDesktopWindow(), "unused", "unused");
            throw new InvalidOperationException("Foreign window accepted taskbar branding");
        } catch (ArgumentException) { }
    }
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern nint GetDesktopWindow();

    private static IToggleProvider Provider(SwitchControl control) =>
        (IToggleProvider)UIElementAutomationPeer.CreatePeerForElement(control)!.GetPattern(PatternInterface.Toggle);

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    private static void OneWaySwitch()
    {
        var source = new State(); var control = new SwitchControl(); int clicks = 0;
        control.SetBinding(ToggleButton.IsCheckedProperty, new Binding(nameof(State.Value)) { Source = source, Mode = BindingMode.OneWay });
        control.Click += (_, _) => { clicks++; source.Value = control.IsChecked == true; };
        var provider = Provider(control); provider.Toggle();
        Require(source.Value && clicks == 1 && provider.ToggleState == ToggleState.On, "Toggle did not commit the tray click handler");
        control.GetBindingExpression(ToggleButton.IsCheckedProperty)!.UpdateTarget();
        Require(control.IsChecked == true, "Native acknowledgment refresh reverted the switch");
        provider.Toggle(); Require(!source.Value && clicks == 2 && provider.ToggleState == ToggleState.Off, "Second toggle did not restore native visibility");
    }

    private static void TwoWaySwitch()
    {
        var source = new State(); var control = new SwitchControl(); int clicks = 0;
        control.SetBinding(ToggleButton.IsCheckedProperty, new Binding(nameof(State.Value)) { Source = source, Mode = BindingMode.TwoWay });
        control.Click += (_, _) => clicks++;
        Provider(control).Toggle();
        Require(source.Value && clicks == 1, "Toggle lost the bound setting or click event");
    }

    private static void DisabledSwitch()
    {
        var control = new SwitchControl { IsEnabled = false }; bool clicked = false;
        control.Click += (_, _) => clicked = true;
        try { Provider(control).Toggle(); throw new InvalidOperationException("Disabled switch accepted automation"); }
        catch (ElementNotEnabledException) { }
        Require(!clicked && control.IsChecked == false, "Disabled automation changed state");
    }

    private static void IdentityRecovery()
    {
        var json = JsonNode.Parse("""
        {"key":"identity-fixture","canPersist":false,"selector":{"executable":"","hasGuid":false,"uid":11},
         "pid":1,"tipCurrent":"Protected app","placement":1,"pixels":"","width":0,"height":0,
         "originalPixels":"","originalWidth":0,"originalHeight":0}
        """)!.AsObject();
        var row = new IconRow(json);
        var control = new SwitchControl();
        control.SetBinding(ToggleButton.IsEnabledProperty, new Binding(nameof(IconRow.CanToggle)) { Source = row });
        Require(!control.IsEnabled && row.Name == "Protected app", "Unresolved owner remains actionable or unnamed");
        json["canPersist"] = true; json["selector"]!["executable"] = @"C:\Fixture\Known.exe";
        row.Update(json);
        Require(control.IsEnabled, "Owner recovery did not notify the real bound control");
        row.IsBusy = true; Require(!control.IsEnabled, "Busy switch remains actionable");
        row.IsBusy = false; Require(control.IsEnabled, "Completed operation did not restore the control");
    }

    private sealed class State : INotifyPropertyChanged
    {
        private bool value;
        public bool Value { get => value; set { this.value = value; PropertyChanged?.Invoke(this, new(nameof(Value))); } }
        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
