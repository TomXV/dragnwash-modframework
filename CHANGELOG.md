# Changelog

Versions of the core and of each library are separate, and follow semantic versioning: from 1.0.0 on, a change that breaks the public API comes only with a new major version.

## Unreleased

### Inspector, next version

- The wireframe is drawn whole for detailed meshes (parts of a dragon were missing): each renderer gets a mesh of its edges, built once (each edge once) and drawn with the camera's matrices. Before, every line went through GL immediate mode each frame, which dropped vertices past about 65,000 and, sent whole, uploaded megabytes a frame and crashed Direct3D 12 (UUM-140564); now only a skinned mesh's positions go up each frame.
- A deep tree (a rig's bones) no longer pushes names out of the tree pane: its levels get narrower, and when even that is not enough, the levels above every row in view are left out.
- The debug view's names keep clear of the selection's name and of each other (moved above, or below the outline when there is no room), instead of being drawn over them.
- Experimental. Rigidbodies and Rigidbody2Ds, read by reflection (no physics module is referenced): a debug view with each body's centre of mass and a velocity arrow, tagged with its speed, mass, kind and sleep (View → Rigidbodies: centre of mass and velocity); a **Rigidbodies list**, scene-wide or under the selection, fastest first, with a filter and Awake only; buttons on a selected body: **Stop**, **Kinematic** (kept in History), **Sleep** / **Wake**; and **Pause physics** with **Step** (simulation mode set to Script, put back on Resume, when the window closes or when developer tools go off). Console: `bodies`, `bodies pause|resume|step [count]`. See docs/INSPECTOR.md, Rigidbodies.

### Assets 1.2.0

- Experimental. Texture replacements per language: `AssetReplacements.AddLanguageFolder(guid, root, subfolder)` takes `<root>/<language>/<subfolder>/*.png`, which apply only while `GameFonts.Language` is that language and win over a plain replacement of the same texture (both are named in the log). Only the language in use is loaded. A language change takes the previous pictures back and loads the new ones; on Direct3D 12 the new ones wait for a restart (`AssetReplacements.PendingLanguage`). `SetLanguageFoldersEnabled(guid, on)` switches a mod's pictures off and on; `AssetReplacements.Changed` is raised afterwards. `TextureReplacement.Language` names the language, and the Assets tab shows it. For Drag'n Wash Localization's translated pictures.
- Replacements can be taken back: the library remembers what each material property and each sprite user held before.

## 2026-09-19: crash reports, sliders, Direct3D 12

Released together with Drag'n Wash Localization v1.3.0. The core and the preloader patcher go to 1.3.0, the Tool window and Assets libraries to 1.1.1; the others stay as they are. Everything new is additive: mods built on 1.2 need no change.

### Core 1.3.0

- Experimental. **Crash reports** ([docs/CRASH_REPORTS.md](docs/CRASH_REPORTS.md)): the core keeps a short record of each session in `BepInEx/CrashReports/session.log` (the start with versions, graphics and mods, scene changes, Unity errors and device messages, mod reloads, a heartbeat), flushed line by line; when the next start finds the session did not end cleanly, it writes a report folder with the last notes and the native stack from Unity's crash folder. `CrashReports.Note` lets mods add their own notes. `[Diagnostics] CrashReports`, on. Nothing is sent anywhere.
- Experimental. Memory dumps: a crash report keeps a copy of Unity's `crash.dmp`, and a watchdog writes a minidump when the game freezes (no frame for 15 s while in front; Unity writes nothing then), `[Diagnostics] HangDumps`, on. Dumps are for private sharing, not public issues.
- Experimental. `[Diagnostics] TraceGpuUploads` (off, advanced) adds a note for every GPU upload and released GPU resource, with the mod on the calling stack, to find what triggers the Direct3D 12 crash (Unity UUM-140564).
- Experimental. **Crash report window**: on Windows the core starts `CrashReporter.exe`, which waits for the game to close and, when it crashed or froze, writes the report and shows it at once in a window of its own (what happened, what to do, details, open the folder, copy the report), in English, Japanese or Chinese. `[Diagnostics] CrashReporterWindow`, on. Not under Wine/Proton.
- The freeze watchdog asks Windows which window is in front rather than Unity, so a game that freezes the moment it comes back (exclusive fullscreen on Direct3D 12, a problem of the game and Unity that the framework leaves alone) is caught too.
- Experimental. On Direct3D 12 the core uploads each changed font atlas once per frame (`[Direct3D12] BatchFontAtlasUploads`, on), which the crash reports showed to be the Tool window's crash; `GameInfo.FontAtlasUploadsBatched` says when it does.
- Experimental. `GameOptions.AddSlider` ([#39](https://github.com/TomXV/dragnwash-modframework/issues/39)): a slider row in the game's own Options screen, built by the game with its own slider (the range at its ends, the value shown while dragging). `OptionsSlider` has Id, Label, Min, Max, Step (values snap to Min + n x Step; 0 is continuous), Section, DefaultValue, GetSaved, Save and Preview, and follows the game's flow like `OptionsChoice`: moving it previews the value and shows Save, Save keeps it, Back returns to the saved value, Set Default. `AddSlider(id, label, step, min, max, getSaved, save, ...)` for the short form.

### Tool window 1.1.1

- On Direct3D 12 the Console (and `ToolWindow.Drawable`) draws translated text instead of `?` while the core batches font atlas uploads; with an older core, or with batching off, nothing changes.

### Assets 1.1.1

- `GameFonts.SetLanguage` notes the language in the crash reports' session record, so the crash report window speaks the language chosen in the game.

## 2026-09-19: saves after the game update

Released together with Drag'n Wash Localization v1.2.1. The flags and saves library goes to 1.0.1; the core and the preloader patcher go to 1.2.1 only because the release carries their version; the other libraries stay as they are.

### Core 1.2.1

- No change in the core; its version is the release's.

### Flags and saves 1.0.1

- Saves made after the game update of 2026-09-14 are found again. The game now writes `<steamid>/slot<N>/savegame.dgn` (inside the folder Steam Cloud syncs) and reads the older `<steamid>_slot<N>/savegame.dgn` only for a slot with no new save, so once a slot was saved in game (or deleted and started again) it disappeared from the Saves tab, which showed "No save files found" ([#40](https://github.com/TomXV/dragnwash-modframework/issues/40)). `GameSaves.Slots()` lists both layouts under the same slot names as before, so snapshot history carries on, and `SavePath` returns the file the game reads. A snapshot taken before the update gets the `{"version":1}` entry the game now expects when it is restored into the newer layout.
- `GameSaves.Slots()` lists slots in slot order (1, 2, 3) instead of most recently written first, so the Saves tab shows them left to right in order.

## 2026-09-17: developer tools, going online, Inspector

Released together with Drag'n Wash Localization v1.2.0, first as a pre-release. The core and the preloader patcher go to 1.2.0; the Tool window, Assets and Dialogue libraries to 1.1.0; Text and Flags and saves stay at 1.0.0; the Inspector library arrives at 1.0.0.

### Core 1.2.0

- Experimental. `DeveloperTools`: one switch for everything meant for mod makers and translators, `[Developer] Tools`, **off by default** (Options → Mods → Drag'n Wash ModFramework → Developer tools). Off, the Tool window does not open (F1 or `ToolWindow.Open`) and texture reloading is refused. Mods keep their own developer features behind `DeveloperTools.Enabled`, `Changed` and `WhenEnabled`; see GUIDE rule 8.
- Experimental. The Mods screen's settings pages edit text values: strings, keyboard shortcuts, colours and anything else BepInEx writes to the config file as text get a text field (Enter or leaving the field applies it; a value the config parser refuses is put back with the reason). A keyboard shortcut also has **Capture key**, which takes the next key pressed with the modifiers held. Before, these values could only be changed in the config file.
- Experimental. `GameEvents`: the game's events received once and handed to each mod on its own. `OnSceneLoaded`, `OnSceneUnloaded`, `OnGameStarted` (the title screen's first appearance; late handlers run at once), `OnQuitting`, each registered with the mod's GUID, and `Remove(guid)`. A handler that throws is logged and shown on the Mods screen under its mod, the other mods' handlers still run, and a handler that fails three times in a row is switched off for the session; a handler slower than 100 ms is noted in the debug log. The Assets library's texture replacements use it. GUIDE rule 9.
- Experimental. `SettingMeta` and `SectionMeta`, put in a `ConfigDescription`'s tags: a display name, an order, **Advanced** (hidden until "Show advanced settings" at the top of the page is turned on) and **RequiresRestart** (the page says so under the description); a section's display name, description and order. Tags with the same member names, including ConfigurationManager's `IsAdvanced`, `DispName` and `Order`, are read the same way, so a mod need not reference the framework.
- Experimental. `ModReload`: a mod's DLL reloaded while the game runs, for people who build mods. Only a mod that says so (`ModInfo.Reloadable = true` or `[ReloadableMod]`) and only while developer tools are on; libraries never. The new build is loaded next to the old one and checked first; then everything the old build registered with the framework and the libraries is taken out by its GUID and its assembly (`ModReload.Unloading`, `Prune`, `PruneEvent` for libraries), its Harmony patches are removed (its Harmony ID must be its GUID), its plugin is destroyed and the new one added where BepInEx started it. A build delivered as `<Mod>.dll.new` next to the DLL (the running DLL is locked on Windows), or a changed DLL where overwriting works, is reloaded by itself half a second later (`[Developer] WatchMods`), or by hand with `mods reload <guid>` in the Console; the preloader patcher makes the `.new` the real DLL at the next launch; `mods watch on|off`. A marker file catches a crash during a reload at the next start and switches watching off. Adding an Options row with an existing id now takes over its callbacks instead of being ignored, so a reloaded mod's row keeps working. See [docs/MOD_RELOAD.md](docs/MOD_RELOAD.md) and GUIDE rule 10.
- Experimental. Mods that go online say so, and players can see it (GUIDE rule 11, [docs/NETWORK.md](docs/NETWORK.md)). `ModInfo.Network` lists each host a mod connects to as a `NetworkUse` (host, what for, what is sent, how to turn it off); the framework declares its own update check, and one for every mod that sets `UpdateRepository`. The Mods screen shows an **Online** tag, a *Uses the internet* line and an **Internet** page. `NetworkWatch` (`[Network] Watch connections`, on) notes which mod actually connected where through `UnityWebRequest`, `WebRequest`, `HttpClient`, sockets and `TcpClient`, credited to the first plugin on the calling stack, and marks a connection nobody declared on the Mods screen and in the log. It only watches: nothing is blocked, and it is not a security boundary.
- `GameHooks.Require(ownerGuid, feature, "Type:Method")` takes Harmony's one-string form as well as a type and a method name, so the name the Inspector's Code view copies goes straight into the check.
- A new **logo** and Mods screen **icon** (a sponge and a gear) by NotaGames (@NotaGames), after the game's own logo, with the developers' OK; they replace the hand-made "Dg" monogram.
- The Options screen's **Mods button** is drawn now: a green plaque with a gear, in the game's menu style, normal and selected, by Mister ERIO (@mistererio), in place of the text label on a blank Back button. The text label is still used if the artwork cannot be loaded or the game's button changes.
- `GameHooks.Unavailable(ownerGuid, feature, reason)` marks a feature unavailable for a reason other than a missing game member (switched off after a crash, refused on this renderer), shown on the Mods screen like a failed check.

### Tool window 1.1.0

- Experimental. `ToolWindow.AddOverlay` (drawing over the game while the window is open), `BlockGameInput` and `Drawable` for tabs that work in the game view, as the Inspector does.
- `mods network` in the Console: what each mod declares online and what it was seen connecting to.
- Experimental. A **Console** tab: every BepInEx log line with its level and source, in colour, the last 2,000 kept; which levels and sources are shown is the player's choice, saved in the config and changeable from the tab, from commands (`log show`, `log level`, `log filter`) and from the Mods screen. Mods register commands with `ToolWindow.AddCommand`; built in: `help`, `log`, `mods`, `scene`, `clear` / `cls`. `ToolWindow.ErrorColor` and `WarningColor`. See [docs/CONSOLE.md](docs/CONSOLE.md).

- `clear` / `cls` console commands.

### Inspector 1.0.0

- Experimental. Its own library on top of the Tool window, so a mod's release can ship the Tool window without it. An **Inspector** tab: every loaded scene's objects as a tree with a name search, the selected object's components and its renderers' materials, and each one's fields and properties (private ones behind **Show private**), read as the game runs (**Freeze** stops that) and editable for booleans, numbers, strings, enums, vectors, colours, rects and lists of those; a material shows its shader's properties and keywords. Object references have a **Go** button. A vector, colour, rect or bounds has one field per component; a colour also has a swatch that opens a picker (RGBA and HSV bars, hex); **Reset** puts a row back to what it held before its first edit. A **Code** view per component: its type and assembly, its methods with a copyable `Type:Method` for `GameHooks.Require` and Harmony, **Patch** for the whole patch at once (the `GameHooks.Require` check, the `[HarmonyPatch]` attribute - carrying the parameter types, and how each is passed, where the name alone is ambiguous - and a Prefix and a Postfix with `__instance`, the game's own parameter names, `ref` where the game passes by reference, and `__result`), the Harmony patches mods put on them and by whom, its UnityEvents' listeners (serialized and added at run time), and a method's IL read with Mono.Cecil ("Copy for dnSpy" for the rest). A **debug view** (View menu) outlines everything of a kind at once: every renderer the camera sees, the selection's children, or what the search text matches by name or component type, plus colliders and triggers and lights, of the whole scene or of the selection alone, drawn in their own shape (a box as a box, a sphere as a sphere, a capsule as a capsule, a mesh collider as its wireframe, or, when only the physics engine knows its shape, as a shape scanned with rays from six sides, labelled as an estimate that may differ from the real one; a spot light as its cone) and as screen rectangles or 3D boxes, coloured by kind, with names near the pointer. **Bones** (B) draws the skinned meshes' armature in the game view, a click on a joint selects the bone for the gizmo; **Wire** (N) draws the selection's meshes as wireframes (a bounds box for a mesh the game keeps unreadable); **Edit mesh** (M, experimental even within the Inspector and labelled so) moves a readable mesh's vertices by dragging, into a copy of the mesh, one History entry per drag, **Reset mesh** puts the original back. A **free camera** (C): a copy of the game's camera flown with the right mouse button held (mouse look, W A S D, Q E, Shift, the wheel for speed) while the game's own camera is disabled, put back when it is turned off. **Pick** selects the object clicked in the game (uGUI first, then the renderer whose screen bounds are smallest around the pointer), **Highlight** outlines the selected object in the game with its name, and while picking the mouse wheel walks through overlapping objects. **Move** / **Rotate** / **Scale** draw a gizmo on the selected object in the game view (its local axes with a handle each, a readout of position, rotation and scale) that is dragged directly; **Reset transform** puts the object back. Every edit (rows and gizmo) is kept in a **History** view with the value before and after, Revert / Redo per entry and Undo last; a right click on a row offers its original and previous values, Copy value and Copy name. The colour picker has a saturation/value square with a hue bar. The hierarchy scrolls to the selection, and **Parent** selects the parent. Nothing is saved; an edit lasts until the scene reloads or the game quits. `Inspector.Inspect(target)` opens it on an object from a mod's own tab; the `inspect` command selects and sets from the console. See [docs/INSPECTOR.md](docs/INSPECTOR.md).

### Assets 1.1.0

- Experimental. Texture replacements: a PNG at `BepInEx/plugins/<Mod>/assets/textures/<texture name>.png` takes the place of the game texture of that name in every material and sprite, read at startup and applied at each scene load; two mods replacing the same texture are both named, never overridden silently. `AssetCatalog` lists loaded textures, materials, meshes and shaders, and the Tool window gets an **Assets** tab. See [docs/ASSET_TOOL.md](docs/ASSET_TOOL.md).
- Experimental. **Reload files** in the Assets tab re-reads changed replacement PNGs while the game runs, and `[Reload] WatchFiles` does it by itself when a file changes (never on Direct3D 12). A marker file catches a crash during a reload at the next start: the Mods screen says so and reloading is switched off until turned back on. Unreadable files keep their previous texture and are listed with the reason.
- Experimental. Clicking a texture's name in the Assets tab shows a **preview**: the texture as it is on the GPU, scaled to fit, over a checked ground, with its size, format and users. Beside the list, or above it in a narrow window.
- Experimental. The `assets` console command: `assets textures [filter]`, `assets replacements`, `assets apply`, `assets reload`.
### Dialogue 1.1.0

- Experimental. `LineKey` and `LineResolver`: keys for a line of dialogue that carry no text and survive a game update editing the line (line ID, exact hash, normalized hash, fingerprint), tried strongest first; matches by anything but the exact text are flagged for review. Same definitions in `tools/linekeys.py`, checked against `ci/linekey-vectors.json` in CI. See [docs/STABLE_LINE_KEYS.md](docs/STABLE_LINE_KEYS.md).

## 2026-09-15: hand-made icon

Released together with Drag'n Wash Localization v1.1.2. The libraries stay at 1.0.0.

### Core 1.1.2

- The Mods screen icon is the "Dg" monogram from the new hand-made logo. The READMEs open with the hand-made logo too.
- The preloader patcher is unchanged; its version follows the core.

## 2026-09-15: icon

Released together with Drag'n Wash Localization v1.1.1. The libraries stay at 1.0.0.

### Core 1.1.1

- The framework has its own icon on the Mods screen (`icon.png` next to the DLL), and the READMEs open with the logo.
- The preloader patcher is unchanged; its version follows the core.

## 2026-09-15: update notices

Released together with Drag'n Wash Localization v1.1.0. The libraries stay at 1.0.0.

### Core 1.1.0

- Shared installer for every mod: `Install.exe` (Windows, no PowerShell) and `install-steamdeck.sh` (Steam Deck / Linux) read the mod's `mod-install.json`. They install BepInEx (pinned SHA-256), never replace a newer framework with an older one, keep the player's files on update, write the mod's choices to its config, and on uninstall keep the framework and BepInEx while other mods need them. Shipped in the release zip under `installer/`; see [docs/INSTALLER.md](docs/INSTALLER.md).
- Uninstall from the Mods screen: press **Uninstall** twice, and the preloader patcher removes the mod's folder at the next launch, keeping the player's data listed in `mod-install.json`. The framework, its libraries and BepInEx are not removable from there.

- Update notices. A mod that names its GitHub repository (`ModInfo.UpdateRepository = "owner/name"`) is checked against the repository's latest release once a day. The Mods screen tags the mod with **Update**, shows the new version and opens its release page, and the title screen says how many updates are available. Nothing is downloaded or changed. Drafts and pre-releases are never offered. Players can switch it off in the framework's settings on the Mods screen (`[Updates] Check for updates`). The framework checks itself the same way.

## 2026-09-15: first release

First release, together with [Drag'n Wash Localization](https://github.com/TomXV/dragnwash-localization) v1.0.0, the first mod built on the framework.

### Core 1.0.0

- Mods screen, reached from the game's Options screen: every BepInEx plugin with its name, version, description, authors, website and icon (`ModFramework.Register(ModInfo)`, or read from the DLL and a Thunderstore manifest), plugins that did not load and why, and preloader patchers.
- On/off switches, applied by the preloader patcher at the next launch; switching off a mod other mods need asks first.
- Settings pages generated from BepInEx config, and rows in the game's own Options screen (`GameOptions`).
- Extension points: service registry (`Services`), health checks for patched game methods (`GameHooks`), extra Mods screen pages (`ModFramework.AddModsPage`), libraries (`ModInfo.IsLibrary`) with the mods that need them and the mods each one uses.
- Detection of game methods patched by more than one mod, shown as a Conflict.
- The title screen shows "Drag'n Wash ModFramework <version>" and how many mods loaded, just above the game's build id, like Minecraft Forge.
- Works with mouse, gamepad and on the Steam Deck: a thin white frame shows the selected item, and A, R2 and the trackpad click press it. The layout follows window resizes and full screen.

### Text 1.0.0

- `GameText.AddRewriter`, `RefreshAll`, `TryGetSource`: see and replace every TextMeshPro text before the game shows it, in an explicit order, and apply the rewriters again after something they depend on changed.

### Dialogue 1.0.0

- `GameDialogue.LineShowing`, `OptionShowing`, `NodeStarted`, `CurrentNode`, `TryGetLine`: the line of dialogue or option about to be shown, with line ID, speaker and node.

### Tool window 1.0.0

- One shared window (F1 by default, `[General] ToggleKey`) where mods add tabs with `ToolWindow.AddTab`. Frees the cursor, blocks game input under the window, turns gamepad and Steam Deck trackpad presses into clicks, and draws with a font that has Japanese and Chinese glyphs. A tab that throws is turned off with its error shown; the other tabs keep working.

### Assets 1.0.0

- `GameFonts`: one fallback font chain for the whole game, prepared per language at startup so Direct3D 12 does not crash. `GameAssets.LoadTexture` and `LoadBundle`, cached per file.

### Flags and saves 1.0.0

- `GameSaves`: save slots, level and flags, edits that snapshot first, restore, and a history of every version in `BepInEx/SaveHistory`. `GameFlags`: the flag catalog from CSV files.
