using System;
using System.Collections.Generic;
using DragNWash.ModFramework.Diagnostics;
using UnityEngine;

namespace DragNWash.ModFramework.ToolWindow
{
    // The "Memory" tab: the memory readings of the last minutes as bars, the
    // numbers now, where each managed thread is, and buttons for a garbage
    // collection and a snapshot. Experimental (Tool window 1.7).
    internal static class MemoryTab
    {
        internal const string Title = "Memory";

        private static bool _unity;
        private static bool _fiveMinutes = true;
        private static bool _allThreads;
        private static List<ThreadStack> _threads;
        private static string _threadsNote;
        private static string _result;
        private static Vector2 _stackScroll;
        private static GUIStyle _chipOn, _chipOff, _small, _mono, _value;

        internal static void Install()
        {
            ToolWindow.AddTab(ToolWindow.Guid, Title, Draw, 42);
        }

        private static void EnsureStyles(ToolWindowStyles s)
        {
            if (_chipOn != null) return;
            _chipOn = new GUIStyle(s.Label) { alignment = TextAnchor.MiddleCenter, wordWrap = false, padding = new RectOffset(8, 8, 0, 0) };
            _chipOff = new GUIStyle(s.MutedLabel) { alignment = TextAnchor.MiddleCenter, wordWrap = false, padding = new RectOffset(8, 8, 0, 0) };
            _small = new GUIStyle(s.MutedLabel) { wordWrap = false };
            _mono = new GUIStyle(s.LogLabel) { wordWrap = false, padding = new RectOffset(4, 4, 0, 0) };
            _value = new GUIStyle(s.Label) { wordWrap = false, alignment = TextAnchor.MiddleRight, clipping = TextClipping.Clip };
        }

