using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace SmartHunter.Ui.Behaviors
{
    // Skin helper: <Border behaviors:SmoothBar.Fraction="{Binding Health.Fraction}" />
    // Eases the element's horizontal scale toward the bound fraction instead of jumping.
    // Delay > 0 makes a lagging "ghost" bar that trails behind damage.
    public static class SmoothBar
    {
        public static readonly DependencyProperty FractionProperty = DependencyProperty.RegisterAttached(
            "Fraction", typeof(double), typeof(SmoothBar), new PropertyMetadata(double.NaN, OnFractionChanged));

        public static readonly DependencyProperty DelayProperty = DependencyProperty.RegisterAttached(
            "Delay", typeof(double), typeof(SmoothBar), new PropertyMetadata(0.0));

        // Where the running animation is heading, so small steps are measured against that and not the mid-animation value
        static readonly DependencyProperty TargetProperty = DependencyProperty.RegisterAttached(
            "Target", typeof(double), typeof(SmoothBar), new PropertyMetadata(0.0));
        static double GetTarget(DependencyObject d) => (double)d.GetValue(TargetProperty);
        static void SetTarget(DependencyObject d, double value) => d.SetValue(TargetProperty, value);

        public static double GetFraction(DependencyObject d) => (double)d.GetValue(FractionProperty);
        public static void SetFraction(DependencyObject d, double value) => d.SetValue(FractionProperty, value);
        public static double GetDelay(DependencyObject d) => (double)d.GetValue(DelayProperty);
        public static void SetDelay(DependencyObject d, double value) => d.SetValue(DelayProperty, value);

        static void OnFractionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (!(d is UIElement element))
            {
                return;
            }

            if (!(element.RenderTransform is ScaleTransform scale) || scale.IsFrozen)
            {
                scale = new ScaleTransform(0, 1);
                element.RenderTransform = scale;
                element.RenderTransformOrigin = new Point(0, 0.5);
            }

            double to = (double)e.NewValue;
            to = double.IsNaN(to) || double.IsInfinity(to) ? 0 : Math.Max(0, Math.Min(1, to));

            // Sub-pixel changes (a slowly draining buildup bar) would restart the animation every tick, and every
            // animated frame makes the transparent overlay window re-composite. Skip what nobody can see.
            double target = scale.HasAnimatedProperties ? GetTarget(d) : scale.ScaleX;
            if (Math.Abs(to - target) < 0.004 && to != 0 && to != 1)
            {
                return;
            }
            SetTarget(d, to);

            // First value (e.g. a monster card appearing) fills from empty, later changes glide
            double delay = GetDelay(d);
            var animation = new DoubleAnimation(to, TimeSpan.FromMilliseconds(delay > 0 ? 650 : 420))
            {
                BeginTime = TimeSpan.FromMilliseconds(to < scale.ScaleX ? delay : 0),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            };
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, animation, HandoffBehavior.SnapshotAndReplace);
        }
    }
}
