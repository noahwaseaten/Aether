using System;
using System.Collections.Generic;
using SmartHunter.Core.Data;

namespace SmartHunter.Core
{
    public class Setting : Bindable
    {
        public string Group { get; }
        public string Name { get; }
        public string Key => Group + "/" + Name;

        // Shows a "New" badge until the player has seen the row or changed it (SettingsViewModel.Seen)
        bool m_IsNew;
        public bool IsNew
        {
            get { return m_IsNew; }
            set { SetProperty(ref m_IsNew, value); }
        }
        // Shown in the row's tooltip
        public string Description { get; }
        // Shown under the name: a current value like a folder path, not an explanation
        string m_Detail;
        public string Detail
        {
            get { return m_Detail; }
            set { if (SetProperty(ref m_Detail, value)) NotifyPropertyChanged(nameof(Subtitle)); }
        }

        // The explanation's first sentence, shown under the name so nobody has to hover to learn what a row does
        public string Summary
        {
            get
            {
                if (Description == null)
                {
                    return null;
                }
                int end = Description.IndexOf(". ", StringComparison.Ordinal);
                return end > 0 ? Description.Substring(0, end + 1) : Description;
            }
        }

        // The "i" tooltip only earns its place when it says more than the line under the name
        public bool HasMore => Description != null && Description != Summary;

        // Under the name: why the row is off, else its current value, else what it does
        public string Subtitle => !IsEnabled ? "Turn on " + m_Parent.Name + " first." : Detail ?? Summary;

        // A row that only means something while another toggle (in the same section) is on
        Setting m_Parent;
        public bool IsEnabled => m_Parent == null || m_Parent.Value;

        public void DependsOn(Setting parent)
        {
            m_Parent = parent;
            parent.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(Value))
                {
                    NotifyPropertyChanged(nameof(IsEnabled));
                    NotifyPropertyChanged(nameof(Subtitle));
                }
            };
            NotifyPropertyChanged(nameof(IsEnabled));
            NotifyPropertyChanged(nameof(Subtitle));
        }

        // A few choices shown side by side, each one visible and one click away (null: a toggle or a button)
        public IList<SettingOption> Options { get; set; }
        public bool IsChoice => Options != null;
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

    public class SettingOption : Bindable
    {
        public string Label { get; }
        public Command Select { get; }

        bool m_IsSelected;
        public bool IsSelected
        {
            get { return m_IsSelected; }
            set { SetProperty(ref m_IsSelected, value); }
        }

        public SettingOption(string label, Command select)
        {
            Label = label;
            Select = select;
        }
    }

    // A tab in the settings window: one group of settings
    public class SettingsCategory : Bindable
    {
        public string Name { get; }
        public Command Select { get; }

        bool m_IsSelected;
        public bool IsSelected
        {
            get { return m_IsSelected; }
            set { SetProperty(ref m_IsSelected, value); }
        }

        // Something in it is still marked new
        bool m_HasNew;
        public bool HasNew
        {
            get { return m_HasNew; }
            set { SetProperty(ref m_HasNew, value); }
        }

        public SettingsCategory(string name, Command select)
        {
            Name = name;
            Select = select;
        }
    }
}
