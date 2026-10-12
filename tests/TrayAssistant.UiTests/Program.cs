using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using LanDeviceMonitor.Controls;

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
            ("disabled automation switch cannot act", DisabledSwitch)
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

    private sealed class State : INotifyPropertyChanged
    {
        private bool value;
        public bool Value { get => value; set { this.value = value; PropertyChanged?.Invoke(this, new(nameof(Value))); } }
        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
