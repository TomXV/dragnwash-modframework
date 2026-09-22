using DragNWash.ModFramework.ToolWindow;
using TW = global::DragNWash.ModFramework.ToolWindow.ToolWindow;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace DragNWash.ModFramework.Inspector
{
    // The Inspector tab's Rigidbodies view: pause/resume/step the physics,
    // a selected Rigidbody or Rigidbody2D's own controls (stop, kinematic,
    // sleep/wake), and the scene's or the selection's rigidbodies listed,
    // fastest first. In place of the members, like History.
    internal static partial class InspectorTab
    {
        private static bool _showBodies;
        private static Vector2 _scrollBodies;
        private static bool _bodiesSelectionOnly;
        private static bool _bodiesAwakeOnly;
        private static bool _bodiesFastestFirst = true;
        private static string _bodiesFilter = "";
        // The outcome of the last rigidbody button.
        private static string _bodyNote = "";
        // The physics pause and step, shared by the list and a selected body.
        private static void PhysicsButtons(ref float bx, ref float y, float x, float w, ToolWindowStyles s, float row)
        {
            string pause = InspectorBodies.Paused ? "Resume physics" : "Pause physics";
            if (FlowButton(ref bx, ref y, x, w, ButtonWidth(s, pause), pause, InspectorBodies.Paused, s, row))
            {
                _bodyNote = InspectorBodies.Paused ? InspectorBodies.Resume() : InspectorBodies.Pause();
            }
            if (InspectorBodies.Paused && FlowButton(ref bx, ref y, x, w, ButtonWidth(s, "Step"), "Step", false, s, row))
            {
                _bodyNote = InspectorBodies.Step();
            }
        }

        // A selected Rigidbody or Rigidbody2D: what it is doing, and buttons to stop it,
        // switch it kinematic, put it to sleep or wake it, and pause the physics.
        private static float DrawBodyControls(Component body, float x, float y, float w, ToolWindowStyles s, float row)
        {
            GUI.Label(new Rect(x, y, w, row), InspectorBodies.Describe(body), _accentCell);
            y += row;
            float bx = x;
            if (FlowButton(ref bx, ref y, x, w, ButtonWidth(s, "Stop"), "Stop", false, s, row))
            {
                _bodyNote = InspectorBodies.Stop(body);
            }
            bool kinematic = InspectorBodies.Kinematic(body);
            if (FlowButton(ref bx, ref y, x, w, ButtonWidth(s, "Kinematic"), "Kinematic", kinematic, s, row))
            {
                _bodyNote = InspectorBodies.SetKinematic(body, !kinematic, WhereLabel());
            }
            string sleep = InspectorBodies.Sleeping(body) ? "Wake" : "Sleep";
            if (FlowButton(ref bx, ref y, x, w, ButtonWidth(s, sleep), sleep, false, s, row))
            {
                _bodyNote = InspectorBodies.SleepOrWake(body);
            }
            PhysicsButtons(ref bx, ref y, x, w, s, row);
            y += row;
            if (!string.IsNullOrEmpty(_bodyNote))
            {
                GUI.Label(new Rect(x, y, w, row), Drawable(_bodyNote), _mutedCell);
                y += row;
            }
            return y + 4;
        }

        private static void DrawBodies(Rect pane, ToolWindowStyles s, float row)
        {
            TW.Fill(pane, TW.InsetColor);
            float x = pane.x + 4, y = pane.y + 2, w = pane.width - 8;
            List<Component> found = InspectorBodies.In(SelectedObject, _bodiesSelectionOnly);
            var shown = new List<Component>();
            foreach (Component c in found)
            {
                if (c == null) continue;
                if (_bodiesAwakeOnly && InspectorBodies.Sleeping(c)) continue;
                if (!string.IsNullOrEmpty(_bodiesFilter) && c.name.IndexOf(_bodiesFilter, StringComparison.OrdinalIgnoreCase) < 0 && c.GetType().Name.IndexOf(_bodiesFilter, StringComparison.OrdinalIgnoreCase) < 0) continue;
                shown.Add(c);
            }
            if (_bodiesFastestFirst)
            {
                shown.Sort((a, b) => InspectorBodies.Velocity(b).sqrMagnitude.CompareTo(InspectorBodies.Velocity(a).sqrMagnitude));
            }
            string where = _bodiesSelectionOnly ? (SelectedObject != null ? "under " + SelectedObject.name : "under the selection (nothing selected)") : "in the scene";
            GUI.Label(new Rect(x, y, w, row), Drawable($"Rigidbodies {where}: {shown.Count} of {found.Count} shown."), _mutedCell);
            y += row;
            if (!string.IsNullOrEmpty(_bodyNote))
            {
                GUI.Label(new Rect(x, y, w, row), Drawable(_bodyNote), _mutedCell);
                y += row;
            }
            float bx = x;
            string scope = _bodiesSelectionOnly ? "Selection's children" : "Whole scene";
            if (FlowButton(ref bx, ref y, x, w, ButtonWidth(s, "Selection's children"), scope, _bodiesSelectionOnly, s, row)) _bodiesSelectionOnly = !_bodiesSelectionOnly;
            if (FlowButton(ref bx, ref y, x, w, ButtonWidth(s, "Awake only"), "Awake only", _bodiesAwakeOnly, s, row)) _bodiesAwakeOnly = !_bodiesAwakeOnly;
            if (FlowButton(ref bx, ref y, x, w, ButtonWidth(s, "Fastest first"), "Fastest first", _bodiesFastestFirst, s, row)) _bodiesFastestFirst = !_bodiesFastestFirst;
            PhysicsButtons(ref bx, ref y, x, w, s, row);
            if (FlowButton(ref bx, ref y, x, w, ButtonWidth(s, "< Members"), "< Members", false, s, row)) _showBodies = false;
            y += row + 4;
            var filterRect = new Rect(x, y, w, row);
            _bodiesFilter = TW.FilterField(filterRect, _bodiesFilter, "Filter by name or type", s);
            y += row + 4;
            var view = new Rect(x, y, w, pane.yMax - y - 2);
            float inner = view.width - 20;
            float lineH = row * 2;
            TW.ApplyScroll(view, ref _scrollBodies);
            _scrollBodies = GUI.BeginScrollView(view, _scrollBodies, new Rect(0, 0, inner, Mathf.Max(view.height, shown.Count * lineH)), false, false);
            float ry = 0;
            foreach (Component c in shown)
            {
                if (ry + lineH >= _scrollBodies.y && ry <= _scrollBodies.y + view.height)
                {
                    bool selected = ReferenceEquals(_target, c);
                    GUI.Label(new Rect(0, ry, inner - 84, row), Drawable(c.name + "  (" + c.GetType().Name + ")"), selected ? _accentCell : _cell);
                    GUI.Label(new Rect(0, ry + row, inner - 84, row), InspectorBodies.Describe(c), _mutedCell);
                    if (GUI.Button(new Rect(inner - 78, ry + 2, 74, row - 4), "Select", s.Button))
                    {
                        Select(c);
                        // Selecting from the list keeps the list open, for going through them.
                        _showBodies = true;
                    }
                }
                ry += lineH;
            }
            GUI.EndScrollView();
        }
    }
}
