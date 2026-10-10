using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Media;
using SmartHunter.Core.Data;

namespace SmartHunter.Game.Data
{
    // One timer on the weapon line: a colour dot (Insect Glaive extracts, Long Sword spirit) or a short label, and m:ss
    public class GaugeTimer
    {
        public Brush Dot { get; set; }
        public string Label { get; set; }
        public string Time { get; set; }
    }

    // The compact extras in the Buffs widget: quest clock, meal timer, and the weapon timers the game's HUD only hints at.
    // Each part is null/empty when there's nothing to show, so the widget only grows when it has something to say.
    public class HuntInfo : Bindable
    {
        string m_Clock;
        public string Clock { get { return m_Clock; } set { SetProperty(ref m_Clock, value); } }

        string m_ClockLimit;
        public string ClockLimit { get { return m_ClockLimit; } set { SetProperty(ref m_ClockLimit, value); } }

        string m_Meal;
        public string Meal { get { return m_Meal; } set { SetProperty(ref m_Meal, value); } }

        public ObservableCollection<GaugeTimer> Gauge { get; } = new ObservableCollection<GaugeTimer>();

        string m_GaugeKey;

        public void SetGauge(IList<GaugeTimer> timers)
        {
            // Rebuild only when something visible changed (timers tick in whole seconds)
            string key = string.Join("|", timers.Select(t => t.Label + t.Time + t.Dot));
            if (key == m_GaugeKey)
                return;
            m_GaugeKey = key;
            Gauge.Clear();
            foreach (var timer in timers)
                Gauge.Add(timer);
        }

        public void Clear()
        {
            Clock = null;
            ClockLimit = null;
            Meal = null;
            SetGauge(new GaugeTimer[0]);
        }

        public static string Format(float seconds)
        {
            if (float.IsNaN(seconds) || seconds < 0)
                return null;
            int s = (int)Math.Ceiling(seconds);
            return $"{s / 60}:{s % 60:00}";
        }

        // The game stores the quest limit as frames, slightly under the real value: round up to the known limits
        static readonly uint[] QuestLimits = { 54000, 72000, 108000, 126000, 180000 }; // 15, 20, 30, 35, 50 min at 60 fps
        public static float QuestLimitSeconds(uint rawFrames)
        {
            uint limit = QuestLimits.FirstOrDefault(l => l >= rawFrames);
            return (limit == 0 ? rawFrames : limit) / 60f;
        }

        public static Brush Frozen(byte r, byte g, byte b)
        {
            var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
            brush.Freeze();
            return brush;
        }
    }
}
