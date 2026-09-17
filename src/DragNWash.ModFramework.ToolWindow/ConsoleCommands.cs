using System;
using System.Collections.Generic;
using System.Text;
using BepInEx.Logging;

namespace DragNWash.ModFramework.ToolWindow
{
    /// <summary>A command a mod registered with <see cref="ToolWindow.AddCommand(string, string, string, Func{string[], string})"/>.</summary>
    public sealed class ConsoleCommand
    {
        internal ConsoleCommand(string owner, string name, string description, Func<string[], string> run, Func<string[], IEnumerable<string>> complete)
        {
            Owner = owner;
            Name = name;
            Description = description ?? "";
            Run = run;
            Complete = complete;
        }

        /// <summary>GUID of the mod that registered it.</summary>
        public string Owner { get; }

        /// <summary>The word typed to run it. Lower case, no spaces.</summary>
        public string Name { get; }

        /// <summary>One line for <c>help</c>.</summary>
        public string Description { get; }

        internal Func<string[], string> Run { get; }

        // Words that could come next, given the words typed so far (the last
        // one possibly partial); null when the command offers none.
        internal Func<string[], IEnumerable<string>> Complete { get; }

        /// <summary>True when another mod registered the same name first; then only <c>owner:name</c> runs this one.</summary>
        public bool Shadowed { get; internal set; }
    }

    /// <summary>
    /// The console's commands: what mods registered, and the built-in ones.
    /// Experimental (Tool window 1.1).
    /// </summary>
    public static class ConsoleCommands
    {
        private static readonly List<ConsoleCommand> Commands = new List<ConsoleCommand>();

        /// <summary>Every registered command, in registration order.</summary>
        public static IReadOnlyList<ConsoleCommand> All
        {
            get { lock (Commands) { return Commands.ToArray(); } }
        }

        internal static ConsoleCommand Register(string owner, string name, string description, Func<string[], string> run, Func<string[], IEnumerable<string>> complete = null)
        {
            if (string.IsNullOrEmpty(owner) || string.IsNullOrEmpty(name) || run == null)
            {
                throw new ArgumentException("A command needs an owner, a name and something to run.");
            }
            name = name.Trim().ToLowerInvariant();
            if (name.IndexOf(' ') >= 0 || name.IndexOf(':') >= 0)
            {
                throw new ArgumentException("A command name is one word without ':'.");
            }
            var command = new ConsoleCommand(owner, name, description, run, complete);
            lock (Commands)
            {
                foreach (ConsoleCommand c in Commands)
                {
                    if (c.Name == name && c.Owner != owner)
                    {
                        // The first one keeps the bare name; this one is reachable as owner:name.
                        command.Shadowed = true;
                        ToolWindowPlugin.Log.LogWarning($"Console command \"{name}\" is registered by both {c.Owner} and {owner}; type {owner}:{name} for the latter.");
                    }
                }
                Commands.Add(command);
            }
            return command;
        }

        internal static void Unregister(ConsoleCommand command)
        {
            lock (Commands)
            {
                Commands.Remove(command);
            }
        }

        internal static void UnregisterOwned(string owner)
        {
            lock (Commands)
            {
                Commands.RemoveAll(c => c.Owner == owner);
            }
        }

        /// <summary>
        /// Runs one line as typed. Output goes to <see cref="ConsoleLog"/>; a
        /// command that throws prints the error and nothing else happens.
        /// </summary>
        public static void Execute(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                return;
            }
            ConsoleLog.Print("> " + line.Trim(), LogLevel.Info);
            string[] words = Split(line);
            string name = words[0].ToLowerInvariant();
            string owner = null;
            int colon = name.IndexOf(':');
            if (colon > 0)
            {
                owner = name.Substring(0, colon);
                name = name.Substring(colon + 1);
            }
            ConsoleCommand command = Find(owner, name);
            if (command == null)
            {
                ConsoleLog.Print($"Unknown command \"{words[0]}\". Type help.", LogLevel.Warning);
                return;
            }
            var args = new string[words.Length - 1];
            Array.Copy(words, 1, args, 0, args.Length);
            try
            {
                string result = command.Run(args);
                if (!string.IsNullOrEmpty(result))
                {
                    foreach (string l in result.Replace("\r\n", "\n").Split('\n'))
                    {
                        ConsoleLog.Print(l);
                    }
                }
            }
            catch (Exception ex)
            {
                ConsoleLog.Print($"{command.Owner}: \"{command.Name}\" failed: {ex.GetType().Name}: {ex.Message}", LogLevel.Error);
                ToolWindowPlugin.Log.LogError($"Console command \"{command.Name}\" of {command.Owner} threw: {ex}");
            }
        }

