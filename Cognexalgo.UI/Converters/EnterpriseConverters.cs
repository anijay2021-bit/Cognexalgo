using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows;

namespace Cognexalgo.UI.Converters
{
    public class PnlToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is decimal pnl)
            {
                return pnl >= 0 ? "Positive" : "Negative";
            }
            if (value is double pnlDouble)
            {
                return pnlDouble >= 0 ? "Positive" : "Negative";
            }
            return "Neutral";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class KillSwitchStatusToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string status = value as string;
            var primaryBrush = Application.Current.Resources["PrimaryBrush"] as Brush;
            var dangerBrush = Application.Current.Resources["DangerBrush"] as Brush;
            var mutedBrush = Application.Current.Resources["TextMutedBrush"] as Brush;

            if (status == "ACTIVATED") return dangerBrush;
            if (status == "OFF") return primaryBrush;
            
            return mutedBrush;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
