using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using SmartHunter.Core.Data;
using SmartHunter.Core.Helpers;

namespace SmartHunter.Core.Windows
{
    public abstract class WidgetWindow : Window
    {
        // Every widget window XAML pads its content with Margin="25" so the drag area is a bit larger than what you see
        const double ContentInset = 25;
        const double SnapDistance = 12;
        const double ScreenMargin = 24;
        const double Gap = 12;

        public Widget Widget { get; private set; }

        protected abstract float ScaleMax { get; }
        protected abstract float ScaleMin { get; }
        protected abstract float ScaleStep { get; }

        bool m_IsDragging;
        Point m_DragStartMouse;
        Point m_DragStartWindow;

        public WidgetWindow(Widget widget)
        {
            Widget = widget;
            LocationChanged += (s, e) => UpdateSide();
            SizeChanged += (s, e) =>
            {
                // Right-side widgets grow to the left, so their outer edge stays where you put it
                if (Widget.IsRightAligned && !m_IsDragging && e.WidthChanged && e.PreviousSize.Width > 0)
                {
                    Left -= e.NewSize.Width - e.PreviousSize.Width;
                }
                UpdateSide();
            };
        }

        void UpdateSide()
        {
            if (ActualWidth > 0)
            {
                Widget.IsRightAligned = Left + ActualWidth / 2 > SystemParameters.PrimaryScreenWidth / 2;
            }
        }

        Rect ContentRect(double left, double top)
        {
            return new Rect(left + ContentInset, top + ContentInset, Math.Max(0, ActualWidth - ContentInset * 2), Math.Max(0, ActualHeight - ContentInset * 2));
        }

        Point MouseOnScreen(MouseEventArgs e)
        {
            var point = PointToScreen(e.GetPosition(this));
            var source = PresentationSource.FromVisual(this);
            return source?.CompositionTarget != null ? source.CompositionTarget.TransformFromDevice.Transform(point) : point;
        }

        protected void WidgetWindow_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left)
            {
                return;
            }

            if (e.ClickCount == 2)
            {
                Widget.Scale = 1;
                return;
            }

