<p align="center"><picture><source media="(prefers-color-scheme: dark)" srcset="images/logo-notagames.png"><img src="images/logo-notagames-panel.png" alt="Drag'n Wash ModFramework" width="420"></picture></p>

# Drag'n Wash ModFramework

[日本語](README.ja.md)

A prerequisite mod for [Drag'n Wash](https://store.steampowered.com/app/4739660/) (BepInEx 5). It's a small core that keeps the code that hooks into the game in one place, and gives other mods, and the libraries built on top of it, a stable API to work with:

- a Mods screen you open from the game's Options screen (like Minecraft Forge's mod list, with on/off switches)
- settings in the game's Options screen
- text and dialogue events
- asset loading that's safe on Direct3D 12
- and more

So when the game updates, only the framework has to catch up.

> [!NOTE]
> **1.6.0** is the latest release. [Releases](#releases) below says what changed in each version, and [CHANGELOG.md](CHANGELOG.md) has every detail. What's coming next is in [docs/ROADMAP.md](docs/ROADMAP.md).

## Where to read

| To... | Read |
|---|---|
| Play with mods, get started, or look up a library | The [wiki](https://github.com/TomXV/dragnwash-modframework/wiki), with a page for players, a getting-started walkthrough and a reference page for each library |
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

Each library is a plugin of its own with its own version, so install the ones your mods need. The versions are in [CHANGELOG.md](CHANGELOG.md).

### Update notices

From core 1.1.0, the framework lets you know on the Mods screen and the title screen when a mod you've installed has a newer release.

It only checks mods that name their GitHub repository, and each of them at most once a day. It asks GitHub's public API (`api.github.com`) for the repository's latest release and sends nothing about you, your game or your other mods. GitHub does see your IP address, the same as with any web page.

Nothing gets downloaded or installed. The Mods screen just opens the release page for you.

To switch it off, open **Options → Mods → Drag'n Wash ModFramework → Settings** and set **Check for updates** to Off, or set `Check for updates = false` in `BepInEx/config/com.tomxv.dragnwash.modframework.cfg`.

## Releases

From 1.0.0 on, a change that breaks the public API only ever comes with a new major version. Every change is in [CHANGELOG.md](CHANGELOG.md).

- **1.6.0**: a launcher before the game. `Launcher.exe` runs from Steam's launch options and brings mods up to date before you play; **Update now** on the Mods screen does it from inside the game. The installer's window is rebuilt to match, and the F1 window draws its text on macOS.
- **1.5.0**: a whole new look. The Mods screen is rebuilt on frosted glass, every tab of the F1 window got a going-over, starts are faster, and the installer fetches ModFramework from its own release. From 1.5.0 on, the core and every library share one version number.
- **1.4.3**: the Bridge tab gets **Open page** and **Graphs** buttons, so the editor is one press away.
- **1.4.2**: a graph that answers a key now tells you which other mods answer it too (`ModFramework.WhoElseUses`).
- **1.4.1**: **[Graphs](https://github.com/TomXV/dragnwash-modframework/wiki/Graphs)** 0.1.0, mods with no code that *do* things (*when this happens, do these things*). It comes with:
  - the first write operations
  - an editor of blocks and nodes on the Bridge's page
  - what a graph changes listed on the Mods screen, put back when it stops and named when two mods change one value

  The core and the preloader patcher go to 1.4.1; Overrides to 0.1.1, the Bridge to 0.1.1 and the Inspector to 1.1.1. The Inspector's History now lists what other mods change too.
- **1.4.0**:
  - mods with no code ([Overrides](https://github.com/TomXV/dragnwash-modframework/wiki/Overrides), a folder of values to change, made with the Inspector)
  - an [operations registry](https://github.com/TomXV/dragnwash-modframework/wiki/Operations) where every library registers what it can do
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

  Along with it, the Tool window, Assets and Dialogue libraries go to 1.1.0 ([Console](https://github.com/TomXV/dragnwash-modframework/wiki/Console), [texture replacements and reloading](https://github.com/TomXV/dragnwash-modframework/wiki/Assets), [stable line keys](https://github.com/TomXV/dragnwash-modframework/wiki/Dialogue)), and there's a new [Inspector](https://github.com/TomXV/dragnwash-modframework/wiki/Inspector) library (1.0.0). The new features are marked experimental, and the Inspector is staying experimental.
- **1.1.2**: the framework's icon is now the "Dg" monogram from the hand-made logo.
- **1.1.1**: the framework gets its icon.
- **1.1.0**: [update notices](#update-notices), the shared installer, and uninstalling from the Mods screen.
- **1.0.0**: released alongside [Drag'n Wash Localization](https://github.com/TomXV/dragnwash-localization) v1.0.0, the first mod built on it.

## For mod developers

Reference `DragNWash.ModFramework.dll` (plus the library DLLs you use) and declare each one as a dependency so BepInEx loads them first.

- What to use for what, and the rules that keep mods working together, are in [Playing well with others (wiki)](https://github.com/TomXV/dragnwash-modframework/wiki/Playing-well-with-others).
- To give players a one-click install, ship the shared installer with a `mod-install.json`. [Installer (wiki)](https://github.com/TomXV/dragnwash-modframework/wiki/Installer) explains how.

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
2. Copy the reference assemblies from your own game install (they never get committed):

   ```bash
   pwsh tools/copy-libs.ps1
   ```

   Pass `-GamePath` if the game isn't in the default Steam library.
3. Build the core, the preloader patcher and the libraries:

   ```bash
   dotnet build src/DragNWash.ModFramework/DragNWash.ModFramework.csproj -c Release
   ```

   and the same for each `src/DragNWash.ModFramework.*` project.

> [!TIP]
> If you have Docker, `docker compose run --rm checks` runs every check CI runs and `docker compose run --rm build` builds the core and the libraries, both in the image CI uses ([docs/DOCKER.md](docs/DOCKER.md)). Nothing gets installed on your machine.

Each DLL ends up in its project's `bin/Release/`. To try them out:

- copy each plugin DLL to its own folder, `<Game>/BepInEx/plugins/<assembly name>/`
- copy `DragNWash.ModFramework.Preloader.dll` to `<Game>/BepInEx/patchers/`

### Building on GitHub

The **Build** workflow (Actions) builds the release zip on GitHub. It runs on every push to `main`, on a `v*` tag, or when you start it by hand. It never runs for pull requests, so a fork can't get at the token.

It fetches the reference assemblies from a private repository (`TomXV/dragnwash-libs`, which is never made public) using the `LIBS_TOKEN` secret, runs `tools/pack.ps1` on a Windows runner and uploads `release/DragNWash.ModFramework-<version>.zip` as a workflow artifact. On a tag it also creates a **draft** release with the zip attached, and a person writes the notes and publishes it.

After a game update, refresh the private repository from a game install with `tools/copy-libs.ps1`.

## Rules for this repository

- Never commit the game's files, BepInEx binaries or anything from `libs/`. A check on every push and pull request makes sure of it.
- Material from the game follows [docs/CONTENT_POLICY.md](docs/CONTENT_POLICY.md). Something made by hand or changed into something new is fine, but the game's data unchanged isn't.
- Code that touches game classes stays `internal`; mods only see the framework's own types.

## Taking part

- If you'd like to contribute, [CONTRIBUTING.md](CONTRIBUTING.md) covers how to set up, what the rules above mean in practice, and what to put in a pull request.
- The code of conduct is in [CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md).
- Please report a security vulnerability privately instead of in an issue. [SECURITY.md](SECURITY.md) says how.
- If you want to and can, you can sponsor the project on [GitHub Sponsors](https://github.com/sponsors/TomXV). The framework is free and stays free either way.

## A note to the developers

This is an unofficial fan project and isn't affiliated with Gator Dragon Games. It doesn't contain any of the game's assets or code as they are (see [docs/CONTENT_POLICY.md](docs/CONTENT_POLICY.md)), and it doesn't modify the game's files, since BepInEx loads it at runtime.

If the development team has any concerns, please open an issue or contact the maintainer, and it'll be changed or taken down.

## Credits

- The **logo** above and the framework's **icon** on the Mods screen were drawn by **NotaGames** ([@NotaGames](https://github.com/NotaGames)) based on the game's own logo, and the game's developers said that's fine ([#15](https://github.com/TomXV/dragnwash-modframework/issues/15)). They're used with permission and aren't covered by the MIT license below.
- The **Mods button** in the Options screen (`ModsButton0.png`, `ModsButton1.png`) was drawn for the framework by **Mister ERIO** ([@mistererio](https://github.com/mistererio)) and is used with permission. It's their own artwork and doesn't come from the game, so it isn't covered by the MIT license below.

## License

[MIT](LICENSE), except the artwork named under [Credits](#credits).
