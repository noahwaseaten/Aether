using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using SmartHunter.Core;
using SmartHunter.Core.Helpers;
using SmartHunter.Game.Data;
using SmartHunter.Game.Data.ViewModels;
using SmartHunter.Game.Helpers;

namespace SmartHunter.Game
{
    // Discord Rich Presence over the local RPC pipe. This is the same unauthenticated desktop
    // transport the Discord Social SDK uses for Client::UpdateRichPresence, so no OAuth or SDK DLL is needed.
    // Addresses are for the final MHW:I build (421810), taken from HunterPie's address map.
    public class DiscordPresence
    {
        public static readonly DiscordPresence Instance = new DiscordPresence();

        const long ZoneAddress = 0x0500ECA0;            // + 0xAED0
        const long SaveAddress = 0x05013950;            // + 0xA8
        const long QuestAddress = 0x0500ED30;           // quest struct
        const long PlayerAddress = 0x050139A0;          // player root (hud, weapon)

        static readonly Dictionary<int, string> Zones = new Dictionary<int, string>
        {
            { 101, "Ancient Forest" }, { 102, "Wildspire Waste" }, { 103, "Coral Highlands" }, { 104, "Rotten Vale" },
            { 105, "Elder's Recess" }, { 106, "Great Ravine" }, { 107, "Great Ravine" }, { 108, "Hoarfrost Reach" },
            { 109, "Guiding Lands" }, { 201, "Special Arena" }, { 202, "Arena" }, { 203, "Seliana Supply Cache" },
            { 301, "Astera" }, { 302, "Astera Gathering Hub" }, { 303, "Research Base" }, { 305, "Seliana" },
            { 306, "Seliana Gathering Hub" }, { 401, "Prologue" }, { 403, "Everstream" }, { 405, "Confluence of Fates" },
            { 406, "Ancient Forest" }, { 409, "Caverns of El Dorado" }, { 411, "Seliana Supply Cache" }, { 412, "Origin Isle" },
            { 413, "Origin Isle" }, { 415, "Secluded Valley" }, { 416, "Secluded Valley" }, { 417, "Castle Schrade" },
            { 501, "Living Quarters" }, { 502, "Private Quarters" }, { 503, "Private Suite" }, { 504, "Training Area" },
            { 505, "Chamber of Five" }, { 506, "Seliana Room" },
        };
        static readonly Ui.Converters.WeaponTypeToNameConverter s_WeaponNames = new Ui.Converters.WeaponTypeToNameConverter();
        static readonly HashSet<int> Towns = new HashSet<int> { 301, 302, 303, 305, 306, 501, 502, 503, 506 };

        readonly object m_Lock = new object();
        object m_Activity;            // latest activity computed on the UI thread
        DateTime m_LastUpdate;        // last time the game fed us data
        DateTime m_LastBuilt;

        // Edge-detection state
        int m_LastZone = -1, m_LastQuestState = -1, m_LastSharpness = -1, m_LastWeaponId = -1;
        float m_LastPlayerHp = -1;
        readonly Dictionary<ulong, float> m_MonsterHp = new Dictionary<ulong, float>();
        DateTime m_SharpenedAt, m_CombatAt;
        readonly Dictionary<ulong, DateTime> m_HitAt = new Dictionary<ulong, DateTime>(); // per monster, for "Fighting A & B"
        DateTime? m_QuestStartedAt;
        TimeSpan? m_ClearTime;

        // Images Discord loads from this repo on GitHub: monster portraits and weapon icons (assets/discord)
        const string AssetBase = "https://raw.githubusercontent.com/noahwaseaten/Aether/main/";
        static readonly Dictionary<string, bool> s_HasPortrait = new Dictionary<string, bool>();
        long m_PhaseStart = Now();

        DiscordPresence()
        {
            new Thread(PipeLoop) { IsBackground = true, Name = "DiscordPresence" }.Start();
        }

        static long Now() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        // Called from the memory updater tick (UI thread).
        public void Update(Process process)
        {
            if (!ConfigHelper.Main.Values.DiscordPresence.Enabled)
            {
                Publish(null);
                return;
            }

            // The pipe sends at most once a second; rebuilding at the 20 Hz tick rate only cost memory reads
            if ((DateTime.UtcNow - m_LastBuilt).TotalMilliseconds < 500)
            {
                lock (m_Lock)
                {
                    m_LastUpdate = DateTime.UtcNow;
                }
                return;
            }
            m_LastBuilt = DateTime.UtcNow;

            try
            {
                Publish(BuildActivity(process));
            }
            catch (Exception ex)
            {
                Log.WriteException(ex);
            }
        }

