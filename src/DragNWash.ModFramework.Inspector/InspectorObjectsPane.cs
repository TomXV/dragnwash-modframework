using DragNWash.ModFramework.ToolWindow;
using TW = global::DragNWash.ModFramework.ToolWindow.ToolWindow;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

namespace DragNWash.ModFramework.Inspector
{
    // The Inspector tab's Objects view: the object explorer's list in the left
    // pane, and over the members pane a header per kind (with a preview of a
    // texture or sprite) and Used by. The members pane itself is the tab's own,
    // so rows, editing, Reset, History and Code work here as in Scene.
    // Experimental; docs/OBJECT_EXPLORER.md.
    internal static partial class InspectorTab
    {
        private static bool _objectsMode;
        private static bool _showObjectList = true;
        private static string _objectsSearch = "";
        // What each view had selected, for coming back to it.
        private static object _sceneTarget, _objectsTarget;
        // A GameObject outside the scenes, while it or one of its components is selected.
        private static GameObject _assetObject;
        private static int _selectedEntryId;
        private static int _revealEntryId;
        private static bool _showHidden;
        private static Vector2 _scrollObjects;

        // The rows drawn, rebuilt only when the search, a folder, Show hidden or the list changes.
        private static List<ObjectRow> _objectRows;
        private static string _objectRowsKey;
        private static int _openVersion;
        private static string _lastObjectsSearch = "";
        private static readonly HashSet<string> OpenFolders = new HashSet<string>();
        private static readonly HashSet<string> ClosedWhileSearching = new HashSet<string>();

        // The header over the members, computed once per selection.
        private static List<string> _header;
        private static UnityEngine.Object _headerFor;

        // Used by: the last list, for which object, and how it went.
        private static bool _showUsedBy;
        private static List<InspectorObjects.Hit> _usedBy;
        private static int _usedByFor;
        private static string _usedBySummary = "";
        private static Vector2 _scrollUsedBy;

        private static bool _sharedNoteShown;

        /// <summary>True while the Objects view is open.</summary>
        internal static bool InObjects => _objectsMode;

        private static void SetMode(bool objects)
        {
            if (_objectsMode == objects)
            {
                return;
            }
            if (_objectsMode) _objectsTarget = _target; else _sceneTarget = _target;
            _objectsMode = objects;
            _toolMenu = null;
            _menuRow = null;
            object back = objects ? _objectsTarget : _sceneTarget;
            if (back is UnityEngine.Object uo && !uo) back = null;
            if (!objects && back == null) back = SelectedObject;
            if (objects && back == null) _assetObject = null;
            SetTarget(back);
            if (objects && (_objectRows == null || InspectorObjects.Current == null))
            {
                _status = "Listing the loaded objects...";
            }
        }

        private static void RefreshObjects()
        {
            ObjectList list = InspectorObjects.Build();
            _status = ListedNote(list);
        }

        private static string ListedNote(ObjectList list)
        {
            int shown = 0;
            foreach (int n in list.Counts(false)) shown += n;
            return $"{shown.ToString("N0", CultureInfo.InvariantCulture)} objects listed in {list.BuildMs} ms at {list.Made:HH:mm:ss}{(list.All.Count > shown ? $" (and {list.All.Count - shown} hidden)" : "")}. Experimental. Refresh lists them again.";
        }

        // Selects any object in Objects: opens its folder and scrolls to it. A
        // component outside the scenes is shown under its GameObject.
        internal static void SelectInObjects(UnityEngine.Object target)
        {
            if (target == null)
            {
                return;
            }
            SetMode(true);
            GameObject owner = target is Component c ? c.gameObject : target as GameObject;
            UnityEngine.Object listed = owner != null ? owner : target;
            int id = listed.GetInstanceID();
            ObjectList list = InspectorObjects.List();
            ObjectEntry e = list.ById(id);
            if (e == null)
            {
                // Made after the list was: list again, once.
                list = InspectorObjects.Build();
                e = list.ById(id);
            }
            _assetObject = owner;
            SetTarget(target);
            _selectedEntryId = id;
            _page = 1;
            if (e == null)
            {
                _status = $"{InspectorObjects.Describe(listed)} is not among the objects listed.";
                return;
            }
            if (e.Hidden && !_showHidden)
            {
                _showHidden = true;
            }
            string folder = ObjectList.KeyOf(e.Type.Kind, null);
            OpenFolders.Add(folder);
            ClosedWhileSearching.Remove(folder);
            if (ObjectList.HasGroups(e.Type.Kind))
            {
                string group = ObjectList.KeyOf(e.Type.Kind, e.Type.Name);
                OpenFolders.Add(group);
                ClosedWhileSearching.Remove(group);
            }
            _openVersion++;
            _revealEntryId = id;
        }

