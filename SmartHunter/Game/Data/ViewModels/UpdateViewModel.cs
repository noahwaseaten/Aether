using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using SmartHunter.Core;
using SmartHunter.Core.Data;
using SmartHunter.Core.Helpers;
using SmartHunter.Game.Helpers;

namespace SmartHunter.Game.Data.ViewModels
{
    // The update card at the top of the Aether window: what is being downloaded, how far along it is,
    // what changed, and when Aether will switch over.
    public class UpdateViewModel : Bindable
    {
        public static UpdateViewModel Instance { get; } = new UpdateViewModel();

        // "Start the game with Aether" waits this long for the startup update, so a restart can't race the game launch
        const int MaxGameLaunchHoldSeconds = 30;

        bool m_IsVisible;
        public bool IsVisible { get { return m_IsVisible; } set { SetProperty(ref m_IsVisible, value); } }

        string m_Title;
        public string Title { get { return m_Title; } set { SetProperty(ref m_Title, value); } }

        string m_Message;
        public string Message { get { return m_Message; } set { SetProperty(ref m_Message, value); } }

        string m_Notes;
        public string Notes
        {
            get { return m_Notes; }
            set
            {
                SetProperty(ref m_Notes, value);
                NotifyPropertyChanged(nameof(HasNotes));
                NotifyPropertyChanged(nameof(NoteSections));
            }
        }

        public class NoteSection
        {
            public string Version { get; set; }
            public string Text { get; set; }
        }

        // One section per version, so each gets its own heading in the card
        public List<NoteSection> NoteSections
        {
            get
            {
                var sections = new List<NoteSection>();
                if (string.IsNullOrEmpty(m_Notes))
                    return sections;
                var current = new NoteSection();
                var lines = new List<string>();
                void Flush()
                {
                    current.Text = string.Join("\n", lines).Trim();
                    if (current.Version != null || current.Text.Length > 0)
                        sections.Add(current);
                    lines.Clear();
                }
                foreach (var line in m_Notes.Split('\n'))
                {
                    if (line.StartsWith("## "))
                    {
                        Flush();
                        current = new NoteSection { Version = "Aether " + line.Substring(3).Trim() };
                    }
                    else
                    {
                        lines.Add(line);
                    }
                }
                Flush();
                return sections;
            }
        }
        public bool HasNotes => !string.IsNullOrEmpty(m_Notes);

        bool m_IsDownloading;
        public bool IsDownloading { get { return m_IsDownloading; } set { SetProperty(ref m_IsDownloading, value); } }

        double m_Progress;
        public double Progress { get { return m_Progress; } set { SetProperty(ref m_Progress, value); } }

        bool m_CanRestart;
        public bool CanRestart { get { return m_CanRestart; } set { SetProperty(ref m_CanRestart, value); } }

        bool m_CanRetry;
        public bool CanRetry { get { return m_CanRetry; } set { SetProperty(ref m_CanRetry, value); } }

        string m_DismissLabel;
        public string DismissLabel { get { return m_DismissLabel; } set { SetProperty(ref m_DismissLabel, value); NotifyPropertyChanged(nameof(CanDismiss)); } }
        public bool CanDismiss => m_DismissLabel != null;

        public Command RestartCommand { get; } = new Command(_ => AppUpdater.Restart());
        public Command DismissCommand { get; }
        public Command RetryCommand { get; }

        bool m_IsBusy;
        DateTime m_BusySince;
        bool m_GameLaunchReleased;
        bool m_CountdownCancelled = true;
        string m_InstalledVersion;

        UpdateViewModel()
        {
            DismissCommand = new Command(_ =>
            {
                // "Not now" on the restart countdown keeps the card, just without the countdown
                if (!m_CountdownCancelled)
                {
                    m_CountdownCancelled = true;
                    ShowReady(m_InstalledVersion, Notes);
                    return;
                }
                IsVisible = false;
            });
            RetryCommand = new Command(async _ => await CheckAsync(false));
        }