        void Publish(object activity)
        {
            lock (m_Lock)
            {
                m_Activity = activity;
                m_LastUpdate = DateTime.UtcNow;
            }
        }

        // Hub areas and living quarters: no hunting there, so field-only widgets (sharpness) stay hidden
        public static bool IsInTown(Process p)
        {
            int zone = MemoryHelper.Read<int>(p, Chain(p, ZoneAddress, 0xAED0));
            return zone == 0 || Towns.Contains(zone);
        }

        static ulong Chain(Process p, long address, params long[] offsets)
        {
            ulong a = 0x140000000 + (ulong)address;
            foreach (var o in offsets)
            {
                ulong next = MemoryHelper.Read<ulong>(p, a);
                if (next == 0) return 0;
                a = (ulong)((long)next + o);
            }
            return a;
        }

        object BuildActivity(Process p)
        {
            var now = DateTime.UtcNow;
            int zone = MemoryHelper.Read<int>(p, Chain(p, ZoneAddress, 0xAED0));
            if (zone == 0)
            {
                return Activity("Main menu", null, null, null);
            }

            ulong save = Chain(p, SaveAddress, 0xA8);
            uint slot = MemoryHelper.Read<uint>(p, save + 0x44);
            ulong header = MemoryHelper.Read<ulong>(p, save) + 0x26CC00UL * slot;
            int hr = MemoryHelper.Read<short>(p, header + 0x90);
            int mr = MemoryHelper.Read<short>(p, header + 0xD4);
            string name = MemoryHelper.ReadString(p, header + 0x50, 32);

            ulong quest = Chain(p, QuestAddress, 0x0);
            int questId = MemoryHelper.Read<int>(p, quest + 0x4C);
            int questStars = MemoryHelper.Read<int>(p, quest + 0x50);
            int questState = MemoryHelper.Read<int>(p, quest + 0x54); // 0 none, 1 ready, 2 in quest, 3 success, 4 complete, 5 failed, 6 abandon, 7 quit

            ulong hud = Chain(p, PlayerAddress, 0x50, 0x7630, 0x0);
            float hp = MemoryHelper.Read<float>(p, hud + 0x64);

            ulong weapon = Chain(p, PlayerAddress, 0x50, 0x98, 0x10, 0x70, 0x18, 0x550, 0x0);
            int sharpness = MemoryHelper.Read<int>(p, weapon + 0x20F8);
            int weaponId = MemoryHelper.Read<int>(p, weapon + 0x1D0C);

            bool town = Towns.Contains(zone);
            Zones.TryGetValue(zone, out var zoneName);
            zoneName = zoneName ?? "the field";

            // New phase (zone or quest state changed): reset timer and edge state
            if (zone != m_LastZone || questState != m_LastQuestState)
            {
                if (questState == 2 && m_LastQuestState != 2)
                    m_QuestStartedAt = now;
                if ((questState == 3 || questState == 4) && m_QuestStartedAt.HasValue)
                    m_ClearTime = now - m_QuestStartedAt.Value;
                if (questState == 0 || questState == 1)
                {
                    m_QuestStartedAt = null;
                    m_ClearTime = null;
                }
                m_HitAt.Clear();
                m_PhaseStart = Now();
                m_LastZone = zone;
                m_LastQuestState = questState;
                m_MonsterHp.Clear();
                m_LastPlayerHp = -1;
                m_CombatAt = DateTime.MinValue;
            }

            // Sharpening = sharpness went up on the same weapon while out in the field
            if (!town && weaponId == m_LastWeaponId && m_LastSharpness >= 0 && sharpness > m_LastSharpness)
            {
                m_SharpenedAt = now;
            }
            m_LastSharpness = sharpness;
            m_LastWeaponId = weaponId;