        private static void SelectEntry(ObjectEntry e)
        {
            UnityEngine.Object o = InspectorObjects.Find(e);
            if (o == null)
            {
                _status = $"{(string.IsNullOrEmpty(e.Name) ? "(no name)" : e.Name)} was destroyed since the list was made; Refresh lists the objects again.";
                return;
            }
            _assetObject = o as GameObject;
            SetTarget(o);
            _selectedEntryId = e.Id;
            _page = 1;
        }

        private static void ToggleFolder(string key, bool searching)
        {
            if (searching)
            {
                if (!ClosedWhileSearching.Remove(key)) ClosedWhileSearching.Add(key);
            }
            else if (!OpenFolders.Remove(key))
            {
                OpenFolders.Add(key);
            }
            _openVersion++;
        }

        // ---- the left pane: folders and objects ------------------------------------------

        private static void DrawObjectList(Rect pane, ToolWindowStyles s, float row)
        {
            TW.Fill(pane, TW.InsetColor);
            if (InspectorObjects.Current == null || InspectorObjects.Stale)
            {
                RefreshObjects();
            }
            ObjectList list = InspectorObjects.Current;
            float x = pane.x + 4, y = pane.y + 2, w = pane.width - 8;
            float bx = x;
            if (FlowButton(ref bx, ref y, x, w, ButtonWidth(s, "Show hidden"), "Show hidden", _showHidden, s, row))
            {
                _showHidden = !_showHidden;
            }
            if (FlowButton(ref bx, ref y, x, w, ButtonWidth(s, "Close all"), "Close all", false, s, row))
            {
                OpenFolders.Clear();
                ClosedWhileSearching.Clear();
                foreach (string folder in ObjectList.FolderNames) ClosedWhileSearching.Add(folder);
                _openVersion++;
            }
            y += row + 4;

            string search = _objectsSearch ?? "";
            if (search != _lastObjectsSearch)
            {
                // A new search opens every folder with a match again.
                _lastObjectsSearch = search;
                ClosedWhileSearching.Clear();
                _scrollObjects = Vector2.zero;
            }
            ObjectList.Query query = ObjectList.Query.Parse(search);
            string key = search + "|" + _showHidden + "|" + _openVersion + "|" + InspectorObjects.Version;
            if (_objectRows == null || key != _objectRowsKey)
            {
                _objectRowsKey = key;
                _objectRows = list.Rows(query, _showHidden, OpenFolders, ClosedWhileSearching);
            }
            List<ObjectRow> rows = _objectRows;

            var view = new Rect(pane.x, y, pane.width, pane.yMax - y);
            float inner = view.width - 20;
            if (_revealEntryId != 0)
            {
                int index = rows.FindIndex(r => r.Entry != null && r.Entry.Id == _revealEntryId);
                if (index >= 0)
                {
                    _scrollObjects.y = Mathf.Clamp(index * row - view.height / 3f, 0, Mathf.Max(0, rows.Count * row - view.height));
                }
                _revealEntryId = 0;
            }
            if (rows.Count == 0)
            {
                GUI.Label(new Rect(x, y, w, row), query.Active ? "Nothing matches." : "Nothing listed.", _mutedCell);
                return;
            }
            TW.ApplyScroll(view, ref _scrollObjects);
            _scrollObjects = GUI.BeginScrollView(view, _scrollObjects, new Rect(0, 0, inner, Mathf.Max(view.height, rows.Count * row)), false, false);
            // Only the rows in view are drawn: tens of thousands scroll like a few.
            int first = Mathf.Clamp((int)(_scrollObjects.y / row), 0, rows.Count);
            int last = Mathf.Min(rows.Count, first + (int)(view.height / row) + 2);
            for (int i = first; i < last; i++)
            {
                ObjectRow r = rows[i];
                float ry = i * row;
                float indent = 4 + r.Depth * 14;
                if (r.Entry == null)
                {
                    bool open = query.Active ? !ClosedWhileSearching.Contains(r.Key) : OpenFolders.Contains(r.Key);
                    string name = r.Group ?? ObjectList.FolderName(r.Kind);
                    string count = query.Active ? $"{r.Shown.ToString("N0", CultureInfo.InvariantCulture)} of {r.Total.ToString("N0", CultureInfo.InvariantCulture)}" : r.Total.ToString("N0", CultureInfo.InvariantCulture);
                    GUI.Label(new Rect(indent, ry, 18, row), open ? "-" : "+", _toggleStyle ?? (_toggleStyle = new GUIStyle(s.MutedLabel) { alignment = TextAnchor.MiddleCenter }));
                    if (GUI.Button(new Rect(indent, ry, inner - indent, row), "", _mutedCell))
                    {
                        ToggleFolder(r.Key, query.Active);
                    }
                    GUI.Label(new Rect(indent + 20, ry, inner - indent - 20, row), Drawable((r.Group == null ? name.ToUpperInvariant() : name) + "   " + count), r.Group == null ? _cell : _mutedCell);
                    continue;
                }
                ObjectEntry e = r.Entry;
                bool selected = e.Id == _selectedEntryId;
                if (selected)
                {
                    TW.Fill(new Rect(0, ry, inner, row), TW.PanelColor);
                }
                string fact = InspectorObjects.Fact(e);
                float factWidth = string.IsNullOrEmpty(fact) ? 0 : Mathf.Min(inner * 0.4f, _mutedCell.CalcSize(new GUIContent(Drawable(fact))).x + 8);
                var label = new Rect(indent, ry, inner - indent - factWidth, row);
                GUIStyle style = selected ? _accentCell : e.Gone || e.Hidden ? _mutedCell : _cell;
                string text = string.IsNullOrEmpty(e.Name) ? "(no name) " + e.Type.Name
                    : e.Type.Kind == ObjectKind.OutsideScenes ? FitPath(e.Name, label.width, style) : e.Name;
                if (GUI.Button(label, Drawable(text), style))
                {
                    SelectEntry(e);
                }
                if (factWidth > 0)
                {
                    GUI.Label(new Rect(inner - factWidth, ry, factWidth, row), Drawable(fact), _mutedCell);
                }
            }
            GUI.EndScrollView();
        }

