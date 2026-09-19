# Operations, MCP and node graphs: a plan

[日本語](API_PLAN.ja.md)

> **Stage 1 is built** (experimental): the registry in the core, the console's `op`, and read operations from the core and every library (the Tool window, the Inspector, Assets, Dialogue, Text, flags and saves). The later stages are still a plan, first written on the `experimental/api-mcp` branch. Each stage gets its own design and research before code; this page says what the stages are, in what order, and what holds them together.

Three wishes, one foundation:

1. **Every library as an API**: what each library can do, callable by name with plain arguments, not only from C#.
2. **MCP**: the same things understandable and usable by an AI client (Claude and others), through the Model Context Protocol.
3. **Node graphs**: existing code seen as nodes, and new behaviour put together from blocks or nodes, without writing C#.

All three are views of one thing: **a registry of operations** that each library fills with what it can do.

```
        each library registers its own operations (each library owns them)
                                   │
       ┌───────────────────┬───────┴───────────┬────────────────────┐
  Console commands      MCP tools          node graph blocks     C# (as today)
  (people, typing)      (an AI client)     (people, arranging)
```

The Core keeps only the counter; what is behind it belongs to each library. It is the same idea as the rest of the framework: the Core does not carry everything.

## Decisions (2026-09-19)

- **MCP starts read-only.** An AI client can look (objects, values, logs, mods) but not change anything. Writing is decided again, on its own, later.
- **The node editor lives outside the game**, in a browser page served by the game on this computer; the game only runs graphs. Drawing a node editor in the F1 window (IMGUI) would be heavy and limited by the Direct3D 12 font restrictions.
- **Building is allowed, from registered operations only.** The roadmap said "no scripting language"; that changes to: no *free* scripting (no arbitrary methods, no reflection), but graphs that call only operations the libraries registered. What a graph can do is exactly what the operations allow, and the Mods screen lists which ones it uses.
- **Order**: the registry, then MCP, then viewing code as nodes, then building.

## Stage 0: inventory

What the libraries offer today (2026-09-19):

| Library | Public types | Console |
|---|---|---|
| Core | ModFramework, ModInfo, GameEvents, GameHooks, GameInfo, GameOptions, DeveloperTools, ModReload, NetworkWatch, Services, … (18) | `mods`, `scene` |
| Tool window | ToolWindow, ConsoleLog, ConsoleCommands, … (6) | `help`, `log`, `clear` |
| Assets | GameAssets, AssetCatalog, AssetReplacements, GameFonts, … (9) | `assets` |
| Dialogue | GameDialogue, LineKey, LineResolver, … (7) | |
| Text | GameText, TextContext | |
| Saves | GameSaves, GameFlags, SaveSnapshot, FlagInfo | |
| Inspector | Inspector (1; the rest is internal) | `inspect` |

The inventory is finished in stage 1's design: every public member and command gets a line saying whether it becomes an operation, and under what name.

## Stage 1: the operation registry (Core)

