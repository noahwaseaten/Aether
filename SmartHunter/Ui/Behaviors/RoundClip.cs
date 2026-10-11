using System;
using System.Windows;
using System.Windows.Media;

namespace SmartHunter.Ui.Behaviors
{
    // Skin helper: <Grid behaviors:RoundClip.Radius="4"> clips a bar (track and fill together) to rounded corners.
    // A negative radius makes a pill (half the height). Rounding the fill itself squashed its corners as it shrank.
    public static class RoundClip
    {
        public static readonly DependencyProperty RadiusProperty = DependencyProperty.RegisterAttached(
            "Radius", typeof(double), typeof(RoundClip), new PropertyMetadata(double.NaN, OnRadiusChanged));

        public static double GetRadius(DependencyObject d) => (double)d.GetValue(RadiusProperty);
        public static void SetRadius(DependencyObject d, double value) => d.SetValue(RadiusProperty, value);

        static void OnRadiusChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is FrameworkElement element)
            {
                element.SizeChanged -= Update;
                element.SizeChanged += Update;
                Apply(element);
            }
        }

        static void Update(object sender, SizeChangedEventArgs e) => Apply((FrameworkElement)sender);

        static void Apply(FrameworkElement element)
        {
            double radius = GetRadius(element);
            if (double.IsNaN(radius) || element.ActualWidth <= 0 || element.ActualHeight <= 0)
            {
                return;
            }
            if (radius < 0)
            {
                radius = element.ActualHeight / 2;
            }
            radius = Math.Min(radius, Math.Min(element.ActualWidth, element.ActualHeight) / 2);
            var clip = new RectangleGeometry(new Rect(0, 0, element.ActualWidth, element.ActualHeight), radius, radius);
            clip.Freeze();
            element.Clip = clip;
        }
    }
}
