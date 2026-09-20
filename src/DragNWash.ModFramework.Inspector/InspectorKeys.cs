using DragNWash.ModFramework.ToolWindow;
using TW = global::DragNWash.ModFramework.ToolWindow.ToolWindow;
using System.Collections.Generic;
using UnityEngine;
using Node = DragNWash.ModFramework.Inspector.InspectorModel.Node;

namespace DragNWash.ModFramework.Inspector
{
    // The keys that move through the left pane's list: the tree and the search
    // results in Scene, the folders and objects in Objects. Up and down step one
    // row, Page up and Page down a pane, Home and End the ends, and left and
    // right close and open a folder or a node with children. The list the keys
    // move in is the one the view shows, so there is no focus to keep track of.
    internal static partial class InspectorTab
    {
        // The height each list had when it was last drawn, for Page up and Page
        // down and for keeping the row the keys are on in view.
        private static float _treeViewHeight, _objectsViewHeight;

        // In Objects the keys also stop on folder rows, which are no selection:
        // the folder the keys are on, or null while they are on an object.
        private static string _cursorFolder;

        // A key of the left pane's list. True when it did something, so the tab
        // swallows the key.
        private static bool ListKey(KeyCode key)
        {
            if (_objectsMode)
            {
                return _showObjectList && ObjectsKey(key);
            }
            return _showHierarchy && TreeKey(key);
        }

        private static int RowsPerPane(float height)
        {
            return Mathf.Max(1, (int)(height / TW.RowHeight) - 1);
        }

        // Scrolls the least that brings a row into view (unlike following a new
        // selection, which puts it in the upper third).
        private static void KeepInView(int index, int count, float viewHeight, ref Vector2 scroll)
        {
            if (viewHeight <= 0 || index < 0)
            {
                return;
            }
            float row = TW.RowHeight;
            float top = index * row;
            if (top < scroll.y) scroll.y = top;
            else if (top + row > scroll.y + viewHeight) scroll.y = top + row - viewHeight;
            scroll.y = Mathf.Clamp(scroll.y, 0, Mathf.Max(0, count * row - viewHeight));
        }

        // ---- Scene: the tree and the search results ----------------------------------------

        private static bool TreeKey(KeyCode key)
        {
            List<Node> nodes = _results ?? _tree;
            if (nodes == null || nodes.Count == 0)
            {
                return false;
            }
            int at = -1;
            if (!ReferenceEquals(_object, null) && _object)
            {
                for (int i = 0; i < nodes.Count; i++)
                {
                    if (nodes[i].Transform != null && nodes[i].Transform && ReferenceEquals(nodes[i].Transform.gameObject, _object))
                    {
                        at = i;
                        break;
                    }
                }
            }
            if (key == KeyCode.LeftArrow || key == KeyCode.RightArrow)
            {
                return TreeOpenClose(nodes, at, key == KeyCode.RightArrow);
            }
            int page = RowsPerPane(_treeViewHeight);
            // With nothing selected the keys start at the end they come from.
            int up = at < 0 ? nodes.Count : at, down = at < 0 ? -1 : at;
            int to;
            switch (key)
            {
                case KeyCode.UpArrow: to = StepTree(nodes, up, -1, 1); break;
                case KeyCode.DownArrow: to = StepTree(nodes, down, 1, 1); break;
                case KeyCode.PageUp: to = StepTree(nodes, up, -1, page); break;
                case KeyCode.PageDown: to = StepTree(nodes, down, 1, page); break;
                case KeyCode.Home: to = StepTree(nodes, -1, 1, 1); break;
                case KeyCode.End: to = StepTree(nodes, nodes.Count, -1, 1); break;
                default: return false;
            }
            if (to < 0)
            {
                return false;
            }
            SelectObject(nodes[to].Transform.gameObject);
            // The row is stepped to, not jumped to: no re-centring.
            _revealSelection = false;
            KeepInView(to, nodes.Count, _treeViewHeight, ref _scrollTree);
            return true;
        }

        // Up to count rows away in the given direction, skipping the scene
        // headings and objects that are gone. -1 when there is none.
        private static int StepTree(List<Node> nodes, int from, int direction, int count)
        {
            int found = -1;
            for (int i = from + direction; i >= 0 && i < nodes.Count && count > 0; i += direction)
            {
                if (nodes[i].Transform == null || !nodes[i].Transform)
                {
                    continue;
                }
                found = i;
                count--;
            }
            return found;
        }

