using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Input;
using System.Windows.Threading;

namespace SmartHunter.Core
{
    public class KeyboardInput
    {
        IntPtr m_User32Handle;
        IntPtr m_KeyboardHookHandle;

        WindowsApi.HookProc m_KeyboardHook;

        public event EventHandler<KeyboardInputEventArgs> InputReceived;

        // The hook lives on its own thread. Windows holds every keystroke (the game's too) until a low-level hook returns,
        // so it must never wait behind the UI thread's memory reads and rendering. Handlers still run on the UI thread.
        readonly Dispatcher m_UiDispatcher;
        Dispatcher m_HookDispatcher;

        public KeyboardInput()
        {
            m_UiDispatcher = Dispatcher.CurrentDispatcher;
            var ready = new ManualResetEventSlim();
            var thread = new Thread(() =>
            {
                m_HookDispatcher = Dispatcher.CurrentDispatcher;
                Hook();
                ready.Set();
                Dispatcher.Run();
            })
            { IsBackground = true, Name = "KeyboardHook" };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            ready.Wait();

            m_UiDispatcher.ShutdownStarted += (s, e) => m_HookDispatcher.BeginInvoke(new Action(() =>
            {
                Unhook();
                m_HookDispatcher.InvokeShutdown();
            }));
        }

        void Hook()
        {
            m_KeyboardHookHandle = IntPtr.Zero;
            m_User32Handle = IntPtr.Zero;
            m_KeyboardHook = KeyboardHook;

            m_User32Handle = WindowsApi.LoadLibrary("User32");
            if (m_User32Handle != IntPtr.Zero)
            {
                m_KeyboardHookHandle = WindowsApi.SetWindowsHookEx((int)WindowsApi.WindowsHook.WH_KEYBOARD_LL, m_KeyboardHook, m_User32Handle, 0);
            }
        }

        void Unhook()
        {
            if (m_KeyboardHookHandle != IntPtr.Zero)
            {
                WindowsApi.UnhookWindowsHookEx(m_KeyboardHookHandle);
                m_KeyboardHookHandle = IntPtr.Zero;
            }

            if (m_User32Handle != IntPtr.Zero)
            {
                WindowsApi.FreeLibrary(m_User32Handle);
                m_User32Handle = IntPtr.Zero;
            }
        }

        static Key KeyFromVirtualCode(uint virtualCode)
        {
            return KeyInterop.KeyFromVirtualKey((int)virtualCode);
        }

        static int VirtualCodeFromKey(Key key)
        {
            return KeyInterop.VirtualKeyFromKey(key);
        }

        IntPtr KeyboardHook(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                var keyboardMessage = (WindowsApi.KeyboardMessage)wParam.ToInt32();
                var keyboardData = (WindowsApi.KBDLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(WindowsApi.KBDLLHOOKSTRUCT));

                var key = KeyFromVirtualCode(keyboardData.VkCode);
                bool isDown = keyboardMessage == WindowsApi.KeyboardMessage.WM_KEYDOWN || keyboardMessage == WindowsApi.KeyboardMessage.WM_SYSKEYDOWN;

                var args = new KeyboardInputEventArgs(key, isDown);
                m_UiDispatcher.BeginInvoke(new Action(() => InputReceived?.Invoke(this, args)));
            }

            return WindowsApi.CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
        }
    }
}