using System;
using System.Windows;
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

            SharpenAlert.Instance.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(SharpenAlert.IsCompact))
                {
                    // One step to the smaller size, no animation; the text is drawn again at its new size so it stays sharp
                    double scale = SharpenAlert.Instance.IsCompact ? SharpenAlert.CompactScale : 1;
                    Root.LayoutTransform = new System.Windows.Media.ScaleTransform(scale, scale);
                    Dispatcher.BeginInvoke(new Action(() => SmartHunter.Ui.Controls.SharpText.Refresh(this)), System.Windows.Threading.DispatcherPriority.Loaded);
                    return;
                }
                Sync();
            };
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

        // It just appears and stays: no entrance, no pulse, no wobble
        void Sync()
        {
            bool want = SharpenAlert.Instance.IsActive && OverlayViewModel.Instance.IsVisible;
            if (want == m_IsShown)
            {
                return;
            }
            m_IsShown = want;

            if (!want)
            {
                Hide();
                return;
            }

            Show();
            WindowHelper.SetTopMostTransparent(this);
        }
    }
}