        public void Start()
        {
            AppUpdater.DeleteLeftovers();

            var notes = AppUpdater.TakeInstalledNotes();
            if (notes != null)
            {
                string version = AppUpdater.CurrentVersion.ToString(3);
                Log.WriteLine($"Now running Aether {version}");
                Show($"Updated to Aether {version}", "Here's what changed.", notes, dismiss: "Got it");
            }

            if (ConfigHelper.Main.Values.AutomaticallyCheckAndDownloadUpdates)
            {
                var _ = CheckAsync(true);
            }
        }

        // Called by the game launcher; false means "wait, an update may restart Aether in a moment"
        public bool TryReleaseGameLaunch()
        {
            if (m_IsBusy && (DateTime.Now - m_BusySince).TotalSeconds < MaxGameLaunchHoldSeconds)
            {
                return false;
            }
            m_GameLaunchReleased = true;
            return true;
        }

        // Returns true when Aether is up to date
        public async Task<bool> CheckAsync(bool atStartup)
        {
            if (m_IsBusy)
            {
                return false;
            }
            m_IsBusy = true;
            m_BusySince = DateTime.Now;

            AppUpdater.Release release = null;
            try
            {
                release = await AppUpdater.FindUpdateAsync();
                if (release == null)
                {
                    Log.WriteLine($"Aether {AppUpdater.CurrentVersion.ToString(3)} is up to date");
                    return true;
                }

                string version = release.Version.ToString(3);
                Log.WriteLine($"Downloading Aether {version}");
                Show($"Downloading Aether {version}", null, release.Notes, dismiss: null);
                IsDownloading = true;
                Progress = 0;
                var progress = new Progress<long>(bytes =>
                {
                    if (!IsDownloading)
                        return; // a report queued after the download finished
                    Progress = release.Size > 0 ? Math.Min(1.0, (double)bytes / release.Size) : 0;
                    Message = release.Size > 0 ? $"{bytes / 1048576.0:0.0} of {release.Size / 1048576.0:0.0} MB" : $"{bytes / 1048576.0:0.0} MB";
                });
                await AppUpdater.InstallAsync(release, progress);
                IsDownloading = false;
                Log.WriteLine($"Aether {version} is installed");

                // Switch over right away only if that can't disturb anything: at startup, before Aether started
                // the game and while the game isn't running. Otherwise the player picks the moment.
                if (atStartup && !m_GameLaunchReleased && !IsGameRunning())
                {
                    // A countdown the player can read, skip or cancel, instead of vanishing mid-sentence
                    Show($"Aether {version} is installed", null, release.Notes, dismiss: "Not now");
                    CanRestart = true;
                    m_InstalledVersion = version;
                    m_CountdownCancelled = false;
                    for (int seconds = 8; seconds > 0 && !m_CountdownCancelled; seconds--)
                    {
                        Message = $"Restarting into it in {seconds} s…";
                        await Task.Delay(1000);
                    }
                    if (!m_CountdownCancelled)
                    {
                        AppUpdater.Restart();
                    }
                    m_CountdownCancelled = true;
                }
                else
                {
                    ShowReady(version, release.Notes);
                }
                return false;
            }
            catch (Exception ex)
            {
                IsDownloading = false;
                Log.WriteLine($"Update failed: {ex.Message}");
                // An offline startup check isn't worth a card; a failed download or a manual check is
                if (release != null || !atStartup)
                {
                    Show("Couldn't update Aether", $"{(release == null ? "Couldn't reach GitHub" : ex.Message)}. This version keeps working; Aether tries again next start.", null, dismiss: "Dismiss");
                    CanRetry = true;
                }
                return false;
            }
            finally
            {
                m_IsBusy = false;
            }
        }

        void ShowReady(string version, string notes)
        {
            Show($"Aether {version} is ready", "Restart Aether to use it, or keep going: it switches over the next time Aether starts.", notes, dismiss: "Later");
            CanRestart = true;
        }

        void Show(string title, string message, string notes, string dismiss)
        {
            Title = title;
            Message = message;
            Notes = notes;
            DismissLabel = dismiss;
            CanRestart = false;
            CanRetry = false;
            IsVisible = true;
        }

        static bool IsGameRunning()
        {
            var processes = Process.GetProcessesByName(ConfigHelper.Memory.Values.ProcessName);
            foreach (var p in processes)
                p.Dispose();
            return processes.Length > 0;
        }
    }
}
