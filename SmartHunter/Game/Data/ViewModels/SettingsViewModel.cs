using System;
using System.Collections.Generic;
using System.Linq;
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

        // A button row showing the current option; each click moves to the next one
        void Choice(string group, string name, string description, (string Value, string Label)[] options, Func<string> get, Action<string> set)
        {
            string LabelOf(string value) => (options.FirstOrDefault(o => o.Value == value).Label) ?? options[0].Label;
            Setting setting = null;
            setting = new Setting(group, name, description, LabelOf(get()), new Command(_ =>
            {
                int next = (Array.FindIndex(options, o => o.Value == get()) + 1) % options.Length;
                set(options[next].Value);
                ConfigHelper.Main.Save();
                setting.ActionLabel = LabelOf(get());
            }));
            Settings.Add(setting);
        }

        public SettingsViewModel()
        {
            const string Monster = "Monster widget";
            Toggle(Monster, "Show widget", "Health, parts and status of large monsters.", () => C.Overlay.MonsterWidget.IsVisible, v => C.Overlay.MonsterWidget.IsVisible = v);
            Choice(Monster, "Which monsters", "Fighting: your map pin, else the last one you hit. Pinned: only your map pin. All: every large monster, the others as one line each.",
                new[] { ("Fighting", "Fighting"), ("Pinned", "Pinned only"), ("All", "All") },
                () => C.Overlay.MonsterWidget.MonsterFilter, v => C.Overlay.MonsterWidget.MonsterFilter = v);
            Toggle(Monster, "Keep idle monsters on screen", "Off: a monster fades out a while after it last took damage. On: it stays, even at full health.",
                () => C.Overlay.MonsterWidget.ShowUnchangedMonsters, v => C.Overlay.MonsterWidget.ShowUnchangedMonsters = v);
            Toggle(Monster, "Health bar", "The bar under the monster's name.", () => C.Overlay.MonsterWidget.ShowBars, v => C.Overlay.MonsterWidget.ShowBars = v);
            Toggle(Monster, "Health numbers", "Exact health left, next to the bar.", () => C.Overlay.MonsterWidget.ShowNumbers, v => C.Overlay.MonsterWidget.ShowNumbers = v);
            Toggle(Monster, "Health percentage", "Health left as a percentage.", () => C.Overlay.MonsterWidget.ShowPercents, v => C.Overlay.MonsterWidget.ShowPercents = v);
            Toggle(Monster, "Size", "The monster's size, for crown hunting.", () => C.Overlay.MonsterWidget.ShowSize, v => C.Overlay.MonsterWidget.ShowSize = v);
            Toggle(Monster, "Crown", "A gold, silver or mini crown icon when the monster qualifies.", () => C.Overlay.MonsterWidget.ShowCrown, v => C.Overlay.MonsterWidget.ShowCrown = v);
            Toggle(Monster, "Parts", "Breakable and severable parts, with the damage left on each.", () => C.Overlay.MonsterWidget.ShowParts, v => C.Overlay.MonsterWidget.ShowParts = v);
            Toggle(Monster, "Always show parts", "Off: a part shows for a few seconds after you hit it.", () => C.Overlay.MonsterWidget.AlwaysShowParts, v => C.Overlay.MonsterWidget.AlwaysShowParts = v);
            Toggle(Monster, "Tenderized parts", "Time left on each clutch claw tenderize.", () => C.Overlay.MonsterWidget.ShowSoftenParts, v => C.Overlay.MonsterWidget.ShowSoftenParts = v);
            Toggle(Monster, "Status effects", "Poison, paralysis, sleep, rage, exhaustion and other buildup.", () => C.Overlay.MonsterWidget.ShowStatusEffects, v => C.Overlay.MonsterWidget.ShowStatusEffects = v);
            Toggle(Monster, "Always show status effects", "Off: a status shows while it's building up or active.", () => C.Overlay.MonsterWidget.ShowUnchangedStatusEffects, v => C.Overlay.MonsterWidget.ShowUnchangedStatusEffects = v);
            Toggle(Monster, "Pulse active statuses", "A status bar pulses while the ailment is in effect, so you notice the opening.", () => C.Overlay.MonsterWidget.UseAnimations, v => C.Overlay.MonsterWidget.UseAnimations = v);

            const string Team = "Team damage widget";
            Toggle(Team, "Show widget", "Damage dealt by each hunter in the party.", () => C.Overlay.TeamWidget.IsVisible, v => C.Overlay.TeamWidget.IsVisible = v);
            Toggle(Team, "Hide when solo", "Only show the widget when someone else is in your quest.", () => C.Overlay.TeamWidget.DontShowIfAlone, v => C.Overlay.TeamWidget.DontShowIfAlone = v);
            Toggle(Team, "Bars", "A bar for each hunter's share of the damage.", () => C.Overlay.TeamWidget.ShowBars, v => C.Overlay.TeamWidget.ShowBars = v);
            Toggle(Team, "Damage numbers", "Each hunter's total damage.", () => C.Overlay.TeamWidget.ShowNumbers, v => C.Overlay.TeamWidget.ShowNumbers = v);
            Toggle(Team, "Damage chart", "Damage over time, above the list.", () => C.Overlay.TeamWidget.ShowChart, v => C.Overlay.TeamWidget.ShowChart = v);

            const string Buffs = "Buffs widget";
            Toggle(Buffs, "Show widget", "Your buffs and debuffs with time left, plus sharpness.", () => C.Overlay.PlayerWidget.IsVisible, v => C.Overlay.PlayerWidget.IsVisible = v);

            const string Quest = "Quest";
            Toggle(Quest, "Call-outs", "A tag at the top of the screen when a monster can be captured, is enraged, or is exhausted.",
                () => C.Overlay.CalloutWidget.IsVisible, v => C.Overlay.CalloutWidget.IsVisible = v);
            Toggle(Quest, "Quest results", "When a quest ends: monsters, carts and each hunter's damage.",
                () => C.Overlay.RecapWidget.IsVisible, v => C.Overlay.RecapWidget.IsVisible = v);

            const string Overlay = "Look and behavior";
            Choice(Overlay, "Overlay size", "Scales every widget. Scroll over a widget while editing the layout to size it on its own.",
                new[] { ("0.9", "90%"), ("1", "100%"), ("1.15", "115%"), ("1.3", "130%") },
                () => C.Overlay.UiScale.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture),
                v => C.Overlay.UiScale = float.Parse(v, System.Globalization.CultureInfo.InvariantCulture));
            Choice(Overlay, "Background shading", "The soft dark layer behind widget text. Normal keeps text readable over bright skies and snow.",
                new[] { ("Normal", "Normal"), ("Light", "Light"), ("Off", "Off") },
                () => C.Overlay.Shading, v => C.Overlay.Shading = v);
            Toggle(Overlay, "Colorblind-friendly colors", "Player colors that stay distinct with red-green color blindness.",
                () => C.Overlay.ColorblindColors, v => C.Overlay.ColorblindColors = v);
            Choice(Overlay, "F1 hides the overlay", "Clears the screen for screenshots or cutscenes.",
                new[] { ("Hold", "While held"), ("Toggle", "Press to toggle") },
                () => C.Overlay.HideKeyToggles ? "Toggle" : "Hold", v => C.Overlay.HideKeyToggles = v == "Toggle");
            Toggle(Overlay, "Hide when the game isn't focused", "Widgets disappear while you're alt-tabbed, so they don't cover other windows.",
                () => C.Overlay.HideWhenGameWindowIsInactive, v => C.Overlay.HideWhenGameWindowIsInactive = v);

            const string Party = "Party sync";
            Toggle(Party, "Share data with your party", "Only the host's game has exact part HP and ailment buildup, and the game doesn't track damage on expeditions. "
                + "With this on, everyone running Aether shares those numbers through the SmartHunter sync server (hashed lobby ID, hunter names, damage, monster data).",
                () => C.Overlay.MonsterWidget.UseNetworkServer, v => C.Overlay.MonsterWidget.UseNetworkServer = v, true);

            const string Game = "Game";
            Toggle(Game, "Start the game with Aether", "Opens Monster Hunter: World through Steam when Aether starts, unless it's already running. If an update is downloading, the game starts once it's done.",
                () => C.StartMHWWhenSmartHunterStart, v => C.StartMHWWhenSmartHunterStart = v, true);
            Toggle(Game, "Close with the game", "Aether quits when you close Monster Hunter: World.", () => C.ShutdownWhenProcessExits, v => C.ShutdownWhenProcessExits = v);
            Toggle(Game, "Discord Rich Presence", "Shows what you're doing on your Discord profile: your area, the monster you're fighting and its health, and how the quest ended.",
                () => C.DiscordPresence.Enabled, v => C.DiscordPresence.Enabled = v);
            Toggle(Game, "Back up saves when the game closes", "Zips your Steam save folder into UserDataBackup next to Aether, in case a save gets corrupted.",
                () => C.BackupWhenProcessExits, v => C.BackupWhenProcessExits = v);
            Setting saveFolder = null;
            saveFolder = new Setting(Game, "Steam save folder", "Where Steam keeps your saves. The backup copies this folder.", "Change…", new Command(_ =>
            {
                using (var dialog = new System.Windows.Forms.FolderBrowserDialog { SelectedPath = C.UserDataPath, Description = "Your Steam userdata folder, used by the save backup" })
                {
                    if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                    {
                        C.UserDataPath = dialog.SelectedPath;
                        ConfigHelper.Main.Save();
                        saveFolder.Detail = C.UserDataPath;
                    }
                }
            })) { Detail = C.UserDataPath };
            Settings.Add(saveFolder);

            const string Updates = "Updates";
            Toggle(Updates, "Update automatically", "Downloads new versions from GitHub when Aether starts and shows what changed. "
                + "If the game isn't running yet, Aether restarts into it after a short countdown you can cancel; otherwise you choose when.",
                () => C.AutomaticallyCheckAndDownloadUpdates, v => C.AutomaticallyCheckAndDownloadUpdates = v);
            Setting check = null;
            check = new Setting(Updates, "Check for updates", "Looks for a newer version on GitHub now.", "Check now", new Command(async _ =>
            {
                if (check.ActionLabel == "Checking…")
                    return;
                check.ActionLabel = "Checking…";
                bool upToDate = await UpdateViewModel.Instance.CheckAsync(false);
                check.ActionLabel = upToDate ? "Up to date" : "Check now";
            })) { Detail = $"You have Aether {Core.Helpers.AppUpdater.CurrentVersion.ToString(3)}" };
            Settings.Add(check);

            const string Troubleshooting = "Troubleshooting";
            Toggle(Troubleshooting, "Software rendering", "Turn on if widgets flicker, show black boxes or don't appear (some AMD and older graphics drivers). Uses a bit more CPU.",
                () => C.UseSoftwareRendering, v => C.UseSoftwareRendering = v, true);
            Toggle(Troubleshooting, "Debug widget", "Session, lobby and weapon info, for bug reports.", () => C.Overlay.DebugWidget.IsVisible, v => C.Overlay.DebugWidget.IsVisible = v);
            Toggle(Troubleshooting, "Sync server log", "Writes every party sync request to the Log tab.", () => C.Debug.ShowServerLogs, v => C.Debug.ShowServerLogs = v);

            const string About = "About";
            const string Repo = "https://github.com/noahwaseaten/Aether";
            string version = Core.Helpers.AppUpdater.CurrentVersion.ToString(3);
            string folder = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
            Settings.Add(new Setting(About, "Version", "What changed in each version, on GitHub.", "Release notes",
                new Command(_ => Open(Repo + "/releases"))) { Detail = $"Aether {version} · {WindowsVersion()}" });
            Settings.Add(new Setting(About, "Aether folder", "Settings, the log (Log.txt) and save backups live here.", "Open",
                new Command(_ => Open(folder))) { Detail = folder });
            Settings.Add(new Setting(About, "Report a bug", "Opens a new GitHub issue with your Aether and Windows versions filled in. Attaching Log.txt from the Aether folder helps a lot.", "Report…",
                new Command(_ => Open($"{Repo}/issues/new?body=" + Uri.EscapeDataString($"**What happened:**\n\n**What you expected:**\n\n---\nAether {version}, {WindowsVersion()}"))))
                { Detail = "Bugs, wrong numbers, widgets in the wrong place" });
        }

        static void Open(string target)
        {
            try { Process.Start(target); }
            catch (Exception ex) { Log.WriteLine($"Couldn't open {target}: {ex.Message}"); }
        }

        // Environment.OSVersion reports Windows 8 to apps without a manifest; the registry has the real build
        static string WindowsVersion()
        {
            try
            {
                using (var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
                {
                    int build = int.Parse((string)key.GetValue("CurrentBuild"));
                    return $"Windows {(build >= 22000 ? 11 : 10)} {key.GetValue("DisplayVersion")} (build {build})";
                }
            }
            catch (Exception)
            {
                return "Windows";
            }
        }
    }
}
