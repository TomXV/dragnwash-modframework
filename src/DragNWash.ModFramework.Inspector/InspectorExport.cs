using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using BepInEx;
using UnityEngine;

namespace DragNWash.ModFramework.Inspector
{
    // Export as overrides: the edits in the History, written as a mod with no
    // code for the Overrides library (docs/OVERRIDES.md):
    //
    //   BepInEx/plugins/<name>/mod.json
    //   BepInEx/plugins/<name>/overrides/main.json
    //
    // Each edit keeps where it was made (scene, path, component and its index
    // among components of its type, member, whether it is private; a material
    // property with the renderer showing it). One row per place, with the
    // value it holds now; places back at the game's value are left out, and
    // so are edits an override cannot hold, each with the reason. Needs the
    // Overrides library, which writes the values (so they read back exactly).
    // Experimental.
    internal static class InspectorExport
    {
        internal sealed class Place
        {
            public string Scene, Path, Component, Member, Material, Property;
            public int Index;
            public bool Private;
            // Why this edit cannot be an override, or null.
            public string Not;

            public string Key => $"{Scene}|{Path}|{Component}|{Index}|{Member}|{Material}|{Property}";
        }

        internal sealed class Row
        {
            public Place Where;
            public string Value;
            public string Label;
        }

        // ---- where an edit is ------------------------------------------------------

        internal static Place PlaceOf(object target, string member)
        {
            try
            {
                return Locate(target, member);
            }
            catch (Exception ex)
            {
                return new Place { Not = ex.Message };
            }
        }

        private static Place Locate(object target, string member)
        {
            if (member == null || member.Contains("[")) return new Place { Not = "an element of a list" };
            if (member.StartsWith("vertex ", StringComparison.Ordinal)) return new Place { Not = "a mesh edit (the Asset tool exports meshes)" };
            switch (target)
            {
                case Material m:
                {
                    if (member == "shader" || member == "renderQueue") return new Place { Not = $"the material's {member}" };
                    GameObject go = InspectorTab.SelectedObject;
                    Renderer r = go != null ? go.GetComponents<Renderer>().FirstOrDefault(x => x.sharedMaterials.Contains(m)) : null;
                    if (r == null) return new Place { Not = "a material not shown by the selected object" };
                    int cut = member.IndexOf("  [", StringComparison.Ordinal);
                    return new Place
                    {
                        Scene = go.scene.name,
                        Path = InspectorModel.PathOf(go.transform),
                        Component = r.GetType().Name,
                        Index = IndexOf(r),
                        Material = m.name.Replace(" (Instance)", ""),
                        Property = cut >= 0 ? member.Substring(0, cut) : member,
                    };
                }
                case Component c:
                {
                    if (member.StartsWith("parameter  ", StringComparison.Ordinal) || member.StartsWith("trigger  ", StringComparison.Ordinal) || member.StartsWith("layer weight  ", StringComparison.Ordinal))
                    {
                        return new Place { Not = "an Animator parameter (not a field or property)" };
                    }
                    if (member == "runtimeAnimatorController") return new Place { Not = "a clip swap (an object, not a value)" };
                    bool? isPrivate = Visibility(c.GetType(), member);
                    if (isPrivate == null) return new Place { Not = $"{c.GetType().Name} has no writable {member}" };
                    return new Place
                    {
                        Scene = c.gameObject.scene.name,
                        Path = InspectorModel.PathOf(c.transform),
                        Component = c.GetType().Name,
                        Index = IndexOf(c),
                        Member = member,
                        Private = isPrivate.Value,
                    };
                }
                case GameObject _:
                    return new Place { Not = $"the GameObject's {member}" };
            }
            return new Place { Not = "not an object in a scene" };
        }

        private static int IndexOf(Component c)
        {
            int index = 0;
            foreach (Component other in c.GetComponents(c.GetType()))
            {
                if (other == c) return index;
                if (other.GetType() == c.GetType()) index++;
            }
            return 0;
        }

