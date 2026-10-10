using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Threading;
using SmartHunter.Core.Helpers;
using SmartHunter.Game.Data.ViewModels;
using SmartHunter.Game.Helpers;

namespace SmartHunter.Core
{
    public abstract class MemoryUpdater
    {
        enum State
        {
            None,
            WaitingForProcess,
            StartMHW,
            ProcessFound,
            FastPatternScanning,
            PatternScanning,
            PatternScanFailed,
            ServerChecking,
            Working
        }

        StateMachine<State> m_StateMachine;
        List<ThreadedMemoryScan> m_MemoryScans;
        List<ThreadedMemoryScan> m_FastMemoryScans;
        DispatcherTimer m_DispatcherTimer;

        protected abstract string ProcessName { get; }
        protected abstract BytePattern[] Patterns { get; }
        protected virtual string UserDataPath { get { return @"C:\Program Files (x86)\Steam\userdata"; } }
        protected virtual int ThreadsPerScan { get { return 2; } }
        protected virtual int UpdatesPerSecond { get { return 20; } }
        protected virtual bool ShutdownWhenProcessExits { get { return false; } }
        protected virtual bool BackupWhenProcessExits { get { return false; } }
        protected virtual bool StartMHWWhenSmartHunterStart { get { return false; } }

        protected Process Process { get; private set; }

        public MemoryUpdater()
        {
            CreateStateMachine();

            Initialize();

            m_DispatcherTimer = new DispatcherTimer();
            m_DispatcherTimer.Tick += Update;
            TryUpdateTimerInterval();
            m_DispatcherTimer.Start();
        }

