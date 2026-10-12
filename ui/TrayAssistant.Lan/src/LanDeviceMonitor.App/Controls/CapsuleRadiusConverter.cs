using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace LanDeviceMonitor.Controls;

public sealed class CapsuleRadiusConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        new CornerRadius(value is double height && double.IsFinite(height) ? Math.Max(0, height / 2) : 0);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        System.Windows.Data.Binding.DoNothing;
}
