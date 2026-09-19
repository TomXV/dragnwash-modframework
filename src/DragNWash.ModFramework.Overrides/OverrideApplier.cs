using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Override = DragNWash.ModFramework.Overrides.OverrideFiles.Override;

namespace DragNWash.ModFramework.Overrides
{
    // Finds each override's object and writes its value, remembering what the
    // game had so it can be put back.
    //
    // When: a frame after a scene loads and again a second later, and whenever
    // a new root object appears (checked twice a second), for the overrides
    // whose path starts with that root. The game spawns each level's dragon
    // long after the scene loads, so an override for "DragonRyanA (Clone)/..."
    // applies whenever that dragon comes. An object is written once per
    // override; a new instance of it is written again.
    internal static class OverrideApplier
    {
        private sealed class Written
        {
            public Override By;
            public UnityEngine.Object Target;
            public Action<object> Set;
            public object Original;
        }

        // instance id + what was written -> the write, for putting it back and
        // for telling when two mods write the same thing.
        private static readonly Dictionary<string, Written> Writes = new Dictionary<string, Written>(StringComparer.Ordinal);
        private static readonly HashSet<string> SaidConflict = new HashSet<string>(StringComparer.Ordinal);
        private static readonly HashSet<string> SaidProblem = new HashSet<string>(StringComparer.Ordinal);
        // Overrides that found their object but could not be written (already
        // warned about), so the "found nothing" report leaves them out.
        private static readonly HashSet<Override> Failed = new HashSet<Override>();
        private static readonly HashSet<int> KnownRoots = new HashSet<int>();
        internal static readonly Dictionary<Override, int> AppliedCount = new Dictionary<Override, int>();

        private static List<Override> _all = new List<Override>();

        internal static void Use(IEnumerable<OverrideFiles.Mod> mods)
        {
            _all = mods.SelectMany(m => m.Overrides).ToList();
            AppliedCount.Clear();
            Failed.Clear();
            SaidProblem.Clear();
        }

        internal static int WriteCount => Writes.Count;

        private static GameObject _probe;

        // Every root object, with the scene it is in (DontDestroyOnLoad too).
        private static List<GameObject> Roots()
        {
            var roots = new List<GameObject>();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene s = SceneManager.GetSceneAt(i);
                if (s.isLoaded) roots.AddRange(s.GetRootGameObjects());
            }
            if (_probe == null)
            {
                _probe = new GameObject("DragNWash.ModFramework.Overrides");
                _probe.hideFlags = HideFlags.HideAndDontSave;
                UnityEngine.Object.DontDestroyOnLoad(_probe);
            }
            foreach (GameObject root in _probe.scene.GetRootGameObjects())
            {
                if (root != _probe) roots.Add(root);
            }
            return roots;
        }

        // Applies to every root (after a scene load), or only to roots not seen
        // before (the watch). Returns the number of values written.
        internal static int Apply(bool newRootsOnly)
        {
            if (_all.Count == 0) return 0;
            List<GameObject> roots = Roots();
            var fresh = new List<GameObject>();
            var now = new HashSet<int>();
            foreach (GameObject root in roots)
            {
                if (root == null) continue;
                int id = root.GetInstanceID();
                now.Add(id);
                if (!KnownRoots.Contains(id)) fresh.Add(root);
            }
            KnownRoots.Clear();
            KnownRoots.UnionWith(now);
            List<GameObject> targets = newRootsOnly ? fresh : roots;
            if (targets.Count == 0) return 0;

            var byName = new Dictionary<string, List<GameObject>>(StringComparer.Ordinal);
            foreach (GameObject root in targets)
            {
                if (root == null) continue;
                if (!byName.TryGetValue(root.name, out List<GameObject> list)) byName[root.name] = list = new List<GameObject>();
                list.Add(root);
            }
            int written = 0;
            foreach (Override o in _all)
            {
                if (!byName.TryGetValue(o.Root, out List<GameObject> candidates)) continue;
                foreach (GameObject root in candidates)
                {
                    if (o.Scene != null && !string.Equals(root.scene.name, o.Scene, StringComparison.Ordinal)) continue;
                    Transform t = o.Rest == null ? root.transform : root.transform.Find(o.Rest);
                    if (t == null) continue;
                    if (Write(o, t.gameObject)) written++;
                }
            }
            return written;
        }

        private static void Problem(Override o, string message)
        {
            Failed.Add(o);
            string key = o.Mod.Guid + "|" + o.File + "|" + o.Line;
            if (SaidProblem.Add(key))
            {
                OverridesPlugin.Log.LogWarning($"[overrides] {o.Mod.Name}, {o.File} #{o.Line} ({o.Target}): {message}");
            }
        }

        private static bool Write(Override o, GameObject go)
        {
            try
            {
                return o.Material != null ? WriteMaterial(o, go) : WriteMember(o, go);
            }
            catch (Exception ex)
            {
                Problem(o, (ex.InnerException ?? ex).Message);
                return false;
            }
        }

        private static Component FindComponent(GameObject go, string name, int index)
        {
            int seen = 0;
            foreach (Component c in go.GetComponents<Component>())
            {
                if (c == null) continue;
                Type t = c.GetType();
                if (t.Name != name && t.FullName != name) continue;
                if (seen++ == index) return c;
            }
            return null;
        }

