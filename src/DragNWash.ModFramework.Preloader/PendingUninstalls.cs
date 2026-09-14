using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace DragNWash.ModFramework
{
    // Mods uninstalled from the Mods screen.
    //
    // A running game has the mod's DLLs loaded, so the Mods screen only records
    // the wish, and the preloader patcher removes the files at the next launch,
    // before BepInEx loads any plugin (the same way switching off works).
    //
    // What goes: the mod's folder under BepInEx/plugins (or its DLL, when it sits
    // in BepInEx/plugins directly). When the folder holds mod-install.json, the
    // file the Drag'n Wash installers leave, its "keep" paths (the player's own
    // data) stay and its "configFiles" go too; otherwise the config file named
    // after the plugin's GUID goes, as BepInEx names it. The framework itself,
    // its libraries and BepInEx are never removed from here.
    //
    // BepInEx/config/com.tomxv.dragnwash.modframework.uninstall.txt, one per line:
    //   unit <TAB> GUID <TAB> name <TAB> version
    // where unit is a folder name under BepInEx/plugins or a DLL file name there.
    //
    // This file is compiled into both the patcher and the framework.
    internal static class PendingUninstalls
    {
        internal const string FileName = "com.tomxv.dragnwash.modframework.uninstall.txt";
        internal const string FrameworkPrefix = "DragNWash.ModFramework";
        internal const string ManifestName = "mod-install.json";

        internal sealed class Record
        {
            public string Unit;
            public string Guid;
            public string Name;
            public string Version;
        }

        // The unit a plugin DLL belongs to: its top folder under BepInEx/plugins,
        // or the DLL itself. Null for the framework and anything unsafe.
        internal static string UnitOf(string relativePath)
        {
            string rel = DisabledMods.NormalizeRelative(relativePath);
            if (rel == null)
            {
                return null;
            }
            int slash = rel.IndexOf('/');
            string unit = slash < 0 ? rel : rel.Substring(0, slash);
            return IsSafeUnit(unit) ? unit : null;
        }

        private static bool IsSafeUnit(string unit)
        {
            return !string.IsNullOrEmpty(unit) && unit != "." && unit != ".." &&
                   unit.IndexOfAny(new[] { '/', '\\', ':' }) < 0 &&
                   !unit.StartsWith(FrameworkPrefix, StringComparison.OrdinalIgnoreCase);
        }

        internal static List<Record> Read(string configPath)
        {
            var records = new List<Record>();
            string path = Path.Combine(configPath, FileName);
            if (!File.Exists(path))
            {
                return records;
            }
            foreach (string raw in File.ReadAllLines(path, Encoding.UTF8))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
                {
                    continue;
                }
                string[] cells = line.Split('\t');
                if (!IsSafeUnit(cells[0]))
                {
                    continue;
                }
                records.Add(new Record
                {
                    Unit = cells[0],
                    Guid = cells.Length > 1 ? cells[1] : "",
                    Name = cells.Length > 2 ? cells[2] : "",
                    Version = cells.Length > 3 ? cells[3] : "",
                });
            }
            return records;
        }

        internal static void Write(string configPath, IEnumerable<Record> records)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Mods uninstalled from the Mods screen. Removed at the next launch.");
            sb.AppendLine("# One per line: folder or DLL under BepInEx/plugins <TAB> GUID <TAB> name <TAB> version");
            foreach (Record r in records)
            {
                sb.Append(r.Unit).Append('\t').Append(Clean(r.Guid)).Append('\t')
                  .Append(Clean(r.Name)).Append('\t').Append(Clean(r.Version)).AppendLine();
            }
            Directory.CreateDirectory(configPath);
            File.WriteAllText(Path.Combine(configPath, FileName), sb.ToString(), new UTF8Encoding(false));
        }

        // Called by the preloader patcher before plugins load.
        internal static void Apply(string pluginPath, string configPath, Action<string> log)
        {
            List<Record> records = Read(configPath);
            if (records.Count == 0)
            {
                return;
            }
            var left = new List<Record>();
            foreach (Record r in records)
            {
                try
                {
                    Remove(pluginPath, configPath, r, log);
                    ForgetUnit(configPath, r.Unit);
                }
                catch (Exception ex)
                {
                    log($"Could not uninstall {r.Name} ({r.Unit}): {ex.Message}");
                    left.Add(r);
                }
            }
            if (left.Count == 0)
            {
                File.Delete(Path.Combine(configPath, FileName));
            }
            else
            {
                Write(configPath, left);
            }
        }

        private static void Remove(string pluginPath, string configPath, Record r, Action<string> log)
        {
            string target = Path.Combine(pluginPath, r.Unit);
            if (File.Exists(target) || File.Exists(target + DisabledMods.DisabledSuffix))
            {
                DeleteFile(target);
                DeleteFile(target + DisabledMods.DisabledSuffix);
                DeleteFile(GuidConfig(configPath, r.Guid));
                log($"Uninstalled {r.Name} ({r.Unit})");
                return;
            }
            if (!Directory.Exists(target))
            {
                return;
            }

            string[] keep = new string[0];
            string[] configFiles = null;
            string manifest = Path.Combine(target, ManifestName);
            if (File.Exists(manifest))
            {
                string json = File.ReadAllText(manifest, Encoding.UTF8);
                keep = StringArray(json, "keep")
                    .Select(k => k.Replace('\\', '/').Trim('/'))
                    .Where(k => k.StartsWith(r.Unit + "/", StringComparison.OrdinalIgnoreCase))
                    .Select(k => k.Substring(r.Unit.Length + 1))
                    .Where(k => k.Length > 0 && k.Split('/').All(p => p.Length > 0 && p != "." && p != ".."))
                    .ToArray();
                configFiles = StringArray(json, "configFiles")
                    .Where(f => f.IndexOfAny(new[] { '/', '\\', ':' }) < 0 && !f.Contains("..") && !f.StartsWith("com.tomxv.dragnwash.modframework", StringComparison.OrdinalIgnoreCase))
                    .ToArray();
            }

            string[] kept = keep.Where(k => File.Exists(Path.Combine(target, k)) || Directory.Exists(Path.Combine(target, k))).ToArray();
            if (kept.Length == 0)
            {
                Directory.Delete(target, true);
            }
            else
            {
                Prune(target, "", kept);
            }
            if (configFiles == null)
            {
                DeleteFile(GuidConfig(configPath, r.Guid));
            }
            else
            {
                foreach (string file in configFiles)
                {
                    DeleteFile(Path.Combine(configPath, file));
                }
            }
            log($"Uninstalled {r.Name} ({r.Unit}){(kept.Length > 0 ? ", kept " + string.Join(", ", kept) : "")}");
        }

        private static void Prune(string dir, string rel, string[] keep)
        {
            foreach (string entry in Directory.GetFileSystemEntries(dir))
            {
                string childRel = (rel.Length == 0 ? "" : rel + "/") + Path.GetFileName(entry);
                if (keep.Any(k => string.Equals(k, childRel, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }
                bool onPath = keep.Any(k => k.StartsWith(childRel + "/", StringComparison.OrdinalIgnoreCase));
                if (Directory.Exists(entry))
                {
                    if (onPath)
                    {
                        Prune(entry, childRel, keep);
                    }
                    else
                    {
                        Directory.Delete(entry, true);
                    }
                }
                else
                {
                    DeleteFile(entry);
                }
            }
        }

        // Drops what the switch-off lists say about the removed files.
        private static void ForgetUnit(string configPath, string unit)
        {
            foreach (string name in new[] { DisabledMods.DesiredFileName, DisabledMods.StateFileName })
            {
                string path = Path.Combine(configPath, name);
                if (!File.Exists(path))
                {
                    continue;
                }
                string[] lines = File.ReadAllLines(path, Encoding.UTF8);
                string[] kept = lines.Where(l =>
                {
                    string rel = l.Split('\t')[0].Trim().Replace('\\', '/');
                    return !(string.Equals(rel, unit, StringComparison.OrdinalIgnoreCase) || rel.StartsWith(unit + "/", StringComparison.OrdinalIgnoreCase));
                }).ToArray();
                if (kept.Length != lines.Length)
                {
                    File.WriteAllLines(path, kept, new UTF8Encoding(false));
                }
            }
        }

        // The strings of a top-level JSON array, e.g. "keep": ["a", "b"]. Enough for
        // the flat mod-install.json the installers write; no JSON library is loaded
        // this early.
        private static IEnumerable<string> StringArray(string json, string name)
        {
            Match array = Regex.Match(json, "\"" + Regex.Escape(name) + "\"\\s*:\\s*\\[(.*?)\\]", RegexOptions.Singleline);
            if (!array.Success)
            {
                yield break;
            }
            foreach (Match s in Regex.Matches(array.Groups[1].Value, "\"((?:[^\"\\\\]|\\\\.)*)\""))
            {
                yield return Regex.Unescape(s.Groups[1].Value.Replace("\\/", "/"));
            }
        }

        // BepInEx names a plugin's config file after its GUID. Null when the GUID
        // could point outside BepInEx/config.
        private static string GuidConfig(string configPath, string guid)
        {
            if (string.IsNullOrEmpty(guid) || guid.IndexOfAny(new[] { '/', '\\', ':' }) >= 0 || guid.Contains("..") ||
                guid.StartsWith("com.tomxv.dragnwash.modframework", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }
            return Path.Combine(configPath, guid + ".cfg");
        }

        private static void DeleteFile(string path)
        {
            if (path != null && File.Exists(path))
            {
                File.SetAttributes(path, FileAttributes.Normal);
                File.Delete(path);
            }
        }

        private static string Clean(string value)
        {
            return (value ?? "").Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
        }
    }
}
