using System;
using SmartHunter.Core.Data;

namespace SmartHunter.Game.Data
{
    // A friend's weapon kept running at red and orange mid-hunt, and the small bar was never enough. For the hunter named
    // Senji, sharpness that is far below what the weapon reaches puts a huge bar and a roast at the top of the screen
    // (SharpenAlertWindow) until he sharpens. It's a gag: impossible to miss, impossible to sit through, and it gets
    // meaner the longer he ignores it, while the click-through window leaves the game playable underneath.
    // Hardcoded on purpose; it only ever looks at the local player's own name.
    public class SharpenAlert : Bindable
    {
        public const string TargetName = "Senji";

        // Created on first use, after the static lines below exist: built any earlier, its starting text reads them as null
        static SharpenAlert s_Instance;
        public static SharpenAlert Instance => s_Instance ?? (s_Instance = new SharpenAlert());

        public static bool IsTarget(string name) => string.Equals(name?.Trim(), TargetName, StringComparison.OrdinalIgnoreCase);

        // Three colours below the best (or red on a weapon that reaches yellow or better) starts it; it stays until he
        // is back within one colour of the best, so it doesn't flicker as the colour wavers
        public static bool ShouldShow(bool wasShowing, bool available, int top, int level)
        {
            if (!available)
            {
                return false;
            }
            int missing = top - level;
            return wasShowing ? missing >= 2 : missing >= 3 || (level == 0 && top >= 2);
        }

        // The big line changes every 20 seconds, starting somewhere different each time; the small one escalates
        public const int SecondsPerStage = 20;
        static readonly string[] Headlines =
        {
            "SENJI. YOUR WEAPON IS A SPOON.",
            "THE MONSTER IS LAUGHING AT YOU, SENJI.",
            "SENJI'S WEAPON HAS FILED A COMPLAINT.",
            "THIS IS AN INTERVENTION, SENJI.",
            "THAT'S NOT A WEAPON, SENJI, THAT'S A VIBE.",
            "WE CAN HEAR THE BONK FROM HERE, SENJI.",
            "BREAKING NEWS: SENJI FORGETS WHETSTONE (AGAIN).",
            "SENJI, YOU ARE HITTING IT WITH A BUTTER KNIFE.",
        };
        static readonly string[] Escalation =
        {
            "Press the whetstone button. You know the one.",
            "Still going? The bar says no.",
            "Your teammates can see this bar too, you know.",
            "The monster sent a thank-you note.",
            "Senji. Please. The bar is begging.",
            "At this point it's a bit. Sharpen anyway.",
        };

        public static string HeadlineFor(int stage, int offset) => Headlines[(offset + Math.Max(0, stage)) % Headlines.Length];
        public static string EscalationFor(int stage) => Escalation[Math.Min(Math.Max(0, stage), Escalation.Length - 1)];
        public static int StageOf(TimeSpan elapsed) => (int)(elapsed.TotalSeconds / SecondsPerStage);

        // The same pips as a teammate's row, drawn huge
        public PlayerSharpness Bar { get; } = new PlayerSharpness();

        bool m_IsActive;
        public bool IsActive
        {
            get { return m_IsActive; }
            private set { SetProperty(ref m_IsActive, value); }
        }

        string m_Headline = Headlines[0];
        public string Headline
        {
            get { return m_Headline; }
            private set { SetProperty(ref m_Headline, value); }
        }

        string m_Escalation = Escalation[0];
        public string Escalated
        {
            get { return m_Escalation; }
            private set { SetProperty(ref m_Escalation, value); }
        }

        // What it actually is, in plain words, under the joke
        string m_Detail = "";
        public string Detail
        {
            get { return m_Detail; }
            private set { SetProperty(ref m_Detail, value); }
        }

        DateTime m_StartedAt;
        int m_Offset;
        static readonly Random s_Random = new Random();

        public void Update(string playerName, Sharpness local)
        {
            if (!IsTarget(playerName))
            {
                IsActive = false;
                return;
            }

            bool show = ShouldShow(IsActive, local.IsAvailable, local.TopIndex, local.LevelIndex);
            if (show)
            {
                if (!IsActive)
                {
                    m_StartedAt = DateTime.Now;
                    m_Offset = s_Random.Next(Headlines.Length);
                }
                int stage = StageOf(DateTime.Now - m_StartedAt);
                Headline = HeadlineFor(stage, m_Offset);
                Escalated = EscalationFor(stage);
                Detail = $"Currently {Sharpness.NameOf(local.LevelIndex)}. This weapon can do {Sharpness.NameOf(local.TopIndex)}.";
                Bar.Apply(SharpnessShare.Pack(true, local.TopIndex, local.LevelIndex, local.Percent, local.HitsLeft));
            }
            IsActive = show;
        }

        public void Reset() => IsActive = false;
    }
}
