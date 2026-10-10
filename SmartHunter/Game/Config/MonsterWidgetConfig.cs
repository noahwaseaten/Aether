using System.Text.RegularExpressions;
using SmartHunter.Core.Config;

namespace SmartHunter.Game.Config
{
    public class MonsterWidgetConfig : WidgetConfig
    {
        // em[0-9]|ems[0-9]|gm[0-9]
        public string IncludeMonsterIdRegex = "em[0-9]";
        public string IncludePartGroupIdRegex = ".*";
        public string IncludePartSoftenGroupIdRegex = ".*";
        public string IncludeStatusEffectGroupIdRegex = ".*";
        public bool ShowUnchangedMonsters = false;
        public float HideMonstersAfterSeconds = 999f;
        public bool ShowParts = true;
        public bool ShowUnchangedParts = false;
        public float HidePartsAfterSeconds = 12f;
        public bool ShowSoftenParts = false;
        public float HideSoftenPartsAfterSeconds = 6f;
        public bool ShowStatusEffects = true;
        public bool ShowUnchangedStatusEffects = false;
        public float HideStatusEffectsAfterSeconds = 12f;

        public bool ShowSize = false; // the crown already says when the size matters
        public bool ShowCrown = false;
        public bool ShowBars = true;
        public bool ShowNumbers = true;
        public bool ShowPercents = true;
        public bool UseAnimations = true;
        public bool ShowOnlySelectedMonster = true; // replaced by MonsterFilter, kept so old Config.json files still load
        // "Fighting": the map-pinned monster, else the one you last hit. "Pinned": only the map-pinned one. "All": every large monster.
        public string MonsterFilter = "Fighting";
        public bool AlwaysShowParts = false;
        public bool FollowLockOn = true;          // the monster you lock onto takes focus over the map pin
        public bool UseNetworkServer = true;

        public MonsterWidgetConfig(float x, float y) : base(x, y)
        {
        }

        public bool MatchIncludeMonsterIdRegex(string monsterId)
        {
            return Regex.IsMatch(monsterId, IncludeMonsterIdRegex);
        }

        public bool MatchIncludePartGroupIdRegex(string groupId)
        {
            return Regex.IsMatch(groupId, IncludePartGroupIdRegex);
        }

        public bool MatchIncludePartSoftenGroupIdRegex(string groupId)
        {
            return Regex.IsMatch(groupId, IncludePartSoftenGroupIdRegex);
        }

        public bool MatchIncludeStatusEffectGroupIdRegex(string groupId)
        {
            return Regex.IsMatch(groupId, IncludeStatusEffectGroupIdRegex);
        }
    }
}
