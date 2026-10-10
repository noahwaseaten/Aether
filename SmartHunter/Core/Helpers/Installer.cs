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
            // Windows' "downloaded from the internet" mark: without this it can warn on every start
            Try("download mark", () => File.Delete(Exe + ":Zone.Identifier"));
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
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            object shell = Activator.CreateInstance(shellType);
            object link = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { path });
            var linkType = link.GetType();
            if (File.Exists(path) && string.Equals((string)linkType.InvokeMember("TargetPath", BindingFlags.GetProperty, null, link, null), Exe, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
            linkType.InvokeMember("TargetPath", BindingFlags.SetProperty, null, link, new object[] { Exe });
            linkType.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, link, new object[] { Path.GetDirectoryName(Exe) });
            linkType.InvokeMember("Description", BindingFlags.SetProperty, null, link, new object[] { "Monster Hunter: World overlay" });
            linkType.InvokeMember("Save", BindingFlags.InvokeMethod, null, link, null);
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

            // A running exe can't delete itself: a hidden command does it once this one has exited
            string cleanup = hasBackups ? $"del /f /q \"{Exe}\"" : $"rmdir /s /q \"{folder}\"";
            Process.Start(new ProcessStartInfo("cmd.exe", $"/c timeout /t 2 /nobreak >nul & {cleanup}") { CreateNoWindow = true, UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden });
        }

        static bool PointsHere(string shortcut)
        {
            if (!File.Exists(shortcut))
            {
                return false;
            }
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            object link = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, Activator.CreateInstance(shellType), new object[] { shortcut });
            return string.Equals((string)link.GetType().InvokeMember("TargetPath", BindingFlags.GetProperty, null, link, null), Exe, StringComparison.OrdinalIgnoreCase);
        }

        static void TryQuiet(Action action)
        {
            try { action(); } catch { }
        }
    }
}
