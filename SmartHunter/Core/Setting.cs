using SmartHunter.Core.Data;

namespace SmartHunter.Core
{
    public class Setting : Bindable
    {
        public string Group { get; }
        public string Name { get; }
        public string Description { get; }
        public bool RequiresRestart { get; }
        public bool IsToggle { get; }
        string m_ActionLabel;
        public string ActionLabel
        {
            get { return m_ActionLabel; }
            set { SetProperty(ref m_ActionLabel, value); }
        }
        public Command TriggerAction { get; }

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
