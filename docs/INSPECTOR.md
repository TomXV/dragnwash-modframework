# Inspector (design)

[日本語](INSPECTOR.ja.md)

> **Experimental.** Built as its own library, `DragNWash.ModFramework.Inspector` (1.0.0), on top of the Tool window, released with framework 1.2.0. It stays **experimental after release**: it may change or go away, and an edit made with it can break the running session. The framework's release ships it; a mod's release (Drag'n Wash Localization's, for one) can ship the Tool window without it. Beyond the design, from use: **Pick** (click an object in the game to select it; uGUI elements first, then the renderer whose screen bounds are smallest around the pointer, so no collider is needed), **Highlight** (an outline around the selected object in the game), one field per component for vectors, colours, rects and bounds, a colour picker behind each colour's swatch (a saturation/value square with a hue bar, RGBA bars, hex), name tags on the outlines and mouse-wheel cycling through overlapping objects while picking, a **Code** view per component (type, methods with copyable Harmony names, **Patch** for a ready Harmony patch - the `GameHooks.Require` check, the attribute with the parameter types where a name is ambiguous, and a Prefix and Postfix with the parameters Harmony fills in - the Harmony patches on them with their owners, UnityEvent listeners, a method's IL through Mono.Cecil; no C# decompilation, that stays with dnSpy or ILSpy on the desk), a **debug view** (everything the camera sees, the selection's children, or what the search matches; colliders, triggers and lights too, scene-wide or for the selection alone, each in its own shape - a box as a box, a sphere as a sphere, a capsule as a capsule, a mesh collider as its wireframe, or as its vertices, or, when the physics engine alone holds its shape, as that shape scanned from the six sides of its bounds with a grid of rays against the collider alone (Collider.Raycast), like a depth camera (**a simulation, not the real data: it can miss thin parts, hollows the rays cannot reach and fine detail, so the real shape may differ**, and its label says so); a spot light as its cone; anything else as its bounds - plus rectangles or 3D boxes, coloured by kind, names near the pointer), **Bones** (the armature drawn over the game, a joint clicked selects its bone), **Wire** (the meshes as wireframes; an unreadable mesh only as its bounds), **Edit mesh** (**experimental even within the Inspector**, and labelled so in the menu, on the button and in the tab while it is on: vertices of a readable mesh dragged in the plane facing the camera, into a copy of the mesh, with History and Reset mesh; skinned and GPU-only meshes cannot be edited, and nothing is saved to a file), a **free camera** (a copy of the game's camera, flown as in Unity's scene view with the right mouse button held; the game's camera is disabled meanwhile and restored after), a transform gizmo (**Move** / **Rotate** / **Scale**: the object's local axes in the game view, dragged by their handles, with a readout and **Reset transform**), **Reset** on every edited row, back to the value it held before its first edit, a **History** view of every edit this session (before and after, Revert / Redo, Undo last) that the gizmo's drags join too, a right-click menu on a row (original, previous, copy), a **Parent** button, and a hierarchy that scrolls to the selection. A texture property's **Go** opens the Assets tab filtered to that texture, and a texture row in the Assets tab has **Inspect**, which opens the first material using it.

## Installing

The Inspector is not in the installers that mods ship, and Drag'n Wash Localization leaves it out, so it has to be installed by hand: download `DragNWash.ModFramework-<version>.zip` from the [framework's releases](https://github.com/TomXV/dragnwash-modframework/releases) and copy its `BepInEx/plugins/DragNWash.ModFramework.Inspector` folder into the game's `BepInEx/plugins` folder. Then turn on **Developer tools** (Options → Mods → Drag'n Wash ModFramework) and press F1. To remove it, delete that folder.

An **Inspector** tab in the Tool window: the scene's objects and their components, the fields and properties of each, and the parameters of the loaded materials, readable and editable while the game runs. What Unity's own Inspector does in the editor, for a game one only has as a build. For people who make mods and want to know "what is this thing, what is it called, what value does it hold", and to try a change before writing a line of code. A developer-tools feature (GUIDE rule 8), never something a player sees.

## What it is for, and what it is not

- **Find out.** The name of the object behind a sign, the component that drives the door, the material of the counter and its shader, the field that holds the scrub speed. Everything a mod's `GameHooks.Require` or `AssetReplacements` needs a name for, found by looking instead of decompiling.
- **Try.** Change a value and see it in the game at once. Nothing is saved: the change lives until the scene reloads or the game quits. Turning a set of changes into a mod is a later design (an overrides file the Assets library applies at startup, the same way texture replacements work); this one does not do it, on purpose, so the two can be judged separately.
- **Not a RuntimeUnityEditor.** No REPL, no method calls with arguments, no adding or removing components, no instantiating prefabs, no gizmos. RuntimeUnityEditor does those well; it also opens its own IMGUI window and takes over the cursor and input, so it and the Tool window will fight. The FAQ will say that: one or the other, not both, until coexistence is tested.

## Where it lives

In the **Tool window** library (Tool window 1.2.0), as a tab. It needs nothing from the game and nothing from the other libraries: reflection and Unity's own scene and shader APIs are enough. The material part reads `AssetCatalog` from the Assets library when that is loaded, the way the Assets tab reads the Tool window today (soft dependency: checked through the chainloader, never a hard reference), and the Assets tab gets an **Inspect** button per material that jumps here.

## The tab

Three panes, left to right; in a narrow window they become three pages with a back button, like the Saves tab's rows do.

1. **Hierarchy.** Every loaded scene, its root objects and their children as a tree, plus a **Search** box that filters by name (substring, case-insensitive) and flattens the tree to the matches with their paths. Inactive objects shown muted. The tree is built when the tab is opened, when **Refresh** is pressed or when a scene loads (`GameEvents.OnSceneLoaded`), never every frame: a scene has thousands of objects and `FindObjectsOfType` is not free.
2. **Components.** The selected object's components in order, with the object's active flag, tag, layer and full path above. The **Materials** entry of a Renderer is listed like a component, so a material is one click from the object that shows it. Selecting a component opens it in the third pane.
3. **Members.** The component's fields and properties, one row each: name, type, value. Public ones first, then non-public ones under a **Show private** toggle (off by default; a value set through a private field is the mod author's own risk, and the page says so once). Values are read on each Repaint for the selected component only, so the numbers move as the game runs; a **Freeze** toggle stops that for reading a value that changes every frame.