        // ---- the header over the members ----------------------------------------------------

        // What the selected object is: its folder and name, a few lines per
        // kind, a preview of a texture or a sprite, Used by, and for a
        // GameObject outside the scenes its components. Returns the height used.
        private static float DrawObjectHeader(Rect pane, ToolWindowStyles s, float row)
        {
            float x = pane.x + 4, y = pane.y + 4, w = pane.width - 8;
            UnityEngine.Object described = _assetObject != null && _assetObject ? _assetObject : _target as UnityEngine.Object;
            if (described == null || !described)
            {
                y = WrappedLine("Select an object in the list. Every loaded object is there by kind; the search takes a name, and t:Material (or any type) for one kind.", x, y, w, _mutedCell, row);
                return y - pane.y + 4;
            }
            if (_header == null || _headerFor != described)
            {
                _header = InspectorObjects.Header(described);
                _headerFor = described;
            }
            y = WrappedLine(InspectorObjects.Describe(described), x, y, w, _accentCell, row);
            foreach (string line in _header)
            {
                y = WrappedLine(line, x, y, w, _mutedCell, row);
            }
            if (described is Texture tex && tex.dimension == TextureDimension.Tex2D && tex.width > 0 && tex.height > 0)
            {
                y = DrawPreviewBox(tex, new Rect(0, 0, 1, 1), tex.width, tex.height, x, y, w);
            }
            else if (described is Sprite sprite && sprite.texture != null)
            {
                Rect r;
                try { r = sprite.textureRect; }
                catch (Exception) { r = sprite.rect; }
                Texture2D t = sprite.texture;
                var coords = new Rect(r.x / t.width, r.y / t.height, r.width / t.width, r.height / t.height);
                y = DrawPreviewBox(t, coords, r.width, r.height, x, y, w);
            }

            float bx = x;
            int id = described.GetInstanceID();
            bool haveUsedBy = _usedBy != null && _usedByFor == id;
            string usedLabel = haveUsedBy ? $"Used by ({_usedBy.Count}{(_usedBy.Count >= InspectorObjects.HitCap ? "+" : "")})" : "Used by";
            if (FlowButton(ref bx, ref y, x, w, ButtonWidth(s, usedLabel), usedLabel, false, s, row))
            {
                RunUsedBy(described);
            }
            if ((described is Texture2D || described is Sprite) && AssetsTabAvailable
                && FlowButton(ref bx, ref y, x, w, ButtonWidth(s, "Assets tab"), "Assets tab", false, s, row))
            {
                ShowInAssetsTab(described is Sprite sp && sp.texture != null ? sp.texture : described);
            }
            y += row + 4;
            if (haveUsedBy && _usedBy.Count > 1 && !(described is GameObject))
            {
                y = WrappedLine($"Shared: used in {_usedBy.Count}{(_usedBy.Count >= InspectorObjects.HitCap ? "+" : "")} places, and a change shows in all of them.", x, y, w, _warningCell, row);
            }
            if (_assetObject != null && _assetObject)
            {
                y += DrawComponentStrip(new Rect(pane.x, y - 4, pane.width, pane.yMax - y), _assetObject, s, row) - 4;
            }
            return y - pane.y;
        }

