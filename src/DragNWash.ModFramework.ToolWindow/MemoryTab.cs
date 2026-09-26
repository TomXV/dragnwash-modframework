using System;
using System.Collections.Generic;
using DragNWash.ModFramework.Diagnostics;
using UnityEngine;

namespace DragNWash.ModFramework.ToolWindow
{
    // The "Memory" tab, in four views: Overview (the memory readings of the
    // last minutes as bars, the numbers now, a garbage collection and a
    // snapshot), Threads (where each thread is, native frames too on Windows),
    // Sampler (where the main thread spends its time) and Types (managed
    // objects by type, experimental). Experimental (Tool window 1.7).
    internal static class MemoryTab
    {
        internal const string Title = "Memory";

        private enum View { Overview, Threads, Sampler, Types }

        private static readonly Color NativeColor = new Color(0.79f, 0.64f, 1f);

        private static View _view;
        private static bool _unity;
        private static bool _fiveMinutes = true;
        private static bool _allThreads, _nativeThreads;
        private static List<ThreadStack> _threads;
        private static string _threadsNote;
        private static string _result;
        private static Vector2 _stackScroll, _samplerScroll, _typesScroll;
        private static int _rate = 10;
        private static bool _samplerNative, _byTotal, _samplerMods;
        private static string _selectedFrame;
        private static List<SampleRow> _rows, _callers;
        private static string _reportKey;
        private static float _reportAt;
        private static bool _typesMods;
        private static string _typesNote;
        private static GUIStyle _chipOn, _chipOff, _small, _mono, _nativeMono, _value, _warn;

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
            _nativeMono = new GUIStyle(_mono);
            _nativeMono.normal.textColor = NativeColor;
            _value = new GUIStyle(s.Label) { wordWrap = false, alignment = TextAnchor.MiddleRight, clipping = TextClipping.Clip };
            _warn = new GUIStyle(s.WrappedLabel) { padding = new RectOffset(10, 8, 4, 4) };
            _warn.normal.textColor = new Color(0.95f, 0.89f, 0.72f);
        }

        private static void Draw(Rect area)
        {
            ToolWindowStyles s = ToolWindow.Styles;
            EnsureStyles(s);
            float row = ToolWindow.RowHeight, pad = ToolWindow.Padding;
            float x = area.x + pad, y = area.y + pad;
            float bx = x;
            if (Chip(ref bx, y, "Overview", _view == View.Overview)) _view = View.Overview;
            if (Chip(ref bx, y, "Threads", _view == View.Threads)) _view = View.Threads;
            if (Chip(ref bx, y, "Sampler", _view == View.Sampler)) _view = View.Sampler;
            if (Chip(ref bx, y, "Types", _view == View.Types)) _view = View.Types;
            if (MainThreadSampler.Running)
            {
                GUI.Label(new Rect(bx + 8, y, area.xMax - pad - bx - 8, row), "sampling", _small);
            }
            y += row + 8;
            var rest = new Rect(area.x, y - pad, area.width, area.yMax - y + pad);
            switch (_view)
            {
                case View.Threads: DrawThreads(rest, s); break;
                case View.Sampler: DrawSampler(rest, s); break;
                case View.Types: DrawTypes(rest, s); break;
                default: DrawOverview(rest, s); break;
            }
        }

        // ---- Overview ------------------------------------------------------------

