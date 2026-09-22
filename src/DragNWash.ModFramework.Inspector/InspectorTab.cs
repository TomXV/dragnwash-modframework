using DragNWash.ModFramework.ToolWindow;
using TW = global::DragNWash.ModFramework.ToolWindow.ToolWindow;
using System;
using System.Collections.Generic;
using UnityEngine;
using Node = DragNWash.ModFramework.Inspector.InspectorModel.Node;
using Member = DragNWash.ModFramework.Inspector.InspectorModel.Member;

namespace DragNWash.ModFramework.Inspector
{
    // The "Inspector" tab: the scene's objects, a selected object's components,
    // and a selected component's (or material's) members, readable and editable
    // while the game runs. Three panes side by side; in a narrow window, three
    // pages. Nothing is saved: an edit lives until the scene reloads or the
    // game quits, and Reset puts back what a row held before its first edit.
    // Scene | Objects: Objects puts the object explorer (every loaded object by
    // kind; InspectorObjectsPane) in the left pane, beside the same members pane.
    // See https://github.com/TomXV/dragnwash-modframework/wiki/Inspector.
    //
    // Split by feature into InspectorTab.<Feature>.cs beside this file (Menus,
    // Animator, Bodies, Scenes, Export, History, Hierarchy, Members, Console;
    // also InspectorObjectsPane.cs and InspectorKeys.cs). This file keeps the
    // shared state, styles setup, the main Draw() entry and the small helpers
    // (FlowButton, ButtonWidth, WrappedLine, MemberId, WhereLabel, Drawable)
    // the other files call into.
    internal static partial class InspectorTab
    {
        internal const string Title = "Inspector";
        private const string ControlPrefix = "DnWInspect:";
        private const float NarrowWidth = 700f;

        private static IDisposable _tab;

        // Hierarchy.
        private static List<Node> _tree;
        private static readonly HashSet<int> Expanded = new HashSet<int>();
        private static string _search = "";
        private static string _searched;
        private static List<Node> _results;
        private static bool _dirty = true;
        // Set when the selection changed from outside the tree (pick, Go, Parent,
        // console): the tree scrolls to show it on its next draw.
        private static bool _revealSelection;
        private static Vector2 _scrollTree, _scrollComponents, _scrollMembers;

        // Selection.
        private static GameObject _object;
        private static object _target;          // GameObject, Component or Material
        internal static object Target => _target;
        private static List<Member> _members;
        private static int _rendererUsers = -1;

        private static string _focusedControl = "";
        private static int _actedFrame = -1;
        private static bool _noteShown;
        private static string _status = "";

        // Narrow window: which pane is the page.
        private static int _page;

        private static GUIStyle _cell, _mutedCell, _errorCell, _accentCell, _toggleStyle;

        /// <summary>The selected object, or null (also when it was destroyed).</summary>
        internal static GameObject SelectedObject => ReferenceEquals(_object, null) || !_object ? null : _object;

        internal static void Install()
        {
            if (_tab != null)
            {
                return;
            }
            _tab = TW.AddTab(TW.Guid, Title, Draw, 45);
            TW.PrepareCharacters(IconHierarchy + IconPick + IconHighlight + IconParent + IconMove + IconRotate + IconScale + IconResetTransform + IconHistory + IconRefresh + IconCamera + IconBones + IconWire + IconEditMesh + "\u25BE");
            GameEvents.OnSceneLoaded(TW.Guid, (scene, mode) => { _dirty = true; InspectorObjects.MarkStale(); });
            GameEvents.OnSceneUnloaded(TW.Guid, scene => { _dirty = true; InspectorObjects.MarkStale(); });
            TW.AddCommand(TW.Guid, "inspect",
                "inspect | inspect <name or path> [component] | inspect set <path> <component> <member> <value> | inspect pick",
                Command, Complete);
            TW.AddCommand(TW.Guid, "objects",
                "objects | objects <kind> [filter] | objects usedby <kind> <name>  (every loaded object by kind: textures, sprites, materials, shaders, meshes, audio, animation, fonts, data, outside, other)",
                ObjectsCommand, ObjectsComplete);
            TW.AddCommand(TW.Guid, "bodies",
                "bodies | bodies pause | bodies resume | bodies step [count]  (rigidbodies, fastest first; the physics pause is experimental)",
                InspectorBodies.Command, InspectorBodies.Complete);
        }

