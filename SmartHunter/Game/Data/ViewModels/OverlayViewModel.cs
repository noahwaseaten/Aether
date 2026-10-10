using System;
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
                // While editing the game isn't focused, and the hide key may be on, but you still need to see what you move
                return CanManipulateWindows || (IsGameActive && !HideWidgetsRequested);
            }
        }

        // For the Aether window's hint; the key can be changed in Settings > Keyboard
        public string EditKeyName => KeyBinder.KeyName(ConfigHelper.Main.Values.Keybinds[InputControl.ManipulateWidget]);
        public void NotifyKeysChanged() => NotifyPropertyChanged(nameof(EditKeyName));

        // Default spots scaled to this screen, so nothing overlaps or lands off screen
        public void ResetLayout()
        {
            var defaults = Config.OverlayConfig.ForScreen();
            void Reset(Core.Data.Widget widget, Core.Config.WidgetConfig config) => widget.ResetPlacement(config.X, config.Y, config.Scale);

            Reset(TeamWidget, defaults.TeamWidget);
            Reset(MonsterWidget, defaults.MonsterWidget);
            Reset(PlayerWidget, defaults.PlayerWidget);
            Reset(DebugWidget, defaults.DebugWidget);
            Reset(CalloutWidget, defaults.CalloutWidget);
            Reset(RecapWidget, defaults.RecapWidget);
            ConfigHelper.Main.Save();
        }

        double m_UiScale = 1;
        public double UiScale
        {
            get { return m_UiScale; }
            set { SetProperty(ref m_UiScale, value); }
        }

        double m_ShadeOpacity = 1;
        public double ShadeOpacity
        {
            get { return m_ShadeOpacity; }
            set { SetProperty(ref m_ShadeOpacity, value); }
        }

        // Okabe-Ito colours: orange, sky blue, yellow and purple stay distinct for the common kinds of colour blindness
        static readonly string[] s_DefaultPlayerColors = { "#E5484D", "#4C8DFF", "#F5C542", "#3DD68C" };
        static readonly string[] s_ColorblindPlayerColors = { "#E69F00", "#56B4E9", "#F0E442", "#CC79A7" };

        public void ApplyDisplaySettings()
        {
            var overlay = ConfigHelper.Main.Values.Overlay;
            UiScale = Math.Max(0.5, Math.Min(2, overlay.UiScale));
            ShadeOpacity = overlay.Shading == "Off" ? 0 : overlay.Shading == "Light" ? 0.55 : 1;

            var app = System.Windows.Application.Current;
            if (app == null) return;
            var colors = overlay.ColorblindColors ? s_ColorblindPlayerColors : s_DefaultPlayerColors;
            for (int i = 0; i < colors.Length; i++)
            {
                var color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(colors[i]);
                var brush = new System.Windows.Media.SolidColorBrush(color);
                brush.Freeze();
                app.Resources["B_Player" + i] = brush;
                app.Resources["A_Color_Player_" + i] = color;
            }
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
            TeamWidget.Context.UpdateAndGetPlayer(1, "Kabuto", 2182).UsesAether = true;
            TeamWidget.Context.Players[2].IsDamagePartial = true; // no Aether: quest targets only
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
            // A busy fight: more parts than fit, ailments building up and one active, a severed tail
            for (int i = 0; i < 8; i++)
            {
                rathian.UpdateAndGetPart((ulong)(10 + i), i == 0, 900, 900, 0);
                rathian.UpdateAndGetPart((ulong)(10 + i), i == 0, 900, i == 0 ? 0 : 880 - 100 * i, i <= 1 ? 1 : 0);
            }
            var statusEffects = ConfigHelper.MonsterData.Values.StatusEffects;
            void Status(string groupId, string nameId, float buildup, float durationLeft, int times)
            {
                int index = System.Array.FindIndex(statusEffects, e => e.GroupId == groupId && (nameId == null || e.NameStringId == nameId));
                rathian.UpdateAndGetStatusEffect((ulong)(50 + index), index, 300, 0, 30, 30, 0);
                rathian.UpdateAndGetStatusEffect((ulong)(50 + index), index, 300, buildup, 30, durationLeft, times);
            }
            Status("StatusEffect", "LOC_STATUS_EFFECT_POISON", 210, 30, 1);
            Status("StatusEffect", "LOC_STATUS_EFFECT_PARALYSIS", 0, 4, 1);
            Status("StatusEffect", "LOC_STATUS_EFFECT_SLEEP", 90, 30, 0);
            Status("StatusEffect", "LOC_STATUS_EFFECT_STUN", 160, 30, 0);
            Status("StatusEffect", "LOC_STATUS_EFFECT_VIOLATED", 120, 30, 1);
            Status("Rage", null, 0, 18, 2);
            Status("Stamina", null, 180, 30, 0);
            rathian.CaptureFraction = 0.3f;
            MonsterWidget.Context.ShowNotice("Getting parts and ailments from Kabuto's game.", 8);
            MonsterWidget.Context.UpdateAndGetMonster(2, "em007_00", 18000, 15100, 1, 1);
            MonsterWidget.Context.UpdateAndGetMonster(4, "em044_00", 12000, 4200, 1, 1);
            MonsterWidget.Context.UpdateFocus(0);

            PlayerWidget.Context.Sharpness.Update(new[] { 60, 100, 150, 200, 260, 0, 0 }, 230, 260);
            PlayerWidget.Context.Hunt.Clock = "12:34";
            PlayerWidget.Context.Hunt.ClockLimit = "/ 50:00";

            CalloutWidget.Context.Callouts.Add(new MonsterCallout { Name = "Rathalos", IsCapturable = true, IsEnraged = true });
            CalloutWidget.Context.Callouts.Add(new MonsterCallout { Name = "Pukei-Pukei", IsExhausted = true });
            RecapWidget.Context.Recap = HuntTracker.SampleRecap();
            RecapWidget.Context.IsShowing = true;
        }
    }    
}
