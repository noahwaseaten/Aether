using System;
using System.Collections.ObjectModel;
using SmartHunter.Core.Data;

namespace SmartHunter.Game.Data
{
    public class Player : Bindable, IComparable<Player>
    {
        int m_Index;

        public int Index
        {
            get { return m_Index; }
            set { SetProperty(ref m_Index, value); }
        }

        string m_Name;
        public string Name
        {
            get { return m_Name; }
            set
            {
                // Someone else took this slot: the badge was for the hunter who left
                if (m_Name != value)
                {
                    UsesAether = false;
                }
                SetProperty(ref m_Name, value);
            }
        }

        // Party sync heard from this hunter's Aether. Stays on between quests, unlike HasSyncedDamage.
        bool m_UsesAether;
        public bool UsesAether
        {
            get { return m_UsesAether; }
            set { SetProperty(ref m_UsesAether, value); }
        }

        // Leads the quest party (slot 0 of the game's party list), whether or not they run Aether
        bool m_IsLeader;
        public bool IsLeader
        {
            get { return m_IsLeader; }
            set { SetProperty(ref m_IsLeader, value); }
        }

        // The game's own counter (quest targets only), and whether party sync sent this hunter's all-monster total
        public int GameDamage;
        public bool HasSyncedDamage;

        // No number at all: on expeditions only this hunter's own Aether knows their damage
        bool m_IsDamageUnknown;
        public bool IsDamageUnknown
        {
            get { return m_IsDamageUnknown; }
            set { SetProperty(ref m_IsDamageUnknown, value); }
        }

        // Quest targets only: counting every monster, but this hunter's Aether isn't sending their full total
        bool m_IsDamagePartial;
        public bool IsDamagePartial
        {
            get { return m_IsDamagePartial; }
            set { SetProperty(ref m_IsDamagePartial, value); }
        }

        int m_Damage;
        public int Damage
        {
            get { return m_Damage; }
            set
            {
                if (m_Damage != value)
                {
                    UpdateDamagePoint(value);
                }
                if (value > m_Damage)
                {
                    LastHitTime = DateTime.Now;
                }

                SetProperty(ref m_Damage, value);
            }
        }

        float m_DamageFraction;
        public float DamageFraction
        {
            get { return m_DamageFraction; }
            set { SetProperty(ref m_DamageFraction, value); }
        }

        float m_BarFraction;
        public float BarFraction
        {
            get { return m_BarFraction; }
            set { SetProperty(ref m_BarFraction, value); }
        }

        public DateTime LastHitTime { get; private set; }

        // Damage per second of quest time; 0 outside a quest
        float m_Dps;
        public float Dps
        {
            get { return m_Dps; }
            set { SetProperty(ref m_Dps, value); }
        }

        // Is this hunter's share of the damage rising (1), falling (-1) or steady (0) over the last few seconds
        int m_Trend;
        public int Trend
        {
            get { return m_Trend; }
            set { SetProperty(ref m_Trend, value); }
        }

        public const double TrendSeconds = 5;
        float m_TrendShare;
        DateTime m_TrendSampledAt;

        public void UpdateTrend(DateTime now)
        {
            if ((now - m_TrendSampledAt).TotalSeconds < TrendSeconds)
                return;
            Trend = m_TrendSampledAt == default(DateTime) ? 0 : TrendOf(m_TrendShare, DamageFraction);
            m_TrendShare = DamageFraction;
            m_TrendSampledAt = now;
        }

        // Under a percentage point either way is noise, not a trend: smaller steps made the arrow flicker
        public static int TrendOf(float before, float now)
        {
            float change = now - before;
            return change > 0.01f ? 1 : change < -0.01f ? -1 : 0;
        }

        // Icon key in Ui/Resources/Icons.xaml
        string m_WeaponIcon;
        public string WeaponIcon
        {
            get { return m_WeaponIcon; }
            set { SetProperty(ref m_WeaponIcon, value); }
        }

        bool m_IsMe;
        public bool IsMe
        {
            get { return m_IsMe; }
            set { SetProperty(ref m_IsMe, value); }
        }

        // True for a moment after this hunter lands damage; the skin flashes their dot
        bool m_IsHitting;
        public bool IsHitting
        {
            get { return m_IsHitting; }
            set { SetProperty(ref m_IsHitting, value); }
        }

        private void UpdateDamagePoint(int damage)
        {
            if (!SmartHunter.Game.Helpers.ConfigHelper.Main.Values.Overlay.TeamWidget.ShowChart)
            {
                return;
            }

            var timestamp = DateTime.Now.ToFileTime();
            DamagePoints.Add(new DamagePoint(timestamp, damage));
        }

        public ObservableCollection<DamagePoint> DamagePoints { get; } = new ObservableCollection<DamagePoint>();

        public int CompareTo(Player other)
        {
            if (this.Damage == 0 && other.Damage == 0)
                return 0;
            return this.Damage > other.Damage ? -1 : 1;
        }

        public override string ToString()
        {
            return String.Format("{0} {1} {2}%", this.Name, this.Damage, (this.DamageFraction * 100).ToString("0.##"));
        }
    }
}
