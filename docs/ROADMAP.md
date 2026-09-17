# Roadmap

[日本語](ROADMAP.ja.md)

Where Drag'n Wash ModFramework is going. Plans change; dates are given only when they are close. Updated 2026-09-17.

The yardsticks stay the same: every mod runs safely together, and each part stays small enough that someone else can carry it on.

## Released: 1.2.0 (2026-09-17)

Released together with Drag'n Wash Localization v1.2.0.

- Core 1.2.0: the developer-tools switch, text and key settings on the Mods screen, `GameEvents`, `SettingMeta`, reloading a mod while the game runs, and mods that go online say so ([NETWORK.md](NETWORK.md)).
- Tool window 1.1.0 (Console), Assets 1.1.0 (texture replacements, previews), Dialogue 1.1.0 (stable line keys).
- Inspector 1.0.0, a new library that stays experimental.
- A new logo by NotaGames and a drawn Mods button by Mister ERIO.

## Planned

- **A notice per mod on the Mods screen.** Today the screen can say a feature is unavailable or that a mod patches the same code as another. Some things fit neither, such as two mods shipping different translations for the same line ([Localization #28](https://github.com/TomXV/dragnwash-localization/issues/28)). A small, general way for a mod or a library to leave a note under a mod.
- **Translations shipped by other mods.** Drag'n Wash Localization will first load `<mod folder>/Translations/` on its own, as an experimental beta feature off by default ([design](https://github.com/TomXV/dragnwash-localization/blob/main/docs/MOD_TRANSLATIONS.md)). If a second translation mod wants the same convention, finding those folders moves into the Text library.
- **Direct3D 12.** Opening the F1 window or reloading textures can, rarely, crash the game on Direct3D 12 (Unity UUM-140564). Find what triggers it, or keep every upload at startup.
- **Export and import for every kind.** Textures, meshes, materials, sounds and the game's data (ScriptableObjects) written to files, changed, and brought back by a mod ([design](ASSET_TOOL.md#export-and-import)).
- **Steam Deck:** typing in the F1 window with the on-screen keyboard.

## Later, if there is a need

- Replacing meshes and shaders, not only textures (Assets).
- More of the game's events in `GameEvents`, when a mod needs them. There will still be no per-frame event.
- Connection watching for more ways to go online, if mods start using them.

## Staying as they are

- **Inspector** stays experimental after release. It is a tool for people who make mods, and mods may leave it out of their releases.
- **Connection watching only watches.** It will not block connections and is not a security boundary; it makes what mods do visible.

## Not planned

- A scripting language in the Console.
- Machine translation.
- Shipping the game's files unchanged ([CONTENT_POLICY.md](CONTENT_POLICY.md)).
- A second mod loader, or taking over what BepInEx does.

## Waiting on others

- **macOS:** BepInEx's Doorstop cannot hook Unity 6.3 yet ([UnityDoorstop#108](https://github.com/NeighTools/UnityDoorstop/issues/108)).
