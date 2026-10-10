using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using SmartHunter.Core.Helpers;

namespace SmartHunter.Core.Windows
{
    // Edit mode's stage, like Discord's overlay and Lunar's HUD editor: the game dims behind a dark layer, a toolbar at the
    // top shows or hides each widget, and alignment guides are drawn here while a widget is dragged. The widgets stay their
    // own windows above it. It never takes focus, so the game keeps its keyboard.
    public class LayoutEditor : Window
    {
        static LayoutEditor s_Instance;
        public static LayoutEditor Instance => s_Instance ?? (s_Instance = new LayoutEditor());

        // Guides are two 1-pixel windows moved around, not lines on the full-screen layer: redrawing that layer (8 MB a
        // frame on the CPU) for every mouse move made dragging lag
        readonly Window m_GuideX, m_GuideY;
        readonly StackPanel m_Chips = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        // Its own small window, so it can stack above the widgets: on the backdrop it hid under the monster widget
        readonly Window m_Toolbar;
        readonly Brush m_Accent;
        Action m_Done, m_Reset;
        Action<WidgetWindow> m_Toggle;
        IList<WidgetWindow> m_Widgets = new List<WidgetWindow>();
        IntPtr m_ReturnFocusTo;
        bool m_IsOpen;

        static readonly Brush Surface = Frozen(Color.FromArgb(0xF2, 0x1A, 0x1B, 0x1E));
        static readonly Brush Stroke = Frozen(Color.FromArgb(0xFF, 0x2E, 0x30, 0x36));
        static readonly Brush Text = Frozen(Color.FromRgb(0xF3, 0xED, 0xE0));
        static readonly Brush Muted = Frozen(Color.FromRgb(0xA3, 0x9C, 0x8C));
        static readonly Brush OnAccent = Frozen(Color.FromRgb(0x1C, 0x14, 0x00));

        LayoutEditor()
        {
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            ShowActivated = false;
            Topmost = true;
            Title = "Aether layout editor";
            Left = SystemParameters.VirtualScreenLeft;
            Top = SystemParameters.VirtualScreenTop;
            Width = SystemParameters.VirtualScreenWidth;
            Height = SystemParameters.VirtualScreenHeight;
            SourceInitialized += (s, e) => WindowHelper.SetTopMostFocusable(this);

            m_Accent = Application.Current.TryFindResource("B_Accent") as Brush ?? Frozen(Color.FromRgb(0xE2, 0xC2, 0x7A));
            // Drawn once when the editor opens and never again: no fade, no guides on it
            Content = new Border { Background = Frozen(Color.FromArgb(0x99, 0x06, 0x07, 0x09)) };
            m_GuideX = GuideLine(true);
            m_GuideY = GuideLine(false);

            m_Toolbar = new Window
            {
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = Brushes.Transparent,
                ShowInTaskbar = false,
                ShowActivated = false,
                Topmost = true,
                Title = "Aether layout toolbar",
                SizeToContent = SizeToContent.WidthAndHeight,
                Content = BuildToolbar(),
            };
            m_Toolbar.SourceInitialized += (s, e) =>
            {
                WindowHelper.SetTopMostSelectable(m_Toolbar);
                WindowHelper.RefuseFocus(m_Toolbar);
            };
            // Alt-Tab or the Windows key while editing: finish, or the dark layer would cover whatever you switched to.
            // Focus moving to one of Aether's own windows doesn't count: clicking a widget or the toolbar closed the editor.
            Deactivated += (s, e) => Dispatcher.BeginInvoke(new Action(() =>
            {
                if (m_IsOpen && !WindowHelper.IsOwnWindow(WindowsApi.GetForegroundWindow()))
                {
                    Log.WriteLine("Layout editor lost focus, closing it");
                    m_ReturnFocusTo = IntPtr.Zero;
                    m_Done?.Invoke();
                }
            }), System.Windows.Threading.DispatcherPriority.Input);
            // Centred on the primary screen: the middle is where the hunter stands, so no widget lives there
            m_Toolbar.SizeChanged += (s, e) =>
            {
                m_Toolbar.Left = (SystemParameters.PrimaryScreenWidth - m_Toolbar.ActualWidth) / 2;
                m_Toolbar.Top = (SystemParameters.PrimaryScreenHeight - m_Toolbar.ActualHeight) / 2;
            };
        }

        Window GuideLine(bool vertical)
        {
            var line = new Window
            {
                WindowStyle = WindowStyle.None,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false,
                ShowActivated = false,
                Topmost = true,
                Background = m_Accent,
                Width = vertical ? 1 : SystemParameters.VirtualScreenWidth,
                Height = vertical ? SystemParameters.VirtualScreenHeight : 1,
                Left = SystemParameters.VirtualScreenLeft,
                Top = SystemParameters.VirtualScreenTop,
                Title = "Aether layout guide",
            };
            line.SourceInitialized += (s, e) => WindowHelper.SetTopMostTransparent(line);
            return line;
        }

        static Brush Frozen(Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }

        UIElement BuildToolbar()
        {
            var title = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 18, 0) };
            title.Children.Add(new TextBlock { Text = "Edit layout", Foreground = Text, FontSize = 14, FontWeight = FontWeights.SemiBold });
            title.Children.Add(new TextBlock { Text = "Click a widget to show or hide it", Foreground = Muted, FontSize = 11.5 });

            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(title);
            row.Children.Add(m_Chips);
            row.Children.Add(new Rectangle { Width = 1, Fill = Stroke, Margin = new Thickness(14, 4, 14, 4) });
            row.Children.Add(Pill("Reset", false, () => m_Reset?.Invoke()));
            row.Children.Add(Pill("Done", true, () => m_Done?.Invoke()));