        // False for a public writable field or property, true for a private
        // one, null when there is no writable member of that name.
        private static bool? Visibility(Type type, string member)
        {
            const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            for (Type t = type; t != null; t = t.BaseType)
            {
                FieldInfo f = t.GetField(member, Any);
                if (f != null && !f.IsInitOnly && !f.IsLiteral) return !f.IsPublic;
                PropertyInfo p = t.GetProperty(member, Any);
                if (p != null && p.CanRead && p.CanWrite && p.GetIndexParameters().Length == 0)
                {
                    return !(p.GetGetMethod() != null && p.GetSetMethod() != null);
                }
            }
            return null;
        }

        // ---- what is exported ----------------------------------------------------------

        // The Overrides library is optional: without it there is nothing to
        // export for.
        private static bool? _available;

        internal static bool Available
        {
            get
            {
                if (_available == null)
                {
                    try
                    {
                        _available = Check();
                    }
                    catch
                    {
                        _available = false;
                    }
                }
                return _available.Value;
            }
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static bool Check() => DragNWash.ModFramework.Overrides.GameOverrides.CanHold(typeof(float));

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static bool CanHold(Type t) => DragNWash.ModFramework.Overrides.GameOverrides.CanHold(t);

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static string Serialize(object v) => DragNWash.ModFramework.Overrides.GameOverrides.Serialize(v);

        // One row per place, from the History (newest edit of a place wins),
        // with the value the object holds now; and what cannot be exported.
        internal static List<Row> Collect(out List<string> skipped)
        {
            skipped = new List<string>();
            var rows = new List<Row>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            IReadOnlyList<InspectorHistory.Entry> all = InspectorHistory.All;
            for (int i = all.Count - 1; i >= 0; i--)
            {
                InspectorHistory.Entry e = all[i];
                if (!seen.Add(e.Key)) continue;
                string label = $"{e.Label} {e.Member}";
                Place where = e.Where;
                if (where == null || where.Not != null)
                {
                    skipped.Add($"{label}: {where?.Not ?? "no place recorded"}");
                    continue;
                }
                if (e.Target is UnityEngine.Object o && o == null)
                {
                    skipped.Add($"{label}: the object is gone (the value it had is no longer known)");
                    continue;
                }
                object now;
                try
                {
                    now = e.Get();
                }
                catch (Exception ex)
                {
                    skipped.Add($"{label}: {(ex.InnerException ?? ex).Message}");
                    continue;
                }
                if (now == null || !CanHold(now.GetType()))
                {
                    skipped.Add($"{label}: a {now?.GetType().Name ?? "null"} cannot be an override");
                    continue;
                }
                if (InspectorHistory.TryOriginal(e.Target, e.Member, out object original) && Equals(original, now))
                {
                    continue;   // back at the game's value
                }
                rows.Add(new Row { Where = where, Value = Serialize(now), Label = label });
            }
            rows.Reverse();
            return rows;
        }

        // ---- writing -------------------------------------------------------------------

        // Writes the mod folder; returns what happened, for the History view.
        internal static string Write(string name, string author, string description)
        {
            List<Row> rows = Collect(out List<string> skipped);
            if (rows.Count == 0)
            {
                return skipped.Count > 0 ? $"Nothing to export: {skipped.Count} edit(s) cannot be overrides ({skipped[0]}...)." : "Nothing to export: no edit in the History changes a value from the game's.";
            }
            string folderName = SafeName(string.IsNullOrWhiteSpace(name) ? "My changes" : name.Trim());
            string folder = Path.Combine(Paths.PluginPath, folderName);
            string overrides = Path.Combine(folder, "overrides");
            bool exists = File.Exists(Path.Combine(folder, "mod.json")) || File.Exists(Path.Combine(folder, "mod.json.disabled"));
            if (Directory.Exists(folder) && !exists && Directory.EnumerateFileSystemEntries(folder).Any())
            {
                return $"BepInEx/plugins/{folderName} already holds something that is not an overrides mod; choose another name.";
            }
            Directory.CreateDirectory(overrides);
            if (!exists)
            {
                File.WriteAllText(Path.Combine(folder, "mod.json"), Manifest(name, author, description), new UTF8Encoding(false));
            }
            // A second export into the same mod adds a file; the mod.json stays.
            string file = exists ? $"inspector-{DateTime.Now:yyyyMMdd-HHmmss}.json" : "main.json";
            File.WriteAllText(Path.Combine(overrides, file), OverridesJson(rows), new UTF8Encoding(false));
            InspectorPlugin.Log.LogInfo($"[export] {rows.Count} override(s) to BepInEx/plugins/{folderName}/overrides/{file}; {skipped.Count} edit(s) left out.");
            foreach (string s in skipped) InspectorPlugin.Log.LogInfo("[export] left out: " + s);
            return $"Exported {rows.Count} value(s) to BepInEx/plugins/{folderName}/overrides/{file}{(skipped.Count > 0 ? $"; {skipped.Count} edit(s) could not be (see the log)" : "")}. Type \"overrides reload\" in the Console, or restart the game, to run it as a mod.";
        }

        private static string SafeName(string name)
        {
            var sb = new StringBuilder();
            foreach (char c in name)
            {
                sb.Append(Path.GetInvalidFileNameChars().Contains(c) || c == '.' && sb.Length == 0 ? '_' : c);
            }
            string s = sb.ToString().Trim().TrimEnd('.');
            return s.Length == 0 ? "My changes" : s;
        }

        private static string Manifest(string name, string author, string description)
        {
            var sb = new StringBuilder("{\n");
            sb.Append("  \"name\": ").Append(Q(string.IsNullOrWhiteSpace(name) ? "My changes" : name.Trim())).Append(",\n");
            if (!string.IsNullOrWhiteSpace(author)) sb.Append("  \"authors\": [").Append(Q(author.Trim())).Append("],\n");
            sb.Append("  \"description\": ").Append(Q(string.IsNullOrWhiteSpace(description) ? "Made with the Inspector." : description.Trim())).Append(",\n");
            sb.Append("  \"version\": \"1.0.0\"\n}\n");
            return sb.ToString();
        }

        private static string OverridesJson(List<Row> rows)
        {
            var sb = new StringBuilder("{\n  \"format\": 1,\n  \"overrides\": [\n");
            for (int i = 0; i < rows.Count; i++)
            {
                Place p = rows[i].Where;
                sb.Append("    { ");
                sb.Append("\"scene\": ").Append(Q(p.Scene)).Append(", ");
                sb.Append("\"path\": ").Append(Q(p.Path)).Append(", ");
                sb.Append("\"component\": ").Append(Q(p.Component)).Append(", ");
                if (p.Index > 0) sb.Append("\"index\": ").Append(p.Index.ToString(CultureInfo.InvariantCulture)).Append(", ");
                if (p.Material != null)
                {
                    sb.Append("\"material\": ").Append(Q(p.Material)).Append(", ");
                    sb.Append("\"property\": ").Append(Q(p.Property)).Append(", ");
                }
                else
                {
                    sb.Append("\"member\": ").Append(Q(p.Member)).Append(", ");
                    if (p.Private) sb.Append("\"private\": true, ");
                }
                sb.Append("\"value\": ").Append(Q(rows[i].Value)).Append(" }");
                sb.Append(i < rows.Count - 1 ? ",\n" : "\n");
            }
            sb.Append("  ]\n}\n");
            return sb.ToString();
        }

        private static string Q(string s)
        {
            var sb = new StringBuilder("\"");
            foreach (char c in s ?? "")
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            return sb.Append('"').ToString();
        }
    }
}
