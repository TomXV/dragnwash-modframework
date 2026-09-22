# Security policy

[日本語](SECURITY.ja.md)

Drag'n Wash ModFramework is an unofficial mod for a game, not a service, but it
runs code on other people's computers and it installs files for them. Reports
about that are welcome and taken seriously.

## Reporting a vulnerability

**Please do not open a public issue for a security problem.**

Use GitHub's private reporting instead:
[**Report a vulnerability**](https://github.com/TomXV/dragnwash-modframework/security/advisories/new)
(the repository's **Security** tab → **Report a vulnerability**). Only you and
the maintainer can see the report, and it stays private until a fix is out.

Helpful things to put in it:

- What an attacker can do, and what they need in order to do it (a file the
  player downloads? a mod already installed? a network they control?)
- The steps to reproduce it, and the version of the framework, the game and
  BepInEx you saw it on
- The log, if there is one: `BepInEx/LogOutput.log`, or the report saved by
  the [crash reporter](https://github.com/TomXV/dragnwash-modframework/wiki/Crash-reports)

This is a hobby project run by one person. You should get a first reply within
a week. If a week goes by with nothing, a ping on the same advisory thread is
welcome — the report was not ignored on purpose.

When a report is confirmed, the fix goes into the next release, the advisory is
published with it, and you are credited in it by whatever name you ask for (or
not credited at all, if you prefer). Please give the fix a chance to reach
players before writing about the problem in public.

## What is covered

The latest release is the one that gets fixes. Older versions do not get
back-ported patches; the answer to "is it fixed in 1.0.x?" is "update".

| Version | Supported |
|---|---|
| The latest release ([Releases](https://github.com/TomXV/dragnwash-modframework/releases)) | Yes |
| Anything older | No — please update first |

In scope, everything this repository ships:

- The core plugin and the libraries (`src/`), and the preloader patcher
- `Install.exe`, the shared installer other mods use ([`installer/`](installer/)),
  including the Steam Deck script
- The crash reporter ([`crashreporter/`](crashreporter/)) and the Code Graph
  tools ([`codegraph/`](codegraph/), [`codegraph-standalone/`](codegraph-standalone/))
- The build and release workflows in [`.github/workflows/`](.github/workflows/)

The places worth looking hardest at, because they are where the framework
touches the outside world:

- **The installer**
  - **What it does:** it picks a game folder, downloads the official BepInEx
    5.4.23.5 release and checks it against a SHA-256 pinned in the source
    before unpacking it, and it reads a mod's `mod-install.json`.
  - **A real bug:** a path that escapes the folder it should stay in, or a way
    to make it accept a different archive.
- **Update notices**
  - **What they do:** they ask `api.github.com` once a day for the latest
    release of a mod that names its repository, send nothing about the player
    and download nothing — the Mods screen only opens the release page in a
    browser.
  - **A real bug:** anything that turns that into a download, or that leaks
    information about the player.
  - See [Going online (wiki)](https://github.com/TomXV/dragnwash-modframework/wiki/Going-online).
- **Asset and mod loading**
  - **What it does:** `GameAssets` and the Overrides library read files out of
    a mod's folder.
  - **A real bug:** a crafted file that reaches outside it, or that makes the
    framework load code the player did not install.
- **The crash reporter**
  - **What it does:** it writes a report file and shows it.
  - **The rule:** a report should never contain more of the player's machine
    than the paths it needs.

## What is not covered

- **A mod can do anything the game can do.** Mods are plain .NET assemblies
  loaded by BepInEx into the game's process; the framework gives them a tidy
  API, not a sandbox, and it cannot take one away. "A malicious mod could read
  my files" is how modding works everywhere, not a flaw in the framework. Only
  install mods you trust.
- **The game itself.** Bugs in Drag'n Wash belong to its developer, not here.
  Do not send them a crash that only happens with mods installed.
- **BepInEx.** Report those to [the BepInEx project](https://github.com/BepInEx/BepInEx).
- **Antivirus false positives on `Install.exe`.** An unsigned installer gets
  flagged; that is a detection quirk, not a vulnerability. What to do about it
  is in the README of the mod you installed, and the installer's source is right
  here. A regular issue is fine for those.
- **Copies from anywhere but [Releases](https://github.com/TomXV/dragnwash-modframework/releases).**
  Each release lists the zip's SHA-256. If a file you got elsewhere does
  something nasty, that is the file, not this project — though a report telling
  us where you found it is still appreciated.
