using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using SmartHunter.Core;
using SmartHunter.Core.Data;
using SmartHunter.Game.Config;
using SmartHunter.Game.Helpers;

namespace SmartHunter.Game.Data.ViewModels
{
    public class SettingsViewModel : Bindable
    {
        static SettingsViewModel s_Instance = null;
        public static SettingsViewModel Instance
        {
            get
            {
                if (s_Instance == null)
                {
                    s_Instance = new SettingsViewModel();
                }

                return s_Instance;
            }
        }

        public IList<Setting> Settings { get; } = new List<Setting>();

        bool m_NeedsRestart;
        public bool NeedsRestart
        {
            get { return m_NeedsRestart; }
            set { SetProperty(ref m_NeedsRestart, value); }
        }

        string m_RestartReason = "Some changes take effect after a restart.";
        public string RestartReason
        {
            get { return m_RestartReason; }
            set { SetProperty(ref m_RestartReason, value); }
        }

        public Command RestartCommand { get; } = new Command(_ => Core.Helpers.AppUpdater.Restart());

        static MainConfig C => ConfigHelper.Main.Values;

        // Every toggle writes the config and saves; ConfigContainer.Save raises Loaded so widgets refresh immediately
        void Toggle(string group, string name, string description, Func<bool> get, Action<bool> set, bool requiresRestart = false)
        {
            Setting setting = null;
            setting = new Setting(group, name, description, get(), new Command(_ =>
            {
                set(!get());
                ConfigHelper.Main.Save();
                setting.Value = get();
                if (requiresRestart)
                {
                    NeedsRestart = true;
                }
            }), requiresRestart);
            Settings.Add(setting);
        }

