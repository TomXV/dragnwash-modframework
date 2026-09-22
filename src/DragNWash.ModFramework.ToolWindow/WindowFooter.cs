using System;
using System.Collections.Generic;
using UnityEngine;

namespace DragNWash.ModFramework.ToolWindow
{
    // The bottom of the window and what lies over the body: the notice strip
    // (ShowNotice: one line with a colour bar for its kind, timed notices
    // clearing themselves and the next one waiting its turn), the hint line
    // under it (the keys, unless something has a better thing to say there),
    // and the busy overlay a tab puts over its own body (ToolWindow.Busy).
    internal static class WindowFooter
    {
        internal const float HintHeight = 22f;
        internal const float NoticeHeight = 28f;
        private const int QueueLimit = 8;

        private sealed class Notice
        {
            public string Text;
            public NoticeKind Kind;
            public float Seconds;
            // Set when it first shows, so time only runs while it can be read.
            public float Until = -1f;
            public bool Timed => Seconds > 0f;
        }

        private static readonly object Lock = new object();
        private static Notice _current;
        private static readonly List<Notice> Queue = new List<Notice>();

        // ---- notices ------------------------------------------------------------------

        internal static void Show(string text, NoticeKind kind, float seconds)
        {
            lock (Lock)
            {
                if (string.IsNullOrEmpty(text))
                {
                    // An empty notice takes back the one showing, as it always did.
                    _current = null;
                    Promote();
                    return;
                }
                // The same notice again (a tab that says it on every draw): its
                // time starts over, and no copy waits behind it.
                if (_current != null && _current.Text == text && _current.Kind == kind)
                {
                    _current.Seconds = seconds;
                    _current.Until = -1f;
                    return;
                }
                if (Queue.Exists(n => n.Text == text && n.Kind == kind))
                {
                    return;
                }
                var notice = new Notice { Text = text, Kind = kind, Seconds = seconds };
                if (_current == null || (!_current.Timed && _current.Kind != NoticeKind.Error))
                {
                    // Nothing showing, or a notice that only waits to be replaced.
                    _current = notice;
                }
                else if (kind == NoticeKind.Error && _current.Kind != NoticeKind.Error)
                {
                    // An error goes first; what it pushed aside comes back after it.
                    if (_current.Timed)
                    {
                        _current.Until = -1f;
                        Queue.Insert(0, _current);
                    }
                    _current = notice;
                }
                else
                {
                    Queue.Add(notice);
                    if (Queue.Count > QueueLimit)
                    {
                        Queue.RemoveAt(0);
                    }
                }
            }
        }

        // The tab changed, or a tab was turned back on: its notices are done.
        internal static void Clear()
        {
            lock (Lock)
            {
                _current = null;
                Queue.Clear();
            }
        }

        private static void Promote()
        {
            if (_current == null && Queue.Count > 0)
            {
                _current = Queue[0];
                Queue.RemoveAt(0);
            }
        }

        private static void Dismiss()
        {
            lock (Lock)
            {
                _current = null;
                Promote();
            }
        }

        internal static bool HasNotice
        {
            get { lock (Lock) { return _current != null; } }
        }

        // After a repaint: a timed notice that ran out gives way to the next.
        // Only here, so a frame's layout and paint see the same footer.
        internal static void Tick()
        {
            lock (Lock)
            {
                if (_current != null && _current.Timed && _current.Until >= 0f && Time.realtimeSinceStartup >= _current.Until)
                {
                    _current = null;
                    Promote();
                }
            }
        }

        internal static Color ColorOf(NoticeKind kind)
        {
            return kind == NoticeKind.Error ? ToolWindow.ErrorColor : kind == NoticeKind.Warning ? ToolWindow.WarningColor : ToolWindow.AccentColor;
        }

        // ---- hint line ----------------------------------------------------------------

        // What the hint line says this draw instead of the keys: a tab's hint
        // (bright), or the window's own word while it waits (muted).
        private static string _hint;
        private static bool _hintBright;

        internal static void BeginDraw()
        {
            _hint = null;
            _hintBright = false;
        }

