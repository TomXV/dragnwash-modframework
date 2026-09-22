using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using BepInEx.Bootstrap;

namespace DragNWash.ModFramework.Mods
{
    // Everything the Mods screen lists: plugins BepInEx loaded, plus plugins that
    // are switched off (their DLL is renamed, so BepInEx no longer knows them and
    // the framework remembers them from the switched-off list).
    internal static class ModCatalog
    {
        internal sealed class Entry
        {
            public string Guid;
            public string Name;
            public string Version;
            public ModInfo Info;

            public string DisplayName => string.IsNullOrEmpty(Info?.DisplayName) ? Name : Info.DisplayName;

            // What the mod registered through the API wins; otherwise what could be
            // read from the DLL and its package, so plain BepInEx plugins get details too.
            public PluginScanner.Found Scanned;
            public string Description => !string.IsNullOrEmpty(Info?.Description) ? Info.Description : Scanned?.Description;
            public string Authors => Info?.Authors != null && Info.Authors.Length > 0 ? string.Join(", ", Info.Authors) : Scanned?.Authors;
            public string Website => !string.IsNullOrEmpty(Info?.Website) ? Info.Website : Scanned?.Website;

            // Why a plugin in BepInEx/plugins did not load, when that can be told:
            // a label ("Needs", "Cannot run together with") and the mods it names,
            // or just a label.
            public string ProblemLabel;
            public List<string> ProblemGuids = new List<string>();

            public bool IsPatcher;

            public bool IsLibrary => Info?.IsLibrary == true;

            // Path under BepInEx/plugins; null when the plugin lives elsewhere and
            // cannot be switched off from here.
            public string RelativePath;

            public bool Loaded;
            public bool IsFramework;

            // What the player wants for the next launch.
            public bool WantOn;

            // GUIDs of loaded plugins that cannot load without this one.
            public List<string> Dependents = new List<string>();

            // Mods this one cannot load without, with the oldest version it accepts.
            public List<KeyValuePair<string, Version>> Uses = new List<KeyValuePair<string, Version>>();

            public bool CanSwitch => RelativePath != null && !IsFramework && !IsPatcher;

            // Uninstalling removes the mod's folder under BepInEx/plugins (or its DLL
            // there) at the next launch. The framework and its libraries cannot be
            // uninstalled from here.
            public string Unit => RelativePath == null ? null : PendingUninstalls.UnitOf(RelativePath);
            public bool CanUninstall => Unit != null && !IsFramework && !IsPatcher;
            public bool PendingUninstall;
        }

        private static bool IsManifest(string rel) => string.Equals(Path.GetFileName(rel), "mod.json", StringComparison.OrdinalIgnoreCase);

        // What only the plugin files themselves can tell: every DLL in
        // BepInEx/plugins read with Cecil, the switched-off ones too, and the
        // preloader patchers. The slow part of listing the mods, so the Mods
        // screen reads them off the frame: this touches files only, never a
        // Unity object, and is safe on a worker thread.
        internal sealed class FileScan
        {
            public List<PluginScanner.Found> Folder = new List<PluginScanner.Found>();

            // By path under BepInEx/plugins, as the switched-off lists give it.
            public Dictionary<string, List<PluginScanner.Found>> SwitchedOff =
                new Dictionary<string, List<PluginScanner.Found>>(StringComparer.OrdinalIgnoreCase);

            public List<KeyValuePair<string, string>> Patchers = new List<KeyValuePair<string, string>>();
        }

