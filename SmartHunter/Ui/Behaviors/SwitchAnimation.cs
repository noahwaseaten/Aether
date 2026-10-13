using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using SmartHunter.Core.Helpers;

namespace SmartHunter.Ui.Behaviors
{
    // Slides a switch's knob only when it's flipped. Where the knob sits when nothing is happening is the template's
    // own job (IsChecked puts it on the right), so it is correct whenever a row is first shown. This only adds the
    // slide: it starts from where the knob was and ends at the template's position, then lets go (FillBehavior.Stop).
    // The template names its parts Knob (with a TranslateTransform) and TrackOn.
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
                box.Checked += (s, a) => Flip(box, true);
                box.Unchecked += (s, a) => Flip(box, false);
            }
        }

        static void Flip(CheckBox box, bool on)
        {
            // A switch that is only being set up (or sits on a hidden page) just takes its place, no slide
            if (!box.IsLoaded || !box.IsVisible)
            {
                return;
            }
            var knob = box.Template?.FindName("Knob", box) as FrameworkElement;
            var track = box.Template?.FindName("TrackOn", box) as FrameworkElement;
            if (knob == null || track == null || !(knob.RenderTransform is TranslateTransform move))
            {
                return;
            }

            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            var slide = new DoubleAnimation(on ? -Travel : Travel, 0, TimeSpan.FromMilliseconds(180)) { EasingFunction = ease, FillBehavior = FillBehavior.Stop };
            var fade = new DoubleAnimation(on ? 0 : 1, on ? 1 : 0, TimeSpan.FromMilliseconds(180)) { FillBehavior = FillBehavior.Stop };
            Timeline.SetDesiredFrameRate(slide, WindowHelper.PrimaryRefreshRate);
            Timeline.SetDesiredFrameRate(fade, WindowHelper.PrimaryRefreshRate);
            move.BeginAnimation(TranslateTransform.XProperty, slide);
            track.BeginAnimation(UIElement.OpacityProperty, fade);
        }
    }
}
