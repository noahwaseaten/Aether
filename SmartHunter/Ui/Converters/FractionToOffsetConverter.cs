using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace SmartHunter.Ui.Converters
{
    // Places a marker along a bar: fraction 0.3 with parameter 336 (the bar's width) -> left margin 100.8
    public class FractionToOffsetConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            double fraction = System.Convert.ToDouble(value, CultureInfo.InvariantCulture);
            double width = System.Convert.ToDouble(parameter, CultureInfo.InvariantCulture);
            return new Thickness(Math.Max(0, Math.Min(1, fraction)) * width, 0, 0, 0);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