        internal static FileScan ScanFiles()
        {
            var scan = new FileScan { Folder = PluginScanner.ScanPluginsFolder() };
            IEnumerable<string> off = DisabledMods.ReadState(Paths.ConfigPath)
                .Concat(DisabledMods.ReadDesired(Paths.ConfigPath).Select(r => r.RelativePath));
            foreach (string rel in off)
            {
                if (rel == null || IsManifest(rel) || scan.SwitchedOff.ContainsKey(rel))
                {
                    continue;
                }
                string path = Path.Combine(Paths.PluginPath, rel) + DisabledMods.DisabledSuffix;
                if (File.Exists(path))
                {
                    scan.SwitchedOff[rel] = PluginScanner.Read(path);
                }
            }
            string patchers = Paths.PatcherPluginPath;
            if (Directory.Exists(patchers))
            {
                foreach (string path in Directory.GetFiles(patchers, "*.dll", SearchOption.AllDirectories))
                {
                    if (!string.Equals(Path.GetFileName(path), "DragNWash.ModFramework.Preloader.dll", StringComparison.OrdinalIgnoreCase))
                    {
                        scan.Patchers.Add(new KeyValuePair<string, string>(path, SafeVersion(path)));
                    }
                }
            }
            return scan;
        }

        internal static List<Entry> Build()
        {
            return Build(ScanFiles());
        }

