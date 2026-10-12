using System.Collections.ObjectModel;
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

        // Localization id ("LOC_PART_HEAD"): how a wound finds its part
        public string StringId
        {
            get
            {
                int index = m_Owner.Parts.Where(part => part.IsRemovable == IsRemovable).ToList().IndexOf(this);
                return ConfigHelper.MonsterData.Values.Monsters.TryGetValue(m_Owner.Id ?? "", out var monsterConfig)
                    ? monsterConfig.Parts.Where(part => part.IsRemovable == IsRemovable).ElementAtOrDefault(index)?.StringId ?? ""
                    : "";
            }
        }

        // Clutch claw wound on this part, shown on its card (as HunterPie does) instead of a second card with the same name
        MonsterPartSoften m_Wound;
        public MonsterPartSoften Wound
        {
            get { return m_Wound; }
            set { SetProperty(ref m_Wound, value); }
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
                    && (Wound != null || config.AlwaysShowParts || IsTimeVisible(config.ShowUnchangedParts, config.HidePartsAfterSeconds));
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
                UpdateBreakPips();
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

        // One pip per time the bar has to be drained to break the part, filled as it's drained. No pips = it only flinches.
        // Unknown threshold: a filled pip per drain so far (the widget adds a "?").
        // ponytail: capped at 3 for unknown parts, since drains after a break keep counting; widen if a part ever needs more.
        // Updated in place, not replaced: a new list rebuilt every pip, and each filled one played its fill animation again.
        readonly ObservableCollection<bool> m_BreakPips = new ObservableCollection<bool>();
        public ObservableCollection<bool> BreakPips
        {
            get
            {
                UpdateBreakPips();
                return m_BreakPips;
            }
        }

        void UpdateBreakPips()
        {
            int threshold = BreakThreshold;
            var pips = threshold < 0
                ? Enumerable.Repeat(true, System.Math.Min(TimesBrokenCount, 3)).ToArray()
                : Enumerable.Range(0, threshold).Select(i => i < TimesBrokenCount).ToArray();
            while (m_BreakPips.Count > pips.Length)
            {
                m_BreakPips.RemoveAt(m_BreakPips.Count - 1);
            }
            for (int i = 0; i < pips.Length; i++)
            {
                if (i == m_BreakPips.Count)
                {
                    m_BreakPips.Add(pips[i]);
                }
                else if (m_BreakPips[i] != pips[i])
                {
                    m_BreakPips[i] = pips[i];
                }
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
