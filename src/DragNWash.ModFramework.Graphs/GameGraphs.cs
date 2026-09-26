using System;
using System.Collections.Generic;
using System.Linq;

namespace DragNWash.ModFramework.Graphs
{
    /// <summary>
    /// Graphs: mods with no code that say <em>when this happens, do these
    /// things</em>. A folder in BepInEx/plugins with a <c>mod.json</c> and
    /// <c>graphs/*.json</c> answers the events the libraries raise
    /// (<see cref="Operations.RegisterEvent"/>) and calls the operations they
    /// registered (<see cref="Operations"/>) — nothing else: no methods by name,
    /// no reflection, no files, no network. Every file is checked before
    /// anything runs, all the graphs together get a millisecond a frame, and a
    /// graph that fails three times in a row is switched off for the session.
    /// Experimental. See docs/GRAPHS.md.
    /// </summary>
    public static class GameGraphs
    {
        /// <summary>The library's BepInEx GUID, for <c>[BepInDependency]</c>.</summary>
        public const string Guid = "com.tomxv.dragnwash.modframework.graphs";

        /// <summary>The library's version.</summary>
        public const string Version = "1.6.0";

        /// <summary>What was read from one graph file, and how it is going.</summary>
        public sealed class GraphReport
        {
            /// <summary>GUID of the mod the graph belongs to.</summary>
            public string ModGuid { get; internal set; }
            /// <summary>That mod's name on the Mods screen.</summary>
            public string Mod { get; internal set; }
            /// <summary>The graph's name, or its file name when it gives none.</summary>
            public string Name { get; internal set; }
            /// <summary>Where it is, under the mod's folder: <c>graphs/scene-notes.json</c>.</summary>
            public string File { get; internal set; }
            /// <summary>The events it answers.</summary>
            public IReadOnlyList<string> Events { get; internal set; }
            /// <summary>The operations it calls that change nothing.</summary>
            public IReadOnlyList<string> Reads { get; internal set; }
            /// <summary>The operations it calls that change something.</summary>
            public IReadOnlyList<string> Changes { get; internal set; }
            /// <summary>Libraries it needs that are not installed.</summary>
            public IReadOnlyList<string> Needs { get; internal set; }
            /// <summary>What is wrong with the file; while there is any, it does not run.</summary>
            public IReadOnlyList<string> Problems { get; internal set; }
            /// <summary>Runs going now.</summary>
            public int Running { get; internal set; }
            /// <summary>Runs started this session.</summary>
            public int Started { get; internal set; }
            /// <summary>Failures in a row; three switch it off.</summary>
            public int Failures { get; internal set; }
            /// <summary>Whether it runs, and if not, why not.</summary>
            public string State { get; internal set; }
            /// <summary>
            /// Keys this graph answers that another mod answers as well, as
            /// <c>F6 is also Drag'n Wash Localization: [Debug] DumpDialogueKey;
            /// both answer it</c>. Nobody owns a key: this only says so.
            /// </summary>
            public IReadOnlyList<string> Shares { get; internal set; }
            /// <summary>
            /// Values this graph changed that another mod changes too, as
            /// <c>Sun [Light] intensity - also changed by graph:...</c>. The
            /// later write is the one that stands, so a person can see why an
            /// edit seems to do nothing. Empty when nothing met.
            /// </summary>
            public IReadOnlyList<string> Clashes { get; internal set; }
        }

        /// <summary>Every graph found at startup, or at the last <see cref="Reload"/>.</summary>
        public static IReadOnlyList<GraphReport> Loaded
        {
            get
            {
                List<Graph> graphs = GraphsPlugin.Instance != null ? GraphsPlugin.Instance.Graphs : new List<Graph>();
                // What the Overrides library has written, read once for the whole
                // list rather than once per graph: the registry writes every
                // result out to measure it, and the list is drawn on a screen.
                List<object> writes = AllWrites();
                return graphs.Select(g => Report(g, writes)).ToList();
            }
        }

        private static List<object> AllWrites()
        {
            if (Operations.Find("objects.writes") == null)
            {
                return null;
            }
            OperationResult result = Operations.CallNow("objects.writes", null, Guid, OperationAudience.None, false);
            return result.Ok ? result.Value as List<object> : null;
        }

        /// <summary>
        /// Stops every graph, puts back what they changed, reads the files again
        /// and checks them against the operations registered now. Nothing carries
        /// over, so a reload is the same as a fresh start. Returns a line for the
        /// log.
        /// </summary>
        public static string Reload() => GraphsPlugin.Instance != null ? GraphsPlugin.Instance.Reload() : "The Graphs library is not running.";

