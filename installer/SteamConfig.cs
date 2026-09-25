using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace DragNWash.Installer
{
    // The game's launch options in Steam, as text. The launcher goes in front of
    // %command%, so whatever the player had stays and still reaches the game:
    //
    //   (empty)                     ->  "<launcher>" %command%
    //   -force-d3d11                ->  "<launcher>" %command% -force-d3d11
    //   <before> %command% <after>  ->  <before> "<launcher>" %command% <after>
    //
    // Taking it out undoes that: the launcher goes, and a %command% that is then the
    // first word goes too when it was the only one, so "-force-d3d11" comes back as
    // "-force-d3d11" (Steam puts options without %command% after the game, so the two
    // start the game the same way). The same rules as install-steamdeck.sh, with the
    // launcher in place of ./run_bepinex.sh.
    //
    // No file access here: the text in, the text out. installer/tests runs these.
    internal static class LaunchOption
    {
        internal const string Command = "%command%";

        // The launcher, quoted or not, in any game folder: after the game folder has
        // moved, the old path is still recognised as ours and replaced.
        private static readonly Regex Ours = new Regex(
            "\"[^\"]*[\\\\/]DragNWash\\.Installer[\\\\/]Launcher\\.exe\"|(?<!\\S)[^\\s\"]*[\\\\/]DragNWash\\.Installer[\\\\/]Launcher\\.exe(?!\\S)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        internal static bool Has(string options)
        {
            return options != null && Ours.IsMatch(options);
        }

        // The launcher at exactly this path, in front of %command%, nothing to change.
        internal static bool IsSet(string options, string launcher)
        {
            return Has(options) && Add(options, launcher) == options;
        }

        internal static string Add(string options, string launcher)
        {
            string ours = "\"" + launcher + "\"";
            string current = Remove(options);
            if (current.Length == 0)
            {
                return ours + " " + Command;
            }
            int at = current.IndexOf(Command, StringComparison.OrdinalIgnoreCase);
            return at >= 0
                ? current.Substring(0, at) + ours + " " + current.Substring(at)
                : ours + " " + Command + " " + current;
        }

        internal static string Remove(string options)
        {
            string text = (options ?? "").Trim();
            bool removed = false;
            for (Match m = Ours.Match(text); m.Success; m = Ours.Match(text))
            {
                removed = true;
                string before = text.Substring(0, m.Index).TrimEnd();
                string after = text.Substring(m.Index + m.Length).TrimStart();
                text = before.Length == 0 ? after : after.Length == 0 ? before : before + " " + after;
            }
            if (!removed)
            {
                return text;
            }
            // "%command% -force-d3d11" is what taking the launcher out of "-force-d3d11" leaves.
            if (text.StartsWith(Command, StringComparison.OrdinalIgnoreCase))
            {
                string rest = text.Substring(Command.Length);
                if ((rest.Length == 0 || char.IsWhiteSpace(rest[0])) && rest.IndexOf(Command, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    text = rest.Trim();
                }
            }
            return text;
        }
    }

    // One Steam account's localconfig.vdf, as far as the game's launch options go:
    // UserLocalConfigStore > Software > Valve > Steam > apps > "<app id>" > LaunchOptions.
    internal static class LocalConfig
    {
        internal static string[] AppPath(string appId) => new[] { "UserLocalConfigStore", "Software", "Valve", "Steam", "apps", appId };

        internal static bool HasApp(VdfFile vdf, string appId) => vdf.HasBlock(AppPath(appId));

        // The launch options now; "" when there are none.
        internal static string Options(VdfFile vdf, string appId)
        {
            return vdf.Get(AppPath(appId).Concat(new[] { "LaunchOptions" }).ToArray()) ?? "";
        }

        // The file's text with the launcher put in (on) or taken out; null when there
        // is nothing to change. Missing blocks are made; nothing else in the file changes.
        internal static string Change(string text, string appId, string launcher, bool on)
        {
            var vdf = new VdfFile(text);
            string current = Options(vdf, appId);
            string wanted = on ? LaunchOption.Add(current, launcher) : LaunchOption.Remove(current);
            if (on ? LaunchOption.IsSet(current, launcher) : !LaunchOption.Has(current))
            {
                return null;
            }
            vdf.Set(wanted, AppPath(appId).Concat(new[] { "LaunchOptions" }).ToArray());
            return vdf.Text;
        }

        // Which accounts get the launcher: every one that has played the game (its
        // localconfig.vdf has the game's block) and the one that signed in last. When
        // neither can be told, every account. Taking it out: every account that has it.
        internal static List<string> Accounts(IList<(string Id, VdfFile Vdf)> accounts, string appId, string lastSignedIn, bool on)
        {
            if (!on)
            {
                return accounts.Where(a => LaunchOption.Has(Options(a.Vdf, appId))).Select(a => a.Id).ToList();
            }
            List<string> chosen = accounts.Where(a => HasApp(a.Vdf, appId) || a.Id == lastSignedIn).Select(a => a.Id).ToList();
            return chosen.Count > 0 ? chosen : accounts.Select(a => a.Id).ToList();
        }

        // config/loginusers.vdf: the account (as its userdata folder is named) marked
        // MostRecent; null when none is.
        internal static string LastSignedIn(VdfFile loginUsers)
        {
            foreach (string id in loginUsers.Keys("users"))
            {
                if (loginUsers.Get("users", id, "MostRecent") == "1" && ulong.TryParse(id, out ulong id64))
                {
                    // A SteamID64's low 32 bits are the account id, the userdata folder's name.
                    return (id64 & 0xFFFFFFFF).ToString(System.Globalization.CultureInfo.InvariantCulture);
                }
            }
            return null;
        }
    }

    // Steam's KeyValues text files: "key" "value" pairs and "key" { ... } blocks, with
    // \" and \\ inside quotes, // comments and [$CONDITION] tags. Keys are matched
    // without regard to case, as Steam does. Set rewrites only the value it changes or
    // inserts the lines it adds, in the file's own indentation and line ends; every
    // other byte stays as it was.
    internal sealed class VdfFile
    {
        private sealed class Node
        {
            internal string Key;
            internal bool Block;

            // A value: where its text starts and ends (inside the quotes when quoted).
            // A block: where its { and } are.
            internal int Start;
            internal int End;
            internal bool Quoted;
            internal int KeyStart;
            internal readonly List<Node> Children = new List<Node>();
        }

        private Node _root;

        internal string Text { get; private set; }

        internal VdfFile(string text)
        {
            Text = text ?? "";
            Parse();
        }

        internal string Get(params string[] path)
        {
            Node node = Find(path);
            return node != null && !node.Block ? Unescape(Text.Substring(node.Start, node.End - node.Start)) : null;
        }

        internal bool HasBlock(params string[] path)
        {
            Node node = Find(path);
            return node != null && node.Block;
        }

        // The keys inside a block, in the file's order.
        internal List<string> Keys(params string[] path)
        {
            Node node = Find(path);
            return node != null && node.Block ? node.Children.Select(c => c.Key).ToList() : new List<string>();
        }

        // Sets the value at path (the last key), making the blocks on the way that are
        // missing. The first key must be the file's own top block.
        internal void Set(string value, params string[] path)
        {
            if (path.Length < 2)
            {
                throw new ArgumentException("a value needs a block and a key");
            }
            Node parent = Child(_root, path[0]);
            if (parent == null || !parent.Block)
            {
                throw new FormatException($"the file has no \"{path[0]}\" block");
            }
            int i = 1;
            for (; i < path.Length - 1; i++)
            {
                Node next = Child(parent, path[i]);
                if (next == null)
                {
                    break;
                }
                if (!next.Block)
                {
                    throw new FormatException($"\"{path[i]}\" is a value, not a block");
                }
                parent = next;
            }
            if (i == path.Length - 1)
            {
                Node existing = Child(parent, path[i]);
                if (existing != null)
                {
                    if (existing.Block)
                    {
                        throw new FormatException($"\"{path[i]}\" is a block, not a value");
                    }
                    string escaped = Escape(value);
                    Text = existing.Quoted
                        ? Text.Substring(0, existing.Start) + escaped + Text.Substring(existing.End)
                        : Text.Substring(0, existing.Start) + "\"" + escaped + "\"" + Text.Substring(existing.End);
                    Parse();
                    return;
                }
            }
            Insert(parent, path.Skip(i).ToArray(), value);
            Parse();
        }

        // The lines for keys[0] { keys[1] { ... "last" "value" } }, before parent's }.
        private void Insert(Node parent, string[] keys, string value)
        {
            string nl = Text.Contains("\r\n") ? "\r\n" : "\n";
            int close = parent.End;
            int lineStart = close == 0 ? 0 : Text.LastIndexOf('\n', close - 1) + 1;
            string beforeBrace = Text.Substring(lineStart, close - lineStart);
            bool ownLine = beforeBrace.Trim().Length == 0;
            string outer = ownLine ? beforeBrace : IndentOf(parent.KeyStart);
            var lines = new StringBuilder();
            string ind = outer + "\t";
            for (int k = 0; k < keys.Length - 1; k++)
            {
                lines.Append(ind).Append('"').Append(Escape(keys[k])).Append('"').Append(nl);
                lines.Append(ind).Append('{').Append(nl);
                ind += "\t";
            }
            lines.Append(ind).Append('"').Append(Escape(keys[keys.Length - 1])).Append("\"\t\t\"").Append(Escape(value)).Append('"').Append(nl);
            for (int k = keys.Length - 2; k >= 0; k--)
            {
                ind = ind.Substring(0, ind.Length - 1);
                lines.Append(ind).Append('}').Append(nl);
            }
            Text = ownLine
                ? Text.Substring(0, lineStart) + lines + Text.Substring(lineStart)
                : Text.Substring(0, close) + nl + lines + outer + Text.Substring(close);
        }

        private string IndentOf(int at)
        {
            int lineStart = at == 0 ? 0 : Text.LastIndexOf('\n', at - 1) + 1;
            int end = lineStart;
            while (end < Text.Length && (Text[end] == ' ' || Text[end] == '\t'))
            {
                end++;
            }
            return Text.Substring(lineStart, end - lineStart);
        }

        private Node Find(string[] path)
        {
            Node node = _root;
            foreach (string key in path)
            {
                node = node.Block ? Child(node, key) : null;
                if (node == null)
                {
                    return null;
                }
            }
            return node;
        }

        private static Node Child(Node parent, string key)
        {
            return parent.Children.FirstOrDefault(c => string.Equals(c.Key, key, StringComparison.OrdinalIgnoreCase));
        }

        // ---- reading ----

        private void Parse()
        {
            _root = new Node { Block = true, Start = -1, End = Text.Length };
            int i = 0;
            ReadBlock(ref i, _root, true);
        }

        private void ReadBlock(ref int i, Node block, bool top)
        {
            while (true)
            {
                SkipSpace(ref i);
                if (i >= Text.Length)
                {
                    if (!top)
                    {
                        throw new FormatException("a block is not closed");
                    }
                    return;
                }
                if (Text[i] == '}')
                {
                    if (top)
                    {
                        throw new FormatException($"a }} with no block at {i}");
                    }
                    return;
                }
                var node = new Node { KeyStart = i };
                node.Key = Unescape(ReadToken(ref i, out _, out _, out _));
                SkipSpace(ref i);
                SkipCondition(ref i);
                if (i < Text.Length && Text[i] == '{')
                {
                    node.Block = true;
                    node.Start = i;
                    i++;
                    ReadBlock(ref i, node, false);
                    node.End = i; // the }
                    i++;
                }
                else
                {
                    ReadToken(ref i, out node.Start, out node.End, out node.Quoted);
                    SkipSpace(ref i);
                    SkipCondition(ref i);
                }
                block.Children.Add(node);
            }
        }

        // Returns the token's raw text; start and end bound it (inside the quotes).
        private string ReadToken(ref int i, out int start, out int end, out bool quoted)
        {
            if (i >= Text.Length)
            {
                throw new FormatException("the file ends where a key or value should be");
            }
            char c = Text[i];
            if (c == '{' || c == '}')
            {
                throw new FormatException($"a {c} where a key or value should be, at {i}");
            }
            if (c == '"')
            {
                quoted = true;
                start = ++i;
                while (i < Text.Length && Text[i] != '"')
                {
                    i += Text[i] == '\\' ? 2 : 1;
                }
                if (i >= Text.Length)
                {
                    throw new FormatException("a quote is not closed");
                }
                end = i++;
                return Text.Substring(start, end - start);
            }
            quoted = false;
            start = i;
            while (i < Text.Length && !char.IsWhiteSpace(Text[i]) && Text[i] != '{' && Text[i] != '}' && Text[i] != '"')
            {
                i++;
            }
            end = i;
            return Text.Substring(start, end - start);
        }

        private void SkipSpace(ref int i)
        {
            while (i < Text.Length)
            {
                if (char.IsWhiteSpace(Text[i]) || Text[i] == '\uFEFF')
                {
                    i++;
                }
                else if (Text[i] == '/' && i + 1 < Text.Length && Text[i + 1] == '/')
                {
                    while (i < Text.Length && Text[i] != '\n')
                    {
                        i++;
                    }
                }
                else
                {
                    return;
                }
            }
        }

        // [$WIN32] and the like after a key or value: kept in the file, not used.
        private void SkipCondition(ref int i)
        {
            if (i < Text.Length && Text[i] == '[')
            {
                int close = Text.IndexOf(']', i);
                if (close < 0)
                {
                    throw new FormatException("a [ is not closed");
                }
                i = close + 1;
                SkipSpace(ref i);
            }
        }

        internal static string Unescape(string raw)
        {
            if (raw.IndexOf('\\') < 0)
            {
                return raw;
            }
            var s = new StringBuilder(raw.Length);
            for (int i = 0; i < raw.Length; i++)
            {
                if (raw[i] == '\\' && i + 1 < raw.Length)
                {
                    char next = raw[++i];
                    switch (next)
                    {
                        case '\\':
                        case '"':
                            s.Append(next);
                            break;
                        case 'n':
                            s.Append('\n');
                            break;
                        case 't':
                            s.Append('\t');
                            break;
                        default:
                            s.Append('\\').Append(next);
                            break;
                    }
                }
                else
                {
                    s.Append(raw[i]);
                }
            }
            return s.ToString();
        }

        internal static string Escape(string value)
        {
            return (value ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\t", "\\t");
        }
    }
}
