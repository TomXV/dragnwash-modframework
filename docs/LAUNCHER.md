# Launcher: what the update check leaves for it

[日本語](LAUNCHER.ja.md)

> **Built so far**: the file below, and the Mods screen's Update button with the file it leaves for the launcher ([Updating from the Mods screen](#updating-from-the-mods-screen)). The launcher itself (a small program that runs before the game from the Steam launch options) is on its own branch.

The launcher doesn't go online to look for updates. The game already does that once a day ([Updates in the core](../src/DragNWash.ModFramework/Updates/UpdateCheck.cs)), so the launcher reads what that check found. The check asks GitHub the same single question as before; it just keeps more of the answer now: the release notes, the page, when it was published, and the files with their sizes and SHA-256.

## Where and when

`BepInEx/cache/DragNWash.ModFramework/updates.json`, UTF-8 without a BOM.

The game writes it:

- about 5 seconds after start, once the mods have registered, so the installed versions are this session's even when nothing was due;
- whenever a check brings a result;
- when **Check for updates** is switched on or off.

It's written to a temporary file beside it first and then moved into place, so a reader never sees half a file.

When **Check for updates** is off, the file is still written, with `"checking": false` and an empty `mods` list, so the launcher shows nothing.

## The format

```json
{
  "schema": 1,
  "written": "2026-09-25T01:55:48.7880680Z",
  "checking": true,
  "mods": [
    {
      "guid": "com.tomxv.dragnwash.modframework",
      "name": "Drag'n Wash ModFramework",
      "installedVersion": "1.4.3",
      "repository": "TomXV/dragnwash-modframework",
      "pluginFolder": "DragNWash.ModFramework",
      "installManifest": false,
      "manifestName": "",
      "manifestVersion": "",
      "latest": {
        "tag": "v1.5.0",
        "version": "1.5.0",
        "htmlUrl": "https://github.com/TomXV/dragnwash-modframework/releases/tag/v1.5.0",
        "publishedAt": "2026-09-23T13:03:06Z",
        "body": "## Drag'n Wash ModFramework 1.5.0\r\n\r\nA new look for everything you see: ...",
        "bodyTruncated": false,
        "assets": [
          {
            "name": "DragNWash.ModFramework-1.5.0.zip",
            "size": 1010034,
            "url": "https://github.com/TomXV/dragnwash-modframework/releases/download/v1.5.0/DragNWash.ModFramework-1.5.0.zip",
            "sha256": "c49e0f0b89fd770903c0041c8fbf5a93703f75dbc6f1fc7af06ef1def749b00d"
          }
        ],
        "checkedUtc": "2026-09-25T01:55:12.7326832Z"
      },
      "newer": true
    }
  ]
}
```

One entry per installed mod that names its GitHub repository (`ModInfo.UpdateRepository`), the framework included.

| Field | What it is |
|---|---|
| `schema` | 1. Goes up only for a change an older launcher can't read; new fields may be added without it. |
| `written` | When the game wrote the file, UTC, ISO 8601. |
| `checking` | Whether **Check for updates** is on. |
| `guid`, `name` | The mod's BepInEx GUID and the name the Mods screen shows. |
| `installedVersion` | The version BepInEx loaded this session. |
| `repository` | `owner/name` on GitHub. |
| `pluginFolder` | The mod's folder directly under `BepInEx/plugins`, or `""` when its DLL sits in `plugins` itself. |
| `installManifest` | True when that folder holds `mod-install.json`, which our installer writes there. Only then can the launcher update the mod itself; otherwise it can only open the release page. |
| `manifestName`, `manifestVersion` | `name` and `version` from that `mod-install.json`, or `""` when there's none or it doesn't read. |
| `latest` | The latest release, or `null` when the repository hasn't been checked yet or has no release. |
| `latest.tag` | The release's tag. |
| `latest.version` | The tag as a version (`v1.2` is `1.2.0`), or `null` when the tag isn't a version number. |
| `latest.htmlUrl` | The release page. |
| `latest.publishedAt` | When GitHub published it, as GitHub gives it, or `""`. |
| `latest.body` | The release notes (Markdown), cut at 65,536 characters; `bodyTruncated` says when they were. |
| `latest.assets` | The release's files, at most 50: `name`, `size` in bytes, `url` and `sha256` (lowercase hex, or `""` when GitHub gives no digest, as for files uploaded before it started to). |
| `latest.checkedUtc` | When the game asked GitHub, UTC, ISO 8601. |
| `newer` | True when `latest.version` is newer than `installedVersion`, the same comparison the Mods screen makes. |

### Reading it with .NET Framework

The launcher is .NET Framework 4.7.2 and reads this with `DataContractJsonSerializer`. The file is plain JSON so that works:

- `latest` is a real `null` when there's no release, and `version` too when the tag isn't a version. Every other string is `""` rather than missing, and `assets` is `[]` rather than missing.
- Times are ISO 8601 strings. Declare them as `string` and parse them yourself; `DataContractJsonSerializer` expects its own `/Date(...)/` form for `DateTime`.
- `size` fits a `long`.
- Fields the launcher doesn't declare are ignored, so new fields don't break it.

