# Aether

Monster Hunter: World overlay (WPF, .NET Framework 4.8, x64), forked from SmartHunter. The exe is `Aether.exe`;
code still uses the `SmartHunter` namespace and folder names. Aether only **reads** game memory. Never add
anything that writes to it.

## Build and test

```powershell
.\build.ps1 -Version 0.0.0      # build Release, run the self-test, package dist\Aether-0.0.0.zip
```

- The self-test (`Aether.exe --selftest`, `SmartHunter/Core/SelfTest.cs`) runs pure logic checks with no game.
  Add a `Check(...)` there for new parsing or decision logic.
- Nothing here can test against the live game. Say so when a change touches memory reading, and don't claim it works in game.
- To try the UI, copy `SmartHunter\bin\Release\Aether.exe` to a scratch folder and run it there; it writes its
  configs and `Log.txt` next to the exe. Only one copy can run at a time (a mutex), so close the others first.
- A `-Version 0.0.0` build run outside the scratch folder **updates itself** from GitHub. That's handy for testing
  the updater, but don't run it from `bin\Release`.

## Git and pushing

- Remotes: `aether` = github.com/noahwaseaten/Aether (ours). `origin` = gabrielefilipp/SmartHunter (the upstream
  this was forked from: **read-only, never push there**).
- Push to `aether`'s `main`: `git push aether <branch>:main`. `main` is branch-protected ("Changes must be
  made through a pull request"); the maintainer's admin account (noahwaseaten, via `gh auth`) bypasses it, and
  GitHub prints that warning even when the push succeeds. Only push straight to `main` when the maintainer asks.
- No attribution lines in commit messages. Commits read like `Wait for the game before scanning, add app icon`.
- `core.autocrlf=true`: the LF->CRLF warnings on commit are expected. Don't write files with a BOM (PowerShell
  `Set-Content -Encoding utf8` adds one).

## Releasing

Every installed copy checks GitHub releases on startup, so **publishing a release is shipping to every user**.

**Every change players would notice gets a release.** Once it's tested and pushed to `main`, publish it in the same
session without waiting to be asked: PATCH for fixes and polish, MINOR for features. Don't leave work on `main`
unreleased. Internal-only changes (docs, build scripts) don't need one.

```powershell
.\build.ps1 -Version 1.2.0 -Publish -Notes "What players will notice, in plain sentences."
```

- Push the commits to `aether/main` **first**: `gh release create` tags the current `main`.
- `-Notes` is required with `-Publish`. Build it in a PowerShell here-string (`@'...'@`), one `- ` line per change.
  `build.ps1` passes the notes through `--notes-file`, because Windows PowerShell 5.1 breaks native arguments
  that contain double quotes.
- Bump the version and write the notes in the same session that publishes, then check with
  `gh release view vX.Y.Z --repo noahwaseaten/Aether`.
- Versions are semver `MAJOR.MINOR.PATCH` and the tag is `v1.2.0`. Bump PATCH for fixes, MINOR for new features or
  settings. Never reuse or lower a version; the updater only installs a version higher than the running one.
- The release must have an asset named exactly `Aether.exe`: that's what the updater downloads. `build.ps1`
  attaches it, plus `Aether-x.y.z.zip` for new installs, which contains `Install Aether.cmd`.
- **Release notes are shown in the app** ("What's new" card), so write them for players, not developers.
  Markdown is cleaned to plain text (`- ` becomes a bullet, `#`, `**` and backticks are stripped). Someone who
  skips versions sees the notes of every version in between, each under its own heading.

## How the updater works (`Core/Helpers/AppUpdater.cs`, `Game/Data/ViewModels/UpdateViewModel.cs`)

- It downloads `Aether.exe` from the newest release, checks the SHA-256 that GitHub publishes for the asset, then
  renames the running exe to `Aether.exe.old` and puts the new one in its place. A running exe can be renamed but
  not overwritten. It also writes `Aether.exe.notes`; on its first start, the new version shows those notes and
  deletes the file.
- It restarts by itself only at startup, before Aether started the game, and only while the game isn't running.
  Then it shows an 8 second countdown that can be cancelled. Otherwise it asks "Restart now / Later".
- "Start the game with Aether" waits for the startup update, up to 30 s (`TryReleaseGameLaunch`). Without that,
  Aether could start the game, restart for the update, and the new copy would start it a second time.

## Things that bit us

- **The game process exists before it's readable.** Right after launch, `Process.MainModule` throws
  ("Only part of a ReadProcessMemory...") and the exe is still unpacking. `MemoryUpdater` waits for the game window
  (`IsProcessReady`) before scanning.
