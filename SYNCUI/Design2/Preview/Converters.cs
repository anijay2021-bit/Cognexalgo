using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace Cognexalgo.UI.Converters
{
    public class IsNegativeConverter : IValueConverter
    {
        public object Convert(object value, Type t, object p, CultureInfo c)
            => value is decimal d ? d < 0 : value is double db ? db < 0 : false;
        public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotImplementedException();
    }

    public class PnlToBrushConverter : IValueConverter
    {
        private static readonly SolidColorBrush Green = new(Color.FromRgb(0x16, 0xA3, 0x4A));
        private static readonly SolidColorBrush Red   = new(Color.FromRgb(0xDC, 0x26, 0x26));
        public object Convert(object value, Type t, object p, CultureInfo c)
            => value is decimal d && d < 0 ? Red : Green;
        public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotImplementedException();
    }

    public class StatusBackgroundConverter : IValueConverter
    {
        public object Convert(object value, Type t, object p, CultureInfo c)
        {
            return (value?.ToString() ?? "") switch
            {
                "RUNNING" => new SolidColorBrush(Color.FromRgb(0xDC, 0xFC, 0xE7)),
                "EXITED"  => new SolidColorBrush(Color.FromRgb(0xFE, 0xE2, 0xE2)),
                "PENDING" => new SolidColorBrush(Color.FromRgb(0xFE, 0xF3, 0xC7)),
                _         => new SolidColorBrush(Color.FromRgb(0xF1, 0xF5, 0xF9)),
            };
        }
        public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotImplementedException();
    }

    public class StatusForegroundConverter : IValueConverter
    {
        public object Convert(object value, Type t, object p, CultureInfo c)
        {
            return (value?.ToString() ?? "") switch
            {
                "RUNNING" => new SolidColorBrush(Color.FromRgb(0x16, 0x65, 0x34)),
                "EXITED"  => new SolidColorBrush(Color.FromRgb(0x99, 0x1B, 0x1B)),
                "PENDING" => new SolidColorBrush(Color.FromRgb(0x92, 0x40, 0x0E)),
                _         => new SolidColorBrush(Color.FromRgb(0x47, 0x55, 0x69)),
            };
        }
        public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotImplementedException();
    }

    public class LogColorConverter : IValueConverter
    {
        private static readonly SolidColorBrush TradeColor = new(Color.FromRgb(0x06, 0x78, 0x5C));
        private static readonly SolidColorBrush ErrorColor = new(Color.FromRgb(0xDC, 0x26, 0x26));
        private static readonly SolidColorBrush WarnColor  = new(Color.FromRgb(0xD9, 0x77, 0x06));
        private static readonly SolidColorBrush RmsColor   = new(Color.FromRgb(0x70, 0x59, 0xD3));
        private static readonly SolidColorBrush InfoColor  = new(Color.FromRgb(0x47, 0x55, 0x69));

        public object Convert(object value, Type t, object p, CultureInfo c)
        {
            var s = value?.ToString() ?? "";
            if (s.Contains("TRADE"))  return TradeColor;
            if (s.Contains("ERROR"))  return ErrorColor;
            if (s.Contains("WARN"))   return WarnColor;
            if (s.Contains("RMS"))    return RmsColor;
            return InfoColor;
        }
        public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotImplementedException();
    }
}
