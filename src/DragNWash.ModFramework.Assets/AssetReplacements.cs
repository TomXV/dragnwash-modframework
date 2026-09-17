using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace DragNWash.ModFramework.Assets
{
    /// <summary>One texture a mod replaces: the file it came from and the loaded texture.</summary>
    public sealed class TextureReplacement
    {
        internal TextureReplacement() { }

        /// <summary>Name of the game texture it replaces (the file name without extension).</summary>
        public string Name { get; internal set; }

        /// <summary>Folder under BepInEx/plugins the file came from, as the mod's name.</summary>
        public string Mod { get; internal set; }

        /// <summary>The language it applies to, or null when it applies whatever the language (a plain replacement).</summary>
        public string Language { get; internal set; }

        /// <summary>The file.</summary>
        public string Path { get; internal set; }

        /// <summary>The loaded replacement, or null when the file could not be read.</summary>
        public Texture2D Texture { get; internal set; }

        /// <summary>Materials and sprites it has been put into so far.</summary>
        public int Applied { get; internal set; }

        /// <summary>Other mods that ship a replacement with the same name and lost to this one.</summary>
        public List<string> Overrides { get; } = new List<string>();

        /// <summary>Why the last reload of this file failed, or null when it loaded.</summary>
        public string Problem { get; internal set; }

        internal string ContentHash { get; set; }
    }

    /// <summary>What <see cref="AssetReplacements.ReloadFiles"/> did to one file.</summary>
    public sealed class ReloadResult
    {
        internal ReloadResult(string name, string status) { Name = name; Status = status; }

        /// <summary>Texture name.</summary>
        public string Name { get; }

        /// <summary>"reloaded", "unchanged", or the problem.</summary>
        public string Status { get; }
    }

    /// <summary>
    /// Replaces the game's textures with PNG files that mods ship, without
    /// touching the game's files: <c>BepInEx/plugins/&lt;Mod&gt;/assets/textures/&lt;texture name&gt;.png</c>
    /// replaces the loaded texture of that name wherever a material or sprite
    /// uses it. Experimental (Assets 1.1).
    /// </summary>
    /// <remarks>
    /// Files are read at startup (safe on Direct3D 12); they are put into
    /// materials and sprites when each scene loads, and again on
    /// <see cref="ApplyNow"/>. When two mods replace the same texture, the one
    /// whose folder sorts last wins and both are named in the log and in the
    /// Tool window, never silently.
    /// </remarks>
    public static class AssetReplacements
    {
        private static readonly Dictionary<string, TextureReplacement> ByName = new Dictionary<string, TextureReplacement>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<Texture2D> Replacements = new HashSet<Texture2D>();
        private static readonly Dictionary<Sprite, Sprite> SpriteFor = new Dictionary<Sprite, Sprite>();
        private static bool _hooked;

        // Replacements per language (AddLanguageFolder). Only the language in use
        // is loaded; its files are in LanguageByName while they apply.
        private sealed class LanguageFolder
        {
            public string Guid, Root, Subfolder, Mod;
            public bool Enabled = true;
        }
        private static readonly List<LanguageFolder> LanguageFolders = new List<LanguageFolder>();
        private static readonly Dictionary<string, TextureReplacement> LanguageByName = new Dictionary<string, TextureReplacement>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<TextureReplacement, LanguageFolder> FolderOf = new Dictionary<TextureReplacement, LanguageFolder>();
        private static string _loadedLanguage;

        // What was there before a replacement went in, so it can be taken back:
        // per material property, and per replacement sprite. Holding the
        // originals keeps Unity from unloading them while they are replaced.
        private static readonly Dictionary<string, Texture> MaterialOriginal = new Dictionary<string, Texture>();
        private static readonly Dictionary<Sprite, Sprite> OriginalSprite = new Dictionary<Sprite, Sprite>();

        /// <summary>
        /// The language whose pictures apply after a restart, or null. Set on
        /// Direct3D 12, where a language change cannot load textures while the
        /// game runs; the previous language's pictures are taken back meanwhile.
        /// </summary>
        public static string PendingLanguage { get; private set; }

        /// <summary>Raised after replacements were loaded, taken back or re-applied because of a language change or <see cref="SetLanguageFoldersEnabled"/>.</summary>
        public static event Action Changed;
        private static readonly List<FileSystemWatcher> Watchers = new List<FileSystemWatcher>();
        private static volatile bool _reloadRequested;
        private static float _reloadRequestedAt;

        /// <summary>Set by the last <see cref="ReloadFiles"/>: one entry per file it looked at.</summary>
        public static IReadOnlyList<ReloadResult> LastReload { get; private set; } = new ReloadResult[0];

        /// <summary>True while reloading is refused: switched off in the config, or after the game crashed during a reload.</summary>
        public static bool ReloadDisabled { get; internal set; }

        /// <summary>Why <see cref="ReloadDisabled"/> is set, for the Assets tab.</summary>
        public static string ReloadDisabledReason { get; internal set; }

        /// <summary>Every replacement found: the plain ones by texture name, then the current language's.</summary>
        public static IReadOnlyCollection<TextureReplacement> All
        {
            get
            {
                var all = new List<TextureReplacement>(ByName.Values);
                all.AddRange(LanguageByName.Values);
                return all;
            }
        }

        /// <summary>Replacements that lost to another mod's file for the same name.</summary>
        public static int ConflictCount
        {
            get
            {
                int n = 0;
                foreach (TextureReplacement r in All)
                {
                    n += r.Overrides.Count;
                }
                return n;
            }
        }

        // The replacement that applies to a texture of this name now: the current
        // language's picture when its folder is on, else a plain replacement.
        private static TextureReplacement Effective(string name)
        {
            if (name != null && LanguageByName.TryGetValue(name, out TextureReplacement l) && l.Texture != null && FolderOf.TryGetValue(l, out LanguageFolder f) && f.Enabled)
            {
                return l;
            }
            return name != null && ByName.TryGetValue(name, out TextureReplacement r) && r.Texture != null ? r : null;
        }

        /// <summary>
        /// Adds replacements that apply only in one language:
        /// <c>&lt;root&gt;/&lt;language&gt;/&lt;subfolder&gt;/&lt;texture name&gt;.png</c>
        /// applies while <see cref="GameFonts.Language"/> is that language.
        /// Only the language in use is loaded. Call from Awake, after
        /// <see cref="GameFonts.SetLanguage"/>. A language picture wins over a
        /// plain replacement of the same texture; both are named in the log.
        /// Experimental (Assets 1.2).
        /// </summary>
        public static void AddLanguageFolder(string guid, string root, string subfolder)
        {
            if (string.IsNullOrEmpty(guid) || string.IsNullOrEmpty(root))
            {
                return;
            }
            LanguageFolders.RemoveAll(f => f.Guid == guid);
            var folder = new LanguageFolder { Guid = guid, Root = root, Subfolder = subfolder ?? "", Mod = System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(root.TrimEnd('/', '\\'))) };
            LanguageFolders.Add(folder);
            string language = GameFonts.Language;
            if (_loadedLanguage == null || string.Equals(_loadedLanguage, language, StringComparison.OrdinalIgnoreCase))
            {
                // Startup (or the same language): loading now is safe.
                _loadedLanguage = language;
                LoadLanguage(folder, language);
                Hook();
                ApplyNow();
            }
            else if (!GameFonts.RuntimeUploadsAreSafe)
            {
                PendingLanguage = language;
            }
            else
            {
                LoadLanguage(folder, _loadedLanguage);
                ApplyNow();
            }
        }

        /// <summary>
        /// Switches one mod's language pictures off or on. Off takes them back at
        /// once; on applies them again, loading them first where that is safe
        /// (on Direct3D 12 pictures that were never loaded wait for a restart).
        /// </summary>
        public static void SetLanguageFoldersEnabled(string guid, bool on)
        {
            foreach (LanguageFolder f in LanguageFolders)
            {
                if (f.Guid != guid || f.Enabled == on)
                {
                    continue;
                }
                f.Enabled = on;
                if (on && !HasLoaded(f))
                {
                    if (GameFonts.RuntimeUploadsAreSafe)
                    {
                        LoadLanguage(f, _loadedLanguage);
                    }
                    else
                    {
                        PendingLanguage = _loadedLanguage;
                    }
                }
            }
            RefreshAll();
        }

        private static bool HasLoaded(LanguageFolder folder)
        {
            foreach (LanguageFolder f in FolderOf.Values)
            {
                if (f == folder)
                {
                    return true;
                }
            }
            return false;
        }

        // From GameFonts.SetLanguage.
        internal static void OnLanguageSet(string language)
        {
            if (LanguageFolders.Count == 0)
            {
                return;
            }
            try
            {
                if (string.Equals(language, _loadedLanguage, StringComparison.OrdinalIgnoreCase))
                {
                    // Back to the language loaded at startup: its pictures apply again.
                    foreach (TextureReplacement r in Suspended)
                    {
                        LanguageByName[r.Name] = r;
                    }
                    Suspended.Clear();
                    PendingLanguage = null;
                    RefreshAll();
                    return;
                }
                if (!GameFonts.RuntimeUploadsAreSafe)
                {
                    // Taking pictures back needs no upload; loading the new ones does.
                    PendingLanguage = language;
                    DisableLoaded();
                    RefreshAll();
                    AssetsLibraryPlugin.Log.LogInfo($"Pictures for \"{language}\" apply after a restart (Direct3D 12 cannot load textures while the game runs).");
                    return;
                }
                var previous = new List<TextureReplacement>(LanguageByName.Values);
                LanguageByName.Clear();
                FolderOf.Clear();
                RefreshAll(previous);
                foreach (TextureReplacement r in previous)
                {
                    Replacements.Remove(r.Texture);
                    if (r.Texture != null)
                    {
                        UnityEngine.Object.Destroy(r.Texture);
                    }
                }
                _loadedLanguage = language;
                PendingLanguage = null;
                foreach (LanguageFolder f in LanguageFolders)
                {
                    LoadLanguage(f, language);
                }
                RefreshAll();
            }
            catch (Exception ex)
            {
                AssetsLibraryPlugin.Log.LogError($"Switching language pictures to \"{language}\" failed: {ex}");
            }
        }

        // Direct3D 12 waiting for a restart: the loaded pictures stay in memory
        // but no longer apply, as if their folders were off.
        private static readonly HashSet<TextureReplacement> Suspended = new HashSet<TextureReplacement>();

        private static void DisableLoaded()
        {
            foreach (TextureReplacement r in LanguageByName.Values)
            {
                Suspended.Add(r);
            }
            LanguageByName.Clear();
        }

        private static void LoadLanguage(LanguageFolder folder, string language)
        {
            if (string.IsNullOrEmpty(language))
            {
                return;
            }
            string dir = System.IO.Path.Combine(System.IO.Path.Combine(folder.Root, language), folder.Subfolder);
            if (!Directory.Exists(dir))
            {
                return;
            }
            string[] files = Directory.GetFiles(dir, "*.png");
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            int loaded = 0;
            foreach (string file in files)
            {
                TextureReplacement r = Load(file, folder.Mod);
                if (r == null)
                {
                    continue;
                }
                r.Language = language;
                if (LanguageByName.TryGetValue(r.Name, out TextureReplacement earlier))
                {
                    r.Overrides.AddRange(earlier.Overrides);
                    r.Overrides.Add(earlier.Mod);
                    Replacements.Remove(earlier.Texture);
                    FolderOf.Remove(earlier);
                    AssetsLibraryPlugin.Log.LogWarning($"Texture \"{r.Name}\" has a {language} picture in both {earlier.Mod} and {r.Mod}; {r.Mod} wins.");
                }
                if (ByName.TryGetValue(r.Name, out TextureReplacement plain))
                {
                    r.Overrides.Add(plain.Mod);
                    AssetsLibraryPlugin.Log.LogWarning($"Texture \"{r.Name}\" is replaced by {plain.Mod} and has a {language} picture in {r.Mod}; the {language} picture applies while that language is in use.");
                }
                LanguageByName[r.Name] = r;
                FolderOf[r] = folder;
                Replacements.Add(r.Texture);
                loaded++;
            }
            if (loaded > 0)
            {
                AssetsLibraryPlugin.Log.LogInfo($"{loaded} {language} picture(s) loaded from {folder.Mod}.");
            }
        }

        private static TextureReplacement Load(string file, string mod)
        {
            string name = System.IO.Path.GetFileNameWithoutExtension(file);
            var replacement = new TextureReplacement { Name = name, Mod = mod, Path = file, Texture = GameAssets.LoadTexture(file) };
            if (replacement.Texture == null)
            {
                return null;
            }
            replacement.Texture.name = name;
            replacement.ContentHash = HashFile(file);
            return replacement;
        }

        private static void Hook()
        {
            if (_hooked)
            {
                return;
            }
            GameEvents.OnSceneLoaded(GameFonts.Guid, (scene, mode) => ApplyNow());
            ModFramework.Ready += () => ApplyNow();
            _hooked = true;
        }

        // Takes back what no longer applies and applies what does, everywhere.
        // With extra, those textures (no longer listed anywhere) are taken back too.
        private static void RefreshAll(List<TextureReplacement> extra = null)
        {
            var gone = new HashSet<Texture>();
            if (extra != null)
            {
                foreach (TextureReplacement r in extra)
                {
                    if (r.Texture != null) gone.Add(r.Texture);
                }
            }
            foreach (TextureReplacement r in Suspended)
            {
                if (r.Texture != null) gone.Add(r.Texture);
            }
            TakeBack(gone);
            ApplyNow();
            try
            {
                Changed?.Invoke();
            }
            catch (Exception ex)
            {
                AssetsLibraryPlugin.Log.LogError($"An AssetReplacements.Changed handler threw: {ex}");
            }
        }

        // Puts the original back wherever one of these textures, or a replacement
        // that no longer applies, is in use.
        private static void TakeBack(HashSet<Texture> gone)
        {
            foreach (Material m in Resources.FindObjectsOfTypeAll<Material>())
            {
                if (m == null)
                {
                    continue;
                }
                foreach (string property in AssetCatalog.SafeTextureProperties(m))
                {
                    Texture current = m.GetTexture(property);
                    if (current == null || !(gone.Contains(current) || IsStale(current)))
                    {
                        continue;
                    }
                    string key = MaterialKey(m, property);
                    if (MaterialOriginal.TryGetValue(key, out Texture original))
                    {
                        m.SetTexture(property, original);
                    }
                }
            }
            foreach (Image image in Resources.FindObjectsOfTypeAll<Image>())
            {
                if (image != null && image.sprite != null && (gone.Contains(image.sprite.texture) || IsStale(image.sprite.texture)) &&
                    OriginalSprite.TryGetValue(image.sprite, out Sprite original))
                {
                    image.sprite = original;
                }
            }
            foreach (SpriteRenderer renderer in Resources.FindObjectsOfTypeAll<SpriteRenderer>())
            {
                if (renderer != null && renderer.sprite != null && (gone.Contains(renderer.sprite.texture) || IsStale(renderer.sprite.texture)) &&
                    OriginalSprite.TryGetValue(renderer.sprite, out Sprite original))
                {
                    renderer.sprite = original;
                }
            }
        }

        // A replacement texture in use that is not what applies to its name now.
        private static bool IsStale(Texture current)
        {
            var t = current as Texture2D;
            if (t == null || !Replacements.Contains(t) && !IsSuspended(t))
            {
                return false;
            }
            TextureReplacement now = Effective(t.name);
            return now == null || now.Texture != t;
        }

        private static bool IsSuspended(Texture2D t)
        {
            foreach (TextureReplacement r in Suspended)
            {
                if (r.Texture == t) return true;
            }
            return false;
        }

        private static string MaterialKey(Material m, string property) => m.GetInstanceID() + "|" + property;

        /// <summary>True when <paramref name="texture"/> is a replacement a mod shipped.</summary>
        public static bool IsReplacement(Texture2D texture)
        {
            return texture != null && Replacements.Contains(texture);
        }

        // Scans every plugin folder and loads the files. Called from the library's Awake.
        internal static void LoadAll()
        {
            string root = Paths.PluginPath;
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
            {
                return;
            }
            string[] mods = Directory.GetDirectories(root);
            Array.Sort(mods, StringComparer.OrdinalIgnoreCase);
            foreach (string modDir in mods)
            {
                string textures = System.IO.Path.Combine(modDir, "assets", "textures");
                if (!Directory.Exists(textures))
                {
                    continue;
                }
                string mod = System.IO.Path.GetFileName(modDir);
                string[] files = Directory.GetFiles(textures, "*.png");
                Array.Sort(files, StringComparer.OrdinalIgnoreCase);
                foreach (string file in files)
                {
                    TextureReplacement replacement = Load(file, mod);
                    if (replacement == null)
                    {
                        continue;
                    }
                    string name = replacement.Name;
                    if (ByName.TryGetValue(name, out TextureReplacement earlier))
                    {
                        replacement.Overrides.AddRange(earlier.Overrides);
                        replacement.Overrides.Add(earlier.Mod);
                        Replacements.Remove(earlier.Texture);
                        AssetsLibraryPlugin.Log.LogWarning($"Texture \"{name}\" is replaced by both {earlier.Mod} and {mod}; {mod} wins.");
                    }
                    ByName[name] = replacement;
                    Replacements.Add(replacement.Texture);
                }
            }
            if (ByName.Count > 0)
            {
                AssetsLibraryPlugin.Log.LogInfo($"{ByName.Count} texture replacement(s) loaded from mods.");
                Hook();
            }
        }

        /// <summary>
        /// Puts every replacement into the materials and sprites that use the
        /// original, now. Runs by itself when a scene loads; call it after the
        /// game created new materials or sprites. Returns how many places changed.
        /// </summary>
        public static int ApplyNow()
        {
            if (ByName.Count == 0 && LanguageByName.Count == 0)
            {
                return 0;
            }
            int changed = 0;
            try
            {
                changed += ApplyToMaterials();
                changed += ApplyToSprites();
            }
            catch (Exception ex)
            {
                AssetsLibraryPlugin.Log.LogError($"Applying texture replacements failed: {ex}");
            }
            return changed;
        }

        /// <summary>
        /// Reads every replacement file again and swaps in the ones that changed,
        /// then applies them. A file that cannot be read keeps its old texture
        /// and is reported. Uploads textures while the game runs, which on
        /// Direct3D 12 can crash it; see <see cref="ReloadDisabled"/>.
        /// </summary>
        public static IReadOnlyList<ReloadResult> ReloadFiles()
        {
            var results = new List<ReloadResult>();
            if (ReloadDisabled)
            {
                results.Add(new ReloadResult("(reload)", ReloadDisabledReason ?? "reloading is switched off"));
                LastReload = results;
                return results;
            }
            var changed = new List<TextureReplacement>();
            foreach (TextureReplacement r in All)
            {
                if (!File.Exists(r.Path))
                {
                    results.Add(new ReloadResult(r.Name, "file removed; the loaded texture stays until the game restarts"));
                    continue;
                }
                if (HashFile(r.Path) == r.ContentHash)
                {
                    results.Add(new ReloadResult(r.Name, "unchanged"));
                    continue;
                }
                changed.Add(r);
            }
            if (changed.Count == 0)
            {
                LastReload = results;
                return results;
            }
            var names = new List<string>();
            foreach (TextureReplacement r in changed)
            {
                names.Add(r.Name);
            }
            ReloadGuard.Begin(string.Join(" ", names));
            try
            {
                foreach (TextureReplacement r in changed)
                {
                    Texture2D fresh = GameAssets.LoadTextureFresh(r.Path, out string error);
                    if (fresh == null)
                    {
                        r.Problem = error;
                        results.Add(new ReloadResult(r.Name, error));
                        AssetsLibraryPlugin.Log.LogWarning($"Texture \"{r.Name}\" was not reloaded: {error}");
                        continue;
                    }
                    fresh.name = r.Name;
                    Texture2D old = r.Texture;
                    Replacements.Remove(old);
                    Replacements.Add(fresh);
                    r.Texture = fresh;
                    r.Problem = null;
                    r.ContentHash = HashFile(r.Path);
                    r.Applied = 0;
                    var stale = new List<Sprite>();
                    foreach (KeyValuePair<Sprite, Sprite> kv in SpriteFor)
                    {
                        if (kv.Value != null && kv.Value.texture == old)
                        {
                            stale.Add(kv.Key);
                        }
                    }
                    foreach (Sprite s in stale)
                    {
                        SpriteFor.Remove(s);
                    }
                    ReplaceEverywhere(old, fresh);
                    results.Add(new ReloadResult(r.Name, "reloaded"));
                }
                ApplyNow();
            }
            catch (Exception ex)
            {
                AssetsLibraryPlugin.Log.LogError($"Reloading texture replacements failed: {ex}");
                results.Add(new ReloadResult("(reload)", ex.Message));
            }
            finally
            {
                ReloadGuard.End();
            }
            LastReload = results;
            return results;
        }

        // Materials and sprite users that hold the previous replacement get the new one.
        private static void ReplaceEverywhere(Texture2D old, Texture2D fresh)
        {
            foreach (Material m in Resources.FindObjectsOfTypeAll<Material>())
            {
                if (m == null)
                {
                    continue;
                }
                foreach (string property in AssetCatalog.SafeTextureProperties(m))
                {
                    if (m.GetTexture(property) == old)
                    {
                        m.SetTexture(property, fresh);
                    }
                }
            }
            var recut = new Dictionary<Sprite, Sprite>();
            foreach (Image image in Resources.FindObjectsOfTypeAll<Image>())
            {
                if (image != null && image.sprite != null && image.sprite.texture == old)
                {
                    image.sprite = ReCut(image.sprite, fresh, recut);
                }
            }
            foreach (SpriteRenderer renderer in Resources.FindObjectsOfTypeAll<SpriteRenderer>())
            {
                if (renderer != null && renderer.sprite != null && renderer.sprite.texture == old)
                {
                    renderer.sprite = ReCut(renderer.sprite, fresh, recut);
                }
            }
        }

        // The same sprite, cut from the new texture, once per previous sprite;
        // it remembers the same original as the sprite it takes over from.
        private static Sprite ReCut(Sprite previous, Texture2D fresh, Dictionary<Sprite, Sprite> done)
        {
            if (done.TryGetValue(previous, out Sprite made))
            {
                return made;
            }
            Sprite sprite = CutFrom(previous, fresh);
            done[previous] = sprite;
            if (OriginalSprite.TryGetValue(previous, out Sprite original))
            {
                OriginalSprite[sprite] = original;
                SpriteFor[original] = sprite;
            }
            return sprite;
        }

        private static Sprite CutFrom(Sprite previous, Texture2D fresh)
        {
            Rect rect = previous.rect;
            float sx = (float)fresh.width / previous.texture.width, sy = (float)fresh.height / previous.texture.height;
            var scaled = new Rect(rect.x * sx, rect.y * sy, rect.width * sx, rect.height * sy);
            Vector2 pivot = new Vector2(previous.pivot.x / rect.width, previous.pivot.y / rect.height);
            Sprite sprite = Sprite.Create(fresh, scaled, pivot, previous.pixelsPerUnit * sx, 0, SpriteMeshType.FullRect, previous.border);
            sprite.name = previous.name;
            sprite.hideFlags = HideFlags.DontUnloadUnusedAsset;
            return sprite;
        }

        private static string HashFile(string path)
        {
            try
            {
                using (var sha = System.Security.Cryptography.SHA256.Create())
                using (FileStream f = File.OpenRead(path))
                {
                    return BitConverter.ToString(sha.ComputeHash(f));
                }
            }
            catch
            {
                return null;
            }
        }

        // Watches every replacement folder and asks for a reload on the next
        // Update, half a second after the last change, so an editor saving in
        // several steps triggers one reload. Never on Direct3D 12.
        internal static void WatchFiles()
        {
            if (Watchers.Count > 0 || !GameFonts.RuntimeUploadsAreSafe)
            {
                return;
            }
            var folders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (TextureReplacement r in ByName.Values)
            {
                folders.Add(System.IO.Path.GetDirectoryName(r.Path));
            }
            foreach (string folder in folders)
            {
                try
                {
                    var w = new FileSystemWatcher(folder, "*.png") { NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName };
                    w.Changed += (s, e) => { _reloadRequested = true; };
                    w.Created += (s, e) => { _reloadRequested = true; };
                    w.Renamed += (s, e) => { _reloadRequested = true; };
                    w.EnableRaisingEvents = true;
                    Watchers.Add(w);
                }
                catch (Exception ex)
                {
                    AssetsLibraryPlugin.Log.LogWarning($"Could not watch {folder}: {ex.Message}");
                }
            }
            if (Watchers.Count > 0)
            {
                AssetsLibraryPlugin.Log.LogInfo($"Watching {Watchers.Count} texture folder(s) for changes.");
            }
        }

        // From the plugin's Update: runs the reload a watcher asked for.
        internal static void Tick()
        {
            if (ReloadDisabled)
            {
                _reloadRequested = false;
            }
            if (!_reloadRequested)
            {
                _reloadRequestedAt = 0f;
                return;
            }
            if (_reloadRequestedAt == 0f)
            {
                _reloadRequestedAt = Time.realtimeSinceStartup;
                return;
            }
            if (Time.realtimeSinceStartup - _reloadRequestedAt < 0.5f)
            {
                return;
            }
            _reloadRequested = false;
            _reloadRequestedAt = 0f;
            int n = 0;
            foreach (ReloadResult r in ReloadFiles())
            {
                if (r.Status == "reloaded")
                {
                    n++;
                }
            }
            AssetsLibraryPlugin.Log.LogMessage($"Texture files changed on disk: {n} replacement(s) reloaded.");
        }

        private static int ApplyToMaterials()
        {
            int changed = 0;
            foreach (Material m in Resources.FindObjectsOfTypeAll<Material>())
            {
                if (m == null)
                {
                    continue;
                }
                foreach (string property in AssetCatalog.SafeTextureProperties(m))
                {
                    Texture current = m.GetTexture(property);
                    if (current == null)
                    {
                        continue;
                    }
                    TextureReplacement r = Effective(current.name);
                    if (r == null || current == r.Texture)
                    {
                        continue;
                    }
                    string key = MaterialKey(m, property);
                    bool isReplacement = current is Texture2D t2 && (Replacements.Contains(t2) || IsSuspended(t2));
                    if (!isReplacement)
                    {
                        MaterialOriginal[key] = current;
                    }
                    else if (!MaterialOriginal.ContainsKey(key))
                    {
                        // A replacement put there before we could note the original.
                        continue;
                    }
                    m.SetTexture(property, r.Texture);
                    r.Applied++;
                    changed++;
                }
            }
            return changed;
        }

        private static int ApplyToSprites()
        {
            int changed = 0;
            foreach (Image image in Resources.FindObjectsOfTypeAll<Image>())
            {
                if (image != null && TryReplacementSprite(image.sprite, out Sprite s))
                {
                    image.sprite = s;
                    changed++;
                }
            }
            foreach (SpriteRenderer renderer in Resources.FindObjectsOfTypeAll<SpriteRenderer>())
            {
                if (renderer != null && TryReplacementSprite(renderer.sprite, out Sprite s))
                {
                    renderer.sprite = s;
                    changed++;
                }
            }
            return changed;
        }

        // A sprite cut from the replacement texture with the original's rect,
        // pivot and pixels-per-unit, made once per original sprite. The
        // replacement image should have the original's size; a different size
        // is scaled to keep the same rect in the texture's proportions.
        private static bool TryReplacementSprite(Sprite current, out Sprite replacement)
        {
            replacement = null;
            if (current == null || current.texture == null)
            {
                return false;
            }
            // A sprite we made stands for the game's sprite it was cut for.
            Sprite original = OriginalSprite.TryGetValue(current, out Sprite o) && o != null ? o : current;
            if (original != current && original.texture == null)
            {
                return false;
            }
            if (original == current && (Replacements.Contains(current.texture) || IsSuspended(current.texture)))
            {
                return false;
            }
            TextureReplacement r = Effective(original.texture.name);
            if (r == null || current.texture == r.Texture)
            {
                return false;
            }
            if (SpriteFor.TryGetValue(original, out replacement) && replacement != null && replacement.texture == r.Texture)
            {
                return true;
            }
            replacement = CutFrom(original, r.Texture);
            SpriteFor[original] = replacement;
            OriginalSprite[replacement] = original;
            r.Applied++;
            return true;
        }
    }
}