        private static bool TreeOpenClose(List<Node> nodes, int at, bool open)
        {
            // Search results are a flat list: there is nothing to open there.
            if (_results != null || at < 0)
            {
                return false;
            }
            Node n = nodes[at];
            if (n.Transform == null || !n.Transform)
            {
                return false;
            }
            int id = n.Transform.GetInstanceID();
            bool expanded = Expanded.Contains(id);
            if (open)
            {
                if (n.HasChildren && !expanded)
                {
                    Expanded.Add(id);
                    _dirty = true;
                    return true;
                }
                // Already open: on to the first child.
                int child = StepTree(nodes, at, 1, 1);
                if (!n.HasChildren || child < 0 || nodes[child].Depth <= n.Depth)
                {
                    return false;
                }
                SelectObject(nodes[child].Transform.gameObject);
                _revealSelection = false;
                KeepInView(child, nodes.Count, _treeViewHeight, ref _scrollTree);
                return true;
            }
            if (n.HasChildren && expanded)
            {
                Expanded.Remove(id);
                _dirty = true;
                return true;
            }
            Transform parent = n.Transform.parent;
            if (parent == null)
            {
                return false;
            }
            SelectObject(parent.gameObject);
            _revealSelection = false;
            for (int i = at - 1; i >= 0; i--)
            {
                if (nodes[i].Transform != null && nodes[i].Transform && ReferenceEquals(nodes[i].Transform, parent))
                {
                    KeepInView(i, nodes.Count, _treeViewHeight, ref _scrollTree);
                    break;
                }
            }
            return true;
        }

        // ---- Objects: the folders and the objects in them --------------------------------

        private static bool ObjectsKey(KeyCode key)
        {
            List<ObjectRow> rows = _objectRows;
            if (rows == null || rows.Count == 0)
            {
                return false;
            }
            int at = -1;
            for (int i = 0; i < rows.Count; i++)
            {
                bool hit = _cursorFolder != null
                    ? rows[i].Entry == null && rows[i].Key == _cursorFolder
                    : rows[i].Entry != null && rows[i].Entry.Id == _selectedEntryId;
                if (hit)
                {
                    at = i;
                    break;
                }
            }
            if (key == KeyCode.LeftArrow || key == KeyCode.RightArrow)
            {
                return ObjectsOpenClose(rows, at, key == KeyCode.RightArrow);
            }
            int page = RowsPerPane(_objectsViewHeight);
            int to;
            switch (key)
            {
                case KeyCode.UpArrow: to = at < 0 ? rows.Count - 1 : at - 1; break;
                case KeyCode.DownArrow: to = at < 0 ? 0 : at + 1; break;
                case KeyCode.PageUp: to = at < 0 ? rows.Count - 1 : at - page; break;
                case KeyCode.PageDown: to = at < 0 ? 0 : at + page; break;
                case KeyCode.Home: to = 0; break;
                case KeyCode.End: to = rows.Count - 1; break;
                default: return false;
            }
            to = Mathf.Clamp(to, 0, rows.Count - 1);
            if (to == at)
            {
                return true;
            }
            GoToObjectRow(rows, to);
            return true;
        }

        private static void GoToObjectRow(List<ObjectRow> rows, int to)
        {
            ObjectRow r = rows[to];
            if (r.Entry == null)
            {
                _cursorFolder = r.Key;
            }
            else
            {
                // Selecting shows the details, which in a narrow window is the
                // other page; the keys stay where they are.
                int page = _page;
                SelectEntry(r.Entry);
                _page = page;
            }
            KeepInView(to, rows.Count, _objectsViewHeight, ref _scrollObjects);
        }

        private static bool ObjectsOpenClose(List<ObjectRow> rows, int at, bool open)
        {
            if (at < 0)
            {
                return false;
            }
            ObjectRow r = rows[at];
            bool searching = ObjectList.Query.Parse(_objectsSearch ?? "").Active;
            if (r.Entry == null)
            {
                bool expanded = searching ? !ClosedWhileSearching.Contains(r.Key) : OpenFolders.Contains(r.Key);
                if (open != expanded)
                {
                    ToggleFolder(r.Key, searching);
                    return true;
                }
                if (open)
                {
                    // Already open: on to the first row in it.
                    if (at + 1 >= rows.Count || rows[at + 1].Depth <= r.Depth)
                    {
                        return false;
                    }
                    GoToObjectRow(rows, at + 1);
                    return true;
                }
            }
            else if (open)
            {
                return false;
            }
            // Closed folder, or an object: up to the folder it is in.
            for (int i = at - 1; i >= 0; i--)
            {
                if (rows[i].Entry == null && rows[i].Depth < r.Depth)
                {
                    GoToObjectRow(rows, i);
                    return true;
                }
            }
            return false;
        }
    }
}