- **The state machine runs a transition's `Begin` before it switches state.** If `Begin` throws, it stays in the
  old state and retries on the next tick. Keep it that way: switching first is what once left Aether in "Working"
  with no game data.
- Every state that holds a `Process` needs a `HasExited` transition, or Aether gets stuck when the game closes.
- Pattern match results (`BytePattern.MatchedAddresses`) are cleared when the game restarts; stale addresses
  would be read as if they were still valid.
- `App.xaml.cs` caps every WPF animation at 30 fps to keep the in-game overlay cheap. The Aether window's
  animations opt out and run at the monitor's refresh rate (`WindowHelper.RefreshRate`). Do the same for new
  window animations, but not for overlay widgets.
- Player buffs and debuffs (`PlayerDataConfig.cs`) mirror HunterPie-legacy's
  `HunterPie/HunterPie.Resources/Data/AbnormalityData.xml`: offset = index × 4. Check its `HasConditions`,
  `ConditionOffset` and `IsPercentageBuff` flags when adding one. Blastscourge ignored them and showed "1 s" forever.
  HunterPie v2 (`HunterPie/HunterPie`, `HunterPie/Game/World/Data/AbnormalityData.xml`, hex offsets) is newer and
  has more skill timers; check it first. Player paralysis, sleep and stun aren't in either, nor in any other public
  MHW project found (Oct 2026), so Aether can't show them without new memory research.
- The quest clock (`MhwHelper.UpdateHuntInfo`) uses HunterPie v2's `HunterPie/Address/MonsterHunterWorld.421810.map`.
  Its offset lists mean the same as Aether's `ReadMultiLevelPointer`: dereference, then add.
- Weapon buffs (Charge Blade, Insect Glaive, Long Sword, Switch Axe) are ordinary entries in the buff list, never a
  separate line; the maintainer rejected that as duplication. The game stores them without Power Prolonger, and
  `MhwHelper.WeaponTimerSeconds` scales them the way HunterPie's `GetPowerProlongerMultiplier` does. Keep the
  widgets terse: the maintainer wants as little text on screen as possible.
- The quest recap is built 3 s after the quest ends (`HuntTracker`). The quest state can flip on the same tick as
  the killing blow, and building it immediately made a slain monster look "captured".
- Discord Rich Presence images are loaded by URL from `main` on GitHub: monster portraits from `SmartHunter/Ui/Monsters/`
  and weapon icons from `assets/discord/weapons/` (rendered by `assets/render-weapon-icons.ps1`). Moving or renaming
  those files breaks the images for every installed copy. It uses Discord's verified MHW app id. Buttons are links
  only and other people see them, not you. "Ask to Join" was ruled out: Aether can't join a game session for the player.
- The clipboard can be held by another app, and `Clipboard.SetText` then throws. Use the `CopyToClipboard` wrapper.
- On expeditions the game's quest roster (team widget names) can leave a slot blank, seen for your own. Without your
  name in the team list, Aether never sent your damage to the party. Blank slots fall back to the party struct
  (`MhwHelper.PartyMemberName`).
- Party sync uses one shared `HttpClient` with a short timeout (`ServerManager`). A client per request plus the 100 s
  default timeout let one stuck reply stall sync for over a minute.
- Before the game has your player name, its session ids are placeholders every copy shares. Don't sync until the name is read.
- Widget placement is saved on every drag and resize (`WidgetWindow.PlacementChanged`), not only when edit mode ends.
- Routine sync calls (pull, push, damage) aren't logged one by one: they filled the log and pushed out what mattered.
  Past sessions' logs are kept in `Logs\`.

## UI conventions (`Ui/Windows/ConsoleWindow.xaml`, `SettingsViewModel.cs`)

- Every setting gets a description; it shows in the row's tooltip. Use `Detail` for a value shown under the
  name (a path, a version). Mark a setting `requiresRestart` only if it's really read at startup alone.
- Colors: background `#111214`, surface `#1A1B1E`, accent gold `#E2C27A`, text `#F3EDE0`, muted `#A39C8C`.
  Overlay widget styles live in `Ui/Resources/Default.xaml`.
- Write user-facing text plainly: say what happens and what the player can do, and no stack traces in the UI.
- When something the player relies on fails, call `Problems.Report(key, text)` (`Core/Problems.cs`) as well as
  logging it. It shows in a card in the Aether window. Call `Problems.Clear(key)` once it works again.
