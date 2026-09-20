# Changelog

Versions of the core and of each library are separate, and follow semantic versioning: from 1.0.0 on, a change that breaks the public API comes only with a new major version.

## Unreleased

### Bridge 0.1.0

- New library, experimental: the registry's **read** operations for AI clients on this computer over MCP (Streamable HTTP at `http://127.0.0.1:47821/mcp`). Off by default (`[Bridge] Enabled`) and only while the developer tools are on. Checks the Host (DNS rebinding) and Origin (web pages) of every request and a token (`Authorization: Bearer`, kept in the user's profile, renewable); at most 8 connections, 4 sessions (30 minutes idle), 20 calls a second, 1 MB requests, 10 seconds a call. `initialize`, `ping`, `tools/list` (each read operation as a tool, `inspector.member.get` as `inspector_member_get`, with a JSON Schema and `readOnlyHint`) and `tools/call` (text and `structuredContent`). Declares itself in `ModInfo.Network`; a **Bridge** tab in the F1 window (status, clients, last calls, Copy setup, New token, Disconnect all) and a console command `bridge`. See docs/BRIDGE.md.
- The **page** on this computer (docs/CODE_GRAPH.md), with a door of its own apart from MCP: `bridge.page.open` (a write operation, so never offered over MCP; the Inspector's **Graph** buttons call it) makes a one-time code (one use, 60 seconds) and opens `http://127.0.0.1:<port>/page#code=…` in the browser; the page trades the code for a cookie (`HttpOnly`, `SameSite=Strict`, `Path=/page`), and every call after that needs the cookie and an `Origin` equal to the Bridge's own address. The page (one HTML file inside the DLL, nothing from the internet; strict Content-Security-Policy, no framing) calls read operations, the page-only ones included, through `/page/api/op`. New token, Disconnect all and turning the Bridge off sign the page out too.
- The **Code Graph app** on Windows (CodeGraph.exe, in the Bridge's folder with the WebView2 parts it needs): the page in a window of its own, opened by the Graph buttons (`[Bridge] OpenPageIn`, App or Browser). It signs in with the Bridge's token through `POST /page/api/code` (only with the token and without an `Origin`, so no web page can), waits while the game is closed and signs in again at the same method when it comes back, keeps one window (a second start hands its method over), Keep on top, and remembers its size and place. The game's start of it has the shell start it again, so Steam does not count the window as the game still running. Built deterministically.
- The Bridge's listening socket is not inherited by programs the game starts (on Windows it kept the port after the game exited).
- While it listens, the game keeps running when its window is not in front (`Application.runInBackground`; the game's own setting comes back when the Bridge stops): the game stops otherwise, and a client, the browser above all, takes the front.

### Overrides 0.1.0

- New library, experimental: mods with no code. A folder in BepInEx/plugins with `mod.json` (name, authors, description, version) and `overrides/*.json` changes values in the game: a component's field or property (`"private": true` for the private fields where the game's scripts keep their settings) or a material's property, found by scene, path, component and member. Applied a frame after each scene load and a second later, and to root objects as they appear (each level's dragon comes long after the scene loads); the game's values are kept, and `GameOverrides.Reload()` puts them back and reads the files again. Values are written so they read back exactly and read leniently (Euler angles, `#RRGGBB`). When two mods change the same thing, the one that loads later (folder name order) wins and the log names both. Each mod is listed on the Mods screen and can be switched off there. With the Tool window installed, the console's `overrides` lists the mods and how many of their values are written, and `overrides reload` reads the files again. See [Overrides (wiki)](https://github.com/TomXV/dragnwash-modframework/wiki/Overrides).

### Inspector, Export as overrides (next version)

- Experimental. **Code graph** (docs/CODE_GRAPH.md): the game's code read with Mono.Cecil from its own assemblies and those of its authors (not Unity's, .NET's, the mods', nor bundled libraries): `code.graph` (a method as basic blocks and branches, each block in words — calls, fields, text, what it decides — with its IL; the calls and fields; its callers, those through a base method marked; the Harmony patches on it with the mod's name; the UnityEvent listeners in the loaded scenes and Unity messages that lead to it; a coroutine or `async` method drawn through its state machine, with the state dispatch and each yield or await marked), `code.type`, `code.callers`, `code.search` and `code.stats`. Page only: AI clients never get them. The index is built on first use (about 80 ms for 5,900 methods). In the Code view, **Graph** on each method and **Type graph** open it in the browser through the Bridge.
- Experimental. Read operations: `inspector.objects.find`, `inspector.objects.children`, `inspector.components.list`, `inspector.member.get` (a component's members as the rows show them, private ones on request) and `inspector.selection.get`.
- Experimental. **Export as overrides** in the History view (with the Overrides library installed): the edits become a mod with no code in BepInEx/plugins/<name> (mod.json and overrides/main.json; a second export into the same mod adds a file), one row per place with the value it holds now. Each History entry now keeps where the edit was made (scene, path, component and its index, member, private or not; for a material, the renderer showing it and the property). Edits an override cannot hold are left out with the reason: list elements, mesh vertices, Animator parameters and clip swaps, GameObject rows.

### Dialogue and Text, next version

- `DialogueLine.SpeakerGuess` and `SpeakerFrom`: the game's script mostly names no speaker in a line, so `Speaker` was empty for nearly every line; the guess takes the script's name when there is one, else the node's (the part before the first `_`: `Ryan_1_intro` is Ryan), and Kobold (the player) for options. `dialogue.recent` and `dialogue.current` report it with where it came from.
- Experimental. Read operations: `dialogue.current` (the node, whether lines or options show, the last line) and `dialogue.recent` (the last 100 lines and options shown this session); `text.rewriters` (the mods that rewrite text, in order) and `text.shown` (text on screen, as the game set it and as shown).

### Flags and saves, next version

- Experimental. Read operations: `saves.list` (slots with their level and history copies), `saves.flags.list` and `saves.flags.get` (with what the flag catalog says).

### Core, next version

- Experimental. `Operation.PageOnly`: an operation only the Bridge's page (and the console) may call, never an AI client over MCP, for what shows the game's own code. `ModFramework.NameOf(guid)`: the name the Mods screen shows for a mod, from its GUID or Harmony ID.
- Experimental. **Operations** (docs/API_PLAN.md, stage 1): a registry of what each library can do, by name (`library.noun.verb`), with a description, plain parameters (text, number, true/false, with the accepted choices) and a kind (read or write). `Operations.Register`, `Find`, `All`; `CallNow` on the main thread and `Call` from any thread (queued to the next frame); arguments are checked and converted against the parameters; results are plain values (lists, dictionaries, text, numbers) with `Operations.ToJson`, capped at 200,000 characters; every call is logged with who made it (write calls at Info); an owner's operations go when it is reloaded. The core registers `mods.list`, `mods.network`, `game.info` and `scene.list`.
- Experimental. `ModFramework.RegisterDataMod(info, version, manifestPath)`: a mod with no DLL (a folder another library reads) is listed on the Mods screen like a plugin, and switching it off renames its `mod.json` to `mod.json.disabled` at the next launch (the preloader patcher renames it, as it does DLLs).

### Inspector, next version

- Experimental. **Object explorer** (docs/OBJECT_EXPLORER.md): a **Scene | Objects** switch at the start of the toolbar. Objects lists every loaded object by kind (Textures, Sprites, Materials, Shaders, Meshes, Audio, Animation, Fonts, Data with a sub-folder per ScriptableObject type and the game's own first, Objects outside the scenes, Other), with counts, a short fact per row, a search by name and `t:Type`, **Show hidden** and **Close all**; only the rows on screen are drawn, and the list keeps instance IDs, not references, so it keeps no asset loaded. It is made in one pass when looked at, on Refresh and after a scene load, and says how long that took. The selected object's members are rows as in Scene, less the arrays Unity copies on every read, under a header per kind (a texture's size, format and readability with a preview; a sprite's part of its texture; a shader's properties, keywords and materials; a mesh's counts and bounds; a clip's length and events; a sound's length and format). Editing, Reset and History work as in Scene; the first edit of a shared object says so once; GameObjects outside the scenes and their components are read-only.
- Experimental. The **keys move through the left pane's list**, in the Objects view and in the tree and the search results of Scene: up and down one row, Page up and Page down a pane, Home and End the ends, left and right closing and opening a folder or a node with children (left on a closed one goes up to the one it is in). Moving onto an object selects it, and the list scrolls only as far as it must to keep the row in view.
- Experimental. **Used by** on the object explorer's header: where the object is used (renderers, mesh filters and colliders, sprites, audio sources, animators and controllers, materials, and every field of scripts and ScriptableObjects that can hold it), each with **Go**; it stops at 2,000 places and says how many objects it looked through and how long it took.
- Experimental. **Go** on every object row, not only objects, components, materials and textures: a scene's object in Scene, anything else in Objects; a texture row also has **Assets**. `Inspector.Inspect` accepts any object and opens the right view.
- Experimental. Console: `objects`, `objects <kind> [filter]`, `objects usedby <kind> <name>` and `inspect object <kind> <name>`. Read operations `inspector.loaded.kinds`, `inspector.loaded.list` and `inspector.loaded.usedby`; `inspector.selection.get` reports a selection in Objects.
- The wireframe is drawn whole for detailed meshes (parts of a dragon were missing): each renderer gets a mesh of its edges, built once (each edge once) and drawn with the camera's matrices. Before, every line went through GL immediate mode each frame, which dropped vertices past about 65,000 and, sent whole, uploaded megabytes a frame and crashed Direct3D 12 (UUM-140564); now only a skinned mesh's positions go up each frame.
- A deep tree (a rig's bones) no longer pushes names out of the tree pane: its levels get narrower, and when even that is not enough, the levels above every row in view are left out.
- Scenes and levels: **Start** and **Load** of another scene begin trial play, in which the game's saves are not written until the title screen; before, finishing a level started from the list saved its number + 1, which moved a further save's progress back. The pane's lines wrap instead of running out of a narrow window, and search results are searched again when a scene change destroyed some of them (they showed as upper-case headings).
- A search result whose path is too wide for the tree pane is cut from the front (.../sunny/main_Directional Light), so the object's own name stays in view.
- The debug view's names keep clear of the selection's name and of each other (moved above, or below the outline when there is no room), instead of being drawn over them.
- Experimental. Rigidbodies and Rigidbody2Ds, read by reflection (no physics module is referenced): a debug view with each body's centre of mass and a velocity arrow, tagged with its speed, mass, kind and sleep (View → Rigidbodies: centre of mass and velocity); a **Rigidbodies list**, scene-wide or under the selection, fastest first, with a filter and Awake only; buttons on a selected body: **Stop**, **Kinematic** (kept in History), **Sleep** / **Wake**; and **Pause physics** with **Step** (simulation mode set to Script, put back on Resume, when the window closes or when developer tools go off). Console: `bodies`, `bodies pause|resume|step [count]`. See [Inspector (wiki)](https://github.com/TomXV/dragnwash-modframework/wiki/Inspector), Rigidbodies.
- Experimental. Animators, read by reflection (no animation module is referenced): a selected Animator's parameters (Float, Int, Bool, Trigger) and layer weights are rows among its members, edited and kept in History like any member; the base layer's line, and **Layers** in place of the members, show each layer's weight, the clips it plays, how far through and the blend to the next state; **Pause animation** (speed 0, put back on Resume, when the window closes or when developer tools go off) with **Step** (1/30 s) and, in Layers, a time slider per layer. See [Inspector (wiki)](https://github.com/TomXV/dragnwash-modframework/wiki/Inspector), Animators.
- Experimental. Animator clips: **Clips** lists the controller's clips (length, frame rate, loop, events, what each is swapped for); **Preview** plays one in a graph of its own with a time slider, pause and stop; **Replace** swaps a clip for any clip loaded (an AnimatorOverrideController on the game's controller), kept in History.
- Experimental. **Scenes and levels** (View menu), read from the game's own types by reflection: the level running (dragon, weather, state, clean), the game's own Skip level and Clean cheats, every level with **Start** (plays it in place of the current one; saved only when finished), the scenes loaded, and every scene with **Load** through the game's loading screen.

### Tool window, next version

- Experimental. Console `op`: lists the operations, `op help <name>` describes one, `op <name> key=value ...` runs it and prints the result as JSON, with completion of names and parameters. The Tool window registers `log.read` (the last console lines, by source and level).
- The footer grows to fit a notice that wraps in a narrow window (up to three lines) instead of cutting off its second line.
- Steam Deck and gamepads: a trackpad click (or A, R2) over the window is now a real left mouse button, held while the button is, and the sticks and d-pad send real wheel steps (XTest on Linux, SendInput on Windows). Every control works with them, not buttons only: text fields, tree rows, value drags, sliders, scroll bars, moving and resizing the window. Where the system takes no such input, presses still click buttons as before.

### Assets 1.2.0

- Experimental. **Inspect** on a texture in the Assets tab opens the texture in the Inspector's Objects view, whose Used by lists its materials and sprites (before, it opened the first material using it, and showed only when one did).
- Experimental. Read operations: `assets.textures.list`, `assets.materials.list`, `assets.meshes.list` (with a name filter), `assets.replacements.list` (which game texture, from which mod, for which language) and `assets.fonts.language`.
- Experimental. Texture replacements per language: `AssetReplacements.AddLanguageFolder(guid, root, subfolder)` takes `<root>/<language>/<subfolder>/*.png`, which apply only while `GameFonts.Language` is that language and win over a plain replacement of the same texture (both are named in the log). Only the language in use is loaded. A language change takes the previous pictures back and loads the new ones; on Direct3D 12 the new ones wait for a restart (`AssetReplacements.PendingLanguage`). `SetLanguageFoldersEnabled(guid, on)` switches a mod's pictures off and on; `AssetReplacements.Changed` is raised afterwards. A texture with no picture in the language falls back to the languages its `fallback.txt` names (one per line, in order), then to a plain replacement, then to the game's own; a picture that cannot be loaded falls back the same way. `TextureReplacement.Language` names the language a picture came from, and the Assets tab shows it. For Drag'n Wash Localization's translated pictures.
- Replacements can be taken back: the library remembers what each material property and each sprite user held before.

## 2026-09-19: crash reports, sliders, Direct3D 12

Released together with Drag'n Wash Localization v1.3.0. The core and the preloader patcher go to 1.3.0, the Tool window and Assets libraries to 1.1.1; the others stay as they are. Everything new is additive: mods built on 1.2 need no change.

### Core 1.3.0

- Experimental. **Crash reports** ([Crash reports (wiki)](https://github.com/TomXV/dragnwash-modframework/wiki/Crash-reports)): the core keeps a short record of each session in `BepInEx/CrashReports/session.log` (the start with versions, graphics and mods, scene changes, Unity errors and device messages, mod reloads, a heartbeat), flushed line by line; when the next start finds the session did not end cleanly, it writes a report folder with the last notes and the native stack from Unity's crash folder. `CrashReports.Note` lets mods add their own notes. `[Diagnostics] CrashReports`, on. Nothing is sent anywhere.
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
- Experimental. `ModReload`: a mod's DLL reloaded while the game runs, for people who build mods. Only a mod that says so (`ModInfo.Reloadable = true` or `[ReloadableMod]`) and only while developer tools are on; libraries never. The new build is loaded next to the old one and checked first; then everything the old build registered with the framework and the libraries is taken out by its GUID and its assembly (`ModReload.Unloading`, `Prune`, `PruneEvent` for libraries), its Harmony patches are removed (its Harmony ID must be its GUID), its plugin is destroyed and the new one added where BepInEx started it. A build delivered as `<Mod>.dll.new` next to the DLL (the running DLL is locked on Windows), or a changed DLL where overwriting works, is reloaded by itself half a second later (`[Developer] WatchMods`), or by hand with `mods reload <guid>` in the Console; the preloader patcher makes the `.new` the real DLL at the next launch; `mods watch on|off`. A marker file catches a crash during a reload at the next start and switches watching off. Adding an Options row with an existing id now takes over its callbacks instead of being ignored, so a reloaded mod's row keeps working. See [Mod reload (wiki)](https://github.com/TomXV/dragnwash-modframework/wiki/Mod-reload) and GUIDE rule 10.
- Experimental. Mods that go online say so, and players can see it (GUIDE rule 11, [Going online (wiki)](https://github.com/TomXV/dragnwash-modframework/wiki/Going-online)). `ModInfo.Network` lists each host a mod connects to as a `NetworkUse` (host, what for, what is sent, how to turn it off); the framework declares its own update check, and one for every mod that sets `UpdateRepository`. The Mods screen shows an **Online** tag, a *Uses the internet* line and an **Internet** page. `NetworkWatch` (`[Network] Watch connections`, on) notes which mod actually connected where through `UnityWebRequest`, `WebRequest`, `HttpClient`, sockets and `TcpClient`, credited to the first plugin on the calling stack, and marks a connection nobody declared on the Mods screen and in the log. It only watches: nothing is blocked, and it is not a security boundary.
- `GameHooks.Require(ownerGuid, feature, "Type:Method")` takes Harmony's one-string form as well as a type and a method name, so the name the Inspector's Code view copies goes straight into the check.
- A new **logo** and Mods screen **icon** (a sponge and a gear) by NotaGames (@NotaGames), after the game's own logo, with the developers' OK; they replace the hand-made "Dg" monogram.
- The Options screen's **Mods button** is drawn now: a green plaque with a gear, in the game's menu style, normal and selected, by Mister ERIO (@mistererio), in place of the text label on a blank Back button. The text label is still used if the artwork cannot be loaded or the game's button changes.
- `GameHooks.Unavailable(ownerGuid, feature, reason)` marks a feature unavailable for a reason other than a missing game member (switched off after a crash, refused on this renderer), shown on the Mods screen like a failed check.

### Tool window 1.1.0

- Experimental. `ToolWindow.AddOverlay` (drawing over the game while the window is open), `BlockGameInput` and `Drawable` for tabs that work in the game view, as the Inspector does.
- `mods network` in the Console: what each mod declares online and what it was seen connecting to.
- Experimental. A **Console** tab: every BepInEx log line with its level and source, in colour, the last 2,000 kept; which levels and sources are shown is the player's choice, saved in the config and changeable from the tab, from commands (`log show`, `log level`, `log filter`) and from the Mods screen. Mods register commands with `ToolWindow.AddCommand`; built in: `help`, `log`, `mods`, `scene`, `clear` / `cls`. `ToolWindow.ErrorColor` and `WarningColor`. See [Console (wiki)](https://github.com/TomXV/dragnwash-modframework/wiki/Console).

- `clear` / `cls` console commands.

### Inspector 1.0.0

- Experimental. Its own library on top of the Tool window, so a mod's release can ship the Tool window without it. An **Inspector** tab: every loaded scene's objects as a tree with a name search, the selected object's components and its renderers' materials, and each one's fields and properties (private ones behind **Show private**), read as the game runs (**Freeze** stops that) and editable for booleans, numbers, strings, enums, vectors, colours, rects and lists of those; a material shows its shader's properties and keywords. Object references have a **Go** button. A vector, colour, rect or bounds has one field per component; a colour also has a swatch that opens a picker (RGBA and HSV bars, hex); **Reset** puts a row back to what it held before its first edit. A **Code** view per component: its type and assembly, its methods with a copyable `Type:Method` for `GameHooks.Require` and Harmony, **Patch** for the whole patch at once (the `GameHooks.Require` check, the `[HarmonyPatch]` attribute - carrying the parameter types, and how each is passed, where the name alone is ambiguous - and a Prefix and a Postfix with `__instance`, the game's own parameter names, `ref` where the game passes by reference, and `__result`), the Harmony patches mods put on them and by whom, its UnityEvents' listeners (serialized and added at run time), and a method's IL read with Mono.Cecil ("Copy for dnSpy" for the rest). A **debug view** (View menu) outlines everything of a kind at once: every renderer the camera sees, the selection's children, or what the search text matches by name or component type, plus colliders and triggers and lights, of the whole scene or of the selection alone, drawn in their own shape (a box as a box, a sphere as a sphere, a capsule as a capsule, a mesh collider as its wireframe, or, when only the physics engine knows its shape, as a shape scanned with rays from six sides, labelled as an estimate that may differ from the real one; a spot light as its cone) and as screen rectangles or 3D boxes, coloured by kind, with names near the pointer. **Bones** (B) draws the skinned meshes' armature in the game view, a click on a joint selects the bone for the gizmo; **Wire** (N) draws the selection's meshes as wireframes (a bounds box for a mesh the game keeps unreadable); **Edit mesh** (M, experimental even within the Inspector and labelled so) moves a readable mesh's vertices by dragging, into a copy of the mesh, one History entry per drag, **Reset mesh** puts the original back. A **free camera** (C): a copy of the game's camera flown with the right mouse button held (mouse look, W A S D, Q E, Shift, the wheel for speed) while the game's own camera is disabled, put back when it is turned off. **Pick** selects the object clicked in the game (uGUI first, then the renderer whose screen bounds are smallest around the pointer), **Highlight** outlines the selected object in the game with its name, and while picking the mouse wheel walks through overlapping objects. **Move** / **Rotate** / **Scale** draw a gizmo on the selected object in the game view (its local axes with a handle each, a readout of position, rotation and scale) that is dragged directly; **Reset transform** puts the object back. Every edit (rows and gizmo) is kept in a **History** view with the value before and after, Revert / Redo per entry and Undo last; a right click on a row offers its original and previous values, Copy value and Copy name. The colour picker has a saturation/value square with a hue bar. The hierarchy scrolls to the selection, and **Parent** selects the parent. Nothing is saved; an edit lasts until the scene reloads or the game quits. `Inspector.Inspect(target)` opens it on an object from a mod's own tab; the `inspect` command selects and sets from the console. See [Inspector (wiki)](https://github.com/TomXV/dragnwash-modframework/wiki/Inspector).

### Assets 1.1.0

- Experimental. Texture replacements: a PNG at `BepInEx/plugins/<Mod>/assets/textures/<texture name>.png` takes the place of the game texture of that name in every material and sprite, read at startup and applied at each scene load; two mods replacing the same texture are both named, never overridden silently. `AssetCatalog` lists loaded textures, materials, meshes and shaders, and the Tool window gets an **Assets** tab. See [Assets (wiki)](https://github.com/TomXV/dragnwash-modframework/wiki/Assets).
- Experimental. **Reload files** in the Assets tab re-reads changed replacement PNGs while the game runs, and `[Reload] WatchFiles` does it by itself when a file changes (never on Direct3D 12). A marker file catches a crash during a reload at the next start: the Mods screen says so and reloading is switched off until turned back on. Unreadable files keep their previous texture and are listed with the reason.
- Experimental. Clicking a texture's name in the Assets tab shows a **preview**: the texture as it is on the GPU, scaled to fit, over a checked ground, with its size, format and users. Beside the list, or above it in a narrow window.
- Experimental. The `assets` console command: `assets textures [filter]`, `assets replacements`, `assets apply`, `assets reload`.
### Dialogue 1.1.0

- Experimental. `LineKey` and `LineResolver`: keys for a line of dialogue that carry no text and survive a game update editing the line (line ID, exact hash, normalized hash, fingerprint), tried strongest first; matches by anything but the exact text are flagged for review. Same definitions in `tools/linekeys.py`, checked against `ci/linekey-vectors.json` in CI. See [Dialogue (wiki)](https://github.com/TomXV/dragnwash-modframework/wiki/Dialogue).

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

- Shared installer for every mod: `Install.exe` (Windows, no PowerShell) and `install-steamdeck.sh` (Steam Deck / Linux) read the mod's `mod-install.json`. They install BepInEx (pinned SHA-256), never replace a newer framework with an older one, keep the player's files on update, write the mod's choices to its config, and on uninstall keep the framework and BepInEx while other mods need them. Shipped in the release zip under `installer/`; see [Installer (wiki)](https://github.com/TomXV/dragnwash-modframework/wiki/Installer).
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
