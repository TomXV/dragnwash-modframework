# Overrides: edits made in the Inspector, shipped as a mod

[日本語](OVERRIDES.ja.md)

> **Design, not built.** On the `experimental/overrides` branch. Research comes first (see [Research before building](#research-before-building)); what is here may change with what it finds.

The Inspector lets anyone change a value in the running game and see it at once, and forgets it all when the game quits ([INSPECTOR.md](INSPECTOR.md#what-it-is-for-and-what-it-is-not) leaves saving to "a later overrides design"). This is that design: **the edits in the History become a file, and the file becomes a mod that needs no code.** Someone who has never written C# makes a heavier dragon, a warmer light or a slower sponge, and hands it to a friend.

It is the Toolsmith idea in one feature: the framework makes the tool, and the person using it decides what to make.

## In one picture

```
Inspector (developer tools on)         Overrides library (always, for players)
  edit values  ->  History              plugins/<Mod>/overrides/*.json
  [Export as overrides]  ----------->   read at startup
                                        applied when a scene loads
                                        taken back when switched off
```

## Two parts, two places

- **Writing** belongs to the Inspector: the History already knows what was changed, before and after. Export is a button there, and a few rows of Tool window UI.
- **Applying** belongs to a new small library, **Overrides**, not to the Inspector. Players need it without developer tools, and mods may leave the Inspector out of their releases. It is not put in the Assets library either (as INSPECTOR.md first thought): Assets is about files that take the place of the game's files; this is about values on objects in a scene. Each library keeps one job.

## What an override is

One line of a file: where, what, and the value, written the way the Inspector's rows write it, so it can be read and changed by hand.

```json
{
  "format": 1,
  "overrides": [
    { "scene": "Wash", "path": "Dragon/Body", "component": "Rigidbody", "member": "mass", "value": "2.5" },
    { "scene": "Wash", "path": "Lights/Key", "component": "Light", "member": "color", "value": "#FFD9A8FF" },
    { "scene": "Wash", "path": "Dragon/Body", "material": "DragonSkin", "property": "_Smoothness", "value": "0.8" }
  ]
}
```

- **Where**: the scene, the object's path (as `inspect` in the Console writes it), and the component by type name. When an object has two components of the same type, `"index"` says which.
- **What**: a public field or property; a material property through the renderer that shows it. A private member only with `"private": true`, which the Mods screen shows.
- **The value**: text, parsed with the same `InspectorModel.Parse` the rows use, so numbers, booleans, text, enums, vectors, colours, rects and quaternions all work. **No object references** (a texture, a mesh, another object): swapping assets is the Assets library's job, and a reference cannot be written as a value anyway.
- **Not in the first version**: mesh vertex edits (they are data, not a value; they belong to export and import in the Asset tool), list elements, adding or removing components.

## The mod a player gets

Export writes a folder that is a complete mod with no DLL:

```
BepInEx/plugins/Heavier Dragon/
  mod.json              name, author, description, version
  overrides/main.json
```

The Overrides library finds `plugins/*/overrides/`, reads `mod.json` and registers each folder with the framework as its own mod (`ModInfo`), so it appears on the Mods screen with its name and author and can be switched off there, like any other mod. Nothing is compiled, and there is nothing to run.

## When it is applied, and taken back

- When a scene loads (`GameEvents.OnSceneLoaded`), and once more a moment later for objects the game creates just after loading.
- Before the first write, the value the game had is kept. Switching the mod off puts every value back, as texture replacements are taken back.
- A target that is not found is not an error: the Mods screen says "3 of 20 overrides found nothing", with the paths in the log. A game update that renames an object costs one override, never the game.
- The game's own scripts may write the same value again every frame. The first version does not fight them; research says how often that happens, and whether "keep applying" is ever needed.

## Mods together

The yardstick is that every mod runs safely together.

- Two mods overriding the same member of the same object: the later one in load order wins, and **both** are named on the Mods screen (the planned per-mod notice), so nobody wonders why their edit did nothing.
- An override never removes anything, calls no methods and loads no code.

## Content policy

An override file holds values people changed by hand, and the names needed to find the object (paths, component and member names). That fits the [content policy](CONTENT_POLICY.md): made or changed by hand is fine; game data copied unchanged is not. Export writes only what the History recorded as changed, never a dump of an object's values.

## Research before building

Before any code, a short study of the game, written down as notes:

1. **Are paths stable?** The same object's path across loads, scenes and the last game update (the object census from the texture work already has most of this).
2. **Objects made at run time.** How many of the things people will want to change are created after loading (`(Clone)` in the name), and whether scene load plus one retry reaches them.
3. **Values the game writes back.** Which members people edit in the Inspector are set again by the game's scripts (the mass-spring controller, the lights), so an override would not stick.
4. **A mod with no DLL.** What the Mods screen needs to show a folder as a mod, and how that sits with BepInEx's own plugin list.
5. **Round trips.** Every supported type written with `Format` and read back with `Parse` gives the same value.

## Order of work

1. Research notes (above).
2. The Overrides library: read files, register data-only mods, apply, take back, report.
3. The Inspector: record where each edit was made (scene, path, component, member; today the History keeps only a label and an instance id), and **Export as overrides** in the History view.
4. Docs: a page for people who have never made a mod: "change it in the Inspector, press Export, send the folder".
