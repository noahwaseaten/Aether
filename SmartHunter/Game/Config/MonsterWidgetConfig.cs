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
        public bool ShowUnchangedMonsters = true;
        public float HideMonstersAfterSeconds = 999f;
        public bool ShowParts = true;
        public bool ShowUnchangedParts = false;
        public float HidePartsAfterSeconds = 12f;
        public bool ShowSoftenParts = true;
        public float HideSoftenPartsAfterSeconds = 6f;
        public bool ShowStatusEffects = true;
        public bool ShowUnchangedStatusEffects = false;
        public float HideStatusEffectsAfterSeconds = 12f;

        public bool ShowSize = false; // the crown already says when the size matters
        public bool ShowCrown = true;
        public bool ShowBars = true;
        public bool ShowNumbers = true;
        public bool ShowPercents = false;
        public bool UseAnimations = false;
        public bool ShowOnlySelectedMonster = true; // replaced by MonsterFilter, kept so old Config.json files still load
        // "Fighting": the map-pinned monster, else the one you last hit. "Pinned": only the map-pinned one. "All": every large monster.
        public string MonsterFilter = "Fighting";
        public bool AlwaysShowParts = false;
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
