using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using DragNWash.ModFramework.CodeGraph;
using HarmonyLib;
using Mono.Cecil;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;

namespace DragNWash.ModFramework.Inspector
{
    // The code graph (docs/CODE_GRAPH.md): the game's code as blocks, branches,
    // calls, callers, patches and events, read with Mono.Cecil from the game's
    // own assemblies. Operations for the Bridge's page only, never MCP: the
    // game's code stays on this computer. Structure, not source.
    //
    // The graph itself is CodeGraphModel (no Unity in it, shared with the
    // standalone app); this file adds what only the running game knows: which
    // assemblies are the game's, the Harmony patches and the UnityEvent listeners.
    internal static class InspectorCodeGraph
    {
        private const int MaxListeners = 50;

        // ---- the index ---------------------------------------------------------------

        private static CodeGraphModel _index;

        private static CodeGraphModel Get()
        {
            if (_index != null) return _index;
            var sw = Stopwatch.StartNew();
            var assemblies = new List<AssemblyDefinition>();
            string managed = Path.GetFullPath(Path.Combine(Application.dataPath, "Managed"));
            foreach (System.Reflection.Assembly a in AppDomain.CurrentDomain.GetAssemblies())
            {
                string location;
                try { location = a.Location; } catch { continue; }
                if (string.IsNullOrEmpty(location)) continue;
                if (!string.Equals(Path.GetDirectoryName(Path.GetFullPath(location)), managed, StringComparison.OrdinalIgnoreCase)) continue;
                if (!IsGameAssembly(a.GetName().Name)) continue;
                try
                {
                    assemblies.Add(AssemblyDefinition.ReadAssembly(location, new ReaderParameters { ReadWrite = false, InMemory = true }));
                }
                catch (Exception ex)
                {
                    InspectorPlugin.Log.LogWarning($"[code] Could not read {Path.GetFileName(location)}: {ex.Message}");
                }
            }
            var index = new CodeGraphModel(assemblies)
            {
                Source = "the game's assemblies",
                Patches = () =>
                {
                    Dictionary<string, MethodBase> patched = PatchedById();
                    return id => PatchesOf(patched, id);
                },
                Listeners = m => Listeners().TryGetValue(m.DeclaringType.FullName + "::" + m.Name, out List<string> found) ? found.Cast<object>() : null,
            };
            index.BuildMs = sw.ElapsedMilliseconds;
            InspectorPlugin.Log.LogInfo($"[code] Indexed {index.Methods.Count} methods of {index.Assemblies.Count} game assemblies in {index.BuildMs} ms.");
            return _index = index;
        }

        // The game's and its authors' assemblies (and YarnSpinner, which runs the
        // dialogue); not Unity's, .NET's, BepInEx's, the mods', nor the libraries
        // bundled with them (Yarn.* are YarnSpinner's copies of .NET packages).
        // Whether the code graph can draw this type: one of the game's assemblies, in the game's folder.
        internal static bool IsGameType(Type type)
        {
            try
            {
                string location = type.Assembly.Location;
                return !string.IsNullOrEmpty(location)
                    && string.Equals(Path.GetDirectoryName(Path.GetFullPath(location)), Path.GetFullPath(Path.Combine(Application.dataPath, "Managed")), StringComparison.OrdinalIgnoreCase)
                    && IsGameAssembly(type.Assembly.GetName().Name);
            }
            catch
            {
                return false;
            }
        }

        private static bool IsGameAssembly(string name)
        {
            string[] skip = { "System", "Unity.", "UnityEngine", "Mono.", "mscorlib", "netstandard", "Microsoft.", "Newtonsoft", "0Harmony", "BepInEx", "HarmonyX", "MonoMod",
                              "Yarn.", "YarnSpinner.Compiler", "MackySoft.", "com.rlabrecque.", "com.alelievr." };
            return !skip.Any(s => name.Equals(s.TrimEnd('.'), StringComparison.OrdinalIgnoreCase) || name.StartsWith(s, StringComparison.OrdinalIgnoreCase));
        }

        // Type::Name(ParamType,ParamType): stable for a game build, the same from Cecil and from reflection.
        internal static string Id(MethodReference m) => CodeGraphModel.Id(m);

