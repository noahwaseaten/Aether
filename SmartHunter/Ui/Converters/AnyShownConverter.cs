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
    // Headings only show while searching, since the selected category is already named by its tab.
    public class AnyShownConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            // Third value (optional): whether a search is running. Without one, a category's own tab names it.
            if (values.Length > 2 && values[2] is bool searching && !searching)
            {
                return Visibility.Collapsed;
            }
            var items = values.Length > 0 ? values[0] as IEnumerable : null;
            return items == null || items.OfType<Core.Setting>().Any(s => s.IsShown) ? Visibility.Visible : Visibility.Collapsed;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => throw new NotSupportedException();
    }
}