        // An image the game has loaded, drawn as it is: nothing is created,
        // read back or uploaded.
        private static float DrawPreviewBox(Texture texture, Rect coords, float width, float height, float x, float y, float w)
        {
            float boxHeight = Mathf.Min(160f, w * 0.5f);
            var box = new Rect(x, y, w, boxHeight);
            if (Event.current.type == EventType.Repaint)
            {
                // A dark and a light square behind it, so a transparent or dark image still reads.
                TW.Fill(box, new Color(0.18f, 0.2f, 0.24f));
                TW.Fill(new Rect(box.x, box.y, box.width / 2, box.height / 2), new Color(0.26f, 0.28f, 0.32f));
                TW.Fill(new Rect(box.x + box.width / 2, box.y + box.height / 2, box.width / 2, box.height / 2), new Color(0.26f, 0.28f, 0.32f));
                float scale = Mathf.Min(box.width / width, box.height / height);
                float dw = width * scale, dh = height * scale;
                var fit = new Rect(box.x + (box.width - dw) / 2, box.y + (box.height - dh) / 2, dw, dh);
                GUI.DrawTextureWithTexCoords(fit, texture, coords, true);
            }
            return y + boxHeight + 4;
        }

        // ---- Used by ------------------------------------------------------------------------------

        private static void RunUsedBy(UnityEngine.Object o)
        {
            _usedBy = InspectorObjects.UsedBy(o, InspectorObjects.HitCap, out _usedBySummary);
            _usedByFor = o.GetInstanceID();
            _showUsedBy = true;
            _showHistory = false;
            _showBodies = false;
            _showScenes = false;
            _scrollUsedBy = Vector2.zero;
            _page = 1;
        }

