using System;
using System.ComponentModel;
using SmartHunter.Core.Data;
using SmartHunter.Game.Helpers;

namespace SmartHunter.Game.Data
{
    public class MonsterStatusEffect : TimedVisibility
    {
        Monster m_Owner;

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

        public ulong Address;

        public string GroupId
        {
            get
            {
                return GetGroupIdFromIndex(Index);
            }
        }

        public Progress Buildup { get; private set; }
        public Progress Duration { get; private set; }

        int m_TimesActivatedCount;
        public int TimesActivatedCount
        {
            get { return m_TimesActivatedCount; }
            set { SetProperty(ref m_TimesActivatedCount, value); }
        }

        public string Name
        {
            get
            {
                return LocalizationHelper.GetMonsterStatusEffectName(Index);
            }
        }

        // Ailment buildup (poison, paralysis...) is host-only data; rage, stamina and exhaustion live on the monster itself
        public bool IsCandidate
        {
            get
            {
                bool hostOnly = GroupId == "StatusEffect" && Data.ViewModels.OverlayViewModel.Instance.MonsterWidget.Context.WaitingForHost;
                return !hostOnly && IsIncluded(GroupId) && ConfigHelper.Main.Values.Overlay.MonsterWidget.ShowStatusEffects
                    && IsTimeVisible(ConfigHelper.Main.Values.Overlay.MonsterWidget.ShowUnchangedStatusEffects, ConfigHelper.Main.Values.Overlay.MonsterWidget.HideStatusEffectsAfterSeconds);
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

        public MonsterStatusEffect(Monster owner, ulong address, int index, float maxBuildup, float currentBuildup, float maxDuration, float currentDuration, int timesActivatedCount)
        {
            m_Owner = owner;
            Address = address;
            m_Index = index;
            Buildup = new Progress(maxBuildup, currentBuildup, true);
            Duration = new Progress(maxDuration, currentDuration, true);
            m_TimesActivatedCount = timesActivatedCount;

            PropertyChanged += MonsterStatusEffect_PropertyChanged;
            Buildup.PropertyChanged += Bar_PropertyChanged;
            Duration.PropertyChanged += Bar_PropertyChanged;
        }

        private void MonsterStatusEffect_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(TimesActivatedCount))
            {
                UpdateLastChangedTime();
            }
        }

        private void Bar_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (sender == Duration)
            {
                NotifyPropertyChanged(nameof(IsActive));
            }
            UpdateLastChangedTime();
        }

        static readonly System.Collections.Generic.Dictionary<string, string> s_Icons = new System.Collections.Generic.Dictionary<string, string>
        {
            { "LOC_STATUS_EFFECT_POISON", "ICON_POISON" }, { "LOC_STATUS_EFFECT_PARALYSIS", "ICON_PARALYSIS" },
            { "LOC_STATUS_EFFECT_SLEEP", "ICON_SLEEP" }, { "LOC_STATUS_EFFECT_BLAST", "ICON_BLAST" },
            { "LOC_STATUS_EFFECT_STUN", "ICON_STUN" }, { "LOC_STATUS_EFFECT_EXHAUST", "ICON_MAXSTAMRECOVERY" },
            { "LOC_STATUS_EFFECT_MOUNT", "ICON_KNOCKBACKNEG" }, { "LOC_STATUS_EFFECT_KNOW_DOWN", "ICON_KNOCKBACKNEG" },
            { "LOC_STATUS_EFFECT_TRANQUILIZE", "ICON_TRAP" }, { "LOC_STATUS_EFFECT_SHOCK_TRAP", "ICON_TRAP" },
            { "LOC_STATUS_EFFECT_PITFALL_TRAP", "ICON_TRAP" }, { "LOC_STATUS_EFFECT_FELVYNE_KNOCK_DOWN_TRAP", "ICON_TRAP" },
            { "LOC_STATUS_EFFECT_ELDERSEAL", "ICON_DRAGONRES" }, { "LOC_STATUS_EFFECT_DUNG", "ICON_ENVNEG" },
            { "LOC_STATUS_EFFECT_SMOKING", "ICON_EFFLUVIA" },
        };

        string NameStringId => ConfigHelper.MonsterData.Values.StatusEffects[Index].NameStringId;

        // Game icon for the ailment; null falls back to the first letters of the name
        public string IconKey => s_Icons.TryGetValue(NameStringId, out var key) ? key : null;
        public string Initials => string.IsNullOrEmpty(Name) ? "?" : Name.Substring(0, Math.Min(2, Name.Length));

        // Building up (ring shows buildup) or active (ring shows time left)
        public bool IsActive => Duration.Max > 0 && Duration.Current > 0 && Duration.Current < Duration.Max;

        public static string GetGroupIdFromIndex(int index)
        {
            return ConfigHelper.MonsterData.Values.StatusEffects[index].GroupId;
        }

        public static bool IsIncluded(string groupId)
        {
            return ConfigHelper.Main.Values.Overlay.MonsterWidget.MatchIncludeStatusEffectGroupIdRegex(groupId);
        }
    }
}
