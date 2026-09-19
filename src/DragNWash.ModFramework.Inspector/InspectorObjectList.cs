using System;
using System.Collections.Generic;

namespace DragNWash.ModFramework.Inspector
{
    // The object explorer's list, as plain data: one entry per loaded object
    // (its instance ID, name, type and folder), sorted once, and the rows the
    // left pane draws for a search and the folders open. No Unity types here:
    // the list holds no reference to an object, so it never keeps an asset
    // alive (InspectorObjects looks one up by its ID when it is needed).
    internal enum ObjectKind
    {
        Textures, Sprites, Materials, Shaders, Meshes, Audio, Animation, Fonts, Data, OutsideScenes, Other,
    }

    // A type as the list sorts and filters by it: shared by every object of that type.
    internal sealed class ObjectType
    {
        public string Name;          // short name, as rows show it
        public string[] Chain;       // its name and its base types' names, for t:
        public ObjectKind Kind;
        public bool Game;            // one of the game's own types (Data lists them first)

        private string _groupKey;

        // Its sub-folder's key in Data and Other, made once.
        internal string GroupKey => _groupKey ?? (_groupKey = ObjectList.KeyOf(Kind, Name));
    }

    internal sealed class ObjectEntry
    {
        public int Id;
        public string Name;          // "" when the object has none; a path for GameObjects outside the scenes
        public ObjectType Type;
        public bool Hidden;          // HideFlags keep it out of Unity's own windows
        public string Fact;          // one short fact, filled when the row is first drawn
        public bool Gone;            // Unity destroyed it since the list was made
    }

    internal sealed class ObjectRow
    {
        public ObjectEntry Entry;    // null for a folder heading
        public ObjectKind Kind;
        public string Group;         // a sub-folder's type, or null
        public string Key;           // the folder's key, for opening and closing it
        public int Shown, Total;     // a heading's counts: matching the search, and all
        public int Depth;
    }

    internal sealed class ObjectList
    {
        internal static readonly string[] FolderNames =
        {
            "Textures", "Sprites", "Materials", "Shaders", "Meshes", "Audio", "Animation", "Fonts", "Data", "Objects outside the scenes", "Other",
        };

        // Words the console and the operations accept for a kind, besides its folder name.
        private static readonly string[][] Aliases =
        {
            new[] { "texture" }, new[] { "sprite" }, new[] { "material" }, new[] { "shader" }, new[] { "mesh" },
            new[] { "audioclip", "sound", "sounds" }, new[] { "animations", "clip", "clips", "controller" }, new[] { "font" },
            new[] { "scriptableobject", "scriptableobjects" }, new[] { "outside", "prefab", "prefabs", "gameobject", "gameobjects" }, new string[0],
        };

        // Data and Other open into a sub-folder per type.
        internal static bool HasGroups(ObjectKind kind) => kind == ObjectKind.Data || kind == ObjectKind.Other;

        internal readonly List<ObjectEntry> All;
        internal readonly long BuildMs;
        internal readonly DateTime Made = DateTime.Now;

        internal ObjectList(List<ObjectEntry> entries, long buildMs)
        {
            All = entries;
            BuildMs = buildMs;
            All.Sort(Compare);
        }

        internal static string KeyOf(ObjectKind kind, string group) => group == null ? FolderNames[(int)kind] : FolderNames[(int)kind] + "/" + group;

        internal static string FolderName(ObjectKind kind) => FolderNames[(int)kind];

        internal static bool TryParseKind(string text, out ObjectKind kind)
        {
            kind = ObjectKind.Other;
            if (string.IsNullOrEmpty(text)) return false;
            string t = text.Replace(" ", "").ToLowerInvariant();
            for (int i = 0; i < FolderNames.Length; i++)
            {
                string folder = FolderNames[i].Replace(" ", "").ToLowerInvariant();
                if (folder == t || Array.IndexOf(Aliases[i], t) >= 0)
                {
                    kind = (ObjectKind)i;
                    return true;
                }
            }
            return false;
        }

        // By folder; in Data, the game's own types first; then by type, name and ID.
        private static int Compare(ObjectEntry a, ObjectEntry b)
        {
            int c = a.Type.Kind.CompareTo(b.Type.Kind);
            if (c != 0) return c;
            if (HasGroups(a.Type.Kind))
            {
                if (a.Type.Game != b.Type.Game) return a.Type.Game ? -1 : 1;
                c = string.Compare(a.Type.Name, b.Type.Name, StringComparison.OrdinalIgnoreCase);
                if (c != 0) return c;
            }
            c = string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            return c != 0 ? c : a.Id.CompareTo(b.Id);
        }

        // ---- search ----------------------------------------------------------------

        // "counter t:Material": a name part and a type (the type's name or a base type's, or a folder).
        internal sealed class Query
        {
            public string Text = "";
            public string Type;
            public bool Active => Text.Length > 0 || Type != null;

