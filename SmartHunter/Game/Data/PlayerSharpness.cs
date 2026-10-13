using System;
using System.Collections.ObjectModel;
using System.Windows.Media;
using SmartHunter.Core.Data;

namespace SmartHunter.Game.Data
{
    // One colour of a hunter's sharpness: a pip in the Team widget that fills from the left
    public class SharpnessPip : Bindable
    {
        public Brush Brush { get; set; }

        double m_Fill;
        public double Fill
        {
            get { return m_Fill; }
            set { SetProperty(ref m_Fill, value); }
        }
    }

    // A hunter's sharpness as the Team widget shows it under their name: one pip per colour up to the best this weapon
    // reaches, full up to the current colour, partly full on it, empty above. Everything is built from the packed number
    // that party sync carries (SharpnessShare), so a teammate's row and your own are drawn from the same thing.
    public class PlayerSharpness : Bindable
    {
        public ObservableCollection<SharpnessPip> Pips { get; } = new ObservableCollection<SharpnessPip>();

        bool m_IsAvailable;
        public bool IsAvailable
        {
            get { return m_IsAvailable; }
            private set { SetProperty(ref m_IsAvailable, value); }
        }

        // Dropped below the best colour this weapon reaches: time to sharpen
        bool m_NeedsSharpening;
        public bool NeedsSharpening
        {
            get { return m_NeedsSharpening; }
            private set { SetProperty(ref m_NeedsSharpening, value); }
        }

        string m_Tip;
        public string Tip
        {
            get { return m_Tip; }
            private set { SetProperty(ref m_Tip, value); }
        }

        public void Apply(int packed)
        {
            if (!SharpnessShare.TryUnpack(packed, out bool available, out int top, out int level, out int percent, out int hits) || !available)
            {
                IsAvailable = false;
                NeedsSharpening = false;
                return;
            }

            // Same number of colours: change the fills in place, so the bars glide instead of being rebuilt
            if (Pips.Count != top + 1)
            {
                Pips.Clear();
                for (int i = 0; i <= top; i++)
                {
                    Pips.Add(new SharpnessPip { Brush = Sharpness.BrushOf(i) });
                }
            }
            for (int i = 0; i < Pips.Count; i++)
            {
                Pips[i].Fill = i < level ? 1 : i == level ? percent / 100.0 : 0;
            }

            NeedsSharpening = level < top;
            Tip = level < top ? $"{Sharpness.NameOf(level)}, best is {Sharpness.NameOf(top)}: time to sharpen" : $"{Sharpness.NameOf(level)}, {hits} hits left";
            IsAvailable = true;
        }
    }

    // How sharpness travels with party sync. The server stores a number per name and hands the whole table back to
    // everyone, and ignores what a number means, so a hunter's sharpness goes in under "~sharp~<name>" as one packed
    // number. Older Aether versions only look up their teammates' names, so they pass over these entries.
    public static class SharpnessShare
    {
        public const string KeyPrefix = "~sharp~";

        // Always set, so the packed number is never 0 (the server skips a missing value)
        const int Marker = 1 << 30;
        // Nothing to show: not on a melee weapon, in town, quest over
        public const int Unavailable = Marker;

        public static int Pack(bool available, int top, int level, int percent, int hits)
        {
            if (!available)
            {
                return Unavailable;
            }
            top = Math.Max(0, Math.Min(6, top));
            level = Math.Max(0, Math.Min(6, level));
            percent = Math.Max(0, Math.Min(100, percent));
            hits = Math.Max(0, Math.Min(1023, hits));
            return Marker | (1 << 29) | (top << 26) | (level << 23) | (percent << 16) | hits;
        }

        public static bool TryUnpack(int packed, out bool available, out int top, out int level, out int percent, out int hits)
        {
            available = (packed & (1 << 29)) != 0;
            top = (packed >> 26) & 7;
            level = (packed >> 23) & 7;
            percent = (packed >> 16) & 127;
            hits = packed & 1023;
            return (packed & Marker) != 0 && packed > 0 && top <= 6 && level <= top && percent <= 100;
        }
    }

    // Decides when a hunter's sharpness is worth showing and sending: when it's first known, when it appears or goes away,
    // on every change of colour, and after a handful of hits. Not on every hit: a bar that twitches on each swing is
    // noise, and so is a request each time.
    public class SharpnessPublisher
    {
        public const int HitStep = 8;

        bool m_HasLast;
        bool m_LastAvailable;
        int m_LastTop, m_LastLevel, m_LastCurrent;

        public bool ShouldPublish(bool available, int top, int level, int current)
        {
            bool publish =
                !m_HasLast
                || available != m_LastAvailable
                || (available && (top != m_LastTop || level != m_LastLevel || Math.Abs(current - m_LastCurrent) >= HitStep));
            if (publish)
            {
                m_HasLast = true;
                m_LastAvailable = available;
                m_LastTop = top;
                m_LastLevel = level;
                m_LastCurrent = current;
            }
            return publish;
        }
    }
}
