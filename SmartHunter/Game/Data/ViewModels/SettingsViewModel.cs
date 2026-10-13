using System;
using SmartHunter.Core.Helpers;
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

        // Bumped by every search or category change, so section headings re-check whether any of their rows is still shown
        int m_SearchVersion;
        public int SearchVersion
        {
            get { return m_SearchVersion; }
            set { SetProperty(ref m_SearchVersion, value); }
        }

        public IList<SettingsCategory> Categories { get; } = new List<SettingsCategory>();

        string m_Selected;

        // Typing shows every matching setting, from any category; empty shows just the selected category
        string m_Query = "";
        public string Query
        {
            get { return m_Query; }
            set
            {
                if (SetProperty(ref m_Query, value ?? ""))
                {
                    if (IsSearching)
                    {
                        IsLogShown = false;
                    }
                    Refresh();
                    NotifyPropertyChanged(nameof(IsSearching));
                }
            }
        }

        public bool IsSearching => m_Query.Trim().Length > 0;
        // A search that finds nothing says so instead of leaving an empty page
        bool m_NoResults;
        public bool NoResults
        {
            get { return m_NoResults; }
            set { SetProperty(ref m_NoResults, value); }
        }

        public string Title => IsLogShown ? "Log" : IsSearching ? "Search results" : m_Selected;

        // The Log is a page of its own, listed under the categories
        bool m_IsLogShown;
        public bool IsLogShown
        {
            get { return m_IsLogShown; }
            private set
            {
                if (SetProperty(ref m_IsLogShown, value))
                {
                    NotifyPropertyChanged(nameof(IsSettingsShown));
                }
            }
        }
        public bool IsSettingsShown => !m_IsLogShown;
        public SettingsCategory LogCategory { get; private set; }

        // The window listens, to slide the new page in and scroll it back to the top
        public event Action PageChanged;

        void Select(string category)
        {
            m_Selected = category;
            IsLogShown = false;
            Query = "";
            Refresh();
            PageChanged?.Invoke();
        }

        void ShowLog()
        {
            Query = "";
            IsLogShown = true;
            Refresh();
            PageChanged?.Invoke();
        }

        // Matches a setting's name, its explanation or its section
        void Refresh()
        {
            string query = m_Query.Trim();
            bool Has(string text) => text != null && text.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
            foreach (var setting in Settings)
            {
                setting.IsShown = query.Length == 0 ? setting.Group == m_Selected : Has(setting.Name) || Has(setting.Description) || Has(setting.Group);
            }
            foreach (var category in Categories)
            {
                category.IsSelected = !m_IsLogShown && query.Length == 0 && category.Name == m_Selected;
                category.HasNew = Settings.Any(s => s.Group == category.Name && s.IsNew);
            }
            if (LogCategory != null)
            {
                LogCategory.IsSelected = m_IsLogShown;
            }
            NoResults = !m_IsLogShown && !Settings.Any(x => x.IsShown);
            NotifyPropertyChanged(nameof(Title));
            SearchVersion++;
        }

        // The player has seen this row or used it: its "New" badge goes and doesn't come back
        public void Seen(Setting setting)
        {
            if (!setting.IsNew)
            {
                return;
            }
            setting.IsNew = false;
            if (!C.SeenSettings.Contains(setting.Key))
            {
                C.SeenSettings.Add(setting.Key);
                ConfigHelper.Main.Save();
            }
            Refresh();
        }

        public Command RestartCommand { get; } = new Command(_ => Core.Helpers.AppUpdater.Restart());

        static MainConfig C => ConfigHelper.Main.Values;

        // Every toggle writes the config and saves; ConfigContainer.Save raises Loaded so widgets refresh immediately
        void Toggle(string group, string name, string description, Func<bool> get, Action<bool> set, bool requiresRestart = false, bool isNew = false)
        {
            Setting setting = null;
            setting = new Setting(group, name, description, get(), new Command(_ =>
            {
                set(!get());
                ConfigHelper.Main.Save();
                setting.Value = get();
                Seen(setting);
                if (requiresRestart)
                {
                    NeedsRestart = true;
                }
            }), requiresRestart);
            Add(setting, isNew);
        }

        void Add(Setting setting, bool isNew)
        {
            setting.IsNew = isNew && !C.SeenSettings.Contains(setting.Key);
            Settings.Add(setting);
        }

        // Up to four options show side by side so every one is visible; more than that is a button that moves to the next
        void Choice(string group, string name, string description, (string Value, string Label)[] options, Func<string> get, Action<string> set, bool isNew = false)
        {
            string LabelOf(string value) => (options.FirstOrDefault(o => o.Value == value).Label) ?? options[0].Label;
            Setting setting = null;
            if (options.Length <= 4)
            {
                setting = new Setting(group, name, description, "", null);
                var items = new List<SettingOption>();
                foreach (var option in options)
                {
                    var item = new SettingOption(option.Label, new Command(_ =>
                    {
                        set(option.Value);
                        ConfigHelper.Main.Save();
                        foreach (var other in items) other.IsSelected = other.Label == LabelOf(get());
                        Seen(setting);
                    })) { IsSelected = option.Value == get() };
                    items.Add(item);
                }
                setting.Options = items;
                Add(setting, isNew);
                return;
            }

            setting = new Setting(group, name, description, LabelOf(get()), new Command(_ =>
            {
                int next = (Array.FindIndex(options, o => o.Value == get()) + 1) % options.Length;
                set(options[next].Value);
                ConfigHelper.Main.Save();
                setting.ActionLabel = LabelOf(get());
                Seen(setting);
            }));
            Add(setting, isNew);
        }

        // A button row showing a shortcut; click it, then press the new key (Esc keeps the old one)
        void KeyRow(string group, string name, string description, InputControl control)
        {
            Setting setting = null;
            setting = new Setting(group, name, description, KeyBinder.KeyName(C.Keybinds[control]), new Command(_ =>
            {
                setting.ActionLabel = "Press a key…";
                KeyBinder.Pending = key =>
                {
                    if (key != System.Windows.Input.Key.Escape)
                    {
                        C.Keybinds[control] = key;
                        ConfigHelper.Main.Save();
                        OverlayViewModel.Instance.NotifyKeysChanged();
                    }
                    setting.ActionLabel = KeyBinder.KeyName(C.Keybinds[control]);
                };
            }));
            Settings.Add(setting);
        }

        public SettingsViewModel()
        {
            const string Monster = "Monster";
            Toggle(Monster, "Show widget", "Health, parts and status of large monsters.", () => C.Overlay.MonsterWidget.IsVisible, v => C.Overlay.MonsterWidget.IsVisible = v);
            Choice(Monster, "Which monsters", "Fighting: your map pin, else the last one you hit, plus a one-line bar for any other monster fought in the last minute (turf wars, double hunts). Pinned: only your map pin. All: every large monster, the others as one line each.",
                new[] { ("Fighting", "Fighting"), ("Pinned", "Pinned only"), ("All", "All") },
                () => C.Overlay.MonsterWidget.MonsterFilter, v => C.Overlay.MonsterWidget.MonsterFilter = v);
            Toggle(Monster, "Follow lock-on", "The monster you lock onto with the camera takes focus, ahead of your map pin and the one you last hit. Pinned only mode still shows only the pin.",
                () => C.Overlay.MonsterWidget.FollowLockOn, v => C.Overlay.MonsterWidget.FollowLockOn = v);
            Toggle(Monster, "Keep idle monsters on screen", "Off: a monster fades out a while after it last took damage. On: it stays, even at full health.",
                () => C.Overlay.MonsterWidget.ShowUnchangedMonsters, v => C.Overlay.MonsterWidget.ShowUnchangedMonsters = v);
            Toggle(Monster, "Health bar", "The bar under the monster's name.", () => C.Overlay.MonsterWidget.ShowBars, v => C.Overlay.MonsterWidget.ShowBars = v);
            Toggle(Monster, "Health numbers", "Exact health left, next to the bar.", () => C.Overlay.MonsterWidget.ShowNumbers, v => C.Overlay.MonsterWidget.ShowNumbers = v);
            Toggle(Monster, "Health percentage", "Health left as a percentage.", () => C.Overlay.MonsterWidget.ShowPercents, v => C.Overlay.MonsterWidget.ShowPercents = v);
            Toggle(Monster, "Size", "The monster's size, for crown hunting.", () => C.Overlay.MonsterWidget.ShowSize, v => C.Overlay.MonsterWidget.ShowSize = v);
            Toggle(Monster, "Crown", "A gold, silver or mini crown icon when the monster qualifies.", () => C.Overlay.MonsterWidget.ShowCrown, v => C.Overlay.MonsterWidget.ShowCrown = v);
            Toggle(Monster, "Parts", "Breakable and severable parts, with the damage left on each. Dots fill each time you drain a part's bar; it breaks when they're all full. ? means Aether doesn't know how many it takes.", () => C.Overlay.MonsterWidget.ShowParts, v => C.Overlay.MonsterWidget.ShowParts = v);
            Toggle(Monster, "Always show parts", "Off: a part shows for a few seconds after you hit it.", () => C.Overlay.MonsterWidget.AlwaysShowParts, v => C.Overlay.MonsterWidget.AlwaysShowParts = v);
            Toggle(Monster, "Wounds", "A cyan line under a wounded part, with the time until the wound heals.", () => C.Overlay.MonsterWidget.ShowSoftenParts, v => C.Overlay.MonsterWidget.ShowSoftenParts = v);
            Toggle(Monster, "Status effects", "Poison, paralysis, sleep, rage, exhaustion and other buildup.", () => C.Overlay.MonsterWidget.ShowStatusEffects, v => C.Overlay.MonsterWidget.ShowStatusEffects = v);
            Toggle(Monster, "Always show status effects", "Off: a status shows while it's building up or active.", () => C.Overlay.MonsterWidget.ShowUnchangedStatusEffects, v => C.Overlay.MonsterWidget.ShowUnchangedStatusEffects = v);
            Toggle(Monster, "Pulse active statuses", "A status bar pulses while the ailment is in effect, so you notice the opening.", () => C.Overlay.MonsterWidget.UseAnimations, v => C.Overlay.MonsterWidget.UseAnimations = v);

            const string Team = "Team";
            Toggle(Team, "Show widget", "Damage dealt by each hunter in the party.", () => C.Overlay.TeamWidget.IsVisible, v => C.Overlay.TeamWidget.IsVisible = v);
            Toggle(Team, "Hide when solo", "Only show the widget when someone else is in your quest.", () => C.Overlay.TeamWidget.DontShowIfAlone, v => C.Overlay.TeamWidget.DontShowIfAlone = v);
            Choice(Team, "Count damage to", "Quest targets: the game's own counter. Exact for every hunter, but ignores other large monsters (turf wars) and small monsters. "
                + "Every monster: adds up the damage numbers on your screen, like on expeditions. Needs damage numbers on in the game's options, and a teammate only counts fully if they also run Aether with this on and party sync; otherwise they show quest-target damage.",
                new[] { ("Targets", "Quest targets"), ("All", "Every monster") },
                () => C.Overlay.TeamWidget.CountAllMonsters ? "All" : "Targets", v => C.Overlay.TeamWidget.CountAllMonsters = v == "All");
            Toggle(Team, "Bars", "Each hunter's row fills with their colour as far as their damage reaches against the top hunter.", () => C.Overlay.TeamWidget.ShowBars, v => C.Overlay.TeamWidget.ShowBars = v);
            Toggle(Team, "Damage share", "Each hunter's percentage of the party's damage, with a green or red arrow while it's rising or falling.", () => C.Overlay.TeamWidget.ShowPercents, v => C.Overlay.TeamWidget.ShowPercents = v);
            Toggle(Team, "Damage numbers", "Each hunter's total damage, with damage per second under it.", () => C.Overlay.TeamWidget.ShowNumbers, v => C.Overlay.TeamWidget.ShowNumbers = v);
            Toggle(Team, "Live damage per second", "On: damage per second over the last 10 seconds, so it drops when a hunter stops hitting. Off: the average over the whole quest.",
                () => C.Overlay.TeamWidget.LiveDps, v => C.Overlay.TeamWidget.LiveDps = v, isNew: true);
            Toggle(Team, "Damage chart", "Damage over time, above the list.", () => C.Overlay.TeamWidget.ShowChart, v => C.Overlay.TeamWidget.ShowChart = v);

            const string Buffs = "Buffs & quest";
            Toggle(Buffs, "Buffs widget", "Your buffs and debuffs with time left, sharpness, the quest clock, and your mantles and boosters while they're on or recharging.", () => C.Overlay.PlayerWidget.IsVisible, v => C.Overlay.PlayerWidget.IsVisible = v);

            const string Quest = Buffs;
            Toggle(Quest, "Call-outs", "A tag at the top of the screen when a monster can be captured, is enraged, or is exhausted.",
                () => C.Overlay.CalloutWidget.IsVisible, v => C.Overlay.CalloutWidget.IsVisible = v);
            Toggle(Quest, "Quest results", "When a quest ends: monsters, deaths and each hunter's damage.",
                () => C.Overlay.RecapWidget.IsVisible, v => C.Overlay.RecapWidget.IsVisible = v);

            const string Overlay = "Appearance";
            Toggle(Overlay, "Show widgets", "Off: nothing is drawn on screen, but Aether still reads the game and shares damage, parts and ailments with your party. "
                + "For friends who only want to help with party sync. Turn it back on any time, everything is where you left it.",
                () => C.Overlay.ShowWidgets, v => C.Overlay.ShowWidgets = v);
            Choice(Overlay, "Overlay size", "Scales every widget. Scroll over a widget while editing the layout to size it on its own.",
                Config.OverlayConfig.UiScales.Select(s => (s.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture), $"{s * 100:0}%")).ToArray(),
                () => C.Overlay.UiScale.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture),
                v => C.Overlay.UiScale = float.Parse(v, System.Globalization.CultureInfo.InvariantCulture));
            Choice(Overlay, "Background shading", "The soft dark layer behind widget text. Normal keeps text readable over bright skies and snow.",
                new[] { ("Normal", "Normal"), ("Light", "Light"), ("Off", "Off") },
                () => C.Overlay.Shading, v => C.Overlay.Shading = v);
            Toggle(Overlay, "Colorblind-friendly colors", "Player colors that stay distinct with red-green color blindness.",
                () => C.Overlay.ColorblindColors, v => C.Overlay.ColorblindColors = v);
            Toggle(Overlay, "Hide when the game isn't focused", "Widgets disappear while you're alt-tabbed, so they don't cover other windows.",
                () => C.Overlay.HideWhenGameWindowIsInactive, v => C.Overlay.HideWhenGameWindowIsInactive = v);
            Toggle(Overlay, "Hide while a game menu is open", "Widgets disappear while the map or a menu is open, so they don't cover it.",
                () => C.Overlay.HideWhenGameMenuIsOpen, v => C.Overlay.HideWhenGameMenuIsOpen = v);

            const string Keyboard = "Keyboard";
            KeyRow(Keyboard, "Layout editor", "Opens and closes the layout editor in game. Click, then press the key you want; Esc keeps the current one.", InputControl.ManipulateWidget);
            KeyRow(Keyboard, "Hide overlay", "Clears the screen for screenshots or cutscenes. Click, then press the key you want; Esc keeps the current one.", InputControl.HideWidgets);
            Choice(Keyboard, "Hide overlay key", "Hide everything only while the key is held, or press once to hide and again to show.",
                new[] { ("Hold", "While held"), ("Toggle", "Press to toggle") },
                () => C.Overlay.HideKeyToggles ? "Toggle" : "Hold", v => C.Overlay.HideKeyToggles = v == "Toggle");

            const string Party = "Party sync";
            Toggle(Party, "Share data with your party", "Only the host's game has exact part HP and ailment buildup, and the game doesn't track damage on expeditions. "
                + "With this on, everyone running Aether shares those numbers through the SmartHunter sync server (hashed lobby ID, hunter names, damage, monster data).",
                () => C.Overlay.MonsterWidget.UseNetworkServer, v => C.Overlay.MonsterWidget.UseNetworkServer = v);

            const string Game = "Game";
            Toggle(Game, "Start the game with Aether", "Opens Monster Hunter: World through Steam when Aether starts, unless it's already running. If an update is downloading, the game starts once it's done.",
                () => C.StartMHWWhenSmartHunterStart, v => C.StartMHWWhenSmartHunterStart = v, true);
            Toggle(Game, "Open with the game", "Aether waits in the background after you sign in to Windows, with no window, and opens when Monster Hunter: World starts. "
                + "It closes with the game and waits for the next start. Close it during a hunt and it stays closed until the game restarts.",
                () => AutoStart.IsEnabled, v => AutoStart.Set(v));
            Toggle(Game, "Close with the game", "Aether quits when you close Monster Hunter: World.", () => C.ShutdownWhenProcessExits, v => C.ShutdownWhenProcessExits = v);
            Toggle(Game, "Discord Rich Presence", "Shows what you're doing on your Discord profile: your area, the monster you're fighting and its health, and how the quest ended.",
                () => C.DiscordPresence.Enabled, v => C.DiscordPresence.Enabled = v);
            Toggle(Game, "Back up saves when the game closes", "Zips your Monster Hunter: World saves into UserDataBackup next to Aether, in case a save gets corrupted. Keeps the newest 30.",
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

            const string Troubleshooting = "Troubleshooting";
            Toggle(Troubleshooting, "Software rendering", "Turn on if widgets flicker, show black boxes or don't appear (some AMD and older graphics drivers). Uses a bit more CPU.",
                () => C.UseSoftwareRendering, v => C.UseSoftwareRendering = v, true);
            Toggle(Troubleshooting, "Debug widget", "Session, lobby and weapon info, for bug reports.", () => C.Overlay.DebugWidget.IsVisible, v => C.Overlay.DebugWidget.IsVisible = v);
            Toggle(Troubleshooting, "Sync server log", "Writes every party sync request to the Log tab.", () => C.Debug.ShowServerLogs, v => C.Debug.ShowServerLogs = v);

            const string About = "About";
            const string Repo = "https://github.com/noahwaseaten/Aether";
            string version = Core.Helpers.AppUpdater.CurrentVersion.ToString(3);
            string folder = FileContainer.GetFullPath().TrimEnd('\\');
            Settings.Add(new Setting(About, "Version", "What changed in each version, on GitHub.", "Release notes",
                new Command(_ => Open(Repo + "/releases"))) { Detail = $"Aether {version} · {WindowsVersion()}" });
            Toggle(About, "Update automatically", "Downloads new versions from GitHub when Aether starts and shows what changed. "
                + "If the game isn't running yet, Aether restarts into it after a short countdown you can cancel; otherwise you choose when.",
                () => C.AutomaticallyCheckAndDownloadUpdates, v => C.AutomaticallyCheckAndDownloadUpdates = v);
            Setting check = null;
            check = new Setting(About, "Check for updates", "Looks for a newer version on GitHub now.", "Check now", new Command(async _ =>
            {
                if (check.ActionLabel == "Checking…")
                    return;
                check.ActionLabel = "Checking…";
                bool upToDate = await UpdateViewModel.Instance.CheckAsync(false);
                check.ActionLabel = upToDate ? "Up to date" : "Check now";
            }));
            Settings.Add(check);
            Settings.Add(new Setting(About, "Aether folder", "Settings, the log (Log.txt) and save backups live here.", "Open",
                new Command(_ => Open(folder))) { Detail = folder });
            Settings.Add(new Setting(About, "Report a bug", "Opens a new GitHub issue with your Aether and Windows versions filled in. Attaching Log.txt from the Aether folder helps a lot.", "Report…",
                new Command(_ => Open($"{Repo}/issues/new?body=" + Uri.EscapeDataString($"**What happened:**\n\n**What you expected:**\n\n---\nAether {version}, {WindowsVersion()}"))))
                { Detail = "Bugs, wrong numbers, widgets in the wrong place" });

            void Depends(string group, string child, string parent) =>
                Settings.First(x => x.Group == group && x.Name == child).DependsOn(Settings.First(x => x.Group == group && x.Name == parent));
            Depends(Monster, "Always show parts", "Parts");
            Depends(Monster, "Always show status effects", "Status effects");
            Depends(Monster, "Pulse active statuses", "Status effects");

            foreach (var name in Settings.Select(x => x.Group).Distinct())
            {
                string category = name;
                Categories.Add(new SettingsCategory(category, new Command(_ => Select(category))));
            }
            LogCategory = new SettingsCategory("Log", new Command(_ => ShowLog()));
            m_Selected = Categories.First().Name;
            Refresh();
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
