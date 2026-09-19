using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using BepInEx.Logging;

namespace DragNWash.ModFramework.ToolWindow
{
    // The console's side of the operations registry (docs/API_PLAN.md,
    // stage 1): "op" lists, describes and runs any operation, and the Tool
    // window registers log.read.
    internal static class ConsoleOperations
    {
        internal static void Register()
        {
            ConsoleCommands.Register(ToolWindow.Guid, "op",
                "op | op <name> key=value ... | op help <name>  (the operations mods registered; read ones change nothing)",
                Run, Complete);

            Operations.Register(ToolWindow.Guid, "log.read", "The last lines of the console log, newest last, optionally only from one source or at a level and above.",
                OperationKind.Read, "a list of { time, level, source, text }", args =>
                {
                    int max = Math.Max(1, Math.Min(500, args.Int("max", 50)));
                    string source = args.String("source");
                    LogLevel minimum = LogLevel.All;
                    if (args.Has("level") && ConsoleLog.TryParseLevel(args.String("level"), out LogLevel parsed)) minimum = parsed;
                    var rows = new List<object>();
                    foreach (ConsoleEntry e in ConsoleLog.Snapshot())
                    {
                        if (source != null && e.Source.IndexOf(source, StringComparison.OrdinalIgnoreCase) < 0) continue;
                        if (minimum != LogLevel.All && (int)e.Level > (int)minimum) continue;
                        rows.Add(new Dictionary<string, object>
                        {
                            ["time"] = e.Time.ToString("HH:mm:ss"),
                            ["level"] = e.Level.ToString(),
                            ["source"] = e.Source,
                            ["text"] = e.Text,
                        });
                    }
                    return rows.Skip(Math.Max(0, rows.Count - max)).ToList();
                },
                Operations.Parameter("max", OperationType.Number, "How many lines, newest last (1 to 500; 50 when left out)."),
                Operations.Parameter("source", OperationType.String, "Only lines whose source contains this (a mod's name or GUID, unity)."),
                Operations.Parameter("level", OperationType.String, "Only this level and more severe.", false, "Fatal", "Error", "Warning", "Message", "Info", "Debug"));
        }

        private static string Run(string[] args)
        {
            if (args.Length == 0) return List();
            if (args[0].Equals("help", StringComparison.OrdinalIgnoreCase))
            {
                return args.Length < 2 ? "op help <name>" : Describe(args[1]);
            }
            var values = new Dictionary<string, object>(StringComparer.Ordinal);
            for (int i = 1; i < args.Length; i++)
            {
                int eq = args[i].IndexOf('=');
                if (eq <= 0) return $"\"{args[i]}\" is not key=value. {Describe(args[0])}";
                values[args[i].Substring(0, eq)] = args[i].Substring(eq + 1);
            }
            OperationResult result = Operations.CallNow(args[0], values, "console");
            return result.Ok ? result.ToJson(indented: true) : result.Error;
        }

        private static string List()
        {
            IReadOnlyList<Operation> all = Operations.All;
            if (all.Count == 0) return "No operations are registered.";
            var sb = new StringBuilder($"{all.Count} operation(s); op help <name> describes one.\n");
            foreach (Operation op in all)
            {
                sb.Append(op.Kind == OperationKind.Write ? "  [write] " : "  ").Append(op.Name).Append("  ").Append(op.Description).Append('\n');
            }
            return sb.ToString().TrimEnd('\n');
        }

        private static string Describe(string name)
        {
            Operation op = Operations.Find(name);
            if (op == null) return $"No operation named \"{name}\".";
            var sb = new StringBuilder();
            sb.Append(op.Name).Append(op.Kind == OperationKind.Write ? " (write)" : " (read)").Append(", from ").Append(op.Owner).Append('\n');
            sb.Append("  ").Append(op.Description).Append('\n');
            foreach (OperationParameter p in op.Parameters)
            {
                sb.Append("  ").Append(p.Name).Append('=').Append(p.Type.ToString().ToLowerInvariant()).Append(p.Required ? " (required)" : "")
                  .Append("  ").Append(p.Description);
                if (p.Choices != null) sb.Append(" One of: ").Append(string.Join(", ", p.Choices));
                sb.Append('\n');
            }
            sb.Append("  returns ").Append(op.Returns);
            return sb.ToString();
        }

        private static IEnumerable<string> Complete(string[] args)
        {
            if (args.Length <= 1)
            {
                return new[] { "help" }.Concat(Operations.All.Select(o => o.Name));
            }
            if (args[0].Equals("help", StringComparison.OrdinalIgnoreCase))
            {
                return Operations.All.Select(o => o.Name);
            }
            Operation op = Operations.Find(args[0]);
            return op == null ? new string[0] : op.Parameters.Select(p => p.Name + "=");
        }
    }
}
