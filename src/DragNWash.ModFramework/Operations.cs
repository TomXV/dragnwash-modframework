using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;

namespace DragNWash.ModFramework
{
    /// <summary>Whether an operation changes anything.</summary>
    public enum OperationKind
    {
        /// <summary>Only looks: objects, values, logs, mods. Safe to offer to anyone.</summary>
        Read,
        /// <summary>Changes something in the game.</summary>
        Write,
    }

    /// <summary>The kind of value an operation's parameter takes.</summary>
    public enum OperationType
    {
        /// <summary>Text. Vectors and colours travel as text, written as the Inspector's rows show them.</summary>
        String,
        /// <summary>A number.</summary>
        Number,
        /// <summary>true or false.</summary>
        Boolean,
    }

    /// <summary>One parameter of an operation.</summary>
    public sealed class OperationParameter
    {
        /// <summary>Its name, lower case (<c>path</c>, <c>max</c>).</summary>
        public string Name { get; set; }
        /// <summary>The kind of value.</summary>
        public OperationType Type { get; set; }
        /// <summary>True when a call must give it.</summary>
        public bool Required { get; set; }
        /// <summary>One line on what it is.</summary>
        public string Description { get; set; }
        /// <summary>The values it accepts, when only some are; null for any.</summary>
        public string[] Choices { get; set; }
    }

    /// <summary>
    /// Something a library can do, by name, with plain arguments: the one thing
    /// behind the console's <c>op</c>, the MCP tools of the Bridge and the blocks
    /// of node graphs (docs/API_PLAN.md). Register with <see cref="Operations.Register"/>.
    /// </summary>
    public sealed class Operation
    {
        /// <summary><c>library.noun.verb</c>, e.g. <c>inspector.member.get</c>.</summary>
        public string Name { get; internal set; }
        /// <summary>One line on what it does, for people and AI clients.</summary>
        public string Description { get; internal set; }
        /// <summary>Read or write.</summary>
        public OperationKind Kind { get; internal set; }
        /// <summary>GUID of the mod that registered it.</summary>
        public string Owner { get; internal set; }
        /// <summary>Its parameters.</summary>
        public IReadOnlyList<OperationParameter> Parameters { get; internal set; }
        /// <summary>One line on what it returns.</summary>
        public string Returns { get; internal set; }
        /// <summary>
        /// True for an operation only the Bridge's page on this computer (and the
        /// console) may call, never an AI client over MCP: set it right after
        /// <see cref="Operations.Register"/>. For what shows the game's own code
        /// (docs/CODE_GRAPH.md). Since 1.4.0.
        /// </summary>
        public bool PageOnly { get; set; }

        internal Func<OperationArgs, object> Run;
    }

    /// <summary>The arguments of one call, read by name.</summary>
    public sealed class OperationArgs
    {
        private readonly Dictionary<string, object> _values;

        internal OperationArgs(Dictionary<string, object> values)
        {
            _values = values;
        }

        /// <summary>True when the call gave this argument.</summary>
        public bool Has(string name) => _values.ContainsKey(name);

        /// <summary>The argument as text, or <paramref name="fallback"/>.</summary>
        public string String(string name, string fallback = null)
        {
            return _values.TryGetValue(name, out object v) && v != null ? Convert.ToString(v, CultureInfo.InvariantCulture) : fallback;
        }

        /// <summary>The argument as a number, or <paramref name="fallback"/>.</summary>
        public double Number(string name, double fallback = 0)
        {
            return _values.TryGetValue(name, out object v) && v is double d ? d : fallback;
        }

        /// <summary>The argument as a whole number, or <paramref name="fallback"/>.</summary>
        public int Int(string name, int fallback = 0) => Has(name) ? (int)Math.Round(Number(name, fallback)) : fallback;

        /// <summary>The argument as true or false, or <paramref name="fallback"/>.</summary>
        public bool Bool(string name, bool fallback = false)
        {
            return _values.TryGetValue(name, out object v) && v is bool b ? b : fallback;
        }
    }

