# Design memo

[日本語](DESIGN.ja.md)

Status: September 2026. The newest release is 1.6.0, and from 1.5.0 on the core and every library share that number. Parts marked planned or future are not done yet. Open an issue to discuss any part of it.

The features this memo once listed as designs are built: [Mod reload (wiki)](https://github.com/TomXV/dragnwash-modframework/wiki/Mod-reload), the [Inspector (wiki)](https://github.com/TomXV/dragnwash-modframework/wiki/Inspector), the [Console (wiki)](https://github.com/TomXV/dragnwash-modframework/wiki/Console), [Assets (wiki)](https://github.com/TomXV/dragnwash-modframework/wiki/Assets) and [Dialogue (wiki)](https://github.com/TomXV/dragnwash-modframework/wiki/Dialogue) all shipped in 1.2.0 or earlier, and their wiki pages describe them as they are now. What is designed but not built is in [ROADMAP.md](ROADMAP.md).

## Why a framework

[Drag'n Wash Localization](https://github.com/TomXV/dragnwash-localization) had to build a lot of machinery to hook into the game: a setting in the game's Options screen, an in-game menu that works with a gamepad and on the Steam Deck, a hook on every text component, the Yarn line that is about to be shown, fonts that do not crash Direct3D 12, save snapshots. Most of that is not specific to translation. Any other mod would have to build it again, and every one of them would break separately when the game updates.

The game update of September 14, 2026 (build `9/12/2026_a93aa21a`) showed how that goes: typo fixes in 25 lines, two new options, a new pause button and a new save format, all in one patch.

The framework puts the code that touches the game in one place and gives mods a stable API instead.

## First principle: every mod runs safely together

In most modding scenes every author designs things their own way. Two mods patch the same game method differently, one mod's error takes another down, load order decides who wins, and the result is conflicts and crashes that players cannot trace. The framework exists to prevent that, and every API is judged by it.

- **One shared hook instead of many patches.** When several mods need the same place in the game (text, dialogue, the Options screen), the framework or a library patches it once and mods register into it, in an explicit order.
- **Isolation.** Every mod callback runs inside the framework's error handling: an exception is logged with the mod's name and the others keep running. One broken mod never stops the game or another mod.
- **Check before patching.** Patch targets are checked with `GameHooks` first; a missing target turns the feature off and says so, instead of crashing after a game update.
- **Conflicts are visible.** The Mods screen shows what it can detect: missing or too old libraries, `BepInIncompatibility`, a plugin that did not load and why, and game methods that several mods patch directly.
- **Nothing silently replaced.** A service can have only one provider; a second registration is refused and logged.
- **Guidelines for authors.** [Playing well with others (wiki)](https://github.com/TomXV/dragnwash-modframework/wiki/Playing-well-with-others) says what to use from the framework, what not to patch directly, and how to write a library others can build on.

## Goals

- **A Mods screen inside the game.** Players see and configure every installed mod from the game's own menus, like Minecraft Forge's mod list. See [The Mods screen](#the-mods-screen).
- **One place absorbs game updates.** Mods use the framework's types, never the game's classes directly for the features the framework covers. When the game changes, the framework follows and the mods keep working.
- **Fail soft.** If a patch target disappears after an update, that feature reports itself unavailable and logs why. The game and the other features keep running.
- **Safe on every platform the game runs on.** Windows (Direct3D 12 and 11), Windows on ARM, Steam Deck / Linux. Knowledge such as the Direct3D 12 upload crash (UUM-140564) lives in the framework, not in each mod.
- **No game files as they are.** Like the localization mod, the repository and releases never contain the game's assets, script or binaries unchanged. Material made by hand or changed follows [CONTENT_POLICY.md](CONTENT_POLICY.md).

## Non-goals (for now)

- Loading new 3D content (custom dragons, models). Possible later, but it depends on how the developers feel about it.
- Replacing BepInEx or Harmony. The framework is a BepInEx 5 plugin and uses Harmony internally.
- Full macOS support, until a released BepInEx can load on Unity 6.3 there (NeighTools/UnityDoorstop#108). Until then, macOS runs only through Drag'n Wash Localization's experimental install script (#85). The framework's own parts that need care on macOS, such as the Tool window's font, are fixed as they are found.

## Packaging and versioning

- BepInEx 5 plugin, GUID `com.tomxv.dragnwash.modframework`, assembly `DragNWash.ModFramework.dll`, namespace `DragNWash.ModFramework`.
- A mod depends on it with `[BepInDependency(ModFramework.Guid, BepInDependency.DependencyFlags.HardDependency)]`, optionally with a minimum version.
- Semantic versioning. **0.x** while the API takes shape: minor versions may break the API, and the changelog says how. **1.0** once the localization mod runs on it and the API has settled; after that, breaking changes only in a new major version.
- Public API: everything `public` in the `DragNWash.ModFramework` namespace. Anything touching game types stays `internal`.

## The Mods screen

The centre of the framework for players: a screen listing every installed mod, in the spirit of Minecraft Forge's mod list, reached from the game's own Options screen so it feels like part of the game.

### What players see

- A **Mods** button in the Options screen, in the column of buttons with Back, Reset to defaults and Save. Options is reachable from both the title screen and the pause menu, so the Mods screen is too, and the title screen and pause menu stay exactly as the game made them.
- The Mods screen: the list of mods on the left, details of the selected mod on the right.
  - Icon, name, version, authors, description, website, and which framework version it needs.
  - **Settings** for that mod, shown as rows in the same style as the game's Options screen.
  - A notice when a mod failed to load or a feature is unavailable on this game build.
  - An **on/off switch** for each mod, applied from the next launch (see below).
- **Back** (button, Esc or the pad's cancel) returns to the Options screen.
- Works with mouse, gamepad and on the Steam Deck, like the rest of the game's menus.

Every BepInEx plugin appears in the list, even one that knows nothing about the framework (GUID, name and version come from BepInEx). A mod built on the framework can add the rest: description, authors, website, icon and settings.

### How it fits into the game

The game's menus are simple enough to extend without touching its files (checked against build `9/12/2026_a93aa21a`):

- Each screen is a `Menu` component registered with `MenuManager` by its GameObject name: `Menu_Main` (the title screen, or the pause menu inside a level), `Menu_Options`, `Menu_SlotsLoad` and so on.
- `Menu_Options` is laid out as `Container/Panel` with `TitleImage`, a `Scroll View` whose rows the settings library generates, and `LeftButtons` holding `Back`, `ResetToDefaults` and `Save`. The same layout is used on the title screen and in levels.
- A button raises an intent named after its GameObject (`MenuWithButtons`), and the current menu answers with a transition to another menu by name, such as `new MenuResponseTransition("Menu_Options", ...)`.
- So the framework can:
  1. clone a button in `Menu_Options/Container/Panel/LeftButtons`, rename it `Mods` and give it its own label,
  2. patch `MenuOptions.OnEvent` so the `Mods` intent transitions to `Menu_Mods`,
  3. create `Menu_Mods` as a `Menu` subclass built from the Options screen's layout, register it with `MenuManager`, and send `Back` to `Menu_Options`.
- Checked in the game: settings changed on the Options screen and not saved yet are still there, with the Save button, after a visit to the Mods screen.
- The game's buttons have their words painted into the artwork. The Mods button and everything on the Mods screen are TextMeshPro text in the game's own TMP font instead, so no artwork is drawn or copied and every label can be translated (the localization mod's text hook picks them up like any other UI text).
- Mod icons (`ModInfo.Icon`, or a file in `ModInfo.IconPath`) are loaded when the mod registers, at startup, which is safe on Direct3D 12.

### Turning mods on and off

A mod cannot be unloaded from a running game, so the switch takes effect from the next launch, and the screen says so.

- The framework ships a small BepInEx preloader patcher (`BepInEx/patchers/DragNWash.ModFramework.Preloader.dll`). Patchers run before any plugin assembly is loaded, so the files are not in use yet.
- On launch the patcher reads the framework's list of disabled mods and renames their DLLs to `.dll.disabled` (and back when a mod is switched on again). BepInEx then simply does not see a disabled mod, and a player can undo it by hand by renaming the file.
- The framework itself cannot be switched off from its own screen.
- Switching off a mod that other mods depend on (`BepInDependency`) lists those mods and asks before doing it.

### Mods that do not use the framework

The Mods screen works for every BepInEx mod, not only mods built on the framework. Nothing is required of a mod; the API only adds to what can be found out anyway.

- **Name, version and dependencies** come from the plugin's `[BepInPlugin]`, `[BepInDependency]` and `[BepInIncompatibility]` attributes.
- **Description and authors** come from the DLL's `AssemblyDescription` and `AssemblyCompany` attributes, and from a Thunderstore `manifest.json` in the mod's folder (description, website).
- DLLs are read with Mono.Cecil, which BepInEx itself ships, so no mod code runs and nothing extra is loaded into the game.
- **Plugins that did not load** are listed too, with the reason when it can be told: a dependency that is not installed, or a mod it cannot run together with. Otherwise the screen points to `BepInEx/LogOutput.log`.
- **Preloader patchers** in `BepInEx/patchers` are listed but cannot be switched off from the screen.
- Anything registered with `ModFramework.Register(ModInfo)` takes priority over what was read from the files.

### For mod authors

```csharp
ModFramework.Register(new ModInfo
{
    Guid = MyPlugin.Guid,
    Description = "Adds ...",
    Authors = new[] { "Me" },
    Website = "https://github.com/me/mymod",
});
```

Settings shown on the Mods screen come from the mod's BepInEx config entries (`ConfigEntry<bool>` becomes a switch, a number with a range gets a slider with - and +, other numbers get - and + and a box to type in, enums and lists of accepted values get a button for each choice, or - and + when there are more than four or they don't fit, text and shortcuts get a box to type in, a shortcut also gets a Change button that takes the next key you press, and anything else is shown with a note to edit the config file), so a mod gets a settings page without writing UI. The Settings API can add rows to the game's own Options screen as well.

### Update notices

Players should not have to visit every mod's page to find out that it was updated. From core 1.1.0:

- A mod opts in with `ModInfo.UpdateRepository = "owner/name"`. The repository is never guessed from `Website`: a guess could compare against releases that do not follow the mod's version numbers.
- Once a day per repository, the framework reads `https://api.github.com/repos/<owner>/<name>/releases/latest` with `UnityWebRequest` (part of the game, so nothing extra ships). GitHub never returns a draft or a pre-release there. The tag, with or without a leading `v`, is compared with the installed plugin's version; a tag that is not a version number is ignored.
- Results are kept in `BepInEx/config/com.tomxv.dragnwash.modframework.updates.txt`, so restarting does not ask again. A failed request is logged once and tried again an hour later; it never shows an error to the player.
- The Mods screen tags the mod **Update**, shows the new version and has a button that opens `https://github.com/<owner>/<name>/releases/tag/<tag>`. The URL is built from the checked repository name, never taken from the answer. The title screen adds "N updates available in Mods".
- Nothing is downloaded or replaced. On by default, with `[Updates] Check for updates` in the framework's settings to switch it off. The README says what is sent.
- Later, if it proves needed: download the release, check it, and let the preloader patcher swap the files at the next launch, when they are not in use. That needs integrity checks designed first.

## Layers: a small core, and libraries on top

The framework does not try to hold every API. It is a small core that other prerequisite mods, libraries, build on, the way the framework itself builds on BepInEx:

```
Game + BepInEx
  └ Drag'n Wash ModFramework (core)      Mods screen, settings, Options rows, game info
      ├ a text library                   text event, re-apply on language change
      ├ a dialogue library               line and option events, speakers, flags
      ├ a tool window library            the shared F1 window
      └ ... any library someone needs
          └ mods that use them            e.g. Drag'n Wash Localization
```

- **Core stays small.** Only what nearly every mod needs, or what must exist once for the whole game, goes into the core: the Mods screen, on/off switching, settings pages, Options rows, game information and the rules for following game updates.
- **Everything else is a library.** A library is an ordinary BepInEx plugin that depends on the framework (`BepInDependency`) and is depended on by mods. It can live in this repository as a separate project, or be written by anyone in their own repository.
- **Libraries are first-class on the Mods screen.** A library says so in its `ModInfo` (`IsLibrary`), and the screen shows it as a library, lists the mods that need it, and asks before switching it off.
- **Libraries extend the core through extension points** rather than by patching it:
  - a service registry, so a library can offer an interface and a mod can ask for it (`ModFramework.Services.Register<T>(implementation)`, `Get<T>()`), with a version on each service;
  - extra pages on a mod's entry in the Mods screen, next to the generated settings page;
  - health checks: a library tells the core which game methods it patches, the core checks them at startup and shows a library as unavailable on a game build where they are gone.
- **Versions are separate.** The core and each library have their own version numbers; a mod depends on the core and on the libraries it uses, each with a minimum version.

## API candidates

Most areas come from working code in the localization mod (file names refer to `src/DragNWashLocalization/` there).

**Core**

| Area | What mods get | Comes from |
|---|---|---|
| Mods screen | A Mods button in the Options screen, the mod list and details, on/off switches applied at the next launch, settings pages generated from BepInEx config, `ModFramework.Register(ModInfo)` | new; uses the menu knowledge from `OptionsLanguage.cs` |
| Settings | Rows in the game's Options screen (`GameOptions`) that preview on change, save with the game's Save button and revert with Back | `OptionsLanguage.cs` |
| Game info | Unity version, graphics API, platform, game build, "is this build known to work" | `Plugin.cs` startup checks |
| Extension points | Service registry, extra pages on the Mods screen, health checks for patched game methods, `ModInfo.IsLibrary` | new |

**Libraries** (separate plugins on top of the core)

| Library | What mods get | Comes from |
|---|---|---|
| Tool window | A shared developer window (F1 by default) for debug tools, where each mod registers a tab; cursor unlock, input blocking behind the window, gamepad and Steam Deck trackpad clicks, a CJK-capable menu font | `Plugin.ImGui.cs`, `CursorUnlock.cs`, `InputBlocker.cs`, `VirtualClick.cs`, `MenuFontBundle.cs` |
| Text | An event before a TMP text is shown, with the source text and the component, where a mod can replace it; re-apply on demand (e.g. after a language switch) | `TmpTextPatches.cs` |
| Dialogue | Events for a line about to be shown and options about to be offered, with line ID, speaker and node; the loaded Yarn project | `LineIdContext.cs`, `SpeakerLookup.cs`, `DialogueDumper.cs` |
| Flags and saves | Read game flags; snapshots of save slots before a mod changes anything | `FlagCatalog.cs`, `SaveHistory.cs` |
| Assets | Load fonts, textures and asset bundles at a safe moment (at startup on Direct3D 12) | `FontFallback.cs`, `MenuFontBundle.cs` |

The installer is not an API; it installs BepInEx, the core and the libraries a mod needs.

## Order of work

1. **0.1 Skeleton.** Plugin, `ModFramework`, `GameInfo`, build and repository rules. (done)
2. **0.2 Mods screen.** (done) The Mods button in the Options screen, the list and details of installed mods, `ModInfo`, and switching mods on and off with the preloader patcher.
3. **0.3 Settings.** (done) Settings pages on the Mods screen generated from BepInEx config, and `GameOptions` for rows in the game's Options screen.
4. **0.4 Extension points.** (done) Service registry, `ModInfo.IsLibrary` and library display on the Mods screen, extra Mods screen pages, health checks.
5. **Libraries**, one at a time and each in the order the localization mod needs them: text, dialogue, tool window, assets, flags and saves. Each is its own plugin with its own version. (done: all five are at 0.1; the text library is at 0.1.1)
6. **1.0 of the core** when Drag'n Wash Localization v1.0.0 runs on the core and the libraries it uses. (done: released as 1.0.0 together with Drag'n Wash Localization v1.0.0, checked on Windows and on the Steam Deck)

Each step moves one feature out of the localization mod, and the localization mod switches to it before the next step starts. Every step is tested in the game on Windows and on the Steam Deck.

## Following game updates

- [GAME_BUILDS.md](GAME_BUILDS.md) lists the game builds the framework was checked against.
- For each patch target, check it exists at startup and log a clear line when it does not.
- After an update: decompile and diff the game assembly, diff the Yarn string table, update the table, release.

## Future: Steam Workshop

Drag'n Wash has no Steam Workshop today; only the developers can enable it for the game. If they do, the framework could take mods from it, so players subscribe on Steam instead of copying files.

What is already in place: the game ships Steamworks.NET (`com.rlabrecque.steamworks.net.dll` and `steam_api64.dll`), so the framework can talk to Steam without adding anything.

How it could work:

- **Finding subscribed mods.** Steam downloads subscribed items to `steamapps/workshop/content/4739660/<item id>/`. The framework asks Steam (`ISteamUGC`) for the subscribed items and their folders.
- **Loading them.** BepInEx only loads plugins from `BepInEx/plugins`. The preloader patcher, which already runs before plugins, would link or copy each item's plugin DLLs into `BepInEx/plugins/Workshop/<item id>/` before BepInEx scans the folder, and remove them when the item is unsubscribed.
- **On the Mods screen.** Workshop mods are listed with where they came from, their Workshop page, and the same on/off switch; switching off keeps the subscription.
- **Dependencies.** A Workshop item can list required items; the Mods screen can say which libraries are missing and link to them.
- **Uploading.** Uploading needs the game's app ID and Workshop enabled, so it would be a small tool or a page on the Mods screen for mod authors.

Things to settle first: whether the developers want code mods on their Workshop at all, the game's adult content rating for Workshop items, and warning players that a mod runs code on their computer.

## Open questions

- How mods show up in the tool window when several register tabs (order, naming).
- The installer: done. One shared installer lives here and every mod's zip ships it, reading the mod's `mod-install.json` ([Installer (wiki)](https://github.com/TomXV/dragnwash-modframework/wiki/Installer)). Mods can also be uninstalled from the Mods screen.
- Distribution beyond GitHub Releases.
- The developers' view on mods, which matters more for a framework than for a translation.