        private static bool WriteMember(Override o, GameObject go)
        {
            Component c = FindComponent(go, o.Component, o.Index);
            if (c == null)
            {
                Problem(o, $"{go.name} has no {o.Component}{(o.Index > 0 ? " #" + o.Index : "")}.");
                return false;
            }
            BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | (o.Private ? BindingFlags.NonPublic : 0);
            Type memberType = null;
            Func<object> get = null;
            Action<object> set = null;
            for (Type t = c.GetType(); t != null && memberType == null; t = t.BaseType)
            {
                FieldInfo f = t.GetField(o.Member, flags | BindingFlags.DeclaredOnly);
                if (f != null && !f.IsInitOnly && !f.IsLiteral)
                {
                    memberType = f.FieldType;
                    get = () => f.GetValue(c);
                    set = v => f.SetValue(c, v);
                    break;
                }
                PropertyInfo p = t.GetProperty(o.Member, flags | BindingFlags.DeclaredOnly);
                if (p != null && p.CanRead && p.CanWrite && p.GetIndexParameters().Length == 0
                    && (o.Private || (p.GetGetMethod() != null && p.GetSetMethod() != null)))
                {
                    memberType = p.PropertyType;
                    get = () => p.GetValue(c, null);
                    set = v => p.SetValue(c, v, null);
                }
            }
            if (memberType == null)
            {
                Problem(o, $"{c.GetType().Name} has no {(o.Private ? "" : "public ")}writable field or property {o.Member}{(o.Private ? "" : " (a private one needs \"private\": true)")}.");
                return false;
            }
            if (!OverrideValues.Supports(memberType))
            {
                Problem(o, $"{o.Member} is a {memberType.Name}, which an override cannot hold.");
                return false;
            }
            object value = OverrideValues.Deserialize(memberType, o.Value, out string error);
            if (error != null)
            {
                Problem(o, $"\"{o.Value}\" is not a {memberType.Name}: {error}.");
                return false;
            }
            return Record(o, c, c.GetInstanceID() + "|" + o.Key, get, set, value);
        }

        private static bool WriteMaterial(Override o, GameObject go)
        {
            Renderer r = o.Component != null ? FindComponent(go, o.Component, o.Index) as Renderer : go.GetComponent<Renderer>();
            if (r == null)
            {
                Problem(o, $"{go.name} has no {o.Component ?? "Renderer"}.");
                return false;
            }
            Material m = r.sharedMaterials.FirstOrDefault(x => x != null && (x.name == o.Material || x.name == o.Material + " (Instance)"));
            if (m == null)
            {
                Problem(o, $"{go.name}'s renderer has no material {o.Material}.");
                return false;
            }
            Shader shader = m.shader;
            int index = shader != null ? shader.FindPropertyIndex(o.Property) : -1;
            if (index < 0)
            {
                Problem(o, $"{m.name}'s shader has no property {o.Property}.");
                return false;
            }
            int id = Shader.PropertyToID(o.Property);
            Type type;
            Func<object> get;
            Action<object> set;
            switch (shader.GetPropertyType(index))
            {
                case ShaderPropertyType.Color:
                    type = typeof(Color); get = () => m.GetColor(id); set = v => m.SetColor(id, (Color)v); break;
                case ShaderPropertyType.Vector:
                    type = typeof(Vector4); get = () => m.GetVector(id); set = v => m.SetVector(id, (Vector4)v); break;
                case ShaderPropertyType.Int:
                    type = typeof(int); get = () => m.GetInteger(id); set = v => m.SetInteger(id, (int)v); break;
                case ShaderPropertyType.Float:
                case ShaderPropertyType.Range:
                    type = typeof(float); get = () => m.GetFloat(id); set = v => m.SetFloat(id, (float)v); break;
                default:
                    Problem(o, $"{o.Property} is a texture; textures are the Assets library's.");
                    return false;
            }
            object value = OverrideValues.Deserialize(type, o.Value, out string error);
            if (error != null)
            {
                Problem(o, $"\"{o.Value}\" is not a {type.Name}: {error}.");
                return false;
            }
            return Record(o, m, m.GetInstanceID() + "|" + o.Key, get, set, value);
        }

        // Writes once per instance; keeps the game's value from before the
        // first write; names both mods when a second one writes the same thing.
        private static bool Record(Override o, UnityEngine.Object target, string key, Func<object> get, Action<object> set, object value)
        {
            if (Writes.TryGetValue(key, out Written earlier) && earlier.Target != null)
            {
                if (ReferenceEquals(earlier.By, o)) return false;
                // The mod that loads later keeps the value.
                if (earlier.By.Mod.Order > o.Mod.Order) return false;
                if (earlier.By.Mod != o.Mod && SaidConflict.Add(o.Key))
                {
                    OverridesPlugin.Log.LogWarning($"[overrides] {earlier.By.Mod.Name} and {o.Mod.Name} both change {o.Target}; {o.Mod.Name}'s value is used (it loads later).");
                }
                set(value);
                earlier.By = o;
                Count(o);
                return true;
            }
            object original = get();
            set(value);
            Writes[key] = new Written { By = o, Target = target, Set = set, Original = original };
            Count(o);
            return true;
        }

        private static void Count(Override o)
        {
            AppliedCount.TryGetValue(o, out int n);
            AppliedCount[o] = n + 1;
        }

        // Puts back every value the overrides changed that still exists.
        internal static int TakeBackAll()
        {
            int restored = 0;
            foreach (Written w in Writes.Values)
            {
                if (w.Target == null) continue;
                try
                {
                    w.Set(w.Original);
                    restored++;
                }
                catch
                {
                }
            }
            Writes.Clear();
            KnownRoots.Clear();
            return restored;
        }

        // Forgets writes to objects that are gone (a scene unloaded).
        internal static void Prune()
        {
            foreach (string key in Writes.Where(kv => kv.Value.Target == null).Select(kv => kv.Key).ToList())
            {
                Writes.Remove(key);
            }
        }

        // The overrides for a scene that found nothing in it, for the log.
        internal static List<Override> NotFoundIn(string scene)
        {
            return _all.Where(o => o.Scene == scene && !AppliedCount.ContainsKey(o) && !Failed.Contains(o)).ToList();
        }
    }
}
