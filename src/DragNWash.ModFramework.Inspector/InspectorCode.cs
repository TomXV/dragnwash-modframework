using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using HarmonyLib;
using Mono.Cecil;
using Mono.Cecil.Cil;
using UnityEngine;
using UnityEngine.Events;
using DragNWash.ModFramework.ToolWindow;
using TW = global::DragNWash.ModFramework.ToolWindow.ToolWindow;

namespace DragNWash.ModFramework.Inspector
{
    // The "Code" view of a component: what the game has for it, read from
    // metadata (the game ships no source). Its type and assembly, its methods
    // with a copyable Harmony / GameHooks name, the Harmony patches mods put on
    // them, the listeners of its UnityEvents, and a method's IL read with
    // Mono.Cecil. A real decompilation to C# would need ILSpy on board; the
    // "Copy for dnSpy" name is the bridge to that on the desk.
    internal static class InspectorCode
    {
        private sealed class MethodRow
        {
            public MethodInfo Method;
            public string Signature;
            public string HarmonyName;    // Type:Method, what GameHooks.Require and AccessTools.Method take
            public int Overloads;         // methods of the type with this name: more than one needs the parameter types
            public bool Inherited;
            public Patches Patches;
        }

        private static Type _type;
        private static List<MethodRow> _methods;
        private static List<string> _events;
        private static string _header;
        private static MethodInfo _ilFor;
        private static string[] _il;
        private static Vector2 _scroll;
        private static bool _showInherited;
        private static bool _showPrivateMethods;
        private static bool _gameType;
        private static GUIStyle _cell, _muted, _mono, _accent;

        internal static void Reset()
        {
            _type = null;
            _methods = null;
            _events = null;
            _ilFor = null;
            _il = null;
            _scroll = Vector2.zero;
        }

        private static void EnsureStyles(ToolWindowStyles s)
        {
            if (_cell != null) return;
            _cell = new GUIStyle(s.Label) { wordWrap = false, clipping = TextClipping.Clip };
            _muted = new GUIStyle(s.MutedLabel) { wordWrap = false, clipping = TextClipping.Clip };
            _mono = new GUIStyle(s.MutedLabel) { wordWrap = false, clipping = TextClipping.Clip };
            _accent = new GUIStyle(_cell);
            _accent.normal.textColor = TW.AccentColor;
            _accent.hover.textColor = TW.AccentColor;
        }

