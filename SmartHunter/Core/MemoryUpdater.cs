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
                        () =>
                        {
                            Log.WriteLine("Checking the sync server...");
                            ServerManager.Instance.RequestCommadWithHandler(ServerManager.Command.ALIVE, null, null, false, 0, null, (result, ping) =>
                            {
                                if (result != null)
                                {
                                    if (result["status"].ToString().Equals("ok"))
                                    {
                                        Log.WriteLine($"Sync server is up ({ping} ms)");
                                        ServerManager.Instance.IsServerOline = 1;
                                    }
                                    else
                                    {
                                        if (result["result"].ToString().Equals("v"))
                                        {
                                            Log.WriteLine("The sync server no longer accepts this version. Party sync is off until Aether updates.");
                                        }
                                        else if (result["result"].ToString().Equals("dev"))
                                        {
                                            Log.WriteLine("The sync server is down for maintenance. Party sync is off for now.");
                                        }
                                        else
                                        {
                                            Log.WriteLine("The sync server returned an error. Party sync is off; restart Aether to try again. Everything else works without it.");
                                        }
                                        ServerManager.Instance.IsServerOline = -1;
                                    }
                                }
                                else
                                {
                                    Log.WriteLine("Couldn't reach the sync server. Party sync is off; restart Aether to try again. Everything else works without it.");
                                    ServerManager.Instance.IsServerOline = -1;
                                }
                                ServerManager.Instance.ResetStats();
                            }, (error) =>
                            {
                                Log.WriteLine("Couldn't reach the sync server. Party sync is off; restart Aether to try again. Everything else works without it.");
                                ServerManager.Instance.IsServerOline = -1;
                                ServerManager.Instance.ResetStats();
                            });
                        })
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
                                m_MemoryScans.RemoveAll(scan => failedMemoryScans.Contains(scan));
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
                    }
                    catch (Exception ex)
                    {
                        LogThrottled(ex);
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
            if (processExited && BackupWhenProcessExits)
            {
                try
                {
                    string zipPath = @"UserDataBackup\";
                    string fileName = DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss") + ".zip";
                    string filepath = Path.Combine(zipPath, fileName);
                    if (!Directory.Exists(zipPath))
                        Directory.CreateDirectory(zipPath);
                    if (File.Exists(filepath))
                        File.Delete(filepath);
                    if (Directory.Exists(UserDataPath))
                    {
                        ZipFile.CreateFromDirectory(UserDataPath, filepath);
                        Log.WriteLine("MonsterHunterWorld process exits. Start backup 'UserData'.");
                    }
                    else
                    {
                        Log.WriteLine("Backup fail 'UserData' path not exist.");
                    }
                }
                catch (Exception ex)
                {
                    Log.WriteException(ex);
                }
            }
            if (processExited && ShutdownWhenProcessExits)
            {
                Log.WriteLine("Process exited. Shutting down");
                Application.Current.Shutdown();
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
            var processes = Process.GetProcesses();
            foreach (var p in processes)
            {
                try
                {
                    if (p != null && p.ProcessName.Equals(ProcessName) && !p.HasExited)
                    {
                        Process = p;
                        return true;
                    }
                }
                catch
                {
                    // nothing here
                }
            }
            return false;
        }
    }
}
