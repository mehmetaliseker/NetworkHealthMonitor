using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using NetworkHealthMonitor.Models;

namespace NetworkHealthMonitor.Converters;

public sealed class StatusBackgroundConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is DeviceStatus status
            ? new SolidColorBrush(status switch
            {
                DeviceStatus.Reachable => Color.FromRgb(220, 252, 231),
                DeviceStatus.Unreachable => Color.FromRgb(254, 226, 226),
                DeviceStatus.Checking => Color.FromRgb(219, 234, 254),
                _ => Color.FromRgb(229, 231, 235)
            })
            : Brushes.Transparent;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

public sealed class StatusForegroundConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is DeviceStatus status
            ? new SolidColorBrush(status switch
            {
                DeviceStatus.Reachable => Color.FromRgb(22, 101, 52),
                DeviceStatus.Unreachable => Color.FromRgb(153, 27, 27),
                DeviceStatus.Checking => Color.FromRgb(30, 64, 175),
                _ => Color.FromRgb(55, 65, 81)
            })
            : Brushes.Black;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

public sealed class RowStatusBackgroundConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is DeviceStatus status
            ? new SolidColorBrush(status switch
            {
                DeviceStatus.Reachable => Color.FromRgb(240, 253, 244),
                DeviceStatus.Unreachable => Color.FromRgb(254, 242, 242),
                DeviceStatus.Checking => Color.FromRgb(239, 246, 255),
                _ => Colors.White
            })
            : Brushes.White;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
