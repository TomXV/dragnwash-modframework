using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Text;
using UnityEngine;
using Member = DragNWash.ModFramework.Inspector.InspectorModel.Member;

namespace DragNWash.ModFramework.Inspector
{
    // The object explorer (Inspector tab, Objects): every loaded object by
    // kind, what one is, and where it is used. Experimental. Design:
    // docs/OBJECT_EXPLORER.md.
    //
    // One pass over Resources.FindObjectsOfTypeAll lists everything, when
    // Objects is first looked at, on Refresh, and after a scene load or unload
    // (when it is looked at again). The list keeps instance IDs, names and
    // types, never references (see ObjectList); an object is looked up again by
    // its ID when it is selected or its row is first drawn. Used by runs only on
    // its button. Nothing here runs while Objects is closed.
    //
    // Types of modules this library does not reference (audio, animation,
    // physics, TextMeshPro) are read by reflection, as the rigidbodies and the
    // animators are.
    internal static class InspectorObjects
    {
        private static ObjectList _list;
        private static bool _stale = true;
        private static int _version;

        internal static int Version => _version;
        internal static bool Stale => _stale;
        internal static ObjectList Current => _list;

        // A scene load or unload: rebuilt the next time the list is looked at.
        internal static void MarkStale() => _stale = true;

        internal static ObjectList List()
        {
            if (_list == null || _stale)
            {
                Build();
            }
            return _list;
        }

        // ---- the one pass -------------------------------------------------------------

        private static readonly Dictionary<Type, ObjectType> Types = new Dictionary<Type, ObjectType>();
        private static readonly ObjectType Skip = new ObjectType();

        internal static ObjectList Build()
        {
            var sw = Stopwatch.StartNew();
            UnityEngine.Object[] all = Resources.FindObjectsOfTypeAll<UnityEngine.Object>();
            long findMs = sw.ElapsedMilliseconds;
            var entries = new List<ObjectEntry>(all.Length / 2);
            for (int i = 0; i < all.Length; i++)
            {
                UnityEngine.Object o = all[i];
                if (ReferenceEquals(o, null))
                {
                    continue;
                }
                ObjectType type = TypeOf(o.GetType());
                if (ReferenceEquals(type, Skip))
                {
                    continue;
                }
                string name;
                try
                {
                    if (type.Kind == ObjectKind.OutsideScenes)
                    {
                        // Scene objects are in Scene; here only those no loaded scene holds.
                        var go = (GameObject)o;
                        if (go.scene.IsValid()) continue;
                        name = InspectorModel.PathOf(go.transform);
                    }
                    else
                    {
                        name = o.name ?? "";
                    }
                    entries.Add(new ObjectEntry
                    {
                        Id = o.GetInstanceID(),
                        Name = name,
                        Type = type,
                        Hidden = (o.hideFlags & (HideFlags.HideInHierarchy | HideFlags.HideInInspector)) != 0,
                    });
                }
                catch (Exception)
                {
                    // An object Unity half tore down: left out, the list goes on.
                }
            }
            // The array is dropped here: nothing keeps the objects alive.
            all = null;
            _list = new ObjectList(entries, sw.ElapsedMilliseconds);
            _stale = false;
            _version++;
            InspectorPlugin.Log.LogInfo($"[objects] Listed {entries.Count} objects in {sw.ElapsedMilliseconds} ms (FindObjectsOfTypeAll {findMs} ms).");
            return _list;
        }

