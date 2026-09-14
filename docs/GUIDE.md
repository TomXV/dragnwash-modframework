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
| Change text before it is shown | `GameText.AddRewriter` (library **Text**) | patching `TMP_Text.text` or `SetText` |
| Know which line of dialogue is shown, and who says it | `GameDialogue.LineShowing`, `OptionShowing`, `TryGetLine` (library **Dialogue**) | patching Yarn Spinner's presenters |
| Add a debug or developer tool | `ToolWindow.AddTab` (library **Tool window**) | your own `OnGUI` window, cursor unlocking or input blocking |
| Show text in scripts the game's fonts lack | `GameFonts.Prepare` and `GameFonts.SetLanguage` (library **Assets**) | adding to `TMP_Settings.fallbackFontAssets` yourself |
| Load a texture or an asset bundle | `GameAssets.LoadTexture`, `GameAssets.LoadBundle` (library **Assets**), from `Awake` | loading while the game runs |
| Read or change save slots and flags | `GameSaves`, `GameFlags` (library **Flags and saves**) | writing `savegame.dgn` yourself |
| Share an API with other mods | `Services.Register<T>` and `Services.Get<T>` | public static fields another mod has to find by reflection |
| Check that a game method you patch still exists | `GameHooks.Require` | patching and hoping |

## Rules

1. **Do not patch what the framework already hooks.** Text, dialogue presenters, the Options screen's settings list, `GUI.Button` and the game's cursor lock all have one shared hook. A second patch on the same method changes what every other mod sees. The Mods screen marks mods that patch the same game method as a **Conflict**.
2. **Check before you patch.** When you do need a game method of your own, look it up first and call `GameHooks.Require(yourGuid, "Feature name", method != null, "Type.Method")`. If it is gone after a game update, skip that feature; the Mods screen shows it as unavailable instead of the game crashing.
3. **Never throw into the game.** Wrap Harmony patches in `try`/`catch` and log. Framework callbacks (rewriters, dialogue events, tool window tabs, option callbacks) are already isolated: an exception is logged with your mod's GUID and the other mods keep running, but your feature stops.
4. **Load textures, fonts and bundles at startup.** On Direct3D 12 with this game's Unity version, creating or uploading a texture while the game runs can crash it (Unity UUM-140564). Do it in `Awake`. In a tool window tab, call `ToolWindow.PrepareCharacters` for every non-ASCII character you draw, from `Awake` or `Update`.
5. **Do not replace another mod's service.** `Services.Register` refuses a second provider for the same interface. Ask for the service with `Services.TryGet<T>(out var service, minimumVersion)` and handle it being absent.
6. **Keep game types out of your public API.** If your mod is a library for other mods, expose your own types, so a game update changes your internals and not every mod built on you.
7. **Do not ship the game's files.** No assets, script text or game DLLs in your repository or releases.

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
