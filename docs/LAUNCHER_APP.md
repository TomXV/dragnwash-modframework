# Launcher: the program that runs before the game

[日本語](LAUNCHER_APP.ja.md)

> **Built**: `Launcher.exe` itself, with its window, and the installer's part: Install.exe puts it in the game folder and sets Steam's launch option. The game's Update button doesn't use it yet; that comes next. What the game writes for it (`updates.json`) is described in `docs/LAUNCHER.md`.

When the game's update check found a new version of a mod last time you played, the launcher shows it before the game starts: what changed, and a button to install it and play. You no longer have to open GitHub, download the zip and run Install.exe again. The game isn't running yet at that point, so no file is in use.

## Where it is and how it starts

`<game>\BepInEx\DragNWash.Installer\Launcher.exe`, with the three WebView2 files next to it (`Microsoft.Web.WebView2.Core.dll`, `Microsoft.Web.WebView2.WinForms.dll`, `WebView2Loader.dll`). The framework's zip has them at that path.

Steam's launch option:

```
"<game>\BepInEx\DragNWash.Installer\Launcher.exe" %command%
```

Steam replaces `%command%` with the game's exe and its arguments. The launcher starts exactly that, once its own window has closed, so the two never show at once. It stays running as the game's parent until the game exits, with no window (so Steam keeps counting play time), and sets `DNW_LAUNCHER=1` for the game so the game knows the launcher is waiting.

The other way in is for a game that was started without the launcher:

```
Launcher.exe --update-after-exit --wait-pid <pid> [--mods <guid>,<guid>]
```

It waits for that process to exit, installs the updates, and asks Steam to start the game again (`steam://rungameid/4739660`). Without `--mods`, the mods come from `update-request.json`.

The third is the Mods screen's launch option switch ([LAUNCHER.md](LAUNCHER.md#switching-the-launch-option-from-the-mods-screen)):

```
Launcher.exe --launch-option on|off --wait-pid <pid>
```

It waits for that process to exit, closes Steam, puts itself into the game's launch options (`on`) or takes itself out (`off`), and starts Steam and the game again.

## What happens

**Before the game starts**

1. It reads `BepInEx/cache/DragNWash.ModFramework/updates.json`. This is a file read only: the launcher doesn't go online to look for updates. The game does that, once a day.
2. When there's nothing to show (no newer version, the file isn't there yet, the game's update check is off, or every update is one you skipped), the logo plays for about four seconds: "Checking for updates", "No updates", then "Starting the game" while the bar fills up. Then the window fades out and closes, and the game starts. A click or any key shortens it. This window doesn't take the focus from other apps.
3. Otherwise the logo moves up and the list of updates comes in: each mod with its version now and the new one, the size, and its release notes. Tick the ones you want. **Update and play** installs them and starts the game; **Play without updating** just starts it. Either way, the window closes first. **Skip this version** stops that version of that mod from bringing the window up again; the next version will.
4. The list only offers a checkbox for mods the installer put in (their folder has `mod-install.json`). Other mods get **Open release page** instead.

