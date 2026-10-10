using System;
using System.Linq;
using SmartHunter.Core.Data;

namespace SmartHunter.Game.Data
{
    // The quest clock at the top of the Buffs widget: time elapsed and the quest's limit. Null outside a quest.
    public class HuntInfo : Bindable
    {
        string m_Clock;
        public string Clock { get { return m_Clock; } set { SetProperty(ref m_Clock, value); } }

        string m_ClockLimit;
        public string ClockLimit { get { return m_ClockLimit; } set { SetProperty(ref m_ClockLimit, value); } }

        public void Clear()
        {
            Clock = null;
            ClockLimit = null;
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
    }
}
