using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace SmartHunter.Ui.Converters
{
    // "em001_00" -> the game's portrait for that monster (Ui/Monsters, from HunterPie). null when there isn't one.
    public class MonsterIconConverter : IValueConverter
    {
        static readonly Dictionary<string, BitmapImage> s_Cache = new Dictionary<string, BitmapImage>();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (!(value is string id) || id.Length == 0)
            {
                return null;
            }
            if (!s_Cache.TryGetValue(id, out var image))
            {
                var uri = new Uri($"pack://application:,,,/Ui/Monsters/{id}.png");
                try
                {
                    if (Application.GetResourceStream(uri) != null)
                    {
                        image = new BitmapImage(uri);
                        image.Freeze();
                    }
                }
                catch (System.IO.IOException)
                {
                    // no portrait for this id
                }
                s_Cache[id] = image;
            }
            return image;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
