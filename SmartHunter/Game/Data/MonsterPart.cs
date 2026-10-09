using System.ComponentModel;
using System.Linq;
using SmartHunter.Core.Data;
using SmartHunter.Game.Helpers;

namespace SmartHunter.Game.Data
{
    public class MonsterPart : TimedVisibility
    {
        Monster m_Owner;
        public ulong Address { get; private set; }

        bool m_IsRemovable;
        public bool IsRemovable
        {
            get { return m_IsRemovable; }
            set { SetProperty(ref m_IsRemovable, value); }
        }

        public Progress Health { get; private set; }

        int m_TimesBrokenCount;
        public int TimesBrokenCount
        {
            get { return m_TimesBrokenCount; }
            set { SetProperty(ref m_TimesBrokenCount, value); }
        }

        public string Name
        {
            get
            {
                return LocalizationHelper.GetMonsterPartName(m_Owner.Id, m_Owner.Parts.Where(part => part.IsRemovable == IsRemovable).ToList().IndexOf(this), IsRemovable);
            }
        }

        public string GroupId
        {
            get
            {
                return GetGroupIdFromIndex(m_Owner.Id, m_Owner.Parts.Where(part => part.IsRemovable == IsRemovable).ToList().IndexOf(this), IsRemovable);
            }
        }

        // Would show if there were room; Monster.RankRows picks which candidates fit
        public bool IsCandidate
        {
            get
            {
                var config = ConfigHelper.Main.Values.Overlay.MonsterWidget;
                return config.ShowParts && IsIncluded(GroupId) && !Data.ViewModels.OverlayViewModel.Instance.MonsterWidget.Context.WaitingForHost
                    && (config.AlwaysShowParts || IsTimeVisible(config.ShowUnchangedParts, config.HidePartsAfterSeconds));
            }
        }

        bool m_IsRanked;
        public bool IsRanked
        {
            get { return m_IsRanked; }
            set
            {
                if (SetProperty(ref m_IsRanked, value))
                {
                    NotifyPropertyChanged(nameof(IsVisible));
                }
            }
        }

        public bool IsVisible => IsCandidate && IsRanked;

        public MonsterPart(Monster owner, ulong address, bool isRemovable, float maxHealth, float currentHealth, int timesBrokenCount)
        {
            m_Owner = owner;
            Address = address;
            m_IsRemovable = isRemovable;
            Health = new Progress(maxHealth, currentHealth);
            m_TimesBrokenCount = timesBrokenCount;

            PropertyChanged += MonsterPart_PropertyChanged;
            Health.PropertyChanged += Health_PropertyChanged;
        }

        private void MonsterPart_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(TimesBrokenCount))
            {
                UpdateLastChangedTime();
            }
        }

        private void Health_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(Progress.Current))
            {
                UpdateLastChangedTime();
            }
        }

        public static string GetGroupIdFromIndex(string monsterId, int index, bool isRemovable)
        {
            if (ConfigHelper.MonsterData.Values.Monsters.TryGetValue(monsterId, out var monsterConfig))
            {
                var parts = monsterConfig.Parts.Where(part => part.IsRemovable == isRemovable);
                if (parts.Count() > index)
                {
                    return parts.ElementAt(index).GroupId;
                }
            }

            return "";
        }

        public static bool IsIncluded(string groupId)
        {
            return ConfigHelper.Main.Values.Overlay.MonsterWidget.MatchIncludePartGroupIdRegex(groupId);
        }
    }
}
