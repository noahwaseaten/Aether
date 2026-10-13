namespace SmartHunter.Core.Config
{
    public class WidgetConfig
    {
        public bool IsVisible = true;
        public float X;
        public float Y;
        public float Scale = 1f;
        public float Width;     // window width when X was saved; right-side widgets are placed by their right edge (0 = not known)

        public WidgetConfig(float x, float y)
        {
            X = x;
            Y = y;
        }
    }
}