        internal static void SetHint(string text, bool bright)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }
            _hint = text;
            _hintBright = bright;
        }

        // ---- busy ---------------------------------------------------------------------

        private static string _busyWhat, _busyDetail;
        private static int _busyFrame = -10;
        private static object _busyTab;

        internal static void Busy(string what, string detail, object tab)
        {
            _busyWhat = string.IsNullOrEmpty(what) ? "Working..." : what;
            _busyDetail = detail;
            _busyFrame = Time.frameCount;
            _busyTab = tab;
        }

        // A tab says it is busy on every draw; the frame after it stops saying
        // so, it is not. Only for the tab that said it.
        internal static bool IsBusy(object tab)
        {
            return tab != null && ReferenceEquals(tab, _busyTab) && Time.frameCount - _busyFrame <= 1;
        }

        // ---- drawing ------------------------------------------------------------------

        // Where the notice strip and hint line go in a window this size, and
        // how far down the body may reach.
        internal static float BodyBottom(float width, float height, out Rect notice, out Rect hint)
        {
            hint = new Rect(ToolWindow.Padding, height - 6 - HintHeight, width - ToolWindow.Padding - 38, HintHeight);
            notice = new Rect(ToolWindow.Padding, hint.y - 2 - NoticeHeight, width - ToolWindow.Padding * 2, NoticeHeight);
            return (HasNotice ? notice.y : hint.y) - 8;
        }

        // The notice while the pointer is on it and its text did not fit: the
        // whole text, over the bottom of the body. Kept from the last paint,
        // for the input of the next events.
        private static Rect _expanded;

        // Input over what the footer draws on top of the body, before the tab
        // sees it: a click on the notice dismisses it. True when the event was
        // the footer's.
        internal static bool HandleInput(Event ev, Rect noticeRect)
        {
            if (!HasNotice)
            {
                _expanded = Rect.zero;
                return false;
            }
            bool over = noticeRect.Contains(ev.mousePosition) || (_expanded.width > 0 && _expanded.Contains(ev.mousePosition));
            if (!over)
            {
                return false;
            }
            if (ev.type == EventType.MouseDown && ev.button == 0)
            {
                Dismiss();
                _expanded = Rect.zero;
                ev.Use();
                return true;
            }
            if (ev.isMouse || ev.type == EventType.ScrollWheel || ev.type == EventType.ContextClick)
            {
                ev.Use();
                return true;
            }
            return false;
        }

        // True when the pointer is on something the footer paints over the body.
        internal static bool Covers(Vector2 point)
        {
            return _expanded.width > 0 && _expanded.Contains(point);
        }

        internal static void DrawNotice(Rect strip, ToolWindowStyles s)
        {
            Notice n;
            int waiting;
            lock (Lock)
            {
                n = _current;
                waiting = Queue.Count;
            }
            if (n == null)
            {
                _expanded = Rect.zero;
                return;
            }
            Event ev = Event.current;
            if (n.Timed && n.Until < 0f && ev.type == EventType.Repaint)
            {
                n.Until = Time.realtimeSinceStartup + n.Seconds;
            }
            string info = waiting > 0 ? $"{waiting} more" : "";
            if (n.Timed && n.Until >= 0f)
            {
                int left = Mathf.Max(1, Mathf.CeilToInt(n.Until - Time.realtimeSinceStartup));
                info += (info.Length > 0 ? "    " : "") + left + " s";
            }
            float infoWidth = info.Length > 0 ? s.SmallMuted.CalcSize(new GUIContent(info)).x + 10 : 0f;
            float textX = strip.x + 3 + 10, textWidth = strip.width - 3 - 20 - infoWidth;
            string shown = ToolWindow.ElideText(n.Text, s.Label, textWidth);
            bool cut = shown != n.Text;

            bool hovered = strip.Contains(ev.mousePosition) || (_expanded.width > 0 && _expanded.Contains(ev.mousePosition));
            Rect box = strip;
            if (hovered && cut)
            {
                // The whole text, growing upwards over the body.
                float h = Mathf.Min(s.WrappedText.CalcHeight(new GUIContent(n.Text), textWidth) + 10, NoticeHeight * 4);
                box = new Rect(strip.x, strip.yMax - Mathf.Max(NoticeHeight, h), strip.width, Mathf.Max(NoticeHeight, h));
            }
            if (ev.type == EventType.Repaint)
            {
                _expanded = box.height > strip.height ? box : Rect.zero;
            }
            if (hovered)
            {
                SetHint("Click the notice to dismiss it.", true);
            }
            ToolWindow.Fill(box, ToolWindow.PanelColor);
            ToolWindow.Fill(new Rect(box.x, box.y, 3, box.height), ColorOf(n.Kind));
            if (box.height > strip.height)
            {
                GUI.Label(new Rect(textX, box.y + 5, textWidth, box.height - 10), n.Text, s.WrappedText);
            }
            else
            {
                GUI.Label(new Rect(textX, strip.y, textWidth, strip.height), shown, s.Label);
            }
            if (info.Length > 0)
            {
                GUI.Label(new Rect(strip.xMax - infoWidth, strip.y, infoWidth, strip.height), info, s.SmallMuted);
            }
        }

        internal static void DrawHint(Rect line, string keys, ToolWindowStyles s)
        {
            string text = _hint ?? keys;
            GUIStyle style = _hint != null && _hintBright ? s.Label : s.MutedLabel;
            GUI.Label(line, ToolWindow.ElideText(text, style, line.width), style);
        }

        private static readonly string Spinner = "|/-\\";

        // The body dimmed, and a small panel in its middle saying what runs.
        internal static void DrawBusy(Rect body, ToolWindowStyles s)
        {
            ToolWindow.Fill(body, new Color(0.024f, 0.031f, 0.047f, 0.62f));
            float pw = Mathf.Min(380f, body.width - 24), ph = 112f;
            var panel = new Rect(body.x + (body.width - pw) / 2, body.y + Mathf.Max(0, (body.height - ph) / 2), pw, ph);
            ToolWindow.Fill(new Rect(panel.x - 1, panel.y - 1, panel.width + 2, panel.height + 2), new Color(0.165f, 0.2f, 0.26f));
            ToolWindow.Fill(panel, ToolWindow.PanelColor);
            char spin = Spinner[(int)(Time.realtimeSinceStartup * 8f) % Spinner.Length];
            float x = panel.x + 16, y = panel.y + 12, w = panel.width - 32;
            GUI.Label(new Rect(x, y, 14, ToolWindow.RowHeight), spin.ToString(), s.AccentLabel);
            GUI.Label(new Rect(x + 26, y, w - 26, ToolWindow.RowHeight), ToolWindow.ElideText(_busyWhat, s.Label, w - 26), s.Label);
            y += ToolWindow.RowHeight;
            if (!string.IsNullOrEmpty(_busyDetail))
            {
                GUI.Label(new Rect(x, y, w, 26), ToolWindow.ElideText(_busyDetail, s.MutedLabel, w), s.MutedLabel);
            }
            y += 26;
            GUI.Label(new Rect(x, y, w, 26), "The window waits until it is done.", s.MutedLabel);
            SetHint(string.IsNullOrEmpty(_busyDetail) ? _busyWhat : _busyWhat + "  " + _busyDetail, false);
        }
    }
}
