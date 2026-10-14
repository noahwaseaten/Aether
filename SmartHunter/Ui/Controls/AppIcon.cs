using System;
using System.Linq;
using System.Windows.Media.Imaging;

namespace SmartHunter.Ui.Controls
{
    // An Image given the .ico decodes its smallest frame, which turns to mush at a bigger widget size. This is the
    // largest frame instead, and WPF scales it down cleanly.
    public static class AppIcon
    {
        public static readonly BitmapSource Large = Load();

        static BitmapSource Load()
        {
            var decoder = BitmapDecoder.Create(new Uri("pack://application:,,,/Ui/Aether.ico"), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            var frame = decoder.Frames.OrderByDescending(f => f.PixelWidth).First();
            frame.Freeze();
            return frame;
        }
    }
}
