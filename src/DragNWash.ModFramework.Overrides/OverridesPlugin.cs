using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DragNWash.ModFramework.Overrides
{
    // The Overrides library's BepInEx entry point: finds the overrides mods,
    // lists them on the Mods screen, and applies them as scenes load and
    // objects appear.
    [BepInPlugin(GameOverrides.Guid, "DragNWash.ModFramework.Overrides", GameOverrides.Version)]
    [BepInDependency(ModFramework.Guid, BepInDependency.DependencyFlags.HardDependency)]
    [BepInDependency(ToolWindow.ToolWindow.Guid, BepInDependency.DependencyFlags.SoftDependency)]
    internal sealed class OverridesPlugin : BaseUnityPlugin
    {
        internal static ManualLogSource Log;
        internal static OverridesPlugin Instance;

        private ConfigEntry<bool> _enabled;
        private float _nextWatch;
        private const float WatchSeconds = 0.5f;

        private void Awake()
        {
            Log = Logger;
            Instance = this;
            ModFramework.Register(new ModInfo
            {
                Guid = GameOverrides.Guid,
                DisplayName = "Drag'n Wash ModFramework: Overrides",
                Description = "Runs mods that have no code: folders of values to change in the game, made with the Inspector. Experimental.",
                Authors = new[] { "TomXV" },
                Website = "https://github.com/TomXV/dragnwash-modframework",
                IsLibrary = true,
            });
            _enabled = Config.Bind("General", "Enabled", true,
                "Apply the overrides mods in BepInEx/plugins (folders with mod.json and overrides/). Each can also be switched off on the Mods screen.");

            Read();
            AddConsoleCommand();
            SceneManager.sceneLoaded += (scene, mode) => { if (_enabled.Value) StartCoroutine(AfterLoad(scene.name)); };
        }

        private void Read()
        {
            GameOverrides.Loaded = OverrideFiles.Scan(Paths.PluginPath);
            foreach (OverrideFiles.Mod mod in GameOverrides.Loaded)
            {
                ListOnModsScreen(mod);
                Log.LogInfo($"[overrides] {mod.Name} {mod.Version}: {mod.Overrides.Count} override(s){(mod.UsesPrivate ? ", some on private members" : "")}.");
                foreach (string problem in mod.Problems)
                {
                    Log.LogWarning($"[overrides] {mod.Name}: {problem}");
                }
            }
            OverrideApplier.Use(_enabled.Value ? GameOverrides.Loaded : new List<OverrideFiles.Mod>());
        }

        // "overrides" in the Tool window's console, when the Tool window is installed.
        private void AddConsoleCommand()
        {
            try
            {
                RegisterCommand();
            }
            catch (Exception ex) when (ex is System.IO.FileNotFoundException || ex is TypeLoadException || ex is MissingMethodException)
            {
                // No Tool window: no console, nothing to add.
            }
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private void RegisterCommand()
        {
            ToolWindow.ToolWindow.AddCommand(GameOverrides.Guid, "overrides",
                "overrides | overrides reload  (the overrides mods and how many of their values are written; reload puts the game's values back and reads the files again)",
                Command);
        }

        private string Command(string[] args)
        {
            if (args.Length > 0 && args[0].Equals("reload", StringComparison.OrdinalIgnoreCase))
            {
                return Reload();
            }
            if (GameOverrides.Loaded.Count == 0)
            {
                return "No overrides mods: a folder in BepInEx/plugins with mod.json and overrides/*.json is one.";
            }
            var lines = new List<string>();
            foreach (GameOverrides.ModReport r in GameOverrides.Mods)
            {
                lines.Add($"{r.Name}: {r.Applied} of {r.Overrides} override(s) written so far{(r.UsesPrivate ? ", some on private members" : "")}{(r.Problems.Count > 0 ? $", {r.Problems.Count} problem(s) in its files" : "")}");
                foreach (string p in r.Problems) lines.Add("  " + p);
            }
            if (!_enabled.Value) lines.Add("[General] Enabled is off: nothing is applied.");
            return string.Join("\n", lines.ToArray());
        }

        // An older core has no data mods: the overrides still apply, and the
        // Mods screen does not show them.
        private static void ListOnModsScreen(OverrideFiles.Mod mod)
        {
            try
            {
                Register(mod);
            }
            catch (MissingMethodException)
            {
                Log.LogInfo($"[overrides] The framework core is older than 1.4.0, so {mod.Name} is not listed on the Mods screen.");
            }
            catch (Exception ex)
            {
                Log.LogWarning($"[overrides] {mod.Name} could not be listed on the Mods screen: {ex.Message}");
            }
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static void Register(OverrideFiles.Mod mod)
        {
            OverrideFiles.Manifest m = mod.Manifest;
            string[] authors = m.authors != null && m.authors.Length > 0 ? m.authors : null;
            string description = string.IsNullOrEmpty(m.description) ? "Changes values in the game (overrides, no code)." : m.description;
            if (mod.UsesPrivate)
            {
                description += " Changes private values of the game's scripts.";
            }
            ModFramework.RegisterDataMod(new ModInfo
            {
                Guid = mod.Guid,
                DisplayName = mod.Name,
                Description = description,
                Authors = authors,
                Website = m.website,
            }, mod.Version, mod.ManifestPath);
        }

        // A frame after the load, and again a second later for what the scene's
        // scripts make in their first frames; then which overrides for this
        // scene found nothing.
        private IEnumerator AfterLoad(string scene)
        {
            OverrideApplier.Prune();
            yield return null;
            Report(OverrideApplier.Apply(newRootsOnly: false), "after loading " + scene);
            yield return new WaitForSecondsRealtime(1f);
            Report(OverrideApplier.Apply(newRootsOnly: false), "a second after loading " + scene);
            List<OverrideFiles.Override> missing = OverrideApplier.NotFoundIn(scene);
            if (missing.Count > 0)
            {
                Log.LogInfo($"[overrides] {missing.Count} override(s) for {scene} found no object yet: {string.Join("; ", missing.Take(5).Select(o => o.Mod.Name + ": " + o.Target).ToArray())}{(missing.Count > 5 ? "; ..." : "")}");
            }
        }

        private void Update()
        {
            if (!_enabled.Value || Time.unscaledTime < _nextWatch) return;
            _nextWatch = Time.unscaledTime + WatchSeconds;
            Report(OverrideApplier.Apply(newRootsOnly: true), "on objects that appeared");
        }

        private static void Report(int written, string when)
        {
            if (written > 0)
            {
                Log.LogInfo($"[overrides] Wrote {written} value(s) {when}.");
            }
        }

        internal string Reload()
        {
            int restored = OverrideApplier.TakeBackAll();
            Read();
            int written = _enabled.Value ? OverrideApplier.Apply(newRootsOnly: false) : 0;
            string line = $"Overrides reloaded: {restored} value(s) put back, {GameOverrides.Loaded.Sum(m => m.Overrides.Count)} override(s) read, {written} value(s) written.";
            Log.LogInfo("[overrides] " + line);
            return line;
        }
    }
}
