<p align="center"><picture><source media="(prefers-color-scheme: dark)" srcset="images/logo-white.svg"><img src="images/logo.svg" alt="Drag'n Wash ModFramework" width="320"></picture></p>

# Drag'n Wash ModFramework

[日本語](README.ja.md)

A prerequisite mod for [Drag'n Wash](https://store.steampowered.com/app/4739660/) (BepInEx 5). It is a small core that keeps the code hooking into the game in one place and gives other mods, and libraries built on top of it, a stable API: a Mods screen reached from the game's Options screen (like Minecraft Forge's mod list, with on/off switches), settings in the game's Options screen, text and dialogue events, safe asset loading on Direct3D 12, and more. When the game updates, only the framework has to follow.

> [!NOTE]
> **Core 1.2.0** on `main`, not yet released; the latest release is 1.1.2 with the libraries at 1.0.0. 1.2.0 adds the developer-tools switch (off by default), text and key-binding fields on the settings pages, [`GameEvents`](docs/GUIDE.md) and `SettingMeta`; with it the Tool window, Assets and Dialogue libraries go to 1.1.0 ([Console](docs/CONSOLE.md), [texture replacements and reloading](docs/ASSET_TOOL.md), [stable line keys](docs/STABLE_LINE_KEYS.md)) and a new [Inspector](docs/INSPECTOR.md) library (1.0.0) arrives, all experimental until released; the Inspector stays experimental after that too. 1.1.0 added [update notices](#update-notices), the shared installer and uninstalling from the Mods screen; 1.1.1 added the framework's icon, and 1.1.2 replaces it with the "Dg" monogram from the hand-made logo. 1.0.0 was released together with [Drag'n Wash Localization](https://github.com/TomXV/dragnwash-localization) v1.0.0, the first mod built on it. From 1.0.0 on, a change that breaks the public API comes only with a new major version; see [CHANGELOG.md](CHANGELOG.md).

See [docs/DESIGN.md](docs/DESIGN.md) for goals and the order of work, [docs/GUIDE.md](docs/GUIDE.md) for how to build a mod on it, and [docs/GAME_BUILDS.md](docs/GAME_BUILDS.md) for the game builds it was checked on. The [wiki](https://github.com/TomXV/dragnwash-modframework/wiki) has a page for players, a getting-started walkthrough and a reference page for each library.

## What is in it

| Plugin | GUID | What mods get |
|---|---|---|
| **Drag'n Wash ModFramework** (core) | `com.tomxv.dragnwash.modframework` | The Mods screen with on/off switches, settings pages and icons (`ModFramework.Register`), rows in the game's Options screen (`GameOptions`), a service registry (`Services`), health checks (`GameHooks`), `GameInfo` |
| **Text** | `com.tomxv.dragnwash.modframework.text` | See and replace every text before the game shows it (`GameText`) |
| **Dialogue** | `com.tomxv.dragnwash.modframework.dialogue` | The line of dialogue or option about to be shown, with line ID, speaker and node (`GameDialogue`) |
| **Tool window** | `com.tomxv.dragnwash.modframework.toolwindow` | One shared F1 window for developer tools, off until **Developer tools** is turned on in Options → Mods, where each mod adds tabs (`ToolWindow`) |
| **Assets** | `com.tomxv.dragnwash.modframework.assets` | Fonts for any language and texture and asset bundle loading, without the Direct3D 12 crash (`GameFonts`, `GameAssets`) |
| **Flags and saves** | `com.tomxv.dragnwash.modframework.saves` | Save slots, flags and a history of every save (`GameSaves`, `GameFlags`) |

Each library is its own plugin with its own version; install the ones the mods you use need. See [CHANGELOG.md](CHANGELOG.md) for versions.

### Update notices

From core 1.1.0, the framework tells you on the Mods screen and the title screen when a mod you have installed has a newer release. Only mods that name their GitHub repository are checked, each at most once a day. The framework asks GitHub's public API (`api.github.com`) for the repository's latest release and sends nothing about you, your game or your other mods; GitHub sees your IP address, as with any web page. Nothing is downloaded or installed: the Mods screen opens the release page for you. To switch it off, open **Options → Mods → Drag'n Wash ModFramework → Settings** and set **Check for updates** to Off, or set `Check for updates = false` in `BepInEx/config/com.tomxv.dragnwash.modframework.cfg`.

## For mod developers

Reference `DragNWash.ModFramework.dll` (and the library DLLs you use) and declare each dependency so BepInEx loads them first. [docs/GUIDE.md](docs/GUIDE.md) has what to use for what, and the rules that keep mods working together. To give players a one-click install, ship the shared installer with a `mod-install.json`: [docs/INSTALLER.md](docs/INSTALLER.md).

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

Each DLL goes to its project's `bin/Release/`. To try them, copy each plugin DLL to its own folder, `<Game>/BepInEx/plugins/<assembly name>/`, and `DragNWash.ModFramework.Preloader.dll` to `<Game>/BepInEx/patchers/`.

### Building on GitHub

The **Build** workflow (Actions) builds the release zip on GitHub: on every push to `main`, on a `v*` tag, or by hand. It fetches the reference assemblies from a private repository (`TomXV/dragnwash-libs`, never public) with the `LIBS_TOKEN` secret, runs `tools/pack.ps1` on a Windows runner and uploads `release/DragNWash.ModFramework-<version>.zip` as a workflow artifact. A tag also creates a **draft** release with the zip attached; a person writes the notes and publishes it. The workflow never runs for pull requests, so a fork cannot reach the token. After a game update, refresh the private repository from a game install with `tools/copy-libs.ps1`.

## Rules for this repository

- Never commit the game's files, BepInEx binaries or anything from `libs/`. A check on every push and pull request enforces it.
- Code that touches game classes stays `internal`; mods only see the framework's own types.

## A note to the developers

This is an unofficial fan project and is not affiliated with Gator Dragon Games. It contains no game assets or code and does not modify the game's files (BepInEx loads it at runtime). If the development team has any concerns, please open an issue or contact the maintainer, and it will be changed or taken down.

## Credits

- The **Mods button** in the Options screen (`ModsButton0.png`, `ModsButton1.png`) was drawn for the framework by **Mister ERIO** ([@mistererio](https://github.com/mistererio)) and is used with permission. It is their artwork, not the game's, and is not covered by the MIT license below.

## License

[MIT](LICENSE), except the artwork named under [Credits](#credits).
