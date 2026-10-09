using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using SmartHunter.Core.Helpers;
using SmartHunter.Game.Data;
using SmartHunter.Game.Data.ViewModels;
using SmartHunter.Game.Data.WidgetContexts;
using SmartHunter.Game.Helpers;

namespace SmartHunter.Game
{
    // Watches a quest from departure to the end screen: drives the monster call-outs and builds the hunt recap.
    // Addresses are for MHW:I build 421810 (HunterPie's map).
    public static class HuntTracker
    {
        const ulong Base = 0x140000000;

        class Seen
        {
            public string Name;
            public float LastFraction = 1;
            public bool WasCapturable;
        }

        static DateTime? s_QuestStart;
        static int s_LastState = -1, s_Stars, s_Carts;
        static float s_LastHp = -1;
        static List<RecapHunter> s_LastParty = new List<RecapHunter>();
        static readonly Dictionary<ulong, Seen> s_Monsters = new Dictionary<ulong, Seen>();
        static DateTime s_RecapShownAt;

        public static void Update(Process process)
        {
            var callouts = OverlayViewModel.Instance.CalloutWidget.Context;
            var recap = OverlayViewModel.Instance.RecapWidget.Context;

            ulong quest = MemoryHelper.ReadMultiLevelPointer(false, process, Base + 0x0500ED30, 0x0);
            int state = MemoryHelper.Read<int>(process, quest + 0x54); // 2 in quest, 3/4 success, 5 failed, 6/7 abandoned
            ulong hud = MemoryHelper.ReadMultiLevelPointer(false, process, Base + 0x050139A0, 0x50, 0x7630, 0x0);
            float hp = MemoryHelper.Read<float>(process, hud + 0x64);

            if (state == 2 && s_LastState != 2)
            {
                s_QuestStart = DateTime.Now;
                s_Stars = MemoryHelper.Read<int>(process, quest + 0x50);
                s_Carts = 0;
                s_LastHp = -1;
                s_LastParty.Clear();
                s_Monsters.Clear();
                recap.IsShowing = false;
            }

            if (state == 2)
            {
                if (s_LastHp > 0 && hp <= 0 && hud != 0)
                {
                    s_Carts++;
                }
                s_LastHp = hp;
                SnapshotParty();
                UpdateMonsters(process, callouts);
            }
            else if (callouts.Callouts.Any())
            {
                callouts.Callouts.Clear();
            }

            if (s_QuestStart.HasValue && state >= 3 && state <= 7 && s_LastState == 2)
            {
                recap.Recap = BuildRecap(state);
                recap.IsShowing = true;
                s_RecapShownAt = DateTime.Now;
                s_QuestStart = null;
            }

            if (recap.IsShowing && (DateTime.Now - s_RecapShownAt).TotalSeconds > RecapWidgetContext.SecondsOnScreen)
            {
                recap.IsShowing = false;
            }

            s_LastState = state;
        }

        // The damage table can be wiped as the quest ends, so keep the last good copy
        static void SnapshotParty()
        {
            var players = OverlayViewModel.Instance.TeamWidget.Context.Players.Where(p => p.Damage > 0).ToList();
            if (!players.Any())
            {
                return;
            }

            s_LastParty = players.Select(p => new RecapHunter
            {
                Name = p.Name,
                WeaponIcon = p.WeaponIcon,
                ColorIndex = p.Index,
                Damage = p.Damage,
                IsMe = p.IsMe,
            }).ToList();
        }

        static void UpdateMonsters(Process process, CalloutWidgetContext callouts)
        {
            var monsters = OverlayViewModel.Instance.MonsterWidget.Context.Monsters.ToList();
            foreach (var monster in monsters)
            {
                float fraction = monster.Health.Max > 0 ? monster.Health.Current / monster.Health.Max : 1;
                int id = MemoryHelper.Read<int>(process, monster.Address + 0x12280);
                CaptureThresholds.ByMonsterId.TryGetValue(id, out int threshold);
                bool capturable = threshold > 0 && fraction > 0 && fraction * 100 <= threshold;
                bool exhausted = monster.StatusEffects.Any(s => s.GroupId == "Fatigue" && s.Duration.Max > 0 && s.Duration.Current > 0);

                if (!s_Monsters.TryGetValue(monster.Address, out var seen))
                {
                    seen = s_Monsters[monster.Address] = new Seen();
                }
                seen.Name = monster.Name;
                seen.LastFraction = fraction;
                seen.WasCapturable |= capturable || monster.IsCaptured;
                if (!monster.IsAlive) seen.LastFraction = monster.IsCaptured ? fraction : 0;

                var callout = callouts.Callouts.FirstOrDefault(c => c.Address == monster.Address);
                bool show = monster.IsAlive && (capturable || monster.IsEnraged || exhausted);
                if (!show)
                {
                    if (callout != null) callouts.Callouts.Remove(callout);
                    continue;
                }
                if (callout == null)
                {
                    callout = new MonsterCallout { Address = monster.Address };
                    callouts.Callouts.Add(callout);
                }
                callout.Name = monster.Name;
                callout.IsCapturable = capturable;
                callout.IsEnraged = monster.IsEnraged;
                callout.IsExhausted = exhausted;
            }

            foreach (var gone in callouts.Callouts.Where(c => monsters.All(m => m.Address != c.Address)).ToList())
            {
                callouts.Callouts.Remove(gone);
            }
        }

