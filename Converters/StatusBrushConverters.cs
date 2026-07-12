using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using NetworkHealthMonitor.Models;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfColor = System.Windows.Media.Color;
using WpfColors = System.Windows.Media.Colors;

namespace NetworkHealthMonitor.Converters;

public sealed class StatusBackgroundConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is DeviceStatus status
            ? new SolidColorBrush(status switch
            {
                DeviceStatus.Online => WpfColor.FromRgb(220, 252, 231),
                DeviceStatus.Warning => WpfColor.FromRgb(254, 243, 199),
                DeviceStatus.UnderWatch => WpfColor.FromRgb(255, 237, 213),
                DeviceStatus.Offline => WpfColor.FromRgb(254, 226, 226),
                DeviceStatus.PingBlockedOrNoReply => WpfColor.FromRgb(224, 231, 255),
                DeviceStatus.Checking => WpfColor.FromRgb(219, 234, 254),
                _ => WpfColor.FromRgb(229, 231, 235)
            })
            : WpfBrushes.Transparent;
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
                DeviceStatus.Online => WpfColor.FromRgb(22, 101, 52),
                DeviceStatus.Warning => WpfColor.FromRgb(146, 64, 14),
                DeviceStatus.UnderWatch => WpfColor.FromRgb(154, 52, 18),
                DeviceStatus.Offline => WpfColor.FromRgb(153, 27, 27),
                DeviceStatus.PingBlockedOrNoReply => WpfColor.FromRgb(55, 48, 163),
                DeviceStatus.Checking => WpfColor.FromRgb(30, 64, 175),
                _ => WpfColor.FromRgb(55, 65, 81)
            })
            : WpfBrushes.Black;
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
                DeviceStatus.Online => WpfColor.FromRgb(240, 253, 244),
                DeviceStatus.Warning => WpfColor.FromRgb(255, 251, 235),
                DeviceStatus.UnderWatch => WpfColor.FromRgb(255, 247, 237),
                DeviceStatus.Offline => WpfColor.FromRgb(254, 242, 242),
                DeviceStatus.PingBlockedOrNoReply => WpfColor.FromRgb(238, 242, 255),
                DeviceStatus.Checking => WpfColor.FromRgb(239, 246, 255),
                _ => WpfColors.White
            })
            : WpfBrushes.White;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
