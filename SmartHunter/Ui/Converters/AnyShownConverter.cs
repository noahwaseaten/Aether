using System;
using System.Collections;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Data;

namespace SmartHunter.Ui.Converters
{
    // A settings section heading shows while any of its rows matches the search. The second binding is only there to
    // re-run this when the search changes.
    public class AnyShownConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            var items = values.Length > 0 ? values[0] as IEnumerable : null;
            return items == null || items.OfType<Core.Setting>().Any(s => s.IsShown) ? Visibility.Visible : Visibility.Collapsed;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => throw new NotSupportedException();
    }
}
