using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using Microsoft.Win32;

namespace SmartHunter.Core.Helpers
{
    // "Open with the game": signing in to Windows starts Aether with --wait. That copy shows nothing and only
    // checks every few seconds for the game; once it starts, Aether opens as usual and closes with the game.
    // Windows can't start a program when another one starts without a resident watcher or admin rights.
    public static class AutoStart
    {
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string ValueName = "Aether";
        const string GameProcessName = "MonsterHunterWorld";

        static string Exe => Assembly.GetExecutingAssembly().Location;

        public static bool IsEnabled
        {
            get
            {
                try
                {
                    using (var key = Registry.CurrentUser.OpenSubKey(RunKey))
                    {
                        return key?.GetValue(ValueName) != null;
                    }
                }
                catch
                {
                    return false;
                }
            }
        }

        public static void Set(bool enabled)
        {
            using (var key = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (enabled)
                {
                    key.SetValue(ValueName, $"\"{Exe}\" --wait");
                }
                else
                {
                    key.DeleteValue(ValueName, false);
                }
            }
            if (enabled)
            {
                StartWaiter(); // works from now on, not only after the next sign-in
            }
        }

        // A game that's already running when the waiter starts has had its Aether (or the player closed it on purpose)
        public static void StartWaiter()
        {
            try
            {
                Process.Start(Exe, "--wait --skip-running");
            }
            catch (System.Exception ex)
            {
                Log.WriteLine($"Couldn't start waiting for the game: {ex.Message}");
            }
        }

        // Blocks until a game starts that no Aether is open for. False: turned off, or another copy already waits.
        public static bool WaitForGame(bool skipRunning)
        {
            using (var waiter = new Mutex(false, "Aether-MHW-Waiter"))
            {
                try
                {
                    if (!waiter.WaitOne(0))
                    {
                        return false;
                    }
                }
                catch (AbandonedMutexException)
                {
                }

                var seen = new HashSet<int>();
                bool first = true;
                var running = Assembly.GetExecutingAssembly().GetName().Version;
                while (IsEnabled)
                {
                    // Aether updated while this copy waited: wait from the new exe instead, so this one stops holding
                    // the old file (the next update has to move it out of the way)
                    if (IsUpdatedOnDisk(running))
                    {
                        waiter.ReleaseMutex();
                        StartWaiter();
                        return false;
                    }
                    foreach (var game in Process.GetProcessesByName(GameProcessName))
                    {
                        // Each game start counts once: if Aether was already open for it, or gets closed during it, stay out
                        if (seen.Add(game.Id) && !(first && skipRunning) && !IsAetherOpen())
                        {
                            waiter.ReleaseMutex();
                            return true;
                        }
                        game.Dispose();
                    }
                    first = false;
                    Thread.Sleep(3000);
                }
                waiter.ReleaseMutex();
                return false;
            }
        }

        static bool IsUpdatedOnDisk(Version running)
        {
            try
            {
                return Version.TryParse(FileVersionInfo.GetVersionInfo(Exe).FileVersion, out var onDisk) && onDisk != running;
            }
            catch
            {
                return false;
            }
        }

        static bool IsAetherOpen()
        {
            using (var mutex = new Mutex(false, App.SingleInstanceName))
            {
                bool owned;
                try
                {
                    owned = mutex.WaitOne(0);
                }
                catch (AbandonedMutexException)
                {
                    owned = true;
                }
                if (owned)
                {
                    mutex.ReleaseMutex();
                }
                return !owned;
            }
        }
    }
}