            m_IsDragging = true;
            m_DragStartMouse = MouseOnScreen(e);
            m_DragStartWindow = new Point(Left, Top);
            CaptureMouse();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!m_IsDragging)
            {
                return;
            }

            var mouse = MouseOnScreen(e);
            double left = m_DragStartWindow.X + mouse.X - m_DragStartMouse.X;
            double top = m_DragStartWindow.Y + mouse.Y - m_DragStartMouse.Y;

            // Hold Shift to place freely
            var guides = new List<Guide>();
            if (!Keyboard.IsKeyDown(Key.LeftShift) && !Keyboard.IsKeyDown(Key.RightShift))
            {
                Snap(ref left, ref top, guides);
            }

            // Never lose a widget off screen
            var content = ContentRect(left, top);
            // Virtual screen can start at negative coordinates (monitor left of / above the primary one)
            left = Clamp(left, SystemParameters.VirtualScreenLeft - ContentInset, SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - content.Width - ContentInset);
            top = Clamp(top, SystemParameters.VirtualScreenTop - ContentInset, SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - content.Height - ContentInset);

            Left = left;
            Top = top;
            GuideWindow.Instance.Show(guides);
        }

        protected override void OnMouseUp(MouseButtonEventArgs e)
        {
            base.OnMouseUp(e);
            EndDrag();
        }

        protected override void OnLostMouseCapture(MouseEventArgs e)
        {
            base.OnLostMouseCapture(e);
            EndDrag();
        }

        void EndDrag()
        {
            if (!m_IsDragging)
            {
                return;
            }

            m_IsDragging = false;
            ReleaseMouseCapture();
            GuideWindow.Instance.Hide();
        }

        static double Clamp(double value, double min, double max) => Math.Max(min, Math.Min(max, value));

        // Snap the visible content to the screen margins and centre lines, and to the edges of the other widgets
        void Snap(ref double left, ref double top, List<Guide> guides)
        {
            var me = ContentRect(left, top);
            double screenW = SystemParameters.PrimaryScreenWidth;
            double screenH = SystemParameters.PrimaryScreenHeight;

            // Candidate x positions for the content's left edge -> guide line x
            var xs = new List<(double target, double line)>
            {
                (ScreenMargin, ScreenMargin),
                (screenW - ScreenMargin - me.Width, screenW - ScreenMargin),
                ((screenW - me.Width) / 2, screenW / 2),
            };
            var ys = new List<(double target, double line)>
            {
                (ScreenMargin, ScreenMargin),
                (screenH - ScreenMargin - me.Height, screenH - ScreenMargin),
                ((screenH - me.Height) / 2, screenH / 2),
            };

            foreach (var other in Application.Current.Windows.OfType<WidgetWindow>().Where(w => w != this && w.IsVisible && w.ActualWidth > 0))
            {
                var r = other.ContentRect(other.Left, other.Top);
                if (r.Width <= 0 || r.Height <= 0) continue;

                xs.Add((r.Left, r.Left));                       // align left edges
                xs.Add((r.Right - me.Width, r.Right));          // align right edges
                xs.Add((r.Right + Gap, r.Right + Gap));         // sit to the right
                xs.Add((r.Left - Gap - me.Width, r.Left - Gap)); // sit to the left
                ys.Add((r.Top, r.Top));
                ys.Add((r.Bottom - me.Height, r.Bottom));
                ys.Add((r.Bottom + Gap, r.Bottom + Gap));
                ys.Add((r.Top - Gap - me.Height, r.Top - Gap));
            }

            var bestX = xs.OrderBy(c => Math.Abs(c.target - me.Left)).First();
            if (Math.Abs(bestX.target - me.Left) <= SnapDistance)
            {
                left += bestX.target - me.Left;
                guides.Add(new Guide(true, bestX.line));
            }

            var bestY = ys.OrderBy(c => Math.Abs(c.target - me.Top)).First();
            if (Math.Abs(bestY.target - me.Top) <= SnapDistance)
            {
                top += bestY.target - me.Top;
                guides.Add(new Guide(false, bestY.line));
            }
        }

        protected void WidgetWindow_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            // Ctrl for fine steps
            float step = Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl) ? ScaleStep / 4 : ScaleStep;
            float scale = Widget.Scale + step * Math.Sign(e.Delta);
            Widget.Scale = (float)Math.Round(Math.Min(Math.Max(scale, ScaleMin), ScaleMax), 3);
        }
    }

    public struct Guide
    {
        public readonly bool IsVertical;
        public readonly double Position;
        public Guide(bool isVertical, double position) { IsVertical = isVertical; Position = position; }
    }

    // A click-through full-screen layer that draws alignment guides while a widget is being dragged
    public class GuideWindow : Window
    {
        public static readonly GuideWindow Instance = new GuideWindow();

        readonly Canvas m_Canvas = new Canvas();
        readonly Brush m_Brush = new SolidColorBrush(Color.FromArgb(0xCC, 0xF5, 0xA5, 0x24));

        GuideWindow()
        {
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            ShowActivated = false;
            Topmost = true;
            Left = SystemParameters.VirtualScreenLeft;
            Top = SystemParameters.VirtualScreenTop;
            Width = SystemParameters.VirtualScreenWidth;
            Height = SystemParameters.VirtualScreenHeight;
            Content = m_Canvas;
            IsHitTestVisible = false;
            SourceInitialized += (s, e) => WindowHelper.SetTopMostTransparent(this);
            m_Brush.Freeze();
        }

        public void Show(IEnumerable<Guide> guides)
        {
            m_Canvas.Children.Clear();
            foreach (var guide in guides)
            {
                var line = new Line
                {
                    Stroke = m_Brush,
                    StrokeThickness = 1,
                    StrokeDashArray = new DoubleCollection { 4, 4 },
                    SnapsToDevicePixels = true,
                };
                if (guide.IsVertical)
                {
                    line.X1 = line.X2 = guide.Position - Left;
                    line.Y2 = Height;
                }
                else
                {
                    line.Y1 = line.Y2 = guide.Position - Top;
                    line.X2 = Width;
                }
                m_Canvas.Children.Add(line);
            }

            if (!IsVisible)
            {
                base.Show();
            }
        }

        public new void Hide()
        {
            m_Canvas.Children.Clear();
            base.Hide();
        }
    }
}