        private static void Draw(Rect area)
        {
            ToolWindowStyles s = ToolWindow.Styles;
            EnsureStyles(s);
            float row = ToolWindow.RowHeight, pad = ToolWindow.Padding;
            float x = area.x + pad, y = area.y + pad, w = area.width - 2 * pad;

            // Which readings, and the two buttons.
            float bx = x;
            if (Chip(ref bx, y, "GC", !_unity)) _unity = false;
            if (Chip(ref bx, y, "Unity", _unity)) _unity = true;
            bx += 10;
            if (Chip(ref bx, y, "1 min", !_fiveMinutes)) _fiveMinutes = false;
            if (Chip(ref bx, y, "5 min", _fiveMinutes)) _fiveMinutes = true;
            if (GUI.Button(new Rect(area.xMax - pad - 90, y, 90, row), "Snapshot", s.Button))
            {
                _result = Run(() => Snapshot.Write(out _));
            }
            if (GUI.Button(new Rect(area.xMax - pad - 90 - 6 - 110, y, 110, row), "Collect now", s.Button))
            {
                _result = Run(MemoryWatch.CollectNow);
            }
            y += row + 6;
            if (!string.IsNullOrEmpty(_result))
            {
                var content = new GUIContent(ConsoleTab.Drawable(_result));
                float h = s.WrappedLabel.CalcHeight(content, w);
                GUI.Label(new Rect(x, y, w, h), content, s.WrappedLabel);
                y += h + 4;
            }

            // The bars: one a second, oldest on the left.
            IReadOnlyList<MemorySample> history = MemoryWatch.History;
            MemorySample latest = history.Count > 0 ? history[history.Count - 1] : null;
            var graph = new Rect(x, y, w, 120);
            ToolWindow.Fill(graph, ToolWindow.InsetColor);
            int slots = _fiveMinutes ? MemoryWatch.Capacity : 60;
            int first = Math.Max(0, history.Count - slots);
            long max = 1;
            for (int i = first; i < history.Count; i++)
            {
                max = Math.Max(max, _unity ? history[i].UnityReserved : history[i].GcReserved);
            }
            float inner = graph.width - 8, slot = inner / slots, baseY = graph.yMax - 4, top = graph.height - 22;
            for (int i = first; i < history.Count; i++)
            {
                MemorySample m = history[i];
                long value = _unity ? m.UnityAllocated : m.GcUsed;
                float h = Mathf.Max(1, top * value / max);
                float left = graph.xMax - 4 - (history.Count - i) * slot;
                Color c = !_unity && m.GcRan ? ToolWindow.WarningColor : ToolWindow.AccentColor;
                ToolWindow.Fill(new Rect(left, baseY - h, Mathf.Max(1, slot - (slot >= 3 ? 1 : 0)), h), c);
            }
            string caption = latest == null
                ? "Collecting: one reading a second while Developer tools are on."
                : _unity
                    ? $"Unity {MemoryWatch.Size(latest.UnityAllocated)} allocated / {MemoryWatch.Size(latest.UnityReserved)} reserved"
                    : $"GC used {MemoryWatch.Size(latest.GcUsed)} / reserved {MemoryWatch.Size(latest.GcReserved)}, {latest.Collections} collections";
            GUI.Label(new Rect(graph.x + 4, graph.y + 2, graph.width - 8, 18), caption, _small);
            string span = (_fiveMinutes ? "5 min" : "1 min") + " ago -> now";
            float spanW = _small.CalcSize(new GUIContent(span)).x;
            GUI.Label(new Rect(graph.xMax - 4 - spanW, graph.y + 2, spanW, 18), span, _small);
            y = graph.yMax + 4;

            // Legend: squares painted with Fill, like the console's toggles.
            float lx = x;
            Legend(ref lx, y, ToolWindow.AccentColor, _unity ? "Unity allocated" : "GC used");
            if (!_unity) Legend(ref lx, y, ToolWindow.WarningColor, "a GC ran that second");
            GUI.Label(new Rect(lx, y, x + w - lx, 18), "scale: the most reserved in view", _small);
            y += 22;

            // The numbers now, in two columns.
            if (latest != null)
            {
                var left = new List<(string, string)>
                {
                    ("GC used", MemoryWatch.Size(latest.GcUsed)),
                    ("GC reserved", MemoryWatch.Size(latest.GcReserved)),
                    ("Collections", latest.Collections.ToString()),
                    ("GC mode", MemoryWatch.GcMode() ?? "?"),
                    ("Last 60 s", LastMinute(history)),
                };
                var right = new List<(string, string)>
                {
                    ("Unity allocated", MemoryWatch.Size(latest.UnityAllocated)),
                    ("Unity reserved", MemoryWatch.Size(latest.UnityReserved)),
                    ("System used", MemoryWatch.Size(latest.SystemUsed)),
                    ("Audio", MemoryWatch.Size(latest.Audio)),
                    ("Video", MemoryWatch.Size(latest.Video)),
                };
                float half = (w - 16) / 2, line = row - 6;
                for (int i = 0; i < left.Count; i++)
                {
                    Pair(new Rect(x, y + i * line, half, line), left[i], s);
                    Pair(new Rect(x + half + 16, y + i * line, half, line), right[i], s);
                }
                y += left.Count * line + 8;
            }

            // Managed threads.
            GUI.Label(new Rect(x, y, 130, row), "Managed threads", _small);
            bx = x + 130;
            if (Chip(ref bx, y, "main", !_allThreads)) _allThreads = false;
            if (Chip(ref bx, y, _threads != null ? $"all ({_threads.Count})" : "all", _allThreads)) _allThreads = true;
            bool refresh = GUI.Button(new Rect(area.xMax - pad - 90, y, 90, row), "Refresh", s.Button);
            if (refresh || (_threads == null && _threadsNote == null))
            {
                _threads = ManagedStacks.Capture(out _threadsNote);
                _threadsNote = _threads == null ? "Managed stacks could not be read: " + _threadsNote : "";
                _stackScroll = Vector2.zero;
            }
            y += row + 6;

            var box = new Rect(x, y, w, Mathf.Max(60, area.yMax - pad - y));
            ToolWindow.Fill(box, ToolWindow.InsetColor);
            var lines = new List<string>();
            if (_threads == null)
            {
                lines.Add(_threadsNote ?? "");
            }
            else
            {
                var empty = new List<ThreadStack>();
                foreach (ThreadStack t in _threads)
                {
                    if (!_allThreads && !t.IsMain) continue;
                    if (t.Frames.Count == 0 && !t.IsMain)
                    {
                        empty.Add(t);
                        continue;
                    }
                    lines.Add(ManagedStacks.Label(t));
                    foreach (string f in t.Frames) lines.Add("  at " + f);
                    if (t.Frames.Count == 0) lines.Add("  (no managed frames)");
                }
                if (empty.Count > 0) lines.Add(ManagedStacks.EmptyLine(empty));
            }
            float lh = row - 8, width = box.width - 20;
            foreach (string l in lines) width = Mathf.Max(width, _mono.CalcSize(new GUIContent(l)).x + 8);
            _stackScroll = GUI.BeginScrollView(box, _stackScroll, new Rect(0, 0, width, Mathf.Max(box.height, lines.Count * lh + 6)), false, false);
            for (int i = 0; i < lines.Count; i++)
            {
                float ly = 3 + i * lh;
                if (ly + lh < _stackScroll.y || ly > _stackScroll.y + box.height) continue;
                GUI.Label(new Rect(0, ly, width, lh), ConsoleTab.Drawable(lines[i]), _mono);
            }
            GUI.EndScrollView();
        }

