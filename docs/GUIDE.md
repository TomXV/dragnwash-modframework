# Guide for mod authors

[日本語](GUIDE.ja.md)

How to build a Drag'n Wash mod on the framework so that it runs safely next to every other mod. The first principle of the framework is that **every mod runs safely together**; this guide is what that means for your code.

## Set up

Reference the framework DLLs you use (build them from this repository, or take them from a release) and declare each one as a dependency, so BepInEx loads them first and tells players when one is missing:

```csharp
[BepInPlugin("com.example.mymod", "My Mod", "1.0.0")]
[BepInDependency(ModFramework.Guid, BepInDependency.DependencyFlags.HardDependency)]
[BepInDependency(GameText.Guid, BepInDependency.DependencyFlags.HardDependency)]
public class MyMod : BaseUnityPlugin
{
    private void Awake()
    {
        ModFramework.Register(new ModInfo
        {
            Guid = "com.example.mymod",
            DisplayName = "My Mod",
            Description = "One or two sentences on what it does.",
            Authors = new[] { "Me" },
            Website = "https://github.com/me/mymod",
            UpdateRepository = "me/mymod",
            IconPath = Path.Combine(Path.GetDirectoryName(Info.Location), "icon.png"),
        });
    }
}
```

With `UpdateRepository` (core 1.1.0 and later) the Mods screen tells players when your GitHub repository has a newer release than the version they run, and opens its page. Tag releases with the plugin's version, like `v1.2.0` or `1.2.0`, and mark test builds as pre-releases: they are never offered. Leave it out if you do not publish releases on GitHub.

A mod that needs a newer library than the one installed can say so with `[BepInDependency(GameText.Guid, "0.1.1")]`; the Mods screen shows the version next to the library's name.

## What to use

| You want to | Use | Instead of |
|---|---|---|
| Tell players what your mod is | `ModFramework.Register(ModInfo)` | nothing: plain plugins are listed too, but with less |
| Give players settings | BepInEx `Config.Bind` (the Mods screen builds a page), `GameOptions.AddChoice` / `AddToggle` for the game's own Options screen | your own settings menu |
| Order your settings, hide the advanced ones, name them, say a restart is needed | a `SettingMeta` / `SectionMeta` in the `ConfigDescription` tags (experimental) | nothing: the page then lists them by section and key |
| Change text before it is shown | `GameText.AddRewriter` (library **Text**) | patching `TMP_Text.text` or `SetText` |
| Know which line of dialogue is shown, and who says it | `GameDialogue.LineShowing`, `OptionShowing`, `TryGetLine` (library **Dialogue**) | patching Yarn Spinner's presenters |
| Find your data for a line after a game update edited it | `LineKey`, `LineResolver` (library **Dialogue**, experimental; [STABLE_LINE_KEYS.md](STABLE_LINE_KEYS.md)) | keying by the English text alone |
| Add a debug or developer tool | `ToolWindow.AddTab` (library **Tool window**) | your own `OnGUI` window, cursor unlocking or input blocking |
| Give people a command to type, or watch the log in the game | `ToolWindow.AddCommand`, the **Console** tab (library **Tool window**, experimental; [CONSOLE.md](CONSOLE.md)) | your own console |
| Show text in scripts the game's fonts lack | `GameFonts.Prepare` and `GameFonts.SetLanguage` (library **Assets**) | adding to `TMP_Settings.fallbackFontAssets` yourself |
| Load a texture or an asset bundle | `GameAssets.LoadTexture`, `GameAssets.LoadBundle` (library **Assets**), from `Awake` | loading while the game runs |
| Replace a game texture with your own, or see what is loaded | `assets/textures/<name>.png` in your mod folder, `AssetCatalog` (library **Assets**, experimental; [ASSET_TOOL.md](ASSET_TOOL.md)) | swapping textures in materials yourself |
| Read or change save slots and flags | `GameSaves`, `GameFlags` (library **Flags and saves**) | writing `savegame.dgn` yourself |
| Share an API with other mods | `Services.Register<T>` and `Services.Get<T>` | public static fields another mod has to find by reflection |
| Check that a game method you patch still exists | `GameHooks.Require` | patching and hoping |
| See what an object, component or material holds, and try a value | the **Inspector** tab (F1, its own library) and `Inspector.Inspect(target)` (experimental) | a decompiler and a rebuild per guess |
| Run something when a scene loads, when the game has started or when it quits | `GameEvents.OnSceneLoaded`, `OnGameStarted`, `OnQuitting` (experimental) | `SceneManager.sceneLoaded` and `Application.quitting` yourself |

## Rules

