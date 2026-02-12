using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace TreadmillDriver.Converters;

/// <summary>
/// Converts a boolean to Visibility (true = Visible, false = Collapsed).
/// </summary>
public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool flag = value is bool b && b;
        if (parameter?.ToString() == "Invert") flag = !flag;
        return flag ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is Visibility v && v == Visibility.Visible;
}

/// <summary>
/// Converts a boolean to a color brush (true = green, false = gray).
/// </summary>
public class BoolToStatusColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool connected = value is bool b && b;
        return new SolidColorBrush(connected
            ? Color.FromRgb(52, 211, 153)   // Green #34D399
            : Color.FromRgb(90, 90, 110));   // Gray
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// Converts a boolean to a highlight brush for output mode selection cards.
/// </summary>
public class BoolToCardHighlightConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool selected = value is bool b && b;
        return new SolidColorBrush(selected
            ? Color.FromRgb(249, 115, 22)   // Orange accent
            : Color.FromRgb(30, 30, 40));    // Card background
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// Converts velocity to a color (green for forward, red for backward, gray for idle).
/// </summary>
public class VelocityToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        double velocity = value is double v ? v : 0;
        if (velocity > 0.01)
            return new SolidColorBrush(Color.FromRgb(52, 211, 153));  // Green #34D399
        if (velocity < -0.01)
            return new SolidColorBrush(Color.FromRgb(248, 113, 113)); // Red #F87171
        return new SolidColorBrush(Color.FromRgb(90, 90, 110));       // Gray
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// Converts a non-empty string to Visible, empty/null to Collapsed.
/// </summary>
public class StringToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return !string.IsNullOrEmpty(value as string) ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// Negates a double value (used for TranslateTransform to move bars upward).
/// </summary>
public class NegateConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is double d)
            return -d;
        return 0.0;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