        // Where the selected object is used, in place of the members. A row's Go
        // selects the place: a scene's component in Scene, an asset in Objects.
        private static void DrawUsedBy(Rect pane, ToolWindowStyles s, float row)
        {
            TW.Fill(pane, TW.InsetColor);
            float x = pane.x + 4, y = pane.y + 2, w = pane.width - 8;
            UnityEngine.Object o = _assetObject != null && _assetObject ? _assetObject : _target as UnityEngine.Object;
            if (o == null || !o || _usedBy == null || _usedByFor != o.GetInstanceID())
            {
                _showUsedBy = false;
                return;
            }
            y = WrappedLine($"Used by: where {InspectorObjects.Describe(o)} is used. {_usedBySummary}", x, y, w, _mutedCell, row);
            float bx = x;
            if (FlowButton(ref bx, ref y, x, w, ButtonWidth(s, "Look again"), "Look again", false, s, row))
            {
                RunUsedBy(o);
            }
            if (FlowButton(ref bx, ref y, x, w, ButtonWidth(s, "< Members"), "< Members", false, s, row))
            {
                _showUsedBy = false;
            }
            y += row + 4;
            if (_usedBy.Count == 0)
            {
                y = WrappedLine("Nothing loaded holds it where Used by looks: Unity's renderers, mesh filters, sprites, audio sources and animators, materials, and the fields of scripts and ScriptableObjects. It may be used from code only, or not at the moment.", x, y, w, _mutedCell, row);
                return;
            }
            var view = new Rect(x, y, w, pane.yMax - y - 2);
            float inner = view.width - 20;
            float lineH = row * 2;
            TW.ApplyScroll(view, ref _scrollUsedBy);
            _scrollUsedBy = GUI.BeginScrollView(view, _scrollUsedBy, new Rect(0, 0, inner, Mathf.Max(view.height, _usedBy.Count * lineH)), false, false);
            int first = Mathf.Clamp((int)(_scrollUsedBy.y / lineH), 0, _usedBy.Count);
            int last = Mathf.Min(_usedBy.Count, first + (int)(view.height / lineH) + 2);
            for (int i = first; i < last; i++)
            {
                InspectorObjects.Hit hit = _usedBy[i];
                float ry = i * lineH;
                GUI.Label(new Rect(0, ry, inner - 50, row), Drawable(FitPath(hit.Where, inner - 50, hit.InScene ? _cell : _mutedCell)), hit.InScene ? _cell : _mutedCell);
                GUI.Label(new Rect(12, ry + row, inner - 62, row), Drawable(hit.Member), _mutedCell);
                if (GUI.Button(new Rect(inner - 46, ry + 2, 44, row - 4), "Go", s.Button))
                {
                    UnityEngine.Object holder = InspectorObjects.Find(hit.Id);
                    if (holder == null) _status = $"{hit.Where} was destroyed since Used by looked.";
                    else Select(holder);
                }
            }
            GUI.EndScrollView();
        }

        // The first edit of a shared object in a session says so, once.
        private static void NoteSharedEdit()
        {
            if (_sharedNoteShown || !_objectsMode || !(_target is UnityEngine.Object o) || !InspectorObjects.IsListed(o))
            {
                return;
            }
            _sharedNoteShown = true;
            _status = "A shared object: the change shows everywhere it is used (Used by lists where), until the game reloads it or quits. Nothing is saved.";
        }

        // ---- the Assets tab, when the Assets library is loaded ------------------------------------

        private static System.Reflection.MethodInfo _showInAssets;
        private static bool _assetsLooked;

        private static bool AssetsTabAvailable
        {
            get
            {
                if (!_assetsLooked)
                {
                    _assetsLooked = true;
                    // Reached by reflection, so the Inspector does not depend on it.
                    Type catalog = Type.GetType("DragNWash.ModFramework.Assets.AssetCatalog, DragNWash.ModFramework.Assets", false);
                    _showInAssets = catalog?.GetMethod("ShowInToolWindow", new[] { typeof(string) });
                }
                return _showInAssets != null;
            }
        }

        private static void ShowInAssetsTab(UnityEngine.Object texture)
        {
            if (AssetsTabAvailable)
            {
                _showInAssets.Invoke(null, new object[] { texture.name });
            }
            else
            {
                _status = $"Texture {texture.name}: the Assets library is not loaded.";
            }
        }

        // ---- console --------------------------------------------------------------------------