        internal static ObjectType TypeOf(Type t)
        {
            if (Types.TryGetValue(t, out ObjectType known))
            {
                return known;
            }
            var chain = new List<string>();
            var full = new List<string>();
            for (Type b = t; b != null && b != typeof(object); b = b.BaseType)
            {
                chain.Add(b.Name);
                full.Add(b.FullName);
            }
            ObjectType type;
            if (typeof(Component).IsAssignableFrom(t))
            {
                // Components are in Scene, or under their GameObject outside the scenes.
                type = Skip;
            }
            else
            {
                ObjectKind kind;
                if (t == typeof(GameObject)) kind = ObjectKind.OutsideScenes;
                else if (typeof(Sprite).IsAssignableFrom(t)) kind = ObjectKind.Sprites;
                else if (typeof(Texture).IsAssignableFrom(t)) kind = ObjectKind.Textures;
                else if (typeof(Material).IsAssignableFrom(t)) kind = ObjectKind.Materials;
                else if (typeof(Shader).IsAssignableFrom(t)) kind = ObjectKind.Shaders;
                else if (typeof(Mesh).IsAssignableFrom(t)) kind = ObjectKind.Meshes;
                else if (full.Contains("UnityEngine.AudioClip")) kind = ObjectKind.Audio;
                else if (full.Contains("UnityEngine.AnimationClip") || full.Contains("UnityEngine.RuntimeAnimatorController")) kind = ObjectKind.Animation;
                // A TextMeshPro font is a ScriptableObject too: fonts are asked first.
                else if (typeof(Font).IsAssignableFrom(t) || full.Contains("TMPro.TMP_FontAsset")) kind = ObjectKind.Fonts;
                else if (typeof(ScriptableObject).IsAssignableFrom(t)) kind = ObjectKind.Data;
                else kind = ObjectKind.Other;
                type = new ObjectType { Name = t.Name, Chain = chain.ToArray(), Kind = kind, Game = kind == ObjectKind.Data && InspectorCodeGraph.IsGameType(t) };
            }
            Types[t] = type;
            return type;
        }

        // ---- looking objects up again ---------------------------------------------------

        // The object behind an ID, or null when Unity destroyed it. Unity 6.3
        // finds an object by its EntityId, which an instance ID converts to.
        internal static UnityEngine.Object Find(int id)
        {
            UnityEngine.Object o = Resources.EntityIdToObject(id);
            return o ? o : null;
        }

        internal static UnityEngine.Object Find(ObjectEntry e)
        {
            UnityEngine.Object o = Find(e.Id);
            if (o == null) e.Gone = true;
            return o;
        }

        // True for a GameObject or a component no loaded scene holds: shown, not edited.
        internal static bool IsOutsideScenes(object target)
        {
            try
            {
                if (target is GameObject go) return go && !go.scene.IsValid();
                if (target is Component c) return c && !c.gameObject.scene.IsValid();
            }
            catch (Exception)
            {
            }
            return false;
        }

        // An object the explorer lists (not a scene's GameObject or component).
        internal static bool IsListed(object target)
        {
            return target is UnityEngine.Object o && !(o is Component) && !(o is GameObject go && go.scene.IsValid());
        }

        internal static string Describe(UnityEngine.Object o)
        {
            if (o == null) return "";
            ObjectType type = TypeOf(o.GetType());
            string folder = ReferenceEquals(type, Skip) ? "" : ObjectList.FolderName(type.Kind) + ": ";
            string name = o is GameObject go ? InspectorModel.PathOf(go.transform) : o.name;
            return folder + (string.IsNullOrEmpty(name) ? "(no name)" : name) + " (" + o.GetType().Name + ")";
        }

        // ---- one short fact per row ------------------------------------------------------

        internal static string Fact(ObjectEntry e)
        {
            if (e.Fact != null)
            {
                return e.Fact;
            }
            UnityEngine.Object o = Find(e);
            if (o == null)
            {
                return e.Fact = "(destroyed)";
            }
            try
            {
                e.Fact = FactOf(o);
            }
            catch (Exception ex)
            {
                e.Fact = "error: " + (ex.InnerException ?? ex).Message;
            }
            return e.Fact;
        }

