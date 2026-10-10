using System;
using System.Text.RegularExpressions;
using System.Windows.Input;

namespace SmartHunter.Game
{
    // A settings row waiting for the next key press to rebind a shortcut. The global keyboard hook delivers it, so
    // it works whatever window has focus.
    public static class KeyBinder
    {
        public static Action<Key> Pending;

        public static string KeyName(Key key)
        {
            switch (key)
            {
                case Key.Scroll: return "Scroll Lock";
                case Key.Capital: return "Caps Lock";
                case Key.Return: return "Enter";
                case Key.Prior: return "Page Up";
                case Key.Next: return "Page Down";
                case Key.Oem3: return "`";
            }
            if (key >= Key.D0 && key <= Key.D9)
            {
                return ((int)(key - Key.D0)).ToString();
            }
            // "LeftAlt" -> "Left Alt", "NumPad5" -> "Num Pad 5"
            return Regex.Replace(key.ToString(), "(?<=[a-z])(?=[A-Z0-9])", " ");
        }
    }
}
