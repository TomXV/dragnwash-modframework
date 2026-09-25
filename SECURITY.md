# Security policy

[日本語](SECURITY.ja.md)

Drag'n Wash ModFramework is an unofficial mod for a game. It isn't a service,
but it does run code on other people's computers and install files for them, so
reports about that are welcome and taken seriously.

## Reporting a vulnerability

**Please don't open a public issue for a security problem.**

Use GitHub's private reporting instead:
[**Report a vulnerability**](https://github.com/TomXV/dragnwash-modframework/security/advisories/new)
(the repository's **Security** tab → **Report a vulnerability**). Only you and
the maintainer can see the report, and it stays private until a fix is out.

It helps a lot if the report says:

- what an attacker can do, and what they need to do it (a file the player
  downloads? a mod that's already installed? a network they control?)
- how to reproduce it, and which versions of the framework, the game and
  BepInEx you saw it on
- what the log says, if there is one: `BepInEx/LogOutput.log`, or the report
  the [crash reporter](https://github.com/TomXV/dragnwash-modframework/wiki/Crash-reports) saved

This is a hobby project and one person runs it, so expect a first reply within
a week. If a week goes by and you've heard nothing, feel free to ping the same
advisory thread. Nobody is ignoring the report on purpose.

Once a report is confirmed, the fix goes into the next release and the advisory
is published along with it. You're credited in it under whatever name you ask
for, or not at all if you'd rather. Please give the fix a chance to reach
players before you write about the problem in public.

## What is covered

Fixes go into the latest release. Older versions don't get patches back-ported,
so the answer to "is it fixed in 1.0.x?" is "update".

| Version | Supported |
|---|---|
| The latest release ([Releases](https://github.com/TomXV/dragnwash-modframework/releases)) | Yes |
| Anything older | No, please update first |

Everything this repository ships is in scope:

- the core plugin and the libraries (`src/`), and the preloader patcher
- `Install.exe`, the shared installer other mods use ([`installer/`](installer/)),
  along with the Steam Deck script
- `Launcher.exe`, the update launcher that runs before the game ([`launcher/`](launcher/))
- the crash reporter ([`crashreporter/`](crashreporter/)) and the Code Graph
  tools ([`codegraph/`](codegraph/), [`codegraph-standalone/`](codegraph-standalone/))
- the build and release workflows in [`.github/workflows/`](.github/workflows/)

The places most worth a hard look are the ones where the framework touches the
outside world:

- **The installer**
  - It picks a game folder, downloads the official BepInEx 5.4.23.5 release,
    checks it against a SHA-256 pinned in the source before unpacking it, and
    reads a mod's `mod-install.json`. If that file pins a ModFramework release
    (schema 2) and the game folder doesn't have it, the installer downloads
    that release's zip from an address it builds from the version alone, and
    only uses it when the size and SHA-256 match the file.
  - A real bug here would be a path that gets out of the folder it should stay
    in, or a way to make it accept a different archive or download from
    another address.
- **Update notices**
  - They ask `api.github.com` once a day for the latest release of a mod that
    names its repository. They send nothing about the player and download
    nothing. What they find is written to `BepInEx/cache` for the launcher.
  - A real bug would be anything that turns that into a download inside the
    game, or that leaks information about the player.
  - There's more in [Going online (wiki)](https://github.com/TomXV/dragnwash-modframework/wiki/Going-online).
- **The launcher**
  - Steam starts it before the game through the launch option the installer
    sets (it only edits that one value in Steam's `localconfig.vdf`). It reads
    what the game's update check wrote and goes online only when the player
    presses Update, and only to github.com and release-assets.githubusercontent.com.
    It downloads a mod's zip from an address it builds from the repository and
    tag, checks its size and SHA-256 against GitHub's digest, and installs it
    with the installer's own code. Its page is built into the exe and loads
    nothing from outside; release notes are shown as text.
  - A real bug would be a way to make it download from another address, accept
    a file that doesn't match, run script from release notes, write outside the
    game folder, or change other launch options or Steam settings.
- **Asset and mod loading**
  - `GameAssets` and the Overrides library read files out of a mod's folder.
  - A real bug would be a crafted file that reaches outside that folder, or one
    that gets the framework to load code the player didn't install.
- **The crash reporter**
  - It writes a report file and shows it.
  - The rule is that a report should never contain more of the player's machine
    than the paths it needs.

## What is not covered

- **A mod can do anything the game can do.** Mods are plain .NET assemblies that
  BepInEx loads into the game's process. The framework gives them a tidy API,
  but it doesn't sandbox them and it can't take away anything they're able to
  do. "A malicious mod could read my files" is just how modding works
  everywhere, and it isn't a flaw in the framework. Only install mods you trust.
- **The game itself.** Bugs in Drag'n Wash are for its developer and don't
  belong here. Please don't send them a crash that only happens with mods
  installed, either.
- **BepInEx.** Report those to [the BepInEx project](https://github.com/BepInEx/BepInEx).
- **Antivirus false positives on `Install.exe`.** Unsigned installers get
  flagged. That's a quirk of detection and isn't a vulnerability. The README of
  the mod you installed says what to do about it, and the installer's source is
  right here. A regular issue is fine for these.
- **Copies from anywhere other than [Releases](https://github.com/TomXV/dragnwash-modframework/releases).**
  Each release lists the zip's SHA-256. If a file you got somewhere else does
  something nasty, that comes from the file and has nothing to do with this
  project. We'd still like a report telling us where you found it, though.
