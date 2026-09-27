using System;
using System.Collections;
using System.Globalization;
using Avalonia.Data.Converters;

namespace PlumbobForge.Desktop.Converters;

public class CountToZeroBoolConverter : IValueConverter
{
    public static readonly CountToZeroBoolConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is int count)
        {
            return count == 0;
        }
        if (value is ICollection collection)
        {
            return collection.Count == 0;
        }
        return true;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
