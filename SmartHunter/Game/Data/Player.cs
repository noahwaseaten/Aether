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
