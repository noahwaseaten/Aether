using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using SmartHunter.Core.Helpers;

namespace SmartHunter.Ui.Behaviors
{
    // Slides a switch's knob only when it's flipped. The old template ran its slide whenever the template was applied
    // to a checked switch, and a page's rows get their template the first time the page is opened: every switch on it
    // visibly slid from off to on. The template names its parts Knob (a TranslateTransform) and TrackOn.
    public static class SwitchAnimation
    {
        const double Travel = 16;

        public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
            "IsEnabled", typeof(bool), typeof(SwitchAnimation), new PropertyMetadata(false, OnIsEnabledChanged));
        public static bool GetIsEnabled(DependencyObject o) => (bool)o.GetValue(IsEnabledProperty);
        public static void SetIsEnabled(DependencyObject o, bool value) => o.SetValue(IsEnabledProperty, value);

        static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is CheckBox box && (bool)e.NewValue)
            {
                // Showing the row: the knob is simply where it belongs
                box.Loaded += (s, a) => Apply(box, false);
                box.Checked += (s, a) => Apply(box, box.IsLoaded);
                box.Unchecked += (s, a) => Apply(box, box.IsLoaded);
            }
        }

        static void Apply(CheckBox box, bool animate)
        {
            var knob = box.Template?.FindName("Knob", box) as FrameworkElement;
            var track = box.Template?.FindName("TrackOn", box) as FrameworkElement;
            if (knob == null || track == null || !(knob.RenderTransform is TranslateTransform move))
            {
                return;
            }

            double x = box.IsChecked == true ? Travel : 0;
            double opacity = box.IsChecked == true ? 1 : 0;
            if (!animate)
            {
                move.BeginAnimation(TranslateTransform.XProperty, null);
                track.BeginAnimation(UIElement.OpacityProperty, null);
                move.X = x;
                track.Opacity = opacity;
                return;
            }

            var slide = new DoubleAnimation(x, TimeSpan.FromMilliseconds(180)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            var fade = new DoubleAnimation(opacity, TimeSpan.FromMilliseconds(180));
            Timeline.SetDesiredFrameRate(slide, WindowHelper.PrimaryRefreshRate);
            Timeline.SetDesiredFrameRate(fade, WindowHelper.PrimaryRefreshRate);
            move.BeginAnimation(TranslateTransform.XProperty, slide);
            track.BeginAnimation(UIElement.OpacityProperty, fade);
        }
    }
}
