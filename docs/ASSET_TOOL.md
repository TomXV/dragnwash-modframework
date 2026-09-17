# Asset tool

[日本語](ASSET_TOOL.ja.md)

> **Experimental.** In the Assets library from 1.1.0, on `main` but not yet in a release. What is here may still change before one.

The framework provides tools for the game's assets; what people make with them is their own responsibility, in the way of REFramework and similar tools. Three things, in this order:

1. **Browse**: what textures, materials, meshes and shaders are loaded.
2. **Replace**: a mod ships its own files and they take the place of the game's, without touching the game's files.
3. **Export and import** (not built yet): write a loaded object to a file in plain form, change it, and bring it back from a mod.

Nothing here ships the game's assets. The game's data unchanged never goes into a repository or a release, and this repository's CI enforces it; material made by hand or changed follows [CONTENT_POLICY.md](CONTENT_POLICY.md).

## Browse

`AssetCatalog.Textures()`, `Materials()`, `Meshes()` and `Shaders()` list what is loaded right now, with name, size, format, whether the CPU can read it, and how many materials and sprites use it. They walk every loaded object of that kind, so call them on a button press, not every frame.

With the Tool window library installed, the **Assets** tab (F1) lists textures with a filter box, shows which replacements mods shipped and where two mods clashed, and has an **Apply replacements** button.

## Replace

Put a PNG in your mod's folder, named after the game texture it replaces:

```
BepInEx/plugins/<YourMod>/assets/textures/<texture name>.png
```

The texture's name is what the Assets tab lists. At startup the library reads every such file (that is the moment texture uploads are safe on Direct3D 12) and, when each scene loads, puts it into every material property and sprite that used the original. A sprite keeps its rect, pivot and pixels-per-unit; give the PNG the original's size, or the rect is scaled with the image. `AssetReplacements.ApplyNow()` does the same again, for materials or sprites the game created since.

When two mods replace the same texture, the mod whose folder sorts last wins, and both are named in the log and in the Assets tab. Nothing is overridden silently.

Not yet: meshes, shaders, and a `replace.json` for targeting by anything but the name. Skinned meshes (characters) are out of scope for now.

### Reloading while the game runs

**Reload files** in the Assets tab reads every replacement PNG again, swaps in the ones whose content changed, and applies them; **Apply replacements** only re-points materials and sprites at textures that are already loaded. With `[Reload] WatchFiles = true` in the library's config, a PNG that changes on disk is reloaded by itself half a second after the last write (never on Direct3D 12).

A reload uploads textures while the game runs, which on Direct3D 12 can crash it (Unity UUM-140564). Because a native crash cannot be caught, the library writes a marker file (`BepInEx/config/<assets GUID>.reload-in-progress`) before uploading and removes it afterwards. A marker still there at the next start means the last reload took the game down: the log and the Mods screen say so, `[Reload] AllowReload` is switched off until you turn it back on, and after two such crashes on Direct3D 12 the button stays off. Working with `-force-d3d11` in the game's launch options avoids all of this.

Failures the library can see are handled per file and never stop the rest: a PNG that is not an image, or that an editor is still saving (retried three times), keeps its previous texture and is listed under **Show replacements** with the reason.

## Export and import

Planned. Every kind the object explorer lists that has a plain form can be written to a file, changed, and brought back by a mod. What may go into a mod's release follows [CONTENT_POLICY.md](CONTENT_POLICY.md): made by hand or changed, never the game's data unchanged.

| Kind | Export | Import (a mod ships it) |
|---|---|---|
| Texture, Sprite | PNG, read back from the GPU, so the game's compressed textures work too | PNG, as today (`assets/textures/`) |
| Mesh (not skinned) | OBJ | OBJ, replacing the mesh of that name (`assets/meshes/`) |
| Material | JSON: the shader's property values and keywords | JSON with only the values to change (`assets/materials/`) |
| Shader | JSON: its properties and keywords (the compiled shader is not exported) | none |
| AudioClip | WAV, for clips whose samples can be read (not streamed ones) | WAV, replacing the clip of that name (`assets/audio/`) |
| ScriptableObject and other serializable objects | JSON of their serialized fields (`JsonUtility`) | JSON with only the fields to change, applied over the object (`assets/data/`) |
| Text and dialogue | Drag'n Wash Localization's exports | its translation packs |

- **Where.** Exports go to `BepInEx/exports/<game build>/<kind>/`, with a `NOTICE.txt` that says what the files are and that they stay on your machine. Imports are read from `BepInEx/plugins/<YourMod>/assets/<kind>/<name>.<ext>`.
- **From where.** An **Export** button on each object in the Inspector's object explorer (designed on the `experimental/object-explorer` branch), on each row of the Assets tab, and `export <kind> <name>` in the Console. Developer tools only.
- **When imports apply.** Read at startup (the moment uploads are safe on Direct3D 12), applied when each scene loads and on **Apply replacements**, like textures. Data imports change only the fields named in the file.
- **Clashes.** Two mods importing the same object: the mod whose folder sorts last wins, both are named in the log and in the Assets tab, as for textures.
- **Untouched exports.** Exports are deterministic, so their hashes can be published without their content: `ci/asset-fingerprints.json` will list them, and `pack.ps1` and CI will refuse a mod zip that contains an untouched export.
- **Not in scope.** Animation clips (the game build has no API to read their curves), fonts, compiled shaders and skinned meshes (characters).

## For mod authors

```csharp
// Nothing to call for replacements: ship the files. To see what is there:
foreach (TextureInfo t in AssetCatalog.Textures())
    if (t.MaterialUsers > 0) Log($"{t.Name} {t.Width}x{t.Height} {t.Format}");

// After creating materials or sprites of your own from game textures:
AssetReplacements.ApplyNow();
```
