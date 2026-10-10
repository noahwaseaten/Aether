using System;
using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;

namespace SmartHunter.Core
{
    public class FileContainer
    {
        FileSystemWatcher m_FileWatcher = null;

        public string FileName { get; private set; }

        public string FullPath
        {
            get
            {
                return GetFullPath();
            }
        }

        public string FullPathFileName
        {
            get
            {
                return GetFullPathFileName(FileName);
            }
        }

        public event EventHandler Changed;

        public FileContainer(string fileName)
        {
            FileName = fileName;

            bool isDesignInstance = LicenseManager.UsageMode == LicenseUsageMode.Designtime;
            if (!isDesignInstance)
            {
                WatchFile(true);
            }
        }

        public void TryChangeFileName(string fileName)
        {
            if (FileName == fileName)
            {
                return;
            }

            bool watching = m_FileWatcher != null;
            if (watching)
            {
                WatchFile(false);
            }

            FileName = fileName;

            OnChanged();
            if (Changed != null)
            {
                Changed(this, new EventArgs());
            }

            if (watching)
            {
                WatchFile(true);
            }
        }

        void WatchFile(bool watch)
        {
            if (watch && m_FileWatcher == null)
            {
                if (!String.IsNullOrEmpty(FileName))
                {
                    m_FileWatcher = new FileSystemWatcher();
                    m_FileWatcher.Path = FullPath;
                    m_FileWatcher.NotifyFilter = NotifyFilters.LastWrite;
                    m_FileWatcher.Filter = FileName;
                    m_FileWatcher.Changed += FileWatcher_Changed;
                    m_FileWatcher.EnableRaisingEvents = true;
                }
            }
            else if (!watch && m_FileWatcher != null)
            {
                m_FileWatcher.Changed -= FileWatcher_Changed;
                m_FileWatcher.Dispose();
                m_FileWatcher = null;
            }
        }

        async void FileWatcher_Changed(object sender, FileSystemEventArgs e)
        {
            TryPauseWatching();

            await Task.Delay(100);

            Application.Current.Dispatcher.Invoke(delegate
            {
                OnChanged();
                if (Changed != null)
                {
                    Changed(this, new EventArgs());
                }
            });

            TryUnpauseWatching();
        }

        protected void TryPauseWatching()
        {
            if (m_FileWatcher != null)
            {
                m_FileWatcher.EnableRaisingEvents = false;
            }
        }

        protected void TryUnpauseWatching()
        {
            if (m_FileWatcher != null)
            {
                m_FileWatcher.EnableRaisingEvents = true;
            }
        }

        virtual protected void OnChanged() { }

        // Settings, logs and save backups live in %LocalAppData%\Aether, so Aether.exe works on its own from anywhere
        // (Desktop, Downloads, a Start Menu shortcut). The self-test and copies with a portable.txt beside them keep
        // everything next to the exe, so test copies never touch the real settings.
        static readonly Lazy<string> s_DataFolder = new Lazy<string>(FindDataFolder);
        public static string GetFullPath() => s_DataFolder.Value; // with trailing slash

        public static bool IsPortable => GetFullPath() == ExeFolder;

        public static string ExeFolder => AppDomain.CurrentDomain.BaseDirectory;

        // Where Aether installs itself and keeps its data: per user, no admin needed
        public static string InstallFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Aether") + "\\";

        public static bool IsSameFolder(string a, string b) =>
            string.Equals(Path.GetFullPath(a).TrimEnd('\\'), Path.GetFullPath(b).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);

        static string FindDataFolder()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "--selftest") >= 0 || File.Exists(Path.Combine(ExeFolder, "portable.txt")))
            {
                return ExeFolder;
            }
            try
            {
                string data = InstallFolder;
                Directory.CreateDirectory(data);
                // A copy that kept its settings next to it (before 2.3) brings them along. Newer wins: an old installed copy's
                // settings must not beat the ones from the copy you've actually been using.
                string oldConfig = Path.Combine(ExeFolder, "Config.json"), newConfig = Path.Combine(data, "Config.json");
                if (!IsSameFolder(ExeFolder, data) && File.Exists(oldConfig)
                    && (!File.Exists(newConfig) || File.GetLastWriteTimeUtc(oldConfig) > File.GetLastWriteTimeUtc(newConfig)))
                {
                    File.Copy(oldConfig, newConfig, true);
                }
                return data;
            }
            catch (Exception)
            {
                return ExeFolder; // %LocalAppData% not writable: still run
            }
        }

        public static string GetFullPathFileName(string fileName)
        {
            return GetFullPath() + fileName;
        }
    }
}
