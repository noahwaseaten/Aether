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
                return config.ShowParts && IsIncluded(GroupId)
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
                NotifyPropertyChanged(nameof(IsBroken));
                NotifyPropertyChanged(nameof(BreakPips));
            }
        }

        private void Health_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(Progress.Current))
            {
                UpdateLastChangedTime();
            }
        }

        // How many times this part's damage pool has to be emptied before it breaks (cut parts: once).
        // 0 = it only flinches, -1 = unknown. TimesBrokenCount is how many times it has been emptied so far.
        public int BreakThreshold
        {
            get
            {
                if (IsRemovable)
                {
                    return 1;
                }
                int index = m_Owner.Parts.Where(part => !part.IsRemovable).ToList().IndexOf(this);
                return PartData.BreakThresholds.TryGetValue(m_Owner.Id, out var thresholds) && index >= 0 && index < thresholds.Length ? thresholds[index] : -1;
            }
        }

        public bool IsBroken => BreakThreshold > 0 && TimesBrokenCount >= BreakThreshold;
        public bool CanBreak => BreakThreshold > 0;
        public bool IsThresholdUnknown => BreakThreshold < 0;

        // One pip per break step, filled as the pool gets emptied (only for parts that take more than one)
        public bool[] BreakPips
        {
            get
            {
                int threshold = BreakThreshold;
                return !IsRemovable && threshold > 1 ? Enumerable.Range(0, threshold).Select(i => i < TimesBrokenCount).ToArray() : new bool[0];
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
