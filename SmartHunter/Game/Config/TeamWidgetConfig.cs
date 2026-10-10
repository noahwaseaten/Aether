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

        public TeamWidgetConfig(float x, float y) : base(x, y)
        {
        }
    }
}
