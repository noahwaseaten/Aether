using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace SmartHunter.Core.Helpers
{
    // Updates from the latest GitHub release of noahwaseaten/Aether: if its tag (v1.2.3) is newer than this exe,
    // download its Aether.exe asset and swap it in. Windows lets a running exe be renamed but not overwritten,
    // so the running one becomes Aether.exe.old and is deleted on the next start.
    public static class AppUpdater
    {
        const string LatestReleaseUrl = "https://api.github.com/repos/noahwaseaten/Aether/releases/latest";
        const string AssetName = "Aether.exe";

        public static Version CurrentVersion => Assembly.GetEntryAssembly().GetName().Version;

        static string ExePath => Assembly.GetEntryAssembly().Location;

        public static void DeleteLeftovers()
        {
            TryDelete(ExePath + ".old");
            TryDelete(ExePath + ".new");
        }

        // Returns the installed version, or null when already up to date
        public static async Task<Version> DownloadLatestAsync()
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            using (var client = new HttpClient { Timeout = TimeSpan.FromSeconds(60) })
            {
                client.DefaultRequestHeaders.UserAgent.ParseAdd("Aether-Updater");

                var release = JObject.Parse(await client.GetStringAsync(LatestReleaseUrl));
                if (!TryParseTag((string)release["tag_name"], out var latest) || latest <= CurrentVersion)
                {
                    return null;
                }

                var asset = release["assets"]?.FirstOrDefault(a => (string)a["name"] == AssetName);
                if (asset == null)
                {
                    Log.WriteLine($"Release {latest} has no {AssetName}, skipping the update");
                    return null;
                }

                byte[] bytes = await client.GetByteArrayAsync((string)asset["browser_download_url"]);
                if (bytes.Length < 64 * 1024 || bytes[0] != 'M' || bytes[1] != 'Z')
                {
                    throw new InvalidDataException("The downloaded update is not a Windows program");
                }

                // GitHub publishes a SHA-256 for every release asset ("sha256:<hex>")
                string digest = (string)asset["digest"];
                if (digest != null && digest.StartsWith("sha256:"))
                {
                    using (var sha = SHA256.Create())
                    {
                        string actual = BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "");
                        if (!actual.Equals(digest.Substring(7), StringComparison.OrdinalIgnoreCase))
                        {
                            throw new InvalidDataException("The downloaded update is corrupt (checksum mismatch)");
                        }
                    }
                }

                string exe = ExePath;
                File.WriteAllBytes(exe + ".new", bytes);
                TryDelete(exe + ".old");
                File.Move(exe, exe + ".old");
                File.Move(exe + ".new", exe);
                return latest;
            }
        }

        public static void Restart()
        {
            App.ReleaseSingleInstance();
            Process.Start(ExePath);
            Environment.Exit(0);
        }

        internal static bool TryParseTag(string tag, out Version version)
        {
            version = null;
            if (tag == null || !Version.TryParse(tag.TrimStart('v', 'V'), out var parsed))
            {
                return false;
            }
            // "1.2" and "1.2.0.0" must compare equal to the exe's four-part version
            version = new Version(parsed.Major, parsed.Minor, Math.Max(parsed.Build, 0), Math.Max(parsed.Revision, 0));
            return true;
        }

        static void TryDelete(string path)
        {
            try { File.Delete(path); }
            catch (Exception) { } // still locked by a process that is exiting: next start cleans it up
        }
    }
}