A small registry in the Core, next to `Services` (which stays: `Services` is typed C# between mods; operations are named and described for callers outside C#).

- **An operation** has a name (`library.noun.verb`, for example `inspector.member.get`), a one-line description, its parameters (name, type, required, description), what it returns, and its **kind**: *read* (changes nothing) or *write*.
- **Types** are the ones that travel as JSON: text, numbers, booleans, enums, lists and objects of those. Vectors and colours travel as text, parsed the way the Inspector's rows parse them.
- **Main thread**: Unity objects may only be touched on the main thread, so every call is queued and run there, whoever made it.
- **Owners**: registered with the library's GUID, taken out with it (as `ModReload` already does for everything else a mod registers).
- **Every call is logged** with who made it (Console, MCP, a graph) and, for writes, joins the Inspector's History where there is one, so it can be undone.
- **Console**: `op <name> key=value …` runs any operation, with completion. The existing commands keep working and later become thin wrappers.

First operations, reads first:

| Library | Operations |
|---|---|
| Core | `mods.list`, `mods.network`, `game.info`, `scene.list` |
| Tool window | `log.read` |
| Inspector | `objects.find`, `objects.children`, `components.list`, `member.get`, `selection.get` (and later `member.set`, a write); for the object explorer, `loaded.kinds`, `loaded.list`, `loaded.usedby` ([OBJECT_EXPLORER.md](OBJECT_EXPLORER.md)) |
| Assets | `textures.list`, `materials.list`, `meshes.list`, `replacements.list` |
| Dialogue | `dialogue.current`, `lines.find` |
| Text | `text.lookup`, `text.language` |
| Saves | `saves.list`, `flags.list`, `flags.get` |

Mods register their own the same way; Drag'n Wash Localization could offer `loc.line.get` and `loc.coverage`.

## Stage 2: the local bridge and MCP

Designed in detail in [BRIDGE.md](BRIDGE.md).

A new, optional library, **Bridge**, its own plugin, so a mod's release can leave it out.

- **This computer only.** It listens on `127.0.0.1` alone (a port in the config), with a token made for each session and written to a file only this user can read. Any other address is refused.
- **Off by default**, and only while developer tools are on. It declares itself under `ModInfo.Network` (a local connection is still a connection, and players should see it, in the spirit of [Going online (wiki)](https://github.com/TomXV/dragnwash-modframework/wiki/Going-online)), and the Mods screen shows *Bridge listening* and *AI client connected*, with a button to disconnect.
- **MCP**: the bridge speaks MCP over Streamable HTTP. `tools/list` is generated from the registry, **read operations only**; `tools/call` runs one on the main thread and returns JSON. Descriptions come from the registry, so an AI client understands them the same way a person reading `help` does.
- A small stdio shim, for MCP clients that only start local programs, comes only if people need one.
- **Limits**: results capped in size, calls rate-limited, no file access beyond what operations offer.
- The same bridge later serves the node editor's page and its plain JSON API (stages 3 and 4).

## Stage 3: code seen as nodes (read only)

Designed in detail in [CODE_GRAPH.md](CODE_GRAPH.md): a page only, not MCP.

The Inspector's Code view already reads a method's IL through Mono.Cecil, and knows the Harmony patches and UnityEvent listeners on a type.

- An operation `code.graph` returns a method or a type as a graph: the method's blocks and branches, the methods it calls, the fields it reads and writes, which Harmony patches (and whose) sit on it, and which events lead to it.
- The editor page draws it as nodes, and a node opens the next method.
- **Structure, not source**: there is no C# decompilation (that stays with dnSpy and ILSpy), and nothing of the game's code is written to files or sent anywhere except to the page on this computer. See the [content policy](CONTENT_POLICY.md).

## Stage 4: building with blocks and nodes

- A **graph** is data: events (a scene loaded, a key pressed, a dialogue line shown), operations (read and write), a little flow (if, wait, repeat with a limit) and values.
- **Blocks** (as in Scratch) and **nodes** are two ways to edit the same graph file.
- A graph is shipped the way [Overrides](https://github.com/TomXV/dragnwash-modframework/wiki/Overrides) are: a folder with `mod.json` and `graphs/*.json`, no DLL, shown and switched off on the Mods screen. Overrides and graphs share that loader.
- A small **Graphs** library runs them: only registered operations, a time budget each frame, a graph that fails three times in a row switched off (as `GameEvents` handlers are), and the Mods screen lists the operations each graph uses, so what a graph *can* do is visible before it runs.

## Order and yardsticks

| Stage | What | Where |
|---|---|---|
| 0 | Inventory | this page, then stage 1's design |
| 1 | Operation registry, `op` in the Console, first read operations | Core, each library |
| 2 | Bridge and MCP (read only) | new Bridge library |
| 3 | Code as nodes; the editor page | Inspector, Bridge |
| 4 | Graphs: blocks and nodes, data-only mods | new Graphs library, shared with Overrides |

The yardsticks stay: every mod runs safely together, each part is small enough for someone else to carry on, and what a mod (or an AI client, or a graph) can do is shown to the player.