1. **Do not patch what the framework already hooks.** Text, dialogue presenters, the Options screen's settings list, `GUI.Button` and the game's cursor lock all have one shared hook. A second patch on the same method changes what every other mod sees. The Mods screen marks mods that patch the same game method as a **Conflict**.
2. **Check before you patch.** When you do need a game method of your own, look it up first and call `GameHooks.Require(yourGuid, "Feature name", method != null, "Type.Method")`. If it is gone after a game update, skip that feature; the Mods screen shows it as unavailable instead of the game crashing. `Require` also takes Harmony's `"Type:Method"` in one string - `GameHooks.Require(yourGuid, "Feature name", "Yarn.Unity.LinePresenter:RunLine")` - which is what the Inspector's Code view copies, so a name found in the game pastes straight into the check. Its **Patch** button copies the whole patch: the check, the `[HarmonyPatch]` attribute (with the parameter types where the name is ambiguous) and a Prefix and Postfix with the parameters Harmony fills in.
3. **Never throw into the game.** Wrap Harmony patches in `try`/`catch` and log. Framework callbacks (rewriters, dialogue events, tool window tabs, option callbacks) are already isolated: an exception is logged with your mod's GUID and the other mods keep running, but your feature stops.
4. **Load textures, fonts and bundles at startup.** On Direct3D 12 with this game's Unity version, creating or uploading a texture while the game runs can crash it (Unity UUM-140564). Do it in `Awake`. In a tool window tab, call `ToolWindow.PrepareCharacters` for every non-ASCII character you draw, from `Awake` or `Update`.
5. **Do not replace another mod's service.** `Services.Register` refuses a second provider for the same interface. Ask for the service with `Services.TryGet<T>(out var service, minimumVersion)` and handle it being absent.
6. **Keep game types out of your public API.** If your mod is a library for other mods, expose your own types, so a game update changes your internals and not every mod built on you.
7. **Do not ship the game's data unchanged.** No assets, script text or game DLLs as they came from the game in your repository or releases. What you made by hand, or changed into something new, follows [docs/CONTENT_POLICY.md](CONTENT_POLICY.md).
8. **Keep developer features behind the developer-tools switch.** Exports, hot reload, debug keys and windows run only while `DeveloperTools.Enabled` is true (`DeveloperTools.WhenEnabled` for features that start later), so that someone who only installed a mod never sees them. Tool window tabs already are.
9. **Take the game's events from `GameEvents`.** A handler on `SceneManager.sceneLoaded` that throws stops every mod that subscribed after it, and nobody can tell which mod it was. `GameEvents.OnSceneLoaded(yourGuid, ...)` runs each mod's handler on its own, names the mod on the Mods screen when it fails, and switches a handler off after three failures in a row.

10. **Say so before you rely on reloading.** A mod is reloaded while the game runs only when it sets `ModInfo.Reloadable = true` (or carries `[ReloadableMod]`), and then it promises the points under [Reloading your mod while the game runs](#reloading-your-mod-while-the-game-runs).
11. **Say so before you go online.** List every host your mod connects to in `ModInfo.Network`, with what for, what is sent and how to turn it off; the Mods screen shows it to players. Send anything about the player (a name, a save, what they typed, an ID that follows them) only after they turn it on. A mod that connects without saying so is marked on the Mods screen. See [docs/NETWORK.md](NETWORK.md).

## Reloading your mod while the game runs

Experimental, core 1.2.0, and only while developer tools are on: build, and the new DLL takes the place of the running one without a restart (`[Developer] WatchMods`, or `mods reload <guid>` in the Console). Libraries are never reloaded. What your mod promises when it says it is reloadable:

- Its Harmony ID is its GUID: `new Harmony(MyMod.Guid)`. That is how its patches are found and removed.
- It registers through the framework (`ModFramework.Register`, `AddTab`, `AddCommand`, `AddRewriter`, `GameEvents`, `Services`, `GameOptions`) rather than through static fields of its own; what the framework does not know about, it cannot take out. Handlers on the libraries' events are taken out by assembly.
- Anything it handed to the game (a coroutine, a `DontDestroyOnLoad` object, a file watcher) is cleaned up in `OnDestroy`.
- Nothing in `Awake` that is unsafe mid-game on Direct3D 12 (a texture upload), or it is gated with `GameFonts.RuntimeUploadsAreSafe`.

Deliver the build as `<Mod>.dll.new` next to the installed DLL, and the framework does the rest. On Windows the running DLL is locked by Mono, so it cannot be overwritten; the game reloads from the `.new` file at once, and the preloader patcher makes it the real DLL at the next launch.

```xml
<!-- In the .csproj: after a build, the DLL goes to the game as .dll.new, and the running game reloads it. -->
<PropertyGroup>
  <GameDir>C:\Program Files (x86)\Steam\steamapps\common\Drag'n Wash</GameDir>
</PropertyGroup>
<Target Name="CopyToGame" AfterTargets="Build" Condition="Exists('$(GameDir)')">
  <Copy SourceFiles="$(TargetPath)" DestinationFiles="$(GameDir)\BepInEx\plugins\$(AssemblyName)\$(AssemblyName).dll.new" />
</Target>
```

What stays: the old assembly (Mono never unloads one; a few hundred kilobytes per reload), and any object of the old build the game still holds. A reload that fails before the old build is taken down leaves it running; one that fails after says so in the log and needs a restart. Details in [MOD_RELOAD.md](MOD_RELOAD.md).

## Writing a library

A library is an ordinary BepInEx plugin that other mods depend on. To make one:

- Depend on the core, and register with `IsLibrary = true`, so the Mods screen shows it as a library, lists the mods that need it, and asks before it is switched off.
- Give it its own GUID and version, and follow semantic versioning: a public API that changes in an incompatible way needs a new major version (or a new minor version while you are at 0.x, with the change written down).
- Hook the game once, at `Priority.First` when the order matters, and let mods register into your hook with an explicit order.
- Check every patch target with `GameHooks.Require`, and expose an `IsAvailable` flag so mods can tell.
- Run each mod's callback on its own: catch its exception, log it with the mod's GUID, and carry on with the next.
- Keep anything that touches game types `internal`.

## Testing

- Test with only your mod and the framework, then next to other mods. The **Conflict** tag on the Mods screen and the line "Game methods patched by more than one mod" in `BepInEx/LogOutput.log` tell you when you share a game method with another mod.
- Test on Direct3D 12 (the default on Windows) and, if you can, on the Steam Deck.
- Switch your mod off from the Mods screen and restart: the game should run without it.