        // ---- selection (also from TW.Inspect, the console and pick mode) ----

        // A scene's object or component opens in Scene; a material stays in the
        // view that is open; anything else, and what no loaded scene holds,
        // opens in Objects.
        internal static void Select(UnityEngine.Object target)
        {
            if (target == null)
            {
                return;
            }
            switch (target)
            {
                case GameObject go when go.scene.IsValid():
                    SetMode(false);
                    SelectObject(go);
                    break;
                case Component c when c.gameObject.scene.IsValid():
                    SetMode(false);
                    SelectObject(c.gameObject);
                    SetTarget(c);
                    break;
                case Material m when !_objectsMode:
                    SetTarget(m);
                    break;
                default:
                    SelectInObjects(target);
                    return;
            }
            _page = _target != null && !(_target is GameObject) ? 2 : 1;
        }

        private static void SelectObject(GameObject go)
        {
            _object = go;
            _status = go != null ? InspectorModel.PathOf(go.transform) : "";
            InspectorModel.ExpandTo(go != null ? go.transform : null, Expanded);
            _dirty = true;
            _revealSelection = go != null;
            SetTarget(go);
            _scrollComponents = Vector2.zero;
        }

        private static void SetTarget(object target)
        {
            _target = target;
            // A new selection shows its members, not the history that was open.
            _showHistory = false;
            _showBodies = false;
            _showUsedBy = false;
            _header = null;
            _bodyNote = "";
            _members = null;
            _rendererUsers = -1;
            Drafts.Clear();
            DraftStart.Clear();
            RowErrors.Clear();
            Frozen.Clear();
            _menuRow = null;
            ExpandedLists.Clear();
            InspectorColorPicker.Close();
            InspectorCode.Reset();
            _scrollMembers = Vector2.zero;
        }

        private static List<Member> MembersOfTarget()
        {
            if (_members != null)
            {
                return _members;
            }
            switch (_target)
            {
                case Material m:
                    _members = InspectorModel.MaterialMembers(m);
                    break;
                case GameObject _:
                    _members = InspectorModel.GameObjectMembers();
                    break;
                case null:
                    _members = new List<Member>();
                    break;
                case UnityEngine.Object listed when InspectorObjects.IsListed(listed):
                    // From Objects: its members, less the arrays Unity copies on every read.
                    _members = InspectorObjects.MembersOf(_target.GetType());
                    break;
                case Component animator when InspectorAnimators.IsAnimator(animator):
                    // Its parameters and layer weights first, then its own members.
                    _members = new List<Member>(InspectorAnimators.Members(animator));
                    _members.AddRange(InspectorModel.MembersOf(_target.GetType()));
                    break;
                default:
                    _members = InspectorModel.MembersOf(_target.GetType());
                    break;
            }
            return _members;
        }

        // ---- drawing -------------------------------------------------------------------

        private static void EnsureStyles(ToolWindowStyles s)
        {
            if (_cell != null)
            {
                return;
            }
            _cell = new GUIStyle(s.Label) { wordWrap = false, clipping = TextClipping.Clip };
            _mutedCell = new GUIStyle(s.MutedLabel) { wordWrap = false, clipping = TextClipping.Clip };
            _errorCell = new GUIStyle(_mutedCell);
            _errorCell.normal.textColor = TW.ErrorColor;
            _errorCell.hover.textColor = TW.ErrorColor;
            _accentCell = new GUIStyle(_cell);
            _accentCell.normal.textColor = TW.AccentColor;
            _accentCell.hover.textColor = TW.AccentColor;
            // Wraps: the note is long, and a narrow window must not cut it off.
            _warningCell = new GUIStyle(s.WrappedLabel);
            _warningCell.normal.textColor = TW.WarningColor;
            _warningCell.hover.textColor = TW.WarningColor;
        }