        private static string FactOf(UnityEngine.Object o)
        {
            switch (o)
            {
                case Texture t: return $"{t.width}x{t.height} {t.graphicsFormat}";
                case Sprite sp: return $"{sp.rect.width:0}x{sp.rect.height:0}" + (sp.texture != null ? " of " + sp.texture.name : "");
                case Material m: return m.shader != null ? m.shader.name : "no shader";
                case Shader sh: return $"{sh.GetPropertyCount()} properties";
                case Mesh mesh: return $"{mesh.vertexCount} vertices, {mesh.subMeshCount} sub-mesh(es)";
                case Font f: return f.dynamic ? "dynamic" : $"{f.fontSize} pt";
                case GameObject go: return $"{go.GetComponents<Component>().Length} components, {go.transform.childCount} children";
            }
            ObjectType type = TypeOf(o.GetType());
            if (type.Kind == ObjectKind.Audio)
            {
                return $"{Num(Read(o, "length")):0.00} s, {Read(o, "channels")} ch, {Read(o, "frequency")} Hz";
            }
            if (type.Kind == ObjectKind.Animation)
            {
                return Read(o, "animationClips") is Array clips ? $"{clips.Length} clip(s)" : InspectorAnimators.DescribeClip(o);
            }
            if (type.Kind == ObjectKind.Fonts)
            {
                return "TextMeshPro";
            }
            return "";
        }

        // ---- the header over the members ---------------------------------------------------

