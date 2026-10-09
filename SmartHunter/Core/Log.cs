using System;
using System.ComponentModel;
using System.IO;
using System.Security.AccessControl;

namespace SmartHunter.Core
{
    public static class Log
    {
        // Next to the exe (not the working folder a shortcut picks), started fresh once it passes 1 MB
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
                        var info = new FileInfo(s_FileName);
                        if (info.Exists && info.Length > 1024 * 1024)
                        {
                            info.Delete();
                        }
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

        public static void WriteException(Exception exception)
        {
            WriteLine($"{exception.GetType().Name}: {exception.Message}\r\n{exception.StackTrace}");
        }
    }
}