        private static GUIStyle _warningCell;

        private static Vector2 _tabScreenOrigin;

        private static void Draw(Rect area)
        {
            ToolWindowStyles s = TW.Styles;
            EnsureStyles(s);
            _tabScreenOrigin = GUIUtility.GUIToScreenPoint(Vector2.zero);
            float row = TW.RowHeight, pad = TW.Padding;
            float x = area.x + pad, y = area.y + pad, w = area.width - 2 * pad;
            Event ev = Event.current;

            // A button paints its hover look wherever the pointer is, even under
            // a menu. While the pointer is over an open menu, everything painted
            // before the menu is told the pointer is elsewhere; the menu gets it back.
            _pointerHidden = false;
            if (ev.type == EventType.Repaint
                && ((_menuRow != null && _menuBoxShown.Contains(ev.mousePosition))
                    || (_toolMenu != null && _toolMenuBoxShown.Contains(ev.mousePosition))))
            {
                _pointer = ev.mousePosition;
                ev.mousePosition = new Vector2(-100000f, -100000f);
                _pointerHidden = true;
            }

            // A destroyed selection is dropped, not thrown on. Unity's == null is
            // already true for a destroyed object, so the reference itself is
            // tested first and the object's liveness second.
            if (!ReferenceEquals(_object, null) && !_object)
            {
                _object = null;
                if (_objectsMode)
                {
                    _sceneTarget = null;
                }
                else
                {
                    SetTarget(null);
                    _status = "The selected object was destroyed.";
                }
            }
            if (_target is UnityEngine.Object uo && !uo)
            {
                if (_objectsMode)
                {
                    _assetObject = null;
                    SetTarget(null);
                    _status = "The selected object was destroyed.";
                }
                else
                {
                    SetTarget(_object);
                    _status = "The selected component was destroyed.";
                }
            }
            if (ev.type == EventType.Repaint)
            {
                _focusedControl = GUI.GetNameOfFocusedControl() ?? "";
            }
            // Enter applies the draft of the focused row. Keys are handled before
            // the fields are drawn (see ConsoleTab for why), and the character
            // half of the key is swallowed in the same frame.
            bool enter = ev.keyCode == KeyCode.Return || ev.keyCode == KeyCode.KeypadEnter || ev.character == (char)10 || ev.character == (char)13;
            if (ev.type == EventType.KeyDown && enter)
            {
                if (_actedFrame == Time.frameCount)
                {
                    ev.Use();
                }
                else if (_focusedControl.StartsWith(ControlPrefix, StringComparison.Ordinal))
                {
                    Apply(RowKeyOf(_focusedControl));
                    _actedFrame = Time.frameCount;
                    ev.Use();
                }
            }

            if (!_noteShown)
            {
                _noteShown = true;
                _status = "Experimental. Edits are not saved. Keys: arrows move in the list (left and right close and open), W/E/R gizmo, Q off, P pick, H highlight, T tree, C camera, B bones, N wireframe, M edit mesh, Ctrl+Z undo, Ctrl+Up parent.";
            }
            // Shortcuts, only while no field has the keyboard (typing must not
            // trigger them), and only for keys the fields never use anyway.
            if (ev.type == EventType.KeyDown && string.IsNullOrEmpty(_focusedControl) && !InspectorColorPicker.Open)
            {
                bool ctrl = ev.control || ev.command;
                bool handled = true;
                if (InspectorFreeCamera.Flying)
                {
                    handled = false;
                }
                else switch (ev.keyCode)
                {
                    case KeyCode.C: if (!ctrl) InspectorFreeCamera.Toggle(); else handled = false; break;
                    case KeyCode.B: if (!ctrl) InspectorBones.Show = !InspectorBones.Show; else handled = false; break;
                    case KeyCode.N: if (!ctrl) InspectorMesh.Wireframe = !InspectorMesh.Wireframe; else handled = false; break;
                    case KeyCode.M: if (!ctrl) { if (InspectorMesh.Editing) InspectorMesh.StopEditing(); else InspectorMesh.Editing = true; } else handled = false; break;
                    case KeyCode.W: if (!ctrl) ToggleGizmo(InspectorGizmo.GizmoMode.Move); else handled = false; break;
                    case KeyCode.E: if (!ctrl) ToggleGizmo(InspectorGizmo.GizmoMode.Rotate); else handled = false; break;
                    case KeyCode.R: if (!ctrl) ToggleGizmo(InspectorGizmo.GizmoMode.Scale); else handled = false; break;
                    case KeyCode.Q: if (!ctrl) InspectorGizmo.Mode = InspectorGizmo.GizmoMode.None; else handled = false; break;
                    case KeyCode.P: if (!ctrl) { if (InspectorPick.Picking) InspectorPick.End(); else InspectorPick.Begin(); } else handled = false; break;
                    case KeyCode.H: if (!ctrl) InspectorPick.Highlight = !InspectorPick.Highlight; else handled = false; break;
                    case KeyCode.T: if (!ctrl) { bool shown = _objectsMode ? (_showObjectList = !_showObjectList) : (_showHierarchy = !_showHierarchy); if (narrowWindow(area)) _page = shown ? 0 : 1; } else handled = false; break;
                    case KeyCode.Z: if (ctrl) _status = InspectorHistory.Undo(); else handled = false; break;
                    case KeyCode.UpArrow:
                        if (ctrl && SelectedObject != null && SelectedObject.transform.parent != null) Select(SelectedObject.transform.parent.gameObject);
                        else handled = !ctrl && ListKey(KeyCode.UpArrow);
                        break;
                    case KeyCode.DownArrow:
                    case KeyCode.LeftArrow:
                    case KeyCode.RightArrow:
                    case KeyCode.Home:
                    case KeyCode.End:
                    case KeyCode.PageUp:
                    case KeyCode.PageDown:
                        handled = !ctrl && ListKey(ev.keyCode);
                        break;
                    case KeyCode.Escape:
                        if (_menuRow != null) _menuRow = null;
                        else if (InspectorPick.Picking) InspectorPick.End();
                        else if (InspectorGizmo.Mode != InspectorGizmo.GizmoMode.None) InspectorGizmo.Mode = InspectorGizmo.GizmoMode.None;
                        else handled = false;
                        break;
                    default: handled = false; break;
                }
                if (handled)
                {
                    ev.Use();
                }
            }

            // The menus lie over everything in the tab: the toolbar, the search
            // field, the breadcrumb and the panes. IMGUI hands an event to
            // controls in drawing order, so the menus' input pass runs here,
            // before any of those is drawn, and only their paint pass at the
            // end; a click inside a menu never reaches what is underneath.
            if (ev.type != EventType.Repaint)
            {
                DrawMenu(area, s, row);
                DrawToolMenu(area, s, row);
            }

            bool narrow = area.width < NarrowWidth;
            // One toolbar row: selection, gizmo, history, search. Icons where
            // the window font has them, short words where it does not; the row
            // wraps in a narrow window.
            float toolbarTop = y;
            float bx = x;
            void Place(float width, Func<Rect, bool> button)
            {
                if (bx + width > x + w && bx > x)
                {
                    bx = x;
                    y += row + 6;
                }
                button(new Rect(bx, y, width, row));
                bx += width + 6;
            }
            void Tool(string icon, string word, string tip, bool on, Action click)
            {
                string label = TW.CanDraw(icon) && icon != word ? icon + " " + word : word;
                float width = Mathf.Max(38, s.Button.CalcSize(new GUIContent(label)).x + 14);
                Place(width, rect =>
                {
                    if (GUI.Button(rect, new GUIContent(label, tip), on ? s.SelectedButton : s.Button))
                    {
                        _lastToolRect = rect;
                        click();
                    }
                    return true;
                });
            }
            if (narrow && _page > 0)
            {
                Tool("<", "<", "Back", false, () => _page--);
            }
            // Scene | Objects: the scenes' hierarchy, or every loaded object by kind.
            Tool("Scene", "Scene", "The scenes' objects and their components", !_objectsMode, () => SetMode(false));
            Tool("Objects", "Objects", "Every loaded object by kind: textures, materials, meshes, sounds, data...", _objectsMode, () => { SetMode(true); if (narrow) _page = 0; });
            bx += 6;
            if (_objectsMode)
            {
                Tool(IconHierarchy, "List", "Show or hide the list", _showObjectList, () => { _showObjectList = !_showObjectList; if (narrow) _page = _showObjectList ? 0 : 1; });
            }
            else
            {
                Tool(IconHierarchy, "Tree", "Show or hide the hierarchy", _showHierarchy, () => { _showHierarchy = !_showHierarchy; if (narrow) _page = _showHierarchy ? 0 : 1; });
            }
            Tool(IconPick, "Pick", "Pick: click an object in the game", InspectorPick.Picking, () => { if (InspectorPick.Picking) InspectorPick.End(); else InspectorPick.Begin(); });
            if (!_objectsMode && SelectedObject != null && SelectedObject.transform.parent != null)
            {
                Tool(IconParent, "Parent", "Select the parent", false, () => Select(SelectedObject.transform.parent.gameObject));
            }
            bx += 6;
            // Two menus hold the rest: what to do to the selection, and what to show over the game.
            string gizmoLabel = InspectorGizmo.Mode == InspectorGizmo.GizmoMode.None ? (InspectorMesh.Editing ? "Edit: mesh (experimental)" : "Edit") : "Edit: " + InspectorGizmo.Mode;
            bool editOn = InspectorGizmo.Mode != InspectorGizmo.GizmoMode.None || InspectorMesh.Editing;
            Tool(IconMove, gizmoLabel + " \u25BE", "Move, rotate, scale, edit the mesh, reset", editOn, () => OpenToolMenu("edit"));
            bool viewOn = InspectorPick.Highlight || InspectorBones.Show || InspectorMesh.Wireframe || InspectorFreeCamera.Active || InspectorDebugView.Active;
            Tool(IconHighlight, "View \u25BE", "Highlight, bones, wireframe, free camera", viewOn, () => OpenToolMenu("view"));
            bx += 6;
            Tool(IconHistory, InspectorHistory.Count > 0 ? "History " + InspectorHistory.Count : "History", "History of edits", _showHistory, () => { _showHistory = !_showHistory; _showBodies = false; _showScenes = false; _showUsedBy = false; });
            Tool(IconRefresh, "Refresh", _objectsMode ? "List the loaded objects again and reread the members" : "Rebuild the tree and reread the members", false, () => { _dirty = true; _members = null; _header = null; if (_objectsMode) RefreshObjects(); });
            if (x + w - bx < 140)
            {
                bx = x;
                y += row + 6;
            }
            var searchRect = new Rect(bx, y, x + w - bx, row);
            // Each view keeps its own search; Objects' takes t:Type too.
            string searchNext = TW.FilterField(searchRect, _objectsMode ? _objectsSearch : _search, _objectsMode ? "Search (t:Material for one type)" : "Search", s);
            if (_objectsMode) _objectsSearch = searchNext;
            else _search = searchNext;
            y += row + 6;
            _toolbarRect = new Rect(x, toolbarTop, w, y - toolbarTop);

            // The breadcrumb: the selection's path, each ancestor a button. In
            // Objects, the status line instead (what was listed, and notes).
            if (SelectedObject != null && !_objectsMode)
            {
                float cx = x;
                var chain = new List<Transform>();
                for (Transform p = SelectedObject.transform; p != null; p = p.parent) chain.Add(p);
                chain.Reverse();
                GUI.Label(new Rect(cx, y, w, row), "", _mutedCell);
                for (int i = 0; i < chain.Count; i++)
                {
                    bool last = i == chain.Count - 1;
                    string name = Drawable(chain[i].name);
                    float cw = Mathf.Min((last ? _accentCell : _mutedCell).CalcSize(new GUIContent(name)).x + 6, w * 0.5f);
                    if (cx + cw + 16 > x + w)
                    {
                        GUI.Label(new Rect(cx, y, x + w - cx, row), "...", _mutedCell);
                        break;
                    }
                    if (GUI.Button(new Rect(cx, y, cw, row), name, last ? _accentCell : _mutedCell) && !last)
                    {
                        Select(chain[i].gameObject);
                    }
                    cx += cw;
                    if (!last)
                    {
                        GUI.Label(new Rect(cx, y, 14, row), "/", _mutedCell);
                        cx += 14;
                    }
                }
            }
            else
            {
                var statusContent = new GUIContent(Drawable(_status));
                float statusHeight = Mathf.Max(row, s.WrappedLabel.CalcHeight(statusContent, w));
                GUI.Label(new Rect(x, y, w, statusHeight), statusContent, s.WrappedLabel);
                y += statusHeight - row;
            }
            y += row + 2;
            if (InspectorFreeCamera.Active)
            {
                GUI.Label(new Rect(x, y, w, row), InspectorFreeCamera.Status(), _accentCell);
                y += row;
            }
            if (InspectorMesh.Editing)
            {
                var meshNote = new GUIContent("Edit mesh is experimental: it changes a copy of the mesh in memory only, and can misbehave on some meshes.");
                float noteHeight = Mathf.Max(row, _warningCell.CalcHeight(meshNote, w));
                GUI.Label(new Rect(x, y, w, noteHeight), meshNote, _warningCell);
                y += noteHeight;
            }
            if (InspectorBodies.Paused)
            {
                GUI.Label(new Rect(x, y, w, row), "Physics paused (experimental): Step moves it one fixed step; Resume or closing the window puts it back.", _accentCell);
                y += row;
            }
            if (InspectorDebugView.Active)
            {
                GUI.Label(new Rect(x, y, w, row), InspectorDebugView.Status(), _accentCell);
                y += row;
            }

            if (_dirty)
            {
                _tree = InspectorModel.BuildTree(Expanded);
                _dirty = false;
            }
            if (_search != _searched)
            {
                _searched = _search;
                if (InspectorDebugView.Mode == InspectorDebugView.Scope.Filter) InspectorDebugView.Filter = _search ?? "";
                _results = string.IsNullOrEmpty(_search) ? null : InspectorModel.Search(_search);
                _scrollTree = Vector2.zero;
                if (_results != null) _showHierarchy = true;
            }

            float bodyHeight = area.yMax - pad - y;
            bool leftPane = _objectsMode ? _showObjectList : _showHierarchy;
            if (narrow)
            {
                var pane = new Rect(x, y, w, bodyHeight);
                if (_page == 0 && leftPane) DrawLeft(pane, s, row);
                else DrawRight(pane, s, row);
            }
            else if (leftPane)
            {
                float gap = 8;
                float w1 = (w - gap) * 0.32f, w2 = (w - gap) - w1;
                DrawLeft(new Rect(x, y, w1, bodyHeight), s, row);
                DrawRight(new Rect(x + w1 + gap, y, w2, bodyHeight), s, row);
            }
            else
            {
                DrawRight(new Rect(x, y, w, bodyHeight), s, row);
            }
            if (ev.type == EventType.Repaint)
            {
                if (_pointerHidden)
                {
                    ev.mousePosition = _pointer;
                    _pointerHidden = false;
                }
                DrawMenu(area, s, row);
                DrawToolMenu(area, s, row);
            }
        }

