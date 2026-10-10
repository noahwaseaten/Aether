using SmartHunter.Core.Data;

namespace SmartHunter.Core
{
    public class Setting : Bindable
    {
        public string Group { get; }
        public string Name { get; }
        // Shown in the row's tooltip
        public string Description { get; }
        // Shown under the name: a current value like a folder path, not an explanation
        string m_Detail;
        public string Detail
        {
            get { return m_Detail; }
            set { SetProperty(ref m_Detail, value); }
        }
        public bool RequiresRestart { get; }
        public bool IsToggle { get; }
        string m_ActionLabel;
        public string ActionLabel
        {
            get { return m_ActionLabel; }
            set { SetProperty(ref m_ActionLabel, value); }
        }
        public Command TriggerAction { get; }

        // Matches the settings search. Rows are hidden in place rather than filtered out: filtering rebuilt every row
        // on each key typed, and each rebuilt switch played its slide from off to on.
        bool m_IsShown = true;
        public bool IsShown
        {
            get { return m_IsShown; }
            set { SetProperty(ref m_IsShown, value); }
        }

        bool m_Value;
        public bool Value
        {
            get { return m_Value; }
            set { SetProperty(ref m_Value, value); }
        }

        // Toggle row
        public Setting(string group, string name, string description, bool value, Command action, bool requiresRestart = false)
        {
            Group = group;
            Name = name;
            Description = description;
            m_Value = value;
            TriggerAction = action;
            RequiresRestart = requiresRestart;
            IsToggle = true;
        }

        // Button row
        public Setting(string group, string name, string description, string actionLabel, Command action)
        {
            Group = group;
            Name = name;
            Description = description;
            m_ActionLabel = actionLabel;
            TriggerAction = action;
        }
    }
}
