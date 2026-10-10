using System;
using System.Linq;
using System.Windows;

namespace SmartHunter.Game.Config
{
    public class OverlayConfig
    {
        public float ScaleMin = 0.5f;
        public float ScaleMax = 2f;
        public float ScaleStep = 0.1f;
        public bool HideWhenGameWindowIsInactive = true;
        public float UiScale = 1f;                 // multiplies every widget's own scale
        public string Shading = "Light";           // Off, Light, Normal: the dark layer behind widget text
        public bool ColorblindColors = false;      // player colours that stay apart with red-green colour blindness
        public bool HideKeyToggles = true;         // the hide key toggles instead of hiding while held
        public int UpdatesPerSecond = 10; // bars ease between reads, so 10 looks as smooth as 20 at half the cost

        // Positions for a 1920x1080 screen; ForScreen scales them to the actual one
        public TeamWidgetConfig TeamWidget = new TeamWidgetConfig(390, 774);
        public MonsterWidgetConfig MonsterWidget = new MonsterWidgetConfig(704, 0);
        public PlayerWidgetConfig PlayerWidget = new PlayerWidgetConfig(1478, 179);
        public DebugWidgetConfig DebugWidget = new DebugWidgetConfig(20, 860);
        public Core.Config.WidgetConfig CalloutWidget = new Core.Config.WidgetConfig(560, 20) { IsVisible = false };
        public Core.Config.WidgetConfig RecapWidget = new Core.Config.WidgetConfig(1026, 652);

        // The overlay sizes offered in Settings
        public static readonly float[] UiScales = { 0.75f, 0.9f, 1f, 1.15f, 1.3f, 1.6f, 2f };

        // The default layout fitted to the primary screen (in DPI-aware units): widgets keep their spots and grow with
        // the screen height, so 1440p or 4K looks like 1080p instead of tiny widgets bunched in a corner
        public static OverlayConfig ForScreen()
        {
            var config = new OverlayConfig();
            double sx = SystemParameters.PrimaryScreenWidth / 1920, sy = SystemParameters.PrimaryScreenHeight / 1080;
            config.UiScale = UiScales.OrderBy(s => Math.Abs(s - sy)).First();
            foreach (var widget in new Core.Config.WidgetConfig[] { config.TeamWidget, config.MonsterWidget, config.PlayerWidget, config.DebugWidget, config.CalloutWidget, config.RecapWidget })
            {
                widget.X = (float)(widget.X * sx);
                widget.Y = (float)(widget.Y * sy);
            }
            return config;
        }
    }
}