    /// <summary>What a call gave back.</summary>
    public sealed class OperationResult
    {
        /// <summary>True when the operation ran without error.</summary>
        public bool Ok { get; internal set; }
        /// <summary>
        /// What it returned: null, a string, a number, a bool, or lists
        /// (<see cref="IList"/>) and objects (<see cref="IDictionary{String, Object}"/>) of these.
        /// </summary>
        public object Value { get; internal set; }
        /// <summary>Why it failed, for people.</summary>
        public string Error { get; internal set; }

        /// <summary>The result as JSON: the value, or <c>{"error": "..."}</c>.</summary>
        public string ToJson(bool indented = false) => Ok ? Operations.ToJson(Value, indented) : Operations.ToJson(new Dictionary<string, object> { ["error"] = Error }, indented);
    }

    /// <summary>
    /// The registry of operations: each library registers what it can do under a
    /// name, with a description and plain parameters, and anyone can call it by
    /// name (the console's <c>op</c>, the Bridge's MCP tools, node graphs).
    /// Calls run on the main thread; every call is logged with who made it.
    /// Experimental. Since 1.4.0.
    /// </summary>
    public static class Operations
    {
        private static readonly Dictionary<string, Operation> Registered = new Dictionary<string, Operation>(StringComparer.Ordinal);
        private static readonly Queue<Action> MainThreadQueue = new Queue<Action>();
        private static Thread _mainThread;

        /// <summary>Largest JSON a call returns, in characters; longer results are an error asking for a narrower call.</summary>
        public const int MaxResultChars = 200000;

        internal static void Install()
        {
            _mainThread = Thread.CurrentThread;
            ModReload.Unloading += (guid, assembly) => RemoveOwner(guid);
        }

        /// <summary>
        /// Registers an operation. The name is <c>library.noun.verb</c>, lower
        /// case; a second registration of a name is refused and logged. The
        /// operation goes when its owner is reloaded or unloaded.
        /// </summary>
        /// <param name="ownerGuid">BepInEx GUID of the mod registering it.</param>
        /// <param name="name">e.g. <c>saves.flags.list</c>.</param>
        /// <param name="description">One line on what it does.</param>
        /// <param name="kind">Read (changes nothing) or Write.</param>
        /// <param name="returns">One line on what it returns.</param>
        /// <param name="run">Runs on the main thread; returns null, strings, numbers, bools, lists and string-keyed dictionaries of these. Throw to fail with a message.</param>
        /// <param name="parameters">Its parameters.</param>
        /// <returns>The operation, or null when the name is taken.</returns>
        public static Operation Register(string ownerGuid, string name, string description, OperationKind kind, string returns, Func<OperationArgs, object> run, params OperationParameter[] parameters)
        {
            if (string.IsNullOrEmpty(ownerGuid) || string.IsNullOrEmpty(name) || run == null)
            {
                throw new ArgumentException("An operation needs its owner's GUID, a name and a function.");
            }
            if (name.Any(c => char.IsWhiteSpace(c) || char.IsUpper(c)))
            {
                throw new ArgumentException($"Operation names are lower case with no spaces: \"{name}\".", nameof(name));
            }
            var op = new Operation
            {
                Name = name,
                Description = description ?? "",
                Kind = kind,
                Owner = ownerGuid,
                Returns = returns ?? "",
                Parameters = (parameters ?? new OperationParameter[0]).Where(p => p != null).ToList(),
                Run = run,
            };
            lock (Registered)
            {
                if (Registered.TryGetValue(name, out Operation existing))
                {
                    ModFramework.Log.LogWarning($"[op] {ownerGuid} tried to register {name}, already registered by {existing.Owner}.");
                    return null;
                }
                Registered[name] = op;
            }
            return op;
        }

        /// <summary>A parameter, for <see cref="Register"/>.</summary>
        public static OperationParameter Parameter(string name, OperationType type, string description, bool required = false, params string[] choices)
        {
            return new OperationParameter { Name = name, Type = type, Description = description, Required = required, Choices = choices != null && choices.Length > 0 ? choices : null };
        }

