using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SmartHunter.Core.Helpers;
using SmartHunter.Game.Config;
using SmartHunter.Game.Data;
using SmartHunter.Game.Data.ViewModels;
using SmartHunter.Game.Helpers;

namespace SmartHunter.Core
{
    // Aether.exe --selftest: checks the logic that's easy to break and hard to notice in game.
    // Writes SelfTest.txt next to the exe and exits 0 when everything passes. build.ps1 runs it before publishing.
    public static class SelfTest
    {
        public static int Run()
        {
            var failures = new List<string>();
            void Check(bool condition, string name)
            {
                if (!condition) failures.Add(name);
            }

            // Death and capture come from the monster's current action name
            Check(MhwHelper.IsDeathAction("nActEm001::Die"), "Die action counts as dead");
            Check(MhwHelper.IsDeathAction("nActEm001::Dead"), "Dead action counts as dead");
            Check(!MhwHelper.IsDeathAction("nActEm001::DieSleep"), "DieSleep is sleeping, not dead");
            Check(!MhwHelper.IsDeathAction("nActEm001::DeadlyPoison"), "Deadly is not dead");
            Check(!MhwHelper.IsDeathAction(""), "unreadable action is not dead");

            // Update versions: tags compare against the exe's four-part version
            Check(AppUpdater.TryParseTag("v1.2.0", out var a) && a == new Version(1, 2, 0, 0), "v1.2.0 parses as 1.2.0.0");
            Check(AppUpdater.TryParseTag("1.2", out var b) && b == new Version(1, 2, 0, 0), "1.2 parses as 1.2.0.0");
            Check(AppUpdater.TryParseTag("v1.10.0", out var c) && c > new Version(1, 9, 0, 0), "1.10 is newer than 1.9");
            Check(!AppUpdater.TryParseTag("latest", out _), "non-version tag is ignored");
            Check(AppUpdater.CleanNotes("## Fixes\r\n- **Bold** `code`") == "Fixes\n• Bold code", "release notes lose their markdown");
            Check(AppUpdater.CleanNotes(null) == "No notes for this release.", "empty release notes get a placeholder");
            Check(WindowHelper.RefreshRate() >= 30, "monitor refresh rate is read or falls back to 60");

            // Sharpness: a sane table shows, garbage from loading screens doesn't
            var sharpness = new Sharpness();
            sharpness.Update(new[] { 60, 100, 150, 200, 260, 0, 0 }, 230, 260);
            Check(sharpness.IsAvailable && sharpness.LevelName == "Blue" && sharpness.HitsLeft == 30, "valid sharpness table reads blue, 30 hits");
            sharpness.Update(new[] { 400, 20, 0, 0, 0, 0, 0 }, 10, 400);
            Check(!sharpness.IsAvailable, "bands that go down are rejected");
            sharpness.Update(new[] { 90, 0, 0, 0, 0, 0, 0 }, 50, 90);
            Check(!sharpness.IsAvailable, "a lone red band is rejected");

            // A weapon that tops out at green, with a cap read from memory that wrongly reaches into blue
            var greenCapped = new Sharpness();
            greenCapped.Update(new[] { 50, 100, 150, 200, 250, 0, 0 }, 120, 250);
            greenCapped.Update(new[] { 50, 100, 150, 200, 250, 0, 0 }, 200, 250); // sharpened: refills to the real max
            greenCapped.Update(new[] { 50, 100, 150, 200, 250, 0, 0 }, 190, 250);
            Check(greenCapped.LevelName == "Green" && !greenCapped.NeedsSharpening, "a green-capped weapon on green doesn't ask to sharpen");
            greenCapped.Update(new[] { 50, 100, 150, 200, 250, 0, 0 }, 140, 250);
            Check(greenCapped.NeedsSharpening, "dropping to yellow on that weapon still asks to sharpen");

            // An unupgraded weapon that tops out at yellow, first read at the start of a quest, before any whetstone
            var yellowCapped = new Sharpness();
            yellowCapped.Update(new[] { 60, 110, 150, 200, 250, 0, 0 }, 150, 250);
            Check(yellowCapped.LevelName == "Yellow" && !yellowCapped.NeedsSharpening, "a yellow-capped weapon at full yellow doesn't ask to sharpen");
            yellowCapped.Update(new[] { 60, 110, 150, 200, 250, 0, 0 }, 120, 250);
            Check(yellowCapped.LevelName == "Yellow" && !yellowCapped.NeedsSharpening && yellowCapped.IsLow, "worn down within yellow it warns low, not sharpen");
            yellowCapped.Update(new[] { 60, 110, 150, 200, 250, 0, 0 }, 100, 250);
            Check(yellowCapped.NeedsSharpening, "dropping to orange on that weapon asks to sharpen");
            yellowCapped.Update(new[] { 60, 110, 150, 200, 250, 0, 0 }, 900, 250);
            yellowCapped.Update(new[] { 60, 110, 150, 200, 250, 0, 0 }, 150, 250);
            Check(!yellowCapped.NeedsSharpening, "a garbage read above the game's cap doesn't raise the cap");

            // Dulled at the first read (Aether started mid-quest): no warning yet, and the next whetstone fixes the cap
            var midQuest = new Sharpness();
            midQuest.Update(new[] { 50, 100, 150, 200, 250, 0, 0 }, 140, 250);
            midQuest.Update(new[] { 50, 100, 150, 200, 250, 0, 0 }, 200, 250);
            midQuest.Update(new[] { 50, 100, 150, 200, 250, 0, 0 }, 140, 250);
            Check(midQuest.NeedsSharpening, "after a whetstone, the real cap is known");

            // Discord weapon icons: every weapon maps to a PNG committed under assets/discord/weapons
            foreach (WeaponType weapon in Enum.GetValues(typeof(WeaponType)))
            {
                string icon = MhwHelper.WeaponIconName(weapon);
                if (icon != null)
                    Check(System.Text.RegularExpressions.Regex.IsMatch(icon, "^[a-z]+$"), $"{weapon} icon name '{icon}' is a plain file name");
            }
            Check(MhwHelper.WeaponIconName(WeaponType.LONG_SWORD) == "longsword", "long sword maps to longsword.png");

            // Save backup: only MHW's folder (582010) of each Steam account goes in the zip, not other games' data
            string fakeUserData = Path.Combine(Path.GetTempPath(), "aether-selftest-userdata-" + Guid.NewGuid());
            Directory.CreateDirectory(Path.Combine(fakeUserData, "111", "582010", "remote"));
            Directory.CreateDirectory(Path.Combine(fakeUserData, "111", "730"));
            File.WriteAllText(Path.Combine(fakeUserData, "111", "582010", "remote", "SAVEDATA1000"), "save");
            File.WriteAllText(Path.Combine(fakeUserData, "111", "730", "other-game.bin"), "not mhw");
            string backupZip = MemoryUpdater.BackupSaves(fakeUserData + "\\");
            var backedUp = backupZip == null ? new List<string>() : System.IO.Compression.ZipFile.OpenRead(backupZip).Entries.Select(e => e.FullName.Replace('\\', '/')).ToList();
            Check(backedUp.SequenceEqual(new[] { "111/582010/remote/SAVEDATA1000" }), "save backup holds only MHW's saves: " + string.Join(", ", backedUp));
            Directory.Delete(fakeUserData, true);

            // Hunt extras: m:ss, and the quest limit rounded up to the game's known limits
            Check(HuntInfo.Format(65.2f) == "1:06" && HuntInfo.Format(0.5f) == "0:01" && HuntInfo.Format(-1) == null, "timers format as m:ss");
            Check(HuntInfo.QuestLimitSeconds(179990) == 3000 && HuntInfo.QuestLimitSeconds(54000) == 900, "quest limits round up to 50 and 15 minutes");
            Check(MhwHelper.PowerProlongerMultiplier(0, WeaponType.CHARGE_BLADE) == 1 && Math.Abs(MhwHelper.PowerProlongerMultiplier(3, WeaponType.CHARGE_BLADE) - 1.4f) < 0.001f
                && Math.Abs(MhwHelper.PowerProlongerMultiplier(3, WeaponType.SWITCH_AXE) - 2.0f) < 0.001f, "Power Prolonger matches HunterPie's formula");

            // Blastscourge is a flagged meter, not a timer (the "1 s forever" bug)
            var blastscourge = new PlayerDataConfig().StatusEffects.First(s => s.NameStringId == "LOC_STATUS_EFFECT_BLASTSCOURGE");
            Check(blastscourge.TimerOffset == null && blastscourge.Conditions.Single().ByteNonZero, "Blastscourge shows only while its flag is set");
            // Two statuses on one timer only work when a condition tells them apart (Might seed vs pill)
            var sharedTimers = new PlayerDataConfig().StatusEffects
                .Where(s => s.TimerOffset != null && (s.Conditions == null || s.Conditions.Length == 0))
                .GroupBy(s => s.Source + ":" + s.TimerOffset).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            Check(!sharedTimers.Any(), "no two unconditioned statuses read the same timer " + string.Join(", ", sharedTimers));

            // Rage: the stored value is time left, so a calm monster reads exactly max
            int rage = Array.FindIndex(ConfigHelper.MonsterData.Values.StatusEffects, s => s.GroupId == "Rage");
            var monster = new Monster(1, "em001_00", 1000, 1000, 1, 1);
            monster.UpdateAndGetStatusEffect(2, rage, 1, 0, 30, 30, 0);
            Check(!monster.IsEnraged, "calm monster (rage time left = max) is not enraged");
            monster.UpdateAndGetStatusEffect(2, rage, 1, 0, 30, 12, 1);
            Check(monster.IsEnraged, "rage counting down is enraged");
            monster.UpdateAndGetStatusEffect(2, rage, 1, 0, 30, 0, 1);
            Check(!monster.IsEnraged, "rage ran out");

            // Host detection decides who pushes and who pulls; two hosts overwrite each other on the server
            var game = new Game.Data.Game { CurrentPlayerName = "Me" };
            Check(game.IsCurrentPlayerLobbyHost(), "offline: you're the host");
            game.SessionID = "session";
            game.PartySize = 2;
            game.PartyLeaderName = "Friend";
            Check(!game.IsCurrentPlayerLobbyHost(), "friend leads the party: you're a client");
            game.PartyLeaderName = "Me";
            Check(game.IsCurrentPlayerLobbyHost(), "you lead the party: you're the host");
            game.PartyLeaderName = "";
            Check(!game.IsCurrentPlayerLobbyHost(), "leader unknown with others around: don't claim host");

            // Junk from the third-party sync server is bounded and can't throw
            var synced = new Monster(5, "em001_00", 1000, 1000, 1, 1);
            var junkParts = new Dictionary<string, int[]>();
            for (int i = 1; i <= 5000; i++) junkParts[i.ToString()] = new[] { 0, 100, 50, 0 };
            junkParts["abc"] = new[] { 0, 1, 1, 0 };
            junkParts["-3"] = new[] { 0, 1, 1, 0 };
            junkParts["7"] = new[] { 0 };
            var junkStatuses = new Dictionary<string, int[]> { { "99999", new[] { 1, 1, 1, 1, 1 } }, { "x", new[] { 1 } }, { "1", null } };
            bool threw = false;
            try
            {
                MhwHelper.UpdateMonsterParts(junkParts, synced);
                MhwHelper.UpdateMonsterStatusEffects(junkStatuses, synced);
            }
            catch (Exception)
            {
                threw = true;
            }
            Check(!threw && synced.Parts.Count <= 48 && synced.StatusEffects.Count == 0, "junk sync data is bounded and ignored");

            // Busy fights: only the most recently hit rows show, and clients without host data show none
            var monsters = OverlayViewModel.Instance.MonsterWidget.Context;
            var busy = new Monster(3, "em001_00", 1000, 1000, 1, 1);
            for (int i = 0; i < 8; i++)
            {
                busy.UpdateAndGetPart((ulong)(100 + i), false, 500, 500, 0);
                busy.UpdateAndGetPart((ulong)(100 + i), false, 500, 400, 0);
            }
            busy.RankRows();
            int shownParts = 0;
            foreach (var part in busy.Parts) if (part.IsVisible) shownParts++;
            Check(shownParts == Game.Data.WidgetContexts.MonsterWidgetContext.MaxPartRows, "8 damaged parts show only the 6 most recent");
            monsters.IsEstimate = true;
            busy.RankRows();
            shownParts = 0;
            foreach (var part in busy.Parts) if (part.IsVisible) shownParts++;
            Check(shownParts == Game.Data.WidgetContexts.MonsterWidgetContext.MaxPartRows, "client without host data still shows its estimate");
            monsters.IsEstimate = false;

            // Breaks: Rathian's head (first normal part) needs its pool emptied twice; legs only flinch; cut parts go once
            var rathian = new Monster(6, "em001_00", 1000, 1000, 1, 1);
            var cut = rathian.UpdateAndGetPart(0x10000001, true, 300, 300, 0);
            var head = rathian.UpdateAndGetPart(0x10000002, false, 300, 300, 1);
            rathian.UpdateAndGetPart(0x10000003, false, 300, 300, 0);
            rathian.UpdateAndGetPart(0x10000004, false, 300, 300, 0);
            rathian.UpdateAndGetPart(0x10000005, false, 300, 300, 0);
            var leg = rathian.UpdateAndGetPart(0x10000006, false, 300, 300, 3);
            Check(head.BreakThreshold == 2 && !head.IsBroken && head.BreakPips.Length == 2, "head: emptied once of twice, not broken yet");
            head.TimesBrokenCount = 2;
            Check(head.IsBroken, "head emptied twice is broken");
            Check(leg.BreakThreshold == 0 && !leg.IsBroken, "legs only flinch, however often they're emptied");
            Check(cut.BreakThreshold == 1 && !cut.IsBroken, "cut part not cut yet");

            // Team damage: a hunter leaving must not shift the others into the wrong slot
            var team = OverlayViewModel.Instance.TeamWidget.Context;
            team.ClearPlayers();
            team.UpdateAndGetPlayer(0, "A", 100);
            team.UpdateAndGetPlayer(1, "B", 200);
            team.UpdateAndGetPlayer(2, "C", 300);
            for (int tick = 0; tick < 3; tick++)
            {
                team.UpdateAndGetPlayer(1, "", 0); // B left
            }
            var c2 = team.UpdateAndGetPlayer(2, "C", 300);
            Check(team.Players.Count == 2 && c2.Index == 2 && c2.Name == "C", "leaver removes only their own slot");
            team.ClearPlayers();

            string report = failures.Count == 0 ? "All checks passed" : "FAILED:\r\n" + string.Join("\r\n", failures);
            File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SelfTest.txt"), report);
            return failures.Count == 0 ? 0 : 1;
        }
    }
}