        void CreateStateMachine()
        {
            m_StateMachine = new StateMachine<State>();

            m_StateMachine.Add(State.None, new StateMachine<State>.StateData(
                null,
                new StateMachine<State>.Transition[]
                {
                    new StateMachine<State>.Transition(
                        State.StartMHW,
                        () => !ConfigHelper.Main.Values.Overlay.MonsterWidget.UseNetworkServer,
                        () =>
                        {
                            Initialize();
                        }),
                    new StateMachine<State>.Transition(
                        State.ServerChecking,
                        () => ConfigHelper.Main.Values.Overlay.MonsterWidget.UseNetworkServer,
                        () => ServerManager.Instance.CheckAlive())
                }));

            m_StateMachine.Add(State.ServerChecking, new StateMachine<State>.StateData(
                null,
                new StateMachine<State>.Transition[]
                {
                    new StateMachine<State>.Transition(
                        State.StartMHW,
                        () => ServerManager.Instance.IsServerOline != 0,
                        () =>
                        {
                            Initialize();
                        })
                }));

            m_StateMachine.Add(State.StartMHW, new StateMachine<State>.StateData(
                null,
                new StateMachine<State>.Transition[]
                {
                    new StateMachine<State>.Transition(
                        State.WaitingForProcess,
                        () =>
                        {
                            if(CheckProcess())
                                return true;
                            if(!StartMHWWhenSmartHunterStart)
                            {
                                Log.WriteLine("Not starting the game (\"Start the game with Aether\" is off)");
                                return true;
                            }
                            // An update found at startup restarts Aether; launching first could start the game twice
                            if (!UpdateViewModel.Instance.TryReleaseGameLaunch())
                            {
                                if (!m_LoggedUpdateWait)
                                    Log.WriteLine("Starting the game once the update check is done");
                                m_LoggedUpdateWait = true;
                                return false;
                            }
                            Log.WriteLine("Starting the game");
                            try
                            {
                                Process.Start(new ProcessStartInfo
                                {
                                    FileName =  "steam://run/582010",
                                    Arguments = "",
                                    UseShellExecute = true
                                });
                                return true;
                            }
                            catch(Exception ex)
                            {
                                Log.WriteException(ex);
                                return false;
                            }
                        },
                        null)
                }));

            m_StateMachine.Add(State.WaitingForProcess, new StateMachine<State>.StateData(
                null,
                new StateMachine<State>.Transition[]
                {
                    new StateMachine<State>.Transition(
                        State.ProcessFound,
                        () =>
                        {
                            return CheckProcess();
                        },
                        null)
                }));


            m_StateMachine.Add(State.ProcessFound, new StateMachine<State>.StateData(
                null,
                new StateMachine<State>.Transition[]
                {
                    new StateMachine<State>.Transition(
                        State.WaitingForProcess,
                        () => Process.HasExited,
                        () => Initialize(true)),
                    new StateMachine<State>.Transition(
                        State.FastPatternScanning,
                        IsProcessReady,
                        () =>
                        {
                            // The title carries the game build, e.g. "MONSTER HUNTER: WORLD(421810)"
                            Log.WriteLine($"Game window: {Process.MainWindowTitle}");
                            m_FastMemoryScans.Clear();
                            foreach (var pattern in Patterns)
                            {
                                if (pattern.Config.LastResultAddress.Length > 0)
                                {
                                    if (MhwHelper.TryParseHex(pattern.Config.LastResultAddress, out var address))
                                    {
                                        var memoryScan = new ThreadedMemoryScan(Process, pattern, new AddressRange((ulong)address, (ulong)pattern.Bytes.Length), true, ThreadsPerScan);
                                        m_FastMemoryScans.Add(memoryScan);
                                    }
                                }
                            }
                        })
                }));

            m_StateMachine.Add(State.FastPatternScanning, new StateMachine<State>.StateData(
                null,
                new StateMachine<State>.Transition[]
                {
                    new StateMachine<State>.Transition(
                        State.WaitingForProcess,
                        () => Process.HasExited,
                        () => Initialize(true)),
                    new StateMachine<State>.Transition(
                        State.PatternScanning,
                        () =>
                        {
                            var completedScans = m_FastMemoryScans.Where(memoryScan => memoryScan.HasCompleted);
                            return completedScans.Count() == m_FastMemoryScans.Count();
                        },
                        () =>
                        {
                            var subPatterns = Patterns.Where(p => p.MatchedAddresses.Count() == 0);
                            if (subPatterns.Count() > 0)
                            {
                                AddressRange addressRange = new AddressRange((ulong)Process.MainModule.BaseAddress.ToInt64(), (ulong)Process.MainModule.ModuleMemorySize);
                                Log.WriteLine($"Base: 0x{addressRange.Start.ToString("X")}, End: 0x{addressRange.End.ToString("X")}, Size: 0x{addressRange.Size.ToString("X")}");

                                foreach (var pattern in subPatterns)
                                {
                                    var memoryScan = new ThreadedMemoryScan(Process, pattern, addressRange, true, ThreadsPerScan);
                                    m_MemoryScans.Add(memoryScan);
                                }
                            }
                        })
                }));

            m_StateMachine.Add(State.PatternScanning, new StateMachine<State>.StateData(
                null,
                new StateMachine<State>.Transition[]
                {
                    new StateMachine<State>.Transition(
                        State.Working,
                        () =>
                        {
                            var completedScans = m_MemoryScans.Where(memoryScan => memoryScan.HasCompleted);
                            if (completedScans.Count() == m_MemoryScans.Count())
                            {
                                var finishedWithResults = m_MemoryScans.Where(memoryScan => memoryScan.HasCompleted && memoryScan.Results.SelectMany(result => result.Matches).Any());
                                return finishedWithResults.Any() || m_MemoryScans.Count() == 0 || m_FastMemoryScans.Where(memoryScan => memoryScan.HasCompleted && memoryScan.Results.SelectMany(result => result.Matches).Any()).Any();
                            }

                            return false;
                        },
                        () =>
                        {
                            var failedMemoryScans = m_MemoryScans.Where(memoryScan => !memoryScan.Results.SelectMany(result => result.Matches).Any());
                            if (failedMemoryScans.Any())
                            {
                                string failedPatterns = String.Join(" ", failedMemoryScans.Select(failedMemoryScan => failedMemoryScan.Pattern.Config.Name));
                                Log.WriteLine($"Failed Patterns [{failedMemoryScans.Count()}/{m_MemoryScans.Count()}]: {failedPatterns}");
                                Log.WriteLine($"The application will continue to work but with limited functionalities...");
                                Problems.Report("patterns", "Aether couldn't find some of the game's data, so some widgets may stay empty. If the game just updated, Aether needs an update too.");
                                m_MemoryScans.RemoveAll(scan => failedMemoryScans.Contains(scan));
                            }
                            else
                            {
                                Problems.Clear("patterns");
                            }
                            ConfigHelper.Memory.Save(false);
                            m_MemoryScans.AddRange(m_FastMemoryScans.Where(f => f.Results.Where(r => r.Matches.Any()).Any()));
                            var orderedMatches = m_MemoryScans.SelectMany(memoryScan => memoryScan.Results.SelectMany(result => result.Matches)).OrderBy(match => match).ToList();
                            if (orderedMatches.Any())
                                Log.WriteLine($"Match Range: {orderedMatches.First():X} - {orderedMatches.Last():X}");
                        }),
                    new StateMachine<State>.Transition(
                        State.PatternScanFailed,
                        () =>
                        {
                            var completedScans = m_MemoryScans.Where(memoryScan => memoryScan.HasCompleted);
                            if (completedScans.Count() == m_MemoryScans.Count())
                            {
                                var finishedWithoutResults = m_MemoryScans.Where(memoryScan => !memoryScan.Results.SelectMany(result => result.Matches).Any());
                                return (finishedWithoutResults.Count() == m_MemoryScans.Count()) && m_FastMemoryScans.Where(memoryScan => memoryScan.HasCompleted && memoryScan.Results.SelectMany(result => result.Matches).Any()).Count() == 0;
                            }

                            return false;
                        },
                        () =>
                        {
                            Log.WriteLine("Couldn't find the game data. Retrying in 30 seconds; if this keeps happening, the game may have updated and Aether needs an update too.");
                            Problems.Report("patterns", "Aether can't find the game's data, so the overlay is empty. It retries every 30 seconds; if this keeps happening, the game may have updated and Aether needs an update too.");
                            m_ScanFailedTime = DateTime.Now;
                        }),
                    new StateMachine<State>.Transition(
                        State.WaitingForProcess,
                        () =>
                        {
                            return Process.HasExited;
                        },
                        () =>
                        {
                            Initialize(true);
                        })
                }));

            m_StateMachine.Add(State.PatternScanFailed, new StateMachine<State>.StateData(
                null,
                new StateMachine<State>.Transition[]
                {
                    new StateMachine<State>.Transition(
                        State.WaitingForProcess,
                        () => Process.HasExited,
                        () => Initialize(true)),
                    new StateMachine<State>.Transition(
                        State.ProcessFound,
                        () => (DateTime.Now - m_ScanFailedTime).TotalSeconds > 30,
                        ResetScans)
                }));

            m_StateMachine.Add(State.Working, new StateMachine<State>.StateData(
                () =>
                {
                    try
                    {
                        UpdateMemory();
                        if (m_FailedReadsInARow > 0)
                        {
                            m_FailedReadsInARow = 0;
                            Problems.Clear("reading");
                        }
                    }
                    catch (Exception ex)
                    {
                        LogThrottled(ex);
                        // One bad read during a loading screen is normal; failing for seconds on end is not
                        if (++m_FailedReadsInARow == 30)
                        {
                            Problems.Report("reading", "Aether keeps failing to read the game, so widgets may be frozen or empty. Restarting Aether usually helps.");
                        }
                    }
                },
                new StateMachine<State>.Transition[]
                {
                    new StateMachine<State>.Transition(
                        State.WaitingForProcess,
                        () =>
                        {
                            return Process.HasExited;
                        },
                        () =>
                        {
                            Initialize(true);
                        })
                }));
        }

