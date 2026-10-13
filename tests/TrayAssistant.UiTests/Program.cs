using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using LanDeviceMonitor.Controls;
using System.Text.Json.Nodes;
using TrayAssistant_App.Models;

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
            ("owner identity recovery reenables bound tray toggle", IdentityRecovery)
        })
        {
            try { test(); Console.WriteLine($"PASS {name}"); }
            catch (Exception error) { failed++; Console.Error.WriteLine($"FAIL {name}: {error.Message}"); }
        }
        return failed == 0 ? 0 : 1;
    }

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
