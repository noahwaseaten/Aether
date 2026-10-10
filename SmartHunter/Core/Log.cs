using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Security.AccessControl;

namespace SmartHunter.Core
{
    public static class Log
    {
        // Next to the exe (not the working folder a shortcut picks)
        static readonly string s_FileName = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Log.txt");
        static bool s_Trimmed;

        public static event EventHandler<GenericEventArgs<string>> LineReceived;

        public static void WriteLine(string message)
        {
            string line = String.Format("[{0:yyyy-MM-dd HH:mm:ss}] {1}", DateTime.Now, message);
            Console.WriteLine(line);

            if (LineReceived != null)
            {
                LineReceived(null, new GenericEventArgs<string>(line));
            }

            bool isDesignInstance = LicenseManager.UsageMode == LicenseUsageMode.Designtime;
            if (!isDesignInstance)
            {
                try
                {
                    if (!s_Trimmed)
                    {
                        s_Trimmed = true;
                        try { KeepPreviousSession(); }
                        catch (Exception) { } // the copy we're replacing may still hold the file; then this session appends to it
                    }
                    using (FileStream fileStream = new FileStream(s_FileName, FileMode.OpenOrCreate, FileSystemRights.AppendData, FileShare.Write, 4096, FileOptions.None))
                    {
                        using (StreamWriter streamWriter = new StreamWriter(fileStream))
                        {
                            streamWriter.AutoFlush = true;
                            streamWriter.WriteLine(line);
                        }
                    }
                }
                catch (Exception) { }
            }
        }

        // Log.txt is this session. The last sessions move to Logs\, named by when they ended, so a problem can still be
        // traced after Aether restarts (an update restarts it mid-session).
        const int KeptSessions = 10;
        static void KeepPreviousSession()
        {
            var info = new FileInfo(s_FileName);
            if (!info.Exists)
            {
                return;
            }
            string folder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");
            Directory.CreateDirectory(folder);
            string target = Path.Combine(folder, $"Log {info.LastWriteTime:yyyy-MM-dd HH-mm-ss}.txt");
            if (File.Exists(target))
            {
                File.Delete(target);
            }
            info.MoveTo(target);
            foreach (var old in new DirectoryInfo(folder).GetFiles("Log *.txt").OrderByDescending(f => f.Name).Skip(KeptSessions))
            {
                old.Delete();
            }
        }

        public static void WriteException(Exception exception)
        {
            WriteLine($"{exception.GetType().Name}: {exception.Message}\r\n{exception.StackTrace}");
        }
    }
}