        private static string Run(Func<string> action)
        {
            try
            {
                return action();
            }
            catch (Exception ex)
            {
                ToolWindowPlugin.Log.LogError("[memory] " + ex);
                return "Failed: " + ex.Message;
            }
        }

        private static string LastMinute(IReadOnlyList<MemorySample> h)
        {
            if (h.Count < 2) return "-";
            int from = h.Count - 1, ran = 0;
            while (from > 0 && (h[h.Count - 1].Time - h[from - 1].Time).TotalSeconds <= 60) from--;
            for (int i = from + 1; i < h.Count; i++) if (h[i].GcRan) ran++;
            long change = h[h.Count - 1].GcUsed - h[from].GcUsed;
            return $"{(change >= 0 ? "+" : "-")}{MemoryWatch.Size(Math.Abs(change))}, {ran} GC{(ran == 1 ? "" : "s")}";
        }

        // A toggle drawn like the console's: panel, a bar along the top when on.
        private static bool Chip(ref float x, float y, string label, bool on)
        {
            float row = ToolWindow.RowHeight;
            float width = Mathf.Max(56, _chipOn.CalcSize(new GUIContent(label)).x);
            var r = new Rect(x, y, width, row);
            ToolWindow.Fill(r, ToolWindow.PanelColor);
            if (on) ToolWindow.Fill(new Rect(r.x, r.y, r.width, 3), ToolWindow.AccentColor);
            bool clicked = GUI.Button(r, label, on || r.Contains(Event.current.mousePosition) ? _chipOn : _chipOff);
            x += width + 4;
            return clicked;
        }

        private static void Legend(ref float x, float y, Color color, string label)
        {
            ToolWindow.Fill(new Rect(x, y + 5, 10, 10), color);
            float width = _small.CalcSize(new GUIContent(label)).x;
            GUI.Label(new Rect(x + 14, y, width, 18), label, _small);
            x += 14 + width + 14;
        }

        private static void Pair(Rect r, (string label, string value) p, ToolWindowStyles s)
        {
            GUI.Label(r, p.label, _small);
            float lw = _small.CalcSize(new GUIContent(p.label)).x + 8;
            GUI.Label(new Rect(r.x + lw, r.y, r.width - lw, r.height), p.value, _value);
        }
    }
}
