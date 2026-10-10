using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Media;
using SmartHunter.Core.Data;

namespace SmartHunter.Game.Data
{
    public class SharpnessSegment
    {
        public Brush Brush { get; set; }
        public double Width { get; set; }
    }

    // Weapon sharpness as the game draws it: colour bands up to the weapon's current cap.
    // Thresholds are cumulative upper bounds per colour (red..purple), as in HunterPie's MHWGameUtils.
    public class Sharpness : Bindable
    {
        public const double BarWidth = 150;

        static readonly Color[] Colors =
        {
            Color.FromRgb(0xD9, 0x4A, 0x45), Color.FromRgb(0xEE, 0x8A, 0x2E), Color.FromRgb(0xEC, 0xCF, 0x32), Color.FromRgb(0x6C, 0xC2, 0x4A),
            Color.FromRgb(0x3A, 0x8D, 0xDE), Color.FromRgb(0xF2, 0xF2, 0xF2), Color.FromRgb(0xB2, 0x66, 0xE0),
        };
        static readonly string[] Names = { "Red", "Orange", "Yellow", "Green", "Blue", "White", "Purple" };

        public ObservableCollection<SharpnessSegment> Segments { get; } = new ObservableCollection<SharpnessSegment>();

        bool m_IsAvailable;
        public bool IsAvailable { get { return m_IsAvailable; } set { SetProperty(ref m_IsAvailable, value); } }

        int m_HitsLeft;
        public int HitsLeft { get { return m_HitsLeft; } set { SetProperty(ref m_HitsLeft, value); } }

        string m_LevelName;
        public string LevelName { get { return m_LevelName; } set { SetProperty(ref m_LevelName, value); } }

        Brush m_LevelBrush = Brushes.White;
        public Brush LevelBrush { get { return m_LevelBrush; } set { SetProperty(ref m_LevelBrush, value); } }

        double m_FilledWidth;
        public double FilledWidth { get { return m_FilledWidth; } set { SetProperty(ref m_FilledWidth, value); } }

        // Dropped below the best colour this weapon can reach: you're losing damage, sharpen
        bool m_NeedsSharpening;
        public bool NeedsSharpening { get { return m_NeedsSharpening; } set { SetProperty(ref m_NeedsSharpening, value); } }

        // Still on the best colour but under a third of it left
        bool m_IsLow;
        public bool IsLow { get { return m_IsLow; } set { SetProperty(ref m_IsLow, value); } }

        string m_Key;
        string m_WeaponKey;
        int m_ObservedCap;
        int m_LastCurrent = -1;

        public void Update(int[] thresholds, int current, int cap)
        {
            // Garbage reads (menus, loading, weapon swap) draw a lone red band: bands must climb and the cap must cover the first one
            var bands = thresholds.TakeWhile(t => t > 0).ToArray();
            bool climbing = bands.Zip(bands.Skip(1), (a, b) => b >= a).All(ok => ok);
            if (bands.Length < 2 || !climbing || bands.Length != thresholds.Count(t => t > 0) || cap <= thresholds[0] || cap > 1000 || current < 0 || current > 1000)
            {
                IsAvailable = false;
                return;
            }

            // The cap read from memory can count sharpness this weapon only reaches with more Handicraft, which made
            // a green-capped weapon say "sharpen" on green. Sharpening (or a quest starting) refills to the real
            // maximum, so the highest value seen right after a refill is the cap that counts. The first read counts too:
            // otherwise an unupgraded yellow weapon said "sharpen" all through the first quest, until the first whetstone.
            // ponytail: starting Aether mid-quest on dulled sharpness underestimates the cap until the next sharpen.
            // The game's cap is in the key so a Handicraft change (new armor) starts over instead of keeping a cap that's too high.
            string weaponKey = string.Join(",", thresholds) + "|" + cap;
            if (weaponKey != m_WeaponKey)
            {
                m_WeaponKey = weaponKey;
                m_ObservedCap = 0;
                m_LastCurrent = -1;
            }
            if ((m_LastCurrent < 0 || current > m_LastCurrent) && current <= cap)
            {
                m_ObservedCap = Math.Max(m_ObservedCap, current);
            }
            m_LastCurrent = current;
            if (m_ObservedCap > thresholds[0])
            {
                cap = m_ObservedCap;
            }

            int top = TopLevel(thresholds, cap);
            int level = LevelOf(thresholds, current);
            int start = level == 0 ? 0 : thresholds[level - 1];
            int end = Math.Min(thresholds[level], cap);

            string key = string.Join(",", thresholds) + "|" + cap;
            if (key != m_Key)
            {
                m_Key = key;
                Segments.Clear();
                int previous = 0;
                for (int i = 0; i <= top; i++)
                {
                    int bandEnd = Math.Min(thresholds[i], cap);
                    if (bandEnd > previous)
                    {
                        Segments.Add(new SharpnessSegment { Brush = Frozen(Colors[i]), Width = (bandEnd - previous) * BarWidth / cap });
                    }
                    previous = bandEnd;
                }
            }

            HitsLeft = Math.Max(0, current - start);
            LevelName = Names[level];
            LevelBrush = Frozen(Colors[level]);
            FilledWidth = Math.Min(current, cap) * BarWidth / cap;
            NeedsSharpening = level < top;
            IsLow = !NeedsSharpening && end > start && (current - start) < (end - start) / 3.0;
            IsAvailable = true;
        }

        public static int LevelOf(int[] thresholds, int current)
        {
            for (int i = 0; i < thresholds.Length; i++)
            {
                if (thresholds[i] > 0 && current <= thresholds[i])
                {
                    return i;
                }
            }
            return Array.FindLastIndex(thresholds, t => t > 0);
        }

        public static int TopLevel(int[] thresholds, int cap)
        {
            for (int i = 0; i < thresholds.Length; i++)
            {
                if (thresholds[i] <= 0 || thresholds[i] >= cap)
                {
                    return thresholds[i] <= 0 ? Math.Max(0, i - 1) : i;
                }
            }
            return thresholds.Length - 1;
        }

        static Brush Frozen(Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }
    }
}
