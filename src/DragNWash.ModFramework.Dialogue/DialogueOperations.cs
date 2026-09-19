using System;
using System.Collections.Generic;
using System.Linq;

namespace DragNWash.ModFramework.Dialogue
{
    // The Dialogue library's operations (docs/API_PLAN.md, stage 1): all
    // read. The library remembers the last lines shown this session.
    internal static class DialogueOperations
    {
        private const int Kept = 100;
        private static readonly List<Dictionary<string, object>> Recent = new List<Dictionary<string, object>>();

        internal static void Register()
        {
            string g = GameDialogue.Guid;
            GameDialogue.LineShowing += line => Remember(line, "line");
            GameDialogue.OptionShowing += line => Remember(line, "option");

            Operations.Register(g, "dialogue.current", "The conversation now: the node running, whether lines or options are showing, and the last line shown.", OperationKind.Read,
                "{ node, lines_showing, options_showing, last }", args => new Dictionary<string, object>
                {
                    ["node"] = GameDialogue.CurrentNode,
                    ["lines_showing"] = GameDialogue.LinesAvailable,
                    ["options_showing"] = GameDialogue.OptionsAvailable,
                    ["last"] = Recent.LastOrDefault(),
                });

            Operations.Register(g, "dialogue.recent", "The last lines and options shown this session, newest last, optionally only those that contain a text.", OperationKind.Read,
                "a list of { kind, line_id, node, speaker, text }", args =>
                {
                    string text = args.String("text");
                    int max = Math.Max(1, Math.Min(Kept, args.Int("max", 20)));
                    List<Dictionary<string, object>> found = Recent
                        .Where(r => text == null || ((string)r["text"] ?? "").IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0)
                        .ToList();
                    return found.Skip(Math.Max(0, found.Count - max)).Cast<object>().ToList();
                },
                Operations.Parameter("text", OperationType.String, "Only lines that contain this."),
                Operations.Parameter("max", OperationType.Number, $"At most this many (1 to {Kept}; 20 when left out)."));
        }

        private static void Remember(DialogueLine line, string kind)
        {
            if (line == null) return;
            Recent.Add(new Dictionary<string, object>
            {
                ["kind"] = kind,
                ["line_id"] = line.LineId,
                ["node"] = line.Node,
                ["speaker"] = line.Speaker,
                ["text"] = line.Text,
            });
            if (Recent.Count > Kept) Recent.RemoveAt(0);
        }
    }
}
