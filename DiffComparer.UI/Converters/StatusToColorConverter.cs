using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using DiffComparer.Core;

namespace DiffComparer.UI.Converters;

public class StatusToColorConverter : IValueConverter
{
    public object Convert(object? value, Type targetType,
        object? parameter, CultureInfo culture)
    {
        return value is LineStatus status ? status switch
        {
            LineStatus.Added => new SolidColorBrush(Color.Parse("#163D16")),   
            LineStatus.Deleted => new SolidColorBrush(Color.Parse("#4A1F1F")),   
            LineStatus.Modified => new SolidColorBrush(Color.Parse("#4A3F12")),  
            _ => Brushes.Transparent
        } : Brushes.Transparent;
    }

    public object ConvertBack(object? value, Type targetType,
        object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}