        DateTime m_ScanFailedTime;
        bool m_LoggedUpdateWait;

        void ResetScans()
        {
            foreach (var memoryScan in (m_MemoryScans ?? new List<ThreadedMemoryScan>()).Concat(m_FastMemoryScans ?? new List<ThreadedMemoryScan>()))
            {
                memoryScan.TryCancel();
            }

            m_FastMemoryScans = new List<ThreadedMemoryScan>();
            m_MemoryScans = new List<ThreadedMemoryScan>();

            // Addresses from a previous game session would be read as if they were still valid
            foreach (var pattern in Patterns)
            {
                pattern.MatchedAddresses.Clear();
            }
        }

        // A just-launched game isn't readable yet: its module list is still loading (ReadProcessMemory
        // fails partway) and the exe is still unpacking, so scanning now finds nothing. Wait for its window.
        bool IsProcessReady()
        {
            try
            {
                Process.Refresh();
                return Process.MainWindowHandle != IntPtr.Zero && Process.MainModule.ModuleMemorySize > 0;
            }
            catch
            {
                return false;
            }
        }

        private void Initialize(bool processExited = false)
        {
            Process = null;

            ResetScans();

            OverlayViewModel.Instance.IsGameActive = false;
            if (!processExited || !(BackupWhenProcessExits || ShutdownWhenProcessExits))
            {
                return;
            }

            // The backup runs off the UI thread: zipping on it froze Aether ("Not responding") until it finished
            bool backup = BackupWhenProcessExits, shutdown = ShutdownWhenProcessExits;
            string userDataPath = UserDataPath;
            Log.WriteLine("The game closed" + (backup ? ", backing up its saves" : "") + (shutdown ? ", then closing Aether" : ""));
            var dispatcher = Application.Current.Dispatcher;
            System.Threading.Tasks.Task.Run(() =>
            {
                if (backup)
                {
                    BackupSaves(userDataPath);
                }
                if (shutdown)
                {
                    dispatcher.BeginInvoke(new Action(() => Application.Current.Shutdown()));
                }
            });
        }

