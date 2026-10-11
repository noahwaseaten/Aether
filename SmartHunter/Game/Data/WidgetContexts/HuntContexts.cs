using System.Collections.ObjectModel;
using SmartHunter.Core.Data;

namespace SmartHunter.Game.Data.WidgetContexts
{
    // One large monster with something worth calling out
    public class MonsterCallout : Bindable
    {
        public ulong Address { get; set; }

        string m_Name;
        public string Name { get { return m_Name; } set { SetProperty(ref m_Name, value); } }

        bool m_IsCapturable;
        public bool IsCapturable { get { return m_IsCapturable; } set { SetProperty(ref m_IsCapturable, value); } }

        bool m_IsEnraged;
        public bool IsEnraged { get { return m_IsEnraged; } set { SetProperty(ref m_IsEnraged, value); } }

        bool m_IsExhausted;
        public bool IsExhausted { get { return m_IsExhausted; } set { SetProperty(ref m_IsExhausted, value); } }

        public bool HasAnything => IsCapturable || IsEnraged || IsExhausted;
    }

    public class CalloutWidgetContext : WidgetContext
    {
        public ObservableCollection<MonsterCallout> Callouts { get; } = new ObservableCollection<MonsterCallout>();
    }

    public class RecapHunter
    {
        public int Rank { get; set; }
        public string Name { get; set; }
        public string WeaponIcon { get; set; }
        public int ColorIndex { get; set; }
        public int Damage { get; set; }
        public float Dps { get; set; }
        public float Share { get; set; }
        public float Bar { get; set; }
        public bool IsMe { get; set; }
        public bool IsMvp { get; set; }
    }

    public class RecapMonster
    {
        public string Id { get; set; } // for the portrait
        public string Name { get; set; }
        public bool IsCaptured => Outcome == "Captured";
        public string Outcome { get; set; }
    }

    public class HuntRecap
    {
        public string Result { get; set; }
        public bool IsSuccess { get; set; }
        public string Stars { get; set; }
        public int Deaths { get; set; }
        // "1/3" from the game's party counter; your own deaths when it wasn't read
        public string PartyDeaths { get; set; }
        public string DeathsText => PartyDeaths ?? Deaths.ToString();
        // Quest time, "12:34"
        public string Time { get; set; }
        public int MyDamage { get; set; }
        public float MyDps { get; set; }
        public string Headline { get; set; }
        public bool IsSolo { get; set; }
        public ObservableCollection<RecapHunter> Hunters { get; } = new ObservableCollection<RecapHunter>();
        public ObservableCollection<RecapMonster> Monsters { get; } = new ObservableCollection<RecapMonster>();
    }

    public class RecapWidgetContext : WidgetContext
    {
        public const double SecondsOnScreen = 25;

        HuntRecap m_Recap;
        public HuntRecap Recap { get { return m_Recap; } set { SetProperty(ref m_Recap, value); } }

        bool m_IsShowing;
        public bool IsShowing { get { return m_IsShowing; } set { SetProperty(ref m_IsShowing, value); } }
    }
}
