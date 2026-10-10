using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SmartHunter.Core;
using SmartHunter.Core.Helpers;
using SmartHunter.Game.Config;
using SmartHunter.Game.Data;
using SmartHunter.Game.Data.ViewModels;

namespace SmartHunter.Game.Helpers
{
    public static class MhwHelper
    {
        public static bool TryParseHex(string hexString, out long hexNumber)
        {
            if (hexString.StartsWith("-"))
            {
                bool res = long.TryParse(hexString.Substring(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out hexNumber);
                hexNumber = (-1) * hexNumber;
                return res;
            }
            return long.TryParse(hexString, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out hexNumber);
        }

        public static ulong AddOffset(ulong address, long offset)
        {
            return (ulong)((long)address + offset);
        }

        // TODO: Wouldn't it be nice if all this were data driven?
        public static class DataOffsets
        {
            public static class Monster
            {
                // Doubly linked list
                public static readonly ulong MonsterStartOfStructOffset = 0x40;
                public static readonly ulong NextMonsterOffset = 0x18;
                public static readonly ulong MonsterHealthComponentOffset = 0x7670;
                public static readonly ulong PreviousMonsterOffset = 0x10;
                public static readonly ulong SizeScale = 0x188;
                public static readonly ulong ScaleModifier = 0x7730;
                public static readonly ulong PartCollection = 0x14528;
                public static readonly ulong RemovablePartCollection = PartCollection + 0x22A0 - 0xF0 - 0xF0 - 0xF0 - 0x38;
                public static readonly ulong StatusEffectCollection = 0x19900;
                public static readonly ulong MonsterStaminaOffset = 0x1C0F0; //0x1BE20(???) 0x1C0D8(old) 0x1C130(now)
                public static readonly ulong MonsterRageOffset = 0x1BE54; //0x1BE20(???) 0x1BE30(old) 0x1BE88(now)
            }

            public static class MonsterModel
            {
                public static readonly int IdLength = 32; // 64?
                public static readonly ulong IdOffset = 0x179;
            }

            public static class MonsterHealthComponent
            {
                public static readonly ulong MaxHealth = 0x60;
                public static readonly ulong CurrentHealth = 0x64;
            }

            public static class MonsterPartCollection
            {
                public static readonly int MaxItemCount = 16;
                public static readonly ulong FirstPart = 0x1C;
            }

            public static class MonsterPart
            {
                public static readonly ulong MaxHealth = 0x00;//0x0c
                public static readonly ulong CurrentHealth = 0x04;//0x10
                public static readonly ulong TimesBrokenCount = 0x0C;//0x18
                public static readonly ulong NextPart = 0x1F8;//0x3F0;
            }

            public static class MonsterSoftenPart
            {
                public static readonly ulong SoftenPartOffset = 0x1C458;
                public static readonly ulong NextSoftenPart = 0x40;
                public static readonly ulong MaxDuration = 0x0C;
                public static readonly ulong MaxExtraDuration = 0x24;
                public static readonly ulong CurrentDuration = 0x08;
                public static readonly ulong CurrentExtraDuration = 0x20;
                public static readonly ulong TimesCountOffset = 0x34;
                public static readonly ulong PartIdOffset = 0x30;
            }

            public static class MonsterRemovablePartCollection
            {
                public static readonly int MaxItemCount = 32;
                public static readonly ulong FirstRemovablePart = 0x78;
            }

            public static class MonsterRemovablePart
            {
                public static readonly ulong MaxHealth = 0x0C;
                public static readonly ulong CurrentHealth = 0x10;
                public static readonly ulong TimesBrokenCount = 0x18;
                public static readonly ulong NextRemovablePart = 0x78;
            }

            public static class MonsterStatusEffectCollection
            {
                public static int MaxItemCount = 20;
                public static ulong NextStatusEffectPtr = 0x08;
            }

            public static class MonsterStatusEffect
            {
                public static readonly ulong MaxDuration = 0x19C;
                public static readonly ulong CurrentBuildup = 0x1B8;
                public static readonly ulong MaxBuildup = 0x1C8;
                public static readonly ulong CurrentDuration = 0x1F8;
                public static readonly ulong TimesActivatedCount = 0x200;
            }

            public static class PlayerNameCollection
            {
                public static readonly int IDLength = 12 + 1; // +1 for null terminator
                public static readonly int PlayerNameLength = 32 + 1; // +1 for null terminator
                public static readonly ulong FirstPlayerName = 0x532D5;
                public static readonly ulong SessionID = FirstPlayerName + 0xF43;
                public static readonly ulong SessionHostPlayerName = SessionID + 0x3F;
                public static readonly ulong LobbyID = FirstPlayerName + 0x463;
                public static readonly ulong LobbyHostPlayerName = LobbyID + 0x29;

                public static readonly ulong NextLobbyHostName = 0x2F; // Is dis even right?
            }
            public static class PlayerDamageCollection
            {
                public static readonly int MaxPlayerCount = 4;
                public static readonly ulong FirstPlayerPtr = 0x48;
                public static readonly ulong NextPlayerPtr = 0x58;
            }

            public static class PlayerDamage
            {
                public static readonly ulong Damage = 0x48;
                public static readonly int MaxOnScreenDamages = 14;
            }
        }

        private static int lastNetworkOperationTime = 0;
        private static bool networkOperationDone = true;
        private static int lastNetworkOperationTimeD = 0;
        private static bool networkOperationDoneD = true;
        private static int[,] expeditionDamageChecker = new int[DataOffsets.PlayerDamage.MaxOnScreenDamages, 2];
        private static ulong[] monsterAddresses = new ulong[3];
        private static List<Monster> updatedMonsters = new List<Monster>();
        private static DateTime lastPulledMonsterData = DateTime.MinValue;
        const ulong HostPartKeyLimit = 0xFFFF;
        private static string lastPulledPayload;

        public static void UpdateCurrentGame(Process process, ulong playerNameCollectionAddress, ulong currentPlayerNameAddress, ulong currentWeaponAddress, ulong lobbyStatusAddress)
        {
            string currentSessionID = MemoryHelper.ReadString(process, playerNameCollectionAddress + DataOffsets.PlayerNameCollection.SessionID, (uint)DataOffsets.PlayerNameCollection.IDLength);
            string currentSessionPlayerName = "";
            if (currentSessionID.Length > 0)
            {
                currentSessionPlayerName = MemoryHelper.ReadString(process, playerNameCollectionAddress + DataOffsets.PlayerNameCollection.SessionHostPlayerName, (uint)DataOffsets.PlayerNameCollection.PlayerNameLength);
            }

            string currentPlayerName = MemoryHelper.ReadString(process, currentPlayerNameAddress, (uint)DataOffsets.PlayerNameCollection.PlayerNameLength);

            string currentLobbyID = "";
            string currentLobbyPlayerName = "";
            bool isPlayerInMission = MemoryHelper.Read<uint>(process, lobbyStatusAddress + 0x54) != 0x0;
            bool isPlayerInExpedition = MemoryHelper.Read<uint>(process, lobbyStatusAddress + 0x38) != 0x1;
            if (isPlayerInMission || isPlayerInExpedition)
            {
                if (currentSessionID.Length > 0)
                {
                    currentLobbyID = MemoryHelper.ReadString(process, playerNameCollectionAddress + DataOffsets.PlayerNameCollection.LobbyID, (uint)DataOffsets.PlayerNameCollection.IDLength);
                    if (currentLobbyID.Length > 0)
                    {
                        for (int index = 0; index < 4; index++)
                        {
                            ulong PlayerNameOffset = DataOffsets.PlayerNameCollection.NextLobbyHostName * (ulong)index;
                            currentLobbyPlayerName = MemoryHelper.ReadString(process, playerNameCollectionAddress + DataOffsets.PlayerNameCollection.LobbyHostPlayerName + PlayerNameOffset, (uint)DataOffsets.PlayerNameCollection.PlayerNameLength);
                            if (currentLobbyPlayerName.Length > 0)
                            {
                                break;
                            }
                        }
                    }
                }
                else
                {
                    currentLobbyID = "Not Online";
                    currentLobbyPlayerName = currentPlayerName;
                }
            }

            string currentEquippedWeaponString = MemoryHelper.ReadString(process, currentWeaponAddress, 0x4F);
            OverlayViewModel.Instance.DebugWidget.Context.UpdateCurrentGame(currentPlayerName, currentEquippedWeaponString, currentSessionID, currentSessionPlayerName, currentLobbyID, currentLobbyPlayerName, !isPlayerInMission && isPlayerInExpedition);
        }

        // Buff offsets are hex strings in PlayerData.json; parse each chain once instead of every tick
        static readonly Dictionary<string, long[]> s_ParsedOffsets = new Dictionary<string, long[]>();
        static long[] ParsedOffsets(string[] offsetStrings)
        {
            string key = string.Join(",", offsetStrings);
            if (!s_ParsedOffsets.TryGetValue(key, out var offsets))
            {
                offsets = new long[offsetStrings.Length];
                for (int i = 0; i < offsetStrings.Length; i++)
                {
                    if (!TryParseHex(offsetStrings[i], out offsets[i]))
                    {
                        offsets = null;
                        break;
                    }
                }
                s_ParsedOffsets[key] = offsets;
            }
            return offsets != null && offsets.Length > 0 ? offsets : null;
        }

        public static void UpdatePlayerWidget(Process process, ulong baseAddress, ulong equipmentAddress, ulong weaponAddress)
        {
            for (int index = 0; index < ConfigHelper.PlayerData.Values.StatusEffects.Length; ++index)
            {
                var statusEffectConfig = ConfigHelper.PlayerData.Values.StatusEffects[index];

                ulong sourceAddress = baseAddress;
                if (statusEffectConfig.Source == (uint)StatusEffectConfig.MemorySource.Equipment)
                {
                    sourceAddress = equipmentAddress;
                }
                else if (statusEffectConfig.Source != (uint)StatusEffectConfig.MemorySource.Base)
                {
                    sourceAddress = weaponAddress;
                }

                bool allConditionsPassed = true;
                if (statusEffectConfig.Conditions != null)
                {
                    foreach (var condition in statusEffectConfig.Conditions)
                    {
                        long[] offsets = ParsedOffsets(condition.Offsets);
                        if (offsets == null)
                        {
                            allConditionsPassed = false;
                            break;
                        }

                        var conditionAddress = MemoryHelper.ReadMultiLevelPointer(false, process, sourceAddress + (ulong)offsets[0], offsets.Skip(1).ToArray());

                        bool isPassed = false;
                        if (condition.ByteNonZero)
                        {
                            isPassed = MemoryHelper.Read<byte>(process, conditionAddress) != 0;
                        }
                        else if (condition.ByteValue.HasValue)
                        {
                            var conditionValue = MemoryHelper.Read<byte>(process, conditionAddress);
                            isPassed = conditionValue == condition.ByteValue;
                        }
                        else if (condition.IntValue.HasValue)
                        {
                            var conditionValue = MemoryHelper.Read<int>(process, conditionAddress);
                            isPassed = conditionValue == condition.IntValue;
                        }
                        else if (condition.StringRegexValue != null)
                        {
                            var conditionValue = MemoryHelper.ReadString(process, conditionAddress, 64);
                            isPassed = Regex.IsMatch(conditionValue, condition.StringRegexValue);
                        }

                        if (!isPassed)
                        {
                            allConditionsPassed = false;
                            break;
                        }
                    }
                }
                if (statusEffectConfig.Source != (uint)StatusEffectConfig.MemorySource.Base && statusEffectConfig.Source != (uint)StatusEffectConfig.MemorySource.Equipment)
                {
                    if (!OverlayViewModel.Instance.DebugWidget.Context.CurrentGame.IsValid)
                    {
                        continue;
                    }
                    if (OverlayViewModel.Instance.DebugWidget.Context.CurrentGame.CurrentEquippedWeaponType() != (WeaponType)statusEffectConfig.Source)
                    {
                        allConditionsPassed = false;
                    }
                }
                float? timer = null;
                if (allConditionsPassed && statusEffectConfig.TimerOffset != null)
                {
                    var timerOffsets = ParsedOffsets(new[] { statusEffectConfig.TimerOffset });
                    if (timerOffsets != null)
                    {
                        long timerOffset = timerOffsets[0];
                        timer = MemoryHelper.Read<float>(process, (ulong)((long)sourceAddress + timerOffset));
                    }

                    if (timer <= 0)
                    {
                        timer = 0;
                        allConditionsPassed = false;
                    }
                }

                OverlayViewModel.Instance.PlayerWidget.Context.UpdateAndGetPlayerStatusEffect(index, timer, allConditionsPassed);
            }
        }

        public static void UpdateTeamWidget(Process process, ulong playerDamageCollectionAddress, ulong playerNameCollectionAddress)
        {
            List<Player> updatedPlayers = new List<Player>();

            for (int playerIndex = 0; playerIndex < DataOffsets.PlayerDamageCollection.MaxPlayerCount; ++playerIndex)
            {
                var player = UpdateAndGetTeamPlayer(process, playerIndex, playerDamageCollectionAddress, playerNameCollectionAddress);
                if (player != null)
                {
                    updatedPlayers.Add(player);
                }
            }

            if (updatedPlayers.Any())
            {
                OverlayViewModel.Instance.TeamWidget.Context.UpdateFractions();
            }
            else if (OverlayViewModel.Instance.TeamWidget.Context.Players.Any())
            {
                OverlayViewModel.Instance.TeamWidget.Context.ClearPlayers();
            }
        }

        // Game weapon id order -> icon key
        static readonly string[] s_WeaponIcons = { "ICON_GREATSWORD", "ICON_SWORDANDSHIELD", "ICON_DUALBLADES", "ICON_LONGSWORD", "ICON_HAMMER", "ICON_HUNTINGHORN",
            "ICON_LANCE", "ICON_GUNLANCE", "ICON_SWITCHAXE", "ICON_CHARGEBLADE", "ICON_INSECTGLAIVE", "ICON_BOW", "ICON_HEAVYBOWGUN", "ICON_LIGHTBOWGUN" };
        static readonly Dictionary<WeaponType, int> s_WeaponTypeIds = new Dictionary<WeaponType, int>
        {
            { WeaponType.GREAT_SWORD, 0 }, { WeaponType.SWORD_AND_SHIELD, 1 }, { WeaponType.DUAL_BLADES, 2 }, { WeaponType.LONG_SWORD, 3 },
            { WeaponType.HAMMER, 4 }, { WeaponType.HUNTING_HORN, 5 }, { WeaponType.LANCE, 6 }, { WeaponType.GUNLANCE, 7 },
            { WeaponType.SWITCH_AXE, 8 }, { WeaponType.CHARGE_BLADE, 9 }, { WeaponType.INSECT_GLAIVE, 10 }, { WeaponType.BOW, 11 },
            { WeaponType.HEAVY_BOWGUN, 12 }, { WeaponType.LIGHT_BOWGUN, 13 },
        };

        // "longsword" for ICON_LONGSWORD: the file name of the weapon's Discord icon in assets/discord/weapons
        public static string WeaponIconName(WeaponType weaponType) =>
            s_WeaponTypeIds.TryGetValue(weaponType, out int id) && id < s_WeaponIcons.Length ? s_WeaponIcons[id].Substring("ICON_".Length).ToLowerInvariant() : null;

        // Quest party leader and size (party struct from HunterPie's map for build 421810). Slot 0 is the leader.
        static string s_LastHostLog;
        public static void UpdateQuestParty(Process process)
        {
            var game = OverlayViewModel.Instance.DebugWidget.Context.CurrentGame;
            ulong root = MemoryHelper.Read<ulong>(process, 0x140000000UL + 0x05013530);
            string leader = "";
            int size = 0;
            if (root != 0)
            {
                for (int i = 0; i < 4; i++)
                {
                    ulong member = MemoryHelper.Read<ulong>(process, root + 0x1AB0 + (ulong)(i * 0x58));
                    string name = member == 0 ? "" : MemoryHelper.ReadString(process, member + 0x49, 32);
                    if (!String.IsNullOrEmpty(name))
                    {
                        size++;
                        if (i == 0) leader = name;
                    }
                }
            }
            game.PartyLeaderName = leader;
            game.PartySize = size;

            if (game.IsPlayerOnline() && size > 1)
            {
                string host = game.IsCurrentPlayerLobbyHost() ? "You're the host" : $"{(leader.Length > 0 ? leader : "Someone else")} is the host";
                if (host != s_LastHostLog)
                {
                    s_LastHostLog = host;
                    Log.WriteLine($"{host} ({size} hunters)");
                }
            }
        }

        // Weapon for each quest member (party struct from HunterPie's map for build 421810)
        public static void UpdatePartyDetails(Process process)
        {
            ulong root = MemoryHelper.Read<ulong>(process, 0x140000000UL + 0x05013530);
            if (root == 0)
            {
                return;
            }

            var game = OverlayViewModel.Instance.DebugWidget.Context.CurrentGame;
            var players = OverlayViewModel.Instance.TeamWidget.Context.Players;
            for (int i = 0; i < 4; i++)
            {
                ulong member = MemoryHelper.Read<ulong>(process, root + 0x1AB0 + (ulong)(i * 0x58));
                if (member == 0) continue;

                string memberName = MemoryHelper.ReadString(process, member + 0x49, 32);
                var target = players.FirstOrDefault(p => p.Name == memberName);
                if (String.IsNullOrEmpty(memberName) || target == null) continue;

                int weapon = MemoryHelper.Read<byte>(process, member + 0x7C);
                if (memberName == game.CurrentPlayerName && s_WeaponTypeIds.TryGetValue(game.EquippedWeaponType, out var mine))
                {
                    weapon = mine;
                }
                target.WeaponIcon = weapon < s_WeaponIcons.Length ? s_WeaponIcons[weapon] : null;
            }
        }

        // Sharpness of the equipped melee weapon (HunterPie's map for build 421810)
        static int[] s_MinimumSharpness;
        public static void UpdateSharpness(Process process)
        {
            var sharpness = OverlayViewModel.Instance.PlayerWidget.Context.Sharpness;
            var weaponType = OverlayViewModel.Instance.DebugWidget.Context.CurrentGame.EquippedWeaponType;
            bool isMelee = s_WeaponTypeIds.ContainsKey(weaponType) && weaponType != WeaponType.BOW && weaponType != WeaponType.HEAVY_BOWGUN && weaponType != WeaponType.LIGHT_BOWGUN;

            const ulong Base = 0x140000000;
            ulong weapon = MemoryHelper.ReadMultiLevelPointer(false, process, Base + 0x050139A0, 0x50, 0x98, 0x10, 0x70, 0x18, 0x550, 0x0);
            ulong weaponData = MemoryHelper.Read<ulong>(process, Base + 0x05012080);
            // Quest over (results screen, then the load back to town): sharpness no longer matters
            ulong quest = MemoryHelper.ReadMultiLevelPointer(false, process, Base + 0x0500ED30, 0x0);
            bool questOver = MemoryHelper.Read<int>(process, quest + 0x54) >= 3;
            if (!isMelee || weapon < 0xFFFF || weaponData == 0 || questOver || DiscordPresence.IsInTown(process))
            {
                sharpness.IsAvailable = false;
                return;
            }

            if (s_MinimumSharpness == null)
            {
                // Only keep a sane table: caching a read from before the game finished loading pinned the cap wrong forever
                var minimums = Enumerable.Range(0, 8).Select(i => MemoryHelper.Read<int>(process, Base + 0x034DAB10 + (ulong)(i * 4))).ToArray();
                if (minimums.All(m => m >= 0 && m <= 1000) && minimums.Any(m => m > 0))
                {
                    s_MinimumSharpness = minimums;
                }
            }

            int weaponId = MemoryHelper.Read<int>(process, weapon + 0x1D0C);
            int current = MemoryHelper.Read<int>(process, weapon + 0x20F8);
            int maxIndex = MemoryHelper.Read<int>(process, weapon + 0x1D10);
            ulong table = MemoryHelper.ReadMultiLevelPointer(false, process, weaponData + 0xC8, weaponId * 8, 0x0C);
            var thresholds = Enumerable.Range(0, 7).Select(i => (int)MemoryHelper.Read<short>(process, table + (ulong)(i * 2))).ToArray();
            int cap = s_MinimumSharpness != null && maxIndex >= 0 && maxIndex < s_MinimumSharpness.Length ? s_MinimumSharpness[maxIndex] : thresholds.Max();

            sharpness.Update(thresholds, current, cap);
        }

        private static Player player;
        private static ulong playerNameOffset, firstPlayerPtr, currentPlayerPtr, currentPlayerAddress;
        private static string name;
        private static int damage;

        private static Player UpdateAndGetTeamPlayer(Process process, int playerIndex, ulong playerDamageCollectionAddress, ulong playerNameCollectionAddress)
        {
            player = null;

            playerNameOffset = (ulong)DataOffsets.PlayerNameCollection.PlayerNameLength * (ulong)playerIndex;
            name = MemoryHelper.ReadString(process, playerNameCollectionAddress + DataOffsets.PlayerNameCollection.FirstPlayerName + playerNameOffset, (uint)DataOffsets.PlayerNameCollection.PlayerNameLength);
            firstPlayerPtr = playerDamageCollectionAddress + DataOffsets.PlayerDamageCollection.FirstPlayerPtr;
            currentPlayerPtr = firstPlayerPtr + ((ulong)playerIndex * DataOffsets.PlayerDamageCollection.NextPlayerPtr);
            currentPlayerAddress = MemoryHelper.Read<ulong>(process, currentPlayerPtr);
            damage = MemoryHelper.Read<int>(process, currentPlayerAddress + DataOffsets.PlayerDamage.Damage);

            if (!String.IsNullOrEmpty(name) || damage > 0)
            {
                player = OverlayViewModel.Instance.TeamWidget.Context.UpdateAndGetPlayer(playerIndex, name, damage);
            }

            return player;
        }

        public static void UpdateDamageOnScreen(Process process, ulong damageOnScreenPtr)
        {
            var p = OverlayViewModel.Instance.TeamWidget.Context.Players.Where(p => p.Name.Equals(OverlayViewModel.Instance.DebugWidget.Context.CurrentGame.CurrentPlayerName));
            if (p.Any())
            {
                var currentPlayer = p.First();
                ulong startOfList = damageOnScreenPtr + 0x2900;
                ulong currentItem = startOfList;
                for (int i = 0; i < DataOffsets.PlayerDamage.MaxOnScreenDamages; i++)
                {
                    int id1 = MemoryHelper.Read<int>(process, currentItem + 0x20);
                    int id2 = MemoryHelper.Read<int>(process, currentItem + 0x24);

                    if (expeditionDamageChecker[i, 0] != id1 || expeditionDamageChecker[i, 1] != id2)
                    {
                        expeditionDamageChecker[i, 0] = id1;
                        expeditionDamageChecker[i, 1] = id2;

                        int value = MemoryHelper.Read<int>(process, currentItem + 0x34);

                        currentPlayer.Damage += value;
                    }
                    currentItem += 0x90;
                }
                if (!OverlayViewModel.Instance.DebugWidget.Context.CurrentGame.IsPlayerAlone() && OverlayViewModel.Instance.DebugWidget.Context.CurrentGame.playersCheckDone && OverlayViewModel.Instance.DebugWidget.Context.CurrentGame.helloDone && networkOperationDoneD && DateTime.Now.Second != lastNetworkOperationTimeD)
                {
                    networkOperationDoneD = false;
                    ServerManager.Instance.RequestCommadWithHandler(ServerManager.Command.DAMAGE, OverlayViewModel.Instance.DebugWidget.Context.CurrentGame.key, OverlayViewModel.Instance.DebugWidget.Context.CurrentGame.CurrentPlayerName, OverlayViewModel.Instance.DebugWidget.Context.CurrentGame.IsCurrentPlayerLobbyHost(), currentPlayer.Damage, null, (result, ping) =>
                    {
                        if (result != null && result["status"].ToString().Equals("error"))
                        {
                            if (result["result"].ToString().Equals("0"))
                            {
                                OverlayViewModel.Instance.DebugWidget.Context.CurrentGame.helloDone = false;
                                OverlayViewModel.Instance.DebugWidget.Context.CurrentGame.checkDone = false;
                                OverlayViewModel.Instance.DebugWidget.Context.CurrentGame.playersCheckDone = false;
                            }
                            else if (result["result"].ToString().Equals("false", StringComparison.CurrentCultureIgnoreCase))
                            {
                                OverlayViewModel.Instance.DebugWidget.Context.CurrentGame.playersCheckDone = false;
                            }
                            else if (result["result"].ToString().Equals("v"))
                            {
                                ServerManager.Instance.IsServerOline = -1;
                                Core.Log.WriteLine("The sync server no longer accepts this version. Party sync is off until Aether updates.");
                            }
                            else if (result["result"].ToString().Equals("dev"))
                            {
                                ServerManager.Instance.IsServerOline = -1;
                                Core.Log.WriteLine("The sync server is down for maintenance. Party sync is off for now.");
                            }
                        }
                        else if (result != null && result["status"].ToString().Equals("ok"))
                        {
                            if (!result["result"].ToString().Equals(""))
                            {
                                var damageData = JsonConvert.DeserializeObject<Dictionary<string, int>>(result["result"].ToString());
                                foreach (var id in damageData.Keys)
                                {
                                    if (!id.Equals(OverlayViewModel.Instance.DebugWidget.Context.CurrentGame.CurrentPlayerName))
                                    {
                                        var p = OverlayViewModel.Instance.TeamWidget.Context.Players.Where(p => p.Name.Equals(id));
                                        if (p.Any())
                                        {
                                            p.First().Damage = damageData[id];
                                            p.First().HasSyncedDamage = true;
                                        }
                                    }
                                }
                            }
                        }
                        networkOperationDoneD = true;
                        lastNetworkOperationTimeD = DateTime.Now.Second;
                    }, (error) =>
                    {
                        networkOperationDoneD = true;
                        lastNetworkOperationTimeD = DateTime.Now.Second;
                    });
                }
            }
        }

        public static void UpdateMonsterWidget(Process process, ulong monsterBaseList, ulong mapBaseAddress)
        {
            var context = OverlayViewModel.Instance.MonsterWidget.Context;
            if (monsterBaseList < 0xffffff)
            {
                context.Monsters.Clear();
                context.HasVisibleMonsters = false;
                return;
            }

            // Always read every large monster slot; the map pin only decides focus. Monsters stay in the
            // list while you change the pin, so they keep their part history and don't re-animate in.
            ulong selectedMonsterAddress = 0;
            if (mapBaseAddress != 0x0)
            {
                bool isMonsterSelected = MemoryHelper.Read<ulong>(process, mapBaseAddress + 0x128) != 0x0 && MemoryHelper.Read<ulong>(process, mapBaseAddress + 0x130) != 0x0 && MemoryHelper.Read<ulong>(process, mapBaseAddress + 0x160) != 0x0;
                if (isMonsterSelected)
                {
                    selectedMonsterAddress = MemoryHelper.Read<ulong>(process, mapBaseAddress + 0x148);
                }
            }

            monsterAddresses[0] = monsterBaseList;
            monsterAddresses[1] = MemoryHelper.Read<ulong>(process, monsterBaseList - 0x30) + 0x40;
            monsterAddresses[2] = MemoryHelper.Read<ulong>(process, MemoryHelper.Read<ulong>(process, monsterBaseList - 0x30) + 0x10) + 0x40;
            updatedMonsters.Clear();
            foreach (var monsterAddress in monsterAddresses.Concat(new[] { selectedMonsterAddress }).Where(a => a > 0xffffff).Distinct())
            {
                var monster = UpdateAndGetMonster(process, monsterAddress);
                if (monster != null)
                {
                    updatedMonsters.Add(monster);
                }
            }
            // Clean out monsters that aren't in the linked list anymore
            foreach (var obsoleteMonster in context.Monsters.Except(updatedMonsters).ToList())
            {
                context.Monsters.Remove(obsoleteMonster);
            }
            context.UpdateFocus(selectedMonsterAddress);

            if (ConfigHelper.Main.Values.Overlay.MonsterWidget.UseNetworkServer && ServerManager.Instance.IsServerOline == 1 && OverlayViewModel.Instance.DebugWidget.Context.CurrentGame.IsValid && OverlayViewModel.Instance.DebugWidget.Context.CurrentGame.IsPlayerOnline())
            {
                if (OverlayViewModel.Instance.DebugWidget.Context.CurrentGame.IsCurrentPlayerLobbyHost())
                {
                    if (!OverlayViewModel.Instance.DebugWidget.Context.CurrentGame.IsPlayerAlone())
                    {
                        if (OverlayViewModel.Instance.DebugWidget.Context.CurrentGame.helloDone && OverlayViewModel.Instance.DebugWidget.Context.CurrentGame.checkDone)
                        {
                            if (networkOperationDone && DateTime.Now.Second != lastNetworkOperationTime)
                            {
                                Dictionary<string, Dictionary<string, Dictionary<string, int[]>>> data = new Dictionary<string, Dictionary<string, Dictionary<string, int[]>>>();
                                // Keyed by species: two of the same monster would throw on Add, and dead ones have nothing to share
                                foreach (var monster in OverlayViewModel.Instance.MonsterWidget.Context.Monsters.Where(m => m.IsAlive).GroupBy(m => m.Id).Select(g => g.First()))
                                {
                                    if (OverlayViewModel.Instance.DebugWidget.Context.CurrentGame.IsValid && OverlayViewModel.Instance.DebugWidget.Context.CurrentGame.IsPlayerOnline() && !OverlayViewModel.Instance.DebugWidget.Context.CurrentGame.IsPlayerAlone())
                                    {
                                        if (OverlayViewModel.Instance.DebugWidget.Context.CurrentGame.helloDone && networkOperationDone && DateTime.Now.Second != lastNetworkOperationTime)
                                        {
                                            Dictionary<string, Dictionary<string, int[]>> monsterData = new Dictionary<string, Dictionary<string, int[]>>();
                                            Dictionary<string, int[]> monsterPartsData = new Dictionary<string, int[]>();
                                            foreach (var part in monster.Parts)
                                            {
                                                int[] partValues = new int[4];
                                                partValues[0] = part.IsRemovable ? 1 : 0;
                                                partValues[1] = (int)part.Health.Max;
                                                partValues[2] = (int)part.Health.Current;
                                                partValues[3] = part.TimesBrokenCount;
                                                monsterPartsData.Add((monster.Parts.IndexOf(part) + 1).ToString(), partValues);
                                            }
                                            monsterData.Add("parts", monsterPartsData);

                                            Dictionary<string, int[]> monsterStatusesData = new Dictionary<string, int[]>();
                                            foreach (var status in monster.StatusEffects)
                                            {
                                                int[] statusValues = new int[5];
                                                statusValues[0] = (int)status.Buildup.Max;
                                                statusValues[1] = (int)status.Buildup.Current;
                                                statusValues[2] = (int)status.Duration.Max;
                                                statusValues[3] = (int)status.Duration.Current;
                                                statusValues[4] = (int)status.TimesActivatedCount;
                                                monsterStatusesData.Add(status.Index.ToString(), statusValues);
                                            }
                                            monsterData.Add("statuses", monsterStatusesData);
                                            data.Add(monster.Id, monsterData);
                                        }
                                    }
                                }
                                if (data.Keys.Count > 0)
                                {
                                    networkOperationDone = false;
                                    ServerManager.Instance.RequestCommadWithHandler(ServerManager.Command.PUSH, OverlayViewModel.Instance.DebugWidget.Context.CurrentGame.key, null, true, 0, JsonConvert.SerializeObject(data), (result, ping) =>
                                    {
                                        if (result != null && result["status"].ToString().Equals("error"))
                                        {
                                            if (result["result"].ToString().Equals("false", StringComparison.CurrentCultureIgnoreCase))
                                            {
                                                OverlayViewModel.Instance.DebugWidget.Context.CurrentGame.checkDone = false;
                                            }
                                            else if (result["result"].ToString().Equals("0"))
                                            {
                                                OverlayViewModel.Instance.DebugWidget.Context.CurrentGame.helloDone = false;
                                                OverlayViewModel.Instance.DebugWidget.Context.CurrentGame.checkDone = false;
                                            }
                                            else if (result["result"].ToString().Equals("v"))
                                            {
                                                ServerManager.Instance.IsServerOline = -1;
                                                Core.Log.WriteLine("The sync server no longer accepts this version. Party sync is off until Aether updates.");
                                            }
                                            else if (result["result"].ToString().Equals("dev"))
                                            {
                                                ServerManager.Instance.IsServerOline = -1;
                                                Core.Log.WriteLine("The sync server is down for maintenance. Party sync is off for now.");
                                            }
                                        }
                                        networkOperationDone = true;
                                        lastNetworkOperationTime = DateTime.Now.Second;
                                    }, (error) =>
                                    {
                                        networkOperationDone = true;
                                        lastNetworkOperationTime = DateTime.Now.Second;
                                    });
                                }
                            }
                        }
                    }
                }
                else
                {
                    if (OverlayViewModel.Instance.DebugWidget.Context.CurrentGame.helloDone && OverlayViewModel.Instance.DebugWidget.Context.CurrentGame.checkDone)
                    {
                        if (networkOperationDone && DateTime.Now.Second != lastNetworkOperationTime)
                        {
                            networkOperationDone = false;
                            ServerManager.Instance.RequestCommadWithHandler(ServerManager.Command.PULL, OverlayViewModel.Instance.DebugWidget.Context.CurrentGame.key, null, false, 0, null, (result, ping) =>
                            {
                                if (result != null)
                                {
                                    if (result["status"].ToString().Equals("ok"))
                                    {
                                        if (!result["result"].ToString().Equals(""))
                                        {
                                            // The server keeps a lobby's last snapshot after the host leaves; only trust it while it's moving
                                            string payload = result["result"].ToString();
                                            if (payload != lastPulledPayload)
                                            {
                                                lastPulledPayload = payload;
                                                lastPulledMonsterData = DateTime.Now;
                                            }
                                            var monstersData = JsonConvert.DeserializeObject<Dictionary<string, Dictionary<string, Dictionary<string, int[]>>>>(result["result"].ToString());//.ToObject<Dictionary<string, Dictionary<string, Dictionary<string, int[]>>>>();
                                            foreach (var id in monstersData.Keys)
                                            {
                                                var m = OverlayViewModel.Instance.MonsterWidget.Context.Monsters.Where(m => m.Id.Equals(id) && m.IsAlive);
                                                if (m.Any())
                                                {
                                                    var monster = m.First();
                                                    var monsterData = monstersData[monster.Id];
                                                    if (monsterData != null && monsterData.TryGetValue("parts", out var monsterPartsData) && monsterPartsData != null)
                                                    {
                                                        UpdateMonsterParts(monsterPartsData, monster);
                                                    }
                                                    if (monsterData != null && monsterData.TryGetValue("statuses", out var monsterStatusesData) && monsterStatusesData != null)
                                                    {
                                                        UpdateMonsterStatusEffects(monsterStatusesData, monster);
                                                    }
                                                    UpdateMonsterPartsSoften(process, monster);
                                                }
                                            }
                                        }
                                    }
                                    else
                                    {
                                        if (result["status"].ToString().Equals("error"))
                                        {
                                            if (result["result"].ToString().Equals("false", StringComparison.CurrentCultureIgnoreCase))
                                            {
                                                OverlayViewModel.Instance.DebugWidget.Context.CurrentGame.checkDone = false;
                                            }
                                            else if (result["result"].ToString().Equals("0"))
                                            {
                                                OverlayViewModel.Instance.DebugWidget.Context.CurrentGame.helloDone = false;
                                                OverlayViewModel.Instance.DebugWidget.Context.CurrentGame.checkDone = false;
                                            }
                                            else if (result["result"].ToString().Equals("v"))
                                            {
                                                ServerManager.Instance.IsServerOline = -1;
                                                Core.Log.WriteLine("The sync server no longer accepts this version. Party sync is off until Aether updates.");
                                            }
                                            else if (result["result"].ToString().Equals("dev"))
                                            {
                                                ServerManager.Instance.IsServerOline = -1;
                                                Core.Log.WriteLine("The sync server is down for maintenance. Party sync is off for now.");
                                            }
                                        }
                                    }
                                }
                                networkOperationDone = true;
                                lastNetworkOperationTime = DateTime.Now.Second;
                            }, (error) =>
                            {
                                networkOperationDone = true;
                                lastNetworkOperationTime = DateTime.Now.Second;
                            });
                        }
                    }
                }
            }
        }

        private static ulong tmp, health_component, nameptr;
        private static string id;
        private static float maxHealth, currentHealth, sizeScale, scaleModifier;
        private static Monster monster;

        private static Monster UpdateAndGetMonster(Process process, ulong monsterAddress)
        {
            monster = null;

            //ulong tmp = monsterAddress + DataOffsets.Monster.MonsterStartOfStructOffset + DataOffsets.Monster.MonsterHealthComponentOffset;
            tmp = monsterAddress + DataOffsets.Monster.MonsterHealthComponentOffset;
            health_component = MemoryHelper.Read<ulong>(process, tmp);
            nameptr = MemoryHelper.Read<ulong>(process, monsterAddress + 0x2A0);
            id = MemoryHelper.ReadString(process, nameptr + 0x0C, (uint)DataOffsets.MonsterModel.IdLength);
            ulong nameptr2 = MemoryHelper.Read<ulong>(process, monsterAddress + 0x8A00);
            string id2 = MemoryHelper.ReadString(process, nameptr2 + 0x0C, (uint)DataOffsets.MonsterModel.IdLength);
            maxHealth = MemoryHelper.Read<float>(process, health_component + DataOffsets.MonsterHealthComponent.MaxHealth);

            if (String.IsNullOrEmpty(id))
            {
                return monster;
            }

            id = id.Split('\\').Last();
            string[] id2_temp = id2.Split('\\');
            if (id2_temp.Length >= 3)
                id2 = id2_temp[1] + "_" + id2_temp[2];
            else
                id2 = null;
            if (id2 != null && Monster.IsIncluded(id2) && !id.Equals(id2))
                id = id2;
            if (!Monster.IsIncluded(id))
            {
                return monster;
            }

            if (maxHealth <= 0)
            {
                return monster;
            }

            currentHealth = MemoryHelper.Read<float>(process, health_component + DataOffsets.MonsterHealthComponent.CurrentHealth);
            //float sizeScale = MemoryHelper.Read<float>(process, monsterAddress + DataOffsets.Monster.MonsterStartOfStructOffset + DataOffsets.Monster.SizeScale);
            //float scaleModifier = MemoryHelper.Read<float>(process, monsterAddress + DataOffsets.Monster.MonsterStartOfStructOffset + DataOffsets.Monster.ScaleModifier);
            sizeScale = MemoryHelper.Read<float>(process, monsterAddress + DataOffsets.Monster.SizeScale);
            scaleModifier = MemoryHelper.Read<float>(process, monsterAddress + DataOffsets.Monster.ScaleModifier);
            if (scaleModifier <= 0 || scaleModifier >= 2)
            {
                scaleModifier = 1;
            }

            monster = OverlayViewModel.Instance.MonsterWidget.Context.UpdateAndGetMonster(monsterAddress, id, maxHealth, currentHealth, sizeScale, scaleModifier);

            string action = ReadMonsterAction(process, monsterAddress);
            monster.IsCaptured = action.Contains("Capture");
            monster.IsAlive = currentHealth > 0 && !monster.IsCaptured && !IsDeathAction(action);
            if (!monster.IsAlive)
            {
                return monster;
            }

            if (ConfigHelper.MonsterData.Values.Monsters.ContainsKey(id) && ConfigHelper.MonsterData.Values.Monsters[id].Parts != null && ConfigHelper.MonsterData.Values.Monsters[id].Parts.Count() > 0)
            {
                // Only the host's game has exact part HP and ailment buildup. When the host's numbers are arriving
                // through the sync server they win; otherwise our own memory is the best estimate we have.
                var game = OverlayViewModel.Instance.DebugWidget.Context.CurrentGame;
                bool isClient = game.IsValid && game.IsPlayerOnline() && !game.IsCurrentPlayerLobbyHost();
                bool hostDataArriving = isClient && (DateTime.Now - lastPulledMonsterData).TotalSeconds < 60;
                OverlayViewModel.Instance.MonsterWidget.Context.IsEstimate = isClient && !hostDataArriving;
                if (!hostDataArriving)
                {
                    // Parts built from host data have key addresses; read from memory, they need rediscovering
                    if (monster.Parts.Any(p => p.Address <= HostPartKeyLimit))
                    {
                        monster.Parts.Clear();
                    }
                    UpdateMonsterParts(process, monster);
                    if (ConfigHelper.MonsterData.Values.Monsters[id].Parts.Where(p => p.IsRemovable).Count() > 0) // In case you are testing add "|| true"
                    {
                        UpdateMonsterRemovableParts(process, monster);
                    }
                    UpdateMonsterStatusEffects(process, monster);
                }
                UpdateMonsterPartsSoften(process, monster);
            }

            return monster;
        }

        // Tells you, once per lobby, where the monster numbers come from
        static string s_NoticeKey;
        static DateTime s_ClientSince;
        public static void UpdateSyncNotice()
        {
            var game = OverlayViewModel.Instance.DebugWidget.Context.CurrentGame;
            var context = OverlayViewModel.Instance.MonsterWidget.Context;
            if (!game.IsValid || !game.IsPlayerOnline() || !game.IsPlayerInLobby() || game.IsPlayerAlone() || !context.Monsters.Any())
            {
                s_ClientSince = DateTime.MinValue;
                return;
            }

            string host = game.PartyLeaderName.Length > 0 ? game.PartyLeaderName : "The host";
            bool sync = ConfigHelper.Main.Values.Overlay.MonsterWidget.UseNetworkServer;
            string kind = null, text = null;
            double seconds = 6;
            if (game.IsCurrentPlayerLobbyHost())
            {
                if (sync && game.playersCheckDone)
                {
                    kind = "host";
                    text = "You're the host. Sharing monster data with your party.";
                }
            }
            else
            {
                if (s_ClientSince == DateTime.MinValue)
                {
                    s_ClientSince = DateTime.Now;
                }
                bool hostDataArriving = (DateTime.Now - lastPulledMonsterData).TotalSeconds < 60;
                if (sync && hostDataArriving)
                {
                    kind = "synced";
                    text = $"Getting parts and ailments from {host}'s game.";
                }
                else if (!sync || (DateTime.Now - s_ClientSince).TotalSeconds > 20)
                {
                    kind = "estimate";
                    seconds = 12;
                    text = sync
                        ? $"{host} isn't sharing data (they need Aether with party sync on). Parts and ailments are your game's estimate and can be off."
                        : "Party sync is off, so parts and ailments are your game's estimate and can be off. Turn it on in Settings > Party sync.";
                }
            }

            string key = kind + "|" + game.key;
            if (kind != null && key != s_NoticeKey)
            {
                s_NoticeKey = key;
                context.ShowNotice(text, seconds);
                Log.WriteLine(text);
            }
        }

        // Current action's reference name, e.g. "nActEm001::Die" (HunterPie's GetMonsterAction)
        private static string ReadMonsterAction(Process process, ulong monsterAddress)
        {
            ulong actionPointer = monsterAddress + 0x61C8;
            int actionId = MemoryHelper.Read<int>(process, actionPointer + 0xB0);
            if (actionId < 0 || actionId > 0x1000)
            {
                return "";
            }
            actionPointer = MemoryHelper.Read<ulong>(process, actionPointer + 2 * 8 + 0x68);
            actionPointer = MemoryHelper.Read<ulong>(process, actionPointer + (ulong)actionId * 8);
            actionPointer = MemoryHelper.Read<ulong>(process, actionPointer);
            actionPointer = MemoryHelper.Read<ulong>(process, actionPointer + 0x20);
            if (actionPointer < 0xffffff)
            {
                return "";
            }
            uint actionOffset = MemoryHelper.Read<uint>(process, actionPointer + 3);
            ulong actionRef = MemoryHelper.Read<ulong>(process, actionPointer + actionOffset + 7 + 8);
            return actionRef < 0xffffff ? "" : MemoryHelper.ReadString(process, actionRef, 64);
        }

        public static bool IsDeathAction(string action)
        {
            return (action.Contains("Die") && !action.Contains("DieSleep")) || (action.Contains("Dead") && !action.Contains("Deadly"));
        }

        internal static void UpdateMonsterParts(Dictionary<string, int[]> parts, Monster monster)
        {
            // Keys are 1-based positions in the host's part list. The client builds its list from them in that order,
            // so part names line up with the host's even when this game discovered its parts differently.
            // The key doubles as the part's address; real addresses are always far above it.
            if (monster.Parts.Any(p => p.Address > HostPartKeyLimit))
            {
                monster.Parts.Clear();
            }
            // The sync server is a third party: only accept as many parts as the game can have, in the shape we expect
            int maxParts = DataOffsets.MonsterPartCollection.MaxItemCount + DataOffsets.MonsterRemovablePartCollection.MaxItemCount;
            var valid = parts
                .Select(e => new { Ok = int.TryParse(e.Key, out int key), Key = key, Values = e.Value })
                .Where(e => e.Ok && e.Key >= 1 && e.Key <= maxParts && e.Values != null && e.Values.Length >= 4)
                .OrderBy(e => e.Key);
            foreach (var entry in valid)
            {
                monster.UpdateAndGetPart((ulong)entry.Key, entry.Values[0] == 1, entry.Values[1], entry.Values[2], entry.Values[3]);
            }
        }

        internal static void UpdateMonsterStatusEffects(Dictionary<string, int[]> statuses, Monster monster)
        {
            foreach (KeyValuePair<string, int[]> entry in statuses)
            {
                // Keyed by status index; keep our real address so memory reads still work if the host stops sending.
                // Indexes outside the status table, or short entries, are ignored (the server is a third party).
                if (!int.TryParse(entry.Key, out int index) || index < 0 || index >= ConfigHelper.MonsterData.Values.StatusEffects.Length
                    || entry.Value == null || entry.Value.Length < 5)
                {
                    continue;
                }
                ulong address = monster.StatusEffects.FirstOrDefault(st => st.Index == index)?.Address ?? 0;
                monster.UpdateAndGetStatusEffect(address, index, entry.Value[0], entry.Value[1], entry.Value[2], entry.Value[3], entry.Value[4]);
            }
        }

        private static void UpdateMonsterParts(Process process, Monster monster)
        {
            var parts = monster.Parts.Where(part => !part.IsRemovable);
            if (parts.Any())
            {
                foreach (var part in parts)
                {
                    UpdateMonsterPart(process, monster, part.Address);
                }
            }
            else
            {
                ulong firstPartAddress = monster.Address + DataOffsets.Monster.PartCollection + DataOffsets.MonsterPartCollection.FirstPart;
                // Names come from the config by index, so extra memory slots would shift every name after them
                int configPartCount = ConfigHelper.MonsterData.Values.Monsters[monster.Id].Parts.Count(p => !p.IsRemovable);

                for (int index = 0; index < DataOffsets.MonsterPartCollection.MaxItemCount && monster.Parts.Count(p => !p.IsRemovable) < configPartCount; ++index)
                {
                    ulong currentPartOffset = DataOffsets.MonsterPart.NextPart * (ulong)index;
                    ulong currentPartAddress = firstPartAddress + currentPartOffset;

                    float maxHealth = MemoryHelper.Read<float>(process, currentPartAddress);

                    if (maxHealth > 0)
                    {
                        UpdateMonsterPart(process, monster, currentPartAddress);
                    }
                }
            }
        }

        private static void UpdateMonsterPart(Process process, Monster monster, ulong partAddress)
        {
            float maxHealth = MemoryHelper.Read<float>(process, partAddress + DataOffsets.MonsterPart.MaxHealth);
            float currentHealth = MemoryHelper.Read<float>(process, partAddress + DataOffsets.MonsterPart.CurrentHealth);
            int timesBrokenCount = MemoryHelper.Read<int>(process, partAddress + DataOffsets.MonsterPart.TimesBrokenCount);

            monster.UpdateAndGetPart(partAddress, false, maxHealth, currentHealth, timesBrokenCount);
        }

        private static void UpdateMonsterRemovableParts(Process process, Monster monster)
        {
            var removableParts = monster.Parts.Where(part => part.IsRemovable);
            if (removableParts.Any())
            {
                foreach (var removablePart in removableParts)
                {
                    UpdateMonsterRemovablePart(process, monster, removablePart.Address);
                }
            }
            else
            {
                //ulong removablePartAddress = monster.Address + DataOffsets.Monster.RemovablePartCollection + DataOffsets.MonsterRemovablePartCollection.FirstRemovablePart;
                ulong removablePartAddress = monster.Address + DataOffsets.Monster.RemovablePartCollection;
                for (int index = 0; index < DataOffsets.MonsterRemovablePartCollection.MaxItemCount; ++index)
                {
                    // Every 16 elements there seems to be a new removable part collection. When we reach this point,
                    // we advance past the first 64 bit field to get to the start of the next part again
                    ulong staticPtr = MemoryHelper.Read<ulong>(process, removablePartAddress);
                    if (staticPtr <= 10)
                    {
                        removablePartAddress += 8;
                    }

                    uint maxRemovableParts = (uint)ConfigHelper.MonsterData.Values.Monsters[monster.Id].Parts.Where(p => p.IsRemovable).Count();
                    //bool isLast = MemoryHelper.Read<uint>(process, removablePartAddress + 0x94) == 0x1;
                    //bool isValid = MemoryHelper.Read<byte>(process, removablePartAddress + 0x15) == 0;
                    bool isValid = MemoryHelper.Read<uint>(process, removablePartAddress + 0x6C) < maxRemovableParts;

                    if (isValid)
                    {
                        float maxHealth = MemoryHelper.Read<float>(process, removablePartAddress + DataOffsets.MonsterRemovablePart.MaxHealth);
                        int mpart = MemoryHelper.Read<int>(process, removablePartAddress + 0x14);
                        if (maxHealth > 0)
                        {
                            MonsterPart mPart = UpdateMonsterRemovablePart(process, monster, removablePartAddress);
                            if (/*isLast || */monster.Parts.Where(p => p.IsRemovable).Count() == maxRemovableParts)
                                break;
                            bool isSame;
                            do
                            {
                                removablePartAddress += DataOffsets.MonsterRemovablePart.NextRemovablePart;
                                float nMaxHealth = MemoryHelper.Read<float>(process, removablePartAddress + DataOffsets.MonsterRemovablePart.MaxHealth);
                                float nHealth = MemoryHelper.Read<float>(process, removablePartAddress + DataOffsets.MonsterRemovablePart.CurrentHealth);
                                int npart = MemoryHelper.Read<int>(process, removablePartAddress + 0x14);
                                int nBrokenCount = MemoryHelper.Read<int>(process, removablePartAddress + DataOffsets.MonsterRemovablePart.TimesBrokenCount);
                                isSame = (mPart.Health.Max == nMaxHealth && mPart.Health.Current == nHealth && mpart == npart);
                            } while (isSame);
                        }
                    }

                    removablePartAddress += DataOffsets.MonsterRemovablePart.NextRemovablePart;
                }
            }
        }

        private static MonsterPart UpdateMonsterRemovablePart(Process process, Monster monster, ulong removablePartAddress)
        {
            float maxHealth = MemoryHelper.Read<float>(process, removablePartAddress + DataOffsets.MonsterRemovablePart.MaxHealth);
            float currentHealth = MemoryHelper.Read<float>(process, removablePartAddress + DataOffsets.MonsterRemovablePart.CurrentHealth);
            int timesBrokenCount = MemoryHelper.Read<int>(process, removablePartAddress + DataOffsets.MonsterRemovablePart.TimesBrokenCount);

            return monster.UpdateAndGetPart(removablePartAddress, true, maxHealth, currentHealth, timesBrokenCount);
        }

        private static void UpdateMonsterPartsSoften(Process process, Monster monster)
        {
            for (int i = 0; i <= 10; i++)
            {
                ulong softenAddress = monster.Address + DataOffsets.MonsterSoftenPart.SoftenPartOffset + (ulong)i * DataOffsets.MonsterSoftenPart.NextSoftenPart;
                float maxTime = MemoryHelper.Read<float>(process, softenAddress + DataOffsets.MonsterSoftenPart.MaxDuration) + MemoryHelper.Read<float>(process, softenAddress + DataOffsets.MonsterSoftenPart.MaxExtraDuration);
                float currentTime = MemoryHelper.Read<float>(process, softenAddress + DataOffsets.MonsterSoftenPart.CurrentDuration) + MemoryHelper.Read<float>(process, softenAddress + DataOffsets.MonsterSoftenPart.CurrentExtraDuration);
                uint timesCount = MemoryHelper.Read<uint>(process, softenAddress + DataOffsets.MonsterSoftenPart.TimesCountOffset);
                uint partid = MemoryHelper.Read<uint>(process, softenAddress + DataOffsets.MonsterSoftenPart.PartIdOffset);

                var partsoftens = monster.PartSoftens.Where(partsoften => partsoften.Address == softenAddress);
                if (partsoftens.Any())
                    foreach (var partsoften in partsoftens)
                        if (partid != uint.MaxValue && maxTime > 0 && maxTime >= currentTime && partsoften.Time.Max == maxTime)
                            monster.UpdateAndGetPartSoften(softenAddress, maxTime, maxTime - currentTime, timesCount, partid);
                        else
                            monster.UpdateAndGetPartSoften(softenAddress, partsoften.Time.Max, 0, partsoften.TimesCount, partsoften.PartID);
                else if (partid != uint.MaxValue && maxTime > 0 && maxTime >= currentTime)
                    monster.UpdateAndGetPartSoften(softenAddress, maxTime, maxTime - currentTime, timesCount, partid);
            }
        }

        private static void UpdateMonsterStatusEffects(Process process, Monster monster)
        {
            int maxIndex = ConfigHelper.MonsterData.Values.StatusEffects.Where(s => s.GroupId.Equals("StatusEffect")).Count() - 1;
            var statuses = monster.StatusEffects;
            if (statuses != null && statuses.Any(s => s.GroupId.Equals("StatusEffect") && s.Address > 0xffffff))
            {
                for (int i = 0; i < statuses.Count(); i++)
                {
                    MonsterStatusEffect status = statuses[i];
                    if (status == null || status.Address <= 0xffffff)
                    {
                        continue;
                    }
                    float currentBuildup = 0;
                    float maxBuildup = MemoryHelper.Read<float>(process, status.Address + DataOffsets.MonsterStatusEffect.MaxBuildup);
                    if (maxBuildup > 0)
                    {
                        currentBuildup = MemoryHelper.Read<float>(process, status.Address + DataOffsets.MonsterStatusEffect.CurrentBuildup);
                    }
                    float currentDuration = 0;
                    float maxDuration = MemoryHelper.Read<float>(process, status.Address + DataOffsets.MonsterStatusEffect.MaxDuration);
                    if (maxDuration > 0)
                    {
                        currentDuration = MemoryHelper.Read<float>(process, status.Address + DataOffsets.MonsterStatusEffect.CurrentDuration);
                    }
                    int timesActivatedCount = MemoryHelper.Read<int>(process, status.Address + DataOffsets.MonsterStatusEffect.TimesActivatedCount);

                    if (maxBuildup > 0 || maxDuration > 0)
                    {
                        uint index = MemoryHelper.Read<uint>(process, status.Address + 0x198);
                        if (index <= maxIndex)
                        {
                            var statusEffectConfig = ConfigHelper.MonsterData.Values.StatusEffects[index];
                            monster.UpdateAndGetStatusEffect(status.Address, (int)index, maxBuildup > 0 ? maxBuildup : 1, !statusEffectConfig.InvertBuildup ? currentBuildup : maxBuildup - currentBuildup, maxDuration, !statusEffectConfig.InvertDuration ? currentDuration : maxDuration - currentDuration, timesActivatedCount);
                        }
                    }
                }
            }
            else
            {
                ulong baseStatus = MemoryHelper.Read<ulong>(process, monster.Address + 0x78);
                baseStatus = MemoryHelper.Read<ulong>(process, baseStatus + 0x57A8);
                ulong nani = baseStatus;
                while (nani != 0)
                {
                    nani = MemoryHelper.Read<ulong>(process, nani + 0x10);
                    if (nani != 0)
                    {
                        baseStatus = nani;
                    }
                }
                ulong currentStatusPointer = baseStatus + 0x40;
                while (currentStatusPointer != 0x0)
                {
                    var currentMonsterInStatus = MemoryHelper.Read<ulong>(process, currentStatusPointer + 0x188);
                    if (currentMonsterInStatus == monster.Address && !monster.StatusEffects.Where(status => status.Address == currentStatusPointer).Any())
                    {
                        float currentBuildup = 0;
                        float maxBuildup = MemoryHelper.Read<float>(process, currentStatusPointer + DataOffsets.MonsterStatusEffect.MaxBuildup);
                        if (maxBuildup > 0)
                        {
                            currentBuildup = MemoryHelper.Read<float>(process, currentStatusPointer + DataOffsets.MonsterStatusEffect.CurrentBuildup);
                        }
                        float currentDuration = 0;
                        float maxDuration = MemoryHelper.Read<float>(process, currentStatusPointer + DataOffsets.MonsterStatusEffect.MaxDuration);
                        if (maxDuration > 0)
                        {
                            currentDuration = MemoryHelper.Read<float>(process, currentStatusPointer + DataOffsets.MonsterStatusEffect.CurrentDuration);
                        }
                        int timesActivatedCount = MemoryHelper.Read<int>(process, currentStatusPointer + DataOffsets.MonsterStatusEffect.TimesActivatedCount);

                        if (maxBuildup > 0 || maxDuration > 0)
                        {
                            uint index = MemoryHelper.Read<uint>(process, currentStatusPointer + 0x198);
                            if (index <= maxIndex && !((index == 14 || index == 15) && monster.isElder) && index != 0) // skip traps for elders
                            {
                                var statusEffectConfig = ConfigHelper.MonsterData.Values.StatusEffects[index];
                                monster.UpdateAndGetStatusEffect(currentStatusPointer, (int)index, maxBuildup > 0 ? maxBuildup : 1, !statusEffectConfig.InvertBuildup ? currentBuildup : maxBuildup - currentBuildup, maxDuration, !statusEffectConfig.InvertDuration ? currentDuration : maxDuration - currentDuration, timesActivatedCount);
                            }
                        }
                    }
                    currentStatusPointer = MemoryHelper.Read<ulong>(process, currentStatusPointer + 0x18);
                }
            }

            // Stamina

            ulong staminaAddress = monster.Address + DataOffsets.Monster.MonsterStaminaOffset;
            float maxStaminaBuildUp = MemoryHelper.Read<float>(process, staminaAddress + 0x4);
            float currentStaminaBuildUp = 0;
            if (maxStaminaBuildUp > 0)
            {
                currentStaminaBuildUp = MemoryHelper.Read<float>(process, staminaAddress);
            }
            float maxFatigueDuration = MemoryHelper.Read<float>(process, staminaAddress + 0x0C);
            float currentFatigueDuration = 0;
            if (maxFatigueDuration > 0)
            {
                currentFatigueDuration = MemoryHelper.Read<float>(process, staminaAddress + 0x10);
            }
            int fatigueActivatedCount = MemoryHelper.Read<int>(process, staminaAddress + 0x14);
            MonsterStatusEffectConfig statusEffect = null;
            if (currentFatigueDuration >= 0)
            {
                statusEffect = ConfigHelper.MonsterData.Values.StatusEffects.SingleOrDefault(s => s.GroupId.Equals("Fatigue"));
                monster.UpdateAndGetStatusEffect(staminaAddress, Array.IndexOf(ConfigHelper.MonsterData.Values.StatusEffects, statusEffect), 1, 0, maxFatigueDuration, currentStaminaBuildUp == 0 ? maxFatigueDuration - currentFatigueDuration : 0, fatigueActivatedCount);
            }
            if (currentStaminaBuildUp >= 0)
            {
                statusEffect = ConfigHelper.MonsterData.Values.StatusEffects.SingleOrDefault(s => s.GroupId.Equals("Stamina"));
                monster.UpdateAndGetStatusEffect(staminaAddress, Array.IndexOf(ConfigHelper.MonsterData.Values.StatusEffects, statusEffect), maxStaminaBuildUp, currentStaminaBuildUp, 1, 0, 0);
            }
            /*
            if (currentFatigueDuration > 0)
            {
                statusEffect = ConfigHelper.MonsterData.Values.StatusEffects.SingleOrDefault(s => s.GroupId.Equals("Fatigue"));
            }
            else
            {
                statusEffect = ConfigHelper.MonsterData.Values.StatusEffects.SingleOrDefault(s => s.GroupId.Equals("Stamina"));
            }

            if (maxStaminaBuildUp > 0 || currentFatigueDuration > 0)
            {
                monster.UpdateAndGetStatusEffect(staminaAddress, Array.IndexOf(ConfigHelper.MonsterData.Values.StatusEffects, statusEffect), maxStaminaBuildUp > 0 ? maxStaminaBuildUp : 1, !statusEffect.InvertBuildup ? currentStaminaBuildUp : maxStaminaBuildUp - currentStaminaBuildUp, maxFatigueDuration, !statusEffect.InvertDuration ? currentFatigueDuration : maxFatigueDuration - currentFatigueDuration, fatigueActivatedCount);
            }
            */
            // Rage

            ulong rageAddress = monster.Address + DataOffsets.Monster.MonsterRageOffset;
            float maxRageBuildUp = MemoryHelper.Read<float>(process, rageAddress + 0x18);
            float currentRageBuildUp = 0;
            if (maxRageBuildUp > 0)
            {
                currentRageBuildUp = MemoryHelper.Read<float>(process, rageAddress - 0x0C);
            }
            float maxRageDuration = MemoryHelper.Read<float>(process, rageAddress + 0x04);
            float currentRageDuration = 0;
            if (maxRageDuration > 0)
            {
                currentRageDuration = MemoryHelper.Read<float>(process, rageAddress + 0x00);
            }
            int rageActivatedCount = MemoryHelper.Read<int>(process, rageAddress + 0x10);
            var rageStatusEffect = ConfigHelper.MonsterData.Values.StatusEffects.SingleOrDefault(s => s.GroupId.Equals("Rage"));//[33]; // 33 is rage
            if (maxRageBuildUp > 0 || maxRageDuration > 0)
            {
                monster.UpdateAndGetStatusEffect(rageAddress, Array.IndexOf(ConfigHelper.MonsterData.Values.StatusEffects, rageStatusEffect), maxRageBuildUp > 0 ? maxRageBuildUp : 1, !rageStatusEffect.InvertBuildup ? currentRageBuildUp : maxRageBuildUp - currentRageBuildUp, maxRageDuration, !rageStatusEffect.InvertDuration ? currentRageDuration : maxRageDuration - currentRageDuration, rageActivatedCount);
            }
        }
    }
}
