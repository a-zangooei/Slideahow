using System.Globalization;
using System.Windows;
using System.Windows.Data;
using JellyfinDesktopSlideshow.Localization;

namespace JellyfinDesktopSlideshow.Views;

public sealed class DynamicTextFlowDirectionConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var fallback = AppLocalization.Current.CurrentFlowDirection;
        return BidiTextFormatter.DetectFlowDirection(value as string, fallback);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