            // Fighting = a large monster lost HP, or we took damage, recently
            var monsters = OverlayViewModel.Instance.MonsterWidget.Context.Monsters.ToList();
            foreach (var monster in monsters)
            {
                float current = monster.Health.Current;
                if (m_MonsterHp.TryGetValue(monster.Address, out var previous) && current < previous && current > 0)
                {
                    m_CombatAt = now;
                    m_HitAt[monster.Address] = now;
                }
                m_MonsterHp[monster.Address] = current;
            }
            // Most recently hit first; a turf war or double hunt shows both
            var fighting = monsters.Where(m => m.IsAlive && m_HitAt.TryGetValue(m.Address, out var at) && (now - at).TotalSeconds < 20)
                .OrderByDescending(m => m_HitAt[m.Address]).ToList();
            var target = fighting.FirstOrDefault();
            if (!town && m_LastPlayerHp > 0 && hp < m_LastPlayerHp && hp > 0)
            {
                m_CombatAt = now;
            }
            bool carted = !town && hud != 0 && hp <= 0 && m_LastPlayerHp > 0;
            m_LastPlayerHp = hp > 0 ? hp : (carted ? m_LastPlayerHp : hp);

            string rank = mr > 0 ? $"MR {mr}" : $"HR {hr}";
            string stars = questStars > 0 ? $" · {questStars}★" : "";
            string hover = string.Join(" · ", new[] { name, rank, s_WeaponNames.Convert(OverlayViewModel.Instance.DebugWidget.Context.CurrentGame.EquippedWeaponType, typeof(string), null, null) as string }.Where(s => !string.IsNullOrEmpty(s)));

            if (town)
            {
                string detail = questState == 1 && questId > 0 ? "Quest accepted" : $"In {zoneName}";
                return Activity(detail, questState == 1 && questId > 0 ? $"{zoneName}{stars}" : null, hover, null);
            }

            string details;
            string state = $"{zoneName}{stars}";
            if (questState == 3 || questState == 4)
            {
                details = "Quest complete";
                if (m_ClearTime.HasValue)
                    state = $"Cleared in {(int)m_ClearTime.Value.TotalMinutes}:{m_ClearTime.Value.Seconds:00} · {zoneName}";
            }
            else if (questState >= 5) details = "Quest failed";
            else if (carted) details = "Carted";
            else if ((now - m_SharpenedAt).TotalSeconds < 6) details = "Sharpening";
            else if (target != null)
            {
                details = fighting.Count > 1 ? $"Fighting {target.Name} & {fighting[1].Name}" : $"Fighting {target.Name}";
                state = $"{(int)Math.Ceiling(target.Health.Fraction * 20) * 5}% HP · {zoneName}"; // 5% steps keeps updates under Discord's rate limit
            }
            else if ((now - m_CombatAt).TotalSeconds < 20) details = "In combat";
            else if (questState == 2 && questId > 0) details = "On a quest";
            else details = "Exploring";

            return Activity(details, state, hover, target);
        }

        object Activity(string details, string state, string hover, Monster target)
        {
            var c = ConfigHelper.Main.Values.DiscordPresence;
            var game = OverlayViewModel.Instance.DebugWidget.Context.CurrentGame;
            int partySize = OverlayViewModel.Instance.TeamWidget.Context.Players.Count;
            string weaponIcon = MhwHelper.WeaponIconName(game.EquippedWeaponType);

            // Big image: the monster you're fighting (crown size on hover), else the game's art
            string largeImage = c.LargeImage, largeText = "Hunting with Aether";
            if (target != null && HasPortrait(target.Id))
            {
                largeImage = $"{AssetBase}SmartHunter/Ui/Monsters/{target.Id}.png";
                largeText = target.Crown == MonsterCrown.None ? target.Name : $"{target.Name} · {target.Crown} crown size";
            }

            return new
            {
                type = 0,
                details,
                state,
                status_display_type = 2, // member list shows "Fighting Rathalos" instead of the game name
                timestamps = new { start = m_PhaseStart },
                assets = new
                {
                    large_image = largeImage,
                    large_text = largeText,
                    large_url = "https://github.com/noahwaseaten/Aether", // clicking the big image opens Aether's page
                    // Small badge: your weapon, with name, rank and weapon on hover
                    small_image = weaponIcon != null ? $"{AssetBase}assets/discord/weapons/{weaponIcon}.png" : null,
                    small_text = weaponIcon != null ? hover : null,
                },
                // Same hashed lobby id for everyone in the session, so Discord shows you as one party
                party = partySize > 1 && state != null
                    ? new { id = "mhw-" + (string.IsNullOrEmpty(game.key) ? "party" : game.key.Substring(0, Math.Min(16, game.key.Length))), size = new[] { partySize, 4 } }
                    : null,
                // Shown to people viewing your profile (Discord never shows you your own buttons)
                buttons = new[] { new { label = "Get the Aether overlay", url = "https://github.com/noahwaseaten/Aether" } },
            };
        }