        internal static string Id(MethodBase m) => CodeGraphModel.Id(m);

        // ---- patches and owners ------------------------------------------------------

        private static Dictionary<string, MethodBase> PatchedById()
        {
            var map = new Dictionary<string, MethodBase>(StringComparer.Ordinal);
            foreach (MethodBase m in Harmony.GetAllPatchedMethods())
            {
                try { map[Id(m)] = m; } catch { }
            }
            return map;
        }

        private static List<object> PatchesOf(Dictionary<string, MethodBase> patched, string id)
        {
            var list = new List<object>();
            if (!patched.TryGetValue(id, out MethodBase m)) return list;
            HarmonyLib.Patches info = Harmony.GetPatchInfo(m);
            if (info == null) return list;
            void Add(IEnumerable<Patch> patches, string kind)
            {
                foreach (Patch p in patches)
                {
                    list.Add(new Dictionary<string, object>
                    {
                        ["kind"] = kind,
                        ["owner"] = p.owner,
                        ["mod"] = ModFramework.NameOf(p.owner),
                        ["patch"] = (p.PatchMethod.DeclaringType?.Name ?? "") + "." + p.PatchMethod.Name,
                        ["priority"] = p.priority,
                    });
                }
            }
            Add(info.Prefixes, "prefix");
            Add(info.Postfixes, "postfix");
            Add(info.Transpilers, "transpiler");
            Add(info.Finalizers, "finalizer");
            return list;
        }

        // ---- UnityEvent listeners in the loaded scenes -------------------------------

        private static Dictionary<string, List<string>> _listeners;
        private static long _listenersMs;
        private static int _listenersComponents;
        private static readonly Dictionary<Type, FieldInfo[]> EventFields = new Dictionary<Type, FieldInfo[]>();

        internal static void OnSceneLoaded()
        {
            _listeners = null;
        }

        // Type::Name -> "field on Object/Path (persistent|run time)". Scanned once per scene load.
        private static Dictionary<string, List<string>> Listeners()
        {
            if (_listeners != null) return _listeners;
            var sw = Stopwatch.StartNew();
            var map = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            int components = 0;
            for (int s = 0; s < SceneManager.sceneCount; s++)
            {
                Scene scene = SceneManager.GetSceneAt(s);
                if (!scene.isLoaded) continue;
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    foreach (MonoBehaviour c in root.GetComponentsInChildren<MonoBehaviour>(true))
                    {
                        if (c == null) continue;
                        components++;
                        foreach (FieldInfo f in FieldsOf(c.GetType()))
                        {
                            UnityEventBase ev;
                            try { ev = f.GetValue(c) as UnityEventBase; } catch { continue; }
                            if (ev == null) continue;
                            string where = f.Name + " on " + InspectorModel.PathOf(c.transform) + " (" + c.GetType().Name + ")";
                            int n = ev.GetPersistentEventCount();
                            for (int i = 0; i < n; i++)
                            {
                                UnityEngine.Object target = ev.GetPersistentTarget(i);
                                if (target == null) continue;
                                AddListener(map, target.GetType(), ev.GetPersistentMethodName(i), where + ", persistent");
                            }
                            foreach (Delegate d in RuntimeCalls(ev))
                            {
                                if (d.Method.DeclaringType != null) AddListener(map, d.Method.DeclaringType, d.Method.Name, where + ", run time");
                            }
                        }
                    }
                }
            }
            _listenersMs = sw.ElapsedMilliseconds;
            _listenersComponents = components;
            InspectorPlugin.Log.LogDebug($"[code] Scanned {components} components for UnityEvent listeners in {_listenersMs} ms.");
            return _listeners = map;
        }

        private static void AddListener(Dictionary<string, List<string>> map, Type type, string method, string where)
        {
            // The listener names a method on the target's own type; the method may be declared on a base type.
            for (Type t = type; t != null && t != typeof(MonoBehaviour) && t != typeof(object); t = t.BaseType)
            {
                string key = (t.FullName ?? t.Name).Replace('+', '/') + "::" + method;
                if (!map.TryGetValue(key, out List<string> list)) map[key] = list = new List<string>();
                if (list.Count < MaxListeners && !list.Contains(where)) list.Add(where);
            }
        }