        private static void DrawLeft(Rect pane, ToolWindowStyles s, float row)
        {
            if (_objectsMode) DrawObjectList(pane, s, row);
            else DrawHierarchy(pane, s, row);
        }

        // The members pane, or the view that stands in its place.
        private static void DrawRight(Rect pane, ToolWindowStyles s, float row)
        {
            if (_showBodies) DrawBodies(pane, s, row);
            else if (_showScenes) DrawScenes(pane, s, row);
            else if (ShowingClips) DrawClips(pane, s, row);
            else if (ShowingLayers) DrawLayers(pane, s, row);
            else if (_showHistory) DrawHistory(pane, s, row);
            else if (_showUsedBy) DrawUsedBy(pane, s, row);
            else DrawMembers(pane, s, row);
        }

        // Toolbar icons, prepared at Install so drawing them uploads nothing;
        // a glyph the font lacks falls back to a word.
        private const string IconHierarchy = "\u2630", IconPick = "\u25CE", IconHighlight = "\u25A3", IconParent = "\u2191",
            IconMove = "\u2725", IconRotate = "\u21BB", IconScale = "\u229E", IconResetTransform = "Reset", IconHistory = "\u25D0", IconCamera = "\u25C9", IconBones = "\u2442", IconWire = "\u25A6", IconEditMesh = "\u25B3", IconRefresh = "\u21BA";
        private static bool _showHierarchy;

