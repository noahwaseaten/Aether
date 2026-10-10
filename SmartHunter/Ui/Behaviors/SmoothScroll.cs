using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace SmartHunter.Ui.Behaviors
{
    // Eases the mouse wheel instead of jumping 48 px per notch. Fast wheel spins add up into one glide.
    public static class SmoothScroll
    {
        const double PixelsPerNotch = 64; // a notch is 120 wheel units
        static readonly Duration Glide = new Duration(TimeSpan.FromMilliseconds(260));
        static readonly IEasingFunction Ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
            "IsEnabled", typeof(bool), typeof(SmoothScroll), new PropertyMetadata(false, OnIsEnabledChanged));
        public static bool GetIsEnabled(DependencyObject o) => (bool)o.GetValue(IsEnabledProperty);
        public static void SetIsEnabled(DependencyObject o, bool value) => o.SetValue(IsEnabledProperty, value);

        // VerticalOffset is read-only, so the animation drives this and it forwards to the ScrollViewer
        static readonly DependencyProperty OffsetProperty = DependencyProperty.RegisterAttached(
            "Offset", typeof(double), typeof(SmoothScroll), new PropertyMetadata(0.0, (o, e) => ((ScrollViewer)o).ScrollToVerticalOffset((double)e.NewValue)));
        static readonly DependencyProperty TargetProperty = DependencyProperty.RegisterAttached(
            "Target", typeof(double), typeof(SmoothScroll), new PropertyMetadata(double.NaN));
        static readonly DependencyProperty AnimationProperty = DependencyProperty.RegisterAttached(
            "Animation", typeof(AnimationTimeline), typeof(SmoothScroll));

        static void OnIsEnabledChanged(DependencyObject o, DependencyPropertyChangedEventArgs e)
        {
            if (o is ScrollViewer scrollViewer)
            {
                scrollViewer.PreviewMouseWheel -= OnPreviewMouseWheel;
                if ((bool)e.NewValue)
                    scrollViewer.PreviewMouseWheel += OnPreviewMouseWheel;
            }
        }

        static bool CanScroll(ScrollViewer scrollViewer, int delta) =>
            delta > 0 ? scrollViewer.VerticalOffset > 0 : scrollViewer.VerticalOffset < scrollViewer.ScrollableHeight;

        static void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            var scrollViewer = (ScrollViewer)sender;

            // The wheel tunnels outside-in: leave it to a scrollable area under the cursor that can still move
            for (var element = e.OriginalSource as DependencyObject; element != null && element != scrollViewer; element = VisualTreeHelper.GetParent(element))
            {
                if (element is ScrollViewer inner && GetIsEnabled(inner) && CanScroll(inner, e.Delta))
                    return;
                if (!(element is Visual))
                    break;
            }

            if (!CanScroll(scrollViewer, e.Delta))
                return;

            e.Handled = true;
            double target = (double)scrollViewer.GetValue(TargetProperty);
            if (double.IsNaN(target))
                target = scrollViewer.VerticalOffset;
            target = Math.Max(0, Math.Min(scrollViewer.ScrollableHeight, target - e.Delta / 120.0 * PixelsPerNotch));
            scrollViewer.SetValue(TargetProperty, target);

            var animation = new DoubleAnimation(scrollViewer.VerticalOffset, target, Glide) { EasingFunction = Ease };
            // App caps animations at 30 fps to keep the in-game overlay cheap; scrolling runs at the monitor's rate
            Timeline.SetDesiredFrameRate(animation, Core.Helpers.WindowHelper.RefreshRate(scrollViewer));
            animation.Completed += (s, _) =>
            {
                // Only the latest glide resets the target; superseded ones finishing late must not
                if (scrollViewer.GetValue(AnimationProperty) == animation)
                    scrollViewer.SetValue(TargetProperty, double.NaN);
            };
            scrollViewer.SetValue(AnimationProperty, animation);
            scrollViewer.BeginAnimation(OffsetProperty, animation);
        }
    }
}
