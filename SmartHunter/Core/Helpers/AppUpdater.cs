using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace SmartHunter.Core.Helpers
{
    // Updates from the GitHub releases of noahwaseaten/Aether: if a release tag (v1.2.3) is newer than this exe,
    // download its Aether.exe asset and swap it in. Windows lets a running exe be renamed but not overwritten,
    // so the running one becomes Aether.exe.old and is deleted on the next start.
    public static class AppUpdater
    {
        const string ReleasesUrl = "https://api.github.com/repos/noahwaseaten/Aether/releases?per_page=30";
        const string AssetName = "Aether.exe";

        public class Release
        {
            public Version Version;
            public string Notes; // every release newer than this exe, newest first; with several, each starts with a "## 1.2.3" line
            public string Url;
            public string Digest;
            public long Size;
        }

        public static Version CurrentVersion => Assembly.GetEntryAssembly().GetName().Version;

        static string ExePath => Assembly.GetEntryAssembly().Location;

        // Written before the swap, read by the new version on its first start to say what changed
        static string NotesPath => ExePath + ".notes";

        public static void DeleteLeftovers()
        {
            TryDelete(ExePath + ".new");
            foreach (var old in Directory.GetFiles(Path.GetDirectoryName(ExePath), Path.GetFileName(ExePath) + ".old*"))
            {
                TryDelete(old);
            }
        }

        // Where the running exe goes during an update. Aether.exe.old can still be running too (the "Open with the game"
        // copy that waited through the last update), and Windows won't delete or replace a running exe: moving onto it
        // failed with "Cannot create a file when that file already exists". A free name always works.
        static string FreeOldPath(string exe)
        {
            TryDelete(exe + ".old");
            return File.Exists(exe + ".old") ? exe + ".old-" + DateTime.Now.Ticks : exe + ".old";
        }

        static HttpClient CreateClient()
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Aether-Updater");
            return client;
        }

        // Null when already up to date
        public static async Task<Release> FindUpdateAsync()
        {
            using (var client = CreateClient())
            using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15)))
            {
                var response = await client.GetAsync(ReleasesUrl, cts.Token);
                response.EnsureSuccessStatusCode();
                var newer = JArray.Parse(await response.Content.ReadAsStringAsync())
                    .Where(r => !(bool)r["draft"] && !(bool)r["prerelease"])
                    .Select(r => (Json: r, Ok: TryParseTag((string)r["tag_name"], out var v), Version: v))
                    .Where(r => r.Ok && r.Version > CurrentVersion)
                    .OrderByDescending(r => r.Version)
                    .ToList();

                var latest = newer.FirstOrDefault();
                var asset = latest.Json?["assets"]?.FirstOrDefault(a => (string)a["name"] == AssetName);
                if (asset == null)
                {
                    if (latest.Json != null)
                        Log.WriteLine($"Release {latest.Version.ToString(3)} has no {AssetName}, skipping the update");
                    return null;
                }

                return new Release
                {
                    Version = latest.Version,
                    Notes = string.Join("\n\n", newer.Select(r => newer.Count > 1 ? $"## {r.Version.ToString(3)}\n{CleanNotes((string)r.Json["body"])}" : CleanNotes((string)r.Json["body"]))),
                    Url = (string)asset["browser_download_url"],
                    Digest = (string)asset["digest"],
                    Size = (long?)asset["size"] ?? 0
                };
            }
        }

        // Downloads, verifies and swaps in the new exe; it runs on the next start
        public static async Task InstallAsync(Release release, IProgress<long> progress)
        {
            byte[] bytes;
            using (var client = CreateClient())
            using (var response = await client.GetAsync(release.Url, HttpCompletionOption.ResponseHeadersRead))
            {
                response.EnsureSuccessStatusCode();
                using (var stream = await response.Content.ReadAsStreamAsync())
                using (var memory = new MemoryStream())
                {
                    var buffer = new byte[81920];
                    int read;
                    while ((read = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                    {
                        memory.Write(buffer, 0, read);
                        progress?.Report(memory.Length);
                    }
                    bytes = memory.ToArray();
                }
            }

            if (bytes.Length < 64 * 1024 || bytes[0] != 'M' || bytes[1] != 'Z')
            {
                throw new InvalidDataException("the download isn't a Windows program");
            }

            // GitHub publishes a SHA-256 for every release asset ("sha256:<hex>")
            if (release.Digest != null && release.Digest.StartsWith("sha256:"))
            {
                using (var sha = SHA256.Create())
                {
                    string actual = BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "");
                    if (!actual.Equals(release.Digest.Substring(7), StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidDataException("the download is corrupt (checksum mismatch)");
                    }
                }
            }

            string exe = ExePath;
            File.WriteAllBytes(exe + ".new", bytes);
            File.Move(exe, FreeOldPath(exe));
            File.Move(exe + ".new", exe);
            File.WriteAllText(NotesPath, release.Version.ToString(3) + "\n" + release.Notes, Encoding.UTF8);
        }

        // The notes saved by the update that installed this version, once; null otherwise
        public static string TakeInstalledNotes()
        {
            try
            {
                if (!File.Exists(NotesPath))
                    return null;
                var text = File.ReadAllText(NotesPath, Encoding.UTF8);
                File.Delete(NotesPath);
                int newline = text.IndexOf('\n');
                return newline > 0 && text.Substring(0, newline) == CurrentVersion.ToString(3) ? text.Substring(newline + 1) : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        public static void Restart()
        {
            Log.WriteLine("Restarting Aether");
            App.ReleaseSingleInstance();
            Process.Start(ExePath);
            Environment.Exit(0);
        }

        // Release bodies are GitHub markdown; the app shows plain text
        internal static string CleanNotes(string body)
        {
            body = (body ?? "").Replace("\r", "");
            // Everything after a "---" line is the release page's footer (which file to download), not news
            int footer = ("\n" + body).IndexOf("\n---");
            if (footer >= 0)
                body = body.Substring(0, footer);
            if (string.IsNullOrWhiteSpace(body))
                return "No notes for this release.";
            var lines = body.Split('\n')
                .Select(l => l.Replace("**", "").Replace("`", "").TrimEnd())
                .Select(l => l.TrimStart().StartsWith("#") ? l.TrimStart('#', ' ') : l)
                .Select(l => l.TrimStart().StartsWith("- ") || l.TrimStart().StartsWith("* ") ? "• " + l.TrimStart().Substring(2) : l);
            return string.Join("\n", lines).Trim();
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