        private static string ObjectsCommand(string[] args)
        {
            if (args.Length == 0)
            {
                return InspectorObjects.KindsText();
            }
            if (args[0].Equals("usedby", StringComparison.OrdinalIgnoreCase))
            {
                if (args.Length < 3) return "objects usedby <kind> <name>";
                if (!ObjectList.TryParseKind(args[1], out ObjectKind kind)) return UnknownKind(args[1]);
                ObjectEntry e = InspectorObjects.FindEntry(kind, string.Join(" ", args, 2, args.Length - 2), out string problem);
                if (e == null) return problem;
                UnityEngine.Object o = InspectorObjects.Find(e);
                if (o == null) return $"{e.Name} was destroyed.";
                List<InspectorObjects.Hit> hits = InspectorObjects.UsedBy(o, InspectorObjects.HitCap, out string summary);
                var sb = new StringBuilder();
                sb.Append(InspectorObjects.Describe(o)).Append(": ").Append(summary).Append('\n');
                for (int i = 0; i < hits.Count && i < 200; i++)
                {
                    sb.Append("  ").Append(hits[i].Where).Append("  ").Append(hits[i].Member).Append('\n');
                }
                if (hits.Count > 200) sb.Append($"  ... {hits.Count - 200} more (the Inspector's Used by lists them all)");
                return sb.ToString().TrimEnd();
            }
            if (!ObjectList.TryParseKind(args[0], out ObjectKind k)) return UnknownKind(args[0]);
            string filter = args.Length > 1 ? string.Join(" ", args, 1, args.Length - 1) : "";
            List<ObjectEntry> found = InspectorObjects.List().Find(k, filter, false, 201);
            var lines = new StringBuilder();
            for (int i = 0; i < found.Count && i < 200; i++)
            {
                ObjectEntry e = found[i];
                string fact = InspectorObjects.Fact(e);
                lines.Append(string.IsNullOrEmpty(e.Name) ? "(no name)" : e.Name).Append("  (").Append(e.Type.Name).Append(')');
                if (!string.IsNullOrEmpty(fact)) lines.Append("  ").Append(fact);
                lines.Append('\n');
            }
            if (found.Count == 0) return $"No {ObjectList.FolderName(k)} object{(filter.Length > 0 ? $" matches \"{filter}\"" : "")}.";
            if (found.Count > 200) lines.Append("... more; narrow it with a filter.");
            return lines.ToString().TrimEnd();
        }

        // inspect object <kind> <name>
        private static string InspectObjectCommand(string[] args)
        {
            if (args.Length < 3) return "inspect object <kind> <name>";
            if (!ObjectList.TryParseKind(args[1], out ObjectKind kind)) return UnknownKind(args[1]);
            ObjectEntry e = InspectorObjects.FindEntry(kind, string.Join(" ", args, 2, args.Length - 2), out string problem);
            if (e == null) return problem;
            UnityEngine.Object o = InspectorObjects.Find(e);
            if (o == null) return $"{e.Name} was destroyed.";
            SelectInObjects(o);
            TW.Open(Title);
            var sb = new StringBuilder(InspectorObjects.Describe(o));
            foreach (string line in InspectorObjects.Header(o)) sb.Append('\n').Append(line);
            return sb.ToString();
        }

        private static string UnknownKind(string text)
        {
            return $"No kind \"{text}\": one of {string.Join(", ", ObjectList.FolderNames)} (textures, sprites, materials, shaders, meshes, audio, animation, fonts, data, outside, other).";
        }

        private static IEnumerable<string> ObjectsComplete(string[] args)
        {
            bool nested = args.Length > 0 && (args[0].Equals("usedby", StringComparison.OrdinalIgnoreCase) || args[0].Equals("object", StringComparison.OrdinalIgnoreCase));
            int kindIndex = nested ? 1 : 0;
            var names = new List<string>();
            if (args.Length - 1 == kindIndex)
            {
                if (!nested) names.Add("usedby");
                names.AddRange(new[] { "textures", "sprites", "materials", "shaders", "meshes", "audio", "animation", "fonts", "data", "outside", "other" });
                return names;
            }
            if (args.Length - 1 == kindIndex + 1 && ObjectList.TryParseKind(args[kindIndex], out ObjectKind kind))
            {
                foreach (ObjectEntry e in InspectorObjects.List().Find(kind, args[kindIndex + 1], false, 30))
                {
                    if (!string.IsNullOrEmpty(e.Name)) names.Add(e.Name.IndexOf(' ') >= 0 ? "\"" + e.Name + "\"" : e.Name);
                }
            }
            return names;
        }
    }
}