        // A few lines on what the object is, per kind; computed once per selection.
        internal static List<string> Header(UnityEngine.Object o)
        {
            var lines = new List<string>();
            if (o == null) return lines;
            try
            {
                switch (o)
                {
                    case Texture t:
                        lines.Add($"{t.width}x{t.height}  {t.graphicsFormat}  {t.dimension}  {(t.isReadable ? "readable by the CPU" : "GPU only")}  {t.mipmapCount} mip level(s)  filter {t.filterMode}  wrap {t.wrapMode}");
                        break;
                    case Sprite sp:
                        lines.Add($"{sp.rect.width:0}x{sp.rect.height:0} at {sp.rect.x:0},{sp.rect.y:0} of {(sp.texture != null ? sp.texture.name : "no texture")}  pivot {sp.pivot.x:0.#},{sp.pivot.y:0.#}  {sp.pixelsPerUnit:0.#} pixels per unit{(sp.packed ? "  packed" : "")}");
                        break;
                    case Material m:
                        lines.Add($"Shader {(m.shader != null ? m.shader.name : "none")}; {InspectorModel.RendererCount(m)} renderer(s) in the scenes share it.");
                        break;
                    case Shader sh:
                    {
                        int count = sh.GetPropertyCount();
                        var names = new List<string>();
                        for (int i = 0; i < count && i < 24; i++) names.Add(sh.GetPropertyName(i));
                        lines.Add($"{count} properties: {string.Join(", ", names.ToArray())}{(count > names.Count ? ", ..." : "")}");
                        string[] keywords = sh.keywordSpace.keywordNames;
                        lines.Add(keywords.Length == 0 ? "No keywords." : $"{keywords.Length} keyword(s): {string.Join(", ", keywords, 0, Math.Min(24, keywords.Length))}{(keywords.Length > 24 ? ", ..." : "")}");
                        int users = 0;
                        foreach (Material m in Resources.FindObjectsOfTypeAll<Material>())
                        {
                            if (m != null && m.shader == sh) users++;
                        }
                        lines.Add($"{users} material(s) loaded use it; passes {sh.passCount}, render queue {sh.renderQueue}{(sh.isSupported ? "" : ", not supported here")}.");
                        break;
                    }
                    case Mesh mesh:
                    {
                        long triangles = 0;
                        for (int i = 0; i < mesh.subMeshCount; i++)
                        {
                            if (mesh.GetTopology(i) == MeshTopology.Triangles) triangles += mesh.GetIndexCount(i) / 3;
                        }
                        Bounds b = mesh.bounds;
                        lines.Add($"{mesh.vertexCount} vertices, {triangles} triangles, {mesh.subMeshCount} sub-mesh(es), {mesh.blendShapeCount} blend shape(s), {(mesh.isReadable ? "readable" : "not readable")}");
                        lines.Add($"Bounds: centre {InspectorModel.Format(b.center)}, size {InspectorModel.Format(b.size)}");
                        break;
                    }
                    case Font f:
                        lines.Add($"{(f.dynamic ? "Dynamic" : "Static")} font, size {f.fontSize}, line height {f.lineHeight}");
                        break;
                    case GameObject go:
                        lines.Add($"Outside the scenes (a template the game keeps loaded): read-only. {go.GetComponents<Component>().Length} components, {go.transform.childCount} children.");
                        break;
                    default:
                    {
                        ObjectKind kind = TypeOf(o.GetType()).Kind;
                        if (kind == ObjectKind.Audio)
                        {
                            lines.Add($"{Num(Read(o, "length")):0.00} s, {Read(o, "channels")} channel(s), {Read(o, "frequency")} Hz, {Read(o, "samples")} samples, load {Read(o, "loadType")}, {Read(o, "loadState")}");
                        }
                        else if (kind == ObjectKind.Animation)
                        {
                            if (Read(o, "animationClips") is Array clips) lines.Add($"A controller with {clips.Length} clip(s).");
                            else lines.Add(InspectorAnimators.DescribeClip(o));
                        }
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                lines.Add("error: " + (ex.InnerException ?? ex).Message);
            }
            return lines;
        }

        // ---- members --------------------------------------------------------------------

        private static readonly Dictionary<Type, List<Member>> Members = new Dictionary<Type, List<Member>>();

        // The rows of an object in the explorer: its members, less the array
        // properties Unity copies on every read (a mesh's vertices, a clip's
        // events), which the header counts instead.
        internal static List<Member> MembersOf(Type type)
        {
            if (Members.TryGetValue(type, out List<Member> known))
            {
                return known;
            }
            var list = new List<Member>();
            foreach (Member m in InspectorModel.MembersOf(type))
            {
                if (m.Type != null && m.Type.IsArray && IsEngineCopy(type, m.Name)) continue;
                list.Add(m);
            }
            Members[type] = list;
            return list;
        }

        private static bool IsEngineCopy(Type type, string name)
        {
            for (Type t = type; t != null; t = t.BaseType)
            {
                PropertyInfo p = t.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (p != null) return p.DeclaringType.Assembly.GetName().Name.StartsWith("UnityEngine", StringComparison.Ordinal);
            }
            return false;
        }

        // ---- Used by --------------------------------------------------------------------

        internal sealed class Hit
        {
            public int Id;           // the component or asset that holds it
            public string Where;
            public string Member;
            public bool InScene;
        }

        internal const int HitCap = 2000;

        // Where the object is used: the places Unity keeps in its own components
        // (renderers, mesh filters, audio sources, animators), materials and
        // sprites, and every field of the scripts' components and of the
        // ScriptableObjects that can hold it. Stops at `cap` places.
        internal static List<Hit> UsedBy(UnityEngine.Object target, int cap, out string summary)
        {
            var sw = Stopwatch.StartNew();
            var hits = new List<Hit>();
            int looked = 0;
            if (target == null)
            {
                summary = "Nothing selected.";
                return hits;
            }
            Type targetType = target.GetType();
            bool Add(UnityEngine.Object holder, string member)
            {
                if (hits.Count >= cap) return false;
                if (holder == target) return true;
                hits.Add(MakeHit(holder, member));
                return hits.Count < cap;
            }

            // What Unity keeps in native code: asked through its properties.
            if (target is Material mat)
            {
                foreach (Renderer r in Resources.FindObjectsOfTypeAll<Renderer>())
                {
                    looked++;
                    Material[] shared = r.sharedMaterials;
                    for (int i = 0; i < shared.Length; i++)
                    {
                        if (shared[i] == mat && !Add(r, $"sharedMaterials[{i}]")) goto done;
                    }
                }
            }
            if (target is Texture tex)
            {
                foreach (Material m in Resources.FindObjectsOfTypeAll<Material>())
                {
                    looked++;
                    if (m == null || m.shader == null) continue;
                    foreach (string property in m.GetTexturePropertyNames())
                    {
                        if (m.HasTexture(property) && m.GetTexture(property) == tex && !Add(m, property)) goto done;
                    }
                }
                foreach (Sprite sp in Resources.FindObjectsOfTypeAll<Sprite>())
                {
                    looked++;
                    if (sp.texture == tex && !Add(sp, "texture")) goto done;
                }
            }
            if (target is Shader shader)
            {
                foreach (Material m in Resources.FindObjectsOfTypeAll<Material>())
                {
                    looked++;
                    if (m.shader == shader && !Add(m, "shader")) goto done;
                }
            }
            if (target is Mesh mesh)
            {
                foreach (MeshFilter f in Resources.FindObjectsOfTypeAll<MeshFilter>())
                {
                    looked++;
                    if (f.sharedMesh == mesh && !Add(f, "sharedMesh")) goto done;
                }
                foreach (SkinnedMeshRenderer s in Resources.FindObjectsOfTypeAll<SkinnedMeshRenderer>())
                {
                    looked++;
                    if (s.sharedMesh == mesh && !Add(s, "sharedMesh")) goto done;
                }
                if (!Native(target, "UnityEngine.MeshCollider, UnityEngine.PhysicsModule", "sharedMesh", Add, ref looked)) goto done;
            }
            if (target is Sprite sprite)
            {
                foreach (SpriteRenderer s in Resources.FindObjectsOfTypeAll<SpriteRenderer>())
                {
                    looked++;
                    if (s.sprite == sprite && !Add(s, "sprite")) goto done;
                }
            }
            ObjectKind kind = TypeOf(targetType).Kind;
            if (kind == ObjectKind.Audio && !Native(target, "UnityEngine.AudioSource, UnityEngine.AudioModule", "clip", Add, ref looked)) goto done;
            if (kind == ObjectKind.Animation)
            {
                if (Read(target, "animationClips") is Array)
                {
                    if (!Native(target, "UnityEngine.Animator, UnityEngine.AnimationModule", "runtimeAnimatorController", Add, ref looked)) goto done;
                }
                else
                {
                    // A clip: the controllers that play it, and legacy Animation components.
                    Type controller = Type.GetType("UnityEngine.RuntimeAnimatorController, UnityEngine.AnimationModule");
                    if (controller != null)
                    {
                        foreach (UnityEngine.Object c in Resources.FindObjectsOfTypeAll(controller))
                        {
                            looked++;
                            if (Read(c, "animationClips") is Array clips && Array.IndexOf(clips, target) >= 0 && !Add(c, "animationClips")) goto done;
                        }
                    }
                    Type animation = Type.GetType("UnityEngine.Animation, UnityEngine.AnimationModule");
                    if (animation != null)
                    {
                        foreach (UnityEngine.Object a in Resources.FindObjectsOfTypeAll(animation))
                        {
                            looked++;
                            if (!(a is IEnumerable states)) continue;
                            foreach (object state in states)
                            {
                                if (Read(state, "clip") as UnityEngine.Object == target && !Add(a, "clip " + Read(state, "name"))) goto done;
                            }
                        }
                    }
                }
            }

            // What scripts keep in fields: every MonoBehaviour and ScriptableObject
            // whose type has a field (or a list, or a field of a serializable
            // class) that can hold the object.
            foreach (MonoBehaviour b in Resources.FindObjectsOfTypeAll<MonoBehaviour>())
            {
                looked++;
                if (!Fields(b, target, targetType, Add)) goto done;
            }
            foreach (ScriptableObject so in Resources.FindObjectsOfTypeAll<ScriptableObject>())
            {
                looked++;
                if (!Fields(so, target, targetType, Add)) goto done;
            }

        done:
            summary = $"{hits.Count} place(s){(hits.Count >= cap ? $", stopped at {cap}" : "")}; looked through {looked} objects in {sw.ElapsedMilliseconds} ms.";
            InspectorPlugin.Log.LogInfo($"[objects] Used by {target.name} ({targetType.Name}): {summary}");
            return hits;
        }

        private static Hit MakeHit(UnityEngine.Object holder, string member)
        {
            var hit = new Hit { Id = holder.GetInstanceID(), Member = member };
            if (holder is Component c)
            {
                bool inScene = c.gameObject.scene.IsValid();
                hit.InScene = inScene;
                hit.Where = InspectorModel.PathOf(c.transform) + " : " + c.GetType().Name + (inScene ? "" : "  (outside the scenes)");
            }
            else
            {
                hit.Where = Describe(holder);
            }
            return hit;
        }

        // A component type of a module this library does not reference, and its property that may hold the object.
        private static bool Native(UnityEngine.Object target, string typeName, string property, Func<UnityEngine.Object, string, bool> add, ref int looked)
        {
            Type type = Type.GetType(typeName);
            PropertyInfo p = type?.GetProperty(property);
            if (p == null) return true;
            foreach (UnityEngine.Object c in Resources.FindObjectsOfTypeAll(type))
            {
                looked++;
                object value;
                try { value = p.GetValue(c, null); } catch { continue; }
                if (value as UnityEngine.Object == target && !add(c, property)) return false;
            }
            return true;
        }

        // Where a field can hold an object of the target's type: the field, or
        // a field of a serializable class in it (UI Text keeps its font so).
        private sealed class FieldPath
        {
            public FieldInfo Outer;
            public FieldInfo Field;
            public bool IsList;
            public string Name;
        }

        private static readonly Dictionary<Type, Dictionary<Type, List<FieldPath>>> Paths = new Dictionary<Type, Dictionary<Type, List<FieldPath>>>();

        private static bool Fields(UnityEngine.Object holder, UnityEngine.Object target, Type targetType, Func<UnityEngine.Object, string, bool> add)
        {
            List<FieldPath> paths = PathsOf(holder.GetType(), targetType);
            foreach (FieldPath path in paths)
            {
                object value;
                try
                {
                    object owner = path.Outer != null ? path.Outer.GetValue(holder) : holder;
                    if (owner == null) continue;
                    value = path.Field.GetValue(owner);
                }
                catch
                {
                    continue;
                }
                if (value == null) continue;
                if (!path.IsList)
                {
                    if (value as UnityEngine.Object == target && !add(holder, path.Name)) return false;
                    continue;
                }
                if (value is IList list)
                {
                    for (int i = 0; i < list.Count; i++)
                    {
                        if (list[i] as UnityEngine.Object == target && !add(holder, $"{path.Name}[{i}]")) return false;
                    }
                }
            }
            return true;
        }

        private static List<FieldPath> PathsOf(Type holder, Type target)
        {
            if (!Paths.TryGetValue(holder, out Dictionary<Type, List<FieldPath>> byTarget))
            {
                Paths[holder] = byTarget = new Dictionary<Type, List<FieldPath>>();
            }
            if (byTarget.TryGetValue(target, out List<FieldPath> known))
            {
                return known;
            }
            var paths = new List<FieldPath>();
            foreach (FieldInfo f in FieldsOf(holder))
            {
                if (CanHold(f.FieldType, target, out bool isList))
                {
                    paths.Add(new FieldPath { Field = f, IsList = isList, Name = f.Name });
                }
                else if (IsNestable(f.FieldType))
                {
                    foreach (FieldInfo inner in FieldsOf(f.FieldType))
                    {
                        if (CanHold(inner.FieldType, target, out bool innerList))
                        {
                            paths.Add(new FieldPath { Outer = f, Field = inner, IsList = innerList, Name = f.Name + "." + inner.Name });
                        }
                    }
                }
            }
            byTarget[target] = paths;
            return paths;
        }

        private static IEnumerable<FieldInfo> FieldsOf(Type type)
        {
            const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            for (Type t = type; t != null && t != typeof(object) && t != typeof(MonoBehaviour) && t != typeof(Behaviour) && t != typeof(Component) && t != typeof(ScriptableObject) && t != typeof(UnityEngine.Object); t = t.BaseType)
            {
                foreach (FieldInfo f in t.GetFields(Any))
                {
                    if (!f.IsLiteral) yield return f;
                }
            }
        }

        private static bool CanHold(Type fieldType, Type target, out bool isList)
        {
            isList = false;
            if (typeof(UnityEngine.Object).IsAssignableFrom(fieldType))
            {
                return fieldType.IsAssignableFrom(target);
            }
            Type element = fieldType.IsArray ? fieldType.GetElementType()
                : fieldType.IsGenericType && fieldType.GetGenericTypeDefinition() == typeof(List<>) ? fieldType.GetGenericArguments()[0] : null;
            if (element != null && typeof(UnityEngine.Object).IsAssignableFrom(element) && element.IsAssignableFrom(target))
            {
                isList = true;
                return true;
            }
            return false;
        }

        private static bool IsNestable(Type t)
        {
            return t.IsClass && t != typeof(string) && !t.IsArray && !typeof(UnityEngine.Object).IsAssignableFrom(t)
                && !typeof(IEnumerable).IsAssignableFrom(t) && !typeof(Delegate).IsAssignableFrom(t) && t.IsSerializable;
        }

        // ---- for the console and the operations -------------------------------------------

        // An object of a kind by name: the one named so, else the only one whose name contains it.
        internal static ObjectEntry FindEntry(ObjectKind kind, string name, out string problem)
        {
            problem = null;
            List<ObjectEntry> candidates = List().Find(kind, "", true, int.MaxValue);
            ObjectEntry exact = candidates.Find(e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase));
            if (exact != null) return exact;
            List<ObjectEntry> partial = candidates.FindAll(e => (e.Name ?? "").IndexOf(name ?? "", StringComparison.OrdinalIgnoreCase) >= 0);
            if (partial.Count == 1) return partial[0];
            problem = partial.Count == 0
                ? $"No {ObjectList.FolderName(kind)} object named \"{name}\"."
                : $"{partial.Count} {ObjectList.FolderName(kind)} objects contain \"{name}\" ({string.Join(", ", partial.GetRange(0, Math.Min(5, partial.Count)).ConvertAll(e => e.Name).ToArray())}{(partial.Count > 5 ? ", ..." : "")}); give the whole name.";
            return null;
        }

        internal static string KindsText()
        {
            ObjectList list = List();
            int[] counts = list.Counts(false), all = list.Counts(true);
            var sb = new StringBuilder();
            for (int i = 0; i < counts.Length; i++)
            {
                sb.Append(ObjectList.FolderNames[i]).Append(": ").Append(counts[i].ToString("N0", CultureInfo.InvariantCulture));
                if (all[i] > counts[i]) sb.Append($" (+{all[i] - counts[i]} hidden)");
                sb.Append('\n');
            }
            sb.Append($"{list.All.Count} objects, listed in {list.BuildMs} ms.");
            return sb.ToString();
        }

        // ---- reflection helpers ------------------------------------------------------------

        private static readonly Dictionary<string, PropertyInfo> Properties = new Dictionary<string, PropertyInfo>();

        internal static object Read(object o, string property)
        {
            if (o == null) return null;
            string key = o.GetType().FullName + "|" + property;
            if (!Properties.TryGetValue(key, out PropertyInfo p))
            {
                Properties[key] = p = o.GetType().GetProperty(property, BindingFlags.Instance | BindingFlags.Public);
            }
            try
            {
                return p?.GetValue(o, null);
            }
            catch
            {
                return null;
            }
        }

        private static float Num(object v) => v is float f ? f : v is int i ? i : 0f;
    }
}