        const string SteamAppId = "582010";
        const int BackupsKept = 30;

        // Zips only MHW's saves (userdata\<account>\582010), not every Steam game's data: the whole userdata folder
        // can be hundreds of MB. Keeps the newest backups next to Aether.
        internal static string BackupSaves(string userDataPath)
        {
            try
            {
                string root = (userDataPath ?? "").TrimEnd('\\', '/');
                var folders = new List<string>();
                if (Path.GetFileName(root) == SteamAppId && Directory.Exists(root))
                {
                    folders.Add(root);
                    root = Path.GetDirectoryName(root);
                }
                else if (Directory.Exists(root))
                {
                    folders.AddRange(Directory.GetDirectories(root).Select(account => Path.Combine(account, SteamAppId)).Where(Directory.Exists));
                    if (Directory.Exists(Path.Combine(root, SteamAppId)))
                        folders.Add(Path.Combine(root, SteamAppId));
                }
                if (!folders.Any())
                {
                    Log.WriteLine($"No Monster Hunter: World saves in {userDataPath}; check the Steam save folder in Settings");
                    Problems.Report("backup", "Your saves weren't backed up: Aether found no Monster Hunter: World saves in the Steam save folder. Check the folder in Settings.");
                    return null;
                }

                string backupFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "UserDataBackup");
                Directory.CreateDirectory(backupFolder);
                string zipFile = Path.Combine(backupFolder, DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss") + ".zip");
                using (var zip = ZipFile.Open(zipFile, ZipArchiveMode.Create))
                {
                    foreach (string file in folders.SelectMany(folder => Directory.GetFiles(folder, "*", SearchOption.AllDirectories)))
                    {
                        zip.CreateEntryFromFile(file, file.Substring(root.Length).TrimStart('\\', '/'));
                    }
                }
                Log.WriteLine($"Saves backed up to UserDataBackup\\{Path.GetFileName(zipFile)}");

                foreach (var old in new DirectoryInfo(backupFolder).GetFiles("*.zip").OrderByDescending(f => f.CreationTimeUtc).Skip(BackupsKept))
                {
                    old.Delete();
                }
                return zipFile;
            }
            catch (Exception ex)
            {
                Log.WriteLine($"Couldn't back up the saves: {ex.Message}");
                Problems.Report("backup", "Your saves couldn't be backed up when the game closed.");
                return null;
            }
        }

        private void Update(object sender, EventArgs e)
        {
            // Keep ticking after an error; stopping the timer would freeze the overlay until a restart
            try
            {
                m_StateMachine.Update();
            }
            catch (Exception ex)
            {
                LogThrottled(ex);
            }
        }

        // A bad read repeats every tick; log each distinct error once a minute instead of 20 times a second
        string m_LastError;
        DateTime m_LastErrorTime;
        int m_FailedReadsInARow;

        void LogThrottled(Exception ex)
        {
            string error = ex.GetType().Name + ex.Message + ex.TargetSite;
            if (error != m_LastError || (DateTime.Now - m_LastErrorTime).TotalSeconds > 60)
            {
                m_LastError = error;
                m_LastErrorTime = DateTime.Now;
                Log.WriteException(ex);
            }
        }

        abstract protected void UpdateMemory();

        protected void TryUpdateTimerInterval()
        {
            const int max = 60;
            const int min = 1;
            int clampedUpdatesPerSecond = Math.Min(Math.Max(UpdatesPerSecond, min), max); // TODO: Dynamic updates per second number based on game perfomance

            int targetMilliseconds = (int)(1000f / clampedUpdatesPerSecond);
            if (m_DispatcherTimer != null && m_DispatcherTimer.Interval.TotalMilliseconds != targetMilliseconds)
            {
                m_DispatcherTimer.Interval = new TimeSpan(0, 0, 0, 0, targetMilliseconds);
            }
        }

        private bool CheckProcess()
        {
            // Look the game up by name (as HunterPie does) instead of opening every process on the PC 20 times a second
            foreach (var p in Process.GetProcessesByName(ProcessName))
            {
                try
                {
                    if (Process == null && !p.HasExited)
                    {
                        Process = p;
                        continue;
                    }
                }
                catch
                {
                    // nothing here
                }
                p.Dispose();
            }
            return Process != null;
        }
    }
}
