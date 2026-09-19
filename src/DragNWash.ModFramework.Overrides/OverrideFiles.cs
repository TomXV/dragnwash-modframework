using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace DragNWash.ModFramework.Overrides
{
    // The files of an overrides mod, read with JsonUtility:
    //
    //   BepInEx/plugins/<Mod>/mod.json          name, authors, description, version
    //   BepInEx/plugins/<Mod>/overrides/*.json  { "format": 1, "overrides": [ ... ] }
    //
    // A folder with mod.json.disabled is a mod the player switched off.
    internal static class OverrideFiles
    {
        internal const int Format = 1;
        // A file larger than this is not read.
        private const long MaxFileBytes = 2L * 1024 * 1024;

#pragma warning disable 0649 // filled by JsonUtility
        [Serializable]
        internal sealed class Manifest
        {
            public string guid;
            public string name;
            public string[] authors;
            public string author;
            public string description;
            public string version;
            public string website;
        }

        [Serializable]
        internal sealed class File
        {
            public int format;
            public Entry[] overrides;
        }

        [Serializable]
        internal sealed class Entry
        {
            public string scene;
            public string path;
            public string component;
            public int index;
            public string member;
            public bool @private;
            public string material;
            public string property;
            public string value;
        }
#pragma warning restore 0649

        internal sealed class Mod
        {
            public string Guid;
            public string Name;
            public string Version;
            public string Folder;
            public string ManifestPath;
            // Place in the load order (folder name order); a later mod wins.
            public int Order;
            public Manifest Manifest;
            public readonly List<Override> Overrides = new List<Override>();
            public readonly List<string> Problems = new List<string>();
            public bool UsesPrivate => Overrides.Any(o => o.Private);
        }

        // One override, checked once when read.
        internal sealed class Override
        {
            public Mod Mod;
            public string File;
            public int Line;
            public string Scene;
            public string Path;
            public string Root;
            public string Rest;
            public string Component;
            public int Index;
            public string Member;
            public bool Private;
            public string Material;
            public string Property;
            public string Value;

            public string Target => Material != null
                ? $"{Path} [{Component ?? "Renderer"}] material {Material} {Property}"
                : $"{Path} [{Component}{(Index > 0 ? " #" + Index : "")}] {Member}";

            // Two overrides on the same thing, whichever mod they come from.
            public string Key => $"{Scene}|{Path}|{Component}|{Index}|{Member}|{Material}|{Property}";
        }

        internal static List<Mod> Scan(string pluginsFolder)
        {
            var mods = new List<Mod>();
            if (!Directory.Exists(pluginsFolder)) return mods;
            foreach (string folder in Directory.GetDirectories(pluginsFolder).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                string manifest = System.IO.Path.Combine(folder, "mod.json");
                string overrides = System.IO.Path.Combine(folder, "overrides");
                if (!System.IO.File.Exists(manifest) || !Directory.Exists(overrides)) continue;
                Mod mod = Read(folder, manifest, overrides);
                mod.Order = mods.Count;
                mods.Add(mod);
            }
            return mods;
        }

        private static Mod Read(string folder, string manifestPath, string overridesFolder)
        {
            string folderName = System.IO.Path.GetFileName(folder);
            var mod = new Mod { Folder = folder, ManifestPath = manifestPath, Name = folderName };
            try
            {
                mod.Manifest = JsonUtility.FromJson<Manifest>(System.IO.File.ReadAllText(manifestPath)) ?? new Manifest();
            }
            catch (Exception ex)
            {
                mod.Manifest = new Manifest();
                mod.Problems.Add("mod.json could not be read: " + ex.Message);
            }
            if (!string.IsNullOrEmpty(mod.Manifest.name)) mod.Name = mod.Manifest.name;
            mod.Version = string.IsNullOrEmpty(mod.Manifest.version) ? "1.0.0" : mod.Manifest.version;
            mod.Guid = !string.IsNullOrEmpty(mod.Manifest.guid) ? mod.Manifest.guid : "overrides." + Slug(folderName);

            foreach (string path in Directory.GetFiles(overridesFolder, "*.json").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                string name = System.IO.Path.GetFileName(path);
                try
                {
                    if (new FileInfo(path).Length > MaxFileBytes)
                    {
                        mod.Problems.Add($"{name} is larger than {MaxFileBytes / (1024 * 1024)} MB and was not read.");
                        continue;
                    }
                    File file = JsonUtility.FromJson<File>(System.IO.File.ReadAllText(path));
                    if (file == null || file.overrides == null)
                    {
                        mod.Problems.Add($"{name} has no \"overrides\" list.");
                        continue;
                    }
                    if (file.format > Format)
                    {
                        mod.Problems.Add($"{name} is format {file.format}; this library reads up to {Format}. Update Drag'n Wash ModFramework.");
                        continue;
                    }
                    for (int i = 0; i < file.overrides.Length; i++)
                    {
                        Override o = Check(mod, name, i + 1, file.overrides[i]);
                        if (o != null) mod.Overrides.Add(o);
                    }
                }
                catch (Exception ex)
                {
                    mod.Problems.Add($"{name} could not be read: {ex.Message}");
                }
            }
            return mod;
        }

        private static Override Check(Mod mod, string file, int line, Entry e)
        {
            string where = $"{file} #{line}";
            if (e == null) return null;
            string path = (e.path ?? "").Trim().Trim('/');
            if (path.Length == 0) { mod.Problems.Add($"{where}: no path."); return null; }
            bool material = !string.IsNullOrEmpty(e.material);
            if (material ? string.IsNullOrEmpty(e.property) : (string.IsNullOrEmpty(e.component) || string.IsNullOrEmpty(e.member)))
            {
                mod.Problems.Add($"{where}: needs {(material ? "\"property\"" : "\"component\" and \"member\"")}.");
                return null;
            }
            if (e.value == null) { mod.Problems.Add($"{where}: no value."); return null; }
            int slash = path.IndexOf('/');
            return new Override
            {
                Mod = mod,
                File = file,
                Line = line,
                Scene = string.IsNullOrEmpty(e.scene) ? null : e.scene.Trim(),
                Path = path,
                Root = slash < 0 ? path : path.Substring(0, slash),
                Rest = slash < 0 ? null : path.Substring(slash + 1),
                Component = string.IsNullOrEmpty(e.component) ? null : e.component.Trim(),
                Index = Math.Max(0, e.index),
                Member = material ? null : e.member.Trim(),
                Private = e.@private,
                Material = material ? e.material.Trim() : null,
                Property = material ? e.property.Trim() : null,
                Value = e.value,
            };
        }

        private static string Slug(string name)
        {
            var chars = name.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray();
            return new string(chars).Trim('-');
        }
    }
}
