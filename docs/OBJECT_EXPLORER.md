# Object explorer (design)

[日本語](OBJECT_EXPLORER.ja.md)

> **Design only, not built.** Written 2026-09-18 on the `experimental/object-explorer` branch. A part of the **Inspector** library ([INSPECTOR.md](INSPECTOR.md)), so it is experimental too, and a developer-tools feature (GUIDE rule 8).

The Inspector shows what is **in the scenes**: the hierarchy, its objects and their components. A game holds much more than that: textures, materials, meshes, shaders, sounds, animations, fonts, and the game's own data in ScriptableObjects. Most of it hangs off some object in a scene, but there is no place to look at it as a whole. The object explorer is that place: every loaded object, sorted by kind, like the Project window in Unity's editor, but for what is loaded in the running game.

## What it is for, and what it is not

- **Find out.** Which textures and sounds the game has loaded, what a ScriptableObject holding the game's data contains, which shader a material uses, and **where each thing is used**.
- **Try.** The same editing as the Inspector's rows, on these objects too. Nothing is saved.
- **Not an exporter.** Writing an asset to a file is step 3 of the [asset tool](ASSET_TOOL.md), and stays there.
- **Not a replacer.** Material overrides that reach players belong in the Assets library, as a later design next to texture replacements.
- No creating, duplicating or destroying objects, no loading asset bundles, no playing a sound in the first version.

## The tab