            var card = new Border
            {
                Background = Surface,
                BorderBrush = Stroke,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(10, 8, 8, 8),
                Child = row,
            };


            var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            stack.Children.Add(card);
            TextOptions.SetTextFormattingMode(stack, TextFormattingMode.Display);
            return stack;
        }

        Border Pill(string text, bool primary, Action click)
        {
            var pill = new Border
            {
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(14, 6, 14, 7),
                Margin = new Thickness(4, 0, 0, 0),
                Background = primary ? m_Accent : Frozen(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF)),
                Cursor = Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock { Text = text, FontWeight = FontWeights.SemiBold, FontSize = 12.5, Foreground = primary ? OnAccent : Text },
            };
            pill.MouseEnter += (s, e) => pill.Opacity = 0.85;
            pill.MouseLeave += (s, e) => pill.Opacity = 1;
            pill.MouseLeftButtonDown += (s, e) => e.Handled = true;
            pill.MouseLeftButtonUp += (s, e) => { e.Handled = true; click(); };
            return pill;
        }

        Border Chip(WidgetWindow window)
        {
            var dot = new Ellipse { Width = 7, Height = 7, Margin = new Thickness(0, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center };
            var label = new TextBlock { Text = window.Title, FontSize = 12.5, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(dot);
            row.Children.Add(label);
            var chip = new Border
            {
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(10, 6, 12, 7),
                Margin = new Thickness(0, 0, 4, 0),
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand,
                Child = row,
                ToolTip = "Show or hide this widget",
            };

            void Refresh()
            {
                bool on = window.Widget.IsVisible;
                dot.Fill = on ? m_Accent : Muted;
                label.Foreground = on ? Text : Muted;
                chip.Background = on ? Frozen(Color.FromArgb(0x1F, 0xE2, 0xC2, 0x7A)) : Brushes.Transparent;
                chip.BorderBrush = on ? Frozen(Color.FromArgb(0x59, 0xE2, 0xC2, 0x7A)) : Stroke;
            }
            Refresh();
            window.Widget.PropertyChanged += (s, e) => { if (e.PropertyName == nameof(Data.Widget.IsVisible)) Refresh(); };
            chip.MouseLeftButtonDown += (s, e) => e.Handled = true;
            chip.MouseLeftButtonUp += (s, e) => { e.Handled = true; m_Toggle?.Invoke(window); };
            return chip;
        }

        public void Open(IEnumerable<WidgetWindow> widgets, Action<WidgetWindow> toggle, Action reset, Action done)
        {
            m_Toggle = toggle;
            m_Reset = reset;
            m_Done = done;
            var list = widgets.ToList();
            if (!list.SequenceEqual(m_Widgets))
            {
                m_Widgets = list;
                m_Chips.Children.Clear();
                foreach (var widget in m_Widgets)
                {
                    m_Chips.Children.Add(Chip(widget));
                }
            }

            m_IsOpen = true;
            ShowGuides(new Guide[0]);
            if (!IsVisible)
            {
                Show();
            }

            // Take focus like Discord's overlay does: while the game has it, it hides the cursor and pins it to the
            // middle of the screen, so nothing could be dragged. The game gets focus back when editing ends.
            var foreground = WindowsApi.GetForegroundWindow();
            var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            if (foreground != handle)
            {
                m_ReturnFocusTo = foreground;
            }
            WindowHelper.BringToForeground(handle);
            WindowsApi.ClipCursor(IntPtr.Zero);
        }

        // Call after the widgets are raised (and again when one is shown), so the toolbar stays on top of them
        public void RaiseToolbar()
        {
            if (!IsVisible)
            {
                return;
            }
            if (!m_Toolbar.IsVisible)
            {
                m_Toolbar.Show();
            }
            WindowHelper.SetTopMostSelectable(m_Toolbar);
        }

        // Not Window.Close: the editor is hidden and reused
        public void CloseEditor()
        {
            m_IsOpen = false;
            if (!IsVisible)
            {
                return;
            }
            ShowGuides(new Guide[0]);
            m_Toolbar.Hide();
            Hide();
            WindowHelper.BringToForeground(m_ReturnFocusTo);
            m_ReturnFocusTo = IntPtr.Zero;
        }

        // At most one vertical and one horizontal guide: the snap that won on each axis
        public void ShowGuides(IEnumerable<Guide> guides)
        {
            var vertical = guides.Where(g => g.IsVertical).Select(g => (double?)g.Position).FirstOrDefault();
            var horizontal = guides.Where(g => !g.IsVertical).Select(g => (double?)g.Position).FirstOrDefault();
            Place(m_GuideX, vertical, true);
            Place(m_GuideY, horizontal, false);
        }

        void Place(Window line, double? position, bool vertical)
        {
            if (position == null)
            {
                if (line.IsVisible) line.Hide();
                return;
            }
            if (vertical) line.Left = position.Value; else line.Top = position.Value;
            if (!line.IsVisible) line.Show();
            WindowHelper.SetTopMostTransparent(line); // above the widget being dragged
        }
    }
}
