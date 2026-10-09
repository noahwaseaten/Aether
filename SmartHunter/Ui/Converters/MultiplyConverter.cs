using System;
using System.Globalization;
using System.Linq;
using System.Windows.Data;

namespace SmartHunter.Ui.Converters
{
    // Widget scale x overlay size
    public class MultiplyConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            return values.Aggregate(1.0, (product, v) => product * (v is IConvertible ? System.Convert.ToDouble(v, CultureInfo.InvariantCulture) : 1.0));
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
