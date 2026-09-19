using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Member = DragNWash.ModFramework.Inspector.InspectorModel.Member;

namespace DragNWash.ModFramework.Inspector
{
    // The Inspector's operations (docs/API_PLAN.md, stage 1): all read. They
    // see what the Inspector sees: objects by name or path, their children
    // and components, and member values as the rows show them.
    internal static class InspectorOperations
    {
        internal static void Register()
        {
            RegisterObjects();
            string g = Inspector.Guid;
            Operations.Register(g, "inspector.objects.find", "Objects in the loaded scenes whose name contains the text.", OperationKind.Read,
                "a list of { path, scene, active }", args =>
                {
                    int max = Math.Max(1, Math.Min(500, args.Int("max", 50)));
                    return InspectorModel.Search(args.String("text"), max)
                        .Where(n => n.Transform != null)
                        .Select(n => (object)Describe(n.Transform)).ToList();
                },
                Operations.Parameter("text", OperationType.String, "Part of the name.", true),
                Operations.Parameter("max", OperationType.Number, "At most this many (1 to 500; 50 when left out)."));

            Operations.Register(g, "inspector.objects.children", "An object's children, or the root objects of the loaded scenes when no path is given.", OperationKind.Read,
                "a list of { path, scene, active, children }", args =>
                {
                    if (!args.Has("path"))
                    {
                        var roots = new List<object>();
                        for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
                        {
                            var scene = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i);
                            if (!scene.isLoaded) continue;
                            roots.AddRange(scene.GetRootGameObjects().Select(r => (object)Describe(r.transform)));
                        }
                        return roots;
                    }
                    Transform t = Object(args.String("path")).transform;
                    var children = new List<object>();
                    for (int i = 0; i < t.childCount; i++) children.Add(Describe(t.GetChild(i)));
                    return children;
                },
                Operations.Parameter("path", OperationType.String, "The object: a path (Root/Child) or a name."));

            Operations.Register(g, "inspector.components.list", "An object's components, in order, with each one's index among components of its type.", OperationKind.Read,
                "a list of { type, index, enabled }", args =>
                {
                    GameObject go = Object(args.String("path"));
                    var counts = new Dictionary<Type, int>();
                    var list = new List<object>();
                    foreach (Component c in go.GetComponents<Component>())
                    {
                        if (c == null) { list.Add(new Dictionary<string, object> { ["type"] = "(missing script)" }); continue; }
                        counts.TryGetValue(c.GetType(), out int n);
                        counts[c.GetType()] = n + 1;
                        list.Add(new Dictionary<string, object>
                        {
                            ["type"] = c.GetType().Name,
                            ["index"] = n,
                            ["enabled"] = c is Behaviour b ? (object)b.enabled : null,
                        });
                    }
                    return list;
                },
                Operations.Parameter("path", OperationType.String, "The object: a path (Root/Child) or a name.", true));

            Operations.Register(g, "inspector.member.get", "A component's members as the Inspector shows them, or one member's value.", OperationKind.Read,
                "{ name, type, value, private, writable } for one member, or a list of them", args =>
                {
                    GameObject go = Object(args.String("path"));
                    string typeName = args.String("component");
                    int index = args.Int("index", 0);
                    Component c = go.GetComponents<Component>().Where(x => x != null && (x.GetType().Name == typeName || x.GetType().FullName == typeName)).Skip(index).FirstOrDefault();
                    if (c == null) throw new InvalidOperationException($"{go.name} has no {typeName}{(index > 0 ? " #" + index : "")} (inspector.components.list shows its components).");
                    bool withPrivate = args.Bool("private", false);
                    IEnumerable<Member> members = InspectorModel.MembersOf(c.GetType()).Where(m => withPrivate || !m.IsPrivate);
                    if (args.Has("member"))
                    {
                        string name = args.String("member");
                        Member m = InspectorModel.MembersOf(c.GetType()).FirstOrDefault(x => x.Name == name);
                        if (m == null) throw new InvalidOperationException($"{c.GetType().Name} has no member {name}.");
                        return Value(m, c);
                    }
                    return members.Select(m => (object)Value(m, c)).ToList();
                },
                Operations.Parameter("path", OperationType.String, "The object: a path (Root/Child) or a name.", true),
                Operations.Parameter("component", OperationType.String, "The component's type name (Light, Transform, a game script).", true),
                Operations.Parameter("index", OperationType.Number, "Which one, when the object has several of that type (0 first)."),
                Operations.Parameter("member", OperationType.String, "One member; all of them when left out."),
                Operations.Parameter("private", OperationType.Boolean, "Also private members (where the game's scripts keep their settings)."));

            Operations.Register(g, "inspector.selection.get", "What is selected in the Inspector: the object and the component or material open.", OperationKind.Read,
                "{ path, scene, target } in Scene, { view: objects, object, kind } in Objects, or null", args =>
                {
                    if (InspectorTab.InObjects)
                    {
                        if (!(InspectorTab.Target is UnityEngine.Object o) || !o) return null;
                        UnityEngine.Object listed = o is Component oc ? oc.gameObject : o;
                        return new Dictionary<string, object>
                        {
                            ["view"] = "objects",
                            ["object"] = InspectorObjects.Describe(listed),
                            ["kind"] = ObjectList.FolderName(InspectorObjects.TypeOf(listed.GetType()).Kind),
                            ["target"] = o is Component tc2 ? tc2.GetType().Name : o.GetType().Name,
                        };
                    }
                    GameObject go = InspectorTab.SelectedObject;
                    if (go == null) return null;
                    Dictionary<string, object> d = Describe(go.transform);
                    object target = InspectorTab.Target;
                    d["target"] = target is Component tc ? tc.GetType().Name : target is Material mat ? "Material " + mat.name : target is GameObject ? "GameObject" : null;
                    return d;
                });
        }

