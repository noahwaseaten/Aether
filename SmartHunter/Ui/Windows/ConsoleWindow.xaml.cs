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

            LogsTab.DataContext = ConsoleViewModel.Instance;
            SettingsTab.DataContext = SettingsViewModel.Instance;
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

        // Matches a setting's name, its explanation or its section; empty shows everything
        void SettingsSearch_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            string query = SettingsSearch.Text.Trim();
            bool Has(string text) => text != null && text.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
            System.Windows.Data.CollectionViewSource.GetDefaultView(SettingsViewModel.Instance.Settings).Filter =
                query.Length == 0 ? null : (Predicate<object>)(item => item is Core.Setting s && (Has(s.Name) || Has(s.Description) || Has(s.Group)));
        }
        void Close_Click(object sender, RoutedEventArgs e) => Close();
        void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
        void Maximize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }
}