### What the launcher still has to check

The game keeps only `https://github.com/` addresses for `htmlUrl`, and only files under `https://github.com/<repository>/releases/download/` in `assets`. The file sits in the game folder, though, so anyone who can write there can change it. Before installing anything, the launcher has to check these addresses again and compare the download's size and SHA-256 with these fields. A file with no `sha256` can't be checked that way.

## Updating from the Mods screen

When a mod has a newer release and our installer put it in (its folder has `mod-install.json`, `installManifest` above), its "New version available" note on the Mods screen gets **Update now** next to **Open release page**. Other mods keep just the page button. So does every mod on a PC without the WebView2 runtime, because the launcher can't update anything without it.

**Update now** asks first, the same way **Uninstall** does. A yellow note at the top of the notes says the game will quit and unsaved progress may be lost, the button turns into **Quit and update** and keeps the focus, and **Open release page** turns into **Cancel**. Pressing it again (A twice on a pad) goes ahead. Picking another mod, leaving the screen or Back (B) cancels. It can't be pressed while the Mods screen is still checking the mods.

Going ahead, the game writes `BepInEx/cache/DragNWash.ModFramework/update-request.json`, again through a temporary file:

```json
{
  "schema": 1,
  "requestedUtc": "2026-09-25T10:00:00Z",
  "gamePid": 1234,
  "mods": [ "com.tomxv.dragnwash.localization" ]
}
```

`gamePid` is the game's process id, and `mods` holds that one mod's GUID. Then:

- **Started by the launcher**: the game just quits, the way its own Quit button does. The launcher is waiting for it, finds the request, updates the mod and starts the game again. The launcher sets `DNW_LAUNCHER=1` for the game it starts; a game without it still counts as started by the launcher when its parent process is `BepInEx/DragNWash.Installer/Launcher.exe`.
- **Started any other way** (from Steam without the launch option, or from the exe): the game starts `Launcher.exe --update-after-exit --wait-pid <pid> --mods "<guid>"` without a window and then quits. The launcher waits for the game to close before its window shows (or after ten seconds, if the game is slow to close), updates the mod and starts the game again through Steam.

If `Launcher.exe` isn't there, the game writes nothing and doesn't quit. The note says the launcher isn't installed and that running the installer again adds it; **Open release page** stays. If anything else goes wrong, `BepInEx/LogOutput.log` says what, and the page button still works.

The game itself still downloads nothing. The launcher only goes online after the player pressed Update, here or in the launcher's own window: it downloads the release file and checks its size and SHA-256 against `updates.json` before installing it.

### The launcher's settings

The launcher reads the `[Launcher]` section of `BepInEx/config/com.tomxv.dragnwash.modframework.cfg` when it starts, and uses the defaults when the file or a line is missing. The game adds the section, so it's also on the Mods screen, under the framework's Settings. The launch option switch above these two isn't in the file (see the next section).

| Key | Values | Default |
|---|---|---|
| `Logo lettering` | `Handwriting` (drawn stroke by stroke) or `Typewriter` (typed letter by letter) | `Handwriting` |
| `Progress bar` | `Bottom edge` (along the window's bottom edge) or `Under text` | `Bottom edge` |

## Switching the launch option from the Mods screen

The `[Launcher]` section on the framework's Settings tab starts with one more row: **Check for mod updates when the game starts (Steam launch option)**. It's the same thing as Install.exe's checkbox, the launcher in front of `%command%` in Steam. It isn't in the config file. The switch shows whether the launcher is in the game's launch options right now, for the Steam account you're playing on (read from that account's `localconfig.vdf` when the Mods screen opens), so it has no dot for a changed value and no reset button.

The row is only there on Windows, with `BepInEx/DragNWash.Installer/Launcher.exe` in place and the WebView2 runtime installed. The launcher only runs on Windows, so the Steam Deck (and Proton) don't get it.

Steam keeps its settings in memory and writes that file again when it exits, so the game can't change it itself. Pressing the switch asks first, like **Update now**: a yellow note at the top of the details says the game will quit and Steam and the game will start again, and the switch turns into **Cancel** and **Restart and apply**, with the focus on **Restart and apply** (A twice on a pad). Picking another mod or another tab, leaving the screen or Back (B) cancels. Nothing says "Saved", because nothing has changed yet.

**Restart and apply** starts `Launcher.exe --launch-option on|off --wait-pid <pid>` without a window, and the game quits the way its own Quit button does. Once the game has closed, the launcher closes Steam, changes the launch option and starts Steam and the game again ([LAUNCHER_APP.md](LAUNCHER_APP.md)). If the launcher can't be started, the game doesn't quit, and a note says so; `BepInEx/LogOutput.log` says why.

## Updating from 1.5.0

1.5.0 kept only each release's tag, in `BepInEx/config/com.tomxv.dragnwash.modframework.updates.txt`. That file stays as it was. The details are read back from `updates.json` at start, for each release whose tag is still the one in the txt file. A release the txt file knows but `updates.json` doesn't (the first start after updating from 1.5.0, or after the cache folder was deleted) is checked once more straight away, instead of waiting for the next day, and from then on it's back to once a day.