        // The tab as it was left, for the config: "objects|tree|list|folder,folder".
        // Set only at startup, before anything is selected.
        internal static string Layout
        {
            get => string.Join("|", _objectsMode ? "objects" : "scene", _showHierarchy ? "tree" : "notree", _showObjectList ? "list" : "nolist", string.Join(",", OpenFolders));
            set
            {
                string[] parts = (value ?? "").Split('|');
                if (parts.Length != 4)
                {
                    return;
                }
                _objectsMode = parts[0] == "objects";
                _showHierarchy = parts[1] == "tree";
                _showObjectList = parts[2] == "list";
                OpenFolders.Clear();
                foreach (string folder in parts[3].Split(','))
                {
                    if (folder.Length > 0) OpenFolders.Add(folder);
                }
            }
        }

        private static string MemberId(RowInfo r)
        {
            return r.Key.Substring(ControlPrefix.Length);
        }

        private static string WhereLabel()
        {
            if (_objectsMode && _target is UnityEngine.Object listed && (InspectorObjects.IsListed(listed) || InspectorObjects.IsOutsideScenes(listed)))
            {
                return listed is Component c ? InspectorObjects.Describe(c.gameObject) + " : " + c.GetType().Name : InspectorObjects.Describe(listed);
            }
            string where = SelectedObject != null ? InspectorModel.PathOf(SelectedObject.transform) : "";
            string what = _target is Material m ? "Material " + m.name : _target is GameObject ? "GameObject" : _target?.GetType().Name ?? "";
            return string.IsNullOrEmpty(where) ? what : where + " : " + what;
        }