        static bool HasPortrait(string id)
        {
            if (string.IsNullOrEmpty(id))
                return false;
            if (!s_HasPortrait.TryGetValue(id, out bool has))
            {
                try { has = System.Windows.Application.GetResourceStream(new Uri($"pack://application:,,,/Ui/Monsters/{id}.png")) != null; }
                catch (IOException) { has = false; }
                s_HasPortrait[id] = has;
            }
            return has;
        }

        // ---- Discord IPC ---------------------------------------------------------------

        void PipeLoop()
        {
            NamedPipeClientStream pipe = null;
            string sent = null;
            DateTime sentAt = DateTime.MinValue;

            while (true)
            {
                try
                {
                    object activity;
                    lock (m_Lock)
                    {
                        // Game closed or updater stopped feeding us: clear presence
                        activity = (DateTime.UtcNow - m_LastUpdate).TotalSeconds > 5 ? null : m_Activity;
                    }

                    string payload = JsonConvert.SerializeObject(activity, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });

                    if (pipe == null && activity != null)
                    {
                        pipe = Connect();
                        sent = null;
                    }

                    // Discord allows ~5 activity updates per 20s
                    if (pipe != null && payload != sent && (DateTime.UtcNow - sentAt).TotalSeconds >= 4)
                    {
                        Send(pipe, 1, new
                        {
                            cmd = "SET_ACTIVITY",
                            nonce = Guid.NewGuid().ToString(),
                            args = new { pid = Process.GetCurrentProcess().Id, activity },
                        });
                        Receive(pipe);
                        sent = payload;
                        sentAt = DateTime.UtcNow;

                        if (activity == null)
                        {
                            pipe.Dispose();
                            pipe = null;
                        }
                    }
                }
                catch (Exception)
                {
                    // Discord not running or restarted; retry later
                    pipe?.Dispose();
                    pipe = null;
                    Thread.Sleep(10000);
                }

                Thread.Sleep(1000);
            }
        }

        NamedPipeClientStream Connect()
        {
            for (int i = 0; i < 10; i++)
            {
                var pipe = new NamedPipeClientStream(".", $"discord-ipc-{i}", PipeDirection.InOut);
                try
                {
                    pipe.Connect(200);
                    Send(pipe, 0, new { v = 1, client_id = ConfigHelper.Main.Values.DiscordPresence.ApplicationId });
                    Receive(pipe);
                    Log.WriteLine($"Discord Rich Presence connected (discord-ipc-{i})");
                    return pipe;
                }
                catch (Exception)
                {
                    pipe.Dispose();
                }
            }
            throw new IOException("Discord is not running");
        }

        static void Send(Stream pipe, int opcode, object body)
        {
            var json = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(body, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore }));
            var frame = new byte[8 + json.Length];
            BitConverter.GetBytes(opcode).CopyTo(frame, 0);
            BitConverter.GetBytes(json.Length).CopyTo(frame, 4);
            json.CopyTo(frame, 8);
            pipe.Write(frame, 0, frame.Length);
            pipe.Flush();
        }

        static string Receive(Stream pipe)
        {
            var header = ReadExactly(pipe, 8);
            int opcode = BitConverter.ToInt32(header, 0);
            string body = Encoding.UTF8.GetString(ReadExactly(pipe, BitConverter.ToInt32(header, 4)));
            if (opcode == 2) // CLOSE
            {
                throw new IOException("Discord closed the connection: " + body);
            }
            if (body.Contains("\"evt\":\"ERROR\""))
            {
                Log.WriteLine("Discord Rich Presence error: " + body);
            }
            return body;
        }

        static byte[] ReadExactly(Stream stream, int count)
        {
            var buffer = new byte[count];
            for (int read = 0; read < count;)
            {
                int n = stream.Read(buffer, read, count - read);
                if (n <= 0) throw new EndOfStreamException();
                read += n;
            }
            return buffer;
        }
    }
}
