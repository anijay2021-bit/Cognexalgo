using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace Cognexalgo.UI.Converters
{
    public class LogColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string log)
            {
                if (log.Contains("[ERROR]")) return new SolidColorBrush(Color.FromRgb(220, 38, 38)); // Red-600
                if (log.Contains("[WARNING]")) return new SolidColorBrush(Color.FromRgb(217, 119, 6)); // Amber-600
                if (log.Contains("[SUCCESS]")) return new SolidColorBrush(Color.FromRgb(22, 163, 74)); // Green-600
                if (log.Contains("[INFO]")) return new SolidColorBrush(Color.FromRgb(75, 85, 99)); // Gray-600
            }
            return Brushes.Black;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
