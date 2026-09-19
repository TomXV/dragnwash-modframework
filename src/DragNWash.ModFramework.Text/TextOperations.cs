using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;

namespace DragNWash.ModFramework.Text
{
    // The Text library's operations (docs/API_PLAN.md, stage 1): all read.
    internal static class TextOperations
    {
        internal static void Register()
        {
            string g = GameText.Guid;
            Operations.Register(g, "text.rewriters", "The mods that rewrite the game's text (translations and the like), in the order they run.", OperationKind.Read,
                "a list of { mod, order }", args =>
                    GameText.RewriterOwners().Select(r => (object)new Dictionary<string, object> { ["mod"] = r.Key, ["order"] = r.Value }).ToList());

            Operations.Register(g, "text.shown", "Text on screen whose English (as the game set it) or shown text contains the given text: what the game set and what is shown after the rewriters.", OperationKind.Read,
                "a list of { path, source, shown }", args =>
                {
                    string text = args.String("text");
                    int max = Math.Max(1, Math.Min(200, args.Int("max", 20)));
                    var found = new List<object>();
                    foreach (TMP_Text t in UnityEngine.Object.FindObjectsByType<TMP_Text>(FindObjectsSortMode.None))
                    {
                        if (t == null || !t.isActiveAndEnabled) continue;
                        string shown = t.text ?? "";
                        GameText.TryGetSource(t, out string source);
                        if (shown.IndexOf(text, StringComparison.OrdinalIgnoreCase) < 0 && (source ?? "").IndexOf(text, StringComparison.OrdinalIgnoreCase) < 0) continue;
                        found.Add(new Dictionary<string, object> { ["path"] = PathOf(t.transform), ["source"] = source, ["shown"] = shown });
                        if (found.Count >= max) break;
                    }
                    return found;
                },
                Operations.Parameter("text", OperationType.String, "Part of the English or of the shown text.", true),
                Operations.Parameter("max", OperationType.Number, "At most this many (1 to 200; 20 when left out)."));
        }

        private static string PathOf(Transform t)
        {
            var parts = new List<string>();
            for (; t != null; t = t.parent) parts.Add(t.name);
            parts.Reverse();
            return string.Join("/", parts.ToArray());
        }
    }
}
