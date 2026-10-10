using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace SmartHunter.Core.Helpers
{
    public static class WindowHelper
    {
        static uint TopMostWindowSizePositions
        {
            get
            {
                return (uint)WindowsApi.WindowSizePositionFlag.SWP_NOMOVE
                    | (uint)WindowsApi.WindowSizePositionFlag.SWP_NOSIZE
                    | (uint)WindowsApi.WindowSizePositionFlag.SWP_SHOWWINDOW
                    | (uint)WindowsApi.WindowSizePositionFlag.SWP_NOACTIVATE;
            }
        }

        static uint TopMostSelectableWindowStyleFlags
        {
            get
            {
                // No activation: clicking a widget or the editor while editing leaves the game focused, so "hide when
                // the game isn't focused" doesn't make the overlay vanish once you're done
                return ((uint)WindowsApi.WindowStyleFlag.WS_EX_LAYERED
                    | (uint)WindowsApi.WindowStyleFlag.WS_EX_TOPMOST
                    | (uint)WindowsApi.WindowStyleFlag.WS_EX_TOOLWINDOW
                    | (uint)WindowsApi.WindowStyleFlag.WS_EX_NOACTIVATE)
                    & ~(uint)WindowsApi.WindowStyleFlag.WS_EX_APPWINDOW;
            }
        }

        static uint TopMostTransparentWindowStyleFlags
        {
            get
            {
                return ((uint)WindowsApi.WindowStyleFlag.WS_EX_LAYERED
                    | (uint)WindowsApi.WindowStyleFlag.WS_EX_TRANSPARENT
                    | (uint)WindowsApi.WindowStyleFlag.WS_EX_TOPMOST
                    | (uint)WindowsApi.WindowStyleFlag.WS_EX_TOOLWINDOW)
                    & ~(uint)WindowsApi.WindowStyleFlag.WS_EX_APPWINDOW;
                ;
            }
        }

        // The global keyboard hook keeps firing while the app shuts down; a closed window has no handle to give
        static bool TryGetHandle(Window window, out IntPtr handle)
        {
            try
            {
                handle = new WindowInteropHelper(window).EnsureHandle();
                return true;
            }
            catch (InvalidOperationException)
            {
                handle = IntPtr.Zero;
                return false;
            }
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct DEVMODE
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
            public short dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
            public int dmFields, dmPositionX, dmPositionY, dmDisplayOrientation, dmDisplayFixedOutput;
            public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
            public short dmLogPixels;
            public int dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency;
            public int dmICMMethod, dmICMIntent, dmMediaType, dmDitherType, dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct MONITORINFOEX
        {
            public int cbSize;
            public int MonitorLeft, MonitorTop, MonitorRight, MonitorBottom, WorkLeft, WorkTop, WorkRight, WorkBottom;
            public int dwFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szDevice;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern bool EnumDisplaySettingsW(string deviceName, int modeNum, ref DEVMODE devMode);
        [DllImport("user32.dll")]
        static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern bool GetMonitorInfoW(IntPtr monitor, ref MONITORINFOEX info);

        // Refresh rate of the monitor showing this element (the primary one if it isn't on screen yet), so the
        // Aether window animates at 60, 144 or 240 fps to match. 60 when Windows won't say.
        public static int RefreshRate(System.Windows.Media.Visual visual = null)
        {
            try
            {
                string device = null;
                var source = visual == null ? null : PresentationSource.FromVisual(visual) as HwndSource;
                if (source != null)
                {
                    var info = new MONITORINFOEX { cbSize = Marshal.SizeOf(typeof(MONITORINFOEX)) };
                    if (GetMonitorInfoW(MonitorFromWindow(source.Handle, 2 /* MONITOR_DEFAULTTONEAREST */), ref info))
                        device = info.szDevice;
                }
                var mode = new DEVMODE { dmSize = (short)Marshal.SizeOf(typeof(DEVMODE)) };
                if (EnumDisplaySettingsW(device, -1 /* ENUM_CURRENT_SETTINGS */, ref mode) && mode.dmDisplayFrequency > 1)
                    return Math.Min(Math.Max(mode.dmDisplayFrequency, 30), 500);
            }
            catch (Exception)
            {
            }
            return 60;
        }

        public static int PrimaryRefreshRate { get; } = RefreshRate();

        public static void SetTopMostSelectable(Window window)
        {
            if (!TryGetHandle(window, out var handle))
            {
                return;
            }
            WindowsApi.SetWindowLong(handle, (int)WindowsApi.WindowLongGroup.GWL_EXSTYLE, TopMostSelectableWindowStyleFlags);
            WindowsApi.SetWindowPos(handle, -1, 0, 0, 0, 0, TopMostWindowSizePositions);
        }

        // Topmost and clickable, and it can take focus: the layout editor's backdrop has to, or the game keeps the
        // cursor pinned to the middle of the screen
        public static void SetTopMostFocusable(Window window)
        {
            if (!TryGetHandle(window, out var handle))
            {
                return;
            }
            WindowsApi.SetWindowLong(handle, (int)WindowsApi.WindowLongGroup.GWL_EXSTYLE, TopMostSelectableWindowStyleFlags & ~(uint)WindowsApi.WindowStyleFlag.WS_EX_NOACTIVATE);
            WindowsApi.SetWindowPos(handle, -1, 0, 0, 0, 0, TopMostWindowSizePositions);
        }

        // Windows only lets the app the user is using bring a window forward. Sharing the foreground window's input
        // queue for a moment makes the request count as coming from it.
        public static void BringToForeground(IntPtr handle)
        {
            if (handle == IntPtr.Zero || !WindowsApi.IsWindow(handle))
            {
                return;
            }
            uint foregroundThread = WindowsApi.GetWindowThreadProcessId(WindowsApi.GetForegroundWindow(), IntPtr.Zero);
            uint thisThread = WindowsApi.GetCurrentThreadId();
            bool attached = foregroundThread != 0 && foregroundThread != thisThread && WindowsApi.AttachThreadInput(thisThread, foregroundThread, true);
            WindowsApi.SetForegroundWindow(handle);
            if (attached)
            {
                WindowsApi.AttachThreadInput(thisThread, foregroundThread, false);
            }
        }

        public static void SetTopMostTransparent(Window window)
        {
            if (!TryGetHandle(window, out var handle))
            {
                return;
            }
            WindowsApi.SetWindowLong(handle, (int)WindowsApi.WindowLongGroup.GWL_EXSTYLE, TopMostTransparentWindowStyleFlags);
            WindowsApi.SetWindowPos(handle, -1, 0, 0, 0, 0, TopMostWindowSizePositions);
        }

        // Experimental function to set the window background to use Windows 10 acrylic - this part of the win api is poorly documented
        public static void EnableAcrylic(Window window)
        {
            var handle = new WindowInteropHelper(window).EnsureHandle();

            var accent = new WindowsApi.AccentPolicy();
            accent.GradientColor = 0x01000000;

            accent.AccentState = WindowsApi.AccentState.ACCENT_ENABLE_ACRYLICBLURBEHIND;

            var accentStructSize = Marshal.SizeOf(accent);

            var accentPtr = Marshal.AllocHGlobal(accentStructSize);
            Marshal.StructureToPtr(accent, accentPtr, false);

            var data = new WindowsApi.WindowCompositionAttributeData();
            data.Attribute = WindowsApi.WindowCompositionAttribute.WCA_ACCENT_POLICY;
            data.SizeOfData = accentStructSize;
            data.Data = accentPtr;

            WindowsApi.SetWindowCompositionAttribute(handle, ref data);

            Marshal.FreeHGlobal(accentPtr);
        }
    }
}
