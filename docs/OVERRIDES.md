# Overrides: edits made in the Inspector, shipped as a mod

[日本語](OVERRIDES.ja.md)

> **Design, not built.** On the `experimental/overrides` branch. The research is done (see [Research before building](#research-before-building)); what it changed is marked **Change** there.

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

Done on 2026-09-19, with a small research mod (never released) that wrote every object's path 1, 5 and 20 s after each scene load, timed the root objects appearing and going, sampled the dragon's components, the lights and the rigidbodies 20 times over 5 s, nudged the values that held still by 0.1% and read them back 1 s later (then put them back), and ran the values through the Inspector's `Format` and `Parse`. Played: the title, PlayGame, the title again, PlayGame again (Direct3D 12, the 2026-09-14 game build). Plus a reading of the framework's code for the Mods screen and the History.

1. **Paths are stable.** The title scene gave the same 1,360 paths on both loads, PlayGame the same 1,675 (apart from short-lived effects such as `TemporaryVFX` and `ParticleSpline (Clone)`). Paths are nearly unique: none repeat in the title scene, 9 do in PlayGame (66 objects, mostly inside `DickApprox(Clone)` and a few in `map_prefab`), so the `"index"` for repeats is needed but rare. Not checked yet: across a game update (the texture census compares builds; do the same for paths when the next update comes).
2. **Objects made at run time.** The dragon (`DragonAlexander_1 (Clone)` here, 295 objects) and `DickApprox(Clone)` are there from the first frame of PlayGame, so scene load plus one retry reaches the first dragon. But each level spawns its own dragon, under its own name, when the level starts, long after the scene load. **Change:** besides the scene load, the library watches for new root objects (a cheap check of the scenes' root lists every half second) and applies the overrides whose path starts with that root. An override for `DragonRyanA (Clone)/...` then applies whenever that dragon comes.
3. **Values the game writes back: few.** Of 972 members sampled, 17 change by themselves, all of them motion (the Animator's root and body positions and velocities, a Rigidbody's velocity, position and centre of mass, a VFX's particle count), which are not things to override. Of the 58 values nudged, 57 stayed; one was put back by the game (the dragon's `AudioSource.volume`, which a script sets). The lights (32 values) and the dragon's scripts kept every change. Bones are the exception the Inspector already names: an Animator writes them every frame.
4. **Game scripts keep their settings in private fields.** The dragon's game scripts have only 38 public fields between them; what a player would want to change (speeds, strengths, thresholds) is mostly in private `[SerializeField]` fields, which the Inspector shows with **Show private**. **Change:** `"private": true` is not an edge case but the usual way to change a game script; the Mods screen still says a mod uses it.
5. **A mod with no DLL.** The Mods screen builds its list from BepInEx's loaded plugins, the plugin DLLs it finds in the folder, and the preloader patchers, and switches a mod off by renaming its DLL at the next start. A data-only mod needs a fourth source in that list (the Overrides library registers the folders it found) and its own off switch: renaming `mod.json` to `mod.json.disabled`, the same way, so it is off from the next start and the library does not read it. Uninstalling works as for any mod folder.
6. **Round trips.** Through the Inspector's `Format` and `Parse`, integers, booleans, enums, Vector2, Vector4 and Color32 come back exact; floats (16 of 149), Vector3 (18 of 26), Quaternion (6 of 8) and Color (8 of 8) come back close but not equal, because `Format` shows three decimals and a rotation as Euler angles. Written losslessly (floats with `R`, a rotation as x, y, z, w) all of them came back exact. **Change:** the files use a lossless form of their own (`Serialize` / `Deserialize` in the library), not the text a row shows; `Parse` still reads what a person types by hand.
7. **The History.** An entry keeps a label for people and a key made of an instance id, which is gone after the game restarts. **Change:** each entry also records the scene, the path, the component type and its index among components of that type, the member and whether it is private, when the Inspector makes it.

A side finding: every PlayGame load leaves about 24 `JigglePhysicsDummyTransform` objects in DontDestroyOnLoad that are never removed (a small leak in the game's jiggle physics, not in the framework).

## Order of work

1. Research notes (above): done.
2. The Overrides library: read files, register data-only mods, apply, take back, report.
3. The Inspector: record where each edit was made (scene, path, component, member; today the History keeps only a label and an instance id), and **Export as overrides** in the History view.
4. Docs: a page for people who have never made a mod: "change it in the Inspector, press Export, send the folder".