        public SettingsViewModel()
        {
            const string Monster = "Monsters";
            Toggle(Monster, "Show widget", "Health, parts and status of large monsters.", () => C.Overlay.MonsterWidget.IsVisible, v => C.Overlay.MonsterWidget.IsVisible = v);
            Toggle(Monster, "Only the monster you're fighting", "Hides the others. Follows your map pin, or else the last monster you hit.", () => C.Overlay.MonsterWidget.ShowOnlySelectedMonster, v => C.Overlay.MonsterWidget.ShowOnlySelectedMonster = v);
            Toggle(Monster, "Show monsters you haven't hit", null, () => C.Overlay.MonsterWidget.ShowUnchangedMonsters, v => C.Overlay.MonsterWidget.ShowUnchangedMonsters = v);
            Toggle(Monster, "Health bar", null, () => C.Overlay.MonsterWidget.ShowBars, v => C.Overlay.MonsterWidget.ShowBars = v);
            Toggle(Monster, "Health numbers", null, () => C.Overlay.MonsterWidget.ShowNumbers, v => C.Overlay.MonsterWidget.ShowNumbers = v);
            Toggle(Monster, "Percentages", null, () => C.Overlay.MonsterWidget.ShowPercents, v => C.Overlay.MonsterWidget.ShowPercents = v);
            Toggle(Monster, "Size", null, () => C.Overlay.MonsterWidget.ShowSize, v => C.Overlay.MonsterWidget.ShowSize = v);
            Toggle(Monster, "Crown", "Gold, silver or mini crown size.", () => C.Overlay.MonsterWidget.ShowCrown, v => C.Overlay.MonsterWidget.ShowCrown = v);
            Toggle(Monster, "Parts", null, () => C.Overlay.MonsterWidget.ShowParts, v => C.Overlay.MonsterWidget.ShowParts = v);
            Toggle(Monster, "Always show parts", "Off: a part shows for a few seconds after you hit it.", () => C.Overlay.MonsterWidget.AlwaysShowParts, v => C.Overlay.MonsterWidget.AlwaysShowParts = v);
            Toggle(Monster, "Tenderized parts", "Clutch claw tenderize timers.", () => C.Overlay.MonsterWidget.ShowSoftenParts, v => C.Overlay.MonsterWidget.ShowSoftenParts = v);
            Toggle(Monster, "Status effects", "Poison, paralysis, sleep, rage, exhaustion.", () => C.Overlay.MonsterWidget.ShowStatusEffects, v => C.Overlay.MonsterWidget.ShowStatusEffects = v);
            Toggle(Monster, "Always show status effects", "Off: a status shows while it's building up or active.", () => C.Overlay.MonsterWidget.ShowUnchangedStatusEffects, v => C.Overlay.MonsterWidget.ShowUnchangedStatusEffects = v);
            Toggle(Monster, "Pulse active statuses", null, () => C.Overlay.MonsterWidget.UseAnimations, v => C.Overlay.MonsterWidget.UseAnimations = v);

            const string Team = "Team damage";
            Toggle(Team, "Show widget", "Damage dealt by each hunter in the party.", () => C.Overlay.TeamWidget.IsVisible, v => C.Overlay.TeamWidget.IsVisible = v);
            Toggle(Team, "Hide when solo", null, () => C.Overlay.TeamWidget.DontShowIfAlone, v => C.Overlay.TeamWidget.DontShowIfAlone = v);
            Toggle(Team, "Bars", null, () => C.Overlay.TeamWidget.ShowBars, v => C.Overlay.TeamWidget.ShowBars = v);
            Toggle(Team, "Damage numbers", null, () => C.Overlay.TeamWidget.ShowNumbers, v => C.Overlay.TeamWidget.ShowNumbers = v);
            Toggle(Team, "Damage chart", "Damage over time, above the list.", () => C.Overlay.TeamWidget.ShowChart, v => C.Overlay.TeamWidget.ShowChart = v);

            const string Buffs = "Buffs";
            Toggle(Buffs, "Show widget", "Your buffs and debuffs with time left, plus sharpness.", () => C.Overlay.PlayerWidget.IsVisible, v => C.Overlay.PlayerWidget.IsVisible = v);

            const string Quest = "Quest";
            Toggle(Quest, "Call-outs", "A tag at the top of the screen when a monster can be captured, is enraged, or is exhausted.",
                () => C.Overlay.CalloutWidget.IsVisible, v => C.Overlay.CalloutWidget.IsVisible = v);
            Toggle(Quest, "Quest results", "When a quest ends: monsters, carts and each hunter's damage.",
                () => C.Overlay.RecapWidget.IsVisible, v => C.Overlay.RecapWidget.IsVisible = v);

            const string Party = "Party sync";
            Toggle(Party, "Share data with your party", "Only the host's game has exact part HP and ailment buildup, and the game doesn't track damage on expeditions. "
                + "With this on, everyone running Aether shares those numbers through the SmartHunter sync server (hashed lobby ID, hunter names, damage, monster data).",
                () => C.Overlay.MonsterWidget.UseNetworkServer, v => C.Overlay.MonsterWidget.UseNetworkServer = v, true);

            const string Overlay = "Overlay";
            Toggle(Overlay, "Hide when the game isn't focused", null,
                () => C.Overlay.HideWhenGameWindowIsInactive, v => C.Overlay.HideWhenGameWindowIsInactive = v);
            Toggle(Overlay, "Discord status", "Shows what you're doing on your Discord profile.",
                () => C.DiscordPresence.Enabled, v => C.DiscordPresence.Enabled = v);

            const string App = "App";
            Toggle(App, "Check for updates on startup", "Downloads new versions of Aether from GitHub and restarts into them.",
                () => C.AutomaticallyCheckAndDownloadUpdates, v => C.AutomaticallyCheckAndDownloadUpdates = v);
            Toggle(App, "Start the game with Aether", null, () => C.StartMHWWhenSmartHunterStart, v => C.StartMHWWhenSmartHunterStart = v, true);
            Toggle(App, "Close with the game", null, () => C.ShutdownWhenProcessExits, v => C.ShutdownWhenProcessExits = v);
            Toggle(App, "Back up saves when the game closes", "Copies your Steam save folder.", () => C.BackupWhenProcessExits, v => C.BackupWhenProcessExits = v);
            Settings.Add(new Setting(App, "Steam save folder", C.UserDataPath, "Change…", new Command(_ =>
            {
                using (var dialog = new System.Windows.Forms.FolderBrowserDialog { SelectedPath = C.UserDataPath })
                {
                    if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                    {
                        C.UserDataPath = dialog.SelectedPath;
                        ConfigHelper.Main.Save();
                    }
                }
            })));

            const string Advanced = "Advanced";
            Toggle(Advanced, "Debug widget", "Session, lobby and weapon info.", () => C.Overlay.DebugWidget.IsVisible, v => C.Overlay.DebugWidget.IsVisible = v);
            Toggle(Advanced, "Software rendering", "Turn on if widgets flicker, show black boxes or don't appear (some AMD and older graphics drivers). Uses a bit more CPU.",
                () => C.UseSoftwareRendering, v => C.UseSoftwareRendering = v, true);
            Toggle(Advanced, "Sync server log", "Logs every request to the sync server.", () => C.Debug.ShowServerLogs, v => C.Debug.ShowServerLogs = v);
        }
    }
}
