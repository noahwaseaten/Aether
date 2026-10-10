using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace SmartHunter.Ui.Behaviors
{
    // Glides the mouse wheel instead of jumping 48 px per notch. Each frame the offset closes a fixed fraction of
    // the gap to the target, so extra notches just move the target and speed changes stay continuous. Restarting
    // an eased animation per notch (the old way) jolted the speed on every notch and stuttered on fast spins.
    public static class SmoothScroll
    {
        const double PixelsPerNotch = 64;   // a notch is 120 wheel units
        const double TimeConstant = 0.06;   // seconds; ~95% of the way there after 0.18 s

        public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
            "IsEnabled", typeof(bool), typeof(SmoothScroll), new PropertyMetadata(false, OnIsEnabledChanged));
        public static bool GetIsEnabled(DependencyObject o) => (bool)o.GetValue(IsEnabledProperty);
        public static void SetIsEnabled(DependencyObject o, bool value) => o.SetValue(IsEnabledProperty, value);

        static readonly DependencyProperty GlideProperty = DependencyProperty.RegisterAttached(
            "Glide", typeof(Glide), typeof(SmoothScroll));

        class Glide
        {
            readonly ScrollViewer m_ScrollViewer;
            double m_Current;
            TimeSpan m_LastFrame;
            public double Target;
            public bool IsRunning { get; private set; }

            public Glide(ScrollViewer scrollViewer) { m_ScrollViewer = scrollViewer; }

            public void Start()
            {
                if (IsRunning)
                    return;
                m_Current = m_ScrollViewer.VerticalOffset;
                m_LastFrame = TimeSpan.Zero;
                IsRunning = true;
                // Runs once per rendered frame, so it follows the monitor's refresh rate
                CompositionTarget.Rendering += OnFrame;
            }

            void Stop()
            {
                IsRunning = false;
                CompositionTarget.Rendering -= OnFrame;
            }

            void OnFrame(object sender, EventArgs e)
            {
                var time = ((RenderingEventArgs)e).RenderingTime;
                if (time == m_LastFrame)
                    return; // Rendering can fire more than once for the same frame
                double dt = m_LastFrame == TimeSpan.Zero ? 1 / 60.0 : Math.Min((time - m_LastFrame).TotalSeconds, 0.1);
                m_LastFrame = time;

                Target = Math.Max(0, Math.Min(m_ScrollViewer.ScrollableHeight, Target));
                m_Current += (Target - m_Current) * (1 - Math.Exp(-dt / TimeConstant));
                if (Math.Abs(Target - m_Current) < 0.5 || !m_ScrollViewer.IsLoaded)
                {
                    m_Current = Target;
                    Stop();
                }
                // Whole pixels: text snapped to the pixel grid shimmers at fractional offsets
                m_ScrollViewer.ScrollToVerticalOffset(Math.Round(m_Current));
            }
        }

        static void OnIsEnabledChanged(DependencyObject o, DependencyPropertyChangedEventArgs e)
        {
            if (o is ScrollViewer scrollViewer)
            {
                scrollViewer.PreviewMouseWheel -= OnPreviewMouseWheel;
                if ((bool)e.NewValue)
                    scrollViewer.PreviewMouseWheel += OnPreviewMouseWheel;
            }
        }

        static bool CanScroll(ScrollViewer scrollViewer, int delta)
        {
            var glide = (Glide)scrollViewer.GetValue(GlideProperty);
            double offset = glide != null && glide.IsRunning ? glide.Target : scrollViewer.VerticalOffset;
            return delta > 0 ? offset > 0 : offset < scrollViewer.ScrollableHeight;
        }

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
            var glide = (Glide)scrollViewer.GetValue(GlideProperty);
            if (glide == null)
            {
                glide = new Glide(scrollViewer);
                scrollViewer.SetValue(GlideProperty, glide);
            }
            if (!glide.IsRunning)
                glide.Target = scrollViewer.VerticalOffset; // the scrollbar may have been dragged since
            glide.Target -= e.Delta / 120.0 * PixelsPerNotch;
            glide.Start();
        }
    }
}
