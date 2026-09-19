using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace DragNWash.ModFramework
{
    // Mods switched off from the Mods screen.
    //
    // A plugin cannot be unloaded from a running game, so the Mods screen only
    // records what the player wants, and the preloader patcher applies it on the
    // next launch, before BepInEx loads any plugin: a disabled plugin's DLL is
    // renamed to .dll.disabled, and renamed back when it is switched on again.
    //
    // Two files in BepInEx/config:
    //   *.disabled.txt  what the player wants off (written by the Mods screen)
    //   *.state.txt     files the patcher itself renamed, so it never touches a
    //                   .dll.disabled that someone else made by hand
    //
    // This file is compiled into both the patcher and the framework.
    internal static class DisabledMods
    {
        internal const string DesiredFileName = "com.tomxv.dragnwash.modframework.disabled.txt";
        internal const string StateFileName = "com.tomxv.dragnwash.modframework.state.txt";
        internal const string DisabledSuffix = ".disabled";

        internal sealed class Record
        {
            // Path of the plugin DLL relative to BepInEx/plugins, with '/' separators.
            public string RelativePath;
            public string Guid;
            public string Name;
            public string Version;
        }

        internal static List<Record> ReadDesired(string configPath)
        {
            var records = new List<Record>();
            string path = Path.Combine(configPath, DesiredFileName);
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
                string rel = NormalizeRelative(cells[0]);
                if (rel == null)
                {
                    continue;
                }
                records.Add(new Record
                {
                    RelativePath = rel,
                    Guid = cells.Length > 1 ? cells[1] : "",
                    Name = cells.Length > 2 ? cells[2] : "",
                    Version = cells.Length > 3 ? cells[3] : "",
                });
            }
            return records;
        }

        internal static void WriteDesired(string configPath, IEnumerable<Record> records)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Mods switched off from the Mods screen. Applied at the next launch.");
            sb.AppendLine("# One per line: path under BepInEx/plugins <TAB> GUID <TAB> name <TAB> version");
            foreach (Record r in records)
            {
                sb.Append(r.RelativePath).Append('\t').Append(Clean(r.Guid)).Append('\t')
                  .Append(Clean(r.Name)).Append('\t').Append(Clean(r.Version)).AppendLine();
            }
            Directory.CreateDirectory(configPath);
            File.WriteAllText(Path.Combine(configPath, DesiredFileName), sb.ToString(), new UTF8Encoding(false));
        }

        internal static HashSet<string> ReadState(string configPath)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string path = Path.Combine(configPath, StateFileName);
            if (!File.Exists(path))
            {
                return set;
            }
            foreach (string raw in File.ReadAllLines(path, Encoding.UTF8))
            {
                string rel = NormalizeRelative(raw.Trim());
                if (rel != null && !raw.TrimStart().StartsWith("#", StringComparison.Ordinal))
                {
                    set.Add(rel);
                }
            }
            return set;
        }

        // Called by the preloader patcher before plugins load.
        internal static void Apply(string pluginPath, string configPath, Action<string> log)
        {
            List<Record> desired = ReadDesired(configPath);
            HashSet<string> state = ReadState(configPath);
            var wanted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (Record r in desired)
            {
                wanted.Add(r.RelativePath);
                string full = Path.Combine(pluginPath, r.RelativePath);
                string off = full + DisabledSuffix;
                if (File.Exists(full) && !File.Exists(off))
                {
                    try
                    {
                        File.Move(full, off);
                        state.Add(r.RelativePath);
                        log($"Switched off {r.Name} ({r.RelativePath})");
                    }
                    catch (Exception ex)
                    {
                        log($"Could not switch off {r.RelativePath}: {ex.Message}");
                    }
                }
            }

            foreach (string rel in new List<string>(state))
            {
                if (wanted.Contains(rel))
                {
                    continue;
                }
                string full = Path.Combine(pluginPath, rel);
                string off = full + DisabledSuffix;
                if (File.Exists(off) && !File.Exists(full))
                {
                    try
                    {
                        File.Move(off, full);
                        log($"Switched on {rel}");
                    }
                    catch (Exception ex)
                    {
                        log($"Could not switch on {rel}: {ex.Message}");
                        continue;
                    }
                }
                state.Remove(rel);
            }

            state.RemoveWhere(rel => !File.Exists(Path.Combine(pluginPath, rel) + DisabledSuffix));

            var sb = new StringBuilder();
            sb.AppendLine("# Plugin files the ModFramework preloader renamed to .dll.disabled. Do not edit.");
            foreach (string rel in state)
            {
                sb.AppendLine(rel);
            }
            Directory.CreateDirectory(configPath);
            File.WriteAllText(Path.Combine(configPath, StateFileName), sb.ToString(), new UTF8Encoding(false));
        }

        // Plugin paths must stay inside BepInEx/plugins.
        internal static string NormalizeRelative(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return null;
            }
            string rel = value.Replace('\\', '/').Trim();
            if (rel.Length == 0 || Path.IsPathRooted(rel) || rel.StartsWith("/", StringComparison.Ordinal))
            {
                return null;
            }
            foreach (string part in rel.Split('/'))
            {
                if (part == ".." || part == ".")
                {
                    return null;
                }
            }
            // A plugin DLL, or the mod.json of a data mod (a folder of files
            // another library reads), which is switched off the same way.
            if (rel.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) return rel;
            return rel.IndexOf('/') > 0 && string.Equals(Path.GetFileName(rel), "mod.json", StringComparison.OrdinalIgnoreCase) ? rel : null;
        }

        private static string Clean(string value)
        {
            return (value ?? "").Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
        }
    }
}
