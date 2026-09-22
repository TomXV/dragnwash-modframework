using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using BepInEx;
using Mono.Cecil;

namespace DragNWash.ModFramework.Mods
{
    // Reads what can be known about a plugin DLL without the plugin's help, so a
    // plain BepInEx plugin that has never heard of the framework still shows up
    // properly on the Mods screen:
    //   - [BepInPlugin] and [BepInDependency] attributes, also for plugins that
    //     BepInEx did not load (a missing dependency, a broken DLL),
    //   - assembly attributes: AssemblyDescription, AssemblyCompany,
    //   - a Thunderstore manifest.json next to the DLL (description, website).
    // DLLs are read with Mono.Cecil, which BepInEx itself ships and uses, so no
    // plugin code runs and nothing is loaded into the game.
    internal static class PluginScanner
    {
        internal sealed class Found
        {
            public string Guid;
            public string Name;
            public string Version;
            public string FullPath;
            public List<string> HardDependencies = new List<string>();
            public List<string> Incompatibilities = new List<string>();
            public string Description;
            public string Authors;
            public string Website;
        }

        private static readonly Dictionary<string, (DateTime Stamp, List<Found> Plugins)> Cache =
            new Dictionary<string, (DateTime, List<Found>)>(StringComparer.OrdinalIgnoreCase);

        // Reading an enum argument such as BepInDependency's flags makes Cecil
        // look up the assembly that defines the enum (BepInEx). Its default
        // resolver only searches next to the game's executable, so point it at
        // BepInEx, the game's managed assemblies and the plugins folder.
        private static DefaultAssemblyResolver _resolver;

        private static DefaultAssemblyResolver Resolver
        {
            get
            {
                if (_resolver == null)
                {
                    _resolver = new DefaultAssemblyResolver();
                    foreach (string dir in new[] { Paths.BepInExAssemblyDirectory, Paths.ManagedPath, Paths.PluginPath, Paths.PatcherPluginPath })
                    {
                        if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                        {
                            _resolver.AddSearchDirectory(dir);
                        }
                    }
                }
                return _resolver;
            }
        }

        private static readonly HashSet<string> Reported = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // The Mods screen scans on a worker thread while mods.list may scan on
        // the game's; the cache and Cecil's resolver are not made for both.
        private static readonly object Gate = new object();

        internal static List<Found> ScanPluginsFolder()
        {
            var all = new List<Found>();
            if (!Directory.Exists(Paths.PluginPath))
            {
                return all;
            }
            foreach (string path in Directory.GetFiles(Paths.PluginPath, "*.dll", SearchOption.AllDirectories))
            {
                all.AddRange(Read(path));
            }
            return all;
        }

        internal static List<Found> Read(string path)
        {
            lock (Gate)
            {
                return ReadLocked(path);
            }
        }

        private static List<Found> ReadLocked(string path)
        {
            try
            {
                DateTime stamp = File.GetLastWriteTimeUtc(path);
                if (Cache.TryGetValue(path, out var cached) && cached.Stamp == stamp)
                {
                    return cached.Plugins;
                }

                var plugins = new List<Found>();
                using (AssemblyDefinition assembly = AssemblyDefinition.ReadAssembly(path, new ReaderParameters { AssemblyResolver = Resolver }))
                {
                    string description = AssemblyString(assembly, "System.Reflection.AssemblyDescriptionAttribute");
                    string company = AssemblyString(assembly, "System.Reflection.AssemblyCompanyAttribute");

                    foreach (TypeDefinition type in assembly.MainModule.Types)
                    {
                        CustomAttribute plugin = type.CustomAttributes.FirstOrDefault(a => a.AttributeType.FullName == "BepInEx.BepInPlugin");
                        if (plugin == null || plugin.ConstructorArguments.Count < 3)
                        {
                            continue;
                        }
                        var found = new Found
                        {
                            Guid = plugin.ConstructorArguments[0].Value as string,
                            Name = plugin.ConstructorArguments[1].Value as string,
                            Version = plugin.ConstructorArguments[2].Value as string,
                            FullPath = path,
                            Description = description,
                            Authors = company,
                        };
                        foreach (CustomAttribute a in type.CustomAttributes)
                        {
                            if (a.AttributeType.FullName == "BepInEx.BepInDependency" && a.ConstructorArguments.Count > 0)
                            {
                                bool hard = true;
                                if (a.ConstructorArguments.Count > 1 && a.ConstructorArguments[1].Value is int flags)
                                {
                                    hard = (flags & 1) != 0;
                                }
                                if (hard)
                                {
                                    found.HardDependencies.Add(a.ConstructorArguments[0].Value as string);
                                }
                            }
                            else if (a.AttributeType.FullName == "BepInEx.BepInIncompatibility" && a.ConstructorArguments.Count > 0)
                            {
                                found.Incompatibilities.Add(a.ConstructorArguments[0].Value as string);
                            }
                        }
                        plugins.Add(found);
                    }
                }

                ApplyManifest(path, plugins);
                Cache[path] = (stamp, plugins);
                return plugins;
            }
            catch (BadImageFormatException ex)
            {
                // Not a .NET assembly (a native DLL some mods ship next to their
                // own): not a plugin, and nothing is wrong. BepInEx skips these at
                // Debug level too.
                if (Reported.Add(path))
                {
                    ModFramework.Log.LogDebug($"Skipped {Path.GetFileName(path)} on the Mods screen: not a .NET assembly ({ex.Message})");
                }
                return new List<Found>();
            }
            catch (Exception ex)
            {
                // Unreadable: not a plugin we can describe.
                if (Reported.Add(path))
                {
                    ModFramework.Log.LogInfo($"Could not read {Path.GetFileName(path)} for the Mods screen: {ex.GetType().Name}: {ex.Message}");
                }
                return new List<Found>();
            }
        }

        private static string AssemblyString(AssemblyDefinition assembly, string attribute)
        {
            CustomAttribute a = assembly.CustomAttributes.FirstOrDefault(x => x.AttributeType.FullName == attribute);
            string value = a != null && a.ConstructorArguments.Count > 0 ? a.ConstructorArguments[0].Value as string : null;
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        // Thunderstore packages put manifest.json in the package folder, one or two
        // levels above the DLL. Only its description and website are used.
        private static void ApplyManifest(string dllPath, List<Found> plugins)
        {
            if (plugins.Count == 0)
            {
                return;
            }
            string root = Path.GetFullPath(Paths.PluginPath).TrimEnd('\\', '/');
            string dir = Path.GetDirectoryName(Path.GetFullPath(dllPath));
            for (int depth = 0; depth < 3 && dir != null && dir.Length > root.Length; depth++)
            {
                string manifest = Path.Combine(dir, "manifest.json");
                if (File.Exists(manifest))
                {
                    string json = File.ReadAllText(manifest);
                    string description = JsonString(json, "description");
                    string website = JsonString(json, "website_url");
                    foreach (Found p in plugins)
                    {
                        p.Description = description ?? p.Description;
                        p.Website = website ?? p.Website;
                    }
                    return;
                }
                dir = Path.GetDirectoryName(dir);
            }
        }

        private static string JsonString(string json, string key)
        {
            Match m = Regex.Match(json, "\"" + Regex.Escape(key) + "\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"");
            if (!m.Success)
            {
                return null;
            }
            string value = Regex.Unescape(m.Groups[1].Value).Trim();
            return value.Length == 0 ? null : value;
        }
    }
}
