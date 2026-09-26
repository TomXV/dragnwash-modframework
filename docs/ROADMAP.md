# Roadmap

[日本語](ROADMAP.ja.md)

Where Drag'n Wash ModFramework is going. Plans change; dates are given only when they are close. Updated 2026-09-26.

The yardsticks stay the same: every mod runs safely together, and each part stays small enough that someone else can carry it on.

## Released: 1.6.0 (2026-09-26)

The core, the preloader patcher and every library go to 1.6.0. See [CHANGELOG.md](../CHANGELOG.md) for the whole list.

- **A launcher before the game.** `Launcher.exe` runs from Steam's launch options: when the game's update check found newer versions, it shows them before the game starts and installs the ones you pick, checked by size and SHA-256 and backed up first. It goes online only when you press Update.
- **Update from the Mods screen.** A mod the installer put in gets **Update now**: the game quits, the launcher updates it and starts the game again.
- **The installer, rebuilt.** Install.exe looks like the launcher, sets the Steam launch option for you, and shows Uninstall's real progress. Without WebView2 the old window opens as before.
- **macOS.** The F1 window draws its text on macOS (thanks to 223n). The game itself runs with mods on macOS only through Drag'n Wash Localization's experimental install script for now ([#85](https://github.com/TomXV/dragnwash-modframework/issues/85)).

## Released: 1.5.0 (2026-09-23)

The core, the preloader patcher and every library go to 1.5.0; from here on they share one number. See [CHANGELOG.md](../CHANGELOG.md) for the whole list.

- **A whole new look.** The Mods screen looks like a settings app on frosted glass (a blurred copy of the game behind each panel), with search, filters, tabs for a mod's pages and a row per setting. Every tab of the F1 window was gone through: notices, questions right where you pressed, a window that opens where you left it, one row of tabs.
- **Faster and safer.** Fonts the fallback drew are kept between starts, the F1 window's font is made when it first opens, and a restore or two snapshots in the same second no longer lose a save snapshot.
- **The installer fetches ModFramework** from its own release, checked by size and SHA-256, and stops before changing anything when another mod loader is in the game folder.

## Released: 1.4.3 (2026-09-20)

- **A way into the editor.** The Bridge tab has **Open page** (the code graph) and **Graphs** (the editor), and its buttons wrap in a narrow window. Until now the page could only be opened from a method in the Inspector's Code view.

## Released: 1.4.2 (2026-09-20)

- **Which mod has which key.** A graph answering `key.pressed` names the other mods with a setting on that key, in the log, the console, the Mods screen and the page. Nobody owns a key, so nothing is refused; it is said, once, where a person will see it (`ModFramework.WhoElseUses`).

## Released: 1.4.1 (2026-09-20)

- **Graphs 0.1.0: mods with no code that do things** (stage 4 of the [plan](API_PLAN.md), [design](GRAPHS.md)). A folder with a `mod.json` and `graphs/*.json` answers the events the libraries raise and calls the operations they registered; every file is checked before anything runs, all the graphs share a millisecond a frame, and a graph that fails three times in a row is switched off with its changes put back. The first writes are `objects.member.set`, `objects.material.set` and `objects.active.set` in the Overrides library, and the editor - blocks to drag, and the same graph drawn as nodes - is the Graphs tab of the Bridge's page.
- **Who changed what.** A change made through a write operation is listed in the Inspector's History with who asked for it and can be put back from there; two mods changing one value are named in the log and on the mod's Graphs page; and a take-back puts back only what it wrote itself. Three of these were promised by 1.4.0's notes and were not there.

## Released: 1.4.0 (2026-09-20)

Released together with Drag'n Wash Localization v1.4.0. The core and the preloader patcher go to 1.4.0; the Tool window and Dialogue to 1.2.0; Text, Flags and saves and the Inspector to 1.1.0; Assets to 1.2.0. Everything below is marked experimental; see [CHANGELOG.md](../CHANGELOG.md) for the whole list.

- **Overrides 0.1.0: a mod with no code.** A folder with `mod.json` and `overrides/*.json` changes a component's field or a material's property, is listed and switched off on the Mods screen like any other mod, and is taken back cleanly; when two mods change the same thing the log names both. The Inspector's History exports the edits made there as one of these mods ([Overrides (wiki)](https://github.com/TomXV/dragnwash-modframework/wiki/Overrides)).
- **Operations, and what came out of them** (stages 1 to 3 of the [plan](API_PLAN.md)). Every library registers what it can do as a named operation (`library.noun.verb`), checked arguments and plain results; from that one registry come the console's `op`, **Bridge 0.1.0** (the read operations for AI clients over MCP, this computer only, off by default, with a token and a door that refuses web pages) and the **code graph** (the game's code as blocks and branches, on a page and in a Windows app that needs no game). Stage 4, building with blocks and nodes, is the **Graphs** library in 1.4.1 ([GRAPHS.md](GRAPHS.md)).
- **An object explorer in the Inspector.** Every loaded object by kind (textures, materials, meshes, shaders, sounds, animations, fonts, the game's ScriptableObject data), like Unity's Project window, with **Used by** to find where each is used, and the arrow keys - or, on the Steam Deck and any gamepad, the d-pad - to walk the list ([design](OBJECT_EXPLORER.md)).
- **Texture replacements per language** (Assets 1.2.0): replacements that apply only while a given language is in use, fall back to the languages a `fallback.txt` names, can be taken back, and wait for a restart on Direct3D 12. For Drag'n Wash Localization's translated pictures, which are built on it ([design](https://github.com/TomXV/dragnwash-localization/blob/main/docs/TRANSLATED_TEXTURES.md)).
- Animators and Rigidbodies in the Inspector, scenes and levels, crash-report follow-ups, and the Tool window's Steam Deck input (a trackpad click is a real mouse button, the d-pad real arrow keys).

## Released: 1.3.0 (2026-09-19)

Released together with Drag'n Wash Localization v1.3.0.

- Crash reports: a record of each session, a report after a crash or a freeze with Unity's crash dump or a freeze dump, and a window outside the game that says what happened (`CrashReporter.exe`, Windows) ([Crash reports (wiki)](https://github.com/TomXV/dragnwash-modframework/wiki/Crash-reports)).
- The Direct3D 12 crash when the Tool window opened, found with them: font atlas uploads are batched to once per frame, and the Console shows translated text again.
- `GameOptions.AddSlider` ([#39](https://github.com/TomXV/dragnwash-modframework/issues/39)).

## Released: 1.2.1 (2026-09-19)

- Flags and saves 1.0.1: the Saves tab finds saves made after the game update of 2026-09-14 again, and lists slots in order ([#40](https://github.com/TomXV/dragnwash-modframework/issues/40)).

## Released: 1.2.0 (2026-09-17)

Released together with Drag'n Wash Localization v1.2.0.

- Core 1.2.0: the developer-tools switch, text and key settings on the Mods screen, `GameEvents`, `SettingMeta`, reloading a mod while the game runs, and mods that go online say so ([Going online (wiki)](https://github.com/TomXV/dragnwash-modframework/wiki/Going-online)).
- Tool window 1.1.0 (Console), Assets 1.1.0 (texture replacements, previews), Dialogue 1.1.0 (stable line keys).
- Inspector 1.0.0, a new library that stays experimental.
- A new logo by NotaGames and a drawn Mods button by Mister ERIO.

## Built, not released

- **The code graph without the game** (`codegraph-standalone/`, [design](CODE_GRAPH_STANDALONE.md)): the same page over any .NET assembly, with no port and no network. A tool for this repository's own work, not part of a release.

## Planned

- **A notice per mod on the Mods screen.** Today the screen can say a feature is unavailable or that a mod patches the same code as another. Some things fit neither, such as two mods shipping different translations for the same line ([Localization #28](https://github.com/TomXV/dragnwash-localization/issues/28)). A small, general way for a mod or a library to leave a note under a mod.
- **Translations shipped by other mods.** Drag'n Wash Localization now loads `<mod folder>/Translations/` on its own, as an experimental beta feature off by default ([design](https://github.com/TomXV/dragnwash-localization/blob/main/docs/MOD_TRANSLATIONS.md)). If a second translation mod wants the same convention, finding those folders moves into the Text library.
- **Direct3D 12.** 1.3.0 removed the most common trigger of the crash (Unity UUM-140564): a burst of font atlas uploads. Reloading textures while the game runs still uploads at once; the crash reports' GPU trace says whether it needs the same treatment.
- **Export and import for every kind.** Textures, meshes, materials, sounds and the game's data (ScriptableObjects) written to files, changed, and brought back by a mod ([design](https://github.com/TomXV/dragnwash-modframework/wiki/Assets)).
- **Steam Deck:** typing in the F1 window with the on-screen keyboard.

## Later, if there is a need

- Replacing meshes and shaders, not only textures (Assets).
- More of the game's events in `GameEvents`, when a mod needs them. There will still be no per-frame event.
- Connection watching for more ways to go online, if mods start using them.

## Staying as they are

- **Inspector** stays experimental after release. It is a tool for people who make mods, and mods may leave it out of their releases.
- **Connection watching only watches.** It will not block connections and is not a security boundary; it makes what mods do visible.

## Not planned

- A free scripting language (arbitrary methods, reflection). Node graphs that call only registered operations are planned instead ([plan](API_PLAN.md)).
- Machine translation.
- Shipping the game's files unchanged ([CONTENT_POLICY.md](CONTENT_POLICY.md)).
- A second mod loader, or taking over what BepInEx does.

## Waiting on others

- **macOS:** the Doorstop in BepInEx's current release can't hook Unity 6.3 ([UnityDoorstop#108](https://github.com/NeighTools/UnityDoorstop/issues/108)). The fix is merged but not released yet, so for now mods run on macOS only through Drag'n Wash Localization's experimental install script, with that unreleased build and under Rosetta. Two releases are still to come: a stable UnityDoorstop 4.6.0, and a BepInEx 5 with the native arm64 fix ([BepInEx#1402](https://github.com/BepInEx/BepInEx/pull/1402)), which would run the game without Rosetta. Progress: [#85](https://github.com/TomXV/dragnwash-modframework/issues/85).
