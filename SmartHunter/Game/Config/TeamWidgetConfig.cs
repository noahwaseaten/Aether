using SmartHunter.Core.Config;

namespace SmartHunter.Game.Config
{
    public class TeamWidgetConfig : WidgetConfig
    {
        public bool DontShowIfAlone = false;
        public bool ShowBars = true;
        public bool ShowNumbers = true;
        public bool ShowPercents = true;
        public bool ShowChart = false;
        public bool CountAllMonsters = true;
        public bool LiveDps = true;   // damage per second over the last few seconds; off = the quest's average

        public TeamWidgetConfig(float x, float y) : base(x, y)
        {
        }
    }
}
