using System;
using System.Globalization;
using System.Windows.Data;

namespace SmartHunter.Ui.Converters
{
    // 7 -> "7s", 95 -> "1:35", like the game's own timers
    public class SecondsToClockConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            double seconds = System.Convert.ToDouble(value, CultureInfo.InvariantCulture);
            if (seconds < 60)
            {
                return $"{Math.Ceiling(seconds):0}s";
            }
            var t = TimeSpan.FromSeconds(Math.Ceiling(seconds));
            return $"{(int)t.TotalMinutes}:{t.Seconds:00}";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
