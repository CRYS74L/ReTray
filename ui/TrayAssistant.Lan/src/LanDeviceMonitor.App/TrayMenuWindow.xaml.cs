using System.Windows;
using System.Windows.Media.Animation;

namespace LanDeviceMonitor;

public partial class TrayMenuWindow : Window
{
    private readonly Action _showWindow;
    private readonly Action _exitApplication;
    private bool _isExiting;
    private bool _isClosing;

    public TrayMenuWindow(Action showWindow, Action exitApplication)
    {
        InitializeComponent();
        _showWindow = showWindow;
        _exitApplication = exitApplication;
        Opacity = 0;
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        if (!SystemParameters.ClientAreaAnimation)
        {
            Opacity = 1;
            return;
        }

        BeginAnimation(
            OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(130))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });
    }

    private void OpenButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isClosing)
        {
            return;
        }

        _isClosing = true;
        Hide();
        _showWindow();
        Close();
    }

    private void ExitButton_Click(object sender, RoutedEventArgs e)
    {
        _isExiting = true;
        _exitApplication();
    }

    internal void PrepareForProcessExit()
    {
        _isExiting = true;
        Hide();
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        // Replacing the menu also closes it externally. Native deactivation
        // during that close must not start another Close on the same window.
        _isClosing = true;
        base.OnClosing(e);
        if (e.Cancel) _isClosing = false;
    }

    private void Window_Deactivated(object? sender, EventArgs e)
    {
        if (!_isExiting && !_isClosing)
        {
            _isClosing = true;
            Close();
        }
    }
}
