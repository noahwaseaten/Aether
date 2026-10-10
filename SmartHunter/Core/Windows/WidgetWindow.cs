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

        // The mouse in screen DIPs, the unit Left and Top use
        public Point PointToScreenDips(MouseEventArgs e)
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
                PlacementChanged?.Invoke();
                return;
            }

            m_IsDragging = true;
            m_DragStartMouse = PointToScreenDips(e);
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

            var mouse = PointToScreenDips(e);
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
            LayoutEditor.Instance.ShowGuides(guides);
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
            LayoutEditor.Instance.ShowGuides(new Guide[0]);
            PlacementChanged?.Invoke();
        }

        // Saved right away: saving only on leaving edit mode lost the layout whenever Aether closed or restarted
        // for an update while you were still editing
        public static event Action PlacementChanged;

        // The hide button on the edit tab; the overlay hides the window and saves
        public static event Action<WidgetWindow> HideRequested;
        public void RequestHide() => HideRequested?.Invoke(this);

        // Corner grip: the widget grows with the drag along its diagonal, so the grip stays under the mouse
        float m_ResizeStartScale;
        Size m_ResizeStartSize;

        public void BeginResize()
        {
            m_ResizeStartScale = Widget.Scale;
            var content = ContentRect(Left, Top);
            m_ResizeStartSize = new Size(Math.Max(1, content.Width), Math.Max(1, content.Height));
        }

        public void ResizeBy(double dx, double dy)
        {
            double factor = (m_ResizeStartSize.Width + dx + m_ResizeStartSize.Height + dy) / (m_ResizeStartSize.Width + m_ResizeStartSize.Height);
            float scale = (float)Math.Round(m_ResizeStartScale * factor, 2);
            Widget.Scale = Math.Min(Math.Max(scale, ScaleMin), ScaleMax);
        }

        public void EndResize() => PlacementChanged?.Invoke();

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
                xs.Add((r.Left + (r.Width - me.Width) / 2, r.Left + r.Width / 2)); // centred on it
                ys.Add((r.Top, r.Top));
                ys.Add((r.Bottom - me.Height, r.Bottom));
                ys.Add((r.Bottom + Gap, r.Bottom + Gap));
                ys.Add((r.Top - Gap - me.Height, r.Top - Gap));
                ys.Add((r.Top + (r.Height - me.Height) / 2, r.Top + r.Height / 2));
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
            PlacementChanged?.Invoke();
        }
    }

    public struct Guide
    {
        public readonly bool IsVertical;
        public readonly double Position;
        public Guide(bool isVertical, double position) { IsVertical = isVertical; Position = position; }
    }
}