        /// <summary>
        /// What could complete <paramref name="line"/>: command names while the
        /// first word is being typed, else what the command suggests for its
        /// arguments. Sorted, at most <paramref name="max"/>.
        /// </summary>
        public static List<string> Suggest(string line, int max = 8)
        {
            var result = new List<string>();
            line = line ?? "";
            string[] words = Split(line);
            bool endsWithSpace = line.Length > 0 && char.IsWhiteSpace(line[line.Length - 1]);
            if (words.Length == 1 && !endsWithSpace)
            {
                string prefix = words[0].ToLowerInvariant();
                foreach (ConsoleCommand c in All)
                {
                    string shown = c.Shadowed ? c.Owner + ":" + c.Name : c.Name;
                    if (shown.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || c.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    {
                        result.Add(shown);
                    }
                }
            }
            else
            {
                string name = words[0].ToLowerInvariant();
                string owner = null;
                int colon = name.IndexOf(':');
                if (colon > 0)
                {
                    owner = name.Substring(0, colon);
                    name = name.Substring(colon + 1);
                }
                ConsoleCommand command = Find(owner, name);
                if (command?.Complete != null)
                {
                    var args = new List<string>();
                    for (int i = 1; i < words.Length; i++)
                    {
                        args.Add(words[i]);
                    }
                    if (endsWithSpace)
                    {
                        args.Add("");
                    }
                    string partial = args.Count > 0 ? args[args.Count - 1] : "";
                    try
                    {
                        foreach (string option in command.Complete(args.ToArray()) ?? new string[0])
                        {
                            if (option.StartsWith(partial, StringComparison.OrdinalIgnoreCase))
                            {
                                result.Add(option);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        ToolWindowPlugin.Log.LogWarning($"Completion for \"{command.Name}\" of {command.Owner} threw: {ex.Message}");
                    }
                }
            }
            result.Sort(StringComparer.OrdinalIgnoreCase);
            if (result.Count > max)
            {
                result.RemoveRange(max, result.Count - max);
            }
            return result;
        }

        // Helper for completers: the options for the argument at this position.
        internal static IEnumerable<string> Options(string[] args, int position, params string[] options)
        {
            return args.Length == position + 1 ? options : new string[0];
        }

        private static readonly string[] LevelNames = { "fatal", "error", "warning", "message", "info", "debug" };

        private static ConsoleCommand Find(string owner, string name)
        {
            lock (Commands)
            {
                ConsoleCommand best = null;
                foreach (ConsoleCommand c in Commands)
                {
                    if (c.Name != name)
                    {
                        continue;
                    }
                    if (owner != null)
                    {
                        if (c.Owner.Equals(owner, StringComparison.OrdinalIgnoreCase) || c.Owner.EndsWith("." + owner, StringComparison.OrdinalIgnoreCase))
                        {
                            return c;
                        }
                        continue;
                    }
                    if (!c.Shadowed)
                    {
                        best = c;
                    }
                }
                return best;
            }
        }

        // Words separated by spaces; "quoted words" stay together.
        internal static string[] Split(string line)
        {
            var words = new List<string>();
            var sb = new StringBuilder();
            bool quoted = false;
            foreach (char c in line)
            {
                if (c == '"')
                {
                    quoted = !quoted;
                }
                else if (char.IsWhiteSpace(c) && !quoted)
                {
                    if (sb.Length > 0)
                    {
                        words.Add(sb.ToString());
                        sb.Length = 0;
                    }
                }
                else
                {
                    sb.Append(c);
                }
            }
            if (sb.Length > 0)
            {
                words.Add(sb.ToString());
            }
            return words.Count > 0 ? words.ToArray() : new[] { "" };
        }

        // The commands the Tool window itself provides.
        internal static void RegisterBuiltIns()
        {
            Register(ToolWindow.Guid, "help", "Lists commands, or describes one: help <name>", args =>
            {
                if (args.Length > 0)
                {
                    ConsoleCommand c = Find(null, args[0].ToLowerInvariant());
                    return c == null ? $"No command \"{args[0]}\"." : $"{c.Name}: {c.Description} ({c.Owner})";
                }
                var sb = new StringBuilder();
                foreach (ConsoleCommand c in All)
                {
                    sb.Append(c.Shadowed ? $"{c.Owner}:{c.Name}" : c.Name).Append(" - ").Append(c.Description).Append('\n');
                }
                return sb.ToString().TrimEnd();
            }, args =>
            {
                var names = new List<string>();
                foreach (ConsoleCommand c in All)
                {
                    names.Add(c.Name);
                }
                return Options(args, 0, names.ToArray());
            });
            Register(ToolWindow.Guid, "log", "log <n>: the last n lines | log show <level> [off] | log level <source|unity|default> <level> | log filter <text> | log clear", args =>
            {
                if (args.Length == 0 || int.TryParse(args[0], out _))
                {
                    int n = args.Length == 0 ? 20 : int.Parse(args[0]);
                    List<ConsoleEntry> all = ConsoleLog.Snapshot();
                    var sb = new StringBuilder();
                    for (int i = Math.Max(0, all.Count - n); i < all.Count; i++)
                    {
                        ConsoleEntry e = all[i];
                        if (e.Source != ConsoleLog.CommandSource)
                        {
                            sb.Append($"[{e.Level,-7}:{e.Source}] {e.Text}\n");
                        }
                    }
                    return sb.Length == 0 ? "(nothing)" : sb.ToString().TrimEnd();
                }
                switch (args[0].ToLowerInvariant())
                {
                    case "clear":
                        ConsoleLog.Clear();
                        return "Cleared.";
                    case "show":
                        if (args.Length < 2 || !ConsoleLog.TryParseLevel(args[1], out LogLevel shown))
                        {
                            return "log show <fatal|error|warning|message|info|debug> [off]";
                        }
                        bool off = args.Length > 2 && args[2].Equals("off", StringComparison.OrdinalIgnoreCase);
                        ConsoleLog.Shown = off ? ConsoleLog.Shown & ~shown : ConsoleLog.Shown | shown;
                        return $"{shown} is now {(off ? "hidden" : "shown")}.";
                    case "level":
                        if (args.Length < 3 || !ConsoleLog.TryParseLevel(args[2], out LogLevel minimum))
                        {
                            return "log level <source|unity|default> <fatal|error|warning|message|info|debug>";
                        }
                        if (args[1].Equals("unity", StringComparison.OrdinalIgnoreCase))
                        {
                            ConsoleLog.UnityMinimum = minimum;
                        }
                        else if (args[1].Equals("default", StringComparison.OrdinalIgnoreCase))
                        {
                            ConsoleLog.DefaultMinimum = minimum;
                        }
                        else
                        {
                            ConsoleLog.SetMinimum(args[1], minimum);
                        }
                        return $"{args[1]} shows {minimum} and above.";
                    case "filter":
                        ConsoleTab.SourceFilter = args.Length > 1 ? string.Join(" ", args, 1, args.Length - 1) : "";
                        return string.IsNullOrEmpty(ConsoleTab.SourceFilter) ? "Filter cleared." : $"Showing sources containing \"{ConsoleTab.SourceFilter}\".";
                    default:
                        return "log <n> | log show <level> [off] | log level <source> <level> | log filter <text> | log clear";
                }
            }, args =>
            {
                if (args.Length == 1)
                {
                    return new[] { "show", "level", "filter", "clear", "20", "50" };
                }
                string what = args[0].ToLowerInvariant();
                if (what == "show")
                {
                    return args.Length == 2 ? LevelNames : Options(args, 2, "off");
                }
                if (what == "level")
                {
                    if (args.Length == 2)
                    {
                        var sources = new List<string> { "unity", "default" };
                        foreach (ConsoleEntry e in ConsoleLog.Snapshot())
                        {
                            if (e.Source != ConsoleLog.CommandSource && !sources.Contains(e.Source))
                            {
                                sources.Add(e.Source);
                            }
                        }
                        return sources;
                    }
                    return Options(args, 2, LevelNames);
                }
                return new string[0];
            });
            Register(ToolWindow.Guid, "mods", "Loaded plugins, with features the framework found unavailable | mods reload <guid> | mods watch on|off | mods network", args =>
            {
                if (args.Length > 0 && args[0].Equals("reload", StringComparison.OrdinalIgnoreCase))
                {
                    return args.Length < 2 ? "mods reload <guid>  (a reloadable mod; see mods)" : ModReload.Reload(args[1]);
                }
                if (args.Length > 0 && args[0].Equals("network", StringComparison.OrdinalIgnoreCase))
                {
                    return NetworkReport();
                }
                if (args.Length > 0 && args[0].Equals("watch", StringComparison.OrdinalIgnoreCase))
                {
                    if (args.Length > 1 && (args[1] == "on" || args[1] == "off"))
                    {
                        ModReload.Watching = args[1] == "on";
                    }
                    return ModReload.Watching
                        ? "Reloadable mods are reloaded when their DLL changes."
                        : DeveloperTools.Enabled ? "Automatic reload is off (mods watch on to turn it on)." : "Automatic reload needs developer tools on.";
                }
                var sb = new StringBuilder();
                foreach (KeyValuePair<string, BepInEx.PluginInfo> kv in BepInEx.Bootstrap.Chainloader.PluginInfos)
                {
                    sb.Append($"{kv.Value.Metadata.Name} {kv.Value.Metadata.Version} ({kv.Key})");
                    if (ModReload.IsReloadable(kv.Key))
                    {
                        int n = ModReload.ReloadCount(kv.Key);
                        sb.Append(n > 0 ? $" - reloadable, reloaded {n} time(s)" : " - reloadable");
                    }
                    IReadOnlyList<string> missing = GameHooks.UnavailableFeatures(kv.Key);
                    if (missing.Count > 0)
                    {
                        sb.Append(" - unavailable: ").Append(string.Join(", ", missing));
                    }
                    sb.Append('\n');
                }
                return sb.ToString().TrimEnd();
            }, args =>
            {
                if (args.Length == 1) return new[] { "reload", "watch", "network" };
                if (args.Length == 2 && args[0].Equals("reload", StringComparison.OrdinalIgnoreCase)) return ModReload.ReloadableMods();
                if (args.Length == 2 && args[0].Equals("watch", StringComparison.OrdinalIgnoreCase)) return new[] { "on", "off" };
                return new string[0];
            });
            Register(ToolWindow.Guid, "scene", "The scene that is loaded", args =>
                UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);
            // The same as "log clear", under the names people type by habit.
            Register(ToolWindow.Guid, "clear", "Clears the console (also: cls, log clear)", args => { ConsoleLog.Clear(); return ""; });
            Register(ToolWindow.Guid, "cls", "Clears the console (also: clear, log clear)", args => { ConsoleLog.Clear(); return ""; });
        }

        // What each mod says it does online, and what the framework saw.
        private static string NetworkReport()
        {
            var sb = new StringBuilder();
            sb.Append(NetworkWatch.Enabled ? "Connection watching is on (it only watches; a mod can get around it)." : "Connection watching is off.").Append('\n');
            foreach (KeyValuePair<string, BepInEx.PluginInfo> kv in BepInEx.Bootstrap.Chainloader.PluginInfos)
            {
                IReadOnlyList<NetworkUse> declared = NetworkWatch.DeclaredBy(kv.Key);
                IReadOnlyList<NetworkWatch.Connection> seen = NetworkWatch.SeenBy(kv.Key);
                if (declared.Count == 0 && seen.Count == 0)
                {
                    continue;
                }
                sb.Append(kv.Value.Metadata.Name).Append(" (").Append(kv.Key).Append(")\n");
                foreach (NetworkUse u in declared)
                {
                    sb.Append("  declares ").Append(u.Host);
                    if (!string.IsNullOrEmpty(u.Purpose)) sb.Append(": ").Append(u.Purpose);
                    sb.Append('\n');
                }
                foreach (NetworkWatch.Connection c in seen)
                {
                    sb.Append("  connected to ").Append(c.Host).Append(" via ").Append(c.Via).Append(", ").Append(c.Count).Append(" time(s)");
                    sb.Append(c.Declared ? "" : "  - NOT DECLARED").Append('\n');
                }
            }
            return sb.ToString().TrimEnd();
        }
    }
}