        private static void DrawOverview(Rect area, ToolWindowStyles s)
        {
            float row = ToolWindow.RowHeight, pad = ToolWindow.Padding;
            float x = area.x + pad, y = area.y + pad, w = area.width - 2 * pad;

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

            float lx = x;
            Legend(ref lx, y, ToolWindow.AccentColor, _unity ? "Unity allocated" : "GC used");
            if (!_unity) Legend(ref lx, y, ToolWindow.WarningColor, "a GC ran that second");
            GUI.Label(new Rect(lx, y, x + w - lx, 18), "scale: the most reserved in view", _small);
            y += 22;

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
                    Pair(new Rect(x, y + i * line, half, line), left[i]);
                    Pair(new Rect(x + half + 16, y + i * line, half, line), right[i]);
                }
            }
        }

        // ---- Threads -------------------------------------------------------------

        private static void DrawThreads(Rect area, ToolWindowStyles s)
        {
            float row = ToolWindow.RowHeight, pad = ToolWindow.Padding;
            float x = area.x + pad, y = area.y + pad, w = area.width - 2 * pad;
            float bx = x;
            bool changed = false;
            if (Chip(ref bx, y, "main", !_allThreads)) _allThreads = false;
            if (Chip(ref bx, y, _threads != null ? $"all ({_threads.Count})" : "all", _allThreads)) _allThreads = true;
            if (NativeStacks.Available && Chip(ref bx, y, "Native", _nativeThreads))
            {
                _nativeThreads = !_nativeThreads;
                changed = true;
            }
            bool refresh = GUI.Button(new Rect(area.xMax - pad - 90, y, 90, row), "Refresh", s.Button);
            if (refresh || changed || (_threads == null && _threadsNote == null))
            {
                string why;
                _threads = _nativeThreads ? NativeStacks.CaptureAll(out why) : ManagedStacks.Capture(out why);
                _threadsNote = _threads == null ? "Stacks could not be read: " + why : "";
                _stackScroll = Vector2.zero;
            }
            y += row + 6;
            if (_nativeThreads)
            {
                GUI.Label(new Rect(x, y, w, 18), "Native names come from the game's PDB files; the first Refresh can take a few seconds.", _small);
                y += 22;
            }

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
            ListBox(new Rect(x, y, w, Mathf.Max(60, area.yMax - pad - y)), lines, ref _stackScroll);
        }

        // ---- Sampler -------------------------------------------------------------

        private static void DrawSampler(Rect area, ToolWindowStyles s)
        {
            float row = ToolWindow.RowHeight, pad = ToolWindow.Padding;
            float x = area.x + pad, y = area.y + pad, w = area.width - 2 * pad;
            bool running = MainThreadSampler.Running;
            if (GUI.Button(new Rect(x, y, 70, row), running ? "Restart" : "Start", s.Button))
            {
                MainThreadSampler.Start(_rate, _samplerNative);
                _selectedFrame = null;
            }
            if (GUI.Button(new Rect(x + 76, y, 70, row), "Stop", s.Button)) MainThreadSampler.Stop();
            float bx = x + 156;
            if (Chip(ref bx, y, "5 /s", _rate == 5)) _rate = 5;
            if (Chip(ref bx, y, "10 /s", _rate == 10)) _rate = 10;
            if (NativeStacks.Available && Chip(ref bx, y, "Native", _samplerNative)) _samplerNative = !_samplerNative;
            int count = MainThreadSampler.Count;
            string status = count == 0 ? (running ? "starting" : "not sampled yet")
                : $"{count} samples, {(DateTime.Now - MainThreadSampler.Started).TotalSeconds:0} s{(MainThreadSampler.Native ? ", native" : "")}";
            float sw = _small.CalcSize(new GUIContent(status)).x;
            GUI.Label(new Rect(area.xMax - pad - sw, y, sw, row), status, _small);
            y += row + 6;
            bx = x;
            if (Chip(ref bx, y, "Self", !_byTotal)) _byTotal = false;
            if (Chip(ref bx, y, "Total", _byTotal)) _byTotal = true;
            if (Chip(ref bx, y, "Mods only", _samplerMods)) _samplerMods = !_samplerMods;
            y += row + 6;

            // Counted again at most twice a second: the window draws several times a frame.
            string key = $"{count}|{_byTotal}|{_samplerMods}|{_selectedFrame}";
            if (key != _reportKey && (Time.unscaledTime - _reportAt > 0.5f || _rows == null || count == 0))
            {
                _reportKey = key;
                _reportAt = Time.unscaledTime;
                _rows = count > 0 ? MainThreadSampler.Report(_byTotal, _samplerMods, 40) : new List<SampleRow>();
                _callers = _selectedFrame != null ? MainThreadSampler.Callers(_selectedFrame) : null;
            }
            List<SampleRow> rows = _rows;
            List<SampleRow> callers = _selectedFrame != null ? _callers : null;
            float callersHeight = callers != null ? 22 + Mathf.Min(8, Mathf.Max(1, callers.Count)) * (row - 8) + 8 : 0;
            var box = new Rect(x, y, w, Mathf.Max(60, area.yMax - pad - y - callersHeight));
            ToolWindow.Fill(box, ToolWindow.InsetColor);
            float lh = row - 6, colW = 58;
            GUI.Label(new Rect(box.x + 4, box.y + 2, box.width - 2 * colW - 24, lh), count == 0 ? "Start samples where the main thread is, a few times a second." : "Method", _small);
            GUI.Label(new Rect(box.xMax - 2 * colW - 16, box.y + 2, colW, lh), "Self", _small);
            GUI.Label(new Rect(box.xMax - colW - 16, box.y + 2, colW, lh), "Total", _small);
            var view = new Rect(box.x, box.y + lh + 4, box.width, box.height - lh - 4);
            _samplerScroll = GUI.BeginScrollView(view, _samplerScroll, new Rect(0, 0, view.width - 16, Mathf.Max(view.height, rows.Count * lh)), false, false);
            for (int i = 0; i < rows.Count; i++)
            {
                SampleRow r = rows[i];
                var line = new Rect(0, i * lh, view.width - 16, lh);
                float share = (float)(_byTotal ? r.Total : r.Self) / count;
                ToolWindow.Fill(new Rect(line.x, line.y + 2, (line.width - 2 * colW) * share, lh - 4), new Color(0.32f, 0.78f, 0.72f, 0.2f));
                if (r.Frame == _selectedFrame) ToolWindow.Fill(new Rect(line.x, line.y, 2, lh), ToolWindow.AccentColor);
                if (GUI.Button(new Rect(line.x + 4, line.y, line.width - 2 * colW - 8, lh), ConsoleTab.Drawable(r.Frame), NativeStacks.IsNative(r.Frame) ? _nativeMono : _mono))
                {
                    _selectedFrame = r.Frame == _selectedFrame ? null : r.Frame;
                }
                GUI.Label(new Rect(line.xMax - 2 * colW, line.y, colW - 4, lh), $"{100.0 * r.Self / count:0.0}%", _value);
                GUI.Label(new Rect(line.xMax - colW, line.y, colW - 4, lh), $"{100.0 * r.Total / count:0.0}%", _value);
            }
            GUI.EndScrollView();
            if (callers != null)
            {
                float cy = box.yMax + 6;
                GUI.Label(new Rect(x, cy, w, 18), "Called from (for the selected row):", _small);
                cy += 22;
                int total = 0;
                foreach (SampleRow c in callers) total += c.Total;
                foreach (SampleRow c in callers)
                {
                    GUI.Label(new Rect(x, cy, w - colW - 8, row - 8), "  " + ConsoleTab.Drawable(c.Frame), NativeStacks.IsNative(c.Frame) ? _nativeMono : _mono);
                    GUI.Label(new Rect(x + w - colW, cy, colW, row - 8), $"{100.0 * c.Total / Math.Max(1, total):0}%", _value);
                    cy += row - 8;
                }
            }
        }

        // ---- Types ---------------------------------------------------------------

        private static void DrawTypes(Rect area, ToolWindowStyles s)
        {
            float row = ToolWindow.RowHeight, pad = ToolWindow.Padding;
            float x = area.x + pad, y = area.y + pad, w = area.width - 2 * pad;
            var warning = new GUIContent("Experimental: counting stops the whole game while it walks the managed heap (a tenth of a second to a second). Save first.");
            float wh = _warn.CalcHeight(warning, w);
            ToolWindow.Fill(new Rect(x, y, w, wh), new Color(0.23f, 0.18f, 0.07f));
            ToolWindow.Fill(new Rect(x, y, 3, wh), ToolWindow.WarningColor);
            GUI.Label(new Rect(x, y, w, wh), warning, _warn);
            y += wh + 8;

            if (GUI.Button(new Rect(x, y, 110, row), "Count now", s.Button))
            {
                _typesNote = null;
                try
                {
                    HeapTypes.Count();
                }
                catch (Exception ex)
                {
                    _typesNote = "Could not count: " + ex.Message;
                    ToolWindowPlugin.Log.LogError("[memory] " + ex);
                }
            }
            float bx = x + 118;
            if (Chip(ref bx, y, "Mods only", _typesMods)) _typesMods = !_typesMods;
            HeapCount c = HeapTypes.Last;
            string status = _typesNote ?? (c == null ? (HeapTypes.Available ? "Not counted yet." : "This Mono cannot count.")
                : $"{c.Time:HH:mm:ss}, {c.Objects:N0} objects, {MemoryWatch.Size(c.Bytes)}, stopped {c.Seconds * 1000:0} ms{(c.Truncated ? ", too many to note all" : "")}");
            float sw = Mathf.Min(w - (bx - x), _small.CalcSize(new GUIContent(status)).x);
            GUI.Label(new Rect(area.xMax - pad - sw, y, sw, row), status, _small);
            y += row + 6;

            var box = new Rect(x, y, w, Mathf.Max(60, area.yMax - pad - y));
            ToolWindow.Fill(box, ToolWindow.InsetColor);
            float lh = row - 6, cw = 80;
            GUI.Label(new Rect(box.x + 4, box.y + 2, box.width - 3 * cw - 24, lh), "Type (reachable from static fields)", _small);
            GUI.Label(new Rect(box.xMax - 3 * cw - 16, box.y + 2, cw - 4, lh), "Count", _value);
            GUI.Label(new Rect(box.xMax - 2 * cw - 16, box.y + 2, cw - 4, lh), "Bytes", _value);
            GUI.Label(new Rect(box.xMax - cw - 16, box.y + 2, cw - 4, lh), "vs last", _value);
            var types = new List<TypeCount>();
            if (c != null)
            {
                foreach (TypeCount t in c.Types)
                {
                    if (_typesMods && !HeapTypes.IsModAssembly(t.Assembly)) continue;
                    types.Add(t);
                    if (types.Count >= 50) break;
                }
            }
            var view = new Rect(box.x, box.y + lh + 4, box.width, box.height - lh - 4);
            _typesScroll = GUI.BeginScrollView(view, _typesScroll, new Rect(0, 0, view.width - 16, Mathf.Max(view.height, types.Count * lh)), false, false);
            for (int i = 0; i < types.Count; i++)
            {
                TypeCount t = types[i];
                float ly = i * lh, right = view.width - 16;
                GUI.Label(new Rect(4, ly, right - 3 * cw - 8, lh), ConsoleTab.Drawable(t.Name), _mono);
                GUI.Label(new Rect(right - 3 * cw, ly, cw - 4, lh), t.Count.ToString("N0"), _value);
                GUI.Label(new Rect(right - 2 * cw, ly, cw - 4, lh), MemoryWatch.Size(t.Bytes), _value);
                string change = t.Change.HasValue ? (t.Change.Value > 0 ? "+" : "") + t.Change.Value.ToString("N0") : "";
                GUI.Label(new Rect(right - cw, ly, cw - 4, lh), change, _value);
            }
            GUI.EndScrollView();
        }

        // ---- shared --------------------------------------------------------------

        private static void ListBox(Rect box, List<string> lines, ref Vector2 scroll)
        {
            ToolWindow.Fill(box, ToolWindow.InsetColor);
            float lh = ToolWindow.RowHeight - 8, width = box.width - 20;
            foreach (string l in lines) width = Mathf.Max(width, _mono.CalcSize(new GUIContent(l)).x + 8);
            scroll = GUI.BeginScrollView(box, scroll, new Rect(0, 0, width, Mathf.Max(box.height, lines.Count * lh + 6)), false, false);
            for (int i = 0; i < lines.Count; i++)
            {
                float ly = 3 + i * lh;
                if (ly + lh < scroll.y || ly > scroll.y + box.height) continue;
                GUI.Label(new Rect(0, ly, width, lh), ConsoleTab.Drawable(lines[i]), NativeStacks.IsNative(lines[i]) ? _nativeMono : _mono);
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

        private static void Pair(Rect r, (string label, string value) p)
        {
            GUI.Label(r, p.label, _small);
            float lw = _small.CalcSize(new GUIContent(p.label)).x + 8;
            GUI.Label(new Rect(r.x + lw, r.y, r.width - lw, r.height), p.value, _value);
        }
    }
}
