using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace ToperJarvis.App;

/// <summary>Mnoży wartość liczbową przez współczynnik z ConverterParameter (np. 0.25 = 25%).</summary>
public sealed class RatioConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is double d &&
            double.TryParse(parameter as string, NumberStyles.Float, CultureInfo.InvariantCulture, out var ratio))
            return d * ratio;

        return value;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
