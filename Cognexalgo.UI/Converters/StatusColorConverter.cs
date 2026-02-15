using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace Cognexalgo.UI.Converters
{
    public class StatusBackgroundConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isActive)
            {
                // Simple mapping for IsActive boolean
                return isActive ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E8F5E9")) : // Enterprise Green
                                  new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F5F5F5"));   // Enterprise Gray
            }

            string status = value as string;
            if (string.IsNullOrEmpty(status)) return Brushes.Transparent;

            switch (status.ToUpper())
            {
                case "RUNNING":
                case "ACTIVE":
                    return new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E8F5E9")); // Emerald Green Bg
                case "EXITED":
                case "COMPLETED":
                case "SQUARED OFF":
                    return new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F5F5F5")); // Steel Gray Bg
                case "ERROR":
                case "FAILED":
                case "REJECTED":
                    return new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFEBEE")); // Crimson Red Bg
                default:
                    return new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F5F5F5"));
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    public class StatusForegroundConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
             if (value is bool isActive)
            {
                return isActive ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2E7D32")) : // Emerald Green
                                  new SolidColorBrush((Color)ColorConverter.ConvertFromString("#607D8B"));   // Steel Gray
            }

            string status = value as string;
            if (string.IsNullOrEmpty(status)) return Brushes.Black;

            switch (status.ToUpper())
            {
                case "RUNNING":
                case "ACTIVE":
                    return new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2E7D32")); // Emerald Green Text
                case "EXITED":
                case "COMPLETED":
                case "SQUARED OFF":
                    return new SolidColorBrush((Color)ColorConverter.ConvertFromString("#607D8B")); // Steel Gray Text
                case "ERROR":
                case "FAILED":
                case "REJECTED":
                    return new SolidColorBrush((Color)ColorConverter.ConvertFromString("#C62828")); // Crimson Red Text
                default:
                    return new SolidColorBrush((Color)ColorConverter.ConvertFromString("#333333"));
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }
}
