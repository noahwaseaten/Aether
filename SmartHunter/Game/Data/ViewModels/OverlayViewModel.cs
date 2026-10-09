using System.ComponentModel;
using SmartHunter.Core.Data;
using SmartHunter.Game.Data.WidgetContexts;
using SmartHunter.Game.Helpers;
using SmartHunter.Game;

namespace SmartHunter.Game.Data.ViewModels
{
    public class OverlayViewModel : Bindable
    {
        static OverlayViewModel s_Instance = null;
        public static OverlayViewModel Instance
        {
            get
            {
                if (s_Instance == null)
                {
                    s_Instance = new OverlayViewModel();

                    bool isDesignInstance = LicenseManager.UsageMode == LicenseUsageMode.Designtime;
                    if (isDesignInstance || ConfigHelper.Main.Values.Debug.UseSampleData)
                    {
                        s_Instance.GenerateSampleData();
                    }
                }

                return s_Instance;
            }
        }

        public ContextualWidget<TeamWidgetContext> TeamWidget { get; private set; }
        public ContextualWidget<MonsterWidgetContext> MonsterWidget { get; private set; }
        public ContextualWidget<PlayerWidgetContext> PlayerWidget { get; private set; }
        public ContextualWidget<DebugWidgetContext> DebugWidget { get; private set; }
        public ContextualWidget<CalloutWidgetContext> CalloutWidget { get; private set; }
        public ContextualWidget<RecapWidgetContext> RecapWidget { get; private set; }

        bool m_CanManipulateWindows;
        public bool CanManipulateWindows
        {
            get { return m_CanManipulateWindows; }
            set
            {
                if (SetProperty(ref m_CanManipulateWindows, value))
                {
                    NotifyPropertyChanged(nameof(IsVisible));
                }
            }
        }

        bool m_HideWidgetsRequested;
        public bool HideWidgetsRequested
        {
            get { return m_HideWidgetsRequested; }
            set
            {
                if (SetProperty(ref m_HideWidgetsRequested, value))
                {
                    NotifyPropertyChanged(nameof(IsVisible));
                }
            }
        }

        bool m_IsGameActive = false;
        public bool IsGameActive
        {
            get { return m_IsGameActive; }
            set
            {
                if (SetProperty(ref m_IsGameActive, value))
                {
                    NotifyPropertyChanged(nameof(IsVisible));
                }
            }
        }

        public bool IsVisible
        {
            get
            {
                // While editing the layout from the app window the game isn't focused, but you still need to see what you move
                return (IsGameActive || CanManipulateWindows) && !HideWidgetsRequested;
            }
        }

        // Default spots laid out for 1920x1080 and scaled to this screen, so nothing overlaps or lands off screen
        public void ResetLayout()
        {
            var defaults = new Config.OverlayConfig();
            double sx = System.Windows.SystemParameters.PrimaryScreenWidth / 1920, sy = System.Windows.SystemParameters.PrimaryScreenHeight / 1080;
            void Reset(Core.Data.Widget widget, Core.Config.WidgetConfig config) => widget.ResetPlacement((float)(config.X * sx), (float)(config.Y * sy));

            Reset(TeamWidget, defaults.TeamWidget);
            Reset(MonsterWidget, defaults.MonsterWidget);
            Reset(PlayerWidget, defaults.PlayerWidget);
            Reset(DebugWidget, defaults.DebugWidget);
            Reset(CalloutWidget, defaults.CalloutWidget);
            Reset(RecapWidget, defaults.RecapWidget);
            ConfigHelper.Main.Save();
        }

        public OverlayViewModel()
        {
            TeamWidget = new ContextualWidget<TeamWidgetContext>(ConfigHelper.Main.Values.Overlay.TeamWidget, new TeamWidgetContext());
            MonsterWidget = new ContextualWidget<MonsterWidgetContext>(ConfigHelper.Main.Values.Overlay.MonsterWidget, new MonsterWidgetContext());
            PlayerWidget = new ContextualWidget<PlayerWidgetContext>(ConfigHelper.Main.Values.Overlay.PlayerWidget, new PlayerWidgetContext());
            DebugWidget = new ContextualWidget<DebugWidgetContext>(ConfigHelper.Main.Values.Overlay.DebugWidget, new DebugWidgetContext());
            CalloutWidget = new ContextualWidget<CalloutWidgetContext>(ConfigHelper.Main.Values.Overlay.CalloutWidget, new CalloutWidgetContext());
            RecapWidget = new ContextualWidget<RecapWidgetContext>(ConfigHelper.Main.Values.Overlay.RecapWidget, new RecapWidgetContext());
        }

        // Debug.UseSampleData: preview every widget without the game running
        void GenerateSampleData()
        {
            IsGameActive = true;

            TeamWidget.Context.UpdateAndGetPlayer(0, "Lythia", 0);
            TeamWidget.Context.UpdateAndGetPlayer(1, "Kabuto", 0);
            TeamWidget.Context.UpdateAndGetPlayer(2, "mike the father", 0);
            TeamWidget.Context.UpdateAndGetPlayer(0, "Lythia", 3244).WeaponIcon = "ICON_SWITCHAXE";
            TeamWidget.Context.UpdateAndGetPlayer(1, "Kabuto", 2182).WeaponIcon = "ICON_LONGSWORD";
            TeamWidget.Context.UpdateAndGetPlayer(2, "mike the father", 569).WeaponIcon = "ICON_BOW";
            DebugWidget.Context.CurrentGame.CurrentPlayerName = "Lythia";
            TeamWidget.Context.UpdateFractions();

            var effects = ConfigHelper.PlayerData.Values.StatusEffects;
            void Effect(string nameId, float? seconds)
            {
                int index = System.Array.FindIndex(effects, e => e.NameStringId == nameId);
                if (index >= 0) PlayerWidget.Context.UpdateAndGetPlayerStatusEffect(index, seconds, true);
            }
            Effect("LOC_STATUS_EFFECT_POISON", 12);
            Effect("LOC_STATUS_EFFECT_ATTACK_UP_L", 95);
            Effect("LOC_STATUS_EFFECT_MIGHT_SEED", 8);
            Effect("LOC_STATUS_EFFECT_DEFENSE_UP_L", 240);
            Effect("LOC_EQUIPMENT_RECHARGE_MANTLE_GHILLIE", 64);

            var rathian = MonsterWidget.Context.UpdateAndGetMonster(1, "em001_00", 21000, 21000, 1.1f, 1);
            rathian.UpdateHealth(21000, 12890);
            for (int i = 0; i < 3; i++)
            {
                rathian.UpdateAndGetPart((ulong)(10 + i), i == 0, 900, 900, 0);
                rathian.UpdateAndGetPart((ulong)(10 + i), i == 0, 900, 900 - 280 * (i + 1), i == 1 ? 1 : 0);
            }
            MonsterWidget.Context.UpdateAndGetMonster(2, "em007_00", 18000, 15100, 1, 1);
            MonsterWidget.Context.UpdateFocus(0);

            PlayerWidget.Context.Sharpness.Update(new[] { 60, 100, 150, 200, 260, 0, 0 }, 230, 260);

            CalloutWidget.Context.Callouts.Add(new MonsterCallout { Name = "Rathalos", IsCapturable = true, IsEnraged = true });
            CalloutWidget.Context.Callouts.Add(new MonsterCallout { Name = "Pukei-Pukei", IsExhausted = true });
            RecapWidget.Context.Recap = HuntTracker.SampleRecap();
            RecapWidget.Context.IsShowing = true;
        }
    }    
}