        private static void Gather(object target)
        {
            Type type = target.GetType();
            if (_type == type && _methods != null)
            {
                return;
            }
            Reset();
            _type = type;
            _gameType = InspectorCodeGraph.IsGameType(type);
            string assembly = type.Assembly.GetName().Name;
            _header = $"{type.FullName}   ({assembly}{(type.BaseType != null ? ", : " + type.BaseType.Name : "")})";
            _methods = new List<MethodRow>();
            var patched = new HashSet<MethodBase>(Harmony.GetAllPatchedMethods());
            const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
            for (Type t = type; t != null && t != typeof(object) && t != typeof(MonoBehaviour) && t != typeof(Behaviour) && t != typeof(Component) && t != typeof(UnityEngine.Object); t = t.BaseType)
            {
                foreach (MethodInfo m in t.GetMethods(All))
                {
                    if (m.IsSpecialName && (m.Name.StartsWith("get_") || m.Name.StartsWith("set_") || m.Name.StartsWith("add_") || m.Name.StartsWith("remove_")))
                    {
                        continue;
                    }
                    // Unity's bindings generate an _Injected twin per native call: noise, not code anyone hooks.
                    if (m.Name.EndsWith("_Injected", StringComparison.Ordinal))
                    {
                        continue;
                    }
                    var sb = new StringBuilder();
                    sb.Append(m.IsPublic ? "public " : m.IsPrivate ? "private " : "internal ");
                    if (m.IsStatic) sb.Append("static ");
                    sb.Append(Short(m.ReturnType)).Append(' ').Append(m.Name).Append('(');
                    ParameterInfo[] ps = m.GetParameters();
                    for (int i = 0; i < ps.Length; i++)
                    {
                        if (i > 0) sb.Append(", ");
                        sb.Append(Short(ps[i].ParameterType)).Append(' ').Append(ps[i].Name);
                    }
                    sb.Append(')');
                    _methods.Add(new MethodRow
                    {
                        Method = m,
                        Signature = sb.ToString(),
                        HarmonyName = t.FullName + ":" + m.Name,
                        Overloads = 0,
                        Inherited = t != type,
                        Patches = patched.Contains(m) ? Harmony.GetPatchInfo(m) : null,
                    });
                }
            }
            // Methods of one type sharing a name: their patches need the
            // parameter types, the name alone would be ambiguous.
            var byName = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (MethodRow mr in _methods)
            {
                string key = mr.Method.DeclaringType.FullName + ":" + mr.Method.Name;
                byName.TryGetValue(key, out int n);
                byName[key] = n + 1;
            }
            foreach (MethodRow mr in _methods)
            {
                mr.Overloads = byName[mr.Method.DeclaringType.FullName + ":" + mr.Method.Name];
            }
            _methods.Sort((a, b) => a.Inherited != b.Inherited ? a.Inherited.CompareTo(b.Inherited) : string.Compare(a.Method.Name, b.Method.Name, StringComparison.Ordinal));

            // UnityEvents: the persistent (serialized) listeners and the ones added at run time.
            _events = new List<string>();
            foreach (FieldInfo f in AllFields(type))
            {
                if (!typeof(UnityEventBase).IsAssignableFrom(f.FieldType))
                {
                    continue;
                }
                UnityEventBase ev;
                try { ev = f.GetValue(target) as UnityEventBase; } catch { continue; }
                if (ev == null) continue;
                int n = ev.GetPersistentEventCount();
                for (int i = 0; i < n; i++)
                {
                    UnityEngine.Object o = ev.GetPersistentTarget(i);
                    _events.Add($"{f.Name}  ->  {(o != null ? o.GetType().Name + " on " + o.name : "?")} . {ev.GetPersistentMethodName(i)}   (persistent)");
                }
                foreach (Delegate d in RuntimeCalls(ev))
                {
                    string where = d.Target is UnityEngine.Object uo ? uo.GetType().Name + " on " + uo.name : d.Target?.GetType().Name ?? "static";
                    _events.Add($"{f.Name}  ->  {where} . {d.Method.DeclaringType?.Name}.{d.Method.Name}   (run time)");
                }
            }
        }