        private static FieldInfo[] FieldsOf(Type type)
        {
            if (EventFields.TryGetValue(type, out FieldInfo[] cached)) return cached;
            var list = new List<FieldInfo>();
            for (Type t = type; t != null && t != typeof(MonoBehaviour); t = t.BaseType)
            {
                foreach (FieldInfo f in t.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                {
                    if (typeof(UnityEventBase).IsAssignableFrom(f.FieldType)) list.Add(f);
                }
            }
            return EventFields[type] = list.ToArray();
        }

        private static IEnumerable<Delegate> RuntimeCalls(UnityEventBase ev)
        {
            var result = new List<Delegate>();
            try
            {
                object calls = typeof(UnityEventBase).GetField("m_Calls", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(ev);
                var list = calls?.GetType().GetField("m_RuntimeCalls", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(calls) as IList;
                if (list == null) return result;
                foreach (object call in list)
                {
                    FieldInfo df = call.GetType().GetField("Delegate", BindingFlags.NonPublic | BindingFlags.Instance) ?? call.GetType().GetField("m_Delegate", BindingFlags.NonPublic | BindingFlags.Instance);
                    if (df?.GetValue(call) is Delegate d) result.Add(d);
                }
            }
            catch { }
            return result;
        }

        // ---- operations --------------------------------------------------------------

        internal static void Register()
        {
            string g = Inspector.Guid;
            string method = "The method: an id from code.search, or Type:Method (the Code view's Copy).";
            Operation op;

            op = Operations.Register(g, "code.graph", "A method of the game as blocks and branches, with what it calls, the fields it touches, its callers, the patches on it and the events that lead to it. For the Bridge's page; not offered to AI clients.", OperationKind.Read,
                "{ id, type, name, signature, patches, callers, events, blocks, edges, calls, fields }",
                args => Get().Graph(args.String("method"), args.Bool("stub", false)),
                Operations.Parameter("method", OperationType.String, method, true),
                Operations.Parameter("stub", OperationType.Boolean, "For a coroutine: the method itself (which only builds its state machine) instead of the state machine's body."));
            if (op != null) op.PageOnly = true;

            op = Operations.Register(g, "code.type", "A type of the game: its methods in groups (Unity messages, public, private) and the calls between them.", OperationKind.Read,
                "{ type, assembly, base, groups: [{ name, methods }] }",
                args => Get().TypeGraph(args.String("type")),
                Operations.Parameter("type", OperationType.String, "The type: its full name or, when unique, its name.", true));
            if (op != null) op.PageOnly = true;

            op = Operations.Register(g, "code.callers", "The methods of the game that call a method (a coroutine's calls count as its method's).", OperationKind.Read,
                "a list of { id, name }",
                args => Get().CallersOf(Id(Get().FindMethod(args.String("method")))),
                Operations.Parameter("method", OperationType.String, method, true));
            if (op != null) op.PageOnly = true;

            op = Operations.Register(g, "code.search", "Types and methods of the game whose name contains the text.", OperationKind.Read,
                "a list of { kind, id, name }",
                args => Get().Search(args.String("text"), Math.Max(1, Math.Min(200, args.Int("max", 50)))),
                Operations.Parameter("text", OperationType.String, "Part of the name.", true),
                Operations.Parameter("max", OperationType.Number, "At most this many (1 to 200; 50 when left out)."));
            if (op != null) op.PageOnly = true;

            op = Operations.Register(g, "code.stats", "How big the code index is and what the last scans cost.", OperationKind.Read,
                "{ assemblies, methods, index_ms, listeners_ms, listeners_components }",
                args =>
                {
                    CodeGraphModel index = Get();
                    Listeners();
                    return new Dictionary<string, object>
                    {
                        ["assemblies"] = index.Assemblies.Select(a => (object)a.Name.Name).ToList(),
                        ["methods"] = index.Methods.Count,
                        ["index_ms"] = index.BuildMs,
                        ["listeners_ms"] = _listenersMs,
                        ["listeners_components"] = _listenersComponents,
                    };
                });
            if (op != null) op.PageOnly = true;
        }
    }
}
