using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using Microsoft.Win32;

namespace SmartHunter.Core.Helpers
{
    // Aether.exe is the whole app: run it from anywhere and it installs itself to %LocalAppData%\Aether, adds a Start Menu
    // shortcut (never a Desktop one) and shows up in Settings > Apps, where Uninstall removes it in one step.
    // No admin rights: everything is per user.
    public static class Installer
    {
        const string ShortcutName = "Aether.lnk";
        const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\Aether";
        const string Repo = "https://github.com/noahwaseaten/Aether";

        static string Exe => Assembly.GetEntryAssembly().Location;
        static string InstalledExe => Path.Combine(FileContainer.InstallFolder, "Aether.exe");
        static string StartMenuShortcut => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), ShortcutName);
        static string DesktopShortcut => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), ShortcutName);

        // Running from Downloads or anywhere else: copy this exe into place (unless a newer one is there already) and start
        // that one with the same arguments. True when this copy should just exit.
        public static bool InstallAndHandOver(string[] args)
        {
            if (FileContainer.IsPortable || FileContainer.IsSameFolder(FileContainer.ExeFolder, FileContainer.InstallFolder))
            {
                return false;
            }
            try
            {
                FileContainer.GetFullPath(); // creates the folder and brings this copy's settings along
                var mine = Assembly.GetEntryAssembly().GetName().Version;
                Version installed = null;
                if (File.Exists(InstalledExe) && Version.TryParse(FileVersionInfo.GetVersionInfo(InstalledExe).FileVersion, out var v))
                {
                    installed = v;
                }
                if (installed == null || installed < mine)
                {
                    try
                    {
                        File.Copy(Exe, InstalledExe, true);
                    }
                    catch (IOException)
                    {
                        // The installed copy is running: leave it, it updates itself
                    }
                }
                // An update of this copy left its "What's new" here; the installed copy is the one that shows it
                if (File.Exists(Exe + ".notes"))
                {
                    File.Copy(Exe + ".notes", InstalledExe + ".notes", true);
                    File.Delete(Exe + ".notes");
                }
                Process.Start(new ProcessStartInfo(InstalledExe, string.Join(" ", args.Select(a => a.Contains(' ') ? $"\"{a}\"" : a))) { UseShellExecute = false });
                return true;
            }
            catch (Exception ex)
            {
                Log.WriteLine($"Couldn't install Aether to {FileContainer.InstallFolder}, running from here instead: {ex.Message}");
                return false;
            }
        }

        // Every normal start of the installed copy: keep the Start Menu shortcut, the Apps entry and the startup entry
        // pointing at this exe. Cheap: each is only written when it's missing or out of date.
        public static void Register()
        {
            if (FileContainer.IsPortable)
            {
                return;
            }
            Try("Start Menu shortcut", () => EnsureShortcut(StartMenuShortcut));
            Try("Apps entry", WriteUninstallEntry);
            Try("startup entry", AutoStart.RefreshPath);
            // The old installer's uninstall script; Settings > Apps does that now
            Try("old uninstaller", () => File.Delete(Path.Combine(FileContainer.InstallFolder, "Uninstall Aether.cmd")));
        }

        static void Try(string what, Action action)
        {
            try { action(); }
            catch (Exception ex) { Log.WriteLine($"Couldn't update the {what}: {ex.Message}"); }
        }

        static void EnsureShortcut(string path)
        {
            if (File.Exists(path) && string.Equals(ShellLink.Target(path), Exe, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
            ShellLink.Save(path, Exe, "Monster Hunter: World overlay");
        }

        static void WriteUninstallEntry()
        {
            string version = AppUpdater.CurrentVersion.ToString(3);
            using (var key = Registry.CurrentUser.CreateSubKey(UninstallKey))
            {
                if ((key.GetValue("DisplayVersion") as string) == version && (key.GetValue("DisplayIcon") as string) == Exe)
                {
                    return;
                }
                key.SetValue("DisplayName", "Aether");
                key.SetValue("DisplayVersion", version);
                key.SetValue("Publisher", "noahwaseaten");
                key.SetValue("DisplayIcon", Exe);
                key.SetValue("InstallLocation", Path.GetDirectoryName(Exe));
                key.SetValue("UninstallString", $"\"{Exe}\" --uninstall");
                key.SetValue("URLInfoAbout", Repo);
                key.SetValue("HelpLink", Repo + "/issues");
                key.SetValue("NoModify", 1, RegistryValueKind.DWord);
                key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                key.SetValue("EstimatedSize", (int)(new FileInfo(Exe).Length / 1024), RegistryValueKind.DWord);
            }
        }

        // From Settings > Apps > Uninstall. One question, then everything goes except the save backups.
        public static void Uninstall()
        {
            string backups = Path.Combine(FileContainer.InstallFolder, "UserDataBackup");
            bool hasBackups = Directory.Exists(backups) && Directory.EnumerateFiles(backups).Any();
            string message = "Remove Aether and its settings?" + (hasBackups ? "\n\nYour save backups stay in " + backups + "." : "");
            if (MessageBox.Show(message, "Uninstall Aether", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            {
                return;
            }

            int me = Process.GetCurrentProcess().Id;
            foreach (var other in Process.GetProcessesByName("Aether").Where(p => p.Id != me))
            {
                try { other.Kill(); other.WaitForExit(3000); } catch { }
            }

            TryQuiet(() => Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false));
            TryQuiet(() => AutoStart.Set(false));
            TryQuiet(() => File.Delete(StartMenuShortcut));
            // Only the one the old installer made, pointing here; any other Aether shortcut is the user's own
            TryQuiet(() => { if (PointsHere(DesktopShortcut)) File.Delete(DesktopShortcut); });

            string folder = FileContainer.InstallFolder.TrimEnd('\\');
            foreach (var path in Directory.GetFileSystemEntries(folder))
            {
                if (FileContainer.IsSameFolder(path, backups) || string.Equals(path, Exe, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                TryQuiet(() => { if (Directory.Exists(path)) Directory.Delete(path, true); else File.Delete(path); });
            }

            // A running exe can't be deleted, but it can be moved: park it in Temp (Windows clears that out) so the folder
            // can go. No command shell: "runs cmd.exe to delete itself" is what got Aether flagged as malware.
            TryQuiet(() => File.Move(Exe, Path.Combine(Path.GetTempPath(), "Aether-removed-" + Guid.NewGuid().ToString("N") + ".exe")));
            if (!hasBackups)
            {
                TryQuiet(() => Directory.Delete(folder, true));
            }
        }

        static bool PointsHere(string shortcut) =>
            File.Exists(shortcut) && string.Equals(ShellLink.Target(shortcut), Exe, StringComparison.OrdinalIgnoreCase);

        static void TryQuiet(Action action)
        {
            try { action(); } catch { }
        }
    }
}