        /// <summary>
        /// Stops one graph for this session and puts back what it changed;
        /// <paramref name="which"/> is its file name or its name. Returns a line
        /// for the log.
        /// </summary>
        public static string Stop(string which) => GraphsPlugin.Instance != null ? GraphsPlugin.Instance.StopOne(which) : "The Graphs library is not running.";

        /// <summary>
        /// Stops one mod's graph, for a caller that knows which mod it means -
        /// the Mods screen, the editor. Two mods may have a graph of the same
        /// name, and then the name alone says nothing. Since 0.1.2.
        /// </summary>
        public static string Stop(string modGuid, string file)
        {
            if (GraphsPlugin.Instance == null) return "The Graphs library is not running.";
            Graph graph = GraphsPlugin.Instance.Graphs.FirstOrDefault(g =>
                string.Equals(g.ModGuid, modGuid, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(g.File, file, StringComparison.OrdinalIgnoreCase));
            return graph != null ? GraphsPlugin.Instance.Stop(graph) : $"No graph {file} in {modGuid}.";
        }

        internal static GraphReport Report(Graph graph) => Report(graph, AllWrites());

        private static GraphReport Report(Graph graph, List<object> writes)
        {
            return new GraphReport
            {
                ModGuid = graph.ModGuid,
                Mod = graph.ModName,
                Name = graph.Name,
                File = graph.File,
                Events = graph.Handlers.Select(h => h.Event).Distinct().ToList(),
                Reads = graph.Uses.Where(u => !u.Value).Select(u => u.Key).ToList(),
                Changes = graph.Uses.Where(u => u.Value).Select(u => u.Key).ToList(),
                Needs = graph.Needs.ToList(),
                Problems = graph.Problems.ToList(),
                Running = graph.Runs.Count(r => !r.Done),
                Started = graph.RunsStarted,
                Failures = graph.Failures,
                State = State(graph),
                Clashes = Clashes(graph, writes),
                Shares = graph.Shares.ToList(),
            };
        }

        // What this graph wrote that somebody else writes too. It is asked of
        // the library that owns the writes, through the registry and by name:
        // the Graphs library knows no operation, and when the Overrides library
        // is not installed there is simply nothing to say.
        private static IReadOnlyList<string> Clashes(Graph graph, List<object> writes)
        {
            var found = new List<string>();
            if (writes == null)
            {
                return found;
            }
            string me = $"graph:{graph.ModGuid}/{graph.File}";
            foreach (object row in writes)
            {
                if (!(row is IDictionary<string, object> write)) continue;
                var also = write.TryGetValue("also", out object a) ? a as System.Collections.IList : null;
                string by = write.TryGetValue("by", out object b) ? b as string : null;
                bool mine = by == me || (also != null && also.Cast<object>().Any(x => (x as string) == me));
                if (!mine) continue;
                var others = new List<string>();
                if (by != null && by != me) others.Add(by);
                if (also != null) others.AddRange(also.Cast<object>().Select(x => x as string).Where(x => x != null && x != me));
                if (others.Count == 0) continue;
                string target = write.TryGetValue("target", out object t) ? t as string : "something";
                found.Add($"{target} - also changed by {string.Join(", ", others.Distinct().Select(Who).ToArray())}");
            }
            return found;
        }

        // A caller's name for the Mods screen: "graph:<mod guid>/<file>" is what
        // the log and the registry use, but a player knows the mod by its name.
        private static string Who(string caller)
        {
            if (caller == null || !caller.StartsWith("graph:", System.StringComparison.Ordinal))
            {
                return caller ?? "something else";
            }
            string rest = caller.Substring("graph:".Length);
            int slash = rest.IndexOf('/');
            if (slash < 0) return rest;
            string guid = rest.Substring(0, slash), file = rest.Substring(slash + 1);
            Graph graph = (GraphsPlugin.Instance != null ? GraphsPlugin.Instance.Graphs : new List<Graph>())
                .FirstOrDefault(g => g.ModGuid == guid && g.File == file);
            return graph != null ? graph.Where : rest;
        }

        internal static string State(Graph graph)
        {
            // Files are removed, renamed and moved while the game runs - by
            // hand, or by another editor. The library keeps what it read until
            // it reads again, so a graph whose file has gone would otherwise
            // sit in the list as though it were fine and fail when opened.
            if (graph.Path != null && !System.IO.File.Exists(graph.Path)) return "its file is gone: reload to clear it";
            if (!graph.Ok) return $"{graph.Problems.Count} problem(s): it does not run";
            if (graph.Stopped) return "switched off for this session" + (graph.StoppedWhy != null ? " because " + graph.StoppedWhy : "");
            if (graph.Waiting != null) return $"waiting for {graph.Waiting}";
            return "runs";
        }
    }
}
