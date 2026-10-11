using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace SmartHunter.Ui.Controls
{
    // A bar built like HunterPie's gauge: a dark trough, a fill inset by the border with rounded ends (clipped, so its
    // corners never squash), a light line every 10%, and a 1px border on top. Over the game a translucent track
    // let the scene show through and the bar had no edge.
    public class Gauge : Grid
    {
        public static readonly DependencyProperty FractionProperty = DependencyProperty.Register(nameof(Fraction), typeof(double), typeof(Gauge),
            new PropertyMetadata(0.0, (d, e) => ((Gauge)d).AnimateTo((double)e.NewValue)));
        public static readonly DependencyProperty FillProperty = DependencyProperty.Register(nameof(Fill), typeof(Brush), typeof(Gauge),
            new PropertyMetadata(Brushes.White, (d, e) => ((Gauge)d).m_Fill.Background = (Brush)e.NewValue));
        public static readonly DependencyProperty CornerRadiusProperty = DependencyProperty.Register(nameof(CornerRadius), typeof(double), typeof(Gauge),
            new PropertyMetadata(3.0, (d, e) => ((Gauge)d).ApplyRadius()));
        // Milliseconds before the fill moves, and whether it animates at the monitor's refresh rate (the recap, shown
        // once a quest) instead of the overlay's 30 fps
        public static readonly DependencyProperty DelayProperty = DependencyProperty.Register(nameof(Delay), typeof(double), typeof(Gauge), new PropertyMetadata(0.0));
        public static readonly DependencyProperty IsSmoothProperty = DependencyProperty.Register(nameof(IsSmooth), typeof(bool), typeof(Gauge), new PropertyMetadata(false));
        public static readonly DependencyProperty ShowMarkersProperty = DependencyProperty.Register(nameof(ShowMarkers), typeof(bool), typeof(Gauge),
            new PropertyMetadata(true, (d, e) => ((Gauge)d).m_Markers.Visibility = (bool)e.NewValue ? Visibility.Visible : Visibility.Collapsed));

        // The fill's shown fraction, eased toward Fraction
        static readonly DependencyProperty ShownProperty = DependencyProperty.Register("Shown", typeof(double), typeof(Gauge),
            new PropertyMetadata(0.0, (d, e) => ((Gauge)d).LayoutFill()));

        public double Fraction { get { return (double)GetValue(FractionProperty); } set { SetValue(FractionProperty, value); } }
        public Brush Fill { get { return (Brush)GetValue(FillProperty); } set { SetValue(FillProperty, value); } }
        public double CornerRadius { get { return (double)GetValue(CornerRadiusProperty); } set { SetValue(CornerRadiusProperty, value); } }
        public bool ShowMarkers { get { return (bool)GetValue(ShowMarkersProperty); } set { SetValue(ShowMarkersProperty, value); } }
        public double Delay { get { return (double)GetValue(DelayProperty); } set { SetValue(DelayProperty, value); } }
        public bool IsSmooth { get { return (bool)GetValue(IsSmoothProperty); } set { SetValue(IsSmoothProperty, value); } }

        readonly Border m_Trough = new Border();
        // Full width and clipped to the shown part: changing its Width re-laid out the whole widget window every
        // animation frame, and in a fight the monster's bars animate almost nonstop
        readonly Border m_Fill = new Border { Margin = new Thickness(1) };
        readonly Control m_Markers = new Control { Margin = new Thickness(1), IsHitTestVisible = false };
        readonly Border m_Edge = new Border { BorderThickness = new Thickness(1), IsHitTestVisible = false };
        double m_Target = -1;

        public Gauge()
        {
            m_Trough.SetResourceReference(Border.BackgroundProperty, "B_GaugeTrough");
            m_Markers.SetResourceReference(Control.TemplateProperty, "A_Template_Gauge");
            m_Edge.SetResourceReference(Border.BorderBrushProperty, "B_GaugeBorder");
            m_Fill.Background = Fill;
            Children.Add(m_Trough);
            Children.Add(m_Fill);
            Children.Add(m_Markers);
            Children.Add(m_Edge);
            ApplyRadius();
            LayoutFill();
            SizeChanged += (s, e) => LayoutFill();
        }

        void ApplyRadius()
        {
            m_Trough.CornerRadius = m_Edge.CornerRadius = new CornerRadius(CornerRadius);
            m_Fill.CornerRadius = new CornerRadius(Math.Max(0, CornerRadius - 1));
            LayoutFill();
        }

        void LayoutFill()
        {
            double width = Math.Max(0, (ActualWidth - 2) * (double)GetValue(ShownProperty));
            double height = Math.Max(0, ActualHeight - 2);
            double radius = Math.Max(0, Math.Min(CornerRadius - 1, Math.Min(width, height) / 2));
            var clip = new RectangleGeometry(new Rect(0, 0, width, height), radius, radius);
            clip.Freeze();
            m_Fill.Clip = clip;
        }

        void AnimateTo(double to)
        {
            to = double.IsNaN(to) || double.IsInfinity(to) ? 0 : Math.Max(0, Math.Min(1, to));
            // Sub-pixel changes (a slowly draining buildup) would restart the animation every tick, and each animated
            // frame re-composites the transparent overlay window: skip what nobody can see
            if (Math.Abs(to - m_Target) < 0.004 && to != 0 && to != 1)
            {
                return;
            }
            m_Target = to;
            var animation = new DoubleAnimation(to, TimeSpan.FromMilliseconds(IsSmooth ? 650 : 420))
            {
                BeginTime = TimeSpan.FromMilliseconds(Delay),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            };
            if (IsSmooth)
            {
                Timeline.SetDesiredFrameRate(animation, SmartHunter.Core.Helpers.WindowHelper.PrimaryRefreshRate);
            }
            BeginAnimation(ShownProperty, animation, HandoffBehavior.SnapshotAndReplace);
        }
    }
}
