<p align="center"><picture><source media="(prefers-color-scheme: dark)" srcset="images/logo-notagames.png"><img src="images/logo-notagames-panel.png" alt="Drag'n Wash ModFramework" width="420"></picture></p>

# Drag'n Wash ModFramework

[日本語](README.ja.md)

A prerequisite mod for [Drag'n Wash](https://store.steampowered.com/app/4739660/) (BepInEx 5). It is a small core that keeps the code hooking into the game in one place, and gives other mods, and libraries built on top of it, a stable API:

- a Mods screen reached from the game's Options screen (like Minecraft Forge's mod list, with on/off switches)
- settings in the game's Options screen
- text and dialogue events
- safe asset loading on Direct3D 12
- and more

When the game updates, only the framework has to follow.

> [!NOTE]
> **1.4.3** is the latest release. What changed in each version: [Releases](#releases) below, and every detail in [CHANGELOG.md](CHANGELOG.md). What comes next: [docs/ROADMAP.md](docs/ROADMAP.md).

## Where to read

| To... | Read |
|---|---|
| Play with mods, get started, or look up a library | The [wiki](https://github.com/TomXV/dragnwash-modframework/wiki): a page for players, a getting-started walkthrough and a reference page for each library |
| Build a mod on it | [Playing well with others (wiki)](https://github.com/TomXV/dragnwash-modframework/wiki/Playing-well-with-others) |
| Know the goals and the order of work | [docs/DESIGN.md](docs/DESIGN.md) |
| See which game builds it was checked on | [docs/GAME_BUILDS.md](docs/GAME_BUILDS.md) |

## What is in it

| Plugin | GUID | What mods get |
|---|---|---|
| **Drag'n Wash ModFramework** (core) | `com.tomxv.dragnwash.modframework` | The Mods screen with on/off switches, settings pages and icons (`ModFramework.Register`), rows in the game's Options screen (`GameOptions`), a service registry (`Services`), health checks (`GameHooks`), `GameInfo` |
| **Text** | `com.tomxv.dragnwash.modframework.text` | See and replace every text before the game shows it (`GameText`) |
| **Dialogue** | `com.tomxv.dragnwash.modframework.dialogue` | The line of dialogue or option about to be shown, with line ID, speaker and node (`GameDialogue`) |
| **Tool window** | `com.tomxv.dragnwash.modframework.toolwindow` | One shared F1 window for developer tools, off until **Developer tools** is turned on in Options → Mods, where each mod adds tabs (`ToolWindow`) |
| **Assets** | `com.tomxv.dragnwash.modframework.assets` | Fonts for any language and texture and asset bundle loading, without the Direct3D 12 crash (`GameFonts`, `GameAssets`) |
| **Flags and saves** | `com.tomxv.dragnwash.modframework.saves` | Save slots, flags and a history of every save (`GameSaves`, `GameFlags`) |
| **Inspector** (experimental) | `com.tomxv.dragnwash.modframework.inspector` | An Inspector tab in the F1 window: every loaded scene and object, their components and values, the game's code, and every loaded object by kind ([Inspector](https://github.com/TomXV/dragnwash-modframework/wiki/Inspector)) |
| **Overrides** (experimental) | `com.tomxv.dragnwash.modframework.overrides` | Runs mods that have no code: a folder of values to change in the game, made with the Inspector ([Overrides](https://github.com/TomXV/dragnwash-modframework/wiki/Overrides)) |
| **Bridge** (experimental) | `com.tomxv.dragnwash.modframework.bridge` | Offers the read operations to AI clients on this computer over MCP, and serves the code graph's page. Off by default ([Bridge](https://github.com/TomXV/dragnwash-modframework/wiki/Bridge)) |
| **Graphs** (experimental) | `com.tomxv.dragnwash.modframework.graphs` | Runs mods with no code that *do* things: *when this happens, do these things*, made on the Bridge's page ([Graphs](https://github.com/TomXV/dragnwash-modframework/wiki/Graphs)) |

Each library is its own plugin with its own version; install the ones the mods you use need. See [CHANGELOG.md](CHANGELOG.md) for versions.

### Update notices

From core 1.1.0, the framework tells you on the Mods screen and the title screen when a mod you have installed has a newer release.

- **What is checked:** only mods that name their GitHub repository, each at most once a day.
- **What is sent:** the framework asks GitHub's public API (`api.github.com`) for the repository's latest release, and sends nothing about you, your game or your other mods. GitHub sees your IP address, as with any web page.
- **What it does:** nothing is downloaded or installed. The Mods screen opens the release page for you.
- **To switch it off:** open **Options → Mods → Drag'n Wash ModFramework → Settings** and set **Check for updates** to Off, or set `Check for updates = false` in `BepInEx/config/com.tomxv.dragnwash.modframework.cfg`.

## Releases

From 1.0.0 on, a change that breaks the public API comes only with a new major version. Every change is in [CHANGELOG.md](CHANGELOG.md).

- **1.4.3** (latest): **Open page** and **Graphs** buttons on the Bridge tab, so the editor is one press away.
- **1.4.2**: a graph that answers a key now says which other mods answer it too (`ModFramework.WhoElseUses`).
- **1.4.1**: **[Graphs](https://github.com/TomXV/dragnwash-modframework/wiki/Graphs)** 0.1.0, mods with no code that *do* things (*when this happens, do these things*), with:
  - the first write operations
  - an editor of blocks and nodes on the Bridge's page
  - what a graph changes listed on the Mods screen, put back when it stops and named when two mods change one value

  The core and the preloader patcher go to 1.4.1; Overrides to 0.1.1, the Bridge to 0.1.1 and the Inspector to 1.1.1, whose History now lists what other mods change.
- **1.4.0**:
  - mods with no code ([Overrides](https://github.com/TomXV/dragnwash-modframework/wiki/Overrides), a folder of values to change, made with the Inspector)
  - an [operations registry](https://github.com/TomXV/dragnwash-modframework/wiki/Operations) every library registers what it can do in
  - the [Bridge](https://github.com/TomXV/dragnwash-modframework/wiki/Bridge), which offers the read operations to AI clients on this computer over MCP
  - the [code graph](https://github.com/TomXV/dragnwash-modframework/wiki/Code-graph)
  - in the [Inspector](https://github.com/TomXV/dragnwash-modframework/wiki/Inspector): an object explorer, Animators, Rigidbodies, and scenes and levels

  The core and the preloader patcher go to 1.4.0; the Tool window and Dialogue to 1.2.0; Text, Flags and saves and the Inspector to 1.1.0; Assets to 1.2.0. Everything new is experimental.
- **1.3.0**:
  - [crash reports](https://github.com/TomXV/dragnwash-modframework/wiki/Crash-reports), with a window outside the game that says what happened
  - the fix for the Direct3D 12 crash they found (font atlas uploads batched to once per frame)
  - `GameOptions.AddSlider`

  The Tool window and Assets go to 1.1.1.
- **1.2.1**: the Saves tab finds saves made after the game update of September 14, 2026 again.
- **1.2.0** (core):
  - the developer-tools switch (off by default)
  - text and key-binding fields on the settings pages
  - [`GameEvents`](https://github.com/TomXV/dragnwash-modframework/wiki/Playing-well-with-others) and `SettingMeta`
  - reloading a mod while the game runs
  - [mods that go online say so](https://github.com/TomXV/dragnwash-modframework/wiki/Going-online)

  With it the Tool window, Assets and Dialogue libraries go to 1.1.0 ([Console](https://github.com/TomXV/dragnwash-modframework/wiki/Console), [texture replacements and reloading](https://github.com/TomXV/dragnwash-modframework/wiki/Assets), [stable line keys](https://github.com/TomXV/dragnwash-modframework/wiki/Dialogue)), and a new [Inspector](https://github.com/TomXV/dragnwash-modframework/wiki/Inspector) library (1.0.0) arrives. The new features are marked experimental, and the Inspector stays experimental.
- **1.1.2**: the framework's icon is replaced with the "Dg" monogram from the hand-made logo.
- **1.1.1**: the framework's icon.
- **1.1.0**: [update notices](#update-notices), the shared installer, and uninstalling from the Mods screen.
- **1.0.0**: released together with [Drag'n Wash Localization](https://github.com/TomXV/dragnwash-localization) v1.0.0, the first mod built on it.

## For mod developers

Reference `DragNWash.ModFramework.dll` (and the library DLLs you use) and declare each dependency so BepInEx loads them first.

- **What to use for what**, and the rules that keep mods working together: [Playing well with others (wiki)](https://github.com/TomXV/dragnwash-modframework/wiki/Playing-well-with-others).
- **A one-click install for players:** ship the shared installer with a `mod-install.json`: [Installer (wiki)](https://github.com/TomXV/dragnwash-modframework/wiki/Installer).

```csharp
[BepInPlugin("com.example.mymod", "MyMod", "1.0.0")]
[BepInDependency(ModFramework.Guid, BepInDependency.DependencyFlags.HardDependency)]
public class MyMod : BaseUnityPlugin
{
    private void Awake()
    {
        ModFramework.Ready += () =>
        {
            if (GameInfo.IsDirect3D12)
            {
                // Load fonts and textures now, not later.
            }
        };
    }
}
```

## Building

1. Install the .NET SDK and have BepInEx 5.4.23.5 installed in the game.
2. Copy the reference assemblies from your own game install (they are never committed):

   ```bash
   pwsh tools/copy-libs.ps1
   ```

   Pass `-GamePath` if the game is not in the default Steam library.
3. Build the core, the preloader patcher and the libraries:

   ```bash
   dotnet build src/DragNWash.ModFramework/DragNWash.ModFramework.csproj -c Release
   ```

   and the same for each `src/DragNWash.ModFramework.*` project.

> [!TIP]
> With Docker, `docker compose run --rm checks` runs every check CI runs, and `docker compose run --rm build` builds the core and the libraries, in the image CI uses: [docs/DOCKER.md](docs/DOCKER.md). Nothing is installed on your machine.

Each DLL goes to its project's `bin/Release/`. To try them:

- copy each plugin DLL to its own folder, `<Game>/BepInEx/plugins/<assembly name>/`
- copy `DragNWash.ModFramework.Preloader.dll` to `<Game>/BepInEx/patchers/`

### Building on GitHub

The **Build** workflow (Actions) builds the release zip on GitHub.

- **When it runs:** on every push to `main`, on a `v*` tag, or by hand. It never runs for pull requests, so a fork cannot reach the token.
- **What it does:** it fetches the reference assemblies from a private repository (`TomXV/dragnwash-libs`, never public) with the `LIBS_TOKEN` secret, runs `tools/pack.ps1` on a Windows runner and uploads `release/DragNWash.ModFramework-<version>.zip` as a workflow artifact.
- **On a tag:** it also creates a **draft** release with the zip attached; a person writes the notes and publishes it.
- **After a game update:** refresh the private repository from a game install with `tools/copy-libs.ps1`.

## Rules for this repository

- Never commit the game's files, BepInEx binaries or anything from `libs/`. A check on every push and pull request enforces it.
- Material from the game follows [docs/CONTENT_POLICY.md](docs/CONTENT_POLICY.md): made by hand or changed into something new is fine, the game's data unchanged is not.
- Code that touches game classes stays `internal`; mods only see the framework's own types.

## Taking part

- **Contributing:** [CONTRIBUTING.md](CONTRIBUTING.md) — how to set up, what the rules above mean in practice, and what to put in a pull request.
- **Code of conduct:** [CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md).
- **Security:** please report a vulnerability privately, not in an issue — [SECURITY.md](SECURITY.md).
- **Sponsoring:** [GitHub Sponsors](https://github.com/sponsors/TomXV), if you want to and can. The framework is free and stays free either way.

## A note to the developers

This is an unofficial fan project and is not affiliated with Gator Dragon Games.

- It contains none of the game's assets or code as they are (see [docs/CONTENT_POLICY.md](docs/CONTENT_POLICY.md)).
- It does not modify the game's files (BepInEx loads it at runtime).

If the development team has any concerns, please open an issue or contact the maintainer, and it will be changed or taken down.

## Credits

- The **logo** above and the framework's **icon** on the Mods screen were drawn by **NotaGames** ([@NotaGames](https://github.com/NotaGames)), after the game's own logo; the game's developers said that is fine ([#15](https://github.com/TomXV/dragnwash-modframework/issues/15)). Used with permission; not covered by the MIT license below.
- The **Mods button** in the Options screen (`ModsButton0.png`, `ModsButton1.png`) was drawn for the framework by **Mister ERIO** ([@mistererio](https://github.com/mistererio)) and is used with permission. It is their artwork, not the game's, and is not covered by the MIT license below.

## License

[MIT](LICENSE), except the artwork named under [Credits](#credits).