## Editing

A row whose type the tab knows gets a control; the others show the value as text and are read-only.

| Type | Control |
|---|---|
| `bool` | toggle |
| `int`, `float`, `double`, `long` and the other numbers | text field; Enter applies, a value the parser refuses is put back and the row says why (the same rule as the Mods screen's settings page) |
| `string` | text field |
| enums | choice, cycled with a button, or a list when it has many values |
| `Vector2/3/4`, `Quaternion` (shown as Euler angles), `Rect`, `Bounds` | one text field per component |
| `Color` | four text fields and a swatch |
| `UnityEngine.Object` references (a Transform, a Material, a Texture, another component) | the object's name and a **Go** button that selects it; not editable |
| lists and arrays of the above | expandable, each element a row; no adding or removing |
| anything else | `ToString()`, read-only |

A property is editable only when it has a setter. Every set runs in a try/catch: an exception from the game's setter is shown on the row, and the tab goes on. `Transform` gets the same rows as any component (`position`, `rotation`, `localScale`) and nothing special.

## Materials and shaders

For a material (from a Renderer, or from the Assets tab), the members pane lists the shader's properties by asking the shader itself (`Shader.GetPropertyCount`, `GetPropertyName`, `GetPropertyType`, and the range limits for Range properties), with the material's current value for each:

| Property type | Control |
|---|---|
| Float, Range | text field; a Range shows its limits |
| Color | four fields and a swatch |
| Vector | four fields |
| Texture | the texture's name, size and format, and **Go** to it in the Assets tab; not editable here (texture replacements already exist and go through their own safe path) |
| Int | text field |

Shader keywords (`_ALPHATEST_ON` and the like) are listed with a toggle each. Changing a material changes every renderer that shares it, which is what a mod's material override would do too; the pane says how many renderers share it.

This part is the reason the tab is worth building even before it can save anything: a colour or a smoothness value found here is exactly what a later overrides file records, and a translator's texture work is a Go button away from the material that shows it.

## Rigidbodies

Experimental. The game moves things with Rigidbodies (its mass-spring controller pushes them with forces, for one), so the Inspector reads them, lists them and drives a few of their switches. The physics modules are not referenced: `Rigidbody` and `Rigidbody2D` are found by name and read through reflection, as colliders are, so the library still loads if a module is missing. Unity 6 renamed `velocity` to `linearVelocity`; both are tried.

- **Debug view.** View → **Rigidbodies: centre of mass and velocity** draws a cross on each body's centre of mass and an arrow for where it is heading (a quarter of a second of travel at its current velocity), with a tag giving its speed, turning speed, mass, kind (dynamic, kinematic, static) and whether it is asleep. A sleeping body is grey. **Of the selection only** limits it to the selection and its children, as for colliders and lights.
- **List.** View → **Rigidbodies list** shows every active Rigidbody and Rigidbody2D in the scene, or only under the selection, in place of the members (like History): name, type and what it is doing, fastest first, with **Awake only** and a filter by name or type. **Select** opens a body and keeps the list open for going through them.
- **Controls.** A selected Rigidbody or Rigidbody2D gets a line saying what it is doing and buttons above its members: **Stop** (velocity and turning to zero), **Kinematic** (on and off; `isKinematic`, or `bodyType` for a Rigidbody2D; kept in History, so Revert puts it back), **Sleep** / **Wake**.
- **Pause physics** (experimental even within the Inspector). Switches `Physics.simulationMode` and `Physics2D.simulationMode` to Script, so bodies stay where they are, and **Step** moves the physics one fixed step (`Physics.Simulate(Time.fixedDeltaTime)`). The game's scripts keep running, so forces they add meanwhile arrive with the next step. **Resume physics**, closing the window and switching developer tools off all put the modes back as the game had them.

## Animators

Experimental. The game moves its characters with Animators, mostly by setting their parameters (Bool, Float, Trigger) and layer weights. Like the rigidbodies, the Animator is read by reflection, and the animation module is not referenced.

- **Parameters and layer weights** are rows at the top of a selected Animator's members: drag a number, switch a Bool, set a Trigger (true) or reset it (false). They are kept in History like any member. The game sets many parameters every frame, so an edit to one of those lasts only until the game's next write.
- **What is playing**: above the rows, the base layer's line; **Layers** lists every layer in place of the members (a dragon has more than ten), each with its weight, the clips it plays (with their weights when blended), how far through the state is, and the clips it is blending to.
- **Pause animation** sets the Animator's speed to 0 and remembers the speed it had; **Step** moves it on by 1/30 s, and in Layers a slider per layer puts the current state at any point. **Resume animation**, closing the window or turning developer tools off puts the speed back.
- Not possible in the game: the state machine (states and transitions) and the curves inside a clip exist only in the Unity editor. A state is known by a hash, so the clips it plays stand for its name.
- **Clips**, a button among the Animator's: the controller's clips in place of the members, each with its length, frame rate, whether it loops and its events, and what it is swapped for. **Preview** plays a clip on the Animator in a graph of its own, over what the controller plays, with a time slider, pause and **Stop preview**. **Replace** lists every clip loaded in memory; **Use** makes the controller play that one instead (an AnimatorOverrideController built on the game's controller), **The game's clip** puts the original back. A swap is kept in History (Revert goes back one swap), and restarts the Animator's states. This ties into the Overrides design (branch `experimental/overrides`): a swap is the kind of edit an override would keep.

## Scenes and levels

Experimental. View → **Scenes and levels** opens a list in place of the members. The game has a few scenes (the title, PlayGame, the scenes after certain levels, the credits) and plays its levels inside PlayGame; everything here is read by reflection from the game's own types, so a game update that renames them turns these tools off.

- **The level running** (in PlayGame): its number, dragon, weather, the dragon's state and how clean it is. **Skip level** and **Clean the dragon** call the game's own cheats, the ones its development builds show: Skip ends the level as if it was done, so the next one is saved as reached.
- **Levels**: every level with its dragon and weather; **Start** plays that level now, in place of the current one. The flags earlier levels would have set are not set, so dialogue may differ from a normal play; nothing is written to the save until the level is finished (and the Saves library keeps a copy of every save the game writes).
- **Scenes**: the scenes loaded, and every scene of the game with **Load**, which goes through the game's loading screen as its menus do; **Reload** loads the active scene again.

## Safety

- **Developer tools only.** The tab exists only while the switch is on, like the rest of the window.
- **No uploads.** Reading and editing values never creates or uploads a texture, so Direct3D 12 is not a concern here. A texture property is shown, never assigned.
- **Names are drawn through the window's guard.** Object names can be anything; non-ASCII is shown as `?` on Direct3D 12 as the Console does.
- **Destroyed objects.** A selected object or component that Unity has destroyed (`== null`) clears the selection with a note instead of throwing.
- **No walking every frame.** The hierarchy is built on demand. Reflection member lists are cached per type. Only the selected component's values are read per Repaint.
- **Nothing persists.** There is no file, no config, no replay at startup. The worst an edit can do is break the running session, which a restart fixes; the page says so once when the tab is first opened.
- **Isolation.** Reflection failures, setter exceptions and shader queries that throw are caught per row and shown on that row.

## Console

- `inspect` lists the hierarchy's root objects; `inspect <name or path>` selects an object (path segments separated by `/`, the first match by name when there is no path) and opens the tab on it; `inspect <path> <component>` selects the component.
- `inspect set <path> <component> <member> <value>` sets one value from the console, with the same parsing as the row. For scripts one runs by hand; there is still no scripting language.
- Completion offers object names for the current search and, after a path, its component names.
- `bodies` lists the scene's rigidbodies, fastest first, with their speed, mass, kind and sleep; `bodies pause`, `bodies resume` and `bodies step [count]` drive the physics pause (experimental).

## For mod authors

- `Inspector.Inspect(UnityEngine.Object target)` selects an object, component or material and opens the tab on it. A mod's own tab can put an **Inspect** button next to anything it lists.
- Nothing to declare: every object and component is shown, including a mod's own. A `[HideInInspector]` attribute from Unity on a field is honoured; a mod that wants a field hidden uses that.
- Custom drawers for a type (a nicer control than the default rows) are not in this design; if the need shows up, they would be registered by GUID like everything else.

## Order of work

1. Hierarchy with search and selection; components pane; members pane read-only. Useful on its own for finding names.
2. Editing of the known types, with the per-row error rule.
3. Materials: shader properties and keywords; the Inspect button in the Assets tab; Go from a texture property to the Assets tab.
4. `inspect` console commands and completion; `ToolWindow.Inspect`.
5. Private members behind the toggle; lists and arrays.
6. FAQ note about RuntimeUnityEditor, and a coexistence test if anyone asks.

Not in this design: saving edits (a later overrides design in the Assets library), method invocation, adding or removing components, prefabs, gizmos, a REPL.
