using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace SmartHunter.Ui.Converters
{
    // "ICON_LONGSWORD" -> the DrawingImage from Ui/Resources/Icons.xaml
    public class IconKeyToImageConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is string key ? Application.Current.TryFindResource(key) : null;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
