using DragNWash.ModFramework.ToolWindow;
using TW = global::DragNWash.ModFramework.ToolWindow.ToolWindow;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace DragNWash.ModFramework.Inspector
{
    // The Inspector tab's two menus: the toolbar's Edit/View tool menu (move,
    // rotate, scale, edit mesh, highlight, bones, wireframe, free camera, debug
    // view...), and a row's right-click menu (now/original/previous, copy,
    // show in History). Shared drawing: the frame, scrolling and input-swallow
    // that make a menu sit on top of everything under it.
    internal static partial class InspectorTab
    {
        private static RowInfo _menuRow;
        private static int _menuComponent = -1;   // which x/y/z field was right-clicked, or -1 for the row
        private static Vector2 _menuAt;
        // ---- the toolbar menus ---------------------------------------------------------------

        private static string _toolMenu;
        private static Rect _lastToolRect;
        // Where the menus were laid out last, for hiding the pointer under them.
        private static Rect _menuBoxShown;
        private static Rect _toolMenuBoxShown;
        private static Vector2 _pointer;
        private static bool _pointerHidden;
        private static Rect _toolbarRect;
        private static Rect _toolMenuButton;
        private static void OpenToolMenu(string name)
        {
            _toolMenu = _toolMenu == name ? null : name;
            _toolMenuButton = _lastToolRect;
            _toolMenuScroll = 0;
        }
        private static void DrawToolMenu(Rect area, ToolWindowStyles s, float row)
        {
            if (_toolMenu == null)
            {
                return;
            }
            Event ev = Event.current;
            var items = new List<(string label, bool on, Action click, bool keep)>();
            GameObject sel = SelectedObject;
            if (_toolMenu == "edit")
            {
                items.Add((IconMove + " Move (W)", InspectorGizmo.Mode == InspectorGizmo.GizmoMode.Move, () => ToggleGizmo(InspectorGizmo.GizmoMode.Move), false));
                items.Add((IconRotate + " Rotate (E)", InspectorGizmo.Mode == InspectorGizmo.GizmoMode.Rotate, () => ToggleGizmo(InspectorGizmo.GizmoMode.Rotate), false));
                items.Add((IconScale + " Scale (R)", InspectorGizmo.Mode == InspectorGizmo.GizmoMode.Scale, () => ToggleGizmo(InspectorGizmo.GizmoMode.Scale), false));
                items.Add((IconEditMesh + " Edit mesh vertices (M) - experimental", InspectorMesh.Editing, () => { if (InspectorMesh.Editing) InspectorMesh.StopEditing(); else InspectorMesh.Editing = true; }, false));
                if (InspectorGizmo.HasOriginal(sel)) items.Add(("Reset transform", false, () => InspectorGizmo.ResetTransform(sel), false));
                if (InspectorMesh.HasEdited(sel)) items.Add(("Reset mesh", false, () => InspectorMesh.ResetMesh(sel), false));
            }
            else
            {
                items.Add((IconHighlight + " Highlight the selection (H)", InspectorPick.Highlight, () => InspectorPick.Highlight = !InspectorPick.Highlight, true));
                items.Add((IconBones + " Bones (B)", InspectorBones.Show, () => InspectorBones.Show = !InspectorBones.Show, true));
                items.Add((IconWire + " Wireframe (N)", InspectorMesh.Wireframe, () => InspectorMesh.Wireframe = !InspectorMesh.Wireframe, true));
                items.Add((IconCamera + " Free camera (C)", InspectorFreeCamera.Active, InspectorFreeCamera.Toggle, false));
                items.Add(("Debug view: everything the camera sees", InspectorDebugView.Mode == InspectorDebugView.Scope.Visible, () => InspectorDebugView.Mode = InspectorDebugView.Mode == InspectorDebugView.Scope.Visible ? InspectorDebugView.Scope.Off : InspectorDebugView.Scope.Visible, true));
                items.Add(("Debug view: the selection's children", InspectorDebugView.Mode == InspectorDebugView.Scope.Children, () => InspectorDebugView.Mode = InspectorDebugView.Mode == InspectorDebugView.Scope.Children ? InspectorDebugView.Scope.Off : InspectorDebugView.Scope.Children, true));
                items.Add(("Debug view: what the search text matches", InspectorDebugView.Mode == InspectorDebugView.Scope.Filter, () => { InspectorDebugView.Filter = _search ?? ""; InspectorDebugView.Mode = InspectorDebugView.Mode == InspectorDebugView.Scope.Filter ? InspectorDebugView.Scope.Off : InspectorDebugView.Scope.Filter; }, true));
                items.Add(("Colliders and triggers", InspectorDebugView.Colliders, () => InspectorDebugView.Colliders = !InspectorDebugView.Colliders, true));
                items.Add(("Lights", InspectorDebugView.Lights, () => InspectorDebugView.Lights = !InspectorDebugView.Lights, true));
                if (InspectorBodies.Available)
                {
                    items.Add(("Rigidbodies: centre of mass and velocity", InspectorDebugView.Bodies, () => InspectorDebugView.Bodies = !InspectorDebugView.Bodies, true));
                }
                items.Add(("    of the selection only", InspectorDebugView.SelectionOnly, () => InspectorDebugView.SelectionOnly = !InspectorDebugView.SelectionOnly, true));
                items.Add(("    collider and light shapes", InspectorDebugView.Shapes, () => InspectorDebugView.Shapes = !InspectorDebugView.Shapes, true));
                items.Add(("    draw screen rectangles", InspectorDebugView.Rects, () => InspectorDebugView.Rects = !InspectorDebugView.Rects, true));
                items.Add(("    draw 3D boxes", InspectorDebugView.Boxes, () => InspectorDebugView.Boxes = !InspectorDebugView.Boxes, true));
                items.Add(("    names (near the pointer when many)", InspectorDebugView.Names, () => InspectorDebugView.Names = !InspectorDebugView.Names, true));
                if (InspectorBodies.Available)
                {
                    items.Add(("Rigidbodies list", _showBodies, () => { _showBodies = !_showBodies; if (_showBodies) { _showHistory = false; _showScenes = false; _showUsedBy = false; if (_page == 0) _page = 1; } }, false));
                }
                items.Add(("Scenes and levels", _showScenes, () => { _showScenes = !_showScenes; if (_showScenes) { _showHistory = false; _showBodies = false; _showUsedBy = false; if (_page == 0) _page = 1; } }, false));
            }
            float lineH = row - 2;
            float width = 200;
            foreach (var item in items)
            {
                width = Mathf.Max(width, s.Button.CalcSize(new GUIContent(Drawable(item.label))).x + 24);
            }
            // The window clips whatever is drawn past its edge, so the menu is
            // kept inside it: no wider than the tab, no taller than the room
            // under its button, and a longer list scrolls with the wheel.
            width = Mathf.Min(width, area.width - 8);
            float contentHeight = items.Count * lineH + 8;
            float top = _toolMenuButton.yMax + 2;
            float height = Mathf.Max(lineH + 8, Mathf.Min(contentHeight, area.yMax - top));
            var box = new Rect(Mathf.Clamp(_toolMenuButton.x, area.x, area.xMax - width), top, width, height);
            _toolMenuBoxShown = box;
            if (ev.type == EventType.MouseDown && !box.Contains(ev.mousePosition) && !_toolMenuButton.Contains(ev.mousePosition))
            {
                _toolMenu = null;
                // Closing it is all the click does, so nothing under the menu
                // answers it; on the toolbar it still opens the other menu.
                if (!_toolbarRect.Contains(ev.mousePosition))
                {
                    ev.Use();
                }
                return;
            }
            if (ev.type == EventType.KeyDown && ev.keyCode == KeyCode.Escape)
            {
                _toolMenu = null;
                ev.Use();
                return;
            }
            ScrollMenu(ev, box, contentHeight, ref _toolMenuScroll);
            MenuFrame(box);
            TW.Fill(box, TW.PanelColor);
            TW.Fill(new Rect(box.x, box.y, 3, box.height), TW.AccentColor);
            GUI.BeginGroup(box);
            float y = 4 - _toolMenuScroll;
            foreach (var item in items)
            {
                var line = new Rect(8, y, box.width - 16, lineH);
                y += lineH;
                if (line.yMax <= 0 || line.y >= box.height) continue;
                if (item.on)
                {
                    TW.Fill(new Rect(3, line.y, box.width - 3, lineH), TW.InsetColor);
                }
                if (GUI.Button(line, Drawable(item.label), item.on ? _accentCell : _cell))
                {
                    item.click();
                    if (!item.keep) _toolMenu = null;
                }
            }
            GUI.EndGroup();
            MenuScrollbar(box, contentHeight, _toolMenuScroll);
            Swallow(ev, box);
        }
        private static float _toolMenuScroll;
        private static float _menuScroll;
        private static RowInfo _menuScrollRow;
        // A shadow below and to the right, and a thin frame, so a menu reads as
        // lying on top of what is under it. Painted only; the box itself takes
        // the input.
        private static void MenuFrame(Rect box)
        {
            if (Event.current.type != EventType.Repaint)
            {
                return;
            }
            var shadow = new Color(0f, 0f, 0f, 0.18f);
            for (int i = 1; i <= 3; i++)
            {
                TW.Fill(new Rect(box.x + i * 2, box.yMax, box.width, i * 2), shadow);
                TW.Fill(new Rect(box.xMax, box.y + i * 2, i * 2, box.height), shadow);
            }
            var edge = new Color(TW.MutedColor.r, TW.MutedColor.g, TW.MutedColor.b, 0.55f);
            TW.Fill(new Rect(box.x - 1, box.y - 1, box.width + 2, 1), edge);
            TW.Fill(new Rect(box.x - 1, box.yMax, box.width + 2, 1), edge);
            TW.Fill(new Rect(box.x - 1, box.y, 1, box.height), edge);
            TW.Fill(new Rect(box.xMax, box.y, 1, box.height), edge);
        }
        // The wheel over a menu taller than its box moves the list.
        private static void ScrollMenu(Event ev, Rect box, float contentHeight, ref float scroll)
        {
            if (ev.type == EventType.ScrollWheel && box.Contains(ev.mousePosition))
            {
                scroll += ev.delta.y * 20f;
            }
            scroll = Mathf.Clamp(scroll, 0f, Mathf.Max(0f, contentHeight - box.height));
        }
        // A thin bar on the right edge, only when the list does not fit.
        private static void MenuScrollbar(Rect box, float contentHeight, float scroll)
        {
            if (contentHeight <= box.height + 0.5f)
            {
                return;
            }
            float track = box.height - 4;
            float thumb = Mathf.Max(16f, track * box.height / contentHeight);
            float t = scroll / (contentHeight - box.height);
            TW.Fill(new Rect(box.xMax - 5, box.y + 2 + (track - thumb) * t, 3, thumb), TW.MutedColor);
        }
        // Every mouse event over an open menu is the menu's, buttons first:
        // whatever is drawn under it - a row, a field, a button - sees none of
        // them, and the wheel does not scroll the pane behind.
        private static void Swallow(Event ev, Rect box)
        {
            if ((ev.isMouse || ev.type == EventType.ScrollWheel || ev.type == EventType.ContextClick) && box.Contains(ev.mousePosition))
            {
                ev.Use();
            }
        }
        private static bool narrowWindow(Rect area)
        {
            return area.width < NarrowWidth;
        }
        private static void ToggleGizmo(InspectorGizmo.GizmoMode m)
        {
            InspectorGizmo.Mode = InspectorGizmo.Mode == m ? InspectorGizmo.GizmoMode.None : m;
        }
        private static void DrawMenu(Rect area, ToolWindowStyles s, float row)
        {
            if (_menuRow == null)
            {
                return;
            }
            Event ev = Event.current;
            if (ev.type == EventType.KeyDown && ev.keyCode == KeyCode.Escape)
            {
                _menuRow = null;
                ev.Use();
                return;
            }
            RowInfo r = _menuRow;
            string member = MemberId(r);
            bool hasOriginal = InspectorHistory.TryOriginal(_target, member, out object original);
            bool hasPrevious = InspectorHistory.TryPrevious(_target, member, out object previous);
            object now = SafeGet(r.Member, r.Getter);
            var items = new List<KeyValuePair<string, Action>>();
            // One component (x, y, z, r, g, b, a) of a composite: its own original and previous.
            if (_menuComponent >= 0 && InspectorModel.IsComposite(r.Type) && now != null)
            {
                int ci = _menuComponent;
                string[] labels = InspectorModel.ComponentLabels(r.Type);
                string cname = ci < labels.Length ? labels[ci] : "?";
                float[] parts = InspectorModel.Components(now);
                items.Add(new KeyValuePair<string, Action>($"{cname} now:  {(ci < parts.Length ? InspectorModel.Fmt(parts[ci]) : "?")}", null));
                if (hasOriginal && original != null)
                {
                    float[] o = InspectorModel.Components(original);
                    if (ci < o.Length)
                    {
                        items.Add(new KeyValuePair<string, Action>($"Reset {cname} to original:  {InspectorModel.Fmt(o[ci])}", () =>
                        {
                            float[] cur = InspectorModel.Components(SafeGet(r.Member, r.Getter));
                            if (ci < cur.Length) { cur[ci] = o[ci]; TrySet(r, InspectorModel.Compose(r.Type, cur)); }
                        }));
                    }
                }
                if (hasPrevious && previous != null)
                {
                    float[] pv = InspectorModel.Components(previous);
                    if (ci < pv.Length)
                    {
                        items.Add(new KeyValuePair<string, Action>($"{cname} back to previous:  {InspectorModel.Fmt(pv[ci])}", () =>
                        {
                            float[] cur = InspectorModel.Components(SafeGet(r.Member, r.Getter));
                            if (ci < cur.Length) { cur[ci] = pv[ci]; TrySet(r, InspectorModel.Compose(r.Type, cur)); }
                        }));
                    }
                }
            }
            items.Add(new KeyValuePair<string, Action>("now:  " + InspectorModel.Format(now), null));
            if (hasOriginal)
            {
                items.Add(new KeyValuePair<string, Action>("Reset to original:  " + InspectorModel.Format(original), () =>
                {
                    if (TrySet(r, original)) { InspectorHistory.ForgetOriginal(_target, member); Drafts.Remove(r.Key); }
                }));
            }
            if (hasPrevious)
            {
                items.Add(new KeyValuePair<string, Action>("Back to previous:  " + InspectorModel.Format(previous), () => { TrySet(r, previous); Drafts.Remove(r.Key); }));
            }
            items.Add(new KeyValuePair<string, Action>("Copy value", () => GUIUtility.systemCopyBuffer = InspectorModel.Format(now)));
            items.Add(new KeyValuePair<string, Action>("Copy name", () => GUIUtility.systemCopyBuffer = r.Member.Name));
            if (InspectorHistory.For(_target, member).Count > 0)
            {
                items.Add(new KeyValuePair<string, Action>("Show in History", () => _showHistory = true));
            }
            float lineH = row - 4;
            float width = 200;
            foreach (KeyValuePair<string, Action> item in items)
            {
                width = Mathf.Max(width, s.Button.CalcSize(new GUIContent(Drawable(item.Key))).x + 16);
            }
            width = Mathf.Min(width, area.width - 8);
            float contentHeight = items.Count * lineH + 8;
            float height = Mathf.Min(contentHeight, area.height);
            var box = new Rect(Mathf.Clamp(_menuAt.x, area.x, area.xMax - width), Mathf.Clamp(_menuAt.y, area.y, area.yMax - height), width, height);
            _menuBoxShown = box;
            if (!ReferenceEquals(_menuScrollRow, r))
            {
                _menuScrollRow = r;
                _menuScroll = 0;
            }
            // A click outside closes it, and does nothing else; a click inside
            // is handled by the buttons.
            if (ev.type == EventType.MouseDown && !box.Contains(ev.mousePosition))
            {
                _menuRow = null;
                ev.Use();
                return;
            }
            ScrollMenu(ev, box, contentHeight, ref _menuScroll);
            MenuFrame(box);
            TW.Fill(box, TW.PanelColor);
            TW.Fill(new Rect(box.x, box.y, 3, box.height), TW.AccentColor);
            GUI.BeginGroup(box);
            float y = 4 - _menuScroll;
            foreach (KeyValuePair<string, Action> item in items)
            {
                var line = new Rect(6, y, box.width - 12, lineH);
                y += lineH;
                if (line.yMax <= 0 || line.y >= box.height) continue;
                if (item.Value == null)
                {
                    GUI.Label(line, Drawable(item.Key), _mutedCell);
                }
                else if (GUI.Button(line, Drawable(item.Key), _cell))
                {
                    item.Value();
                    _menuRow = null;
                }
            }
            GUI.EndGroup();
            MenuScrollbar(box, contentHeight, _menuScroll);
            Swallow(ev, box);
        }
    }
}