The Inspector's toolbar gets a switch: **Scene | Objects**. **Scene** is the tab as it is today. **Objects** puts the explorer in the left pane; the right side stays the same members pane, so everything that pane does (rows, editing, Reset, History, Code) works here too.

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
| Animation | `AnimationClip`, `RuntimeAnimatorController` |
| Fonts | `Font`, `TMP_FontAsset` |
| Data | `ScriptableObject`, with a sub-folder per type (the game's own types first) |
| Objects outside the scenes | `GameObject`s that no loaded scene holds (prefabs and templates the game keeps loaded) |
| Other | everything else, by type |

- A folder opens to its objects: name, and one short fact per kind (a texture's size, a mesh's vertex count, a clip's length).
- The **Search** box filters the open folders by name, as it filters the hierarchy in Scene. A type filter (`t:Material`, as in Unity's editor) limits the search to one kind.
- Unnamed objects are listed as `(no name)` with their type.
- Objects Unity hides (`HideFlags.HideAndDontSave` and similar) are listed only with a **Show hidden** toggle on, off by default.
- Rows are drawn only for the part of the list on screen, so a folder with tens of thousands of objects scrolls like a short one.

### Right pane: the selected object

The members pane as it is, for any object: public fields and properties, private ones behind **Show private**, the same controls and the same rule for errors. On top, a short header per kind:

| Kind | Header |
|---|---|
| Texture, Sprite | a preview of the existing texture, size, format, whether the CPU can read it, mipmaps; **Go** to the Assets tab, which shows a replacement a mod ships for it |
| Material | what the Inspector already shows for a material: the shader's properties and keywords, and how many renderers share it |
| Shader | its properties and keywords, and how many materials use it |
| Mesh | vertex, triangle and sub-mesh counts, bounds, whether it is readable |
| AudioClip | length, channels, frequency, load state |
| AnimationClip | length, frame rate, events |
| ScriptableObject | nothing extra: its fields are the point, and they are rows like a component's |
| GameObject outside the scenes | its components, read-only (see [Editing](#editing)) |

### Used by

A **Used by** button on the header lists where the selected object is used. It runs on the button, never on its own, because it walks many objects:

| Selected | Looked for in |
|---|---|
| Material | renderers' `sharedMaterials`, UI `Graphic.material` |
| Texture | material texture properties, sprites, UI `RawImage` |
| Sprite | `SpriteRenderer`, UI `Image` |
| Shader | materials |
| Mesh | `MeshFilter`, `SkinnedMeshRenderer`, `MeshCollider` (read through reflection, as the collider shapes are) |
| AudioClip | `AudioSource.clip` |
| AnimationClip | `RuntimeAnimatorController.animationClips`, `Animation` |
| Font | `TMP_Text.font`, UI `Text.font` |
| ScriptableObject, anything else | fields of the scenes' components that hold it (reflection over the fields of each component type, cached per type; capped at a few thousand hits) |

Each hit is a row. Clicking it switches to **Scene** with that object selected, or selects the material or asset in Objects. The list says when it stopped at the cap.

### Getting there from Scene, and back

- A `UnityEngine.Object` row in the members pane (a texture, a mesh, a clip, a ScriptableObject) gets **Go** for every kind, not only the ones the Inspector knows today. Go on something outside the scenes opens it in Objects.
- `Inspector.Inspect(UnityEngine.Object)` accepts any object and opens the right view.
- A texture keeps its Go to the Assets tab; the Assets tab's **Inspect** on a texture opens it in Objects.

## Editing

The same rows and the same rules as in Scene, with two additions:

- **Shared objects.** An asset is usually used by many objects: changing a material, a texture's filter mode or a ScriptableObject's value changes every place that uses it. The header says how many users it has once **Used by** has run, and the first edit of a shared asset in a session says so once.
- **Objects outside the scenes are read-only.** Changing a prefab the game keeps loaded changes everything the game makes from it later, which is hard to see and hard to undo. Their rows are shown, not edited. If a real need shows up, it becomes a toggle with its own warning.

Edits join the **History** view, with Revert and Redo, as Scene's edits do. Nothing is written to a file.

## Building the list

- One pass over `Resources.FindObjectsOfTypeAll<UnityEngine.Object>()`, sorted into folders. It runs when Objects is first opened, on **Refresh**, and on a type filter the list does not have yet. Never every frame.
- **The list must not keep assets alive.** Unity's `Resources.UnloadUnusedAssets` does not unload an asset that managed code still holds, so a list of references would keep a scene's assets in memory after the game has moved on. The list keeps instance IDs, names and the short facts. It looks an object up again only when it is selected or drawn in a preview. Implementation checks how the object is found by its ID in this Unity version (Unity 6.3). If no public way exists, the list holds references only while Objects is open, drops them when the tab closes or a scene unloads, and says the list is out of date until **Refresh**.
- A scene load marks the list out of date (the same event the hierarchy uses); it is not rebuilt until it is looked at.
- A selected object that Unity destroyed clears the selection with a note, as in Scene.

## Safety

- **Developer tools only**, like the rest of the Inspector.
- **No uploads.** A preview draws a texture the game has already loaded; nothing is created, read back or uploaded, so Direct3D 12 is not a concern. A texture the GPU alone holds is drawn as it is, never read.
- **Names through the window's guard**, as everywhere in the Tool window.
- **Failures stay on their row.** A property getter that throws (some Unity objects throw on properties that do not apply to them) is shown on its row; the list goes on.
- **Cost on demand only.** The list, Used by and previews run on a press or for the selected object. Playing the game with Objects closed costs nothing.

## Console

- `objects` prints the folders with their counts; `objects <kind> [filter]` lists matching objects (`objects Material counter`).
- `inspect object <kind> <name>` selects an object in Objects.
- `objects usedby <kind> <name>` prints the Used by list.

## Order of work

1. Scene | Objects switch; the list by kind with counts, search and `t:`; selection into the existing members pane (read-only at first); Go from Scene rows for every kind; `Inspector.Inspect` for any object.
2. Headers per kind, with texture and sprite previews.
3. Used by.
4. Editing with the shared-object note; History.
5. Console commands.
6. Show hidden; ScriptableObject sub-folders by type.

Later, and not in this design: sound playback, exporting (asset tool step 3), material overrides that ship with a mod (Assets library).
