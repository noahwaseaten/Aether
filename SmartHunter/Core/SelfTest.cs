using System;
using System.Collections.Generic;
using System.IO;
using SmartHunter.Core.Helpers;
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

            // Sharpness: a sane table shows, garbage from loading screens doesn't
            var sharpness = new Sharpness();
            sharpness.Update(new[] { 60, 100, 150, 200, 260, 0, 0 }, 230, 260);
            Check(sharpness.IsAvailable && sharpness.LevelName == "Blue" && sharpness.HitsLeft == 30, "valid sharpness table reads blue, 30 hits");
            sharpness.Update(new[] { 400, 20, 0, 0, 0, 0, 0 }, 10, 400);
            Check(!sharpness.IsAvailable, "bands that go down are rejected");
            sharpness.Update(new[] { 90, 0, 0, 0, 0, 0, 0 }, 50, 90);
            Check(!sharpness.IsAvailable, "a lone red band is rejected");

            // Rage: the stored value is time left, so a calm monster reads exactly max
            int rage = Array.FindIndex(ConfigHelper.MonsterData.Values.StatusEffects, s => s.GroupId == "Rage");
            var monster = new Monster(1, "em001_00", 1000, 1000, 1, 1);
            monster.UpdateAndGetStatusEffect(2, rage, 1, 0, 30, 30, 0);
            Check(!monster.IsEnraged, "calm monster (rage time left = max) is not enraged");
            monster.UpdateAndGetStatusEffect(2, rage, 1, 0, 30, 12, 1);
            Check(monster.IsEnraged, "rage counting down is enraged");
            monster.UpdateAndGetStatusEffect(2, rage, 1, 0, 30, 0, 1);
            Check(!monster.IsEnraged, "rage ran out");

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
            monsters.WaitingForHost = true;
            busy.RankRows();
            shownParts = 0;
            foreach (var part in busy.Parts) if (part.IsVisible) shownParts++;
            Check(shownParts == 0, "client without host data hides parts");
            monsters.WaitingForHost = false;

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
