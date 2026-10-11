using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using SmartHunter.Core.Data;
using SmartHunter.Game.Helpers;

namespace SmartHunter.Game.Data.WidgetContexts
{
    public class PlayerWidgetContext : WidgetContext
    {
        public ObservableCollection<PlayerStatusEffect> StatusEffects { get; private set; }

        public Sharpness Sharpness { get; } = new Sharpness();

        public HuntInfo Hunt { get; } = new HuntInfo();

        // Mantles and boosters in use, as tiles above the buff list (HunterPie's specialized tools)
        public ObservableCollection<SpecializedTool> Tools { get; } = new ObservableCollection<SpecializedTool>();

        // This tick's timers for each tool: while it's on, and while it recharges
        readonly Dictionary<string, float?> m_ToolActive = new Dictionary<string, float?>();
        readonly Dictionary<string, float?> m_ToolRecharge = new Dictionary<string, float?>();
        readonly Dictionary<string, int> m_ToolIndex = new Dictionary<string, int>();

        public PlayerWidgetContext()
        {
            StatusEffects = new ObservableCollection<PlayerStatusEffect>();
        }

        public PlayerStatusEffect UpdateAndGetPlayerStatusEffect(int index, float? currentTime, bool isConditionPassed)
        {
            if (UpdateTool(index, currentTime, isConditionPassed))
            {
                return null;
            }

            PlayerStatusEffect playerStatusEffect = StatusEffects.SingleOrDefault(statusEffect => statusEffect.Index == index);
            if (playerStatusEffect == null && isConditionPassed)
            {
                playerStatusEffect = new PlayerStatusEffect(index, currentTime, currentTime, isConditionPassed);
                StatusEffects.Add(playerStatusEffect);
            }
            else if (playerStatusEffect != null)
            {
                if (currentTime.HasValue)
                {
                    playerStatusEffect.Time.Current = currentTime.Value;
                }

                playerStatusEffect.IsConditionPassed = isConditionPassed;
            }

            return playerStatusEffect;
        }

        public static bool IsTool(string nameStringId) => nameStringId.StartsWith("LOC_EQUIPMENT_");

        public static string ToolKey(string nameStringId) => nameStringId.Replace("RECHARGE_", "");

        bool UpdateTool(int index, float? time, bool isOn)
        {
            string id = ConfigHelper.PlayerData.Values.StatusEffects[index].NameStringId;
            if (!IsTool(id))
            {
                return false;
            }

            string key = ToolKey(id);
            float? value = isOn && time > 0 ? time : null;
            if (id == key)
            {
                m_ToolActive[key] = value;
                m_ToolIndex[key] = index;
            }
            else
            {
                m_ToolRecharge[key] = value;
            }
            return true;
        }

        // After every timer was read: one tile per tool that's on or recharging
        public void SyncTools()
        {
            bool shown = PlayerStatusEffect.IsIncluded("Equipment");
            foreach (var key in m_ToolIndex.Keys)
            {
                m_ToolActive.TryGetValue(key, out var active);
                m_ToolRecharge.TryGetValue(key, out var recharge);
                var tool = Tools.FirstOrDefault(t => t.Key == key);
                if (!shown || (active == null && recharge == null))
                {
                    if (tool != null) Tools.Remove(tool);
                    continue;
                }

                if (tool == null)
                {
                    tool = new SpecializedTool(key, LocalizationHelper.GetPlayerStatusEffectName(m_ToolIndex[key]),
                        IconMap.StatusEffects.TryGetValue(key, out var icon) ? icon : null);
                    Tools.Add(tool);
                }
                tool.Update(active ?? recharge.Value, active == null);
            }
        }

        public override void UpdateFromConfig()
        {
            base.UpdateFromConfig();

            foreach (var statusEffect in StatusEffects)
            {
                statusEffect.NotifyPropertyChanged(nameof(PlayerStatusEffect.IsVisible));
            }
        }
    }
}
