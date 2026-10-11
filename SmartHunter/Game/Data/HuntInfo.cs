using System;
using System.Linq;
using SmartHunter.Core.Data;

namespace SmartHunter.Game.Data
{
    // The quest clock (time elapsed, and the quest's limit for the Team widget) and the party's deaths. Null outside a quest.
    public class HuntInfo : Bindable
    {
        // For damage per second; 0 outside a quest
        public float ElapsedSeconds { get; set; }

        // "1/3": deaths so far and how many fail the quest
        string m_Deaths;
        public string Deaths { get { return m_Deaths; } set { SetProperty(ref m_Deaths, value); } }

        string m_Clock;
        public string Clock { get { return m_Clock; } set { SetProperty(ref m_Clock, value); } }

        string m_ClockLimit;
        public string ClockLimit { get { return m_ClockLimit; } set { SetProperty(ref m_ClockLimit, value); } }

        public void Clear()
        {
            Clock = null;
            ClockLimit = null;
            Deaths = null;
            ElapsedSeconds = 0;
        }

        public static string FormatDeaths(int deaths, int maxDeaths)
        {
            return maxDeaths > 0 && maxDeaths <= 9 && deaths >= 0 && deaths <= maxDeaths ? $"{deaths}/{maxDeaths}" : null;
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