        /// <summary>Every operation, by name.</summary>
        public static IReadOnlyList<Operation> All
        {
            get
            {
                lock (Registered)
                {
                    return Registered.Values.OrderBy(o => o.Name, StringComparer.Ordinal).ToList();
                }
            }
        }

        /// <summary>The operation of that name, or null.</summary>
        public static Operation Find(string name)
        {
            lock (Registered)
            {
                return name != null && Registered.TryGetValue(name, out Operation op) ? op : null;
            }
        }

        internal static void RemoveOwner(string guid)
        {
            lock (Registered)
            {
                foreach (string name in Registered.Values.Where(o => o.Owner == guid).Select(o => o.Name).ToList())
                {
                    Registered.Remove(name);
                }
            }
        }

        /// <summary>
        /// Calls an operation now. Main thread only (use <see cref="Call"/>
        /// from anywhere else). <paramref name="caller"/> names who asked
        /// ("console", "mcp", a graph) in the log. Arguments are checked
        /// against the parameters: text is turned into numbers and true/false
        /// where the parameter says so.
        /// </summary>
        public static OperationResult CallNow(string name, IDictionary<string, object> args, string caller)
        {
            if (_mainThread != null && Thread.CurrentThread != _mainThread)
            {
                return Fail("Operations run on the main thread; use Operations.Call from other threads.");
            }
            Operation op = Find(name);
            if (op == null)
            {
                return Fail($"No operation named \"{name}\". \"op\" in the console lists them.");
            }
            var values = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (OperationParameter p in op.Parameters)
            {
                if (args == null || !args.TryGetValue(p.Name, out object raw) || raw == null)
                {
                    if (p.Required) return Fail($"{name} needs {p.Name} ({p.Description}).");
                    continue;
                }
                if (!Convert(raw, p, out object value, out string error)) return Fail($"{name}: {p.Name} {error}.");
                values[p.Name] = value;
            }
            if (args != null)
            {
                foreach (string key in args.Keys)
                {
                    if (op.Parameters.All(p => p.Name != key)) return Fail($"{name} has no parameter {key}. It takes: {string.Join(", ", op.Parameters.Select(p => p.Name).ToArray())}.");
                }
            }
            OperationResult result;
            try
            {
                object value = op.Run(new OperationArgs(values));
                string json = ToJson(value);
                result = json.Length > MaxResultChars
                    ? Fail($"The result is {json.Length} characters, more than {MaxResultChars}; ask for less (a narrower path, a smaller max).")
                    : new OperationResult { Ok = true, Value = value };
            }
            catch (Exception ex)
            {
                result = Fail((ex is System.Reflection.TargetInvocationException && ex.InnerException != null ? ex.InnerException : ex).Message);
            }
            string argText = string.Join(" ", values.Select(kv => kv.Key + "=" + kv.Value).ToArray());
            string line = $"[op] {caller ?? "?"}: {name}{(argText.Length > 0 ? " " + argText : "")} -> {(result.Ok ? "ok" : result.Error)}";
            if (op.Kind == OperationKind.Write) ModFramework.Log.LogInfo(line);
            else ModFramework.Log.LogDebug(line);
            return result;
        }

        /// <summary>
        /// Calls an operation from any thread: it runs on the main thread at the
        /// next frame, and <paramref name="done"/> gets the result there.
        /// </summary>
        public static void Call(string name, IDictionary<string, object> args, string caller, Action<OperationResult> done)
        {
            lock (MainThreadQueue)
            {
                MainThreadQueue.Enqueue(() =>
                {
                    OperationResult r = CallNow(name, args, caller);
                    try { done?.Invoke(r); }
                    catch (Exception ex) { ModFramework.Log.LogWarning($"[op] The callback for {name} threw: {ex.Message}"); }
                });
            }
        }

