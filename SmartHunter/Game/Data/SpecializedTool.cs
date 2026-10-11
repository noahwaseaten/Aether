using SmartHunter.Core.Data;

namespace SmartHunter.Game.Data
{
    // A mantle or booster in use: one tile whose bar drains while it's on and fills back up while it recharges
    public class SpecializedTool : Bindable
    {
        // The tool's name id without "RECHARGE_", shared by its two timers
        public string Key { get; }
        public string Name { get; }
        public string IconKey { get; }

        // Drains while active, fills while recharging
        public Progress Ring { get; } = new Progress(1, 0);

        float m_Seconds;
        public float Seconds
        {
            get { return m_Seconds; }
            private set { SetProperty(ref m_Seconds, value); }
        }

        bool m_IsRecharging;
        public bool IsRecharging
        {
            get { return m_IsRecharging; }
            private set { SetProperty(ref m_IsRecharging, value); }
        }

        float m_PhaseSeconds;

        public SpecializedTool(string key, string name, string iconKey)
        {
            Key = key;
            Name = name;
            IconKey = iconKey;
        }

        // ponytail: a phase's length is the first time read in it, so a tool first seen mid-recharge starts from an empty ring
        public void Update(float seconds, bool isRecharging)
        {
            if (isRecharging != IsRecharging || seconds > m_PhaseSeconds)
            {
                m_PhaseSeconds = seconds;
            }
            IsRecharging = isRecharging;
            Seconds = seconds;
            Ring.Max = m_PhaseSeconds;
            Ring.Current = isRecharging ? m_PhaseSeconds - seconds : seconds;
        }
    }
}