A version already installed since the game looked (the mod's `mod-install.json` says so) isn't shown. The other way round, when an older version has been put back since then, it is shown even though the game didn't list it.

**After an update asked for in the game**

The game writes `BepInEx/cache/DragNWash.ModFramework/update-request.json` and quits. When the game exits, the launcher finds the file (only one written after this launch counts; it's deleted once read), shows the update screen, installs, counts down 10 seconds, closes the window and starts the game again with the same command. **Start now** starts it right away instead of waiting, and **Don't start it now** closes the window without starting it.

**Switching the launch option from the game**

The window comes once the game has closed (or after ten seconds, if the game is slow to close). It's the same board as an update's, with steps of its own: wait for the game to close, close Steam, change the launch option, start Steam and the game.

1. **Close Steam.** Steam writes over `localconfig.vdf` while it runs, so the launcher asks it to exit (`steam.exe -shutdown`, what its own Exit does) and waits up to 90 seconds. When Steam isn't running, this step just passes. A launcher that started the game is waited for too, since Steam counts the game as running until it has gone.
2. **Change the launch option.** With Install.exe's own code (`installer/Steam.cs` and `SteamConfig.cs` are compiled into both): the same accounts and the same rules as the checkbox below, `localconfig.vdf.dnw-backup` first, and nothing else in the file changes.
3. **Done.** The same 10-second countdown as after an update, then Steam and the game start together (`steam://rungameid/4739660`). With the option on, the game comes through the launcher again, so the logo shows first. **Start now** skips the wait.

When Steam hasn't closed after 90 seconds, nothing is changed: **Try again** starts again from closing Steam, and **Just start the game** starts it through Steam as it is. When the file can't be written, nothing is changed either, and the backup stays. There's no Try again for that: after the countdown, Steam and the game start as they were.

✕ and **Don't start them now** never start the game here either. If the launcher already closed Steam, it starts Steam again on its own, so you're not left without it. While the file is being written, the window waits for that to finish first. Nothing goes online, and the log has every step.

**The window's ✕**

✕ (or Alt+F4) closes the launcher without starting the game, on every screen: the logo, the list, the failure screen and the countdown. Before the checks are done, it cancels the download first, so the game folder stays as it was; while files are being written, it does nothing. The launcher then exits, so Steam sees the game as stopped. To play without the update, press **Play without updating**.

**Without WebView2**

No window at all. The game starts as usual (or, after `--update-after-exit`, Steam is asked to start it without updating), and the log says why. The Mods screen still links to each release page.

**If anything goes wrong**

The game still starts (unless you closed the window with ✕). A failure while updating shows what happened and what became of the game folder. Before the game, you can try again or play without updating. After an update asked for in the game, the game starts again unchanged after the countdown. When the page doesn't come up (6 seconds for the logo, 15 for the other screens) or the window doesn't close within 2.5 seconds, the window is hidden and the game starts anyway.

## Installing

The launcher goes online only after you press **Update and play** (or Update in the game), and only to github.com and release-assets.githubusercontent.com.

- **Which file.** The release's zip: the only `.zip`, or, when there are several, the one with the version in its name (like `DragNWash.ModFramework-1.5.1.zip`). A release where neither is true gets only the release page.
- **Which address.** Built from the repository and the tag in `updates.json`: `https://github.com/<owner>/<repo>/releases/download/<tag>/<file>`. The address in the file has to be exactly that, or the zip isn't used. The release page opened by **Open release page** has to be on github.com under the same repository.
- **Checks.** The download has to be the size GitHub gave, and have the SHA-256 GitHub gave. Files uploaded before GitHub started giving a SHA-256 are checked by size only, and the log says so. Then the zip has to hold `mod-install.json` next to a `BepInEx` folder, for the mod's own folder under `plugins`. A zip that fails a check is not used, and nothing in the game folder has changed.
- **Installing.** With Install.exe's own code (the files in `installer/` are compiled into both), the same way `Install.exe --install` does it: the mod's choices keep what the config file says, a pinned ModFramework is fetched when needed, and a framework part is never replaced by an older one. The files it replaces are backed up to `BepInEx\DragNWash.Installer\backup\<date_time>` first, and if copying fails, everything is put back.
- **Several mods** are downloaded and checked first, then installed one after another. Each install is complete on its own, so if the second one fails, the first stays updated (the failure screen says so). As with Install.exe, only the last install's backup is kept.
- **Cancel** works until the checks are done. Until then nothing is written to the game folder.

## Putting it in place: Install.exe

**The launcher itself.** Whenever Install.exe installs or updates the framework, it also puts `Launcher.exe` and its three WebView2 files into `BepInEx\DragNWash.Installer`, from the framework's zip (or from the mod's zip, when that brings the framework). It does so whether or not the launch option is set, because the game's Update button uses the launcher too. It goes through the same backup and putting back as every other file, and a newer launcher already there is never replaced by an older one. When the launcher installs an update itself, its own files are in use: they're renamed to `<name>.old` and the new ones put in their place, and the next install deletes the `.old` copies.

**The checkbox.** The Install group has **Check for mod updates when the game starts (sets the Steam launch option)**, ticked to begin with. If the launcher is already in the game folder but in none of the game's launch options, it was left out last time, so the box starts unticked. The list of what Install will do then says "Add the update launcher to the game's launch options in Steam (launch options you already have stay)", or that it's already there. The box is greyed out when there's no Steam account on the PC or no launcher to start (a mod that pins a framework older than 1.6.0).

Unticked, and the launcher is in the launch options: Install takes it out ("Take the update launcher out of the game's launch options in Steam (your other launch options stay)").

**What gets written.** Every Steam account's `userdata\<id>\config\localconfig.vdf`, under `UserLocalConfigStore > Software > Valve > Steam > apps > 4739660 > LaunchOptions` (missing blocks are made). The launcher goes in front of `%command%`, so whatever you had still reaches the game:

| You had | It becomes |
|---|---|
| (nothing) | `"<game>\BepInEx\DragNWash.Installer\Launcher.exe" %command%` |
| `-force-d3d11` | `"...\Launcher.exe" %command% -force-d3d11` |
| `<something> %command% -x` | `<something> "...\Launcher.exe" %command% -x` |
| the launcher of another game folder | the same, with this game folder's path |

Taking it out removes the launcher, and a `%command%` left at the start with nothing before it: `-force-d3d11` comes back as `-force-d3d11` (Steam puts options without `%command%` after the game, so both start the game the same way). Anything else you had in front of `%command%` stays.

- **Which accounts.** Putting it in: every account that has played the game (its file has the game's block) and the one that signed in last (`config\loginusers.vdf`); when neither can be told, every account. Taking it out: every account that has it.
- **Only that value changes.** The rest of the file stays byte for byte: its order, tabs, escapes and line ends. The file as it was is copied to `localconfig.vdf.dnw-backup` first, and the new one replaces it in one step. A file that isn't UTF-8 or isn't laid out as expected is left alone, and the log says so.
- **The log** says, for each account, what the options were and what they are now.

**When Steam is running.** Steam keeps its settings in memory and writes them over the file when it exits, so the file can only be changed with Steam closed. After you press Install, Update or Uninstall, and before the download question or any change, a window says so, with three buttons:

- **Close Steam for me** asks Steam to exit (`steam.exe -shutdown`, what its own Exit does) and waits. When Steam hasn't closed after 90 seconds, the window says so, and you can close it yourself or skip.
- **I'll close it** waits until Steam is gone.
- **Skip this option** (on Uninstall, **Leave it as it is**) carries on without changing the launch options this time.

Either way, the window carries on by itself once Steam has closed. The close box and Esc cancel the whole install, with nothing changed. When the installer closed Steam, it starts Steam again once the install has gone well.

**Uninstalling.** When Uninstall removes ModFramework itself (no other mod uses it), it takes the launcher out of the launch options first (same window if Steam is running), then removes `BepInEx\DragNWash.Installer` with the launcher in it. While other mods still use the framework, the launcher and the launch option both stay, and only the installer's backup goes. If the launch option couldn't be taken out (Steam running and skipped), the launcher stays too: without it, the game wouldn't start from Steam.

**Command line.** `Install.exe --install` sets the launch option too; `--launch-option on|off|keep` changes that (`on` is the default, `off` takes it out, `keep` leaves the launch options alone). `--uninstall` takes it out when ModFramework goes, unless `--launch-option keep`. The command line never closes Steam: while Steam runs, the launch options are left as they are, and the log says so.

## Settings

The launcher reads `[Launcher]` in `BepInEx\config\com.tomxv.dragnwash.modframework.cfg` (the game registers these, so they show in the Mods screen too). Missing means the default.

| Setting | Values |
|---|---|
| `Logo lettering` | `Handwriting` (default), `Typewriter`: how MOD FRAMEWORK comes in on the logo |
| `Progress bar` | `Bottom edge` (default), `Under text`: where the logo screen's progress bar sits |

The window is in Japanese when Drag'n Wash Localization is set to Japanese (`TargetLocale` in its config), otherwise in English; without Localization's setting it follows Windows' language. Release notes show their `## 日本語` section in Japanese and the rest in English.

## Files

| File | |
|---|---|
| `BepInEx/cache/DragNWash.ModFramework/updates.json` | Read. Written by the game. |
| `BepInEx/cache/DragNWash.ModFramework/update-request.json` | Read and deleted. Written by the game. |
| `BepInEx/cache/DragNWash.ModFramework/launcher-state.json` | The launcher's own: `{ "schema": 1, "skipped": { "<guid>": "<tag>" } }` |
| `BepInEx/DragNWash.Installer/launcher.log` | This run's log, in English. The run before is `launcher.prev.log`. |
| `%LOCALAPPDATA%\DragNWash ModFramework\Launcher\WebView2` | WebView2's own data for the window. |

## The window

A borderless 720 × 440 window with WebView2 (part of Windows 10 and 11). The page (`launcher/web/`) is inside the exe and served from it at `https://launcher.invalid/`: the page can't load anything else, can't go online, and can't open other pages. **Open release page** and **Open the log folder** go through the launcher, which checks the address and opens it in your browser.

The page and the launcher talk in small JSON messages. The page posts `{cmd, ...}` with `chrome.webview.postMessage`, and the launcher calls `window.dnw(event)`:

- **Page to launcher**: `ready`, `update` (the ticked mods), `skip`, `open` (a mod's release page), `proceed`, `cancel`, `retry`, `play` (also the end of the logo), `close`, `minimize`, `drag`, `openLog`, `copy`, `gone` (faded out).
- **Launcher to page**: `init` (the mode, language, settings and mods), `step` (`wait`, `dl`, `chk`, `bak`, `ins`, and `steam`, `opt` for the launch option), `download` (bytes), `verified` (one zip passed), `checked` (all passed; the launcher writes nothing until the page answers `proceed`, once its check pictures are done), `log`, `done`, `failed`, `cancelled`, `bye` (the window is closing).

Closing goes the same way on every screen: the launcher sends `bye`, the page fades out (0.25 s) and answers `gone`, the launcher fades the window itself away (0.16 s) and hides it, and only then starts the game. With reduce-animations on, the window just goes.

The launcher's work can be far ahead of the pictures, so the page queues the events and shows each step for at least the time it has in the design.

## Building and testing

`tools/pack.ps1` builds it into the framework's zip. By hand:

```
dotnet build launcher/DragNWash.Launcher.csproj -c Release -warnaserror
```

A Debug build also reads these environment variables, for trying it without GitHub, Steam or the game; a Release build ignores them:

| Variable | |
|---|---|
| `DNW_LAUNCHER_LOCAL_RELEASES=<folder>` | Copies the zips from this folder instead of downloading them. |
| `DNW_LAUNCHER_FAIL=offline\|busy\|limited\|notfound` | With the above, fails the download that way. |
| `DNW_LAUNCHER_NO_WEBVIEW2=1` | Behaves as if WebView2 isn't installed. |
| `DNW_LAUNCHER_NO_STEAM=1` | Doesn't ask Steam to start the game. |
| `DNW_LAUNCHER_WEB=<launcher/web folder>` | Serves the page from disk, so it can be changed without building again. |

Any exe named `DragNWash.exe` in a folder with `BepInEx\core\BepInEx.dll` will do as the game.

Install.exe's part: `dotnet run --project installer/tests -c Release` checks the launch option rules and the `localconfig.vdf` edits on made-up files (CI runs it too). A Debug build of Install.exe also reads these; a Release build ignores them:

| Variable | |
|---|---|
| `DNW_INSTALLER_STEAM=<folder>` | Takes this folder for Steam's (`steamapps`, `userdata`, `config\loginusers.vdf`, `steam.exe`), and only a `steam.exe` started from it counts as Steam running. For trying the launch option without the real Steam. |
| `DNW_INSTALLER_STEAM_WAIT=<seconds>` | How long **Close Steam for me** waits before saying Steam hasn't closed (otherwise 90). |

A Debug build of the launcher reads these two as well, so `--launch-option` can be tried against a made-up Steam folder with a stand-in `steam.exe` that exits when it's run again with `-shutdown`.
