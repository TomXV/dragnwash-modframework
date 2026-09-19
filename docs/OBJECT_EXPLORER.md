# Object explorer (design)

[日本語](OBJECT_EXPLORER.ja.md)

> **Built, experimental, not merged.** Designed 2026-09-18 on the `experimental/object-explorer` branch; brought up to date with today's Inspector and built 2026-09-20 on `experimental/object-explorer-build` (all six steps of [Order of work](#order-of-work)). Built and checked outside the game only: it still needs testing in the game before it is merged. A part of the **Inspector** library ([Inspector (wiki)](https://github.com/TomXV/dragnwash-modframework/wiki/Inspector)), so it is experimental too, and a developer-tools feature ([Playing well with others (wiki)](https://github.com/TomXV/dragnwash-modframework/wiki/Playing-well-with-others)).

The Inspector shows what is **in the scenes**: the hierarchy, its objects and their components. A game holds much more than that: textures, materials, meshes, shaders, sounds, animations, fonts, and the game's own data in ScriptableObjects. Most of it hangs off some object in a scene, but there is no place to look at it as a whole. The object explorer is that place: every loaded object, sorted by kind, like the Project window in Unity's editor, but for what is loaded in the running game.

## What it is for, and what it is not

- **Find out.** Which textures and sounds the game has loaded, what a ScriptableObject holding the game's data contains, which shader a material uses, and **where each thing is used**.
- **Try.** The same editing as the Inspector's rows, on these objects too. Nothing is saved.
- **Not an exporter.** Writing an asset to a file is step 3 of the asset tool ([Assets (wiki)](https://github.com/TomXV/dragnwash-modframework/wiki/Assets)), and stays there.
- **Not a replacer.** Material overrides that reach players belong in the Assets library, as a later design next to texture replacements.
- No creating, duplicating or destroying objects, no loading asset bundles, no playing a sound in the first version.

## The tab

The Inspector's toolbar starts with a switch: **Scene | Objects**. **Scene** is the tab as it was. **Objects** puts the explorer in the left pane; the right side stays the same members pane, so everything that pane does (rows, editing, Reset, History, Code) works here too, and the views that stand in its place (History, the Rigidbodies list, Scenes and levels) do as well.

- In Objects, the toolbar's **Tree** button reads **List** and shows or hides the explorer (**T** does the same); **Parent** is left out. Pick, Edit, View, History and Refresh stay; **Refresh** lists the loaded objects again.
- Each view keeps its own search text and its own selection: switching back to Scene finds what was selected there.
- Where Scene shows the selection's path, Objects shows a status line: how many objects were listed, how long it took, and notes.

### Left pane: kinds and objects

Folders by kind, each with a count:

| Folder | What goes in |
|---|---|
| Textures | `Texture2D`, `RenderTexture`, `Cubemap` and other `Texture`s |
| Sprites | `Sprite` |
| Materials | `Material` |
| Shaders | `Shader` |
| Meshes | `Mesh` |
| Audio | `AudioClip` |
| Animation | `AnimationClip`, `RuntimeAnimatorController` (and override controllers) |
| Fonts | `Font`, `TMP_FontAsset` |
| Data | `ScriptableObject`, with a sub-folder per type; the game's own types first (the game's assemblies, as the code graph decides them) |
| Objects outside the scenes | every `GameObject` no loaded scene holds (prefabs and templates the game keeps loaded), by its path |
| Other | everything else, with a sub-folder per type |

- Components are not listed: a scene's are in Scene, and those of a GameObject outside the scenes are under it.
- A folder opens to its objects: name, and one short fact per kind (a texture's size and format, a mesh's vertex and sub-mesh counts, a clip's length, a material's shader, a GameObject's components and children). The fact is read when the row is first drawn and kept.
- The **Search** box filters by name, as it filters the hierarchy in Scene. A type filter (`t:Material`, as in Unity's editor) limits it to one type: the type's name, the name of a base type (`t:Texture` finds every texture), or a folder (`t:data`). While searching, only folders with a match are listed, each with "matches of total", and they open; one closed while searching stays closed until the search changes.
- **Close all** closes every folder.
- Unnamed objects are listed as `(no name)` with their type.
- Objects Unity hides (`HideFlags.HideInHierarchy` or `HideInInspector`, which `HideAndDontSave` includes) are listed only with **Show hidden** on, off by default. Selecting a hidden object from elsewhere turns it on.
- Rows are drawn only for the part of the list on screen, so a folder with tens of thousands of objects scrolls like a short one.

### Right pane: the selected object

The members pane as it is, for any object: public fields and properties, private ones behind **Show private**, the same controls and the same rule for errors. Array properties that Unity copies on every read (a mesh's `vertices`, `uv` and `triangles`, a clip's `events`, a controller's `animationClips`) are left out of the rows, since reading them each frame would copy them each frame; the header gives their counts instead.

On top, a header: the folder and name, then a few lines per kind, computed once when the object is selected:

| Kind | Header |
|---|---|
| Texture | size, format, dimension, whether the CPU can read it, mip levels, filter and wrap modes; a preview of a 2D texture; **Assets tab** (for a `Texture2D`) opens the Assets tab on it, which shows a replacement a mod ships for it |
| Sprite | its rectangle in its texture, pivot, pixels per unit, packed or not; a preview of its part of the texture; **Assets tab** on its texture |
| Material | its shader, and how many renderers in the scenes share it; its rows are the shader's properties and keywords, as in Scene |
| Shader | its properties (the first 24 named), its keywords, how many loaded materials use it, passes and render queue |
| Mesh | vertex, triangle, sub-mesh and blend shape counts, whether it is readable, bounds |
| AudioClip | length, channels, frequency, samples, load type and state (read by reflection: the audio module is not referenced) |
| AnimationClip | length, frame rate, loop, events (as the Animator's Clips view says); a controller: how many clips |
| Font | dynamic or not, size and line height; a TextMeshPro font has no extra lines |
| ScriptableObject | nothing extra: its fields are the point, and they are rows like a component's |
| GameObject outside the scenes | that it is read-only, its component and child counts, and its components as the strip Scene shows over the members, read-only (see [Editing](#editing)) |

Under the header: **Used by**, and **Assets tab** where it applies.

### Used by

**Used by** lists where the selected object is used, in place of the members (like History), with **Look again** and **< Members**. It runs on the button, never on its own, because it walks many objects. It looks in two kinds of places.

What Unity keeps in its own (native) components and assets, asked through their properties:

| Selected | Looked for in |
|---|---|
| Material | renderers' `sharedMaterials` |
| Texture | every material's texture properties, sprites' `texture` |
| Sprite | `SpriteRenderer.sprite` |
| Shader | materials' `shader` |
| Mesh | `MeshFilter.sharedMesh`, `SkinnedMeshRenderer.sharedMesh`, `MeshCollider.sharedMesh` (by reflection: the physics module is not referenced) |
| AudioClip | `AudioSource.clip` (by reflection) |
| AnimationClip | controllers' `animationClips`, the states of legacy `Animation` components (by reflection) |
| Animator controller | `Animator.runtimeAnimatorController` (by reflection) |

And, for every kind, what scripts keep in fields: every `MonoBehaviour` (in the scenes and outside them) and every `ScriptableObject` whose type has a field that can hold the object: the field itself, an array or `List<T>` of such, or a field of a `[Serializable]` class held in a field (one level: UI `Text` keeps its font so). The fields that can hold an object of a type are found once per holder type and kept. This is how UI `Image` (sprites), `RawImage` (textures), `Graphic` (materials), TextMeshPro text (fonts and materials), TextMeshPro font assets (their atlas textures and materials) and the game's own scripts and data are found; Unity's own UI keeps these in private fields (`m_Sprite`, `m_Material`), which is why fields are read rather than properties such as `Graphic.material` (which returns a default material when none is set).

- Each place is a row: where (a component's path and type, or an asset's folder and name; places outside the scenes are dimmed and say so) and the member (`sharedMaterials[2]`, `m_Sprite`, `m_FontData.m_Font`). **Go** selects it: a scene's component in **Scene**, an asset or a GameObject outside the scenes in **Objects**.
- It stops at 2,000 places and says so. The line over the list says how many objects it looked through and how long it took.
- After Used by, the header's button reads **Used by (N)**, and a shared object says "used in N places, and a change shows in all of them".
- An object only code uses (or nothing, at the moment) gets an empty list that says where it looked.

### Getting there from Scene, and back

- A `UnityEngine.Object` row in the members pane gets **Go** for every kind, not only the ones the Inspector knew. Go on a scene's object or component selects it in Scene; on anything else (a texture, a mesh, a clip, a ScriptableObject, a prefab) it opens Objects on it, with its folder open and scrolled to.
- A texture row keeps a way to the Assets tab: a second button, **Assets**, next to Go.
- `Inspector.Inspect(UnityEngine.Object)` accepts any object and opens the right view: Scene for a scene's object or component, the view that is open for a material, Objects for anything else.
- The Assets tab's **Inspect** on a texture opens the texture in Objects (before, it opened the first material using it, and only when one did). Its Used by lists the materials and sprites.

## Editing

The same rows and the same rules as in Scene, with two additions:

- **Shared objects.** An asset is usually used by many objects: changing a material, a texture's filter mode or a ScriptableObject's value changes every place that uses it. The header says how many places use it once **Used by** has run, and the first edit of a shared object in a session says so once, in the status line.
- **Objects outside the scenes are read-only**, and so are their components. Changing a prefab the game keeps loaded changes everything the game makes from it later, which is hard to see and hard to undo. Their rows are shown, not edited; the Enabled button and the Rigidbody and Animator buttons are left out for them. If a real need shows up, it becomes a toggle with its own warning.

Edits join the **History** view, with Revert and Redo, as Scene's edits do; they are labelled by folder and name (`Materials: Counter (Material)`). **Export as overrides** leaves them out with the reason: an override names a place in a scene, and an edit made in Objects is an edit of the asset. Nothing is written to a file.

## Building the list

- One pass over `Resources.FindObjectsOfTypeAll<UnityEngine.Object>()`, sorted into folders, then by type (in Data and Other) and name. It runs when Objects is first looked at, on **Refresh**, and when it is looked at again after a scene loaded or unloaded (the same events the hierarchy uses; Objects, the console's `objects` and the operations all look). Never every frame, and never while nothing looks. A type filter is only a filter over that list.
- **The list does not keep assets alive.** Unity's `Resources.UnloadUnusedAssets` does not unload an asset that managed code still holds, so a list of references would keep a scene's assets in memory after the game has moved on. The list keeps instance IDs, names, types and the short facts; the array from `FindObjectsOfTypeAll` is dropped at the end of the pass. An object is looked up again by its ID when it is selected, its row is first drawn or Used by names it. Unity 6.3 does this with `Resources.EntityIdToObject` (its `InstanceIDToObject` is obsolete there), and an instance ID converts to an `EntityId`, so no fallback is needed.
- The selection itself is a reference while it is selected, as in Scene.
- An object Unity destroyed since the list was made shows `(destroyed)` on its row; selecting it says so and suggests Refresh. A selected object that Unity destroyed clears the selection with a note, as in Scene. An object made after the list was is found by listing again once, when something selects it.

## Speed

The game has tens of thousands of loaded objects. What costs what:

- **The one pass** is mostly Unity's own `FindObjectsOfTypeAll` and reading each name. It is logged each time (`[objects] Listed N objects in X ms (FindObjectsOfTypeAll Y ms)`) and shown in the status line, so it is measured in the game on every run; it needs a number from the game (see the in-game test).
- **Sorting, searching and the rows**, measured outside the game with the list's own code (`InspectorObjectList.cs`, which has no Unity types) on made-up lists shaped like a game's (a fifth textures, 15% data in 50 types, names of 2 to 4 words, 1 in 20 hidden), .NET Framework 4.7.2, release build, median of 21 runs on the owner's development PC (Mono in the game is slower; count on two to three times):

| Objects | Sort (once per pass) | Rows, folders closed | Rows, 4 folders open | Name search | `t:Materials` | Search "a", hidden on |
|---:|---:|---:|---:|---:|---:|---:|
| 20,000 | 7 ms | 0.2 ms | 0.2 ms | 1.9 ms | 0.3 ms | 1.7 ms |
| 60,000 | 26 ms | 0.6 ms | 1.0 ms | 6.1 ms | 1.0 ms | 6.8 ms |
| 200,000 | 106 ms | 3.1 ms | 5.9 ms | 25 ms | 4.5 ms | 31 ms |

- The rows are made again only when the search, a folder, Show hidden or the list changes, not per frame; drawing touches only the rows on screen (about 30), and a row's fact is read once. So a keystroke in the search costs one row pass, and scrolling costs nothing more than a short list.
- A first version of the row pass looked up string keys per object and took 4 to 5 times as long with no search (60,000: 4.2 ms closed, 4.8 ms open); counting per folder and per type in place brought it down to the table above.
- **Used by** walks renderers, materials, sprites and the like for the kinds that need them, then every MonoBehaviour and ScriptableObject once, reading only the fields that can hold the selected type (found once per holder type). Its time is logged and shown over the list; it needs a number from the game too.
- The header, the facts and Used by are computed for the selected object or the rows on screen only. With Objects closed, nothing runs.

## Safety

- **Developer tools only**, like the rest of the Inspector.
- **No uploads.** A preview draws a texture the game has already loaded (`GUI.DrawTextureWithTexCoords`, as the Assets tab's preview does); nothing is created, read back or uploaded, so Direct3D 12 is not a concern. A texture the GPU alone holds is drawn as it is, never read.
- **Names through the window's guard**, as everywhere in the Tool window.
- **Failures stay on their row.** A property getter that throws (some Unity objects throw on properties that do not apply to them) is shown on its row; the list goes on. An object that fails while being listed is left out; a header line that fails says so.
- **Cost on demand only.** The list, Used by and previews run on a press or for the selected object. Playing the game with Objects closed costs nothing.

## Console

- `objects` prints the folders with their counts (and how many are hidden); `objects <kind> [filter]` lists matching objects, 200 at most (`objects materials counter`, `objects textures t:RenderTexture`).
- `inspect object <kind> <name>` selects an object in Objects, opens the window, and prints its header.
- `objects usedby <kind> <name>` prints the Used by list (200 places at most in the console).
- Kinds are the folder names or a short word: `textures`, `sprites`, `materials`, `shaders`, `meshes`, `audio`, `animation`, `fonts`, `data`, `outside`, `other` (and singulars such as `material`, `clip`, `prefab`). A name is the whole name, or a part only one object of that kind has. Completion offers kinds and names.

## Operations

Read operations in the registry ([API_PLAN.md](API_PLAN.md), stage 1), so the console's `op` and the Bridge's AI clients can ask the same questions:

| Operation | Parameters | Returns |
|---|---|---|
| `inspector.loaded.kinds` | | every folder with its count and hidden count, and how long listing took |
| `inspector.loaded.list` | `kind` (required), `filter` (a name part, `t:Type`), `hidden`, `max` (1 to 1000; 200) | `{ name, type, fact, id }` |
| `inspector.loaded.usedby` | `kind` (required), `name` or `id`, `max` (1 to 2000; 200) | `{ object, summary, places: { where, member, in_scene } }` |

`inspector.selection.get` also says what is selected in Objects (`{ view: "objects", object, kind, target }`). These give names, types, sizes and places, never an object's contents.

## Order of work

All six are built on `experimental/object-explorer-build`:

1. Scene | Objects switch; the list by kind with counts, search and `t:`; selection into the existing members pane; Go from Scene rows for every kind; `Inspector.Inspect` for any object.
2. Headers per kind, with texture and sprite previews.
3. Used by.
4. Editing with the shared-object note; History.
5. Console commands, and the read operations.
6. Show hidden; sub-folders by type for Data (and Other).

Later, and not in this design: sound playback, exporting (asset tool step 3), material overrides that ship with a mod (Assets library), a way to walk a prefab's children other than their paths in the list.