        // Buttons laid out left to right, onto a new line when the pane is too narrow.
        private static bool FlowButton(ref float bx, ref float y, float x, float w, float width, string label, bool on, ToolWindowStyles s, float row)
        {
            if (bx > x && bx + width > x + w)
            {
                bx = x;
                y += row + 2;
            }
            bool clicked = GUI.Button(new Rect(bx, y, width, row), label, on ? s.SelectedButton : s.Button);
            bx += width + 6;
            return clicked;
        }

        private static float ButtonWidth(ToolWindowStyles s, string label) => Mathf.Max(60, s.Button.CalcSize(new GUIContent(label)).x + 14);

        private static readonly Dictionary<GUIStyle, GUIStyle> Wrapped = new Dictionary<GUIStyle, GUIStyle>();
        // A line that wraps in a narrow pane instead of running out of it; returns the y below it.
        private static float WrappedLine(string text, float x, float y, float w, GUIStyle style, float row)
        {
            if (!Wrapped.TryGetValue(style, out GUIStyle wrapped))
            {
                Wrapped[style] = wrapped = new GUIStyle(style) { wordWrap = true, clipping = TextClipping.Clip };
            }
            var content = new GUIContent(Drawable(text));
            float h = Mathf.Max(row, wrapped.CalcHeight(content, w));
            GUI.Label(new Rect(x, y, w, h), content, wrapped);
            return y + h;
        }

        // ---- helpers -------------------------------------------------------------------
        private static string Drawable(string text)
        {
            return TW.Drawable(text ?? "");
        }
    }
}
