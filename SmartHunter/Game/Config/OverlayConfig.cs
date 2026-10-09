namespace SmartHunter.Game.Config
{
    public class OverlayConfig
    {
        public float ScaleMin = 0.5f;
        public float ScaleMax = 2f;
        public float ScaleStep = 0.1f;
        public bool HideWhenGameWindowIsInactive = false;
        public float UiScale = 1f;                 // multiplies every widget's own scale
        public string Shading = "Normal";          // Off, Light, Normal: the dark layer behind widget text
        public bool ColorblindColors = false;      // player colours that stay apart with red-green colour blindness
        public bool HideKeyToggles = false;        // the hide key toggles instead of hiding while held
        public int UpdatesPerSecond = 10; // bars ease between reads, so 10 looks as smooth as 20 at half the cost

        // Positions for a 1920x1080 screen; Reset layout scales them to the actual one
        public TeamWidgetConfig TeamWidget = new TeamWidgetConfig(440, 800);
        public MonsterWidgetConfig MonsterWidget = new MonsterWidgetConfig(1200, 20);
        public PlayerWidgetConfig PlayerWidget = new PlayerWidgetConfig(140, 380);
        public DebugWidgetConfig DebugWidget = new DebugWidgetConfig(20, 860);
        public Core.Config.WidgetConfig CalloutWidget = new Core.Config.WidgetConfig(560, 20);
        public Core.Config.WidgetConfig RecapWidget = new Core.Config.WidgetConfig(695, 300);
    }
}
