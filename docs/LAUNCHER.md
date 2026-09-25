# Launcher: what the update check leaves for it

[日本語](LAUNCHER.ja.md)

> **Stage 1 is built**: the file below. The launcher itself (a small program that runs before the game from the Steam launch options) comes later.

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

## Updating from 1.5.0

1.5.0 kept only each release's tag, in `BepInEx/config/com.tomxv.dragnwash.modframework.updates.txt`. That file stays as it was. The details are read back from `updates.json` at start, for each release whose tag is still the one in the txt file. A release the txt file knows but `updates.json` doesn't (the first start after updating from 1.5.0, or after the cache folder was deleted) is checked once more straight away, instead of waiting for the next day, and from then on it's back to once a day.