        static HuntRecap BuildRecap(int state)
        {
            var recap = new HuntRecap
            {
                IsSuccess = state == 3 || state == 4,
                Result = state == 3 || state == 4 ? "QUEST COMPLETE" : state == 5 ? "QUEST FAILED" : "QUEST ABANDONED",
                                Stars = s_Stars > 0 ? new string('★', Math.Min(s_Stars, 10)) : "",
                Carts = s_Carts,
            };

            int total = Math.Max(1, s_LastParty.Sum(h => h.Damage));
            int top = s_LastParty.Count > 0 ? s_LastParty.Max(h => h.Damage) : 0;
            int rank = 0;
            foreach (var h in s_LastParty.OrderByDescending(h => h.Damage))
            {
                h.Rank = ++rank;
                h.Share = (float)h.Damage / total;
                h.Bar = top > 0 ? (float)h.Damage / top : 0;
                h.IsMvp = rank == 1 && s_LastParty.Count > 1;
                recap.Hunters.Add(h);
            }

            var me = recap.Hunters.FirstOrDefault(h => h.IsMe) ?? (recap.Hunters.Count == 1 ? recap.Hunters[0] : null);
            recap.IsSolo = recap.Hunters.Count <= 1;
            recap.MyDamage = me?.Damage ?? 0;
            if (me != null && !recap.IsSolo)
            {
                recap.Headline = me.Rank == 1
                    ? $"Top hunter · {me.Damage:N0} damage"
                    : $"{Ordinal(me.Rank)} of {recap.Hunters.Count} · {me.Damage:N0} damage";
            }

            foreach (var m in s_Monsters.Values.Where(m => !string.IsNullOrEmpty(m.Name)))
            {
                string outcome = m.LastFraction <= 0 ? "Slain"
                    : recap.IsSuccess && m.WasCapturable ? "Captured"
                    : $"{m.LastFraction:P0} HP";
                recap.Monsters.Add(new RecapMonster { Name = m.Name, Outcome = outcome });
            }

            return recap;
        }

        static string Ordinal(int n) => n == 1 ? "1st" : n == 2 ? "2nd" : n == 3 ? "3rd" : $"{n}th";

        // Debug.UseSampleData preview
        public static HuntRecap SampleRecap()
        {
            var recap = new HuntRecap { IsSuccess = true, Result = "QUEST COMPLETE", Stars = "★★★★★★", Carts = 1, MyDamage = 18420, IsSolo = false };
            recap.Hunters.Add(new RecapHunter { Rank = 1, Name = "Lythia", WeaponIcon = "ICON_SWITCHAXE", ColorIndex = 0, Damage = 18420, Share = 0.47f, Bar = 1, IsMe = true, IsMvp = true });
            recap.Hunters.Add(new RecapHunter { Rank = 2, Name = "Kabuto", WeaponIcon = "ICON_LONGSWORD", ColorIndex = 1, Damage = 13950, Share = 0.36f, Bar = 0.76f });
            recap.Hunters.Add(new RecapHunter { Rank = 3, Name = "mike the father", WeaponIcon = "ICON_BOW", ColorIndex = 2, Damage = 6630, Share = 0.17f, Bar = 0.36f });
            recap.Headline = "Top hunter · 18,420 damage";
            recap.Monsters.Add(new RecapMonster { Name = "Rathalos", Outcome = "Captured" });
            recap.Monsters.Add(new RecapMonster { Name = "Pukei-Pukei", Outcome = "Slain" });
            return recap;
        }
    }
}