        private static IEnumerable<FieldInfo> AllFields(Type type)
        {
            for (Type t = type; t != null && t != typeof(MonoBehaviour); t = t.BaseType)
            {
                foreach (FieldInfo f in t.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                {
                    yield return f;
                }
            }
        }

        // UnityEventBase keeps run-time listeners in m_Calls.m_RuntimeCalls, each with a Delegate.
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

        private static string Short(Type t)
        {
            if (t == typeof(void)) return "void";
            if (t == typeof(int)) return "int";
            if (t == typeof(float)) return "float";
            if (t == typeof(bool)) return "bool";
            if (t == typeof(string)) return "string";
            return t.Name;
        }

        // The method's IL through Mono.Cecil, from the assembly file on disk.
        // ---- the patch stub ----------------------------------------------------------

        // The Harmony patch a mod would write for this method: the guard that
        // checks the game still has it (so a game update turns the feature off
        // instead of breaking it), the attribute - with the parameter types
        // when the name alone is ambiguous - and a Prefix and a Postfix whose
        // parameters are the ones Harmony fills in. Pasted into a mod, with the
        // GUID and the feature name filled in, it compiles and runs.
        private static string PatchStub(MethodRow mr)
        {
            MethodInfo m = mr.Method;
            Type t = m.DeclaringType;
            var sb = new StringBuilder();
            string cls = Identifier(t.Name + m.Name) + "Patch";
            string target = mr.HarmonyName;
            ParameterInfo[] ps = m.GetParameters();
            string types = "";
            if (mr.Overloads > 1)
            {
                var list = new List<string>();
                var kinds = new List<string>();
                bool byRefAnywhere = false;
                foreach (ParameterInfo p in ps)
                {
                    list.Add("typeof(" + CSharp(p.ParameterType) + ")");
                    // An attribute takes constants, so a ref or out parameter
                    // cannot be written as typeof(T).MakeByRefType(): Harmony
                    // takes a second array saying how each one is passed.
                    string kind = p.IsOut ? "Out" : p.ParameterType.IsByRef ? "Ref" : p.ParameterType.IsPointer ? "Pointer" : "Normal";
                    if (kind != "Normal") byRefAnywhere = true;
                    kinds.Add("ArgumentType." + kind);
                }
                types = ", new Type[] { " + string.Join(", ", list.ToArray()) + " }";
                if (byRefAnywhere)
                {
                    types += ", new ArgumentType[] { " + string.Join(", ", kinds.ToArray()) + " }";
                }
            }
            sb.Append("// ").Append(mr.Signature).Append('\n');
            if (m.IsGenericMethodDefinition)
            {
                sb.Append("// A generic method: Harmony needs the closed method, see AccessTools.Method(..., generics).\n");
            }
            sb.Append("// In the mod: the check first, the patch only when it passes. MyMod.Guid is your plugin's BepInEx GUID.\n");
            sb.Append("if (GameHooks.Require(MyMod.Guid, \"<what stops working, in the player's words>\", \"").Append(target).Append("\"))\n");
            sb.Append("{\n    new Harmony(MyMod.Guid).PatchAll(typeof(").Append(cls).Append("));\n}\n\n");
            sb.Append("[HarmonyPatch(typeof(").Append(CSharp(t)).Append("), \"").Append(m.Name).Append('"').Append(types).Append(")]\n");
            sb.Append("internal static class ").Append(cls).Append("\n{\n");

            var args = new List<string>();
            if (!m.IsStatic)
            {
                args.Add(CSharp(t) + " __instance");
            }
            foreach (ParameterInfo p in ps)
            {
                // Harmony names a parameter as the game does; by-ref and out
                // parameters arrive as ref, and any of them may be left out.
                bool byRef = p.ParameterType.IsByRef;
                args.Add((byRef ? "ref " : "") + CSharp(byRef ? p.ParameterType.GetElementType() : p.ParameterType) + " " + Identifier(p.Name));
            }
            bool returns = m.ReturnType != typeof(void);
            string resultType = returns ? CSharp(m.ReturnType) : null;

            sb.Append("    // Before the game's method. Return false to skip it (and set __result).\n");
            sb.Append("    [HarmonyPrefix]\n");
            var prefix = new List<string>(args);
            if (returns) prefix.Add("ref " + resultType + " __result");
            sb.Append("    private static bool Prefix(").Append(string.Join(", ", prefix.ToArray())).Append(")\n");
            sb.Append("    {\n        return true;\n    }\n\n");

            sb.Append("    // After it").Append(returns ? ", with what it returned in __result." : ".").Append("\n");
            sb.Append("    [HarmonyPostfix]\n");
            var postfix = new List<string>(args);
            if (returns) postfix.Add("ref " + resultType + " __result");
            sb.Append("    private static void Postfix(").Append(string.Join(", ", postfix.ToArray())).Append(")\n");
            sb.Append("    {\n    }\n}\n");
            return sb.ToString();
        }

        // A type as it is written in C#: nested types with a dot, generics in
        // angle brackets, the built-in names for the common ones.
        private static string CSharp(Type t)
        {
            if (t.IsByRef) return CSharp(t.GetElementType());
            if (t.IsArray) return CSharp(t.GetElementType()) + "[]";
            switch (t.FullName)
            {
                case "System.Void": return "void";
                case "System.Boolean": return "bool";
                case "System.Byte": return "byte";
                case "System.SByte": return "sbyte";
                case "System.Int16": return "short";
                case "System.UInt16": return "ushort";
                case "System.Int32": return "int";
                case "System.UInt32": return "uint";
                case "System.Int64": return "long";
                case "System.UInt64": return "ulong";
                case "System.Single": return "float";
                case "System.Double": return "double";
                case "System.Decimal": return "decimal";
                case "System.String": return "string";
                case "System.Object": return "object";
                case "System.Char": return "char";
            }
            string name = (t.FullName ?? t.Name).Replace('+', '.');
            if (!t.IsGenericType)
            {
                return name;
            }
            int tick = name.IndexOf('`');
            if (tick > 0) name = name.Substring(0, tick);
            var parts = new List<string>();
            foreach (Type a in t.GetGenericArguments())
            {
                parts.Add(CSharp(a));
            }
            return name + "<" + string.Join(", ", parts.ToArray()) + ">";
        }

        // A name C# will take: the compiler-generated ones hold characters it will not.
        private static string Identifier(string name)
        {
            if (string.IsNullOrEmpty(name)) return "value";
            var sb = new StringBuilder();
            foreach (char c in name)
            {
                sb.Append(char.IsLetterOrDigit(c) || c == '_' ? c : '_');
            }
            if (char.IsDigit(sb[0])) sb.Insert(0, '_');
            return sb.ToString();
        }

        private static string[] Disassemble(MethodInfo m)
        {
            try
            {
                string path = m.DeclaringType.Assembly.Location;
                if (string.IsNullOrEmpty(path)) return new[] { "No assembly file for this type (loaded from memory)." };
                using (var asm = AssemblyDefinition.ReadAssembly(path, new ReaderParameters { ReadWrite = false, InMemory = true }))
                {
                    TypeDefinition td = asm.MainModule.GetType(m.DeclaringType.FullName.Replace('+', '/'));
                    if (td == null) return new[] { "Type not found in " + System.IO.Path.GetFileName(path) };
                    foreach (MethodDefinition md in td.Methods)
                    {
                        if (md.Name != m.Name || md.Parameters.Count != m.GetParameters().Length) continue;
                        bool same = true;
                        ParameterInfo[] ps = m.GetParameters();
                        for (int i = 0; i < ps.Length; i++)
                        {
                            if (md.Parameters[i].ParameterType.Name != ps[i].ParameterType.Name) { same = false; break; }
                        }
                        if (!same) continue;
                        if (!md.HasBody) return new[] { "(no body)" };
                        var lines = new List<string>();
                        foreach (Instruction ins in md.Body.Instructions)
                        {
                            lines.Add(ins.ToString());
                        }
                        return lines.ToArray();
                    }
                }
                return new[] { "Method not found in the assembly." };
            }
            catch (Exception ex)
            {
                return new[] { "IL not available: " + ex.Message };
            }
        }

        // Draws the view into the pane; the caller has drawn the strip above.
        internal static void Draw(Rect pane, object target, ToolWindowStyles s, float row)
        {
            EnsureStyles(s);
            if (target == null || target is GameObject || target is Material)
            {
                GUI.Label(new Rect(pane.x + 4, pane.y + 2, pane.width - 8, row), "Code: select a component.", _muted);
                return;
            }
            Gather(target);
            float x = pane.x + 4, y = pane.y + 2, w = pane.width - 8;
            GUI.Label(new Rect(x, y, w, row), TW.Drawable(_header), _cell);
            y += row;
            float bx = x;
            if (GUI.Button(new Rect(bx, y, 150, row), "Copy for dnSpy", s.Button))
            {
                GUIUtility.systemCopyBuffer = _type.FullName;
                TW.ShowNotice($"Copied \"{_type.FullName}\"; search it in dnSpy or ILSpy on {_type.Assembly.GetName().Name}.dll.");
            }
            bx += 158;
            if (GUI.Button(new Rect(bx, y, 100, row), "Inherited", _showInherited ? s.SelectedButton : s.Button))
            {
                _showInherited = !_showInherited;
            }
            bx += 108;
            if (GUI.Button(new Rect(bx, y, 90, row), "Private", _showPrivateMethods ? s.SelectedButton : s.Button))
            {
                _showPrivateMethods = !_showPrivateMethods;
            }
            bx += 98;
            // The graph draws the game's code only, not Unity's or .NET's.
            if (_gameType && GUI.Button(new Rect(bx, y, 110, row), "Type graph", s.Button))
            {
                OpenGraph("t:" + _type.FullName.Replace('+', '/'));
            }
            if (_gameType) bx += 118;
            GUI.Label(new Rect(bx, y, w - (bx - x), row), "Copy gives Type:Method, for GameHooks.Require and AccessTools.Method; Patch gives the whole Harmony patch, guard and all.", _muted);
            y += row + 4;

            // Rows: events first, then methods; an opened method's IL follows it.
            var lines = new List<Action<Rect>>();
            var heights = new List<float>();
            if (_events.Count > 0)
            {
                lines.Add(r => GUI.Label(r, "UnityEvent listeners", _accent)); heights.Add(row);
                foreach (string e in _events)
                {
                    string text = e;
                    lines.Add(r => GUI.Label(r, TW.Drawable(text), _muted)); heights.Add(row);
                }
            }
            lines.Add(r => GUI.Label(r, "Methods", _accent)); heights.Add(row);
            foreach (MethodRow m in _methods)
            {
                if (m.Inherited && !_showInherited) continue;
                if (!m.Method.IsPublic && !_showPrivateMethods && m.Patches == null) continue;
                MethodRow mr = m;
                lines.Add(r =>
                {
                    bool open = _ilFor == mr.Method;
                    if (GUI.Button(new Rect(r.x, r.y + 2, 60, row - 4), "Copy", s.Button))
                    {
                        GUIUtility.systemCopyBuffer = mr.HarmonyName;
                        TW.ShowNotice($"Copied \"{mr.HarmonyName}\".");
                    }
                    if (GUI.Button(new Rect(r.x + 64, r.y + 2, 56, row - 4), "Patch", s.Button))
                    {
                        GUIUtility.systemCopyBuffer = PatchStub(mr);
                        TW.ShowNotice($"Copied a Harmony patch for {mr.Method.Name}: paste it into your mod.");
                    }
                    if (GUI.Button(new Rect(r.x + 124, r.y + 2, 40, row - 4), "IL", open ? s.SelectedButton : s.Button))
                    {
                        if (open) { _ilFor = null; _il = null; }
                        else { _ilFor = mr.Method; _il = Disassemble(mr.Method); }
                    }
                    if (InspectorCodeGraph.IsGameType(mr.Method.DeclaringType) && GUI.Button(new Rect(r.x + 168, r.y + 2, 60, row - 4), "Graph", s.Button))
                    {
                        OpenGraph("m:" + InspectorCodeGraph.Id(mr.Method));
                    }
                    GUI.Label(new Rect(r.x + 234, r.y, r.width - 234, row), TW.Drawable(mr.Signature + (mr.Overloads > 1 ? "   (" + mr.Overloads + " overloads)" : "") + (mr.Inherited ? "   (" + mr.Method.DeclaringType.Name + ")" : "")), mr.Patches != null ? _accent : (mr.Method.IsPublic ? _cell : _muted));
                });
                heights.Add(row);
                if (m.Patches != null)
                {
                    foreach (string p in Describe(m.Patches))
                    {
                        string text = "    patched: " + p;
                        lines.Add(r => GUI.Label(r, TW.Drawable(text), _accent)); heights.Add(row);
                    }
                }
                if (_ilFor == m.Method && _il != null)
                {
                    foreach (string il in _il)
                    {
                        string text = "        " + il;
                        lines.Add(r => GUI.Label(r, TW.Drawable(text), _mono)); heights.Add(row - 8);
                    }
                }
            }
            var view = new Rect(x, y, w, pane.yMax - y - 2);
            float total = 0;
            foreach (float h in heights) total += h;
            float inner = view.width - 20;
            TW.ApplyScroll(view, ref _scroll);
            _scroll = GUI.BeginScrollView(view, _scroll, new Rect(0, 0, inner, Mathf.Max(view.height, total)), false, false);
            float ry = 0;
            for (int i = 0; i < lines.Count; i++)
            {
                if (ry + heights[i] >= _scroll.y && ry <= _scroll.y + view.height)
                {
                    lines[i](new Rect(0, ry, inner, heights[i]));
                }
                ry += heights[i];
            }
            GUI.EndScrollView();
        }

        // The code graph opens in the browser through the Bridge (docs/CODE_GRAPH.md), when it is installed and on.
        private static void OpenGraph(string focus)
        {
            if (Operations.Find("bridge.page.open") == null)
            {
                TW.ShowNotice("The graph needs the Bridge library, on (its tab in this window).");
                return;
            }
            OperationResult r = Operations.CallNow("bridge.page.open", new Dictionary<string, object> { ["focus"] = focus }, "inspector");
            TW.ShowNotice(r.Ok ? "The graph opens in your browser." : r.Error);
        }

        private static IEnumerable<string> Describe(Patches p)
        {
            foreach (Patch x in p.Prefixes) yield return $"prefix by {x.owner} ({x.PatchMethod.DeclaringType?.Name}.{x.PatchMethod.Name})";
            foreach (Patch x in p.Postfixes) yield return $"postfix by {x.owner} ({x.PatchMethod.DeclaringType?.Name}.{x.PatchMethod.Name})";
            foreach (Patch x in p.Transpilers) yield return $"transpiler by {x.owner} ({x.PatchMethod.DeclaringType?.Name}.{x.PatchMethod.Name})";
            foreach (Patch x in p.Finalizers) yield return $"finalizer by {x.owner} ({x.PatchMethod.DeclaringType?.Name}.{x.PatchMethod.Name})";
        }
    }
}
