using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SmartHunter.Ui.Converters
{
    // "ICON_LONGSWORD" -> the DrawingImage from Ui/Resources/Icons.xaml. ConverterParameter=Trim crops it to what's drawn:
    // the game's icons sit off centre in their square, which showed inside a circle.
    public class IconKeyToImageConverter : IValueConverter
    {
        static readonly Dictionary<string, ImageSource> s_Trimmed = new Dictionary<string, ImageSource>();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var image = value is string key ? Application.Current.TryFindResource(key) as DrawingImage : null;
            if (image == null || parameter as string != "Trim")
            {
                return image;
            }

            string k = (string)value;
            if (!s_Trimmed.TryGetValue(k, out var trimmed))
            {
                trimmed = Trim(image);
                s_Trimmed[k] = trimmed;
            }
            return trimmed;
        }

        // The icon's paths include invisible points at its square's corners, so their bounds are the square: find the
        // drawn pixels instead, and clip the drawing to them
        static ImageSource Trim(DrawingImage image)
        {
            var bounds = image.Drawing.Bounds;
            const int Size = 128;
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                dc.DrawImage(image, new Rect(0, 0, Size, Size));
            }
            var bitmap = new RenderTargetBitmap(Size, Size, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            var pixels = new byte[Size * Size * 4];
            bitmap.CopyPixels(pixels, Size * 4, 0);

            int left = Size, top = Size, right = -1, bottom = -1;
            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    if (pixels[(y * Size + x) * 4 + 3] > 24)
                    {
                        left = Math.Min(left, x); right = Math.Max(right, x);
                        top = Math.Min(top, y); bottom = Math.Max(bottom, y);
                    }
                }
            }
            if (right < left || bounds.IsEmpty)
            {
                return image;
            }

            double sx = bounds.Width / Size, sy = bounds.Height / Size;
            var crop = new Rect(bounds.X + left * sx, bounds.Y + top * sy, (right - left + 1) * sx, (bottom - top + 1) * sy);
            var group = new DrawingGroup { ClipGeometry = new RectangleGeometry(crop) };
            group.Children.Add(image.Drawing);
            var result = new DrawingImage(group);
            result.Freeze();
            return result;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
