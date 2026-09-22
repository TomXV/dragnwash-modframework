using DragNWash.ModFramework.ToolWindow;
using TW = global::DragNWash.ModFramework.ToolWindow.ToolWindow;
using System.Collections.Generic;
using UnityEngine;

namespace DragNWash.ModFramework.Inspector
{
    // The Inspector tab's History view: every edit this session, newest
    // first, with Revert/Redo and Copy, Undo last and Clear. In place of
    // the members, like Rigidbodies; the Export as overrides form opens here.
    internal static partial class InspectorTab
    {
        private static bool _showHistory;
        private static Vector2 _scrollHistory;
        private static void DrawHistory(Rect pane, ToolWindowStyles s, float row)
        {
            TW.Fill(pane, TW.InsetColor);
            float x = pane.x + 4, y = pane.y + 2, w = pane.width - 8;
            GUI.Label(new Rect(x, y, w, row), $"History: {InspectorHistory.Count} edit(s) this session, newest first. Nothing is saved.", _mutedCell);
            y += row;
            float bx = x;
            if (GUI.Button(new Rect(bx, y, 100, row), "Undo last", s.Button))
            {
                _status = InspectorHistory.Undo();
            }
            bx += 108;
            if (GUI.Button(new Rect(bx, y, 70, row), "Clear", s.Button))
            {
                InspectorHistory.Clear();
            }
            bx += 78;
            if (GUI.Button(new Rect(bx, y, 120, row), "< Members", s.Button))
            {
                _showHistory = false;
            }
            bx += 128;
            if (InspectorExport.Available && InspectorHistory.Count > 0 && GUI.Button(new Rect(bx, y, 170, row), "Export as overrides", _exporting ? s.SelectedButton : s.Button))
            {
                _exporting = !_exporting;
                _exportNote = "";
            }
            y += row + 4;
            if (_exporting)
            {
                y = DrawExportForm(x, y, w, s, row);
            }
            var view = new Rect(x, y, w, pane.yMax - y - 2);
            IReadOnlyList<InspectorHistory.Entry> all = InspectorHistory.All;
            float inner = view.width - 20;
            float lineH = row * 2;
            TW.ApplyScroll(view, ref _scrollHistory);
            _scrollHistory = GUI.BeginScrollView(view, _scrollHistory, new Rect(0, 0, inner, Mathf.Max(view.height, all.Count * lineH)), false, false);
            float ry = 0;
            for (int i = all.Count - 1; i >= 0; i--)
            {
                InspectorHistory.Entry e = all[i];
                if (ry + lineH >= _scrollHistory.y && ry <= _scrollHistory.y + view.height)
                {
                    GUIStyle nameStyle = e.Reverted ? _mutedCell : _cell;
                    string by = e.By != null ? "  by " + e.By : "";
                    GUI.Label(new Rect(0, ry, inner - 160, row), Drawable($"{e.Time:HH:mm:ss}  {e.Member}{by}" + (e.Reverted ? "  (put back)" : "")), nameStyle);
                    GUI.Label(new Rect(0, ry + row, inner - 160, row), Drawable($"{e.Label}:  {InspectorModel.Format(e.Before)}  ->  {InspectorModel.Format(e.After)}"), _mutedCell);
                    // A change another mod made can be put back, but not made
                    // again from here: it is the mod's to repeat.
                    bool canPress = e.Undo == null || !e.Reverted;
                    if (canPress && GUI.Button(new Rect(inner - 154, ry + 2, 72, row - 4), e.Reverted ? "Redo" : "Revert", s.Button))
                    {
                        bool redo = e.Reverted;
                        string problem = redo ? InspectorHistory.Reapply(e) : InspectorHistory.Revert(e);
                        _status = problem != null
                            ? $"{(redo ? "Redo" : "Revert")} of {e.Member} failed: {problem}"
                            : $"{(redo ? "Reapplied" : "Reverted")} {e.Member} = {InspectorModel.Format(redo ? e.After : e.Before)}";
                        InspectorPlugin.Log.LogInfo($"[inspector] {_status}");
                    }
                    if (GUI.Button(new Rect(inner - 76, ry + 2, 72, row - 4), "Copy", s.Button))
                    {
                        GUIUtility.systemCopyBuffer = $"{e.Label} {e.Member} = {InspectorModel.Format(e.After)}";
                    }
                }
                ry += lineH;
            }
            GUI.EndScrollView();
        }
    }
}
