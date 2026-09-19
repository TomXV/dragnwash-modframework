using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace DragNWash.ModFramework.CodeGraph
{
    // The code graph's model (docs/CODE_GRAPH.md): assemblies read with
    // Mono.Cecil, as blocks, branches, calls, callers and types, in the JSON the
    // page draws. Only Mono.Cecil and .NET here, no Unity, Harmony or BepInEx:
    // the Inspector adds the patches and the UnityEvent listeners of the running
    // game through the two hooks below, and the standalone app
    // (codegraph-standalone/, docs/CODE_GRAPH_STANDALONE.md) compiles this same
    // file to show any assembly without the game.
    internal sealed class CodeGraphModel
    {
        private const int MaxLinesPerBlock = 12;
        private const int MaxIlPerBlock = 200;
        private const int MaxCallers = 100;

        internal static readonly string[] UnityMessages =
        {
            "Awake", "Start", "OnEnable", "OnDisable", "OnDestroy", "Update", "LateUpdate", "FixedUpdate", "OnGUI",
            "OnTriggerEnter", "OnTriggerExit", "OnTriggerStay", "OnCollisionEnter", "OnCollisionExit", "OnCollisionStay",
            "OnValidate", "Reset", "OnApplicationQuit", "OnApplicationFocus", "OnApplicationPause", "OnAnimatorMove", "OnAnimatorIK",
            "OnBecameVisible", "OnBecameInvisible", "OnRenderObject", "OnWillRenderObject", "OnPreRender", "OnPostRender",
        };

        internal readonly List<AssemblyDefinition> Assemblies = new List<AssemblyDefinition>();
        internal readonly Dictionary<string, MethodDefinition> Methods = new Dictionary<string, MethodDefinition>(StringComparer.Ordinal);
        internal readonly Dictionary<string, TypeDefinition> Types = new Dictionary<string, TypeDefinition>(StringComparer.Ordinal);
        internal readonly Dictionary<string, List<string>> Callers = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        internal readonly Dictionary<string, string> MachineOwner = new Dictionary<string, string>(StringComparer.Ordinal);   // state machine type -> method id
        internal long BuildMs;

        // Where the assemblies come from, for messages ("the game's assemblies").
        internal string Source = "the given assemblies";

        // Makes, once per graph, a lookup of the patches on a method id: a list of
        // { kind, owner, mod, patch, priority }, empty when none. Null: no patches.
        internal Func<Func<string, List<object>>> Patches { get; set; }

        // What else leads to a method (the game's UnityEvent listeners). Null: nothing.
        internal Func<MethodDefinition, IEnumerable<object>> Listeners { get; set; }

        internal CodeGraphModel(IEnumerable<AssemblyDefinition> assemblies)
        {
            Assemblies.AddRange(assemblies);
            foreach (AssemblyDefinition asm in Assemblies)
            {
                foreach (TypeDefinition t in asm.MainModule.GetTypes())
                {
                    Types[t.FullName] = t;
                    foreach (MethodDefinition m in t.Methods)
                    {
                        Methods[Id(m)] = m;
                        foreach (CustomAttribute at in m.CustomAttributes)
                        {
                            if ((at.AttributeType.Name == "IteratorStateMachineAttribute" || at.AttributeType.Name == "AsyncStateMachineAttribute")
                                && at.ConstructorArguments.Count > 0 && at.ConstructorArguments[0].Value is TypeReference machine)
                            {
                                MachineOwner[machine.FullName] = Id(m);
                            }
                        }
                    }
                }
            }
            foreach (MethodDefinition m in Methods.Values)
            {
                if (!m.HasBody) continue;
                string caller = Id(m);
                // A state machine's calls count as its owner's: that is how people read a coroutine.
                if (MachineOwner.TryGetValue(m.DeclaringType.FullName, out string owner)) caller = owner;
                foreach (Instruction ins in m.Body.Instructions)
                {
                    if (!(ins.Operand is MethodReference r) || (ins.OpCode.FlowControl != FlowControl.Call && ins.OpCode.Code != Code.Newobj && ins.OpCode.Code != Code.Ldftn && ins.OpCode.Code != Code.Ldvirtftn)) continue;
                    string callee = Id(r);
                    if (!Callers.TryGetValue(callee, out List<string> list)) Callers[callee] = list = new List<string>();
                    if (!list.Contains(caller)) list.Add(caller);
                }
            }
        }

        // Type::Name(ParamType,ParamType): stable for a build, the same from Cecil and from reflection.
        internal static string Id(MethodReference m)
        {
            if (m is GenericInstanceMethod g) m = g.ElementMethod;
            string type = m.DeclaringType is GenericInstanceType gt ? gt.ElementType.FullName : m.DeclaringType.FullName;
            return type + "::" + m.Name + "(" + string.Join(",", m.Parameters.Select(p => p.ParameterType.Name).ToArray()) + ")";
        }

        internal static string Id(MethodBase m)
        {
            Type t = m.DeclaringType;
            if (t != null && t.IsGenericType && !t.IsGenericTypeDefinition) t = t.GetGenericTypeDefinition();
            string type = t == null ? "" : (t.FullName ?? t.Name).Replace('+', '/');
            MethodBase def = m is MethodInfo mi && mi.IsGenericMethod && !mi.IsGenericMethodDefinition ? mi.GetGenericMethodDefinition() : m;
            return type + "::" + def.Name + "(" + string.Join(",", def.GetParameters().Select(p => p.ParameterType.Name).ToArray()) + ")";
        }

        // ---- finding what the caller means -------------------------------------------

        // An id (Type::Name(...)), a Harmony name (Type:Name), or Type.Name.
        internal MethodDefinition FindMethod(string text)
        {
            if (string.IsNullOrEmpty(text)) throw new InvalidOperationException("Which method? An id from code.search, or Type:Method.");
            text = text.Trim();
            if (Methods.TryGetValue(text, out MethodDefinition exact)) return exact;
            int split = text.LastIndexOf("::", StringComparison.Ordinal);
            int sepLen = 2;
            if (split < 0) { split = text.LastIndexOf(':'); sepLen = 1; }
            if (split < 0) { split = text.LastIndexOf('.'); sepLen = 1; }
            if (split <= 0) throw new InvalidOperationException($"\"{text}\" is not Type:Method (code.search finds methods by name).");
            string typeName = text.Substring(0, split);
            string name = text.Substring(split + sepLen);
            int paren = name.IndexOf('(');
            if (paren >= 0) name = name.Substring(0, paren);
            TypeDefinition type = FindType(typeName);
            List<MethodDefinition> found = type.Methods.Where(m => m.Name == name).ToList();
            if (found.Count == 1) return found[0];
            if (found.Count == 0) throw new InvalidOperationException($"{type.FullName} has no method {name}.");
            throw new InvalidOperationException($"{type.FullName} has {found.Count} methods named {name}; use one of: {string.Join("; ", found.Select(Id).ToArray())}.");
        }

        internal TypeDefinition FindType(string text)
        {
            string name = (text ?? "").Trim().Replace('+', '/');
            if (Types.TryGetValue(name, out TypeDefinition t)) return t;
            List<TypeDefinition> byShort = Types.Values.Where(x => x.Name == name || x.FullName.EndsWith("." + name, StringComparison.Ordinal) || x.FullName.EndsWith("/" + name, StringComparison.Ordinal)).ToList();
            if (byShort.Count == 1) return byShort[0];
            if (byShort.Count == 0) throw new InvalidOperationException($"No type \"{text}\" in {Source} (code.search finds types by name).");
            throw new InvalidOperationException($"Several types are named {text}: {string.Join(", ", byShort.Select(x => x.FullName).ToArray())}.");
        }

        private Func<string, List<object>> PatchLookup()
        {
            Func<string, List<object>> lookup = Patches?.Invoke();
            return lookup ?? (id => new List<object>());
        }

        // ---- the graph of one method -------------------------------------------------

        private sealed class Block
        {
            public int Index;
            public int Start;
            public List<Instruction> Code = new List<Instruction>();
            public bool Dispatch;
            public int? YieldState;
        }

        internal Dictionary<string, object> Graph(string text, bool stub)
        {
            MethodDefinition focus = FindMethod(text);
            string focusId = Id(focus);
            MethodDefinition body = focus;
            string coroutine = null;
            // A coroutine or async method only builds its state machine; its body is the machine's MoveNext.
            foreach (CustomAttribute at in focus.CustomAttributes)
            {
                if ((at.AttributeType.Name == "IteratorStateMachineAttribute" || at.AttributeType.Name == "AsyncStateMachineAttribute")
                    && at.ConstructorArguments.Count > 0 && at.ConstructorArguments[0].Value is TypeReference machineRef
                    && Types.TryGetValue(machineRef.FullName, out TypeDefinition machine))
                {
                    coroutine = at.AttributeType.Name.StartsWith("Async") ? "async" : "iterator";
                    MethodDefinition moveNext = machine.Methods.FirstOrDefault(m => m.Name == "MoveNext");
                    if (moveNext != null && !stub) body = moveNext;
                }
            }
            string machineOf = MachineOwner.TryGetValue(focus.DeclaringType.FullName, out string owner) ? owner : null;

            Func<string, List<object>> patched = PatchLookup();
            var result = new Dictionary<string, object>
            {
                ["id"] = focusId,
                ["type"] = focus.DeclaringType.FullName,
                ["name"] = focus.Name,
                ["signature"] = Signature(focus),
                ["assembly"] = focus.Module.Assembly.Name.Name,
                ["static"] = focus.IsStatic,
                ["public"] = focus.IsPublic,
                ["unity_message"] = UnityMessages.Contains(focus.Name) && !focus.IsStatic,
                ["patches"] = patched(focusId),
                ["callers"] = CallersWithBases(focus),
                ["events"] = EventsOf(focus),
            };
            if (coroutine != null)
            {
                result["coroutine"] = coroutine;
                result["body"] = body == focus ? "stub" : Id(body);
            }
            if (machineOf != null) result["state_machine_of"] = machineOf;

            if (!body.HasBody)
            {
                result["blocks"] = new List<object>();
                result["edges"] = new List<object>();
                result["note"] = body.IsAbstract ? "Abstract: the body is in the types that override it." : "No body (native, extern or an interface).";
                return result;
            }

            List<Block> blocks = SplitBlocks(body);
            MarkStateMachine(body, blocks, coroutine != null && body != focus || machineOf != null);
            var byStart = blocks.ToDictionary(b => b.Start);
            var edges = new List<object>();
            var calls = new List<object>();
            var fields = new List<object>();
            var blockList = new List<object>();
            foreach (Block b in blocks)
            {
                Instruction last = b.Code[b.Code.Count - 1];
                string ends = "next";
                void Edge(int to, string kind, string label)
                {
                    if (!byStart.TryGetValue(to, out Block target)) return;
                    string k = kind;
                    if (b.Dispatch && !target.Dispatch) k = "resume";
                    edges.Add(new Dictionary<string, object> { ["from"] = b.Index, ["to"] = target.Index, ["kind"] = k, ["label"] = label, ["back"] = target.Start <= b.Start });
                }
                switch (last.OpCode.FlowControl)
                {
                    case FlowControl.Cond_Branch:
                        ends = "branch";
                        if (last.Operand is Instruction[] cases)
                        {
                            for (int i = 0; i < cases.Length; i++) Edge(cases[i].Offset, "case", "case " + i);
                            if (last.Next != null) Edge(last.Next.Offset, "default", "default");
                        }
                        else if (last.Operand is Instruction target)
                        {
                            Edge(target.Offset, "true", Condition(last.OpCode.Code));
                            if (last.Next != null) Edge(last.Next.Offset, "false", "else");
                        }
                        break;
                    case FlowControl.Branch:
                        ends = last.OpCode.Code == Code.Leave || last.OpCode.Code == Code.Leave_S ? "leave" : "jump";
                        if (last.Operand is Instruction jump) Edge(jump.Offset, ends, ends == "leave" ? "leave" : "");
                        break;
                    case FlowControl.Return:
                        ends = last.OpCode.Code == Code.Ret ? "return" : "end";
                        break;
                    case FlowControl.Throw:
                        ends = "throw";
                        break;
                    default:
                        if (last.Next != null) Edge(last.Next.Offset, "next", "");
                        break;
                }
                // Each line: its words, and for a call the index of that call in "calls".
                var lines = new List<object>();
                string previous = null;
                foreach (Instruction ins in b.Code)
                {
                    int before = calls.Count;
                    string line = Describe(ins, b.Index, calls, fields, patched);
                    if (line == null || line == previous) continue;
                    previous = line;
                    var row = new Dictionary<string, object> { ["text"] = line };
                    if (calls.Count > before) row["call"] = calls.Count - 1;
                    lines.Add(row);
                }
                // A block that only compares and branches still says what it decides.
                if (last.OpCode.FlowControl == FlowControl.Cond_Branch)
                {
                    string decides = last.OpCode.Code == Code.Switch ? $"switch ({((Instruction[])last.Operand).Length} cases)" : Condition(last.OpCode.Code) + " → IL_" + ((Instruction)last.Operand).Offset.ToString("x4");
                    if (previous != decides) lines.Add(new Dictionary<string, object> { ["text"] = decides });
                }
                // A block with nothing else to say still says where it goes.
                if (lines.Count == 0)
                {
                    string where = last.Operand is Instruction to ? "IL_" + to.Offset.ToString("x4") : null;
                    string said = ends == "leave" ? "leave the try → " + where
                        : ends == "jump" ? "go to " + where
                        : ends == "end" ? "end of finally"
                        : "(only moves values)";
                    lines.Add(new Dictionary<string, object> { ["text"] = said });
                }
                if (lines.Count > MaxLinesPerBlock)
                {
                    int more = lines.Count - MaxLinesPerBlock;
                    lines = lines.Take(MaxLinesPerBlock).ToList();
                    lines.Add(new Dictionary<string, object> { ["text"] = $"… {more} more (IL on click)" });
                }
                var block = new Dictionary<string, object>
                {
                    ["index"] = b.Index,
                    ["offset"] = b.Start,
                    ["ends"] = ends,
                    ["lines"] = lines,
                    ["il"] = b.Code.Take(MaxIlPerBlock).Select(i => (object)i.ToString()).ToList(),
                };
                if (b.Dispatch) block["dispatch"] = true;
                if (b.YieldState.HasValue) block["yield"] = b.YieldState.Value;
                blockList.Add(block);
            }
            foreach (ExceptionHandler h in body.Body.ExceptionHandlers)
            {
                if (!byStart.TryGetValue(h.TryStart.Offset, out Block from) || !byStart.TryGetValue(h.HandlerStart.Offset, out Block handler)) continue;
                string kind = h.HandlerType.ToString().ToLowerInvariant();
                edges.Add(new Dictionary<string, object>
                {
                    ["from"] = from.Index,
                    ["to"] = handler.Index,
                    ["kind"] = kind,
                    ["label"] = h.HandlerType == ExceptionHandlerType.Catch && h.CatchType != null ? "catch " + h.CatchType.Name : kind,
                    ["back"] = false,
                });
            }
            result["blocks"] = blockList;
            result["edges"] = edges;
            result["calls"] = calls;
            result["fields"] = fields;
            return result;
        }

        private static List<Block> SplitBlocks(MethodDefinition m)
        {
            var ins = m.Body.Instructions;
            var leaders = new SortedSet<int> { 0 };
            foreach (Instruction i in ins)
            {
                if (i.Operand is Instruction t) leaders.Add(t.Offset);
                if (i.Operand is Instruction[] ts) foreach (Instruction x in ts) leaders.Add(x.Offset);
                FlowControl fc = i.OpCode.FlowControl;
                if ((fc == FlowControl.Branch || fc == FlowControl.Cond_Branch || fc == FlowControl.Return || fc == FlowControl.Throw) && i.Next != null) leaders.Add(i.Next.Offset);
            }
            foreach (ExceptionHandler h in m.Body.ExceptionHandlers)
            {
                leaders.Add(h.TryStart.Offset);
                leaders.Add(h.HandlerStart.Offset);
                if (h.FilterStart != null) leaders.Add(h.FilterStart.Offset);
                if (h.TryEnd != null) leaders.Add(h.TryEnd.Offset);
                if (h.HandlerEnd != null) leaders.Add(h.HandlerEnd.Offset);
            }
            var blocks = new List<Block>();
            Block current = null;
            foreach (Instruction i in ins)
            {
                if (current == null || leaders.Contains(i.Offset))
                {
                    current = new Block { Index = blocks.Count, Start = i.Offset };
                    blocks.Add(current);
                }
                current.Code.Add(i);
            }
            return blocks;
        }

        // In a state machine's MoveNext: the blocks at the head that only read the
        // state and branch on it (the dispatch), and the blocks that store a new
        // state before returning or awaiting (a yield or an await).
        private static void MarkStateMachine(MethodDefinition body, List<Block> blocks, bool isMachine)
        {
            if (!isMachine || body.Name != "MoveNext") return;
            FieldDefinition state = body.DeclaringType.Fields.FirstOrDefault(f => f.Name.EndsWith("__state", StringComparison.Ordinal));
            if (state == null) return;
            foreach (Block b in blocks)
            {
                bool onlyDispatch = b.Code.All(i =>
                    i.OpCode.Code == Code.Ldarg_0 || i.OpCode.Code == Code.Nop || i.OpCode.Code == Code.Sub ||
                    i.OpCode.Code.ToString().StartsWith("Ldloc", StringComparison.Ordinal) || i.OpCode.Code.ToString().StartsWith("Stloc", StringComparison.Ordinal) ||
                    i.OpCode.Code.ToString().StartsWith("Ldc_I4", StringComparison.Ordinal) ||
                    (i.OpCode.Code == Code.Ldfld && i.Operand is FieldReference f && f.Name == state.Name) ||
                    i.OpCode.FlowControl == FlowControl.Cond_Branch || i.OpCode.FlowControl == FlowControl.Branch);
                if (!onlyDispatch) break;
                b.Dispatch = true;
            }
            foreach (Block b in blocks)
            {
                for (int k = 1; k < b.Code.Count; k++)
                {
                    Instruction i = b.Code[k];
                    if (i.OpCode.Code != Code.Stfld || !(i.Operand is FieldReference f) || f.Name != state.Name) continue;
                    int? value = Constant(b.Code[k - 1]);
                    if (value.HasValue && value.Value >= 0)
                    {
                        Instruction last = b.Code[b.Code.Count - 1];
                        bool suspends = last.OpCode.Code == Code.Ret || last.OpCode.Code == Code.Leave || last.OpCode.Code == Code.Leave_S
                                        || b.Code.Any(x => x.Operand is MethodReference r && r.Name.StartsWith("Await", StringComparison.Ordinal));
                        if (suspends) b.YieldState = value.Value;
                    }
                }
            }
        }

        private static int? Constant(Instruction i)
        {
            switch (i.OpCode.Code)
            {
                case Code.Ldc_I4_M1: return -1;
                case Code.Ldc_I4_0: return 0;
                case Code.Ldc_I4_1: return 1;
                case Code.Ldc_I4_2: return 2;
                case Code.Ldc_I4_3: return 3;
                case Code.Ldc_I4_4: return 4;
                case Code.Ldc_I4_5: return 5;
                case Code.Ldc_I4_6: return 6;
                case Code.Ldc_I4_7: return 7;
                case Code.Ldc_I4_8: return 8;
                case Code.Ldc_I4_S: return (sbyte)i.Operand;
                case Code.Ldc_I4: return (int)i.Operand;
            }
            return null;
        }

        private static string Condition(Code code)
        {
            switch (code)
            {
                case Code.Brtrue: case Code.Brtrue_S: return "if true / set";
                case Code.Brfalse: case Code.Brfalse_S: return "if false / null";
                case Code.Beq: case Code.Beq_S: return "if ==";
                case Code.Bne_Un: case Code.Bne_Un_S: return "if !=";
                case Code.Blt: case Code.Blt_S: case Code.Blt_Un: case Code.Blt_Un_S: return "if <";
                case Code.Ble: case Code.Ble_S: case Code.Ble_Un: case Code.Ble_Un_S: return "if <=";
                case Code.Bgt: case Code.Bgt_S: case Code.Bgt_Un: case Code.Bgt_Un_S: return "if >";
                case Code.Bge: case Code.Bge_S: case Code.Bge_Un: case Code.Bge_Un_S: return "if >=";
            }
            return "if";
        }

        // One instruction in words, when it says something a reader cares about;
        // calls and fields are also collected for the page.
        private string Describe(Instruction ins, int block, List<object> calls, List<object> fields, Func<string, List<object>> patched)
        {
            switch (ins.Operand)
            {
                case MethodReference r when ins.OpCode.FlowControl == FlowControl.Call || ins.OpCode.Code == Code.Newobj || ins.OpCode.Code == Code.Ldftn || ins.OpCode.Code == Code.Ldvirtftn:
                {
                    string id = Id(r);
                    string type = r.DeclaringType.Name;
                    int tick = type.IndexOf('`');
                    if (tick > 0) type = type.Substring(0, tick);
                    List<object> patches = patched(id);
                    calls.Add(new Dictionary<string, object>
                    {
                        ["block"] = block,
                        ["id"] = id,
                        ["name"] = type + "." + r.Name,
                        ["game"] = Methods.ContainsKey(id),
                        ["patched"] = patches.Count > 0 ? (object)patches : null,
                    });
                    if (ins.OpCode.Code == Code.Newobj) return "new " + type;
                    if (ins.OpCode.Code == Code.Ldftn || ins.OpCode.Code == Code.Ldvirtftn) return "delegate to " + type + "." + r.Name;
                    if (r.Name.StartsWith("get_", StringComparison.Ordinal)) return "reads " + type + "." + r.Name.Substring(4);
                    if (r.Name.StartsWith("set_", StringComparison.Ordinal)) return "sets " + type + "." + r.Name.Substring(4);
                    return "calls " + type + "." + r.Name + "()";
                }
                case FieldReference f:
                {
                    bool write = ins.OpCode.Code == Code.Stfld || ins.OpCode.Code == Code.Stsfld;
                    string name = f.DeclaringType.Name + "." + f.Name;
                    fields.Add(new Dictionary<string, object> { ["block"] = block, ["field"] = name, ["type"] = f.FieldType.Name, ["write"] = write });
                    return (write ? "sets " : "reads ") + name;
                }
                case string s:
                    return "text \"" + (s.Length > 40 ? s.Substring(0, 40) + "…" : s) + "\"";
            }
            switch (ins.OpCode.Code)
            {
                case Code.Ret: return "return";
                case Code.Throw: return "throw";
                case Code.Rethrow: return "rethrow";
                case Code.Switch: return "switch (" + ((Instruction[])ins.Operand).Length + " cases)";
            }
            return null;
        }

        internal List<object> CallersOf(string id)
        {
            if (!Callers.TryGetValue(id, out List<string> ids)) return new List<object>();
            return ids.Take(MaxCallers).Select(c => (object)new Dictionary<string, object>
            {
                ["id"] = c,
                ["name"] = Methods.TryGetValue(c, out MethodDefinition m) ? Short(m.DeclaringType) + "." + m.Name : c,
            }).ToList();
        }

        // Callers of the method, and of the methods it overrides: a call through
        // the base type (menu.OnEvent(e)) reaches the override, and is marked so.
        private List<object> CallersWithBases(MethodDefinition m)
        {
            List<object> list = CallersOf(Id(m));
            if (!m.IsVirtual || m.IsNewSlot) return list;
            string ps = "(" + string.Join(",", m.Parameters.Select(p => p.ParameterType.Name).ToArray()) + ")";
            for (TypeReference baseRef = m.DeclaringType.BaseType; baseRef != null; )
            {
                if (!Types.TryGetValue(baseRef.FullName, out TypeDefinition baseType)) break;
                string baseId = baseType.FullName + "::" + m.Name + ps;
                if (Methods.ContainsKey(baseId))
                {
                    foreach (Dictionary<string, object> c in CallersOf(baseId).Cast<Dictionary<string, object>>())
                    {
                        c["via"] = Short(baseType) + "." + m.Name;
                        list.Add(c);
                    }
                }
                baseRef = baseType.BaseType;
            }
            return list;
        }

        private List<object> EventsOf(MethodDefinition m)
        {
            var list = new List<object>();
            if (UnityMessages.Contains(m.Name) && !m.IsStatic) list.Add("Unity calls it (" + m.Name + ")");
            IEnumerable<object> found = Listeners?.Invoke(m);
            if (found != null) list.AddRange(found);
            return list;
        }

        private static string Signature(MethodDefinition m)
        {
            string ps = string.Join(", ", m.Parameters.Select(p => p.ParameterType.Name + " " + p.Name).ToArray());
            return (m.IsPublic ? "public " : m.IsPrivate ? "private " : "internal ") + (m.IsStatic ? "static " : "") + m.ReturnType.Name + " " + m.Name + "(" + ps + ")";
        }

        private static string Short(TypeReference t)
        {
            string name = t.Name;
            int tick = name.IndexOf('`');
            return tick > 0 ? name.Substring(0, tick) : name;
        }

        // ---- a type ------------------------------------------------------------------

        internal Dictionary<string, object> TypeGraph(string text)
        {
            TypeDefinition type = FindType(text);
            Func<string, List<object>> patched = PatchLookup();
            var groups = new Dictionary<string, List<object>> { ["Unity messages"] = new List<object>(), ["Public"] = new List<object>(), ["Private and internal"] = new List<object>(), ["Properties and events"] = new List<object>() };
            var ids = new HashSet<string>(type.Methods.Select(Id));
            foreach (MethodDefinition m in type.Methods.OrderBy(x => x.Name, StringComparer.Ordinal))
            {
                if (m.Name.StartsWith("<", StringComparison.Ordinal)) continue;   // lambdas and local functions: they show inside their method
                string id = Id(m);
                var inType = new List<object>();
                if (m.HasBody)
                {
                    IEnumerable<Instruction> code = m.Body.Instructions;
                    MethodDefinition moveNext = MachineBody(m);
                    if (moveNext != null && moveNext.HasBody) code = code.Concat(moveNext.Body.Instructions);
                    foreach (Instruction ins in code)
                    {
                        if (ins.Operand is MethodReference r && ins.OpCode.FlowControl == FlowControl.Call && ids.Contains(Id(r)) && Id(r) != id && !inType.Contains(Id(r))) inType.Add(Id(r));
                    }
                }
                string accessor = Accessor(m);
                var row = new Dictionary<string, object>
                {
                    ["id"] = id,
                    ["name"] = accessor ?? m.Name,
                    ["signature"] = Signature(m),
                    ["patched"] = patched(id).Count > 0,
                    ["coroutine"] = MachineBody(m) != null,
                    ["calls"] = inType,
                };
                string group = accessor != null ? "Properties and events" : UnityMessages.Contains(m.Name) && !m.IsStatic ? "Unity messages" : m.IsPublic ? "Public" : "Private and internal";
                groups[group].Add(row);
            }
            return new Dictionary<string, object>
            {
                ["type"] = type.FullName,
                ["assembly"] = type.Module.Assembly.Name.Name,
                ["base"] = type.BaseType?.FullName,
                ["groups"] = groups.Where(g => g.Value.Count > 0).Select(g => (object)new Dictionary<string, object> { ["name"] = g.Key, ["methods"] = g.Value }).ToList(),
            };
        }

        // get_Speed is "Speed (get)", add_OnJump "OnJump (add)": how people name a property's and an event's code.
        private static string Accessor(MethodDefinition m)
        {
            if (!m.IsSpecialName) return null;
            foreach (string prefix in new[] { "get_", "set_", "add_", "remove_" })
            {
                if (m.Name.StartsWith(prefix, StringComparison.Ordinal)) return m.Name.Substring(prefix.Length) + " (" + prefix.TrimEnd('_') + ")";
            }
            return null;
        }

        private MethodDefinition MachineBody(MethodDefinition m)
        {
            foreach (CustomAttribute at in m.CustomAttributes)
            {
                if ((at.AttributeType.Name == "IteratorStateMachineAttribute" || at.AttributeType.Name == "AsyncStateMachineAttribute")
                    && at.ConstructorArguments.Count > 0 && at.ConstructorArguments[0].Value is TypeReference r
                    && Types.TryGetValue(r.FullName, out TypeDefinition machine))
                {
                    return machine.Methods.FirstOrDefault(x => x.Name == "MoveNext");
                }
            }
            return null;
        }

        // ---- search ------------------------------------------------------------------

        internal List<object> Search(string text, int max)
        {
            if (string.IsNullOrEmpty(text)) throw new InvalidOperationException("Search for what? Part of a type or method name.");
            var found = new List<object>();
            foreach (TypeDefinition t in Types.Values.Where(t => !t.Name.Contains("<") && t.Name.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0).OrderBy(t => t.Name.Length).Take(max))
            {
                found.Add(new Dictionary<string, object> { ["kind"] = "type", ["id"] = t.FullName, ["name"] = t.FullName, ["assembly"] = t.Module.Assembly.Name.Name });
            }
            foreach (MethodDefinition m in Methods.Values.Where(m => !m.Name.Contains("<") && !m.DeclaringType.Name.Contains("<") && m.Name.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0).OrderBy(m => m.Name.Length).ThenBy(m => m.DeclaringType.Name, StringComparer.Ordinal))
            {
                if (found.Count >= max) break;
                found.Add(new Dictionary<string, object> { ["kind"] = "method", ["id"] = Id(m), ["name"] = Short(m.DeclaringType) + "." + m.Name, ["signature"] = Signature(m) });
            }
            return found;
        }
    }
}
