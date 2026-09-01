using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace OptiGames;

/// <summary>Bool to Visible/Collapsed. Set Invert to flip it.</summary>
public sealed class BoolToVisibility : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool b = value is bool v && v;
        if (Invert) b = !b;
        return b ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type t, object p, CultureInfo c)
        => throw new NotSupportedException();
}

/// <summary>Shows an element only when a string has content.</summary>
public sealed class StringToVisibility : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => string.IsNullOrWhiteSpace(value as string) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type t, object p, CultureInfo c)
        => throw new NotSupportedException();
}

/// <summary>Shows an element only when the bound object is non-null.</summary>
public sealed class NullToVisibility : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is null ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type t, object p, CultureInfo c)
        => throw new NotSupportedException();
}

/// <summary>
/// Resolves an icon resource key ("I.Home") to its Geometry. Lets view models name their
/// icon without taking a dependency on WPF types.
/// </summary>
public sealed class IconKeyToGeometry : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is string key ? Application.Current?.TryFindResource(key) as Geometry : null;

    public object ConvertBack(object value, Type t, object p, CultureInfo c)
        => throw new NotSupportedException();
}

/// <summary>
/// Turns a 0..1 fraction into a pixel width against the element's own ActualWidth, for
/// the drive-usage meters. Expects a MultiBinding of [fraction, actualWidth].
/// </summary>
public sealed class FractionToWidth : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2 || values[0] is not double fraction || values[1] is not double total)
            return 0d;
        if (double.IsNaN(total) || total <= 0) return 0d;
        return Math.Max(0, Math.Min(1, fraction)) * total;
    }

    public object[] ConvertBack(object value, Type[] t, object p, CultureInfo c)
        => throw new NotSupportedException();
}

/// <summary>Picks one of two brushes on a bool — used for status text colour.</summary>
public sealed class BoolToBrush : IValueConverter
{
    public Brush? True { get; set; }
    public Brush? False { get; set; }

    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b && b ? True : False;

    public object ConvertBack(object value, Type t, object p, CultureInfo c)
        => throw new NotSupportedException();
}
