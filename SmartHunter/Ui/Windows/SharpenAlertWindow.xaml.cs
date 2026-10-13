using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using SmartHunter.Core.Helpers;
using SmartHunter.Game.Data;
using SmartHunter.Game.Data.ViewModels;

namespace SmartHunter.Ui.Windows
{
    public partial class SharpenAlertWindow : Window
    {
        static SharpenAlertWindow s_Instance;

        // Called once at startup: the window stays hidden until the alert is on and the overlay is showing
        public static void Initialize()
        {
            try
            {
                s_Instance = s_Instance ?? new SharpenAlertWindow();
            }
            catch (Exception ex)
            {
                // A reminder for one hunter must never be the reason Aether doesn't start
                SmartHunter.Core.Log.WriteLine($"The sharpen reminder couldn't start: {ex.Message}");
            }
        }

        bool m_IsShown;

        SharpenAlertWindow()
        {
            InitializeComponent();
            DataContext = SharpenAlert.Instance;

            SharpenAlert.Instance.PropertyChanged += (s, e) => Sync();
            OverlayViewModel.Instance.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(OverlayViewModel.IsVisible))
                {
                    Sync();
                }
            };
            Sync();
            // Top centre of the main screen, wherever the content ends up sized
            SizeChanged += (s, e) =>
            {
                Left = (SystemParameters.PrimaryScreenWidth - ActualWidth) / 2;
                Top = SystemParameters.PrimaryScreenHeight * 0.06;
            };
        }

        void Sync()
        {
            bool want = SharpenAlert.Instance.IsActive && OverlayViewModel.Instance.IsVisible;
            if (want == m_IsShown)
            {
                return;
            }
            m_IsShown = want;

            var group = (TransformGroup)Root.RenderTransform;
            var scale = (ScaleTransform)group.Children[0];
            var tilt = (RotateTransform)group.Children[1];
            if (!want)
            {
                Card.BeginAnimation(OpacityProperty, null);
                tilt.BeginAnimation(RotateTransform.AngleProperty, null);
                Hide();
                return;
            }

            Show();
            WindowHelper.SetTopMostTransparent(this);

            // Arrives quickly (a 6% grow and fade, 180 ms), then breathes for as long as it's needed. This is the one overlay
            // element that is meant to keep moving: it is only ever up while something needs fixing.
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            var grow = new DoubleAnimation(0.94, 1, TimeSpan.FromMilliseconds(180)) { EasingFunction = ease };
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
            var breathe = new DoubleAnimation(1, 0.78, TimeSpan.FromMilliseconds(700))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
            };
            Card.BeginAnimation(OpacityProperty, breathe);
            // ...and shakes its head, a degree and a bit either way, like it's very disappointed
            var wobble = new DoubleAnimation(-1.1, 1.1, TimeSpan.FromMilliseconds(520))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
            };
            tilt.BeginAnimation(RotateTransform.AngleProperty, wobble);
        }
    }
}
