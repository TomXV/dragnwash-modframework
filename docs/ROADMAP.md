# Roadmap

[日本語](ROADMAP.ja.md)

Where Drag'n Wash ModFramework is going. Plans change; dates are given only when they are close. Updated 2026-09-19.

The yardsticks stay the same: every mod runs safely together, and each part stays small enough that someone else can carry it on.

## Released: 1.3.0 (2026-09-19)

Released together with Drag'n Wash Localization v1.3.0.

- Crash reports: a record of each session, a report after a crash or a freeze with Unity's crash dump or a freeze dump, and a window outside the game that says what happened (`CrashReporter.exe`, Windows) ([CRASH_REPORTS.md](CRASH_REPORTS.md)).
- The Direct3D 12 crash when the Tool window opened, found with them: font atlas uploads are batched to once per frame, and the Console shows translated text again.
- `GameOptions.AddSlider` ([#39](https://github.com/TomXV/dragnwash-modframework/issues/39)).

## Released: 1.2.1 (2026-09-19)

- Flags and saves 1.0.1: the Saves tab finds saves made after the game update of 2026-09-14 again, and lists slots in order ([#40](https://github.com/TomXV/dragnwash-modframework/issues/40)).

## Released: 1.2.0 (2026-09-17)

Released together with Drag'n Wash Localization v1.2.0.

- Core 1.2.0: the developer-tools switch, text and key settings on the Mods screen, `GameEvents`, `SettingMeta`, reloading a mod while the game runs, and mods that go online say so ([NETWORK.md](NETWORK.md)).
- Tool window 1.1.0 (Console), Assets 1.1.0 (texture replacements, previews), Dialogue 1.1.0 (stable line keys).
- Inspector 1.0.0, a new library that stays experimental.
- A new logo by NotaGames and a drawn Mods button by Mister ERIO.

## Planned

- **A notice per mod on the Mods screen.** Today the screen can say a feature is unavailable or that a mod patches the same code as another. Some things fit neither, such as two mods shipping different translations for the same line ([Localization #28](https://github.com/TomXV/dragnwash-localization/issues/28)). A small, general way for a mod or a library to leave a note under a mod.
- **Translations shipped by other mods.** Drag'n Wash Localization will first load `<mod folder>/Translations/` on its own, as an experimental beta feature off by default ([design](https://github.com/TomXV/dragnwash-localization/blob/main/docs/MOD_TRANSLATIONS.md)). If a second translation mod wants the same convention, finding those folders moves into the Text library.
- **Direct3D 12.** 1.3.0 removed the most common trigger of the crash (Unity UUM-140564): a burst of font atlas uploads. Reloading textures while the game runs still uploads at once; the crash reports' GPU trace says whether it needs the same treatment.
- **Texture replacements per language.** Replacements that apply only while a given language is in use, can be taken back, and wait for a restart on Direct3D 12. For Drag'n Wash Localization's translated pictures ([design](https://github.com/TomXV/dragnwash-localization/blob/experimental/translated-textures/docs/TRANSLATED_TEXTURES.md)).
- **Export and import for every kind.** Textures, meshes, materials, sounds and the game's data (ScriptableObjects) written to files, changed, and brought back by a mod ([design](ASSET_TOOL.md#export-and-import)).
- **Overrides: edits made in the Inspector, shipped as a mod.** The History exported as a file, and a small Overrides library that applies it for players, so a mod can be made with no code: a folder with `mod.json` and `overrides/*.json`, shown and switched off on the Mods screen like any mod, taken back cleanly. Research first ([design](OVERRIDES.md)).
- **Operations, MCP and node graphs.** Each library registers what it can do as named operations; from that one registry come Console commands, MCP tools for AI clients (read-only first, this computer only, off by default) and blocks for node graphs built outside the game and shipped as data-only mods. In four stages: the registry, MCP, code seen as nodes, building ([plan](API_PLAN.md)).
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

- **macOS:** BepInEx's Doorstop cannot hook Unity 6.3 yet ([UnityDoorstop#108](https://github.com/NeighTools/UnityDoorstop/issues/108)).
