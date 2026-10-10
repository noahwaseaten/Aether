using System.Collections.Generic;
using System.Windows.Input;
using SmartHunter.Core.Config;

namespace SmartHunter.Game.Config
{
    public class MainConfig
    {
        public string VersionsFileName = "Versions.json";
        public string LocalizationFileName = "en-US.json";
        public string SkinFileName = "Default.xaml";
        public string MonsterDataFileName = "MonsterData.json";
        public string PlayerDataFileName = "PlayerData.json";
        public string MemoryFileName = "Memory.json";
        public string ServerUrl = "http://140.238.55.121/index.php";
        public string UserDataPath = @"C:\Program Files (x86)\Steam\userdata\";

        public bool IgnoreHttpsErrors = true;
        public bool ShutdownWhenProcessExits = true;
        public bool BackupWhenProcessExits = true;
        public bool AutomaticallyCheckAndDownloadUpdates = true;
        public bool UseSoftwareRendering = false; // fallback for graphics drivers that draw transparent overlay windows wrong
        public bool StartMHWWhenSmartHunterStart = false;

        public OverlayConfig Overlay = new OverlayConfig();

        public DiscordPresenceConfig DiscordPresence = new DiscordPresenceConfig();

        [PreserveCollectionIntegrity]
        public Dictionary<InputControl, Key> Keybinds = new Dictionary<InputControl, Key>()
        {
            { InputControl.ManipulateWidget, Key.Scroll }, // Scroll Lock, as in HunterPie: no game, chat or Discord overlay uses it
            { InputControl.HideWidgets, Key.F1 },
            { InputControl.CopyTeamDamage, Key.F5 },
            { InputControl.CopyPlayer1Damage, Key.F6},
            { InputControl.CopyPlayer2Damage, Key.F7},
            { InputControl.CopyPlayer3Damage, Key.F9},
            { InputControl.CopyPlayer4Damage, Key.F10}
        };

        public DebugConfig Debug = new DebugConfig();
    }

    public class DiscordPresenceConfig
    {
        public bool Enabled = true;
        public string ApplicationId = "477152881196269569"; // Discord's verified "Monster Hunter: World" app
        public string LargeImage = "https://cdn.cloudflare.steamstatic.com/steam/apps/582010/capsule_616x353.jpg";
    }
}