        // Without a scan (null), only what BepInEx and the framework already
        // know: the loaded mods and the ones switched off from here, without
        // what their files say about them. Quick enough for any frame.
        internal static List<Entry> Build(FileScan scan)
        {
            var entries = new List<Entry>();
            List<PluginScanner.Found> scanned = scan?.Folder ?? new List<PluginScanner.Found>();
            List<DisabledMods.Record> desired = DisabledMods.ReadDesired(Paths.ConfigPath);
            var desiredPaths = new HashSet<string>(desired.Select(r => r.RelativePath), StringComparer.OrdinalIgnoreCase);

            foreach (PluginInfo plugin in Chainloader.PluginInfos.Values)
            {
                BepInPlugin meta = plugin.Metadata;
                string rel = RelativeToPlugins(plugin.Location);
                entries.Add(new Entry
                {
                    Guid = meta.GUID,
                    Name = meta.Name,
                    Version = meta.Version.ToString(),
                    Info = ModFramework.GetInfo(meta.GUID),
                    RelativePath = rel,
                    Loaded = true,
                    IsFramework = meta.GUID == ModFramework.Guid || string.Equals(rel, FrameworkRelativePath, StringComparison.OrdinalIgnoreCase),
                    WantOn = rel == null || !desiredPaths.Contains(rel),
                    Scanned = scanned.FirstOrDefault(s => s.Guid == meta.GUID),
                });
            }

            // Mods with no DLL, listed by the library that read their files
            // (ModFramework.RegisterDataMod): on for this session; their mod.json
            // is what gets renamed to switch them off.
            foreach (ModFramework.DataMod data in ModFramework.AllDataMods())
            {
                string rel = RelativeToPlugins(data.ManifestPath);
                if (rel == null || entries.Any(e => e.Guid == data.Info.Guid))
                {
                    continue;
                }
                entries.Add(new Entry
                {
                    Guid = data.Info.Guid,
                    Name = data.Info.DisplayName ?? data.Info.Guid,
                    Version = data.Version,
                    Info = data.Info,
                    RelativePath = rel,
                    Loaded = true,
                    WantOn = !desiredPaths.Contains(rel),
                });
            }

            // Plugins BepInEx found in the folder but did not load.
            var loadedGuids = new HashSet<string>(Chainloader.PluginInfos.Keys);
            foreach (PluginScanner.Found s in scanned)
            {
                if (s.Guid == null || loadedGuids.Contains(s.Guid))
                {
                    continue;
                }
                string rel = RelativeToPlugins(s.FullPath);
                if (rel == null || entries.Any(e => e.Guid == s.Guid && string.Equals(e.RelativePath, rel, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }
                var entry = new Entry
                {
                    Guid = s.Guid,
                    Name = s.Name,
                    Version = s.Version,
                    Info = ModFramework.GetInfo(s.Guid),
                    Scanned = s,
                    RelativePath = rel,
                    Loaded = false,
                    WantOn = !desiredPaths.Contains(rel),
                };
                List<string> missing = s.HardDependencies.Where(g => !loadedGuids.Contains(g)).ToList();
                List<string> conflicts = s.Incompatibilities.Where(g => loadedGuids.Contains(g)).ToList();
                if (missing.Count > 0)
                {
                    entry.ProblemLabel = TextNeeds;
                    entry.ProblemGuids = missing;
                }
                else if (conflicts.Count > 0)
                {
                    entry.ProblemLabel = TextConflicts;
                    entry.ProblemGuids = conflicts;
                }
                else if (entry.WantOn)
                {
                    entry.ProblemLabel = TextNotLoaded;
                }
                entries.Add(entry);
            }

            // Preloader patchers run before plugins and cannot be switched from here.
            foreach (KeyValuePair<string, string> patcher in scan?.Patchers ?? new List<KeyValuePair<string, string>>())
            {
                entries.Add(new Entry
                {
                    Guid = null,
                    Name = Path.GetFileNameWithoutExtension(patcher.Key),
                    Version = patcher.Value,
                    Loaded = true,
                    WantOn = true,
                    IsPatcher = true,
                    ProblemLabel = TextPatcher,
                });
            }

            // Plugins the preloader renamed at this launch and the player has since
            // switched back on: not loaded, no longer in the list, but still there.
            foreach (string rel in scan != null ? DisabledMods.ReadState(Paths.ConfigPath) : new HashSet<string>())
            {
                if (desiredPaths.Contains(rel) || entries.Any(e => string.Equals(e.RelativePath, rel, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }
                string off = Path.Combine(Paths.PluginPath, rel) + DisabledMods.DisabledSuffix;
                if (!File.Exists(off))
                {
                    continue;
                }
                // A data mod's mod.json is no assembly; its folder names it.
                List<PluginScanner.Found> inFile = !IsManifest(rel) && scan.SwitchedOff.TryGetValue(rel, out List<PluginScanner.Found> read) ? read : new List<PluginScanner.Found>();
                if (inFile.Count == 0)
                {
                    entries.Add(new Entry { Name = IsManifest(rel) ? Path.GetFileName(Path.GetDirectoryName(rel)) : Path.GetFileNameWithoutExtension(rel), RelativePath = rel, Loaded = false, WantOn = true });
                    continue;
                }
                foreach (PluginScanner.Found s in inFile)
                {
                    entries.Add(new Entry
                    {
                        Guid = s.Guid,
                        Name = s.Name,
                        Version = s.Version,
                        Info = ModFramework.GetInfo(s.Guid),
                        Scanned = s,
                        RelativePath = rel,
                        Loaded = false,
                        WantOn = true,
                    });
                }
            }

            foreach (DisabledMods.Record record in desired)
            {
                if (entries.Any(e => string.Equals(e.RelativePath, record.RelativePath, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }
                entries.Add(new Entry
                {
                    Guid = record.Guid,
                    Name = string.IsNullOrEmpty(record.Name) ? Path.GetFileNameWithoutExtension(record.RelativePath) : record.Name,
                    Version = record.Version,
                    RelativePath = record.RelativePath,
                    Loaded = false,
                    WantOn = false,
                    Info = ModFramework.GetInfo(record.Guid),
                    Scanned = scan != null && scan.SwitchedOff.TryGetValue(record.RelativePath, out List<PluginScanner.Found> read)
                        ? read.FirstOrDefault(s => s.Guid == record.Guid)
                        : null,
                });
            }

            foreach (PluginInfo plugin in Chainloader.PluginInfos.Values)
            {
                foreach (BepInDependency dep in plugin.Dependencies)
                {
                    if ((dep.Flags & BepInDependency.DependencyFlags.HardDependency) == 0)
                    {
                        continue;
                    }
                    Entry target = entries.FirstOrDefault(e => e.Guid == dep.DependencyGUID);
                    target?.Dependents.Add(plugin.Metadata.GUID);
                    entries.FirstOrDefault(e => e.Loaded && e.Guid == plugin.Metadata.GUID)?
                        .Uses.Add(new KeyValuePair<string, Version>(dep.DependencyGUID, dep.MinimumVersion));
                }
            }

            var pending = new HashSet<string>(PendingUninstalls.Read(Paths.ConfigPath).Select(r => r.Unit), StringComparer.OrdinalIgnoreCase);
            foreach (Entry e in entries)
            {
                e.PendingUninstall = e.CanUninstall && pending.Contains(e.Unit);
            }

            return entries
                .OrderBy(e => e.IsFramework ? 0 : 1)
                .ThenBy(e => e.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        // Records what the player wants and writes it for the preloader. Every
        // plugin in the same DLL follows, since the file is what gets renamed.
        internal static void SetWantOn(List<Entry> entries, Entry entry, bool on)
        {
            if (!entry.CanSwitch)
            {
                return;
            }
            foreach (Entry e in entries)
            {
                if (string.Equals(e.RelativePath, entry.RelativePath, StringComparison.OrdinalIgnoreCase))
                {
                    e.WantOn = on;
                }
            }

            var records = new List<DisabledMods.Record>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Entry e in entries)
            {
                if (e.WantOn || e.RelativePath == null || !seen.Add(e.RelativePath))
                {
                    continue;
                }
                records.Add(new DisabledMods.Record { RelativePath = e.RelativePath, Guid = e.Guid, Name = e.DisplayName, Version = e.Version });
            }
            DisabledMods.WriteDesired(Paths.ConfigPath, records);
            ModFramework.Log.LogInfo($"{entry.Name} will be switched {(on ? "on" : "off")} at the next launch.");
        }

        // Records what the player wants and writes it for the preloader. Every
        // plugin in the same folder follows, since the folder is what gets removed.
        internal static void SetUninstall(List<Entry> entries, Entry entry, bool uninstall)
        {
            if (!entry.CanUninstall)
            {
                return;
            }
            foreach (Entry e in entries)
            {
                if (e.CanUninstall && string.Equals(e.Unit, entry.Unit, StringComparison.OrdinalIgnoreCase))
                {
                    e.PendingUninstall = uninstall;
                }
            }

            var records = new List<PendingUninstalls.Record>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Entry e in entries)
            {
                if (e.PendingUninstall && seen.Add(e.Unit))
                {
                    records.Add(new PendingUninstalls.Record { Unit = e.Unit, Guid = e.Guid, Name = e.DisplayName, Version = e.Version });
                }
            }
            PendingUninstalls.Write(Paths.ConfigPath, records);
            ModFramework.Log.LogInfo(uninstall
                ? $"{entry.Name} will be uninstalled at the next launch ({entry.Unit})."
                : $"{entry.Name} will no longer be uninstalled.");
        }

        internal const string TextNeeds = "Not loaded. It needs";
        internal const string TextConflicts = "Not loaded. It cannot run together with";
        internal const string TextNotLoaded = "Not loaded. See BepInEx/LogOutput.log for why.";
        internal const string TextPatcher = "Preloader patcher. It runs before other mods and cannot be switched off here.";

        private static string SafeVersion(string path)
        {
            try
            {
                return System.Reflection.AssemblyName.GetAssemblyName(path).Version?.ToString();
            }
            catch
            {
                return null;
            }
        }

        internal static string NameOf(List<Entry> entries, string guid)
        {
            Entry e = entries.FirstOrDefault(x => x.Guid == guid && x.Guid != null);
            return e != null ? e.DisplayName : guid;
        }

        // In a list of what a mod uses, the framework's own libraries are named
        // without the shared prefix ("Text", not "Drag'n Wash ModFramework: Text").
        internal static string ShortNameOf(List<Entry> entries, string guid)
        {
            const string Prefix = "Drag'n Wash ModFramework: ";
            string name = NameOf(entries, guid);
            if (guid == ModFramework.Guid)
            {
                return "ModFramework";
            }
            return name.StartsWith(Prefix, StringComparison.Ordinal) ? name.Substring(Prefix.Length) : name;
        }

        private static string FrameworkRelativePath => RelativeToPlugins(typeof(ModCatalog).Assembly.Location);

        private static string RelativeToPlugins(string location)
        {
            if (string.IsNullOrEmpty(location))
            {
                return null;
            }
            try
            {
                string root = Path.GetFullPath(Paths.PluginPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
                string full = Path.GetFullPath(location);
                if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }
                return DisabledMods.NormalizeRelative(full.Substring(root.Length));
            }
            catch
            {
                return null;
            }
        }
    }
}
