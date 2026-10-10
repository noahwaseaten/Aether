using System;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace SmartHunter.Core.Windows
{
    // What a widget shows while the layout is edited: an outline, a tab with its name, size and a hide button (like the
    // header bar on Discord's overlay widgets), and a corner grip that resizes it (like Lunar's HUD editor). Fades in and
    // out with edit mode. Put one last inside the widget window's root grid.
    public class EditChrome : Grid
    {
        public static readonly DependencyProperty IsEditingProperty = DependencyProperty.Register(
            nameof(IsEditing), typeof(bool), typeof(EditChrome), new PropertyMetadata(false, (d, e) => ((EditChrome)d).Fade((bool)e.NewValue)));

        public bool IsEditing
        {
            get { return (bool)GetValue(IsEditingProperty); }
            set { SetValue(IsEditingProperty, value); }
        }

        public string Title { get; set; }

        readonly TextBlock m_Title = new TextBlock { FontSize = 11, FontWeight = FontWeights.Bold };
        readonly TextBlock m_Size = new TextBlock { FontSize = 11, Margin = new Thickness(6, 0, 0, 0), Opacity = 0.7 };
        readonly Border m_Tab = new Border { CornerRadius = new CornerRadius(7), Padding = new Thickness(8, 2, 4, 3), VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(12, -12, 12, 0) };
        readonly Border m_Grip = new Border { Width = 14, Height = 14, CornerRadius = new CornerRadius(4), VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(-5), Cursor = Cursors.SizeNWSE, ToolTip = "Drag to resize" };
        WidgetWindow m_Window;
        bool m_Resizing;
        Point m_ResizeStart;

        public EditChrome()
        {
            Margin = new Thickness(-8);
            Opacity = 0;
            Visibility = Visibility.Hidden;
            SetBinding(IsEditingProperty, new Binding("CanManipulateWindows"));

            var accent = Application.Current.TryFindResource("B_Accent") as SolidColorBrush ?? new SolidColorBrush(Color.FromRgb(0xE2, 0xC2, 0x7A));
            var onAccent = new SolidColorBrush(Color.FromRgb(0x1C, 0x14, 0x00));
            onAccent.Freeze();

            Children.Add(new Border
            {
                CornerRadius = new CornerRadius(16),
                BorderThickness = new Thickness(1.5),
                BorderBrush = accent,
                Background = new SolidColorBrush(Color.FromArgb(0x14, accent.Color.R, accent.Color.G, accent.Color.B)),
                IsHitTestVisible = false,
            });

            m_Title.Foreground = m_Size.Foreground = onAccent;
            var hide = new TextBlock
            {
                Text = "", // Segoe MDL2 "Hide"
                FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
                FontSize = 11,
                Foreground = onAccent,
                Margin = new Thickness(8, 1, 2, 0),
                Padding = new Thickness(2, 0, 2, 0),
                Cursor = Cursors.Hand,
                Background = Brushes.Transparent,
                ToolTip = "Hide this widget (show it again from the toolbar)",
                VerticalAlignment = VerticalAlignment.Center,
            };
            hide.MouseLeftButtonDown += (s, e) => e.Handled = true;
            hide.MouseLeftButtonUp += (s, e) => { e.Handled = true; m_Window?.RequestHide(); };

            var tabRow = new StackPanel { Orientation = Orientation.Horizontal };
            tabRow.Children.Add(m_Title);
            tabRow.Children.Add(m_Size);
            tabRow.Children.Add(hide);
            m_Tab.Child = tabRow;
            m_Tab.Background = accent;
            Children.Add(m_Tab);

            m_Grip.Background = accent;
            m_Grip.BorderBrush = onAccent;
            m_Grip.BorderThickness = new Thickness(2);
            m_Grip.MouseLeftButtonDown += Grip_Down;
            m_Grip.MouseMove += Grip_Move;
            m_Grip.MouseLeftButtonUp += (s, e) => EndResize();
            m_Grip.LostMouseCapture += (s, e) => EndResize();
            Children.Add(m_Grip);

            Loaded += (s, e) =>
            {
                m_Window = Window.GetWindow(this) as WidgetWindow;
                if (m_Window == null)
                {
                    return;
                }
                m_Title.Text = Title ?? m_Window.Title;
                m_Size.SetBinding(TextBlock.TextProperty, new Binding(nameof(Data.Widget.Scale)) { Source = m_Window.Widget, StringFormat = "{0:0%}", ConverterCulture = CultureInfo.InvariantCulture });
                m_Window.Widget.PropertyChanged += Widget_PropertyChanged;
                UpdateSide();
            };
        }

        void Widget_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(Data.Widget.IsRightAligned))
            {
                UpdateSide();
            }
        }

        // Right-side widgets grow to the left, so their grip and tab sit on the left as well
        void UpdateSide()
        {
            bool right = m_Window?.Widget.IsRightAligned == true;
            m_Grip.HorizontalAlignment = right ? HorizontalAlignment.Left : HorizontalAlignment.Right;
            m_Grip.Cursor = right ? Cursors.SizeNESW : Cursors.SizeNWSE;
            m_Tab.HorizontalAlignment = right ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        }

        void Fade(bool show)
        {
            if (show)
            {
                Visibility = Visibility.Visible;
            }
            var animation = new DoubleAnimation(show ? 1 : 0, TimeSpan.FromMilliseconds(show ? 160 : 200));
            if (!show)
            {
                animation.Completed += (s, e) => { if (!IsEditing) Visibility = Visibility.Hidden; };
            }
            BeginAnimation(OpacityProperty, animation);
        }

        void Grip_Down(object sender, MouseButtonEventArgs e)
        {
            if (m_Window == null)
            {
                return;
            }
            e.Handled = true;
            m_Resizing = true;
            m_ResizeStart = m_Window.PointToScreenDips(e);
            m_Window.BeginResize();
            m_Grip.CaptureMouse();
        }

        void Grip_Move(object sender, MouseEventArgs e)
        {
            if (!m_Resizing)
            {
                return;
            }
            var now = m_Window.PointToScreenDips(e);
            double dx = now.X - m_ResizeStart.X;
            double dy = now.Y - m_ResizeStart.Y;
            m_Window.ResizeBy(m_Window.Widget.IsRightAligned ? -dx : dx, dy);
        }

        void EndResize()
        {
            if (!m_Resizing)
            {
                return;
            }
            m_Resizing = false;
            m_Grip.ReleaseMouseCapture();
            m_Window.EndResize();
        }
    }
}
