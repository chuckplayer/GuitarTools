using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace GuitarTools;

public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        bool.TryParse(value.ToString(), out var isVisible);
        return isVisible ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        // If needed, convert back from Visibility to bool.
        if (value is Visibility visibility)
        {
            return visibility == Visibility.Visible;
        }
        return false;
    }
}

