using SmartHunter.Core.Data;
using SmartHunter.Game.Helpers;

namespace SmartHunter.Game.Data
{
    public class PlayerStatusEffect : Bindable
    {
        int m_Index;
        public int Index
        {
            get { return m_Index; }
            set
            {
                if (SetProperty(ref m_Index, value))
                {
                    NotifyPropertyChanged(nameof(GroupId));
                    NotifyPropertyChanged(nameof(Name));
                    NotifyPropertyChanged(nameof(IsVisible));
                }
            }
        }

        public Progress Time { get; private set; }

        bool m_IsConditionPassed;
        public bool IsConditionPassed
        {
            get { return m_IsConditionPassed; }
            set
            {
                if (SetProperty(ref m_IsConditionPassed, value))
                {
                    NotifyPropertyChanged(nameof(IsVisible));
                }
            }
        }

        public string GroupId
        {
            get
            {
                return GetGroupIdFromIndex(Index);
            }
        }

        string NameStringId => ConfigHelper.PlayerData.Values.StatusEffects[Index].NameStringId;

        public string IconKey => IconMap.StatusEffects.TryGetValue(NameStringId, out var key) ? key : null;

        public bool IsDebuff => GroupId == "Debuff";

        // Mantle/booster recharge timers: shown as quiet "ready in" cooldowns
        public bool IsCooldown => NameStringId.Contains("RECHARGE");

        // Last 10 seconds of a timed buff: the skin tints it so you can refresh in time
        public bool IsExpiring => Time != null && !IsCooldown && !IsDebuff && Time.Current > 0 && Time.Current <= 10;

        // Debuffs first, then active timed buffs, then permanent ones, cooldowns last
        public int SortGroup => IsDebuff ? 0 : IsCooldown ? 3 : Time == null ? 2 : 1;

        // Cooldowns read as the item ("Ghillie Mantle"), the dimmed row already says it's recharging
        public string DisplayName => IsCooldown && Name.StartsWith("Recharge ") ? Name.Substring("Recharge ".Length) : Name;

        public string Name
        {
            get
            {
                return LocalizationHelper.GetPlayerStatusEffectName(Index);
            }
        }

        public bool IsVisible
        {
            get
            {
                return IsIncluded(GroupId) && IsConditionPassed;
            }
        }

        public PlayerStatusEffect(int index, float? maxTime, float? currentTime, bool isConditionPassed)
        {
            m_Index = index;

            if (maxTime.HasValue && currentTime.HasValue)
            {
                Time = new Progress(maxTime.Value, currentTime.Value);
                Time.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName == nameof(Progress.Current))
                    {
                        NotifyPropertyChanged(nameof(IsExpiring));
                    }
                };
            }

            m_IsConditionPassed = isConditionPassed;
        }

        public static string GetGroupIdFromIndex(int index)
        {
            return ConfigHelper.PlayerData.Values.StatusEffects[index].GroupId;
        }

        public static bool IsIncluded(string groupId)
        {
            return ConfigHelper.Main.Values.Overlay.PlayerWidget.MatchIncludeStatusEffectGroupIdRegex(groupId);
        }
    }
}
