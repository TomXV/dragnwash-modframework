using DragNWash.ModFramework.ToolWindow;
using TW = global::DragNWash.ModFramework.ToolWindow.ToolWindow;
using System.Collections.Generic;
using UnityEngine;
using Node = DragNWash.ModFramework.Inspector.InspectorModel.Node;

namespace DragNWash.ModFramework.Inspector
{
    // The Inspector tab's Scene hierarchy: the tree (or the search results),
    // one row per object, indented and scrolled to keep the selection in view.
    // A deep tree narrows its indent, down to a minimum, before it starts
    // hiding levels above what is in view.
    internal static partial class InspectorTab
    {
        private static string FitPath(string path, float width, GUIStyle style)
        {
            if (string.IsNullOrEmpty(path) || style == null || style.CalcSize(new GUIContent(Drawable(path))).x <= width) return path;
            string[] parts = path.Split('/');
            for (int first = 1; first < parts.Length; first++)
            {
                string cut = ".../" + string.Join("/", parts, first, parts.Length - first);
                if (style.CalcSize(new GUIContent(Drawable(cut))).x <= width || first == parts.Length - 1) return cut;
            }
            return path;
        }
        // Room kept for a name in the tree, and the width of one level.
        private const float TreeNameRoom = 120;
        private const float MaxStep = 18;
        private const float MinStep = 8;

        // The tree, or the search results, one row per object.
        private static void DrawHierarchy(Rect pane, ToolWindowStyles s, float row)
        {
            TW.Fill(pane, TW.InsetColor);
            // Search results name objects; after a scene change some are gone
            // (a destroyed Transform, not a heading), so search again.
            if (_results != null && _results.Exists(n => !ReferenceEquals(n.Transform, null) && n.Transform == null))
            {
                _results = InspectorModel.Search(_search ?? "");
            }
            List<Node> nodes = _results ?? _tree ?? new List<Node>();
            float inner = pane.width - 20;
            // Follow the selection: scroll so its row sits in the upper third of the pane.
            if (_revealSelection && !ReferenceEquals(_object, null) && _object)
            {
                for (int i = 0; i < nodes.Count; i++)
                {
                    if (nodes[i].Transform != null && nodes[i].Transform && ReferenceEquals(nodes[i].Transform.gameObject, _object))
                    {
                        float target = i * row - pane.height / 3f;
                        _scrollTree.y = Mathf.Clamp(target, 0, Mathf.Max(0, nodes.Count * row - pane.height));
                        _revealSelection = false;
                        break;
                    }
                }
            }
            _treeViewHeight = pane.height;
            TW.ApplyScroll(pane, ref _scrollTree);
            _scrollTree = GUI.BeginScrollView(pane, _scrollTree, new Rect(0, 0, inner, Mathf.Max(pane.height, nodes.Count * row)), false, false);
            // A deep tree (a rig's bones) would push names out of the pane: the
            // levels get narrower, down to MinStep, and when even that is too
            // wide, the levels above every row in view are left out.
            int deepest = 0;
            foreach (Node n in nodes) deepest = Mathf.Max(deepest, n.Depth);
            float room = inner - 24 - TreeNameRoom;
            float step = deepest > 0 ? Mathf.Clamp(room / deepest, MinStep, MaxStep) : MaxStep;
            int skip = 0;
            if (deepest * step > room)
            {
                int first = Mathf.Clamp((int)(_scrollTree.y / row), 0, nodes.Count);
                int last = Mathf.Clamp((int)((_scrollTree.y + pane.height) / row) + 1, 0, nodes.Count);
                int shallow = int.MaxValue, deep = 0;
                for (int i = first; i < last; i++)
                {
                    if (nodes[i].Transform == null) continue;
                    shallow = Mathf.Min(shallow, nodes[i].Depth);
                    deep = Mathf.Max(deep, nodes[i].Depth);
                }
                if (shallow != int.MaxValue)
                {
                    skip = Mathf.Clamp(Mathf.CeilToInt((deep * step - room) / step), 0, shallow);
                }
            }
            float ry = 0;
            foreach (Node n in nodes)
            {
                if (ry + row >= _scrollTree.y && ry <= _scrollTree.y + pane.height)
                {
                    if (n.Transform == null)
                    {
                        GUI.Label(new Rect(4, ry, inner - 4, row), Drawable(n.Name.ToUpperInvariant()), _mutedCell);
                    }
                    else if (n.Transform)
                    {
                        int depth = n.Depth - skip;
                        float indent = 4 + depth * step;
                        // Indent guides: one faint line per ancestor level, through
                        // the middle of that level's toggle, and a short tick to this
                        // row that stops before the row's own toggle.
                        var guide = new Color(TW.MutedColor.r, TW.MutedColor.g, TW.MutedColor.b, 0.35f);
                        for (int d = skip > 0 ? 0 : 1; d < depth; d++)
                        {
                            TW.Fill(new Rect(4 + d * step + 9, ry, 1, row), guide);
                        }
                        if (depth > 1 || (skip > 0 && depth > 0))
                        {
                            TW.Fill(new Rect(4 + (depth - 1) * step + 9, ry + row / 2, Mathf.Max(2, step - 12), 1), guide);
                        }
                        if (_results == null && n.HasChildren)
                        {
                            int id = n.Transform.GetInstanceID();
                            bool open = Expanded.Contains(id);
                            if (GUI.Button(new Rect(indent, ry, 18, row), open ? "-" : "+", _toggleStyle ?? (_toggleStyle = new GUIStyle(s.MutedLabel) { alignment = TextAnchor.MiddleCenter })))
                            {
                                if (open) Expanded.Remove(id); else Expanded.Add(id);
                                _dirty = true;
                            }
                        }
                        bool selected = !ReferenceEquals(_object, null) && ReferenceEquals(_object, n.Transform.gameObject);
                        var label = new Rect(indent + 20, ry, inner - indent - 20, row);
                        if (selected)
                        {
                            TW.Fill(new Rect(0, ry, inner, row), TW.PanelColor);
                        }
                        GUIStyle cellStyle = selected ? _accentCell : (n.Active ? _cell : _mutedCell);
                        // A search result's path, cut from the front when it is too
                        // wide, so the object's own name stays in view.
                        string text = _results != null ? FitPath(n.Path, label.width, cellStyle) : n.Name;
                        if (GUI.Button(label, Drawable(text), cellStyle))
                        {
                            SelectObject(n.Transform.gameObject);
                            _page = 1;
                        }
                    }
                }
                ry += row;
            }
            GUI.EndScrollView();
        }
    }
}
