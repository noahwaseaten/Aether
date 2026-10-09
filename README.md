# Aether

An overlay for Monster Hunter: World on PC (Iceborne, game version 15.20). It's a fork of [SmartHunter](https://github.com/gabrielefilipp/SmartHunter) with a redesigned look, fixes for multiplayer and expeditions, and an installer that keeps itself up to date.

What it shows:

- The monster you're fighting: health, parts, breaks, tenderize timers, status buildup, rage and exhaustion. Other large monsters shrink to one line, or hide.
- Damage dealt by each hunter in the party.
- Your buffs and debuffs with time left, and your weapon's sharpness.
- Call-outs when a monster can be captured, is enraged or is exhausted.
- A results screen when a quest ends.

## Install

1. Download `Aether-x.y.z.zip` from the [latest release](https://github.com/noahwaseaten/Aether/releases/latest) and extract it.
2. Run `Install Aether.cmd`. If Windows says "Windows protected your PC", click More info, then Run anyway (Aether isn't code-signed).

It installs to `%LOCALAPPDATA%\Aether` with Start Menu and Desktop shortcuts, and no admin rights are needed. Aether checks for a new release every time it starts and installs it by itself.

Requires Windows 10 or 11 (.NET Framework 4.8 is already part of both).

## Use

Start Aether before or after the game. To move widgets, click Edit layout in the Aether window or hold Left Alt in game, then drag to move and scroll to resize. Ctrl+scroll resizes in small steps, double-click resets the size and Shift turns off snapping. Reset layout puts everything back. F1 hides the overlay while you hold it.

## Party sync

Only the host's game has exact part HP and ailment buildup, and the game doesn't count damage on expeditions at all. When everyone in the party runs Aether with "Share data with your party" on, the host's numbers and everyone's expedition damage go through the SmartHunter sync server, so clients see them too. It sends a hashed lobby ID, hunter names, damage and monster data. Turn it off in Settings if you'd rather not.

Without it, clients still see monster health (the game syncs that) and an estimate of parts and statuses from their own game.

## Building and releasing

Needs the .NET SDK and, for publishing, the GitHub CLI signed in to the repo.

```powershell
.\build.ps1 -Version 1.0.1            # build, self-test, dist\Aether-1.0.1.zip
.\build.ps1 -Version 1.0.1 -Publish   # same, then publish release v1.0.1
```

Publishing a release is how updates ship: every installed copy compares its version with the latest release's tag on startup and downloads that release's `Aether.exe`. Bump the version each time.

`Aether.exe --selftest` runs the built-in checks and writes `SelfTest.txt`. The build script refuses to package a build that fails them.

## Skins and translations

The look is built into the exe. To customize it, put a copy of `SmartHunter/Ui/Resources/Default.xaml` next to `Aether.exe` and edit it; Aether reloads it when the file changes. When a new version installs, a custom skin is renamed to `Default.xaml.old` so the new built-in look shows up. Rename it back to keep using yours.

For a translation, copy `en-US.json`, translate the values (not the keys), and point `LocalizationFileName` in `Config.json` at your file.

## Credits

Built on SmartHunter by [r00telement](https://github.com/r00telement/SmartHunter), [gabrielefilipp](https://github.com/gabrielefilipp/SmartHunter) and [dragonyue0417](https://github.com/dragonyue0417/SmartHunter). Monster, buff and weapon memory offsets cross-checked against [HunterPie](https://github.com/HunterPie/HunterPie-legacy). Damage chart idea by [Phil-Pa](https://github.com/Phil-Pa).

## Disclaimer

Use at your own risk. Aether only reads the game's memory and never writes to it, and there are no known bans in Monster Hunter: World for overlays like this, but that could change.