        // The object explorer (docs/OBJECT_EXPLORER.md): what is loaded, by kind, and where one is used.
        internal static void RegisterObjects()
        {
            string g = Inspector.Guid;
            OperationParameter kind = Operations.Parameter("kind", OperationType.String, "The kind (a folder of Objects).", true,
                "textures", "sprites", "materials", "shaders", "meshes", "audio", "animation", "fonts", "data", "outside", "other");

            Operations.Register(g, "inspector.loaded.kinds", "Every loaded object by kind (the folders of the Inspector's Objects view), with counts.", OperationKind.Read,
                "a list of { kind, count, hidden }, and how long listing took", args =>
                {
                    ObjectList list = InspectorObjects.List();
                    int[] shown = list.Counts(false), all = list.Counts(true);
                    return new Dictionary<string, object>
                    {
                        ["kinds"] = Enumerable.Range(0, shown.Length).Select(i => (object)new Dictionary<string, object>
                        {
                            ["kind"] = ObjectList.FolderNames[i],
                            ["count"] = shown[i],
                            ["hidden"] = all[i] - shown[i],
                        }).ToList(),
                        ["listed_ms"] = list.BuildMs,
                    };
                });

            Operations.Register(g, "inspector.loaded.list", "Loaded objects of one kind, by name: textures, materials, meshes, sounds, the game's data (ScriptableObjects), GameObjects outside the scenes.", OperationKind.Read,
                "a list of { name, type, fact, id }", args =>
                {
                    ObjectKind k = Kind(args.String("kind"));
                    int max = Math.Max(1, Math.Min(1000, args.Int("max", 200)));
                    return InspectorObjects.List().Find(k, args.String("filter") ?? "", args.Bool("hidden", false), max)
                        .Select(e => (object)new Dictionary<string, object>
                        {
                            ["name"] = e.Name,
                            ["type"] = e.Type.Name,
                            ["fact"] = InspectorObjects.Fact(e),
                            ["id"] = e.Id,
                        }).ToList();
                },
                kind,
                Operations.Parameter("filter", OperationType.String, "Only names that contain this; t:Type limits it to a type (t:Texture2D)."),
                Operations.Parameter("hidden", OperationType.Boolean, "Also objects Unity hides (HideFlags)."),
                Operations.Parameter("max", OperationType.Number, "At most this many (1 to 1000; 200 when left out)."));

            Operations.Register(g, "inspector.loaded.usedby", "Where a loaded object is used: renderers, mesh filters, sprites, audio sources, animators, materials, and the fields of scripts and ScriptableObjects.", OperationKind.Read,
                "{ object, summary, places: a list of { where, member, in_scene } }", args =>
                {
                    ObjectKind k = Kind(args.String("kind"));
                    ObjectEntry e;
                    if (args.Has("id"))
                    {
                        e = InspectorObjects.List().ById(args.Int("id"));
                        if (e == null) throw new InvalidOperationException($"No listed object has the id {args.Int("id")} (inspector.loaded.list gives them).");
                    }
                    else
                    {
                        e = InspectorObjects.FindEntry(k, args.String("name") ?? "", out string problem);
                        if (e == null) throw new InvalidOperationException(problem);
                    }
                    UnityEngine.Object o = InspectorObjects.Find(e);
                    if (o == null) throw new InvalidOperationException($"{e.Name} was destroyed since it was listed.");
                    int max = Math.Max(1, Math.Min(InspectorObjects.HitCap, args.Int("max", 200)));
                    List<InspectorObjects.Hit> hits = InspectorObjects.UsedBy(o, max, out string summary);
                    return new Dictionary<string, object>
                    {
                        ["object"] = InspectorObjects.Describe(o),
                        ["summary"] = summary,
                        ["places"] = hits.Select(h => (object)new Dictionary<string, object> { ["where"] = h.Where, ["member"] = h.Member, ["in_scene"] = h.InScene }).ToList(),
                    };
                },
                kind,
                Operations.Parameter("name", OperationType.String, "The object's name (the whole name, or a part only one object has)."),
                Operations.Parameter("id", OperationType.Number, "Or its id, from inspector.loaded.list."),
                Operations.Parameter("max", OperationType.Number, "At most this many places (1 to 2000; 200 when left out)."));
        }

        private static ObjectKind Kind(string text)
        {
            if (!ObjectList.TryParseKind(text, out ObjectKind kind))
            {
                throw new InvalidOperationException($"No kind \"{text}\" (inspector.loaded.kinds lists them).");
            }
            return kind;
        }

        private static GameObject Object(string path)
        {
            GameObject go = InspectorModel.Find(path);
            if (go == null) throw new InvalidOperationException($"No object \"{path}\" in the loaded scenes (inspector.objects.find looks by name).");
            return go;
        }

        private static Dictionary<string, object> Describe(Transform t)
        {
            return new Dictionary<string, object>
            {
                ["path"] = InspectorModel.PathOf(t),
                ["scene"] = t.gameObject.scene.name,
                ["active"] = t.gameObject.activeInHierarchy,
                ["children"] = t.childCount,
            };
        }

        private static Dictionary<string, object> Value(Member m, object target)
        {
            object value;
            string failure = null;
            try
            {
                value = InspectorModel.Format(m.Get(target));
            }
            catch (Exception ex)
            {
                value = null;
                failure = (ex.InnerException ?? ex).Message;
            }
            var d = new Dictionary<string, object>
            {
                ["name"] = m.Name,
                ["type"] = m.Type?.Name,
                ["value"] = value,
                ["private"] = m.IsPrivate,
                ["writable"] = m.CanWrite,
            };
            if (failure != null) d["error"] = failure;
            return d;
        }
    }
}
