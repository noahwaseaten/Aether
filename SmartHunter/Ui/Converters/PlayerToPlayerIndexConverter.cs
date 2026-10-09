using System;
using System.Globalization;
using System.Windows.Data;
using SmartHunter.Game.Data;

namespace SmartHunter.Ui.Converters
{
    public class PlayerToPlayerIndexConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // Colour by game slot, so a hunter keeps their colour when someone else leaves
            return value is Player player ? (object)player.Index : Binding.DoNothing;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
