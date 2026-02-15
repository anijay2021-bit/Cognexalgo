using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Cognexalgo.UI.Converters
{
    public class GridLengthConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double d)
            {
                return new GridLength(d);
            }
            return new GridLength(200); // Default
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is GridLength gl)
            {
                return gl.Value;
            }
            return 200.0;
        }
    }
}
