# Code graph: the game's code seen as nodes

[日本語](CODE_GRAPH.ja.md)

> **Design** (2026-09-19), stage 3 of the [API plan](API_PLAN.md). Nothing is built yet. The first measurements are in [Research](#research); the rest of the research comes before any code.

The Inspector's Code view lists a component's methods, the Harmony patches on them and a method's IL, as rows in the F1 window. The code graph shows the same code as a drawing: a method's blocks and branches, what it calls, the fields it reads and writes, whose patches sit on it and which events lead to it. Clicking a call opens that method. It is for mod makers who want to know *where* to hook and *what else* a method touches, without dnSpy.

## Decisions (2026-09-19)

- **On a page in the browser, served by the Bridge on this computer.** Not in the F1 window: IMGUI would be slow for a drawing and limited by the Direct3D 12 font restrictions (as the [plan](API_PLAN.md) decided).
- **Page only, not MCP.** The graph is an operation, but one only the page can call; AI clients do not see it. The game's code is Gator Dragon Games' work, and the [content policy](CONTENT_POLICY.md) keeps it on this computer. Letting an AI service see part of it (say, only who calls whom) would be decided again, on its own.
- **Opened from the F1 window.** A button makes a one-time code and opens the page with it; the page trades it for a cookie of its own. No token to paste.
- **Down to branches.** A method is drawn as its basic blocks with their branches (if, loops, switch, try), not only as one node in a call graph.

## What it shows

For one method (the *focus*):

- **Blocks**: the method's basic blocks, from IL, in order. Each block shows what it does in words the page can make without decompiling: the calls it makes, the fields it reads and writes, what it compares, what it returns or throws. Its IL is one click away (the same IL the F1 window already shows).
- **Branches**: edges between blocks, labelled *true*/*false*, *case n*, *loop back*, *catch*, *finally*.
- **Calls**: every called method, as a node beside the block that calls it. Methods of the game open on click; Unity's and .NET's are shown but not opened.
- **Callers**: which methods of the game call the focus (a reverse index built once per session).
- **Patches**: the Harmony prefixes, postfixes, transpilers and finalizers on the focus and on each called method, with the mod that owns them (the Mods screen's name, not only the Harmony ID).
- **Events**: the UnityEvent listeners in the loaded scenes that call the focus, and the framework's `GameEvents` it raises or answers.
- **Coroutines**: a method that returns `IEnumerator` only builds a hidden class; the graph follows it to that class's `MoveNext` and draws its states as the method's body, each `yield` a labelled edge. The same for `async` methods.

For one type: its methods as nodes, grouped (Unity messages such as `Update`, public, private), with the calls between them; clicking one focuses it.

**Structure, not source.** No C# decompilation (that stays with dnSpy and ILSpy). Nothing of the game's code is written to a file or sent anywhere but to the page on this computer.

## Parts

| Part | Where | What |
|---|---|---|
| `code.graph`, `code.type`, `code.callers`, `code.search` | Inspector (operations) | Read the game's assemblies with Mono.Cecil (as the Code view does) and return the graph as JSON. Marked *page only*. |
| Page-only operations | Core (`Operations`) | A new flag on an operation: *page only*. MCP's `tools/list` and `tools/call` leave these out; the console's `op` and the page can call them. |
| The page | Bridge | `GET /page` (one HTML file: its script, its styles and the drawing code inside it; nothing from the internet), and `/page/api/…` for the page's calls. |
| Page sign-in | Bridge | One-time code → cookie (below). |
| The button | Inspector | **Open graph** in the Code view (with the method or type selected), and `code open <Type:Method>` in the console. |

The layout is drawn by the page's own code: at most 79 blocks per method (see research), so a simple layered layout is enough, and no library has to be shipped or fetched.

## The page's sign-in

The Bridge refuses web pages today (any `Origin` but `null`). The page is a web page, from the Bridge's own address, so it gets its own door, separate from MCP:

1. **Open graph** makes a one-time code (32 random bytes; one use; 60 seconds) and opens `http://127.0.0.1:<port>/page#<code>` with the system's browser.
2. The page reads the code from after `#` (a browser never sends that part to a server, so it is in no log), removes it from the address bar, and posts it to `/page/api/login`.
3. The Bridge answers with a cookie: `HttpOnly`, `SameSite=Strict`, `Path=/page`, lasting while the game runs. The code is spent.
4. Every later call to `/page/api/…` needs that cookie **and** an `Origin` equal to the Bridge's own address (`http://127.0.0.1:<port>`); the Host check stays as it is. Other sites cannot call it (no cookie is sent from another site with `SameSite=Strict`, and their `Origin` differs), and the MCP door does not accept the cookie.
5. **New token** and turning the Bridge off end the page's sign-ins too.

The page is only for this computer, like the rest of the Bridge. On the Steam Deck it opens in Desktop Mode's browser.

## Not in this stage

Changing code, or anything that writes; stage 4 (building graphs) uses the same page later. Mods' own code is not drawn: a mod shows up as its patches (owner and patch method), not its IL. Debugging (breakpoints, stepping) is out of scope.

## Research

Done 2026-09-19, outside the game, with Mono.Cecil (the copy in `BepInEx/core`) on `Assembly-CSharp.dll` (424 KB, the game's update of 2026-09-14):

| | |
|---|---|
| Reading the assembly | 8 ms; walking every method 28 ms; finding every method's blocks 15 ms |
| Types | 466, of which 56 made by the compiler |
| Methods with a body | 2385; 10 lambdas; 44 state machines (coroutines and `async`) |
| Blocks per method | median 1, 90th percentile 7, 99th 19, **max 79** |
| Instructions per method | median 6, 90th percentile 42, max 527 |
| Call sites | 8904; 2612 into the game's own assembly |
| Field reads and writes | 6537; delegates made (`ldftn`) 318 |
| `switch` / `try` | 21 / 108 |

So: the whole assembly can be indexed at once (calls, callers) in about 50 ms, graphs are small, and coroutines are common enough to need their own handling. The game also ships assemblies of its studio and its authors (`GatorDragonGamesExtensions`, `Naelstrof.*`, `RalivIK` and others); they are drawn the same way.

**Coroutines** (checked the same day): all 44 state machines (39 iterators, 5 `async`) lead back to the method that makes them, through its `IteratorStateMachine` / `AsyncStateMachine` attribute. `MoveNext` begins by reading the state field and branching on it: a chain of comparisons in 37, a `switch` in 7 (the ones with more states). Each resume point is a constant stored into that field just before returning (median 1 per machine, at most 12); 18 have `try`/`finally`, which the compiler also routes through the state. So the page can replace the dispatch at the head with edges *resumes after yield n*, and draw the rest as the method's body. Cecil's `Resolve()` needs an assembly resolver pointed at the game's `Managed` folder for types from other assemblies; comparing full names is enough for this.

Still to check, before code:

1. **Opening the browser**: `Application.OpenURL` with a `#` part, on Windows (does the default browser keep the part after `#`?) and on the Steam Deck's Desktop Mode.
2. **Cookies on 127.0.0.1** with `SameSite=Strict` and `HttpOnly` in Edge, Chrome and Firefox; the `Origin` a same-address `fetch` sends.
3. ~~**Coroutine states**~~ (done, below).
4. **UnityEvent listeners in loaded scenes**: the cost of scanning every loaded component's `UnityEventBase` fields for "who calls this method", and whether to scan once per scene load.
5. **Patch owners**: mapping a Harmony ID to the Mods screen's name for every mod that patches (framework libraries, Localization, other mods).

## Order of work

1. Research (above).
2. Page-only operations in the registry; `code.graph`, `code.type`, `code.callers`, `code.search` in the Inspector, tried with `op`.
3. The page door in the Bridge: `/page`, sign-in, cookie, `/page/api/op`.
4. The page: layout, blocks, calls, callers, patches, coroutines, search.
5. **Open graph** in the Code view; docs.
