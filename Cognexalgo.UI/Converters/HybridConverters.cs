using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace Cognexalgo.UI.Converters
{
    /// <summary>
    /// Converts radio button selection to boolean for two-way binding
    /// </summary>
    public class RadioButtonConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value?.ToString() == parameter?.ToString();
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return (bool)value ? parameter : Binding.DoNothing;
        }
    }

    /// <summary>
    /// Converts OptionType selection to button background color
    /// </summary>
    public class OptionTypeToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string selectedType = value?.ToString();
            string buttonType = parameter?.ToString();

            if (selectedType == buttonType)
            {
                // Active state - bright cyan
                return new SolidColorBrush((Color)ColorConverter.ConvertFromString("#00BCD4"));
            }
            else
            {
                // Inactive state - dark gray
                return new SolidColorBrush((Color)ColorConverter.ConvertFromString("#333333"));
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Converts Action selection to button background color
    /// Buy = Green, Sell = Red
    /// </summary>
    public class ActionToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string selectedAction = value?.ToString();
            string buttonAction = parameter?.ToString();

            if (selectedAction == buttonAction)
            {
                // Active state - use action-specific colors
                if (buttonAction == "Buy")
                {
                    return new SolidColorBrush((Color)ColorConverter.ConvertFromString("#4CAF50")); // Green
                }
                else if (buttonAction == "Sell")
                {
                    return new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F44336")); // Red
                }
            }
            
            // Inactive state - dark gray
            return new SolidColorBrush((Color)ColorConverter.ConvertFromString("#333333"));
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
