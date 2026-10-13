using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using SmartHunter.Game.Data.ViewModels;

namespace SmartHunter.Ui.Windows
{
    public partial class ConsoleWindow : Window
    {
        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        public ConsoleWindow()
        {
            InitializeComponent();

            Shell.DataContext = SettingsViewModel.Instance;
            LogPanel.DataContext = ConsoleViewModel.Instance;
            VersionText.Text = Core.Helpers.AppUpdater.CurrentVersion.ToString(3);
            SettingsViewModel.Instance.PageChanged += OnPageChanged;
            System.Windows.Data.CollectionViewSource.GetDefaultView(SettingsViewModel.Instance.Settings)
                .GroupDescriptions.Add(new System.Windows.Data.PropertyGroupDescription("Group"));

            // Dark frame + Windows 11 rounded corners; silently ignored on older Windows
            SourceInitialized += (s, e) =>
            {
                var handle = new WindowInteropHelper(this).Handle;
                int on = 1, round = 2;
                DwmSetWindowAttribute(handle, 20, ref on, sizeof(int));
                DwmSetWindowAttribute(handle, 33, ref round, sizeof(int));
            };

            // Maximized WindowChrome windows overhang the screen by the frame size
            StateChanged += (s, e) =>
            {
                Shell.Margin = WindowState == WindowState.Maximized ? new Thickness(7) : new Thickness(0);
                MaxButton.Content = WindowState == WindowState.Maximized ? "" : "";
            };
        }

        void ResetLayout_Click(object sender, RoutedEventArgs e) => OverlayViewModel.Instance.ResetLayout();

        // Esc clears the search; Ctrl+F from anywhere in the window jumps to it
        void SettingsSearch_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Escape && SettingsSearch.Text.Length > 0)
            {
                SettingsSearch.Clear();
                e.Handled = true;
            }
        }

        protected override void OnPreviewKeyDown(System.Windows.Input.KeyEventArgs e)
        {
            base.OnPreviewKeyDown(e);
            if (e.Key == System.Windows.Input.Key.F && System.Windows.Input.Keyboard.Modifiers == System.Windows.Input.ModifierKeys.Control)
            {
                SettingsSearch.Focus();
                SettingsSearch.SelectAll();
                e.Handled = true;
            }
        }

        // The new page slides in 6 px and fades up, quickly (it's a click away, used all the time), and starts at the top
        void OnPageChanged()
        {
            SettingsScroll.ScrollToTop();
            if (!SystemParameters.ClientAreaAnimation)
            {
                return;
            }
            var ease = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut };
            var fade = new System.Windows.Media.Animation.DoubleAnimation(0.35, 1, TimeSpan.FromMilliseconds(160)) { EasingFunction = ease };
            var slide = new System.Windows.Media.Animation.DoubleAnimation(6, 0, TimeSpan.FromMilliseconds(180)) { EasingFunction = ease };
            System.Windows.Media.Animation.Timeline.SetDesiredFrameRate(fade, Core.Helpers.WindowHelper.PrimaryRefreshRate);
            System.Windows.Media.Animation.Timeline.SetDesiredFrameRate(slide, Core.Helpers.WindowHelper.PrimaryRefreshRate);
            SettingsCard.BeginAnimation(OpacityProperty, fade);
            ((System.Windows.Media.TranslateTransform)SettingsCard.RenderTransform).BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, slide);
        }

        // The header's edge line shows only while content is scrolled under it
        bool m_HeaderLineOn;

        void SettingsScroll_ScrollChanged(object sender, System.Windows.Controls.ScrollChangedEventArgs e)
        {
            bool on = e.VerticalOffset > 0;
            if (on == m_HeaderLineOn)
            {
                return;
            }
            m_HeaderLineOn = on;
            var fade = new System.Windows.Media.Animation.DoubleAnimation(on ? 1 : 0, TimeSpan.FromMilliseconds(120));
            System.Windows.Media.Animation.Timeline.SetDesiredFrameRate(fade, Core.Helpers.WindowHelper.PrimaryRefreshRate);
            HeaderLine.BeginAnimation(OpacityProperty, fade);
        }

        // A row counts as seen once the mouse has rested on it for a moment (passing over it doesn't)
        DateTime m_HoverStart;

        void SettingRow_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e) => m_HoverStart = DateTime.Now;

        void SettingRow_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if ((DateTime.Now - m_HoverStart).TotalMilliseconds >= 700 && ((FrameworkElement)sender).DataContext is Core.Setting setting)
            {
                SettingsViewModel.Instance.Seen(setting);
            }
        }

        void Close_Click(object sender, RoutedEventArgs e) => Close();
        void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
        void Maximize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }
}
