# Code graph without the game

[日本語](CODE_GRAPH_STANDALONE.ja.md)

> **Researched and prototyped** (experimental, 2026-09-20) on the branch `experimental/code-graph-standalone`. The prototype works on Windows; it is not packed into a release. Where it lives for good is [open](#open-decisions).

The [code graph](CODE_GRAPH.md) draws a method as blocks and branches, with its calls, callers and fields. Today it only works inside Drag'n Wash: the Inspector builds the graph, the Bridge serves the page. Nothing in the drawing itself needs the game, though, so the same graph can show **any .NET assembly**: another Unity game's `Managed` folder, a mod, a library, a DLL of your own. This record says what has to be split off for that, how the page gets its data without the game, and what the prototype does.

## Decisions

Decided by the owner on 2026-09-20.

1. **Where it lives: in this repository, as `codegraph-standalone/`**, while it is experimental. The shared file is compiled into both builds from one place, so a fix to the graph reaches the Inspector and the app in one commit. When it gets users of its own (issues from people who never played the game, releases of its own), it moves to a repository of its own with the model and the page, and neither side carries the other: the framework keeps what only the game knows (patches, listeners, the game's assemblies), the tool keeps the rest.
2. **Its name is Code Graph**, with no game's name in the window, in the page's header or in what Windows shows. The file stays `CodeGraphStandalone.exe` in this repository, so it is never mistaken for the game's own `CodeGraph.exe` next to the Bridge; the zip it is released in may name it `CodeGraph.exe`.
3. **Unity messages outside Unity**: the model stays as it is, so the Inspector's answers do not change. A method named `Update` or `Awake` in a DLL that is not a Unity game is marked wrongly; a hook that asks "is this a MonoBehaviour?" through the base types comes when the app is used on non-Unity code for real.
4. **Released on its own**, as a zip of the tool's own, not inside the framework's zip, which is for people who play the game.

## What is shared, what is split off

| Part | Where | Needs |
|---|---|---|
| **`CodeGraphModel`**: the index (methods, types, callers, state machines), `code.graph`, `code.type`, `code.callers`, `code.search` as the page's JSON | `src/DragNWash.ModFramework.Inspector/CodeGraphModel.cs`, compiled into the Inspector and linked into the app | Mono.Cecil and .NET only |
| The game's side: which assemblies are the game's (`Application.dataPath`, the skip list), the Harmony patches and their mods, the UnityEvent listeners of the loaded scenes, the log, registering the operations | `InspectorCodeGraph.cs` | Unity, Harmony, BepInEx, the framework |
| The page | `src/DragNWash.ModFramework.Bridge/Page/page.html`, embedded in the Bridge and in the app | a browser engine |
| The app: opening files, answering the page | `codegraph-standalone/` | WebView2, Mono.Cecil (NuGet) |

The model takes what only the game knows through three settings: `Patches` (a lookup of the patches on a method id, made once per graph), `Listeners` (what else leads to a method) and `Source` (the words in "No type … in the game's assemblies"). Left empty, a graph has no patches and no listeners. Nothing else changed: `Id()` for Cecil and for reflection moved with it (they must agree), and `InspectorCodeGraph.Id()` forwards to it, so the Code view is untouched.

**The Inspector answers exactly as before.** Checked outside the game: the code from `main` (its Harmony and scene parts replaced by empty ones) and the new model were run side by side over the game's 15 assemblies, every method (as body and as stub), its callers, every type, some searches and some wrong names: 18,751 answers, all identical as JSON, errors included. A deliberately changed message was caught. The patches and listeners code is moved, not changed; it needs the game to try (below).

## Opening files and folders

- **Files**: one DLL or EXE, or several (Open… → Assemblies…, the command line, or dropped on the window).
- **A folder**: its `.dll` and `.exe` files, leaving out .NET's and Unity's own (`System*`, `Microsoft.*`, `mscorlib`, `netstandard`, `Unity*`, `Mono.*`…) unless nothing else is there; `--all` keeps them. Calls into what is left out are listed, not opened.
- **A Unity game's folder** (or its `_Data` folder): its `<Name>_Data/Managed` folder, as above.
- **An IL2CPP game** (`GameAssembly.dll`, no `Managed`): refused with the reason; its code is native, and there is no IL to draw. Three of the six Unity games on this PC are IL2CPP.
- **Native DLLs** in a folder are skipped and counted (the .NET runtime folder has 13).
- Files are read into memory (`InMemory`), so they are not locked and can be rebuilt; **Read again** reads the same paths and stays on the method shown.

Command line: `CodeGraphStandalone.exe [file.dll …|folder] [--focus m:<method>|t:<type>] [--all]`.

## How the page gets its data without the game

The page calls `POST /page/api/op` with `{ name, args }` and reads `{ ok, value | error }`. Four ways to answer it, tried or checked:

| Way | Result |
|---|---|
| **WebView2 `WebResourceRequested`** on an address that exists only in the window (`https://codegraph.example/…`) | **Chosen.** The page and its calls are answered inside the app's process; the page stays as it is (same-origin `fetch`), POST bodies arrive, and no port is opened (checked: the app listens on nothing). A call's round trip: median 0.8 ms, at most 14 ms over 50 graphs. |
| `SetVirtualHostNameToFolderMapping` | Serves files from a folder only; it cannot answer the page's POST, so it would still need one of the others. The page is embedded in the exe anyway. |
| `chrome.webview.postMessage` both ways | Would work (not tried), but the page's `call()` would need a second transport (ids, replies) kept in step with `fetch`. No gain over the above. |
| A local HTTP server | The only way for a normal browser, so the way for Linux and macOS (below). It needs the Bridge's door again: one-time code, cookie, Origin and Host checks, since any page and any program on the PC could otherwise call it. |

The page learns it is in the app from `window.dnwHost`, which the app sets before the page's script runs: its own header, status line and hint, and an **Open…** button that asks the app for its menu. Without `dnwHost` (in the game, in a browser) the page is word for word as before (checked in a browser).

Dropping a file: the drop reaches the page, which hands the dropped files to the app with `chrome.webview.postMessageWithAdditionalObjects`; WebView2 gives the app their real paths. This script is added by the app, not written in the page.

## Linux and macOS (designed only)

The model builds and gives the same graphs on .NET 8 with Mono.Cecil 0.11.4 (checked on the game's assemblies: the same blocks, similar times). What differs is the window:

- **Recommended: the page in the system's browser, served by a small console host** (`net8.0`, also running on Windows): `codegraph-standalone serve <paths>` listens on `127.0.0.1` on a free port, opens the browser at `/page#code=<one-time code>`, and uses the same sign-in as the Bridge's page (code → `HttpOnly`, `SameSite=Strict` cookie; every call needs the cookie, the right `Origin` and the right `Host`). Opening files is by the command line (a browser cannot hand over paths). The door's logic should then be shared with the Bridge the same way the model is, not written twice.
- A native window (WebKitGTK, Photino): one more dependency per system, for little over the browser.
- On the Steam Deck, the browser way works in Desktop Mode.

## Lost and gained outside the game

**Lost:** Harmony patches (no mods are loaded), UnityEvent listeners (no scenes), and anything else that only the running game knows. The graph says "Patches on it (0)", "Events that lead here" lists only Unity messages.

**Gained:** any Mono-built .NET assembly, including other games', mods, BepInEx itself and your own code; no game running, no Bridge, no token; offline; no decompiler (structure, not source, as in the game).

## Security

- It reads only what the user opens (dialog, drop, command line), and writes nothing but its window's WebView2 profile in `%LOCALAPPDATA%\DragNWash ModFramework\CodeGraphStandalone`.
- No port, no network: the page's address is answered inside the process; other programs and web pages cannot reach it. Navigation away from it is refused, new windows are refused, DevTools and host objects are off, and the page keeps the Bridge's Content-Security-Policy (nothing from the internet).
- The page can only name an operation and its arguments; it cannot ask for a file.
- Not for AI clients: like the game's graph, it is not offered over MCP.

## License and content policy

- The app is MIT like the rest; it ships Mono.Cecil (MIT) and Microsoft's WebView2 SDK, as CodeGraph.exe does.
- **Never ship others' code.** The repository, releases, docs, screenshots and tests never hold anything read from an assembly that is not ours: no graphs, no IL, no names. Tests use the app's own assemblies, or assemblies built for the test. The measurements below give only counts and times.
- It shows structure, not source, and has no export. What a user may do with another program's code is set by that program's license, not by this tool; the app says nothing is sent anywhere, and nothing is.
- For Drag'n Wash itself the [content policy](CONTENT_POLICY.md) is unchanged: the game's code stays on the user's computer.

## Research

Measured 2026-09-20 on this PC (Windows 11, .NET Framework 4.7.2 with the Mono.Cecil 0.10.4 that BepInEx ships), read-only. "Read" is opening the files; most of the reading happens in "index", where Cecil decodes the bodies.

| Assemblies | Files | Size | Methods | Read + index | Every graph | Largest method | Memory |
|---|---|---|---|---|---|---|---|
| Drag'n Wash, the game's 15 | 15 | 1.1 MB | 5,891 | 14 + 113 ms | 103 ms | 259 blocks | +21 MB |
| Drag'n Wash, Assembly-CSharp | 1 | 0.4 MB | 2,455 | 13 + 64 ms | 45 ms | 79 | +7 MB |
| Drag'n Wash, whole `Managed` (Unity, .NET, all) | 172 | 37 MB | 230,390 | 76 + 3,215 ms | 3.5 s | 1,774 | +743 MB |
| BepInEx `core` | 12 | 1.3 MB | 8,319 | 16 + 124 ms | 123 ms | 221 | +29 MB |
| .NET 10 `System.Private.CoreLib` | 1 | 15.7 MB | 36,285 | 22 + 486 ms | 529 ms | 206 | +131 MB |
| .NET 10 shared runtime (13 native skipped) | 159 | 62 MB | 128,344 | 310 + 2,047 ms | 2.2 s | 354 | +524 MB |
| Another Mono Unity game, Assembly-CSharp | 1 | 2.9 MB | 11,170 | 137 + 169 ms | 342 ms | 306 | +60 MB |
| The same game, its 24 own assemblies | 24 | 5.7 MB | 28,225 | 1,272 (cold disk) + 237 ms | 625 ms | 306 | +120 MB |
| A Unity tool, Assembly-CSharp | 1 | 1.1 MB | 5,638 | 103 + 56 ms | 135 ms | 470 | +24 MB |

- One graph: median 0.005 to 0.011 ms, 99th percentile at most 0.36 ms, the slowest 24 ms. No method failed in any set.
- About 3 KB of memory per method, and the whole index is built at once: that is why a folder leaves out .NET's and Unity's own assemblies by default. Of the 3.2 s for the whole `Managed` folder, 1.2 s is Cecil decoding the bodies.
- In the app: the game's folder (33 assemblies after the filter, 27,476 methods) opens in 380 ms; the other game's folder in 590 ms; a single DLL in about 80 ms. The largest method found (1,774 blocks) is laid out and drawn by the page in 0.72 s.
- Mono.Cecil 0.10.4 (BepInEx's) and 0.11.4 (NuGet) both read everything above; the shared file compiles against both.

## The prototype

`codegraph-standalone/` (net472, x64, WinForms and WebView2, built deterministically like CodeGraph.exe):

- `Program.cs`: the command line.
- `Opened.cs`: files, folders, Unity game folders, IL2CPP and native DLLs, into one `CodeGraphModel`.
- `ViewerForm.cs`: the window, the page and its calls through `WebResourceRequested`, Open… (assemblies, folder, read again), dropped files.
- `codegraph/WindowChrome.cs`, built into both apps: the dark title bar and the WebView2 settings the two windows share.

Tried on Windows by driving the window through WebView2's DevTools protocol (a debugging port turned on only for the test): the type and method views of the game's assemblies, search, a coroutine through its state machine, an unknown operation's error, a request for another path (404), dropping a DLL, a folder, a Unity game's folder and an IL2CPP game's folder.

## Order of work

1. Research and prototype (this branch).
2. The owner decides where it lives and its name.
3. Windows app: remember window size, Keep on top, a hook for "is this a MonoBehaviour", a recent-files list.
4. The console host for Linux and macOS with the shared door.
5. Its own release.
