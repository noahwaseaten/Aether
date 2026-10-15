using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace SmartHunter.Core.Helpers
{
    // Medal, Discord and other overlays are topmost windows too, and Windows draws the one raised last on top.
    // A widget is raised only when a topmost window from another app actually overlaps it, so nothing moves when
    // nothing covers them. Windows reports a window being shown at once; a window that only re-raises itself sends
    // no event at all, so a once-a-second look at the window order (reads only) catches that.
    public class StayOnTop
    {
        const uint EVENT_SYSTEM_FOREGROUND = 0x0003, EVENT_OBJECT_SHOW = 0x8002, EVENT_OBJECT_REORDER = 0x8004;
        const uint WINEVENT_OUTOFCONTEXT = 0x0000, WINEVENT_SKIPOWNPROCESS = 0x0002;
        const uint GW_HWNDPREV = 3;

        delegate void WinEventProc(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);

        [DllImport("user32.dll")]
        static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr module, WinEventProc proc, uint process, uint thread, uint flags);
        [DllImport("user32.dll")]
        static extern bool UnhookWinEvent(IntPtr hook);
        [DllImport("user32.dll")]
        static extern IntPtr GetWindow(IntPtr hwnd, uint cmd);
        [DllImport("user32.dll")]
        static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll")]
        static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);

        [StructLayout(LayoutKind.Sequential)]
        struct RECT { public int Left, Top, Right, Bottom; }

        readonly Func<IEnumerable<Window>> m_Windows;
        readonly Func<bool> m_IsPaused;
        readonly WinEventProc m_Proc; // kept in a field: the GC would collect a delegate only Windows holds
        readonly List<IntPtr> m_Hooks = new List<IntPtr>();
        readonly RaiseLimiter m_Limiter = new RaiseLimiter();
        bool m_CheckPending;

        public StayOnTop(Func<IEnumerable<Window>> windows, Func<bool> isPaused)
        {
            m_Windows = windows;
            m_IsPaused = isPaused;
            m_Proc = OnWinEvent;
            // Other processes only: our own raises would trigger another check
            m_Hooks.Add(SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, m_Proc, 0, 0, WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS));
            m_Hooks.Add(SetWinEventHook(EVENT_OBJECT_SHOW, EVENT_OBJECT_SHOW, IntPtr.Zero, m_Proc, 0, 0, WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS));
            m_Hooks.Add(SetWinEventHook(EVENT_OBJECT_REORDER, EVENT_OBJECT_REORDER, IntPtr.Zero, m_Proc, 0, 0, WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS));
            Application.Current.Exit += (s, e) => m_Hooks.Where(h => h != IntPtr.Zero).ToList().ForEach(h => UnhookWinEvent(h));

            var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
            timer.Tick += (s, e) => Check();
            timer.Start();
        }

        void OnWinEvent(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
        {
            // Show events fire for every tooltip and caret in every app; only whole windows count, and several
            // events in a row make one check
            if (idObject != 0 || idChild != 0 || m_CheckPending)
            {
                return;
            }
            m_CheckPending = true;
            Dispatcher.CurrentDispatcher.BeginInvoke(new Action(Check), DispatcherPriority.Background);
        }

        void Check()
        {
            m_CheckPending = false;
            if (m_IsPaused())
            {
                return;
            }
            var covered = m_Windows().Where(w => w.IsVisible && IsCovered(new WindowInteropHelper(w).Handle)).ToList();
            if (covered.Count == 0 || !m_Limiter.Allow(DateTime.UtcNow))
            {
                return;
            }
            foreach (var window in covered)
            {
                WindowHelper.RaiseTopMost(window);
            }
        }

        // A visible topmost window from another app sits above this one and overlaps it
        static bool IsCovered(IntPtr handle)
        {
            if (handle == IntPtr.Zero || !GetWindowRect(handle, out var own))
            {
                return false;
            }
            int steps = 0;
            for (var above = GetWindow(handle, GW_HWNDPREV); above != IntPtr.Zero && steps < 1000; above = GetWindow(above, GW_HWNDPREV), steps++)
            {
                if (!IsWindowVisible(above) || WindowHelper.IsOwnWindow(above)
                    || (WindowsApi.GetWindowLong(above, (int)WindowsApi.WindowLongGroup.GWL_EXSTYLE) & (int)WindowsApi.WindowStyleFlag.WS_EX_TOPMOST) == 0
                    || !GetWindowRect(above, out var r))
                {
                    continue;
                }
                if (r.Left < own.Right && own.Left < r.Right && r.Top < own.Bottom && own.Top < r.Bottom)
                {
                    return true;
                }
            }
            return false;
        }
    }

    // Two overlays that both insist on being on top would swap places forever. After 5 raises in 10 s,
    // stop for a minute and let the other one win.
    public class RaiseLimiter
    {
        public const int MaxRaises = 5;
        public static readonly TimeSpan Window = TimeSpan.FromSeconds(10), Pause = TimeSpan.FromMinutes(1);

        readonly Queue<DateTime> m_Raises = new Queue<DateTime>();
        DateTime m_PausedUntil;

        public bool Allow(DateTime now)
        {
            if (now < m_PausedUntil)
            {
                return false;
            }
            while (m_Raises.Count > 0 && now - m_Raises.Peek() > Window)
            {
                m_Raises.Dequeue();
            }
            if (m_Raises.Count >= MaxRaises)
            {
                m_Raises.Clear();
                m_PausedUntil = now + Pause;
                Log.WriteLine("Another overlay keeps covering the widgets; Aether stops raising them for a minute");
                return false;
            }
            m_Raises.Enqueue(now);
            return true;
        }
    }
}