            internal static Query Parse(string search)
            {
                var q = new Query();
                var words = new List<string>();
                foreach (string word in (search ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (word.StartsWith("t:", StringComparison.OrdinalIgnoreCase) && word.Length > 2) q.Type = word.Substring(2);
                    else words.Add(word);
                }
                q.Text = string.Join(" ", words.ToArray());
                return q;
            }

            internal bool Matches(ObjectEntry e)
            {
                if (Type != null && !TypeMatches(e.Type)) return false;
                return Text.Length == 0 || (e.Name ?? "").IndexOf(Text, StringComparison.OrdinalIgnoreCase) >= 0;
            }

            private readonly Dictionary<ObjectType, bool> _typeCache = new Dictionary<ObjectType, bool>();

            private bool TypeMatches(ObjectType type)
            {
                if (_typeCache.TryGetValue(type, out bool yes)) return yes;
                yes = TryParseKind(Type, out ObjectKind kind) && kind == type.Kind;
                foreach (string name in type.Chain)
                {
                    if (yes) break;
                    yes = name.Equals(Type, StringComparison.OrdinalIgnoreCase);
                }
                _typeCache[type] = yes;
                return yes;
            }
        }

        // The objects of one kind (and a name part), for the console and the operations.
        internal List<ObjectEntry> Find(ObjectKind? kind, string search, bool hidden, int max)
        {
            Query q = Query.Parse(search);
            var found = new List<ObjectEntry>();
            foreach (ObjectEntry e in All)
            {
                if ((kind == null || e.Type.Kind == kind) && (hidden || !e.Hidden) && q.Matches(e))
                {
                    found.Add(e);
                    if (found.Count >= max) break;
                }
            }
            return found;
        }

        // Every folder's count, hidden objects left out unless asked for.
        internal int[] Counts(bool hidden)
        {
            var counts = new int[FolderNames.Length];
            foreach (ObjectEntry e in All)
            {
                if (hidden || !e.Hidden) counts[(int)e.Type.Kind]++;
            }
            return counts;
        }

        // The rows of the left pane: a heading per folder (and per type in Data
        // and Other), and the objects of the open ones. With a search, a folder
        // shows how many match, only folders with a match are listed, and they
        // are open unless closed while searching.
        internal List<ObjectRow> Rows(Query q, bool hidden, ICollection<string> open, ICollection<string> closedWhileSearching)
        {
            // One pass: what matches, in list order (already by folder and type),
            // and the counts. Entries of a type sit together, so a sub-folder's
            // count is kept in a local until the type changes.
            var matching = new List<ObjectEntry>(q.Active ? 256 : All.Count);
            var folderTotal = new int[FolderNames.Length];
            var folderShown = new int[FolderNames.Length];
            var totals = new Dictionary<string, int>();
            var shown = new Dictionary<string, int>();
            ObjectType current = null;
            int typeTotal = 0, typeShown = 0;
            void Flush()
            {
                if (current == null || !HasGroups(current.Kind)) return;
                totals.TryGetValue(current.GroupKey, out int t);
                totals[current.GroupKey] = t + typeTotal;
                shown.TryGetValue(current.GroupKey, out int s);
                shown[current.GroupKey] = s + typeShown;
            }
            foreach (ObjectEntry e in All)
            {
                if (!hidden && e.Hidden) continue;
                if (!ReferenceEquals(e.Type, current))
                {
                    Flush();
                    current = e.Type;
                    typeTotal = typeShown = 0;
                }
                int k = (int)e.Type.Kind;
                folderTotal[k]++;
                typeTotal++;
                if (!q.Matches(e)) continue;
                matching.Add(e);
                folderShown[k]++;
                typeShown++;
            }
            Flush();
            bool IsOpen(string key) => q.Active ? !closedWhileSearching.Contains(key) : open.Contains(key);

            var rows = new List<ObjectRow>();
            int m = 0;
            for (int k = 0; k < FolderNames.Length; k++)
            {
                var kind = (ObjectKind)k;
                string folder = FolderNames[k];
                if (q.Active && folderShown[k] == 0) continue;
                rows.Add(new ObjectRow { Kind = kind, Key = folder, Shown = folderShown[k], Total = folderTotal[k] });
                bool folderOpen = IsOpen(folder);
                string lastGroup = null;
                bool groupOpen = false;
                while (m < matching.Count && matching[m].Type.Kind == kind)
                {
                    ObjectEntry e = matching[m++];
                    if (!folderOpen) continue;
                    if (HasGroups(kind))
                    {
                        if (e.Type.Name != lastGroup)
                        {
                            lastGroup = e.Type.Name;
                            string key = e.Type.GroupKey;
                            shown.TryGetValue(key, out int gs);
                            totals.TryGetValue(key, out int gt);
                            rows.Add(new ObjectRow { Kind = kind, Group = lastGroup, Key = key, Shown = gs, Total = gt, Depth = 1 });
                            groupOpen = IsOpen(key);
                        }
                        if (!groupOpen) continue;
                        rows.Add(new ObjectRow { Entry = e, Kind = kind, Depth = 2 });
                    }
                    else
                    {
                        rows.Add(new ObjectRow { Entry = e, Kind = kind, Depth = 1 });
                    }
                }
            }
            return rows;
        }

        internal ObjectEntry ById(int id)
        {
            foreach (ObjectEntry e in All)
            {
                if (e.Id == id) return e;
            }
            return null;
        }
    }
}
