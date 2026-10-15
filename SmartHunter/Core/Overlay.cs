using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using SmartHunter.Core.Helpers;
using SmartHunter.Core.Windows;

namespace SmartHunter.Core
{
    public abstract class Overlay
    {
        KeyboardInput m_KeyboardInput;

        Window m_MainWindow;
        protected WidgetWindow[] WidgetWindows { get; private set; }

        public Overlay(Window mainWindow, params WidgetWindow[] widgetWindows)
        {
            m_MainWindow = mainWindow;
            WidgetWindows = widgetWindows;

            // Closing the console quits the app. Without this, the hidden drag-guide window would keep the process and its keyboard hook alive.
            Application.Current.MainWindow = mainWindow;
            Application.Current.ShutdownMode = ShutdownMode.OnMainWindowClose;

            m_MainWindow.Loaded += MainWindow_Loaded;
            m_MainWindow.Closing += MainWindow_Closing;

            m_KeyboardInput = new KeyboardInput();
            m_KeyboardInput.InputReceived += KeyboardInput_InputReceived;

            m_MainWindow.Show();
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            UpdateWidgetsFromConfig();
            /*
            foreach (var widgetWindow in WidgetWindows)
            {
                if (!widgetWindow.IsVisible)
                {
                    widgetWindow.Owner = m_MainWindow;
                    widgetWindow.Opacity = 0.0f;
                    widgetWindow.Hide();
                }
            }
            */
        }

        private void MainWindow_Closing(object sender, CancelEventArgs e)
        {
            foreach (var widgetWindow in WidgetWindows)
            {
                widgetWindow.Close();
            }
        }

        private void ToggleWidgetWindow(WidgetWindow widgetWindow)
        {
            if (widgetWindow.Visibility != Visibility.Visible)
            {
                // No owner, not even for a moment: giving it the Aether window as owner brought that window to the
                // front every time a widget was shown, e.g. from the layout editor's toolbar
                widgetWindow.Show();

                if (IsEditing)
                    WindowHelper.SetTopMostSelectable(widgetWindow);
                else
                    WindowHelper.SetTopMostTransparent(widgetWindow);
            }
            else
            {
                widgetWindow.Hide();
            }
        }

        public void UpdateWidgetsFromConfig()
        {
            foreach (var widgetWindow in WidgetWindows)
            {
                widgetWindow.Widget.UpdateFromConfig();

                if ((widgetWindow.Widget.IsVisible && widgetWindow.Visibility != Visibility.Visible) ||
                    (!widgetWindow.Widget.IsVisible && widgetWindow.Visibility == Visibility.Visible))
                {
                    ToggleWidgetWindow(widgetWindow);
                }
            }
        }

        public void RefreshWidgetsLayout()
        {
            foreach (var widgetWindow in WidgetWindows)
            {
                var dataContext = widgetWindow.DataContext;
                widgetWindow.DataContext = null;
                widgetWindow.DataContext = dataContext;
            }
        }

        private void KeyboardInput_InputReceived(object sender, KeyboardInputEventArgs e)
        {
            InputReceived(e.Key, e.IsDown);
        }

        abstract protected void InputReceived(Key key, bool down);

        // A widget shown while the layout is edited must take the mouse like the others
        protected virtual bool IsEditing => false;
    }
}