        // Main thread, every frame (the core's Update).
        internal static void Tick()
        {
            for (int i = 0; i < 32; i++)
            {
                Action next;
                lock (MainThreadQueue)
                {
                    if (MainThreadQueue.Count == 0) return;
                    next = MainThreadQueue.Dequeue();
                }
                next();
            }
        }

        private static OperationResult Fail(string error) => new OperationResult { Ok = false, Error = error };

        private static bool Convert(object raw, OperationParameter p, out object value, out string error)
        {
            error = null;
            value = null;
            string text = raw as string;
            switch (p.Type)
            {
                case OperationType.Number:
                    if (raw is double || raw is float || raw is int || raw is long) { value = System.Convert.ToDouble(raw, CultureInfo.InvariantCulture); return true; }
                    if (text != null && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double d)) { value = d; return true; }
                    error = "is a number";
                    return false;
                case OperationType.Boolean:
                    if (raw is bool b) { value = b; return true; }
                    switch (text?.Trim().ToLowerInvariant())
                    {
                        case "true": case "1": case "yes": case "on": value = true; return true;
                        case "false": case "0": case "no": case "off": value = false; return true;
                    }
                    error = "is true or false";
                    return false;
                default:
                    value = text ?? System.Convert.ToString(raw, CultureInfo.InvariantCulture);
                    if (p.Choices != null && !p.Choices.Contains((string)value, StringComparer.OrdinalIgnoreCase))
                    {
                        error = "is one of " + string.Join(", ", p.Choices);
                        return false;
                    }
                    return true;
            }
        }

        // ---- JSON ------------------------------------------------------------------

        /// <summary>
        /// A value as JSON: null, strings, numbers, bools, lists and string-keyed
        /// dictionaries; anything else is written as its text.
        /// </summary>
        public static string ToJson(object value, bool indented = false)
        {
            var sb = new StringBuilder();
            Write(sb, value, indented, 0);
            return sb.ToString();
        }

        private static void Write(StringBuilder sb, object v, bool indented, int depth)
        {
            switch (v)
            {
                case null: sb.Append("null"); return;
                case string s: Quote(sb, s); return;
                case bool b: sb.Append(b ? "true" : "false"); return;
                case double d: sb.Append(double.IsNaN(d) || double.IsInfinity(d) ? "null" : d.ToString("R", CultureInfo.InvariantCulture)); return;
                case float f: sb.Append(float.IsNaN(f) || float.IsInfinity(f) ? "null" : f.ToString("R", CultureInfo.InvariantCulture)); return;
                case int _: case long _: case short _: case byte _: case uint _: case ulong _: sb.Append(System.Convert.ToString(v, CultureInfo.InvariantCulture)); return;
                case IDictionary<string, object> o:
                {
                    sb.Append('{');
                    bool first = true;
                    foreach (KeyValuePair<string, object> kv in o)
                    {
                        if (!first) sb.Append(',');
                        first = false;
                        NewLine(sb, indented, depth + 1);
                        Quote(sb, kv.Key);
                        sb.Append(indented ? ": " : ":");
                        Write(sb, kv.Value, indented, depth + 1);
                    }
                    if (!first) NewLine(sb, indented, depth);
                    sb.Append('}');
                    return;
                }
                case IEnumerable list:
                {
                    sb.Append('[');
                    bool first = true;
                    foreach (object item in list)
                    {
                        if (!first) sb.Append(',');
                        first = false;
                        NewLine(sb, indented, depth + 1);
                        Write(sb, item, indented, depth + 1);
                    }
                    if (!first) NewLine(sb, indented, depth);
                    sb.Append(']');
                    return;
                }
            }
            Quote(sb, v is IFormattable fm ? fm.ToString(null, CultureInfo.InvariantCulture) : v.ToString());
        }

        private static void NewLine(StringBuilder sb, bool indented, int depth)
        {
            if (!indented) return;
            sb.Append('\n').Append(' ', depth * 2);
        }

        private static void Quote(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
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
            sb.Append('"');
        }
    }
}
