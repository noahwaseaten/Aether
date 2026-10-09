using System.Collections.ObjectModel;
using System.Linq;
using SmartHunter.Core.Data;
using SmartHunter.Game.Helpers;

namespace SmartHunter.Game.Data.WidgetContexts
{
    public class MonsterWidgetContext : WidgetContext
    {
        public ObservableCollection<Monster> Monsters { get; private set; }

        bool m_ShowSize = false;
        public bool ShowSize
        {
            get { return m_ShowSize; }
            set { SetProperty(ref m_ShowSize, value); }
        }

        bool m_ShowCrown = true;
        public bool ShowCrown
        {
            get { return m_ShowCrown; }
            set { SetProperty(ref m_ShowCrown, value); }
        }

        bool m_ShowBars = true;
        public bool ShowBars
        {
            get { return m_ShowBars; }
            set { SetProperty(ref m_ShowBars, value); }
        }

        bool m_ShowNumbers = true;
        public bool ShowNumbers
        {
            get { return m_ShowNumbers; }
            set { SetProperty(ref m_ShowNumbers, value); }
        }

        bool m_ShowPercents = true;
        public bool ShowPercents
        {
            get { return m_ShowPercents; }
            set { SetProperty(ref m_ShowPercents, value); }
        }

        bool m_UseAnimations = true;
        public bool UseAnimations
        {
            get { return m_UseAnimations; }
            set { SetProperty(ref m_UseAnimations, value); }
        }

        bool m_AlwaysShowParts = false;
        public bool AlwaysShowParts
        {
            get { return m_AlwaysShowParts; }
            set { SetProperty(ref m_AlwaysShowParts, value); }
        }

        // Online and not the host, with no host numbers coming through the sync server. The game only refreshes a
        // client's part HP and ailment buildup every few minutes, so those rows stay hidden instead of showing stale jumps.
        bool m_WaitingForHost;
        public bool WaitingForHost
        {
            get { return m_WaitingForHost; }
            set { SetProperty(ref m_WaitingForHost, value); }
        }

        public const int MaxPartRows = 6;
        public const int MaxStatusRows = 5;

        bool m_HasVisibleMonsters;
        public bool HasVisibleMonsters
        {
            get { return m_HasVisibleMonsters; }
            set { SetProperty(ref m_HasVisibleMonsters, value); }
        }

        public MonsterWidgetContext()
        {
            Monsters = new ObservableCollection<Monster>();

            UpdateFromConfig();
        }

        public Monster UpdateAndGetMonster(ulong address, string id, float maxHealth, float currentHealth, float sizeScale, float scaleModifier)
        {
            Monster monster = Monsters.FirstOrDefault(existingMonster => existingMonster.Address == address);

            // The game reuses monster slots: a different id, or a dead entry back at full HP, is a new spawn
            if (monster != null && (monster.Id != id || (!monster.IsAlive && currentHealth >= maxHealth)))
            {
                Monsters.Remove(monster);
                monster = null;
            }

            if (monster != null)
            {
                monster.UpdateHealth(maxHealth, currentHealth);
                monster.SizeScale = sizeScale;
                monster.ScaleModifier = scaleModifier;
            }
            else
            {
                monster = new Monster(address, id, maxHealth, currentHealth, sizeScale, scaleModifier);
                Monsters.Add(monster);
            }

            monster.NotifyPropertyChanged(nameof(Monster.IsVisible));

            return monster;
        }

        // Focus: the map-pinned monster, else whoever lost HP most recently, else the only one alive
        public void UpdateFocus(ulong selectedAddress)
        {
            var alive = Monsters.Where(m => m.IsAlive).ToList();
            var focus = alive.FirstOrDefault(m => selectedAddress != 0 && m.Address == selectedAddress)
                ?? alive.Where(m => m.LastDamagedTime.HasValue).OrderByDescending(m => m.LastDamagedTime).FirstOrDefault()
                ?? (alive.Count == 1 ? alive[0] : null);

            bool onlyFocused = ConfigHelper.Main.Values.Overlay.MonsterWidget.ShowOnlySelectedMonster;
            foreach (var monster in Monsters)
            {
                monster.IsFocused = monster == focus;
                monster.IsSuppressed = onlyFocused && focus != null && monster != focus;
            }
            foreach (var monster in Monsters)
            {
                monster.RankRows();
            }
            HasVisibleMonsters = Monsters.Any(m => m.IsVisible);
        }

        public override void UpdateFromConfig()
        {
            base.UpdateFromConfig();

            ShowSize = ConfigHelper.Main.Values.Overlay.MonsterWidget.ShowSize;
            ShowCrown = ConfigHelper.Main.Values.Overlay.MonsterWidget.ShowCrown;
            ShowBars = ConfigHelper.Main.Values.Overlay.MonsterWidget.ShowBars;
            ShowNumbers = ConfigHelper.Main.Values.Overlay.MonsterWidget.ShowNumbers;
            ShowPercents = ConfigHelper.Main.Values.Overlay.MonsterWidget.ShowPercents;
            UseAnimations = ConfigHelper.Main.Values.Overlay.MonsterWidget.UseAnimations;
            AlwaysShowParts = ConfigHelper.Main.Values.Overlay.MonsterWidget.AlwaysShowParts;

            foreach (var monster in Monsters)
            {
                monster.NotifyPropertyChanged(nameof(Monster.IsVisible));
            }
        }
    }
}
